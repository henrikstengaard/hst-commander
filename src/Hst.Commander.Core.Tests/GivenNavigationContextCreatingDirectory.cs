using Hst.Commander.Abstractions;

namespace Hst.Commander.Core.Tests;

public class GivenNavigationContextCreatingDirectory
{
    private readonly TestVolumeProvider volumeProvider = new(volume =>
    {
        volume.AddEntry([], new TestEntry("dir1", EntryType.Container));
        volume.AddEntry([], new TestEntry("file1.txt", EntryType.Item, 5, data: "file1"u8.ToArray()));
    });

    [Fact]
    public async Task When_CreateDirectoryInLocalDirectory_Then_DirectoryIsCreatedAndSelected()
    {
        // arrange
        var rootPath = CreateTempDirectory();
        try
        {
            await File.WriteAllTextAsync(Path.Combine(rootPath, "a.txt"), "a");
            using var navigationContext = new NavigationContext(rootPath);
            await navigationContext.ListEntriesAsync();
            var previousPath = navigationContext.CurrentPath;

            // act
            await navigationContext.CreateDirectoryAsync(" new dir ");
            var entries = await navigationContext.ListEntriesAsync();
            var selectedIndex = navigationContext.GetSelectedIndex(entries, previousPath, 0);

            // assert
            Assert.True(Directory.Exists(Path.Combine(rootPath, "new dir")));
            Assert.Equal("new dir", entries[selectedIndex].Name);
            Assert.Equal(0, navigationContext.GetSelectedIndex(entries, previousPath, 0));
        }
        finally
        {
            Directory.Delete(rootPath, true);
        }
    }

    [Fact]
    public async Task When_CreateDirectoryInDriveList_Then_NotSupportedExceptionIsThrown()
    {
        // arrange
        using var navigationContext = new NavigationContext(string.Empty);

        // act & assert
        Assert.NotNull(navigationContext.CreateDirectoryNotSupportedMessage);
        await Assert.ThrowsAsync<NotSupportedException>(() => navigationContext.CreateDirectoryAsync("dir1"));
    }

    [Fact]
    public async Task When_CreateDirectoryInVolume_Then_ContainerIsCreated()
    {
        // arrange
        using var navigationContext = new NavigationContext(string.Empty);
        await navigationContext.ChangeVolumeProviderAsync(volumeProvider);

        // act
        await navigationContext.CreateDirectoryAsync("dir2");
        var entries = await navigationContext.ListEntriesAsync();

        // assert
        Assert.Null(navigationContext.CreateDirectoryNotSupportedMessage);
        Assert.Contains(entries, entry => entry.Name == "dir2" && entry.Type == EntryType.Container);
    }

    [Theory]
    [InlineData("dir1")]
    [InlineData("file1.txt")]
    public async Task When_CreateDirectoryWithExistingName_Then_IOExceptionIsThrown(string name)
    {
        // arrange
        using var navigationContext = new NavigationContext(string.Empty);
        await navigationContext.ChangeVolumeProviderAsync(volumeProvider);

        // act & assert
        await Assert.ThrowsAsync<IOException>(() => navigationContext.CreateDirectoryAsync(name));
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData(".")]
    [InlineData("..")]
    [InlineData("a/b")]
    [InlineData("a\\b")]
    public async Task When_CreateDirectoryWithInvalidName_Then_IOExceptionIsThrown(string name)
    {
        // arrange
        using var navigationContext = new NavigationContext(string.Empty);
        await navigationContext.ChangeVolumeProviderAsync(volumeProvider);

        // act & assert
        await Assert.ThrowsAsync<IOException>(() => navigationContext.CreateDirectoryAsync(name));
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), $"hst-commander-{Guid.NewGuid()}");
        Directory.CreateDirectory(path);
        return path;
    }
}
