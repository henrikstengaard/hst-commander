using Hst.Commander.Abstractions;
using Hst.Commander.Plugins.Ftp;

namespace Hst.Commander.Core.Tests;

public class GivenVolumeConnectorWithConnectionVolumeProvider : IDisposable
{
    /// <summary>
    ///     The FTP volume provider with a name of its own, as <see cref="VolumeConnector" /> remembers the last location
    ///     and user name by name for all tests.
    /// </summary>
    private sealed class TestConnectionVolumeProvider(FtpVolumeProvider volumeProvider, string name)
        : IConnectionVolumeProvider
    {
        public string Name => name;
        public string Description => volumeProvider.Description;
        public string LocationLabel => volumeProvider.LocationLabel;
        public string LocationExample => volumeProvider.LocationExample;
        public IReadOnlyList<ConnectionProtocol> Protocols => volumeProvider.Protocols;
        public string FormatLocation(ConnectionDetails details) => volumeProvider.FormatLocation(details);
        public ConnectionDetails ParseLocation(string location) => volumeProvider.ParseLocation(location);

        public Task<IVolume> CreateVolume(CancellationToken cancellationToken = default) =>
            volumeProvider.CreateVolume(cancellationToken);

        public Task<VolumeConnection> Connect(string location, VolumeCredentials? credentials,
            CancellationToken cancellationToken = default) =>
            volumeProvider.Connect(location, credentials, cancellationToken);
    }

    private readonly string _rootPath =
        Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), $"hst-commander-{Guid.NewGuid()}")).FullName;

    private readonly TestRemoteServer _server;
    private readonly TestConnectionVolumeProvider _volumeProvider;

    public GivenVolumeConnectorWithConnectionVolumeProvider()
    {
        Directory.CreateDirectory(Path.Combine(_rootPath, "pub"));
        _server = new TestRemoteServer(_rootPath) { Credentials = new VolumeCredentials("user", "secret") };
        _volumeProvider = new TestConnectionVolumeProvider(new FtpVolumeProvider(_server), $"Test-{Guid.NewGuid()}");
    }

    public void Dispose()
    {
        Directory.Delete(_rootPath, true);
        GC.SuppressFinalize(this);
    }

    private static void Ignore(string message)
    {
    }

    private static void Ignore()
    {
    }

    [Fact]
    public void When_NothingWasConnected_Then_FirstProtocolWithoutHostIsShown()
    {
        // act
        var details = VolumeConnector.GetLastConnectionDetails(_volumeProvider);

        // assert
        Assert.Equal(new ConnectionDetails("FTP", string.Empty, null, string.Empty), details);
        Assert.Equal(string.Empty, VolumeConnector.GetLastUserName(_volumeProvider));
        Assert.Equal("22", VolumeConnector.GetDefaultPortText(_volumeProvider, "SFTP"));
    }

    [Fact]
    public void When_DialogInputHasUserName_Then_LocationAndCredentialsAreReturned()
    {
        // act
        var request = VolumeConnector.CreateConnectionRequest(_volumeProvider, "SFTP", " server ", " 2222 ", "/pub",
            " user ", "secret");

        // assert
        Assert.Equal(new ConnectionRequest("sftp://server:2222/pub", new VolumeCredentials("user", "secret")),
            request);
    }

    [Fact]
    public void When_DialogInputHasNoUserName_Then_CredentialsAreNull() =>
        Assert.Null(VolumeConnector.CreateConnectionRequest(_volumeProvider, "FTP", "server", "", "", " ", "")
            .Credentials);

    [Theory]
    [InlineData("", "")]
    [InlineData("server", "port")]
    [InlineData("server", "0")]
    [InlineData("server", "65536")]
    public void When_DialogInputIsInvalid_Then_IOExceptionIsThrown(string host, string port) =>
        Assert.Throws<IOException>(() =>
            VolumeConnector.CreateConnectionRequest(_volumeProvider, "FTP", host, port, "", "", ""));

    [Fact]
    public async Task When_ConnectedWithDialogCredentials_Then_TheyAreNotAskedForAndRemembered()
    {
        // arrange
        var request = VolumeConnector.CreateConnectionRequest(_volumeProvider, "FTPS", "server", "2121", "/pub",
            "user", "secret");
        var asked = 0;

        // act
        var connection = await VolumeConnector.ConnectAsync(_volumeProvider, request.Location, _ =>
        {
            asked++;
            return Task.FromResult<VolumeCredentials?>(null);
        }, Ignore, Ignore, request.Credentials);

        // assert
        Assert.NotNull(connection);
        Assert.Equal(0, asked);
        Assert.Equal(["pub"], connection.Connection.PathComponents);
        Assert.Equal(new ConnectionDetails("FTPS", "server", 2121, "/pub"),
            VolumeConnector.GetLastConnectionDetails(_volumeProvider));
        Assert.Equal("user", VolumeConnector.GetLastUserName(_volumeProvider));
    }

    [Fact]
    public async Task When_DialogCredentialsAreRejected_Then_TheyAreAskedForWithUserName()
    {
        // arrange
        var requests = new List<CredentialsRequest>();

        // act
        var connection = await VolumeConnector.ConnectAsync(_volumeProvider, "ftp://server", request =>
        {
            requests.Add(request);
            return Task.FromResult<VolumeCredentials?>(new VolumeCredentials("user", "secret"));
        }, Ignore, Ignore, new VolumeCredentials("user", "wrong"));

        // assert
        Assert.NotNull(connection);
        Assert.Equal("user", Assert.Single(requests).UserName);
    }
}
