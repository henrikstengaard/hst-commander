using System.Text;
using Hst.Commander.Abstractions;
using Hst.Commander.Plugins.RarSevenZip;

namespace Hst.Commander.Core.Tests;

/// <summary>
/// Tar archives and gzip and xz files of TestData/Archives made with GNU tar, gzip and xz (see
/// TestData/Archives/make/make-archives.sh), opened by the RarSevenZip plugin.
/// </summary>
public class GivenTarGzipXzArchives : IDisposable
{
    private static readonly DateTime FileDate = new(2024, 5, 6, 7, 8, 10, DateTimeKind.Utc);

    private readonly string _rootPath =
        Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), $"hst-commander-{Guid.NewGuid()}")).FullName;

    public void Dispose()
    {
        Directory.Delete(_rootPath, true);
        GC.SuppressFinalize(this);
    }

    private static IFileVolumeProvider GetProvider(string fileName) =>
        fileName.EndsWith(".tar", StringComparison.Ordinal) ? new TarFileVolumeProvider()
        : fileName.EndsWith(".xz", StringComparison.Ordinal) ? new XzFileVolumeProvider()
        : new GzipFileVolumeProvider();

    private static Task<IVolume> CreateVolumeAsync(string fileName) =>
        GetProvider(fileName).CreateVolume(fileName, new MemoryStream(ArchiveTestData.Read(fileName)));

    private static async Task<string> ReadTextAsync(IVolume volume, string name)
    {
        await using var stream = await volume.OpenRead(name);
        using var reader = new StreamReader(stream, Encoding.ASCII);
        return await reader.ReadToEndAsync();
    }

    [Theory]
    [InlineData("dirs-files.tar", "Tar")]
    [InlineData("dirs-files-posix.tar", "Tar")]
    [InlineData("dirs-files-v7.tar", "Tar")]
    [InlineData("dirs-files.tar.gz", "Gzip")]
    [InlineData("hello.txt.gz", "Gzip")]
    [InlineData("dirs-files.tar.xz", "Xz")]
    [InlineData("hello.txt.xz", "Xz")]
    [InlineData("dirs-files.7z", null)]
    [InlineData("dirs-files.rar", null)]
    [InlineData("dirs-files.lha", null)]
    public async Task When_IsMatch_Then_OnlyTheProviderOfTheFormatMatches(string fileName, string? expected)
    {
        // arrange
        IFileVolumeProvider[] providers =
            [new TarFileVolumeProvider(), new GzipFileVolumeProvider(), new XzFileVolumeProvider()];

        // act
        var matches = new List<string>();
        foreach (var provider in providers)
        {
            if (await provider.IsMatch(fileName, new MemoryStream(ArchiveTestData.Read(fileName))))
            {
                matches.Add(provider.Name);
            }
        }

        // assert
        Assert.Equal(expected == null ? [] : [expected], matches);
    }

    [Fact]
    public async Task When_FileIsEmptyOrZeros_Then_TarIsNotMatched()
    {
        // act
        var emptyMatch = await new TarFileVolumeProvider().IsMatch("empty.tar", new MemoryStream());
        var zerosMatch = await new TarFileVolumeProvider().IsMatch("zeros.tar", new MemoryStream(new byte[1024]));

        // assert
        Assert.False(emptyMatch);
        Assert.False(zerosMatch);
    }

    [Theory]
    [InlineData("dirs-files.tar")]
    [InlineData("dirs-files-posix.tar")]
    [InlineData("dirs-files-v7.tar")]
    [InlineData("dirs-files.tar.gz")]
    [InlineData("dirs-files.tgz")]
    [InlineData("dirs-files.tar.xz")]
    public async Task When_TarArchiveIsOpened_Then_DirectoriesAndFilesAreListedAndRead(string fileName)
    {
        // arrange
        using var volume = (IDisposable)await CreateVolumeAsync(fileName);
        var archiveVolume = (IVolume)volume;

        // act
        var rootEntries = await archiveVolume.ListEntries().ToListAsync();
        await archiveVolume.ChangeDirectory(["dir1"]);
        var dirEntries = await archiveVolume.ListEntries().ToListAsync();
        var text = await ReadTextAsync(archiveVolume, "file1.txt");

        // assert
        Assert.Equal(["dir1", "dir2"], rootEntries.Select(entry => entry.Name));
        Assert.Equal(["dir3", "file1.txt"], dirEntries.Select(entry => entry.Name));
        Assert.Equal([EntryType.Container, EntryType.Item], dirEntries.Select(entry => entry.Type));
        Assert.Equal(1, dirEntries[1].Size);
        Assert.Equal(FileDate, dirEntries[1].ModifiedDate);
        Assert.Equal("a", text);
        await Assert.ThrowsAsync<NotSupportedException>(() => archiveVolume.OpenWrite("new.txt"));
    }

    [Fact]
    public async Task When_CompressedTarArchiveIsClosed_Then_ItsTemporaryFileIsDeleted()
    {
        // arrange
        var temporaryFiles = Directory.GetFiles(Path.GetTempPath(), "hst-commander-*.tar").ToHashSet();

        // act
        var volume = await CreateVolumeAsync("dirs-files.tar.xz");
        var openFiles = Directory.GetFiles(Path.GetTempPath(), "hst-commander-*.tar").Except(temporaryFiles).ToList();
        ((IDisposable)volume).Dispose();
        var closedFiles = Directory.GetFiles(Path.GetTempPath(), "hst-commander-*.tar").Except(temporaryFiles).ToList();

        // assert
        Assert.Single(openFiles);
        Assert.Empty(closedFiles);
    }

    [Theory]
    [InlineData("hello.txt.gz", "hello.txt", true)]
    [InlineData("noname.txt.gz", "noname.txt", false)]
    [InlineData("hello.txt.xz", "hello.txt", false)]
    public async Task When_CompressedFileIsOpened_Then_DecompressedFileIsListedAndRead(string fileName,
        string expectedName, bool hasDate)
    {
        // arrange
        var volume = await CreateVolumeAsync(fileName);

        // act
        var entry = Assert.Single(await volume.ListEntries().ToListAsync());
        var text = await ReadTextAsync(volume, entry.Name);
        var textAgain = await ReadTextAsync(volume, entry.Name);

        // assert
        Assert.Equal(expectedName, entry.Name);
        Assert.Equal(EntryType.Item, entry.Type);
        Assert.Equal(6, entry.Size);
        Assert.Equal(hasDate ? FileDate : null, entry.ModifiedDate);
        Assert.Equal("Hello\n", text);
        Assert.Equal("Hello\n", textAgain);
    }

    [Theory]
    [InlineData("disk.img.xz", "disk.img")]
    [InlineData("backup.TXZ", "backup.tar")]
    [InlineData("data", "data")]
    public void When_XzFileIsNamed_Then_DecompressedFileIsNamedAfterIt(string fileName, string expected) =>
        Assert.Equal(expected, new XzFileVolumeProvider().GetDecompressedName(fileName));

    [Theory]
    [InlineData("multi.txt.gz")]
    [InlineData("multi.txt.xz")]
    public async Task When_CompressedFileHasSeveralMembers_Then_AllAreRead(string fileName)
    {
        // arrange
        var volume = await CreateVolumeAsync(fileName);

        // act
        var entry = Assert.Single(await volume.ListEntries().ToListAsync());
        var text = await ReadTextAsync(volume, entry.Name);

        // assert
        Assert.Equal("Hello\nWorld\n", text);
    }

    [Fact]
    public async Task When_GzipFileIsTruncated_Then_ReadingItFails()
    {
        // arrange
        var data = ArchiveTestData.Read("hello.txt.gz");

        // act & assert - a small file is read to its end when checked for a tar archive, a large one when read
        await Assert.ThrowsAsync<InvalidDataException>(async () =>
        {
            var volume = await new GzipFileVolumeProvider().CreateVolume("hello.txt.gz",
                new MemoryStream(data[..^10]));
            await ReadTextAsync(volume, "hello.txt");
        });
    }

    [Fact]
    public async Task When_LargeGzipFileIsDamaged_Then_ReadingItFails()
    {
        // arrange - a 1 MB file of random bytes compressed, with a byte of its compressed data changed
        var data = new byte[1024 * 1024];
        new Random(1).NextBytes(data);
        var compressed = new MemoryStream();
        await using (var gzip = new System.IO.Compression.GZipStream(compressed,
                         System.IO.Compression.CompressionLevel.Fastest, true))
        {
            await gzip.WriteAsync(data);
        }

        var damaged = compressed.ToArray();
        damaged[damaged.Length / 2] ^= 0xFF;
        var volume = await new GzipFileVolumeProvider().CreateVolume("random.bin.gz", new MemoryStream(damaged));

        // act & assert
        await Assert.ThrowsAsync<InvalidDataException>(async () =>
        {
            await using var stream = await volume.OpenRead("random.bin");
            await stream.CopyToAsync(Stream.Null);
        });
    }

    [Fact]
    public async Task When_TarArchiveHasLinks_Then_TheyAreListedButNotRead()
    {
        // arrange
        using var volume = (IDisposable)await CreateVolumeAsync("links.tar");
        var archiveVolume = (IVolume)volume;
        await archiveVolume.ChangeDirectory(["dir2"]);

        // act
        var entries = await archiveVolume.ListEntries().ToListAsync();

        // assert
        Assert.Equal(["hardlink.txt", "symlink.txt"], entries.Select(entry => entry.Name));
        var exception = await Assert.ThrowsAsync<NotSupportedException>(() => archiveVolume.OpenRead("symlink.txt"));
        Assert.Contains("../dir1/file1.txt", exception.Message);
    }

    [Fact]
    public async Task When_DiskImageInGzipFileIsOpened_Then_ItIsBrowsed()
    {
        // arrange
        File.Copy(Path.Combine(AppContext.BaseDirectory, "TestData", "Disks", "xfs.img.gz"),
            Path.Combine(_rootPath, "xfs.img.gz"));
        using var navigationContext = new NavigationContext(_rootPath);

        // act
        await navigationContext.OpenEntryAsync(
            (await navigationContext.ListEntriesAsync()).First(entry => entry.Name == "xfs.img.gz"));
        var gzipNames = (await navigationContext.ListEntriesAsync()).Select(entry => entry.Name).ToList();
        await navigationContext.OpenEntryAsync(
            (await navigationContext.ListEntriesAsync()).First(entry => entry.Name == "xfs.img"));
        var imageNames = (await navigationContext.ListEntriesAsync()).Select(entry => entry.Name).ToList();

        // assert
        Assert.Equal([Paths.Parent, "xfs.img"], gzipNames);
        Assert.Equal([Paths.Parent, "dir", "hello.txt"], imageNames);
        Assert.Equal(Path.Combine(_rootPath, "xfs.img.gz", "xfs.img"), navigationContext.CurrentPath);
    }
}
