using System.Collections.ObjectModel;
using Terminal.Gui;

namespace Hst.Commander.Tui;

/// <summary>
///     Drop-down list of choices in one line, showing the chosen one with "▼": Enter, Space, F4, Alt+Down or a click opens
///     the list below it in a popup (Enter or a click chooses, Esc cancels), and Up and Down change the choice in place.
///     Used instead of Terminal.Gui's ComboBox, which needs room below it for its list and moves itself first in its
///     dialog's tab order when its list is shown or hidden.
/// </summary>
public sealed class ChoiceField : View
{
    private readonly IReadOnlyList<string> _choices;
    private readonly ColorScheme? _popupColorScheme;
    private int _selectedIndex;

    /// <param name="choices">Choices shown.</param>
    /// <param name="selectedIndex">Index of the choice shown initially.</param>
    /// <param name="popupColorScheme">Color scheme of the popup with the list, null for the default.</param>
    public ChoiceField(IReadOnlyList<string> choices, int selectedIndex, ColorScheme? popupColorScheme = null)
    {
        _choices = choices ?? throw new ArgumentNullException(nameof(choices));
        _popupColorScheme = popupColorScheme;
        _selectedIndex = choices.Count == 0 ? -1 : Math.Clamp(selectedIndex, 0, choices.Count - 1);
        CanFocus = true;
        Height = 1;
    }

    /// <summary>Index of the chosen choice, -1 when there are no choices.</summary>
    public int SelectedIndex
    {
        get => _selectedIndex;
        set
        {
            _selectedIndex = _choices.Count == 0 ? -1 : Math.Clamp(value, 0, _choices.Count - 1);
            SetNeedsDraw();
        }
    }

    protected override bool OnKeyDown(Key key)
    {
        if (key == Key.CursorUp)
        {
            SelectedIndex = _selectedIndex - 1;
            return true;
        }

        if (key == Key.CursorDown)
        {
            SelectedIndex = _selectedIndex + 1;
            return true;
        }

        if (key == Key.Enter || key == Key.Space || key == Key.F4 || key == Key.CursorDown.WithAlt)
        {
            OpenList();
            return true;
        }

        return base.OnKeyDown(key);
    }

    protected override bool OnMouseClick(MouseEventArgs mouseEvent)
    {
        if (!Enabled)
        {
            return base.OnMouseClick(mouseEvent);
        }

        SetFocus();
        OpenList();
        return true;
    }

    protected override bool OnDrawingContent()
    {
        var attribute = !Enabled && ColorScheme != null
            ? ColorScheme.Disabled
            : HasFocus
                ? GetFocusColor()
                : ColorScheme?.Focus ?? GetNormalColor();
        SetAttribute(attribute);
        Move(0, 0);
        var width = Viewport.Width;
        if (width <= 0)
        {
            return true;
        }

        // the choice, cut to leave room for the arrow at the right
        var text = (_selectedIndex >= 0 ? _choices[_selectedIndex] : string.Empty).PadRight(width);
        AddStr(width > 2 ? text[..(width - 2)] + " ▼" : text[..width]);
        return true;
    }

    /// <summary>Shows the choices in a popup below the field and chooses the one picked.</summary>
    private void OpenList()
    {
        if (_choices.Count == 0)
        {
            return;
        }

        var screen = FrameToScreen();
        var listView = new ListView { X = 0, Y = 0, Width = Dim.Fill(), Height = Dim.Fill() };
        listView.SetSource(new ObservableCollection<string>(_choices));
        listView.SelectedItem = _selectedIndex;

        // the popup's border is drawn around the list, starting on the line below the field
        var popup = new Dialog
        {
            X = Math.Max(0, screen.X - 1),
            Y = screen.Y + 1,
            Width = screen.Width + 2,
            Height = _choices.Count + 2,
            ShadowStyle = ShadowStyle.None,
            BorderStyle = LineStyle.Single
        };
        popup.Padding!.Thickness = new Thickness(0);
        if (_popupColorScheme != null)
        {
            popup.ColorScheme = _popupColorScheme;
        }

        var chosen = -1;
        void Choose(int index)
        {
            chosen = index;
            Application.RequestStop(popup);
        }

        listView.OpenSelectedItem += (_, e) => Choose(e.Item);
        listView.KeyDown += (_, e) =>
        {
            if (e != Key.Enter && e != Key.Space)
            {
                return;
            }

            e.Handled = true;
            Choose(listView.SelectedItem);
        };
        listView.MouseClick += (_, e) =>
        {
            var index = listView.TopItem + e.Position.Y;
            if (index >= 0 && index < _choices.Count)
            {
                e.Handled = true;
                Choose(index);
            }
        };
        popup.Add(listView);
        popup.Initialized += (_, _) => listView.SetFocus();

        Application.Run(popup);
        popup.Dispose();
        if (chosen >= 0)
        {
            SelectedIndex = chosen;
        }
    }
}
