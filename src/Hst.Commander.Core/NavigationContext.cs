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
        }
        catch
        {
            isoStream.Dispose();
            throw;
        }
    }

    public async Task GoParentAsync(CancellationToken cancellationToken = default)
    {
        if (_isoStreams.Count > 0 && _nestedVolume.CurrentVolumeIsAtRoot)
        {
            _nestedVolume.PopVolume();
            _isoStreams.Pop().Dispose();
            return;
        }

        await _nestedVolume.ChangeDirectory([Paths.Parent], cancellationToken);
    }

    public void Dispose()
    {
        while (_isoStreams.Count > 0)
        {
            _isoStreams.Pop().Dispose();
        }
    }
}
