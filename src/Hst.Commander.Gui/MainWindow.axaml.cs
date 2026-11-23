using System.ComponentModel;
using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Hst.Commander.Core;
using Hst.Commander.Abstractions;
using Hst.Commander.Gui.Icons;

namespace Hst.Commander.Gui;

public partial class MainWindow : Window
{
    private PaneView _activePane;
    private VolumeProviderKeys _volumeProviderKeys;
    private string _selectGroupFilter = SelectGroupOptions.Default.Filter;
    private SearchOptions _searchOptions = SearchOptions.Default;
    private SearchResultSort _searchSort = SearchResultSort.Default;
    private bool _quitConfirmed;
    private GuiSettings _editorSettings = new();
    private readonly CommandLine _commandLine = new();

    public MainWindow() : this(Environment.CurrentDirectory)
    {
    }

    public MainWindow(string initialPath)
    {
        InitializeComponent();
        Title = Elevation.IsElevated
            ? $"{AppInfo.Current.NameAndVersion} ({Elevation.AdministratorName})"
            : AppInfo.Current.NameAndVersion;
        AdministratorIndicator.IsVisible = Elevation.IsElevated;
        AdministratorText.Text = Elevation.AdministratorName;

        _activePane = LeftPane;
        LeftPane.IsActive = true;
        var settings = GuiSettings.Load();
        ApplyVolumeProviderKeys(settings.EffectiveVolumeProviderKeys);
        LeftPane.GotFocus += (_, _) => SetActivePane(LeftPane);
        RightPane.GotFocus += (_, _) => SetActivePane(RightPane);

        LeftPane.NavigationContext = new NavigationContext(initialPath);
        RightPane.NavigationContext = new NavigationContext(initialPath);
        ApplyEnterFileAction(settings.EnterFileAction);
        LeftPane.NavigationContext.DetectVolumeFiles = settings.DetectVolumeFiles;
        RightPane.NavigationContext.DetectVolumeFiles = settings.DetectVolumeFiles;
        ApplyUaeMetadata(settings.UaeMetadata);
        ApplyTemporaryCopyLimit(settings.TemporaryCopyLimitBytes);
        _editorSettings = settings;
        LeftPane.NavigationFailed += async (_, exception) => await ShowErrorAsync(exception);
        RightPane.NavigationFailed += async (_, exception) => await ShowErrorAsync(exception);
        LeftPane.SortChanged += async (_, _) => await SaveSortsAsync();
        RightPane.SortChanged += async (_, _) => await SaveSortsAsync();
        // e.g. icon files written when saving icon positions, which the other pane may show
        LeftPane.EntriesChanged += async (_, _) => await RightPane.RefreshEntriesAsync();
        RightPane.EntriesChanged += async (_, _) => await LeftPane.RefreshEntriesAsync();
        LeftPane.EntriesDropped += async (_, request) => await DropAsync(LeftPane, request);
        RightPane.EntriesDropped += async (_, request) => await DropAsync(RightPane, request);
        LeftPane.ActionRequested += async (_, action) => await RunPaneActionAsync(LeftPane, action);
        RightPane.ActionRequested += async (_, action) => await RunPaneActionAsync(RightPane, action);
        LeftPane.ShellHereRequested += async (_, kind) => await OpenShellHereAsync(LeftPane, kind);
        RightPane.ShellHereRequested += async (_, kind) => await OpenShellHereAsync(RightPane, kind);
        BuildViewMenu();
        BottomPanel.ActionInvoked += async (_, action) => await RunActionAsync(action);
        ApplyAppearance(Themes.Find(settings.Theme), settings.FontFamily, settings.FontSize);
        ViewMenuItem.Click += async (_, _) => await ViewAsync(_activePane);
        EditMenuItem.Click += async (_, _) => await EditAsync(_activePane);
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
        SearchMenuItem.Click += async (_, _) => await SearchAsync(_activePane);
        // after the menu has closed and given the focus back
        QuickSearchMenuItem.Click += (_, _) => Dispatcher.UIThread.Post(() => _activePane.StartQuickSearch());
        LeftVolumeMenuItem.Click += async (_, _) => await ChooseVolumeProviderAsync(LeftPane);
        RightVolumeMenuItem.Click += async (_, _) => await ChooseVolumeProviderAsync(RightPane);
        LocationMenuItem.Click += async (_, _) => await ShowLocationAsync(_activePane);
        HomeMenuItem.Click += async (_, _) => await GoHomeAsync(_activePane);
        AddBookmarkMenuItem.Click += async (_, _) => await AddBookmarkAsync(_activePane);
        RefreshMenuItem.Click += async (_, _) => await RefreshAsync(_activePane);
        SwapPanesMenuItem.Click += async (_, _) => await SwapPanesAsync();
        CommandLineMenuItem.Click += (_, _) => FocusCommandLine();
        LeftPane.PathClicked += async (_, _) => await ShowLocationAsync(LeftPane);
        RightPane.PathClicked += async (_, _) => await ShowLocationAsync(RightPane);
        SettingsMenuItem.Click += async (_, _) => await ShowSettingsAsync();
        QuitMenuItem.Click += (_, _) => Close();
        AboutMenuItem.Click += async (_, _) => await new AboutDialog().ShowDialog(this);

        AddHandler(KeyDownEvent, OnWindowKeyDown, RoutingStrategies.Tunnel);
        Opened += async (_, _) =>
        {
            // e.g. where the panes were before restarting as administrator
            await StartupOptions.Left.ApplyAsync(LeftPane.NavigationContext);
            await StartupOptions.Right.ApplyAsync(RightPane.NavigationContext);
            var startPane = StartupOptions.IsRightActive ? RightPane : LeftPane;
            LeftPane.NavigationContext.Sort = settings.LeftSort ?? EntrySort.Default;
            RightPane.NavigationContext.Sort = settings.RightSort ?? EntrySort.Default;

            // setting the navigation modes lists the entries, at the same time so a pane listing slowly, e.g. a
            // sleeping physical disk waking up, doesn't keep the other empty
            await Task.WhenAll(
                LeftPane.SetNavigationModeAsync(PaneNavigationMode.Find(settings.LeftNavigationMode)),
                RightPane.SetNavigationModeAsync(PaneNavigationMode.Find(settings.RightNavigationMode)));
            startPane.FocusEntries();
            if (StartupError is { } startupError)
            {
                StartupError = null;
                await new ErrorDialog(startupError).ShowDialog(this);
                startPane.FocusEntries();
            }

            if (LastCrashReport is { } lastCrashReport)
            {
                LastCrashReport = null;
                await UnexpectedErrorDialog.ForLastCrash(lastCrashReport).ShowDialog(this);
                startPane.FocusEntries();
            }

            if (CrashHandler.TestCrash == "ui")
            {
                throw CrashHandler.CreateTestUiException();
            }
        };
        // cut files pasted in another application (e.g. Explorer) are moved by it, so the panes are listed again
        var cutMovedTimer = new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Background, async (_, _) =>
        {
            if (EntryClipboard.CheckCutMoved())
            {
                await RefreshPanesAsync(LeftPane, RightPane);
            }
        });
        cutMovedTimer.Start();
        Closing += OnClosing;
        Closed += (_, _) =>
        {
            cutMovedTimer.Stop();
            LeftPane.NavigationContext.Dispose();
            RightPane.NavigationContext.Dispose();
        };
    }

    /// <summary>Error shown when the window is opened, e.g. plugins that failed to load.</summary>
    public string? StartupError { get; set; }

    /// <summary>Report of the crash of the last run (<see cref="CrashMarker" />), offered to be reported when opened.</summary>
    public string? LastCrashReport { get; set; }

    /// <summary>Where the panes start when the window is opened, e.g. given on the command line.</summary>
    public StartupOptions StartupOptions { get; init; } = new();

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

    /// <summary>
    ///     Goes to the user's home directory in a pane (Ctrl+H, Home in the location dialog), switching to the Local
    ///     volume provider. The pane gets the focus.
    /// </summary>
    private static async Task GoHomeAsync(PaneView pane)
    {
        await pane.GoHomeAsync();
        pane.FocusEntries();
    }

    /// <summary>Lists the entries of a pane again (Ctrl+R), e.g. after files were changed outside.</summary>
    private static async Task RefreshAsync(PaneView pane)
    {
        await pane.RefreshEntriesAsync();
        pane.FocusEntries();
    }

    /// <summary>
    ///     Swaps the left and right panes (Ctrl+U), like Midnight Commander: each pane's location, entries, marks,
    ///     cursor and navigation mode move to the other side, and the focus moves with the active pane to the other
    ///     side. The navigation modes and the orders of the entries are remembered for their new sides.
    /// </summary>
    private async Task SwapPanesAsync()
    {
        await LeftPane.SwapWithAsync(RightPane);
        var activePane = _activePane == LeftPane ? RightPane : LeftPane;
        SetActivePane(activePane);
        activePane.FocusEntries();
        try
        {
            (GuiSettings.Load() with
            {
                LeftNavigationMode = LeftPane.NavigationMode.Name,
                RightNavigationMode = RightPane.NavigationMode.Name,
                LeftSort = LeftPane.NavigationContext.Sort,
                RightSort = RightPane.NavigationContext.Sort
            }).Save();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            await ShowErrorAsync(new IOException(
                $"Failed to save settings to '{GuiSettings.FilePath}': {exception.Message}", exception));
        }
    }

    /// <summary>Remembers the order of the entries of both panes in the settings, after a column header was clicked.</summary>
    private async Task SaveSortsAsync()
    {
        try
        {
            (GuiSettings.Load() with
            {
                LeftSort = LeftPane.NavigationContext.Sort,
                RightSort = RightPane.NavigationContext.Sort
            }).Save();
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

    /// <summary>Focuses the command line (Ctrl+T), keeping the command typed.</summary>
    private void FocusCommandLine()
    {
        CommandBox.Focus();
        CommandBox.CaretIndex = CommandBox.Text?.Length ?? 0;
    }

    private bool IsCommandLineFocused() => FocusManager?.GetFocusedElement() == CommandBox;

    /// <summary>
    ///     Inserts the name or full path of the entry under the cursor of the active pane in the command line
    ///     (Ctrl+Enter, Ctrl+Shift+Enter): at its caret when typing in it, otherwise at the end.
    /// </summary>
    private void InsertSelectedEntryInCommandLine(bool fullPath, bool atCaret)
    {
        if (_activePane.SelectedEntry is not { } entry ||
            _activePane.NavigationContext.GetCommandLineText(entry, fullPath) is not { } text)
        {
            return;
        }

        _commandLine.Text = CommandBox.Text ?? string.Empty;
        var position = _commandLine.Insert(text, atCaret ? CommandBox.CaretIndex : _commandLine.Text.Length);
        CommandBox.Text = _commandLine.Text;
        CommandBox.CaretIndex = position;
    }

    /// <summary>
    ///     Keys of the command line: Enter runs the command, Esc and Tab go back to the active pane keeping the command,
    ///     Up and Down go through the commands run. True when the key was handled.
    /// </summary>
    private async Task<bool> HandleCommandLineKeyAsync(KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Enter when e.KeyModifiers == KeyModifiers.None:
                e.Handled = true;
                await RunCommandLineAsync();
                return true;
            case Key.Escape:
            case Key.Tab:
                e.Handled = true;
                _activePane.FocusEntries();
                return true;
            case Key.Up or Key.Down when e.KeyModifiers == KeyModifiers.None:
                e.Handled = true;
                _commandLine.Text = CommandBox.Text ?? string.Empty;
                if (e.Key == Key.Up ? _commandLine.Previous() : _commandLine.Next())
                {
                    CommandBox.Text = _commandLine.Text;
                    CommandBox.CaretIndex = _commandLine.Text.Length;
                }

                return true;
            // typed in the command line, not selecting entries or deleting them
            case Key.Delete or Key.Add or Key.Subtract or Key.Multiply:
                return true;
            default:
                return e.KeySymbol is "+" or "-" or "*";
        }
    }

    /// <summary>
    ///     Runs the command typed in the command line in the active pane's directory with the shell, without waiting for
    ///     it, as the GUI has no terminal to show its output; both panes are listed again when it has exited.
    /// </summary>
    private async Task RunCommandLineAsync()
    {
        if ((CommandBox.Text ?? string.Empty).Trim().Length == 0)
        {
            return;
        }

        if (_activePane.NavigationContext.CurrentHostDirectory is not { } directory)
        {
            await ErrorDialog.Notice(CommandLine.NotLocalMessage).ShowDialog(this);
            CommandBox.Focus();
            return;
        }

        _commandLine.Text = CommandBox.Text ?? string.Empty;
        if (_commandLine.TakeCommand() is not { } command)
        {
            return;
        }

        CommandBox.Text = string.Empty;
        _activePane.FocusEntries();
        Process process;
        try
        {
            process = Process.Start(ShellCommand.CreateStartInfo(command, directory)) ??
                      throw new InvalidOperationException($"The command '{command}' couldn't be run");
        }
        catch (Exception exception) when (exception is Win32Exception or IOException or InvalidOperationException)
        {
            await new ErrorDialog(exception.Message).ShowDialog(this);
            _activePane.FocusEntries();
            return;
        }

        using (process)
        {
            await process.WaitForExitAsync();
        }

        await RefreshPanesAsync(LeftPane, RightPane);
    }

    /// <summary>
    ///     Opens PowerShell, a terminal or the command prompt in a pane's directory from its context menu
    ///     (<see cref="ShellHere" />), without waiting for it.
    /// </summary>
    private async Task OpenShellHereAsync(PaneView pane, ShellHereKind kind)
    {
        SetActivePane(pane);
        if (pane.NavigationContext.CurrentHostDirectory is not { } directory)
        {
            return;
        }

        try
        {
            ShellHere.Start(kind, directory);
        }
        catch (Exception exception) when (exception is Win32Exception or IOException or InvalidOperationException)
        {
            await new ErrorDialog(exception.Message).ShowDialog(this);
            pane.FocusEntries();
        }
    }

    private async void OnWindowKeyDown(object? sender, KeyEventArgs e)
    {
        // the quick search of the active pane takes its keys first, other keys stop it and are handled below
        if (IsPaneFocused() && _activePane.HandleQuickSearchKey(e))
        {
            return;
        }

        if (e.Key == Key.Enter && e.KeyModifiers.HasFlag(KeyModifiers.Control) &&
            (IsPaneFocused() || IsCommandLineFocused()))
        {
            e.Handled = true;
            InsertSelectedEntryInCommandLine(e.KeyModifiers.HasFlag(KeyModifiers.Shift), IsCommandLineFocused());
            return;
        }

        if (e.Key == Key.T && e.KeyModifiers == KeyModifiers.Control)
        {
            e.Handled = true;
            FocusCommandLine();
            return;
        }

        if (IsCommandLineFocused() && await HandleCommandLineKeyAsync(e))
        {
            return;
        }

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
            // only in the panes, text boxes keep their own clipboard keys
            case Key.C or Key.X or Key.V when e.KeyModifiers == KeyModifiers.Control && IsPaneFocused():
                e.Handled = true;
                await RunActionAsync(e.Key switch
                {
                    Key.C => CommanderActions.ClipboardCopy,
                    Key.X => CommanderActions.ClipboardCut,
                    _ => CommanderActions.ClipboardPaste
                });
                break;
            // Avalonia opens the context menu when the menu key or Shift+F10 is released (Control.OnKeyUp) and closes
            // it on the next release, so opening it here when pressed made the release close it again; only F10's
            // quitting is stopped
            case Key.F10 when e.KeyModifiers is KeyModifiers.Shift or KeyModifiers.Control:
                e.Handled = true;
                break;
            case Key.F6 when e.KeyModifiers == KeyModifiers.Shift:
                e.Handled = true;
                await RenameAsync(_activePane);
                break;
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
            // plain F1/F2 also when other keys are chosen, like the TUI (see VolumeProviderKeyBindings)
            case Key.F1 when e.KeyModifiers == VolumeProviderModifier || e.KeyModifiers == KeyModifiers.None:
                e.Handled = true;
                await ChooseVolumeProviderAsync(LeftPane);
                break;
            case Key.F2 when e.KeyModifiers == VolumeProviderModifier || e.KeyModifiers == KeyModifiers.None:
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
            case Key.H when e.KeyModifiers == KeyModifiers.Control:
                e.Handled = true;
                await GoHomeAsync(_activePane);
                break;
            case Key.B when e.KeyModifiers == KeyModifiers.Control:
                e.Handled = true;
                await AddBookmarkAsync(_activePane);
                break;
            case Key.S when e.KeyModifiers == KeyModifiers.Control:
                e.Handled = true;
                await SearchAsync(_activePane);
                break;
            case Key.F when e.KeyModifiers == KeyModifiers.Control && IsPaneFocused():
                e.Handled = true;
                _activePane.StartQuickSearch();
                break;
            case Key.R when e.KeyModifiers == KeyModifiers.Control:
                e.Handled = true;
                await RefreshAsync(_activePane);
                break;
            case Key.U when e.KeyModifiers == KeyModifiers.Control:
                e.Handled = true;
                await SwapPanesAsync();
                break;
            case Key.F3:
                e.Handled = true;
                await ViewAsync(_activePane);
                break;
            case Key.F4:
                e.Handled = true;
                await EditAsync(_activePane);
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
            case Key.Q when e.KeyModifiers == KeyModifiers.Control:
                e.Handled = true;
                Close();
                break;
        }
    }

    /// <summary>True when the entries of a pane have the keyboard focus, not e.g. a text box.</summary>
    private bool IsPaneFocused() =>
        (LeftPane.IsKeyboardFocusWithin || RightPane.IsKeyboardFocusWithin) &&
        FocusManager?.GetFocusedElement() is not TextBox;

    /// <summary>
    ///     Confirms quitting (F10, Ctrl+Q, File > Quit, Alt+F4 or closing the window) with <see cref="QuitDialog" />: closing is
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

    private KeyModifiers VolumeProviderModifier => _volumeProviderKeys switch
    {
        VolumeProviderKeys.FunctionKeys => KeyModifiers.None,
        VolumeProviderKeys.ShiftFunctionKeys => KeyModifiers.Shift,
        _ => KeyModifiers.Alt
    };

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

    /// <summary>
    ///     Sets whether both panes detect which listed files can be opened as volumes, listing them again when it
    ///     changes, so the arrows are shown or removed.
    /// </summary>
    private async Task ApplyDetectVolumeFilesAsync(bool detect)
    {
        var changed = LeftPane.NavigationContext.DetectVolumeFiles != detect;
        LeftPane.NavigationContext.DetectVolumeFiles = detect;
        RightPane.NavigationContext.DetectVolumeFiles = detect;
        if (changed)
        {
            await LeftPane.RefreshEntriesAsync();
            await RightPane.RefreshEntriesAsync();
            _activePane.FocusEntries();
        }
    }

    /// <summary>Sets the largest file both panes copy to a temporary file to open it.</summary>
    private void ApplyTemporaryCopyLimit(long limit)
    {
        LeftPane.NavigationContext.TemporaryCopyLimit = limit;
        RightPane.NavigationContext.TemporaryCopyLimit = limit;
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
        var volumeChoice = await ChooseVolumeProviderAsync(this, pane);
        if (volumeChoice != null)
        {
            await ChangeVolumeProviderAsync(pane, volumeChoice);
        }

        pane.FocusEntries();
    }

    /// <summary>Shows a volume provider or opens a bookmark chosen in the volume provider list.</summary>
    private Task ChangeVolumeProviderAsync(PaneView pane, VolumeChoice volumeChoice) =>
        volumeChoice.Bookmark is { } bookmark
            ? OpenBookmarkAsync(pane, bookmark)
            : ChangeVolumeProviderAsync(pane, volumeChoice.VolumeProvider!);

    /// <summary>
    ///     Opens a bookmark in a pane, connecting with the stored credentials and asking for them when needed. A volume
    ///     provider needing administrator rights offers to restart Hst Commander as administrator first, with the pane
    ///     at the bookmark.
    /// </summary>
    private async Task OpenBookmarkAsync(PaneView pane, Bookmark bookmark)
    {
        IVolumeProvider volumeProvider;
        try
        {
            volumeProvider = BookmarkOpener.GetVolumeProvider(bookmark);
        }
        catch (IOException exception)
        {
            await ShowErrorAsync(exception);
            return;
        }

        if (Elevation.NeedsRestart(volumeProvider) &&
            await RestartAsAdministratorAsync(pane, volumeProvider, bookmark.Path))
        {
            return;
        }

        await pane.OpenBookmarkAsync(bookmark,
            request => new CredentialsDialog(request).ShowDialog<VolumeCredentials?>(this));
    }

    /// <summary>
    ///     Bookmarks the location of a pane with the description entered (Ctrl+B). When the location was connected to
    ///     with a user name and password (e.g. SMB), asks whether to store them in the credential store of the
    ///     operating system, or says they can't be stored when there is none. The pane gets the focus.
    /// </summary>
    private async Task AddBookmarkAsync(PaneView pane)
    {
        var context = pane.NavigationContext;
        var description = await new BookmarkDialog("Add bookmark", context.CurrentPath,
            context.GetDefaultBookmarkDescription()).ShowDialog<string?>(this);
        if (description != null)
        {
            await AddBookmarkAsync(context, description);
        }

        pane.FocusEntries();
    }

    private async Task AddBookmarkAsync(NavigationContext context, string description)
    {
        var store = BookmarkStore.Default;
        var bookmark = context.CreateBookmark(description);
        try
        {
            store.Add(bookmark);
        }
        catch (IOException exception)
        {
            await ShowErrorAsync(exception);
            return;
        }

        if (bookmark.UserName == null || context.ConnectedLocation?.Credentials is not { } credentials)
        {
            return;
        }

        if (!store.CredentialStore.IsAvailable)
        {
            await new ConfirmDialog("Bookmark", BookmarkStore.GetUnavailableMessage(), "_OK", null)
                .ShowDialog<bool?>(this);
            return;
        }

        if (await new ConfirmDialog("Bookmark", store.GetStorePasswordQuestion(bookmark), "_Yes", "_No")
                .ShowDialog<bool?>(this) != true)
        {
            return;
        }

        try
        {
            store.StorePassword(bookmark, credentials.Password);
        }
        catch (IOException exception)
        {
            await ShowErrorAsync(exception);
        }
    }

    /// <summary>
    ///     Shows the root of a new volume from a volume provider in a pane. A volume provider needing administrator
    ///     rights offers to restart Hst Commander as administrator first, with the pane showing the volume provider;
    ///     declined or cancelled, the volume provider is shown without them.
    /// </summary>
    private async Task ChangeVolumeProviderAsync(PaneView pane, IVolumeProvider volumeProvider)
    {
        if (Elevation.NeedsRestart(volumeProvider) && await RestartAsAdministratorAsync(pane, volumeProvider))
        {
            return;
        }

        if (volumeProvider is ILocationVolumeProvider locationVolumeProvider)
        {
            await ConnectAsync(pane, locationVolumeProvider);
            return;
        }

        await pane.ChangeVolumeProviderAsync(volumeProvider);
    }

    /// <summary>
    ///     Asks for the location of a volume provider needing one (e.g. an SMB path) and shows it in a pane, asking for
    ///     a user name and password when the location needs them. A volume provider asking for connection details (e.g.
    ///     FTP/SFTP) shows the connection dialog instead. The pane is unchanged when a dialog is cancelled.
    /// </summary>
    private async Task ConnectAsync(PaneView pane, ILocationVolumeProvider volumeProvider)
    {
        string? location;
        VolumeCredentials? credentials = null;
        if (volumeProvider is IConnectionVolumeProvider connectionVolumeProvider)
        {
            // e.g. FTP/SFTP, connected with the credentials entered in the connection dialog first
            var request = await new ConnectionDialog(connectionVolumeProvider).ShowDialog<ConnectionRequest?>(this);
            location = request?.Location;
            credentials = request?.Credentials;
        }
        else
        {
            location = await new VolumeLocationDialog(volumeProvider,
                VolumeConnector.GetLastLocation(volumeProvider)).ShowDialog<string?>(this);
        }

        if (location == null)
        {
            return;
        }

        await pane.ConnectAsync(volumeProvider, location,
            request => new CredentialsDialog(request).ShowDialog<VolumeCredentials?>(this), credentials);
    }

    /// <summary>
    ///     Asks to start Hst Commander as administrator (UAC on Windows, pkexec on Linux, osascript on macOS) with the
    ///     panes where they are and the pane showing the volume provider, and closes the window when started. Returns
    ///     false when it isn't started, e.g. declined, the password dialog cancelled, or on Linux without pkexec, where
    ///     the GUI can't restart itself as root and says so.
    /// </summary>
    private async Task<bool> RestartAsAdministratorAsync(PaneView pane, IVolumeProvider volumeProvider,
        string? path = null)
    {
        if (!Elevation.CanStartAsAdministrator)
        {
            await new ConfirmDialog(Elevation.AdministratorName, Elevation.GetRestartManuallyMessage(volumeProvider),
                "_OK", null).ShowDialog<bool?>(this);
            return false;
        }

        if (await new ConfirmDialog(Elevation.AdministratorName, Elevation.GetRestartQuestion(volumeProvider, false),
                "_Restart", "_Continue").ShowDialog<bool?>(this) != true)
        {
            return false;
        }

        var isRight = pane == RightPane;
        var options = StartupOptions.FromPanes(LeftPane.NavigationContext, RightPane.NavigationContext, isRight,
            isRight, volumeProvider, path);
        try
        {
            if (!await Elevation.StartAsAdministratorAsync(options))
            {
                return false;
            }
        }
        catch (IOException exception)
        {
            await ShowErrorAsync(exception);
            return false;
        }

        _quitConfirmed = true;
        Close();
        return true;
    }

    /// <summary>
    ///     Shows the volume provider list with the bookmarks over a pane, owned by a window, returning the chosen volume
    ///     provider or bookmark, or null.
    /// </summary>
    private Task<VolumeChoice?> ChooseVolumeProviderAsync(Window owner, PaneView pane) =>
        new VolumeProviderDialog(pane == LeftPane ? "Left" : "Right", VolumeProviders.All, BookmarkStore.Default,
            pane).ShowDialog<VolumeChoice?>(owner);

    /// <summary>
    ///     Shows the location dialog of a pane with its current path, changing to the entered path on OK, showing the
    ///     volume provider chosen with Volumes or going to the home directory with Home. The pane gets the focus.
    /// </summary>
    private async Task ShowLocationAsync(PaneView pane)
    {
        var result = await new LocationDialog(pane == LeftPane ? "Left location" : "Right location", pane.NavigationContext.CurrentPath,
            owner => ChooseVolumeProviderAsync(owner, pane)).ShowDialog<LocationDialogResult?>(this);
        if (result?.VolumeChoice is { } volumeChoice)
        {
            await ChangeVolumeProviderAsync(pane, volumeChoice);
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
    ///     Shows the search dialog (Ctrl+S) starting in the location of a pane, with the other options of the last
    ///     search, and goes to the result chosen with Go to, selecting it. The pane gets the focus.
    /// </summary>
    private async Task SearchAsync(PaneView pane)
    {
        var context = pane.NavigationContext;
        var dialog = new SearchDialog(_searchOptions with { StartPath = context.CurrentPath }, _searchSort,
            context.SearchAsync);
        var result = await dialog.ShowDialog<SearchResult?>(this);
        _searchOptions = dialog.Options;
        _searchSort = dialog.Sort;
        if (result != null)
        {
            await pane.GoToSearchResultAsync(result);
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
        catch (OperationCanceledException)
        {
            // cancelled by the user, e.g. making a temporary copy of a file inside an image
            pane.FocusEntries();
            return;
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
    ///     Edits the file under the cursor of a pane (F4) in the editor chosen in the settings: the built-in editor or
    ///     an external editor. Local files are opened in the external editor directly; a file inside an image or another
    ///     volume is edited as a temporary copy while <see cref="ExternalEditDialog" /> is shown and written back when
    ///     the editor exits. Both panes are refreshed after saving, as the other pane may show the same location.
    /// </summary>
    private async Task EditAsync(PaneView pane)
    {
        if (pane.SelectedEntry is not { Type: EntryType.Item } entry)
        {
            return;
        }

        var otherPane = pane == LeftPane ? RightPane : LeftPane;
        var settings = _editorSettings;
        if (settings.Editor == EditorKind.BuiltIn)
        {
            await EditWithBuiltInEditorAsync(pane, otherPane, entry);
            return;
        }

        if (ExternalEditor.Get(settings.Editor, settings.EditorCommand, false) is not { } command)
        {
            await ErrorDialog.Notice(settings.Editor == EditorKind.Custom
                ? "Enter the command of the custom editor in the settings"
                : "No text editor was found, choose another editor in the settings").ShowDialog(this);
            return;
        }

        try
        {
            using var file = await pane.NavigationContext.OpenExternalEditAsync(entry);
            Process? process;
            try
            {
                process = ExternalEditor.Start(command, file.Path);
            }
            catch (Exception exception) when (exception is Win32Exception or IOException)
            {
                throw new IOException($"The editor '{command.FileName}' couldn't be started: {exception.Message}",
                    exception);
            }

            using (process)
            {
                if (!file.IsTemporary)
                {
                    // the panes show the new size and date when the editor exits
                    _ = RefreshWhenExitedAsync(process, pane, otherPane);
                    pane.FocusEntries();
                    return;
                }

                if (await new ExternalEditDialog(entry.Name, command.Name, process).ShowDialog<bool?>(this) == true &&
                    await file.SaveChangesAsync())
                {
                    await pane.RefreshEntriesAsync();
                    await otherPane.RefreshEntriesAsync();
                }
            }
        }
        catch (Exception exception)
        {
            await ShowErrorAsync(exception);
        }

        pane.FocusEntries();
    }

    private async Task EditWithBuiltInEditorAsync(PaneView pane, PaneView otherPane, IEntry entry)
    {
        EditorSession session;
        try
        {
            session = await pane.NavigationContext.OpenEditorAsync(entry);
        }
        catch (Exception exception)
        {
            await ShowErrorAsync(exception);
            pane.FocusEntries();
            return;
        }

        using (session)
        {
            await new EditorDialog(session).ShowDialog(this);
        }

        if (session.HasSaved)
        {
            await pane.RefreshEntriesAsync();
            await otherPane.RefreshEntriesAsync();
        }

        pane.FocusEntries();
    }

    private static async Task RefreshWhenExitedAsync(Process? process, PaneView pane, PaneView otherPane)
    {
        if (process == null)
        {
            return;
        }

        await process.WaitForExitAsync();
        await Dispatcher.UIThread.InvokeAsync(async () =>
        {
            await pane.RefreshEntriesAsync();
            await otherPane.RefreshEntriesAsync();
        });
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
            await ChangePropertiesAsync(pane, properties, checkedFlags, dialog.TabTexts);
        }

        pane.FocusEntries();
    }

    /// <summary>
    ///     Saves the properties dialog: changes the attributes and Amiga protection bits of the entry to its check boxes
    ///     and saves the changed texts of its tabs, refreshing both panes when changed, as the other pane may show the
    ///     same location.
    /// </summary>
    private async Task ChangePropertiesAsync(PaneView pane, EntryProperties properties,
        IReadOnlyList<IReadOnlyList<bool>> checkedFlags, IReadOnlyList<string?>? tabTexts)
    {
        try
        {
            if (!await pane.NavigationContext.ChangePropertiesAsync(properties, checkedFlags, tabTexts))
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
            await ErrorDialog.Notice(message).ShowDialog(this);
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
            await ErrorDialog.Notice(message).ShowDialog(this);
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
    private Task CopyAsync(PaneView pane)
    {
        var otherPane = pane == LeftPane ? RightPane : LeftPane;
        var entries = pane.NavigationContext.GetOperationEntries(pane.SelectedEntry);
        return entries.Count == 0
            ? Task.CompletedTask
            : CopyAsync(pane, otherPane, entries, otherPane.NavigationContext.CurrentPath);
    }

    /// <summary>
    ///     Copies entries of a pane to a destination of the other pane (or the same pane, when dropped on one of its
    ///     directories) after confirming the destination and filter in the copy dialog, showing the progress while
    ///     copying. Both panes are refreshed afterwards.
    /// </summary>
    private async Task CopyAsync(PaneView pane, PaneView otherPane, IReadOnlyList<IEntry> entries,
        string destination)
    {
        var request = await new CopyDialog(NavigationContext.GetCopyDescription(entries), destination)
            .ShowDialog<CopyDialogResult?>(this);
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

        await RefreshPanesAsync(pane, otherPane);
        pane.FocusEntries();
    }

    /// <summary>Lists the entries of a pane and of the other pane again, once when they are the same pane.</summary>
    private static async Task RefreshPanesAsync(PaneView pane, PaneView otherPane)
    {
        await pane.RefreshEntriesAsync();
        if (otherPane != pane)
        {
            await otherPane.RefreshEntriesAsync();
        }
    }

    /// <summary>
    ///     Moves the marked entries, or the entry under the cursor, of a pane to the other pane after confirming the
    ///     destination and filter in the move dialog, showing the progress while moving. A single entry moved to a name
    ///     without path separators is renamed instead and selected.
    /// </summary>
    private Task MoveAsync(PaneView pane)
    {
        var otherPane = pane == LeftPane ? RightPane : LeftPane;
        var entries = pane.NavigationContext.GetOperationEntries(pane.SelectedEntry);
        return entries.Count == 0
            ? Task.CompletedTask
            : MoveAsync(pane, otherPane, entries, otherPane.NavigationContext.CurrentPath);
    }

    /// <summary>
    ///     Moves entries of a pane to a destination of the other pane (or the same pane, when dropped on one of its
    ///     directories) after confirming the destination and filter in the move dialog, like
    ///     <see cref="MoveAsync(PaneView)" />.
    /// </summary>
    private async Task MoveAsync(PaneView pane, PaneView otherPane, IReadOnlyList<IEntry> entries,
        string destination)
    {
        if (pane.NavigationContext.MoveNotSupportedMessage is { } message)
        {
            await ErrorDialog.Notice(message).ShowDialog(this);
            pane.FocusEntries();
            return;
        }

        var request = await new CopyDialog(NavigationContext.GetMoveDescription(entries), destination, "Move")
            .ShowDialog<CopyDialogResult?>(this);
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
            if (otherPane != pane)
            {
                await otherPane.RefreshEntriesAsync();
            }

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

        await RefreshPanesAsync(pane, otherPane);
        pane.FocusEntries();
    }

    /// <summary>
    ///     Copies or moves (Shift) entries dropped on a pane (see <see cref="PaneDropRequest" />) after confirming in the
    ///     copy or move dialog: entries dragged from a pane like F5/F6 to the destination dropped on, and local files
    ///     and directories dragged from another application through <see cref="DroppedEntries" />. The pane dropped on
    ///     gets the focus.
    /// </summary>
    private async Task DropAsync(PaneView targetPane, PaneDropRequest request)
    {
        if (OwnedWindows.Count > 0)
        {
            return;
        }

        Activate();
        if (request is { SourcePane: { } sourcePane, Entries: { } entries })
        {
            await (request.IsMove
                ? MoveAsync(sourcePane, targetPane, entries, request.Destination)
                : CopyAsync(sourcePane, targetPane, entries, request.Destination));
        }
        else if (request.LocalPaths is { } paths)
        {
            await DropLocalPathsAsync(targetPane, paths, request.Destination, request.IsMove);
        }

        SetActivePane(targetPane);
        targetPane.FocusEntries();
    }

    /// <summary>Copies or moves local files and directories dragged from another application to a pane.</summary>
    private async Task DropLocalPathsAsync(PaneView targetPane, IReadOnlyList<string> paths, string destination,
        bool isMove)
    {
        var otherPane = targetPane == LeftPane ? RightPane : LeftPane;
        DroppedEntries dropped;
        try
        {
            dropped = await DroppedEntries.OpenAsync(paths, targetPane.NavigationContext);
        }
        catch (Exception exception)
        {
            await ShowErrorAsync(exception);
            return;
        }

        using (dropped)
        {
            var title = isMove ? "Move" : "Copy";
            var request = await new CopyDialog(isMove ? dropped.MoveDescription : dropped.CopyDescription,
                destination, title).ShowDialog<CopyDialogResult?>(this);
            if (request == null)
            {
                return;
            }

            var progressDialog = new CopyProgressDialog((progress, confirmOverwrite, cancellationToken) => isMove
                ? dropped.MoveAsync(targetPane.NavigationContext, request.Destination, request.Filter, progress,
                    confirmOverwrite, cancellationToken)
                : dropped.CopyAsync(targetPane.NavigationContext, request.Destination, request.Filter, progress,
                    confirmOverwrite, cancellationToken), title);
            await progressDialog.ShowDialog(this);
            if (progressDialog.Exception is { } exception and not OperationCanceledException)
            {
                await ShowErrorAsync(exception);
            }
        }

        // the other pane may show the location dropped in or the one moved from
        await RefreshPanesAsync(targetPane, otherPane);
    }

    /// <summary>
    ///     Applies a theme: its colors, the buttons of the bottom area (the function key bar of the built-in themes) and
    ///     the parent buttons at the outer side of the panes, and the font of the panes and the bottom area: the font of
    ///     the settings (null for none), otherwise the theme's (see <see cref="GuiFonts" />).
    /// </summary>
    private void ApplyAppearance(ThemeChoice theme, string? fontFamily, double? fontSize)
    {
        ThemeManager.Apply(theme);
        var (family, size) = GuiFonts.Resolve(theme, fontFamily, fontSize);
        foreach (var control in new Control[] { LeftPane, RightPane, BottomPanel })
        {
            TextElement.SetFontFamily(control, family);
            TextElement.SetFontSize(control, size);
        }

        BottomPanel.CenterLabels = theme.Plugin != null;
        BottomPanel.Panel = theme.GetBottomPanel();
        LeftPane.ParentButtonSide = theme.ShowParentButtons ? ParentButtonSide.Left : null;
        RightPane.ParentButtonSide = theme.ShowParentButtons ? ParentButtonSide.Right : null;
        LeftPane.HighlightActivePathPanel = theme.HighlightActivePathPanel;
        RightPane.HighlightActivePathPanel = theme.HighlightActivePathPanel;
    }

    /// <summary>Runs a command of the context menu of a pane, which becomes the active pane.</summary>
    private Task RunPaneActionAsync(PaneView pane, string action)
    {
        SetActivePane(pane);
        return RunActionAsync(action);
    }

    /// <summary>
    ///     Renames the entry under the cursor of a pane to the name entered (Shift+F6), see
    ///     <see cref="NavigationContext.RenameAsync" />. The other pane is refreshed too, as it may show the same
    ///     location.
    /// </summary>
    private async Task RenameAsync(PaneView pane)
    {
        var otherPane = pane == LeftPane ? RightPane : LeftPane;
        if (pane.SelectedEntry is not { } entry || entry.Name == Paths.Parent || pane.NavigationContext.IsDriveList)
        {
            return;
        }

        if (pane.NavigationContext.MoveNotSupportedMessage is { } message)
        {
            await ErrorDialog.Notice(message).ShowDialog(this);
            pane.FocusEntries();
            return;
        }

        var newName = await new MakeDirectoryDialog("Rename", $"Rename '{entry.Name}' to:", entry.Name)
            .ShowDialog<string?>(this);
        if (newName != null && newName != entry.Name)
        {
            await pane.RenameAsync(entry, newName);
            await otherPane.RefreshEntriesAsync();
        }

        pane.FocusEntries();
    }

    /// <summary>
    ///     Copies or cuts the marked entries, or the entry under the cursor, of a pane to the clipboard (Ctrl+C,
    ///     Ctrl+X), see <see cref="GuiClipboard" />.
    /// </summary>
    private async Task CopyToClipboardAsync(PaneView pane, bool isCut)
    {
        var entries = pane.NavigationContext.GetOperationEntries(pane.SelectedEntry);
        if (entries.Count == 0 || pane.NavigationContext.IsDriveList)
        {
            return;
        }

        try
        {
            await GuiClipboard.SetAsync(this, pane.NavigationContext, entries, isCut,
                exception => Dispatcher.UIThread.Post(async () => await ShowErrorAsync(exception)),
                clipboard => Dispatcher.UIThread.Post(async () => await ClipboardEntriesMovedAsync(clipboard)));
        }
        catch (Exception exception)
        {
            await ShowErrorAsync(exception);
        }

        pane.FocusEntries();
    }

    /// <summary>
    ///     Cut entries of an image pasted in another application have been deleted: the clipboard is emptied and the
    ///     panes are listed again.
    /// </summary>
    private async Task ClipboardEntriesMovedAsync(EntryClipboard clipboard)
    {
        if (EntryClipboard.Current == clipboard)
        {
            EntryClipboard.Clear();
            if (OperatingSystem.IsWindows())
            {
                WindowsFileClipboard.Clear();
            }
        }

        await RefreshPanesAsync(LeftPane, RightPane);
    }

    /// <summary>
    ///     Pastes the clipboard in the current location of a pane (Ctrl+V): entries copied or cut in the panes, or
    ///     files copied or cut in another application, copied or moved without asking for the destination, showing
    ///     the progress. Pasted cut entries are removed from the clipboard. Both panes are refreshed afterwards.
    /// </summary>
    private async Task PasteAsync(PaneView pane)
    {
        var otherPane = pane == LeftPane ? RightPane : LeftPane;
        if (pane.NavigationContext.IsDriveList)
        {
            return;
        }

        PasteSource? source;
        try
        {
            source = await GuiClipboard.GetPasteSourceAsync(this, pane.NavigationContext);
        }
        catch (Exception exception)
        {
            await ShowErrorAsync(exception);
            pane.FocusEntries();
            return;
        }

        if (source == null)
        {
            pane.FocusEntries();
            return;
        }

        using (source)
        {
            var progressDialog = new CopyProgressDialog((progress, confirmOverwrite, cancellationToken) =>
                source.PasteAsync(pane.NavigationContext, progress, confirmOverwrite, cancellationToken), source.Title);
            await progressDialog.ShowDialog(this);
            switch (progressDialog.Exception)
            {
                case null:
                    if (source.IsMove)
                    {
                        await GuiClipboard.ClearAfterMoveAsync(this, source);
                    }

                    break;
                case OperationCanceledException:
                    break;
                default:
                    await ShowErrorAsync(progressDialog.Exception);
                    break;
            }
        }

        await RefreshPanesAsync(pane, otherPane);
        pane.FocusEntries();
    }

    /// <summary>Runs a command of a button in the bottom area (<see cref="CommanderActions" />) on the active pane.</summary>
    private async Task RunActionAsync(string action)
    {
        var pane = _activePane;
        switch (action)
        {
            case CommanderActions.LeftVolume:
                await ChooseVolumeProviderAsync(LeftPane);
                break;
            case CommanderActions.RightVolume:
                await ChooseVolumeProviderAsync(RightPane);
                break;
            case CommanderActions.View:
                await ViewAsync(pane);
                break;
            case CommanderActions.Edit:
                await EditAsync(pane);
                break;
            case CommanderActions.Copy:
                await CopyAsync(pane);
                break;
            case CommanderActions.Move:
                await MoveAsync(pane);
                break;
            case CommanderActions.MakeDirectory:
                await MakeDirectoryAsync(pane);
                break;
            case CommanderActions.Delete:
                await DeleteAsync(pane);
                break;
            case CommanderActions.CreateVolume:
                await CreateVolumeAsync(pane);
                break;
            case CommanderActions.Properties:
                await ShowPropertiesAsync(pane);
                break;
            case CommanderActions.Rename:
                await RenameAsync(pane);
                break;
            case CommanderActions.ClipboardCopy:
                await CopyToClipboardAsync(pane, false);
                break;
            case CommanderActions.ClipboardCut:
                await CopyToClipboardAsync(pane, true);
                break;
            case CommanderActions.ClipboardPaste:
                await PasteAsync(pane);
                break;
            case CommanderActions.OpenAsVolume:
                await pane.OpenSelectedEntryAsVolumeAsync();
                pane.FocusEntries();
                break;
            case CommanderActions.Parent:
                await pane.GoParentAsync();
                pane.FocusEntries();
                break;
            case CommanderActions.Root:
                await pane.GoRootAsync();
                pane.FocusEntries();
                break;
            case CommanderActions.SelectAll:
                pane.MarkAll();
                pane.FocusEntries();
                break;
            case CommanderActions.SelectNone:
                pane.UnmarkAll();
                pane.FocusEntries();
                break;
            case CommanderActions.SelectGroup:
                await SelectGroupAsync(pane, true);
                break;
            case CommanderActions.UnselectGroup:
                await SelectGroupAsync(pane, false);
                break;
            case CommanderActions.InvertSelection:
                InvertSelection(pane);
                break;
            case CommanderActions.Refresh:
                await RefreshAsync(pane);
                break;
            case CommanderActions.SwapPanes:
                await SwapPanesAsync();
                break;
            case CommanderActions.Location:
                await ShowLocationAsync(pane);
                break;
            case CommanderActions.AddBookmark:
                await AddBookmarkAsync(pane);
                break;
            case CommanderActions.Search:
                await SearchAsync(pane);
                break;
            case CommanderActions.QuickSearch:
                pane.StartQuickSearch();
                break;
            case CommanderActions.Settings:
                await ShowSettingsAsync();
                break;
            case CommanderActions.Quit:
                Close();
                break;
        }
    }

    private async Task ShowSettingsAsync()
    {
        var dialog = new SettingsDialog(GuiSettings.Load(), ApplyAppearance);
        var settings = await dialog.ShowDialog<GuiSettings?>(this);
        if (settings == null)
        {
            return;
        }

        if (dialog.ChangedButtons is var (buttonPanel, layout))
        {
            try
            {
                buttonPanel.SaveLayout(layout);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                await ShowErrorAsync(new IOException($"Failed to save the buttons: {exception.Message}", exception));
            }
        }

        ApplyAppearance(Themes.Find(settings.Theme), settings.FontFamily, settings.FontSize);
        ApplyVolumeProviderKeys(settings.EffectiveVolumeProviderKeys);
        ApplyEnterFileAction(settings.EnterFileAction);
        await ApplyDetectVolumeFilesAsync(settings.DetectVolumeFiles);
        ApplyUaeMetadata(settings.UaeMetadata);
        ApplyTemporaryCopyLimit(settings.TemporaryCopyLimitBytes);
        _editorSettings = settings;
        try
        {
            settings.Save();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            await ShowErrorAsync(new IOException($"Failed to save settings to '{GuiSettings.FilePath}': {exception.Message}", exception));
        }
    }

    private Task ShowErrorAsync(Exception exception) => new ErrorDialog(exception.Message, exception).ShowDialog(this);
}
