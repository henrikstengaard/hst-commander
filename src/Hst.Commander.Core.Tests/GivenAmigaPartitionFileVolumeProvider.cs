using System.Text;
using Hst.Commander.Abstractions;
using Hst.Commander.Core.Volumes;
using Hst.Commander.Plugins.Amiga;

namespace Hst.Commander.Core.Tests;

public class GivenAmigaPartitionFileVolumeProvider : IDisposable
{
    private static readonly byte[] Dos7DosType = [0x44, 0x4f, 0x53, 0x7];

    private readonly string _rootPath =
        Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), $"hst-commander-{Guid.NewGuid()}")).FullName;

    private readonly AmigaPartitionFileVolumeProvider _provider = new();

    public void Dispose()
    {
        Directory.Delete(_rootPath, true);
        GC.SuppressFinalize(this);
    }

    public static TheoryData<string, uint> FileSystems => new()
    {
        { "DOS3", 512 },
        { "DOS3", 1024 },
        { "DOS7", 512 },
        { "DOS7", 4096 },
        { "PFS3", 512 },
        { "SFS0", 512 }
    };

    private static byte[] GetDosType(string name) => name switch
    {
        "DOS3" => AmigaTestData.Dos3DosType,
        "DOS7" => Dos7DosType,
        "PFS3" => AmigaTestData.Pfs3DosType,
        "SFS0" => AmigaTestData.SfsDosType,
        _ => throw new ArgumentOutOfRangeException(nameof(name))
    };

    [Theory]
    [MemberData(nameof(FileSystems))]
    public async Task When_PartitionImageIsOpened_Then_FilesCanBeWrittenAndReadAgain(string fileSystem,
        uint fileSystemBlockSize)
    {
        // arrange
        await using var stream = new MemoryStream();
        await AmigaTestData.WritePartitionImage(stream, GetDosType(fileSystem), fileSystemBlockSize);
        stream.Position = 0;
        await using var nonSeekableStream = new NonSeekableStream(new MemoryStream(stream.ToArray()));

        // act
        var isMatch = await _provider.IsMatch("work.hdf", nonSeekableStream);
        stream.Position = 0;
        using (var volume = (AmigaFileSystemVolume)await _provider.CreateVolume(stream))
        {
            Assert.Equal("Work", volume.VolumeName);
            await volume.CreateContainer("dir1");
            await using var writeStream = await volume.OpenWrite("file1.txt");
            await writeStream.WriteAsync("Amiga"u8.ToArray());
        }

        stream.Position = 0;
        using var reopenedVolume = (AmigaFileSystemVolume)await _provider.CreateVolume(stream);
        var names = await reopenedVolume.ListEntries().Select(entry => entry.Name).OrderBy(name => name)
            .ToListAsync();
        await using var readStream = await reopenedVolume.OpenRead("file1.txt");
        using var reader = new StreamReader(readStream, Encoding.ASCII);

        // assert
        Assert.True(isMatch);
        Assert.Equal(["dir1", "file1.txt"], names);
        Assert.Equal("Amiga", await reader.ReadToEndAsync());
    }

    [Fact]
    public async Task When_AdfAndRdbImageAreDetected_Then_TheyAreNotOpenedAsPartitionImages()
    {
        // arrange
        var adfPath = Path.Combine(_rootPath, "disk.adf");
        var rdbPath = Path.Combine(_rootPath, "disk.hdf");
        var partitionPath = Path.Combine(_rootPath, "work.hdf");
        await AmigaTestData.CreateAdf(adfPath);
        await AmigaTestData.CreateRdbImage(rdbPath, AmigaTestData.Dos3DosType);
        await AmigaTestData.CreatePartitionImage(partitionPath, AmigaTestData.Dos3DosType);

        // act
        async Task<IFileVolumeProvider?> DetectAsync(string path)
        {
            foreach (var provider in VolumeProviders.Files)
            {
                await using var stream = File.OpenRead(path);
                if (await provider.IsMatch(Path.GetFileName(path), stream))
                {
                    return provider;
                }
            }

            return null;
        }

        // assert
        Assert.IsType<AdfFileVolumeProvider>(await DetectAsync(adfPath));
        Assert.IsType<RdbFileVolumeProvider>(await DetectAsync(rdbPath));
        Assert.IsType<AmigaPartitionFileVolumeProvider>(await DetectAsync(partitionPath));
    }

    [Fact]
    public async Task When_StreamHasNoAmigaFileSystem_Then_ItDoesNotMatchAndCantBeOpened()
    {
        // arrange
        await using var stream = new MemoryStream(new byte[64 * 1024]);

        // act
        var isMatch = await _provider.IsMatch("zeros.img", stream);
        stream.Position = 0;
        var exception = await Record.ExceptionAsync(() => _provider.CreateVolume(stream));

        // assert
        Assert.False(isMatch);
        Assert.IsType<IOException>(exception);
    }

    [Fact]
    public async Task When_PartitionImageIsOpenedFromLocalDirectory_Then_ItIsAWritableImage()
    {
        // arrange
        await AmigaTestData.CreatePartitionImage(Path.Combine(_rootPath, "work.hdf"), AmigaTestData.Pfs3DosType);
        using (var navigationContext = new NavigationContext(_rootPath))
        {
            // act
            await navigationContext.OpenEntryAsync(
                (await navigationContext.ListEntriesAsync()).First(entry => entry.Name == "work.hdf"));
            await navigationContext.CreateDirectoryAsync("dir1");
            var names = (await navigationContext.ListEntriesAsync()).Select(entry => entry.Name).ToList();
            await navigationContext.OpenEntryAsync(
                (await navigationContext.ListEntriesAsync()).First(entry => entry.Name == Paths.Parent));

            // assert
            Assert.Equal([Paths.Parent, "dir1"], names);
            Assert.False(navigationContext.IsInImage);
        }

        // the directory is written to the file
        await using var stream = File.OpenRead(Path.Combine(_rootPath, "work.hdf"));
        using var volume = await _provider.CreateVolume(stream) as IDisposable;
        Assert.Equal(["dir1"],
            await ((IVolume)volume!).ListEntries().Select(entry => entry.Name).ToListAsync());
    }
}
