using Hst.Commander.Abstractions;

namespace Hst.Commander.Core.Tests;

public class GivenNavigationContextCalculatingDirectorySize
{
    private readonly ManualResetEventSlim allowCounting = new(true);
    private int createdVolumes;

    private async Task<NavigationContext> CreateVolumeContextAsync()
    {
        // volumes after the pane's own are opened for counting, so they can be held back with allowCounting
        var volumeProvider = new TestVolumeProvider(volume =>
        {
            if (Interlocked.Increment(ref createdVolumes) > 1)
            {
                allowCounting.Wait(TimeSpan.FromSeconds(30));
            }

            volume.AddEntry([], new TestEntry("dir1", EntryType.Container));
            volume.AddEntry(["dir1"], new TestEntry("a.bin", EntryType.Item, 1024));
            volume.AddEntry(["dir1"], new TestEntry("dir2", EntryType.Container));
            volume.AddEntry(["dir1", "dir2"], new TestEntry("b.bin", EntryType.Item, 2048));
            volume.AddEntry([], new TestEntry("dir3", EntryType.Container));
            volume.AddEntry([], new TestEntry("file1.txt", EntryType.Item, 5));
        });
        var navigationContext = new NavigationContext(string.Empty);
        await navigationContext.ChangeVolumeProviderAsync(volumeProvider);
        return navigationContext;
    }

    private static IEntry GetEntry(IEnumerable<IEntry> entries, string name) =>
        entries.Single(entry => entry.Name == name);

    [Fact]
    public async Task When_CalculateDirectorySize_Then_SizeIsFormattedAndCountedInMarkedSize()
    {
        // arrange
        using var navigationContext = await CreateVolumeContextAsync();
        var entries = await navigationContext.ListEntriesAsync();
        var directory = GetEntry(entries, "dir1");
        navigationContext.ToggleMark(directory);
        navigationContext.ToggleMark(GetEntry(entries, "file1.txt"));

        // act
        var calculated = await navigationContext.CalculateDirectorySizeAsync(directory);

        // assert
        Assert.True(calculated);
        Assert.Equal("3K", navigationContext.FormatEntrySize(directory));
        Assert.Equal(3077, navigationContext.MarkedSize);
        Assert.Equal(SizeFormatter.DirectorySize, navigationContext.FormatEntrySize(GetEntry(entries, "dir3")));
    }

    [Fact]
    public async Task When_CalculatingDirectorySize_Then_SizeIsShownAsCalculating()
    {
        // arrange
        using var navigationContext = await CreateVolumeContextAsync();
        var entries = await navigationContext.ListEntriesAsync();
        var directory = GetEntry(entries, "dir1");
        allowCounting.Reset();

        // act
        var calculation = navigationContext.CalculateDirectorySizeAsync(directory);
        var calculatingSize = navigationContext.FormatEntrySize(directory);
        var calculatedAgain = await navigationContext.CalculateDirectorySizeAsync(directory);
        allowCounting.Set();
        var calculated = await calculation;

        // assert
        Assert.Equal(SizeFormatter.CalculatingSize, calculatingSize);
        Assert.False(calculatedAgain);
        Assert.True(calculated);
        Assert.Equal("3K", navigationContext.FormatEntrySize(directory));
    }

    [Fact]
    public async Task When_DirectoryIsUnmarked_Then_CalculatedSizeIsKept()
    {
        // arrange
        using var navigationContext = await CreateVolumeContextAsync();
        var entries = await navigationContext.ListEntriesAsync();
        var directory = GetEntry(entries, "dir1");
        navigationContext.ToggleMark(directory);
        await navigationContext.CalculateDirectorySizeAsync(directory);

        // act
        navigationContext.ToggleMark(directory);

        // assert
        Assert.False(navigationContext.IsMarked(directory));
        Assert.Equal(0, navigationContext.MarkedSize);
        Assert.Equal("3K", navigationContext.FormatEntrySize(directory));
    }

    [Fact]
    public async Task When_SameLocationIsListedAgain_Then_CalculatedSizesAreKept()
    {
        // arrange
        using var navigationContext = await CreateVolumeContextAsync();
        var entries = await navigationContext.ListEntriesAsync();
        await navigationContext.CalculateDirectorySizeAsync(GetEntry(entries, "dir1"));

        // act
        var listedEntries = await navigationContext.ListEntriesAsync();

        // assert
        Assert.Equal("3K", navigationContext.FormatEntrySize(GetEntry(listedEntries, "dir1")));
    }

    [Fact]
    public async Task When_LocationChanges_Then_CalculatedSizesAreCleared()
    {
        // arrange
        using var navigationContext = await CreateVolumeContextAsync();
        var entries = await navigationContext.ListEntriesAsync();
        var directory = GetEntry(entries, "dir1");
        await navigationContext.CalculateDirectorySizeAsync(directory);

        // act
        await navigationContext.OpenEntryAsync(directory);
        await navigationContext.GoParentAsync();
        var listedEntries = await navigationContext.ListEntriesAsync();

        // assert
        Assert.Equal(SizeFormatter.DirectorySize, navigationContext.FormatEntrySize(GetEntry(listedEntries, "dir1")));
    }

    [Fact]
    public async Task When_LocationChangesWhileCalculating_Then_SizeIsNotKept()
    {
        // arrange
        using var navigationContext = await CreateVolumeContextAsync();
        var entries = await navigationContext.ListEntriesAsync();
        var directory = GetEntry(entries, "dir1");
        allowCounting.Reset();

        // act
        var calculation = navigationContext.CalculateDirectorySizeAsync(directory);
        await navigationContext.OpenEntryAsync(directory);
        await navigationContext.GoParentAsync();
        allowCounting.Set();
        var calculated = await calculation;

        // assert
        Assert.False(calculated);
        Assert.Equal(SizeFormatter.DirectorySize, navigationContext.FormatEntrySize(directory));
    }

    [Fact]
    public async Task When_CalculateSizeOfFile_Then_NothingIsCalculated()
    {
        // arrange
        using var navigationContext = await CreateVolumeContextAsync();
        var entries = await navigationContext.ListEntriesAsync();
        var file = GetEntry(entries, "file1.txt");

        // act
        var calculated = await navigationContext.CalculateDirectorySizeAsync(file);

        // assert
        Assert.False(calculated);
        Assert.Equal("5B", navigationContext.FormatEntrySize(file));
    }
}
