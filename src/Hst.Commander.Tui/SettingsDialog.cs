using Hst.Commander.Core;
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
    private readonly RadioGroup _volumeProviderKeysRadioGroup;
    private readonly RadioGroup _enterFileActionRadioGroup;
    private readonly RadioGroup _uaeMetadataRadioGroup;
    private readonly TuiSettings _settings;
    private readonly Action<TuiTheme> _preview;

    public SettingsDialog(TuiSettings settings, Action<TuiTheme> preview, TuiPalette palette)
    {
        _settings = settings;
        _preview = preview;
        Title = "Settings";
        Width = 50;
        Height = 20;
        ColorScheme = palette.DialogColorScheme;

        var themeLabel = new Label { X = 1, Y = 1, Text = "Theme:" };
        _themeRadioGroup = new RadioGroup
        {
            X = 15,
            Y = 1,
            RadioLabels = Themes.Select(theme => theme.Name).ToArray(),
            SelectedItem = Math.Max(0, Array.FindIndex(Themes, theme => theme.Theme == settings.Theme))
        };
        _themeRadioGroup.SelectedItemChanged += (_, _) => Preview(SelectedTheme);

        var volumeProviderKeysLabel = new Label { X = 1, Y = 5, Text = "Volume keys:" };
        _volumeProviderKeysRadioGroup = new RadioGroup
        {
            X = 15,
            Y = 5,
            RadioLabels = VolumeProviderKeyBindings.All.Select(keys => keys == VolumeProviderKeyBindings.Default
                ? $"{VolumeProviderKeyBindings.GetName(keys)} (default)"
                : VolumeProviderKeyBindings.GetName(keys)).ToArray(),
            SelectedItem = Math.Max(0, VolumeProviderKeyBindings.All.ToList().IndexOf(settings.EffectiveVolumeProviderKeys))
        };

        var enterFileActionLabel = new Label { X = 1, Y = 8, Text = "Enter on file:" };
        _enterFileActionRadioGroup = new RadioGroup
        {
            X = 15,
            Y = 8,
            RadioLabels = EnterFileActions.All.Select(EnterFileActions.GetName).ToArray(),
            SelectedItem = Math.Max(0, EnterFileActions.All.ToList().IndexOf(settings.EnterFileAction))
        };

        var uaeMetadataLabel = new Label { X = 1, Y = 11, Text = "UAE metadata:" };
        _uaeMetadataRadioGroup = new RadioGroup
        {
            X = 15,
            Y = 11,
            RadioLabels = UaeMetadataFormats.All.Select(UaeMetadataFormats.GetName).ToArray(),
            SelectedItem = Math.Max(0, UaeMetadataFormats.All.ToList().IndexOf(settings.UaeMetadata))
        };

        var okButton = new Button { Text = "OK", IsDefault = true };
        okButton.Accepting += (_, e) =>
        {
            Result = (_settings with
                {
                    Theme = SelectedTheme,
                    EnterFileAction = SelectedEnterFileAction,
                    UaeMetadata = SelectedUaeMetadata
                })
                .WithVolumeProviderKeys(SelectedVolumeProviderKeys);
            e.Cancel = true;
            Application.RequestStop(this);
        };
        var cancelButton = new Button { Text = "Cancel" };
        cancelButton.Accepting += (_, e) =>
        {
            e.Cancel = true;
            Application.RequestStop(this);
        };

        Add(themeLabel, _themeRadioGroup, volumeProviderKeysLabel, _volumeProviderKeysRadioGroup,
            enterFileActionLabel, _enterFileActionRadioGroup, uaeMetadataLabel, _uaeMetadataRadioGroup);
        AddButton(okButton);
        AddButton(cancelButton);
        Initialized += (_, _) => _themeRadioGroup.SetFocus();
    }

    /// <summary>Settings with the chosen theme when OK was pressed, otherwise null.</summary>
    public TuiSettings? Result { get; private set; }

    private TuiTheme SelectedTheme => Themes[Math.Max(0, _themeRadioGroup.SelectedItem)].Theme;

    private VolumeProviderKeys SelectedVolumeProviderKeys =>
        VolumeProviderKeyBindings.All[Math.Max(0, _volumeProviderKeysRadioGroup.SelectedItem)];

    private EnterFileAction SelectedEnterFileAction =>
        EnterFileActions.All[Math.Max(0, _enterFileActionRadioGroup.SelectedItem)];

    private UaeMetadataFormat SelectedUaeMetadata =>
        UaeMetadataFormats.All[Math.Max(0, _uaeMetadataRadioGroup.SelectedItem)];

    private void Preview(TuiTheme theme)
    {
        _preview(theme);
        ColorScheme = TuiPalette.For(theme).DialogColorScheme;
        NeedsDraw = true;
    }
}
