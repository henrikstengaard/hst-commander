using Hst.Commander.Core;
using Terminal.Gui;

namespace Hst.Commander.Tui;

/// <summary>
///     Modal settings view with the settings grouped in tabs: appearance, panes, copying and the editor. Theme changes
///     are previewed immediately through the preview action; <see cref="Result" /> holds the changed settings on OK or
///     null on cancel, in which case the caller reverts the preview.
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
    private readonly TextField _temporaryCopyLimitField;
    private readonly RadioGroup _uaeMetadataRadioGroup;
    private readonly RadioGroup _editorRadioGroup;
    private readonly TextField _editorCommandField;
    private readonly TuiSettings _settings;
    private readonly Action<TuiTheme> _preview;

    public SettingsDialog(TuiSettings settings, Action<TuiTheme> preview, TuiPalette palette)
    {
        _settings = settings;
        _preview = preview;
        Title = "Settings";
        Width = 64;
        Height = 18;
        ColorScheme = palette.DialogColorScheme;

        // appearance
        var themeLabel = new Label { X = 1, Y = 1, Text = "Theme:" };
        _themeRadioGroup = new RadioGroup
        {
            X = 16,
            Y = 1,
            RadioLabels = Themes.Select(theme => theme.Name).ToArray(),
            SelectedItem = Math.Max(0, Array.FindIndex(Themes, theme => theme.Theme == settings.Theme))
        };
        _themeRadioGroup.SelectedItemChanged += (_, _) => Preview(SelectedTheme);

        // panes
        var volumeProviderKeysLabel = new Label { X = 1, Y = 1, Text = "Volume keys:" };
        _volumeProviderKeysRadioGroup = new RadioGroup
        {
            X = 16,
            Y = 1,
            RadioLabels = VolumeProviderKeyBindings.All.Select(keys => keys == VolumeProviderKeyBindings.Default
                ? $"{VolumeProviderKeyBindings.GetName(keys)} (default)"
                : VolumeProviderKeyBindings.GetName(keys)).ToArray(),
            SelectedItem = Math.Max(0, VolumeProviderKeyBindings.All.ToList().IndexOf(settings.EffectiveVolumeProviderKeys))
        };

        var enterFileActionLabel = new Label { X = 1, Y = 4, Text = "Enter on file:" };
        _enterFileActionRadioGroup = new RadioGroup
        {
            X = 16,
            Y = 4,
            RadioLabels = EnterFileActions.All.Select(EnterFileActions.GetName).ToArray(),
            SelectedItem = Math.Max(0, EnterFileActions.All.ToList().IndexOf(settings.EnterFileAction))
        };

        var temporaryCopyLimitLabel = new Label { X = 1, Y = 7, Text = "Copy limit:" };
        _temporaryCopyLimitField = new TextField
        {
            X = 16, Y = 7, Width = 10, Text = settings.TemporaryCopyLimit.ToString()
        };
        var temporaryCopyLimitHelp = new Label
        {
            X = 16,
            Y = 8,
            Width = Dim.Fill(1),
            Height = 2,
            Text = "MB copied to open an image inside an image\n(e.g. zip in zip), 0 for no limit"
        };

        // copying
        var uaeMetadataLabel = new Label { X = 1, Y = 1, Text = "UAE metadata:" };
        _uaeMetadataRadioGroup = new RadioGroup
        {
            X = 16,
            Y = 1,
            RadioLabels = UaeMetadataFormats.All.Select(UaeMetadataFormats.GetName).ToArray(),
            SelectedItem = Math.Max(0, UaeMetadataFormats.All.ToList().IndexOf(settings.UaeMetadata))
        };

        // editor
        var editorLabel = new Label { X = 1, Y = 1, Text = "Editor (F4):" };
        _editorRadioGroup = new RadioGroup
        {
            X = 16,
            Y = 1,
            RadioLabels = EditorKinds.All.Select(kind => EditorKinds.GetName(kind, true)).ToArray(),
            SelectedItem = Math.Max(0, EditorKinds.All.ToList().IndexOf(settings.Editor))
        };
        var editorCommandLabel = new Label { X = 1, Y = 5, Text = "Command:" };
        _editorCommandField = new TextField
        {
            X = 16, Y = 5, Width = Dim.Fill(1), Text = settings.EditorCommand ?? string.Empty
        };
        var editorCommandHelp = new Label
        {
            X = 16,
            Y = 6,
            Width = Dim.Fill(1),
            Text = $"e.g. vim, {ExternalEditor.FilePlaceholder} is the file (else added last)"
        };
        _editorRadioGroup.SelectedItemChanged += (_, _) => UpdateEditorCommand();
        UpdateEditorCommand();

        var tabView = new TabView { X = 0, Y = 0, Width = Dim.Fill(), Height = Dim.Fill(1) };
        tabView.AddTab(CreateTab("Appearance", themeLabel, _themeRadioGroup), true);
        tabView.AddTab(CreateTab("Panes", volumeProviderKeysLabel, _volumeProviderKeysRadioGroup,
            enterFileActionLabel, _enterFileActionRadioGroup, temporaryCopyLimitLabel, _temporaryCopyLimitField,
            temporaryCopyLimitHelp), false);
        tabView.AddTab(CreateTab("Copying", uaeMetadataLabel, _uaeMetadataRadioGroup), false);
        tabView.AddTab(CreateTab("Editor", editorLabel, _editorRadioGroup, editorCommandLabel, _editorCommandField,
            editorCommandHelp), false);

        var okButton = new Button { Text = "OK", IsDefault = true };
        okButton.Accepting += (_, e) =>
        {
            e.Cancel = true;
            if (SelectedEditor == EditorKind.Custom && ExternalEditor.ParseCommand(_editorCommandField.Text) == null)
            {
                MessageBox.ErrorQuery("Error", "Enter the command of the custom editor", "OK");
                return;
            }

            if (!long.TryParse(_temporaryCopyLimitField.Text, out var temporaryCopyLimit) || temporaryCopyLimit < 0)
            {
                MessageBox.ErrorQuery("Error", "Enter the copy limit in MB, 0 for no limit", "OK");
                return;
            }

            Result = (_settings with
                {
                    Theme = SelectedTheme,
                    EnterFileAction = SelectedEnterFileAction,
                    UaeMetadata = SelectedUaeMetadata,
                    TemporaryCopyLimit = temporaryCopyLimit,
                    Editor = SelectedEditor,
                    EditorCommand = string.IsNullOrWhiteSpace(_editorCommandField.Text)
                        ? null
                        : _editorCommandField.Text.Trim()
                })
                .WithVolumeProviderKeys(SelectedVolumeProviderKeys);
            Application.RequestStop(this);
        };
        var cancelButton = new Button { Text = "Cancel" };
        cancelButton.Accepting += (_, e) =>
        {
            e.Cancel = true;
            Application.RequestStop(this);
        };

        Add(tabView);
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

    private EditorKind SelectedEditor => EditorKinds.All[Math.Max(0, _editorRadioGroup.SelectedItem)];

    /// <summary>The command can only be entered for the custom editor.</summary>
    private void UpdateEditorCommand() => _editorCommandField.Enabled = SelectedEditor == EditorKind.Custom;

    private static Tab CreateTab(string name, params View[] views)
    {
        var view = new View { Width = Dim.Fill(), Height = Dim.Fill(), CanFocus = true };
        // a radio group highlighted while pressed keeps the color scheme it had when pressed as its own when released,
        // so after clicking a theme it would keep the colors of the theme before instead of the dialog's
        foreach (var radioGroup in views.OfType<RadioGroup>())
        {
            radioGroup.HighlightStyle = HighlightStyle.None;
        }

        view.Add(views);
        return new Tab { DisplayText = name, View = view };
    }

    private void Preview(TuiTheme theme)
    {
        _preview(theme);
        ColorScheme = TuiPalette.For(theme).DialogColorScheme;
        NeedsDraw = true;
    }
}
