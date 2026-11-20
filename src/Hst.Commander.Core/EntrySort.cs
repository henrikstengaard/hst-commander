using Hst.Commander.Abstractions;

namespace Hst.Commander.Core;

/// <summary>Column of the panes, see <see cref="EntrySort" />.</summary>
public enum EntryColumn
{
    Name,
    Size,
    Modified
}

/// <summary>
///     Order of the entries of a pane by a column, ascending or descending, shared by the front-ends: clicking a column
///     header (<see cref="Toggle" />) sorts by it ascending, clicking it again descending, like the search results
///     (<see cref="SearchResultSort" />). ".." stays first and directories stay before files. Sorting by name keeps
///     the order the volume lists the entries in (by name, with the volume's own comparison, e.g. case-sensitive for
///     ext file systems, or the order of e.g. disks and partitions), reversed when descending; equal sizes and dates
///     are ordered by name.
/// </summary>
public sealed record EntrySort(EntryColumn Column = EntryColumn.Name, bool Descending = false)
{
    public static EntrySort Default { get; } = new();

    /// <summary>The columns in the order they're shown.</summary>
    public static IReadOnlyList<EntryColumn> Columns { get; } =
        [EntryColumn.Name, EntryColumn.Size, EntryColumn.Modified];

    /// <summary>Sorting after clicking a column header: the same column reversed, another column ascending.</summary>
    public EntrySort Toggle(EntryColumn column) =>
        column == Column ? this with { Descending = !Descending } : new EntrySort(column);

    /// <summary>Header of a column, with an arrow when the entries are sorted by it, e.g. "Size ▼".</summary>
    public string GetHeader(EntryColumn column)
    {
        var name = column switch
        {
            EntryColumn.Size => "Size",
            EntryColumn.Modified => "Modified",
            _ => "Name"
        };
        return column == Column
            ? $"{name} {(Descending ? SearchResultSort.DescendingArrow : SearchResultSort.AscendingArrow)}"
            : name;
    }

    /// <summary>The entries sorted in this order, the entries in the order the volume listed them.</summary>
    public IReadOnlyList<IEntry> Sort(IReadOnlyList<IEntry> listedEntries)
    {
        ArgumentNullException.ThrowIfNull(listedEntries);

        if (this == Default)
        {
            return listedEntries;
        }

        var indexes = new Dictionary<IEntry, int>(ReferenceEqualityComparer.Instance);
        for (var index = 0; index < listedEntries.Count; index++)
        {
            indexes.TryAdd(listedEntries[index], index);
        }

        return listedEntries
            .OrderBy(GetGroup)
            .ThenBy(entry => entry, Comparer<IEntry>.Create((x, y) =>
            {
                var result = Column switch
                {
                    EntryColumn.Size => Nullable.Compare(x.Size, y.Size),
                    EntryColumn.Modified => Nullable.Compare(x.ModifiedDate, y.ModifiedDate),
                    _ => 0
                };
                if (result == 0)
                {
                    result = indexes[x].CompareTo(indexes[y]);
                }

                return Descending ? -result : result;
            }))
            .ToList();
    }

    // ".." first, then directories, then files
    private static int GetGroup(IEntry entry) =>
        entry.Name.Equals(Paths.Parent, StringComparison.Ordinal) ? 0 : entry.Type == EntryType.Container ? 1 : 2;
}
