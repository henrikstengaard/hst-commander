using Hst.Commander.Abstractions;

namespace Hst.Commander.Core.Tests;

public class GivenNavigationContextWithAmigaImages : IDisposable
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

    /// <summary>True when the file is closed, so it can be opened without sharing.</summary>
    private static bool IsClosed(string path)
    {
        try
        {
            using var _ = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            return true;
        }
        catch (IOException)
        {
            return false;
        }
    }

    [Fact]
    public async Task When_AdfIsChanged_Then_ChangesAreWrittenToTheFile()
    {
        // arrange
        var adfPath = Path.Combine(_rootPath, "disk.adf");
        await AmigaTestData.CreateAdf(adfPath);
        await File.WriteAllTextAsync(Path.Combine(_rootPath, "file1.txt"), "Amiga");
        var startedFiles = new List<string>();

        using (var adfContext = new NavigationContext(_rootPath, startedFiles.Add))
        using (var localContext = new NavigationContext(_rootPath))
        {
            // act
            await OpenAsync(adfContext, "disk.adf");
            Assert.Null(adfContext.CreateDirectoryNotSupportedMessage);
            Assert.Null(adfContext.MoveNotSupportedMessage);
            await adfContext.CreateDirectoryAsync("dir1");
            var localEntries = await localContext.ListEntriesAsync();
            await localContext.CopyAsync([localEntries.First(entry => entry.Name == "file1.txt")], adfContext,
                adfContext.CurrentPath, "*");
            var entries = await adfContext.ListEntriesAsync();
            await adfContext.RenameAsync(entries.First(entry => entry.Name == "dir1"), "Dir2");
            var names = await ListNamesAsync(adfContext);

            // assert
            Assert.Empty(startedFiles);
            Assert.True(adfContext.IsInImage);
            Assert.Equal([Paths.Parent, "Dir2", "file1.txt"], names);
            var copiedEntry = (await adfContext.ListEntriesAsync()).First(entry => entry.Name == "file1.txt");
            Assert.Equal(5, copiedEntry.Size);
            Assert.Equal(File.GetLastWriteTimeUtc(Path.Combine(_rootPath, "file1.txt")),
                copiedEntry.ModifiedDate!.Value, TimeSpan.FromSeconds(1));
            Assert.False(IsClosed(adfPath));
        }

        Assert.True(IsClosed(adfPath));
        Assert.Equal(["Dir2", "file1.txt"], await AmigaTestData.ListAdfNames(adfPath));
    }

    [Fact]
    public async Task When_AdfIsOpenInBothPanes_Then_ChangesInOnePaneAreSeenInTheOther()
    {
        // arrange
        var adfPath = Path.Combine(_rootPath, "disk.adf");
        await AmigaTestData.CreateAdf(adfPath);
        using var leftContext = new NavigationContext(_rootPath);
        using var rightContext = new NavigationContext(_rootPath);
        await OpenAsync(leftContext, "disk.adf");
        await OpenAsync(rightContext, "disk.adf");

        // act
        await leftContext.CreateDirectoryAsync("dir1");
        await rightContext.CreateDirectoryAsync("dir2");
        await OpenAsync(leftContext, "dir1");
        await leftContext.CreateDirectoryAsync("dir3");
        var leftEntries = await leftContext.ListEntriesAsync();
        await leftContext.CopyAsync([leftEntries.First(entry => entry.Name == "dir3")], rightContext,
            rightContext.CurrentPath, "*");
        var rightNames = await ListNamesAsync(rightContext);
        var leftNames = await ListNamesAsync(leftContext);

        // assert
        Assert.Equal([Paths.Parent, "dir1", "dir2", "dir3"], rightNames);
        Assert.Equal([Paths.Parent, "dir3"], leftNames);
        Assert.Equal(Path.Combine(_rootPath, "disk.adf", "dir1"), leftContext.CurrentPath);
    }

    [Fact]
    public async Task When_EntriesAreDeletedAndMovedInAdf_Then_TheyAreRemoved()
    {
        // arrange
        var adfPath = Path.Combine(_rootPath, "disk.adf");
        await AmigaTestData.CreateAdf(adfPath);
        await File.WriteAllTextAsync(Path.Combine(_rootPath, "file1.txt"), "file1");
        await File.WriteAllTextAsync(Path.Combine(_rootPath, "file2.txt"), "file2");
        using var adfContext = new NavigationContext(_rootPath);
        using var localContext = new NavigationContext(_rootPath);
        await OpenAsync(adfContext, "disk.adf");
        await adfContext.CreateDirectoryAsync("dir1");
        var localEntries = await localContext.ListEntriesAsync();
        await localContext.MoveAsync(
            localEntries.Where(entry => entry.Name is "file1.txt" or "file2.txt").ToList(), adfContext,
            adfContext.CurrentPath, "*");

        // act
        var entries = await adfContext.ListEntriesAsync();
        await adfContext.DeleteAsync(entries.Where(entry => entry.Name is "dir1" or "file1.txt").ToList());
        entries = await adfContext.ListEntriesAsync();
        await adfContext.MoveAsync(entries.Where(entry => entry.Name == "file2.txt").ToList(), localContext,
            localContext.CurrentPath, "*");

        // assert
        Assert.Equal([Paths.Parent], await ListNamesAsync(adfContext));
        Assert.Equal("file2", await File.ReadAllTextAsync(Path.Combine(_rootPath, "file2.txt")));
        Assert.False(File.Exists(Path.Combine(_rootPath, "file1.txt")));
    }

    [Fact]
    public async Task When_AdfFileIsReadOnly_Then_AdfIsReadOnly()
    {
        // arrange
        var adfPath = Path.Combine(_rootPath, "disk.adf");
        await AmigaTestData.CreateAdf(adfPath);
        File.SetAttributes(adfPath, FileAttributes.ReadOnly);
        try
        {
            using var navigationContext = new NavigationContext(_rootPath);

            // act
            await OpenAsync(navigationContext, "disk.adf");

            // assert
            Assert.True(navigationContext.IsInImage);
            Assert.NotNull(navigationContext.CreateDirectoryNotSupportedMessage);
            Assert.NotNull(navigationContext.MoveNotSupportedMessage);
            await Assert.ThrowsAsync<NotSupportedException>(() => navigationContext.CreateDirectoryAsync("dir1"));
        }
        finally
        {
            File.SetAttributes(adfPath, FileAttributes.Normal);
        }
    }

    [Theory]
    [InlineData("disk.hdf", false)]
    [InlineData("disk.vhd", true)]
    public async Task When_RdbImageIsOpened_Then_PartitionsAreListedAndWritable(string name, bool isVhd)
    {
        // arrange
        var imagePath = Path.Combine(_rootPath, name);
        if (isVhd)
        {
            await AmigaTestData.CreateVhdRdbImage(imagePath, AmigaTestData.Dos3DosType, AmigaTestData.Pfs3DosType);
        }
        else
        {
            await AmigaTestData.CreateRdbImage(imagePath, AmigaTestData.Dos3DosType, AmigaTestData.Pfs3DosType);
        }

        var startedFiles = new List<string>();
        using (var navigationContext = new NavigationContext(_rootPath, startedFiles.Add))
        {
            // act
            await OpenAsync(navigationContext, name);
            var tableNames = await ListNamesAsync(navigationContext);
            await OpenAsync(navigationContext, "rdb");
            var partitionNames = await ListNamesAsync(navigationContext);
            var partitionEntries = await navigationContext.ListEntriesAsync();
            await OpenAsync(navigationContext, "1");
            await navigationContext.CreateDirectoryAsync("ffs");
            await navigationContext.GoParentAsync();
            await OpenAsync(navigationContext, "2");
            await navigationContext.CreateDirectoryAsync("pfs3");

            // assert
            Assert.Empty(startedFiles);
            Assert.Equal([Paths.Parent, "rdb"], tableNames);
            Assert.Equal([Paths.Parent, "1", "2"], partitionNames);
            Assert.All(partitionEntries.Skip(1), entry => Assert.Equal(EntryType.Container, entry.Type));
            Assert.Equal(["1 - DH0 - Workbench", "2 - DH1 - Work"], partitionEntries.Skip(1).Select(entry => entry.DisplayName));
            Assert.All(partitionEntries.Skip(1), entry => Assert.True(entry.IsVolume));
            Assert.Equal([Paths.Parent, "pfs3"], await ListNamesAsync(navigationContext));
            Assert.Equal(Path.Combine(imagePath, "rdb", "2"), navigationContext.CurrentPath);
        }

        // the changes are read again from the file
        using var reopenedContext = new NavigationContext(_rootPath);
        await OpenAsync(reopenedContext, name);
        await OpenAsync(reopenedContext, "rdb");
        await OpenAsync(reopenedContext, "1");
        Assert.Equal([Paths.Parent, "ffs"], await ListNamesAsync(reopenedContext));
        await reopenedContext.ChangeLocationAsync(Path.Combine(imagePath, "rdb", "2", "pfs3"));
        Assert.Equal(Path.Combine(imagePath, "rdb", "2", "pfs3"), reopenedContext.CurrentPath);
    }

    [Fact]
    public async Task When_PropertiesOfRdbPartitionAreShown_Then_PartitionDetailsAreShown()
    {
        // arrange
        var imagePath = Path.Combine(_rootPath, "disk.hdf");
        await AmigaTestData.CreateRdbImage(imagePath, AmigaTestData.Dos3DosType, AmigaTestData.Pfs3DosType);
        using var navigationContext = new NavigationContext(_rootPath);
        await OpenAsync(navigationContext, "disk.hdf");
        await OpenAsync(navigationContext, "rdb");
        var partitions = await navigationContext.ListEntriesAsync();

        // act
        var properties = navigationContext.GetProperties([partitions.First(entry => entry.Name == "2")]);

        // assert
        var lines = properties.GetSections().SelectMany(section => section)
            .ToDictionary(line => line.Label, line => line.Value);
        Assert.Equal("2 - DH1 - Work", properties.Name);
        Assert.Equal("Amiga partition", lines["Type"]);
        Assert.Equal("2", lines["Partition"]);
        Assert.Equal("DH1", lines["Drive name"]);
        Assert.Equal("Work", lines["Volume name"]);
        Assert.Equal("Professional File System 3 (PFS\\3)", lines["File system"]);
        Assert.Equal("No", lines["Bootable"]);
        Assert.Contains("bytes", lines["Free space"]);
        Assert.Contains("bytes", lines["Capacity"]);
        Assert.NotNull(properties.UsedFraction);
        Assert.InRange(properties.UsedFraction.Value, 0, 1);
    }

    [Fact]
    public async Task When_RdbPartitionCantBeMounted_Then_ItIsShownWithoutVolumeName()
    {
        // arrange
        var imagePath = Path.Combine(_rootPath, "disk.hdf");
        await AmigaTestData.CreateRdbImage(imagePath, AmigaTestData.Dos3DosType, AmigaTestData.SfsDosType);
        using var navigationContext = new NavigationContext(_rootPath);
        await OpenAsync(navigationContext, "disk.hdf");
        await OpenAsync(navigationContext, "rdb");

        // act
        var partitions = await navigationContext.ListEntriesAsync();
        var properties = navigationContext.GetProperties([partitions.First(entry => entry.Name == "2")]);

        // assert
        Assert.Equal(["1 - DH0 - Workbench", "2 - DH1"], partitions.Skip(1).Select(entry => entry.DisplayName));
        var lines = properties.GetSections().SelectMany(section => section)
            .ToDictionary(line => line.Label, line => line.Value);
        Assert.Equal("DH1", lines["Drive name"]);
        Assert.Equal("Smart File System (SFS\\0)", lines["File system"]);
        Assert.False(lines.ContainsKey("Volume name"));
        Assert.False(lines.ContainsKey("Free space"));
        Assert.Null(properties.UsedFraction);
    }

    [Fact]
    public async Task When_RdbPartitionHasDiskIcon_Then_IconIsReadFromInsideThePartition()
    {
        // arrange
        var imagePath = Path.Combine(_rootPath, "disk.hdf");
        await AmigaTestData.CreateRdbImage(imagePath, AmigaTestData.Dos3DosType, AmigaTestData.Pfs3DosType);
        await File.WriteAllBytesAsync(Path.Combine(_rootPath, "Disk.info"), await AmigaIconTestData.CreatePlanarIcon());
        using var localContext = new NavigationContext(_rootPath);
        using var rdbContext = new NavigationContext(_rootPath);
        await OpenAsync(rdbContext, "disk.hdf");
        await OpenAsync(rdbContext, "rdb");
        await OpenAsync(rdbContext, "1");
        var localEntries = await localContext.ListEntriesAsync();
        await localContext.CopyAsync([localEntries.First(entry => entry.Name == "Disk.info")], rdbContext,
            rdbContext.CurrentPath, "*");
        await rdbContext.GoParentAsync();
        var partitions = await rdbContext.ListEntriesAsync();

        // act
        var icon = await rdbContext.ReadIconAsync(partitions.First(entry => entry.Name == "1"), 16);
        var icons = await rdbContext.ReadIconsAsync(partitions.First(entry => entry.Name == "1"));
        var noIcon = await rdbContext.ReadIconAsync(partitions.First(entry => entry.Name == "2"), 16);

        // assert
        Assert.True(rdbContext.HasIcon(partitions.First(entry => entry.Name == "1")));
        Assert.Null(rdbContext.GetIconFile(partitions.First(entry => entry.Name == "1")));
        Assert.NotNull(icon);
        Assert.Equal(4, icon.Width);
        Assert.Equal(["Normal (planar)"], icons.Select(entryIcon => entryIcon.Name));
        Assert.Null(noIcon);
        // the pane stays in the partition list
        Assert.Equal(Path.Combine(imagePath, "rdb"), rdbContext.CurrentPath);
        Assert.Equal([Paths.Parent, "1", "2"], await ListNamesAsync(rdbContext));
    }

    [Fact]
    public async Task When_RdbPartitionIconsAreReadOnUiThread_Then_ItDoesNotDeadlock()
    {
        // arrange
        var imagePath = Path.Combine(_rootPath, "disk.hdf");
        await AmigaTestData.CreateRdbImage(imagePath, AmigaTestData.Dos3DosType, AmigaTestData.Pfs3DosType);

        // act and assert
        SingleThreadSynchronizationContext.Run(async () =>
        {
            using var navigationContext = new NavigationContext(_rootPath);
            await OpenAsync(navigationContext, "disk.hdf");
            await OpenAsync(navigationContext, "rdb");
            var partitions = await navigationContext.ListEntriesAsync();
            foreach (var partition in partitions.Skip(1))
            {
                Assert.Null(await navigationContext.ReadIconAsync(partition, 16));
            }

            // listing again closes the location icons were read from
            await navigationContext.ListEntriesAsync();
        }, TimeSpan.FromSeconds(30));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task When_RdbImageIsClosedOnUiThread_Then_ItDoesNotDeadlock(bool writeFile)
    {
        // arrange
        var imagePath = Path.Combine(_rootPath, "disk.hdf");
        await AmigaTestData.CreateRdbImage(imagePath, AmigaTestData.Dos3DosType, AmigaTestData.Pfs3DosType);
        await File.WriteAllTextAsync(Path.Combine(_rootPath, "file1.txt"), "Amiga");

        // act, like a front-end on its UI thread: closing PFS3 flushes it, which must not wait for the UI thread
        SingleThreadSynchronizationContext.Run(async () =>
        {
            using var navigationContext = new NavigationContext(_rootPath);
            using var localContext = new NavigationContext(_rootPath);
            await OpenAsync(navigationContext, "disk.hdf");
            await OpenAsync(navigationContext, "rdb");
            await OpenAsync(navigationContext, "2");
            if (writeFile)
            {
                var localEntries = await localContext.ListEntriesAsync();
                await localContext.CopyAsync([localEntries.First(entry => entry.Name == "file1.txt")],
                    navigationContext, navigationContext.CurrentPath, "*");
            }

            await navigationContext.GoParentAsync();
            await navigationContext.GoParentAsync();
            await navigationContext.GoParentAsync();

            // assert
            Assert.False(navigationContext.IsInImage);
            Assert.Equal(_rootPath, navigationContext.CurrentPath);
        }, TimeSpan.FromSeconds(30));

        Assert.True(IsClosed(imagePath));
    }

    [Fact]
    public async Task When_RdbHasNoPartitions_Then_OpeningItThrowsIOException()
    {
        // arrange
        var imagePath = Path.Combine(_rootPath, "disk.hdf");
        await AmigaTestData.CreateRdbImage(imagePath);
        var startedFiles = new List<string>();
        using var navigationContext = new NavigationContext(_rootPath, startedFiles.Add);
        var entry = (await navigationContext.ListEntriesAsync()).First(entry => entry.Name == "disk.hdf");

        // act
        var exception = await Assert.ThrowsAsync<IOException>(() => navigationContext.OpenEntryAsync(entry));

        // assert
        Assert.Contains("no partitions", exception.Message);
        Assert.Empty(startedFiles);
        Assert.False(navigationContext.IsInImage);
        Assert.True(IsClosed(imagePath));
    }

    [Fact]
    public async Task When_FileHasAdfSignatureButNotAdfSize_Then_FileIsStarted()
    {
        // arrange
        await File.WriteAllBytesAsync(Path.Combine(_rootPath, "notes.txt"), "DOS\u0003 not an adf"u8.ToArray());
        var startedFiles = new List<string>();
        using var navigationContext = new NavigationContext(_rootPath, startedFiles.Add);

        // act
        await OpenAsync(navigationContext, "notes.txt");

        // assert
        Assert.False(navigationContext.IsInImage);
        Assert.Equal([Path.Combine(_rootPath, "notes.txt")], startedFiles);
    }
}
