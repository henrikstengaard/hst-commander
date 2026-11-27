using Hst.Commander.Abstractions;
using Hst.Commander.Core.Volumes;
using Hst.Commander.Plugins.LhaLzx;
using Hst.Compression.Lha;

namespace Hst.Commander.Core.Tests;

public class GivenWritableLhaVolume
{
    private static readonly DateTime Date = new(2024, 5, 17, 13, 37, 42, DateTimeKind.Utc);

    /// <summary>Creates a writable volume of an empty archive created with the volume creator.</summary>
    private static async Task<(IVolume Volume, MemoryStream Stream)> CreateEmptyAsync()
    {
        var stream = new MemoryStream();
        await new LhaVolumeCreator().Create(stream, new VolumeCreatorValues(new Dictionary<string, object?>()));
        return (await new LhaFileVolumeProvider().CreateVolume(stream), stream);
    }

    /// <summary>Creates a writable volume of a copy of a test archive.</summary>
    private static async Task<(IVolume Volume, MemoryStream Stream)> CreateAsync(string fileName)
    {
        var stream = new MemoryStream();
        stream.Write(ArchiveTestData.Read(fileName));
        return (await new LhaFileVolumeProvider().CreateVolume(stream), stream);
    }

    /// <summary>Opens the archive written again with a new, read-only volume, checking it's valid.</summary>
    private static async Task<IVolume> ReopenAsync(MemoryStream stream) =>
        await new LhaFileVolumeProvider().CreateVolume(new MemoryStream(stream.ToArray(), false));

    private static async Task WriteAsync(IVolume volume, string name, byte[] data)
    {
        await using var stream = await volume.OpenWrite(name);
        await stream.WriteAsync(data);
    }

    /// <summary>Reads a file to the end, which verifies the crc of its data.</summary>
    private static async Task<byte[]> ReadAsync(IVolume volume, string name)
    {
        await using var stream = await volume.OpenRead(name);
        var data = new MemoryStream();
        await stream.CopyToAsync(data);
        return data.ToArray();
    }

    private static async Task<List<string>> ListNamesAsync(IVolume volume) =>
        await volume.ListEntries().Select(entry => entry.Name).ToListAsync();

    private static byte[] CreateData(int size, int seed) =>
        Enumerable.Range(0, size).Select(index => (byte)("lha archive data "[index % 17] + seed % 3)).ToArray();

    [Theory]
    [InlineData("new.lha", new byte[0], true)]
    [InlineData("NEW.LZH", new byte[] { 0 }, true)]
    [InlineData("new.txt", new byte[0], false)]
    [InlineData("new.lha", new byte[] { 1 }, false)]
    public async Task When_IsMatchEmptyFile_Then_EmptyArchivesAreDetectedByExtension(string name, byte[] data,
        bool expected)
    {
        // act
        var isMatch = await new LhaFileVolumeProvider().IsMatch(name, new MemoryStream(data));

        // assert
        Assert.Equal(expected, isMatch);
    }

    [Fact]
    public async Task When_CreateWithCreator_Then_EmptyArchiveIsCreated()
    {
        // act
        var (volume, stream) = await CreateEmptyAsync();

        // assert - only the end of archive marker
        Assert.Equal([0], stream.ToArray());
        Assert.Empty(await ListNamesAsync(volume));
        Assert.True(await volume.SupportsAmigaMetadata());
        Assert.True(await volume.CanSetAmigaMetadata());
        Assert.True(new LhaFileVolumeProvider().CanWrite);
    }

    [Fact]
    public async Task When_WriteFileWithDateAndAmigaMetadata_Then_FileIsReadFromArchive()
    {
        // arrange
        var (volume, stream) = await CreateEmptyAsync();
        var data = CreateData(20000, 1);

        // act - copying sets the Amiga metadata, then the date
        await WriteAsync(volume, "file.txt", data);
        await volume.SetAmigaMetadata("file.txt", new AmigaMetadata(0x42, "Comment with / and \\"));
        await volume.SetModifiedDate("file.txt", Date);

        // assert - the volume and the archive opened again
        foreach (var readVolume in new[] { volume, await ReopenAsync(stream) })
        {
            var entry = Assert.Single(await readVolume.ListEntries().ToListAsync());
            Assert.Equal("file.txt", entry.Name);
            Assert.Equal(data.Length, entry.Size);
            Assert.Equal(Date, entry.ModifiedDate);
            Assert.Equal(new AmigaMetadata(0x42, "Comment with / and \\"), entry.AmigaMetadata);
            Assert.Equal(data, await ReadAsync(readVolume, "file.txt"));
        }

        // assert - an Amiga entry with a level 2 header, compressed with -lh5-
        stream.Position = 0;
        var lhaEntry = Assert.Single(await new LhaArchive(stream, LhaOptions.AmigaLhaOptions, true).EntriesAsync());
        Assert.Equal(LhaVolume.DefaultHeaderLevel, lhaEntry.HeaderLevel);
        Assert.Equal(LhaOsType.Amiga, lhaEntry.OsType);
        Assert.Equal(LhaMethod.Lh5, lhaEntry.Method);
    }

    [Fact]
    public async Task When_CreateContainerAndWriteFiles_Then_DirectoriesAndFilesAreListed()
    {
        // arrange
        var (volume, stream) = await CreateEmptyAsync();

        // act
        await volume.CreateContainer("dir");
        await volume.CreateContainer("dir");
        await volume.ChangeDirectory(["dir"]);
        await volume.CreateContainer("sub");
        await WriteAsync(volume, "file1.txt", CreateData(100, 1));
        await volume.SetModifiedDate("sub", Date);

        // assert - the current directory is kept when the archive changes
        Assert.Equal(["dir"], volume.CurrentPathComponents);
        Assert.Equal(["sub", "file1.txt"], await ListNamesAsync(volume));
        var reopened = await ReopenAsync(stream);
        Assert.Equal(["dir"], await ListNamesAsync(reopened));
        await reopened.ChangeDirectory(["dir"]);
        var entries = await reopened.ListEntries().ToListAsync();
        Assert.Equal(["sub", "file1.txt"], entries.Select(entry => entry.Name));
        Assert.Equal(Date, entries[0].ModifiedDate);
        Assert.Equal(CreateData(100, 1), await ReadAsync(reopened, "file1.txt"));
    }

    [Fact]
    public async Task When_OverwriteFile_Then_FileHasNewContent()
    {
        // arrange
        var (volume, stream) = await CreateEmptyAsync();
        await WriteAsync(volume, "file.txt", CreateData(1000, 1));
        await WriteAsync(volume, "other.txt", CreateData(500, 2));

        // act - the name is found case-insensitive and its case is kept
        await WriteAsync(volume, "FILE.TXT", CreateData(3000, 3));

        // assert
        var reopened = await ReopenAsync(stream);
        Assert.Equal(["file.txt", "other.txt"], await ListNamesAsync(reopened));
        Assert.Equal(CreateData(3000, 3), await ReadAsync(reopened, "file.txt"));
        Assert.Equal(CreateData(500, 2), await ReadAsync(reopened, "other.txt"));
    }

    [Theory]
    [InlineData("file1.txt")]
    [InlineData("file2.txt")]
    [InlineData("file3.txt")]
    public async Task When_DeleteFile_Then_OtherFilesAreKept(string name)
    {
        // arrange
        var (volume, stream) = await CreateEmptyAsync();
        var files = Enumerable.Range(1, 3).ToDictionary(index => $"file{index}.txt", index => CreateData(5000, index));
        foreach (var (fileName, data) in files)
        {
            await WriteAsync(volume, fileName, data);
        }

        // act
        await volume.Delete(name);

        // assert
        var reopened = await ReopenAsync(stream);
        var remaining = files.Where(file => file.Key != name).ToList();
        Assert.Equal(remaining.Select(file => file.Key), await ListNamesAsync(reopened));
        foreach (var (fileName, data) in remaining)
        {
            Assert.Equal(data, await ReadAsync(reopened, fileName));
        }

        // assert - the archive ends after the last entry
        stream.Position = 0;
        var lhaEntries = await new LhaArchive(stream, LhaOptions.AmigaLhaOptions, true).EntriesAsync();
        var last = lhaEntries.Last();
        Assert.Equal(last.HeaderOffset + last.HeaderSize + last.PackedSize + 1, stream.Length);
    }

    [Fact]
    public async Task When_DeleteDirectory_Then_EmptyDirectoryIsDeletedAndOtherIsNot()
    {
        // arrange
        var (volume, stream) = await CreateEmptyAsync();
        await volume.CreateContainer("empty");
        await volume.CreateContainer("dir");
        await volume.ChangeDirectory(["dir"]);
        await WriteAsync(volume, "file.txt", CreateData(100, 1));
        await volume.ChangeDirectory([Paths.Root]);

        // act & assert
        await Assert.ThrowsAnyAsync<IOException>(() => volume.Delete("dir"));
        await volume.Delete("empty");
        await Assert.ThrowsAnyAsync<IOException>(() => volume.Delete("missing"));

        // assert
        Assert.Equal(["dir"], await ListNamesAsync(await ReopenAsync(stream)));
    }

    [Fact]
    public async Task When_RenameFileAndDirectory_Then_EntriesAreRenamedWithTheirMetadata()
    {
        // arrange
        var (volume, stream) = await CreateEmptyAsync();
        await volume.CreateContainer("dir");
        await volume.ChangeDirectory(["dir"]);
        await WriteAsync(volume, "file.txt", CreateData(2000, 1));
        await volume.SetAmigaMetadata("file.txt", new AmigaMetadata(0x02, "Comment"));
        await volume.ChangeDirectory([Paths.Root]);
        await WriteAsync(volume, "root.txt", CreateData(1000, 2));

        // act
        await volume.Rename("root.txt", "renamed-root-file.txt");
        await volume.Rename("dir", "Directory");
        await Assert.ThrowsAnyAsync<IOException>(() => volume.Rename("Directory", "renamed-root-file.txt"));

        // assert
        var reopened = await ReopenAsync(stream);
        Assert.Equal(["Directory", "renamed-root-file.txt"], await ListNamesAsync(reopened));
        Assert.Equal(CreateData(1000, 2), await ReadAsync(reopened, "renamed-root-file.txt"));
        await reopened.ChangeDirectory(["Directory"]);
        var entry = Assert.Single(await reopened.ListEntries().ToListAsync());
        Assert.Equal("file.txt", entry.Name);
        Assert.Equal(new AmigaMetadata(0x02, "Comment"), entry.AmigaMetadata);
        Assert.Equal(CreateData(2000, 1), await ReadAsync(reopened, "file.txt"));
    }

    [Fact]
    public async Task When_RenameOnlyCase_Then_EntryIsRenamed()
    {
        // arrange
        var (volume, stream) = await CreateEmptyAsync();
        await WriteAsync(volume, "file.txt", CreateData(100, 1));

        // act
        await volume.Rename("file.txt", "FILE.TXT");

        // assert
        Assert.Equal(["FILE.TXT"], await ListNamesAsync(await ReopenAsync(stream)));
    }

    [Fact]
    public async Task When_DeleteLastFileInDirectoryWithoutEntry_Then_CurrentDirectoryBecomesParent()
    {
        // arrange - test1 and test2 only exist in the path of test1/test2/test2.txt
        var (volume, _) = await CreateAsync("amiga.lha");
        await volume.ChangeDirectory(["test1", "test2"]);

        // act
        await volume.Delete("test2.txt");

        // assert - test2 no longer exists, nor does test1 when it has no other entries
        var currentPath = volume.CurrentPathComponents;
        Assert.DoesNotContain("test2", currentPath);
        await volume.ChangeDirectory([Paths.Root]);
        Assert.Equal(currentPath.Count > 0, (await ListNamesAsync(volume)).Contains("test1"));
    }

    [Fact]
    public async Task When_DeleteLastFileInDirectoryWithEntry_Then_CurrentDirectoryIsKept()
    {
        // arrange
        var (volume, _) = await CreateEmptyAsync();
        await volume.CreateContainer("dir");
        await volume.ChangeDirectory(["dir"]);
        await WriteAsync(volume, "file.txt", CreateData(100, 1));

        // act
        await volume.Delete("file.txt");

        // assert - the directory entry is kept
        Assert.Equal(["dir"], volume.CurrentPathComponents);
        Assert.Empty(await ListNamesAsync(volume));
    }

    [Fact]
    public async Task When_WriteToExistingArchive_Then_OriginalEntriesAreKept()
    {
        // arrange - amiga.lha has level 0 headers and test1 only exists in the paths of its files
        var (volume, stream) = await CreateAsync("amiga.lha");
        var longName = new string('a', 240) + ".txt";

        // act
        await WriteAsync(volume, "new.txt", CreateData(4000, 1));
        await WriteAsync(volume, longName, CreateData(10, 2));
        await volume.SetModifiedDate("test1", Date);
        await volume.SetAmigaMetadata("test.txt", new AmigaMetadata(0x10, "Changed"));

        // assert
        var reopened = await ReopenAsync(stream);
        var entries = await reopened.ListEntries().ToListAsync();
        Assert.Equal(["test1", longName, "new.txt", "test.txt", "test1.info"], entries.Select(entry => entry.Name));
        Assert.Equal(Date, entries[0].ModifiedDate);
        Assert.Equal(new AmigaMetadata(0x10, "Changed"), entries[3].AmigaMetadata);
        Assert.Equal(15, (await ReadAsync(reopened, "test.txt")).Length);
        Assert.Equal(900, (await ReadAsync(reopened, "test1.info")).Length);
        Assert.Equal(CreateData(4000, 1), await ReadAsync(reopened, "new.txt"));
        Assert.Equal(CreateData(10, 2), await ReadAsync(reopened, longName));
        await reopened.ChangeDirectory(["test1", "test2"]);
        Assert.Equal(["test2.txt"], await ListNamesAsync(reopened));

        // assert - new entries get the level of the last entry, level 2 when the name is too long for it
        stream.Position = 0;
        var lhaEntries = await new LhaArchive(stream, LhaOptions.AmigaLhaOptions, true).EntriesAsync();
        Assert.Equal(0, lhaEntries.Single(lhaEntry => lhaEntry.Name == "new.txt").HeaderLevel);
        Assert.Equal(2, lhaEntries.Single(lhaEntry => lhaEntry.Name == longName).HeaderLevel);
    }

    [Fact]
    public async Task When_ChangeArchiveWithSymbolicLinks_Then_LinksAreKept()
    {
        // arrange - dir2 contains the symbolic links symlink1 and symlink2
        var (volume, stream) = await CreateAsync("test_read_format_lha_header2.lzh");

        // act
        await volume.Rename("file1", "renamed");
        await volume.Rename("dir2", "links");

        // assert
        stream.Position = 0;
        var links = (await new LhaArchive(stream, LhaOptions.AmigaLhaOptions, true).EntriesAsync())
            .Where(lhaEntry => lhaEntry.LinkTarget != null)
            .ToList();
        Assert.Equal(2, links.Count);
        Assert.All(links, link => Assert.StartsWith(@"links\", link.Name));
        Assert.Equal(["dir", "links", "file2", "renamed"], await ListNamesAsync(await ReopenAsync(stream)));

        // act - a directory with only links is empty
        await volume.Delete("links");

        // assert
        Assert.Equal(["dir", "file2", "renamed"], await ListNamesAsync(await ReopenAsync(stream)));
    }

    [Fact]
    public async Task When_CopyFromAmigaArchiveAndBack_Then_FilesAndAmigaMetadataAreEqual()
    {
        // arrange
        var source = await new LhaFileVolumeProvider().CreateVolume(
            new MemoryStream(ArchiveTestData.Read("amiga.lha"), false));
        var (destination, stream) = await CreateEmptyAsync();

        // act
        await source.CopyToAsync(await source.ListEntries().ToListAsync(), destination);

        // assert - same entries, dates, Amiga metadata and data
        var copy = await ReopenAsync(stream);
        await AssertEqualAsync(source, copy);
        await source.ChangeDirectory(["test1", "test2"]);
        await copy.ChangeDirectory(["test1", "test2"]);
        await AssertEqualAsync(source, copy);
    }

    private static async Task AssertEqualAsync(IVolume expected, IVolume actual)
    {
        var expectedEntries = await expected.ListEntries().ToListAsync();
        var actualEntries = await actual.ListEntries().ToListAsync();
        Assert.Equal(expectedEntries.Select(entry => (entry.Name, entry.Type, entry.Size)),
            actualEntries.Select(entry => (entry.Name, entry.Type, entry.Size)));

        // directories only in the paths of files have no date and metadata of their own
        foreach (var entry in expectedEntries.Where(entry => entry.Type == EntryType.Item))
        {
            var actualEntry = actualEntries.Single(x => x.Name == entry.Name);
            Assert.Equal(entry.ModifiedDate, actualEntry.ModifiedDate);
            Assert.Equal(entry.AmigaMetadata, actualEntry.AmigaMetadata);
            Assert.Equal(await ReadAsync(expected, entry.Name), await ReadAsync(actual, entry.Name));
        }
    }
}
