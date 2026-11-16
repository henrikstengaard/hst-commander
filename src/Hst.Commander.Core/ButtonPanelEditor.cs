using Hst.Commander.Abstractions;

namespace Hst.Commander.Core;

/// <summary>
///     Changes the buttons of a <see cref="ButtonPanel" /> in the button editor of the settings (see
///     <see cref="ICustomizableButtonPanel" />), shared by the TUI and GUI: adding, moving and removing columns and
///     buttons and changing buttons. There is always at least one column; empty columns are kept while editing and left
///     out by <see cref="ToPanel" />.
/// </summary>
public sealed class ButtonPanelEditor
{
    private readonly List<List<PanelButton>> _columns = [];

    public ButtonPanelEditor(ButtonPanel panel)
    {
        ArgumentNullException.ThrowIfNull(panel);
        Reset(panel);
    }

    /// <summary>Label of a button added with <see cref="AddButton" />.</summary>
    public const string NewButtonLabel = "Button";

    /// <summary>Raised after any change.</summary>
    public event EventHandler? Changed;

    /// <summary>The columns from left to right with their buttons from top to bottom.</summary>
    public IReadOnlyList<IReadOnlyList<PanelButton>> Columns => _columns;

    /// <summary>Replaces all columns with the columns of a panel, e.g. the default layout.</summary>
    public void Reset(ButtonPanel panel)
    {
        ArgumentNullException.ThrowIfNull(panel);
        _columns.Clear();
        _columns.AddRange(panel.Columns.Select(column => column.Buttons.ToList()));
        if (_columns.Count == 0)
        {
            _columns.Add([]);
        }

        OnChanged();
    }

    /// <summary>Adds an empty column at an index, clamped to the columns, and returns the index.</summary>
    public int AddColumn(int index)
    {
        index = Math.Clamp(index, 0, _columns.Count);
        _columns.Insert(index, []);
        OnChanged();
        return index;
    }

    /// <summary>Removes a column with its buttons. The last column isn't removed, only emptied.</summary>
    public void RemoveColumn(int column)
    {
        CheckColumn(column);
        if (_columns.Count == 1)
        {
            _columns[0].Clear();
        }
        else
        {
            _columns.RemoveAt(column);
        }

        OnChanged();
    }

    /// <summary>Moves a column to another index, clamped to the columns, and returns the new index.</summary>
    public int MoveColumn(int from, int to)
    {
        CheckColumn(from);
        to = Math.Clamp(to, 0, _columns.Count - 1);
        if (from != to)
        {
            var buttons = _columns[from];
            _columns.RemoveAt(from);
            _columns.Insert(to, buttons);
            OnChanged();
        }

        return to;
    }

    /// <summary>
    ///     Adds a button to a column at a row, clamped to the column's buttons, and returns the row. Without a button,
    ///     a button labeled <see cref="NewButtonLabel" /> viewing files is added.
    /// </summary>
    public int AddButton(int column, int row, PanelButton? button = null)
    {
        CheckColumn(column);
        var buttons = _columns[column];
        row = Math.Clamp(row, 0, buttons.Count);
        buttons.Insert(row, button ?? new PanelButton(NewButtonLabel, CommanderActions.View));
        OnChanged();
        return row;
    }

    /// <summary>Removes a button.</summary>
    public void RemoveButton(int column, int row)
    {
        CheckButton(column, row);
        _columns[column].RemoveAt(row);
        OnChanged();
    }

    /// <summary>
    ///     Moves a button to a row of a column, the same or another one, and returns the row it ends up at. The row is
    ///     clamped to the buttons of the column; within the same column it is the row after the move.
    /// </summary>
    public int MoveButton(int fromColumn, int fromRow, int toColumn, int toRow)
    {
        CheckButton(fromColumn, fromRow);
        CheckColumn(toColumn);
        var button = _columns[fromColumn][fromRow];
        _columns[fromColumn].RemoveAt(fromRow);
        var buttons = _columns[toColumn];
        toRow = Math.Clamp(toRow, 0, buttons.Count);
        buttons.Insert(toRow, button);
        OnChanged();
        return toRow;
    }

    /// <summary>Replaces a button, e.g. with another label or command.</summary>
    public void SetButton(int column, int row, PanelButton button)
    {
        ArgumentNullException.ThrowIfNull(button);
        CheckButton(column, row);
        _columns[column][row] = button;
        OnChanged();
    }

    /// <summary>The buttons as a panel, leaving out empty columns.</summary>
    public ButtonPanel ToPanel() => new(_columns
        .Where(buttons => buttons.Count > 0)
        .Select(buttons => new ButtonColumn(buttons.ToList()))
        .ToList());

    /// <summary>
    ///     Normalizes a color typed by the user to "#RRGGBB": null for an empty text, the color for "#RGB", "RRGGBB" or
    ///     "#RRGGBB" in any case, otherwise false.
    /// </summary>
    public static bool TryParseColor(string? text, out string? color)
    {
        color = null;
        var value = text?.Trim() ?? string.Empty;
        if (value.Length == 0)
        {
            return true;
        }

        if (value.StartsWith('#'))
        {
            value = value[1..];
        }

        if (value.Length == 3)
        {
            value = string.Concat(value.Select(c => new string(c, 2)));
        }

        if (value.Length != 6 || !value.All(Uri.IsHexDigit))
        {
            return false;
        }

        color = "#" + value.ToUpperInvariant();
        return true;
    }

    private void CheckColumn(int column) => ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(
        (uint)column, (uint)_columns.Count, nameof(column));

    private void CheckButton(int column, int row)
    {
        CheckColumn(column);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual((uint)row, (uint)_columns[column].Count, nameof(row));
    }

    private void OnChanged() => Changed?.Invoke(this, EventArgs.Empty);
}
