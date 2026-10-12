using System.Text;
using Terminal.Gui;
using TuiAttribute = Terminal.Gui.Attribute;

namespace Hst.Commander.Tui;

/// <summary>
///     Top border of a pane with the current path embedded, joining the column dividers of the table below.
/// </summary>
public sealed class PathBorderView : View
{
    private const string Ellipsis = "...";

    public string Path { get; set; } = string.Empty;

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

        SetAttribute(BorderAttribute);
        for (var x = 0; x < width; x++)
        {
            AddRune(x, 0, GetBorderGlyph(x, width));
        }

        // path is placed after the left corner and a line segment, padded with a space on each side
        var maxPathLength = width - 4;
        if (maxPathLength <= 0 || string.IsNullOrEmpty(Path))
        {
            return true;
        }

        var text = Path.Length <= maxPathLength
            ? Path
            : maxPathLength <= Ellipsis.Length
                ? Path[^maxPathLength..]
                : Ellipsis + TrimToSeparator(Path[^(maxPathLength - Ellipsis.Length)..]);

        SetAttribute(IsActive ? ActivePathAttribute : PathAttribute);
        Move(1, 0);
        AddStr($" {text} ");
        return true;
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
