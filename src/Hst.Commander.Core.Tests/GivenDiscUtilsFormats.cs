using System.Text;
using Hst.Commander.Abstractions;
using Hst.Commander.Plugins.Amiga;
using Hst.Commander.Plugins.Disk;

namespace Hst.Commander.Core.Tests;

/// <summary>
/// The file systems DiscUtils only reads (XFS, Btrfs, HFS+, SquashFS and UDF) and the virtual disk images (VHDX and
/// VDI, writable) opened by the Disk plugin.
/// </summary>
public class GivenDiscUtilsFormats : IDisposable
{
    private readonly string _rootPath =
        Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), $"hst-commander-{Guid.NewGuid()}")).FullName;

    private readonly FileSystemFileVolumeProvider _fileSystemProvider = new();

    // a host with the rigid disk block provider of the Amiga plugin, opening Amiga disks in virtual disks
    private readonly DiskFileVolumeProvider _diskProvider = new(new PluginHost(() => [new RdbFileVolumeProvider()]));

    public void Dispose()
    {
        Directory.Delete(_rootPath, true);
        GC.SuppressFinalize(this);
    }

    private static async Task<Stream> OpenReadOnlyImage(string fileSystem) => fileSystem == "squashfs"
        ? DiskTestData.CreateSquashFsImage()
        : await DiskTestData.OpenGzippedImage($"{fileSystem}.img.gz");

    private static async Task<string> ReadTextAsync(IVolume volume, string name)
    {
        await using var stream = await volume.OpenRead(name);
        using var reader = new StreamReader(stream, Encoding.ASCII);
        return await reader.ReadToEndAsync();
    }

    private static async Task WriteTextAsync(IVolume volume, string name, string text)
    {
        await using var stream = await volume.OpenWrite(name);
        await stream.WriteAsync(Encoding.ASCII.GetBytes(text));
    }

    [Theory]
    [InlineData("xfs", "XFSTEST")]
    [InlineData("btrfs", "BTRFSTEST")]
    [InlineData("udf", "UDFTEST")]
    [InlineData("squashfs", null)]
    public async Task When_ReadOnlyFileSystemIsOpened_Then_FilesAreReadButNotWritten(string fileSystem,
        string? volumeLabel)
    {
        // arrange
        await using var stream = await OpenReadOnlyImage(fileSystem);

        // act
        var isMatch = await _fileSystemProvider.IsMatch("image.img", stream);
        using var volume = (DiscFileSystemVolume)await _fileSystemProvider.CreateVolume(stream);
        var entries = await volume.ListEntries().ToListAsync();
        var text = await ReadTextAsync(volume, "hello.txt");
        await volume.ChangeDirectory(["dir"]);
        var nestedText = await ReadTextAsync(volume, "nested.txt");
        var changeableAttributes = await volume.GetChangeableAttributes();

        // assert
        Assert.True(isMatch);
        Assert.Contains(entries, entry => entry is { Name: "dir", Type: EntryType.Container });
        Assert.Contains(entries, entry => entry is { Name: "hello.txt", Type: EntryType.Item, Size: > 0 });
        Assert.Equal($"Hello from {fileSystem}\n", text);
        Assert.Equal("Nested file\n", nestedText);
        if (volumeLabel != null)
        {
            Assert.Equal(volumeLabel, volume.VolumeLabel);
        }

        Assert.Equal(EntryAttributes.None, changeableAttributes);
        await Assert.ThrowsAsync<NotSupportedException>(() => volume.OpenWrite("new.txt"));
        await Assert.ThrowsAsync<NotSupportedException>(() => volume.Delete("nested.txt"));
    }

    [Fact]
    public async Task When_HfsPlusFileSystemIsOpened_Then_ItIsReadOnly()
    {
        // arrange
        await using var stream = await DiskTestData.OpenGzippedImage("hfsplus.img.gz");

        // act
        var isMatch = await _fileSystemProvider.IsMatch("image.img", stream);
        using var volume = (DiscFileSystemVolume)await _fileSystemProvider.CreateVolume(stream);
        var entries = await volume.ListEntries().ToListAsync();

        // assert
        Assert.True(isMatch);
        Assert.Equal("HFSTEST", volume.VolumeLabel);
        Assert.DoesNotContain(entries, entry => entry.Type == EntryType.Item && entry.Name == "hello.txt");
        await Assert.ThrowsAsync<NotSupportedException>(() => volume.CreateContainer("dir"));
    }

    [Fact]
    public async Task When_DiskHasXfsPartition_Then_PartitionIsMounted()
    {
        // arrange
        await using var image = await DiskTestData.OpenGzippedImage("xfs.img.gz");
        await using var disk = await DiskTestData.CreateLinuxPartitionDisk(image);

        // act
        using var volume = (VolumeListVolume)await _diskProvider.CreateVolume(disk);
        await volume.ChangeDirectory(["mbr"]);
        var partition = Assert.Single(await volume.ListEntries().ToListAsync());
        await volume.ChangeDirectory(["1"]);
        var text = await ReadTextAsync(volume, "hello.txt");

        // assert
        Assert.Equal("1 - XFSTEST", partition.DisplayName);
        Assert.Equal("Hello from xfs\n", text);
    }

    [Theory]
    [InlineData("VHDX")]
    [InlineData("VDI")]
    public async Task When_FilesAreWrittenToFatPartitionOfVirtualDisk_Then_TheyAreReadAfterOpeningAgain(
        string format)
    {
        // arrange
        await using var content = DiskTestData.CreateMbrDisk();
        await using var stream = await DiskTestData.CreateVirtualDisk(format, content);

        // act
        var isMatch = await _diskProvider.IsMatch("disk", stream);
        stream.Position = 0;
        using (var volume = (VolumeListVolume)await _diskProvider.CreateVolume(stream))
        {
            await volume.ChangeDirectory(["mbr", "1"]);
            await volume.CreateContainer("dir1");
            await volume.ChangeDirectory(["dir1"]);
            await WriteTextAsync(volume, "file.txt", "Hello");
        }

        stream.Position = 0;
        using var reopened = (VolumeListVolume)await _diskProvider.CreateVolume(stream);
        await reopened.ChangeDirectory(["mbr", "1", "dir1"]);
        var text = await ReadTextAsync(reopened, "file.txt");

        // assert
        Assert.True(isMatch);
        Assert.Equal("Hello", text);
    }

    [Theory]
    [InlineData("VHDX")]
    [InlineData("VDI")]
    [InlineData("VHD")]
    public async Task When_VirtualDiskHasFileSystemWithoutPartitionTable_Then_ItIsOpenedAndWritable(string format)
    {
        // arrange
        await using var content = DiskTestData.CreateFatImage();
        await using var stream = await DiskTestData.CreateVirtualDisk(format, content);

        // act
        var isMatch = await _diskProvider.IsMatch("disk", stream);
        stream.Position = 0;
        using (var volume = (DiscFileSystemVolume)await _diskProvider.CreateVolume(stream))
        {
            await WriteTextAsync(volume, "file.txt", "Hello");
        }

        stream.Position = 0;
        using var reopened = (DiscFileSystemVolume)await _diskProvider.CreateVolume(stream);
        var text = await ReadTextAsync(reopened, "file.txt");

        // assert
        Assert.True(isMatch);
        Assert.Equal(DiskTestData.FatLabel, reopened.VolumeLabel.Trim());
        Assert.Equal("Hello", text);
    }

    [Fact]
    public async Task When_VirtualDiskHasReadOnlyFileSystem_Then_ItIsOpened()
    {
        // arrange
        await using var content = DiskTestData.CreateSquashFsImage();
        await using var stream = await DiskTestData.CreateVirtualDisk("VHDX", content);

        // act
        var isMatch = await _diskProvider.IsMatch("disk.vhdx", stream);
        stream.Position = 0;
        using var volume = (IDisposable)await _diskProvider.CreateVolume(stream);
        var text = await ReadTextAsync((IVolume)volume, "hello.txt");

        // assert
        Assert.True(isMatch);
        Assert.Equal("Hello from squashfs\n", text);
    }

    [Theory]
    [InlineData("VHDX")]
    [InlineData("VDI")]
    public async Task When_VirtualDiskHasRigidDiskBlock_Then_ItIsOpenedByHost(string format)
    {
        // arrange
        var rdbPath = Path.Combine(_rootPath, "disk.hdf");
        await AmigaTestData.CreateRdbImage(rdbPath, AmigaTestData.Dos3DosType);
        await using var content = File.OpenRead(rdbPath);
        await using var stream = await DiskTestData.CreateVirtualDisk(format, content);

        // act
        var isMatch = await _diskProvider.IsMatch("disk", stream);
        stream.Position = 0;
        using var volume = (IDisposable)await _diskProvider.CreateVolume(stream);
        var entries = await ((IVolume)volume).ListEntries().ToListAsync();

        // assert
        Assert.True(isMatch);
        Assert.Contains(entries, entry => entry.Name == "rdb");
    }

    [Fact]
    public async Task When_VhdHasRigidDiskBlock_Then_ItIsLeftToTheAmigaPlugin()
    {
        // arrange
        var vhdPath = Path.Combine(_rootPath, "disk.vhd");
        await AmigaTestData.CreateVhdRdbImage(vhdPath, AmigaTestData.Dos3DosType);
        await using var stream = File.OpenRead(vhdPath);

        // act
        var isMatch = await _diskProvider.IsMatch("disk.vhd", stream);

        // assert
        Assert.False(isMatch);
    }

    [Fact]
    public async Task When_VirtualDiskIsEmpty_Then_OpeningItFails()
    {
        // arrange
        await using var content = new MemoryStream(new byte[1024 * 1024]);
        await using var stream = await DiskTestData.CreateVirtualDisk("VDI", content);

        // act & assert
        await Assert.ThrowsAsync<IOException>(() => _diskProvider.CreateVolume(stream));
    }
}
