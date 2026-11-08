using Hst.Commander.Abstractions;
using Hst.Commander.Plugins.Ftp;

namespace Hst.Commander.Core.Tests;

/// <summary>
///     FTP or SFTP server for the tests of the FTP plugin, serving a local directory as the root ("/") of the server.
///     Without <see cref="Credentials" /> anyone logs in, otherwise only with them. Records its connections.
/// </summary>
public sealed class TestRemoteServer(string rootPath) : IRemoteConnector
{
    /// <summary>Credentials needed to log in, null to let anyone log in, also without credentials.</summary>
    public VolumeCredentials? Credentials { get; set; }

    /// <summary>Directory users start in after logging in.</summary>
    public string HomeDirectory { get; set; } = "/";

    /// <summary>Connections made, in order.</summary>
    public List<TestRemoteClient> Connections { get; } = [];

    /// <summary>Servers connected to.</summary>
    public List<RemoteServer> Servers { get; } = [];

    public Task<IRemoteClient> Connect(RemoteServer server, VolumeCredentials? credentials,
        CancellationToken cancellationToken)
    {
        Servers.Add(server);
        if (Credentials != null && credentials != Credentials)
        {
            throw new CredentialsRequiredException(credentials == null
                ? "Test server needs a user name and password"
                : "Logging in to test server failed");
        }

        var client = new TestRemoteClient(rootPath, HomeDirectory);
        Connections.Add(client);
        return Task.FromResult<IRemoteClient>(client);
    }
}

/// <summary>Connection to a <see cref="TestRemoteServer" />.</summary>
public sealed class TestRemoteClient(string rootPath, string homeDirectory) : IRemoteClient
{
    public bool IsDisposed { get; private set; }

    /// <summary>Set to false to make the volume connect again, like a server closing an idle connection.</summary>
    public bool IsConnected { get; set; } = true;

    private string GetLocalPath(string path) =>
        Path.Combine([rootPath, ..path.Split('/', StringSplitOptions.RemoveEmptyEntries)]);

    private static RemoteEntry CreateEntry(FileSystemInfo info) => info is DirectoryInfo
        ? new RemoteEntry(info.Name, true, null, info.LastWriteTimeUtc)
        : new RemoteEntry(info.Name, false, ((FileInfo)info).Length, info.LastWriteTimeUtc);

    public Task<string> GetHomeDirectory(CancellationToken cancellationToken) => Task.FromResult(homeDirectory);

    public Task<IReadOnlyList<RemoteEntry>> List(string path, CancellationToken cancellationToken)
    {
        var directory = new DirectoryInfo(GetLocalPath(path));
        if (!directory.Exists)
        {
            throw new DirectoryNotFoundException($"Directory '{path}' not found");
        }

        return Task.FromResult<IReadOnlyList<RemoteEntry>>(directory.EnumerateFileSystemInfos()
            .Select(CreateEntry).ToList());
    }

    public Task<RemoteEntry?> GetEntry(string path, CancellationToken cancellationToken)
    {
        var localPath = GetLocalPath(path);
        FileSystemInfo? info = Directory.Exists(localPath) ? new DirectoryInfo(localPath)
            : File.Exists(localPath) ? new FileInfo(localPath)
            : null;
        return Task.FromResult(info == null ? null : CreateEntry(info));
    }

    public Task<bool> DirectoryExists(string path, CancellationToken cancellationToken) =>
        Task.FromResult(Directory.Exists(GetLocalPath(path)));

    public Task<Stream> OpenRead(string path, CancellationToken cancellationToken) =>
        Task.FromResult<Stream>(File.OpenRead(GetLocalPath(path)));

    public Task<Stream> OpenWrite(string path, CancellationToken cancellationToken) =>
        Task.FromResult<Stream>(File.Create(GetLocalPath(path)));

    public Task CreateDirectory(string path, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(GetLocalPath(path));
        return Task.CompletedTask;
    }

    public Task DeleteFile(string path, CancellationToken cancellationToken)
    {
        File.Delete(GetLocalPath(path));
        return Task.CompletedTask;
    }

    public Task DeleteDirectory(string path, CancellationToken cancellationToken)
    {
        Directory.Delete(GetLocalPath(path));
        return Task.CompletedTask;
    }

    public Task Rename(string path, string newPath, CancellationToken cancellationToken)
    {
        var localPath = GetLocalPath(path);
        if (Directory.Exists(localPath))
        {
            Directory.Move(localPath, GetLocalPath(newPath));
        }
        else
        {
            File.Move(localPath, GetLocalPath(newPath));
        }

        return Task.CompletedTask;
    }

    public Task SetModifiedDate(string path, DateTime modifiedDate, CancellationToken cancellationToken)
    {
        var localPath = GetLocalPath(path);
        if (Directory.Exists(localPath))
        {
            Directory.SetLastWriteTimeUtc(localPath, modifiedDate);
        }
        else
        {
            File.SetLastWriteTimeUtc(localPath, modifiedDate);
        }

        return Task.CompletedTask;
    }

    public Task<VolumeSpace?> GetSpace(string path, CancellationToken cancellationToken) =>
        Task.FromResult<VolumeSpace?>(new VolumeSpace(1000, 2000));

    public void Dispose() => IsDisposed = true;
}
