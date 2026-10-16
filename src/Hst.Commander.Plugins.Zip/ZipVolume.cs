using System.IO.Compression;
using System.Runtime.CompilerServices;
using Hst.Commander.Abstractions;

namespace Hst.Commander.Plugins.Zip;

/// <summary>
/// Read-only volume browsing the entries of a zip archive. Directories are the directory entries of the archive and the
/// directories in the paths of its files, as archives don't need to contain entries for directories.
/// </summary>
public class ZipVolume : IVolume, IDisposable
{
    private readonly ZipArchive _zipArchive;
    private readonly ZipDirectory _root = new(string.Empty);
    private List<ZipDirectory> _currentDirectories = [];

    /// <param name="zipArchive">Archive opened for reading, disposed with the volume.</param>
    public ZipVolume(ZipArchive zipArchive)
    {
        ArgumentNullException.ThrowIfNull(zipArchive);
        _zipArchive = zipArchive;

        foreach (var zipArchiveEntry in zipArchive.Entries)
        {
            AddEntry(zipArchiveEntry);
        }
    }

    public string Name => "Zip";

    public IEnumerable<string> CurrentPathComponents => _currentDirectories.Select(directory => directory.Name).ToList();

    private ZipDirectory CurrentDirectory => _currentDirectories.Count > 0 ? _currentDirectories[^1] : _root;

    /// <summary>
    /// Adds an archive entry to the directory tree. Empty, "." and ".." path components are skipped, so entries can't
    /// point outside the archive.
    /// </summary>
    private void AddEntry(ZipArchiveEntry zipArchiveEntry)
    {
        var pathComponents = zipArchiveEntry.FullName
            .Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries)
            .Where(pathComponent => pathComponent != "." && pathComponent != Paths.Parent)
            .ToList();
        if (pathComponents.Count == 0)
        {
            return;
        }

        var isDirectory = zipArchiveEntry.FullName.EndsWith('/') || zipArchiveEntry.FullName.EndsWith('\\');
        var directoryCount = isDirectory ? pathComponents.Count : pathComponents.Count - 1;
        var directory = _root;
        for (var index = 0; index < directoryCount; index++)
        {
            directory = directory.GetOrAddDirectory(pathComponents[index]);
        }

        if (isDirectory)
        {
            directory.ModifiedDate = GetModifiedDate(zipArchiveEntry);
        }
        else
        {
            directory.Files[pathComponents[^1]] = zipArchiveEntry;
        }
    }

    /// <summary>Modified date of an archive entry, stored in local time without a time zone in zip archives.</summary>
    private static DateTime GetModifiedDate(ZipArchiveEntry zipArchiveEntry) =>
        zipArchiveEntry.LastWriteTime.UtcDateTime;

    public async IAsyncEnumerable<IEntry> ListEntries(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var directory = CurrentDirectory;
        foreach (var subDirectory in directory.Directories.Values.OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return CreateEntry(subDirectory);
        }

        foreach (var (name, zipArchiveEntry) in directory.Files.OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return CreateEntry(name, zipArchiveEntry);
        }

        await Task.CompletedTask;
    }

    private static IEntry CreateEntry(ZipDirectory directory) =>
        new ZipEntry(directory.Name, EntryType.Container, null, directory.ModifiedDate);

    private static IEntry CreateEntry(string name, ZipArchiveEntry zipArchiveEntry) =>
        new ZipEntry(name, EntryType.Item, zipArchiveEntry.Length, GetModifiedDate(zipArchiveEntry));

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

        return directory.FindFile(name) is (var fileName, var zipArchiveEntry)
            ? Task.FromResult(CreateEntry(fileName, zipArchiveEntry))
            : throw new IOException($"Entry '{name}' not found");
    }

    public Task<Stream> OpenRead(string name, CancellationToken cancellationToken = default) =>
        CurrentDirectory.FindFile(name) is (_, var zipArchiveEntry)
            ? Task.FromResult(zipArchiveEntry.Open())
            : throw new FileNotFoundException($"File '{name}' not found");

    public Task<Stream> OpenWrite(string name, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Zip archives are read-only");

    public Task CreateContainer(string name, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Zip archives are read-only");

    public Task Delete(string name, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Zip archives are read-only");

    public Task Rename(string name, string newName, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Zip archives are read-only");

    public Task SetModifiedDate(string name, DateTime modifiedDate, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Zip archives are read-only");

    public void Dispose()
    {
        _zipArchive.Dispose();
        GC.SuppressFinalize(this);
    }

    private sealed record ZipEntry(string Name, EntryType Type, long? Size, DateTime? ModifiedDate) : IEntry;

    /// <summary>
    /// Directory in the archive. Names are case-sensitive like in the archive, but found case-insensitive when no name
    /// matches exactly.
    /// </summary>
    private sealed class ZipDirectory(string name)
    {
        public string Name { get; } = name;

        public DateTime? ModifiedDate { get; set; }

        public Dictionary<string, ZipDirectory> Directories { get; } = new(StringComparer.Ordinal);

        public Dictionary<string, ZipArchiveEntry> Files { get; } = new(StringComparer.Ordinal);

        public ZipDirectory GetOrAddDirectory(string directoryName)
        {
            if (!Directories.TryGetValue(directoryName, out var directory))
            {
                directory = new ZipDirectory(directoryName);
                Directories[directoryName] = directory;
            }

            return directory;
        }

        public ZipDirectory? FindDirectory(string directoryName) =>
            Directories.TryGetValue(directoryName, out var directory)
                ? directory
                : Directories.Values.FirstOrDefault(x =>
                    x.Name.Equals(directoryName, StringComparison.OrdinalIgnoreCase));

        public (string Name, ZipArchiveEntry Entry)? FindFile(string fileName)
        {
            if (Files.TryGetValue(fileName, out var zipArchiveEntry))
            {
                return (fileName, zipArchiveEntry);
            }

            foreach (var (name, entry) in Files)
            {
                if (name.Equals(fileName, StringComparison.OrdinalIgnoreCase))
                {
                    return (name, entry);
                }
            }

            return null;
        }
    }
}
