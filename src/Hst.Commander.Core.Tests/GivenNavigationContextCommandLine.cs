using Hst.Commander.Abstractions;

namespace Hst.Commander.Core.Tests;

public class GivenNavigationContextCommandLine
{
    private static async Task<IEntry> GetEntryAsync(NavigationContext navigationContext, string name) =>
        (await navigationContext.ListEntriesAsync()).First(entry => entry.Name == name);

    [Fact]
    public async Task When_InLocalDirectory_Then_CommandsRunThereAndEntriesAreInsertedByNameOrFullPath()
    {
        // arrange
        var directory = Directory.CreateTempSubdirectory().FullName;
        await File.WriteAllTextAsync(Path.Combine(directory, "file1.txt"), "hello");
        try
        {
            using var navigationContext = new NavigationContext(directory);
            var entry = await GetEntryAsync(navigationContext, "file1.txt");
            var parent = await GetEntryAsync(navigationContext, Paths.Parent);

            // act
            var hostDirectory = navigationContext.CurrentHostDirectory;
            var name = navigationContext.GetCommandLineText(entry, false);
            var fullPath = navigationContext.GetCommandLineText(entry, true);
            var parentText = navigationContext.GetCommandLineText(parent, false);

            // assert
            Assert.NotNull(hostDirectory);
            Assert.Equal(Path.GetFullPath(directory), Path.GetFullPath(hostDirectory));
            Assert.Equal("file1.txt", name);
            Assert.Equal(Path.Combine(hostDirectory, "file1.txt"), fullPath);
            Assert.Null(parentText);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public async Task When_InImage_Then_CommandsCantRunAndEntriesAreInsertedByName()
    {
        // arrange
        var directory = Directory.CreateTempSubdirectory().FullName;
        await File.WriteAllBytesAsync(Path.Combine(directory, "archive.zip"),
            ZipTestData.CreateZipWithDirectoriesAndFiles());
        try
        {
            using var navigationContext = new NavigationContext(directory, _ => { });
            await navigationContext.OpenEntryAsync(await GetEntryAsync(navigationContext, "archive.zip"));
            var entry = await GetEntryAsync(navigationContext, "file1.txt");

            // act
            var hostDirectory = navigationContext.CurrentHostDirectory;
            var fullPath = navigationContext.GetCommandLineText(entry, true);

            // assert
            Assert.True(navigationContext.IsInImage);
            Assert.Null(hostDirectory);
            Assert.Equal("file1.txt", fullPath);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public async Task When_OnOtherVolumeProvider_Then_CommandsCantRun()
    {
        // arrange - e.g. an FTP server
        var volumeProvider = new TestVolumeProvider(volume =>
            volume.AddEntry([], new TestEntry("dir1", EntryType.Container)));
        using var navigationContext = new NavigationContext(string.Empty);
        await navigationContext.ChangeVolumeProviderAsync(volumeProvider);

        // act
        var hostDirectory = navigationContext.CurrentHostDirectory;

        // assert
        Assert.Null(hostDirectory);
    }
}
