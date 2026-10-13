using System.Diagnostics;
using Hst.Commander.Extensions.Volume.Abstractions;

namespace Hst.Commander.Core.Volumes;

/// <summary>
/// Options for <see cref="VolumeCopy.CopyToAsync" />.
/// </summary>
/// <param name="Filter">Wildcard filter of the items to copy, see <see cref="EntryFilter" />. Containers are always copied.</param>
/// <param name="TargetName">Name to copy a single item as, e.g. to copy "a.txt" as "b.txt". Null keeps the name.</param>
/// <param name="ConfirmOverwrite">
/// Asks whether to overwrite an item that already exists in the destination. Null overwrites existing items.
/// </param>
public record CopyOptions(
    string Filter = EntryFilter.All,
    string? TargetName = null,
    Func<OverwriteRequest, CancellationToken, Task<OverwriteAnswer>>? ConfirmOverwrite = null);

/// <summary>
/// Copies entries from one volume to another using the stream and container methods of <see cref="IVolume" />,
/// so any volume can be copied to any writable volume.
/// </summary>
public static class VolumeCopy
{
    private const int BufferSize = 1024 * 1024;
    private static readonly TimeSpan ReportInterval = TimeSpan.FromMilliseconds(100);

    /// <summary>
    /// Copies entries in the current directory of the source volume to the current directory of the destination
    /// volume. Containers are copied recursively and merged with existing containers. Existing items are overwritten
    /// when <see cref="CopyOptions.ConfirmOverwrite" /> answers yes or all (or isn't set) and skipped otherwise.
    /// An item partially copied when the copy is cancelled or fails is deleted. Modified dates of copied items and
    /// containers are preserved; containers get their dates last, as copying into them changes them. Both volumes
    /// are changed back to
    /// their current directories when the copy is done.
    /// </summary>
    /// <param name="source">Volume to copy entries from.</param>
    /// <param name="entries">Entries in the current directory of the source volume to copy.</param>
    /// <param name="destination">Volume to copy entries to.</param>
    /// <param name="options">Copy options, e.g. the filter of items to copy.</param>
    /// <param name="progress">Receives the copy progress, at most every 100 ms and when an item is copied.</param>
    /// <param name="cancellationToken"></param>
    public static async Task CopyToAsync(this IVolume source, IEnumerable<IEntry> entries, IVolume destination,
        CopyOptions? options = null, IProgress<CopyProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(destination);
        options ??= new CopyOptions();

        var filter = new EntryFilter(options.Filter);
        var items = new List<CopyItem>();
        foreach (var entry in entries)
        {
            await AddCopyItemsAsync(source, [], entry, filter, items, cancellationToken);
        }

        var state = new CopyState(items, progress);
        var sourceDirectory = new List<string>();
        var destinationDirectory = new List<string>();
        var existingEntries = new ExistingEntries(destination);
        var overwriteAll = options.ConfirmOverwrite == null;

        foreach (var item in items)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await ChangeDirectoryAsync(source, sourceDirectory, item.Directory, cancellationToken);
            await ChangeDirectoryAsync(destination, destinationDirectory, item.Directory, cancellationToken);

            if (item.Entry.Type == EntryType.Container)
            {
                await destination.CreateContainer(item.Entry.Name, cancellationToken);
                continue;
            }

            var targetName = item.Directory.Count == 0 && options.TargetName != null
                ? options.TargetName
                : item.Entry.Name;

            if (!overwriteAll && options.ConfirmOverwrite != null &&
                await existingEntries.GetAsync(destinationDirectory, targetName, cancellationToken) is
                    { } existingEntry)
            {
                state.Pause();
                var answer = await options.ConfirmOverwrite(
                    new OverwriteRequest(targetName, item.Entry, existingEntry), cancellationToken);
                state.Resume();

                if (answer == OverwriteAnswer.No)
                {
                    state.SkipItem(item);
                    continue;
                }

                overwriteAll = answer == OverwriteAnswer.All;
            }

            await CopyItemAsync(source, destination, item, targetName, state, cancellationToken);
            await SetModifiedDateAsync(destination, targetName, item.Entry, cancellationToken);
        }

        // containers are in the order they are created, so in reverse their entries come before them
        foreach (var item in items.Where(item => item.Entry.Type == EntryType.Container).Reverse())
        {
            await ChangeDirectoryAsync(destination, destinationDirectory, item.Directory, cancellationToken);
            await SetModifiedDateAsync(destination, item.Entry.Name, item.Entry, cancellationToken);
        }

        await ChangeDirectoryAsync(source, sourceDirectory, [], cancellationToken);
        await ChangeDirectoryAsync(destination, destinationDirectory, [], cancellationToken);
        state.Report(force: true);
    }

    /// <summary>
    /// Adds the entry to copy and, for containers, the entries within it. Containers are added before their
    /// entries, so they are created before entries are copied into them.
    /// </summary>
    private static async Task AddCopyItemsAsync(IVolume source, IReadOnlyList<string> directory, IEntry entry,
        EntryFilter filter, List<CopyItem> items, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (entry.Name.Equals(Paths.Parent, StringComparison.Ordinal))
        {
            return;
        }

        if (entry.Type == EntryType.Item)
        {
            if (filter.IsMatch(entry.Name))
            {
                items.Add(new CopyItem(directory, entry));
            }

            return;
        }

        items.Add(new CopyItem(directory, entry));

        await source.ChangeDirectory([entry.Name], cancellationToken);
        var childEntries = await source.ListEntries(cancellationToken).ToListAsync(cancellationToken);
        var childDirectory = directory.Append(entry.Name).ToList();
        foreach (var childEntry in childEntries)
        {
            await AddCopyItemsAsync(source, childDirectory, childEntry, filter, items, cancellationToken);
        }

        await source.ChangeDirectory([Paths.Parent], cancellationToken);
    }

    private static async Task CopyItemAsync(IVolume source, IVolume destination, CopyItem item, string targetName,
        CopyState state, CancellationToken cancellationToken)
    {
        state.StartItem(item);

        await using var sourceStream = await source.OpenRead(item.Entry.Name, cancellationToken);
        var destinationStream = await destination.OpenWrite(targetName, cancellationToken);
        try
        {
            await using (destinationStream)
            {
                var buffer = new byte[BufferSize];
                int bytesRead;
                while ((bytesRead = await sourceStream.ReadAsync(buffer, cancellationToken)) > 0)
                {
                    await destinationStream.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken);
                    state.AddBytes(bytesRead);
                }
            }
        }
        catch
        {
            // the stream is closed before deleting, so the partially copied item isn't locked
            await DeletePartialItemAsync(destination, targetName);
            throw;
        }

        state.CompleteItem();
    }

    /// <summary>
    /// Sets the modified date of a copied entry to the date of the source entry, if it has one. Destinations that
    /// can't set dates keep the date the entry was written.
    /// </summary>
    private static async Task SetModifiedDateAsync(IVolume destination, string name, IEntry sourceEntry,
        CancellationToken cancellationToken)
    {
        if (sourceEntry.ModifiedDate is not { } modifiedDate)
        {
            return;
        }

        try
        {
            await destination.SetModifiedDate(name, modifiedDate, cancellationToken);
        }
        catch (NotSupportedException)
        {
        }
    }

    private static async Task DeletePartialItemAsync(IVolume destination, string name)
    {
        try
        {
            await destination.Delete(name, CancellationToken.None);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
                                              or NotSupportedException)
        {
            // keep the error that stopped the copy rather than the failed cleanup
        }
    }

    /// <summary>
    /// Changes directory of a volume from the current to the target directory, both relative to the directory the
    /// copy started in, by going up to their common parent and down into the target.
    /// </summary>
    private static async Task ChangeDirectoryAsync(IVolume volume, List<string> currentDirectory,
        IReadOnlyList<string> targetDirectory, CancellationToken cancellationToken)
    {
        var commonLength = 0;
        while (commonLength < currentDirectory.Count && commonLength < targetDirectory.Count &&
               currentDirectory[commonLength].Equals(targetDirectory[commonLength], StringComparison.Ordinal))
        {
            commonLength++;
        }

        if (commonLength == currentDirectory.Count && commonLength == targetDirectory.Count)
        {
            return;
        }

        if (currentDirectory.Count > commonLength)
        {
            await volume.ChangeDirectory(Enumerable.Repeat(Paths.Parent, currentDirectory.Count - commonLength),
                cancellationToken);
        }

        if (targetDirectory.Count > commonLength)
        {
            await volume.ChangeDirectory(targetDirectory.Skip(commonLength).ToList(), cancellationToken);
        }

        currentDirectory.Clear();
        currentDirectory.AddRange(targetDirectory);
    }

    /// <summary>
    /// Entries in the current directory of the destination volume, listed once per directory when an item is
    /// about to be copied, to find items that already exist.
    /// </summary>
    private sealed class ExistingEntries(IVolume destination)
    {
        private static readonly StringComparer NameComparer =
            OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

        private string? _directory;
        private Dictionary<string, IEntry> _entries = new(NameComparer);

        public async Task<IEntry?> GetAsync(IReadOnlyList<string> directory, string name,
            CancellationToken cancellationToken)
        {
            var directoryKey = string.Join('\0', directory);
            if (_directory != directoryKey)
            {
                _entries = new Dictionary<string, IEntry>(NameComparer);
                await foreach (var entry in destination.ListEntries(cancellationToken))
                {
                    _entries.TryAdd(entry.Name, entry);
                }

                _directory = directoryKey;
            }

            return _entries.GetValueOrDefault(name);
        }
    }

    /// <summary>An entry to copy and the directory containing it, relative to the directory the copy started in.</summary>
    private sealed record CopyItem(IReadOnlyList<string> Directory, IEntry Entry);

    /// <summary>Tracks copied bytes and items and reports progress with speed and estimated time left.</summary>
    private sealed class CopyState(IReadOnlyList<CopyItem> items, IProgress<CopyProgress>? progress)
    {
        private readonly Stopwatch _stopwatch = Stopwatch.StartNew();
        private long _totalBytes = items
            .Where(item => item.Entry.Type == EntryType.Item)
            .Sum(item => Math.Max(0, item.Entry.Size ?? 0));
        private int _totalItems = items.Count(item => item.Entry.Type == EntryType.Item);
        private TimeSpan _lastReport = TimeSpan.MinValue;
        private CopyItem? _currentItem;
        private long _currentBytes;
        private long _copiedBytes;
        private int _copiedItems;

        public void StartItem(CopyItem item)
        {
            _currentItem = item;
            _currentBytes = 0;
            Report(force: true);
        }

        public void AddBytes(int bytes)
        {
            _currentBytes += bytes;
            _copiedBytes += bytes;
            Report(force: false);
        }

        public void CompleteItem()
        {
            _copiedItems++;
            Report(force: true);
        }

        /// <summary>Removes an item that isn't overwritten from the totals.</summary>
        public void SkipItem(CopyItem item)
        {
            _totalBytes -= Math.Max(0, item.Entry.Size ?? 0);
            _totalItems--;
            Report(force: true);
        }

        /// <summary>Stops the time used for speed and estimated time left, e.g. while asking to overwrite.</summary>
        public void Pause() => _stopwatch.Stop();

        public void Resume() => _stopwatch.Start();

        public void Report(bool force)
        {
            var elapsed = _stopwatch.Elapsed;
            if (progress == null || (!force && elapsed - _lastReport < ReportInterval))
            {
                return;
            }

            _lastReport = elapsed;

            // items may grow while copied, so the total is never less than what is copied
            var totalBytes = Math.Max(_totalBytes, _copiedBytes);
            var currentSize = Math.Max(_currentItem?.Entry.Size ?? 0, _currentBytes);
            var bytesPerSecond = elapsed.TotalSeconds > 0 ? _copiedBytes / elapsed.TotalSeconds : 0;
            TimeSpan? eta = bytesPerSecond > 0
                ? TimeSpan.FromSeconds(Math.Ceiling((totalBytes - _copiedBytes) / bytesPerSecond))
                : null;

            progress.Report(new CopyProgress(_currentItem?.Entry.Name ?? string.Empty, _currentBytes, currentSize,
                _copiedBytes, totalBytes, _copiedItems, _totalItems, bytesPerSecond, eta));
        }
    }
}
