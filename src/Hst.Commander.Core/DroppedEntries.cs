using Hst.Commander.Abstractions;
using Hst.Commander.Core.Volumes;

namespace Hst.Commander.Core;

/// <summary>
///     Local files and directories dropped on a pane from another application, e.g. Explorer or Finder, copied or
///     moved to a location like the entries of a pane (see <see cref="NavigationContext.CopyAsync" /> and
///     <see cref="NavigationContext.MoveAsync" />): the paths are grouped by their directory and each directory is
///     opened as a navigation context of its own, so UAE metadata, confirming overwrites and moving by renaming work
///     as usual. Disposing closes the directories.
/// </summary>
public sealed class DroppedEntries : IDisposable
{
    private readonly IReadOnlyList<(NavigationContext Context, IReadOnlyList<IEntry> Entries)> _groups;

    private DroppedEntries(IReadOnlyList<(NavigationContext Context, IReadOnlyList<IEntry> Entries)> groups)
    {
        _groups = groups;
        Entries = groups.SelectMany(group => group.Entries).ToList();
    }

    /// <summary>The dropped entries, of all directories.</summary>
    public IReadOnlyList<IEntry> Entries { get; }

    /// <summary>Description shown in the copy dialog, e.g. "Copy 'a.txt'" or "Copy 3 entries".</summary>
    public string CopyDescription => NavigationContext.GetCopyDescription(Entries);

    /// <summary>Description shown in the move dialog, e.g. "Move 'a.txt'" or "Move 3 entries".</summary>
    public string MoveDescription => NavigationContext.GetMoveDescription(Entries);

    /// <summary>
    ///     Finds the entries of dropped local paths, handling UAE metadata like <paramref name="settings" /> (the pane
    ///     dropped on). Paths that don't exist and drives throw <see cref="IOException" />.
    /// </summary>
    public static async Task<DroppedEntries> OpenAsync(IReadOnlyCollection<string> paths,
        NavigationContext? settings = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(paths);

        var comparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        var groups = new List<(NavigationContext Context, IReadOnlyList<IEntry> Entries)>();
        try
        {
            foreach (var directoryPaths in paths
                         .Select(path => Path.TrimEndingDirectorySeparator(Path.GetFullPath(path)))
                         .Distinct(comparer)
                         .GroupBy(path => Path.GetDirectoryName(path) ??
                                          throw new IOException($"The drive '{path}' can't be copied"), comparer))
            {
                var context = new NavigationContext(directoryPaths.Key);
                groups.Add((context, []));
                if (settings != null)
                {
                    context.UaeMetadata = settings.UaeMetadata;
                    context.TemporaryCopyLimit = settings.TemporaryCopyLimit;
                }

                var listed = (await context.ListEntriesAsync(cancellationToken))
                    .Where(entry => !entry.Name.Equals(Paths.Parent, StringComparison.Ordinal))
                    .ToList();
                var entries = new List<IEntry>();
                foreach (var path in directoryPaths)
                {
                    var name = Path.GetFileName(path);
                    entries.Add(listed.FirstOrDefault(entry => entry.Name.Equals(name, StringComparison.Ordinal)) ??
                                listed.FirstOrDefault(entry => comparer.Equals(entry.Name, name)) ??
                                throw new IOException($"'{path}' was not found"));
                }

                groups[^1] = (context, entries);
            }
        }
        catch
        {
            foreach (var group in groups)
            {
                group.Context.Dispose();
            }

            throw;
        }

        if (groups.Count == 0)
        {
            throw new IOException("Nothing was dropped");
        }

        return new DroppedEntries(groups);
    }

    /// <summary>Copies the dropped entries to a destination, see <see cref="NavigationContext.CopyAsync" />.</summary>
    public async Task CopyAsync(NavigationContext destinationContext, string destinationPath, string filter,
        IProgress<CopyProgress>? progress = null,
        Func<OverwriteRequest, CancellationToken, Task<OverwriteAnswer>>? confirmOverwrite = null,
        CancellationToken cancellationToken = default)
    {
        foreach (var (context, entries) in _groups)
        {
            await context.CopyAsync(entries, destinationContext, destinationPath, filter, progress, confirmOverwrite,
                cancellationToken);
        }
    }

    /// <summary>Moves the dropped entries to a destination, see <see cref="NavigationContext.MoveAsync" />.</summary>
    public async Task MoveAsync(NavigationContext destinationContext, string destinationPath, string filter,
        IProgress<CopyProgress>? progress = null,
        Func<OverwriteRequest, CancellationToken, Task<OverwriteAnswer>>? confirmOverwrite = null,
        CancellationToken cancellationToken = default)
    {
        foreach (var (context, entries) in _groups)
        {
            await context.MoveAsync(entries, destinationContext, destinationPath, filter, progress, confirmOverwrite,
                cancellationToken);
        }
    }

    public void Dispose()
    {
        foreach (var group in _groups)
        {
            group.Context.Dispose();
        }
    }
}
