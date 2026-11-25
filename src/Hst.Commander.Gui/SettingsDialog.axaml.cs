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
    private const double DefaultHeight = 430;
    private const double ButtonsWidth = 820;
    private const double ButtonsHeight = 600;

    private readonly IReadOnlyList<ThemeChoice> _themes = Themes.SortedByName;
    private readonly Action<ThemeChoice, string?, double?> _preview;

    /// <summary>Items of the font list: the default (the theme's font or the default fonts), then the installed fonts.</summary>
    private readonly List<string> _fonts;

    /// <summary>Editors of the buttons by theme, so changes are kept when another theme is chosen and back again.</summary>
    private readonly Dictionary<ICustomizableButtonPanel, ButtonPanelEditorView> _buttonEditors = [];

    private TabItem? _buttonsTab;

    public SettingsDialog() : this(new GuiSettings())
    {
    }

    /// <param name="settings">Settings shown.</param>
    /// <param name="preview">
    ///     Applies a theme and font (family and size, null for the theme's) chosen in the dialog, and those of the
    ///     settings again on cancel; only the theme's colors when null.
    /// </param>
    public SettingsDialog(GuiSettings settings, Action<ThemeChoice, string?, double?>? preview = null)
    {
        InitializeComponent();
        _preview = preview ?? ((theme, _, _) => ThemeManager.Apply(theme));

        var originalTheme = Themes.Find(_themes, settings.Theme);
        ThemeComboBox.ItemsSource = _themes.Select(theme => theme.Name).ToList();
        ThemeComboBox.SelectedIndex = Math.Max(0, _themes.ToList().IndexOf(originalTheme));
        ThemeComboBox.SelectionChanged += (_, _) =>
        {
            Preview();
            UpdateButtonsTab();
            UpdateFontDownload();
        };
        _fonts = [DefaultFontName, ..GuiFonts.Installed];
        if (!string.IsNullOrWhiteSpace(settings.FontFamily) &&
            !_fonts.Contains(settings.FontFamily, StringComparer.OrdinalIgnoreCase))
        {
            // a font that isn't installed any more is kept until another one is chosen
            _fonts.Insert(1, settings.FontFamily);
        }

        FontComboBox.ItemsSource = _fonts;
        FontComboBox.SelectedIndex = string.IsNullOrWhiteSpace(settings.FontFamily)
            ? 0
            : Math.Max(0, _fonts.FindIndex(font =>
                string.Equals(font, settings.FontFamily, StringComparison.OrdinalIgnoreCase)));
        FontComboBox.SelectionChanged += (_, _) =>
        {
            Preview();
            UpdateFontDownload();
        };
        DownloadFontButton.Click += async (_, _) => await DownloadFontAsync();
        UpdateFontDownload();
        FontSizeUpDown.Minimum = (decimal)GuiFonts.MinimumSize;
        FontSizeUpDown.Maximum = (decimal)GuiFonts.MaximumSize;
        FontSizeUpDown.Value = settings.FontSize is { } fontSize ? (decimal)fontSize : null;
        StartMaximizedCheckBox.IsChecked = settings.StartMaximized;
        FontSizeUpDown.ValueChanged += (_, _) => Preview();
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
        DetectVolumeFilesCheckBox.IsChecked = settings.DetectVolumeFiles;
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
                    FontFamily = SelectedFontFamily,
                    FontSize = SelectedFontSize,
                    StartMaximized = StartMaximizedCheckBox.IsChecked == true,
                    EnterFileAction = SelectedEnterFileAction,
                    DetectVolumeFiles = DetectVolumeFilesCheckBox.IsChecked == true,
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
                _preview(originalTheme, settings.FontFamily, settings.FontSize);
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

    private const string DefaultFontName = "Default (the theme's font)";

    /// <summary>Font family chosen, null for the theme's font or the default.</summary>
    private string? SelectedFontFamily => FontComboBox.SelectedIndex > 0 ? _fonts[FontComboBox.SelectedIndex] : null;

    /// <summary>Font size chosen, null for the theme's size or the default.</summary>
    private double? SelectedFontSize => FontSizeUpDown.Value is { } value
        ? Math.Clamp((double)value, GuiFonts.MinimumSize, GuiFonts.MaximumSize)
        : null;

    private void Preview() => _preview(SelectedTheme, SelectedFontFamily, SelectedFontSize);

    /// <summary>
    ///     Shows the button downloading the theme's font when the theme has one to download, none of its fonts is
    ///     installed and no font of its own is chosen.
    /// </summary>
    private void UpdateFontDownload()
    {
        var theme = SelectedTheme.Theme;
        var visible = theme is { FontDownloads.Count: > 0, FontFamily: { } family } &&
                      !GuiFonts.IsInstalled(family) && SelectedFontFamily == null;
        FontDownloadPanel.IsVisible = visible;
        if (visible)
        {
            FontDownloadText.Text = $"{SelectedTheme.Name} uses a font that isn't installed, so a monospace font is used instead.";
        }
    }

    /// <summary>Downloads the theme's font after asking, then lists and uses it.</summary>
    private async Task DownloadFontAsync()
    {
        var theme = SelectedTheme.Theme;
        var downloaded = await new FontDownloadDialog(SelectedTheme.Name, theme.FontDownloads).ShowDialog<bool>(this);
        if (!downloaded)
        {
            return;
        }

        // list the downloaded fonts, keeping the chosen one
        var selected = SelectedFontFamily;
        _fonts.RemoveRange(1, _fonts.Count - 1);
        _fonts.AddRange(GuiFonts.Installed);
        FontComboBox.ItemsSource = null;
        FontComboBox.ItemsSource = _fonts;
        FontComboBox.SelectedIndex = selected == null
            ? 0
            : Math.Max(0, _fonts.FindIndex(font => string.Equals(font, selected, StringComparison.OrdinalIgnoreCase)));
        Preview();
        UpdateFontDownload();
    }

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
