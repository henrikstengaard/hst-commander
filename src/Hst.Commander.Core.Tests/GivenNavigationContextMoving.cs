using Hst.Commander.Core.Volumes;
using Hst.Commander.Extensions.Volume.Abstractions;

namespace Hst.Commander.Core.Tests;

public class GivenNavigationContextMoving : IDisposable
{
    private readonly string rootPath;
    private readonly string sourcePath;
    private readonly string destinationPath;

    public GivenNavigationContextMoving()
    {
        // arrange - source with files and a directory, destination empty
        rootPath = Path.Combine(Path.GetTempPath(), $"hst-commander-{Guid.NewGuid()}");
        sourcePath = Path.Combine(rootPath, "source");
        destinationPath = Path.Combine(rootPath, "destination");
        Directory.CreateDirectory(Path.Combine(sourcePath, "dir1"));
        Directory.CreateDirectory(destinationPath);
        File.WriteAllText(Path.Combine(sourcePath, "file1.txt"), "file1");
        File.WriteAllText(Path.Combine(sourcePath, "file2.bin"), "file2");
        File.WriteAllText(Path.Combine(sourcePath, "dir1", "file3.txt"), "file3");
    }

    public void Dispose() => Directory.Delete(rootPath, true);

    [Fact]
    public async Task When_MoveMarkedEntriesToOtherContext_Then_EntriesAreMoved()
    {
        // arrange
        using var source = new NavigationContext(sourcePath);
        using var destination = new NavigationContext(destinationPath);
        foreach (var entry in await source.ListEntriesAsync())
        {
            source.ToggleMark(entry);
        }

        var entries = source.GetOperationEntries(null);

        // act
        await source.MoveAsync(entries, destination, destination.CurrentPath, EntryFilter.All);

        // assert
        Assert.Equal(3, entries.Count);
        Assert.Equal("file1", await File.ReadAllTextAsync(Path.Combine(destinationPath, "file1.txt")));
        Assert.Equal("file3", await File.ReadAllTextAsync(Path.Combine(destinationPath, "dir1", "file3.txt")));
        Assert.Empty(Directory.EnumerateFileSystemEntries(sourcePath));
    }

    [Fact]
    public async Task When_MoveWithFilter_Then_OnlyMatchingFilesAreMoved()
    {
        // arrange
        using var source = new NavigationContext(sourcePath);
        using var destination = new NavigationContext(destinationPath);
        var entries = (await source.ListEntriesAsync()).Where(entry => entry.Name != Paths.Parent).ToList();

        // act
        await source.MoveAsync(entries, destination, destination.CurrentPath, "*.txt");

        // assert - the directory is empty after moving its txt file, so it's deleted
        Assert.True(File.Exists(Path.Combine(destinationPath, "file1.txt")));
        Assert.True(File.Exists(Path.Combine(destinationPath, "dir1", "file3.txt")));
        Assert.False(File.Exists(Path.Combine(destinationPath, "file2.bin")));
        Assert.True(File.Exists(Path.Combine(sourcePath, "file2.bin")));
        Assert.False(File.Exists(Path.Combine(sourcePath, "file1.txt")));
        Assert.False(Directory.Exists(Path.Combine(sourcePath, "dir1")));
    }

    [Fact]
    public async Task When_MoveOntoExistingFile_Then_OverwriteIsConfirmed()
    {
        // arrange
        await File.WriteAllTextAsync(Path.Combine(destinationPath, "file1.txt"), "old");
        using var source = new NavigationContext(sourcePath);
        using var destination = new NavigationContext(destinationPath);
        var entries = source.GetOperationEntries(await GetEntryAsync(source, "file1.txt"));
        var confirmed = new List<string>();

        // act
        await source.MoveAsync(entries, destination, destination.CurrentPath, EntryFilter.All,
            confirmOverwrite: (request, _) =>
            {
                confirmed.Add(request.Name);
                return Task.FromResult(OverwriteAnswer.Yes);
            });

        // assert
        Assert.Equal(["file1.txt"], confirmed);
        Assert.Equal("file1", await File.ReadAllTextAsync(Path.Combine(destinationPath, "file1.txt")));
        Assert.False(File.Exists(Path.Combine(sourcePath, "file1.txt")));
    }

    [Fact]
    public async Task When_MoveToName_Then_EntryIsRenamedAndSelected()
    {
        // arrange
        using var source = new NavigationContext(sourcePath);
        using var destination = new NavigationContext(destinationPath);
        var entries = source.GetOperationEntries(await GetEntryAsync(source, "file1.txt"));
        var previousPath = source.CurrentPath;

        // act
        Assert.Equal("renamed.txt", await source.GetRenameNameAsync(entries, destination, " renamed.txt "));
        await source.MoveAsync(entries, destination, " renamed.txt ", EntryFilter.All);
        var listedEntries = await source.ListEntriesAsync();
        var selectedIndex = source.GetSelectedIndex(listedEntries, previousPath, 0);

        // assert
        Assert.Equal("file1", await File.ReadAllTextAsync(Path.Combine(sourcePath, "renamed.txt")));
        Assert.False(File.Exists(Path.Combine(sourcePath, "file1.txt")));
        Assert.Equal("renamed.txt", listedEntries[selectedIndex].Name);
        Assert.Empty(Directory.EnumerateFileSystemEntries(destinationPath));
    }

    [Fact]
    public async Task When_MoveToNameOfExistingDirectory_Then_EntryIsMovedIntoIt()
    {
        // arrange
        using var source = new NavigationContext(sourcePath);
        using var destination = new NavigationContext(destinationPath);
        var entries = source.GetOperationEntries(await GetEntryAsync(source, "file1.txt"));

        // act
        Assert.Null(await source.GetRenameNameAsync(entries, destination, "dir1"));
        await source.MoveAsync(entries, destination, "dir1", EntryFilter.All);

        // assert
        Assert.True(File.Exists(Path.Combine(sourcePath, "dir1", "file1.txt")));
        Assert.False(File.Exists(Path.Combine(sourcePath, "file1.txt")));
    }

    [Fact]
    public async Task When_MoveMultipleEntriesToName_Then_NotRenamed()
    {
        // arrange
        using var source = new NavigationContext(sourcePath);
        using var destination = new NavigationContext(destinationPath);
        var listedEntries = await source.ListEntriesAsync();
        source.ToggleMark(listedEntries.First(entry => entry.Name == "file1.txt"));
        source.ToggleMark(listedEntries.First(entry => entry.Name == "file2.bin"));
        var entries = source.GetOperationEntries(null);

        // act
        Assert.Null(await source.GetRenameNameAsync(entries, destination, "newdir"));
        await source.MoveAsync(entries, destination, "newdir", EntryFilter.All);

        // assert - moved into a new directory relative to the source
        Assert.True(File.Exists(Path.Combine(sourcePath, "newdir", "file1.txt")));
        Assert.True(File.Exists(Path.Combine(sourcePath, "newdir", "file2.bin")));
    }

    [Fact]
    public async Task When_RenameToExistingName_Then_IOExceptionIsThrown()
    {
        // arrange
        using var source = new NavigationContext(sourcePath);
        var entry = await GetEntryAsync(source, "file1.txt");

        // act & assert
        await Assert.ThrowsAsync<IOException>(() => source.RenameAsync(entry, "file2.bin"));
        Assert.True(File.Exists(Path.Combine(sourcePath, "file1.txt")));
    }

    [Fact]
    public async Task When_MoveDirectoryIntoItself_Then_IOExceptionIsThrown()
    {
        // arrange
        using var source = new NavigationContext(sourcePath);
        var entries = source.GetOperationEntries(await GetEntryAsync(source, "dir1"));

        // act & assert
        await Assert.ThrowsAsync<IOException>(() =>
            source.MoveAsync(entries, source, Path.Combine(sourcePath, "dir1", "sub"), EntryFilter.All));
        Assert.True(File.Exists(Path.Combine(sourcePath, "dir1", "file3.txt")));
    }

    [Fact]
    public async Task When_MoveInDriveList_Then_NotSupportedExceptionIsThrown()
    {
        // arrange
        using var source = new NavigationContext(string.Empty);
        using var destination = new NavigationContext(destinationPath);
        var entries = source.GetOperationEntries((await source.ListEntriesAsync()).FirstOrDefault());

        // act & assert
        Assert.NotNull(source.MoveNotSupportedMessage);
        await Assert.ThrowsAsync<NotSupportedException>(() =>
            source.MoveAsync(entries, destination, destination.CurrentPath, EntryFilter.All));
    }

    [Fact]
    public async Task When_RenameInVolume_Then_EntryIsRenamed()
    {
        // arrange
        using var navigationContext = new NavigationContext(string.Empty);
        await navigationContext.ChangeVolumeProviderAsync(new TestVolumeProvider(volume =>
        {
            volume.AddEntry([], new TestEntry("dir1", EntryType.Container));
            volume.AddEntry(["dir1"], new TestEntry("file1.txt", EntryType.Item, 5));
        }));
        var entry = await GetEntryAsync(navigationContext, "dir1");

        // act
        await navigationContext.RenameAsync(entry, "dir2");
        var entries = await navigationContext.ListEntriesAsync();
        await navigationContext.OpenEntryAsync(entries.First(listedEntry => listedEntry.Name == "dir2"));

        // assert - the directory keeps its entries
        Assert.Equal(["dir2"], entries.Select(listedEntry => listedEntry.Name));
        Assert.Equal(["..", "file1.txt"], (await navigationContext.ListEntriesAsync()).Select(e => e.Name));
    }

    private static async Task<IEntry> GetEntryAsync(NavigationContext navigationContext, string name) =>
        (await navigationContext.ListEntriesAsync()).First(entry => entry.Name == name);
}
