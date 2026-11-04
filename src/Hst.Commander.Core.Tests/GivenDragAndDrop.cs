using Hst.Commander.Abstractions;
using Hst.Commander.Core.Volumes;

namespace Hst.Commander.Core.Tests;

public class GivenDragAndDrop : IDisposable
{
    private readonly string _rootPath =
        Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), $"hst-commander-{Guid.NewGuid()}")).FullName;

    public void Dispose()
    {
        Directory.Delete(_rootPath, true);
        GC.SuppressFinalize(this);
    }

    private static async Task OpenAsync(NavigationContext navigationContext, string name) =>
        await navigationContext.OpenEntryAsync(
            (await navigationContext.ListEntriesAsync()).First(entry => entry.Name == name));

    private static async Task<IReadOnlyList<string>> ListNamesAsync(NavigationContext navigationContext) =>
        (await navigationContext.ListEntriesAsync()).Select(entry => entry.Name).ToList();

    private string CreateDirectory(params string[] names) =>
        Directory.CreateDirectory(Path.Combine([_rootPath, ..names])).FullName;

    [Fact]
    public async Task When_EntriesOfTwoDirectoriesAreDropped_Then_TheyAreCopied()
    {
        // arrange
        var source1 = CreateDirectory("source1");
        var source2 = CreateDirectory("source2", "sub");
        var destination = CreateDirectory("destination");
        await File.WriteAllTextAsync(Path.Combine(source1, "file1.txt"), "file1");
        await File.WriteAllTextAsync(Path.Combine(source2, "file2.txt"), "file2");
        using var destinationContext = new NavigationContext(destination);

        // act
        using (var dropped = await DroppedEntries.OpenAsync(
                   [Path.Combine(source1, "file1.txt"), Path.Combine(_rootPath, "source2")], destinationContext))
        {
            Assert.Equal("Copy 2 entries", dropped.CopyDescription);
            await dropped.CopyAsync(destinationContext, destinationContext.CurrentPath, EntryFilter.All);
        }

        // assert
        Assert.Equal("file1", await File.ReadAllTextAsync(Path.Combine(destination, "file1.txt")));
        Assert.Equal("file2", await File.ReadAllTextAsync(Path.Combine(destination, "source2", "sub", "file2.txt")));
        Assert.True(File.Exists(Path.Combine(source1, "file1.txt")));
    }

    [Fact]
    public async Task When_EntriesAreDroppedToMove_Then_TheyAreMoved()
    {
        // arrange
        var source = CreateDirectory("source");
        var destination = CreateDirectory("destination");
        await File.WriteAllTextAsync(Path.Combine(source, "file1.txt"), "file1");
        using var destinationContext = new NavigationContext(destination);

        // act
        using (var dropped = await DroppedEntries.OpenAsync([Path.Combine(source, "file1.txt")]))
        {
            Assert.Equal("Move 'file1.txt'", dropped.MoveDescription);
            await dropped.MoveAsync(destinationContext, destinationContext.CurrentPath, EntryFilter.All);
        }

        // assert
        Assert.True(File.Exists(Path.Combine(destination, "file1.txt")));
        Assert.False(File.Exists(Path.Combine(source, "file1.txt")));
    }

    [Fact]
    public async Task When_MissingPathIsDropped_Then_IOExceptionIsThrown()
    {
        await Assert.ThrowsAsync<IOException>(() =>
            DroppedEntries.OpenAsync([Path.Combine(_rootPath, "missing.txt")]));
    }

    [Fact]
    public async Task When_EntriesAreDroppedOnTheirOwnDirectory_Then_IOExceptionIsThrown()
    {
        // arrange
        await File.WriteAllTextAsync(Path.Combine(_rootPath, "file1.txt"), "file1");
        using var destinationContext = new NavigationContext(_rootPath);
        using var dropped = await DroppedEntries.OpenAsync([Path.Combine(_rootPath, "file1.txt")]);

        // act and assert
        await Assert.ThrowsAsync<IOException>(() =>
            dropped.CopyAsync(destinationContext, destinationContext.CurrentPath, EntryFilter.All));
    }

    [Fact]
    public async Task When_CopiedToSubdirectoryOfOtherPaneInImage_Then_EntriesAreCopiedIntoIt()
    {
        // arrange
        await AmigaTestData.CreateAdf(Path.Combine(_rootPath, "disk.adf"));
        await File.WriteAllTextAsync(Path.Combine(_rootPath, "file1.txt"), "Amiga");
        using var localContext = new NavigationContext(_rootPath);
        using var adfContext = new NavigationContext(_rootPath);
        await OpenAsync(adfContext, "disk.adf");
        await adfContext.CreateDirectoryAsync("dir1");
        var entries = (await localContext.ListEntriesAsync()).Where(entry => entry.Name == "file1.txt").ToList();

        // act
        await localContext.CopyAsync(entries, adfContext,
            adfContext.CurrentPath + Path.DirectorySeparatorChar + "dir1", EntryFilter.All);
        await OpenAsync(adfContext, "dir1");

        // assert
        Assert.Equal([Paths.Parent, "file1.txt"], await ListNamesAsync(adfContext));
    }

    [Fact]
    public async Task When_CopiedToMissingSubdirectoryOfOtherPaneInImage_Then_IOExceptionIsThrown()
    {
        // arrange
        await AmigaTestData.CreateAdf(Path.Combine(_rootPath, "disk.adf"));
        await File.WriteAllTextAsync(Path.Combine(_rootPath, "file1.txt"), "Amiga");
        using var localContext = new NavigationContext(_rootPath);
        using var adfContext = new NavigationContext(_rootPath);
        await OpenAsync(adfContext, "disk.adf");
        var entries = (await localContext.ListEntriesAsync()).Where(entry => entry.Name == "file1.txt").ToList();

        // act and assert
        await Assert.ThrowsAsync<IOException>(() => localContext.CopyAsync(entries, adfContext,
            adfContext.CurrentPath + Path.DirectorySeparatorChar + "missing", EntryFilter.All));
    }

    [Fact]
    public async Task When_EntriesInZipAreDragged_Then_FilesAreListedAndRead()
    {
        // arrange
        await File.WriteAllBytesAsync(Path.Combine(_rootPath, "archive.zip"),
            ZipTestData.CreateZipWithDirectoriesAndFiles());
        using var zipContext = new NavigationContext(_rootPath);
        await OpenAsync(zipContext, "archive.zip");
        var entries = (await zipContext.ListEntriesAsync())
            .Where(entry => entry.Name is "file1.txt" or "dir1").ToList();

        Assert.Null(zipContext.GetLocalPaths(entries));

        // act
        using var dragSource = await VolumeDragSource.OpenAsync(zipContext, entries);
        await zipContext.GoParentAsync();
        var files = await dragSource.ListFilesAsync();
        string content;
        await using (var stream = await dragSource.OpenReadAsync(files.First(file => file.Name == "file2.txt")))
        using (var reader = new StreamReader(stream))
        {
            content = await reader.ReadToEndAsync();
        }

        // assert - the pane navigated away, the drag source reads its own volume
        Assert.Equal(["dir1", "dir1\\file2.txt", "file1.txt"],
            files.Select(file => file.RelativePath).Order(StringComparer.Ordinal));
        Assert.True(files.Single(file => file.RelativePath == "dir1").IsDirectory);
        Assert.Equal(9, files.Single(file => file.RelativePath == "file1.txt").Size);
        Assert.Equal("zip-file2", content);
    }

    [Fact]
    public async Task When_AmigaEntriesWithInvalidNamesAreDragged_Then_LocalNamesAreUsed()
    {
        // arrange
        var adfPath = Path.Combine(_rootPath, "disk.adf");
        await AmigaTestData.CreateAdf(adfPath);
        await using (var stream = File.Open(adfPath, FileMode.Open, FileAccess.ReadWrite))
        using (var volume = await Hst.Commander.Plugins.Amiga.AmigaFileSystems.MountAdf(stream))
        {
            await volume.CreateContainer("dir*1");
            await volume.ChangeDirectory(["dir*1"]);
            await using var itemStream = await volume.OpenWrite("file?1");
            await itemStream.WriteAsync("Amiga"u8.ToArray());
        }

        using var adfContext = new NavigationContext(_rootPath) { UaeMetadata = UaeMetadataFormat.None };
        await OpenAsync(adfContext, "disk.adf");
        var entries = (await adfContext.ListEntriesAsync()).Where(entry => entry.Name == "dir*1").ToList();

        // act
        using var dragSource = await VolumeDragSource.OpenAsync(adfContext, entries);
        var files = await dragSource.ListFilesAsync();

        // assert
        Assert.Equal(["dir_1", "dir_1\\file_1"], files.Select(file => file.RelativePath));
        Assert.Equal("dir*1", files[0].Name);
        Assert.Equal(5, files[1].Size);
    }

    [Theory]
    [InlineData(UaeMetadataFormat.UaeFsDb)]
    [InlineData(UaeMetadataFormat.UaeMetafile)]
    public async Task When_AmigaEntriesAreDraggedWithUaeMetadata_Then_FilesAreTheFilesCopyingWrites(
        UaeMetadataFormat format)
    {
        // arrange - Amiga names that can't be used locally, a comment and protection bits
        var adfPath = Path.Combine(_rootPath, "disk.adf");
        await AmigaTestData.CreateAdf(adfPath);
        await using (var stream = File.Open(adfPath, FileMode.Open, FileAccess.ReadWrite))
        using (var volume = await Hst.Commander.Plugins.Amiga.AmigaFileSystems.MountAdf(stream))
        {
            await volume.CreateContainer("dir*1");
            await volume.ChangeDirectory(["dir*1"]);
            await using (var itemStream = await volume.OpenWrite("file?1"))
            {
                await itemStream.WriteAsync("Amiga"u8.ToArray());
            }

            await volume.SetAmigaMetadata("file?1", new AmigaMetadata(0, "Comment"));
            await using (var itemStream = await volume.OpenWrite("file2"))
            {
                await itemStream.WriteAsync("file2"u8.ToArray());
            }
        }

        using var adfContext = new NavigationContext(_rootPath) { UaeMetadata = format };
        await OpenAsync(adfContext, "disk.adf");
        var entries = (await adfContext.ListEntriesAsync()).Where(entry => entry.Name == "dir*1").ToList();
        var copied = CreateDirectory("copied");
        using var copiedContext = new NavigationContext(copied);
        await adfContext.CopyAsync(entries, copiedContext, copiedContext.CurrentPath, "*");

        // act - the dragged files written like Explorer writes them
        var dragged = CreateDirectory("dragged");
        using (var dragSource = await VolumeDragSource.OpenAsync(adfContext, entries))
        {
            foreach (var file in await dragSource.ListFilesAsync())
            {
                var path = Path.Combine(dragged, file.RelativePath.Replace(VolumeDragSource.Separator,
                    Path.DirectorySeparatorChar));
                if (file.IsDirectory)
                {
                    Directory.CreateDirectory(path);
                    continue;
                }

                await using var source = await dragSource.OpenReadAsync(file);
                await using var destination = File.Create(path);
                await source.CopyToAsync(destination);
            }
        }

        // assert - the same names and metadata files as copying
        var copiedFiles = ReadFiles(copied);
        var draggedFiles = ReadFiles(dragged);
        Assert.Equal(copiedFiles.Keys, draggedFiles.Keys);
        Assert.Contains(copiedFiles.Keys, path => path.EndsWith(
            format == UaeMetadataFormat.UaeFsDb ? "_UAEFSDB.___" : ".uaem", StringComparison.Ordinal));
        foreach (var (path, contents) in copiedFiles)
        {
            Assert.Equal(contents, draggedFiles[path]);
        }
    }

    private static Dictionary<string, byte[]?> ReadFiles(string directory) =>
        Directory.EnumerateFileSystemEntries(directory, "*", SearchOption.AllDirectories)
            .Order(StringComparer.Ordinal)
            .ToDictionary(path => Path.GetRelativePath(directory, path),
                path => File.Exists(path) ? File.ReadAllBytes(path) : null);

    [Fact]
    public async Task When_DraggedDirectoryIsCopiedToLocalPath_Then_ItGetsThePathName()
    {
        // arrange
        await File.WriteAllBytesAsync(Path.Combine(_rootPath, "archive.zip"),
            ZipTestData.CreateZipWithDirectoriesAndFiles());
        using var zipContext = new NavigationContext(_rootPath);
        await OpenAsync(zipContext, "archive.zip");
        var entries = (await zipContext.ListEntriesAsync()).Where(entry => entry.Name == "dir1").ToList();
        var destination = CreateDirectory("destination");

        // act
        using var dragSource = await VolumeDragSource.OpenAsync(zipContext, entries);
        await dragSource.CopyToLocalAsync(entries[0], Path.Combine(destination, "dir1 2"));

        // assert
        Assert.Equal("zip-file2", await File.ReadAllTextAsync(Path.Combine(destination, "dir1 2", "file2.txt")));
        Assert.False(Directory.Exists(Path.Combine(destination, "dir1")));
    }

    [Fact]
    public async Task When_LocalPathsOfEntriesAreRead_Then_FullPathsAreReturned()
    {
        // arrange
        await File.WriteAllTextAsync(Path.Combine(_rootPath, "file1.txt"), "file1");
        using var context = new NavigationContext(_rootPath);
        var entries = (await context.ListEntriesAsync()).Where(entry => entry.Name == "file1.txt").ToList();

        // act
        var paths = context.GetLocalPaths(entries);

        // assert
        Assert.Equal([Path.Combine(_rootPath, "file1.txt")], paths);
    }
}
