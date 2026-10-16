using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Hst.Commander.Core;
using Hst.Commander.Abstractions;
using Hst.Commander.Gui.Icons;

namespace Hst.Commander.Gui;

public partial class MainWindow : Window
{
    private PaneView _activePane;
    private VolumeProviderKeys _volumeProviderKeys;
    private string _selectGroupFilter = SelectGroupOptions.Default.Filter;

    public MainWindow() : this(Environment.CurrentDirectory)
    {
    }

    public MainWindow(string initialPath)
    {
        InitializeComponent();
        Title = AppInfo.Current.NameAndVersion;

        _activePane = LeftPane;
        LeftPane.IsActive = true;
        ApplyVolumeProviderKeys(GuiSettings.Load().EffectiveVolumeProviderKeys);
        LeftPane.GotFocus += (_, _) => SetActivePane(LeftPane);
        RightPane.GotFocus += (_, _) => SetActivePane(RightPane);

        LeftPane.NavigationContext = new NavigationContext(initialPath);
        RightPane.NavigationContext = new NavigationContext(initialPath);
        LeftPane.NavigationFailed += async (_, exception) => await ShowErrorAsync(exception);
        RightPane.NavigationFailed += async (_, exception) => await ShowErrorAsync(exception);
        CopyMenuItem.Click += async (_, _) => await CopyAsync(_activePane);
        MoveMenuItem.Click += async (_, _) => await MoveAsync(_activePane);
        MakeDirectoryMenuItem.Click += async (_, _) => await MakeDirectoryAsync(_activePane);
        DeleteMenuItem.Click += async (_, _) => await DeleteAsync(_activePane);
        PropertiesMenuItem.Click += async (_, _) => await ShowPropertiesAsync(_activePane);
        SelectGroupMenuItem.Click += async (_, _) => await SelectGroupAsync(_activePane, true);
        UnselectGroupMenuItem.Click += async (_, _) => await SelectGroupAsync(_activePane, false);
        InvertSelectionMenuItem.Click += (_, _) => InvertSelection(_activePane);
        CopyButton.Click += async (_, _) => await CopyAsync(_activePane);
        MoveButton.Click += async (_, _) => await MoveAsync(_activePane);
        MkdirButton.Click += async (_, _) => await MakeDirectoryAsync(_activePane);
        DeleteButton.Click += async (_, _) => await DeleteAsync(_activePane);
        LeftVolumeMenuItem.Click += async (_, _) => await ChooseVolumeProviderAsync(LeftPane);
        RightVolumeMenuItem.Click += async (_, _) => await ChooseVolumeProviderAsync(RightPane);
        LocationMenuItem.Click += async (_, _) => await ShowLocationAsync(_activePane);
        LeftPane.PathDoubleClicked += async (_, _) => await ShowLocationAsync(LeftPane);
        RightPane.PathDoubleClicked += async (_, _) => await ShowLocationAsync(RightPane);
        SettingsMenuItem.Click += async (_, _) => await ShowSettingsAsync();
        QuitMenuItem.Click += (_, _) => Close();
        AboutMenuItem.Click += async (_, _) => await new AboutDialog().ShowDialog(this);

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

    /// <summary>Makes a pane the active pane, only the active pane shows the current entry.</summary>
    private void SetActivePane(PaneView pane)
    {
        _activePane = pane;
        LeftPane.IsActive = pane == LeftPane;
        RightPane.IsActive = pane == RightPane;
    }

    private async void OnWindowKeyDown(object? sender, KeyEventArgs e)
    {
        // +, - and * depend on the keyboard layout, e.g. shift+= for + on US keyboards, so match the typed symbol
        if (e.KeySymbol == "+" || e.Key == Key.Add)
        {
            e.Handled = true;
            await SelectGroupAsync(_activePane, true);
            return;
        }

        if (e.KeySymbol == "-" || e.Key == Key.Subtract)
        {
            e.Handled = true;
            await SelectGroupAsync(_activePane, false);
            return;
        }

        if (e.KeySymbol == "*" || e.Key == Key.Multiply)
        {
            e.Handled = true;
            InvertSelection(_activePane);
            return;
        }

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
            case Key.Enter when e.KeyModifiers == KeyModifiers.Alt:
            case Key.P when e.KeyModifiers == KeyModifiers.Control:
                e.Handled = true;
                await ShowPropertiesAsync(_activePane);
                break;
            case Key.L when e.KeyModifiers == KeyModifiers.Control:
                e.Handled = true;
                await ShowLocationAsync(_activePane);
                break;
            case Key.F5:
                e.Handled = true;
                await CopyAsync(_activePane);
                break;
            case Key.F6:
                e.Handled = true;
                await MoveAsync(_activePane);
                break;
            case Key.F7:
                e.Handled = true;
                await MakeDirectoryAsync(_activePane);
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
    ///     Marks (select) or unmarks (unselect) the entries of a pane matching the filter entered in the select group
    ///     dialog, keeping the other marks. The filter is remembered for the next time, the check boxes start unchecked.
    /// </summary>
    private async Task SelectGroupAsync(PaneView pane, bool select)
    {
        var options = await new SelectGroupDialog(SelectGroupOptions.Default with { Filter = _selectGroupFilter },
            select ? "Select group" : "Unselect group").ShowDialog<SelectGroupOptions?>(this);
        if (options != null)
        {
            _selectGroupFilter = options.Filter;
            if (select)
            {
                pane.MarkGroup(options);
            }
            else
            {
                pane.UnmarkGroup(options);
            }
        }

        pane.FocusEntries();
    }

    /// <summary>Inverts the marks of the entries of a pane: marked entries are unmarked and the others marked.</summary>
    private static void InvertSelection(PaneView pane)
    {
        pane.InvertMarks();
        pane.FocusEntries();
    }

    /// <summary>
    ///     Shows the properties of the marked entries, or the entry under the cursor, of a pane. The size and contents
    ///     are counted in the background while the dialog is open; closing it stops counting.
    /// </summary>
    private async Task ShowPropertiesAsync(PaneView pane)
    {
        var entries = pane.NavigationContext.GetOperationEntries(pane.SelectedEntry);
        if (entries.Count == 0)
        {
            return;
        }

        EntryProperties properties;
        try
        {
            properties = pane.NavigationContext.GetProperties(entries);
        }
        catch (Exception exception)
        {
            await ShowErrorAsync(exception);
            pane.FocusEntries();
            return;
        }

        // several entries show the type icon when they are all of the same type, e.g. folders or .txt files
        var iconEntry = entries[0];
        var typeOnly = entries.Count > 1;
        var sameType = entries.All(entry => entry.Type == iconEntry.Type && (entry.Type == EntryType.Container
            ? !pane.NavigationContext.IsDriveList
            : Path.GetExtension(entry.Name).Equals(Path.GetExtension(iconEntry.Name),
                StringComparison.OrdinalIgnoreCase)));
        var initialIcon = entries.All(entry => entry.Type == EntryType.Container)
            ? FileIcons.DefaultFolderIcon
            : FileIcons.DefaultFileIcon;
        await new PropertiesDialog(properties,
            (progress, cancellationToken) =>
                pane.NavigationContext.CountContentsAsync(entries, progress, cancellationToken),
            initialIcon,
            !typeOnly || sameType
                ? (size, cancellationToken) =>
                    FileIcons.LoadIconAsync(iconEntry, pane.NavigationContext, size, typeOnly, cancellationToken)
                : null).ShowDialog(this);
        pane.FocusEntries();
    }

    /// <summary>
    ///     Creates a directory in the current location of a pane with the name entered in the make directory dialog and
    ///     selects it. The other pane is refreshed too, as it may show the same location.
    /// </summary>
    private async Task MakeDirectoryAsync(PaneView pane)
    {
        var otherPane = pane == LeftPane ? RightPane : LeftPane;
        if (pane.NavigationContext.CreateDirectoryNotSupportedMessage is { } message)
        {
            await new ErrorDialog(message).ShowDialog(this);
            pane.FocusEntries();
            return;
        }

        var name = await new MakeDirectoryDialog().ShowDialog<string?>(this);
        if (name != null)
        {
            await pane.CreateDirectoryAsync(name);
            await otherPane.RefreshEntriesAsync();
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

    /// <summary>
    ///     Moves the marked entries, or the entry under the cursor, of a pane to the other pane after confirming the
    ///     destination and filter in the move dialog, showing the progress while moving. A single entry moved to a name
    ///     without path separators is renamed instead and selected.
    /// </summary>
    private async Task MoveAsync(PaneView pane)
    {
        var otherPane = pane == LeftPane ? RightPane : LeftPane;
        var entries = pane.NavigationContext.GetOperationEntries(pane.SelectedEntry);
        if (entries.Count == 0)
        {
            return;
        }

        if (pane.NavigationContext.MoveNotSupportedMessage is { } message)
        {
            await new ErrorDialog(message).ShowDialog(this);
            pane.FocusEntries();
            return;
        }

        var request = await new CopyDialog(NavigationContext.GetMoveDescription(entries),
            otherPane.NavigationContext.CurrentPath, "Move").ShowDialog<CopyDialogResult?>(this);
        if (request == null)
        {
            pane.FocusEntries();
            return;
        }

        string? newName;
        try
        {
            newName = await pane.NavigationContext.GetRenameNameAsync(entries, otherPane.NavigationContext,
                request.Destination);
        }
        catch (Exception exception)
        {
            await ShowErrorAsync(exception);
            pane.FocusEntries();
            return;
        }

        if (newName != null)
        {
            await pane.RenameAsync(entries[0], newName);
            await otherPane.RefreshEntriesAsync();
            pane.FocusEntries();
            return;
        }

        var progressDialog = new CopyProgressDialog((progress, confirmOverwrite, cancellationToken) =>
            pane.NavigationContext.MoveAsync(entries, otherPane.NavigationContext, request.Destination,
                request.Filter, progress, confirmOverwrite, cancellationToken), "Move");
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
