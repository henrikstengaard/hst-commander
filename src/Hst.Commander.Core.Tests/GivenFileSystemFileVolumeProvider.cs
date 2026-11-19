using System.Text;
using Hst.Commander.Abstractions;
using Hst.Commander.Core.Volumes;
using Hst.Commander.Plugins.Disk;

namespace Hst.Commander.Core.Tests;

public class GivenFileSystemFileVolumeProvider : IDisposable
{
    private readonly string _rootPath =
        Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), $"hst-commander-{Guid.NewGuid()}")).FullName;

    private readonly FileSystemFileVolumeProvider _provider = new();

    public void Dispose()
    {
        Directory.Delete(_rootPath, true);
        GC.SuppressFinalize(this);
    }

    private static async Task<Stream> CreateImage(string fileSystem)
    {
        switch (fileSystem)
        {
            case "FAT":
                return DiskTestData.CreateFatImage();
            case "NTFS":
                return DiskTestData.CreateNtfsImage();
            case "exFAT":
                var creator = new ExFatVolumeCreator();
                var stream = new MemoryStream();
                await creator.Create(stream, VolumeCreation.GetValues(creator, new Dictionary<string, object?>
                {
                    [ExFatVolumeCreator.SizeField] = new VolumeCreatorSizeInput("4", "MB"),
                    [ExFatVolumeCreator.VolumeLabelField] = DiskTestData.ExFatLabel,
                    [ExFatVolumeCreator.ClusterSizeField] = ExFatVolumeCreator.DefaultClusterSize
                }));
                stream.Position = 0;
                return stream;
            default:
                return await DiskTestData.CreateExtImage();
        }
    }

    [Theory]
    [InlineData("FAT", DiskTestData.FatLabel)]
    [InlineData("NTFS", DiskTestData.NtfsLabel)]
    [InlineData("exFAT", DiskTestData.ExFatLabel)]
    public async Task When_FileSystemWithoutPartitionTableIsOpened_Then_FilesCanBeWrittenAndReadAgain(
        string fileSystem, string volumeLabel)
    {
        // arrange
        await using var stream = await CreateImage(fileSystem);
        await using var nonSeekableStream = new NonSeekableStream(new MemoryStream(((MemoryStream)stream).ToArray()));

        // act
        var isMatch = await _provider.IsMatch("disk.img", nonSeekableStream);
        stream.Position = 0;
        using (var volume = (DiscFileSystemVolume)await _provider.CreateVolume(stream))
        {
            Assert.Equal(fileSystem, volume.Name);
            Assert.Equal(volumeLabel, volume.VolumeLabel.Trim());
            await volume.CreateContainer("dir1");
            await using var writeStream = await volume.OpenWrite("file1.txt");
            await writeStream.WriteAsync("Disk"u8.ToArray());
        }

        stream.Position = 0;
        using var reopenedVolume = (DiscFileSystemVolume)await _provider.CreateVolume(stream);
        var names = await reopenedVolume.ListEntries().Select(entry => entry.Name)
            .Where(name => !name.StartsWith('$') && name != "System Volume Information").OrderBy(name => name)
            .ToListAsync();
        await using var readStream = await reopenedVolume.OpenRead("file1.txt");
        using var reader = new StreamReader(readStream, Encoding.ASCII);

        // assert
        Assert.True(isMatch);
        Assert.Equal(["dir1", "file1.txt"], names);
        Assert.Equal("Disk", await reader.ReadToEndAsync());
    }

    [Fact]
    public async Task When_ExtWithoutPartitionTableIsOpened_Then_FilesAreListedAndItIsReadOnly()
    {
        // arrange
        await using var stream = await DiskTestData.CreateExtImage();
        await using var nonSeekableStream = new NonSeekableStream(new MemoryStream(((MemoryStream)stream).ToArray()));

        // act
        var isMatch = await _provider.IsMatch("ext4.img", nonSeekableStream);
        stream.Position = 0;
        using var volume = (DiscFileSystemVolume)await _provider.CreateVolume(stream);
        var names = await volume.ListEntries().Select(entry => entry.Name).ToListAsync();
        var writeException = await Record.ExceptionAsync(() => volume.CreateContainer("dir2"));

        // assert
        Assert.True(isMatch);
        Assert.Equal("ext", volume.Name);
        Assert.Equal(DiskTestData.ExtLabel, volume.VolumeLabel);
        Assert.Contains("file1.txt", names);
        Assert.Contains("dir1", names);
        Assert.IsType<NotSupportedException>(writeException);
    }

    [Fact]
    public async Task When_DiskAndFileSystemImagesAreChecked_Then_OnlyTheMatchingProviderMatches()
    {
        // arrange
        await using var mbrDisk = DiskTestData.CreateMbrDisk();
        await using var gptDisk = DiskTestData.CreateGptDisk();
        await using var fat = DiskTestData.CreateFatImage();
        await using var ntfs = DiskTestData.CreateNtfsImage();
        await using var ext = await DiskTestData.CreateExtImage();
        var diskProvider = new DiskFileVolumeProvider();

        async Task<bool> IsMatch(IFileVolumeProvider provider, Stream stream)
        {
            stream.Position = 0;
            return await provider.IsMatch("disk.img", stream);
        }

        // act & assert
        Assert.False(await IsMatch(_provider, mbrDisk));
        Assert.False(await IsMatch(_provider, gptDisk));
        Assert.False(await IsMatch(diskProvider, fat));
        Assert.False(await IsMatch(diskProvider, ntfs));
        Assert.False(await IsMatch(diskProvider, ext));
        Assert.False(await IsMatch(_provider, new MemoryStream(new byte[4096])));
    }

    [Fact]
    public async Task When_FatImageIsOpenedFromLocalDirectory_Then_ItIsAWritableImage()
    {
        // arrange
        var path = Path.Combine(_rootPath, "floppy.img");
        await using (var stream = DiskTestData.CreateFatImage())
        await using (var fileStream = File.Create(path))
        {
            await stream.CopyToAsync(fileStream);
        }

        using (var navigationContext = new NavigationContext(_rootPath))
        {
            // act
            await navigationContext.OpenEntryAsync(
                (await navigationContext.ListEntriesAsync()).First(entry => entry.Name == "floppy.img"));
            var isInImage = navigationContext.IsInImage;
            await navigationContext.CreateDirectoryAsync("dir1");
            var names = (await navigationContext.ListEntriesAsync()).Select(entry => entry.Name).ToList();
            await navigationContext.OpenEntryAsync(
                (await navigationContext.ListEntriesAsync()).First(entry => entry.Name == Paths.Parent));

            // assert
            Assert.True(isInImage);
            Assert.Equal([Paths.Parent, "dir1"], names);
            Assert.False(navigationContext.IsInImage);
        }

        // the directory is written to the file
        await using var reopenedStream = File.OpenRead(path);
        using var volume = (DiscFileSystemVolume)await _provider.CreateVolume(reopenedStream);
        Assert.Equal(["dir1"], await volume.ListEntries().Select(entry => entry.Name).ToListAsync());
    }
}
