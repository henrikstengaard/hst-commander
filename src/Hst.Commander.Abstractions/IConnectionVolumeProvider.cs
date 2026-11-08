namespace Hst.Commander.Abstractions;

/// <summary>
/// Volume provider asking for connection details and credentials in one dialog when chosen (Alt+F1/Alt+F2), e.g. an FTP
/// or SFTP server, instead of the location text of an <see cref="ILocationVolumeProvider" />. The dialog has a choice of
/// <see cref="Protocols" />, a host, a port, a path, a user name and a password. Hst Commander makes the location of the
/// details with <see cref="FormatLocation" /> (e.g. "sftp://server/home/user"), connects with
/// <see cref="ILocationVolumeProvider.Connect" /> with the credentials entered (null when no user name is entered), and
/// asks for credentials again when it throws <see cref="CredentialsRequiredException" />. Bookmarks store the location
/// and the user name, and the password in the credential store of the operating system, like other location volume
/// providers.
/// </summary>
public interface IConnectionVolumeProvider : ILocationVolumeProvider
{
    /// <summary>
    /// Protocols the user chooses from, e.g. FTP and SFTP, the first chosen initially.
    /// </summary>
    IReadOnlyList<ConnectionProtocol> Protocols { get; }

    /// <summary>
    /// Location of connection details, e.g. "sftp://server:2222/home/user", passed to
    /// <see cref="ILocationVolumeProvider.Connect" /> and stored with bookmarks. It must not hold the credentials.
    /// Throws <see cref="IOException" /> when the details aren't valid.
    /// </summary>
    string FormatLocation(ConnectionDetails details);

    /// <summary>
    /// Connection details of a location, e.g. the last one connected to, shown when the dialog opens. Throws
    /// <see cref="IOException" /> when the location isn't valid.
    /// </summary>
    ConnectionDetails ParseLocation(string location);
}

/// <summary>
/// Protocol of an <see cref="IConnectionVolumeProvider" />, e.g. SFTP.
/// </summary>
/// <param name="Name">Name of the protocol in <see cref="ConnectionDetails.Protocol" />, e.g. "SFTP".</param>
/// <param name="Label">Label shown to the user, e.g. "SFTP (SSH)".</param>
/// <param name="DefaultPort">Port used when none is entered, e.g. 22.</param>
public sealed record ConnectionProtocol(string Name, string Label, int DefaultPort);

/// <summary>
/// Connection details entered in the connection dialog of an <see cref="IConnectionVolumeProvider" />.
/// </summary>
/// <param name="Protocol">Name of the protocol (<see cref="ConnectionProtocol.Name" />).</param>
/// <param name="Host">Name or IP address of the server, e.g. "ftp.example.com" or "192.168.1.10".</param>
/// <param name="Port">Port, null for the default port of the protocol.</param>
/// <param name="Path">Directory to start in, e.g. "/pub", empty for the user's home directory.</param>
public sealed record ConnectionDetails(string Protocol, string Host, int? Port, string Path);
