using System.Text;
using Terminal.Gui;
using TuiAttribute = Terminal.Gui.Attribute;

namespace Hst.Commander.Tui;

/// <summary>
///     Bordered panel above a pane showing the current path on the first line and the summary of the marked entries
///     and the free/total disk space on the second line,
///     followed by the top border of the pane joining the column dividers of the table below.
/// </summary>
public sealed class PathPanelView : View
{
    private const string Ellipsis = "...";

    /// <summary>
    ///     Rows used by the panel (top border, path line, summary line and bottom border) and the pane's top border.
    /// </summary>
    public const int RequiredHeight = 5;

    public string Path { get; set; } = string.Empty;

    /// <summary>Summary of the marked entries, shown left-aligned on the second line. Hidden when null.</summary>
    public string? Summary { get; set; }

    /// <summary>Free/total disk space, shown right-aligned on the second line.</summary>
    public string? DiskSpace { get; set; }

    public IReadOnlyList<int> Dividers { get; set; } = [];

    public TuiAttribute BorderAttribute { get; set; }

    public TuiAttribute PathAttribute { get; set; }

    public TuiAttribute ActivePathAttribute { get; set; }

    public bool IsActive { get; set; }

    protected override bool OnDrawingContent()
    {
        var width = Viewport.Width;
        if (width <= 0)
        {
            return true;
        }

        DrawPanel(width);
        DrawPaneTopBorder(width, RequiredHeight - 1);
        return true;
    }

    private void DrawPanel(int width)
    {
        SetAttribute(BorderAttribute);
        for (var x = 0; x < width; x++)
        {
            var isLeft = x == 0;
            var isRight = x == width - 1;
            AddRune(x, 0, isLeft ? Glyphs.ULCorner : isRight ? Glyphs.URCorner : Glyphs.HLine);
            AddRune(x, 1, isLeft || isRight ? Glyphs.VLine : (Rune)' ');
            AddRune(x, 2, isLeft || isRight ? Glyphs.VLine : (Rune)' ');
            AddRune(x, 3, isLeft ? Glyphs.LLCorner : isRight ? Glyphs.LRCorner : Glyphs.HLine);
        }

        // content is placed inside the left and right border, padded with a space on each side
        var contentWidth = width - 4;
        if (contentWidth <= 0)
        {
            return;
        }

        DrawPath(contentWidth);
        DrawSummary(width, contentWidth);
    }

    private void DrawPath(int contentWidth)
    {
        // path is padded with a space on each side, so it starts on the border padding
        if (string.IsNullOrEmpty(Path))
        {
            return;
        }

        var text = Path.Length <= contentWidth
            ? Path
            : contentWidth <= Ellipsis.Length
                ? Path[^contentWidth..]
                : Ellipsis + TrimToSeparator(Path[^(contentWidth - Ellipsis.Length)..]);

        SetAttribute(IsActive ? ActivePathAttribute : PathAttribute);
        Move(1, 1);
        AddStr($" {text} ");
    }

    private void DrawSummary(int width, int contentWidth)
    {
        // disk space is right-aligned and only shown when it leaves room for at least part of the summary
        var diskSpace = DiskSpace ?? string.Empty;
        if (diskSpace.Length > 0 && diskSpace.Length + 1 + Ellipsis.Length > contentWidth)
        {
            diskSpace = string.Empty;
        }

        SetAttribute(PathAttribute);
        if (diskSpace.Length > 0)
        {
            Move(width - 2 - diskSpace.Length, 2);
            AddStr(diskSpace);
        }

        var maxSummaryLength = contentWidth - (diskSpace.Length > 0 ? diskSpace.Length + 1 : 0);
        if (maxSummaryLength <= 0 || string.IsNullOrEmpty(Summary))
        {
            return;
        }

        Move(2, 2);
        AddStr(Summary.Length <= maxSummaryLength ? Summary : Summary[..maxSummaryLength]);
    }

    private void DrawPaneTopBorder(int width, int row)
    {
        if (Viewport.Height <= row)
        {
            return;
        }

        SetAttribute(BorderAttribute);
        for (var x = 0; x < width; x++)
        {
            AddRune(x, row, GetBorderGlyph(x, width));
        }
    }

    /// <summary>Drops a partial leading segment so the tail starts at a separator, unless the last segment alone doesn't fit.</summary>
    private static string TrimToSeparator(string tail)
    {
        var separatorIndex = tail.IndexOfAny(['\\', '/']);
        return separatorIndex > 0 ? tail[separatorIndex..] : tail;
    }

    private Rune GetBorderGlyph(int x, int width)
    {
        var left = Dividers.Count > 0 ? Dividers[0] : 0;
        var right = Dividers.Count > 0 ? Dividers[^1] : width - 1;
        if (x == left)
        {
            return Glyphs.ULCorner;
        }

        if (x == right)
        {
            return Glyphs.URCorner;
        }

        return Dividers.Contains(x) ? Glyphs.TopTee : Glyphs.HLine;
    }
}
