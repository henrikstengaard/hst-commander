using Hst.Commander.Abstractions;
using Hst.Commander.Core;
using TuiAttribute = Terminal.Gui.Drawing.Attribute;

namespace Hst.Commander.Tui;

/// <summary>
///     Editor of the buttons of a theme in the settings (see <see cref="ICustomizableButtonPanel" />): the columns side
///     by side, each with its header and its buttons below it, and the cursor on a header or a button. The TUI has no
///     drag and drop, so buttons and columns are moved with keys: Ctrl+Up/Down moves the button within its column,
///     Ctrl+Left/Right moves it to the next column, or moves the column when the cursor is on its header. Insert adds a
///     button below the cursor (a column on a header), Delete removes it, and Enter or a double-click changes the
///     button. Changes are made with a <see cref="ButtonPanelEditor" />, shared with the GUI.
/// </summary>
public sealed class ButtonPanelEditorView : View
{
    private const int ColumnWidth = 12;

    private int _column;
    private int _row = -1;
    private int _scrollColumn;

    public ButtonPanelEditorView(ButtonPanelEditor editor)
    {
        Editor = editor ?? throw new ArgumentNullException(nameof(editor));
        CanFocus = true;
        Editor.Changed += (_, _) => Clamp();
    }

    /// <summary>The buttons as changed by the user.</summary>
    public ButtonPanelEditor Editor { get; }

    /// <summary>Raised when the button under the cursor is to be changed (Enter or double-click).</summary>
    public event EventHandler<(int Column, int Row)>? EditRequested;

    /// <summary>Column of the cursor.</summary>
    public int SelectedColumn => _column;

    /// <summary>Row of the button under the cursor, -1 when the cursor is on the column's header.</summary>
    public int SelectedRow => _row;

    /// <summary>Adds a column right of the cursor's column, or a button below the cursor.</summary>
    public void AddColumn()
    {
        _column = Editor.AddColumn(_column + 1);
        _row = -1;
    }

    /// <summary>Adds a button below the cursor and changes it.</summary>
    public void AddButton()
    {
        _row = Editor.AddButton(_column, _row + 1);
        EditRequested?.Invoke(this, (_column, _row));
    }

    /// <summary>Removes the button under the cursor, or its column when the cursor is on the header.</summary>
    public void RemoveSelected()
    {
        if (_row < 0)
        {
            Editor.RemoveColumn(_column);
        }
        else
        {
            Editor.RemoveButton(_column, _row);
            _row = Math.Min(_row, Editor.Columns[_column].Count - 1);
        }

        Clamp();
    }

    /// <summary>Changes the button under the cursor, if any.</summary>
    public void EditSelected()
    {
        if (_row >= 0)
        {
            EditRequested?.Invoke(this, (_column, _row));
        }
    }

    protected override bool OnKeyDown(Key key)
    {
        if (key == Key.CursorLeft)
        {
            Select(_column - 1, _row);
        }
        else if (key == Key.CursorRight)
        {
            Select(_column + 1, _row);
        }
        else if (key == Key.CursorUp)
        {
            Select(_column, _row - 1);
        }
        else if (key == Key.CursorDown)
        {
            Select(_column, _row + 1);
        }
        else if (key == Key.CursorUp.WithCtrl && _row > 0)
        {
            _row = Editor.MoveButton(_column, _row, _column, _row - 1);
        }
        else if (key == Key.CursorDown.WithCtrl && _row >= 0 && _row < Editor.Columns[_column].Count - 1)
        {
            _row = Editor.MoveButton(_column, _row, _column, _row + 1);
        }
        else if (key == Key.CursorLeft.WithCtrl || key == Key.CursorRight.WithCtrl)
        {
            var to = _column + (key == Key.CursorLeft.WithCtrl ? -1 : 1);
            if (to >= 0 && to < Editor.Columns.Count)
            {
                if (_row < 0)
                {
                    _column = Editor.MoveColumn(_column, to);
                }
                else
                {
                    _row = Editor.MoveButton(_column, _row, to, _row);
                    _column = to;
                }
            }
        }
        else if (key.KeyCode == KeyCode.Insert)
        {
            if (_row < 0)
            {
                AddColumn();
            }
            else
            {
                AddButton();
            }
        }
        else if (key.KeyCode == KeyCode.Delete)
        {
            RemoveSelected();
        }
        else if (key == Key.Enter)
        {
            EditSelected();
        }
        else
        {
            return base.OnKeyDown(key);
        }

        Clamp();
        return true;
    }

    protected override bool OnMouseEvent(Mouse mouseEvent)
    {
        if (!mouseEvent.IsSingleClicked && !mouseEvent.IsDoubleClicked)
        {
            return base.OnMouseEvent(mouseEvent);
        }

        SetFocus();
        var column = _scrollColumn + mouseEvent.Position!.Value.X / ColumnWidth;
        if (column < Editor.Columns.Count)
        {
            Select(column, mouseEvent.Position!.Value.Y - 1);
            if (mouseEvent.IsDoubleClicked)
            {
                EditSelected();
            }
        }

        return true;
    }

    private void Select(int column, int row)
    {
        _column = Math.Clamp(column, 0, Editor.Columns.Count - 1);
        _row = Math.Clamp(row, -1, Editor.Columns[_column].Count - 1);
        Clamp();
    }

    /// <summary>Keeps the cursor on a header or button and its column visible, and redraws.</summary>
    private void Clamp()
    {
        _column = Math.Clamp(_column, 0, Editor.Columns.Count - 1);
        _row = Math.Clamp(_row, -1, Editor.Columns[_column].Count - 1);
        var visibleColumns = Math.Max(1, Viewport.Width / ColumnWidth);
        if (_column < _scrollColumn)
        {
            _scrollColumn = _column;
        }
        else if (_column >= _scrollColumn + visibleColumns)
        {
            _scrollColumn = _column - visibleColumns + 1;
        }

        SetNeedsDraw();
    }

    protected override bool OnDrawingContent(DrawContext? context)
    {
        var normal = GetAttributeForRole(VisualRole.Normal);
        var cursor = HasFocus ? GetAttributeForRole(VisualRole.Focus) : new TuiAttribute(normal.Background, normal.Foreground);
        for (var y = 0; y < Viewport.Height; y++)
        {
            Move(0, y);
            SetAttribute(normal);
            AddStr(new string(' ', Viewport.Width));
        }

        for (var column = _scrollColumn; column < Editor.Columns.Count; column++)
        {
            var x = (column - _scrollColumn) * ColumnWidth;
            if (x + ColumnWidth > Viewport.Width && column > _scrollColumn)
            {
                break;
            }

            var isSelectedColumn = column == _column;
            DrawCell(x, 0, $"[Column {column + 1}]", isSelectedColumn && _row < 0 ? cursor : normal);
            var buttons = Editor.Columns[column];
            for (var row = 0; row < buttons.Count && row + 1 < Viewport.Height; row++)
            {
                var button = buttons[row];
                var attribute = isSelectedColumn && row == _row
                    ? cursor
                    : TuiColors.ButtonAttribute(button, normal);
                DrawCell(x, row + 1, button.Label, attribute);
            }
        }

        if (_scrollColumn > 0)
        {
            Move(0, Viewport.Height - 1);
            SetAttribute(normal);
            AddStr("<");
        }

        return true;
    }

    private void DrawCell(int x, int y, string text, TuiAttribute attribute)
    {
        var width = Math.Min(ColumnWidth - 1, Viewport.Width - x);
        if (width <= 0)
        {
            return;
        }

        Move(x, y);
        SetAttribute(attribute);
        AddStr(text.PadRight(width)[..width]);
    }
}
