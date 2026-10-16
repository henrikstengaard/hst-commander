using System.IO.Compression;
using Hst.Commander.Abstractions;
using Hst.Commander.Plugins.Zip;

namespace Hst.Commander.Core.Tests;

public class GivenZipVolume
{
    private static async Task<IVolume> CreateZipVolumeAsync(byte[] zipData) =>
        await new ZipFileVolumeProvider().CreateVolume(new MemoryStream(zipData));

    [Fact]
    public async Task When_ListEntriesInRoot_Then_DirectoriesAndFilesAreListed()
    {
        // arrange - dir1 only exists in the path of a file, dir2 as a directory entry
        var zipVolume = await CreateZipVolumeAsync(ZipTestData.CreateZipWithDirectoriesAndFiles());

        // act
        var entries = await zipVolume.ListEntries().ToListAsync();

        // assert - directories first
        Assert.Equal(["dir1", "dir2", "file1.txt"], entries.Select(entry => entry.Name));
        Assert.Equal([EntryType.Container, EntryType.Container, EntryType.Item], entries.Select(entry => entry.Type));
        Assert.Equal(9, entries[2].Size);
    }

    [Fact]
    public async Task When_ChangeDirectory_Then_EntriesInDirectoryAreListed()
    {
        // arrange
        var zipVolume = await CreateZipVolumeAsync(ZipTestData.CreateZipWithDirectoriesAndFiles());

        // act
        await zipVolume.ChangeDirectory(["dir1"]);
        var entries = await zipVolume.ListEntries().ToListAsync();

        // assert
        Assert.Equal(["dir1"], zipVolume.CurrentPathComponents);
        Assert.Equal(["file2.txt"], entries.Select(entry => entry.Name));
    }

    [Fact]
    public async Task When_ChangeDirectoryToParentAndRoot_Then_CurrentDirectoryIsChanged()
    {
        // arrange
        var zipVolume = await CreateZipVolumeAsync(ZipTestData.CreateZip(("dir1/dir2/file1.txt", "file1")));
        await zipVolume.ChangeDirectory(["dir1", "dir2"]);

        // act
        await zipVolume.ChangeDirectory([Paths.Parent]);
        var parentPathComponents = zipVolume.CurrentPathComponents.ToList();
        await zipVolume.ChangeDirectory([Paths.Root]);

        // assert
        Assert.Equal(["dir1"], parentPathComponents);
        Assert.Empty(zipVolume.CurrentPathComponents);
    }

    [Fact]
    public async Task When_ChangeDirectoryToMissingDirectory_Then_IOExceptionIsThrownAndDirectoryIsUnchanged()
    {
        // arrange
        var zipVolume = await CreateZipVolumeAsync(ZipTestData.CreateZipWithDirectoriesAndFiles());

        // act & assert - a file is not a directory
        await Assert.ThrowsAnyAsync<IOException>(() => zipVolume.ChangeDirectory(["dir1", "missing"]));
        await Assert.ThrowsAnyAsync<IOException>(() => zipVolume.ChangeDirectory(["file1.txt"]));
        Assert.Empty(zipVolume.CurrentPathComponents);
    }

    [Fact]
    public async Task When_OpenRead_Then_ContentIsRead()
    {
        // arrange
        var zipVolume = await CreateZipVolumeAsync(ZipTestData.CreateZipWithDirectoriesAndFiles());
        await zipVolume.ChangeDirectory(["dir1"]);

        // act
        await using var stream = await zipVolume.OpenRead("file2.txt");
        using var reader = new StreamReader(stream);

        // assert
        Assert.Equal("zip-file2", await reader.ReadToEndAsync());
    }

    [Fact]
    public async Task When_EntryPathHasParentComponents_Then_EntryIsKeptInsideArchive()
    {
        // arrange
        var zipVolume = await CreateZipVolumeAsync(ZipTestData.CreateZip(("../../outside.txt", "outside")));

        // act
        var entries = await zipVolume.ListEntries().ToListAsync();

        // assert
        Assert.Equal(["outside.txt"], entries.Select(entry => entry.Name));
    }

    [Fact]
    public async Task When_Write_Then_NotSupportedExceptionIsThrown()
    {
        // arrange
        var zipVolume = await CreateZipVolumeAsync(ZipTestData.CreateZipWithDirectoriesAndFiles());

        // act & assert - zip archives are read-only
        await Assert.ThrowsAsync<NotSupportedException>(() => zipVolume.OpenWrite("new.txt"));
        await Assert.ThrowsAsync<NotSupportedException>(() => zipVolume.CreateContainer("new"));
        await Assert.ThrowsAsync<NotSupportedException>(() => zipVolume.Delete("file1.txt"));
        await Assert.ThrowsAsync<NotSupportedException>(() => zipVolume.Rename("file1.txt", "new.txt"));
    }

    [Fact]
    public async Task When_IsMatch_Then_ZipArchivesAreDetectedBySignature()
    {
        // arrange
        var provider = new ZipFileVolumeProvider();
        byte[] emptyZip;
        using (var stream = new MemoryStream())
        {
            new ZipArchive(stream, ZipArchiveMode.Create, true).Dispose();
            emptyZip = stream.ToArray();
        }

        // act & assert - the name doesn't matter, only the signature
        Assert.True(await provider.IsMatch("archive.dat", new MemoryStream(ZipTestData.CreateZipWithDirectoriesAndFiles())));
        Assert.True(await provider.IsMatch("empty.zip", new MemoryStream(emptyZip)));
        Assert.False(await provider.IsMatch("text.zip", new MemoryStream("not a zip"u8.ToArray())));
        Assert.False(await provider.IsMatch("short.zip", new MemoryStream("PK"u8.ToArray())));
    }

    [Fact]
    public void When_PluginsAreLoaded_Then_ZipFileVolumeProviderIsRegistered()
    {
        Assert.Contains(VolumeProviders.Files, provider => provider is ZipFileVolumeProvider);
        Assert.Same(VolumeProviders.Iso9660, VolumeProviders.Files[0]);
    }
}
