using Hst.Commander.Abstractions;

namespace Hst.Commander.Plugins.Ftp;

/// <summary>
/// File or directory listed by an <see cref="IRemoteClient" />.
/// </summary>
/// <param name="Name">Name.</param>
/// <param name="IsDirectory">True for a directory, also a link to one.</param>
/// <param name="Size">Size of a file in bytes, null for directories or when unknown.</param>
/// <param name="ModifiedDate">Modified date in UTC, null when unknown.</param>
public sealed record RemoteEntry(string Name, bool IsDirectory, long? Size, DateTime? ModifiedDate);

/// <summary>
/// Logged in connection to an FTP or SFTP server used by a <see cref="RemoteVolume" />, disposed with it. Paths are
/// absolute paths of the server, e.g. "/home/user/file.txt". Missing files and directories throw
/// <see cref="FileNotFoundException" /> or <see cref="DirectoryNotFoundException" />, denied access
/// <see cref="UnauthorizedAccessException" /> and other failures <see cref="IOException" />. Not thread safe, like
/// volumes.
/// </summary>
public interface IRemoteClient : IDisposable
{
    /// <summary>False when the server closed the connection, e.g. after being idle, so it must be connected again.</summary>
    bool IsConnected { get; }

    /// <summary>Directory the user starts in after logging in, e.g. "/home/user".</summary>
    Task<string> GetHomeDirectory(CancellationToken cancellationToken);

    /// <summary>Files and directories in a directory, without "." and "..".</summary>
    Task<IReadOnlyList<RemoteEntry>> List(string path, CancellationToken cancellationToken);

    /// <summary>File or directory at a path, null when it doesn't exist.</summary>
    Task<RemoteEntry?> GetEntry(string path, CancellationToken cancellationToken);

    /// <summary>True when a directory exists at a path, also a link to one.</summary>
    Task<bool> DirectoryExists(string path, CancellationToken cancellationToken);

    /// <summary>Opens a file for reading.</summary>
    Task<Stream> OpenRead(string path, CancellationToken cancellationToken);

    /// <summary>Creates or overwrites a file for writing. The file is complete when the stream is disposed.</summary>
    Task<Stream> OpenWrite(string path, CancellationToken cancellationToken);

    /// <summary>Creates a directory.</summary>
    Task CreateDirectory(string path, CancellationToken cancellationToken);

    /// <summary>Deletes a file.</summary>
    Task DeleteFile(string path, CancellationToken cancellationToken);

    /// <summary>Deletes an empty directory.</summary>
    Task DeleteDirectory(string path, CancellationToken cancellationToken);

    /// <summary>Renames or moves a file or directory.</summary>
    Task Rename(string path, string newPath, CancellationToken cancellationToken);

    /// <summary>
    /// Sets the modified date of a file or directory. Throws <see cref="NotSupportedException" /> when the server can't.
    /// </summary>
    Task SetModifiedDate(string path, DateTime modifiedDate, CancellationToken cancellationToken);

    /// <summary>Free and total space of the file system of a path, null when the server doesn't tell.</summary>
    Task<VolumeSpace?> GetSpace(string path, CancellationToken cancellationToken);
}

/// <summary>Connects and logs in to FTP and SFTP servers.</summary>
public interface IRemoteConnector
{
    /// <summary>
    /// Connects to a server and logs in with credentials, as anonymous (FTP) when null. Throws
    /// <see cref="CredentialsRequiredException" /> when the credentials are rejected or needed, and
    /// <see cref="IOException" /> when the server can't be reached.
    /// </summary>
    Task<IRemoteClient> Connect(RemoteServer server, VolumeCredentials? credentials,
        CancellationToken cancellationToken);
}
