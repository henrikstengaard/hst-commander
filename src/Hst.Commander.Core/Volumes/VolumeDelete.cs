using System.Diagnostics;
using Hst.Commander.Abstractions;

namespace Hst.Commander.Core.Volumes;

/// <summary>
/// Deletes entries from a volume using <see cref="IVolume.Delete" />, which only deletes items and empty containers,
/// so containers are emptied recursively before they are deleted.
/// </summary>
public static class VolumeDelete
{
    private static readonly TimeSpan ReportInterval = TimeSpan.FromMilliseconds(100);

    /// <summary>
    /// Deletes entries in the current directory of the volume. Containers are deleted with everything in them: the
    /// entries are scanned first to know the total, then entries within a container are deleted before the container.
    /// The volume is changed back to its current directory when the delete is done.
    /// </summary>
    /// <param name="volume">Volume to delete entries from.</param>
    /// <param name="entries">Entries in the current directory of the volume to delete.</param>
    /// <param name="progress">Receives the delete progress, at most every 100 ms and when the delete is done.</param>
    /// <param name="cancellationToken"></param>
    public static async Task DeleteAsync(this IVolume volume, IEnumerable<IEntry> entries,
        IProgress<DeleteProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(volume);
        ArgumentNullException.ThrowIfNull(entries);

        var items = new List<DeleteItem>();
        foreach (var entry in entries)
        {
            await AddDeleteItemsAsync(volume, [], entry, items, cancellationToken);
        }

        var stopwatch = Stopwatch.StartNew();
        var lastReport = -ReportInterval;
        var currentDirectory = new List<string>();
        var deletedEntries = 0;

        void Report(string currentName, bool force)
        {
            var elapsed = stopwatch.Elapsed;
            if (progress == null || (!force && elapsed - lastReport < ReportInterval))
            {
                return;
            }

            lastReport = elapsed;
            progress.Report(new DeleteProgress(currentName, deletedEntries, items.Count));
        }

        try
        {
            foreach (var item in items)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Report(item.Entry.Name, force: false);
                await VolumeCopy.ChangeDirectoryAsync(volume, currentDirectory, item.Directory, cancellationToken);
                await volume.Delete(item.Entry.Name, cancellationToken);
                deletedEntries++;
            }
        }
        finally
        {
            // change back also when cancelled or failed, so the volume stays at its starting directory
            await VolumeCopy.ChangeDirectoryAsync(volume, currentDirectory, [], CancellationToken.None);
        }

        Report(string.Empty, force: true);
    }

    /// <summary>
    /// Adds the entry to delete and, for containers, the entries within it. Entries within a container are added
    /// before the container, so it is empty when deleted.
    /// </summary>
    private static async Task AddDeleteItemsAsync(IVolume volume, IReadOnlyList<string> directory, IEntry entry,
        List<DeleteItem> items, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (entry.Name.Equals(Paths.Parent, StringComparison.Ordinal))
        {
            return;
        }

        if (entry.Type == EntryType.Container)
        {
            await volume.ChangeDirectory([entry.Name], cancellationToken);
            var childEntries = await volume.ListEntries(cancellationToken).ToListAsync(cancellationToken);
            var childDirectory = directory.Append(entry.Name).ToList();
            foreach (var childEntry in childEntries)
            {
                await AddDeleteItemsAsync(volume, childDirectory, childEntry, items, cancellationToken);
            }

            await volume.ChangeDirectory([Paths.Parent], cancellationToken);
        }

        items.Add(new DeleteItem(directory, entry));
    }

    /// <summary>An entry to delete and the directory containing it, relative to the directory the delete started in.</summary>
    private sealed record DeleteItem(IReadOnlyList<string> Directory, IEntry Entry);
}
