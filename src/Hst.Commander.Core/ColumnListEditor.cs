using Hst.Commander.Abstractions;

namespace Hst.Commander.Core;

/// <summary>
///     The columns of a pane as edited in the Columns tab of the settings of both front-ends: all columns
///     (<see cref="EntryColumns.All" />), the shown ones first in their order followed by the hidden ones, each shown or
///     hidden. Name can't be hidden, as the panes need it.
/// </summary>
public sealed class ColumnListEditor
{
    private readonly List<Item> _items = [];

    /// <param name="ids">Ids of the shown columns from the settings, null for <see cref="EntryColumns.Default" />.</param>
    public ColumnListEditor(IReadOnlyList<string>? ids)
    {
        Reset(ids);
    }

    /// <summary>A column and whether it's shown.</summary>
    public sealed record Item(IEntryColumn Column, bool IsShown)
    {
        /// <summary>True when the column can be hidden: all but Name.</summary>
        public bool CanHide => Column != EntryColumns.Name;
    }

    public IReadOnlyList<Item> Items => _items;

    /// <summary>Ids of the shown columns in order, as saved in the settings.</summary>
    public IReadOnlyList<string> ShownIds =>
        _items.Where(item => item.IsShown).Select(item => item.Column.Id).ToList();

    /// <summary>Shows the columns of ids, null for <see cref="EntryColumns.Default" />, e.g. Reset in the settings.</summary>
    public void Reset(IReadOnlyList<string>? ids)
    {
        var shown = EntryColumns.Resolve(ids);
        _items.Clear();
        _items.AddRange(shown.Select(column => new Item(column, true)));
        _items.AddRange(EntryColumns.All.Where(column => !shown.Contains(column))
            .Select(column => new Item(column, false)));
    }

    /// <summary>Shows a hidden column or hides a shown one, except Name. Returns true when it changed.</summary>
    public bool Toggle(int index)
    {
        if (index < 0 || index >= _items.Count || !_items[index].CanHide)
        {
            return false;
        }

        _items[index] = _items[index] with { IsShown = !_items[index].IsShown };
        return true;
    }

    /// <summary>Moves a column to another position, clamped to the list. Returns its new index.</summary>
    public int Move(int from, int to)
    {
        if (from < 0 || from >= _items.Count)
        {
            return from;
        }

        to = Math.Clamp(to, 0, _items.Count - 1);
        var item = _items[from];
        _items.RemoveAt(from);
        _items.Insert(to, item);
        return to;
    }
}
