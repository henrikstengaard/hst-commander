using DiscUtils.Iso9660;
using Hst.Commander.Core.Volumes;
using Hst.Commander.Extensions.Volume.Abstractions;

namespace Hst.Commander.Core.Tests;

public class GivenIso9660Volume
{
    [Fact]
    public async Task When_ListEntriesInRoot_Then_Iso9660VolumeListsEntries()
    {
        // arrange - create an iso9660 volume with entries
        await using var iso9660Stream = CreateIso9660WithDirectoriesAndFiles();
        var iso9660Volume = new Iso9660Volume(new CDReader(iso9660Stream, true));
        
        // act - list entries in iso9660 volume
        var entries = await iso9660Volume.ListEntries().ToListAsync();
        
        // assert - root lists 3 entries from iso9660 volume
        Assert.Equal(3, entries.Count);
        var dir1Entry = entries.FirstOrDefault(entry => entry.Name.Equals("dir1") && entry.Type == EntryType.Container);
        Assert.Equal("dir1", dir1Entry?.Name);
        var file1TxtEntry = entries.FirstOrDefault(entry => entry.Name.Equals("file1.txt") && entry.Type == EntryType.Item);
        Assert.Equal("file1.txt", file1TxtEntry?.Name);
        var file2TxtEntry = entries.FirstOrDefault(entry => entry.Name.Equals("file2.txt") && entry.Type == EntryType.Item);
        Assert.Equal("file2.txt", file2TxtEntry?.Name);
    }

    [Fact]
    public async Task When_ChangeDirectory_Then_DirectoryIsChanged()
    {
        // arrange - create an iso9660 volume with entries
        await using var iso9660Stream = CreateIso9660WithDirectoriesAndFiles();
        var iso9660Volume = new Iso9660Volume(new CDReader(iso9660Stream, true));

        // assert - current path components of the iso9660 volume is root
        Assert.Equal([], iso9660Volume.CurrentPathComponents);

        // act - change directory to "dir1"
        await iso9660Volume.ChangeDirectory(["dir1"]);
        
        // assert - current path components of the iso9660 volume is changed to "dir1"
        Assert.Equal(["dir1"], iso9660Volume.CurrentPathComponents);
    }

    [Fact]
    public async Task When_ChangeDirectoryToParent_Then_CurrentDirectoryIsChanged()
    {
        await using var iso9660Stream = CreateIso9660WithDirectoriesAndFiles();
        var iso9660Volume = new Iso9660Volume(new CDReader(iso9660Stream, true));
        await iso9660Volume.ChangeDirectory(["dir1"]);

        await iso9660Volume.ChangeDirectory([Paths.Parent]);

        Assert.Equal([], iso9660Volume.CurrentPathComponents);
    }

    [Fact]
    public async Task When_OpenDirectoryEntry_Then_DirectoryIsChanged()
    {
        // arrange - create an iso9660 volume with entries
        await using var iso9660Stream = CreateIso9660WithDirectoriesAndFiles();
        var iso9660Volume = new Iso9660Volume(new CDReader(iso9660Stream, true));

        // assert - current path components of the iso9660 volume is root
        Assert.Equal([], iso9660Volume.CurrentPathComponents);

        // act - open entry "dir1"
        var entry = await iso9660Volume.OpenEntry("dir1");
        
        // assert - entry is a directory
        Assert.NotNull(entry);
        Assert.Equal("dir1", entry.Name);
        Assert.Equal(EntryType.Container, entry.Type);
        
        // assert - current path components of the iso9660 volume is changed to "dir1"
        Assert.Equal(["dir1"], iso9660Volume.CurrentPathComponents);
    }

    [Fact]
    public async Task When_OpenFileEntry_Then_DirectoryIsNotChanged()
    {
        // arrange - create an iso9660 volume with entries
        await using var iso9660Stream = CreateIso9660WithDirectoriesAndFiles();
        var iso9660Volume = new Iso9660Volume(new CDReader(iso9660Stream, true));

        // assert - current path components of the iso9660 volume is root
        Assert.Equal([], iso9660Volume.CurrentPathComponents);

        // act - open entry "file1.txt"
        var entry = await iso9660Volume.OpenEntry("file1.txt");
        
        // assert - entry is a file
        Assert.NotNull(entry);
        Assert.Equal("file1.txt", entry.Name);
        Assert.Equal(EntryType.Item, entry.Type);
        
        // assert - current path components of the iso9660 volume is root
        Assert.Equal([], iso9660Volume.CurrentPathComponents);
    }

    [Fact]
    public async Task When_ListEntriesInDirectory_Then_Iso9660VolumeListsEntries()
    {
        // arrange - create an iso9660 volume with entries
        await using var iso9660Stream = CreateIso9660WithDirectoriesAndFiles();
        var iso9660Volume = new Iso9660Volume(new CDReader(iso9660Stream, true));
        
        // act - change directory to "dir1"
        await iso9660Volume.ChangeDirectory(["dir1"]);
        
        // act - list entries in iso9660 volume
        var entries = await iso9660Volume.ListEntries().ToListAsync();
        
        // assert - directory lists 3 entries from iso9660 volume
        Assert.Equal(3, entries.Count);
        var dir2Entry = entries.FirstOrDefault(entry => entry.Name.Equals("dir2") && entry.Type == EntryType.Container);
        Assert.Equal("dir2", dir2Entry?.Name);
        var file3TxtEntry = entries.FirstOrDefault(entry => entry.Name.Equals("file3.txt") && entry.Type == EntryType.Item);
        Assert.Equal("file3.txt", file3TxtEntry?.Name);
        var testTxtEntry = entries.FirstOrDefault(entry => entry.Name.Equals("test.txt") && entry.Type == EntryType.Item);
        Assert.Equal("test.txt", testTxtEntry?.Name);
    }

    private static Stream CreateIso9660WithDirectoriesAndFiles()
    {
        var builder = new CDBuilder
        {
            UseJoliet = true
        };

        builder.AddFile("file1.txt", []);
        builder.AddFile("file2.txt", []);
        builder.AddDirectory("dir1");
        builder.AddFile(Path.Combine("dir1", "file3.txt"), []);
        builder.AddFile(@"dir1\test.txt", []);
        builder.AddDirectory("dir1/dir2");
        builder.AddFile(Path.Combine("dir1", "dir2", "file4.txt"), []);

        var iso9660Stream = new MemoryStream();
        builder.Build(iso9660Stream);

        iso9660Stream.Position = 0;
        return iso9660Stream;
    }
}