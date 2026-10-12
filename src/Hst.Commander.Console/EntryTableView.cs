using System.Text;
using Terminal.Gui;

namespace Hst.Commander.Console;

/// <summary>
///     TableView that continues the column dividers below the last entry and shows the scroll position on its right border.
/// </summary>
public sealed class EntryTableView : TableView
{
    private static readonly Rune ScrollThumb = new('█');

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

    protected override bool OnDrawingContent()
    {
        var result = base.OnDrawingContent();
        if (Table == null || ColumnWidths.Count == 0 || Viewport.Width <= 0)
        {
            return result;
        }

        SetAttribute(GetNormalColor());
        DrawEmptyRowDividers();
        DrawScrollThumb();
        return result;
    }

    private void DrawEmptyRowDividers()
    {
        var dividers = GetDividerPositions();
        var firstEmptyRow = HeaderHeight + Math.Max(0, Table.Rows - RowOffset);
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
        if (Table.Rows <= trackHeight)
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
