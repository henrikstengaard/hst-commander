using System.Runtime.CompilerServices;

namespace Hst.Commander.Abstractions;

/// <summary>
/// Base class for read-only volumes browsing the entries of an archive (e.g. zip, lha, rar). A derived class adds the
/// archive's entries with <see cref="AddEntry" /> and opens them with <see cref="OpenArchiveEntry" />. Directories are
/// the directory entries of the archive and the directories in the paths of its files, as archives don't need to
/// contain entries for directories. Names are case-sensitive like in the archive, but found case-insensitive when no
/// name matches exactly.
/// </summary>
/// <typeparam name="TArchiveEntry">Entry of the archive, read with <see cref="OpenArchiveEntry" />.</typeparam>
public abstract class ArchiveVolume<TArchiveEntry> : IVolume
{
    private readonly bool _supportsAmigaMetadata;
    private readonly ArchiveDirectory _root = new(string.Empty);
    private List<ArchiveDirectory> _currentDirectories = [];
    private long _fileCount;

    /// <param name="name">Name of the volume, e.g. "Zip", also used in the read-only messages.</param>
    /// <param name="supportsAmigaMetadata">
    /// True when the archive stores Amiga names, protection bits and comments as they are (e.g. lha and lzx archives),
    /// given to <see cref="AddEntry" />, so copying from it keeps them like copying from an Amiga volume.
    /// </param>
    protected ArchiveVolume(string name, bool supportsAmigaMetadata = false)
    {
        Name = name;
        _supportsAmigaMetadata = supportsAmigaMetadata;
    }

    public string Name { get; }

    public IEnumerable<string> CurrentPathComponents => _currentDirectories.Select(directory => directory.Name).ToList();

    private ArchiveDirectory CurrentDirectory => _currentDirectories.Count > 0 ? _currentDirectories[^1] : _root;

    /// <summary>
    /// True when files are read faster in the order they are added with <see cref="AddEntry" /> (the archive's order),
    /// e.g. in a solid archive; <see cref="GetReadOrder" /> then returns their position, so copying reads them in this
    /// order. Defaults to false.
    /// </summary>
    protected virtual bool ReadsInArchiveOrder => false;

    /// <summary>
    /// Adds an archive entry to the directory tree. "/" and "\" separate path components and a path ending with one is
    /// a directory. Empty, "." and ".." path components are skipped, so entries can't point outside the archive. A
    /// later file with the same path replaces an earlier one.
    /// </summary>
    /// <param name="path">Path of the entry in the archive.</param>
    /// <param name="size">Size of a file, ignored for directories.</param>
    /// <param name="modifiedDate">Modified date of the entry, preferably in UTC.</param>
    /// <param name="archiveEntry">Entry given to <see cref="OpenArchiveEntry" /> when the file is read.</param>
    /// <param name="amigaMetadata">Amiga protection bits and comment of the entry, if the archive stores them.</param>
    /// <param name="isDirectory">True when the entry is a directory without a path separator at the end.</param>
    protected void AddEntry(string path, long size, DateTime? modifiedDate, TArchiveEntry archiveEntry,
        AmigaMetadata? amigaMetadata = null, bool isDirectory = false)
    {
        ArgumentNullException.ThrowIfNull(path);

        var pathComponents = path
            .Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries)
            .Where(pathComponent => pathComponent != "." && pathComponent != Paths.Parent)
            .ToList();
        if (pathComponents.Count == 0)
        {
            return;
        }

        isDirectory = isDirectory || path.EndsWith('/') || path.EndsWith('\\');
        var directoryCount = isDirectory ? pathComponents.Count : pathComponents.Count - 1;
        var directory = _root;
        for (var index = 0; index < directoryCount; index++)
        {
            directory = directory.GetOrAddDirectory(pathComponents[index]);
        }

        if (isDirectory)
        {
            directory.ModifiedDate = modifiedDate;
            directory.AmigaMetadata = amigaMetadata;
        }
        else
        {
            directory.Files[pathComponents[^1]] =
                new ArchiveFile(size, modifiedDate, archiveEntry, amigaMetadata, _fileCount++);
        }
    }

    /// <summary>
    /// Opens an archive entry for reading, decompressing it as it's read. Throw <see cref="NotSupportedException" />
    /// when the entry can't be read, e.g. when it's encrypted.
    /// </summary>
    /// <param name="name">Name of the file in the current directory, for error messages.</param>
    /// <param name="archiveEntry">Entry given to <see cref="AddEntry" />.</param>
    /// <param name="cancellationToken"></param>
    protected abstract Task<Stream> OpenArchiveEntry(string name, TArchiveEntry archiveEntry,
        CancellationToken cancellationToken);

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
        new ArchiveEntry(directory.Name, EntryType.Container, null, directory.ModifiedDate, directory.AmigaMetadata);

    private static IEntry CreateEntry(string name, ArchiveFile file) =>
        new ArchiveEntry(name, EntryType.Item, file.Size, file.ModifiedDate, file.AmigaMetadata);

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

        return directory.FindFile(name) is var (fileName, file)
            ? Task.FromResult(CreateEntry(fileName, file))
            : throw new IOException($"Entry '{name}' not found");
    }

    public Task<Stream> OpenRead(string name, CancellationToken cancellationToken = default) =>
        CurrentDirectory.FindFile(name) is var (fileName, file)
            ? OpenArchiveEntry(fileName, file.ArchiveEntry, cancellationToken)
            : throw new FileNotFoundException($"File '{name}' not found");

    /// <summary>Position of a file in the archive's order when <see cref="ReadsInArchiveOrder" />, otherwise null.</summary>
    public long? GetReadOrder(string name) =>
        ReadsInArchiveOrder && CurrentDirectory.FindFile(name) is var (_, file) ? file.Index : null;

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

    public Task<bool> SupportsAmigaMetadata(CancellationToken cancellationToken = default) =>
        Task.FromResult(_supportsAmigaMetadata);

    public Task SetAmigaMetadata(string name, AmigaMetadata metadata, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException($"{Name} archives are read-only");

    private sealed record ArchiveEntry(string Name, EntryType Type, long? Size, DateTime? ModifiedDate,
        AmigaMetadata? AmigaMetadata) : IEntry;

    private sealed record ArchiveFile(long Size, DateTime? ModifiedDate, TArchiveEntry ArchiveEntry,
        AmigaMetadata? AmigaMetadata, long Index);

    private sealed class ArchiveDirectory(string name)
    {
        public string Name { get; } = name;

        public DateTime? ModifiedDate { get; set; }

        public AmigaMetadata? AmigaMetadata { get; set; }

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
