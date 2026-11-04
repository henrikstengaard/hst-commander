using Hst.Commander.Abstractions;
using Hst.Commander.Core.Volumes;

namespace Hst.Commander.Core;

/// <summary>
///     A file or directory of entries dragged out of a volume, see <see cref="VolumeDragSource.ListFilesAsync" />.
/// </summary>
/// <param name="RelativePath">
///     Path below the drop location with local names, separated by <see cref="VolumeDragSource.Separator" />, e.g.
///     "dir1\file1.txt".
/// </param>
/// <param name="IsDirectory">True for a directory, false for a file.</param>
/// <param name="Size">Size of a file in bytes, 0 for directories.</param>
/// <param name="ModifiedDate">Modified date, null when unknown.</param>
/// <param name="PathComponents">Directories of the file below the dragged entries' location in the volume.</param>
/// <param name="Name">Name of the file in the volume.</param>
/// <param name="Contents">
///     Contents of a UAE metadata file made for the dragged entries ("_UAEFSDB.___" or ".uaem"), null for entries of
///     the volume.
/// </param>
public sealed record DragFile(
    string RelativePath,
    bool IsDirectory,
    long Size,
    DateTime? ModifiedDate,
    IReadOnlyList<string> PathComponents,
    string Name,
    byte[]? Contents = null);

/// <summary>
///     Entries dragged out of an image or another volume that isn't a local directory to another application, which
///     reads them when they are dropped (Windows virtual files, macOS file promises), so they needn't be copied first.
///     The entries are read through a volume of their own at the pane's location (see
///     <see cref="NavigationContext.OpenLocationAsync" />), so the pane can keep navigating; reading is serialized,
///     as volumes aren't thread safe, and runs on the thread pool. Amiga names, protection bits and comments are
///     dragged as UAE metadata files like copying (<see cref="NavigationContext.UaeMetadata" />): written for
///     entries of volumes storing Amiga metadata, and for entries with UAE metadata in other volumes, whose metadata
///     files aren't dragged themselves. Without UAE metadata, names are local names. Disposing closes the volume.
/// </summary>
public sealed class VolumeDragSource : IDisposable
{
    /// <summary>Separator of <see cref="DragFile.RelativePath" />, as Windows virtual files use.</summary>
    public const char Separator = '\\';

    private readonly VolumeLocation _location;
    private readonly IReadOnlyList<string> _basePathComponents;
    private readonly UaeMetadataFormat _uaeMetadata;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private IReadOnlyList<DragFile>? _files;
    private bool _isDisposed;

    private VolumeDragSource(VolumeLocation location, IReadOnlyList<IEntry> entries, UaeMetadataFormat uaeMetadata)
    {
        _location = location;
        _uaeMetadata = uaeMetadata;
        Entries = entries;
        _basePathComponents = location.Volume.CurrentPathComponents
            .Where(component => !component.Equals(Paths.Root, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    /// <summary>The dragged entries, in the location of the pane when the drag started.</summary>
    public IReadOnlyList<IEntry> Entries { get; }

    /// <summary>
    ///     Opens a volume of its own at the current location of a pane for dragging entries out of it, when the drag
    ///     starts (the pane doesn't change while the mouse button is held). It's opened on the thread pool like copying,
    ///     so the drag can wait for it on the UI thread, where the drop target may ask for the files.
    /// </summary>
    public static async Task<VolumeDragSource> OpenAsync(NavigationContext context, IReadOnlyList<IEntry> entries,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(entries);

        var uaeMetadata = context.UaeMetadata;
        var location = await Task.Run(() => context.OpenLocationAsync(cancellationToken), cancellationToken)
            .ConfigureAwait(false);
        return new VolumeDragSource(location, entries
            .Where(entry => !entry.Name.Equals(Paths.Parent, StringComparison.Ordinal))
            .ToList(), uaeMetadata);
    }

    /// <summary>Local name of a dragged entry, e.g. "file_1" for the Amiga name "file*1".</summary>
    public static string GetLocalName(IEntry entry) => UaeMetadataWriter.GetLocalName(entry.Name);

    /// <summary>
    ///     Lists the dragged entries with everything in their directories, directories before what's in them, and
    ///     the UAE metadata files of each directory after its entries, read once. Names are the names copying gives
    ///     the entries; local names used twice in a directory get a number, e.g. "file_1 (2)".
    /// </summary>
    public async Task<IReadOnlyList<DragFile>> ListFilesAsync(CancellationToken cancellationToken = default)
    {
        if (_files is { } files)
        {
            return files;
        }

        return await RunLockedAsync(async () =>
        {
            if (_files != null)
            {
                return _files;
            }

            var list = new List<DragFile>();
            await ChangeDirectoryAsync([], cancellationToken);
            var metadataMode = _uaeMetadata == UaeMetadataFormat.None
                ? MetadataMode.None
                : await _location.Volume.SupportsAmigaMetadata(cancellationToken)
                    ? MetadataMode.Write
                    : MetadataMode.Copy;
            await AddFilesAsync(list, Entries, metadataMode, [], string.Empty, cancellationToken);
            _files = list;
            return list;
        });
    }

    /// <summary>How Amiga names, protection bits and comments are dragged, like copying to a local directory.</summary>
    private enum MetadataMode
    {
        /// <summary>Not at all, entries get local names.</summary>
        None,

        /// <summary>The volume stores them, they are written as UAE metadata.</summary>
        Write,

        /// <summary>
        ///     The volume doesn't store them: UAE metadata of the dragged entries is read in their directories and
        ///     written again, without dragging the metadata files.
        /// </summary>
        Copy
    }

    private async Task AddFilesAsync(List<DragFile> files, IReadOnlyList<IEntry> entries, MetadataMode metadataMode,
        IReadOnlyList<string> pathComponents, string relativeDirectory, CancellationToken cancellationToken)
    {
        var metadata = UaeDirectoryMetadata.Empty;
        if (metadataMode == MetadataMode.Copy)
        {
            var directoryEntries = await _location.Volume.ListEntries(cancellationToken).ToListAsync(cancellationToken);
            metadata = await UaeMetadataReader.ReadAsync(_location.Volume, directoryEntries, true, cancellationToken);
        }

        // the metadata files are made in memory, as if the directory was copied to an empty directory
        var metadataVolume = new MetadataFileVolume();
        var writer = metadataMode == MetadataMode.None
            ? null
            : await UaeMetadataWriter.CreateAsync(metadataVolume, _uaeMetadata, cancellationToken);
        var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var isDirectory = entry.Type == EntryType.Container;
            if (!isDirectory && metadata.IsMetadataFile(entry.Name))
            {
                continue;
            }

            var node = metadata.Find(entry.Name);
            var amigaName = node?.AmigaName ?? entry.Name;
            var entryWriter = metadataMode == MetadataMode.Write || node != null ? writer : null;
            var name = entryWriter?.GetName(amigaName) ??
                       GetUniqueName(UaeMetadataWriter.GetLocalName(entry.Name), usedNames);
            usedNames.Add(name);
            var relativePath = relativeDirectory.Length == 0 ? name : relativeDirectory + Separator + name;
            files.Add(new DragFile(relativePath, isDirectory, isDirectory ? 0 : entry.Size ?? 0, entry.ModifiedDate,
                pathComponents, entry.Name));
            if (entryWriter != null)
            {
                await entryWriter.SetAsync(metadataVolume, amigaName, name, entry.AmigaMetadata ?? node?.Metadata,
                    entry.ModifiedDate, cancellationToken);
            }

            if (!isDirectory)
            {
                continue;
            }

            await _location.Volume.ChangeDirectory([entry.Name], cancellationToken);
            var children = await _location.Volume.ListEntries(cancellationToken)
                .Where(child => !child.Name.Equals(Paths.Parent, StringComparison.Ordinal))
                .ToListAsync(cancellationToken);
            await AddFilesAsync(files, children, metadataMode, [..pathComponents, entry.Name], relativePath,
                cancellationToken);
            await _location.Volume.ChangeDirectory([Paths.Parent], cancellationToken);
        }

        if (writer == null)
        {
            return;
        }

        await writer.FlushAsync(metadataVolume, cancellationToken);
        foreach (var (name, contents) in metadataVolume.Files)
        {
            var relativePath = relativeDirectory.Length == 0 ? name : relativeDirectory + Separator + name;
            files.Add(new DragFile(relativePath, false, contents.Length, DateTime.Now, pathComponents, name,
                contents));
        }
    }

    private static string GetUniqueName(string name, HashSet<string> usedNames)
    {
        var uniqueName = name;
        for (var number = 2; !usedNames.Add(uniqueName); number++)
        {
            uniqueName = Path.GetFileNameWithoutExtension(name) + $" ({number})" + Path.GetExtension(name);
        }

        return uniqueName;
    }

    /// <summary>Opens a file of <see cref="ListFilesAsync" /> for reading.</summary>
    public Task<Stream> OpenReadAsync(DragFile file, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(file);
        if (file.IsDirectory)
        {
            throw new IOException($"'{file.RelativePath}' is a directory");
        }

        if (file.Contents is { } contents)
        {
            return Task.FromResult<Stream>(new MemoryStream(contents, false));
        }

        return RunLockedAsync(async () =>
        {
            await ChangeDirectoryAsync(file.PathComponents, cancellationToken);
            return await _location.Volume.OpenRead(file.Name, cancellationToken);
        });
    }

    /// <summary>
    ///     Copies a dragged entry, a directory with everything in it, to a local path, e.g. where a macOS file promise
    ///     is dropped. The entry is copied into the directory of the path like copying, with UAE metadata
    ///     (<see cref="NavigationContext.UaeMetadata" />) written there, and gets the path's name, unless it has UAE
    ///     metadata (e.g. an Amiga name or a comment), which refers to the name copying gave it.
    /// </summary>
    public Task CopyToLocalAsync(IEntry entry, string destinationPath, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(destinationPath);

        return RunLockedAsync<bool>(async () =>
        {
            var directory = Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(destinationPath)) ??
                            throw new IOException($"'{destinationPath}' has no directory");
            Directory.CreateDirectory(directory);
            await ChangeDirectoryAsync([], cancellationToken);
            var localVolume = new LocalVolume(directory);
            await _location.Volume.CopyToAsync([entry], localVolume,
                new CopyOptions(EntryFilter.All, null, null, _uaeMetadata), null, cancellationToken);

            // copied with its local name, or its name when it has no invalid characters for the volume
            var copiedPath = new[] { GetLocalName(entry), entry.Name }
                .Select(name => Path.Combine(directory, name))
                .FirstOrDefault(path => File.Exists(path) || Directory.Exists(path));
            var fullPath = Path.GetFullPath(destinationPath);

            // an entry with UAE metadata keeps its name, which the metadata refers to
            var hasMetadata = false;
            if (copiedPath != null && _uaeMetadata != UaeMetadataFormat.None)
            {
                var localEntries = await localVolume.ListEntries(cancellationToken).ToListAsync(cancellationToken);
                var metadata = await UaeMetadataReader.ReadAsync(localVolume, localEntries, false, cancellationToken);
                hasMetadata = metadata.Find(Path.GetFileName(copiedPath)) != null;
            }

            if (copiedPath != null && !hasMetadata && !copiedPath.Equals(fullPath, StringComparison.Ordinal) &&
                !File.Exists(fullPath) && !Directory.Exists(fullPath))
            {
                if (Directory.Exists(copiedPath))
                {
                    Directory.Move(copiedPath, fullPath);
                }
                else
                {
                    File.Move(copiedPath, fullPath);
                }
            }

            return true;
        });
    }

    /// <summary>An empty directory keeping the UAE metadata files written to it in memory.</summary>
    private sealed class MetadataFileVolume : IVolume
    {
        private readonly Dictionary<string, byte[]> _files = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>The files written, by name.</summary>
        public IEnumerable<(string Name, byte[] Contents)> Files => _files.Select(pair => (pair.Key, pair.Value));

        public string Name => string.Empty;

        public IEnumerable<string> CurrentPathComponents => [];

        public IAsyncEnumerable<IEntry> ListEntries(CancellationToken cancellationToken = default) =>
            AsyncEnumerable.Empty<IEntry>();

        public Task ChangeDirectory(IEnumerable<string> pathComponents,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<IEntry> OpenEntry(string name, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<Stream> OpenRead(string name, CancellationToken cancellationToken = default) =>
            Task.FromResult<Stream>(new MemoryStream(_files[name], false));

        public Task<Stream> OpenWrite(string name, CancellationToken cancellationToken = default) =>
            Task.FromResult<Stream>(new MetadataFileStream(contents => _files[name] = contents));

        public Task CreateContainer(string name, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task Delete(string name, CancellationToken cancellationToken = default)
        {
            _files.Remove(name);
            return Task.CompletedTask;
        }

        public Task Rename(string name, string newName, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task SetModifiedDate(string name, DateTime modifiedDate,
            CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    /// <summary>A file written in memory, stored when it is closed.</summary>
    private sealed class MetadataFileStream(Action<byte[]> closed) : MemoryStream
    {
        private bool _isClosed;

        protected override void Dispose(bool disposing)
        {
            if (disposing && !_isClosed)
            {
                _isClosed = true;
                closed(ToArray());
            }

            base.Dispose(disposing);
        }
    }

    private Task ChangeDirectoryAsync(IReadOnlyList<string> pathComponents, CancellationToken cancellationToken) =>
        _location.Volume.ChangeDirectory([Paths.Root, .._basePathComponents, ..pathComponents], cancellationToken);

    /// <summary>
    ///     Runs a volume operation on the thread pool (the lock of a shared image is only held there), one at a time.
    /// </summary>
    private Task<T> RunLockedAsync<T>(Func<Task<T>> operation) => Task.Run(async () =>
    {
        await _lock.WaitAsync();
        try
        {
            ObjectDisposedException.ThrowIf(_isDisposed, this);
            return await operation();
        }
        finally
        {
            _lock.Release();
        }
    });

    public void Dispose()
    {
        _lock.Wait();
        try
        {
            if (_isDisposed)
            {
                return;
            }

            _isDisposed = true;
            _location.Dispose();
        }
        finally
        {
            _lock.Release();
        }
    }
}
