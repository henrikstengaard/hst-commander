using Hst.Commander.Abstractions;
using Hst.Commander.Plugins.LhaLzx;

namespace Hst.Commander.Core.Tests;

public class GivenLzxVolume
{
    private static async Task<IVolume> CreateLzxVolumeAsync() =>
        await new LzxFileVolumeProvider().CreateVolume(
            new MemoryStream(ArchiveTestData.Read(ArchiveTestData.DirsFiles + ".lzx")));

    [Fact]
    public async Task When_IsMatch_Then_LzxArchiveIsDetected()
    {
        // arrange
        var lzxFileVolumeProvider = new LzxFileVolumeProvider();

        // act
        var isLzxMatch = await lzxFileVolumeProvider.IsMatch("archive.lzx",
            new MemoryStream(ArchiveTestData.Read(ArchiveTestData.DirsFiles + ".lzx")));
        var isLhaMatch = await lzxFileVolumeProvider.IsMatch("archive.lzx",
            new MemoryStream(ArchiveTestData.Read(ArchiveTestData.DirsFiles + ".lha")));

        // assert
        Assert.True(isLzxMatch);
        Assert.False(isLhaMatch);
    }

    [Fact]
    public async Task When_ListEntries_Then_DirectoriesAndFilesAreListed()
    {
        // arrange
        var lzxVolume = await CreateLzxVolumeAsync();

        // act
        var rootEntries = await lzxVolume.ListEntries().ToListAsync();
        await lzxVolume.ChangeDirectory(["dir1"]);
        var dirEntries = await lzxVolume.ListEntries().ToListAsync();

        // assert
        Assert.Equal(["dir1", "dir2"], rootEntries.Select(entry => entry.Name));
        Assert.All(rootEntries, entry => Assert.Equal(EntryType.Container, entry.Type));
        Assert.Equal(["dir3", "file1.txt"], dirEntries.Select(entry => entry.Name));
        Assert.Equal(1, dirEntries[1].Size);
        Assert.Equal(DateTimeKind.Utc, dirEntries[1].ModifiedDate?.Kind);
    }

    [Fact]
    public async Task When_OpenRead_Then_ContentIsRead()
    {
        // arrange
        var lzxVolume = await CreateLzxVolumeAsync();
        await lzxVolume.ChangeDirectory(["dir1"]);

        // act
        var data = new MemoryStream();
        await using (var stream = await lzxVolume.OpenRead("file1.txt"))
        {
            await stream.CopyToAsync(data);
        }

        // assert - the entry stream verifies the crc when read to the end
        Assert.Equal(1, data.Length);
    }

    [Fact]
    public async Task When_Write_Then_NotSupportedExceptionIsThrown()
    {
        // arrange
        var lzxVolume = await CreateLzxVolumeAsync();

        // act & assert
        await Assert.ThrowsAsync<NotSupportedException>(() => lzxVolume.OpenWrite("new.txt"));
        await Assert.ThrowsAsync<NotSupportedException>(() => lzxVolume.CreateContainer("new"));
    }
}
