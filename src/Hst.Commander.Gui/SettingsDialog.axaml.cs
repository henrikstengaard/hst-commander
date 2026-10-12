using Avalonia.Controls;

namespace Hst.Commander.Gui;

/// <summary>
///     Modal settings view. Theme changes are previewed immediately and reverted on cancel; the dialog returns the
///     changed settings on OK or null on cancel.
/// </summary>
public partial class SettingsDialog : Window
{
    private static readonly (GuiTheme Theme, string Name)[] Themes =
    [
        (GuiTheme.MidnightCommander, "Midnight Commander"),
        (GuiTheme.Light, "Light"),
        (GuiTheme.Dark, "Dark")
    ];

    public SettingsDialog() : this(new GuiSettings())
    {
    }

    public SettingsDialog(GuiSettings settings)
    {
        InitializeComponent();

        ThemeComboBox.ItemsSource = Themes.Select(theme => theme.Name).ToList();
        ThemeComboBox.SelectedIndex = Math.Max(0, Array.FindIndex(Themes, theme => theme.Theme == settings.Theme));
        ThemeComboBox.SelectionChanged += (_, _) => ThemeManager.Apply(SelectedTheme);

        var accepted = false;
        OkButton.Click += (_, _) =>
        {
            accepted = true;
            Close(settings with { Theme = SelectedTheme });
        };
        CancelButton.Click += (_, _) => Close();
        Closed += (_, _) =>
        {
            if (!accepted)
            {
                ThemeManager.Apply(settings.Theme);
            }
        };
        Opened += (_, _) => ThemeComboBox.Focus();
    }

    private GuiTheme SelectedTheme => Themes[Math.Max(0, ThemeComboBox.SelectedIndex)].Theme;
}
