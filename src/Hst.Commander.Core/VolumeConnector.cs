using Hst.Commander.Abstractions;

namespace Hst.Commander.Core;

/// <summary>
///     Connects volume providers asking for a location (<see cref="ILocationVolumeProvider" />, e.g. SMB) for both
///     front-ends: after the location is entered, connects without credentials and asks for a user name and password
///     each time the location needs them or rejects them, until connected or the credentials dialog is cancelled.
///     Remembers the last location entered for each volume provider while Hst Commander runs, shown the next time.
/// </summary>
public static class VolumeConnector
{
    private static readonly Lock LastLocationsLock = new();
    private static readonly Dictionary<string, string> LastLocations = new(StringComparer.Ordinal);

    /// <summary>Last location entered for a volume provider, empty when none.</summary>
    public static string GetLastLocation(ILocationVolumeProvider volumeProvider)
    {
        ArgumentNullException.ThrowIfNull(volumeProvider);

        lock (LastLocationsLock)
        {
            return LastLocations.GetValueOrDefault(volumeProvider.Name, string.Empty);
        }
    }

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
