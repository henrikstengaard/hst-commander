namespace Hst.Commander.Abstractions;

/// <summary>
/// Volume provider showing a volume at a location the user enters when choosing it (Alt+F1/Alt+F2), e.g. a network
/// share like "\\server\share". Hst Commander asks for the location, connects with <see cref="Connect" /> and shows the
/// volumes of the returned volume provider; when <see cref="Connect" /> throws
/// <see cref="CredentialsRequiredException" />, the user is asked for a user name and password and it is called again.
/// <see cref="IVolumeProvider.CreateVolume" /> of this provider isn't used by the panes and may throw
/// <see cref="IOException" />.
/// </summary>
public interface ILocationVolumeProvider : IVolumeProvider
{
    /// <summary>
    /// Label of the location asked for, e.g. "SMB path".
    /// </summary>
    string LocationLabel { get; }

    /// <summary>
    /// Examples of locations shown below the location, e.g. "\\server\share or smb://server/share".
    /// </summary>
    string LocationExample { get; }

    /// <summary>
    /// Connects to a location, checking it can be reached with the credentials.
    /// Throws <see cref="CredentialsRequiredException" /> when the location needs (other) credentials, and
    /// <see cref="IOException" /> when it can't be reached or isn't valid.
    /// </summary>
    /// <param name="location">Location entered by the user.</param>
    /// <param name="credentials">Credentials entered by the user, null for none (e.g. a guest login).</param>
    /// <param name="cancellationToken"></param>
    /// <returns>
    /// A volume provider creating volumes of the location, each call a volume of its own with its own connection, and
    /// the path components a pane starts in.
    /// </returns>
    Task<VolumeConnection> Connect(string location, VolumeCredentials? credentials,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Volume provider connected to a location by <see cref="ILocationVolumeProvider.Connect" />, e.g. an SMB server with
/// the user name and password entered, and the path components a pane starts in, e.g. ["share", "dir"].
/// </summary>
/// <param name="VolumeProvider">
/// Volume provider creating volumes of the location. Its name is shown as the root of the pane's path.
/// </param>
/// <param name="PathComponents">Path components from the root of its volumes to start in, empty for the root.</param>
public sealed record VolumeConnection(IVolumeProvider VolumeProvider, IReadOnlyList<string> PathComponents);

/// <summary>
/// User name and password entered to connect to a location (<see cref="ILocationVolumeProvider.Connect" />).
/// </summary>
/// <param name="UserName">User name, e.g. "user", "DOMAIN\user" or "user@domain".</param>
/// <param name="Password">Password.</param>
public sealed record VolumeCredentials(string UserName, string Password)
{
    /// <summary>Doesn't show the password, e.g. in logs.</summary>
    public override string ToString() => $"VolumeCredentials {{ UserName = {UserName} }}";
}

/// <summary>
/// Thrown by <see cref="ILocationVolumeProvider.Connect" /> when the location needs a user name and password, or the
/// ones given were rejected. Hst Commander then asks the user for credentials.
/// </summary>
public class CredentialsRequiredException(string message, Exception? innerException = null)
    : IOException(message, innerException);
