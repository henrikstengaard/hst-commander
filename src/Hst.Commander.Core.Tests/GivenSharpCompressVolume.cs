using Hst.Commander.Abstractions;
using Hst.Commander.Core.Volumes;
using Hst.Commander.Plugins.RarSevenZip;

namespace Hst.Commander.Core.Tests;

public class GivenSharpCompressVolume
{
    private static IFileVolumeProvider GetProvider(string fileName) =>
        Path.GetExtension(fileName) == ".7z" ? new SevenZipFileVolumeProvider() : new RarFileVolumeProvider();

    private static async Task<IVolume> CreateVolumeAsync(string fileName) =>
        await GetProvider(fileName).CreateVolume(new MemoryStream(ArchiveTestData.Read(fileName)));

    [Theory]
    [InlineData("dirs-files.rar")]
    [InlineData("dirs-files-rar4.rar")]
    [InlineData("dirs-files.7z")]
    public async Task When_IsMatch_Then_ArchiveIsDetected(string fileName)
    {
        // act
        var isMatch = await GetProvider(fileName).IsMatch(fileName, new MemoryStream(ArchiveTestData.Read(fileName)));

        // assert
        Assert.True(isMatch);
    }

    [Fact]
    public async Task When_IsMatchOtherArchive_Then_ArchiveIsNotDetected()
    {
        // act
        var isRarMatchOf7Z = await new RarFileVolumeProvider().IsMatch("archive.rar",
            new MemoryStream(ArchiveTestData.Read(ArchiveTestData.DirsFiles + ".7z")));
        var is7ZMatchOfRar = await new SevenZipFileVolumeProvider().IsMatch("archive.7z",
            new MemoryStream(ArchiveTestData.Read(ArchiveTestData.DirsFiles + ".rar")));
        var isRarMatchOfShortFile = await new RarFileVolumeProvider().IsMatch("short.rar",
            new MemoryStream("Rar!\x1a\x07\x01"u8.ToArray()));

        // assert
        Assert.False(isRarMatchOf7Z);
        Assert.False(is7ZMatchOfRar);
        Assert.False(isRarMatchOfShortFile);
    }

    [Theory]
    [InlineData("dirs-files.rar")]
    [InlineData("dirs-files-rar4.rar")]
    [InlineData("dirs-files.7z")]
    public async Task When_ListEntries_Then_DirectoriesAndFilesAreListed(string fileName)
    {
        // arrange
        using var volume = (SharpCompressVolume)await CreateVolumeAsync(fileName);

        // act
        var rootEntries = await volume.ListEntries().ToListAsync();
        await volume.ChangeDirectory(["dir1"]);
        var dirEntries = await volume.ListEntries().ToListAsync();

        // assert - dir1 only exists in the paths of entries
        Assert.Equal(["dir1", "dir2"], rootEntries.Select(entry => entry.Name));
        Assert.All(rootEntries, entry => Assert.Equal(EntryType.Container, entry.Type));
        Assert.Equal(["dir3", "file1.txt"], dirEntries.Select(entry => entry.Name));
        Assert.Equal([EntryType.Container, EntryType.Item], dirEntries.Select(entry => entry.Type));
        Assert.Equal(1, dirEntries[1].Size);
        Assert.Equal(DateTimeKind.Utc, dirEntries[1].ModifiedDate?.Kind);
        Assert.Equal(["dir1"], volume.CurrentPathComponents);
    }

    [Theory]
    [InlineData("dirs-files.rar")]
    [InlineData("dirs-files-rar4.rar")]
    [InlineData("dirs-files.7z")]
    public async Task When_OpenRead_Then_ContentIsRead(string fileName)
    {
        // arrange
        using var volume = (SharpCompressVolume)await CreateVolumeAsync(fileName);
        await volume.ChangeDirectory(["dir1"]);

        // act
        var data = new MemoryStream();
        await using (var stream = await volume.OpenRead("FILE1.TXT"))
        {
            await stream.CopyToAsync(data);
        }

        // assert - found case-insensitive
        Assert.Equal("a"u8.ToArray(), data.ToArray());
    }

    [Fact]
    public async Task When_ModifiedDateIsUtc_Then_ItIsKept()
    {
        // arrange - rar 5 archives store the modified date in utc
        using var volume = (SharpCompressVolume)await CreateVolumeAsync("dirs-files.rar");
        await volume.ChangeDirectory(["dir1"]);

        // act
        var entry = await volume.OpenEntry("file1.txt");

        // assert
        Assert.Equal(new DateTime(2024, 1, 2, 3, 4, 6, DateTimeKind.Utc), entry.ModifiedDate);
    }

    [Theory]
    [InlineData("dirs-files.rar")]
    [InlineData("dirs-files.7z")]
    public async Task When_Write_Then_NotSupportedExceptionIsThrown(string fileName)
    {
        // arrange
        using var volume = (SharpCompressVolume)await CreateVolumeAsync(fileName);

        // act & assert
        await Assert.ThrowsAsync<NotSupportedException>(() => volume.OpenWrite("new.txt"));
        await Assert.ThrowsAsync<NotSupportedException>(() => volume.CreateContainer("new"));
        await Assert.ThrowsAsync<NotSupportedException>(() => volume.Delete("dir2"));
    }

    [Fact]
    public async Task When_DisposeVolume_Then_StreamIsLeftOpen()
    {
        // arrange
        var stream = new MemoryStream(ArchiveTestData.Read("dirs-files.7z"));
        var volume = (SharpCompressVolume)await new SevenZipFileVolumeProvider().CreateVolume(stream);

        // act
        volume.Dispose();

        // assert
        Assert.True(stream.CanRead);
    }

    private static async Task<SharpCompressVolume> CreateSolidVolumeAsync() =>
        (SharpCompressVolume)await new SevenZipFileVolumeProvider().CreateVolume(
            new MemoryStream(ArchiveTestData.Read("solid.7z")));

    private static async Task<string> ReadTextAsync(IVolume volume, IReadOnlyList<string> directory, string name)
    {
        await volume.ChangeDirectory([Paths.Root, ..directory]);
        await using var stream = await volume.OpenRead(name);
        using var reader = new StreamReader(stream);
        return await reader.ReadToEndAsync();
    }

    [Fact]
    public async Task When_SolidArchiveFilesAreReadInOtherOrder_Then_FilesPassedNearbyAreReadFromCache()
    {
        // arrange - archive order dir1/a.txt, dir1/b.txt, dir1/sub/c.txt, other/d.txt in one solid block
        using var volume = await CreateSolidVolumeAsync();
        var solidArchiveReader = volume.SolidArchiveReader!;

        // act - c.txt passes a.txt and b.txt, cached as they are in dir1, the parent of c.txt's directory
        var c = await ReadTextAsync(volume, ["dir1", "sub"], "c.txt");
        var cacheSizeAfterC = solidArchiveReader.CacheSize;
        var b = await ReadTextAsync(volume, ["dir1"], "b.txt");
        var a = await ReadTextAsync(volume, ["dir1"], "a.txt");
        var d = await ReadTextAsync(volume, ["other"], "d.txt");
        var restartsBeforeA = solidArchiveReader.Restarts;
        var aAgain = await ReadTextAsync(volume, ["dir1"], "a.txt");

        // assert - one pass for all files, a second for a.txt read again
        Assert.Equal(["file a", "file b", "file c", "file d", "file a"], new[] { a, b, c, d, aAgain });
        Assert.Equal(12, cacheSizeAfterC);
        Assert.Equal(1, restartsBeforeA);
        Assert.Equal(2, solidArchiveReader.Restarts);
        Assert.Equal(0, solidArchiveReader.CacheSize);
    }

    [Fact]
    public async Task When_SolidArchiveFilesAreReadInOtherDirectory_Then_FilesPassedAreNotCached()
    {
        // arrange
        using var volume = await CreateSolidVolumeAsync();

        // act - d.txt in the top-level directory "other" passes the files of dir1
        var d = await ReadTextAsync(volume, ["other"], "d.txt");

        // assert
        Assert.Equal("file d", d);
        Assert.Equal(0, volume.SolidArchiveReader!.CacheSize);
    }

    [Fact]
    public async Task When_SolidArchiveFileIsOpenWhileOtherFileIsOpened_Then_BothAreRead()
    {
        // arrange
        using var volume = await CreateSolidVolumeAsync();
        await volume.ChangeDirectory(["dir1"]);

        // act - the reader moves on to b.txt while a.txt hasn't been read
        await using var aStream = await volume.OpenRead("a.txt");
        await using var bStream = await volume.OpenRead("b.txt");
        using var bReader = new StreamReader(bStream);
        var b = await bReader.ReadToEndAsync();
        using var aReader = new StreamReader(aStream);
        var a = await aReader.ReadToEndAsync();

        // assert
        Assert.Equal("file a", a);
        Assert.Equal("file b", b);
    }

    [Fact]
    public async Task When_DeflateArchiveIsRead_Then_FilesAreReadInFull()
    {
        // arrange - deflate.7z is a solid deflate block: dir1/big1.txt to big3.txt are the numbers 1 to 3000 on lines,
        // which SharpCompress 0.50.5 read only partly
        using var volume = (SharpCompressVolume)await new SevenZipFileVolumeProvider().CreateVolume(
            new MemoryStream(ArchiveTestData.Read("deflate.7z")));
        var expected = string.Concat(Enumerable.Range(1, 3000).Select(number => $"{number}\n"));

        // act
        var big = new List<string>();
        foreach (var name in new[] { "big3.txt", "big1.txt", "big2.txt" })
        {
            big.Add(await ReadTextAsync(volume, ["dir1"], name));
        }

        // assert
        Assert.All(big, text => Assert.Equal(expected, text));
    }

    [Fact]
    public async Task When_SolidArchiveIsCopied_Then_FilesAreReadInArchiveOrderInOnePass()
    {
        // arrange - copying lists directories before files, so dir1/sub/c.txt would come before dir1/a.txt
        using var volume = await CreateSolidVolumeAsync();
        var destinationPath = Path.Combine(Path.GetTempPath(), $"hst-commander-{Guid.NewGuid()}");
        Directory.CreateDirectory(destinationPath);
        try
        {
            var entries = await volume.ListEntries().ToListAsync();

            // act
            await volume.CopyToAsync(entries, new LocalVolume(destinationPath));

            // assert - read in the archive's order: no file is passed, cached or decompressed twice
            Assert.Equal(1, volume.SolidArchiveReader!.Restarts);
            Assert.Equal(0, volume.SolidArchiveReader.CacheSize);
            Assert.Equal("file c", await File.ReadAllTextAsync(Path.Combine(destinationPath, "dir1", "sub", "c.txt")));
            Assert.Equal("file d", await File.ReadAllTextAsync(Path.Combine(destinationPath, "other", "d.txt")));
        }
        finally
        {
            Directory.Delete(destinationPath, true);
        }
    }

    [Fact]
    public async Task When_GetReadOrder_Then_SolidArchivesReturnTheArchiveOrderAndOthersNull()
    {
        // arrange
        using var solidVolume = await CreateSolidVolumeAsync();
        using var otherVolume = (SharpCompressVolume)await CreateVolumeAsync("dirs-files.rar");
        await solidVolume.ChangeDirectory(["dir1"]);
        await otherVolume.ChangeDirectory(["dir1"]);

        // act & assert
        Assert.True(solidVolume.GetReadOrder("a.txt") < solidVolume.GetReadOrder("b.txt"));
        Assert.Null(solidVolume.GetReadOrder("sub"));
        Assert.Null(otherVolume.GetReadOrder("file1.txt"));
    }

    [Fact]
    public async Task When_DamagedArchiveIsRead_Then_InvalidDataExceptionIsThrown()
    {
        // arrange - a byte of the compressed data of the solid block (after the 32 byte header) is changed
        var data = ArchiveTestData.Read("solid.7z");
        data[40] ^= 0xFF;
        using var volume = (SharpCompressVolume)await new SevenZipFileVolumeProvider().CreateVolume(
            new MemoryStream(data));

        // act - the files are decompressed with errors or with the wrong content and CRC
        var exception = await Record.ExceptionAsync(async () =>
        {
            foreach (var (directory, name) in new[]
                     { ("dir1", "a.txt"), ("dir1", "b.txt"), ("dir1/sub", "c.txt"), ("other", "d.txt") })
            {
                await ReadTextAsync(volume, directory.Split('/'), name);
            }
        });

        // assert
        Assert.IsType<InvalidDataException>(exception);
        Assert.Contains("damaged", exception.Message);
    }

    [Fact]
    public async Task When_NotSolidArchive_Then_NoSolidArchiveReaderIsUsed()
    {
        // arrange - dirs-files.rar has stored entries without a solid block
        using var volume = (SharpCompressVolume)await CreateVolumeAsync("dirs-files.rar");

        // act & assert
        Assert.Null(volume.SolidArchiveReader);
    }
}
