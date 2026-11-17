using Hst.Commander.Abstractions;

namespace Hst.Commander.Core.Tests;

public class GivenNavigationContextStartingFiles
{
    private const string HostPathRoot = @"\\server\share";

    private static async Task<NavigationContext> CreateContextAsync(List<string> startedFiles, string? hostPathRoot)
    {
        var volumeProvider = new TestVolumeProvider(volume =>
        {
            volume.AddEntry([], new TestEntry("dir1", EntryType.Container));
            volume.AddEntry(["dir1"], new TestEntry("file1.txt", EntryType.Item, 5, data: "hello"u8.ToArray()));
            volume.AddEntry(["dir1"], new TestEntry("tool.exe", EntryType.Item, 4, data: "tool"u8.ToArray()));
            var zipData = ZipTestData.CreateZipWithDirectoriesAndFiles();
            volume.AddEntry(["dir1"], new TestEntry("archive.zip", EntryType.Item, zipData.Length, data: zipData));
        }, hostPathRoot);
        var navigationContext = new NavigationContext(string.Empty, startedFiles.Add);
        await navigationContext.ChangeVolumeProviderAsync(volumeProvider);
        await OpenAsync(navigationContext, "dir1");
        return navigationContext;
    }

    private static async Task OpenAsync(NavigationContext navigationContext, string name) =>
        await navigationContext.OpenEntryAsync(await GetEntryAsync(navigationContext, name));

    private static async Task<IEntry> GetEntryAsync(NavigationContext navigationContext, string name) =>
        (await navigationContext.ListEntriesAsync()).First(entry => entry.Name == name);

    [Fact]
    public async Task When_OpeningFileWithHostPath_Then_FileIsStartedWithItsHostPath()
    {
        // arrange - e.g. a file in an SMB share on Windows
        var startedFiles = new List<string>();
        using var navigationContext = await CreateContextAsync(startedFiles, HostPathRoot);

        // act
        await OpenAsync(navigationContext, "file1.txt");

        // assert
        Assert.Equal([@"\\server\share\dir1\file1.txt"], startedFiles);
    }

    [Fact]
    public async Task When_OpeningFileWithoutHostPath_Then_FileIsNotStarted()
    {
        // arrange - e.g. a file on an FTP server
        var startedFiles = new List<string>();
        using var navigationContext = await CreateContextAsync(startedFiles, null);

        // act
        await OpenAsync(navigationContext, "file1.txt");

        // assert
        Assert.Empty(startedFiles);
    }

    [Fact]
    public async Task When_OpeningZipFileWithHostPath_Then_ZipIsBrowsed()
    {
        // arrange
        var startedFiles = new List<string>();
        using var navigationContext = await CreateContextAsync(startedFiles, HostPathRoot);

        // act
        await OpenAsync(navigationContext, "archive.zip");

        // assert
        Assert.Empty(startedFiles);
        Assert.True(navigationContext.IsInImage);
    }

    [Fact]
    public async Task When_OpeningZipFileWithHostPathSetToStartWithHost_Then_ZipIsStarted()
    {
        // arrange
        var startedFiles = new List<string>();
        using var navigationContext = await CreateContextAsync(startedFiles, HostPathRoot);
        navigationContext.EnterFileAction = EnterFileAction.StartWithHost;

        // act
        await OpenAsync(navigationContext, "archive.zip");

        // assert
        Assert.False(navigationContext.IsInImage);
        Assert.Equal([@"\\server\share\dir1\archive.zip"], startedFiles);
    }

    [Fact]
    public async Task When_GettingHostPaths_Then_OnlyFilesHaveThem()
    {
        // arrange
        var startedFiles = new List<string>();
        using var navigationContext = await CreateContextAsync(startedFiles, HostPathRoot);
        var entries = await navigationContext.ListEntriesAsync();

        // act
        var hostPaths = entries.ToDictionary(entry => entry.Name, navigationContext.GetHostPath);

        // assert
        Assert.Null(hostPaths[Paths.Parent]);
        Assert.Equal(@"\\server\share\dir1\file1.txt", hostPaths["file1.txt"]);
    }

    [Fact]
    public async Task When_ListingProgramsWithHostPaths_Then_TheyAreExecutableOnWindows()
    {
        // arrange
        var startedFiles = new List<string>();
        using var withHostPaths = await CreateContextAsync(startedFiles, HostPathRoot);
        using var withoutHostPaths = await CreateContextAsync(startedFiles, null);

        // act
        var isExecutable = withHostPaths.IsExecutable(await GetEntryAsync(withHostPaths, "tool.exe"));
        var isExecutableWithoutHostPath =
            withoutHostPaths.IsExecutable(await GetEntryAsync(withoutHostPaths, "tool.exe"));

        // assert - by extension on Windows, by the (here missing) execute permission elsewhere
        Assert.Equal(OperatingSystem.IsWindows(), isExecutable);
        Assert.False(isExecutableWithoutHostPath);
    }
}
