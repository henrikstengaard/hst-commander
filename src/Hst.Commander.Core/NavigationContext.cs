using DiscUtils.Iso9660;
using Hst.Commander.Core.Volumes;
using Hst.Commander.Extensions.Volume.Abstractions;

namespace Hst.Commander.Core;

public sealed class NavigationContext : IDisposable
{
    private readonly LocalVolume _localVolume;
    private readonly NestedVolume _nestedVolume;
    private readonly Stack<Stream> _isoStreams = new();

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

    public bool HasParent => _nestedVolume.CurrentPathComponents.Any();

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
            PreviousEntryName = null;
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
        }
        else
        {
            await _nestedVolume.ChangeDirectory([Paths.Parent], cancellationToken);
        }

        PreviousEntryName = previousEntryName;
    }

    public void Dispose()
    {
        while (_isoStreams.Count > 0)
        {
            _isoStreams.Pop().Dispose();
        }
    }
}
