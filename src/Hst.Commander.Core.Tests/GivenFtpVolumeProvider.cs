using Hst.Commander.Abstractions;
using Hst.Commander.Plugins.Ftp;

namespace Hst.Commander.Core.Tests;

public class GivenFtpVolumeProvider : IDisposable
{
    private readonly string _rootPath =
        Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), $"hst-commander-{Guid.NewGuid()}")).FullName;

    private readonly TestRemoteServer _server;
    private readonly FtpVolumeProvider _volumeProvider;
    private readonly VolumeCredentials _credentials = new("user", "secret");

    public GivenFtpVolumeProvider()
    {
        var homePath = Directory.CreateDirectory(Path.Combine(_rootPath, "home", "user")).FullName;
        Directory.CreateDirectory(Path.Combine(homePath, "dir1"));
        File.WriteAllText(Path.Combine(homePath, "file1.txt"), "file1");
        _server = new TestRemoteServer(_rootPath) { HomeDirectory = "/home/user" };
        _volumeProvider = new FtpVolumeProvider(_server);
    }

    public void Dispose()
    {
        Directory.Delete(_rootPath, true);
        GC.SuppressFinalize(this);
    }

    private async Task<RemoteVolume> OpenHomeAsync()
    {
        var connection = await _volumeProvider.Connect("sftp://server", _credentials);
        var volume = (RemoteVolume)await connection.VolumeProvider.CreateVolume();
        await volume.ChangeDirectory(connection.PathComponents);
        return volume;
    }

    [Fact]
    public async Task When_ConnectingWithoutPath_Then_HomeDirectoryIsReturned()
    {
        // act
        var connection = await _volumeProvider.Connect("sftp://server:2222", _credentials);

        // assert
        Assert.Equal(["home", "user"], connection.PathComponents);
        Assert.Equal("sftp://server:2222", connection.VolumeProvider.Name);
        Assert.Equal("sftp://server:2222", connection.VolumeProvider.PathRoot);
        Assert.Equal(new RemoteServer(RemoteProtocol.Sftp, "server", 2222), Assert.Single(_server.Servers));
        Assert.True(Assert.Single(_server.Connections).IsDisposed);
    }

    [Fact]
    public async Task When_ConnectingWithPath_Then_PathIsReturned()
    {
        // act
        var connection = await _volumeProvider.Connect("ftp://server/home", null);

        // assert
        Assert.Equal(["home"], connection.PathComponents);
        Assert.Equal("ftp://server", connection.VolumeProvider.Name);
    }

    [Fact]
    public async Task When_ConnectingToMissingDirectory_Then_IOExceptionIsThrown() =>
        await Assert.ThrowsAnyAsync<IOException>(() => _volumeProvider.Connect("ftp://server/missing", null));

    [Fact]
    public async Task When_ServerNeedsCredentials_Then_CredentialsRequiredExceptionIsThrownUntilGiven()
    {
        // arrange
        _server.Credentials = _credentials;

        // act and assert
        await Assert.ThrowsAsync<CredentialsRequiredException>(() => _volumeProvider.Connect("ftp://server", null));
        await Assert.ThrowsAsync<CredentialsRequiredException>(() =>
            _volumeProvider.Connect("ftp://server", new VolumeCredentials("user", "wrong")));
        var connection = await _volumeProvider.Connect("ftp://server", _credentials);
        Assert.Equal(["home", "user"], connection.PathComponents);
    }

    [Fact]
    public async Task When_UrlHasCredentials_Then_TheyAreUsed()
    {
        // arrange
        _server.Credentials = _credentials;

        // act
        var connection = await _volumeProvider.Connect("sftp://user:secret@server/home", null);

        // assert
        Assert.Equal(["home"], connection.PathComponents);
    }

    [Fact]
    public async Task When_ListingHome_Then_DirectoriesComeFirst()
    {
        // arrange
        using var volume = await OpenHomeAsync();

        // act
        var entries = await volume.ListEntries().ToListAsync();

        // assert
        Assert.Equal(["dir1", "file1.txt"], entries.Select(entry => entry.Name));
        Assert.Equal(EntryType.Container, entries[0].Type);
        Assert.Equal(5, entries[1].Size);
        Assert.Equal(["home", "user"], volume.CurrentPathComponents);
        Assert.Equal("sftp://server", volume.Name);
        Assert.Equal(new VolumeSpace(1000, 2000), await volume.GetSpace());
    }

    [Fact]
    public async Task When_ChangingToMissingDirectory_Then_VolumeStaysWhereItIs()
    {
        // arrange
        using var volume = await OpenHomeAsync();

        // act and assert
        await Assert.ThrowsAsync<DirectoryNotFoundException>(() => volume.ChangeDirectory(["missing"]));
        Assert.Equal(["home", "user"], volume.CurrentPathComponents);
        await volume.ChangeDirectory([Paths.Parent, Paths.Parent]);
        Assert.Empty(volume.CurrentPathComponents);
    }

    [Fact]
    public async Task When_WritingReadingAndDeletingFiles_Then_ServerIsChanged()
    {
        // arrange
        using var volume = await OpenHomeAsync();
        var date = new DateTime(2020, 1, 2, 3, 4, 5, DateTimeKind.Utc);

        // act
        await using (var stream = await volume.OpenWrite("new.txt"))
        {
            await stream.WriteAsync("hello"u8.ToArray());
        }

        await volume.SetModifiedDate("new.txt", date);
        await volume.CreateContainer("dir2");
        await volume.Rename("file1.txt", "renamed.txt");
        await volume.Delete("dir1");
        string text;
        using (var reader = new StreamReader(await volume.OpenRead("new.txt")))
        {
            text = await reader.ReadToEndAsync();
        }

        // assert
        Assert.Equal("hello", text);
        var homePath = Path.Combine(_rootPath, "home", "user");
        Assert.Equal(date, File.GetLastWriteTimeUtc(Path.Combine(homePath, "new.txt")));
        Assert.True(Directory.Exists(Path.Combine(homePath, "dir2")));
        Assert.False(Directory.Exists(Path.Combine(homePath, "dir1")));
        Assert.True(File.Exists(Path.Combine(homePath, "renamed.txt")));
    }

    [Fact]
    public async Task When_RenamingToExistingName_Then_IOExceptionIsThrown()
    {
        // arrange
        using var volume = await OpenHomeAsync();

        // act and assert
        await Assert.ThrowsAsync<IOException>(() => volume.Rename("file1.txt", "dir1"));
    }

    [Fact]
    public async Task When_OpeningDirectoryEntry_Then_VolumeChangesToIt()
    {
        // arrange
        using var volume = await OpenHomeAsync();

        // act
        var entry = await volume.OpenEntry("dir1");

        // assert
        Assert.Equal(EntryType.Container, entry.Type);
        Assert.Equal(["home", "user", "dir1"], volume.CurrentPathComponents);
        await Assert.ThrowsAsync<FileNotFoundException>(() => volume.OpenEntry("missing"));
    }

    [Fact]
    public async Task When_ServerClosedConnection_Then_VolumeConnectsAgain()
    {
        // arrange
        using var volume = await OpenHomeAsync();
        var first = _server.Connections[^1];
        first.IsConnected = false;

        // act
        var entries = await volume.ListEntries().ToListAsync();

        // assert
        Assert.Equal(2, entries.Count);
        Assert.True(first.IsDisposed);
        Assert.NotSame(first, _server.Connections[^1]);
    }

    [Fact]
    public void When_PluginIsBuiltIn_Then_FtpVolumeProviderIsListed() =>
        Assert.Contains(Core.VolumeProviders.All, volumeProvider => volumeProvider is FtpVolumeProvider);
}
