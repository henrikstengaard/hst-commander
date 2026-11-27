using Hst.Commander.Abstractions;

namespace Hst.Commander.Core;

/// <summary>
///     Order of the entries of a pane by a column (its id, see <see cref="EntryColumns" />), ascending or descending,
///     shared by the front-ends: clicking a column header (<see cref="Toggle" />) sorts by it ascending, clicking it
///     again descending, like the search results (<see cref="SearchResultSort" />). ".." stays first and directories
///     stay before files. Sorting by name keeps the order the volume lists the entries in (by name, with the volume's
///     own comparison, e.g. case-sensitive for ext file systems, or the order of e.g. disks and partitions), reversed
///     when descending; entries a column compares as equal (<see cref="IEntryColumn.Compare" />) are ordered by name.
///     A column that isn't known (e.g. of a plugin that isn't loaded) sorts by name.
/// </summary>
public sealed record EntrySort(string Column = EntryColumns.NameId, bool Descending = false)
{
    public static EntrySort Default { get; } = new();

    /// <summary>Sorting after clicking a column header: the same column reversed, another column ascending.</summary>
    public EntrySort Toggle(string column) =>
        column == Column ? this with { Descending = !Descending } : new EntrySort(column);

    /// <summary>Header of a column, with an arrow when the entries are sorted by it, e.g. "Size ▼".</summary>
    public string GetHeader(IEntryColumn column)
    {
        ArgumentNullException.ThrowIfNull(column);

        return column.Id == Column
            ? $"{column.Header} {(Descending ? SearchResultSort.DescendingArrow : SearchResultSort.AscendingArrow)}"
            : column.Header;
    }

    /// <summary>
    ///     The entries sorted in this order, the entries in the order the volume listed them, with the location given
    ///     to the column (e.g. for UAE metadata).
    /// </summary>
    public IReadOnlyList<IEntry> Sort(IReadOnlyList<IEntry> listedEntries, IEntryColumnContext? context = null)
    {
        ArgumentNullException.ThrowIfNull(listedEntries);

        var column = EntryColumns.Find(Column) ?? EntryColumns.Name;
        if (column == EntryColumns.Name && !Descending)
        {
            return listedEntries;
        }

        context ??= new EntryColumnContext();
        var indexes = new Dictionary<IEntry, int>(ReferenceEqualityComparer.Instance);
        for (var index = 0; index < listedEntries.Count; index++)
        {
            indexes.TryAdd(listedEntries[index], index);
        }

        return listedEntries
            .OrderBy(GetGroup)
            .ThenBy(entry => entry, Comparer<IEntry>.Create((x, y) =>
            {
                var result = Compare(column, x, y, context);
                if (result == 0)
                {
                    result = indexes[x].CompareTo(indexes[y]);
                }

                return Descending ? -result : result;
            }))
            .ToList();
    }

    // a plugin column failing to compare leaves the entries in the listed order
    private static int Compare(IEntryColumn column, IEntry x, IEntry y, IEntryColumnContext context)
    {
        try
        {
            return column.Compare(x, y, context);
        }
        catch (Exception)
        {
            return 0;
        }
    }

    // ".." first, then directories, then files
    private static int GetGroup(IEntry entry) =>
        entry.Name.Equals(Paths.Parent, StringComparison.Ordinal) ? 0 : entry.Type == EntryType.Container ? 1 : 2;
}
