using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Hst.Commander.Abstractions;

namespace Hst.Commander.Gui;

/// <summary>
///     Bottom area of the window showing the buttons of the theme (<see cref="ButtonPanel" />), e.g. the function key
///     bar of the built-in themes or the button bank of Directory Opus 4: one column of equal width for each column of
///     the panel, with its buttons from top to bottom. The buttons aren't focusable, so the active pane keeps the focus.
///     Clicking a button raises <see cref="ActionInvoked" /> with its command.
/// </summary>
public sealed class ButtonPanelView : Border
{
    private readonly Grid _grid = new();
    private ButtonPanel _panel = new([]);

    public ButtonPanelView()
    {
        Child = _grid;
    }

    /// <summary>Raised with the command (<see cref="PanelButton.Action" />) of a clicked button.</summary>
    public event EventHandler<string>? ActionInvoked;

    /// <summary>
    ///     True to center the labels, e.g. in the button bank of a plugin theme; false to align them left with the key
    ///     first, like the function key bar.
    /// </summary>
    public bool CenterLabels { get; set; }

    /// <summary>Buttons shown.</summary>
    public ButtonPanel Panel
    {
        get => _panel;
        set
        {
            _panel = value ?? throw new ArgumentNullException(nameof(value));
            Build();
        }
    }

    private void Build()
    {
        _grid.Children.Clear();
        _grid.ColumnDefinitions.Clear();
        _grid.RowDefinitions.Clear();
        var rows = _panel.Rows;
        for (var row = 0; row < rows; row++)
        {
            _grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        }

        for (var column = 0; column < _panel.Columns.Count; column++)
        {
            _grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
            var buttons = _panel.Columns[column].Buttons;
            for (var row = 0; row < buttons.Count; row++)
            {
                var button = CreateButton(buttons[row], CenterLabels);
                var action = buttons[row].Action;
                button.Click += (_, _) => ActionInvoked?.Invoke(this, action);
                Grid.SetColumn(button, column);
                Grid.SetRow(button, row);
                _grid.Children.Add(button);
            }
        }
    }

    /// <summary>A button of a panel with its key in bold before its label and its own colors, if any.</summary>
    public static Button CreateButton(PanelButton panelButton, bool centerLabel)
    {
        var content = new StackPanel { Orientation = Orientation.Horizontal };
        if (!string.IsNullOrEmpty(panelButton.Key))
        {
            content.Children.Add(new TextBlock
            {
                Text = panelButton.Key, FontWeight = FontWeight.Bold, Padding = new Thickness(6, 3, 0, 3)
            });
        }

        content.Children.Add(new TextBlock
        {
            Text = panelButton.Label, Padding = new Thickness(6, 3), TextTrimming = TextTrimming.CharacterEllipsis
        });
        var button = new Button
        {
            Content = content,
            Focusable = false,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            HorizontalContentAlignment = centerLabel ? HorizontalAlignment.Center : HorizontalAlignment.Left,
            Margin = new Thickness(1),
            Padding = new Thickness(0)
        };
        button.Classes.Add("panelButton");
        ToolTip.SetTip(button, CommanderActions.GetDescription(panelButton.Action));
        if (panelButton.Background != null)
        {
            button.Background = ThemeManager.Brush(panelButton.Background);
        }

        if (panelButton.Foreground != null)
        {
            button.Foreground = ThemeManager.Brush(panelButton.Foreground);
        }

        return button;
    }
}
