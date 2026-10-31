using System.IO.Compression;
using Hst.Commander.Abstractions;

namespace Hst.Commander.Core.Tests;

public class GivenNavigationContextDetectingVolumeFiles
{
    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), $"hst-commander-{Guid.NewGuid()}");
        Directory.CreateDirectory(path);
        return path;
    }

    private static byte[] CreateZipWithBinaryEntry(string name, byte[] content)
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, true))
        {
            using var entryStream = archive.CreateEntry(name).Open();
            entryStream.Write(content);
        }

        return stream.ToArray();
    }

    private static async Task<IReadOnlyList<string>> DetectAsync(NavigationContext navigationContext,
        IReadOnlyList<IEntry> entries)
    {
        var detectedNames = new List<string>();
        await navigationContext.DetectVolumeFilesAsync(entries, entry => detectedNames.Add(entry.Name));
        return detectedNames;
    }

    [Fact]
    public async Task When_DetectVolumeFilesInLocalDirectory_Then_ZipFilesAreDetectedBySignature()
    {
        // arrange
        var rootPath = CreateTempDirectory();
        Directory.CreateDirectory(Path.Combine(rootPath, "dir.zip"));
        await File.WriteAllBytesAsync(Path.Combine(rootPath, "archive.zip"),
            ZipTestData.CreateZipWithDirectoriesAndFiles());
        await File.WriteAllBytesAsync(Path.Combine(rootPath, "package.nupkg"),
            ZipTestData.CreateZipWithDirectoriesAndFiles());
        await File.WriteAllTextAsync(Path.Combine(rootPath, "text.zip"), "not a zip archive");
        try
        {
            using var navigationContext = new NavigationContext(rootPath);
            var entries = await navigationContext.ListEntriesAsync();

            // act
            var detectedNames = await DetectAsync(navigationContext, entries);

            // assert
            Assert.Equal(["archive.zip", "package.nupkg"], detectedNames.Order(StringComparer.Ordinal));
            Assert.Equal(["archive.zip", "package.nupkg"], entries.Where(navigationContext.IsVolumeFile)
                .Select(entry => entry.Name).Order(StringComparer.Ordinal));
        }
        finally
        {
            Directory.Delete(rootPath, true);
        }
    }

    [Fact]
    public async Task When_DetectVolumeFilesInsideZip_Then_InnerZipIsDetected()
    {
        // arrange
        var rootPath = CreateTempDirectory();
        await File.WriteAllBytesAsync(Path.Combine(rootPath, "outer.zip"),
            CreateZipWithBinaryEntry("inner.zip", ZipTestData.CreateZipWithDirectoriesAndFiles()));
        try
        {
            using var navigationContext = new NavigationContext(rootPath);
            await navigationContext.OpenEntryAsync(
                (await navigationContext.ListEntriesAsync()).First(entry => entry.Name == "outer.zip"));
            var entries = await navigationContext.ListEntriesAsync();

            // act
            var detectedNames = await DetectAsync(navigationContext, entries);

            // assert
            Assert.Equal(["inner.zip"], detectedNames);
        }
        finally
        {
            Directory.Delete(rootPath, true);
        }
    }

    [Fact]
    public async Task When_ListEntriesAgain_Then_DetectedVolumeFilesAreCleared()
    {
        // arrange
        var rootPath = CreateTempDirectory();
        await File.WriteAllBytesAsync(Path.Combine(rootPath, "archive.zip"),
            ZipTestData.CreateZipWithDirectoriesAndFiles());
        try
        {
            using var navigationContext = new NavigationContext(rootPath);
            await DetectAsync(navigationContext, await navigationContext.ListEntriesAsync());

            // act
            var entries = await navigationContext.ListEntriesAsync();

            // assert
            Assert.DoesNotContain(entries, navigationContext.IsVolumeFile);
        }
        finally
        {
            Directory.Delete(rootPath, true);
        }
    }
}
