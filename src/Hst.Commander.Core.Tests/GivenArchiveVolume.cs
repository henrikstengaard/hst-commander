using System.Text;
using Hst.Commander.Abstractions;

namespace Hst.Commander.Core.Tests;

public class GivenArchiveVolume
{
    [Fact]
    public async Task When_EntriesAreAdded_Then_DirectoriesAreCreatedFromFlagsSeparatorsAndPaths()
    {
        // arrange - "dir1" is a directory by its flag, "dir2/" by its separator and "dir3" only in the path of a file
        var volume = new TestArchiveVolume(false,
            ("dir1", true), ("dir2/", false), (@"dir3\file.txt", false), ("./../file.txt", false));

        // act
        var entries = await volume.ListEntries().ToListAsync();

        // assert - "." and ".." are skipped
        Assert.Equal(["dir1", "dir2", "dir3", "file.txt"], entries.Select(entry => entry.Name));
        Assert.Equal([EntryType.Container, EntryType.Container, EntryType.Container, EntryType.Item],
            entries.Select(entry => entry.Type));
    }

    [Fact]
    public async Task When_OpenReadWithOtherCase_Then_FileIsOpenedWithItsNameInTheArchive()
    {
        // arrange
        var volume = new TestArchiveVolume(false, ("Dir/File.txt", false));
        await volume.ChangeDirectory(["dir"]);

        // act
        await using var stream = await volume.OpenRead("FILE.TXT");
        using var reader = new StreamReader(stream);

        // assert - the derived volume gets the name and entry of the file
        Assert.Equal("File.txt:Dir/File.txt", await reader.ReadToEndAsync());
        Assert.Equal(["Dir"], volume.CurrentPathComponents);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task When_SupportsAmigaMetadata_Then_ValueGivenToConstructorIsReturned(bool supportsAmigaMetadata)
    {
        // arrange
        var volume = new TestArchiveVolume(supportsAmigaMetadata);

        // act & assert
        Assert.Equal(supportsAmigaMetadata, await volume.SupportsAmigaMetadata());
        await Assert.ThrowsAsync<NotSupportedException>(() =>
            volume.SetAmigaMetadata("file", new AmigaMetadata(0, string.Empty)));
        await Assert.ThrowsAsync<NotSupportedException>(() => volume.OpenWrite("file"));
    }

    private sealed class TestArchiveVolume : ArchiveVolume<string>
    {
        public TestArchiveVolume(bool supportsAmigaMetadata, params (string Path, bool IsDirectory)[] entries)
            : base("Test", supportsAmigaMetadata)
        {
            foreach (var (path, isDirectory) in entries)
            {
                AddEntry(path, path.Length, null, path, isDirectory: isDirectory);
            }
        }

        protected override Task<Stream> OpenArchiveEntry(string name, string archiveEntry,
            CancellationToken cancellationToken) =>
            Task.FromResult<Stream>(new MemoryStream(Encoding.UTF8.GetBytes($"{name}:{archiveEntry}")));
    }
}
