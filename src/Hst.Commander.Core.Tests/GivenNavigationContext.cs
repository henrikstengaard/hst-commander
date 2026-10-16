using DiscUtils.Iso9660;
using Hst.Commander.Core.Volumes;
using Hst.Commander.Abstractions;

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
    public async Task When_GoRoot_Then_DriveRootIsListedAndFirstEntryIsSelected()
    {
        // arrange
        var rootPath = Path.Combine(Path.GetTempPath(), $"hst-commander-{Guid.NewGuid()}");
        var dirPath = Path.Combine(rootPath, "dir1", "dir2");
        Directory.CreateDirectory(dirPath);
        var driveRoot = Path.GetPathRoot(Path.GetFullPath(rootPath))!;
        try
        {
            using var navigationContext = new NavigationContext(dirPath);
            var previousPath = navigationContext.CurrentPath;

            // act
            await navigationContext.GoRootAsync();
            var entries = await navigationContext.ListEntriesAsync();
            var selectedIndex = navigationContext.GetSelectedIndex(entries, previousPath, 5);

            // assert
            Assert.Equal(driveRoot, navigationContext.CurrentPath);
            Assert.Null(navigationContext.PreviousEntryName);
            Assert.Equal(0, selectedIndex);
        }
        finally
        {
            Directory.Delete(rootPath, true);
        }
    }

    [Fact]
    public async Task When_GoRootAtDriveList_Then_DriveListIsKeptAndFirstEntryIsSelected()
    {
        // arrange
        using var navigationContext = new NavigationContext(string.Empty);
        var previousPath = navigationContext.CurrentPath;
        var entries = await navigationContext.ListEntriesAsync();

        // act
        await navigationContext.GoRootAsync();
        var selectedIndex = navigationContext.GetSelectedIndex(entries, previousPath, entries.Count - 1);
        var nextSelectedIndex = navigationContext.GetSelectedIndex(entries, previousPath, entries.Count - 1);

        // assert - the first entry is selected once
        Assert.Equal("Computer", navigationContext.CurrentPath);
        Assert.Equal(0, selectedIndex);
        Assert.Equal(entries.Count - 1, nextSelectedIndex);
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

    [Fact]
    public async Task When_ToggleMark_Then_SummaryContainsSizeAndCount()
    {
        // arrange
        var rootPath = Path.Combine(Path.GetTempPath(), $"hst-commander-{Guid.NewGuid()}");
        Directory.CreateDirectory(Path.Combine(rootPath, "dir1"));
        File.WriteAllBytes(Path.Combine(rootPath, "file1.bin"), new byte[1024]);
        File.WriteAllBytes(Path.Combine(rootPath, "file2.bin"), new byte[1024]);
        try
        {
            using var navigationContext = new NavigationContext(rootPath);
            var entries = await navigationContext.ListEntriesAsync();

            // act
            foreach (var entry in entries)
            {
                navigationContext.ToggleMark(entry);
            }

            // assert
            Assert.False(navigationContext.IsMarked(entries.Single(entry => entry.Name == Paths.Parent)));
            Assert.Equal(3, navigationContext.MarkedEntries.Count);
            Assert.Equal(2048, navigationContext.MarkedSize);
            Assert.Equal("2K in 3 selected", navigationContext.MarkedSummary);
        }
        finally
        {
            Directory.Delete(rootPath, true);
        }
    }

    [Fact]
    public async Task When_ToggleMarkTwice_Then_EntryIsNotMarked()
    {
        // arrange
        var rootPath = Path.Combine(Path.GetTempPath(), $"hst-commander-{Guid.NewGuid()}");
        Directory.CreateDirectory(Path.Combine(rootPath, "dir1"));
        try
        {
            using var navigationContext = new NavigationContext(rootPath);
            var entry = (await navigationContext.ListEntriesAsync()).Single(entry => entry.Name == "dir1");

            // act
            var firstToggle = navigationContext.ToggleMark(entry);
            var secondToggle = navigationContext.ToggleMark(entry);

            // assert
            Assert.True(firstToggle);
            Assert.False(secondToggle);
            Assert.False(navigationContext.IsMarked(entry));
            Assert.Null(navigationContext.MarkedSummary);
        }
        finally
        {
            Directory.Delete(rootPath, true);
        }
    }

    [Fact]
    public async Task When_LocationChanges_Then_MarksAreCleared()
    {
        // arrange
        var rootPath = Path.Combine(Path.GetTempPath(), $"hst-commander-{Guid.NewGuid()}");
        Directory.CreateDirectory(Path.Combine(rootPath, "dir1"));
        try
        {
            using var navigationContext = new NavigationContext(rootPath);
            var entry = (await navigationContext.ListEntriesAsync()).Single(entry => entry.Name == "dir1");
            navigationContext.ToggleMark(entry);

            // act
            await navigationContext.OpenEntryAsync(entry);

            // assert
            Assert.Empty(navigationContext.MarkedEntries);
        }
        finally
        {
            Directory.Delete(rootPath, true);
        }
    }

    [Fact]
    public async Task When_ListEntriesAtSameLocation_Then_MarksOfRemovedEntriesAreDropped()
    {
        // arrange
        var rootPath = Path.Combine(Path.GetTempPath(), $"hst-commander-{Guid.NewGuid()}");
        Directory.CreateDirectory(Path.Combine(rootPath, "dir1"));
        Directory.CreateDirectory(Path.Combine(rootPath, "dir2"));
        try
        {
            using var navigationContext = new NavigationContext(rootPath);
            foreach (var entry in await navigationContext.ListEntriesAsync())
            {
                navigationContext.ToggleMark(entry);
            }

            // act
            Directory.Delete(Path.Combine(rootPath, "dir2"));
            await navigationContext.ListEntriesAsync();

            // assert
            Assert.Equal(["dir1"], navigationContext.MarkedEntries.Select(entry => entry.Name));
        }
        finally
        {
            Directory.Delete(rootPath, true);
        }
    }

    [Fact]
    public async Task When_CopyMarkedEntriesToOtherContext_Then_EntriesAreCopied()
    {
        // arrange - source with a file and a directory, destination empty
        var rootPath = CreateTempDirectory();
        var sourcePath = Path.Combine(rootPath, "source");
        var destinationPath = Path.Combine(rootPath, "destination");
        Directory.CreateDirectory(Path.Combine(sourcePath, "dir1"));
        Directory.CreateDirectory(destinationPath);
        await File.WriteAllTextAsync(Path.Combine(sourcePath, "file1.txt"), "file1");
        await File.WriteAllTextAsync(Path.Combine(sourcePath, "dir1", "file2.txt"), "file2");
        try
        {
            using var source = new NavigationContext(sourcePath);
            using var destination = new NavigationContext(destinationPath);
            foreach (var entry in await source.ListEntriesAsync())
            {
                source.ToggleMark(entry);
            }

            var entries = source.GetOperationEntries(null);

            // act
            await source.CopyAsync(entries, destination, destination.CurrentPath, EntryFilter.All);

            // assert
            Assert.Equal(2, entries.Count);
            Assert.Equal("file1", await File.ReadAllTextAsync(Path.Combine(destinationPath, "file1.txt")));
            Assert.Equal("file2", await File.ReadAllTextAsync(Path.Combine(destinationPath, "dir1", "file2.txt")));
        }
        finally
        {
            Directory.Delete(rootPath, true);
        }
    }

    [Fact]
    public async Task When_CopySingleFileToNewFilePath_Then_FileIsCopiedWithNewName()
    {
        // arrange
        var rootPath = CreateTempDirectory();
        await File.WriteAllTextAsync(Path.Combine(rootPath, "file1.txt"), "file1");
        try
        {
            using var source = new NavigationContext(rootPath);
            var entries = source.GetOperationEntries(
                (await source.ListEntriesAsync()).First(entry => entry.Name == "file1.txt"));

            // act - copy with a relative destination path
            await source.CopyAsync(entries, source, "copy.txt", EntryFilter.All);

            // assert
            Assert.Equal("file1", await File.ReadAllTextAsync(Path.Combine(rootPath, "copy.txt")));
        }
        finally
        {
            Directory.Delete(rootPath, true);
        }
    }

    [Fact]
    public async Task When_CopyToMissingDirectory_Then_DirectoryIsCreated()
    {
        // arrange
        var rootPath = CreateTempDirectory();
        await File.WriteAllTextAsync(Path.Combine(rootPath, "file1.txt"), "file1");
        await File.WriteAllTextAsync(Path.Combine(rootPath, "file2.txt"), "file2");
        try
        {
            using var source = new NavigationContext(rootPath);
            var entries = (await source.ListEntriesAsync()).Where(entry => entry.Name != Paths.Parent).ToList();
            var destinationPath = Path.Combine(rootPath, "new-dir");

            // act
            await source.CopyAsync(entries, source, destinationPath, EntryFilter.All);

            // assert
            Assert.True(File.Exists(Path.Combine(destinationPath, "file1.txt")));
            Assert.True(File.Exists(Path.Combine(destinationPath, "file2.txt")));
        }
        finally
        {
            Directory.Delete(rootPath, true);
        }
    }

    [Fact]
    public async Task When_CopyOntoItself_Then_IOExceptionIsThrown()
    {
        // arrange
        var rootPath = CreateTempDirectory();
        await File.WriteAllTextAsync(Path.Combine(rootPath, "file1.txt"), "file1");
        try
        {
            using var source = new NavigationContext(rootPath);
            var entries = source.GetOperationEntries(
                (await source.ListEntriesAsync()).First(entry => entry.Name == "file1.txt"));

            // act & assert - the file is left unchanged
            await Assert.ThrowsAsync<IOException>(() =>
                source.CopyAsync(entries, source, source.CurrentPath, EntryFilter.All));
            Assert.Equal("file1", await File.ReadAllTextAsync(Path.Combine(rootPath, "file1.txt")));
        }
        finally
        {
            Directory.Delete(rootPath, true);
        }
    }

    [Fact]
    public async Task When_CopyDirectoryIntoItself_Then_IOExceptionIsThrown()
    {
        // arrange
        var rootPath = CreateTempDirectory();
        Directory.CreateDirectory(Path.Combine(rootPath, "dir1", "sub"));
        try
        {
            using var source = new NavigationContext(rootPath);
            var entries = source.GetOperationEntries(
                (await source.ListEntriesAsync()).First(entry => entry.Name == "dir1"));

            // act & assert
            await Assert.ThrowsAsync<IOException>(() =>
                source.CopyAsync(entries, source, Path.Combine(rootPath, "dir1", "sub"), EntryFilter.All));
        }
        finally
        {
            Directory.Delete(rootPath, true);
        }
    }

    [Fact]
    public async Task When_CopyFromIsoImage_Then_FilesAreCopiedToLocalDirectory()
    {
        // arrange - create an iso image with a file in a directory
        var rootPath = CreateTempDirectory();
        var destinationPath = Path.Combine(rootPath, "destination");
        Directory.CreateDirectory(destinationPath);
        var builder = new CDBuilder { UseJoliet = true };
        builder.AddDirectory("dir1");
        builder.AddFile(@"dir1\file1.txt", "iso-file1"u8.ToArray());
        builder.Build(Path.Combine(rootPath, "image.iso"));
        try
        {
            using var source = new NavigationContext(rootPath);
            using var destination = new NavigationContext(destinationPath);
            await source.OpenEntryAsync((await source.ListEntriesAsync()).First(entry => entry.Name == "image.iso"));
            await source.OpenEntryAsync((await source.ListEntriesAsync()).First(entry => entry.Name == "dir1"));
            var entries = source.GetOperationEntries(
                (await source.ListEntriesAsync()).First(entry => entry.Name == "file1.txt"));

            // act
            await source.CopyAsync(entries, destination, destination.CurrentPath, EntryFilter.All);

            // assert
            Assert.Equal("iso-file1", await File.ReadAllTextAsync(Path.Combine(destinationPath, "file1.txt")));
        }
        finally
        {
            Directory.Delete(rootPath, true);
        }
    }

    [Fact]
    public async Task When_GetOperationEntriesWithoutMarks_Then_CurrentEntryIsReturnedExceptParent()
    {
        // arrange
        var rootPath = CreateTempDirectory();
        Directory.CreateDirectory(Path.Combine(rootPath, "dir1"));
        try
        {
            using var navigationContext = new NavigationContext(rootPath);
            var entries = await navigationContext.ListEntriesAsync();

            // act
            var parentEntries = navigationContext.GetOperationEntries(entries[0]);
            var dirEntries = navigationContext.GetOperationEntries(entries[1]);

            // assert
            Assert.Equal(Paths.Parent, entries[0].Name);
            Assert.Empty(parentEntries);
            Assert.Equal(["dir1"], dirEntries.Select(entry => entry.Name));
        }
        finally
        {
            Directory.Delete(rootPath, true);
        }
    }

    [Fact]
    public async Task When_DeleteMarkedEntries_Then_EntriesAreDeleted()
    {
        // arrange - a file, a directory with a file and a file to keep
        var rootPath = CreateTempDirectory();
        Directory.CreateDirectory(Path.Combine(rootPath, "dir1"));
        await File.WriteAllTextAsync(Path.Combine(rootPath, "dir1", "file2.txt"), "file2");
        await File.WriteAllTextAsync(Path.Combine(rootPath, "file1.txt"), "file1");
        await File.WriteAllTextAsync(Path.Combine(rootPath, "keep.txt"), "keep");
        try
        {
            using var navigationContext = new NavigationContext(rootPath);
            foreach (var entry in (await navigationContext.ListEntriesAsync())
                     .Where(entry => entry.Name is "dir1" or "file1.txt"))
            {
                navigationContext.ToggleMark(entry);
            }

            var entries = navigationContext.GetOperationEntries(null);

            // act
            await navigationContext.DeleteAsync(entries);

            // assert
            Assert.Equal("Delete 2 entries?", NavigationContext.GetDeleteDescription(entries));
            Assert.False(Directory.Exists(Path.Combine(rootPath, "dir1")));
            Assert.False(File.Exists(Path.Combine(rootPath, "file1.txt")));
            Assert.True(File.Exists(Path.Combine(rootPath, "keep.txt")));
        }
        finally
        {
            Directory.Delete(rootPath, true);
        }
    }

    [Fact]
    public async Task When_ListEntries_Then_ExecutableFilesAreDetected()
    {
        // arrange
        var rootPath = CreateTempDirectory();
        var toolPath = Path.Combine(rootPath, OperatingSystem.IsWindows() ? "tool.exe" : "tool");
        await File.WriteAllTextAsync(toolPath, "tool");
        await File.WriteAllTextAsync(Path.Combine(rootPath, "readme.txt"), "readme");
        Directory.CreateDirectory(Path.Combine(rootPath, "dir.exe"));
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(toolPath, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }

        try
        {
            using var navigationContext = new NavigationContext(rootPath);

            // act
            var entries = await navigationContext.ListEntriesAsync();

            // assert
            Assert.True(navigationContext.IsExecutable(entries.Single(entry => entry.Name == Path.GetFileName(toolPath))));
            Assert.False(navigationContext.IsExecutable(entries.Single(entry => entry.Name == "readme.txt")));
            Assert.False(navigationContext.IsExecutable(entries.Single(entry => entry.Name == "dir.exe")));
        }
        finally
        {
            Directory.Delete(rootPath, true);
        }
    }

    [Theory]
    [InlineData("setup.exe", true)]
    [InlineData("BUILD.CMD", true)]
    [InlineData("run.bat", true)]
    [InlineData("readme.txt", false)]
    [InlineData("exe", false)]
    public void When_IsExecutableName_Then_ExecutableExtensionsAreDetected(string name, bool expected) =>
        Assert.Equal(expected, NavigationContext.IsExecutableName(name));

    [Fact]
    public async Task When_OpenFile_Then_FileIsStarted()
    {
        // arrange
        var rootPath = CreateTempDirectory();
        await File.WriteAllTextAsync(Path.Combine(rootPath, "file1.txt"), "file1");
        try
        {
            var startedPaths = new List<string>();
            using var navigationContext = new NavigationContext(rootPath, startedPaths.Add);
            var entry = (await navigationContext.ListEntriesAsync()).Single(entry => entry.Name == "file1.txt");

            // act
            await navigationContext.OpenEntryAsync(entry);

            // assert
            Assert.Equal([Path.Combine(rootPath, "file1.txt")], startedPaths);
            Assert.Equal(rootPath, navigationContext.CurrentPath);
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
