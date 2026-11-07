using System.Runtime.CompilerServices;
using Hst.Commander.Abstractions;

namespace Hst.Commander.Plugins.Ftp;

/// <summary>
/// Volume of an FTP or SFTP server: its root is the root directory of the server ("/") and the path components are the
/// directories from it, e.g. ["home", "user"]. Files are read and written with the user's credentials. A connection
/// closed by the server, e.g. after being idle, is connected again.
/// </summary>
public sealed class RemoteVolume : IVolume, IDisposable
{
    private readonly IRemoteConnector _connector;
    private readonly RemoteServer _server;
    private readonly VolumeCredentials? _credentials;
    private IRemoteClient _client;
    private List<string> _pathComponents = [];
    private bool _disposed;

    private RemoteVolume(IRemoteConnector connector, RemoteServer server, VolumeCredentials? credentials,
        IRemoteClient client)
    {
        _connector = connector;
        _server = server;
        _credentials = credentials;
        _client = client;
    }

    /// <summary>
    /// Connects to a server and opens its volume at the root. Throws <see cref="CredentialsRequiredException" /> when
    /// the credentials are rejected or needed and <see cref="IOException" /> when the server can't be reached.
    /// </summary>
    public static async Task<RemoteVolume> Open(IRemoteConnector connector, RemoteServer server,
        VolumeCredentials? credentials, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connector);
        ArgumentNullException.ThrowIfNull(server);

        return new RemoteVolume(connector, server, credentials,
            await connector.Connect(server, credentials, cancellationToken));
    }

    /// <summary>Root of the server's paths, e.g. "sftp://server".</summary>
    public string Name => _server.PathRoot;

    public IEnumerable<string> CurrentPathComponents => _pathComponents.ToList();

    /// <summary>Path components of the directory the user starts in after logging in, e.g. ["home", "user"].</summary>
    public async Task<IReadOnlyList<string>> GetHomePathComponents(CancellationToken cancellationToken = default) =>
        SplitPath(await (await GetClient(cancellationToken)).GetHomeDirectory(cancellationToken));

    public async IAsyncEnumerable<IEntry> ListEntries(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var client = await GetClient(cancellationToken);
        var entries = await client.List(GetPath(_pathComponents), cancellationToken);
        foreach (var entry in entries
                     .OrderBy(entry => entry.IsDirectory ? 0 : 1)
                     .ThenBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return new RemoteVolumeEntry(entry);
        }
    }

    public async Task ChangeDirectory(IEnumerable<string> pathComponents,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(pathComponents);

        var newPathComponents = _pathComponents.ToList();
        foreach (var pathComponent in pathComponents)
        {
            if (pathComponent.Equals(Paths.Root, StringComparison.OrdinalIgnoreCase))
            {
                newPathComponents.Clear();
            }
            else if (pathComponent.Equals(Paths.Parent, StringComparison.OrdinalIgnoreCase))
            {
                if (newPathComponents.Count > 0)
                {
                    newPathComponents.RemoveAt(newPathComponents.Count - 1);
                }
            }
            else if (pathComponent.Length > 0 && pathComponent != ".")
            {
                newPathComponents.Add(pathComponent);
            }
        }

        // the directory must exist before changing to it, so the volume stays where it is when it doesn't
        if (newPathComponents.Count > 0)
        {
            var client = await GetClient(cancellationToken);
            var path = GetPath(newPathComponents);
            if (!await client.DirectoryExists(path, cancellationToken))
            {
                throw new DirectoryNotFoundException($"Directory '{path}' not found");
            }
        }

        _pathComponents = newPathComponents;
    }

    public async Task<IEntry> OpenEntry(string name, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(name);

        var pathComponents = _pathComponents.ToList();
        var client = await GetClient(cancellationToken);
        var entry = await client.GetEntry(GetPath(pathComponents, name), cancellationToken) ??
                    throw new FileNotFoundException($"Entry '{name}' not found");
        if (entry.IsDirectory)
        {
            _pathComponents = [..pathComponents, entry.Name];
        }

        return new RemoteVolumeEntry(entry);
    }

    public async Task<Stream> OpenRead(string name, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(name);

        var client = await GetClient(cancellationToken);
        return await client.OpenRead(GetPath(_pathComponents, name), cancellationToken);
    }

    public async Task<Stream> OpenWrite(string name, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(name);

        var client = await GetClient(cancellationToken);
        return await client.OpenWrite(GetPath(_pathComponents, name), cancellationToken);
    }

    public async Task CreateContainer(string name, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(name);

        var client = await GetClient(cancellationToken);
        await client.CreateDirectory(GetPath(_pathComponents, name), cancellationToken);
    }

    public async Task Delete(string name, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(name);

        var client = await GetClient(cancellationToken);
        var path = GetPath(_pathComponents, name);
        var entry = await client.GetEntry(path, cancellationToken) ??
                    throw new FileNotFoundException($"Entry '{name}' not found");
        if (entry.IsDirectory)
        {
            await client.DeleteDirectory(path, cancellationToken);
        }
        else
        {
            await client.DeleteFile(path, cancellationToken);
        }
    }

    public async Task Rename(string name, string newName, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(newName);

        var client = await GetClient(cancellationToken);
        var newPath = GetPath(_pathComponents, newName);
        if (!newName.Equals(name, StringComparison.OrdinalIgnoreCase) &&
            await client.GetEntry(newPath, cancellationToken) != null)
        {
            throw new IOException($"'{newName}' already exists");
        }

        await client.Rename(GetPath(_pathComponents, name), newPath, cancellationToken);
    }

    public async Task SetModifiedDate(string name, DateTime modifiedDate,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(name);

        var date = modifiedDate.Kind == DateTimeKind.Unspecified
            ? DateTime.SpecifyKind(modifiedDate, DateTimeKind.Utc)
            : modifiedDate.ToUniversalTime();
        var client = await GetClient(cancellationToken);
        await client.SetModifiedDate(GetPath(_pathComponents, name), date, cancellationToken);
    }

    public async Task<VolumeSpace?> GetSpace(CancellationToken cancellationToken = default)
    {
        var client = await GetClient(cancellationToken);
        return await client.GetSpace(GetPath(_pathComponents), cancellationToken);
    }

    /// <summary>The client, connected again when the server closed the connection, e.g. after being idle.</summary>
    private async Task<IRemoteClient> GetClient(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_client.IsConnected)
        {
            return _client;
        }

        var client = await _connector.Connect(_server, _credentials, cancellationToken);
        _client.Dispose();
        _client = client;
        return client;
    }

    /// <summary>Absolute path on the server of path components and a name, e.g. "/home/user/file.txt".</summary>
    internal static string GetPath(IEnumerable<string> pathComponents, string? name = null) =>
        "/" + string.Join('/', name == null ? pathComponents : [..pathComponents, name]);

    /// <summary>Name of the last component of a path, e.g. "file.txt" of "/home/user/file.txt".</summary>
    internal static string GetName(string path) => path.TrimEnd('/')[(path.TrimEnd('/').LastIndexOf('/') + 1)..];

    /// <summary>Parent directory of a path, e.g. "/home/user" of "/home/user/file.txt", "/" for the root.</summary>
    internal static string GetParent(string path)
    {
        var trimmed = path.TrimEnd('/');
        var slash = trimmed.LastIndexOf('/');
        return slash <= 0 ? "/" : trimmed[..slash];
    }

    /// <summary>Path components of an absolute path, e.g. ["home", "user"] of "/home/user".</summary>
    internal static IReadOnlyList<string> SplitPath(string path) =>
        path.Split('/', StringSplitOptions.RemoveEmptyEntries).Where(part => part != ".").ToList();

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _client.Dispose();
    }

    private sealed record RemoteVolumeEntry(RemoteEntry Entry) : IEntry
    {
        public string Name => Entry.Name;
        public EntryType Type => Entry.IsDirectory ? EntryType.Container : EntryType.Item;
        public long? Size => Entry.Size;
        public DateTime? ModifiedDate => Entry.ModifiedDate;
    }
}
