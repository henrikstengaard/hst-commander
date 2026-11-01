using Hst.Commander.Abstractions;
using SMBLibrary;

namespace Hst.Commander.Plugins.Smb;

/// <summary>
/// Logged in connection to an SMB server used by an <see cref="SmbVolume" />, disposed with it. Not thread safe, like
/// volumes.
/// </summary>
public interface ISmbConnection : IDisposable
{
    /// <summary>False when the server closed the connection, e.g. after being idle, so it must be connected again.</summary>
    bool IsConnected { get; }

    /// <summary>Most bytes read with one request.</summary>
    int MaxReadSize { get; }

    /// <summary>Most bytes written with one request.</summary>
    int MaxWriteSize { get; }

    /// <summary>
    /// Names of the disk shares of the server. Throws <see cref="UnauthorizedAccessException" /> when access is denied
    /// and <see cref="IOException" /> when the server can't list them.
    /// </summary>
    IReadOnlyList<string> ListShares();

    /// <summary>
    /// Connects to a share. Throws <see cref="UnauthorizedAccessException" /> when access is denied and
    /// <see cref="DirectoryNotFoundException" /> when the share doesn't exist.
    /// </summary>
    INTFileStore OpenShare(string share);
}

/// <summary>Connects and logs in to SMB servers.</summary>
public interface ISmbConnector
{
    /// <summary>
    /// Connects to a server and logs in with credentials, or as guest when null. Throws
    /// <see cref="CredentialsRequiredException" /> when the credentials are rejected or needed, and
    /// <see cref="IOException" /> when the server can't be reached.
    /// </summary>
    ISmbConnection Connect(string server, VolumeCredentials? credentials);
}
