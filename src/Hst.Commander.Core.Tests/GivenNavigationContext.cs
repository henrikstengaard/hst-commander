using Hst.Commander.Core.Volumes;
using Hst.Commander.Extensions.Volume.Abstractions;

namespace Hst.Commander.Core.Tests;

public class GivenNavigationContext
{
    [Fact]
    public void When_DiskSpaceInLocalDirectory_Then_FreeAndTotalSpaceAreReturned()
    {
        // arrange
        using var navigationContext = new NavigationContext(Directory.GetCurrentDirectory());

        // act
        var diskSpace = navigationContext.DiskSpace;

        // assert
        Assert.NotNull(diskSpace);
        Assert.Matches(@"^\d+[BKMGTPE]/\d+[BKMGTPE]$", diskSpace);
    }

    [Fact]
    public void When_DiskSpaceAtDriveList_Then_NoDiskSpaceIsReturned()
    {
        // arrange
        using var navigationContext = new NavigationContext(string.Empty);

        // act
        var diskSpace = navigationContext.DiskSpace;

        // assert
        Assert.Null(diskSpace);
    }

    [Fact]
    public async Task When_GoParent_Then_DirectoryLeftIsPreviousEntry()
    {
        // arrange
        var rootPath = Path.Combine(Path.GetTempPath(), $"hst-commander-{Guid.NewGuid()}");
        var dirPath = Path.Combine(rootPath, "dir1");
        Directory.CreateDirectory(dirPath);
        Directory.CreateDirectory(Path.Combine(rootPath, "a-dir"));
        try
        {
            using var navigationContext = new NavigationContext(dirPath);

            // act
            await navigationContext.GoParentAsync();
            var entries = await navigationContext.ListEntriesAsync();
            var index = navigationContext.IndexOfPreviousEntry(entries);

            // assert
            Assert.Equal("dir1", navigationContext.PreviousEntryName);
            Assert.True(index >= 0);
            Assert.Equal("dir1", entries[index].Name);
        }
        finally
        {
            Directory.Delete(rootPath, true);
        }
    }

    [Fact]
    public async Task When_OpenDirectory_Then_NoPreviousEntry()
    {
        // arrange
        var rootPath = Path.Combine(Path.GetTempPath(), $"hst-commander-{Guid.NewGuid()}");
        var dirPath = Path.Combine(rootPath, "dir1");
        Directory.CreateDirectory(Path.Combine(dirPath, "dir2"));
        try
        {
            using var navigationContext = new NavigationContext(dirPath);
            await navigationContext.GoParentAsync();

            // act
            await navigationContext.OpenEntryAsync(new Entry("dir1", EntryType.Container));
            var entries = await navigationContext.ListEntriesAsync();

            // assert
            Assert.Null(navigationContext.PreviousEntryName);
            Assert.Equal(-1, navigationContext.IndexOfPreviousEntry(entries));
        }
        finally
        {
            Directory.Delete(rootPath, true);
        }
    }

    [Fact]
    public async Task When_OpenDirectory_Then_FirstEntryIsSelected()
    {
        // arrange
        var rootPath = Path.Combine(Path.GetTempPath(), $"hst-commander-{Guid.NewGuid()}");
        Directory.CreateDirectory(Path.Combine(rootPath, "dir1", "dir2"));
        Directory.CreateDirectory(Path.Combine(rootPath, "dir1", "dir3"));
        try
        {
            using var navigationContext = new NavigationContext(rootPath);
            var previousPath = navigationContext.CurrentPath;

            // act
            await navigationContext.OpenEntryAsync(new Entry("dir1", EntryType.Container));
            var entries = await navigationContext.ListEntriesAsync();
            var selectedIndex = navigationContext.GetSelectedIndex(entries, previousPath, 2);

            // assert
            Assert.Equal(0, selectedIndex);
        }
        finally
        {
            Directory.Delete(rootPath, true);
        }
    }

    [Fact]
    public async Task When_GoParent_Then_DirectoryLeftIsSelected()
    {
        // arrange
        var rootPath = Path.Combine(Path.GetTempPath(), $"hst-commander-{Guid.NewGuid()}");
        var dirPath = Path.Combine(rootPath, "dir2");
        Directory.CreateDirectory(dirPath);
        Directory.CreateDirectory(Path.Combine(rootPath, "dir1"));
        try
        {
            using var navigationContext = new NavigationContext(dirPath);
            var previousPath = navigationContext.CurrentPath;

            // act
            await navigationContext.GoParentAsync();
            var entries = await navigationContext.ListEntriesAsync();
            var selectedIndex = navigationContext.GetSelectedIndex(entries, previousPath, 0);

            // assert
            Assert.Equal("dir2", entries[selectedIndex].Name);
        }
        finally
        {
            Directory.Delete(rootPath, true);
        }
    }

    [Fact]
    public async Task When_LocationIsUnchanged_Then_SelectedIndexIsKept()
    {
        // arrange
        var rootPath = Path.Combine(Path.GetTempPath(), $"hst-commander-{Guid.NewGuid()}");
        Directory.CreateDirectory(Path.Combine(rootPath, "dir1"));
        Directory.CreateDirectory(Path.Combine(rootPath, "dir2"));
        try
        {
            using var navigationContext = new NavigationContext(rootPath);
            var entries = await navigationContext.ListEntriesAsync();

            // act
            var selectedIndex = navigationContext.GetSelectedIndex(entries, navigationContext.CurrentPath, 2);

            // assert
            Assert.Equal(2, selectedIndex);
        }
        finally
        {
            Directory.Delete(rootPath, true);
        }
    }
}
