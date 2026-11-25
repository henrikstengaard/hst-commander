using Hst.Commander.Abstractions;
using Hst.Commander.Core.Volumes;
using Hst.Commander.Plugins.Archives;
using Hst.Commander.Plugins.Zip;

namespace Hst.Commander.Core.Tests;

public class GivenSplitFiles : IDisposable
{
    private readonly string _rootPath = Path.Combine(Path.GetTempPath(), $"hst-commander-{Guid.NewGuid()}");

    public GivenSplitFiles() => Directory.CreateDirectory(_rootPath);

    public void Dispose() => Directory.Delete(_rootPath, true);

    /// <summary>Writes a file split in parts of a size, like 7-Zip's "split to volumes": name.001, name.002, ...</summary>
    private void WriteParts(string name, byte[] data, int partSize)
    {
        for (var (offset, number) = (0, 1); offset < data.Length; offset += partSize, number++)
        {
            File.WriteAllBytes(Path.Combine(_rootPath, $"{name}.{number:D3}"),
                data[offset..Math.Min(data.Length, offset + partSize)]);
        }
    }

    private static async Task OpenAsync(NavigationContext navigationContext, string name) =>
        await navigationContext.OpenEntryAsync(
            (await navigationContext.ListEntriesAsync()).First(entry => entry.Name == name));

    private static async Task<IReadOnlyList<string>> ListNamesAsync(NavigationContext navigationContext) =>
        (await navigationContext.ListEntriesAsync()).Select(entry => entry.Name).ToList();

    [Fact]
    public async Task When_OpenFirstPartOfSplit7zArchive_Then_ArchiveOfAllPartsIsBrowsed()
    {
        // arrange - a 7z archive has its list of files at the end, so it's in the last part
        var data = ArchiveTestData.Read("dirs-files.7z");
        await File.WriteAllBytesAsync(Path.Combine(_rootPath, "whole.7z"), data);
        WriteParts("archive.7z", data, data.Length / 3 + 1);
        var startedFiles = new List<string>();
        using var navigationContext = new NavigationContext(_rootPath, startedFiles.Add);
        await OpenAsync(navigationContext, "whole.7z");
        var expectedNames = await ListNamesAsync(navigationContext);
        await navigationContext.GoParentAsync();

        // act
        await OpenAsync(navigationContext, "archive.7z.001");
        var names = await ListNamesAsync(navigationContext);

        // assert
        Assert.Empty(startedFiles);
        Assert.True(navigationContext.IsInImage);
        Assert.Equal(expectedNames, names);
    }

    [Fact]
    public async Task When_OpenFirstPartWithoutOtherParts_Then_IncompleteArchiveIsReported()
    {
        // arrange - only the first part of a split 7z archive
        var data = ArchiveTestData.Read("dirs-files.7z");
        await File.WriteAllBytesAsync(Path.Combine(_rootPath, "archive.7z.001"), data[..(data.Length / 2)]);
        using var navigationContext = new NavigationContext(_rootPath, _ => { });

        // act
        var exception = await Assert.ThrowsAsync<InvalidDataException>(() =>
            OpenAsync(navigationContext, "archive.7z.001"));

        // assert
        Assert.Equal(SevenZipFileVolumeProvider.IncompleteMessage, exception.Message);
    }

    [Fact]
    public async Task When_DetectVolumeFiles_Then_FirstPartOfSplitArchiveIsAVolume()
    {
        // arrange
        WriteParts("archive.zip", ZipTestData.CreateZipWithDirectoriesAndFiles(), 100);
        using var navigationContext = new NavigationContext(_rootPath, _ => { });
        var entries = await navigationContext.ListEntriesAsync();
        var detected = new List<string>();

        // act
        await navigationContext.DetectVolumeFilesAsync(entries, entry => detected.Add(entry.Name));

        // assert - the zip's central directory is in the last part
        Assert.Contains("archive.zip.001", detected);
    }

    [Fact]
    public async Task When_ReadSplitLocalFile_Then_PartsAreReadAsJoinedFileAndSeekable()
    {
        // arrange
        var data = Enumerable.Range(0, 1000).Select(value => (byte)value).ToArray();
        WriteParts("data.bin", data, 300);

        // act
        await using var stream = SplitFile.OpenLocal(Path.Combine(_rootPath, "data.bin.001"));
        using var memoryStream = new MemoryStream();
        await stream!.CopyToAsync(memoryStream);
        stream.Position = 290;
        var acrossParts = new byte[20];
        stream.ReadExactly(acrossParts);

        // assert
        Assert.Equal(data.Length, stream.Length);
        Assert.Equal(data, memoryStream.ToArray());
        Assert.Equal(data[290..310], acrossParts);
    }

    [Fact]
    public async Task When_OpenSplitFileFromVolume_Then_PartsAreReadAsJoinedFile()
    {
        // arrange
        var data = Enumerable.Range(0, 1000).Select(value => (byte)(value * 7)).ToArray();
        WriteParts("data.bin", data, 400);
        var volume = new LocalVolume(_rootPath);

        // act
        await using var stream = await SplitFile.OpenAsync(volume, "data.bin.001", CancellationToken.None);
        using var memoryStream = new MemoryStream();
        await stream!.CopyToAsync(memoryStream);

        // assert
        Assert.Equal(data, memoryStream.ToArray());
    }

    [Fact]
    public async Task When_FileHasNoSecondPart_Then_ItIsNotASplitFile()
    {
        // arrange
        await File.WriteAllBytesAsync(Path.Combine(_rootPath, "single.001"), [1, 2, 3]);
        var volume = new LocalVolume(_rootPath);

        // act
        var local = SplitFile.OpenLocal(Path.Combine(_rootPath, "single.001"));
        var fromVolume = await SplitFile.OpenAsync(volume, "single.001", CancellationToken.None);

        // assert
        Assert.Null(local);
        Assert.Null(fromVolume);
    }

    [Fact]
    public async Task When_OpeningIsCancelled_Then_ReadingEntriesStops()
    {
        // arrange
        var zip = ZipTestData.CreateZipWithDirectoriesAndFiles();
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        // act and assert - the archive volume reports its entries, which throws once cancelled
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => VolumeOpening.RunAsync(
            () => new ZipFileVolumeProvider().CreateVolume(new MemoryStream(zip)), null, cancellation.Token));
    }

    [Fact]
    public async Task When_VolumeIsCreatedOutsideOpening_Then_EntriesAreNotCounted()
    {
        // act - e.g. opened again for copying, where nothing is cancelled
        var volume = await new ZipFileVolumeProvider().CreateVolume(
            new MemoryStream(ZipTestData.CreateZipWithDirectoriesAndFiles()));

        // assert
        Assert.NotEmpty(await volume.ListEntries().ToListAsync());
    }
}
