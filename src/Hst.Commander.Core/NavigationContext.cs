using DiscUtils.Iso9660;
using Hst.Commander.Core.Volumes;
using Hst.Commander.Extensions.Volume.Abstractions;

namespace Hst.Commander.Core;

public sealed class NavigationContext : IDisposable
{
    private readonly LocalVolume _localVolume;
    private readonly NestedVolume _nestedVolume;
    private readonly Stack<Stream> _isoStreams = new();
    private readonly Dictionary<string, IEntry> _markedEntries = new(StringComparer.Ordinal);
    private Iso9660Volume? _isoVolume;
    private string? _isoPath;

    public NavigationContext(string initialPath)
    {
        _localVolume = new LocalVolume(initialPath);
        _nestedVolume = new NestedVolume(_localVolume);
    }

    /// <summary>
    ///     Name of the entry that was left when navigating to the parent, e.g. "dir1" when going up from
    ///     "c:	emp\dir1". Front-ends select this entry after the parent is listed. Null after other navigation.
    /// </summary>
    public string? PreviousEntryName { get; private set; }

    /// <summary>
    ///     Entries selected (marked) by the user in the current location, e.g. for file operations. The entry under the
    ///     cursor is not marked unless toggled. Cleared when the location changes.
    /// </summary>
    public IReadOnlyCollection<IEntry> MarkedEntries => _markedEntries.Values;

    /// <summary>Total size of the marked entries in bytes. Entries without a size, like directories, count as 0.</summary>
    public long MarkedSize => _markedEntries.Values.Sum(entry => entry.Size ?? 0);

    /// <summary>
    ///     Summary of the marked entries shown below the current path, e.g. "12K in 3 selected".
    ///     Null when no entries are marked.
    /// </summary>
    public string? MarkedSummary => _markedEntries.Count == 0
        ? null
        : $"{SizeFormatter.FormatHumanized(MarkedSize)} in {_markedEntries.Count} selected";

    public bool IsMarked(IEntry entry) => _markedEntries.ContainsKey(entry.Name);

    /// <summary>Toggles the mark of an entry. The parent entry ".." can't be marked. Returns true when marked.</summary>
    public bool ToggleMark(IEntry entry)
    {
        if (entry.Name.Equals(Paths.Parent, StringComparison.Ordinal))
        {
            return false;
        }

        if (_markedEntries.Remove(entry.Name))
        {
            return false;
        }

        _markedEntries[entry.Name] = entry;
        return true;
    }

    /// <summary>Clears the marks of all entries, e.g. after the marked entries are copied.</summary>
    public void ClearMarks() => _markedEntries.Clear();

    /// <summary>
    ///     Entries a file operation like copy applies to: the marked entries or, when none are marked, the entry under
    ///     the cursor. The parent entry ".." is never included.
    /// </summary>
    public IReadOnlyList<IEntry> GetOperationEntries(IEntry? currentEntry)
    {
        if (_markedEntries.Count > 0)
        {
            return _markedEntries.Values.ToList();
        }

        return currentEntry == null || currentEntry.Name.Equals(Paths.Parent, StringComparison.Ordinal)
            ? []
            : [currentEntry];
    }

    /// <summary>Description of the entries to copy shown in the copy dialog, e.g. "Copy 3 entries" or "Copy 'a.txt'".</summary>
    public static string GetCopyDescription(IReadOnlyCollection<IEntry> entries) => entries.Count == 1
        ? $"Copy '{entries.First().Name}'"
        : $"Copy {entries.Count} entries";

    /// <summary>Question shown in the delete confirm dialog, e.g. "Delete 3 entries?" or "Delete 'a.txt'?".</summary>
    public static string GetDeleteDescription(IReadOnlyCollection<IEntry> entries) => entries.Count == 1
        ? $"Delete '{entries.First().Name}'?"
        : $"Delete {entries.Count} entries?";

    public bool HasParent =>_nestedVolume.CurrentPathComponents.Any();

    public string CurrentPath
    {
        get
        {
            var pathComponents = _nestedVolume.CurrentPathComponents.ToArray();
            return pathComponents.Length == 0
                ? "Computer"
                : string.Join(Path.DirectorySeparatorChar, pathComponents);
        }
    }

    /// <summary>Local directory of the current path or containing the current image. Null in the drive list.</summary>
    private string? LocalPath => _localVolume.CurrentPathComponents.Any()
        ? string.Join(Path.DirectorySeparatorChar, _localVolume.CurrentPathComponents)
        : null;

    /// <summary>
    ///     Free and total space of the local drive at the current path formatted as "free/total", e.g. "87G/256G".
    ///     Null when listing drives, inside an image or when the drive is unavailable.
    /// </summary>
    public string? DiskSpace
    {
        get
        {
            if (_isoStreams.Count > 0 || !_localVolume.CurrentPathComponents.Any())
            {
                return null;
            }

            try
            {
                var currentPath = Path.GetFullPath(
                    string.Join(Path.DirectorySeparatorChar, _localVolume.CurrentPathComponents));
                var driveInfo = FindDrive(currentPath);
                return driveInfo is { IsReady: true }
                    ? $"{SizeFormatter.FormatHumanized(driveInfo.AvailableFreeSpace)}/{SizeFormatter.FormatHumanized(driveInfo.TotalSize)}"
                    : null;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
            {
                return null;
            }
        }
    }

    /// <summary>Finds the drive or mount point with the longest root directory containing the path.</summary>
    private static DriveInfo? FindDrive(string path)
    {
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        return DriveInfo.GetDrives()
            .Where(drive => IsInDirectory(path, drive.RootDirectory.FullName, comparison))
            .MaxBy(drive => drive.RootDirectory.FullName.Length);
    }

    private static bool IsInDirectory(string path, string directory, StringComparison comparison)
    {
        var directoryWithSeparator = Path.EndsInDirectorySeparator(directory)
            ? directory
            : directory + Path.DirectorySeparatorChar;
        return path.Equals(directory, comparison) ||
               (path + Path.DirectorySeparatorChar).StartsWith(directoryWithSeparator, comparison);
    }

    public async Task<IReadOnlyList<IEntry>> ListEntriesAsync(CancellationToken cancellationToken = default)
    {
        var entries = new List<IEntry>();

        if (HasParent)
        {
            entries.Add(new Entry(Paths.Parent, EntryType.Container));
        }

        await foreach (var entry in _nestedVolume.ListEntries(cancellationToken))
        {
            entries.Add(entry);
        }

        // keep marks of entries still present, refreshed with the listed entries so sizes are current
        var markedEntries = entries.Where(IsMarked).ToList();
        _markedEntries.Clear();
        foreach (var entry in markedEntries)
        {
            _markedEntries[entry.Name] = entry;
        }

        return entries;
    }

    /// <summary>
    ///     Index of the entry named <see cref="PreviousEntryName" /> in the entries, or -1 when not found.
    ///     Trailing directory separators are ignored, so a drive root like "C:" matches the drive entry "C:\".
    /// </summary>
    public int IndexOfPreviousEntry(IReadOnlyList<IEntry> entries)
    {
        if (PreviousEntryName == null)
        {
            return -1;
        }

        for (var index = 0; index < entries.Count; index++)
        {
            if (Path.TrimEndingDirectorySeparator(entries[index].Name)
                .Equals(PreviousEntryName, StringComparison.Ordinal))
            {
                return index;
            }
        }

        return -1;
    }

    /// <summary>
    ///     Index of the entry to select after listing entries. When the location changed since
    ///     <paramref name="previousPath" />, the entry left when going to the parent is selected, otherwise the first
    ///     entry. When the location is unchanged, the selected index is kept within the entries. -1 when empty.
    /// </summary>
    public int GetSelectedIndex(IReadOnlyList<IEntry> entries, string? previousPath, int selectedIndex)
    {
        if (entries.Count == 0)
        {
            return -1;
        }

        if (CurrentPath.Equals(previousPath, StringComparison.Ordinal))
        {
            return Math.Clamp(selectedIndex, 0, entries.Count - 1);
        }

        return Math.Max(IndexOfPreviousEntry(entries), 0);
    }

    public async Task OpenEntryAsync(IEntry entry, CancellationToken cancellationToken = default)
    {
        if (entry.Name.Equals(Paths.Parent, StringComparison.Ordinal))
        {
            await GoParentAsync(cancellationToken);
            return;
        }

        if (entry.Type == EntryType.Container)
        {
            await _nestedVolume.ChangeDirectory([entry.Name], cancellationToken);
            PreviousEntryName = null;
            _markedEntries.Clear();
            return;
        }

        if (!entry.Name.EndsWith(".iso", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (_isoStreams.Count > 0)
        {
            throw new IOException("Opening ISO files from inside another ISO image is not supported.");
        }

        var currentPath = string.Join(Path.DirectorySeparatorChar, _localVolume.CurrentPathComponents);
        var isoPath = Path.Combine(currentPath, entry.Name);
        var isoStream = File.OpenRead(isoPath);

        try
        {
            var isoVolume = new Iso9660Volume(new CDReader(isoStream, true));
            _nestedVolume.PushVolume(new EntryVolumeProvider(entry, isoVolume));
            _isoStreams.Push(isoStream);
            _isoVolume = isoVolume;
            _isoPath = isoPath;
            PreviousEntryName = null;
            _markedEntries.Clear();
        }
        catch
        {
            isoStream.Dispose();
            throw;
        }
    }

    public async Task GoParentAsync(CancellationToken cancellationToken = default)
    {
        // the last non-empty path component is the entry being left, e.g. "C:" for the drive root "C:\"
        var previousEntryName = _nestedVolume.CurrentPathComponents
            .LastOrDefault(pathComponent => pathComponent.Length > 0);

        if (_isoStreams.Count > 0 && _nestedVolume.CurrentVolumeIsAtRoot)
        {
            _nestedVolume.PopVolume();
            _isoStreams.Pop().Dispose();
            _isoVolume = null;
            _isoPath = null;
        }
        else
        {
            await _nestedVolume.ChangeDirectory([Paths.Parent], cancellationToken);
        }

        PreviousEntryName = previousEntryName;
        _markedEntries.Clear();
    }

    /// <summary>
    ///     Opens a volume of its own at the current location, so it can be used, e.g. for copying, without changing the
    ///     current directory of this context. An image is opened again from its file.
    /// </summary>
    public async Task<VolumeLocation> OpenLocationAsync(CancellationToken cancellationToken = default)
    {
        if (_isoVolume != null && _isoPath != null)
        {
            var isoStream = File.OpenRead(_isoPath);
            try
            {
                var isoVolume = new Iso9660Volume(new CDReader(isoStream, true));
                await isoVolume.ChangeDirectory(_isoVolume.CurrentPathComponents.ToList(), cancellationToken);
                return new VolumeLocation(isoVolume, CurrentPath, isoStream);
            }
            catch
            {
                await isoStream.DisposeAsync();
                throw;
            }
        }

        var localPath = LocalPath ?? throw new IOException("The drive list is not a directory");
        return CreateLocalLocation(localPath);
    }

    /// <summary>
    ///     Copies entries in the current location to a destination. When the destination path is the current path of
    ///     <paramref name="destinationContext" /> (the other pane), entries are copied to its location, which may be
    ///     inside an image. Otherwise the destination is a local directory; a relative path is relative to the current
    ///     local directory and a missing directory is created. A single file copied to a path that isn't a directory
    ///     is copied with that name. Existing files are overwritten only when <paramref name="confirmOverwrite" />
    ///     answers yes or all (or is null).
    /// </summary>
    public async Task CopyAsync(IReadOnlyCollection<IEntry> entries, NavigationContext destinationContext,
        string destinationPath, string filter, IProgress<CopyProgress>? progress = null,
        Func<OverwriteRequest, CancellationToken, Task<OverwriteAnswer>>? confirmOverwrite = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(destinationContext);

        using var source = await OpenLocationAsync(cancellationToken);
        var (destination, targetName) = await OpenDestinationAsync(entries, destinationContext, destinationPath,
            cancellationToken);
        using (destination)
        {
            ThrowIfCopyingOntoItself(source.Path, entries, destination.Path, targetName);
            await source.Volume.CopyToAsync(entries, destination.Volume,
                new CopyOptions(filter, targetName, confirmOverwrite), progress, cancellationToken);
        }
    }

    /// <summary>
    ///     Deletes entries in the current location, directories with everything in them. Like copying, it uses a volume
    ///     of its own, so it can run on a background thread. Images are read-only and throw
    ///     <see cref="NotSupportedException" />.
    /// </summary>
    public async Task DeleteAsync(IReadOnlyCollection<IEntry> entries, IProgress<DeleteProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entries);

        using var location = await OpenLocationAsync(cancellationToken);
        await location.Volume.DeleteAsync(entries, progress, cancellationToken);
    }

    private async Task<(VolumeLocation Location, string? TargetName)> OpenDestinationAsync(
        IReadOnlyCollection<IEntry> entries, NavigationContext destinationContext, string destinationPath,
        CancellationToken cancellationToken)
    {
        var path = destinationPath.Trim();
        if (path.Length == 0)
        {
            throw new IOException("Destination is empty");
        }

        if (path.Equals(destinationContext.CurrentPath, StringComparison.Ordinal))
        {
            return (await destinationContext.OpenLocationAsync(cancellationToken), null);
        }

        var basePath = LocalPath;
        if (basePath == null && !Path.IsPathRooted(path))
        {
            throw new IOException($"Destination '{path}' is not a full path");
        }

        var fullPath = Path.GetFullPath(basePath == null ? path : Path.Combine(basePath, path));
        if (Directory.Exists(fullPath))
        {
            return (CreateLocalLocation(fullPath), null);
        }

        // a single file copied to a path that isn't a directory, e.g. "c:\temp\b.txt", is copied as that file
        var parentPath = Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(fullPath));
        if (entries.Count == 1 && entries.First().Type == EntryType.Item && !Path.EndsInDirectorySeparator(path) &&
            parentPath != null && Directory.Exists(parentPath))
        {
            return (CreateLocalLocation(parentPath), Path.GetFileName(Path.TrimEndingDirectorySeparator(fullPath)));
        }

        Directory.CreateDirectory(fullPath);
        return (CreateLocalLocation(fullPath), null);
    }

    private static VolumeLocation CreateLocalLocation(string path)
    {
        var localVolume = new LocalVolume(path);
        return new VolumeLocation(localVolume,
            string.Join(Path.DirectorySeparatorChar, localVolume.CurrentPathComponents));
    }

    private static void ThrowIfCopyingOntoItself(string sourcePath, IEnumerable<IEntry> entries,
        string destinationPath, string? targetName)
    {
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        sourcePath = Path.TrimEndingDirectorySeparator(sourcePath);
        destinationPath = Path.TrimEndingDirectorySeparator(destinationPath);
        var entryList = entries.ToList();

        if (destinationPath.Equals(sourcePath, comparison) &&
            (targetName == null || entryList.Any(entry => entry.Name.Equals(targetName, comparison))))
        {
            throw new IOException("Cannot copy entries onto themselves");
        }

        foreach (var entry in entryList.Where(entry => entry.Type == EntryType.Container))
        {
            if (IsInDirectory(destinationPath, Path.Combine(sourcePath, entry.Name), comparison))
            {
                throw new IOException($"Cannot copy directory '{entry.Name}' into itself");
            }
        }
    }

    public void Dispose()
    {
        while (_isoStreams.Count > 0)
        {
            _isoStreams.Pop().Dispose();
        }
    }
}
