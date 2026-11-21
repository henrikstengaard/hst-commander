namespace Hst.Commander.Tui;

/// <summary>
///     TableView with the selected row and column as properties and <see cref="CellActivated" /> raised by Enter and a
///     double-click, for tables where the cursor picks one row (the panes and the search results). Terminal.Gui v2 keeps
///     the cursor and the selection together in <see cref="TableView.Value" />.
/// </summary>
public class CursorTableView : TableView
{
    /// <summary>Raised when the cell under the cursor is activated with Enter or a double-click.</summary>
    public event EventHandler? CellActivated;

    /// <summary>Raised when the cursor or the selection changes.</summary>
    public event EventHandler? SelectedCellChanged;

    public CursorTableView()
    {
        ValueChanged += (_, _) => SelectedCellChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Row of the cursor, 0 when there is none.</summary>
    public int SelectedRow
    {
        get => Value?.SelectedCell.Y ?? 0;
        set => SetSelection(SelectedColumn, value, false);
    }

    /// <summary>Column of the cursor, 0 when there is none.</summary>
    public int SelectedColumn
    {
        get => Value?.SelectedCell.X ?? 0;
        set => SetSelection(value, SelectedRow, false);
    }

    /// <summary>Scrolls the table so the cursor is visible.</summary>
    public void EnsureSelectedCellIsVisible() => EnsureCursorIsVisible();

    /// <summary>
    ///     Raises <see cref="CellActivated" /> instead of accepting, so Enter in a table in a dialog doesn't press the
    ///     dialog's default button.
    /// </summary>
    protected override bool OnAccepting(CommandEventArgs args)
    {
        if (Table == null || Table.Rows == 0)
        {
            return base.OnAccepting(args);
        }

        CellActivated?.Invoke(this, EventArgs.Empty);
        return true;
    }
}
