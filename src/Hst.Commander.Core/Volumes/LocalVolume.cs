using System.Runtime.CompilerServices;
using Hst.Commander.Abstractions;

namespace Hst.Commander.Core.Volumes;

public class LocalVolume(string path) : IVolume
{
    private string? _currentPath = string.IsNullOrWhiteSpace(path) ? null : Path.GetFullPath(path);
    private IList<string> _currentPathComponents = new List<string>(string.IsNullOrWhiteSpace(path)
        ? []
        : Path.GetFullPath(path).Split(Path.DirectorySeparatorChar));

    public string Name => "Local";

    public IEnumerable<string> CurrentPathComponents => _currentPathComponents;

    public async IAsyncEnumerable<IEntry> ListEntries([EnumeratorCancellation] CancellationToken cancellationToken =
        default)
    {
        var entries = _currentPath == null
            ? await GetDriveEntries()
            : await GetDirectoryEntries(_currentPath);
        
        foreach (var entry in entries)
        {
            yield return entry;
        }
    }

    public Task ChangeDirectory(IEnumerable<string> pathComponents, CancellationToken cancellationToken = default)
    {
        var pathComponentsArray = pathComponents as string[] ?? pathComponents.ToArray();
        var currentPath = _currentPath;
        
        foreach (var pathComponent in pathComponentsArray)
        {
            if (pathComponent.Equals(Paths.Root, StringComparison.OrdinalIgnoreCase))
            {
                currentPath = currentPath == null && Path.IsPathRooted(pathComponent) &&
                              Directory.Exists(pathComponent)
                    ? Path.GetFullPath(pathComponent)
                    : null;
                continue;
            }
            
            if (pathComponent.Equals(Paths.Parent, StringComparison.OrdinalIgnoreCase))
            {
                currentPath = currentPath == null ? null : Directory.GetParent(currentPath)?.FullName;
                continue;
            }
            
            var path = Path.GetFullPath(Path.Combine(currentPath ?? string.Empty, pathComponent));
            if (!Directory.Exists(path))
            {
                throw new IOException($"Dir not found '{pathComponent}'");
            }
            
            currentPath = path;
        }

        SetCurrentPath(currentPath);
        
        return Task.CompletedTask;
    }
    
    /// <summary>
    /// Change current path to container. The container must exist in order to change to it.
    /// </summary>
    /// <param name="name">Name of container to change to.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns></returns>
    /// <exception cref="IOException"></exception>
    public Task ChangeContainer(string name, CancellationToken cancellationToken = default)
    {
        var path = Path.GetFullPath(Path.Combine(_currentPath ?? string.Empty, name));
        var directoryInfo = new DirectoryInfo(path);

        if (!directoryInfo.Exists)
        {
            throw new IOException("Container not found");
        }

        SetCurrentPath(path);
        
        return Task.CompletedTask;
    }

    /// <summary>
    /// Change to root container.
    /// </summary>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    public Task ChangeRoot(CancellationToken cancellationToken = default)
    {
        SetCurrentPath(null);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Change to parent container. If current container is root, it will stay root.
    /// </summary>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    public Task ChangeParent(CancellationToken cancellationToken = default)
    {
        return ChangeDirectory([Paths.Parent], cancellationToken);
    }
    
    public Task<IEntry> OpenEntry(string name, CancellationToken cancellationToken = default)
    {
        var path = Path.Combine(_currentPath ?? string.Empty, name);
        var directoryInfo = new DirectoryInfo(path);

        if (directoryInfo.Exists)
        {
            var entry = CreateEntry(directoryInfo);
            
            _currentPath = directoryInfo.FullName;
            _currentPathComponents = _currentPathComponents.Concat([entry.Name]).ToList();
            
            return Task.FromResult(entry);
        }
        
        var fileInfo = new FileInfo(path);
        if (!fileInfo.Exists)
        {
            throw new IOException("Entry not found");
        }

        var fileEntry = CreateEntry(fileInfo);

        return Task.FromResult(fileEntry);
    }

    /// <summary>
    /// Opens an item for reading, shared with writers, e.g. a writable image opened in a pane (see
    /// <c>SharedImages</c>), so it can still be detected as an image or copied.
    /// </summary>
    public Task<Stream> OpenRead(string name, CancellationToken cancellationToken = default) =>
        Task.FromResult<Stream>(new FileStream(GetItemPath(name), FileMode.Open, FileAccess.Read,
            FileShare.ReadWrite, BufferSize, true));

    public Task<Stream> OpenWrite(string name, CancellationToken cancellationToken = default) =>
        Task.FromResult<Stream>(new FileStream(GetItemPath(name), FileMode.Create, FileAccess.Write, FileShare.None,
            BufferSize, true));

    public Task CreateContainer(string name, CancellationToken cancellationToken = default)
    {
        if (_currentPath == null)
        {
            throw new NotSupportedException("Creating a directory is not supported in the drive list");
        }

        Directory.CreateDirectory(GetItemPath(name));
        return Task.CompletedTask;
    }

    public Task Delete(string name, CancellationToken cancellationToken = default)
    {
        var path = GetItemPath(name);
        if (Directory.Exists(path))
        {
            Directory.Delete(path);
        }
        else
        {
            File.Delete(path);
        }

        return Task.CompletedTask;
    }

    public Task Rename(string name, string newName, CancellationToken cancellationToken = default)
    {
        var path = GetItemPath(name);
        var newPath = GetItemPath(newName);
        var caseOnly = path.Equals(newPath, StringComparison.OrdinalIgnoreCase) &&
                       !path.Equals(newPath, StringComparison.Ordinal);
        if (!caseOnly && (File.Exists(newPath) || Directory.Exists(newPath)))
        {
            throw new IOException($"'{newName}' already exists");
        }

        if (Directory.Exists(path))
        {
            Directory.Move(path, newPath);
        }
        else
        {
            File.Move(path, newPath);
        }

        return Task.CompletedTask;
    }

    public Task SetModifiedDate(string name, DateTime modifiedDate, CancellationToken cancellationToken = default)
    {
        var path = GetItemPath(name);
        var modifiedDateUtc = modifiedDate.Kind == DateTimeKind.Local ? modifiedDate.ToUniversalTime() : modifiedDate;
        if (Directory.Exists(path))
        {
            Directory.SetLastWriteTimeUtc(path, modifiedDateUtc);
        }
        else
        {
            File.SetLastWriteTimeUtc(path, modifiedDateUtc);
        }

        return Task.CompletedTask;
    }

    private const int BufferSize = 81920;

    /// <summary>
    /// Path of an entry in the current directory. Entries can't be read or written in the drive list.
    /// </summary>
    private string GetItemPath(string name)
    {
        if (_currentPath == null)
        {
            throw new IOException("Entries can't be read or written in the drive list");
        }

        return Path.Combine(_currentPath, name);
    }

    private static Task<IEnumerable<IEntry>> GetDriveEntries() => 
        Task.FromResult(DriveInfo.GetDrives().Select(CreateEntry));

    private static Task<List<IEntry>> GetDirectoryEntries(string path)
    {
        var directoryInfo = new DirectoryInfo(path);
        var entries = new List<IEntry>();
        
        entries.AddRange(directoryInfo.GetDirectories().OrderBy(directory => directory.Name)
            .Select(CreateEntry));

        entries.AddRange(directoryInfo.GetFiles().OrderBy(directory => directory.Name)
            .Select(CreateEntry));
        
        return Task.FromResult(entries);
    }
    
    private static IEntry CreateEntry(DriveInfo driveInfo) => 
        new Entry(driveInfo.Name, EntryType.Container, driveInfo.TotalSize);

    private static IEntry CreateEntry(DirectoryInfo directoryInfo) => 
        new Entry(directoryInfo.Name, EntryType.Container, 0, directoryInfo.LastWriteTimeUtc);

    private static IEntry CreateEntry(FileInfo fileInfo) => 
        new Entry(fileInfo.Name, EntryType.Item, fileInfo.Length, fileInfo.LastWriteTimeUtc);

    private void SetCurrentPath(string? path)
    {
        _currentPath = path;
        _currentPathComponents = path == null
            ? []
            : Path.GetFullPath(path).Split(Path.DirectorySeparatorChar);
    }
}