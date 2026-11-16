using Hst.Commander.Abstractions;
using Terminal.Gui;
using TuiAttribute = Terminal.Gui.Attribute;

namespace Hst.Commander.Tui;

/// <summary>
///     Bottom area of the window showing the buttons of the theme (<see cref="ButtonPanel" />), e.g. the function key
///     bar of the built-in themes (<c>F5Copy</c>, with no space so the ten buttons fit in 80 columns) or the button bank
///     of Directory Opus 4. Each column gets an equal share of the width and shows its buttons on one line each, so the
///     view is as high as the column with the most buttons. Clicking a button raises <see cref="ActionInvoked" />.
/// </summary>
public sealed class ButtonPanelView : View
{
    private ButtonPanel _panel = new([]);

    /// <summary>Buttons shown; set <see cref="View.Height" /> to <see cref="ButtonPanel.Rows" /> too.</summary>
    public ButtonPanel Panel
    {
        get => _panel;
        set
        {
            _panel = value ?? throw new ArgumentNullException(nameof(value));
            NeedsDraw = true;
        }
    }

    /// <summary>Keys before the labels, e.g. "F5".</summary>
    public TuiAttribute KeyAttribute { get; set; }

    /// <summary>Labels of buttons without their own colors.</summary>
    public TuiAttribute TitleAttribute { get; set; }

    /// <summary>
    ///     True to center the labels, e.g. in the button bank of a plugin theme; false to align them left after the key,
    ///     like the function key bar.
    /// </summary>
    public bool CenterLabels { get; set; }

    /// <summary>Raised with the command (<see cref="PanelButton.Action" />) of a clicked button.</summary>
    public event EventHandler<string>? ActionInvoked;

    protected override bool OnDrawingContent()
    {
        var width = Viewport.Width;
        var columns = _panel.Columns;
        if (width <= 0)
        {
            return true;
        }

        for (var row = 0; row < Viewport.Height; row++)
        {
            Move(0, row);
            SetAttribute(TitleAttribute);
            AddStr(new string(' ', width));
        }

        for (var column = 0; column < columns.Count; column++)
        {
            var (start, columnWidth) = GetColumnBounds(column, columns.Count, width);
            if (columnWidth <= 0)
            {
                continue;
            }

            var buttons = columns[column].Buttons;
            for (var row = 0; row < buttons.Count && row < Viewport.Height; row++)
            {
                DrawButton(buttons[row], start, row, columnWidth);
            }
        }

        return true;
    }

    private void DrawButton(PanelButton button, int start, int row, int width)
    {
        var key = button.Key ?? string.Empty;
        var label = button.Label;
        var text = key + label;
        if (CenterLabels && text.Length < width)
        {
            var left = (width - text.Length) / 2;
            text = new string(' ', left) + text;
            key = new string(' ', left) + key;
        }

        // a space between the buttons of a plugin theme, so buttons with their own colors are told apart
        var drawnWidth = CenterLabels && width > 1 ? width - 1 : width;
        text = text.PadRight(drawnWidth)[..drawnWidth];
        var keyLength = Math.Min(key.Length, drawnWidth);
        var title = button.Background != null || button.Foreground != null
            ? new TuiAttribute(
                button.Foreground != null ? TuiPalette.Closest(button.Foreground) : TitleAttribute.Foreground,
                button.Background != null ? TuiPalette.Closest(button.Background) : TitleAttribute.Background)
            : TitleAttribute;

        Move(start, row);
        SetAttribute(string.IsNullOrEmpty(button.Key) ? title : KeyAttribute);
        AddStr(text[..keyLength]);
        SetAttribute(title);
        AddStr(text[keyLength..]);
    }

    protected override bool OnMouseClick(MouseEventArgs mouseEvent)
    {
        var columns = _panel.Columns;
        for (var column = 0; column < columns.Count; column++)
        {
            var (start, columnWidth) = GetColumnBounds(column, columns.Count, Viewport.Width);
            if (mouseEvent.Position.X < start || mouseEvent.Position.X >= start + columnWidth)
            {
                continue;
            }

            var row = mouseEvent.Position.Y;
            if (row >= 0 && row < columns[column].Buttons.Count)
            {
                ActionInvoked?.Invoke(this, columns[column].Buttons[row].Action);
            }

            return true;
        }

        return false;
    }

    /// <summary>Gets the start and width of a column, spreading the remaining width over the first columns.</summary>
    public static (int Start, int Width) GetColumnBounds(int index, int count, int width)
    {
        var baseWidth = width / count;
        var remainder = width % count;
        var start = index * baseWidth + Math.Min(index, remainder);
        return (start, baseWidth + (index < remainder ? 1 : 0));
    }
}
