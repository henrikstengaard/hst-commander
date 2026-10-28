using Hst.Commander.Core.Volumes;
using Hst.Commander.Abstractions;

namespace Hst.Commander.Core;

public sealed class NavigationContext : IDisposable
{
    private IVolumeProvider _volumeProvider;
    private IVolume _rootVolume;
    private NestedVolume _nestedVolume;
    private readonly Stack<OpenedImage> _images = new();
    private readonly Dictionary<string, IEntry> _markedEntries = new(StringComparer.Ordinal);
    private readonly HashSet<string> _executableNames = new(StringComparer.Ordinal);
    private readonly Action<string> _startFile;
    private readonly IReadOnlyList<IFileVolumeProvider> _fileVolumeProviders;
    private readonly IReadOnlyList<IIconProvider> _iconProviders;
    private readonly Dictionary<string, IconFile> _iconFiles = new(StringComparer.Ordinal);
    private readonly Dictionary<string, IReadOnlyList<VolumeIconFile>> _volumeIconFiles = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _iconLocationLock = new(1, 1);
    private Task<VolumeLocation>? _iconLocation;
    private string? _newEntryName;
    private bool _selectFirstEntry;

    /// <param name="initialPath">Local directory to start in, empty for the drive list.</param>
    /// <param name="startFile">
    ///     Starts a local file opened with <see cref="OpenEntryAsync" />, given its full path. Defaults to starting it
    ///     with the shell, which executes programs and opens other files with their associated application.
    /// </param>
    /// <param name="fileVolumeProviders">
    ///     File volume providers asked in order whether a file opened with <see cref="OpenEntryAsync" /> is browsed as a
    ///     volume (an image), e.g. an ISO image or a zip archive. Defaults to <see cref="VolumeProviders.Files" />.
    /// </param>
    /// <param name="iconProviders">
    ///     Icon providers reading icons from icon files next to the listed entries, e.g. Amiga .info files (see
    ///     <see cref="GetIconFile" />). Defaults to <see cref="VolumeProviders.Icons" />.
    /// </param>
    public NavigationContext(string initialPath, Action<string>? startFile = null,
        IReadOnlyList<IFileVolumeProvider>? fileVolumeProviders = null,
        IReadOnlyList<IIconProvider>? iconProviders = null)
    {
        _startFile = startFile ?? FileStarter.StartWithShell;
        _fileVolumeProviders = fileVolumeProviders ?? VolumeProviders.Files;
        _iconProviders = iconProviders ?? VolumeProviders.Icons;
        _volumeProvider = VolumeProviders.Local;
        _rootVolume = new LocalVolume(initialPath);
        _nestedVolume = new NestedVolume(_rootVolume);
    }

    /// <summary>Volume provider of the volume shown, changed with <see cref="ChangeVolumeProviderAsync" />.</summary>
    public IVolumeProvider VolumeProvider => _volumeProvider;

    /// <summary>
    ///     Shows a new volume from a volume provider at its root, e.g. chosen with Alt+F1/Alt+F2. Images opened in the
    ///     current volume are closed and marks are cleared.
    /// </summary>
    public async Task ChangeVolumeProviderAsync(IVolumeProvider volumeProvider,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(volumeProvider);

        SetRootVolume(volumeProvider, await volumeProvider.CreateVolume(cancellationToken));
    }

    /// <summary>
    ///     Changes to a location entered by the user, e.g. in the location dialog (Ctrl+L). The location is unchanged
    ///     when the path fails or equals <see cref="CurrentPath" />. A path is resolved as:
    ///     <list type="bullet">
    ///         <item>empty or "Computer" (in a local volume): the local drive list;</item>
    ///         <item>a local directory, relative paths relative to the current local directory;</item>
    ///         <item>
    ///             a local path going into an image (a file opened as a volume), e.g. "c:\images\disk.iso\dir1" or
    ///             "c:\images\disk.iso\archive.zip\dir1";
    ///         </item>
    ///         <item>
    ///             otherwise, when another volume provider is shown, a path in its volume from the root, e.g. "dir1"
    ///             or the provider name for the root, also going into images, e.g. "PhysicalDrive2\mbr" of a
    ///             physical disk.
    ///         </item>
    ///     </list>
    /// </summary>
    public async Task ChangeLocationAsync(string path, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(path);

        path = path.Trim();
        if (path.Equals(CurrentPath, StringComparison.Ordinal))
        {
            return;
        }

        var isLocal = _rootVolume is LocalVolume;
        if (path.Length == 0 || (isLocal && path.Equals("Computer", StringComparison.OrdinalIgnoreCase)))
        {
            SetRootVolume(VolumeProviders.Local, new LocalVolume(string.Empty));
            return;
        }

        if (!isLocal && !Path.IsPathRooted(path))
        {
            await ChangeVolumeLocationAsync(path, cancellationToken);
            return;
        }

        // a drive like "c:" is its root directory, not the current directory of the drive
        if (OperatingSystem.IsWindows() && path.Length == 2 && path[1] == ':')
        {
            path += Path.DirectorySeparatorChar;
        }

        var basePath = LocalPath;
        if (basePath == null && !Path.IsPathRooted(path))
        {
            throw new IOException($"Location '{path}' is not a full path");
        }

        await ChangeLocalLocationAsync(Path.GetFullPath(basePath == null ? path : Path.Combine(basePath, path)),
            cancellationToken);
    }

    /// <summary>
    ///     Changes to a path from the root of a new volume of the current (non-local) volume provider, where path
    ///     components are directories or images.
    /// </summary>
    private async Task ChangeVolumeLocationAsync(string path, CancellationToken cancellationToken)
    {
        var pathComponents = path.Equals(_volumeProvider.Name, StringComparison.OrdinalIgnoreCase)
            ? []
            : SplitPath(path);
        var volume = await _volumeProvider.CreateVolume(cancellationToken);
        var context = new NavigationContext(string.Empty, _startFile, _fileVolumeProviders, _iconProviders);
        context.SetRootVolume(_volumeProvider, volume);
        await ChangeToPathAsync(context, pathComponents, path, cancellationToken);
    }

    /// <summary>Changes to a local directory or a directory inside a local image.</summary>
    private async Task ChangeLocalLocationAsync(string fullPath, CancellationToken cancellationToken)
    {
        if (Directory.Exists(fullPath))
        {
            SetRootVolume(VolumeProviders.Local, new LocalVolume(fullPath));
            return;
        }

        // the closest existing file in the path must be an image, e.g. "c:\images\disk.iso" of
        // "c:\images\disk.iso\dir1"
        var imagePath = Path.TrimEndingDirectorySeparator(fullPath);
        while (imagePath != null && !File.Exists(imagePath) && !Directory.Exists(imagePath))
        {
            imagePath = Path.GetDirectoryName(imagePath);
        }

        if (imagePath == null || !File.Exists(imagePath) || Path.GetDirectoryName(imagePath) is not { } imageDirectory)
        {
            throw new IOException($"Location '{fullPath}' not found");
        }

        // the path is opened in a context of its own, so the location is unchanged when it fails. Path components
        // inside the image are directories or images, e.g. "archive.zip" of "c:\images\disk.iso\archive.zip\dir1",
        // detected regardless of their extension like opening them as volumes
        List<string> pathComponents =
        [
            Path.GetFileName(imagePath),
            ..SplitPath(Path.GetRelativePath(imagePath, fullPath)).Where(pathComponent => pathComponent != ".")
        ];
        var context = new NavigationContext(imageDirectory, _startFile, _fileVolumeProviders, _iconProviders);
        await ChangeToPathAsync(context, pathComponents, fullPath, cancellationToken);
    }

    /// <summary>
    ///     Changes a new context to path components that are directories or images and then shows its location. The
    ///     context is disposed when a path component isn't found, leaving this location unchanged.
    /// </summary>
    private async Task ChangeToPathAsync(NavigationContext context, IEnumerable<string> pathComponents,
        string location, CancellationToken cancellationToken)
    {
        try
        {
            foreach (var pathComponent in pathComponents)
            {
                var entry = await context.FindEntryAsync(pathComponent, cancellationToken);
                if (entry is { Type: EntryType.Container })
                {
                    await context._nestedVolume.ChangeDirectory([entry.Name], cancellationToken);
                }
                else if (entry == null ||
                         !await context.OpenImageAsync(entry, _fileVolumeProviders, true, cancellationToken))
                {
                    throw new IOException($"Location '{location}' not found");
                }
            }
        }
        catch
        {
            context.Dispose();
            throw;
        }

        SetRootVolume(context._volumeProvider, context._rootVolume);
        _nestedVolume = context._nestedVolume;
        foreach (var image in context._images.Reverse())
        {
            _images.Push(image);
        }
    }

    private static string[] SplitPath(string path) => path.Split(
        [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries);

    /// <summary>Shows a root volume, closing the current one and clearing the marks.</summary>
    private void SetRootVolume(IVolumeProvider volumeProvider, IVolume volume)
    {
        CloseVolumes();
        _volumeProvider = volumeProvider;
        _rootVolume = volume;
        _nestedVolume = new NestedVolume(volume);
        PreviousEntryName = null;
        _markedEntries.Clear();
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

    /// <summary>
    ///     True when the entry is an executable local file, shown in the executable color. On Windows by its extension
    ///     (see <see cref="IsExecutableName" />), on other systems by its execute permission. Updated when entries are
    ///     listed.
    /// </summary>
    public bool IsExecutable(IEntry entry) => _executableNames.Contains(entry.Name);

    /// <summary>File extensions of executable files on Windows.</summary>
    public static IReadOnlyCollection<string> ExecutableExtensions { get; } = [".exe", ".com", ".bat", ".cmd", ".msi"];

    /// <summary>True when the file name has an executable extension, e.g. "setup.exe" or "build.cmd".</summary>
    public static bool IsExecutableName(string name) =>
        ExecutableExtensions.Contains(Path.GetExtension(name), StringComparer.OrdinalIgnoreCase);

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

    /// <summary>
    ///     Marks the listed entries whose names match the filter of the select group dialog (+), adding to the entries
    ///     already marked. Directories are marked too unless <see cref="SelectGroupOptions.FilesOnly" />; the parent
    ///     entry ".." is never marked. Returns the number of matching entries. Throws <see cref="IOException" /> when
    ///     the filter is an invalid regular expression.
    /// </summary>
    public int MarkGroup(IEnumerable<IEntry> entries, SelectGroupOptions options)
    {
        var matchingEntries = FindGroup(entries, options);
        foreach (var entry in matchingEntries)
        {
            _markedEntries[entry.Name] = entry;
        }

        return matchingEntries.Count;
    }

    /// <summary>
    ///     Unmarks the listed entries whose names match the filter of the unselect group dialog (-), matched like
    ///     <see cref="MarkGroup" />; other marks are kept. Returns the number of matching entries. Throws
    ///     <see cref="IOException" /> when the filter is an invalid regular expression.
    /// </summary>
    public int UnmarkGroup(IEnumerable<IEntry> entries, SelectGroupOptions options)
    {
        var matchingEntries = FindGroup(entries, options);
        foreach (var entry in matchingEntries)
        {
            _markedEntries.Remove(entry.Name);
        }

        return matchingEntries.Count;
    }

    /// <summary>
    ///     Inverts the marks of the listed entries (*): marked entries are unmarked and the others are marked,
    ///     directories included. The parent entry ".." is never marked.
    /// </summary>
    public void InvertMarks(IEnumerable<IEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);

        foreach (var entry in entries.ToList())
        {
            ToggleMark(entry);
        }
    }

    /// <summary>Listed entries, except "..", matching select group options.</summary>
    private static List<IEntry> FindGroup(IEnumerable<IEntry> entries, SelectGroupOptions options)
    {
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(options);

        var filter = new NameFilter(options.Filter, options.CaseSensitive);
        return entries
            .Where(entry => !entry.Name.Equals(Paths.Parent, StringComparison.Ordinal) &&
                            (!options.FilesOnly || entry.Type == EntryType.Item) &&
                            filter.IsMatch(entry.Name))
            .ToList();
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

    /// <summary>Description of the entries to move shown in the move dialog, e.g. "Move 3 entries" or "Move 'a.txt'".</summary>
    public static string GetMoveDescription(IReadOnlyCollection<IEntry> entries) => entries.Count == 1
        ? $"Move '{entries.First().Name}'"
        : $"Move {entries.Count} entries";

    /// <summary>Question shown in the delete confirm dialog, e.g. "Delete 3 entries?" or "Delete 'a.txt'?".</summary>
    public static string GetDeleteDescription(IReadOnlyCollection<IEntry> entries) => entries.Count == 1
        ? $"Delete '{entries.First().Name}'?"
        : $"Delete {entries.Count} entries?";

    public bool HasParent =>_nestedVolume.CurrentPathComponents.Any();

    /// <summary>True when listing the local drives, where the entries are drive roots.</summary>
    public bool IsDriveList => _rootVolume is LocalVolume && !HasParent;

    public string CurrentPath
    {
        get
        {
            var pathComponents = _nestedVolume.CurrentPathComponents.ToArray();
            return pathComponents.Length == 0
                ? _rootVolume is LocalVolume ? "Computer" : _volumeProvider.Name
                : string.Join(Path.DirectorySeparatorChar, pathComponents);
        }
    }

    /// <summary>
    ///     True inside an image: a file opened as a volume by a file volume provider, e.g. an ISO image or a zip archive.
    /// </summary>
    public bool IsInImage => _images.Count > 0;

    /// <summary>Local directory of the current path or containing the current image. Null in the drive list.</summary>
    private string? LocalPath => _rootVolume is LocalVolume localVolume && localVolume.CurrentPathComponents.Any()
        ? string.Join(Path.DirectorySeparatorChar, localVolume.CurrentPathComponents)
        : null;

    /// <summary>
    ///     Free and total space of the local drive at the current path formatted as "free/total", e.g. "87G/256G".
    ///     Null when listing drives, inside an image or when the drive is unavailable.
    /// </summary>
    public string? DiskSpace
    {
        get
        {
            if (IsInImage || LocalPath is not { } localPath)
            {
                return null;
            }

            try
            {
                var currentPath = Path.GetFullPath(localPath);
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

    /// <summary>
    ///     Full local path of a listed entry, e.g. to get its icon from the OS: the drive root in the drive list. Null
    ///     for "..", inside images and in other volume providers.
    /// </summary>
    public string? GetLocalPath(IEntry entry)
    {
        if (IsInImage || _rootVolume is not LocalVolume ||
            entry.Name.Equals(Paths.Parent, StringComparison.Ordinal))
        {
            return null;
        }

        return LocalPath is { } localPath ? Path.Combine(localPath, entry.Name) : entry.Name;
    }

    /// <summary>
    ///     Opens a listed item in the current location for reading, e.g. to read its icon. Uses the pane's own volume,
    ///     so it must not run concurrently with navigation.
    /// </summary>
    public Task<Stream> OpenReadAsync(IEntry entry, CancellationToken cancellationToken = default) =>
        _nestedVolume.OpenRead(entry.Name, cancellationToken);

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

        UpdateExecutableNames(entries);
        UpdateIconFiles(entries);

        // keep marks of entries still present, refreshed with the listed entries so sizes are current
        var markedEntries = entries.Where(IsMarked).ToList();
        _markedEntries.Clear();
        foreach (var entry in markedEntries)
        {
            _markedEntries[entry.Name] = entry;
        }

        return entries;
    }

    private void UpdateExecutableNames(IEnumerable<IEntry> entries)
    {
        _executableNames.Clear();

        // files inside images can't be executed
        var localPath = !IsInImage ? LocalPath : null;
        if (localPath == null)
        {
            return;
        }

        foreach (var entry in entries.Where(entry => entry.Type == EntryType.Item))
        {
            if (OperatingSystem.IsWindows()
                    ? IsExecutableName(entry.Name)
                    : FileStarter.HasExecutePermission(Path.Combine(localPath, entry.Name)))
            {
                _executableNames.Add(entry.Name);
            }
        }
    }

    /// <summary>Largest icon file read by <see cref="ReadIconAsync" />, larger files have no icon.</summary>
    private const long MaxIconFileSize = 4 * 1024 * 1024;

    private sealed record IconFile(IIconProvider Provider, IEntry Entry);

    /// <summary>
    ///     Finds the icon files of the listed entries with the icon providers: the first provider whose icon file for
    ///     an entry (e.g. "file.info" for "file" and for "file.info" itself) is a listed file, matched exactly,
    ///     otherwise case-insensitive. Not in the drive list.
    /// </summary>
    private void UpdateIconFiles(IReadOnlyList<IEntry> entries)
    {
        _iconFiles.Clear();
        _volumeIconFiles.Clear();
        CloseIconLocation();
        if (_iconProviders.Count == 0 || IsDriveList)
        {
            return;
        }

        var exactItems = new Dictionary<string, IEntry>(StringComparer.Ordinal);
        var items = new Dictionary<string, IEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in entries.Where(entry => entry.Type == EntryType.Item))
        {
            exactItems.TryAdd(item.Name, item);
            items.TryAdd(item.Name, item);
        }

        foreach (var entry in entries.Where(entry => !entry.Name.Equals(Paths.Parent, StringComparison.Ordinal)))
        {
            foreach (var provider in _iconProviders)
            {
                if (provider.GetIconFileName(entry.Name) is { } iconFileName &&
                    (exactItems.GetValueOrDefault(iconFileName) ?? items.GetValueOrDefault(iconFileName)) is
                    { } iconFile)
                {
                    _iconFiles[entry.Name] = new IconFile(provider, iconFile);
                    break;
                }
            }

            // volumes, e.g. partitions, can hold their icon inside, looked up when the icon is read
            if (!_iconFiles.ContainsKey(entry.Name) && entry is { IsVolume: true, Type: EntryType.Container })
            {
                var volumeIconFiles = _iconProviders
                    .Select(provider => (Provider: provider, FileName: provider.GetVolumeIconFileName(entry.Name)))
                    .Where(volumeIconFile => volumeIconFile.FileName != null)
                    .Select(volumeIconFile => new VolumeIconFile(volumeIconFile.Provider, volumeIconFile.FileName!))
                    .ToList();
                if (volumeIconFiles.Count > 0)
                {
                    _volumeIconFiles[entry.Name] = volumeIconFiles;
                }
            }
        }
    }

    /// <summary>Icon file inside a volume entry, e.g. Disk.info in an Amiga partition.</summary>
    private sealed record VolumeIconFile(IIconProvider Provider, string FileName);

    /// <summary>
    ///     Icon file holding the icon of a listed entry, e.g. "file.info" for "file" and for "file.info" itself, null
    ///     when no icon provider has an icon file listed for it. Updated by <see cref="ListEntriesAsync" />.
    /// </summary>
    public IEntry? GetIconFile(IEntry entry) => _iconFiles.GetValueOrDefault(entry.Name)?.Entry;

    /// <summary>
    ///     True when a listed entry can have an icon read by <see cref="ReadIconAsync" />: it has an icon file (see
    ///     <see cref="GetIconFile" />), or it is a volume (<see cref="IEntry.IsVolume" />, e.g. a partition) and an icon
    ///     provider has an icon file inside volumes (e.g. Disk.info), which may not exist.
    /// </summary>
    public bool HasIcon(IEntry entry) =>
        _iconFiles.ContainsKey(entry.Name) || _volumeIconFiles.ContainsKey(entry.Name);

    /// <summary>
    ///     Reads the icon of a listed entry from its icon file (see <see cref="GetIconFile" />) with the icon provider,
    ///     null when it has no icon file or the icon can't be read. Local files are read on a background thread, files
    ///     inside images and other volumes with the pane's own volume, so it must not run concurrently with navigation.
    /// </summary>
    /// <param name="entry">Listed entry.</param>
    /// <param name="size">Wanted width/height in pixels.</param>
    /// <param name="cancellationToken"></param>
    public Task<IconImage?> ReadIconAsync(IEntry entry, int size, CancellationToken cancellationToken = default) =>
        ReadIconFileAsync(entry, (provider, name, stream) => provider.ReadIcon(name, stream, size, cancellationToken),
            null, cancellationToken);

    /// <summary>
    ///     Reads all icons of the icon file of a listed entry (see <see cref="GetIconFile" />) with the icon provider,
    ///     e.g. the normal and the selected image of an Amiga icon, empty when it has no icon file or the icons can't be
    ///     read. Like <see cref="ReadIconAsync" />, it must not run concurrently with navigation.
    /// </summary>
    public async Task<IReadOnlyList<EntryIcon>> ReadIconsAsync(IEntry entry,
        CancellationToken cancellationToken = default) =>
        await ReadIconFileAsync(entry, (provider, name, stream) => provider.ReadIcons(name, stream, cancellationToken),
            [], cancellationToken) ?? [];

    /// <summary>
    ///     Reads the icon file of a listed entry into memory and reads it with the icon provider on the thread pool,
    ///     returning <paramref name="none" /> when there is no icon file, it can't be read or the provider fails.
    /// </summary>
    private async Task<T?> ReadIconFileAsync<T>(IEntry entry, Func<IIconProvider, string, Stream, Task<T>> read,
        T? none, CancellationToken cancellationToken)
    {
        if (_volumeIconFiles.TryGetValue(entry.Name, out var volumeIconFiles))
        {
            return await ReadVolumeIconFileAsync(entry, volumeIconFiles, read, none, cancellationToken);
        }

        if (!_iconFiles.TryGetValue(entry.Name, out var iconFile) || iconFile.Entry.Size > MaxIconFileSize)
        {
            return none;
        }

        try
        {
            var localPath = GetLocalPath(iconFile.Entry);
            var data = new MemoryStream();
            if (localPath != null)
            {
                await Task.Run(async () =>
                {
                    await using var stream = new FileStream(localPath, FileMode.Open, FileAccess.Read,
                        FileShare.ReadWrite);
                    await CopyIconFileAsync(stream, data, cancellationToken);
                }, cancellationToken);
            }
            else
            {
                await using var stream = await OpenReadAsync(iconFile.Entry, cancellationToken);
                await CopyIconFileAsync(stream, data, cancellationToken);
            }

            data.Position = 0;
            return await Task.Run(() => read(iconFile.Provider, iconFile.Entry.Name, data), cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // an unreadable or invalid icon file just has no icon, also when a plugin fails reading it
            return none;
        }
    }

    /// <summary>
    ///     Reads the icon file inside a volume entry (e.g. Disk.info in an Amiga partition) with the first provider whose
    ///     icon file exists. The volume entry is opened on a volume of its own (<see cref="OpenLocationAsync" />, opened
    ///     once per listing and shared by the volume entries), on the thread pool, so the pane's volume isn't changed.
    /// </summary>
    private async Task<T?> ReadVolumeIconFileAsync<T>(IEntry entry, IReadOnlyList<VolumeIconFile> volumeIconFiles,
        Func<IIconProvider, string, Stream, Task<T>> read, T? none, CancellationToken cancellationToken)
    {
        // opened on the calling (UI) thread, as it reads the pane's state
        var locationTask = _iconLocation ??= OpenLocationAsync(CancellationToken.None);
        try
        {
            return await Task.Run(async () =>
            {
                await _iconLocationLock.WaitAsync(cancellationToken);
                try
                {
                    var volume = (await locationTask).Volume;
                    // a failing change keeps the directory, so only a successful one is changed back
                    await volume.ChangeDirectory([entry.Name], cancellationToken);
                    try
                    {
                        foreach (var volumeIconFile in volumeIconFiles)
                        {
                            var data = new MemoryStream();
                            try
                            {
                                await using var stream = await volume.OpenRead(volumeIconFile.FileName,
                                    cancellationToken);
                                await CopyIconFileAsync(stream, data, cancellationToken);
                            }
                            catch (Exception exception) when (exception is IOException
                                                                  or UnauthorizedAccessException
                                                                  or NotSupportedException)
                            {
                                // e.g. the volume has no icon file of this provider
                                continue;
                            }

                            data.Position = 0;
                            return await read(volumeIconFile.Provider, volumeIconFile.FileName, data);
                        }

                        return none;
                    }
                    finally
                    {
                        await volume.ChangeDirectory([Paths.Parent], CancellationToken.None);
                    }
                }
                finally
                {
                    _iconLocationLock.Release();
                }
            }, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // e.g. a partition that can't be mounted just has no icon
            return none;
        }
    }

    /// <summary>
    ///     Closes the location volume icons are read from, once no icon is read from it, e.g. when the entries are
    ///     listed again.
    /// </summary>
    private void CloseIconLocation()
    {
        if (_iconLocation is not { } locationTask)
        {
            return;
        }

        _iconLocation = null;
        _ = CloseAsync();
        return;

        async Task CloseAsync()
        {
            await _iconLocationLock.WaitAsync();
            try
            {
                (await locationTask).Dispose();
            }
            catch (Exception)
            {
                // the location failed to open, nothing to close
            }
            finally
            {
                _iconLocationLock.Release();
            }
        }
    }

    private static async Task CopyIconFileAsync(Stream source, MemoryStream destination,
        CancellationToken cancellationToken)
    {
        var buffer = new byte[81920];
        int read;
        while ((read = await source.ReadAsync(buffer, cancellationToken)) > 0)
        {
            if (destination.Length + read > MaxIconFileSize)
            {
                throw new IOException("Icon file is too large");
            }

            destination.Write(buffer, 0, read);
        }
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

        // going to the root selects the first entry once, also when the location is unchanged
        if (_selectFirstEntry)
        {
            _selectFirstEntry = false;
            return 0;
        }

        if (CurrentPath.Equals(previousPath, StringComparison.Ordinal))
        {
            // a directory just created or an entry just renamed is selected once
            var newEntryName = _newEntryName;
            _newEntryName = null;
            var newIndex = newEntryName == null
                ? -1
                : entries.ToList().FindIndex(entry => entry.Name.Equals(newEntryName, StringComparison.Ordinal));
            return newIndex >= 0 ? newIndex : Math.Clamp(selectedIndex, 0, entries.Count - 1);
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

        // local files are started without detecting images when set to start them with the host
        var localFilePath = !IsInImage && LocalPath is { } localPath ? Path.Combine(localPath, entry.Name) : null;
        if (EnterFileAction == EnterFileAction.StartWithHost && localFilePath != null)
        {
            _startFile(localFilePath);
            return;
        }

        // files detected by any file volume provider, e.g. ISO images or zip archives (also with other extensions like
        // .nupkg), are browsed as images; local files that can't be read are started, errors reading other files,
        // e.g. a physical disk without access, are thrown
        if (await OpenImageAsync(entry, _fileVolumeProviders, localFilePath != null, cancellationToken))
        {
            return;
        }

        // local files are started, e.g. executing a program; files inside images can't be started
        if (localFilePath != null)
        {
            _startFile(localFilePath);
        }
    }

    /// <summary>
    ///     What <see cref="OpenEntryAsync" /> does with files: browse detected images as volumes (default) or start
    ///     local files with the host operating system.
    /// </summary>
    public EnterFileAction EnterFileAction { get; set; } = EnterFileAction.OpenAsVolume;

    /// <summary>
    ///     How <see cref="CopyAsync" /> and <see cref="MoveAsync" /> store Amiga names, protection bits and comments
    ///     when copying from an Amiga volume to other volumes, e.g. a local directory (see
    ///     <see cref="CopyOptions.UaeMetadata" />).
    /// </summary>
    public UaeMetadataFormat UaeMetadata { get; set; } = UaeMetadataFormat.UaeFsDb;

    /// <summary>
    ///     Opens an entry as a volume (Ctrl+PageDown): a file detected by any file volume provider is browsed as an
    ///     image regardless of its extension, e.g. a .docx document as a zip archive. Directories and ".." are opened
    ///     like <see cref="OpenEntryAsync" />. Throws <see cref="IOException" /> when no file volume provider detects
    ///     the file.
    /// </summary>
    public async Task OpenAsVolumeAsync(IEntry entry, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);

        if (entry.Type == EntryType.Container || entry.Name.Equals(Paths.Parent, StringComparison.Ordinal))
        {
            await OpenEntryAsync(entry, cancellationToken);
            return;
        }

        if (!await OpenImageAsync(entry, _fileVolumeProviders, false, cancellationToken))
        {
            throw new IOException($"'{entry.Name}' can't be opened as a volume");
        }
    }

    /// <summary>
    ///     Opens an item in the current location as an image when one of the file volume providers detects it, e.g.
    ///     by its magic signature, and pushes its volume. Returns false when none detects it, or when the file can't
    ///     be read and <paramref name="ignoreReadErrors" /> is true.
    /// </summary>
    private async Task<bool> OpenImageAsync(IEntry entry, IEnumerable<IFileVolumeProvider> fileVolumeProviders,
        bool ignoreReadErrors, CancellationToken cancellationToken)
    {
        var volume = _nestedVolume;
        var fileVolumeProvider = await DetectFileVolumeProviderAsync(entry.Name, fileVolumeProviders.ToList(),
            token => volume.OpenRead(entry.Name, token), ignoreReadErrors, cancellationToken);
        if (fileVolumeProvider == null)
        {
            return false;
        }

        // a file of the root volume with an id, e.g. a local file or a physical disk, is shared when it can be
        // written, otherwise an image inside another image is opened again from the directory it's in, e.g. for copying
        var parentPathComponents = IsInImage ? _images.Peek().Volume.CurrentPathComponents.ToList() : [];
        var sharedId = fileVolumeProvider.CanWrite && !IsInImage ? volume.GetWritableImageId(entry.Name) : null;
        var image = await OpenImageAsync(fileVolumeProvider, entry.Name, parentPathComponents, sharedId, volume,
            cancellationToken);
        _nestedVolume.PushVolume(new EntryVolumeProvider(entry, image.Volume));
        _images.Push(image);
        PreviousEntryName = null;
        _markedEntries.Clear();
        return true;
    }

    /// <summary>
    ///     The first file volume provider detecting a file, null when none does. Files that can't be read, e.g. locked
    ///     by another process, are not detected when <paramref name="ignoreReadErrors" /> is true, so local files are
    ///     started as usual.
    /// </summary>
    private static async Task<IFileVolumeProvider?> DetectFileVolumeProviderAsync(string name,
        IReadOnlyList<IFileVolumeProvider> fileVolumeProviders, Func<CancellationToken, Task<Stream>> openRead,
        bool ignoreReadErrors, CancellationToken cancellationToken)
    {
        if (fileVolumeProviders.Count == 0)
        {
            return null;
        }

        Stream? stream = null;
        try
        {
            foreach (var fileVolumeProvider in fileVolumeProviders)
            {
                // a seekable stream is read again from the start, otherwise the file is opened again
                if (stream is { CanSeek: true })
                {
                    stream.Position = 0;
                }
                else
                {
                    if (stream != null)
                    {
                        await stream.DisposeAsync();
                    }

                    stream = await openRead(cancellationToken);
                }

                if (await fileVolumeProvider.IsMatch(name, stream, cancellationToken))
                {
                    return fileVolumeProvider;
                }
            }
        }
        catch (Exception exception) when (ignoreReadErrors && exception is IOException or UnauthorizedAccessException
                                              or NotSupportedException)
        {
            return null;
        }
        finally
        {
            if (stream != null)
            {
                await stream.DisposeAsync();
            }
        }

        return null;
    }

    /// <summary>
    ///     Opens a file in the current directory of a volume as an image with a file volume provider. A file with an id
    ///     opened by a file volume provider that can write (<paramref name="sharedId" />), e.g. a local file or a
    ///     physical disk, is shared with the other pane and file operations (see <see cref="SharedImages" />), other
    ///     files are opened for reading.
    /// </summary>
    private static async Task<OpenedImage> OpenImageAsync(IFileVolumeProvider fileVolumeProvider, string name,
        IReadOnlyList<string> parentPathComponents, string? sharedId, IVolume parentVolume,
        CancellationToken cancellationToken)
    {
        if (sharedId != null)
        {
            var sharedVolume = await SharedImages.OpenAsync(sharedId, name, parentVolume, fileVolumeProvider,
                cancellationToken);
            return new OpenedImage(fileVolumeProvider, name, parentPathComponents, sharedVolume, null, sharedId,
                sharedVolume.IsWritable);
        }

        var stream = await OpenSeekableAsync(token => parentVolume.OpenRead(name, token), cancellationToken);
        try
        {
            // a file volume provider that can write gets a read-only stream, e.g. of a temporary copy of the file
            var volume = await fileVolumeProvider.CreateVolume(
                fileVolumeProvider.CanWrite ? new ReadOnlyStream(stream) : stream, cancellationToken);
            return new OpenedImage(fileVolumeProvider, name, parentPathComponents, volume, stream, null, false);
        }
        catch
        {
            await stream.DisposeAsync();
            throw;
        }
    }

    /// <summary>
    ///     Opens a file as a seekable stream for a file volume provider. A stream that isn't seekable, e.g. of a file
    ///     inside a zip archive, is copied to a temporary file deleted when the stream is closed.
    /// </summary>
    private static async Task<Stream> OpenSeekableAsync(Func<CancellationToken, Task<Stream>> openRead,
        CancellationToken cancellationToken)
    {
        var stream = await openRead(cancellationToken);
        if (stream.CanSeek)
        {
            return stream;
        }

        await using (stream)
        {
            var temporaryStream = new FileStream(Path.GetTempFileName(), FileMode.Create, FileAccess.ReadWrite,
                FileShare.None, 81920, FileOptions.DeleteOnClose | FileOptions.Asynchronous);
            try
            {
                await stream.CopyToAsync(temporaryStream, cancellationToken);
                temporaryStream.Position = 0;
                return temporaryStream;
            }
            catch
            {
                await temporaryStream.DisposeAsync();
                throw;
            }
        }
    }

    public async Task GoParentAsync(CancellationToken cancellationToken = default)
    {
        // the last non-empty path component is the entry being left, e.g. "C:" for the drive root "C:\"
        var previousEntryName = _nestedVolume.CurrentPathComponents
            .LastOrDefault(pathComponent => pathComponent.Length > 0);

        if (IsInImage && _nestedVolume.CurrentVolumeIsAtRoot)
        {
            _nestedVolume.PopVolume();
            _images.Pop().Dispose();
        }
        else
        {
            await _nestedVolume.ChangeDirectory([Paths.Parent], cancellationToken);
        }

        PreviousEntryName = previousEntryName;
        _markedEntries.Clear();
    }

    /// <summary>
    ///     Home directory of the user, e.g. "C:\Users\name" on Windows, "/Users/name" on macOS or "/home/name" on Linux:
    ///     the user profile folder, otherwise the HOME or USERPROFILE environment variable. Null when there is none.
    /// </summary>
    public static string? HomeDirectory =>
        new[]
            {
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                Environment.GetEnvironmentVariable("HOME"),
                Environment.GetEnvironmentVariable("USERPROFILE")
            }
            .FirstOrDefault(path => !string.IsNullOrWhiteSpace(path) && Path.IsPathRooted(path));

    /// <summary>
    ///     Goes to the home directory of the user (<see cref="HomeDirectory" />, Home in the location dialog), switching
    ///     to the Local volume provider like <see cref="ChangeLocationAsync" />.
    /// </summary>
    /// <exception cref="IOException">There is no home directory.</exception>
    public Task GoHomeAsync(CancellationToken cancellationToken = default) =>
        HomeDirectory is { } homeDirectory
            ? ChangeLocationAsync(homeDirectory, cancellationToken)
            : throw new IOException("Home directory not found");

    /// <summary>
    ///     Goes to the root of the current volume (Ctrl+\): the root of the local drive, e.g. "c:\" or "/", the root of
    ///     an image or the root of another volume provider's volume. At the root of an image, it goes to the root of the
    ///     drive or volume the image is in. The location is unchanged in the drive list or at the root. Either way the
    ///     first entry is selected once by <see cref="GetSelectedIndex" />.
    /// </summary>
    public async Task GoRootAsync(CancellationToken cancellationToken = default)
    {
        _selectFirstEntry = true;

        if (IsInImage && !_nestedVolume.CurrentVolumeIsAtRoot)
        {
            await _images.Peek().Volume.ChangeDirectory([Paths.Root], cancellationToken);
        }
        else if (_rootVolume is LocalVolume)
        {
            if (LocalPath is not { } localPath || Path.GetPathRoot(localPath) is not { Length: > 0 } driveRoot ||
                (!IsInImage && Path.TrimEndingDirectorySeparator(localPath)
                    .Equals(Path.TrimEndingDirectorySeparator(driveRoot), StringComparison.Ordinal)))
            {
                return;
            }

            SetRootVolume(_volumeProvider, new LocalVolume(driveRoot));
        }
        else
        {
            if (!IsInImage && !HasParent)
            {
                return;
            }

            while (IsInImage)
            {
                _nestedVolume.PopVolume();
                _images.Pop().Dispose();
            }

            await _rootVolume.ChangeDirectory([Paths.Root], cancellationToken);
        }

        PreviousEntryName = null;
        _markedEntries.Clear();
    }

    /// <summary>
    ///     Opens a listed file for viewing (F3). Local files are read directly (shared with writers), files inside
    ///     images and other volumes with a volume of their own (<see cref="OpenLocationAsync" />), so the viewer can read
    ///     and search on a background thread; a file that isn't seekable (e.g. inside a zip archive) is copied to a
    ///     temporary file first. The images of the file are read with the image providers (default
    ///     <see cref="VolumeProviders.Images" />; none, e.g. for the TUI, views images as binary files), so an image
    ///     starts in the image view. Directories throw
    ///     <see cref="IOException" />.
    /// </summary>
    public async Task<ViewerSession> OpenViewerAsync(IEntry entry, IEnumerable<IImageProvider>? imageProviders = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);

        var document = await OpenViewerDocumentAsync(entry, cancellationToken);
        try
        {
            var images = await document.ReadImagesAsync(entry.Name, imageProviders ?? VolumeProviders.Images,
                cancellationToken);
            return new ViewerSession(document, images);
        }
        catch
        {
            document.Dispose();
            throw;
        }
    }

    private async Task<ViewerDocument> OpenViewerDocumentAsync(IEntry entry, CancellationToken cancellationToken)
    {
        if (entry.Type == EntryType.Container)
        {
            throw new IOException($"'{entry.Name}' is a directory and can't be viewed");
        }

        if (GetLocalPath(entry) is { } localPath)
        {
            return await Task.Run(() => ViewerDocument.Open(new FileStream(localPath, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete, 0, FileOptions.RandomAccess)), cancellationToken);
        }

        var location = await OpenLocationAsync(cancellationToken);
        Stream stream;
        try
        {
            stream = await OpenSeekableAsync(token => location.Volume.OpenRead(entry.Name, token),
                cancellationToken);
        }
        catch
        {
            location.Dispose();
            throw;
        }

        // the document owns the stream and the location, also when opening fails
        return await Task.Run(() => ViewerDocument.Open(stream, location), CancellationToken.None);
    }

    /// <summary>
    ///     Opens a volume of its own at the current location, so it can be used, e.g. for copying, without changing the
    ///     current directory of this context. Images are opened again from their files, images inside images from the
    ///     image they are in.
    /// </summary>
    public async Task<VolumeLocation> OpenLocationAsync(CancellationToken cancellationToken = default)
    {
        var rootLocation = await OpenRootLocationAsync(cancellationToken);
        if (!IsInImage)
        {
            return rootLocation;
        }

        var resources = new Stack<IDisposable>([rootLocation]);
        try
        {
            var volume = rootLocation.Volume;
            foreach (var image in _images.Reverse())
            {
                // the first image is in the root location, the others in a directory of the image before
                if (resources.Count > 1)
                {
                    await volume.ChangeDirectory([Paths.Root, ..image.ParentPathComponents], cancellationToken);
                }

                var openedImage = await OpenImageAsync(image.FileVolumeProvider, image.Name,
                    image.ParentPathComponents, image.SharedId, volume, cancellationToken);
                resources.Push(openedImage);
                volume = openedImage.Volume;
            }

            await volume.ChangeDirectory([Paths.Root, .._images.Peek().Volume.CurrentPathComponents],
                cancellationToken);
            return new VolumeLocation(volume, CurrentPath, new Resources(resources));
        }
        catch
        {
            new Resources(resources).Dispose();
            throw;
        }
    }

    /// <summary>Opens a volume of its own at the current location of the root volume, e.g. the directory of an image.</summary>
    private async Task<VolumeLocation> OpenRootLocationAsync(CancellationToken cancellationToken)
    {
        if (_rootVolume is LocalVolume)
        {
            var localPath = LocalPath ?? throw new IOException("The drive list is not a directory");
            return CreateLocalLocation(localPath);
        }

        // other volumes are created again by their provider and changed to the current directory
        var volume = await _volumeProvider.CreateVolume(cancellationToken);
        try
        {
            var pathComponents = _rootVolume.CurrentPathComponents
                .Where(pathComponent => !pathComponent.Equals(Paths.Root, StringComparison.OrdinalIgnoreCase));
            await volume.ChangeDirectory([Paths.Root, ..pathComponents], cancellationToken);
            return new VolumeLocation(volume, CurrentPath, volume as IDisposable);
        }
        catch
        {
            (volume as IDisposable)?.Dispose();
            throw;
        }
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
            ThrowIfCopyingOntoItself(source.Path, entries, destination.Path, targetName, "copy");
            await source.Volume.CopyToAsync(entries, destination.Volume,
                new CopyOptions(filter, targetName, confirmOverwrite, UaeMetadata), progress, cancellationToken);
        }
    }

    /// <summary>
    ///     Deletes entries in the current location, directories with everything in them. Like copying, it uses a volume
    ///     of its own, so it can run on a background thread. Read-only images throw
    ///     <see cref="NotSupportedException" />. The UAE metadata of deleted entries is removed (see
    ///     <see cref="UaeMetadata" />).
    /// </summary>
    public async Task DeleteAsync(IReadOnlyCollection<IEntry> entries, IProgress<DeleteProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entries);

        using var location = await OpenLocationAsync(cancellationToken);
        var metadata = await UaeMetadataUpdater.ReadAsync(location.Volume, UaeMetadata, false, null,
            cancellationToken);
        try
        {
            await location.Volume.DeleteAsync(entries, progress, cancellationToken);
        }
        catch
        {
            // the metadata of the entries deleted so far is removed, also when deleting fails
            await UaeMetadataUpdater.TryRemoveMissingAsync(location.Volume, metadata);
            throw;
        }

        await UaeMetadataReader.RemoveMissingAsync(location.Volume, metadata, cancellationToken);
    }

    /// <summary>
    ///     Properties of entries in the current location shown in the properties dialog (Alt+Enter), e.g. of
    ///     <see cref="GetOperationEntries" />. Local files and directories get details from the file system, drives in
    ///     the drive list their space, entries inside images and other volumes the details of the entries. Count the
    ///     size and contents with <see cref="CountContentsAsync" /> when <see cref="EntryProperties.HasContents" />.
    /// </summary>
    public EntryProperties GetProperties(IReadOnlyList<IEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);

        var localDirectory = !IsInImage ? LocalPath : null;
        return EntryProperties.Create(entries, CurrentPath, localDirectory, IsDriveList);
    }

    /// <summary>
    ///     Properties like <see cref="GetProperties" />, with the Amiga name, protection bits and comment of a single
    ///     entry from the UAE metadata of the current directory (see <see cref="UaeMetadataReader" />) in volumes not
    ///     storing them, e.g. a file copied from an Amiga volume to a local directory. Reads with the pane's volume,
    ///     so call it on the thread using the pane. Not with <see cref="UaeMetadataFormat.None" />. The check boxes of
    ///     a single entry (<see cref="EntryProperties.FlagGroups" />) can be changed when the volume can change its
    ///     attributes (<see cref="IVolume.GetChangeableAttributes" />) or Amiga protection bits
    ///     (<see cref="IVolume.CanSetAmigaMetadata" />, or UAE metadata in a writable location).
    /// </summary>
    public async Task<EntryProperties> GetPropertiesAsync(IReadOnlyList<IEntry> entries,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entries);

        if (entries is not [var entry] || IsDriveList || entry.Name == Paths.Parent)
        {
            return GetProperties(entries);
        }

        var uaeMetadata = entry.AmigaMetadata == null ? await FindUaeMetadataAsync(entry, cancellationToken) : null;
        var changes = new PropertyChanges(await _nestedVolume.GetChangeableAttributes(cancellationToken),
            entry.AmigaMetadata != null
                ? await _nestedVolume.CanSetAmigaMetadata(cancellationToken)
                : uaeMetadata != null && MoveNotSupportedMessage == null);
        var localDirectory = !IsInImage ? LocalPath : null;
        return EntryProperties.Create(entries, CurrentPath, localDirectory, IsDriveList, uaeMetadata, changes,
            await ReadIconPropertiesAsync(entry, cancellationToken));
    }

    /// <summary>
    ///     Properties of the icon file of a listed entry read by its icon provider
    ///     (<see cref="IIconProvider.ReadIconProperties" />), e.g. the type and position of an Amiga icon, after the name
    ///     of the icon file, for the entry and the icon file itself (e.g. "file" and "file.info") and volume entries
    ///     (e.g. the Disk.info of a partition). Empty when it has none.
    /// </summary>
    private async Task<IReadOnlyList<EntryProperty>> ReadIconPropertiesAsync(IEntry entry,
        CancellationToken cancellationToken)
    {
        if (!HasIcon(entry))
        {
            return [];
        }

        var properties = await ReadIconFileAsync(entry,
            (provider, name, stream) => provider.ReadIconProperties(name, stream, cancellationToken), [],
            cancellationToken) ?? [];
        return properties.Count > 0 && GetIconFile(entry) is { } iconFile
            ? [new EntryProperty("Icon file", iconFile.Name), ..properties]
            : properties;
    }

    /// <summary>
    ///     Changes the attributes and Amiga protection bits of the entry of properties from
    ///     <see cref="GetPropertiesAsync" /> to its check boxes, given whether each check box of each
    ///     <see cref="EntryProperties.FlagGroups" /> is checked. Only groups that can be changed and differ are changed:
    ///     attributes with <see cref="IVolume.SetAttributes" />, protection bits with
    ///     <see cref="IVolume.SetAmigaMetadata" /> or in the UAE metadata of the entry (see
    ///     <see cref="UaeMetadataUpdater.SetMetadataAsync" />). Changes with the pane's volume, so call it on the thread
    ///     using the pane. Returns true when something was changed.
    /// </summary>
    public async Task<bool> ChangeFlagsAsync(EntryProperties properties, IReadOnlyList<IReadOnlyList<bool>> isChecked,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(properties);
        ArgumentNullException.ThrowIfNull(isChecked);

        if (properties.Entry is not { } entry)
        {
            return false;
        }

        var changed = false;
        for (var index = 0; index < properties.FlagGroups.Count && index < isChecked.Count; index++)
        {
            var group = properties.FlagGroups[index];
            if (!group.CanChange)
            {
                continue;
            }

            switch (group.Kind)
            {
                case PropertyFlagKind.Attributes:
                {
                    var attributes = group.Flags.Where(flag => flag.IsSet).Sum(flag => flag.Value);
                    var newAttributes = EntryProperties.GetValue(group, attributes, isChecked[index]);
                    if (newAttributes != attributes)
                    {
                        await _nestedVolume.SetAttributes(entry.Name, (EntryAttributes)newAttributes,
                            cancellationToken);
                        changed = true;
                    }

                    break;
                }
                case PropertyFlagKind.ProtectionBits
                    when (entry.AmigaMetadata ?? properties.UaeMetadata?.Metadata) is { } metadata:
                {
                    var protectionBits = EntryProperties.GetValue(group, metadata.ProtectionBits, isChecked[index]);
                    if (protectionBits == metadata.ProtectionBits)
                    {
                        break;
                    }

                    if (entry.AmigaMetadata != null)
                    {
                        await _nestedVolume.SetAmigaMetadata(entry.Name,
                            metadata with { ProtectionBits = protectionBits }, cancellationToken);
                    }
                    else
                    {
                        await UaeMetadataUpdater.SetMetadataAsync(_nestedVolume, entry,
                            metadata with { ProtectionBits = protectionBits }, UaeMetadata,
                            !IsInImage ? LocalPath : null, cancellationToken);
                    }

                    changed = true;
                    break;
                }
            }
        }

        return changed;
    }

    /// <summary>
    ///     True when icon positions of a navigation mode can be saved in the current location (see
    ///     <see cref="SaveIconPositionsAsync" />): not in the drive list or inside a read-only image.
    /// </summary>
    public bool CanSaveIconPositions => CreateDirectoryNotSupportedMessage == null;

    /// <summary>
    ///     Lays out listed entries of the current location as icons of a navigation mode of a plugin (see
    ///     <see cref="INavigationMode.Layout" />), without the parent entry "..". Reads with the pane's volume, so call it
    ///     on the thread using the pane. The options are the values of <see cref="INavigationMode.Options" /> by name.
    /// </summary>
    public Task<NavigationLayout> LayoutAsync(INavigationMode navigationMode, IReadOnlyList<IEntry> entries,
        IReadOnlyDictionary<string, bool>? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(navigationMode);
        ArgumentNullException.ThrowIfNull(entries);

        return navigationMode.Layout(_nestedVolume, LayoutEntries(entries), options, cancellationToken);
    }

    /// <summary>
    ///     Saves the positions of icons of a navigation mode moved by the user in the current location (see
    ///     <see cref="INavigationMode.SavePositions" />). Writes with the pane's volume, so call it on the thread using
    ///     the pane. Throws <see cref="NotSupportedException" /> when <see cref="CanSaveIconPositions" /> is false.
    /// </summary>
    public Task SaveIconPositionsAsync(INavigationMode navigationMode, IReadOnlyList<IEntry> entries,
        IReadOnlyList<NavigationIconPosition> positions, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(navigationMode);
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(positions);

        if (!CanSaveIconPositions)
        {
            throw new NotSupportedException("Icon positions can't be saved in a read-only location");
        }

        return navigationMode.SavePositions(_nestedVolume, LayoutEntries(entries), positions, cancellationToken);
    }

    private static List<IEntry> LayoutEntries(IEnumerable<IEntry> entries) =>
        entries.Where(entry => entry.Name != Paths.Parent).ToList();

    private async Task<UaeMetadataNode?> FindUaeMetadataAsync(IEntry entry, CancellationToken cancellationToken)
    {
        if (UaeMetadata == UaeMetadataFormat.None || await _nestedVolume.SupportsAmigaMetadata(cancellationToken))
        {
            return null;
        }

        // only the entry and the metadata files it can have are read
        var metafileName = entry.Name + Hst.Amiga.DataTypes.UaeMetafiles.Constants.UaeMetafileExtension;
        var entries = await _nestedVolume.ListEntries(cancellationToken)
            .Where(listedEntry => listedEntry.Name.Equals(entry.Name, StringComparison.Ordinal) ||
                                  listedEntry.Name.Equals(Hst.Amiga.DataTypes.UaeFsDbs.Constants.UaeFsDbFileName,
                                      StringComparison.OrdinalIgnoreCase) ||
                                  listedEntry.Name.Equals(metafileName, StringComparison.OrdinalIgnoreCase))
            .ToListAsync(cancellationToken);
        var localDirectory = !IsInImage ? LocalPath : null;
        var metadata = await UaeMetadataReader.ReadAsync(_nestedVolume, entries, localDirectory != null,
            cancellationToken, localDirectory);
        return metadata.Find(entry.Name);
    }

    /// <summary>
    ///     Counts the size and number of entries of entries in the current location for the properties dialog,
    ///     directories recursively. Like copying, it doesn't use the pane's volume, so it can run on a background
    ///     thread: local directories are read from the file system (with the size on disk on Windows), other
    ///     locations with a volume of their own (see <see cref="VolumeContents.CountContentsAsync" />).
    /// </summary>
    public async Task<EntryContents> CountContentsAsync(IReadOnlyCollection<IEntry> entries,
        IProgress<EntryContents>? progress = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entries);

        if (!IsInImage && _rootVolume is LocalVolume)
        {
            return LocalPath is { } localPath
                ? FileSystemDetails.CountContents(localPath, entries, progress, cancellationToken)
                : new EntryContents(0, null, 0, 0, true);
        }

        using var location = await OpenLocationAsync(cancellationToken);
        return await location.Volume.CountContentsAsync(entries, progress, cancellationToken);
    }

    /// <summary>
    ///     Why a directory can't be created in the current location, e.g. in the local drive list or inside a read-only
    ///     image (images are writable when their file volume provider can write and their local file can be written).
    ///     Null when creating a directory is supported, front-ends then ask for its name.
    /// </summary>
    public string? CreateDirectoryNotSupportedMessage =>
        _rootVolume is LocalVolume && !_nestedVolume.CurrentPathComponents.Any()
            ? "Creating a directory is not supported in the drive list"
            : IsInImage && !_images.Peek().IsWritable
                ? "Creating a directory is not supported in a read-only image"
                : null;

    /// <summary>
    ///     Creates a directory in the current location. The created directory is selected by
    ///     <see cref="GetSelectedIndex" /> when the entries are listed again. Throws <see cref="NotSupportedException" />
    ///     when the location doesn't support it (see <see cref="CreateDirectoryNotSupportedMessage" />) and
    ///     <see cref="IOException" /> when the name is invalid or an entry with the name already exists.
    /// </summary>
    public async Task CreateDirectoryAsync(string name, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(name);

        if (CreateDirectoryNotSupportedMessage is { } message)
        {
            throw new NotSupportedException(message);
        }

        name = ValidateName(name, "Directory name");
        if (await FindEntryAsync(name, cancellationToken) is { } existingEntry)
        {
            throw new IOException($"'{existingEntry.Name}' already exists");
        }

        await _nestedVolume.CreateContainer(name, cancellationToken);
        _newEntryName = name;
    }

    /// <summary>
    ///     Why a volume can't be created in the current location, e.g. in the local drive list, inside a read-only image
    ///     or when no plugin can create volumes. Null when supported, front-ends then show the create dialog.
    /// </summary>
    public string? GetCreateVolumeNotSupportedMessage(IReadOnlyCollection<IVolumeCreator> creators)
    {
        ArgumentNullException.ThrowIfNull(creators);

        return creators.Count == 0
            ? "No plugin can create volumes"
            : _rootVolume is LocalVolume && !_nestedVolume.CurrentPathComponents.Any()
                ? "Creating a volume is not supported in the drive list"
                : IsInImage && !_images.Peek().IsWritable
                    ? "Creating a volume is not supported in a read-only image"
                    : null;
    }

    /// <summary>
    ///     Creates a volume as a file in the current location with a volume creator, e.g. an empty Amiga disk file. The
    ///     file name gets the creator's extension when missing (see <see cref="VolumeCreation.GetFileName" />) and the
    ///     input entered for the creator's fields is checked (see <see cref="VolumeCreation.GetValues" />). Local files are
    ///     written directly, otherwise the volume is created in a temporary file and copied. The created file is selected
    ///     by <see cref="GetSelectedIndex" /> when the entries are listed again. Throws <see cref="NotSupportedException" />
    ///     when the location doesn't support it and <see cref="IOException" /> when the name or input is invalid or an
    ///     entry with the name already exists; a partially created file is deleted.
    /// </summary>
    public async Task CreateVolumeAsync(IVolumeCreator creator, string fileName,
        IReadOnlyDictionary<string, object?> input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(creator);
        ArgumentNullException.ThrowIfNull(fileName);
        ArgumentNullException.ThrowIfNull(input);

        if (GetCreateVolumeNotSupportedMessage([creator]) is { } message)
        {
            throw new NotSupportedException(message);
        }

        var name = ValidateName(VolumeCreation.GetFileName(creator, fileName), "File name");
        var values = VolumeCreation.GetValues(creator, input);
        if (await FindEntryAsync(name, cancellationToken) is { } existingEntry)
        {
            throw new IOException($"'{existingEntry.Name}' already exists");
        }

        if (!IsInImage && LocalPath is { } localPath)
        {
            await CreateLocalVolumeAsync(creator, Path.Combine(localPath, name), values, cancellationToken);
        }
        else
        {
            await CreateVolumeInVolumeAsync(creator, name, values, cancellationToken);
        }

        _newEntryName = name;
    }

    /// <summary>Creates a volume in a new local file, on the thread pool as creating may take a while.</summary>
    private static async Task CreateLocalVolumeAsync(IVolumeCreator creator, string path, VolumeCreatorValues values,
        CancellationToken cancellationToken)
    {
        await Task.Run(async () =>
        {
            var stream = new FileStream(path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None);
            try
            {
                await using (stream)
                {
                    await creator.Create(stream, values, cancellationToken);
                }
            }
            catch (Exception)
            {
                TryDeleteFile(path);
                throw;
            }
        }, cancellationToken);
    }

    /// <summary>
    ///     Creates a volume in a temporary file and copies it to the current volume, e.g. a writable image, as the
    ///     volume's streams may not be seekable.
    /// </summary>
    private async Task CreateVolumeInVolumeAsync(IVolumeCreator creator, string name, VolumeCreatorValues values,
        CancellationToken cancellationToken)
    {
        await using var temporaryStream = new FileStream(Path.GetTempFileName(), FileMode.Create,
            FileAccess.ReadWrite, FileShare.None, 4096, FileOptions.DeleteOnClose);
        await Task.Run(() => creator.Create(temporaryStream, values, cancellationToken), cancellationToken);
        temporaryStream.Position = 0;

        var written = false;
        try
        {
            await using var stream = await _nestedVolume.OpenWrite(name, cancellationToken);
            written = true;
            await temporaryStream.CopyToAsync(stream, cancellationToken);
        }
        catch (Exception) when (written)
        {
            try
            {
                await _nestedVolume.Delete(name, CancellationToken.None);
            }
            catch (Exception)
            {
                // keep the original error
            }

            throw;
        }
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception)
        {
            // keep the original error
        }
    }

    /// <summary>
    ///     Why entries can't be moved from the current location, e.g. in the local drive list or inside a read-only
    ///     image. Null when moving is supported, front-ends then show the move dialog.
    /// </summary>
    public string? MoveNotSupportedMessage =>
        _rootVolume is LocalVolume && !_nestedVolume.CurrentPathComponents.Any()
            ? "Moving is not supported in the drive list"
            : IsInImage && !_images.Peek().IsWritable
                ? "Moving is not supported in a read-only image"
                : null;

    /// <summary>
    ///     The new name when a move destination entered in the move dialog renames the entry instead: a single entry
    ///     moved to a name without path separators, which isn't the other pane's path or an existing directory in the
    ///     current location (entries are moved into an existing directory). Null when the entries are moved.
    /// </summary>
    public async Task<string?> GetRenameNameAsync(IReadOnlyCollection<IEntry> entries,
        NavigationContext destinationContext, string destinationPath, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(destinationContext);
        ArgumentNullException.ThrowIfNull(destinationPath);

        var name = destinationPath.Trim();
        if (entries.Count != 1 || name.Length == 0 || name is "." or ".." || name.IndexOfAny(['/', '\\']) >= 0 ||
            Path.IsPathRooted(name) || name.Equals(destinationContext.CurrentPath, StringComparison.Ordinal))
        {
            return null;
        }

        var entry = entries.First();
        var existingEntry = await FindEntryAsync(name, cancellationToken);
        return existingEntry is { Type: EntryType.Container } && !existingEntry.Name.Equals(entry.Name, NameComparison)
            ? null
            : name;
    }

    /// <summary>
    ///     Renames an entry in the current location. The renamed entry is selected by <see cref="GetSelectedIndex" />
    ///     when the entries are listed again. Throws <see cref="NotSupportedException" /> when the location doesn't
    ///     support it (see <see cref="MoveNotSupportedMessage" />) and <see cref="IOException" /> when the name is
    ///     invalid or another entry with the name already exists. When UAE metadata is handled (see
    ///     <see cref="UaeMetadata" />) in a volume not storing Amiga metadata, the name is the new Amiga name of
    ///     entries with UAE metadata and can contain characters the volume can't store, see
    ///     <see cref="UaeMetadataUpdater.RenameAsync" />.
    /// </summary>
    public async Task RenameAsync(IEntry entry, string newName, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(newName);

        if (MoveNotSupportedMessage is { } message)
        {
            throw new NotSupportedException(message);
        }

        var handlesUaeMetadata = UaeMetadata != UaeMetadataFormat.None &&
                                 !await _nestedVolume.SupportsAmigaMetadata(cancellationToken);
        newName = ValidateName(newName, "Name", !handlesUaeMetadata);
        if (newName.Equals(entry.Name, StringComparison.Ordinal))
        {
            return;
        }

        // only the case of the name may change, e.g. "a.txt" to "A.txt" on Windows
        if (await FindEntryAsync(newName, cancellationToken) is { } existingEntry &&
            !existingEntry.Name.Equals(entry.Name, StringComparison.Ordinal))
        {
            throw new IOException($"'{existingEntry.Name}' already exists");
        }

        var localDirectory = !IsInImage ? LocalPath : null;
        var normalName = handlesUaeMetadata
            ? await UaeMetadataUpdater.RenameAsync(_nestedVolume, entry, newName, UaeMetadata, localDirectory,
                cancellationToken)
            : null;
        if (normalName == null)
        {
            await _nestedVolume.Rename(entry.Name, newName, cancellationToken);
        }

        _markedEntries.Remove(entry.Name);
        _newEntryName = normalName ?? newName;
    }

    /// <summary>
    ///     Moves entries in the current location to a destination, resolved like <see cref="CopyAsync" />. When the
    ///     destination is a new name for a single entry (see <see cref="GetRenameNameAsync" />), the entry is renamed
    ///     instead. Entries are moved by renaming them when the source and destination are on the same local drive,
    ///     the filter matches all items and the destination has no entry with the name; otherwise they are copied and
    ///     deleted from the source (see <see cref="VolumeMove.MoveToAsync" />), asking
    ///     <paramref name="confirmOverwrite" /> before overwriting existing files.
    /// </summary>
    public async Task MoveAsync(IReadOnlyCollection<IEntry> entries, NavigationContext destinationContext,
        string destinationPath, string filter, IProgress<CopyProgress>? progress = null,
        Func<OverwriteRequest, CancellationToken, Task<OverwriteAnswer>>? confirmOverwrite = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(destinationContext);

        if (MoveNotSupportedMessage is { } message)
        {
            throw new NotSupportedException(message);
        }

        if (await GetRenameNameAsync(entries, destinationContext, destinationPath, cancellationToken) is { } newName)
        {
            await RenameAsync(entries.First(), newName, cancellationToken);
            return;
        }

        using var source = await OpenLocationAsync(cancellationToken);
        var (destination, targetName) = await OpenDestinationAsync(entries, destinationContext, destinationPath,
            cancellationToken);
        using (destination)
        {
            ThrowIfCopyingOntoItself(source.Path, entries, destination.Path, targetName, "move");
            var remainingEntries = await MoveOnSameDriveAsync(source, entries, destination, filter, targetName,
                progress, cancellationToken);
            if (remainingEntries.Count > 0)
            {
                await source.Volume.MoveToAsync(remainingEntries, destination.Volume,
                    new CopyOptions(filter, targetName, confirmOverwrite, UaeMetadata), progress, cancellationToken);
            }
        }
    }

    /// <summary>
    ///     Moves entries by renaming them when both locations are local directories on the same drive and all items
    ///     are moved, which is fast and needs no extra space. Returns the entries left to move by copying: all entries
    ///     when renaming isn't possible, otherwise entries that exist in the destination (to confirm overwriting and
    ///     merge directories). When UAE metadata is handled, the metadata of moved entries is moved too, while
    ///     entries with metadata moved to another name or whose Amiga name is in the destination's metadata are left
    ///     to copy, which gives them their names in the destination. The metadata files themselves aren't moved.
    /// </summary>
    private async Task<IReadOnlyList<IEntry>> MoveOnSameDriveAsync(VolumeLocation source,
        IReadOnlyCollection<IEntry> entries, VolumeLocation destination, string filter, string? targetName,
        IProgress<CopyProgress>? progress, CancellationToken cancellationToken)
    {
        if (source.Volume is not LocalVolume || destination.Volume is not LocalVolume ||
            !(string.IsNullOrWhiteSpace(filter) || filter.Trim() is EntryFilter.All or "*.*") ||
            FindDrive(source.Path) is not { } sourceDrive || FindDrive(destination.Path) is not { } destinationDrive ||
            !sourceDrive.RootDirectory.FullName.Equals(destinationDrive.RootDirectory.FullName, NameComparison))
        {
            return entries.ToList();
        }

        // alternate data streams move with the entries
        var sourceMetadata = await UaeMetadataUpdater.ReadAsync(source.Volume, UaeMetadata, false, null,
            cancellationToken);
        var destinationMetadata = sourceMetadata.IsEmpty
            ? UaeDirectoryMetadata.Empty
            : await UaeMetadataUpdater.ReadAsync(destination.Volume, UaeMetadata, false, null, cancellationToken);

        var remainingEntries = new List<IEntry>();
        var movedEntries = new List<IEntry>();
        try
        {
            foreach (var entry in entries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (entry.Type == EntryType.Item && sourceMetadata.IsMetadataFile(entry.Name))
                {
                    continue;
                }

                var sourcePath = Path.Combine(source.Path, entry.Name);
                var destinationPath = Path.Combine(destination.Path, targetName ?? entry.Name);
                var node = sourceMetadata.Find(entry.Name);
                if (File.Exists(destinationPath) || Directory.Exists(destinationPath) ||
                    (node != null && (targetName != null ||
                                      destinationMetadata.FindOtherByAmigaName(node.AmigaName, null) != null)))
                {
                    remainingEntries.Add(entry);
                    continue;
                }

                progress?.Report(new CopyProgress(entry.Name, 0, 0, 0, 0, movedEntries.Count, entries.Count, 0,
                    null));
                if (entry.Type == EntryType.Container)
                {
                    Directory.Move(sourcePath, destinationPath);
                }
                else
                {
                    File.Move(sourcePath, destinationPath);
                }

                movedEntries.Add(entry);
            }
        }
        catch when (!sourceMetadata.IsEmpty)
        {
            // the metadata of the entries moved so far is moved too, keeping the error that stopped the move
            try
            {
                await UaeMetadataUpdater.MoveAsync(source.Volume, sourceMetadata, destination.Volume, movedEntries,
                    CancellationToken.None);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
                                                  or NotSupportedException)
            {
            }

            throw;
        }

        if (!sourceMetadata.IsEmpty)
        {
            await UaeMetadataUpdater.MoveAsync(source.Volume, sourceMetadata, destination.Volume, movedEntries,
                cancellationToken);
        }

        if (movedEntries.Count > 0)
        {
            progress?.Report(new CopyProgress(string.Empty, 0, 0, 0, 0, movedEntries.Count, entries.Count, 0,
                null));
        }

        return remainingEntries;
    }

    private static StringComparison NameComparison =>
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    /// <summary>
    ///     Trims an entry name entered by the user and checks it's valid: an empty name, "." and "..", and names with
    ///     path separators, control characters or (with <paramref name="checkFileNameCharacters" />) invalid file name
    ///     characters throw <see cref="IOException" />.
    /// </summary>
    private static string ValidateName(string name, string description, bool checkFileNameCharacters = true)
    {
        name = name.Trim();
        if (name.Length == 0)
        {
            throw new IOException($"{description} is empty");
        }

        if (name is "." or ".." || name.IndexOfAny(['/', '\\']) >= 0 || name.Any(char.IsControl) ||
            (checkFileNameCharacters && name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0))
        {
            throw new IOException($"Invalid {description.ToLowerInvariant()} '{name}'");
        }

        return name;
    }

    /// <summary>Finds an entry in the current location by name, case-insensitive on Windows.</summary>
    private async Task<IEntry?> FindEntryAsync(string name, CancellationToken cancellationToken)
    {
        await foreach (var entry in _nestedVolume.ListEntries(cancellationToken))
        {
            if (entry.Name.Equals(name, NameComparison))
            {
                return entry;
            }
        }

        return null;
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
        string destinationPath, string? targetName, string operation)
    {
        var comparison = NameComparison;
        sourcePath = Path.TrimEndingDirectorySeparator(sourcePath);
        destinationPath = Path.TrimEndingDirectorySeparator(destinationPath);
        var entryList = entries.ToList();

        if (destinationPath.Equals(sourcePath, comparison) &&
            (targetName == null || entryList.Any(entry => entry.Name.Equals(targetName, comparison))))
        {
            throw new IOException($"Cannot {operation} entries onto themselves");
        }

        foreach (var entry in entryList.Where(entry => entry.Type == EntryType.Container))
        {
            if (IsInDirectory(destinationPath, Path.Combine(sourcePath, entry.Name), comparison))
            {
                throw new IOException($"Cannot {operation} directory '{entry.Name}' into itself");
            }
        }
    }

    /// <summary>Closes images opened in the current volume and the volume itself, when disposable.</summary>
    private void CloseVolumes()
    {
        while (IsInImage)
        {
            _images.Pop().Dispose();
        }

        (_rootVolume as IDisposable)?.Dispose();
    }

    public void Dispose()
    {
        CloseIconLocation();
        CloseVolumes();
    }

    /// <summary>
    ///     A file opened as a volume (an image) by a file volume provider.
    /// </summary>
    /// <param name="fileVolumeProvider">File volume provider that detected the file and created the volume.</param>
    /// <param name="name">Name of the file.</param>
    /// <param name="parentPathComponents">
    ///     Directory of the file in the image it's in, used to open it again. Empty for a file in the root volume.
    /// </param>
    /// <param name="volume">Volume of the image, a view of a shared image when shared.</param>
    /// <param name="stream">Stream of the file, closed after the volume. Null when shared.</param>
    /// <param name="sharedId">Id of the file of a shared image (see <see cref="SharedImages" />).</param>
    /// <param name="isWritable">True when the image can be changed.</param>
    private sealed class OpenedImage(IFileVolumeProvider fileVolumeProvider, string name,
        IReadOnlyList<string> parentPathComponents, IVolume volume, Stream? stream, string? sharedId,
        bool isWritable) : IDisposable
    {
        public IFileVolumeProvider FileVolumeProvider { get; } = fileVolumeProvider;

        public string Name { get; } = name;

        public IReadOnlyList<string> ParentPathComponents { get; } = parentPathComponents;

        public IVolume Volume { get; } = volume;

        public string? SharedId { get; } = sharedId;

        public bool IsWritable { get; } = isWritable;

        public void Dispose()
        {
            (Volume as IDisposable)?.Dispose();
            stream?.Dispose();
        }
    }

    /// <summary>Resources disposed in reverse order of opening, e.g. images inside images.</summary>
    private sealed class Resources(Stack<IDisposable> resources) : IDisposable
    {
        public void Dispose()
        {
            while (resources.Count > 0)
            {
                resources.Pop().Dispose();
            }
        }
    }
}
