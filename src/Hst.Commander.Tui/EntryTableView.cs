using System.Text;

namespace Hst.Commander.Tui;

/// <summary>
///     TableView that continues the column dividers below the last entry, shows the scroll position on its right border
///     and raises <see cref="RightClicked" /> for the context menu of the entries and <see cref="HeaderClicked" /> for
///     sorting by a column.
/// </summary>
public sealed class EntryTableView : CursorTableView
{
    private static readonly Rune ScrollThumb = new('█');

    /// <summary>
    ///     Raised when the right mouse button is clicked: the row clicked (null below the entries or on the header) and
    ///     the position on the screen.
    /// </summary>
    public event EventHandler<(int? Row, System.Drawing.Point ScreenPosition)>? RightClicked;

    /// <summary>Raised when a column header is clicked with the left mouse button: the column clicked.</summary>
    public event EventHandler<int>? HeaderClicked;

    /// <summary>Raised when a mouse button is pressed or clicked in the table, e.g. to stop the quick search.</summary>
    public event EventHandler? MousePressed;

    protected override bool OnMouseEvent(Mouse mouseEvent)
    {
        if (mouseEvent.Flags.HasFlag(MouseFlags.LeftButtonPressed) || mouseEvent.Flags.HasFlag(MouseFlags.LeftButtonClicked) ||
            mouseEvent.Flags.HasFlag(MouseFlags.RightButtonPressed) || mouseEvent.Flags.HasFlag(MouseFlags.RightButtonClicked))
        {
            MousePressed?.Invoke(this, EventArgs.Empty);
        }

        if (mouseEvent.Flags.HasFlag(MouseFlags.LeftButtonClicked) && Table != null &&
            ScreenToCell(mouseEvent.Position ?? System.Drawing.Point.Empty, out int? headerColumn) == null &&
            headerColumn is { } column && column >= 0 && column < Table.Columns)
        {
            HeaderClicked?.Invoke(this, column);
            return true;
        }

        if (!mouseEvent.Flags.HasFlag(MouseFlags.RightButtonClicked))
        {
            return base.OnMouseEvent(mouseEvent);
        }

        var position = mouseEvent.Position ?? System.Drawing.Point.Empty;
        var cell = ScreenToCell(position.X, position.Y);
        RightClicked?.Invoke(this, (cell?.Y, mouseEvent.ScreenPosition));
        return true;
    }

    public IReadOnlyList<int> ColumnWidths { get; set; } = [];

    public int HeaderHeight =>
        (Style.ShowHeaders ? 1 : 0) +
        (Style.ShowHorizontalHeaderOverline ? 1 : 0) +
        (Style.ShowHorizontalHeaderUnderline ? 1 : 0);

    public int VisibleRows => Math.Max(1, Viewport.Height - HeaderHeight);

    /// <summary>Returns the x positions of the vertical lines: the left border, between columns and the right border.</summary>
    public IReadOnlyList<int> GetDividerPositions()
    {
        var positions = new List<int> { 0 };
        var x = 0;
        foreach (var width in ColumnWidths)
        {
            x += width + 1;
            positions.Add(Math.Min(x, Viewport.Width - 1));
        }

        return positions;
    }

    protected override bool OnDrawingContent(DrawContext? context)
    {
        var result = base.OnDrawingContent(context);
        if (Table == null || ColumnWidths.Count == 0 || Viewport.Width <= 0)
        {
            return result;
        }

        SetAttribute(GetAttributeForRole(VisualRole.Normal));
        DrawEmptyRowDividers();
        DrawScrollThumb();
        return result;
    }

    private void DrawEmptyRowDividers()
    {
        var dividers = GetDividerPositions();
        var firstEmptyRow = HeaderHeight + Math.Max(0, (Table?.Rows ?? 0) - RowOffset);
        for (var row = firstEmptyRow; row < Viewport.Height; row++)
        {
            foreach (var x in dividers)
            {
                AddRune(x, row, Glyphs.VLine);
            }
        }
    }

    private void DrawScrollThumb()
    {
        var trackHeight = VisibleRows;
        if (Table == null || Table.Rows <= trackHeight)
        {
            return;
        }

        var thumbHeight = Math.Max(1, trackHeight * trackHeight / Table.Rows);
        var maxOffset = Table.Rows - trackHeight;
        var thumbStart = (int)Math.Round((double)Math.Clamp(RowOffset, 0, maxOffset) / maxOffset * (trackHeight - thumbHeight));
        var x = Viewport.Width - 1;
        for (var row = 0; row < thumbHeight; row++)
        {
            AddRune(x, HeaderHeight + thumbStart + row, ScrollThumb);
        }
    }
}
