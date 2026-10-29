using Hst.Commander.Core;
using Terminal.Gui;
using TuiAttribute = Terminal.Gui.Attribute;

namespace Hst.Commander.Tui;

/// <summary>
///     Draws the lines (text view) or rows (hex view) of a <see cref="ViewerSession" /> shown in the viewer, reading
///     only the lines that fit. The text found by a search and the selection are highlighted, hex offsets are drawn in
///     the header color. Dragging with the left mouse button selects (scrolling when dragged outside the view),
///     Shift+click extends the selection and a click clears it; the mouse wheel scrolls. Images aren't shown (the TUI
///     opens the viewer without image providers), they are viewed as binary files.
/// </summary>
public sealed class ViewerView : View
{
    private readonly ViewerSession _session;
    private bool _selecting;

    public ViewerView(ViewerSession session)
    {
        _session = session;
        WantMousePositionReports = true;
    }

    public TuiAttribute TextAttribute { get; set; }

    public TuiAttribute OffsetAttribute { get; set; }

    public TuiAttribute MatchAttribute { get; set; }

    public TuiAttribute SelectionAttribute { get; set; }

    /// <summary>Rows shown, at least 1.</summary>
    public int Rows => Math.Max(1, Viewport.Height);

    /// <summary>Columns shown, at least 1.</summary>
    public int Columns => Math.Max(1, Viewport.Width);

    /// <summary>Raised when the mouse scrolled the view or changed the selection.</summary>
    public event EventHandler? ViewChanged;

    protected override bool OnMouseEvent(MouseEventArgs mouseEvent)
    {
        var flags = mouseEvent.Flags;
        if (flags.HasFlag(MouseFlags.WheeledDown) || flags.HasFlag(MouseFlags.WheeledUp))
        {
            _session.Scroll(flags.HasFlag(MouseFlags.WheeledDown) ? 3 : -3, Rows);
            Changed();
            return true;
        }

        if (flags.HasFlag(MouseFlags.Button1Pressed))
        {
            SetFocus();
            var (row, column) = GetCell(mouseEvent.Position);
            if (_selecting)
            {
                _session.ExtendSelection(row, column, Rows);
            }
            else
            {
                // the mouse is grabbed while selecting, so dragging outside the view scrolls
                _selecting = true;
                Application.GrabMouse(this);
                if (flags.HasFlag(MouseFlags.ButtonShift) && _session.Selection != null)
                {
                    _session.ExtendSelection(row, column, Rows);
                }
                else
                {
                    _session.StartSelection(row, column, Rows);
                }
            }

            Changed();
            return true;
        }

        if (flags.HasFlag(MouseFlags.Button1Released) && _selecting)
        {
            _selecting = false;
            if (Application.MouseGrabView == this)
            {
                Application.UngrabMouse();
            }

            return true;
        }

        return base.OnMouseEvent(mouseEvent);
    }

    /// <summary>Cell of the view at a mouse position, scrolling a line or column when it is outside the view.</summary>
    private (int Row, int Column) GetCell(System.Drawing.Point position)
    {
        var row = position.Y;
        var column = position.X;
        if (row < 0 || row >= Rows)
        {
            _session.Scroll(row < 0 ? -1 : 1, Rows);
            row = Math.Clamp(row, 0, Rows - 1);
        }

        if (!_session.IsHex && (column < 0 || column >= Columns))
        {
            _session.ScrollColumns(column < 0 ? -1 : 1, Rows, Columns);
            column = Math.Clamp(column, 0, Columns - 1);
        }

        return (row, Math.Max(0, column));
    }

    private void Changed()
    {
        NeedsDraw = true;
        ViewChanged?.Invoke(this, EventArgs.Empty);
    }

    protected override bool OnDrawingContent()
    {
        var width = Viewport.Width;
        var height = Viewport.Height;
        if (width <= 0 || height <= 0)
        {
            return true;
        }

        // lines are wrapped at the width shown
        _session.SetColumns(width);
        if (_session.IsHex)
        {
            DrawHexRows(width, height);
        }
        else
        {
            DrawLines(width, height);
        }

        return true;
    }

    private void DrawLines(int width, int height)
    {
        var lines = _session.GetLines(height);
        for (var row = 0; row < height; row++)
        {
            var line = row < lines.Count ? lines[row] : null;
            var text = line == null || line.Text.Length <= _session.LeftColumn
                ? string.Empty
                : line.Text[_session.LeftColumn..];
            text = Fit(text, width);

            Move(0, row);
            SetAttribute(TextAttribute);
            AddStr(text);
            if (line == null)
            {
                continue;
            }

            DrawColumns(text, _session.GetMatchColumns(line), MatchAttribute, row, width);
            DrawColumns(text, _session.GetSelectionColumns(line), SelectionAttribute, row, width);
        }
    }

    /// <summary>Draws columns of a line (in the text, not the view) highlighted, e.g. the match or the selection.</summary>
    private void DrawColumns(string text, (int Column, int Length)? columns, TuiAttribute attribute, int row,
        int width)
    {
        if (columns is not var (column, length))
        {
            return;
        }

        SetAttribute(attribute);
        DrawPart(text, Math.Max(0, column - _session.LeftColumn), column - _session.LeftColumn + length, row, width);
    }

    private void DrawHexRows(int width, int height)
    {
        var digits = _session.Document.HexOffsetDigits;
        var rows = _session.GetHexRows(height);
        for (var row = 0; row < height; row++)
        {
            var hexRow = row < rows.Count ? rows[row] : null;
            var text = Fit(hexRow?.Text ?? string.Empty, width);

            Move(0, row);
            SetAttribute(TextAttribute);
            AddStr(text);
            if (hexRow == null)
            {
                continue;
            }

            Move(0, row);
            SetAttribute(OffsetAttribute);
            AddStr(text[..Math.Min(digits, width)]);
            for (var index = 0; index < hexRow.Bytes.Length; index++)
            {
                var offset = hexRow.Offset + index;
                if (!_session.IsMatchByte(offset) && !_session.IsSelected(offset))
                {
                    continue;
                }

                SetAttribute(_session.IsSelected(offset) ? SelectionAttribute : MatchAttribute);
                var hexColumn = ViewerDocument.GetHexColumn(digits, index);
                var textColumn = ViewerDocument.GetHexTextColumn(digits, index);
                DrawPart(text, hexColumn, hexColumn + 2, row, width);
                DrawPart(text, textColumn, textColumn + 1, row, width);
            }
        }
    }

    /// <summary>Draws the columns from start to before end of text drawn in a row, within the width.</summary>
    private void DrawPart(string text, int start, int end, int row, int width)
    {
        var column = start;
        end = Math.Min(width, end);
        if (column >= end)
        {
            return;
        }

        Move(column, row);
        AddStr(text[column..end]);
    }

    /// <summary>Cuts or pads text to a width, so each row clears what was drawn before.</summary>
    private static string Fit(string text, int width) => text.Length >= width ? text[..width] : text.PadRight(width);
}
