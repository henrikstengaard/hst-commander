using DiscUtils.Iso9660;
using Hst.Commander.Core.Volumes;
using Hst.Commander.Extensions.Volume.Abstractions;

namespace Hst.Commander.Core.Tests;

public class GivenVolumeDelete
{
    private readonly TestVolume volume;

    public GivenVolumeDelete()
    {
        // arrange - create a volume with directories and files
        volume = new TestVolume();
        volume.AddEntry([], new TestEntry("file1.txt", EntryType.Item));
        volume.AddEntry([], new TestEntry("file2.txt", EntryType.Item));
        volume.AddEntry([], new TestEntry("dir1", EntryType.Container));
        volume.AddEntry(["dir1"], new TestEntry("file3.txt", EntryType.Item));
        volume.AddEntry(["dir1"], new TestEntry("dir2", EntryType.Container));
        volume.AddEntry(["dir1", "dir2"], new TestEntry("file4.txt", EntryType.Item));
    }

    [Fact]
    public async Task When_DeleteItems_Then_ItemsAreDeleted()
    {
        // arrange
        var entries = await GetEntriesAsync(volume, "file1.txt");

        // act
        await volume.DeleteAsync(entries);

        // assert - only the deleted item is gone
        Assert.Null(volume.GetEntry(["file1.txt"]));
        Assert.NotNull(volume.GetEntry(["file2.txt"]));
        Assert.NotNull(volume.GetEntry(["dir1"]));
    }

    [Fact]
    public async Task When_DeleteContainer_Then_ContainerIsDeletedRecursively()
    {
        // arrange
        var entries = await GetEntriesAsync(volume, "dir1");

        // act
        await volume.DeleteAsync(entries);

        // assert - container is deleted, other entries are kept and the current directory is unchanged
        Assert.Null(volume.GetEntry(["dir1"]));
        Assert.NotNull(volume.GetEntry(["file1.txt"]));
        Assert.Equal([Paths.Root], volume.CurrentPathComponents);
    }

    [Fact]
    public async Task When_Delete_Then_ProgressIsReportedForAllEntries()
    {
        // arrange
        var entries = await volume.ListEntries().ToListAsync();
        var reports = new List<DeleteProgress>();

        // act
        await volume.DeleteAsync(entries, new SynchronousProgress<DeleteProgress>(reports.Add));

        // assert - 6 entries deleted, items and containers
        Assert.Empty(await volume.ListEntries().ToListAsync());
        Assert.Equal(6, reports[^1].DeletedEntries);
        Assert.Equal(6, reports[^1].TotalEntries);
        Assert.Equal(1, reports[^1].TotalFraction);
    }

    [Fact]
    public async Task When_DeleteIsCancelled_Then_OperationCanceledExceptionIsThrown()
    {
        // arrange
        var entries = await volume.ListEntries().ToListAsync();
        using var cancellationTokenSource = new CancellationTokenSource();
        await cancellationTokenSource.CancelAsync();

        // act & assert - nothing is deleted
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            volume.DeleteAsync(entries, cancellationToken: cancellationTokenSource.Token));
        Assert.NotNull(volume.GetEntry(["file1.txt"]));
    }

    [Fact]
    public async Task When_DeleteFromIso9660Volume_Then_NotSupportedExceptionIsThrown()
    {
        // arrange
        var builder = new CDBuilder { UseJoliet = true };
        builder.AddFile("file1.txt", "file1"u8.ToArray());
        await using var isoStream = new MemoryStream();
        builder.Build(isoStream);
        isoStream.Position = 0;
        var isoVolume = new Iso9660Volume(new CDReader(isoStream, true));
        var entries = await isoVolume.ListEntries().ToListAsync();

        // act & assert
        await Assert.ThrowsAsync<NotSupportedException>(() => isoVolume.DeleteAsync(entries));
    }

    private static async Task<List<IEntry>> GetEntriesAsync(IVolume volume, params string[] names) =>
        (await volume.ListEntries().ToListAsync()).Where(entry => names.Contains(entry.Name)).ToList();

    /// <summary>Progress calling the handler on the reporting thread, unlike <see cref="Progress{T}" />.</summary>
    private sealed class SynchronousProgress<T>(Action<T> handler) : IProgress<T>
    {
        public void Report(T value) => handler(value);
    }
}
