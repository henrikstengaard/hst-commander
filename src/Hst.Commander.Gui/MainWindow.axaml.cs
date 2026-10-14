using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Hst.Commander.Core;
using Hst.Commander.Extensions.Volume.Abstractions;

namespace Hst.Commander.Gui;

public partial class MainWindow : Window
{
    private PaneView _activePane;
    private VolumeProviderKeys _volumeProviderKeys;

    public MainWindow() : this(Environment.CurrentDirectory)
    {
    }

    public MainWindow(string initialPath)
    {
        InitializeComponent();

        _activePane = LeftPane;
        ApplyVolumeProviderKeys(GuiSettings.Load().EffectiveVolumeProviderKeys);
        LeftPane.GotFocus += (_, _) => _activePane = LeftPane;
        RightPane.GotFocus += (_, _) => _activePane = RightPane;

        LeftPane.NavigationContext = new NavigationContext(initialPath);
        RightPane.NavigationContext = new NavigationContext(initialPath);
        LeftPane.NavigationFailed += async (_, exception) => await ShowErrorAsync(exception);
        RightPane.NavigationFailed += async (_, exception) => await ShowErrorAsync(exception);
        CopyMenuItem.Click += async (_, _) => await CopyAsync(_activePane);
        DeleteMenuItem.Click += async (_, _) => await DeleteAsync(_activePane);
        CopyButton.Click += async (_, _) => await CopyAsync(_activePane);
        DeleteButton.Click += async (_, _) => await DeleteAsync(_activePane);
        LeftVolumeMenuItem.Click += async (_, _) => await ChooseVolumeProviderAsync(LeftPane);
        RightVolumeMenuItem.Click += async (_, _) => await ChooseVolumeProviderAsync(RightPane);
        LocationMenuItem.Click += async (_, _) => await ShowLocationAsync(_activePane);
        LeftPane.PathDoubleClicked += async (_, _) => await ShowLocationAsync(LeftPane);
        RightPane.PathDoubleClicked += async (_, _) => await ShowLocationAsync(RightPane);
        SettingsMenuItem.Click += async (_, _) => await ShowSettingsAsync();
        QuitMenuItem.Click += (_, _) => Close();

        AddHandler(KeyDownEvent, OnWindowKeyDown, RoutingStrategies.Tunnel);
        Opened += async (_, _) =>
        {
            await LeftPane.InitializeAsync();
            await RightPane.InitializeAsync();
            LeftPane.FocusEntries();
        };
        Closed += (_, _) =>
        {
            LeftPane.NavigationContext.Dispose();
            RightPane.NavigationContext.Dispose();
        };
    }

    private async void OnWindowKeyDown(object? sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Tab:
                // tab always switches between the panes instead of moving focus through the controls
                e.Handled = true;
                (LeftPane.IsKeyboardFocusWithin ? RightPane : LeftPane).FocusEntries();
                break;
            case Key.F1 when e.KeyModifiers == VolumeProviderModifier:
                e.Handled = true;
                await ChooseVolumeProviderAsync(LeftPane);
                break;
            case Key.F2 when e.KeyModifiers == VolumeProviderModifier:
                e.Handled = true;
                await ChooseVolumeProviderAsync(RightPane);
                break;
            case Key.L when e.KeyModifiers == KeyModifiers.Control:
                e.Handled = true;
                await ShowLocationAsync(_activePane);
                break;
            case Key.F5:
                e.Handled = true;
                await CopyAsync(_activePane);
                break;
            case Key.F8:
            case Key.Delete:
                e.Handled = true;
                await DeleteAsync(_activePane);
                break;
        }
    }

    private KeyModifiers VolumeProviderModifier => _volumeProviderKeys == VolumeProviderKeys.ShiftFunctionKeys
        ? KeyModifiers.Shift
        : KeyModifiers.Alt;

    /// <summary>Sets the keys opening the volume provider lists, shown in the File menu.</summary>
    private void ApplyVolumeProviderKeys(VolumeProviderKeys keys)
    {
        _volumeProviderKeys = keys;
        LeftVolumeMenuItem.InputGesture = KeyGesture.Parse(VolumeProviderKeyBindings.GetLeftKeyText(keys));
        RightVolumeMenuItem.InputGesture = KeyGesture.Parse(VolumeProviderKeyBindings.GetRightKeyText(keys));
    }

    /// <summary>
    ///     Lets the user choose the volume provider a pane shows from the list of volume providers, centered over the
    ///     pane, and shows the root of a new volume from it. The pane gets the focus.
    /// </summary>
    private async Task ChooseVolumeProviderAsync(PaneView pane)
    {
        var volumeProvider = await ChooseVolumeProviderAsync(this, pane);
        if (volumeProvider != null)
        {
            await pane.ChangeVolumeProviderAsync(volumeProvider);
        }

        pane.FocusEntries();
    }

    /// <summary>Shows the volume provider list over a pane, owned by a window, returning the chosen one or null.</summary>
    private Task<IVolumeProvider?> ChooseVolumeProviderAsync(Window owner, PaneView pane) =>
        new VolumeProviderDialog(pane == LeftPane ? "Left" : "Right", VolumeProviders.All,
            pane.NavigationContext.VolumeProvider, pane).ShowDialog<IVolumeProvider?>(owner);

    /// <summary>
    ///     Shows the location dialog of a pane with its current path, changing to the entered path on OK or showing the
    ///     volume provider chosen with Volumes. The pane gets the focus.
    /// </summary>
    private async Task ShowLocationAsync(PaneView pane)
    {
        var result = await new LocationDialog(pane.NavigationContext.CurrentPath,
            owner => ChooseVolumeProviderAsync(owner, pane)).ShowDialog<LocationDialogResult?>(this);
        if (result?.VolumeProvider is { } volumeProvider)
        {
            await pane.ChangeVolumeProviderAsync(volumeProvider);
        }
        else if (result?.Path is { } path)
        {
            await pane.ChangeLocationAsync(path);
        }

        pane.FocusEntries();
    }

    /// <summary>
    ///     Deletes the marked entries, or the entry under the cursor, of a pane after confirming, showing the progress
    ///     while deleting. The other pane is refreshed too, as it may show the same location.
    /// </summary>
    private async Task DeleteAsync(PaneView pane)
    {
        var otherPane = pane == LeftPane ? RightPane : LeftPane;
        var entries = pane.NavigationContext.GetOperationEntries(pane.SelectedEntry);
        if (entries.Count == 0)
        {
            return;
        }

        if (await new DeleteDialog(NavigationContext.GetDeleteDescription(entries)).ShowDialog<bool?>(this) != true)
        {
            pane.FocusEntries();
            return;
        }

        var progressDialog = new DeleteProgressDialog((progress, cancellationToken) =>
            pane.NavigationContext.DeleteAsync(entries, progress, cancellationToken));
        await progressDialog.ShowDialog(this);

        switch (progressDialog.Exception)
        {
            case null:
                pane.NavigationContext.ClearMarks();
                break;
            case OperationCanceledException:
                break;
            default:
                await ShowErrorAsync(progressDialog.Exception);
                break;
        }

        await pane.RefreshEntriesAsync();
        await otherPane.RefreshEntriesAsync();
        pane.FocusEntries();
    }

    /// <summary>
    ///     Copies the marked entries, or the entry under the cursor, of a pane to the other pane after confirming the
    ///     destination and filter in the copy dialog, showing the progress while copying.
    /// </summary>
    private async Task CopyAsync(PaneView pane)
    {
        var otherPane = pane == LeftPane ? RightPane : LeftPane;
        var entries = pane.NavigationContext.GetOperationEntries(pane.SelectedEntry);
        if (entries.Count == 0)
        {
            return;
        }

        var request = await new CopyDialog(NavigationContext.GetCopyDescription(entries),
            otherPane.NavigationContext.CurrentPath).ShowDialog<CopyDialogResult?>(this);
        if (request == null)
        {
            pane.FocusEntries();
            return;
        }

        var progressDialog = new CopyProgressDialog((progress, confirmOverwrite, cancellationToken) =>
            pane.NavigationContext.CopyAsync(entries, otherPane.NavigationContext, request.Destination,
                request.Filter, progress, confirmOverwrite, cancellationToken));
        await progressDialog.ShowDialog(this);

        switch (progressDialog.Exception)
        {
            case null:
                pane.NavigationContext.ClearMarks();
                break;
            case OperationCanceledException:
                break;
            default:
                await ShowErrorAsync(progressDialog.Exception);
                break;
        }

        await pane.RefreshEntriesAsync();
        await otherPane.RefreshEntriesAsync();
        pane.FocusEntries();
    }

    private async Task ShowSettingsAsync()
    {
        var settings = await new SettingsDialog(GuiSettings.Load()).ShowDialog<GuiSettings?>(this);
        if (settings == null)
        {
            return;
        }

        ThemeManager.Apply(settings.Theme);
        ApplyVolumeProviderKeys(settings.EffectiveVolumeProviderKeys);
        try
        {
            settings.Save();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            await ShowErrorAsync(new IOException($"Failed to save settings to '{GuiSettings.FilePath}': {exception.Message}", exception));
        }
    }

    private Task ShowErrorAsync(Exception exception) => new ErrorDialog(exception.Message).ShowDialog(this);
}
