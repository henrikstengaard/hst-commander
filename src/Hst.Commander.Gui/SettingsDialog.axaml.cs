using Avalonia.Controls;
using Hst.Commander.Abstractions;
using Hst.Commander.Core;

namespace Hst.Commander.Gui;

/// <summary>
///     Modal settings view with the settings grouped in tabs: appearance, panes, copying and the editor, and the buttons
///     of a theme the user can change (<see cref="ICustomizableButtonPanel" />) while it is chosen. Theme changes are
///     previewed immediately and reverted on cancel; the dialog returns the changed settings on OK or null on cancel,
///     with the changed buttons in <see cref="ChangedButtons" />.
/// </summary>
public partial class SettingsDialog : Window
{
    private const double DefaultWidth = 520;
    private const double DefaultHeight = 380;
    private const double ButtonsWidth = 820;
    private const double ButtonsHeight = 600;

    private readonly IReadOnlyList<ThemeChoice> _themes = Themes.All;
    private readonly Action<ThemeChoice> _preview;

    /// <summary>Editors of the buttons by theme, so changes are kept when another theme is chosen and back again.</summary>
    private readonly Dictionary<ICustomizableButtonPanel, ButtonPanelEditorView> _buttonEditors = [];

    private TabItem? _buttonsTab;

    public SettingsDialog() : this(new GuiSettings())
    {
    }

    /// <param name="settings">Settings shown.</param>
    /// <param name="preview">
    ///     Applies a theme chosen in the dialog, and the theme of the settings again on cancel; only its colors when
    ///     null.
    /// </param>
    public SettingsDialog(GuiSettings settings, Action<ThemeChoice>? preview = null)
    {
        InitializeComponent();
        _preview = preview ?? ThemeManager.Apply;

        var originalTheme = Themes.Find(_themes, settings.Theme);
        ThemeComboBox.ItemsSource = _themes.Select(theme => theme.Name).ToList();
        ThemeComboBox.SelectedIndex = Math.Max(0, _themes.ToList().IndexOf(originalTheme));
        ThemeComboBox.SelectionChanged += (_, _) =>
        {
            _preview(SelectedTheme);
            UpdateButtonsTab();
        };
        UpdateButtonsTab();
        VolumeProviderKeysComboBox.ItemsSource = VolumeProviderKeyBindings.All
            .Select(keys => keys == VolumeProviderKeyBindings.Default
                ? $"{VolumeProviderKeyBindings.GetName(keys)} (default)"
                : VolumeProviderKeyBindings.GetName(keys))
            .ToList();
        VolumeProviderKeysComboBox.SelectedIndex =
            Math.Max(0, VolumeProviderKeyBindings.All.ToList().IndexOf(settings.EffectiveVolumeProviderKeys));
        EnterFileActionComboBox.ItemsSource = EnterFileActions.All.Select(EnterFileActions.GetName).ToList();
        EnterFileActionComboBox.SelectedIndex =
            Math.Max(0, EnterFileActions.All.ToList().IndexOf(settings.EnterFileAction));
        TemporaryCopyLimitUpDown.Value = settings.TemporaryCopyLimit;
        UaeMetadataComboBox.ItemsSource = UaeMetadataFormats.All.Select(UaeMetadataFormats.GetName).ToList();
        UaeMetadataComboBox.SelectedIndex = Math.Max(0, UaeMetadataFormats.All.ToList().IndexOf(settings.UaeMetadata));
        EditorComboBox.ItemsSource = EditorKinds.All.Select(kind => EditorKinds.GetName(kind, false)).ToList();
        EditorComboBox.SelectedIndex = Math.Max(0, EditorKinds.All.ToList().IndexOf(settings.Editor));
        EditorComboBox.SelectionChanged += (_, _) => UpdateEditorCommand();
        EditorCommandTextBox.Text = settings.EditorCommand ?? string.Empty;
        EditorCommandTextBox.PlaceholderText = OperatingSystem.IsWindows()
            ? "\"C:\\Program Files\\Notepad++\\notepad++.exe\" -multiInst"
            : "code --wait";
        EditorCommandHelpText.Text =
            $"The program and its arguments; {ExternalEditor.FilePlaceholder} is replaced with the file, otherwise it's added last. Files inside images are written back when the editor exits.";
        UpdateEditorCommand();

        var accepted = false;
        OkButton.Click += (_, _) =>
        {
            var command = EditorCommandTextBox.Text?.Trim();
            if (SelectedEditor == EditorKind.Custom && (command == null || ExternalEditor.ParseCommand(command) == null))
            {
                TabControl.SelectedIndex = TabControl.ItemCount - 1;
                EditorCommandTextBox.Focus();
                return;
            }

            if (SelectedTheme.CustomizableButtons is { } buttonPanel &&
                _buttonEditors.TryGetValue(buttonPanel, out var editorView))
            {
                ChangedButtons = (buttonPanel, editorView.Editor.ToPanel());
            }

            accepted = true;
            Close((settings with
                {
                    Theme = SelectedTheme.Id,
                    EnterFileAction = SelectedEnterFileAction,
                    UaeMetadata = SelectedUaeMetadata,
                    TemporaryCopyLimit = (long)Math.Max(0, TemporaryCopyLimitUpDown.Value ?? 0),
                    Editor = SelectedEditor,
                    EditorCommand = string.IsNullOrEmpty(command) ? null : command
                })
                .WithVolumeProviderKeys(SelectedVolumeProviderKeys));
        };
        CancelButton.Click += (_, _) => Close();
        Closed += (_, _) =>
        {
            if (!accepted)
            {
                _preview(originalTheme);
            }
        };
        Opened += (_, _) => ThemeComboBox.Focus();
    }

    /// <summary>
    ///     Buttons of the chosen theme as changed in the editor, to be saved by the caller with
    ///     <see cref="ICustomizableButtonPanel.SaveLayout" /> when OK was clicked; null when the theme's buttons can't be
    ///     changed.
    /// </summary>
    public (ICustomizableButtonPanel ButtonPanel, ButtonPanel Layout)? ChangedButtons { get; private set; }

    private ThemeChoice SelectedTheme => _themes[Math.Max(0, ThemeComboBox.SelectedIndex)];

    private VolumeProviderKeys SelectedVolumeProviderKeys =>
        VolumeProviderKeyBindings.All[Math.Max(0, VolumeProviderKeysComboBox.SelectedIndex)];

    private EnterFileAction SelectedEnterFileAction =>
        EnterFileActions.All[Math.Max(0, EnterFileActionComboBox.SelectedIndex)];

    private UaeMetadataFormat SelectedUaeMetadata =>
        UaeMetadataFormats.All[Math.Max(0, UaeMetadataComboBox.SelectedIndex)];

    private EditorKind SelectedEditor => EditorKinds.All[Math.Max(0, EditorComboBox.SelectedIndex)];

    /// <summary>The command can only be entered for the custom editor.</summary>
    private void UpdateEditorCommand() => EditorCommandTextBox.IsEnabled = SelectedEditor == EditorKind.Custom;

    /// <summary>
    ///     Shows the tab with the button editor after the appearance tab while the chosen theme's buttons can be changed,
    ///     making the dialog larger for it.
    /// </summary>
    private void UpdateButtonsTab()
    {
        if (_buttonsTab != null)
        {
            TabControl.Items.Remove(_buttonsTab);
            _buttonsTab = null;
        }

        if (SelectedTheme.CustomizableButtons is not { } buttonPanel)
        {
            Width = DefaultWidth;
            Height = DefaultHeight;
            return;
        }

        if (!_buttonEditors.TryGetValue(buttonPanel, out var editorView))
        {
            editorView = new ButtonPanelEditorView(buttonPanel.Layout, buttonPanel.DefaultLayout);
            _buttonEditors[buttonPanel] = editorView;
        }

        _buttonsTab = new TabItem { Header = buttonPanel.SettingsTitle, Content = editorView };
        TabControl.Items.Insert(1, _buttonsTab);
        Width = ButtonsWidth;
        Height = ButtonsHeight;
    }
}
