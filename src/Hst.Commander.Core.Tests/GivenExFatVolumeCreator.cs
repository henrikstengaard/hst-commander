using System.Globalization;
using System.Text;
using Hst.Commander.Abstractions;
using Hst.Commander.Plugins.Disk;

namespace Hst.Commander.Core.Tests;

public class GivenExFatVolumeCreator : IDisposable
{
    private const long MegaByte = 1L << 20;
    private const long GigaByte = 1L << 30;

    // offset of the sectors per cluster shift in the exFAT boot sector
    private const int SectorsPerClusterShiftOffset = 109;

    private readonly string _rootPath =
        Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), $"hst-commander-{Guid.NewGuid()}")).FullName;

    private readonly ExFatVolumeCreator _creator = new();

    public void Dispose()
    {
        Directory.Delete(_rootPath, true);
        GC.SuppressFinalize(this);
    }

    private static Dictionary<string, object?> Input(string number, string unit, string volumeLabel = "",
        string clusterSize = ExFatVolumeCreator.DefaultClusterSize) => new()
    {
        [ExFatVolumeCreator.SizeField] = new VolumeCreatorSizeInput(number, unit),
        [ExFatVolumeCreator.VolumeLabelField] = volumeLabel,
        [ExFatVolumeCreator.ClusterSizeField] = clusterSize
    };

    private static async Task<IReadOnlyList<string>> ListNamesAsync(NavigationContext navigationContext) =>
        (await navigationContext.ListEntriesAsync()).Select(entry => entry.Name).ToList();

    private static async Task<byte> ReadSectorsPerClusterShift(string path)
    {
        await using var stream = File.OpenRead(path);
        var bootSector = new byte[512];
        await stream.ReadExactlyAsync(bootSector);
        return bootSector[SectorsPerClusterShiftOffset];
    }

    [Fact]
    public void When_ListingCreators_Then_ExFatVolumeCreatorOfDiskPluginIsListed()
    {
        // act & assert
        Assert.Contains(VolumeProviders.Creators, creator => creator is ExFatVolumeCreator);
        Assert.Contains(VolumeProviders.Files, provider => provider is FileSystemFileVolumeProvider);
    }

    [Fact]
    public async Task When_CreateExFatInLocalDirectory_Then_ItIsCreatedWithLabelAndOpenedAsWritableImage()
    {
        // arrange
        using var navigationContext = new NavigationContext(_rootPath);
        await navigationContext.ListEntriesAsync();
        var previousPath = navigationContext.CurrentPath;

        // act
        await navigationContext.CreateVolumeAsync(_creator, "usb", Input("8", "MB", "MYUSB"));
        var entries = await navigationContext.ListEntriesAsync();
        var selectedIndex = navigationContext.GetSelectedIndex(entries, previousPath, 0);
        await navigationContext.OpenEntryAsync(entries[selectedIndex]);
        var emptyNames = await ListNamesAsync(navigationContext);
        await navigationContext.CreateDirectoryAsync("dir1");
        var names = await ListNamesAsync(navigationContext);

        // assert
        var path = Path.Combine(_rootPath, "usb.img");
        Assert.Equal("usb.img", entries[selectedIndex].Name);
        Assert.True(navigationContext.IsInImage);
        Assert.Equal([Paths.Parent], emptyNames);
        Assert.Equal([Paths.Parent, "dir1"], names);
        navigationContext.Dispose();
        Assert.Equal(8 * MegaByte, new FileInfo(path).Length);

        // opened again by the provider, the directory is in the file system
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.ReadWrite);
        var provider = new FileSystemFileVolumeProvider();
        Assert.True(await provider.IsMatch("usb.img", stream));
        stream.Position = 0;
        using var volume = (DiscFileSystemVolume)await provider.CreateVolume(stream);
        Assert.Equal("MYUSB", volume.VolumeLabel);
        Assert.Equal(["dir1"], await volume.ListEntries().Select(entry => entry.Name).ToListAsync());
    }

    [Fact]
    public async Task When_ExFatIsOpenedByProvider_Then_FilesCanBeWrittenAndRead()
    {
        // arrange
        await using var stream = new MemoryStream();
        await _creator.Create(stream, VolumeCreation.GetValues(_creator, Input("4", "MB")));
        stream.Position = 0;
        var provider = new FileSystemFileVolumeProvider();

        // act
        using (var volume = (IDisposable)await provider.CreateVolume(stream))
        {
            await using var writeStream = await ((IVolume)volume).OpenWrite("file1.txt");
            await writeStream.WriteAsync("exFAT"u8.ToArray());
        }

        stream.Position = 0;
        using var reopenedVolume = (DiscFileSystemVolume)await provider.CreateVolume(stream);
        await using var readStream = await reopenedVolume.OpenRead("file1.txt");
        using var reader = new StreamReader(readStream, Encoding.ASCII);

        // assert
        Assert.Equal("exFAT", await reader.ReadToEndAsync());
        Assert.Equal(string.Empty, reopenedVolume.VolumeLabel);
    }

    [Fact]
    public async Task When_CreateLargeExFat_Then_FileIsSparseWhereSupported()
    {
        // arrange
        using var navigationContext = new NavigationContext(_rootPath);
        await navigationContext.ListEntriesAsync();

        // act
        await navigationContext.CreateVolumeAsync(_creator, "large", Input("4", "GB"));

        // assert
        var path = Path.Combine(_rootPath, "large.img");
        var fileInfo = new FileInfo(path);
        Assert.Equal(4 * GigaByte, fileInfo.Length);
        if (OperatingSystem.IsWindows() && new DriveInfo(_rootPath).DriveFormat == "NTFS")
        {
            Assert.True(fileInfo.Attributes.HasFlag(FileAttributes.SparseFile));
        }

        // 32 KB clusters, 64 sectors of 512 bytes
        Assert.Equal(6, await ReadSectorsPerClusterShift(path));
    }

    [Theory]
    [InlineData(MegaByte, 4096)]
    [InlineData(GigaByte, 32768)]
    [InlineData(300 * GigaByte, 131072)]
    [InlineData(2048 * GigaByte, 131072)]
    [InlineData(4096 * GigaByte, 131072)]
    public async Task When_CreateExFat_Then_FileSystemIsValidAndWritable(long size, int expectedClusterSize)
    {
        // arrange: a sparse file, as DiscUtils' exFAT formatter made an invalid up-case table and too small a FAT
        // above 2 TB
        var path = Path.Combine(_rootPath, "disk.img");
        await using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.ReadWrite);
        SparseFile.TryMakeSparse(stream);

        // act
        await _creator.Create(stream, VolumeCreation.GetValues(_creator, Input(size.ToString(CultureInfo.InvariantCulture),
            "B", "VALID")));
        stream.Position = 0;
        var validator = ExFatValidator.Validate(stream, size);
        stream.Position = 0;
        using (var volume = (DiscFileSystemVolume)await new FileSystemFileVolumeProvider().CreateVolume(stream))
        {
            await volume.CreateContainer("dir1");
            await using var writeStream = await volume.OpenWrite("file1.txt");
            await writeStream.WriteAsync("exFAT"u8.ToArray());
        }

        // the validator only validates empty file systems, the entries are read when mounted again
        stream.Position = 0;
        using var reopenedVolume = (DiscFileSystemVolume)await new FileSystemFileVolumeProvider().CreateVolume(stream);
        var names = await reopenedVolume.ListEntries().Select(entry => entry.Name).ToListAsync();

        // assert
        Assert.Empty(validator.Errors);
        Assert.Equal(["dir1", "file1.txt"], names);
        Assert.Equal("VALID", validator.VolumeLabel);
        Assert.Equal(expectedClusterSize, validator.BytesPerCluster);
        Assert.Equal(size, stream.Length);
    }

    [Fact]
    public async Task When_ClusterSizeIsChosen_Then_FileSystemHasClusterSize()
    {
        // arrange
        using var navigationContext = new NavigationContext(_rootPath);
        await navigationContext.ListEntriesAsync();

        // act
        await navigationContext.CreateVolumeAsync(_creator, "disk", Input("64", "MB", clusterSize: "65536"));

        // assert: 128 sectors of 512 bytes
        Assert.Equal(7, await ReadSectorsPerClusterShift(Path.Combine(_rootPath, "disk.img")));
    }

    [Theory]
    [InlineData("1023", "KB", "")]
    [InlineData("8", "MB", "TOOLONGLABEL")]
    public async Task When_CreateExFatWithInvalidInput_Then_IOExceptionIsThrownAndNoFileIsLeft(string number,
        string unit, string volumeLabel)
    {
        // arrange
        using var navigationContext = new NavigationContext(_rootPath);
        await navigationContext.ListEntriesAsync();

        // act & assert
        await Assert.ThrowsAsync<IOException>(() =>
            navigationContext.CreateVolumeAsync(_creator, "disk", Input(number, unit, volumeLabel)));
        Assert.Empty(Directory.GetFiles(_rootPath));
    }

    [Fact]
    public async Task When_DiskOrFatImageIsChecked_Then_ExFatProviderDoesNotMatchAndDiskProviderDoesNotMatchExFat()
    {
        // arrange
        await using var mbrDisk = DiskTestData.CreateMbrDisk();
        await using var exFat = new MemoryStream();
        await _creator.Create(exFat, VolumeCreation.GetValues(_creator, Input("2", "MB")));
        exFat.Position = 0;

        // act
        var exFatMatchesDisk = await new FileSystemFileVolumeProvider().IsMatch("disk.img", mbrDisk);
        var diskMatchesExFat = await new DiskFileVolumeProvider().IsMatch("disk.img", exFat);

        // assert
        Assert.False(exFatMatchesDisk);
        Assert.False(diskMatchesExFat);
    }

    [Fact]
    public void When_SizeFieldHasDefault_Then_ItIsShownInLargestWholeUnitAndGigabytesWithoutDefault()
    {
        // arrange
        VolumeCreatorField Field(object? defaultValue) =>
            new("Size", "Size", VolumeCreatorFieldType.Size) { IsRequired = true, DefaultValue = defaultValue };

        // act & assert
        Assert.Equal(new VolumeCreatorSizeInput("1", "GB"), VolumeCreation.GetSizeInput(_creator.Fields[0], null));
        Assert.Equal(new VolumeCreatorSizeInput(string.Empty, "GB"), VolumeCreation.GetSizeInput(Field(null), null));
        Assert.Equal(new VolumeCreatorSizeInput("3", "MB"), VolumeCreation.GetSizeInput(Field(3 * MegaByte), null));
        Assert.Equal(new VolumeCreatorSizeInput("1536", "B"), VolumeCreation.GetSizeInput(Field(1536L), null));
        Assert.Equal(new VolumeCreatorSizeInput("5", "KB"),
            VolumeCreation.GetSizeInput(Field(null), new VolumeCreatorSizeInput("5", "KB")));
        Assert.Equal(3, VolumeCreation.GetSizeUnitIndex("gb"));
    }

    [Fact]
    public void When_GetValuesWithSize_Then_SizeIsConvertedToBytes()
    {
        // arrange
        var oneAndAHalf = 1.5m.ToString(CultureInfo.CurrentCulture);

        // act
        var gigaBytes = VolumeCreation.GetValues(_creator, Input(oneAndAHalf, "GB"));
        var bytes = VolumeCreation.GetValues(_creator, Input("1000", "B"));
        var defaultSize = VolumeCreation.GetValues(_creator, new Dictionary<string, object?>());

        // assert
        Assert.Equal(GigaByte + GigaByte / 2, gigaBytes.GetNumber(ExFatVolumeCreator.SizeField));
        Assert.Equal(1000, bytes.GetNumber(ExFatVolumeCreator.SizeField));
        Assert.Equal(GigaByte, defaultSize.GetNumber(ExFatVolumeCreator.SizeField));
    }

    [Theory]
    [InlineData("", "GB")]
    [InlineData("abc", "GB")]
    [InlineData("-1", "MB")]
    [InlineData("1", "TB")]
    [InlineData("99999999999999999999999", "GB")]
    public void When_GetValuesWithMissingOrInvalidSize_Then_IOExceptionIsThrown(string number, string unit)
    {
        // act & assert
        Assert.Throws<IOException>(() => VolumeCreation.GetValues(_creator, Input(number, unit)));
    }
}
