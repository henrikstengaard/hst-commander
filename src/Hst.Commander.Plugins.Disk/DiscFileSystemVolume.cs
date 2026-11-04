using System.Runtime.CompilerServices;
using DiscUtils;
using DiscUtils.Ext;
using Hst.Commander.Abstractions;

namespace Hst.Commander.Plugins.Disk;

/// <summary>
/// Volume browsing a file system read by DiscUtils, e.g. FAT, exFAT, NTFS or ext4 of a partition, writable when the file
/// system is (mounted from a writable stream; ext2/3/4 is read-only). Names are case-insensitive like FAT, exFAT and
/// NTFS, or case-sensitive like ext2/3/4 (DiscUtils lists ext names with characters Windows doesn't allow, e.g. ':',
/// replaced by '_', and opens them by those names). Entries have their attributes, which can be changed when writable.
/// Errors of the file system are thrown as <see cref="IOException" />.
/// </summary>
public sealed class DiscFileSystemVolume : IVolume, IDisposable
{
    private const char Separator = '\\';
    private static readonly char[] InvalidNameCharacters = ['\\', '/', ':', '*', '?', '"', '<', '>', '|'];

    /// <summary>Attributes of the file systems shown and changed as <see cref="EntryAttributes" />.</summary>
    private const EntryAttributes ChangeableAttributes =
        EntryAttributes.ReadOnly | EntryAttributes.Hidden | EntryAttributes.System | EntryAttributes.Archive;

    private readonly DiscFileSystem _fileSystem;
    private readonly bool _caseSensitive;
    private readonly string? _volumeLabel;
    private List<string> _currentPathComponents = [];

    /// <param name="name">Name of the volume.</param>
    /// <param name="fileSystem">File system at its root, disposed with the volume.</param>
    /// <param name="caseSensitive">True for case-sensitive file systems (e.g. ext2/3/4), false for FAT, exFAT and NTFS.</param>
    /// <param name="volumeLabel">
    /// Volume label for file systems whose DiscUtils implementation doesn't read it (exFAT), null to read it from the
    /// file system.
    /// </param>
    public DiscFileSystemVolume(string name, DiscFileSystem fileSystem, bool caseSensitive = false,
        string? volumeLabel = null)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);

        Name = name;
        _fileSystem = fileSystem;
        _caseSensitive = caseSensitive;
        _volumeLabel = volumeLabel;
    }

    private StringComparer NameComparer => _caseSensitive ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase;

    public string Name { get; }

    public IEnumerable<string> CurrentPathComponents => _currentPathComponents.ToList();

    /// <summary>Name of the file system, e.g. "Microsoft FAT".</summary>
    public string FileSystemName => _fileSystem.FriendlyName;

    /// <summary>Volume label of the file system, e.g. "EMU68BOOT".</summary>
    public string VolumeLabel => _volumeLabel ?? Run(() => _fileSystem.VolumeLabel ?? string.Empty);

    /// <summary>
    /// Size of the file system in bytes. DiscUtils returns too small a size for ext2/3/4 (less than the free space), so
    /// it's the number of blocks times the block size.
    /// </summary>
    public long Size => Run(() => _fileSystem is ExtFileSystem extFileSystem
        ? extFileSystem.TotalClusters * extFileSystem.ClusterSize
        : _fileSystem.Size);

    /// <summary>Free space of the file system in bytes.</summary>
    public long Free => Run(() => _fileSystem.AvailableSpace);

    public Task<VolumeSpace?> GetSpace(CancellationToken cancellationToken = default) =>
        Task.FromResult<VolumeSpace?>(new VolumeSpace(Math.Clamp(Free, 0, Size), Size));

    public async IAsyncEnumerable<IEntry> ListEntries(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var entries = Run(() =>
        {
            var directoryInfo = _fileSystem.GetDirectoryInfo(GetPath(_currentPathComponents));
            return directoryInfo.GetDirectories()
                .Select(directory => (IEntry)new DiscEntry(directory.Name, EntryType.Container, null,
                    directory.LastWriteTimeUtc, ToEntryAttributes(directory.Attributes)))
                .OrderBy(entry => entry.Name, NameComparer)
                .Concat(directoryInfo.GetFiles()
                    .Select(file => new DiscEntry(file.Name, EntryType.Item, file.Length, file.LastWriteTimeUtc,
                        ToEntryAttributes(file.Attributes)))
                    .OrderBy(entry => entry.Name, NameComparer))
                .ToList();
        });

        foreach (var entry in entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return entry;
        }

        await Task.CompletedTask;
    }

    public Task ChangeDirectory(IEnumerable<string> pathComponents, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(pathComponents);

        var changedPathComponents = _currentPathComponents.ToList();
        foreach (var pathComponent in pathComponents)
        {
            if (pathComponent.Equals(Paths.Root, StringComparison.OrdinalIgnoreCase))
            {
                changedPathComponents.Clear();
            }
            else if (pathComponent.Equals(Paths.Parent, StringComparison.OrdinalIgnoreCase))
            {
                if (changedPathComponents.Count > 0)
                {
                    changedPathComponents.RemoveAt(changedPathComponents.Count - 1);
                }
            }
            else
            {
                var info = FindEntry(changedPathComponents, pathComponent);
                if (info is not { IsDirectory: true })
                {
                    throw new DirectoryNotFoundException($"Directory '{pathComponent}' not found");
                }

                changedPathComponents.Add(info.Name);
            }
        }

        _currentPathComponents = changedPathComponents;
        return Task.CompletedTask;
    }

    public async Task<IEntry> OpenEntry(string name, CancellationToken cancellationToken = default)
    {
        var info = FindEntry(_currentPathComponents, name) ?? throw new IOException($"Entry '{name}' not found");
        if (info.IsDirectory)
        {
            await ChangeDirectory([info.Name], cancellationToken);
            var path = GetPath(_currentPathComponents);
            return new DiscEntry(info.Name, EntryType.Container, null,
                Run(() => _fileSystem.GetLastWriteTimeUtc(path)),
                ToEntryAttributes(Run(() => _fileSystem.GetAttributes(path))));
        }

        var fileInfo = Run(() => _fileSystem.GetFileInfo(GetPath(info.Name)));
        return new DiscEntry(fileInfo.Name, EntryType.Item, fileInfo.Length, fileInfo.LastWriteTimeUtc,
            ToEntryAttributes(Run(() => fileInfo.Attributes)));
    }

    public Task<Stream> OpenRead(string name, CancellationToken cancellationToken = default)
    {
        if (FindEntry(_currentPathComponents, name) is not { IsDirectory: false } fileInfo)
        {
            throw new FileNotFoundException($"File '{name}' not found");
        }

        return Task.FromResult<Stream>(Run(() =>
            _fileSystem.OpenFile(GetPath(fileInfo.Name), FileMode.Open, FileAccess.Read)));
    }

    public Task<Stream> OpenWrite(string name, CancellationToken cancellationToken = default)
    {
        ThrowIfReadOnly();
        ValidateName(name);

        var info = FindEntry(_currentPathComponents, name);
        if (info is { IsDirectory: true })
        {
            throw new IOException($"'{info.Name}' is a directory");
        }

        return Task.FromResult<Stream>(Run(() =>
            _fileSystem.OpenFile(GetPath(info?.Name ?? name), FileMode.Create, FileAccess.ReadWrite)));
    }

    public Task CreateContainer(string name, CancellationToken cancellationToken = default)
    {
        ThrowIfReadOnly();
        ValidateName(name);

        switch (FindEntry(_currentPathComponents, name))
        {
            case { IsDirectory: true }:
                return Task.CompletedTask;
            case { } info:
                throw new IOException($"'{info.Name}' already exists");
        }

        Run(() => _fileSystem.CreateDirectory(GetPath(name)));
        return Task.CompletedTask;
    }

    public Task Delete(string name, CancellationToken cancellationToken = default)
    {
        ThrowIfReadOnly();

        switch (FindEntry(_currentPathComponents, name))
        {
            case { IsDirectory: true } directoryInfo:
                Run(() =>
                {
                    var path = GetPath(directoryInfo.Name);
                    if (_fileSystem.GetFileSystemEntries(path).Any())
                    {
                        throw new IOException($"Directory '{directoryInfo.Name}' is not empty");
                    }

                    _fileSystem.DeleteDirectory(path);
                });
                break;
            case { } info:
                Run(() => _fileSystem.DeleteFile(GetPath(info.Name)));
                break;
            default:
                throw new FileNotFoundException($"Entry '{name}' not found");
        }

        return Task.CompletedTask;
    }

    public Task Rename(string name, string newName, CancellationToken cancellationToken = default)
    {
        ThrowIfReadOnly();
        ValidateName(newName);

        var info = FindEntry(_currentPathComponents, name) ?? throw new FileNotFoundException($"Entry '{name}' not found");
        if (info.Name.Equals(newName, StringComparison.Ordinal))
        {
            return Task.CompletedTask;
        }

        // names are case-insensitive, so only the case is changed by renaming to a temporary name first
        if (!_caseSensitive && info.Name.Equals(newName, StringComparison.OrdinalIgnoreCase))
        {
            var temporaryName = GetTemporaryName();
            Move(info, info.Name, temporaryName);
            Move(info, temporaryName, newName);
        }
        else
        {
            if (FindEntry(_currentPathComponents, newName) is { } existingInfo)
            {
                throw new IOException($"'{existingInfo.Name}' already exists");
            }

            Move(info, info.Name, newName);
        }

        return Task.CompletedTask;
    }

    private void Move(FoundEntry info, string name, string newName) => Run(() =>
    {
        if (info.IsDirectory)
        {
            _fileSystem.MoveDirectory(GetPath(name), GetPath(newName));
        }
        else
        {
            _fileSystem.MoveFile(GetPath(name), GetPath(newName), false);
        }
    });

    private string GetTemporaryName()
    {
        for (var number = 0;; number++)
        {
            var temporaryName = $"~hst-commander-{number}";
            if (FindEntry(_currentPathComponents, temporaryName) == null)
            {
                return temporaryName;
            }
        }
    }

    public Task SetModifiedDate(string name, DateTime modifiedDate, CancellationToken cancellationToken = default)
    {
        ThrowIfReadOnly();

        var info = FindEntry(_currentPathComponents, name) ?? throw new FileNotFoundException($"Entry '{name}' not found");
        var utcDate = modifiedDate.Kind == DateTimeKind.Local
            ? modifiedDate.ToUniversalTime()
            : DateTime.SpecifyKind(modifiedDate, DateTimeKind.Utc);
        Run(() => _fileSystem.SetLastWriteTimeUtc(GetPath(info.Name), utcDate));
        return Task.CompletedTask;
    }

    public Task<EntryAttributes> GetChangeableAttributes(CancellationToken cancellationToken = default) =>
        Task.FromResult(_fileSystem.CanWrite ? ChangeableAttributes : EntryAttributes.None);

    public Task SetAttributes(string name, EntryAttributes attributes, CancellationToken cancellationToken = default)
    {
        ThrowIfReadOnly();

        var info = FindEntry(_currentPathComponents, name) ?? throw new FileNotFoundException($"Entry '{name}' not found");
        var path = GetPath(info.Name);
        Run(() =>
        {
            var fileAttributes = _fileSystem.GetAttributes(path) & ~(FileAttributes)ChangeableAttributes;
            _fileSystem.SetAttributes(path, fileAttributes | (FileAttributes)(attributes & ChangeableAttributes));
        });
        return Task.CompletedTask;
    }

    private static EntryAttributes ToEntryAttributes(FileAttributes attributes) =>
        (EntryAttributes)attributes & ChangeableAttributes;

    /// <summary>
    /// Finds an entry in a directory, case-insensitive like FAT, exFAT and NTFS (preferring the exact name) or
    /// case-sensitive like ext2/3/4, with the name as stored. Null when not found or the name is invalid.
    /// </summary>
    private FoundEntry? FindEntry(IReadOnlyList<string> directoryPathComponents, string name)
    {
        if (name.Length == 0 || name.IndexOfAny(InvalidNameCharacters) >= 0 || name is "." or "..")
        {
            return null;
        }

        return Run(() =>
        {
            var directoryInfo = _fileSystem.GetDirectoryInfo(GetPath(directoryPathComponents));
            var entries = directoryInfo.GetDirectories().Select(info => new FoundEntry(info.Name, true))
                .Concat(directoryInfo.GetFiles().Select(info => new FoundEntry(info.Name, false)))
                .ToList();
            return entries.FirstOrDefault(entry => entry.Name.Equals(name, StringComparison.Ordinal)) ??
                   (_caseSensitive
                       ? null
                       : entries.FirstOrDefault(entry => entry.Name.Equals(name, StringComparison.OrdinalIgnoreCase)));
        });
    }

    private string GetPath(string name) => GetPath([.._currentPathComponents, name]);

    private static string GetPath(IEnumerable<string> pathComponents) => string.Join(Separator, pathComponents);

    private static void ValidateName(string name)
    {
        ArgumentNullException.ThrowIfNull(name);

        if (name.Length == 0 || name is "." or ".." || name.IndexOfAny(InvalidNameCharacters) >= 0 ||
            name.Any(char.IsControl))
        {
            throw new IOException(
                $"Invalid name '{name}', names can't contain {string.Join(' ', InvalidNameCharacters)}");
        }
    }

    private void ThrowIfReadOnly()
    {
        if (!_fileSystem.CanWrite)
        {
            throw new NotSupportedException($"The file system ({FileSystemName}) is read-only");
        }
    }

    /// <summary>Runs a file system operation, throwing its errors as <see cref="IOException" />.</summary>
    private static T Run<T>(Func<T> operation)
    {
        try
        {
            return operation();
        }
        catch (Exception exception) when (IsFileSystemError(exception))
        {
            throw new IOException(exception.Message, exception);
        }
    }

    /// <summary>Runs a file system operation, throwing its errors as <see cref="IOException" />.</summary>
    private static void Run(Action operation) => Run(() =>
    {
        operation();
        return true;
    });

    private static bool IsFileSystemError(Exception exception) =>
        exception is not (IOException or NotSupportedException or OperationCanceledException);

    public void Dispose() => _fileSystem.Dispose();

    /// <summary>Entry found in a directory, with its name as stored.</summary>
    private sealed record FoundEntry(string Name, bool IsDirectory);

    private sealed record DiscEntry(string Name, EntryType Type, long? Size, DateTime? ModifiedDate,
        EntryAttributes? Attributes) : IEntry;
}
