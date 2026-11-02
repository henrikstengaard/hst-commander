using Hst.Commander.Abstractions;

namespace Hst.Commander.Plugins.Smb;

/// <summary>
/// Volume provider listed as "SMB", asking for an SMB path (see <see cref="SmbPath" />) and connecting to its server
/// with <see cref="Connect" />, as guest first and with the user name and password entered when the server needs them.
/// </summary>
/// <param name="connector">Connects to servers, the SMBLibrary client by default.</param>
public sealed class SmbVolumeProvider(ISmbConnector? connector = null) : ILocationVolumeProvider
{
    private readonly ISmbConnector _connector = connector ?? new SmbClientConnector();

    public string Name => "SMB";

    public string Description => "Windows file shares (SMB)";

    public string LocationLabel => "SMB path";

    public string LocationExample => SmbPath.Example;

    /// <summary>Volumes are created by the volume provider returned by <see cref="Connect" />.</summary>
    public Task<IVolume> CreateVolume(CancellationToken cancellationToken = default) =>
        throw new IOException($"Enter an SMB path like {SmbPath.Example}");

    /// <summary>
    /// Connects to the server of an SMB path and opens its share and directories, so they are known to be accessible
    /// with the credentials, then disconnects. The credentials of a URL are used when none are given. Access denied
    /// (e.g. a guest login can't open the share) asks for credentials.
    /// </summary>
    public Task<VolumeConnection> Connect(string location, VolumeCredentials? credentials,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(location);

        var path = SmbPath.Parse(location);
        credentials ??= path.Credentials;
        return Task.Run(async () =>
        {
            using var volume = SmbVolume.Open(_connector, path.Server, credentials);
            try
            {
                if (path.Share == null)
                {
                    // the server's shares must be listed
                    await foreach (var _ in volume.ListEntries(cancellationToken))
                    {
                    }
                }
                else
                {
                    await volume.ChangeDirectory(path.PathComponents, cancellationToken);
                }
            }
            catch (UnauthorizedAccessException exception)
            {
                throw new CredentialsRequiredException(credentials == null || credentials.UserName.Length == 0
                    ? $"{path} needs a user name and password"
                    : $"Access to {path} is denied for '{credentials.UserName}'", exception);
            }

            return new VolumeConnection(new SmbServerVolumeProvider(_connector, path.Server, credentials),
                path.PathComponents);
        }, cancellationToken);
    }
}

/// <summary>
/// Volume provider of an SMB server connected to with credentials by <see cref="SmbVolumeProvider.Connect" />, creating
/// a volume with a connection of its own on each call. Its name is the root of the paths, e.g. "\\server".
/// </summary>
public sealed class SmbServerVolumeProvider(ISmbConnector connector, string server, VolumeCredentials? credentials)
    : IVolumeProvider
{
    public string Name { get; } = SmbPath.GetPathRoot(server);

    public string Description => $"SMB server {server}";

    /// <summary>Server the volumes connect to.</summary>
    public string Server => server;

    /// <summary>Paths are shown like UNC paths, e.g. "\\server\share\dir".</summary>
    public string? PathRoot => Name;

    public Task<IVolume> CreateVolume(CancellationToken cancellationToken = default) =>
        Task.Run<IVolume>(() => SmbVolume.Open(connector, server, credentials), cancellationToken);
}
