using System.Runtime.CompilerServices;
using Hst.Commander.Extensions.Volume.Abstractions;

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