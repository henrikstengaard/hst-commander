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
    public static async Task<VolumeConnection?> ConnectAsync(ILocationVolumeProvider volumeProvider, string location,
        Func<CredentialsRequest, Task<VolumeCredentials?>> askCredentials, Action<string> show, Action hide,
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

        VolumeCredentials? credentials = null;
        while (true)
        {
            try
            {
                return await ListingProgress.RunAsync(volumeProvider.Connect(location, credentials, cancellationToken),
                    GetMessage(location), show, hide, ListingProgress.Delay);
            }
            catch (CredentialsRequiredException exception)
            {
                credentials = await askCredentials(new CredentialsRequest(volumeProvider.Name, location,
                    exception.Message, credentials?.UserName ?? string.Empty));
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
