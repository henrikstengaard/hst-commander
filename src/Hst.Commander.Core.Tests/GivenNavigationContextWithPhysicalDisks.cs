using Hst.Commander.Abstractions;
using Hst.Commander.Core.Volumes;
using Hst.Commander.Plugins.PhysicalDisk;

namespace Hst.Commander.Core.Tests;

public class GivenNavigationContextWithPhysicalDisks : IDisposable
{
    private readonly string _rootPath =
        Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), $"hst-commander-{Guid.NewGuid()}")).FullName;

    private readonly TestPhysicalDisks _disks = new();
    private readonly PhysicalDiskVolumeProvider _volumeProvider;

    public GivenNavigationContextWithPhysicalDisks()
    {
        _volumeProvider = new PhysicalDiskVolumeProvider(_disks);
    }

    public void Dispose()
    {
        Directory.Delete(_rootPath, true);
        GC.SuppressFinalize(this);
    }

    /// <summary>Adds a disk with an MBR and a FAT partition.</summary>
    private async Task AddMbrDisk(string name, string? model = null)
    {
        var path = Path.Combine(_rootPath, $"{name}.img");
        await using (var disk = DiskTestData.CreateMbrDisk())
        {
            await DiskTestData.WriteToFile(disk, path);
        }

        _disks.AddDisk(name, path, model);
    }

    private async Task<NavigationContext> CreateContextAsync()
    {
        var navigationContext = new NavigationContext(_rootPath);
        await navigationContext.ChangeVolumeProviderAsync(_volumeProvider);
        return navigationContext;
    }

    private static async Task OpenAsync(NavigationContext navigationContext, string name) =>
        await navigationContext.OpenEntryAsync(
            (await navigationContext.ListEntriesAsync()).First(entry => entry.Name == name));

    private static async Task<IReadOnlyList<string>> ListNamesAsync(NavigationContext navigationContext) =>
        (await navigationContext.ListEntriesAsync()).Select(entry => entry.Name).ToList();

    [Fact]
    public async Task When_ListingPhysicalDisks_Then_DisksAreListedWithTheirModel()
    {
        // arrange
        await AddMbrDisk("PhysicalDrive1", "SanDisk Ultra");
        await AddMbrDisk("PhysicalDrive2");
        using var navigationContext = await CreateContextAsync();

        // act
        var entries = await navigationContext.ListEntriesAsync();

        // assert
        Assert.Equal(PhysicalDiskVolume.VolumeName, navigationContext.CurrentPath);
        Assert.Equal(["PhysicalDrive1", "PhysicalDrive2"], entries.Select(entry => entry.Name));
        Assert.Equal(["PhysicalDrive1 - SanDisk Ultra", "PhysicalDrive2"], entries.Select(entry => entry.DisplayName));
        Assert.All(entries, entry => Assert.Equal(EntryType.Item, entry.Type));
        Assert.Equal(new FileInfo(Path.Combine(_rootPath, "PhysicalDrive1.img")).Length, entries[0].Size);
        var details = Assert.IsType<EntryDetails>(entries[0].Details);
        Assert.Contains(details.Sections.SelectMany(section => section),
            property => property is { Label: "Model", Value: "SanDisk Ultra" });
    }

    [Fact]
    public async Task When_DiskIsOpened_Then_PathStartsWithTheDiskAndFilesAreWrittenToIt()
    {
        // arrange
        await AddMbrDisk("PhysicalDrive1");
        await File.WriteAllTextAsync(Path.Combine(_rootPath, "file1.txt"), "Disk");

        using (var diskContext = await CreateContextAsync())
        using (var otherDiskContext = await CreateContextAsync())
        using (var localContext = new NavigationContext(_rootPath))
        {
            // act
            await OpenAsync(diskContext, "PhysicalDrive1");
            var tableNames = await ListNamesAsync(diskContext);
            await OpenAsync(diskContext, "mbr");
            await OpenAsync(diskContext, "1");
            var path = diskContext.CurrentPath;
            Assert.Null(diskContext.CreateDirectoryNotSupportedMessage);
            await diskContext.CreateDirectoryAsync("dir1");
            var localEntries = await localContext.ListEntriesAsync();
            await localContext.CopyAsync([localEntries.First(entry => entry.Name == "file1.txt")], diskContext,
                diskContext.CurrentPath, "*");

            // the other pane shares the opened disk, so it sees the changes
            await otherDiskContext.ChangeLocationAsync(Path.Combine("PhysicalDrive1", "mbr", "1"));
            var otherNames = await ListNamesAsync(otherDiskContext);

            // assert
            Assert.True(diskContext.IsInImage);
            Assert.Equal([Paths.Parent, "mbr"], tableNames);
            Assert.Equal(Path.Combine("PhysicalDrive1", "mbr", "1"), path);
            Assert.Equal([Paths.Parent, "dir1", "file1.txt"], otherNames);
        }

        // opened again, the changes are on the disk
        using var reopenedContext = await CreateContextAsync();
        await reopenedContext.ChangeLocationAsync(Path.Combine("PhysicalDrive1", "mbr", "1"));
        Assert.Equal([Paths.Parent, "dir1", "file1.txt"], await ListNamesAsync(reopenedContext));
    }

    [Fact]
    public async Task When_DiskIsChanged_Then_CachedWritesAreWrittenWhenIdleWhileTheDiskIsOpen()
    {
        // arrange
        await AddMbrDisk("PhysicalDrive1");
        var diskPath = Path.Combine(_rootPath, "PhysicalDrive1.img");
        var original = ReadDisk(diskPath);
        using var diskContext = await CreateContextAsync();
        await diskContext.ChangeLocationAsync(Path.Combine("PhysicalDrive1", "mbr", "1"));

        // act
        await diskContext.CreateDirectoryAsync("dir1");

        // assert
        var timeout = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        while (ReadDisk(diskPath).SequenceEqual(original) && DateTime.UtcNow < timeout)
        {
            await Task.Delay(50);
        }

        Assert.False(ReadDisk(diskPath).SequenceEqual(original));
    }

    [Fact]
    public async Task When_FilesAreCopiedToDisk_Then_CachedWritesAreWrittenBeforeTheCopyEndsWithProgress()
    {
        // arrange
        await AddMbrDisk("PhysicalDrive1");
        var diskPath = Path.Combine(_rootPath, "PhysicalDrive1.img");
        await File.WriteAllTextAsync(Path.Combine(_rootPath, "file1.txt"), "Disk");
        using var diskContext = await CreateContextAsync();
        using var localContext = new NavigationContext(_rootPath);
        await diskContext.ChangeLocationAsync(Path.Combine("PhysicalDrive1", "mbr", "1"));
        var original = ReadDisk(diskPath);
        var progress = new List<CopyProgress>();

        // act
        var localEntries = await localContext.ListEntriesAsync();
        await localContext.CopyAsync([localEntries.First(entry => entry.Name == "file1.txt")], diskContext,
            diskContext.CurrentPath, "*", new SynchronousProgress<CopyProgress>(progress.Add));

        // assert
        Assert.False(ReadDisk(diskPath).SequenceEqual(original));
        var diskWrites = progress.Select(value => value.DiskWrite).OfType<DiskWriteProgress>().ToList();
        Assert.Equal(0, diskWrites[0].WrittenBytes);
        Assert.True(diskWrites[0].TotalBytes > 0);
        Assert.Equal(1, diskWrites[^1].Fraction);
        Assert.True(diskWrites[^1].BytesPerSecond > 0);
        Assert.Equal(TimeSpan.Zero, diskWrites[^1].Eta);
        Assert.Equal("file1.txt", progress[^1].CurrentName);
    }

    [Fact]
    public async Task When_FilesAreDeletedFromDisk_Then_CachedWritesAreWrittenBeforeTheDeleteEndsWithProgress()
    {
        // arrange
        await AddMbrDisk("PhysicalDrive1");
        var diskPath = Path.Combine(_rootPath, "PhysicalDrive1.img");
        using (var context = await CreateContextAsync())
        {
            await context.ChangeLocationAsync(Path.Combine("PhysicalDrive1", "mbr", "1"));
            await context.CreateDirectoryAsync("dir1");
        }

        using var diskContext = await CreateContextAsync();
        await diskContext.ChangeLocationAsync(Path.Combine("PhysicalDrive1", "mbr", "1"));
        var original = ReadDisk(diskPath);
        var progress = new List<DeleteProgress>();

        // act
        var entries = await diskContext.ListEntriesAsync();
        await diskContext.DeleteAsync([entries.First(entry => entry.Name == "dir1")],
            new SynchronousProgress<DeleteProgress>(progress.Add));

        // assert
        Assert.False(ReadDisk(diskPath).SequenceEqual(original));
        Assert.Contains(progress, value => value.DiskWrite is { Fraction: 1, Eta: not null });
    }

    private sealed class SynchronousProgress<T>(Action<T> handler) : IProgress<T>
    {
        public void Report(T value) => handler(value);
    }

    /// <summary>Bytes of a disk image file, read while the disk is open.</summary>
    private static byte[] ReadDisk(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        var bytes = new byte[stream.Length];
        stream.ReadExactly(bytes);
        return bytes;
    }

    [Fact]
    public async Task When_DiskCantBeWritten_Then_ItIsOpenedReadOnly()
    {
        // arrange
        await AddMbrDisk("PhysicalDrive1");
        _disks.ReadOnlyDisks.Add("PhysicalDrive1");
        using var navigationContext = await CreateContextAsync();

        // act
        await navigationContext.ChangeLocationAsync(Path.Combine("PhysicalDrive1", "mbr", "1"));

        // assert
        Assert.NotNull(navigationContext.CreateDirectoryNotSupportedMessage);
        await Assert.ThrowsAnyAsync<Exception>(() => navigationContext.CreateDirectoryAsync("dir1"));
    }

    [Fact]
    public async Task When_DiskCantBeRead_Then_OpeningItThrowsTheError()
    {
        // arrange
        await AddMbrDisk("PhysicalDrive1");
        _disks.DeniedDisks.Add("PhysicalDrive1");
        using var navigationContext = await CreateContextAsync();

        // act and assert
        var exception = await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            OpenAsync(navigationContext, "PhysicalDrive1"));
        Assert.Contains("denied", exception.Message);
        Assert.False(navigationContext.IsInImage);
    }

    [Fact]
    public async Task When_SystemDiskIsOpened_Then_ItIsNotAccessible()
    {
        // arrange
        await AddMbrDisk("PhysicalDrive0");
        _disks.SystemDisks.Add("PhysicalDrive0");
        using var navigationContext = await CreateContextAsync();
        var volume = new PhysicalDiskVolume(_disks);

        // act and assert
        var exception = await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            OpenAsync(navigationContext, "PhysicalDrive0"));
        Assert.Contains("system disk", exception.Message);
        Assert.False(navigationContext.IsInImage);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => volume.OpenRead("PhysicalDrive0"));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => volume.OpenReadWrite("PhysicalDrive0"));
    }

    [Fact]
    public async Task When_PhysicalDiskListIsChanged_Then_NotSupportedExceptionIsThrown()
    {
        // arrange
        await AddMbrDisk("PhysicalDrive1");
        var volume = await _volumeProvider.CreateVolume();

        // act and assert
        await Assert.ThrowsAsync<NotSupportedException>(() => volume.Delete("PhysicalDrive1"));
        await Assert.ThrowsAsync<NotSupportedException>(() => volume.CreateContainer("dir1"));
        await Assert.ThrowsAsync<DirectoryNotFoundException>(() => volume.ChangeDirectory(["PhysicalDrive1"]));
    }
}
