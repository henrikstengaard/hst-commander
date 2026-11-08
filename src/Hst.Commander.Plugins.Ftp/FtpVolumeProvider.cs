using Hst.Commander.Abstractions;

namespace Hst.Commander.Plugins.Ftp;

/// <summary>
/// Volume provider listed as "FTP/SFTP", asking for the protocol, host, port, path, user name and password of a server
/// in the connection dialog (see <see cref="IConnectionVolumeProvider" />) and connecting to it with
/// <see cref="Connect" />. Its locations are URLs like "sftp://server/home/user" (see <see cref="RemoteLocation" />).
/// </summary>
/// <param name="connector">Connects to servers, FluentFTP and SSH.NET by default.</param>
public sealed class FtpVolumeProvider(IRemoteConnector? connector = null) : IConnectionVolumeProvider
{
    private readonly IRemoteConnector _connector = connector ?? new RemoteConnector();

    public string Name => "FTP/SFTP";

    public string Description => "FTP, FTPS and SFTP servers";

    public string LocationLabel => "FTP or SFTP location";

    public string LocationExample => RemoteLocation.Example;

    public IReadOnlyList<ConnectionProtocol> Protocols => RemoteLocation.Protocols;

    public string FormatLocation(ConnectionDetails details) => RemoteLocation.Format(details);

    public ConnectionDetails ParseLocation(string location) => RemoteLocation.Parse(location).ToConnectionDetails();

    /// <summary>Volumes are created by the volume provider returned by <see cref="Connect" />.</summary>
    public Task<IVolume> CreateVolume(CancellationToken cancellationToken = default) =>
        throw new IOException($"Enter a location like {RemoteLocation.Example}");

    /// <summary>
    /// Connects to the server of a location and changes to its directory, the user's home directory when it has none, so
    /// it is known to be accessible with the credentials, then disconnects. The credentials of a URL are used when none
    /// are given.
    /// </summary>
    public async Task<VolumeConnection> Connect(string location, VolumeCredentials? credentials,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(location);

        var remoteLocation = RemoteLocation.Parse(location);
        credentials ??= remoteLocation.Credentials;
        using var volume = await RemoteVolume.Open(_connector, remoteLocation.Server, credentials, cancellationToken);
        var pathComponents = remoteLocation.Directories ?? await volume.GetHomePathComponents(cancellationToken);
        await volume.ChangeDirectory([Paths.Root, ..pathComponents], cancellationToken);
        return new VolumeConnection(new FtpServerVolumeProvider(_connector, remoteLocation.Server, credentials),
            pathComponents);
    }
}

/// <summary>
/// Volume provider of an FTP or SFTP server connected to with credentials by <see cref="FtpVolumeProvider.Connect" />,
/// creating a volume with a connection of its own on each call. Its name is the root of the paths, e.g. "sftp://server".
/// </summary>
public sealed class FtpServerVolumeProvider(IRemoteConnector connector, RemoteServer server,
    VolumeCredentials? credentials) : IVolumeProvider
{
    public string Name => server.PathRoot;

    public string Description => $"{server.Protocol.ToString().ToUpperInvariant()} server {server.Host}";

    /// <summary>Server the volumes connect to.</summary>
    public RemoteServer Server => server;

    /// <summary>Paths are shown as URLs, e.g. "sftp://server/home/user".</summary>
    public string? PathRoot => Name;

    /// <summary>
    /// Listed files aren't read in the background to detect images or read icons, as FTP servers often allow one
    /// connection per user and reading the files would block browsing. Images are still detected when opened.
    /// </summary>
    public bool ReadListedFiles => false;

    public async Task<IVolume> CreateVolume(CancellationToken cancellationToken = default) =>
        await RemoteVolume.Open(connector, server, credentials, cancellationToken);
}
