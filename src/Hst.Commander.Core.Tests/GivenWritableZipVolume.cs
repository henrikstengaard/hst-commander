using System.IO.Compression;
using Hst.Commander.Abstractions;
using Hst.Commander.Plugins.Zip;

namespace Hst.Commander.Core.Tests;

public class GivenWritableZipVolume
{
    private static readonly DateTime Date = new(2024, 5, 17, 13, 37, 42, DateTimeKind.Utc);

    /// <summary>Creates a writable volume of an empty archive created with the volume creator.</summary>
    private static async Task<(ZipVolume Volume, MemoryStream Stream)> CreateEmptyAsync()
    {
        var stream = new MemoryStream();
        await new ZipVolumeCreator().Create(stream, new VolumeCreatorValues(new Dictionary<string, object?>()));
        return ((ZipVolume)await new ZipFileVolumeProvider().CreateVolume(stream), stream);
    }

    /// <summary>Creates a writable volume of a copy of an archive.</summary>
    private static async Task<(ZipVolume Volume, MemoryStream Stream)> CreateAsync(byte[] zip)
    {
        var stream = new MemoryStream();
        stream.Write(zip);
        return ((ZipVolume)await new ZipFileVolumeProvider().CreateVolume(stream), stream);
    }

    /// <summary>Opens the archive written again with a new, read-only volume, checking it's valid.</summary>
    private static async Task<IVolume> ReopenAsync(MemoryStream stream) =>
        await new ZipFileVolumeProvider().CreateVolume(new MemoryStream(stream.ToArray(), false));

    /// <summary>Opens the archive written with <see cref="ZipArchive" />.</summary>
    private static ZipArchive OpenZipArchive(MemoryStream stream) =>
        new(new MemoryStream(stream.ToArray(), false), ZipArchiveMode.Read);

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
        Enumerable.Range(0, size).Select(index => (byte)("zip archive data "[index % 17] + seed % 3)).ToArray();

    private static byte[] CreateRandomData(int size)
    {
        var data = new byte[size];
        new Random(42).NextBytes(data);
        return data;
    }

    [Theory]
    [InlineData("new.zip", new byte[0], true)]
    [InlineData("NEW.ZIP", new byte[0], true)]
    [InlineData("new.txt", new byte[0], false)]
    [InlineData("new.zip", new byte[] { 1 }, false)]
    public async Task When_IsMatchEmptyFile_Then_EmptyArchivesAreDetectedByExtension(string name, byte[] data,
        bool expected)
    {
        // act
        var isMatch = await new ZipFileVolumeProvider().IsMatch(name, new MemoryStream(data));

        // assert
        Assert.Equal(expected, isMatch);
    }

    [Fact]
    public async Task When_CreateWithCreator_Then_EmptyArchiveIsCreated()
    {
        // act
        var (volume, stream) = await CreateEmptyAsync();

        // assert - only the end of central directory record
        Assert.Equal([0x50, 0x4b, 0x05, 0x06, ..new byte[18]], stream.ToArray());
        Assert.True(await new ZipFileVolumeProvider().IsMatch("archive.zip", new MemoryStream(stream.ToArray())));
        Assert.Empty(await ListNamesAsync(volume));
        Assert.True(volume.IsWritable);
        Assert.False(await volume.SupportsAmigaMetadata());
        Assert.True(new ZipFileVolumeProvider().CanWrite);
        Assert.Contains(VolumeProviders.Creators, creator => creator is ZipVolumeCreator);
    }

    [Fact]
    public async Task When_WriteFileWithDate_Then_FileIsCompressedAndReadFromArchive()
    {
        // arrange
        var (volume, stream) = await CreateEmptyAsync();
        var data = CreateData(20000, 1);

        // act - copying sets the date after writing
        await WriteAsync(volume, "file.txt", data);
        await volume.SetModifiedDate("file.txt", Date);

        // assert - the volume and the archive opened again
        foreach (var readVolume in new[] { volume, await ReopenAsync(stream) })
        {
            var entry = Assert.Single(await readVolume.ListEntries().ToListAsync());
            Assert.Equal("file.txt", entry.Name);
            Assert.Equal(data.Length, entry.Size);
            Assert.Equal(Date, entry.ModifiedDate);
            Assert.Equal(data, await ReadAsync(readVolume, "file.txt"));
        }

        // assert - compressed with deflate
        using var zipArchive = OpenZipArchive(stream);
        var zipArchiveEntry = Assert.Single(zipArchive.Entries);
        Assert.Equal("file.txt", zipArchiveEntry.FullName);
        Assert.True(zipArchiveEntry.CompressedLength < data.Length);
        Assert.Equal(Date, zipArchiveEntry.LastWriteTime.UtcDateTime);
    }

    [Fact]
    public async Task When_WriteIncompressibleAndEmptyFiles_Then_TheyAreStored()
    {
        // arrange
        var (volume, stream) = await CreateEmptyAsync();
        var data = CreateRandomData(10000);

        // act
        await WriteAsync(volume, "random.bin", data);
        await WriteAsync(volume, "empty.txt", []);

        // assert
        var readVolume = await ReopenAsync(stream);
        Assert.Equal(data, await ReadAsync(readVolume, "random.bin"));
        Assert.Empty(await ReadAsync(readVolume, "empty.txt"));
        using var zipArchive = OpenZipArchive(stream);
        Assert.Equal(data.Length, zipArchive.GetEntry("random.bin")!.CompressedLength);
        Assert.Equal(0, zipArchive.GetEntry("empty.txt")!.CompressedLength);
    }

    [Fact]
    public async Task When_CreateContainerAndWriteFiles_Then_DirectoriesAndFilesAreListed()
    {
        // arrange
        var (volume, stream) = await CreateEmptyAsync();

        // act
        await volume.CreateContainer("dir");
        await volume.ChangeDirectory(["dir"]);
        await volume.CreateContainer("sub");
        await WriteAsync(volume, "file1.txt", CreateData(100, 1));
        await volume.ChangeDirectory([Paths.Root]);
        await WriteAsync(volume, "file2.txt", CreateData(200, 2));

        // assert
        var readVolume = await ReopenAsync(stream);
        Assert.Equal(["dir", "file2.txt"], await ListNamesAsync(readVolume));
        await readVolume.ChangeDirectory(["dir"]);
        Assert.Equal(["sub", "file1.txt"], await ListNamesAsync(readVolume));
        Assert.Equal(CreateData(100, 1), await ReadAsync(readVolume, "file1.txt"));
        using var zipArchive = OpenZipArchive(stream);
        Assert.Equal(["dir/", "dir/sub/", "dir/file1.txt", "file2.txt"],
            zipArchive.Entries.Select(entry => entry.FullName));
    }

    [Fact]
    public async Task When_RenameDirectory_Then_EntriesInItAreRenamed()
    {
        // arrange
        var (volume, stream) = await CreateEmptyAsync();
        await WriteAsync(volume, "first.txt", CreateData(300, 0));
        await volume.CreateContainer("dir");
        await volume.ChangeDirectory(["dir"]);
        await WriteAsync(volume, "file.txt", CreateData(100, 1));
        await volume.ChangeDirectory([Paths.Root]);
        await WriteAsync(volume, "last.txt", CreateData(200, 2));

        // act - a longer name rewrites the archive from the first entry changed
        await volume.Rename("dir", "Renamed directory");

        // assert
        foreach (var readVolume in new[] { volume, await ReopenAsync(stream) })
        {
            Assert.Equal(["Renamed directory", "first.txt", "last.txt"], await ListNamesAsync(readVolume));
            Assert.Equal(CreateData(300, 0), await ReadAsync(readVolume, "first.txt"));
            Assert.Equal(CreateData(200, 2), await ReadAsync(readVolume, "last.txt"));
            await readVolume.ChangeDirectory(["Renamed directory"]);
            Assert.Equal(CreateData(100, 1), await ReadAsync(readVolume, "file.txt"));
        }

        using var zipArchive = OpenZipArchive(stream);
        Assert.Equal(["first.txt", "Renamed directory/", "Renamed directory/file.txt", "last.txt"],
            zipArchive.Entries.Select(entry => entry.FullName));
    }

    [Fact]
    public async Task When_RenameFileToNameOfSameLength_Then_HeaderIsWrittenInPlace()
    {
        // arrange
        var (volume, stream) = await CreateEmptyAsync();
        await WriteAsync(volume, "file1.txt", CreateData(100, 1));
        await WriteAsync(volume, "file2.txt", CreateData(200, 2));
        var length = stream.Length;

        // act
        await volume.Rename("file1.txt", "FILE3.TXT");

        // assert
        Assert.Equal(length, stream.Length);
        var readVolume = await ReopenAsync(stream);
        Assert.Equal(["file2.txt", "FILE3.TXT"], await ListNamesAsync(readVolume));
        Assert.Equal(CreateData(100, 1), await ReadAsync(readVolume, "FILE3.TXT"));
        Assert.Equal(CreateData(200, 2), await ReadAsync(readVolume, "file2.txt"));
    }

    [Fact]
    public async Task When_DeleteFilesAndDirectory_Then_TheyAreRemovedAndArchiveIsCompacted()
    {
        // arrange
        var (volume, stream) = await CreateEmptyAsync();
        await WriteAsync(volume, "file1.txt", CreateData(1000, 1));
        await WriteAsync(volume, "file2.txt", CreateData(2000, 2));
        await volume.CreateContainer("dir");
        await WriteAsync(volume, "file3.txt", CreateData(3000, 0));
        var (_, emptyStream) = await CreateEmptyAsync();

        // act - the first file, the last file and the empty directory
        await volume.Delete("file1.txt");
        await volume.Delete("file3.txt");
        await volume.Delete("dir");

        // assert
        var readVolume = await ReopenAsync(stream);
        Assert.Equal(["file2.txt"], await ListNamesAsync(readVolume));
        Assert.Equal(CreateData(2000, 2), await ReadAsync(readVolume, "file2.txt"));

        // act - the last entry
        await volume.Delete("file2.txt");

        // assert
        Assert.Empty(await ListNamesAsync(volume));
        Assert.Equal(emptyStream.ToArray(), stream.ToArray());
    }

    [Fact]
    public async Task When_DeleteDirectoryWithFiles_Then_IOExceptionIsThrown()
    {
        // arrange
        var (volume, _) = await CreateAsync(ZipTestData.CreateZipWithDirectoriesAndFiles());

        // act and assert
        await Assert.ThrowsAsync<IOException>(() => volume.Delete("dir1"));
        await Assert.ThrowsAsync<FileNotFoundException>(() => volume.Delete("missing"));
    }

    [Fact]
    public async Task When_WriteExistingFile_Then_FileIsReplaced()
    {
        // arrange
        var (volume, stream) = await CreateEmptyAsync();
        await WriteAsync(volume, "file.txt", CreateData(1000, 1));
        await WriteAsync(volume, "other.txt", CreateData(500, 2));

        // act
        await WriteAsync(volume, "FILE.TXT", CreateData(300, 0));

        // assert - the name as in the archive
        var readVolume = await ReopenAsync(stream);
        Assert.Equal(["file.txt", "other.txt"], await ListNamesAsync(readVolume));
        Assert.Equal(CreateData(300, 0), await ReadAsync(readVolume, "file.txt"));
        Assert.Equal(CreateData(500, 2), await ReadAsync(readVolume, "other.txt"));
    }

    [Fact]
    public async Task When_ChangeArchiveOfOtherApplication_Then_EntriesAndCommentAreKept()
    {
        // arrange
        var zip = new MemoryStream();
        using (var zipArchive = new ZipArchive(zip, ZipArchiveMode.Create, true))
        {
            zipArchive.Comment = "Archive comment";
            var zipArchiveEntry = zipArchive.CreateEntry("dir1/file1.txt");
            zipArchiveEntry.Comment = "Entry comment";
            await using var entryStream = await zipArchiveEntry.OpenAsync();
            await entryStream.WriteAsync(CreateData(5000, 1));
        }

        var (volume, stream) = await CreateAsync(zip.ToArray());

        // act
        await WriteAsync(volume, "file2.txt", CreateData(100, 2));
        await volume.Rename("dir1", "directory1");
        await volume.ChangeDirectory(["directory1"]);
        await volume.SetModifiedDate("file1.txt", Date);

        // assert
        using var readArchive = OpenZipArchive(stream);
        Assert.Equal("Archive comment", readArchive.Comment);
        Assert.Equal(["directory1/file1.txt", "file2.txt"], readArchive.Entries.Select(entry => entry.FullName));
        var readEntry = readArchive.GetEntry("directory1/file1.txt")!;
        Assert.Equal("Entry comment", readEntry.Comment);
        Assert.Equal(Date, readEntry.LastWriteTime.UtcDateTime);
        var readVolume = await ReopenAsync(stream);
        await readVolume.ChangeDirectory(["directory1"]);
        Assert.Equal(CreateData(5000, 1), await ReadAsync(readVolume, "file1.txt"));
    }

    [Fact]
    public async Task When_WriteFileWithNonAsciiName_Then_NameIsUtf8()
    {
        // arrange
        var (volume, stream) = await CreateEmptyAsync();

        // act
        await WriteAsync(volume, "Blåbærgrød.txt", CreateData(10, 1));

        // assert
        Assert.Equal(["Blåbærgrød.txt"], await ListNamesAsync(await ReopenAsync(stream)));
    }

    [Fact]
    public async Task When_SetModifiedDateOfDirectoryWithoutEntry_Then_DirectoryEntryIsAdded()
    {
        // arrange
        var (volume, stream) = await CreateAsync(ZipTestData.CreateZip(("dir/file.txt", "zip")));

        // act
        await volume.SetModifiedDate("dir", Date);

        // assert
        var entry = Assert.Single(await (await ReopenAsync(stream)).ListEntries().ToListAsync());
        Assert.Equal(EntryType.Container, entry.Type);
        Assert.Equal(Date, entry.ModifiedDate);
    }

    [Fact]
    public async Task When_ArchiveHasMoreThan65535Entries_Then_Zip64CentralDirectoryIsWritten()
    {
        // arrange
        const int count = 65536;
        var zip = new MemoryStream();
        using (var zipArchive = new ZipArchive(zip, ZipArchiveMode.Create, true))
        {
            for (var index = 0; index < count; index++)
            {
                zipArchive.CreateEntry($"{index}.txt");
            }
        }

        var (volume, stream) = await CreateAsync(zip.ToArray());

        // act
        await WriteAsync(volume, "new.txt", CreateData(100, 1));

        // assert
        using var readArchive = OpenZipArchive(stream);
        Assert.Equal(count + 1, readArchive.Entries.Count);
        Assert.Equal("new.txt", readArchive.Entries[^1].FullName);
    }

    [Fact]
    public async Task When_StreamIsReadOnly_Then_WritingThrowsNotSupportedException()
    {
        // arrange
        var volume = (ZipVolume)await new ZipFileVolumeProvider().CreateVolume(
            new MemoryStream(ZipTestData.CreateZipWithDirectoriesAndFiles(), false));

        // act and assert
        Assert.False(volume.IsWritable);
        await Assert.ThrowsAsync<NotSupportedException>(() => volume.OpenWrite("new.txt"));
        await Assert.ThrowsAsync<NotSupportedException>(() => volume.CreateContainer("dir"));
        await Assert.ThrowsAsync<NotSupportedException>(() => volume.Delete("file1.txt"));
    }

    [Fact]
    public async Task When_EmptyFileIsOpened_Then_ItIsAnEmptyWritableArchive()
    {
        // arrange
        var stream = new MemoryStream();
        var volume = await new ZipFileVolumeProvider().CreateVolume(stream);

        // act
        await WriteAsync(volume, "file.txt", CreateData(100, 1));

        // assert
        Assert.Equal(["file.txt"], await ListNamesAsync(await ReopenAsync(stream)));
    }

    [Fact]
    public async Task When_ZipArchiveIsCreatedAndChangedInNavigationContext_Then_ChangesAreWrittenToTheFile()
    {
        // arrange
        var rootPath = Directory.CreateDirectory(
            Path.Combine(Path.GetTempPath(), $"hst-commander-{Guid.NewGuid()}")).FullName;
        try
        {
            var archivePath = Path.Combine(rootPath, "archive.zip");
            await File.WriteAllTextAsync(Path.Combine(rootPath, "file1.txt"), "zip file");
            var startedFiles = new List<string>();

            using (var zipContext = new NavigationContext(rootPath, startedFiles.Add))
            using (var localContext = new NavigationContext(rootPath))
            {
                // act - create an empty archive with F9 and open it
                await zipContext.CreateVolumeAsync(new ZipVolumeCreator(), "archive",
                    new Dictionary<string, object?>());
                await zipContext.OpenEntryAsync(
                    (await zipContext.ListEntriesAsync()).First(entry => entry.Name == "archive.zip"));
                Assert.Null(zipContext.CreateDirectoryNotSupportedMessage);
                await zipContext.CreateDirectoryAsync("dir1");
                var localEntries = await localContext.ListEntriesAsync();
                await localContext.CopyAsync([localEntries.First(entry => entry.Name == "file1.txt")], zipContext,
                    zipContext.CurrentPath, "*");
                var entries = await zipContext.ListEntriesAsync();
                await zipContext.RenameAsync(entries.First(entry => entry.Name == "dir1"), "Dir2");

                // assert
                Assert.Empty(startedFiles);
                Assert.True(zipContext.IsInImage);
                var copiedEntry = (await zipContext.ListEntriesAsync()).First(entry => entry.Name == "file1.txt");
                Assert.Equal(8, copiedEntry.Size);
                Assert.Equal(File.GetLastWriteTimeUtc(Path.Combine(rootPath, "file1.txt")),
                    copiedEntry.ModifiedDate!.Value, TimeSpan.FromSeconds(2));
                Assert.Equal([Paths.Parent, "Dir2", "file1.txt"],
                    (await zipContext.ListEntriesAsync()).Select(entry => entry.Name));
            }

            await using var stream = File.OpenRead(archivePath);
            var volume = await new ZipFileVolumeProvider().CreateVolume(stream);
            Assert.Equal(["Dir2", "file1.txt"], await ListNamesAsync(volume));
            Assert.Equal("zip file"u8.ToArray(), await ReadAsync(volume, "file1.txt"));
        }
        finally
        {
            Directory.Delete(rootPath, true);
        }
    }
}
