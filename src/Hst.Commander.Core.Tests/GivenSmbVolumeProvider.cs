using Hst.Commander.Abstractions;
using Hst.Commander.Plugins.Smb;

namespace Hst.Commander.Core.Tests;

public class GivenSmbVolumeProvider : IDisposable
{
    private readonly string _rootPath =
        Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), $"hst-commander-{Guid.NewGuid()}")).FullName;

    private readonly TestSmbServer _server = new();
    private readonly SmbVolumeProvider _volumeProvider;
    private readonly VolumeCredentials _credentials = new(@"DOMAIN\user", "secret");

    public GivenSmbVolumeProvider()
    {
        var sharePath = Directory.CreateDirectory(Path.Combine(_rootPath, "share")).FullName;
        Directory.CreateDirectory(Path.Combine(sharePath, "dir1"));
        File.WriteAllText(Path.Combine(sharePath, "file1.txt"), "file1");
        _server.AddShare("share", sharePath);
        _volumeProvider = new SmbVolumeProvider(_server);
    }

    public void Dispose()
    {
        Directory.Delete(_rootPath, true);
        GC.SuppressFinalize(this);
    }

    private static string Join(params string[] parts) => string.Join(Path.DirectorySeparatorChar, parts);

    [Fact]
    public async Task When_ConnectingAsGuest_Then_ServerVolumeProviderAndPathAreReturned()
    {
        // act
        var connection = await _volumeProvider.Connect(@"\\server\share\dir1", null);

        // assert
        Assert.Equal(["share", "dir1"], connection.PathComponents);
        Assert.Equal(SmbPath.GetPathRoot("server"), connection.VolumeProvider.Name);
        Assert.Equal(SmbPath.GetPathRoot("server"), connection.VolumeProvider.PathRoot);
        Assert.True(Assert.Single(_server.Connections).IsDisposed);
    }

    [Fact]
    public async Task When_ConnectingToMissingDirectory_Then_IOExceptionIsThrown() =>
        await Assert.ThrowsAnyAsync<IOException>(() => _volumeProvider.Connect("smb://server/share/missing", null));

    [Fact]
    public async Task When_ServerNeedsCredentials_Then_CredentialsRequiredExceptionIsThrownUntilGiven()
    {
        // arrange
        _server.Credentials = _credentials;

        // act and assert
        await Assert.ThrowsAsync<CredentialsRequiredException>(() => _volumeProvider.Connect(@"\\server", null));
        await Assert.ThrowsAsync<CredentialsRequiredException>(() =>
            _volumeProvider.Connect(@"\\server", new VolumeCredentials(@"DOMAIN\user", "wrong")));
        var connection = await _volumeProvider.Connect(@"\\server", _credentials);
        Assert.Empty(connection.PathComponents);
    }

    [Fact]
    public async Task When_ShareDeniesGuests_Then_CredentialsRequiredExceptionIsThrown()
    {
        // arrange
        _server.GuestDeniedShares.Add("share");

        // act and assert
        var exception = await Assert.ThrowsAsync<CredentialsRequiredException>(() =>
            _volumeProvider.Connect(@"\\server\share", null));
        Assert.Contains("needs a user name and password", exception.Message);
        await _volumeProvider.Connect(@"\\server\share", _credentials);
    }

    [Fact]
    public async Task When_UrlHasCredentials_Then_TheyAreUsed()
    {
        // arrange
        _server.Credentials = _credentials;

        // act
        var connection = await _volumeProvider.Connect("smb://DOMAIN%5Cuser:secret@server/share", null);

        // assert
        Assert.Equal(["share"], connection.PathComponents);
    }

    [Fact]
    public async Task When_CreatingVolumeOfProviderAskingForLocation_Then_IOExceptionIsThrown() =>
        await Assert.ThrowsAsync<IOException>(() => _volumeProvider.CreateVolume());

    [Fact]
    public async Task When_NavigationContextShowsConnection_Then_PathsAreUncPaths()
    {
        // arrange
        using var navigationContext = new NavigationContext(_rootPath);
        var connection = await _volumeProvider.Connect(@"\\server\share", null);

        // act
        await navigationContext.ChangeVolumeProviderAsync(connection);
        var sharePath = navigationContext.CurrentPath;
        var names = (await navigationContext.ListEntriesAsync()).Select(entry => entry.Name).ToList();
        await navigationContext.ChangeLocationAsync(Join(SmbPath.GetPathRoot("server"), "share", "dir1"));
        var dirPath = navigationContext.CurrentPath;
        await navigationContext.ChangeLocationAsync(SmbPath.GetPathRoot("server"));
        var serverPath = navigationContext.CurrentPath;
        await navigationContext.ChangeLocationAsync(Join("share", "dir1"));

        // assert
        Assert.Equal(Join(SmbPath.GetPathRoot("server"), "share"), sharePath);
        Assert.Contains("file1.txt", names);
        Assert.Equal(Join(SmbPath.GetPathRoot("server"), "share", "dir1"), dirPath);
        Assert.Equal(SmbPath.GetPathRoot("server"), serverPath);
        Assert.Equal(dirPath, navigationContext.CurrentPath);
    }

    [Fact]
    public async Task When_CopyingFromShare_Then_FileIsCopiedWithVolumeOfItsOwn()
    {
        // arrange
        var destinationPath = Directory.CreateDirectory(Path.Combine(_rootPath, "destination")).FullName;
        using var navigationContext = new NavigationContext(_rootPath);
        using var destinationContext = new NavigationContext(destinationPath);
        await navigationContext.ChangeVolumeProviderAsync(await _volumeProvider.Connect(@"\\server\share", null));
        var file = (await navigationContext.ListEntriesAsync()).Single(entry => entry.Name == "file1.txt");

        // act
        await navigationContext.CopyAsync([file], destinationContext, destinationContext.CurrentPath, "*");

        // assert
        Assert.Equal("file1", await File.ReadAllTextAsync(Path.Combine(destinationPath, "file1.txt")));
        Assert.True(_server.Connections.Count >= 3);
    }

    [Fact]
    public async Task When_ConnectionPathIsMissing_Then_LocationIsUnchanged()
    {
        // arrange
        using var navigationContext = new NavigationContext(_rootPath);
        var connection = new VolumeConnection(new SmbServerVolumeProvider(_server, "server", null),
            ["share", "missing"]);

        // act and assert
        await Assert.ThrowsAnyAsync<IOException>(() => navigationContext.ChangeVolumeProviderAsync(connection));
        Assert.Equal(_rootPath, navigationContext.CurrentPath);
    }

    [Fact]
    public async Task When_PaneShowsConnection_Then_StartupIsLocal()
    {
        // arrange
        using var navigationContext = new NavigationContext(_rootPath);
        await navigationContext.ChangeVolumeProviderAsync(await _volumeProvider.Connect(@"\\server\share", null));

        // act
        var startup = PaneStartup.From(navigationContext);

        // assert
        Assert.Equal(new PaneStartup(null, null), startup);
    }
}
