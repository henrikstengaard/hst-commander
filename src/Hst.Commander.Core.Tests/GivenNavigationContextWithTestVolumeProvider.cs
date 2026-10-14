using Hst.Commander.Extensions.Volume.Abstractions;

namespace Hst.Commander.Core.Tests;

public class GivenNavigationContextWithTestVolumeProvider
{
    private readonly TestVolumeProvider volumeProvider = new(volume =>
    {
        volume.AddEntry([], new TestEntry("dir1", EntryType.Container));
        volume.AddEntry([], new TestEntry("file1.txt", EntryType.Item, 5, data: "file1"u8.ToArray()));
        volume.AddEntry(["dir1"], new TestEntry("file2.txt", EntryType.Item, 5, data: "file2"u8.ToArray()));
    });

    [Fact]
    public async Task When_ChangeVolumeProvider_Then_EntriesAreListedFromRootOfNewVolume()
    {
        // arrange
        var rootPath = CreateTempDirectory();
        try
        {
            using var navigationContext = new NavigationContext(rootPath);
            var localEntry = new TestEntry("local.txt", EntryType.Item);
            navigationContext.ToggleMark(localEntry);

            // act
            await navigationContext.ChangeVolumeProviderAsync(volumeProvider);
            var entries = await navigationContext.ListEntriesAsync();

            // assert
            Assert.Same(volumeProvider, navigationContext.VolumeProvider);
            Assert.Equal("Test", navigationContext.CurrentPath);
            Assert.False(navigationContext.HasParent);
            Assert.Null(navigationContext.DiskSpace);
            Assert.Empty(navigationContext.MarkedEntries);
            Assert.Equal(["dir1", "file1.txt"], entries.Select(entry => entry.Name));
        }
        finally
        {
            Directory.Delete(rootPath, true);
        }
    }

    [Fact]
    public async Task When_ChangeToLocalVolumeProvider_Then_DrivesAreListed()
    {
        // arrange
        using var navigationContext = new NavigationContext(string.Empty);
        await navigationContext.ChangeVolumeProviderAsync(volumeProvider);

        // act
        await navigationContext.ChangeVolumeProviderAsync(VolumeProviders.Local);
        var entries = await navigationContext.ListEntriesAsync();

        // assert
        Assert.Equal("Computer", navigationContext.CurrentPath);
        Assert.Equal(DriveInfo.GetDrives().Select(drive => drive.Name), entries.Select(entry => entry.Name));
    }

    [Fact]
    public async Task When_OpenLocation_Then_NewVolumeIsAtCurrentDirectory()
    {
        // arrange
        using var navigationContext = new NavigationContext(string.Empty);
        await navigationContext.ChangeVolumeProviderAsync(volumeProvider);
        var dirEntry = (await navigationContext.ListEntriesAsync()).Single(entry => entry.Name == "dir1");
        await navigationContext.OpenEntryAsync(dirEntry);

        // act
        using var location = await navigationContext.OpenLocationAsync();
        var entries = await location.Volume.ListEntries().ToListAsync();

        // assert
        Assert.Equal("dir1", location.Path);
        Assert.Equal(["file2.txt"], entries.Select(entry => entry.Name));
    }

    [Fact]
    public async Task When_GoParentAtRoot_Then_RootIsListed()
    {
        // arrange
        using var navigationContext = new NavigationContext(string.Empty);
        await navigationContext.ChangeVolumeProviderAsync(volumeProvider);
        var dirEntry = (await navigationContext.ListEntriesAsync()).Single(entry => entry.Name == "dir1");
        await navigationContext.OpenEntryAsync(dirEntry);

        // act
        var dirEntries = await navigationContext.ListEntriesAsync();
        await navigationContext.GoParentAsync();
        var rootEntries = await navigationContext.ListEntriesAsync();

        // assert
        Assert.Equal([Paths.Parent, "file2.txt"], dirEntries.Select(entry => entry.Name));
        Assert.Equal("dir1", navigationContext.PreviousEntryName);
        Assert.Equal(["dir1", "file1.txt"], rootEntries.Select(entry => entry.Name));
    }

    [Fact]
    public async Task When_CopyFromVolumeToLocalDirectory_Then_ItemIsCopied()
    {
        // arrange
        var rootPath = CreateTempDirectory();
        try
        {
            using var navigationContext = new NavigationContext(string.Empty);
            await navigationContext.ChangeVolumeProviderAsync(volumeProvider);
            using var destinationContext = new NavigationContext(rootPath);
            var entry = (await navigationContext.ListEntriesAsync()).Single(entry => entry.Name == "file1.txt");

            // act
            await navigationContext.CopyAsync([entry], destinationContext, destinationContext.CurrentPath, "*");

            // assert
            Assert.Equal("file1", await File.ReadAllTextAsync(Path.Combine(rootPath, "file1.txt")));
        }
        finally
        {
            Directory.Delete(rootPath, true);
        }
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), $"hst-commander-{Guid.NewGuid()}");
        Directory.CreateDirectory(path);
        return path;
    }
}
