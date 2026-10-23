using System.Diagnostics;
using Hst.Commander.Abstractions;

namespace Hst.Commander.Core.Volumes;

/// <summary>
/// Options for <see cref="VolumeCopy.CopyToAsync" />.
/// </summary>
/// <param name="Filter">Wildcard filter of the items to copy, see <see cref="EntryFilter" />. Containers are always copied.</param>
/// <param name="TargetName">Name to copy a single item as, e.g. to copy "a.txt" as "b.txt". Null keeps the name.</param>
/// <param name="ConfirmOverwrite">
/// Asks whether to overwrite an item that already exists in the destination. Null overwrites existing items.
/// </param>
/// <param name="UaeMetadata">
/// How Amiga names, protection bits and comments are stored when copying from a volume storing them (see
/// <see cref="IVolume.SupportsAmigaMetadata" />) to one that doesn't, e.g. from an Amiga disk to a local directory.
/// </param>
public record CopyOptions(
    string Filter = EntryFilter.All,
    string? TargetName = null,
    Func<OverwriteRequest, CancellationToken, Task<OverwriteAnswer>>? ConfirmOverwrite = null,
    UaeMetadataFormat UaeMetadata = UaeMetadataFormat.UaeFsDb);

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
    /// Amiga names, protection bits and comments are kept between volumes storing them (Amiga file systems), stored
    /// as UAE metadata (<see cref="CopyOptions.UaeMetadata" />) when copying from them to other volumes and read from
    /// UAE metadata when copying from other volumes to them, without copying the metadata files.
    /// </summary>
    /// <param name="source">Volume to copy entries from.</param>
    /// <param name="entries">Entries in the current directory of the source volume to copy.</param>
    /// <param name="destination">Volume to copy entries to.</param>
    /// <param name="options">Copy options, e.g. the filter of items to copy.</param>
    /// <param name="progress">Receives the copy progress, at most every 100 ms and when an item is copied.</param>
    /// <param name="cancellationToken"></param>
    public static Task CopyToAsync(this IVolume source, IEnumerable<IEntry> entries, IVolume destination,
        CopyOptions? options = null, IProgress<CopyProgress>? progress = null,
        CancellationToken cancellationToken = default) =>
        TransferAsync(source, entries, destination, options, progress, deleteSource: false, cancellationToken);

    /// <summary>
    /// Copies entries like <see cref="CopyToAsync" />. With <paramref name="deleteSource" /> (used by
    /// <see cref="VolumeMove" />), each item is deleted from the source once it is copied and each container once it
    /// is empty, so items skipped or not matching the filter are kept with their containers. UAE metadata of moved
    /// entries is removed from the source.
    /// </summary>
    internal static async Task TransferAsync(IVolume source, IEnumerable<IEntry> entries, IVolume destination,
        CopyOptions? options, IProgress<CopyProgress>? progress, bool deleteSource,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(destination);
        options ??= new CopyOptions();

        var metadataMode = await GetMetadataModeAsync(source, destination, cancellationToken);
        var sourceMetadata = new Dictionary<string, UaeDirectoryMetadata>();
        var metadata = UaeDirectoryMetadata.Empty;
        if (metadataMode == MetadataMode.ReadUae)
        {
            var sourceEntries = await source.ListEntries(cancellationToken).ToListAsync(cancellationToken);
            metadata = await UaeMetadataReader.ReadAsync(source, sourceEntries, true, cancellationToken);
            sourceMetadata[GetKey([])] = metadata;
        }

        var filter = new EntryFilter(options.Filter);
        var items = new List<CopyItem>();
        foreach (var entry in entries)
        {
            await AddCopyItemsAsync(source, [], entry, filter, metadata, metadataMode == MetadataMode.ReadUae,
                sourceMetadata, items, cancellationToken);
        }

        var state = new CopyState(items, progress);
        var sourceDirectory = new List<string>();
        var destinationDirectory = new List<string>();
        var existingEntries = new ExistingEntries(destination);
        var overwriteAll = options.ConfirmOverwrite == null;

        // directories in the destination by the directories in the source, as names can differ with UAE metadata
        var destinationDirectories = new Dictionary<string, IReadOnlyList<string>> { [GetKey([])] = [] };
        var writers = new Dictionary<string, (IReadOnlyList<string> Directory, UaeMetadataWriter Writer)>();

        try
        {
            foreach (var item in items)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await ChangeDirectoryAsync(source, sourceDirectory, item.Directory, cancellationToken);
                var itemDestinationDirectory = destinationDirectories[GetKey(item.Directory)];
                await ChangeDirectoryAsync(destination, destinationDirectory, itemDestinationDirectory,
                    cancellationToken);

                var amigaName = item.Directory.Count == 0 && options.TargetName != null
                    ? options.TargetName
                    : item.AmigaName ?? item.Entry.Name;
                var writer = metadataMode == MetadataMode.WriteUae
                    ? await GetWriterAsync(destination, itemDestinationDirectory, options.UaeMetadata, writers,
                        cancellationToken)
                    : null;
                var targetName = writer?.GetName(amigaName) ?? amigaName;
                item.DestinationName = targetName;

                if (item.Entry.Type == EntryType.Container)
                {
                    await destination.CreateContainer(targetName, cancellationToken);
                    destinationDirectories[GetKey([..item.Directory, item.Entry.Name])] =
                        [..itemDestinationDirectory, targetName];
                    if (writer != null)
                    {
                        await writer.SetAsync(destination, amigaName, targetName, item.Metadata,
                            item.Entry.ModifiedDate, cancellationToken);
                    }

                    continue;
                }

                if (!overwriteAll && options.ConfirmOverwrite != null &&
                    await existingEntries.GetAsync(itemDestinationDirectory, targetName, cancellationToken) is
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
                if (writer != null)
                {
                    await writer.SetAsync(destination, amigaName, targetName, item.Metadata, item.Entry.ModifiedDate,
                        cancellationToken);
                }
                else
                {
                    await SetAmigaMetadataAsync(destination, targetName, item, metadataMode, cancellationToken);
                }

                await SetModifiedDateAsync(destination, targetName, item.Entry, cancellationToken);
                if (deleteSource)
                {
                    await source.Delete(item.Entry.Name, cancellationToken);
                }
            }

            // the "_UAEFSDB.___" files are written before the dates of the containers, as writing changes them
            await FlushWritersAsync(destination, destinationDirectory, writers, cancellationToken);
        }
        catch
        {
            // the metadata of the entries copied is kept, as they may have normal names
            await FlushWritersAfterErrorAsync(destination, destinationDirectory, writers);
            throw;
        }

        // containers are in the order they are created, so in reverse their entries come before them
        foreach (var item in items.Where(item => item.Entry.Type == EntryType.Container).Reverse())
        {
            await ChangeDirectoryAsync(destination, destinationDirectory,
                destinationDirectories[GetKey(item.Directory)], cancellationToken);
            await SetAmigaMetadataAsync(destination, item.DestinationName, item, metadataMode, cancellationToken);
            await SetModifiedDateAsync(destination, item.DestinationName, item.Entry, cancellationToken);
            if (deleteSource)
            {
                // the metadata of moved entries is removed first, so the container is empty
                List<string> containerDirectory = [..item.Directory, item.Entry.Name];
                if (sourceMetadata.TryGetValue(GetKey(containerDirectory), out var containerMetadata))
                {
                    await ChangeDirectoryAsync(source, sourceDirectory, containerDirectory, cancellationToken);
                    await UaeMetadataReader.RemoveMissingAsync(source, containerMetadata, cancellationToken);
                }

                await ChangeDirectoryAsync(source, sourceDirectory, item.Directory, cancellationToken);
                await DeleteIfEmptyAsync(source, item.Entry.Name, cancellationToken);
            }
        }

        await ChangeDirectoryAsync(source, sourceDirectory, [], cancellationToken);
        if (deleteSource && sourceMetadata.TryGetValue(GetKey([]), out var startMetadata))
        {
            await UaeMetadataReader.RemoveMissingAsync(source, startMetadata, cancellationToken);
        }

        await ChangeDirectoryAsync(destination, destinationDirectory, [], cancellationToken);
        state.Report(force: true);
    }

    /// <summary>How Amiga names, protection bits and comments are copied, by the volumes storing them or not.</summary>
    private enum MetadataMode
    {
        /// <summary>Neither volume stores them, entries are copied as they are.</summary>
        None,

        /// <summary>Both volumes store them, they are copied as they are.</summary>
        Amiga,

        /// <summary>Only the source stores them, they are written as UAE metadata in the destination.</summary>
        WriteUae,

        /// <summary>Only the destination stores them, they are read from UAE metadata in the source.</summary>
        ReadUae
    }

    private static async Task<MetadataMode> GetMetadataModeAsync(IVolume source, IVolume destination,
        CancellationToken cancellationToken) =>
        (await source.SupportsAmigaMetadata(cancellationToken),
                await destination.SupportsAmigaMetadata(cancellationToken)) switch
            {
                (true, true) => MetadataMode.Amiga,
                (true, false) => MetadataMode.WriteUae,
                (false, true) => MetadataMode.ReadUae,
                _ => MetadataMode.None
            };

    /// <summary>Key of a directory relative to the directory the copy started in.</summary>
    private static string GetKey(IEnumerable<string> directory) => string.Join('\0', directory);

    /// <summary>
    /// Adds the entry to copy and, for containers, the entries within it. Containers are added before their
    /// entries, so they are created before entries are copied into them. The entries get their Amiga names,
    /// protection bits and comments from the UAE metadata of their directory, whose metadata files are skipped; with
    /// <paramref name="readMetadata" />, the UAE metadata of each container is read.
    /// </summary>
    private static async Task AddCopyItemsAsync(IVolume source, IReadOnlyList<string> directory, IEntry entry,
        EntryFilter filter, UaeDirectoryMetadata metadata, bool readMetadata,
        Dictionary<string, UaeDirectoryMetadata> sourceMetadata, List<CopyItem> items,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (entry.Name.Equals(Paths.Parent, StringComparison.Ordinal) ||
            (entry.Type == EntryType.Item && metadata.IsMetadataFile(entry.Name)))
        {
            return;
        }

        var node = metadata.Find(entry.Name);
        var item = new CopyItem(directory, entry, node?.AmigaName, entry.AmigaMetadata ?? node?.Metadata);
        if (entry.Type == EntryType.Item)
        {
            if (filter.IsMatch(entry.Name))
            {
                items.Add(item);
            }

            return;
        }

        items.Add(item);

        await source.ChangeDirectory([entry.Name], cancellationToken);
        var childEntries = await source.ListEntries(cancellationToken).ToListAsync(cancellationToken);
        var childDirectory = directory.Append(entry.Name).ToList();
        var childMetadata = UaeDirectoryMetadata.Empty;
        if (readMetadata)
        {
            childMetadata = await UaeMetadataReader.ReadAsync(source, childEntries, true, cancellationToken);
            if (!childMetadata.IsEmpty)
            {
                sourceMetadata[GetKey(childDirectory)] = childMetadata;
            }
        }

        foreach (var childEntry in childEntries)
        {
            await AddCopyItemsAsync(source, childDirectory, childEntry, filter, childMetadata, readMetadata,
                sourceMetadata, items, cancellationToken);
        }

        await source.ChangeDirectory([Paths.Parent], cancellationToken);
    }

    /// <summary>
    /// Sets the Amiga protection bits and comment of a copied entry when the destination stores them, from the
    /// source entry or its UAE metadata.
    /// </summary>
    private static async Task SetAmigaMetadataAsync(IVolume destination, string name, CopyItem item,
        MetadataMode metadataMode, CancellationToken cancellationToken)
    {
        if (metadataMode is MetadataMode.Amiga or MetadataMode.ReadUae && item.Metadata is { } metadata)
        {
            await destination.SetAmigaMetadata(name, metadata, cancellationToken);
        }
    }

    /// <summary>Writer of the UAE metadata of a destination directory, created when first used.</summary>
    private static async Task<UaeMetadataWriter> GetWriterAsync(IVolume destination,
        IReadOnlyList<string> directory, UaeMetadataFormat format,
        Dictionary<string, (IReadOnlyList<string> Directory, UaeMetadataWriter Writer)> writers,
        CancellationToken cancellationToken)
    {
        var key = GetKey(directory);
        if (!writers.TryGetValue(key, out var directoryWriter))
        {
            directoryWriter = (directory, await UaeMetadataWriter.CreateAsync(destination, format, cancellationToken));
            writers[key] = directoryWriter;
        }

        return directoryWriter.Writer;
    }

    private static async Task FlushWritersAsync(IVolume destination, List<string> destinationDirectory,
        Dictionary<string, (IReadOnlyList<string> Directory, UaeMetadataWriter Writer)> writers,
        CancellationToken cancellationToken)
    {
        foreach (var (directory, writer) in writers.Values)
        {
            await ChangeDirectoryAsync(destination, destinationDirectory, directory, cancellationToken);
            await writer.FlushAsync(destination, cancellationToken);
        }
    }

    private static async Task FlushWritersAfterErrorAsync(IVolume destination, List<string> destinationDirectory,
        Dictionary<string, (IReadOnlyList<string> Directory, UaeMetadataWriter Writer)> writers)
    {
        try
        {
            await FlushWritersAsync(destination, destinationDirectory, writers, CancellationToken.None);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
                                              or NotSupportedException)
        {
            // keep the error that stopped the copy rather than the failed metadata
        }
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

    /// <summary>Deletes a container in the current directory when it is empty.</summary>
    private static async Task DeleteIfEmptyAsync(IVolume volume, string name, CancellationToken cancellationToken)
    {
        await volume.ChangeDirectory([name], cancellationToken);
        var isEmpty = !await volume.ListEntries(cancellationToken).AnyAsync(cancellationToken);
        await volume.ChangeDirectory([Paths.Parent], cancellationToken);
        if (isEmpty)
        {
            await volume.Delete(name, cancellationToken);
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
    /// copy started in, by going up to their common parent and down into the target. Also used by
    /// <see cref="VolumeDelete" />.
    /// </summary>
    internal static async Task ChangeDirectoryAsync(IVolume volume, List<string> currentDirectory,
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

    /// <summary>
    /// An entry to copy and the directory containing it, relative to the directory the copy started in, with its
    /// Amiga name from UAE metadata (null when none) and its Amiga protection bits and comment (null when none).
    /// </summary>
    private sealed class CopyItem(IReadOnlyList<string> directory, IEntry entry, string? amigaName,
        AmigaMetadata? metadata)
    {
        public IReadOnlyList<string> Directory { get; } = directory;
        public IEntry Entry { get; } = entry;
        public string? AmigaName { get; } = amigaName;
        public AmigaMetadata? Metadata { get; } = metadata;

        /// <summary>Name in the destination, set when it is copied.</summary>
        public string DestinationName { get; set; } = entry.Name;
    }

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
