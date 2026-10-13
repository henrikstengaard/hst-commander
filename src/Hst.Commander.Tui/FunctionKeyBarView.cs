using Terminal.Gui;
using TuiAttribute = Terminal.Gui.Attribute;

namespace Hst.Commander.Tui;

/// <summary>
///     Bar at the bottom of the window with the function key items (e.g. <c>F5 Copy</c>), each getting an equal share of
///     the width. Clicking an item runs its action; items without an action do nothing.
/// </summary>
public sealed class FunctionKeyBarView : View
{
    public IReadOnlyList<FunctionKeyItem> Items { get; set; } = [];

    public TuiAttribute KeyAttribute { get; set; }

    public TuiAttribute TitleAttribute { get; set; }

    protected override bool OnDrawingContent()
    {
        var width = Viewport.Width;
        if (width <= 0 || Items.Count == 0)
        {
            return true;
        }

        for (var index = 0; index < Items.Count; index++)
        {
            var (start, itemWidth) = GetItemBounds(index, width);
            if (itemWidth <= 0)
            {
                continue;
            }

            var key = Items[index].Key;
            var text = (key + " " + Items[index].Title).PadRight(itemWidth)[..itemWidth];
            var keyLength = Math.Min(key.Length, itemWidth);

            Move(start, 0);
            SetAttribute(KeyAttribute);
            AddStr(text[..keyLength]);
            SetAttribute(TitleAttribute);
            AddStr(text[keyLength..]);
        }

        return true;
    }

    protected override bool OnMouseClick(MouseEventArgs mouseEvent)
    {
        var width = Viewport.Width;
        for (var index = 0; index < Items.Count; index++)
        {
            var (start, itemWidth) = GetItemBounds(index, width);
            if (mouseEvent.Position.X < start || mouseEvent.Position.X >= start + itemWidth)
            {
                continue;
            }

            Items[index].Action?.Invoke();
            return true;
        }

        return false;
    }

    /// <summary>Gets the start column and width of an item, spreading the remaining columns over the first items.</summary>
    private (int Start, int Width) GetItemBounds(int index, int width)
    {
        var baseWidth = width / Items.Count;
        var remainder = width % Items.Count;
        var start = index * baseWidth + Math.Min(index, remainder);
        return (start, baseWidth + (index < remainder ? 1 : 0));
    }
}

/// <summary>Item in the <see cref="FunctionKeyBarView" />, e.g. key <c>F5</c> with title <c>Copy</c>.</summary>
public sealed record FunctionKeyItem(string Key, string Title, Action? Action = null);
