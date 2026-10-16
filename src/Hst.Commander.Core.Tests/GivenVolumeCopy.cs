using DiscUtils.Iso9660;
using Hst.Commander.Core.Volumes;
using Hst.Commander.Abstractions;

namespace Hst.Commander.Core.Tests;

public class GivenVolumeCopy
{
    private readonly TestVolume sourceVolume;
    private readonly TestVolume destinationVolume;

    public GivenVolumeCopy()
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
        destinationVolume.AddEntry([], new TestEntry("dest", EntryType.Container));
    }

    [Fact]
    public async Task When_CopyItems_Then_ItemsAreCopiedToDestination()
    {
        // arrange - copy to "dest" directory in destination volume
        await destinationVolume.ChangeDirectory(["dest"]);
        var entries = await GetEntriesAsync(sourceVolume, "file1.txt", "file2.bin");

        // act
        await sourceVolume.CopyToAsync(entries, destinationVolume);

        // assert - files are copied with their data
        Assert.Equal(CreateData(10), destinationVolume.GetEntry(["dest", "file1.txt"])?.Data);
        Assert.Equal(CreateData(20), destinationVolume.GetEntry(["dest", "file2.bin"])?.Data);
    }

    [Fact]
    public async Task When_CopyContainer_Then_ContainerIsCopiedRecursively()
    {
        // arrange
        var entries = await GetEntriesAsync(sourceVolume, "dir1");

        // act
        await sourceVolume.CopyToAsync(entries, destinationVolume);

        // assert - directories and files are copied
        Assert.Equal(EntryType.Container, destinationVolume.GetEntry(["dir1"])?.Type);
        Assert.Equal(EntryType.Container, destinationVolume.GetEntry(["dir1", "dir2"])?.Type);
        Assert.Equal(CreateData(30), destinationVolume.GetEntry(["dir1", "file3.txt"])?.Data);
        Assert.Equal(CreateData(40), destinationVolume.GetEntry(["dir1", "file4.bin"])?.Data);
        Assert.Equal(CreateData(50), destinationVolume.GetEntry(["dir1", "dir2", "file5.txt"])?.Data);

        // assert - current directories of the volumes are unchanged
        Assert.Equal([Paths.Root], sourceVolume.CurrentPathComponents);
        Assert.Equal([Paths.Root], destinationVolume.CurrentPathComponents);
    }

    [Fact]
    public async Task When_CopyWithFilter_Then_OnlyMatchingItemsAreCopied()
    {
        // arrange
        var entries = await sourceVolume.ListEntries().ToListAsync();

        // act
        await sourceVolume.CopyToAsync(entries, destinationVolume, new CopyOptions("*.txt"));

        // assert - only txt files are copied, directories are always copied
        Assert.NotNull(destinationVolume.GetEntry(["file1.txt"]));
        Assert.Null(destinationVolume.GetEntry(["file2.bin"]));
        Assert.NotNull(destinationVolume.GetEntry(["dir1", "file3.txt"]));
        Assert.Null(destinationVolume.GetEntry(["dir1", "file4.bin"]));
        Assert.NotNull(destinationVolume.GetEntry(["dir1", "dir2", "file5.txt"]));
    }

    [Fact]
    public async Task When_CopySingleItemWithTargetName_Then_ItemIsCopiedWithTargetName()
    {
        // arrange
        var entries = await GetEntriesAsync(sourceVolume, "file1.txt");

        // act
        await sourceVolume.CopyToAsync(entries, destinationVolume, new CopyOptions(TargetName: "renamed.txt"));

        // assert
        Assert.Null(destinationVolume.GetEntry(["file1.txt"]));
        Assert.Equal(CreateData(10), destinationVolume.GetEntry(["renamed.txt"])?.Data);
    }

    [Fact]
    public async Task When_Copy_Then_ProgressIsReported()
    {
        // arrange
        var entries = await sourceVolume.ListEntries().ToListAsync();
        var reports = new List<CopyProgress>();
        var progress = new SynchronousProgress<CopyProgress>(reports.Add);

        // act
        await sourceVolume.CopyToAsync(entries, destinationVolume, progress: progress);

        // assert - the last report has all files and bytes copied
        Assert.NotEmpty(reports);
        var last = reports[^1];
        Assert.Equal(5, last.TotalItems);
        Assert.Equal(5, last.CopiedItems);
        Assert.Equal(150, last.TotalBytes);
        Assert.Equal(150, last.CopiedBytes);
        Assert.Equal(1, last.TotalFraction);

        // assert - each file is reported when started
        Assert.Contains(reports, report => report is { CurrentName: "file5.txt", CurrentBytes: 0 });
    }

    [Fact]
    public async Task When_CopyIsCancelled_Then_OperationCanceledExceptionIsThrown()
    {
        // arrange
        var entries = await sourceVolume.ListEntries().ToListAsync();
        using var cancellationTokenSource = new CancellationTokenSource();
        await cancellationTokenSource.CancelAsync();

        // act & assert
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            sourceVolume.CopyToAsync(entries, destinationVolume, cancellationToken: cancellationTokenSource.Token));
    }

    [Fact]
    public async Task When_CopyIsCancelledWhileCopyingItem_Then_PartiallyCopiedItemIsDeleted()
    {
        // arrange - cancelled when the copy of the file starts, after the destination file is created
        sourceVolume.AddEntry([], CreateFile("large.bin", 3 * 1024 * 1024));
        var entries = await GetEntriesAsync(sourceVolume, "large.bin");
        using var cancellationTokenSource = new CancellationTokenSource();
        var progress = new SynchronousProgress<CopyProgress>(report =>
        {
            if (report.CurrentName == "large.bin")
            {
                cancellationTokenSource.Cancel();
            }
        });

        // act
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            sourceVolume.CopyToAsync(entries, destinationVolume, progress: progress,
                cancellationToken: cancellationTokenSource.Token));

        // assert - the partially copied file is deleted
        Assert.Null(destinationVolume.GetEntry(["large.bin"]));
    }

    [Fact]
    public async Task When_CopyBetweenLocalVolumesIsCancelled_Then_PartiallyCopiedFileIsDeleted()
    {
        // arrange
        var rootPath = Path.Combine(Path.GetTempPath(), $"hst-commander-{Guid.NewGuid()}");
        var sourcePath = Path.Combine(rootPath, "source");
        var destinationPath = Path.Combine(rootPath, "destination");
        Directory.CreateDirectory(sourcePath);
        Directory.CreateDirectory(destinationPath);
        await File.WriteAllBytesAsync(Path.Combine(sourcePath, "large.bin"), CreateData(3 * 1024 * 1024));
        try
        {
            var localSourceVolume = new LocalVolume(sourcePath);
            var entries = await GetEntriesAsync(localSourceVolume, "large.bin");
            using var cancellationTokenSource = new CancellationTokenSource();
            var progress = new SynchronousProgress<CopyProgress>(report =>
            {
                if (report.CurrentName == "large.bin")
                {
                    cancellationTokenSource.Cancel();
                }
            });

            // act
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                localSourceVolume.CopyToAsync(entries, new LocalVolume(destinationPath), progress: progress,
                    cancellationToken: cancellationTokenSource.Token));

            // assert
            Assert.False(File.Exists(Path.Combine(destinationPath, "large.bin")));
        }
        finally
        {
            Directory.Delete(rootPath, true);
        }
    }

    [Fact]
    public async Task When_ItemExistsAndOverwriteIsAnsweredNo_Then_ItemIsSkipped()
    {
        // arrange - destination has file1.txt and file2.bin already
        destinationVolume.AddEntry([], new TestEntry("file1.txt", EntryType.Item, 1, data: [42]));
        destinationVolume.AddEntry([], new TestEntry("file2.bin", EntryType.Item, 1, data: [42]));
        var entries = await GetEntriesAsync(sourceVolume, "file1.txt", "file2.bin");
        var requests = new List<OverwriteRequest>();
        var options = new CopyOptions(ConfirmOverwrite: (request, _) =>
        {
            requests.Add(request);
            return Task.FromResult(request.Name == "file1.txt" ? OverwriteAnswer.No : OverwriteAnswer.Yes);
        });

        // act
        await sourceVolume.CopyToAsync(entries, destinationVolume, options);

        // assert - file1.txt is kept and file2.bin is overwritten
        Assert.Equal(["file1.txt", "file2.bin"], requests.Select(request => request.Name));
        Assert.Equal([42], destinationVolume.GetEntry(["file1.txt"])?.Data);
        Assert.Equal(CreateData(20), destinationVolume.GetEntry(["file2.bin"])?.Data);
    }

    [Fact]
    public async Task When_OverwriteIsAnsweredAll_Then_FollowingItemsAreOverwrittenWithoutAsking()
    {
        // arrange - destination has all files of dir1 already
        destinationVolume.AddEntry([], new TestEntry("dir1", EntryType.Container));
        destinationVolume.AddEntry(["dir1"], new TestEntry("file3.txt", EntryType.Item, 1, data: [42]));
        destinationVolume.AddEntry(["dir1"], new TestEntry("file4.bin", EntryType.Item, 1, data: [42]));
        destinationVolume.AddEntry(["dir1"], new TestEntry("dir2", EntryType.Container));
        destinationVolume.AddEntry(["dir1", "dir2"], new TestEntry("file5.txt", EntryType.Item, 1, data: [42]));
        var entries = await GetEntriesAsync(sourceVolume, "dir1");
        var requests = 0;
        var options = new CopyOptions(ConfirmOverwrite: (_, _) =>
        {
            requests++;
            return Task.FromResult(OverwriteAnswer.All);
        });

        // act
        await sourceVolume.CopyToAsync(entries, destinationVolume, options);

        // assert - asked once and all files are overwritten
        Assert.Equal(1, requests);
        Assert.Equal(CreateData(30), destinationVolume.GetEntry(["dir1", "file3.txt"])?.Data);
        Assert.Equal(CreateData(40), destinationVolume.GetEntry(["dir1", "file4.bin"])?.Data);
        Assert.Equal(CreateData(50), destinationVolume.GetEntry(["dir1", "dir2", "file5.txt"])?.Data);
    }

    [Fact]
    public async Task When_Copy_Then_ModifiedDatesArePreserved()
    {
        // arrange - source entries with modified dates
        var fileDate = new DateTime(2020, 1, 2, 3, 4, 5, DateTimeKind.Utc);
        var dirDate = new DateTime(2019, 6, 7, 8, 9, 10, DateTimeKind.Utc);
        var volume = new TestVolume();
        volume.AddEntry([], new TestEntry("dir1", EntryType.Container, date: dirDate));
        volume.AddEntry(["dir1"], new TestEntry("file1.txt", EntryType.Item, 1, fileDate, [1]));
        var entries = await volume.ListEntries().ToListAsync();

        // act
        await volume.CopyToAsync(entries, destinationVolume);

        // assert
        Assert.Equal(dirDate, destinationVolume.GetEntry(["dir1"])?.ModifiedDate);
        Assert.Equal(fileDate, destinationVolume.GetEntry(["dir1", "file1.txt"])?.ModifiedDate);
    }

    [Fact]
    public async Task When_CopyBetweenLocalVolumes_Then_ModifiedDatesOfFilesAndDirectoriesArePreserved()
    {
        // arrange - source directory with a file, both with dates in the past
        var rootPath = Path.Combine(Path.GetTempPath(), $"hst-commander-{Guid.NewGuid()}");
        var sourcePath = Path.Combine(rootPath, "source");
        var destinationPath = Path.Combine(rootPath, "destination");
        Directory.CreateDirectory(Path.Combine(sourcePath, "dir1"));
        Directory.CreateDirectory(destinationPath);
        var fileDate = new DateTime(2020, 1, 2, 3, 4, 5, DateTimeKind.Utc);
        var dirDate = new DateTime(2019, 6, 7, 8, 9, 10, DateTimeKind.Utc);
        var sourceFilePath = Path.Combine(sourcePath, "dir1", "file1.txt");
        await File.WriteAllTextAsync(sourceFilePath, "file1");
        File.SetLastWriteTimeUtc(sourceFilePath, fileDate);
        Directory.SetLastWriteTimeUtc(Path.Combine(sourcePath, "dir1"), dirDate);
        try
        {
            var localSourceVolume = new LocalVolume(sourcePath);
            var entries = await GetEntriesAsync(localSourceVolume, "dir1");

            // act
            await localSourceVolume.CopyToAsync(entries, new LocalVolume(destinationPath));

            // assert
            Assert.Equal(fileDate, File.GetLastWriteTimeUtc(Path.Combine(destinationPath, "dir1", "file1.txt")));
            Assert.Equal(dirDate, Directory.GetLastWriteTimeUtc(Path.Combine(destinationPath, "dir1")));
        }
        finally
        {
            Directory.Delete(rootPath, true);
        }
    }

    [Fact]
    public void When_OverwriteMessage_Then_SizesAreShownInBytes()
    {
        // arrange
        var date = new DateTime(2026, 10, 13, 12, 30, 0);
        var request = new OverwriteRequest("file1.txt",
            new TestEntry("file1.txt", EntryType.Item, 1234567, date),
            new TestEntry("file1.txt", EntryType.Item, 512, date));

        // act
        var message = request.Message;

        // assert - dates use the same culture dependent format as the panes
        var formattedDate = date.ToString("yyyy-MM-dd HH:mm");
        Assert.Contains($"New:      1,234,567 bytes, {formattedDate}", message);
        Assert.Contains($"Existing: 512 bytes, {formattedDate}", message);
    }

    [Fact]
    public async Task When_ItemDoesNotExist_Then_OverwriteIsNotConfirmed()
    {
        // arrange
        var entries = await sourceVolume.ListEntries().ToListAsync();
        var requests = 0;
        var options = new CopyOptions(ConfirmOverwrite: (_, _) =>
        {
            requests++;
            return Task.FromResult(OverwriteAnswer.No);
        });

        // act
        await sourceVolume.CopyToAsync(entries, destinationVolume, options);

        // assert
        Assert.Equal(0, requests);
        Assert.NotNull(destinationVolume.GetEntry(["dir1", "dir2", "file5.txt"]));
    }

    [Fact]
    public async Task When_CopyFromIso9660Volume_Then_FilesAreCopied()
    {
        // arrange - create an iso9660 volume with a file
        var builder = new CDBuilder { UseJoliet = true };
        builder.AddDirectory("dir1");
        builder.AddFile(@"dir1\file1.txt", CreateData(100));
        await using var isoStream = new MemoryStream();
        builder.Build(isoStream);
        isoStream.Position = 0;
        var isoVolume = new Iso9660Volume(new CDReader(isoStream, true));
        var entries = await GetEntriesAsync(isoVolume, "dir1");

        // act
        await isoVolume.CopyToAsync(entries, destinationVolume);

        // assert
        Assert.Equal(CreateData(100), destinationVolume.GetEntry(["dir1", "file1.txt"])?.Data);
    }

    [Fact]
    public async Task When_CopyToIso9660Volume_Then_NotSupportedExceptionIsThrown()
    {
        // arrange
        var builder = new CDBuilder { UseJoliet = true };
        await using var isoStream = new MemoryStream();
        builder.Build(isoStream);
        isoStream.Position = 0;
        var isoVolume = new Iso9660Volume(new CDReader(isoStream, true));
        var entries = await GetEntriesAsync(sourceVolume, "file1.txt");

        // act & assert
        await Assert.ThrowsAsync<NotSupportedException>(() => sourceVolume.CopyToAsync(entries, isoVolume));
    }

    private static async Task<List<IEntry>> GetEntriesAsync(IVolume volume, params string[] names) =>
        (await volume.ListEntries().ToListAsync()).Where(entry => names.Contains(entry.Name)).ToList();

    private static TestEntry CreateFile(string name, int size) =>
        new(name, EntryType.Item, size, data: CreateData(size));

    private static byte[] CreateData(int size) => Enumerable.Range(0, size).Select(index => (byte)index).ToArray();

    /// <summary>Progress calling the handler on the reporting thread, unlike <see cref="Progress{T}" />.</summary>
    private sealed class SynchronousProgress<T>(Action<T> handler) : IProgress<T>
    {
        public void Report(T value) => handler(value);
    }
}
