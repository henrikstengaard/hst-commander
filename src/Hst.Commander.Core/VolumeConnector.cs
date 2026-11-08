using Hst.Commander.Abstractions;

namespace Hst.Commander.Core;

/// <summary>
///     Connects volume providers asking for a location (<see cref="ILocationVolumeProvider" />, e.g. SMB) for both
///     front-ends: after the location is entered, connects without credentials and asks for a user name and password
///     each time the location needs them or rejects them, until connected or the credentials dialog is cancelled.
///     Volume providers asking for connection details (<see cref="IConnectionVolumeProvider" />, e.g. FTP/SFTP) are
///     connected with the credentials entered in the connection dialog first. Remembers the last location entered and
///     the last user name connected with for each volume provider while Hst Commander runs, shown the next time.
/// </summary>
public static class VolumeConnector
{
    private static readonly Lock LastLocationsLock = new();
    private static readonly Dictionary<string, string> LastLocations = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, string> LastUserNames = new(StringComparer.Ordinal);

    /// <summary>Last location entered for a volume provider, empty when none.</summary>
    public static string GetLastLocation(ILocationVolumeProvider volumeProvider)
    {
        ArgumentNullException.ThrowIfNull(volumeProvider);

        lock (LastLocationsLock)
        {
            return LastLocations.GetValueOrDefault(volumeProvider.Name, string.Empty);
        }
    }

    /// <summary>Last user name a volume provider was connected with, empty when none.</summary>
    public static string GetLastUserName(ILocationVolumeProvider volumeProvider)
    {
        ArgumentNullException.ThrowIfNull(volumeProvider);

        lock (LastLocationsLock)
        {
            return LastUserNames.GetValueOrDefault(volumeProvider.Name, string.Empty);
        }
    }

    /// <summary>
    ///     Connection details shown when the connection dialog of a volume provider opens: those of the last location
    ///     entered, otherwise the first protocol without a host.
    /// </summary>
    public static ConnectionDetails GetLastConnectionDetails(IConnectionVolumeProvider volumeProvider)
    {
        ArgumentNullException.ThrowIfNull(volumeProvider);

        var location = GetLastLocation(volumeProvider);
        if (location.Length > 0)
        {
            try
            {
                return volumeProvider.ParseLocation(location);
            }
            catch (IOException)
            {
                // e.g. a location entered with the location dialog (Ctrl+L) that isn't valid
            }
        }

        return new ConnectionDetails(volumeProvider.Protocols.FirstOrDefault()?.Name ?? string.Empty, string.Empty,
            null, string.Empty);
    }

    /// <summary>
    ///     Port entered in the connection dialog, null when empty (the default port of the protocol). Throws
    ///     <see cref="IOException" /> with a message for the dialog when it isn't a port from 1 to 65535.
    /// </summary>
    public static int? ParsePort(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        text = text.Trim();
        if (text.Length == 0)
        {
            return null;
        }

        return int.TryParse(text, out var port) && port is >= 1 and <= 65535
            ? port
            : throw new IOException($"Port '{text}' is invalid, enter a port from 1 to 65535");
    }

    /// <summary>
    ///     Location and credentials of the input of the connection dialog of a volume provider: credentials when a user
    ///     name is entered, otherwise none (e.g. an anonymous FTP login). Throws <see cref="IOException" /> with a
    ///     message for the dialog when the input isn't valid, e.g. no host or an invalid port.
    /// </summary>
    /// <param name="volumeProvider">Volume provider of the dialog.</param>
    /// <param name="protocol">Name of the protocol chosen.</param>
    /// <param name="host">Host entered.</param>
    /// <param name="port">Port entered, empty for the default port.</param>
    /// <param name="path">Path entered, empty for the home directory.</param>
    /// <param name="userName">User name entered.</param>
    /// <param name="password">Password entered.</param>
    public static ConnectionRequest CreateConnectionRequest(IConnectionVolumeProvider volumeProvider,
        string protocol, string host, string port, string path, string userName, string password)
    {
        ArgumentNullException.ThrowIfNull(volumeProvider);

        if (string.IsNullOrWhiteSpace(host))
        {
            throw new IOException("Enter the host of the server");
        }

        var location = volumeProvider.FormatLocation(new ConnectionDetails(protocol, host.Trim(), ParsePort(port),
            path.Trim()));
        userName = userName.Trim();
        return new ConnectionRequest(location,
            userName.Length > 0 ? new VolumeCredentials(userName, password) : null);
    }

    /// <summary>Default port shown for a protocol in the connection dialog, e.g. "22".</summary>
    public static string GetDefaultPortText(IConnectionVolumeProvider volumeProvider, string protocol) =>
        volumeProvider.Protocols.FirstOrDefault(candidate => candidate.Name == protocol)?.DefaultPort.ToString() ??
        string.Empty;

    /// <summary>Text shown with the progress while connecting, e.g. "Connecting to \\server\share...".</summary>
    public static string GetMessage(string location) => $"Connecting to {location.Trim()}...";

    /// <summary>
    ///     Connects to a location, asking for credentials with <paramref name="askCredentials" /> (called on the
    ///     caller's thread, e.g. the UI thread) when needed. <paramref name="show" /> is called with
    ///     <see cref="GetMessage" /> when connecting takes longer than <see cref="ListingProgress.Delay" /> and
    ///     <paramref name="hide" /> when it's done. Returns null when the credentials dialog is cancelled. Throws
    ///     <see cref="IOException" /> when the location can't be reached.
    /// </summary>
    /// <param name="volumeProvider">Volume provider connecting to the location.</param>
    /// <param name="location">Location entered, e.g. "\server\share".</param>
    /// <param name="askCredentials">Asks the user for credentials.</param>
    /// <param name="show">Shows the progress.</param>
    /// <param name="hide">Hides the progress.</param>
    /// <param name="credentials">
    ///     Credentials tried first, e.g. stored with a bookmark, null to connect without credentials first. Rejected
    ///     ones are asked for again with their user name.
    /// </param>
    /// <param name="userName">
    ///     User name shown the first time credentials are asked for when <paramref name="credentials" /> is null, e.g. the
    ///     user name of a bookmark without a stored password.
    /// </param>
    /// <param name="cancellationToken"></param>
    public static async Task<LocationConnection?> ConnectAsync(ILocationVolumeProvider volumeProvider,
        string location, Func<CredentialsRequest, Task<VolumeCredentials?>> askCredentials, Action<string> show,
        Action hide, VolumeCredentials? credentials = null, string? userName = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(volumeProvider);
        ArgumentNullException.ThrowIfNull(location);
        ArgumentNullException.ThrowIfNull(askCredentials);

        location = location.Trim();
        lock (LastLocationsLock)
        {
            LastLocations[volumeProvider.Name] = location;
        }

        while (true)
        {
            try
            {
                var connection = await ListingProgress.RunAsync(
                    volumeProvider.Connect(location, credentials, cancellationToken), GetMessage(location), show, hide,
                    ListingProgress.Delay);
                if (credentials is { UserName.Length: > 0 })
                {
                    lock (LastLocationsLock)
                    {
                        LastUserNames[volumeProvider.Name] = credentials.UserName;
                    }
                }

                return new LocationConnection(volumeProvider, location, connection, credentials);
            }
            catch (CredentialsRequiredException exception)
            {
                credentials = await askCredentials(new CredentialsRequest(volumeProvider.Name, location,
                    exception.Message, credentials?.UserName ?? userName ?? string.Empty));
                if (credentials == null)
                {
                    return null;
                }
            }
        }
    }
}

/// <summary>
///     Asks the user for a user name and password to connect to a location, shown in the credentials dialog of the
///     front-ends.
/// </summary>
/// <param name="VolumeProviderName">Name of the volume provider, e.g. "SMB".</param>
/// <param name="Location">Location connected to, e.g. "\\server\share".</param>
/// <param name="Message">Why credentials are needed, e.g. the user name or password was rejected.</param>
/// <param name="UserName">User name shown initially, the last one entered.</param>
public sealed record CredentialsRequest(string VolumeProviderName, string Location, string Message, string UserName);

/// <summary>
///     Input of the connection dialog of a volume provider asking for connection details
///     (<see cref="IConnectionVolumeProvider" />), made by <see cref="VolumeConnector.CreateConnectionRequest" />.
/// </summary>
/// <param name="Location">Location of the connection details, e.g. "sftp://server/home/user".</param>
/// <param name="Credentials">Credentials entered, null when no user name was entered.</param>
public sealed record ConnectionRequest(string Location, VolumeCredentials? Credentials);

/// <summary>
///     Connection to a location made by <see cref="VolumeConnector.ConnectAsync" />: the volume provider asking for the
///     location (e.g. SMB), the location entered (which may hold credentials, e.g. an smb:// URL, so it isn't stored),
///     the connection and the credentials it was connected with, null for none (e.g. a guest login), offered to be
///     stored with a bookmark (see <see cref="BookmarkStore" />).
/// </summary>
public sealed record LocationConnection(
    ILocationVolumeProvider LocationVolumeProvider,
    string Location,
    VolumeConnection Connection,
    VolumeCredentials? Credentials);
