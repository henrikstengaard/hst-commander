using Hst.Commander.Abstractions;
using Hst.Commander.Core;
using Terminal.Gui;

namespace Hst.Commander.Tui;

/// <summary>
///     Modal settings view with the settings grouped in tabs: appearance, panes, copying and the editor, and the buttons
///     of a theme the user can change (<see cref="ICustomizableButtonPanel" />) while it is chosen. Theme changes are
///     previewed immediately through the preview action; <see cref="Result" /> holds the changed settings on OK or null
///     on cancel, in which case the caller reverts the preview, and <see cref="ChangedButtons" /> the changed buttons.
/// </summary>
public sealed class SettingsDialog : Dialog
{
    private const int DefaultWidth = 64;
    private const int DefaultHeight = 18;

    private readonly IReadOnlyList<ThemeChoice> _themes = Themes.All;
    private readonly TabView _tabView;

    /// <summary>Editors of the buttons by theme, so changes are kept when another theme is chosen and back again.</summary>
    private readonly Dictionary<ICustomizableButtonPanel, ButtonPanelEditor> _buttonEditors = [];

    private Tab? _buttonsTab;
    private readonly RadioGroup _themeRadioGroup;
    private readonly RadioGroup _volumeProviderKeysRadioGroup;
    private readonly RadioGroup _enterFileActionRadioGroup;
    private readonly TextField _temporaryCopyLimitField;
    private readonly RadioGroup _uaeMetadataRadioGroup;
    private readonly RadioGroup _editorRadioGroup;
    private readonly TextField _editorCommandField;
    private readonly TuiSettings _settings;
    private readonly Action<ThemeChoice> _preview;

    public SettingsDialog(TuiSettings settings, Action<ThemeChoice> preview, TuiPalette palette)
    {
        _settings = settings;
        _preview = preview;
        Title = "Settings";
        Width = DefaultWidth;
        Height = DefaultHeight;
        ColorScheme = palette.DialogColorScheme;

        // appearance
        var themeLabel = new Label { X = 1, Y = 1, Text = "Theme:" };
        _themeRadioGroup = new RadioGroup
        {
            X = 16,
            Y = 1,
            RadioLabels = _themes.Select(theme => theme.Name).ToArray(),
            SelectedItem = Math.Max(0, _themes.ToList().IndexOf(Themes.Find(_themes, settings.Theme)))
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
        _tabView = tabView;
        tabView.AddTab(CreateTab("Appearance", themeLabel, _themeRadioGroup), true);
        tabView.AddTab(CreateTab("Panes", volumeProviderKeysLabel, _volumeProviderKeysRadioGroup,
            enterFileActionLabel, _enterFileActionRadioGroup, temporaryCopyLimitLabel, _temporaryCopyLimitField,
            temporaryCopyLimitHelp), false);
        tabView.AddTab(CreateTab("Copying", uaeMetadataLabel, _uaeMetadataRadioGroup), false);
        tabView.AddTab(CreateTab("Editor", editorLabel, _editorRadioGroup, editorCommandLabel, _editorCommandField,
            editorCommandHelp), false);
        UpdateButtonsTab(palette);

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

            if (SelectedTheme.CustomizableButtons is { } buttonPanel &&
                _buttonEditors.TryGetValue(buttonPanel, out var buttonEditor))
            {
                ChangedButtons = (buttonPanel, buttonEditor.ToPanel());
            }

            Result = (_settings with
                {
                    Theme = SelectedTheme.Id,
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

    /// <summary>
    ///     Buttons of the chosen theme as changed in the editor, to be saved by the caller with
    ///     <see cref="ICustomizableButtonPanel.SaveLayout" /> when OK was pressed; null when the theme's buttons can't be
    ///     changed.
    /// </summary>
    public (ICustomizableButtonPanel ButtonPanel, ButtonPanel Layout)? ChangedButtons { get; private set; }

    private ThemeChoice SelectedTheme => _themes[Math.Max(0, _themeRadioGroup.SelectedItem)];

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

    private void Preview(ThemeChoice theme)
    {
        _preview(theme);
        var palette = TuiPalette.For(theme);
        ColorScheme = palette.DialogColorScheme;
        UpdateButtonsTab(palette);
        NeedsDraw = true;
    }

    /// <summary>
    ///     Shows the tab with the button editor while the chosen theme's buttons can be changed, making the dialog
    ///     larger for it.
    /// </summary>
    private void UpdateButtonsTab(TuiPalette palette)
    {
        if (_buttonsTab != null)
        {
            _tabView.RemoveTab(_buttonsTab);
            _buttonsTab = null;
        }

        if (SelectedTheme.CustomizableButtons is not { } buttonPanel)
        {
            Width = DefaultWidth;
            Height = DefaultHeight;
            return;
        }

        if (!_buttonEditors.TryGetValue(buttonPanel, out var editor))
        {
            editor = new ButtonPanelEditor(buttonPanel.Layout);
            _buttonEditors[buttonPanel] = editor;
        }

        var editorView = new ButtonPanelEditorView(editor) { X = 1, Y = 1, Width = Dim.Fill(1), Height = Dim.Fill(5) };
        editorView.EditRequested += (_, position) =>
        {
            var dialog = new PanelButtonDialog(editor.Columns[position.Column][position.Row], palette);
            Application.Run(dialog);
            if (dialog.Result is { } button)
            {
                editor.SetButton(position.Column, position.Row, button);
            }

            dialog.Dispose();
            editorView.SetFocus();
        };
        var help = new Label
        {
            X = 1,
            Y = Pos.AnchorEnd(4),
            Width = Dim.Fill(1),
            Height = 2,
            Text = "Ins add, Del remove, Enter change, Ctrl+arrows move the button\n(on a column header: add, remove or move the column)"
        };
        var addColumnButton = new Button { X = 1, Y = Pos.AnchorEnd(2), Text = "Add column" };
        addColumnButton.Accepting += (_, e) =>
        {
            e.Cancel = true;
            editorView.AddColumn();
            editorView.SetFocus();
        };
        var addButton = new Button { X = Pos.Right(addColumnButton) + 1, Y = Pos.AnchorEnd(2), Text = "Add button" };
        addButton.Accepting += (_, e) =>
        {
            e.Cancel = true;
            editorView.AddButton();
            editorView.SetFocus();
        };
        var removeButton = new Button { X = Pos.Right(addButton) + 1, Y = Pos.AnchorEnd(2), Text = "Remove" };
        removeButton.Accepting += (_, e) =>
        {
            e.Cancel = true;
            editorView.RemoveSelected();
            editorView.SetFocus();
        };
        var resetButton = new Button { X = Pos.Right(removeButton) + 1, Y = Pos.AnchorEnd(2), Text = "Reset" };
        resetButton.Accepting += (_, e) =>
        {
            e.Cancel = true;
            editor.Reset(buttonPanel.DefaultLayout);
            editorView.SetFocus();
        };

        _buttonsTab = CreateTab(buttonPanel.SettingsTitle, editorView, help, addColumnButton, addButton, removeButton,
            resetButton);
        _tabView.AddTab(_buttonsTab, false);
        Width = Dim.Percent(90);
        Height = Dim.Percent(90);
    }
}
