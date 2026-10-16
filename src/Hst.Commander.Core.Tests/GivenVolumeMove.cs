using Hst.Commander.Core.Volumes;
using Hst.Commander.Abstractions;

namespace Hst.Commander.Core.Tests;

public class GivenVolumeMove
{
    private readonly TestVolume sourceVolume;
    private readonly TestVolume destinationVolume;

    public GivenVolumeMove()
    {
        // arrange - create a source volume with directories and files and an empty destination volume
        sourceVolume = new TestVolume();
        sourceVolume.AddEntry([], CreateFile("file1.txt", 10));
        sourceVolume.AddEntry([], CreateFile("file2.bin", 20));
        sourceVolume.AddEntry([], new TestEntry("dir1", EntryType.Container));
        sourceVolume.AddEntry(["dir1"], CreateFile("file3.txt", 30));
        sourceVolume.AddEntry(["dir1"], CreateFile("file4.bin", 40));
        sourceVolume.AddEntry(["dir1"], new TestEntry("dir2", EntryType.Container));
        sourceVolume.AddEntry(["dir1", "dir2"], CreateFile("file5.txt", 50));

        destinationVolume = new TestVolume();
    }

    [Fact]
    public async Task When_MoveItems_Then_ItemsAreMovedToDestination()
    {
        // arrange
        var entries = await GetEntriesAsync(sourceVolume, "file1.txt", "file2.bin");

        // act
        await sourceVolume.MoveToAsync(entries, destinationVolume);

        // assert - files are in the destination with their data and deleted from the source
        Assert.Equal(CreateData(10), destinationVolume.GetEntry(["file1.txt"])?.Data);
        Assert.Equal(CreateData(20), destinationVolume.GetEntry(["file2.bin"])?.Data);
        Assert.Null(sourceVolume.GetEntry(["file1.txt"]));
        Assert.Null(sourceVolume.GetEntry(["file2.bin"]));
    }

    [Fact]
    public async Task When_MoveContainer_Then_ContainerIsMovedRecursively()
    {
        // arrange
        var entries = await GetEntriesAsync(sourceVolume, "dir1");

        // act
        await sourceVolume.MoveToAsync(entries, destinationVolume);

        // assert - directories and files are in the destination and the directory is deleted from the source
        Assert.Equal(CreateData(30), destinationVolume.GetEntry(["dir1", "file3.txt"])?.Data);
        Assert.Equal(CreateData(50), destinationVolume.GetEntry(["dir1", "dir2", "file5.txt"])?.Data);
        Assert.Null(sourceVolume.GetEntry(["dir1"]));
        Assert.Equal([Paths.Root], sourceVolume.CurrentPathComponents);
        Assert.Equal([Paths.Root], destinationVolume.CurrentPathComponents);
    }

    [Fact]
    public async Task When_MoveWithFilter_Then_ItemsNotMatchingAreKeptInSource()
    {
        // arrange
        var entries = await sourceVolume.ListEntries().ToListAsync();

        // act
        await sourceVolume.MoveToAsync(entries, destinationVolume, new CopyOptions("*.txt"));

        // assert - txt files are moved, directories still containing other files are kept, empty ones are deleted
        Assert.NotNull(destinationVolume.GetEntry(["dir1", "dir2", "file5.txt"]));
        Assert.Null(sourceVolume.GetEntry(["file1.txt"]));
        Assert.NotNull(sourceVolume.GetEntry(["file2.bin"]));
        Assert.Null(sourceVolume.GetEntry(["dir1", "file3.txt"]));
        Assert.NotNull(sourceVolume.GetEntry(["dir1", "file4.bin"]));
        Assert.Null(sourceVolume.GetEntry(["dir1", "dir2"]));
    }

    [Fact]
    public async Task When_OverwriteIsAnsweredNo_Then_ItemIsKeptInSource()
    {
        // arrange - the destination already has file1.txt
        destinationVolume.AddEntry([], CreateFile("file1.txt", 5));
        var entries = await GetEntriesAsync(sourceVolume, "file1.txt", "file2.bin");

        // act
        await sourceVolume.MoveToAsync(entries, destinationVolume, new CopyOptions(
            ConfirmOverwrite: (_, _) => Task.FromResult(OverwriteAnswer.No)));

        // assert - the existing file isn't overwritten and the skipped file stays in the source
        Assert.Equal(CreateData(5), destinationVolume.GetEntry(["file1.txt"])?.Data);
        Assert.NotNull(sourceVolume.GetEntry(["file1.txt"]));
        Assert.Null(sourceVolume.GetEntry(["file2.bin"]));
    }

    [Fact]
    public async Task When_MoveToReadOnlyVolume_Then_SourceIsUnchanged()
    {
        // arrange
        var entries = await GetEntriesAsync(sourceVolume, "file1.txt");

        // act & assert
        await Assert.ThrowsAsync<NotSupportedException>(() => sourceVolume.MoveToAsync(entries, new NullVolume()));
        Assert.NotNull(sourceVolume.GetEntry(["file1.txt"]));
    }

    private static async Task<List<IEntry>> GetEntriesAsync(IVolume volume, params string[] names) =>
        (await volume.ListEntries().ToListAsync()).Where(entry => names.Contains(entry.Name)).ToList();

    private static TestEntry CreateFile(string name, int size) =>
        new(name, EntryType.Item, size, data: CreateData(size));

    private static byte[] CreateData(int size) => Enumerable.Range(0, size).Select(index => (byte)index).ToArray();
}
