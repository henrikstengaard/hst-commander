using System.Runtime.CompilerServices;
using Hst.Commander.Abstractions;

namespace Hst.Commander.Plugins.LhaLzx;

/// <summary>
/// Read-only volume browsing the entries of an archive. Directories are the directory entries of the archive and the
/// directories in the paths of its files, as archives don't need to contain entries for directories.
/// </summary>
/// <typeparam name="TArchiveEntry">Entry of the archive, read with <see cref="OpenArchiveEntry" />.</typeparam>
public abstract class ArchiveVolume<TArchiveEntry> : IVolume
{
    private readonly ArchiveDirectory _root = new(string.Empty);
    private List<ArchiveDirectory> _currentDirectories = [];

    protected ArchiveVolume(string name)
    {
        Name = name;
    }

    public string Name { get; }

    public IEnumerable<string> CurrentPathComponents => _currentDirectories.Select(directory => directory.Name).ToList();

    private ArchiveDirectory CurrentDirectory => _currentDirectories.Count > 0 ? _currentDirectories[^1] : _root;

    /// <summary>
    /// Adds an archive entry to the directory tree. "/" and "\" separate path components and a path ending with one is
    /// a directory. Empty, "." and ".." path components are skipped, so entries can't point outside the archive.
    /// </summary>
    protected void AddEntry(string path, long size, DateTime? modifiedDate, TArchiveEntry archiveEntry)
    {
        var pathComponents = path
            .Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries)
            .Where(pathComponent => pathComponent != "." && pathComponent != Paths.Parent)
            .ToList();
        if (pathComponents.Count == 0)
        {
            return;
        }

        var isDirectory = path.EndsWith('/') || path.EndsWith('\\');
        var directoryCount = isDirectory ? pathComponents.Count : pathComponents.Count - 1;
        var directory = _root;
        for (var index = 0; index < directoryCount; index++)
        {
            directory = directory.GetOrAddDirectory(pathComponents[index]);
        }

        if (isDirectory)
        {
            directory.ModifiedDate = modifiedDate;
        }
        else
        {
            directory.Files[pathComponents[^1]] = new ArchiveFile(size, modifiedDate, archiveEntry);
        }
    }

    /// <summary>Opens an archive entry for reading, decompressing it as it's read.</summary>
    protected abstract Task<Stream> OpenArchiveEntry(TArchiveEntry archiveEntry);

    public async IAsyncEnumerable<IEntry> ListEntries(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var directory = CurrentDirectory;
        foreach (var subDirectory in directory.Directories.Values.OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return CreateEntry(subDirectory);
        }

        foreach (var (name, file) in directory.Files.OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return CreateEntry(name, file);
        }

        await Task.CompletedTask;
    }

    private static IEntry CreateEntry(ArchiveDirectory directory) =>
        new ArchiveEntry(directory.Name, EntryType.Container, null, directory.ModifiedDate);

    private static IEntry CreateEntry(string name, ArchiveFile file) =>
        new ArchiveEntry(name, EntryType.Item, file.Size, file.ModifiedDate);

    public Task ChangeDirectory(IEnumerable<string> pathComponents, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(pathComponents);

        var directories = _currentDirectories.ToList();
        foreach (var pathComponent in pathComponents)
        {
            if (pathComponent.Equals(Paths.Root, StringComparison.OrdinalIgnoreCase))
            {
                directories.Clear();
            }
            else if (pathComponent.Equals(Paths.Parent, StringComparison.OrdinalIgnoreCase))
            {
                if (directories.Count > 0)
                {
                    directories.RemoveAt(directories.Count - 1);
                }
            }
            else
            {
                var directory = directories.Count > 0 ? directories[^1] : _root;
                directories.Add(directory.FindDirectory(pathComponent) ??
                                throw new DirectoryNotFoundException($"Directory '{pathComponent}' not found"));
            }
        }

        _currentDirectories = directories;
        return Task.CompletedTask;
    }

    public Task<IEntry> OpenEntry(string name, CancellationToken cancellationToken = default)
    {
        var directory = CurrentDirectory;
        if (directory.FindDirectory(name) is { } subDirectory)
        {
            _currentDirectories.Add(subDirectory);
            return Task.FromResult(CreateEntry(subDirectory));
        }

        return directory.FindFile(name) is (var fileName, var file)
            ? Task.FromResult(CreateEntry(fileName, file))
            : throw new IOException($"Entry '{name}' not found");
    }

    public Task<Stream> OpenRead(string name, CancellationToken cancellationToken = default) =>
        CurrentDirectory.FindFile(name) is (_, var file)
            ? OpenArchiveEntry(file.ArchiveEntry)
            : throw new FileNotFoundException($"File '{name}' not found");

    public Task<Stream> OpenWrite(string name, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException($"{Name} archives are read-only");

    public Task CreateContainer(string name, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException($"{Name} archives are read-only");

    public Task Delete(string name, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException($"{Name} archives are read-only");

    public Task Rename(string name, string newName, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException($"{Name} archives are read-only");

    public Task SetModifiedDate(string name, DateTime modifiedDate, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException($"{Name} archives are read-only");

    private sealed record ArchiveEntry(string Name, EntryType Type, long? Size, DateTime? ModifiedDate) : IEntry;

    private sealed record ArchiveFile(long Size, DateTime? ModifiedDate, TArchiveEntry ArchiveEntry);

    /// <summary>
    /// Directory in the archive. Names are case-sensitive like in the archive, but found case-insensitive when no name
    /// matches exactly.
    /// </summary>
    private sealed class ArchiveDirectory(string name)
    {
        public string Name { get; } = name;

        public DateTime? ModifiedDate { get; set; }

        public Dictionary<string, ArchiveDirectory> Directories { get; } = new(StringComparer.Ordinal);

        public Dictionary<string, ArchiveFile> Files { get; } = new(StringComparer.Ordinal);

        public ArchiveDirectory GetOrAddDirectory(string directoryName)
        {
            if (!Directories.TryGetValue(directoryName, out var directory))
            {
                directory = new ArchiveDirectory(directoryName);
                Directories[directoryName] = directory;
            }

            return directory;
        }

        public ArchiveDirectory? FindDirectory(string directoryName) =>
            Directories.TryGetValue(directoryName, out var directory)
                ? directory
                : Directories.Values.FirstOrDefault(x =>
                    x.Name.Equals(directoryName, StringComparison.OrdinalIgnoreCase));

        public (string Name, ArchiveFile File)? FindFile(string fileName)
        {
            if (Files.TryGetValue(fileName, out var file))
            {
                return (fileName, file);
            }

            foreach (var (name, archiveFile) in Files)
            {
                if (name.Equals(fileName, StringComparison.OrdinalIgnoreCase))
                {
                    return (name, archiveFile);
                }
            }

            return null;
        }
    }
}
