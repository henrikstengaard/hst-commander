using Hst.Commander.Abstractions;
using Hst.Commander.Plugins.LhaLzx;

namespace Hst.Commander.Core.Tests;

public class GivenLhaVolume
{
    private static async Task<IVolume> CreateLhaVolumeAsync(string fileName) =>
        await new LhaFileVolumeProvider().CreateVolume(new MemoryStream(ArchiveTestData.Read(fileName)));

    [Theory]
    [InlineData("dirs-files.lha")]
    [InlineData("amiga.lha")]
    [InlineData("test_read_format_lha_header2.lzh")]
    public async Task When_IsMatchLhaArchive_Then_ArchiveIsDetected(string fileName)
    {
        // arrange
        var stream = new MemoryStream(ArchiveTestData.Read(fileName));

        // act
        var isMatch = await new LhaFileVolumeProvider().IsMatch(fileName, stream);

        // assert
        Assert.True(isMatch);
    }

    [Fact]
    public async Task When_IsMatchOtherFile_Then_FileIsNotDetected()
    {
        // arrange
        var lhaFileVolumeProvider = new LhaFileVolumeProvider();

        // act - a lzx archive and a file shorter than a header
        var isLzxMatch = await lhaFileVolumeProvider.IsMatch("archive.lha",
            new MemoryStream(ArchiveTestData.Read(ArchiveTestData.DirsFiles + ".lzx")));
        var isShortMatch = await lhaFileVolumeProvider.IsMatch("short.lha", new MemoryStream("  -lh0"u8.ToArray()));

        // assert
        Assert.False(isLzxMatch);
        Assert.False(isShortMatch);
    }

    [Fact]
    public async Task When_ListEntriesInRoot_Then_DirectoriesAndFilesAreListed()
    {
        // arrange
        var lhaVolume = await CreateLhaVolumeAsync("amiga.lha");

        // act
        var entries = await lhaVolume.ListEntries().ToListAsync();

        // assert - directories first, test1 only exists in the paths of files
        Assert.Equal(["test1", "test.txt", "test1.info"], entries.Select(entry => entry.Name));
        Assert.Equal([EntryType.Container, EntryType.Item, EntryType.Item], entries.Select(entry => entry.Type));
        Assert.Equal([null, 15L, 900L], entries.Select(entry => entry.Size));
    }

    [Fact]
    public async Task When_ChangeDirectory_Then_EntriesInDirectoryAreListed()
    {
        // arrange
        var lhaVolume = await CreateLhaVolumeAsync("amiga.lha");

        // act
        await lhaVolume.ChangeDirectory(["test1", "test2"]);
        var entries = await lhaVolume.ListEntries().ToListAsync();

        // assert
        Assert.Equal(["test1", "test2"], lhaVolume.CurrentPathComponents);
        Assert.Equal(["test2.txt"], entries.Select(entry => entry.Name));
    }

    [Fact]
    public async Task When_ListEntriesWithDirectoryEntries_Then_DirectoriesAreListed()
    {
        // arrange
        var lhaVolume = await CreateLhaVolumeAsync(ArchiveTestData.DirsFiles + ".lha");

        // act
        var rootNames = await lhaVolume.ListEntries().Select(entry => entry.Name).ToListAsync();
        await lhaVolume.ChangeDirectory(["dir1"]);
        var dirNames = await lhaVolume.ListEntries().Select(entry => entry.Name).ToListAsync();

        // assert
        Assert.Equal(["dir1", "dir2"], rootNames);
        Assert.Equal(["dir3", "file1.txt"], dirNames);
    }

    [Fact]
    public async Task When_ListEntriesWithSymbolicLinks_Then_LinksAreSkipped()
    {
        // arrange - dir2 contains the symbolic links symlink1 and symlink2
        var lhaVolume = await CreateLhaVolumeAsync("test_read_format_lha_header2.lzh");

        // act
        var rootNames = await lhaVolume.ListEntries().Select(entry => entry.Name).ToListAsync();
        await lhaVolume.ChangeDirectory(["dir2"]);
        var dirNames = await lhaVolume.ListEntries().Select(entry => entry.Name).ToListAsync();

        // assert
        Assert.Equal(["dir", "dir2", "file1", "file2"], rootNames);
        Assert.Empty(dirNames);
    }

    [Fact]
    public async Task When_OpenRead_Then_StoredAndCompressedContentIsRead()
    {
        // arrange - test.txt is stored (-lh0-), test1.info compressed (-lh5-)
        var lhaVolume = await CreateLhaVolumeAsync("amiga.lha");

        // act
        var storedData = new MemoryStream();
        await using (var stream = await lhaVolume.OpenRead("test.txt"))
        {
            await stream.CopyToAsync(storedData);
        }

        var compressedData = new MemoryStream();
        await using (var stream = await lhaVolume.OpenRead("TEST1.INFO"))
        {
            await stream.CopyToAsync(compressedData);
        }

        // assert - the entry streams verify the crc when read to the end
        Assert.Equal(15, storedData.Length);
        Assert.Equal(900, compressedData.Length);
    }

    [Fact]
    public async Task When_ChangeDirectoryToMissingDirectory_Then_IOExceptionIsThrownAndDirectoryIsUnchanged()
    {
        // arrange
        var lhaVolume = await CreateLhaVolumeAsync("amiga.lha");

        // act & assert - a file is not a directory
        await Assert.ThrowsAnyAsync<IOException>(() => lhaVolume.ChangeDirectory(["test1", "missing"]));
        await Assert.ThrowsAnyAsync<IOException>(() => lhaVolume.ChangeDirectory(["test.txt"]));
        Assert.Empty(lhaVolume.CurrentPathComponents);
    }

    [Fact]
    public async Task When_Write_Then_NotSupportedExceptionIsThrown()
    {
        // arrange
        var lhaVolume = await CreateLhaVolumeAsync("amiga.lha");

        // act & assert
        await Assert.ThrowsAsync<NotSupportedException>(() => lhaVolume.OpenWrite("new.txt"));
        await Assert.ThrowsAsync<NotSupportedException>(() => lhaVolume.CreateContainer("new"));
        await Assert.ThrowsAsync<NotSupportedException>(() => lhaVolume.Delete("test.txt"));
        await Assert.ThrowsAsync<NotSupportedException>(() => lhaVolume.Rename("test.txt", "new.txt"));
        await Assert.ThrowsAsync<NotSupportedException>(() => lhaVolume.SetModifiedDate("test.txt", DateTime.UtcNow));
    }
}
