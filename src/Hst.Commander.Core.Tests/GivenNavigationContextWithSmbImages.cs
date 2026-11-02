using Hst.Commander.Abstractions;
using Hst.Commander.Plugins.Smb;

namespace Hst.Commander.Core.Tests;

/// <summary>Writable images (Amiga disk files) on SMB shares, opened in panes showing a share.</summary>
public class GivenNavigationContextWithSmbImages : IDisposable
{
    private readonly string _rootPath =
        Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), $"hst-commander-{Guid.NewGuid()}")).FullName;

    private readonly TestSmbServer _server = new();
    private readonly SmbVolumeProvider _volumeProvider;
    private readonly string _sharePath;

    public GivenNavigationContextWithSmbImages()
    {
        _sharePath = Directory.CreateDirectory(Path.Combine(_rootPath, "share")).FullName;
        _server.AddShare("share", _sharePath);
        _volumeProvider = new SmbVolumeProvider(_server);
    }

    public void Dispose()
    {
        Directory.Delete(_rootPath, true);
        GC.SuppressFinalize(this);
    }

    private async Task<NavigationContext> OpenShareAsync()
    {
        var navigationContext = new NavigationContext(_rootPath);
        await navigationContext.ChangeVolumeProviderAsync(await _volumeProvider.Connect(@"\\server\share", null));
        return navigationContext;
    }

    private static async Task OpenAsync(NavigationContext navigationContext, string name) =>
        await navigationContext.OpenEntryAsync(
            (await navigationContext.ListEntriesAsync()).First(entry => entry.Name == name));

    private static async Task<IReadOnlyList<string>> ListNamesAsync(NavigationContext navigationContext) =>
        (await navigationContext.ListEntriesAsync()).Select(entry => entry.Name).ToList();

    [Fact]
    public async Task When_AdfOnShareIsChanged_Then_ChangesAreWrittenToTheFileOnTheShare()
    {
        // arrange
        var adfPath = Path.Combine(_sharePath, "disk.adf");
        await AmigaTestData.CreateAdf(adfPath);

        using (var navigationContext = await OpenShareAsync())
        {
            // act
            await OpenAsync(navigationContext, "disk.adf");
            Assert.Null(navigationContext.CreateDirectoryNotSupportedMessage);
            await navigationContext.CreateDirectoryAsync("dir1");
            var entries = await navigationContext.ListEntriesAsync();
            await navigationContext.RenameAsync(entries.First(entry => entry.Name == "dir1"), "Dir2");

            // assert
            Assert.True(navigationContext.IsInImage);
            Assert.Equal([Paths.Parent, "Dir2"], await ListNamesAsync(navigationContext));
        }

        // the image and its connection are closed with the last pane showing it
        Assert.All(_server.Connections, connection => Assert.True(connection.IsDisposed));
        Assert.Equal(["Dir2"], await AmigaTestData.ListAdfNames(adfPath));
    }

    [Fact]
    public async Task When_AdfOnShareIsOpenInBothPanes_Then_ChangesInOnePaneAreSeenInTheOther()
    {
        // arrange
        await AmigaTestData.CreateAdf(Path.Combine(_sharePath, "disk.adf"));
        using var leftContext = await OpenShareAsync();
        using var rightContext = await OpenShareAsync();
        await OpenAsync(leftContext, "disk.adf");
        await OpenAsync(rightContext, "disk.adf");

        // act
        await leftContext.CreateDirectoryAsync("dir1");

        // assert - one image is shared, as two volumes writing it would damage its file system
        Assert.Equal([Paths.Parent, "dir1"], await ListNamesAsync(rightContext));
    }

    [Fact]
    public async Task When_AdfOnShareIsReadOnly_Then_AdfIsReadOnly()
    {
        // arrange
        await AmigaTestData.CreateAdf(Path.Combine(_sharePath, "disk.adf"));
        using (var volume = SmbVolume.Open(_server, "server", null))
        {
            await volume.ChangeDirectory(["share"]);
            await volume.SetAttributes("disk.adf", EntryAttributes.ReadOnly);
        }

        using var navigationContext = await OpenShareAsync();

        // act
        await OpenAsync(navigationContext, "disk.adf");

        // assert
        Assert.True(navigationContext.IsInImage);
        Assert.NotNull(navigationContext.CreateDirectoryNotSupportedMessage);
        await Assert.ThrowsAsync<NotSupportedException>(() => navigationContext.CreateDirectoryAsync("dir1"));
    }
}
