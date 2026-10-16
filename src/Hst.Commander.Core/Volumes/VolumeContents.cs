using System.Diagnostics;
using Hst.Commander.Extensions.Volume.Abstractions;

namespace Hst.Commander.Core.Volumes;

/// <summary>
/// Counts the size and number of entries of entries in a volume, e.g. for the properties dialog, using only
/// <see cref="IVolume.ListEntries" /> and <see cref="IVolume.ChangeDirectory" />.
/// </summary>
public static class VolumeContents
{
    private static readonly TimeSpan ReportInterval = TimeSpan.FromMilliseconds(100);

    /// <summary>
    /// Counts entries in the current directory of the volume, containers recursively. The entries within containers
    /// are counted and, when more than one entry is given, the entries themselves, like Windows Explorer counts what a
    /// folder or a selection contains. The volume is changed back to its current directory when done.
    /// </summary>
    /// <param name="volume">Volume to count entries in.</param>
    /// <param name="entries">Entries in the current directory of the volume.</param>
    /// <param name="progress">Receives the contents counted so far, at most every 100 ms and when done.</param>
    /// <param name="cancellationToken"></param>
    public static async Task<EntryContents> CountContentsAsync(this IVolume volume, IReadOnlyCollection<IEntry> entries,
        IProgress<EntryContents>? progress = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(volume);
        ArgumentNullException.ThrowIfNull(entries);

        var counter = new ContentsCounter(progress, sizeOnDisk: false);
        var countEntries = entries.Count > 1;
        foreach (var entry in entries.Where(entry => !entry.Name.Equals(Paths.Parent, StringComparison.Ordinal)))
        {
            if (entry.Type == EntryType.Container)
            {
                if (countEntries)
                {
                    counter.AddFolder();
                }

                await CountContainerAsync(volume, entry.Name, counter, cancellationToken);
            }
            else
            {
                counter.AddFile(entry.Size ?? 0, null, countEntries);
            }
        }

        return counter.Complete();
    }

    private static async Task CountContainerAsync(IVolume volume, string name, ContentsCounter counter,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await volume.ChangeDirectory([name], cancellationToken);
        try
        {
            var childEntries = await volume.ListEntries(cancellationToken).ToListAsync(cancellationToken);
            foreach (var childEntry in childEntries)
            {
                if (childEntry.Type == EntryType.Container)
                {
                    counter.AddFolder();
                    await CountContainerAsync(volume, childEntry.Name, counter, cancellationToken);
                }
                else
                {
                    counter.AddFile(childEntry.Size ?? 0, null);
                }
            }
        }
        finally
        {
            // change back also when cancelled or failed, so the volume stays at its starting directory
            await volume.ChangeDirectory([Paths.Parent], CancellationToken.None);
        }
    }

    /// <summary>Adds up the contents and reports them at most every 100 ms.</summary>
    internal sealed class ContentsCounter(IProgress<EntryContents>? progress, bool sizeOnDisk)
    {
        private readonly Stopwatch _stopwatch = Stopwatch.StartNew();
        private TimeSpan _lastReport = TimeSpan.Zero;
        private long _size;
        private long? _sizeOnDisk = sizeOnDisk ? 0 : null;
        private int _files;
        private int _folders;

        /// <summary>Adds the size of an item, counting it as a file unless <paramref name="count" /> is false.</summary>
        public void AddFile(long size, long? sizeOnDisk, bool count = true)
        {
            _size += size;
            _sizeOnDisk = _sizeOnDisk == null || sizeOnDisk == null ? null : _sizeOnDisk + sizeOnDisk;
            if (count)
            {
                _files++;
            }

            Report();
        }

        public void AddFolder()
        {
            _folders++;
            Report();
        }

        public EntryContents Complete()
        {
            var contents = Create(true);
            progress?.Report(contents);
            return contents;
        }

        private void Report()
        {
            var elapsed = _stopwatch.Elapsed;
            if (progress == null || elapsed - _lastReport < ReportInterval)
            {
                return;
            }

            _lastReport = elapsed;
            progress.Report(Create(false));
        }

        private EntryContents Create(bool isComplete) => new(_size, _sizeOnDisk, _files, _folders, isComplete);
    }
}
