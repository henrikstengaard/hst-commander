using Hst.Commander.Abstractions;

namespace Hst.Commander.Core.Tests;

public class GivenNavigationContextWithDiskImages : IDisposable
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

    [Fact]
    public async Task When_PiStormDiskIsOpened_Then_AmigaPartitionIsWritten()
    {
        // arrange
        await using (var disk = await DiskTestData.CreatePiStormDisk(AmigaTestData.Dos3DosType))
        {
            await DiskTestData.WriteToFile(disk, Path.Combine(_rootPath, "pistorm.img"));
        }

        await File.WriteAllTextAsync(Path.Combine(_rootPath, "file1.txt"), "Amiga");
        var startedFiles = new List<string>();

        using (var diskContext = new NavigationContext(_rootPath, startedFiles.Add))
        using (var localContext = new NavigationContext(_rootPath))
        {
            // act
            await OpenAsync(diskContext, "pistorm.img");
            var tableNames = await ListNamesAsync(diskContext);
            await OpenAsync(diskContext, "mbr");
            var partitionNames = await ListNamesAsync(diskContext);
            await OpenAsync(diskContext, "2");
            await OpenAsync(diskContext, "rdb");
            await OpenAsync(diskContext, "1");
            var path = diskContext.CurrentPath;
            Assert.Null(diskContext.CreateDirectoryNotSupportedMessage);
            await diskContext.CreateDirectoryAsync("dir1");
            var localEntries = await localContext.ListEntriesAsync();
            await localContext.CopyAsync([localEntries.First(entry => entry.Name == "file1.txt")], diskContext,
                diskContext.CurrentPath, "*");
            var names = await ListNamesAsync(diskContext);

            // assert
            Assert.Empty(startedFiles);
            Assert.True(diskContext.IsInImage);
            Assert.Equal([Paths.Parent, "mbr"], tableNames);
            Assert.Equal([Paths.Parent, "1", "2"], partitionNames);
            Assert.Equal(Path.Combine(_rootPath, "pistorm.img", "mbr", "2", "rdb", "1"), path);
            Assert.Equal([Paths.Parent, "dir1", "file1.txt"], names);
        }

        // opened again, the changes are in the file
        using var reopenedContext = new NavigationContext(_rootPath);
        await reopenedContext.ChangeLocationAsync(Path.Combine(_rootPath, "pistorm.img", "mbr", "2", "rdb", "1"));
        Assert.Equal([Paths.Parent, "dir1", "file1.txt"], await ListNamesAsync(reopenedContext));
    }

    [Fact]
    public async Task When_AttributesOfFatEntryAreChanged_Then_TheyAreWritten()
    {
        // arrange
        await using (var disk = DiskTestData.CreateMbrDisk())
        {
            await DiskTestData.WriteToFile(disk, Path.Combine(_rootPath, "disk.img"));
        }

        using var navigationContext = new NavigationContext(_rootPath);
        await OpenAsync(navigationContext, "disk.img");
        await OpenAsync(navigationContext, "mbr");
        await OpenAsync(navigationContext, "1");
        await navigationContext.CreateDirectoryAsync("dir1");
        var entry = (await navigationContext.ListEntriesAsync()).Single(entry => entry.Name == "dir1");
        var properties = await navigationContext.GetPropertiesAsync([entry]);
        var attributes = Assert.Single(properties.FlagGroups);

        // act
        var changed = await navigationContext.ChangeFlagsAsync(properties,
            [attributes.Flags.Select(flag => flag.Label is "Read-only" or "Hidden").ToList()]);

        // assert
        Assert.Equal(["Read-only", "Hidden", "System", "Archive"], attributes.Flags.Select(flag => flag.Label));
        Assert.True(attributes.CanChange);
        Assert.True(changed);
        var changedEntry = (await navigationContext.ListEntriesAsync()).Single(entry => entry.Name == "dir1");
        Assert.Equal(EntryAttributes.ReadOnly | EntryAttributes.Hidden, changedEntry.Attributes);
    }

    [Theory]
    [InlineData(false, "mbr")]
    [InlineData(true, "gpt")]
    public async Task When_DiskIsOpened_Then_PartitionTableHasDiskSize(bool gpt, string partitionTableName)
    {
        // arrange
        var imagePath = Path.Combine(_rootPath, "disk.img");
        await using (var disk = gpt ? DiskTestData.CreateGptDisk() : DiskTestData.CreateMbrDisk())
        {
            await DiskTestData.WriteToFile(disk, imagePath);
        }

        using var navigationContext = new NavigationContext(_rootPath);

        // act
        await OpenAsync(navigationContext, "disk.img");
        var partitionTable = (await navigationContext.ListEntriesAsync()).Single(entry => entry.Name == partitionTableName);
        var lines = navigationContext.GetProperties([partitionTable]).GetSections().SelectMany(section => section)
            .ToDictionary(line => line.Label, line => line.Value);

        // assert
        var diskSize = new FileInfo(imagePath).Length;
        Assert.Equal(diskSize, partitionTable.Size);
        Assert.Equal(SizeFormatter.FormatHumanized(diskSize), SizeFormatter.FormatEntrySize(partitionTable));
        Assert.Equal(SizeFormatter.FormatWithBytes(diskSize), lines["Disk size"]);
    }

    [Fact]
    public async Task When_HybridDiskIsOpened_Then_PartitionTablesAreListed()
    {
        // arrange
        await DiskTestData.CreateHybridDisk(Path.Combine(_rootPath, "hybrid.img"), AmigaTestData.Dos3DosType);
        using var navigationContext = new NavigationContext(_rootPath);

        // act
        await OpenAsync(navigationContext, "hybrid.img");
        var names = await ListNamesAsync(navigationContext);
        await OpenAsync(navigationContext, "mbr");
        await OpenAsync(navigationContext, "1");
        await navigationContext.CreateDirectoryAsync("dir1");
        var fatNames = await ListNamesAsync(navigationContext);

        // assert
        Assert.Equal([Paths.Parent, "mbr", "rdb"], names);
        Assert.Equal([Paths.Parent, "dir1"], fatNames);
    }
}
