using Hst.Commander.Abstractions;

namespace Hst.Commander.Core.Tests;

// EntryClipboard.Current is static, so these tests don't run in parallel with each other
[Collection(nameof(GivenEntryClipboard))]
public class GivenEntryClipboard : IDisposable
{
    private readonly string rootPath;
    private readonly string sourcePath;
    private readonly string destinationPath;

    public GivenEntryClipboard()
    {
        // arrange - source with a file and a directory, destination empty
        rootPath = Path.Combine(Path.GetTempPath(), $"hst-commander-{Guid.NewGuid()}");
        sourcePath = Path.Combine(rootPath, "source");
        destinationPath = Path.Combine(rootPath, "destination");
        Directory.CreateDirectory(Path.Combine(sourcePath, "dir1"));
        Directory.CreateDirectory(destinationPath);
        File.WriteAllText(Path.Combine(sourcePath, "file1.txt"), "file1");
        File.WriteAllText(Path.Combine(sourcePath, "dir1", "file2.txt"), "file2");
    }

    public void Dispose()
    {
        EntryClipboard.Clear();
        Directory.Delete(rootPath, true);
    }

    private static async Task<IEntry> GetEntryAsync(NavigationContext context, string name) =>
        (await context.ListEntriesAsync()).First(entry => entry.Name == name);

    [Fact]
    public async Task When_CopiedEntriesArePastedAfterSourceChangedLocation_Then_EntriesAreCopied()
    {
        // arrange
        using var source = new NavigationContext(sourcePath);
        using var destination = new NavigationContext(destinationPath);
        var clipboard = await EntryClipboard.SetAsync(source,
            [await GetEntryAsync(source, "file1.txt"), await GetEntryAsync(source, "dir1")], false);
        await source.GoParentAsync();

        // act
        using var pasted = await EntryClipboard.GetPasteSourceAsync(true, null, false);
        await pasted!.PasteAsync(destination);

        // assert
        Assert.Same(clipboard, EntryClipboard.Current);
        Assert.False(pasted.IsMove);
        Assert.False(pasted.IsFromHost);
        Assert.Equal([Path.Combine(sourcePath, "file1.txt"), Path.Combine(sourcePath, "dir1")], clipboard.LocalPaths);
        Assert.Equal("file1", await File.ReadAllTextAsync(Path.Combine(destinationPath, "file1.txt")));
        Assert.Equal("file2", await File.ReadAllTextAsync(Path.Combine(destinationPath, "dir1", "file2.txt")));
        Assert.True(File.Exists(Path.Combine(sourcePath, "file1.txt")));
    }

    [Fact]
    public async Task When_CutEntriesArePasted_Then_EntriesAreMoved()
    {
        // arrange
        using var source = new NavigationContext(sourcePath);
        using var destination = new NavigationContext(destinationPath);
        await EntryClipboard.SetAsync(source, [await GetEntryAsync(source, "file1.txt")], true);

        // act
        using var pasted = await EntryClipboard.GetPasteSourceAsync(true, null, false);
        await pasted!.PasteAsync(destination);

        // assert
        Assert.True(pasted.IsMove);
        Assert.Equal("file1", await File.ReadAllTextAsync(Path.Combine(destinationPath, "file1.txt")));
        Assert.False(File.Exists(Path.Combine(sourcePath, "file1.txt")));
    }

    [Fact]
    public async Task When_HostDoesNotHoldEntries_Then_HostFilesArePasted()
    {
        // arrange - entries on the clipboard, then a file copied in another application
        using var source = new NavigationContext(sourcePath);
        using var destination = new NavigationContext(destinationPath);
        await EntryClipboard.SetAsync(source, [await GetEntryAsync(source, "dir1")], false);
        var hostFile = Path.Combine(rootPath, "host.txt");
        await File.WriteAllTextAsync(hostFile, "host");

        // act
        using var pasted = await EntryClipboard.GetPasteSourceAsync(false, [hostFile], true, destination);
        await pasted!.PasteAsync(destination);

        // assert
        Assert.True(pasted.IsFromHost);
        Assert.Equal("host", await File.ReadAllTextAsync(Path.Combine(destinationPath, "host.txt")));
        Assert.False(File.Exists(hostFile));
        Assert.False(Directory.Exists(Path.Combine(destinationPath, "dir1")));
    }

    [Fact]
    public async Task When_CutEntriesAreMovedByAnotherApplication_Then_ClipboardIsCleared()
    {
        // arrange
        using var source = new NavigationContext(sourcePath);
        await EntryClipboard.SetAsync(source,
            [await GetEntryAsync(source, "file1.txt"), await GetEntryAsync(source, "dir1")], true);
        var unchanged = EntryClipboard.CheckCutMoved();

        // act - another application moves one entry, then the other
        File.Move(Path.Combine(sourcePath, "file1.txt"), Path.Combine(destinationPath, "file1.txt"));
        var firstMoved = EntryClipboard.CheckCutMoved();
        var isClearedAfterFirst = EntryClipboard.Current == null;
        var unchangedAfterFirst = EntryClipboard.CheckCutMoved();
        Directory.Move(Path.Combine(sourcePath, "dir1"), Path.Combine(destinationPath, "dir1"));
        var secondMoved = EntryClipboard.CheckCutMoved();

        // assert
        Assert.False(unchanged);
        Assert.True(firstMoved);
        Assert.False(isClearedAfterFirst);
        Assert.False(unchangedAfterFirst);
        Assert.True(secondMoved);
        Assert.Null(EntryClipboard.Current);
    }

    [Fact]
    public async Task When_CutEntriesArePastedInPane_Then_TheyAreNotSeenAsMovedByAnotherApplication()
    {
        // arrange
        using var source = new NavigationContext(sourcePath);
        using var destination = new NavigationContext(destinationPath);
        var clipboard = await EntryClipboard.SetAsync(source, [await GetEntryAsync(source, "file1.txt")], true);

        // act
        bool isMovedWhilePasting;
        using (var pasted = await EntryClipboard.GetPasteSourceAsync(true, null, false))
        {
            await pasted!.PasteAsync(destination);
            isMovedWhilePasting = EntryClipboard.CheckCutMoved();
        }

        // assert
        Assert.False(isMovedWhilePasting);
        Assert.Same(clipboard, EntryClipboard.Current);
    }

    [Fact]
    public async Task When_CopiedEntriesAreMoved_Then_TheyAreNotChecked()
    {
        // arrange
        using var source = new NavigationContext(sourcePath);
        await EntryClipboard.SetAsync(source, [await GetEntryAsync(source, "file1.txt")], false);

        // act
        File.Delete(Path.Combine(sourcePath, "file1.txt"));
        var isMoved = EntryClipboard.CheckCutMoved();

        // assert
        Assert.False(isMoved);
        Assert.NotNull(EntryClipboard.Current);
    }

    [Fact]
    public async Task When_NothingOnClipboard_Then_NothingIsPasted()
    {
        // act
        EntryClipboard.Clear();
        var pasted = await EntryClipboard.GetPasteSourceAsync(true, null, false);

        // assert
        Assert.Null(pasted);
    }

    [Fact]
    public async Task When_ParentIsPutOnClipboard_Then_ItIsLeftOut()
    {
        // arrange
        using var source = new NavigationContext(sourcePath);
        var entries = await source.ListEntriesAsync();

        // act
        var clipboard = await EntryClipboard.SetAsync(source, entries, false);

        // assert
        Assert.DoesNotContain(clipboard.Entries, entry => entry.Name == Paths.Parent);
        Assert.Equal(2, clipboard.Entries.Count);
    }

    [Fact]
    public async Task When_EntriesInZipAreCopiedAfterLeavingIt_Then_EntriesAreCopied()
    {
        // arrange
        await File.WriteAllBytesAsync(Path.Combine(sourcePath, "archive.zip"),
            ZipTestData.CreateZipWithDirectoriesAndFiles());
        using var source = new NavigationContext(sourcePath);
        using var destination = new NavigationContext(destinationPath);
        await source.OpenEntryAsync(await GetEntryAsync(source, "archive.zip"));
        await source.OpenEntryAsync(await GetEntryAsync(source, "dir1"));
        var clipboard = await EntryClipboard.SetAsync(source, [await GetEntryAsync(source, "file2.txt")], false);
        await source.GoRootAsync();

        // act
        using var pasted = await EntryClipboard.GetPasteSourceAsync(true, null, false);
        await pasted!.PasteAsync(destination);

        // assert
        Assert.Null(clipboard.LocalPaths);
        Assert.Equal(Path.Combine(sourcePath, "archive.zip", "dir1"), clipboard.Context.CurrentPath);
        Assert.Equal("zip-file2", await File.ReadAllTextAsync(Path.Combine(destinationPath, "file2.txt")));
    }

    [Fact]
    public async Task When_CutEntriesInZip_Then_NotSupportedIsThrown()
    {
        // arrange
        await File.WriteAllBytesAsync(Path.Combine(sourcePath, "archive.zip"),
            ZipTestData.CreateZipWithDirectoriesAndFiles());
        using var source = new NavigationContext(sourcePath);
        await source.OpenEntryAsync(await GetEntryAsync(source, "archive.zip"));
        var entry = await GetEntryAsync(source, "file1.txt");

        // act & assert
        await Assert.ThrowsAsync<NotSupportedException>(() => EntryClipboard.SetAsync(source, [entry], true));
    }
}
