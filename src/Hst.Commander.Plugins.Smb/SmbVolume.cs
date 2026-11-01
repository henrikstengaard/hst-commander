using System.Runtime.CompilerServices;
using Hst.Commander.Abstractions;
using SMBLibrary;
using FileAttributes = SMBLibrary.FileAttributes;

namespace Hst.Commander.Plugins.Smb;

/// <summary>
/// Volume of an SMB server: its root lists the disk shares of the server, except hidden shares ending with "$" (which
/// can still be changed to by name, e.g. "c$"), and the first path component is a share, e.g. ["share", "dir"]. The
/// shares are read and written with the user's credentials. A connection closed by the server, e.g. after being idle,
/// is connected again. Requests to the server are synchronous, so the volume runs them on the thread pool.
/// </summary>
public sealed class SmbVolume : IVolume, IDisposable
{
    private const EntryAttributes ChangeableAttributes =
        EntryAttributes.ReadOnly | EntryAttributes.Hidden | EntryAttributes.System | EntryAttributes.Archive;

    private const ShareAccess ShareAll = ShareAccess.Read | ShareAccess.Write | ShareAccess.Delete;

    private readonly ISmbConnector _connector;
    private readonly string _server;
    private readonly VolumeCredentials? _credentials;
    private readonly Dictionary<string, INTFileStore> _shares = new(StringComparer.OrdinalIgnoreCase);
    private ISmbConnection _connection;
    private List<string> _pathComponents = [];
    private bool _disposed;

    private SmbVolume(ISmbConnector connector, string server, VolumeCredentials? credentials,
        ISmbConnection connection)
    {
        _connector = connector;
        _server = server;
        _credentials = credentials;
        _connection = connection;
        Name = SmbPath.GetPathRoot(server);
    }

    /// <summary>
    /// Connects to a server and opens its volume at the root. Throws <see cref="CredentialsRequiredException" /> when
    /// the credentials are rejected or needed and <see cref="IOException" /> when the server can't be reached.
    /// </summary>
    public static SmbVolume Open(ISmbConnector connector, string server, VolumeCredentials? credentials)
    {
        ArgumentNullException.ThrowIfNull(connector);
        ArgumentNullException.ThrowIfNull(server);

        return new SmbVolume(connector, server, credentials, connector.Connect(server, credentials));
    }

    /// <summary>Root of the server's paths, e.g. "\\server".</summary>
    public string Name { get; }

    public IEnumerable<string> CurrentPathComponents => _pathComponents.ToList();

    private bool IsShareList => _pathComponents.Count == 0;

    public async IAsyncEnumerable<IEntry> ListEntries(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var pathComponents = _pathComponents.ToList();
        var entries = await RunAsync(() => ListEntries(pathComponents), cancellationToken);
        foreach (var entry in entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return entry;
        }
    }

    private List<IEntry> ListEntries(IReadOnlyList<string> pathComponents)
    {
        if (pathComponents.Count == 0)
        {
            return EnsureConnected().ListShares()
                .Where(share => !share.EndsWith('$'))
                .Order(StringComparer.OrdinalIgnoreCase)
                .Select(share => (IEntry)new SmbEntry(share, EntryType.Container, null, null) { IsVolume = true })
                .ToList();
        }

        var fileStore = GetShare(pathComponents[0]);
        var directoryPath = GetSharePath(pathComponents, null);
        var handle = OpenDirectory(fileStore, directoryPath);
        try
        {
            var status = fileStore.QueryDirectory(out var files, handle, "*",
                FileInformationClass.FileDirectoryInformation);
            if (status != NTStatus.STATUS_SUCCESS && status != NTStatus.STATUS_NO_MORE_FILES)
            {
                throw SmbStatus.CreateException(status, $"Listing '{GetDisplayPath(pathComponents)}'");
            }

            return files.OfType<FileDirectoryInformation>()
                .Where(file => file.FileName != "." && file.FileName != Paths.Parent)
                .Select(CreateEntry)
                .OrderBy(entry => entry.Type == EntryType.Container ? 0 : 1)
                .ThenBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        finally
        {
            fileStore.CloseFile(handle);
        }
    }

    private static IEntry CreateEntry(FileDirectoryInformation file)
    {
        var isDirectory = file.FileAttributes.HasFlag(FileAttributes.Directory);
        return new SmbEntry(file.FileName, isDirectory ? EntryType.Container : EntryType.Item,
            isDirectory ? null : file.EndOfFile, ToUtc(file.LastWriteTime))
        {
            Attributes = ToEntryAttributes(file.FileAttributes)
        };
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

        // the directory must be opened before changing to it, so the volume stays where it is when it can't
        await RunAsync(() => CheckDirectory(newPathComponents), cancellationToken);
        _pathComponents = newPathComponents;
    }

    private bool CheckDirectory(IReadOnlyList<string> pathComponents)
    {
        if (pathComponents.Count == 0)
        {
            return true;
        }

        var fileStore = GetShare(pathComponents[0]);
        fileStore.CloseFile(OpenDirectory(fileStore, GetSharePath(pathComponents, null)));
        return true;
    }

    public async Task<IEntry> OpenEntry(string name, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(name);

        var pathComponents = _pathComponents.ToList();
        var entry = await RunAsync(() => GetEntry(pathComponents, name) ??
                                         throw new FileNotFoundException($"Entry '{name}' not found"),
            cancellationToken);
        if (entry.Type == EntryType.Container)
        {
            _pathComponents = [..pathComponents, entry.Name];
        }

        return entry;
    }

    /// <summary>Entry with a name in a directory, null when it doesn't exist.</summary>
    private IEntry? GetEntry(IReadOnlyList<string> pathComponents, string name)
    {
        if (pathComponents.Count == 0)
        {
            // hidden shares aren't listed, but are opened by name
            GetShare(name);
            return new SmbEntry(name, EntryType.Container, null, null) { IsVolume = true };
        }

        var fileStore = GetShare(pathComponents[0]);
        var path = GetSharePath(pathComponents, name);
        var status = fileStore.CreateFile(out var handle, out _, path,
            (AccessMask)FileAccessMask.FILE_READ_ATTRIBUTES, FileAttributes.Normal, ShareAll,
            CreateDisposition.FILE_OPEN, 0, null);
        if (status is NTStatus.STATUS_OBJECT_NAME_NOT_FOUND or NTStatus.STATUS_NO_SUCH_FILE
            or NTStatus.STATUS_OBJECT_PATH_NOT_FOUND)
        {
            return null;
        }

        SmbStatus.ThrowIfFailed(status, $"Opening '{GetDisplayPath(pathComponents, name)}'");
        try
        {
            status = fileStore.GetFileInformation(out var information, handle,
                FileInformationClass.FileNetworkOpenInformation);
            SmbStatus.ThrowIfFailed(status, $"Reading '{GetDisplayPath(pathComponents, name)}'");
            var file = (FileNetworkOpenInformation)information;
            var isDirectory = file.FileAttributes.HasFlag(FileAttributes.Directory);
            return new SmbEntry(name, isDirectory ? EntryType.Container : EntryType.Item,
                isDirectory ? null : file.EndOfFile, file.LastWriteTime is { } lastWriteTime ? ToUtc(lastWriteTime) : null)
            {
                Attributes = ToEntryAttributes(file.FileAttributes)
            };
        }
        finally
        {
            fileStore.CloseFile(handle);
        }
    }

    public Task<Stream> OpenRead(string name, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(name);

        var pathComponents = _pathComponents.ToList();
        return RunAsync<Stream>(() =>
        {
            var (fileStore, path) = GetFile(pathComponents, name, "read");
            var status = fileStore.CreateFile(out var handle, out _, path, AccessMask.GENERIC_READ,
                FileAttributes.Normal, ShareAccess.Read | ShareAccess.Write, CreateDisposition.FILE_OPEN,
                CreateOptions.FILE_NON_DIRECTORY_FILE, null);
            SmbStatus.ThrowIfFailed(status, $"Opening '{GetDisplayPath(pathComponents, name)}'");
            try
            {
                status = fileStore.GetFileInformation(out var information, handle,
                    FileInformationClass.FileStandardInformation);
                SmbStatus.ThrowIfFailed(status, $"Reading '{GetDisplayPath(pathComponents, name)}'");
                return new SmbFileStream(fileStore, handle, path, ((FileStandardInformation)information).EndOfFile,
                    true, false, _connection.MaxReadSize, _connection.MaxWriteSize);
            }
            catch
            {
                fileStore.CloseFile(handle);
                throw;
            }
        }, cancellationToken);
    }

    public Task<Stream> OpenWrite(string name, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(name);

        var pathComponents = _pathComponents.ToList();
        return RunAsync<Stream>(() =>
        {
            var (fileStore, path) = GetFile(pathComponents, name, "written");
            var status = fileStore.CreateFile(out var handle, out _, path,
                AccessMask.GENERIC_READ | AccessMask.GENERIC_WRITE, FileAttributes.Normal, ShareAccess.Read,
                CreateDisposition.FILE_OVERWRITE_IF, CreateOptions.FILE_NON_DIRECTORY_FILE, null);
            SmbStatus.ThrowIfFailed(status, $"Creating '{GetDisplayPath(pathComponents, name)}'");
            return new SmbFileStream(fileStore, handle, path, 0, true, true, _connection.MaxReadSize,
                _connection.MaxWriteSize);
        }, cancellationToken);
    }

    public Task CreateContainer(string name, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(name);

        var pathComponents = _pathComponents.ToList();
        return RunAsync(() =>
        {
            if (pathComponents.Count == 0)
            {
                throw new NotSupportedException("Shares can't be created");
            }

            var fileStore = GetShare(pathComponents[0]);
            var status = fileStore.CreateFile(out var handle, out _, GetSharePath(pathComponents, name),
                AccessMask.GENERIC_READ, FileAttributes.Directory, ShareAccess.Read | ShareAccess.Write,
                CreateDisposition.FILE_OPEN_IF, CreateOptions.FILE_DIRECTORY_FILE, null);
            SmbStatus.ThrowIfFailed(status, $"Creating directory '{GetDisplayPath(pathComponents, name)}'");
            fileStore.CloseFile(handle);
            return true;
        }, cancellationToken);
    }

    public Task Delete(string name, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(name);

        var pathComponents = _pathComponents.ToList();
        return RunAsync(() =>
        {
            var (fileStore, path) = GetFile(pathComponents, name, "deleted");
            var displayPath = GetDisplayPath(pathComponents, name);
            var status = fileStore.CreateFile(out var handle, out _, path, AccessMask.DELETE, FileAttributes.Normal,
                ShareAll, CreateDisposition.FILE_OPEN, 0, null);
            SmbStatus.ThrowIfFailed(status, $"Deleting '{displayPath}'");
            try
            {
                status = fileStore.SetFileInformation(handle, new FileDispositionInformation { DeletePending = true });
                SmbStatus.ThrowIfFailed(status, $"Deleting '{displayPath}'");
            }
            finally
            {
                fileStore.CloseFile(handle);
            }

            return true;
        }, cancellationToken);
    }

    public Task Rename(string name, string newName, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(newName);

        var pathComponents = _pathComponents.ToList();
        return RunAsync(() =>
        {
            var (fileStore, path) = GetFile(pathComponents, name, "renamed");
            var displayPath = GetDisplayPath(pathComponents, name);
            if (!newName.Equals(name, StringComparison.OrdinalIgnoreCase) &&
                GetEntry(pathComponents, newName) != null)
            {
                throw new IOException($"'{newName}' already exists");
            }

            var status = fileStore.CreateFile(out var handle, out _, path, AccessMask.DELETE, FileAttributes.Normal,
                ShareAll, CreateDisposition.FILE_OPEN, 0, null);
            SmbStatus.ThrowIfFailed(status, $"Renaming '{displayPath}'");
            try
            {
                status = fileStore.SetFileInformation(handle, new FileRenameInformationType2
                {
                    FileName = GetSharePath(pathComponents, newName),
                    ReplaceIfExists = false
                });
                SmbStatus.ThrowIfFailed(status, $"Renaming '{displayPath}'");
            }
            finally
            {
                fileStore.CloseFile(handle);
            }

            return true;
        }, cancellationToken);
    }

    public Task SetModifiedDate(string name, DateTime modifiedDate, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(name);

        var pathComponents = _pathComponents.ToList();
        var date = ToUtc(modifiedDate);
        return RunAsync(() =>
        {
            // times and attributes left at their defaults aren't changed
            SetBasicInformation(pathComponents, name, "Setting the date of",
                _ => new FileBasicInformation { LastWriteTime = new SetFileTime(date) });
            return true;
        }, cancellationToken);
    }

    public Task<EntryAttributes> GetChangeableAttributes(CancellationToken cancellationToken = default) =>
        Task.FromResult(IsShareList ? EntryAttributes.None : ChangeableAttributes);

    public Task SetAttributes(string name, EntryAttributes attributes, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(name);

        var pathComponents = _pathComponents.ToList();
        return RunAsync(() =>
        {
            SetBasicInformation(pathComponents, name, "Setting the attributes of", current =>
            {
                var fileAttributes = (current.FileAttributes & ~(FileAttributes)ChangeableAttributes) |
                                     (FileAttributes)(attributes & ChangeableAttributes);
                fileAttributes &= ~FileAttributes.Normal;

                // no attributes means unchanged, a file without attributes is normal
                return new FileBasicInformation
                {
                    FileAttributes = fileAttributes == 0 ? FileAttributes.Normal : fileAttributes
                };
            });
            return true;
        }, cancellationToken);
    }

    /// <summary>Changes the times or attributes of an entry with information made from its current information.</summary>
    private void SetBasicInformation(IReadOnlyList<string> pathComponents, string name, string what,
        Func<FileBasicInformation, FileBasicInformation> change)
    {
        var (fileStore, path) = GetFile(pathComponents, name, "changed");
        var displayPath = GetDisplayPath(pathComponents, name);
        var status = fileStore.CreateFile(out var handle, out _, path,
            (AccessMask)(FileAccessMask.FILE_READ_ATTRIBUTES | FileAccessMask.FILE_WRITE_ATTRIBUTES),
            FileAttributes.Normal, ShareAll, CreateDisposition.FILE_OPEN, 0, null);
        SmbStatus.ThrowIfFailed(status, $"{what} '{displayPath}'");
        try
        {
            status = fileStore.GetFileInformation(out var information, handle,
                FileInformationClass.FileBasicInformation);
            SmbStatus.ThrowIfFailed(status, $"{what} '{displayPath}'");
            status = fileStore.SetFileInformation(handle, change((FileBasicInformation)information));
            SmbStatus.ThrowIfFailed(status, $"{what} '{displayPath}'");
        }
        finally
        {
            fileStore.CloseFile(handle);
        }
    }

    public async Task<VolumeSpace?> GetSpace(CancellationToken cancellationToken = default)
    {
        if (IsShareList)
        {
            return null;
        }

        var share = _pathComponents[0];
        return await RunAsync<VolumeSpace?>(() =>
        {
            var status = GetShare(share).GetFileSystemInformation(out var information,
                FileSystemInformationClass.FileFsFullSizeInformation);
            if (status != NTStatus.STATUS_SUCCESS || information is not FileFsFullSizeInformation size)
            {
                return null;
            }

            var unitSize = (long)size.SectorsPerAllocationUnit * size.BytesPerSector;
            return new VolumeSpace(size.CallerAvailableAllocationUnits * unitSize,
                size.TotalAllocationUnits * unitSize);
        }, cancellationToken);
    }

    /// <summary>Share and path of an item in a share, throwing for the share list where items can't be used.</summary>
    private (INTFileStore FileStore, string Path) GetFile(IReadOnlyList<string> pathComponents, string name,
        string action)
    {
        if (pathComponents.Count == 0)
        {
            throw new NotSupportedException($"Shares can't be {action}");
        }

        return (GetShare(pathComponents[0]), GetSharePath(pathComponents, name));
    }

    private static object OpenDirectory(INTFileStore fileStore, string path)
    {
        var status = fileStore.CreateFile(out var handle, out _, path, AccessMask.GENERIC_READ,
            FileAttributes.Directory, ShareAccess.Read | ShareAccess.Write, CreateDisposition.FILE_OPEN,
            CreateOptions.FILE_DIRECTORY_FILE, null);
        SmbStatus.ThrowIfFailed(status, $"Opening directory '{path}'");
        return handle;
    }

    /// <summary>Opened share, connected once per connection.</summary>
    private INTFileStore GetShare(string share)
    {
        var connection = EnsureConnected();
        if (!_shares.TryGetValue(share, out var fileStore))
        {
            fileStore = connection.OpenShare(share);
            _shares[share] = fileStore;
        }

        return fileStore;
    }

    /// <summary>The connection, connected again when the server closed it.</summary>
    private ISmbConnection EnsureConnected()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!_connection.IsConnected)
        {
            _shares.Clear();
            _connection.Dispose();
            _connection = _connector.Connect(_server, _credentials);
        }

        return _connection;
    }

    /// <summary>Path in the share of the path components after the share and a name, e.g. "dir\file".</summary>
    private static string GetSharePath(IReadOnlyList<string> pathComponents, string? name) =>
        string.Join('\\', name == null ? pathComponents.Skip(1) : [..pathComponents.Skip(1), name]);

    private string GetDisplayPath(IReadOnlyList<string> pathComponents, string? name = null) =>
        string.Join(Path.DirectorySeparatorChar, name == null ? [Name, ..pathComponents] : [Name, ..pathComponents, name]);

    private static DateTime ToUtc(DateTime date) => date.Kind switch
    {
        DateTimeKind.Utc => date,
        DateTimeKind.Local => date.ToUniversalTime(),
        _ => DateTime.SpecifyKind(date, DateTimeKind.Utc)
    };

    private static EntryAttributes ToEntryAttributes(FileAttributes fileAttributes) =>
        (EntryAttributes)fileAttributes & ChangeableAttributes;

    /// <summary>Runs synchronous requests to the server on the thread pool.</summary>
    private static Task<T> RunAsync<T>(Func<T> action, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.Run(action, cancellationToken);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _shares.Clear();
        _connection.Dispose();
    }

    private sealed record SmbEntry(string Name, EntryType Type, long? Size, DateTime? ModifiedDate) : IEntry
    {
        public bool IsVolume { get; init; }

        public EntryAttributes? Attributes { get; init; }
    }
}
