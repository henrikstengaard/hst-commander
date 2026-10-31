using DiscUtils.Iso9660;
using Hst.Commander.Core.Volumes;
using Hst.Commander.Abstractions;

namespace Hst.Commander.Core.Tests;

public class GivenNavigationContextOpeningImages
{
    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), $"hst-commander-{Guid.NewGuid()}");
        Directory.CreateDirectory(path);
        return path;
    }

    private static async Task OpenAsync(NavigationContext navigationContext, string name) =>
        await navigationContext.OpenEntryAsync(
            (await navigationContext.ListEntriesAsync()).First(entry => entry.Name == name));

    private static async Task<IReadOnlyList<string>> ListNamesAsync(NavigationContext navigationContext) =>
        (await navigationContext.ListEntriesAsync()).Select(entry => entry.Name).ToList();

    [Fact]
    public async Task When_OpenZipFile_Then_ZipIsDetectedBySignatureAndBrowsed()
    {
        // arrange
        var rootPath = CreateTempDirectory();
        await File.WriteAllBytesAsync(Path.Combine(rootPath, "archive.zip"),
            ZipTestData.CreateZipWithDirectoriesAndFiles());
        var startedFiles = new List<string>();
        try
        {
            using var navigationContext = new NavigationContext(rootPath, startedFiles.Add);

            // act
            await OpenAsync(navigationContext, "archive.zip");
            var rootNames = await ListNamesAsync(navigationContext);
            await OpenAsync(navigationContext, "dir1");
            var dirNames = await ListNamesAsync(navigationContext);

            // assert
            Assert.Empty(startedFiles);
            Assert.True(navigationContext.IsInImage);
            Assert.Equal([Paths.Parent, "dir1", "dir2", "file1.txt"], rootNames);
            Assert.Equal([Paths.Parent, "file2.txt"], dirNames);
            Assert.Equal(Path.Combine(rootPath, "archive.zip", "dir1"), navigationContext.CurrentPath);
            Assert.NotNull(navigationContext.CreateDirectoryNotSupportedMessage);
        }
        finally
        {
            Directory.Delete(rootPath, true);
        }
    }

    [Theory]
    [InlineData("archive.lha", "dirs-files.lha")]
    [InlineData("archive.lzx", "dirs-files.lzx")]
    public async Task When_OpenLhaOrLzxFile_Then_ArchiveIsDetectedBySignatureAndBrowsed(string name,
        string testDataFileName)
    {
        // arrange
        var rootPath = CreateTempDirectory();
        File.Copy(ArchiveTestData.GetPath(testDataFileName), Path.Combine(rootPath, name));
        var startedFiles = new List<string>();
        try
        {
            using var navigationContext = new NavigationContext(rootPath, startedFiles.Add);

            // act
            await OpenAsync(navigationContext, name);
            var rootNames = await ListNamesAsync(navigationContext);
            await OpenAsync(navigationContext, "dir1");
            var dirNames = await ListNamesAsync(navigationContext);

            // assert
            Assert.Empty(startedFiles);
            Assert.True(navigationContext.IsInImage);
            Assert.Equal([Paths.Parent, "dir1", "dir2"], rootNames);
            Assert.Equal([Paths.Parent, "dir3", "file1.txt"], dirNames);
            Assert.Equal(Path.Combine(rootPath, name, "dir1"), navigationContext.CurrentPath);
        }
        finally
        {
            Directory.Delete(rootPath, true);
        }
    }

    [Fact]
    public async Task When_OpenZipFileWithOtherExtension_Then_ZipIsDetectedBySignatureAndBrowsed()
    {
        // arrange - e.g. a .nupkg package is a zip archive
        var rootPath = CreateTempDirectory();
        await File.WriteAllBytesAsync(Path.Combine(rootPath, "package.nupkg"),
            ZipTestData.CreateZipWithDirectoriesAndFiles());
        var startedFiles = new List<string>();
        try
        {
            using var navigationContext = new NavigationContext(rootPath, startedFiles.Add);

            // act
            await OpenAsync(navigationContext, "package.nupkg");
            var names = await ListNamesAsync(navigationContext);

            // assert
            Assert.Empty(startedFiles);
            Assert.True(navigationContext.IsInImage);
            Assert.Equal([Paths.Parent, "dir1", "dir2", "file1.txt"], names);
        }
        finally
        {
            Directory.Delete(rootPath, true);
        }
    }

    [Fact]
    public async Task When_OpenZipFileSetToStartWithHost_Then_FileIsStarted()
    {
        // arrange
        var rootPath = CreateTempDirectory();
        await File.WriteAllBytesAsync(Path.Combine(rootPath, "archive.zip"),
            ZipTestData.CreateZipWithDirectoriesAndFiles());
        var startedFiles = new List<string>();
        try
        {
            using var navigationContext = new NavigationContext(rootPath, startedFiles.Add)
            {
                EnterFileAction = EnterFileAction.StartWithHost
            };

            // act
            await OpenAsync(navigationContext, "archive.zip");

            // assert
            Assert.False(navigationContext.IsInImage);
            Assert.Equal([Path.Combine(rootPath, "archive.zip")], startedFiles);
        }
        finally
        {
            Directory.Delete(rootPath, true);
        }
    }

    [Fact]
    public async Task When_OpenZipFileSetToStartWithHostAsVolume_Then_ZipIsBrowsed()
    {
        // arrange
        var rootPath = CreateTempDirectory();
        await File.WriteAllBytesAsync(Path.Combine(rootPath, "archive.zip"),
            ZipTestData.CreateZipWithDirectoriesAndFiles());
        var startedFiles = new List<string>();
        try
        {
            using var navigationContext = new NavigationContext(rootPath, startedFiles.Add)
            {
                EnterFileAction = EnterFileAction.StartWithHost
            };

            // act
            await navigationContext.OpenAsVolumeAsync(
                (await navigationContext.ListEntriesAsync()).First(entry => entry.Name == "archive.zip"));

            // assert
            Assert.Empty(startedFiles);
            Assert.True(navigationContext.IsInImage);
        }
        finally
        {
            Directory.Delete(rootPath, true);
        }
    }

    [Fact]
    public async Task When_OpenZipInsideZipSetToStartWithHost_Then_InnerZipIsBrowsed()
    {
        // arrange - files inside images can't be started, so they are still opened as images
        var rootPath = CreateTempDirectory();
        await File.WriteAllBytesAsync(Path.Combine(rootPath, "outer.zip"),
            CreateZipWithBinaryEntry("inner.zip", ZipTestData.CreateZipWithDirectoriesAndFiles()));
        var startedFiles = new List<string>();
        try
        {
            using var navigationContext = new NavigationContext(rootPath, startedFiles.Add)
            {
                EnterFileAction = EnterFileAction.StartWithHost
            };
            await navigationContext.OpenAsVolumeAsync(
                (await navigationContext.ListEntriesAsync()).First(entry => entry.Name == "outer.zip"));

            // act
            await OpenAsync(navigationContext, "inner.zip");

            // assert
            Assert.Empty(startedFiles);
            Assert.Equal(Path.Combine(rootPath, "outer.zip", "inner.zip"), navigationContext.CurrentPath);
        }
        finally
        {
            Directory.Delete(rootPath, true);
        }
    }

    [Fact]
    public async Task When_OpenZipFileWithOtherExtensionAsVolume_Then_ZipIsDetectedBySignatureAndBrowsed()
    {
        // arrange
        var rootPath = CreateTempDirectory();
        await File.WriteAllBytesAsync(Path.Combine(rootPath, "archive.dat"),
            ZipTestData.CreateZipWithDirectoriesAndFiles());
        var startedFiles = new List<string>();
        try
        {
            using var navigationContext = new NavigationContext(rootPath, startedFiles.Add);

            // act
            await navigationContext.OpenAsVolumeAsync(
                (await navigationContext.ListEntriesAsync()).First(entry => entry.Name == "archive.dat"));
            var rootNames = await ListNamesAsync(navigationContext);
            await OpenAsync(navigationContext, "dir1");
            var dirNames = await ListNamesAsync(navigationContext);

            // assert
            Assert.Empty(startedFiles);
            Assert.True(navigationContext.IsInImage);
            Assert.Equal([Paths.Parent, "dir1", "dir2", "file1.txt"], rootNames);
            Assert.Equal([Paths.Parent, "file2.txt"], dirNames);
            Assert.Equal(Path.Combine(rootPath, "archive.dat", "dir1"), navigationContext.CurrentPath);
            Assert.NotNull(navigationContext.CreateDirectoryNotSupportedMessage);
        }
        finally
        {
            Directory.Delete(rootPath, true);
        }
    }

    [Fact]
    public async Task When_GoRootInZip_Then_ZipRootAndThenDriveRootIsListed()
    {
        // arrange
        var rootPath = CreateTempDirectory();
        var zipPath = Path.Combine(rootPath, "archive.zip");
        await File.WriteAllBytesAsync(zipPath, ZipTestData.CreateZipWithDirectoriesAndFiles());
        var driveRoot = Path.GetPathRoot(Path.GetFullPath(rootPath))!;
        try
        {
            using var navigationContext = new NavigationContext(rootPath);
            await OpenAsync(navigationContext, "archive.zip");
            await OpenAsync(navigationContext, "dir1");

            // act
            await navigationContext.GoRootAsync();
            var zipRootPath = navigationContext.CurrentPath;
            var zipPreviousEntryName = navigationContext.PreviousEntryName;
            await navigationContext.GoRootAsync();

            // assert - the file is closed, so it can be deleted
            Assert.Equal(zipPath, zipRootPath);
            Assert.Null(zipPreviousEntryName);
            Assert.False(navigationContext.IsInImage);
            Assert.Equal(driveRoot, navigationContext.CurrentPath);
            File.Delete(zipPath);
        }
        finally
        {
            Directory.Delete(rootPath, true);
        }
    }

    [Fact]
    public async Task When_GoParentFromZipRoot_Then_ZipIsClosedAndSelected()
    {
        // arrange
        var rootPath = CreateTempDirectory();
        var zipPath = Path.Combine(rootPath, "archive.zip");
        await File.WriteAllBytesAsync(zipPath, ZipTestData.CreateZipWithDirectoriesAndFiles());
        try
        {
            using var navigationContext = new NavigationContext(rootPath);
            await OpenAsync(navigationContext, "archive.zip");

            // act
            await navigationContext.GoParentAsync();

            // assert - the file is closed, so it can be deleted
            Assert.False(navigationContext.IsInImage);
            Assert.Equal(rootPath, navigationContext.CurrentPath);
            Assert.Equal("archive.zip", navigationContext.PreviousEntryName);
            File.Delete(zipPath);
        }
        finally
        {
            Directory.Delete(rootPath, true);
        }
    }

    [Fact]
    public async Task When_OpenFileNotDetected_Then_FileIsStarted()
    {
        // arrange - a text file named like a zip archive is not a zip archive
        var rootPath = CreateTempDirectory();
        await File.WriteAllTextAsync(Path.Combine(rootPath, "text.zip"), "not a zip archive");
        var startedFiles = new List<string>();
        try
        {
            using var navigationContext = new NavigationContext(rootPath, startedFiles.Add);

            // act
            await OpenAsync(navigationContext, "text.zip");

            // assert
            Assert.False(navigationContext.IsInImage);
            Assert.Equal([Path.Combine(rootPath, "text.zip")], startedFiles);
        }
        finally
        {
            Directory.Delete(rootPath, true);
        }
    }

    [Fact]
    public async Task When_OpenFileNotDetectedAsVolume_Then_IOExceptionIsThrownAndFileIsNotStarted()
    {
        // arrange
        var rootPath = CreateTempDirectory();
        await File.WriteAllTextAsync(Path.Combine(rootPath, "text.txt"), "text");
        var startedFiles = new List<string>();
        try
        {
            using var navigationContext = new NavigationContext(rootPath, startedFiles.Add);
            var entry = (await navigationContext.ListEntriesAsync()).First(entry => entry.Name == "text.txt");

            // act & assert
            await Assert.ThrowsAsync<IOException>(() => navigationContext.OpenAsVolumeAsync(entry));
            Assert.False(navigationContext.IsInImage);
            Assert.Empty(startedFiles);
        }
        finally
        {
            Directory.Delete(rootPath, true);
        }
    }

    [Fact]
    public async Task When_CopyFromZipInsideIsoImage_Then_FilesAreCopiedToLocalDirectory()
    {
        // arrange - an iso image with a zip archive in a directory
        var rootPath = CreateTempDirectory();
        var destinationPath = Path.Combine(rootPath, "destination");
        Directory.CreateDirectory(destinationPath);
        var builder = new CDBuilder { UseJoliet = true };
        builder.AddDirectory("dir1");
        builder.AddFile(@"dir1\archive.zip", ZipTestData.CreateZipWithDirectoriesAndFiles());
        builder.Build(Path.Combine(rootPath, "image.iso"));
        try
        {
            using var source = new NavigationContext(rootPath);
            using var destination = new NavigationContext(destinationPath);
            await OpenAsync(source, "image.iso");
            await OpenAsync(source, "dir1");
            await OpenAsync(source, "archive.zip");
            await OpenAsync(source, "dir1");
            var entries = source.GetOperationEntries(
                (await source.ListEntriesAsync()).First(entry => entry.Name == "file2.txt"));

            // act
            await source.CopyAsync(entries, destination, destination.CurrentPath, EntryFilter.All);

            // assert
            Assert.Equal(Path.Combine(rootPath, "image.iso", "dir1", "archive.zip", "dir1"), source.CurrentPath);
            Assert.Equal("zip-file2", await File.ReadAllTextAsync(Path.Combine(destinationPath, "file2.txt")));
        }
        finally
        {
            Directory.Delete(rootPath, true);
        }
    }

    [Fact]
    public async Task When_CopyFromZipInsideZip_Then_FilesAreCopiedToLocalDirectory()
    {
        // arrange - files inside a zip archive aren't seekable, so the inner archive is read from a temporary file
        var rootPath = CreateTempDirectory();
        var destinationPath = Path.Combine(rootPath, "destination");
        Directory.CreateDirectory(destinationPath);
        var innerZip = ZipTestData.CreateZipWithDirectoriesAndFiles();
        await File.WriteAllBytesAsync(Path.Combine(rootPath, "outer.zip"), CreateZipWithBinaryEntry("inner.zip", innerZip));
        try
        {
            using var source = new NavigationContext(rootPath);
            using var destination = new NavigationContext(destinationPath);
            await OpenAsync(source, "outer.zip");
            await OpenAsync(source, "inner.zip");
            var entries = source.GetOperationEntries(
                (await source.ListEntriesAsync()).First(entry => entry.Name == "file1.txt"));

            // act
            await source.CopyAsync(entries, destination, destination.CurrentPath, EntryFilter.All);

            // assert
            Assert.Equal("zip-file1", await File.ReadAllTextAsync(Path.Combine(destinationPath, "file1.txt")));
        }
        finally
        {
            Directory.Delete(rootPath, true);
        }
    }

    private static byte[] CreateZipWithBinaryEntry(string name, byte[] data)
    {
        using var stream = new MemoryStream();
        using (var zipArchive = new System.IO.Compression.ZipArchive(stream,
                   System.IO.Compression.ZipArchiveMode.Create, true))
        {
            using var entryStream = zipArchive.CreateEntry(name).Open();
            entryStream.Write(data);
        }

        return stream.ToArray();
    }

    [Fact]
    public async Task When_ChangeLocationIntoZipInsideIsoImage_Then_DirectoryInZipIsListed()
    {
        // arrange
        var rootPath = CreateTempDirectory();
        var isoPath = Path.Combine(rootPath, "image.iso");
        var builder = new CDBuilder { UseJoliet = true };
        builder.AddFile("archive.zip", ZipTestData.CreateZipWithDirectoriesAndFiles());
        builder.Build(isoPath);
        try
        {
            using var navigationContext = new NavigationContext(string.Empty);

            // act
            await navigationContext.ChangeLocationAsync(Path.Combine(isoPath, "archive.zip", "dir1"));
            var names = await ListNamesAsync(navigationContext);

            // assert
            Assert.Equal(Path.Combine(isoPath, "archive.zip", "dir1"), navigationContext.CurrentPath);
            Assert.Equal([Paths.Parent, "file2.txt"], names);
        }
        finally
        {
            Directory.Delete(rootPath, true);
        }
    }

    [Fact]
    public async Task When_ChangeLocationIntoFileNotDetected_Then_LocationIsUnchanged()
    {
        // arrange
        var rootPath = CreateTempDirectory();
        await File.WriteAllTextAsync(Path.Combine(rootPath, "text.txt"), "text");
        try
        {
            using var navigationContext = new NavigationContext(rootPath);

            // act & assert
            await Assert.ThrowsAsync<IOException>(() =>
                navigationContext.ChangeLocationAsync(Path.Combine(rootPath, "text.txt", "dir1")));
            Assert.Equal(rootPath, navigationContext.CurrentPath);
            Assert.False(navigationContext.IsInImage);
        }
        finally
        {
            Directory.Delete(rootPath, true);
        }
    }

    [Fact]
    public async Task When_OpenFileDetectedByPluginFileVolumeProvider_Then_VolumeOfProviderIsBrowsed()
    {
        // arrange - a plugin file volume provider detecting files starting with "TEST"
        var rootPath = CreateTempDirectory();
        await File.WriteAllTextAsync(Path.Combine(rootPath, "volume.bin"), "TEST");
        try
        {
            using var navigationContext = new NavigationContext(rootPath, _ => { },
                [new TestFileVolumeProvider()]);

            // act
            await OpenAsync(navigationContext, "volume.bin");
            var names = await ListNamesAsync(navigationContext);

            // assert
            Assert.Equal([Paths.Parent, "test-file.txt"], names);
        }
        finally
        {
            Directory.Delete(rootPath, true);
        }
    }

    private sealed class TestFileVolumeProvider : IFileVolumeProvider
    {
        public string Name => "Test";

        public string Description => "Test files";

        public async Task<bool> IsMatch(string name, Stream stream, CancellationToken cancellationToken = default)
        {
            var signature = new byte[4];
            return await stream.ReadAtLeastAsync(signature, 4, false, cancellationToken) == 4 &&
                   signature.AsSpan().SequenceEqual("TEST"u8);
        }

        public Task<IVolume> CreateVolume(Stream stream, CancellationToken cancellationToken = default)
        {
            var volume = new TestVolume();
            volume.AddEntry([], new TestEntry("test-file.txt", EntryType.Item));
            return Task.FromResult<IVolume>(volume);
        }
    }

    [Fact]
    public async Task When_OpenZipInsideZipLargerThanTemporaryCopyLimit_Then_IOExceptionIsThrown()
    {
        // arrange - a file inside a zip archive isn't seekable, so it's copied to a temporary file to open it
        var rootPath = CreateTempDirectory();
        await File.WriteAllBytesAsync(Path.Combine(rootPath, "outer.zip"),
            CreateZipWithBinaryEntry("inner.zip", ZipTestData.CreateZipWithDirectoriesAndFiles()));
        try
        {
            using var navigationContext = new NavigationContext(rootPath) { TemporaryCopyLimit = 100 };
            await OpenAsync(navigationContext, "outer.zip");
            var entry = (await navigationContext.ListEntriesAsync()).First(entry => entry.Name == "inner.zip");

            // act
            var exception = await Assert.ThrowsAsync<IOException>(() => navigationContext.OpenEntryAsync(entry));

            // assert
            Assert.Equal(NavigationContext.GetTemporaryCopyLimitMessage("inner.zip", 100), exception.Message);
            Assert.Equal(Path.Combine(rootPath, "outer.zip"), navigationContext.CurrentPath);
        }
        finally
        {
            Directory.Delete(rootPath, true);
        }
    }

    [Fact]
    public async Task When_OpenZipInsideZipWithoutTemporaryCopyLimit_Then_InnerZipIsBrowsed()
    {
        // arrange
        var rootPath = CreateTempDirectory();
        await File.WriteAllBytesAsync(Path.Combine(rootPath, "outer.zip"),
            CreateZipWithBinaryEntry("inner.zip", ZipTestData.CreateZipWithDirectoriesAndFiles()));
        try
        {
            using var navigationContext = new NavigationContext(rootPath) { TemporaryCopyLimit = 0 };
            await OpenAsync(navigationContext, "outer.zip");

            // act
            await OpenAsync(navigationContext, "inner.zip");

            // assert
            Assert.Equal(Path.Combine(rootPath, "outer.zip", "inner.zip"), navigationContext.CurrentPath);
        }
        finally
        {
            Directory.Delete(rootPath, true);
        }
    }
}
