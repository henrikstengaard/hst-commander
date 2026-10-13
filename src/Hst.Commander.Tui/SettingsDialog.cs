using Terminal.Gui;

namespace Hst.Commander.Tui;

/// <summary>
///     Modal settings view. Theme changes are previewed immediately through the preview action; <see cref="Result" />
///     holds the changed settings on OK or null on cancel, in which case the caller reverts the preview.
/// </summary>
public sealed class SettingsDialog : Dialog
{
    private static readonly (TuiTheme Theme, string Name)[] Themes =
    [
        (TuiTheme.MidnightCommander, "Midnight Commander"),
        (TuiTheme.Light, "Light"),
        (TuiTheme.Dark, "Dark")
    ];

    private readonly RadioGroup _themeRadioGroup;
    private readonly TuiSettings _settings;
    private readonly Action<TuiTheme> _preview;

    public SettingsDialog(TuiSettings settings, Action<TuiTheme> preview, TuiPalette palette)
    {
        _settings = settings;
        _preview = preview;
        Title = "Settings";
        Width = 40;
        Height = 10;
        ColorScheme = palette.DialogColorScheme;

        var themeLabel = new Label { X = 1, Y = 1, Text = "Theme:" };
        _themeRadioGroup = new RadioGroup
        {
            X = Pos.Right(themeLabel) + 1,
            Y = 1,
            RadioLabels = Themes.Select(theme => theme.Name).ToArray(),
            SelectedItem = Math.Max(0, Array.FindIndex(Themes, theme => theme.Theme == settings.Theme))
        };
        _themeRadioGroup.SelectedItemChanged += (_, _) => Preview(SelectedTheme);

        var okButton = new Button { Text = "OK", IsDefault = true };
        okButton.Accepting += (_, e) =>
        {
            Result = _settings with { Theme = SelectedTheme };
            e.Cancel = true;
            Application.RequestStop(this);
        };
        var cancelButton = new Button { Text = "Cancel" };
        cancelButton.Accepting += (_, e) =>
        {
            e.Cancel = true;
            Application.RequestStop(this);
        };

        Add(themeLabel, _themeRadioGroup);
        AddButton(okButton);
        AddButton(cancelButton);
        Initialized += (_, _) => _themeRadioGroup.SetFocus();
    }

    /// <summary>Settings with the chosen theme when OK was pressed, otherwise null.</summary>
    public TuiSettings? Result { get; private set; }

    private TuiTheme SelectedTheme => Themes[Math.Max(0, _themeRadioGroup.SelectedItem)].Theme;

    private void Preview(TuiTheme theme)
    {
        _preview(theme);
        ColorScheme = TuiPalette.For(theme).DialogColorScheme;
        NeedsDraw = true;
    }
}
