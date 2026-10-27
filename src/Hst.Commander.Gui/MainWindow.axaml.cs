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
    private bool _quitConfirmed;

    public MainWindow() : this(Environment.CurrentDirectory)
    {
    }

    public MainWindow(string initialPath)
    {
        InitializeComponent();
        Title = AppInfo.Current.NameAndVersion;

        _activePane = LeftPane;
        LeftPane.IsActive = true;
        var settings = GuiSettings.Load();
        ApplyVolumeProviderKeys(settings.EffectiveVolumeProviderKeys);
        LeftPane.GotFocus += (_, _) => SetActivePane(LeftPane);
        RightPane.GotFocus += (_, _) => SetActivePane(RightPane);

        LeftPane.NavigationContext = new NavigationContext(initialPath);
        RightPane.NavigationContext = new NavigationContext(initialPath);
        ApplyEnterFileAction(settings.EnterFileAction);
        ApplyUaeMetadata(settings.UaeMetadata);
        LeftPane.NavigationFailed += async (_, exception) => await ShowErrorAsync(exception);
        RightPane.NavigationFailed += async (_, exception) => await ShowErrorAsync(exception);
        // e.g. icon files written when saving icon positions, which the other pane may show
        LeftPane.EntriesChanged += async (_, _) => await RightPane.RefreshEntriesAsync();
        RightPane.EntriesChanged += async (_, _) => await LeftPane.RefreshEntriesAsync();
        BuildViewMenu();
        ViewMenuItem.Click += async (_, _) => await ViewAsync(_activePane);
        CopyMenuItem.Click += async (_, _) => await CopyAsync(_activePane);
        MoveMenuItem.Click += async (_, _) => await MoveAsync(_activePane);
        MakeDirectoryMenuItem.Click += async (_, _) => await MakeDirectoryAsync(_activePane);
        DeleteMenuItem.Click += async (_, _) => await DeleteAsync(_activePane);
        CreateVolumeMenuItem.Click += async (_, _) => await CreateVolumeAsync(_activePane);
        PropertiesMenuItem.Click += async (_, _) => await ShowPropertiesAsync(_activePane);
        OpenAsVolumeMenuItem.Click += async (_, _) =>
        {
            await _activePane.OpenSelectedEntryAsVolumeAsync();
            _activePane.FocusEntries();
        };
        SelectGroupMenuItem.Click += async (_, _) => await SelectGroupAsync(_activePane, true);
        UnselectGroupMenuItem.Click += async (_, _) => await SelectGroupAsync(_activePane, false);
        InvertSelectionMenuItem.Click += (_, _) => InvertSelection(_activePane);
        ViewButton.Click += async (_, _) => await ViewAsync(_activePane);
        CopyButton.Click += async (_, _) => await CopyAsync(_activePane);
        MoveButton.Click += async (_, _) => await MoveAsync(_activePane);
        MkdirButton.Click += async (_, _) => await MakeDirectoryAsync(_activePane);
        DeleteButton.Click += async (_, _) => await DeleteAsync(_activePane);
        CreateButton.Click += async (_, _) => await CreateVolumeAsync(_activePane);
        QuitButton.Click += (_, _) => Close();
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
            // setting the navigation modes lists the entries
            await LeftPane.SetNavigationModeAsync(PaneNavigationMode.Find(settings.LeftNavigationMode));
            await RightPane.SetNavigationModeAsync(PaneNavigationMode.Find(settings.RightNavigationMode));
            LeftPane.FocusEntries();
            if (StartupError is { } startupError)
            {
                StartupError = null;
                await new ErrorDialog(startupError).ShowDialog(this);
                LeftPane.FocusEntries();
            }
        };
        Closing += OnClosing;
        Closed += (_, _) =>
        {
            LeftPane.NavigationContext.Dispose();
            RightPane.NavigationContext.Dispose();
        };
    }

    /// <summary>Error shown when the window is opened, e.g. plugins that failed to load.</summary>
    public string? StartupError { get; set; }

    /// <summary>
    ///     Adds the navigation modes to the View menu, acting on the active pane: the built-in list and thumbnail modes
    ///     and the navigation modes of the plugins, the first nine with Ctrl+1 to Ctrl+9.
    /// </summary>
    private void BuildViewMenu()
    {
        var navigationModes = PaneNavigationMode.All;
        for (var index = 0; index < navigationModes.Count; index++)
        {
            var navigationMode = navigationModes[index];
            var menuItem = new MenuItem
            {
                Header = navigationMode.Name,
                ToggleType = MenuItemToggleType.Radio,
                GroupName = "NavigationMode",
                Tag = navigationMode
            };
            ToolTip.SetTip(menuItem, navigationMode.Description);
            if (index < 9)
            {
                menuItem.InputGesture = new KeyGesture(Key.D1 + index, KeyModifiers.Control);
            }

            menuItem.Click += async (_, _) => await SetNavigationModeAsync(_activePane, navigationMode);
            ViewMenu.Items.Add(menuItem);
        }

        ViewMenu.SubmenuOpened += (_, _) =>
        {
            foreach (var menuItem in ViewMenu.Items.OfType<MenuItem>())
            {
                menuItem.IsChecked = Equals(menuItem.Tag, _activePane.NavigationMode);
            }
        };
    }

    /// <summary>Shows the entries of a pane in a navigation mode, remembered in the settings.</summary>
    private async Task SetNavigationModeAsync(PaneView pane, PaneNavigationMode navigationMode)
    {
        await pane.SetNavigationModeAsync(navigationMode);
        pane.FocusEntries();
        try
        {
            var settings = GuiSettings.Load();
            (pane == LeftPane
                ? settings with { LeftNavigationMode = navigationMode.Name }
                : settings with { RightNavigationMode = navigationMode.Name }).Save();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            await ShowErrorAsync(new IOException(
                $"Failed to save settings to '{GuiSettings.FilePath}': {exception.Message}", exception));
        }
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
            case >= Key.D1 and <= Key.D9 when e.KeyModifiers == KeyModifiers.Control:
                if (e.Key - Key.D1 < PaneNavigationMode.All.Count)
                {
                    e.Handled = true;
                    await SetNavigationModeAsync(_activePane, PaneNavigationMode.All[e.Key - Key.D1]);
                }

                break;
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
            case Key.F3:
                e.Handled = true;
                await ViewAsync(_activePane);
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
            case Key.F9:
                e.Handled = true;
                await CreateVolumeAsync(_activePane);
                break;
            case Key.F10:
                e.Handled = true;
                Close();
                break;
        }
    }

    /// <summary>
    ///     Confirms quitting (F10, File > Quit, Alt+F4 or closing the window) with <see cref="QuitDialog" />: closing is
    ///     cancelled while the dialog is shown and the window is closed again on yes.
    /// </summary>
    private async void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        if (_quitConfirmed || e.CloseReason is WindowCloseReason.OSShutdown or WindowCloseReason.ApplicationShutdown)
        {
            return;
        }

        e.Cancel = true;
        if (OwnedWindows.Count > 0)
        {
            return;
        }

        if (await new QuitDialog().ShowDialog<bool?>(this) != true)
        {
            _activePane.FocusEntries();
            return;
        }

        _quitConfirmed = true;
        Close();
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

    /// <summary>Sets what Enter does with files in both panes.</summary>
    private void ApplyEnterFileAction(EnterFileAction action)
    {
        LeftPane.NavigationContext.EnterFileAction = action;
        RightPane.NavigationContext.EnterFileAction = action;
    }

    /// <summary>Sets how both panes store Amiga names, protection bits and comments when copying from Amiga volumes.</summary>
    private void ApplyUaeMetadata(UaeMetadataFormat format)
    {
        LeftPane.NavigationContext.UaeMetadata = format;
        RightPane.NavigationContext.UaeMetadata = format;
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
    ///     Shows the location dialog of a pane with its current path, changing to the entered path on OK, showing the
    ///     volume provider chosen with Volumes or going to the home directory with Home. The pane gets the focus.
    /// </summary>
    private async Task ShowLocationAsync(PaneView pane)
    {
        var result = await new LocationDialog(pane.NavigationContext.CurrentPath,
            owner => ChooseVolumeProviderAsync(owner, pane)).ShowDialog<LocationDialogResult?>(this);
        if (result?.VolumeProvider is { } volumeProvider)
        {
            await pane.ChangeVolumeProviderAsync(volumeProvider);
        }
        else if (result?.IsHome == true)
        {
            await pane.GoHomeAsync();
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
    ///     Views the file under the cursor of a pane in the viewer (F3), reading only the part of the file shown.
    ///     Directories aren't viewed.
    /// </summary>
    private async Task ViewAsync(PaneView pane)
    {
        if (pane.SelectedEntry is not { Type: EntryType.Item } entry)
        {
            return;
        }

        ViewerSession session;
        try
        {
            session = await pane.NavigationContext.OpenViewerAsync(entry);
        }
        catch (Exception exception)
        {
            await ShowErrorAsync(exception);
            pane.FocusEntries();
            return;
        }

        using (session)
        {
            await new ViewerDialog(entry.Name, session).ShowDialog(this);
        }

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
            properties = await pane.NavigationContext.GetPropertiesAsync(entries);
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
        var dialog = new PropertiesDialog(properties,
            (progress, cancellationToken) =>
                pane.NavigationContext.CountContentsAsync(entries, progress, cancellationToken),
            initialIcon,
            !typeOnly || sameType
                ? (size, cancellationToken) =>
                    FileIcons.LoadIconAsync(iconEntry, pane.NavigationContext, size, typeOnly, cancellationToken)
                : null,
            !typeOnly || sameType
                ? cancellationToken =>
                    FileIcons.LoadIconsAsync(iconEntry, pane.NavigationContext, typeOnly, cancellationToken)
                : null);
        await dialog.ShowDialog(this);
        if (dialog.CheckedFlags is { } checkedFlags)
        {
            await ChangeFlagsAsync(pane, properties, checkedFlags);
        }

        pane.FocusEntries();
    }

    /// <summary>
    ///     Changes the attributes and Amiga protection bits of the entry of the properties dialog to its check boxes,
    ///     refreshing both panes when changed, as the other pane may show the same location.
    /// </summary>
    private async Task ChangeFlagsAsync(PaneView pane, EntryProperties properties,
        IReadOnlyList<IReadOnlyList<bool>> checkedFlags)
    {
        try
        {
            if (!await pane.NavigationContext.ChangeFlagsAsync(properties, checkedFlags))
            {
                return;
            }
        }
        catch (Exception exception)
        {
            await ShowErrorAsync(exception);
        }

        await LeftPane.RefreshEntriesAsync();
        await RightPane.RefreshEntriesAsync();
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
    ///     Creates a volume as a file in the current location of a pane: the volume to create is chosen in the create
    ///     dialog, then its file name and fields are entered. When creating fails, the error is shown and the fields are
    ///     asked again with the input entered. The created file is selected and the other pane is refreshed too, as it
    ///     may show the same location.
    /// </summary>
    private async Task CreateVolumeAsync(PaneView pane)
    {
        var otherPane = pane == LeftPane ? RightPane : LeftPane;
        var creators = VolumeProviders.Creators;
        if (pane.NavigationContext.GetCreateVolumeNotSupportedMessage(creators) is { } message)
        {
            await new ErrorDialog(message).ShowDialog(this);
            pane.FocusEntries();
            return;
        }

        var creator = await new CreateVolumeDialog(creators).ShowDialog<IVolumeCreator?>(this);
        VolumeCreatorInput? input = null;
        while (creator != null)
        {
            input = await new CreateVolumeFieldsDialog(creator, input).ShowDialog<VolumeCreatorInput?>(this);
            if (input == null)
            {
                // icons may have been stopped by a failed attempt
                await pane.RefreshEntriesAsync();
                break;
            }

            try
            {
                await pane.CreateVolumeAsync(creator, input);
            }
            catch (Exception exception)
            {
                await ShowErrorAsync(exception);
                continue;
            }

            await otherPane.RefreshEntriesAsync();
            break;
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
        ApplyEnterFileAction(settings.EnterFileAction);
        ApplyUaeMetadata(settings.UaeMetadata);
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
