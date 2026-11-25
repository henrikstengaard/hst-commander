using System.Data;
using Hst.Commander.Core;
using Hst.Commander.Core.Volumes;
using Hst.Commander.Abstractions;
using TuiAttribute = Terminal.Gui.Drawing.Attribute;

namespace Hst.Commander.Tui;

public sealed class MainWindow : Window
{
    private const int SizeColumnWidth = 8;
    private const int ModifiedColumnWidth = 16;
    private const int ColumnCount = 3;

    private readonly MenuBar _menuBar;
    private readonly Label? _administratorLabel;
    private readonly ButtonPanelView _bottomPanel;
    private readonly CommandLine _commandLine = new();
    private readonly Label _commandPrompt;
    private readonly TextField _commandField;
    private Pane _leftPane;
    private Pane _rightPane;
    private readonly MenuItem _leftVolumeMenuItem;
    private readonly MenuItem _rightVolumeMenuItem;
    private PopoverMenu? _entryMenu;
    private VolumeProviderKeys _volumeProviderKeys;
    private TuiPalette _palette;
    private Pane _activePane;
    private TerminalAction? _pendingTerminalAction;
    private string? _pendingError;
    private bool _restarting;
    private string _selectGroupFilter = SelectGroupOptions.Default.Filter;
    private SearchOptions _searchOptions = SearchOptions.Default;
    private SearchResultSort _searchSort = SearchResultSort.Default;
    private TuiSettings _editorSettings;
    private ExternalEditFile? _pendingExternalEdit;

    public MainWindow(string initialPath, TuiSettings settings)
    {
        var theme = Themes.Find(settings.Theme);
        BorderStyle = LineStyle.None;
        X = 0;
        Y = 0;
        Width = Dim.Fill();
        Height = Dim.Fill();
        _palette = TuiPalette.For(theme);

        // menus get their border when created
        MenuSettings.Current = MenuSettings.Current with { DefaultBorderStyle = LineStyle.Single };
        _leftVolumeMenuItem = new MenuItem("_Left volume...", string.Empty, ChooseLeftVolumeProvider);
        _rightVolumeMenuItem = new MenuItem("_Right volume...", string.Empty, ChooseRightVolumeProvider);
        _menuBar = new MenuBar
        {
            Menus =
            [
                new MenuBarItem("_File", (MenuItem[])
                [
                    new("Vie_w...", "F3", ViewInActivePane),
                    new("_Edit...", "F4", EditInActivePane),
                    new("_Copy...", "F5", CopyFromActivePane),
                    new("Mo_ve/rename...", "F6", MoveFromActivePane),
                    new("_Make directory...", "F7", MakeDirectoryInActivePane),
                    new("_Delete...", "F8", DeleteFromActivePane),
                    new("Create vo_lume...", "F9", CreateVolumeInActivePane),
                    new("_Properties...", "Ctrl+P", ShowPropertiesOfActivePane),
                    new("Ope_n as volume", "Ctrl+PgDn", OpenAsVolumeInActivePane),
                    null!,
                    // hidden when already running as administrator or root
                    ..(Elevation.IsElevated
                        ? []
                        : new MenuItem[] { new(Elevation.RestartMenuText, string.Empty, RestartElevated) }),
                    new("_Settings...", string.Empty, ShowSettings),
                    null!,
                    new("_Quit", "F10/Ctrl+Q", Quit)
                ]),
                new MenuBarItem("_Mark", new MenuItem[]
                {
                    new("Select _all", "Ctrl+A", SelectAllInActivePane),
                    new("Select _group...", "+", SelectGroupInActivePane),
                    new("_Unselect group...", "-", UnselectGroupInActivePane),
                    new("_Invert selection", "*", InvertSelectionInActivePane)
                }),
                new MenuBarItem("_Go", new MenuItem[]
                {
                    _leftVolumeMenuItem,
                    _rightVolumeMenuItem,
                    new("L_ocation...", "Ctrl+L", ShowLocationOfActivePane),
                    new("_Home directory", "Ctrl+H", GoHomeInActivePane),
                    new("Add _bookmark...", "Ctrl+B", AddBookmarkInActivePane),
                    null!,
                    new("_Same location in other pane", "Ctrl+D", ShowSameLocationInOtherPane),
                    new("Sw_ap panes", "Ctrl+U", SwapPanes),
                    new("Re_fresh", "Ctrl+R", RefreshActivePane)
                }),
                new MenuBarItem("_Commands", new MenuItem[]
                {
                    new("_Search...", "Ctrl+S", SearchInActivePane),
                    new("_Quick search", "Ctrl+F", QuickSearchInActivePane),
                    new("Command _line", "Ctrl+T", FocusCommandLine)
                }),
                new MenuBarItem("_Help", new MenuItem[]
                {
                    new("_About...", string.Empty, ShowAbout)
                })
            ]
        };
        // the bottom area with the buttons of the theme, e.g. the function key bar; its height is set by ApplyTheme
        _bottomPanel = new ButtonPanelView
        {
            X = 0,
            Y = Pos.AnchorEnd(1),
            Width = Dim.Fill(),
            Height = 1
        };
        _bottomPanel.ActionInvoked += (_, action) => RunAction(action);
        // the command line above the bottom area, like Midnight Commander's; its row is set by ApplyTheme. The prompt
        // doesn't show the directory, which the path panel of the active pane shows, to leave room for the command
        _commandPrompt = new Label { X = 0, Y = Pos.AnchorEnd(2), Width = 2, Height = 1, Text = "> " };
        _commandField = new TextField { X = Pos.Right(_commandPrompt), Y = Pos.AnchorEnd(2), Width = Dim.Fill() };
        _commandField.KeyDown += OnCommandFieldKeyDown;
        // the panes handle f9 (create volume) and f10 (quit) before the menu bar's key, so alt+f opens the menu
        IsRunningChanging += (_, e) => e.Cancel = !e.NewValue && !ConfirmClosing();
        _leftPane = CreatePane(new NavigationContext(initialPath, StartFile), true);
        _rightPane = CreatePane(new NavigationContext(initialPath, StartFile), false);
        _activePane = _leftPane;
        _leftPane.NavigationContext.Sort = settings.LeftSort ?? EntrySort.Default;
        _rightPane.NavigationContext.Sort = settings.RightSort ?? EntrySort.Default;

        Add(_menuBar, _leftPane.View, _rightPane.View, _commandPrompt, _commandField, _bottomPanel);

        // running as administrator or root is shown at the right of the menu bar
        if (Elevation.IsElevated)
        {
            var text = $" {Elevation.AdministratorName} ";
            _menuBar.Width = Dim.Fill(text.Length);
            _administratorLabel = new Label { X = Pos.AnchorEnd(text.Length), Y = 0, Text = text };
            Add(_administratorLabel);
        }

        ApplyTheme(theme);
        // Application.Init reloads Terminal.Gui's configuration, resetting its color schemes, and runs again after a
        // program has run in the terminal
        Application.InitializedChanged += (_, args) =>
        {
            if (args.Value)
            {
                ApplyMessageBoxColors();
            }
        };
        ApplyVolumeProviderKeys(settings.EffectiveVolumeProviderKeys);
        ApplyEnterFileAction(settings.EnterFileAction);
        _leftPane.NavigationContext.DetectVolumeFiles = settings.DetectVolumeFiles;
        _rightPane.NavigationContext.DetectVolumeFiles = settings.DetectVolumeFiles;
        ApplyUaeMetadata(settings.UaeMetadata);
        ApplyTemporaryCopyLimit(settings.TemporaryCopyLimitBytes);
        _editorSettings = settings;
        ConfigurePaneKeys(_leftPane, _rightPane);
        ConfigurePaneKeys(_rightPane, _leftPane);
        // also when run again after a program has run in the terminal. The panes are listed once the window runs:
        // UiThread.Attach installs the main loop's synchronization context, so awaiting a listing that isn't done at
        // once (e.g. the physical disks) before Application.Run would wait for a main loop that isn't running yet
        // cut files pasted in another application (e.g. Explorer) are moved by it, so the panes are listed again;
        // checked on the main loop, also after the window runs again when a program has run in the terminal (skipped
        // while it runs)
        _cutMovedTimer = new Timer(_ => UiThread.PostIfRunning(() =>
        {
            if (EntryClipboard.CheckCutMoved())
            {
                _ = InitializeAsync();
            }
        }), null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
        IsRunningChanged += async (_, args) =>
        {
            if (!args.Value)
            {
                return;
            }

            _activePane.TableView.SetFocus();
            ShowPendingError();
            ShowPendingCrashReport();
            if (!_loadedOnce)
            {
                _loadedOnce = true;
                if (CrashHandler.TestCrash == "ui")
                {
                    UiThread.Post(() => throw CrashHandler.CreateTestUiException());
                }
            }

            await SavePendingExternalEditAsync();
            await InitializeAsync();
        };
    }

    /// <summary>Timer checking whether cut files were moved by another application, kept from being collected.</summary>
    private readonly Timer _cutMovedTimer;

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

        await InitializeAsync();
    }

    /// <summary>Shows an error when the window is run again, e.g. when a program couldn't be started.</summary>
    public void ShowErrorWhenLoaded(string message) => _pendingError = message;

    private void ShowPendingError()
    {
        if (_pendingError is not { } message)
        {
            return;
        }

        _pendingError = null;
        UiThread.Post(() => ErrorBox.Show(message));
    }

    /// <summary>Report of the crash of the last run (<see cref="CrashMarker" />), offered to be reported when loaded.</summary>
    private string? _pendingCrashReport;

    /// <summary>True while an <see cref="UnexpectedErrorDialog" /> is shown, so further exceptions are only logged.</summary>
    private bool _showingUnexpectedError;

    /// <summary>True once the window was loaded the first time, to throw the test exception only once.</summary>
    private bool _loadedOnce;

    /// <summary>Offers to report the crash of the last run when the window is loaded.</summary>
    public void ShowLastCrashWhenLoaded(string crashReport) => _pendingCrashReport = crashReport;

    private void ShowPendingCrashReport()
    {
        if (_pendingCrashReport is not { } crashReport)
        {
            return;
        }

        _pendingCrashReport = null;
        UiThread.Post(() => ShowUnexpectedError(UnexpectedErrorDialog.ForLastCrash(crashReport, _palette)));
    }

    /// <summary>
    ///     Shows an unhandled exception caught by the main loop, already logged, unless one is shown already. Called
    ///     through <see cref="UiThread.Post" />, not from the error handler of the main loop.
    /// </summary>
    public void ShowUnexpectedError(Exception exception) =>
        ShowUnexpectedError(UnexpectedErrorDialog.ForException(exception, _palette));

    private void ShowUnexpectedError(UnexpectedErrorDialog dialog)
    {
        if (_showingUnexpectedError || !Application.Initialized)
        {
            dialog.Dispose();
            return;
        }

        _showingUnexpectedError = true;
        try
        {
            Application.Run(dialog);
        }
        catch (Exception exception)
        {
            ErrorLog.Unhandled(exception, "showing an unexpected error");
        }
        finally
        {
            dialog.Dispose();
            _showingUnexpectedError = false;
        }
    }

    /// <summary>
    ///     Returns and clears what to do in the terminal, which stopped the window so it can be done while the TUI is
    ///     shut down: run a program opened in a pane or show the output screen. Null when the window was stopped to quit.
    /// </summary>
    public TerminalAction? TakePendingTerminalAction()
    {
        var action = _pendingTerminalAction;
        _pendingTerminalAction = null;
        return action;
    }

    /// <summary>Quits the application (F10, Ctrl+Q, File > Quit), after confirming when the window is closing.</summary>
    private void Quit() => Application.RequestStop(this);

    /// <summary>
    ///     Confirms quitting when the window is stopped by F10, Ctrl+Q, File > Quit or Esc (<see cref="Application.QuitKey" />);
    ///     stopping it for a terminal action isn't quitting. Esc in the confirm dialog answers no.
    /// </summary>
    private bool ConfirmClosing() => _pendingTerminalAction != null || _restarting ||
                                     TuiMessageBox.Query("Quit", AppInfo.Current.QuitQuestion, "_Yes", "_No") == 0;

    /// <summary>Stops the window, so the action is done in the terminal while the TUI is shut down.</summary>
    private void RequestTerminalAction(TerminalAction action)
    {
        _pendingTerminalAction = action;
        Application.RequestStop(this);
    }

    /// <summary>
    ///     Starts a local file opened in a pane. Programs running in the terminal stop the window, like Midnight
    ///     Commander suspends its UI, other files are started with the shell.
    /// </summary>
    private void StartFile(string path)
    {
        if (!FileStarter.IsTerminalProgram(path))
        {
            FileStarter.StartWithShell(path);
            return;
        }

        RequestTerminalAction(new TerminalAction.RunProgram(path));
    }

    /// <summary>
    ///     Starts the panes where the startup options say, e.g. where they were before restarting as administrator, and
    ///     makes the pane active. Called once before the window is first run.
    /// </summary>
    public async Task ApplyStartupOptionsAsync(StartupOptions options)
    {
        await options.Left.ApplyAsync(_leftPane.NavigationContext);
        await options.Right.ApplyAsync(_rightPane.NavigationContext);
        _activePane = options.IsRightActive ? _rightPane : _leftPane;
    }

    /// <summary>
    ///     Lists both panes at the same time and draws each when listed, so a pane listing slowly, e.g. a sleeping
    ///     physical disk waking up, doesn't keep the other blank.
    /// </summary>
    private Task InitializeAsync() =>
        Task.WhenAll(InitializeAndDrawPaneAsync(_leftPane), InitializeAndDrawPaneAsync(_rightPane));

    private static async Task InitializeAndDrawPaneAsync(Pane pane)
    {
        await InitializePaneAsync(pane);

        // the window was drawn before the pane was listed and isn't drawn again by itself
        Application.LayoutAndDraw(true);
    }

    /// <summary>True when the window was stopped to restart Hst Commander as administrator.</summary>
    public bool IsRestarting => _restarting;

    /// <summary>
    ///     Saves where the panes are in the settings, so they start there next time (see
    ///     <see cref="StartupOptions.WithSavedPanes" />). Called when quitting; a failure isn't shown, as the TUI has
    ///     been shut down.
    /// </summary>
    public void SavePaneLocations()
    {
        try
        {
            (TuiSettings.Load() with
            {
                Panes = PaneLocations.From(_leftPane.NavigationContext, _rightPane.NavigationContext,
                    _activePane == _rightPane)
            }).Save();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            ErrorLog.Error("Failed to save the pane locations", exception);
        }
    }

    public void DisposeNavigationContexts()
    {
        _leftPane.VolumeDetection?.Cancel();
        _rightPane.VolumeDetection?.Cancel();
        _leftPane.NavigationContext.Dispose();
        _rightPane.NavigationContext.Dispose();
    }

    /// <summary>
    ///     Applies a theme: its colors to the menu bar, both panes and the bottom area, the buttons of the bottom area
    ///     (the function key bar of the built-in themes) and the parent buttons at the outer side of the panes.
    /// </summary>
    public void ApplyTheme(ThemeChoice theme)
    {
        _palette = TuiPalette.For(theme);
        var bottomPanel = theme.GetBottomPanel();
        var bottomHeight = Math.Max(1, bottomPanel.Rows);
        _bottomPanel.Panel = bottomPanel;
        _bottomPanel.CenterLabels = theme.CenterButtonLabels;
        _bottomPanel.Height = bottomHeight;
        _bottomPanel.Y = Pos.AnchorEnd(bottomHeight);
        _commandPrompt.Y = Pos.AnchorEnd(bottomHeight + 1);
        _commandField.Y = Pos.AnchorEnd(bottomHeight + 1);
        foreach (var pane in new[] { _leftPane, _rightPane })
        {
            pane.View.Height = Dim.Fill(bottomHeight + 1);
            ShowParentStrip(pane, theme.ShowParentButtons);
            pane.PathPanel.HighlightWhenActive = theme.HighlightActivePathPanel;
        }

        _menuBar.SetScheme(_palette.MenuColorScheme);
        _menuBar.SetNeedsDraw();
        if (_administratorLabel != null)
        {
            _administratorLabel.SetScheme(_palette.AdministratorColorScheme);
        }

        _bottomPanel.KeyAttribute = _palette.ButtonKey;
        _bottomPanel.TitleAttribute = _palette.ButtonTitle;
        _bottomPanel.SetNeedsDraw();
        var commandAttribute = new TuiAttribute(_palette.Item, _palette.PaneBackground);
        _commandPrompt.SetScheme(CreateColorScheme(_palette.PaneNormal, _palette.PaneNormal));
        _commandField.SetScheme(TuiPalette.CreateScheme(commandAttribute, commandAttribute));
        foreach (var pane in new[] { _leftPane, _rightPane })
        {
            ApplyPaneColors(pane);
        }

        ApplyMessageBoxColors();
        SetNeedsDraw();
    }

    /// <summary>
    ///     Message boxes (<see cref="TuiMessageBox" />) use Terminal.Gui's "Dialog" and "Error" color schemes, whose
    ///     focused buttons barely differ from the others (dark gray instead of black text), so they get the theme's
    ///     dialog colors like the other dialogs. The menus of the menu bar and the context menu of the entries are
    ///     popovers with Terminal.Gui's "Menu" scheme, so it gets the theme's menu colors, and a border.
    /// </summary>
    private void ApplyMessageBoxColors()
    {
        SchemeManager.AddScheme("Dialog", _palette.DialogColorScheme);
        SchemeManager.AddScheme("Error", _palette.ErrorColorScheme);
        SchemeManager.AddScheme("Menu", _palette.MenuColorScheme);
        MenuSettings.Current = MenuSettings.Current with { DefaultBorderStyle = LineStyle.Single };
    }

    /// <summary>Sets the keys opening the volume provider lists, shown in the File menu.</summary>
    private void ApplyVolumeProviderKeys(VolumeProviderKeys keys)
    {
        _volumeProviderKeys = keys;
        _leftVolumeMenuItem.HelpText = VolumeProviderKeyBindings.GetLeftKeyText(keys);
        _rightVolumeMenuItem.HelpText = VolumeProviderKeyBindings.GetRightKeyText(keys);
    }

    /// <summary>Sets what Enter does with files in both panes.</summary>
    private void ApplyEnterFileAction(EnterFileAction action)
    {
        _leftPane.NavigationContext.EnterFileAction = action;
        _rightPane.NavigationContext.EnterFileAction = action;
    }

    /// <summary>
    ///     Sets whether both panes detect which listed files can be opened as volumes, listing them again when it
    ///     changes, so the markers are shown or removed.
    /// </summary>
    private void ApplyDetectVolumeFiles(bool detect)
    {
        var changed = _leftPane.NavigationContext.DetectVolumeFiles != detect;
        _leftPane.NavigationContext.DetectVolumeFiles = detect;
        _rightPane.NavigationContext.DetectVolumeFiles = detect;
        if (changed)
        {
            _ = RunNavigationAsync(_leftPane, () => Task.CompletedTask);
            _ = RunNavigationAsync(_rightPane, () => Task.CompletedTask);
        }
    }

    /// <summary>Sets how both panes store Amiga names, protection bits and comments when copying from Amiga volumes.</summary>
    private void ApplyUaeMetadata(UaeMetadataFormat format)
    {
        _leftPane.NavigationContext.UaeMetadata = format;
        _rightPane.NavigationContext.UaeMetadata = format;
    }

    /// <summary>Sets the largest file both panes copy to a temporary file to open it.</summary>
    private void ApplyTemporaryCopyLimit(long limit)
    {
        _leftPane.NavigationContext.TemporaryCopyLimit = limit;
        _rightPane.NavigationContext.TemporaryCopyLimit = limit;
    }

    private Key LeftVolumeProviderKey => WithVolumeProviderModifier(Key.F1);

    private Key RightVolumeProviderKey => WithVolumeProviderModifier(Key.F2);

    private Key WithVolumeProviderModifier(Key key) => _volumeProviderKeys switch
    {
        VolumeProviderKeys.FunctionKeys => key,
        VolumeProviderKeys.ShiftFunctionKeys => key.WithShift,
        _ => key.WithAlt
    };

    private void ApplyPaneColors(Pane pane)
    {
        var normal = _palette.PaneNormal;
        pane.PathPanel.BorderAttribute = normal;
        pane.PathPanel.PathAttribute = new TuiAttribute(_palette.Path, _palette.PaneBackground);
        pane.PathPanel.ActivePathAttribute = _palette.ActivePath;
        pane.PathPanel.SetNeedsDraw();
        pane.TableView.SetScheme(CreateColorScheme(
            normal,
            _palette.Cursor,
            new TuiAttribute(_palette.Header, _palette.PaneBackground),
            _palette.Cursor));
        // the headers are drawn in the pane's normal color, also while the pane has focus (Terminal.Gui v2 draws them
        // in the focus color otherwise)
        pane.TableView.Style.HeaderScheme = CreateColorScheme(normal, normal);
        pane.TableView.SetNeedsDraw();
        pane.BottomLine.SetScheme(CreateColorScheme(normal, normal));
        pane.BottomLine.SetNeedsDraw();
        pane.QuickSearchLabel.SetScheme(CreateColorScheme(_palette.ActivePath, _palette.ActivePath));
        pane.QuickSearchLabel.SetNeedsDraw();
        pane.ParentStrip.Attribute = _palette.ButtonTitle;
        pane.ParentStrip.SetNeedsDraw();
    }

    /// <summary>
    ///     Shows or hides the parent button of a pane, one column at its outer side: left of the left pane and right of
    ///     the right pane. The rest of the pane is moved next to it.
    /// </summary>
    private static void ShowParentStrip(Pane pane, bool show)
    {
        pane.ParentStrip.Visible = show;
        var x = show && pane.IsLeft ? 1 : 0;
        var width = show ? Dim.Fill(1) : Dim.Fill();
        foreach (var view in new View[] { pane.PathPanel, pane.TableView, pane.BottomLine })
        {
            view.X = x;
            view.Width = width;
        }

        pane.View.SetNeedsLayout();
        pane.View.SetNeedsDraw();
    }

    /// <summary>Runs a command of a button in the bottom area (<see cref="CommanderActions" />) on the active pane.</summary>
    private void RunAction(string action)
    {
        var pane = _activePane;
        switch (action)
        {
            case CommanderActions.LeftVolume:
                ChooseLeftVolumeProvider();
                break;
            case CommanderActions.RightVolume:
                ChooseRightVolumeProvider();
                break;
            case CommanderActions.View:
                ViewInActivePane();
                break;
            case CommanderActions.Edit:
                EditInActivePane();
                break;
            case CommanderActions.Copy:
                CopyFromActivePane();
                break;
            case CommanderActions.Move:
                MoveFromActivePane();
                break;
            case CommanderActions.MakeDirectory:
                MakeDirectoryInActivePane();
                break;
            case CommanderActions.Delete:
                DeleteFromActivePane();
                break;
            case CommanderActions.CreateVolume:
                CreateVolumeInActivePane();
                break;
            case CommanderActions.Properties:
                ShowPropertiesOfActivePane();
                break;
            case CommanderActions.Rename:
                _ = RenameAsync(pane, GetOtherPane(pane));
                break;
            case CommanderActions.ClipboardCopy:
                _ = CopyToClipboardAsync(pane, false);
                break;
            case CommanderActions.ClipboardCut:
                _ = CopyToClipboardAsync(pane, true);
                break;
            case CommanderActions.ClipboardPaste:
                _ = PasteAsync(pane, GetOtherPane(pane));
                break;
            case CommanderActions.OpenAsVolume:
                OpenAsVolumeInActivePane();
                break;
            case CommanderActions.Parent:
                _ = RunNavigationAsync(pane, () => pane.NavigationContext.GoParentAsync());
                break;
            case CommanderActions.Root:
                _ = RunNavigationAsync(pane, () => pane.NavigationContext.GoRootAsync());
                break;
            case CommanderActions.SelectAll:
                SelectAll(pane);
                break;
            case CommanderActions.SelectNone:
                pane.NavigationContext.ClearMarks();
                UpdateMarks(pane);
                break;
            case CommanderActions.SelectGroup:
                SelectGroupInActivePane();
                break;
            case CommanderActions.UnselectGroup:
                UnselectGroupInActivePane();
                break;
            case CommanderActions.InvertSelection:
                InvertSelectionInActivePane();
                break;
            case CommanderActions.Refresh:
                RefreshActivePane();
                break;
            case CommanderActions.SwapPanes:
                SwapPanes();
                break;
            case CommanderActions.SameLocation:
                ShowSameLocationInOtherPane();
                break;
            case CommanderActions.Location:
                ShowLocationOfActivePane();
                break;
            case CommanderActions.AddBookmark:
                AddBookmarkInActivePane();
                break;
            case CommanderActions.Search:
                SearchInActivePane();
                break;
            case CommanderActions.QuickSearch:
                QuickSearchInActivePane();
                break;
            case CommanderActions.Settings:
                ShowSettings();
                break;
            case CommanderActions.Quit:
                Quit();
                break;
        }
    }

    /// <summary>Shows the About dialog with the version, author and the license notices.</summary>
    private void ShowAbout()
    {
        var dialog = new AboutDialog(_palette);
        Application.Run(dialog);
        dialog.Dispose();
    }

    private void ShowSettings()
    {
        var settings = TuiSettings.Load();
        var dialog = new SettingsDialog(settings, ApplyTheme, _palette);
        Application.Run(dialog);
        var result = dialog.Result;
        var changedButtons = dialog.ChangedButtons;
        dialog.Dispose();

        if (result == null)
        {
            ApplyTheme(Themes.Find(settings.Theme));
            return;
        }

        if (changedButtons is var (buttonPanel, layout))
        {
            try
            {
                buttonPanel.SaveLayout(layout);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                ErrorBox.Show($"Failed to save the buttons: {exception.Message}", exception);
            }
        }

        ApplyTheme(Themes.Find(result.Theme));
        ApplyVolumeProviderKeys(result.EffectiveVolumeProviderKeys);
        ApplyEnterFileAction(result.EnterFileAction);
        ApplyDetectVolumeFiles(result.DetectVolumeFiles);
        ApplyUaeMetadata(result.UaeMetadata);
        ApplyTemporaryCopyLimit(result.TemporaryCopyLimitBytes);
        _editorSettings = result;
        try
        {
            result.Save();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            ErrorBox.Show($"Failed to save settings to '{TuiSettings.FilePath}': {exception.Message}", exception);
        }
    }

    private Pane CreatePane(NavigationContext navigationContext, bool isLeft)
    {
        var view = new View
        {
            Y = 1,
            Height = Dim.Fill(1),
            CanFocus = true
        };
        PlacePaneView(view, isLeft);
        var pathPanel = new PathPanelView
        {
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Height = PathPanelView.RequiredHeight,
            Path = navigationContext.CurrentPath
        };
        var tableView = new EntryTableView
        {
            X = 0,
            Y = PathPanelView.RequiredHeight,
            Width = Dim.Fill(),
            Height = Dim.Fill(1),
            FullRowSelect = true,
            MaxCellWidth = int.MaxValue,
            // typing letters doesn't jump to entries, the names start with a marker; Ctrl+F starts a quick search
            CollectionNavigator = null
        };

        // a fixed height: an automatic height wraps the border made for a wider pane (set while the pane grows, before
        // the label is wider) to two rows, leaving an empty row below the pane
        var bottomLine = new Label
        {
            X = 0,
            Y = Pos.AnchorEnd(),
            Width = Dim.Fill(),
            Height = 1
        };

        // the quick search text (Ctrl+F) on the bottom border, like Midnight Commander
        var quickSearchLabel = new Label
        {
            X = Pos.Left(bottomLine) + 1,
            Y = Pos.AnchorEnd(),
            Width = Dim.Auto(),
            Height = 1,
            Visible = false
        };

        // the parent button at the outer side of the pane, shown by themes of plugins (see ShowParentStrip)
        var parentStrip = new ParentStripView
        {
            X = isLeft ? 0 : Pos.AnchorEnd(1),
            Y = 0,
            Width = 1,
            Height = Dim.Fill(),
            Visible = false
        };

        var progress = new ListingProgressView();
        var pane = new Pane(navigationContext, view, pathPanel, tableView, bottomLine, progress, parentStrip, isLeft)
        {
            QuickSearchLabel = quickSearchLabel
        };
        parentStrip.Clicked += (_, _) =>
        {
            pane.TableView.SetFocus();
            _ = RunNavigationAsync(pane, () => pane.NavigationContext.GoParentAsync());
        };
        progress.FocusOnHide = tableView;
        navigationContext.ProgressIndicator = progress;
        tableView.Style = new TableStyle
        {
            ShowVerticalCellLines = true,
            ShowHorizontalHeaderOverline = false,
            AlwaysShowHeaders = true,
            ExpandLastColumn = false,
            RowColorGetter = args => GetRowColorScheme(pane, args.RowIndex)
        };
        tableView.FrameChanged += (_, _) => UpdatePaneColumns(pane);

        view.Add(pathPanel, tableView, bottomLine, quickSearchLabel, parentStrip, progress);

        return pane;
    }

    /// <summary>
    ///     Places a pane's view on the left or right half of the window. The left pane is half the width rounded down
    ///     and the right pane fills the rest, so with an odd width no column is left empty at the right edge.
    /// </summary>
    private static void PlacePaneView(View view, bool left)
    {
        view.X = left ? 0 : Pos.Percent(50);
        view.Width = left ? Dim.Percent(50) : Dim.Fill();
    }

    private void ConfigurePaneKeys(Pane pane, Pane otherPane)
    {
        pane.TableView.CellActivated += async (_, _) => await ActivateSelectedEntryAsync(pane);
        pane.PathPanel.PathClicked += async (_, _) => await ShowLocationAsync(pane);
        pane.TableView.HasFocusChanged += (_, _) =>
        {
            if (pane.TableView.HasFocus)
            {
                _activePane = pane;
            }
            else
            {
                StopQuickSearch(pane);
            }

            pane.TableView.SetNeedsDraw();
            pane.PathPanel.IsActive = pane.TableView.HasFocus;
            pane.PathPanel.SetNeedsDraw();
        };
        pane.TableView.SelectedCellChanged += (_, _) => pane.TableView.SetNeedsDraw();
        pane.TableView.RightClicked += (_, click) => ShowEntryMenu(pane, click.Row, click.ScreenPosition);
        pane.TableView.HeaderClicked += (_, column) => SortPane(pane, column);
        pane.TableView.MousePressed += (_, _) => StopQuickSearch(pane);
        pane.TableView.KeyDown += (_, key) =>
        {
            if (pane.QuickSearch.IsActive && HandleQuickSearchKey(pane, key))
            {
                key.Handled = true;
            }
            else if (key.KeyCode == KeyCode.Tab)
            {
                otherPane.TableView.SetFocus();
                key.Handled = true;
            }
            else if (key.KeyCode == KeyCode.CursorUp || key.KeyCode == KeyCode.CursorDown)
            {
                MoveSelection(pane, key.KeyCode == KeyCode.CursorUp ? -1 : 1);
                key.Handled = true;
            }
            else if (key == Key.CursorUp.WithShift || key == Key.CursorDown.WithShift)
            {
                // Key equality includes Handled, so the key is compared before it's handled
                var offset = key == Key.CursorUp.WithShift ? -1 : 1;
                key.Handled = true;
                ToggleMarkAndMove(pane, offset);
            }
            else if (key == Key.PageDown.WithCtrl)
            {
                key.Handled = true;
                _ = OpenSelectedEntryAsVolumeAsync(pane);
            }
            else if (key.KeyCode == KeyCode.PageUp || key.KeyCode == KeyCode.PageDown)
            {
                MoveSelection(pane, GetPageSize(pane) * (key.KeyCode == KeyCode.PageUp ? -1 : 1));
                key.Handled = true;
            }
            else if (key.KeyCode == KeyCode.Home || key.KeyCode == KeyCode.End)
            {
                MoveSelection(pane, key.KeyCode == KeyCode.Home ? 0 : pane.Entries.Count - 1,
                    key.KeyCode == KeyCode.Home || key.KeyCode == KeyCode.End);
                key.Handled = true;
            }
            else if (key.KeyCode == KeyCode.Insert || key.KeyCode == KeyCode.Space)
            {
                ToggleMarkAndMove(pane, 1);
                key.Handled = true;
            }
            else if (key.AsRune.Value == '+')
            {
                key.Handled = true;
                SelectGroup(pane, true);
            }
            else if (key.AsRune.Value == '-')
            {
                key.Handled = true;
                SelectGroup(pane, false);
            }
            else if (key.AsRune.Value == '*')
            {
                key.Handled = true;
                InvertSelection(pane);
            }
            else if (key.KeyCode == KeyCode.Backspace)
            {
                key.Handled = true;
                _ = RunNavigationAsync(pane, () => pane.NavigationContext.GoParentAsync());
            }
            else if (IsGoRootKey(key))
            {
                key.Handled = true;
                _ = RunNavigationAsync(pane, () => pane.NavigationContext.GoRootAsync());
            }
            // terminals use alt+enter for full screen, so ctrl+p too
            else if (key == Key.P.WithCtrl || key == Key.Enter.WithAlt)
            {
                key.Handled = true;
                _ = ShowPropertiesAsync(pane);
            }
            else if (key == Key.O.WithCtrl)
            {
                key.Handled = true;
                RequestTerminalAction(new TerminalAction.ShowOutput());
            }
            else if (key == Key.T.WithCtrl)
            {
                key.Handled = true;
                FocusCommandLine();
            }
            else if (IsInsertEntryKey(key, out var fullPath))
            {
                key.Handled = true;
                InsertSelectedEntryInCommandLine(pane, fullPath, false);
            }
            else if (key == Key.L.WithCtrl)
            {
                key.Handled = true;
                _ = ShowLocationAsync(pane);
            }
            // terminals and the Windows console send Ctrl+H as the control character 0x08, which Terminal.Gui decodes
            // as Ctrl+Backspace
            else if (key == Key.H.WithCtrl || key == Key.Backspace.WithCtrl)
            {
                key.Handled = true;
                GoHome(pane);
            }
            else if (key == Key.B.WithCtrl)
            {
                key.Handled = true;
                AddBookmark(pane);
            }
            else if (key == Key.S.WithCtrl)
            {
                key.Handled = true;
                _ = SearchAsync(pane);
            }
            else if (key == Key.F.WithCtrl)
            {
                key.Handled = true;
                StartQuickSearch(pane);
            }
            else if (key == Key.A.WithCtrl)
            {
                key.Handled = true;
                SelectAll(pane);
            }
            else if (key == Key.R.WithCtrl)
            {
                key.Handled = true;
                _ = RunNavigationAsync(pane, () => Task.CompletedTask);
            }
            else if (key == Key.U.WithCtrl)
            {
                key.Handled = true;
                SwapPanes();
            }
            else if (key == Key.D.WithCtrl)
            {
                key.Handled = true;
                ShowSameLocationInOtherPane();
            }
            // plain F1/F2 also when other keys are chosen, for terminals that don't send them, e.g. Terminal.app sends
            // nothing for Shift+F1/Shift+F2 (see VolumeProviderKeyBindings)
            else if (key == LeftVolumeProviderKey || key == Key.F1)
            {
                key.Handled = true;
                ChooseLeftVolumeProvider();
            }
            else if (key == RightVolumeProviderKey || key == Key.F2)
            {
                key.Handled = true;
                ChooseRightVolumeProvider();
            }
            else if (key.KeyCode == KeyCode.F3)
            {
                key.Handled = true;
                _ = ViewAsync(pane);
            }
            else if (key.KeyCode == KeyCode.F4)
            {
                key.Handled = true;
                _ = EditAsync(pane, otherPane);
            }
            else if (key.KeyCode == KeyCode.F5)
            {
                key.Handled = true;
                _ = CopyAsync(pane, otherPane);
            }
            else if (key == Key.F6.WithShift)
            {
                key.Handled = true;
                _ = RenameAsync(pane, otherPane);
            }
            else if (key.KeyCode == KeyCode.F6)
            {
                key.Handled = true;
                _ = MoveAsync(pane, otherPane);
            }
            else if (key == Key.C.WithCtrl || key == Key.X.WithCtrl)
            {
                key.Handled = true;
                // Key equality includes Handled, so compare the key code once it's handled
                _ = CopyToClipboardAsync(pane, key.KeyCode == Key.X.WithCtrl.KeyCode);
            }
            // Windows Terminal and the classic console paste text with Ctrl+V themselves, so Alt+V pastes too
            else if (key == Key.V.WithCtrl || key == Key.V.WithAlt)
            {
                key.Handled = true;
                _ = PasteAsync(pane, otherPane);
            }
            else if (key.KeyCode == KeyCode.F7)
            {
                key.Handled = true;
                _ = MakeDirectoryAsync(pane, otherPane);
            }
            else if (key.KeyCode == KeyCode.F8 || key.KeyCode == KeyCode.Delete)
            {
                key.Handled = true;
                _ = DeleteAsync(pane, otherPane);
            }
            else if (key.KeyCode == KeyCode.F9)
            {
                key.Handled = true;
                _ = CreateVolumeAsync(pane, otherPane);
            }
            // the context menu at the current entry, like the GUI's Shift+F10 (terminals send no menu key); Ctrl+F10
            // too, as some terminals take Shift+F10 for their own context menu (e.g. GNOME Terminal)
            else if (key == Key.F10.WithShift || key == Key.F10.WithCtrl)
            {
                key.Handled = true;
                ShowEntryMenu(pane);
            }
            // terminals like gnome terminal use f10 for their menu, so ctrl+q too
            else if (key.KeyCode == KeyCode.F10 || key == Key.Q.WithCtrl)
            {
                key.Handled = true;
                Quit();
            }
        };
    }

    /// <summary>
    ///     Ctrl+Enter inserts the name of the entry under the cursor in the command line, Ctrl+Shift+Enter its full path.
    ///     Ctrl+J too, as terminals sending Ctrl+Enter as a line feed (e.g. on Linux and macOS) send it for Ctrl+J.
    /// </summary>
    private static bool IsInsertEntryKey(Key key, out bool fullPath)
    {
        fullPath = key == Key.Enter.WithCtrl.WithShift;
        return fullPath || key == Key.Enter.WithCtrl || key == Key.J.WithCtrl;
    }

    /// <summary>Focuses the command line (Ctrl+T), keeping the command typed.</summary>
    private void FocusCommandLine()
    {
        _commandField.SetFocus();
        _commandField.InsertionPoint = _commandField.Text.Length;
    }

    /// <summary>
    ///     Inserts the name or full path of the entry under the cursor of a pane in the command line (Ctrl+Enter,
    ///     Ctrl+Shift+Enter): at its cursor when typing in it, otherwise at the end.
    /// </summary>
    private void InsertSelectedEntryInCommandLine(Pane pane, bool fullPath, bool atCursor)
    {
        var row = pane.TableView.SelectedRow;
        if (row < 0 || row >= pane.Entries.Count ||
            pane.NavigationContext.GetCommandLineText(pane.Entries[row], fullPath) is not { } text)
        {
            return;
        }

        _commandLine.Text = _commandField.Text;
        var position = atCursor ? _commandField.InsertionPoint : _commandField.Text.Length;
        position = _commandLine.Insert(text, position);
        _commandField.Text = _commandLine.Text;
        _commandField.InsertionPoint = position;
        _commandField.SetNeedsDraw();
    }

    /// <summary>
    ///     Keys of the command line: Enter runs the command, Esc and Tab go back to the active pane keeping the command,
    ///     Ctrl+Enter and Ctrl+Shift+Enter insert the active pane's entry and Up and Down go through the commands run.
    /// </summary>
    private void OnCommandFieldKeyDown(object? sender, Key key)
    {
        if (key.KeyCode == KeyCode.Enter)
        {
            key.Handled = true;
            RunCommandLine();
        }
        else if (key.KeyCode == KeyCode.Esc || key.KeyCode == KeyCode.Tab)
        {
            key.Handled = true;
            _activePane.TableView.SetFocus();
        }
        else if (IsInsertEntryKey(key, out var fullPath))
        {
            key.Handled = true;
            InsertSelectedEntryInCommandLine(_activePane, fullPath, true);
        }
        else if (key.KeyCode == KeyCode.CursorUp || key.KeyCode == KeyCode.CursorDown)
        {
            // Key equality includes Handled, so the key code is read before it's handled
            var previous = key.KeyCode == KeyCode.CursorUp;
            key.Handled = true;
            _commandLine.Text = _commandField.Text;
            if (previous ? _commandLine.Previous() : _commandLine.Next())
            {
                _commandField.Text = _commandLine.Text;
                _commandField.InsertionPoint = _commandField.Text.Length;
            }
        }
    }

    /// <summary>
    ///     Runs the command typed in the command line in the active pane's directory, in the terminal while the TUI is
    ///     shut down like a program opened in a pane, then the panes are listed again.
    /// </summary>
    private void RunCommandLine()
    {
        if (_commandField.Text.Trim().Length == 0)
        {
            return;
        }

        if (_activePane.NavigationContext.CurrentHostDirectory is not { } directory)
        {
            ErrorBox.ShowNotice(CommandLine.NotLocalMessage);
            return;
        }

        _commandLine.Text = _commandField.Text;
        if (_commandLine.TakeCommand() is not { } command)
        {
            return;
        }

        _commandField.Text = string.Empty;
        _activePane.TableView.SetFocus();
        RequestTerminalAction(new TerminalAction.RunCommand(command, directory));
    }

    private void ViewInActivePane() => _ = ViewAsync(_activePane);

    private void EditInActivePane() => _ = EditAsync(_activePane, _activePane == _leftPane ? _rightPane : _leftPane);

    private void CopyFromActivePane() => _ = CopyAsync(_activePane, _activePane == _leftPane ? _rightPane : _leftPane);

    private void MoveFromActivePane() => _ = MoveAsync(_activePane, _activePane == _leftPane ? _rightPane : _leftPane);

    private void DeleteFromActivePane() =>
        _ = DeleteAsync(_activePane, _activePane == _leftPane ? _rightPane : _leftPane);

    private void MakeDirectoryInActivePane() =>
        _ = MakeDirectoryAsync(_activePane, _activePane == _leftPane ? _rightPane : _leftPane);

    private void CreateVolumeInActivePane() =>
        _ = CreateVolumeAsync(_activePane, _activePane == _leftPane ? _rightPane : _leftPane);

    private void SelectGroupInActivePane() => SelectGroup(_activePane, true);

    private void UnselectGroupInActivePane() => SelectGroup(_activePane, false);

    private void InvertSelectionInActivePane() => InvertSelection(_activePane);

    private void SelectAllInActivePane() => SelectAll(_activePane);

    private void ChooseLeftVolumeProvider() => _ = ChooseVolumeProviderAsync(_leftPane);

    private void ChooseRightVolumeProvider() => _ = ChooseVolumeProviderAsync(_rightPane);

    private void ShowLocationOfActivePane() => _ = ShowLocationAsync(_activePane);

    private void GoHomeInActivePane() => GoHome(_activePane);

    /// <summary>
    ///     Goes to the user's home directory in a pane (Ctrl+H, Home in the location dialog), switching to the Local
    ///     volume provider, see <see cref="NavigationContext.GoHomeAsync" />.
    /// </summary>
    private void GoHome(Pane pane) => _ = RunNavigationAsync(pane, () => pane.NavigationContext.GoHomeAsync());

    private void SearchInActivePane() => _ = SearchAsync(_activePane);

    private void QuickSearchInActivePane()
    {
        _activePane.TableView.SetFocus();
        StartQuickSearch(_activePane);
    }

    /// <summary>Starts the quick search of a pane (Ctrl+F), see <see cref="QuickSearch" />.</summary>
    private static void StartQuickSearch(Pane pane)
    {
        pane.QuickSearch.Start();
        UpdateQuickSearchLabel(pane);
    }

    private static void StopQuickSearch(Pane pane)
    {
        if (!pane.QuickSearch.IsActive)
        {
            return;
        }

        pane.QuickSearch.Stop();
        UpdateQuickSearchLabel(pane);
    }

    private static void UpdateQuickSearchLabel(Pane pane)
    {
        pane.QuickSearchLabel.Visible = pane.QuickSearch.IsActive;
        pane.QuickSearchLabel.Text = $" Find: {pane.QuickSearch.Text} ";
        pane.QuickSearchLabel.SetNeedsDraw();
        pane.BottomLine.SetNeedsDraw();
    }

    /// <summary>
    ///     Handles a key while the quick search of a pane is active: typed characters, Backspace, Ctrl+F, Up and Down
    ///     search, Esc stops. Other keys stop the quick search and return false, so they do what they always do, e.g.
    ///     Enter opens the entry found. <c>+</c>, <c>-</c>, <c>*</c> and Space only stop it as the first character.
    /// </summary>
    private static bool HandleQuickSearchKey(Pane pane, Key key)
    {
        var quickSearch = pane.QuickSearch;
        int? index;
        // Key equality includes Handled, so the key codes are compared (the caller handles the key afterwards)
        switch (key.KeyCode)
        {
            case KeyCode.Esc:
                StopQuickSearch(pane);
                return true;
            case KeyCode.Backspace:
                index = quickSearch.RemoveLast(GetQuickSearchNames(pane));
                break;
            case KeyCode.CursorDown:
            case KeyCode.CursorUp:
            case KeyCode.F | KeyCode.CtrlMask:
                index = quickSearch.Next(GetQuickSearchNames(pane), pane.TableView.SelectedRow,
                    key.KeyCode != KeyCode.CursorUp);
                break;
            default:
                var rune = key.AsRune;
                if (key.IsCtrl || key.IsAlt || rune.Value < ' ' ||
                    (quickSearch.Text.Length == 0 && rune.Value is '+' or '-' or '*' or ' '))
                {
                    StopQuickSearch(pane);
                    return false;
                }

                // a character without a match is rejected
                index = quickSearch.TryAppend(rune.ToString(), GetQuickSearchNames(pane));
                break;
        }

        if (index is { } row)
        {
            MoveSelection(pane, row, true);
        }

        UpdateQuickSearchLabel(pane);
        return true;
    }

    /// <summary>The shown names of a pane's entries for the quick search, null for the parent entry.</summary>
    private static string?[] GetQuickSearchNames(Pane pane) =>
        pane.Entries.Select(entry => entry.Name == Paths.Parent
            ? null
            : string.IsNullOrEmpty(entry.DisplayName) ? entry.Name : entry.DisplayName).ToArray();

    /// <summary>Lists the entries of the active pane again (Ctrl+R), e.g. after files were changed outside.</summary>
    private void RefreshActivePane() => _ = RunNavigationAsync(_activePane, () => Task.CompletedTask);

    /// <summary>
    ///     Swaps the left and right panes (Ctrl+U), like Midnight Commander: each pane moves to the other side with its
    ///     location, entries, marks and cursor, and the active pane stays active, so the focus moves to the other side.
    /// </summary>
    /// <summary>
    ///     Shows the location of the active pane in the other pane (Ctrl+D), also inside images and on servers
    ///     connected to (see <see cref="NavigationContext.ChangeLocationAsync(NavigationContext, CancellationToken)" />).
    ///     The active pane stays active.
    /// </summary>
    private void ShowSameLocationInOtherPane()
    {
        var pane = _activePane;
        var otherPane = GetOtherPane(pane);
        _ = RunNavigationAsync(otherPane, () => otherPane.NavigationContext.ChangeLocationAsync(pane.NavigationContext));
    }

    private void SwapPanes()
    {
        (_leftPane, _rightPane) = (_rightPane, _leftPane);
        PlacePaneView(_leftPane.View, true);
        PlacePaneView(_rightPane.View, false);
        _activePane.TableView.SetFocus();
        SetNeedsLayout();
        SetNeedsDraw();
        SaveSorts();
    }

    /// <summary>Remembers the order of the entries of both panes in the settings, for their sides.</summary>
    private void SaveSorts()
    {
        try
        {
            (TuiSettings.Load() with
            {
                LeftSort = _leftPane.NavigationContext.Sort,
                RightSort = _rightPane.NavigationContext.Sort
            }).Save();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            ErrorBox.Show($"Failed to save settings to '{TuiSettings.FilePath}': {exception.Message}", exception);
        }
    }

    private void AddBookmarkInActivePane() => AddBookmark(_activePane);

    /// <summary>
    ///     Bookmarks the location of a pane with the description entered (Ctrl+B). When the location was connected to
    ///     with a user name and password (e.g. SMB), asks whether to store them in the credential store of the
    ///     operating system, or says they can't be stored when there is none. The pane gets the focus.
    /// </summary>
    private void AddBookmark(Pane pane)
    {
        var context = pane.NavigationContext;
        var dialog = new BookmarkDialog("Add bookmark", context.CurrentPath, context.GetDefaultBookmarkDescription(),
            _palette);
        Application.Run(dialog);
        var description = dialog.Result;
        dialog.Dispose();
        pane.TableView.SetFocus();
        if (description == null)
        {
            return;
        }

        var store = BookmarkStore.Default;
        var bookmark = context.CreateBookmark(description);
        try
        {
            store.Add(bookmark);
        }
        catch (IOException exception)
        {
            ErrorBox.Show(exception);
            return;
        }

        if (bookmark.UserName == null || context.ConnectedLocation?.Credentials is not { } credentials)
        {
            return;
        }

        if (!store.CredentialStore.IsAvailable)
        {
            TuiMessageBox.Query("Bookmark", BookmarkStore.GetUnavailableMessage(), "OK");
            return;
        }

        if (TuiMessageBox.Query("Bookmark", store.GetStorePasswordQuestion(bookmark), "_Yes",
                "_No") != 0)
        {
            return;
        }

        try
        {
            store.StorePassword(bookmark, credentials.Password);
        }
        catch (IOException exception)
        {
            ErrorBox.Show(exception);
        }

        pane.TableView.SetFocus();
    }

    private void ShowPropertiesOfActivePane() => _ = ShowPropertiesAsync(_activePane);

    /// <summary>
    ///     Views the file under the cursor in the viewer (F3), reading only the part of the file shown. Directories
    ///     aren't viewed.
    /// </summary>
    private async Task ViewAsync(Pane pane)
    {
        if (GetCurrentEntry(pane) is not { Type: EntryType.Item } entry)
        {
            return;
        }

        ViewerSession session;
        try
        {
            // a terminal can't show images, so they are viewed as binary files in the hex view
            session = await pane.NavigationContext.OpenViewerAsync(entry, []);
        }
        catch (OperationCanceledException)
        {
            // cancelled by the user, e.g. making a temporary copy of a file inside an image
            pane.TableView.SetFocus();
            return;
        }
        catch (Exception exception)
        {
            ErrorBox.Show(exception);
            pane.TableView.SetFocus();
            return;
        }

        using (session)
        {
            var dialog = new ViewerDialog(entry.Name, session, _palette);
            Application.Run(dialog);
            dialog.Dispose();
        }

        pane.TableView.SetFocus();
    }

    /// <summary>
    ///     Edits the file under the cursor (F4) in the editor chosen in the settings: the built-in editor, or an
    ///     external editor run in the terminal while the TUI is shut down, like Midnight Commander. A file inside an
    ///     image or another volume is edited as a temporary copy, written back when the TUI runs again. Both panes are
    ///     refreshed after saving, as the other pane may show the same location.
    /// </summary>
    private async Task EditAsync(Pane pane, Pane otherPane)
    {
        if (GetCurrentEntry(pane) is not { Type: EntryType.Item } entry)
        {
            return;
        }

        var settings = _editorSettings;
        if (settings.Editor == EditorKind.BuiltIn)
        {
            await EditWithBuiltInEditorAsync(pane, otherPane, entry);
            return;
        }

        if (ExternalEditor.Get(settings.Editor, settings.EditorCommand, true) is not { } command)
        {
            ErrorBox.ShowNotice(settings.Editor == EditorKind.Custom
                ? "Enter the command of the custom editor in the settings"
                : "No text editor was found, choose another editor in the settings");
            return;
        }

        ExternalEditFile file;
        try
        {
            file = await pane.NavigationContext.OpenExternalEditAsync(entry);
        }
        catch (Exception exception)
        {
            ErrorBox.Show(exception);
            return;
        }

        _pendingExternalEdit = file;
        RequestTerminalAction(new TerminalAction.RunEditor(command, file.Path));
    }

    private async Task EditWithBuiltInEditorAsync(Pane pane, Pane otherPane, IEntry entry)
    {
        EditorSession session;
        try
        {
            session = await pane.NavigationContext.OpenEditorAsync(entry);
        }
        catch (Exception exception)
        {
            ErrorBox.Show(exception);
            pane.TableView.SetFocus();
            return;
        }

        using (session)
        {
            var dialog = new EditorDialog(session, _palette);
            Application.Run(dialog);
            dialog.Dispose();
        }

        if (session.HasSaved)
        {
            await RefreshPaneAsync(pane);
            await RefreshPaneAsync(otherPane);
        }

        pane.TableView.SetFocus();
    }

    /// <summary>
    ///     Writes back the temporary copy of a file edited with an external editor when it changed, once the TUI runs
    ///     again after the editor exited, and deletes the copy.
    /// </summary>
    private async Task SavePendingExternalEditAsync()
    {
        if (_pendingExternalEdit is not { } file)
        {
            return;
        }

        _pendingExternalEdit = null;
        using (file)
        {
            try
            {
                await file.SaveChangesAsync();
            }
            catch (Exception exception)
            {
                ErrorBox.Show($"Failed to save the changes of '{file.Name}': {exception.Message}", exception);
            }
        }
    }

    /// <summary>
    ///     Shows the properties of the marked entries, or the entry under the cursor, of a pane. The size and contents
    ///     are counted in the background while the dialog is open; closing it stops counting.
    /// </summary>
    private async Task ShowPropertiesAsync(Pane pane)
    {
        var entries = pane.NavigationContext.GetOperationEntries(GetCurrentEntry(pane));
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
            ErrorBox.Show(exception);
            return;
        }

        var dialog = new PropertiesDialog(properties, _palette);
        if (properties.HasContents)
        {
            // count in the background and update the dialog on the UI thread
            var progress = new UiProgress<EntryContents>(value => UiThread.Post(() => dialog.Update(value)));
            var cancellationToken = dialog.CancellationToken;
            var countTask = Task.Run(() => pane.NavigationContext.CountContentsAsync(entries, progress,
                cancellationToken), cancellationToken);
            _ = countTask.ContinueWith(task => UiThread.Post(() =>
                    dialog.ShowError(task.Exception?.GetBaseException().Message ?? "Counting failed")),
                CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted, TaskScheduler.Default);
        }

        Application.Run(dialog);
        var checkedFlags = dialog.CheckedFlags;
        var tabTexts = dialog.TabTexts;
        dialog.Dispose();
        if (checkedFlags != null)
        {
            await ChangePropertiesAsync(pane, properties, checkedFlags, tabTexts);
        }

        pane.TableView.SetFocus();
    }

    /// <summary>
    ///     Saves the properties dialog: changes the attributes and Amiga protection bits of the entry to its check boxes
    ///     and saves the changed texts of its tabs, refreshing both panes when changed, as the other pane may show the
    ///     same location.
    /// </summary>
    private async Task ChangePropertiesAsync(Pane pane, EntryProperties properties,
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
            ErrorBox.Show(exception);
        }

        await RefreshPaneAsync(_leftPane);
        await RefreshPaneAsync(_rightPane);
    }

    /// <summary>
    ///     Marks (select) or unmarks (unselect) the entries of a pane matching the filter entered in the select group
    ///     dialog, keeping the other marks. The filter is remembered for the next time, the check boxes start unchecked.
    /// </summary>
    private void SelectGroup(Pane pane, bool select)
    {
        var dialog = new SelectGroupDialog(SelectGroupOptions.Default with { Filter = _selectGroupFilter }, _palette,
            select ? "Select group" : "Unselect group");
        Application.Run(dialog);
        var options = dialog.Result;
        dialog.Dispose();

        pane.TableView.SetFocus();
        if (options == null)
        {
            return;
        }

        _selectGroupFilter = options.Filter;
        try
        {
            if (select)
            {
                pane.NavigationContext.MarkGroup(pane.Entries, options);
            }
            else
            {
                pane.NavigationContext.UnmarkGroup(pane.Entries, options);
            }
        }
        catch (Exception exception)
        {
            ErrorBox.Show(exception);
            return;
        }

        UpdateMarks(pane);
    }

    /// <summary>Marks all entries of a pane except ".." (Ctrl+A).</summary>
    private static void SelectAll(Pane pane)
    {
        pane.NavigationContext.MarkAll(pane.Entries);
        UpdateMarks(pane);
    }

    /// <summary>Inverts the marks of the entries of a pane: marked entries are unmarked and the others marked.</summary>
    private static void InvertSelection(Pane pane)
    {
        pane.NavigationContext.InvertMarks(pane.Entries);
        UpdateMarks(pane);
    }

    /// <summary>Redraws a pane and its marked-entries summary after its marks changed.</summary>
    private static void UpdateMarks(Pane pane)
    {
        pane.PathPanel.Summary = pane.NavigationContext.MarkedSummary;
        pane.PathPanel.SetNeedsDraw();
        pane.TableView.SetNeedsDraw();
    }

    /// <summary>
    ///     Shows the location dialog of a pane with its current path, changing to the entered path on OK, showing the
    ///     volume provider chosen with Volumes or going to the home directory with Home. The pane gets the focus.
    /// </summary>
    private async Task ShowLocationAsync(Pane pane)
    {
        var dialog = new LocationDialog(pane == _leftPane ? "Left location" : "Right location", pane.NavigationContext.CurrentPath, () => ChooseVolumeProvider(pane), _palette);
        Application.Run(dialog);
        var result = dialog.Result;
        dialog.Dispose();

        pane.TableView.SetFocus();
        if (result?.VolumeChoice is { } volumeChoice)
        {
            await ChangeVolumeProviderAsync(pane, volumeChoice);
        }
        else if (result?.IsHome == true)
        {
            GoHome(pane);
        }
        else if (result?.Path is { } path)
        {
            await RunNavigationAsync(pane, () => pane.NavigationContext.ChangeLocationAsync(path));
        }
    }

    /// <summary>
    ///     Shows the search dialog (Ctrl+S) starting in the location of a pane, with the other options of the last
    ///     search, and goes to the result chosen with Go to, selecting it. The pane gets the focus.
    /// </summary>
    private async Task SearchAsync(Pane pane)
    {
        var context = pane.NavigationContext;
        var dialog = new SearchDialog(_searchOptions with { StartPath = context.CurrentPath }, _searchSort,
            context.SearchAsync, _palette);
        Application.Run(dialog);
        var result = dialog.Result;
        _searchOptions = dialog.Options;
        _searchSort = dialog.Sort;
        dialog.Dispose();

        pane.TableView.SetFocus();
        if (result != null)
        {
            await RunNavigationAsync(pane, () => pane.NavigationContext.GoToSearchResultAsync(result));
        }
    }

    /// <summary>
    ///     Lets the user choose the volume provider a pane shows from the list of volume providers, centered over the
    ///     pane, and shows the root of a new volume from it. The pane gets the focus.
    /// </summary>
    private async Task ChooseVolumeProviderAsync(Pane pane)
    {
        var volumeChoice = ChooseVolumeProvider(pane);
        pane.TableView.SetFocus();
        if (volumeChoice != null)
        {
            await ChangeVolumeProviderAsync(pane, volumeChoice);
        }
    }

    /// <summary>Shows a volume provider or opens a bookmark chosen in the volume provider list.</summary>
    private Task ChangeVolumeProviderAsync(Pane pane, VolumeChoice volumeChoice) =>
        volumeChoice.Bookmark is { } bookmark
            ? OpenBookmarkAsync(pane, bookmark)
            : ChangeVolumeProviderAsync(pane, volumeChoice.VolumeProvider!);

    /// <summary>
    ///     Opens a bookmark in a pane, connecting with the stored credentials and asking for them when needed. A volume
    ///     provider needing administrator rights offers to restart Hst Commander as administrator first, with the pane
    ///     at the bookmark.
    /// </summary>
    private async Task OpenBookmarkAsync(Pane pane, Bookmark bookmark)
    {
        IVolumeProvider volumeProvider;
        try
        {
            volumeProvider = BookmarkOpener.GetVolumeProvider(bookmark);
        }
        catch (IOException exception)
        {
            ErrorBox.Show(exception);
            return;
        }

        if (Elevation.NeedsRestart(volumeProvider) &&
            TuiMessageBox.Query(Elevation.AdministratorName, Elevation.GetRestartQuestion(volumeProvider), "_Restart",
                "_Continue") == 0 &&
            RestartAsAdministrator(pane, volumeProvider, bookmark.Path))
        {
            return;
        }

        await RunNavigationAsync(pane, () => BookmarkOpener.OpenAsync(pane.NavigationContext, bookmark,
            BookmarkStore.Default, AskCredentials, pane.Progress.Show, pane.Progress.Hide));
        pane.TableView.SetFocus();
    }

    /// <summary>
    ///     Shows the root of a new volume from a volume provider in a pane. A volume provider needing administrator
    ///     rights offers to restart Hst Commander as administrator first, with the pane showing the volume provider;
    ///     declined or cancelled, the volume provider is shown without them.
    /// </summary>
    private async Task ChangeVolumeProviderAsync(Pane pane, IVolumeProvider volumeProvider)
    {
        if (Elevation.NeedsRestart(volumeProvider) &&
            TuiMessageBox.Query(Elevation.AdministratorName, Elevation.GetRestartQuestion(volumeProvider), "_Restart",
                "_Continue") == 0 &&
            RestartAsAdministrator(pane, volumeProvider))
        {
            return;
        }

        if (volumeProvider is ILocationVolumeProvider locationVolumeProvider)
        {
            await ConnectAsync(pane, locationVolumeProvider);
            return;
        }

        await RunNavigationAsync(pane, () => pane.NavigationContext.ChangeVolumeProviderAsync(volumeProvider));
    }

    /// <summary>
    ///     Asks for the location of a volume provider needing one (e.g. an SMB path) and shows it in a pane, asking for
    ///     a user name and password when the location needs them. A volume provider asking for connection details (e.g.
    ///     FTP/SFTP) shows the connection dialog instead, and connects with the credentials entered in it first. The pane
    ///     is unchanged when a dialog is cancelled.
    /// </summary>
    private async Task ConnectAsync(Pane pane, ILocationVolumeProvider volumeProvider)
    {
        string? location;
        VolumeCredentials? credentials = null;
        if (volumeProvider is IConnectionVolumeProvider connectionVolumeProvider)
        {
            var connectionDialog = new ConnectionDialog(connectionVolumeProvider, _palette);
            Application.Run(connectionDialog);
            location = connectionDialog.Result?.Location;
            credentials = connectionDialog.Result?.Credentials;
            connectionDialog.Dispose();
        }
        else
        {
            var dialog = new VolumeLocationDialog(volumeProvider, VolumeConnector.GetLastLocation(volumeProvider),
                _palette);
            Application.Run(dialog);
            location = dialog.Result;
            dialog.Dispose();
        }

        pane.TableView.SetFocus();
        if (location == null)
        {
            return;
        }

        await RunNavigationAsync(pane, async () =>
        {
            var connection = await VolumeConnector.ConnectAsync(volumeProvider, location, AskCredentials,
                pane.Progress.Show, pane.Progress.Hide, credentials);
            if (connection != null)
            {
                await pane.NavigationContext.ChangeVolumeProviderAsync(connection);
            }
        });
        pane.TableView.SetFocus();
    }

    /// <summary>Shows the credentials dialog, returning the user name and password entered or null.</summary>
    private Task<VolumeCredentials?> AskCredentials(CredentialsRequest request)
    {
        var dialog = new CredentialsDialog(request, _palette);
        Application.Run(dialog);
        var credentials = dialog.Result;
        dialog.Dispose();
        return Task.FromResult(credentials);
    }

    /// <summary>
    ///     Starts Hst Commander as administrator with the panes where they are and the pane showing the volume provider,
    ///     and quits: on Windows with UAC, elsewhere with sudo in the terminal while the TUI is shut down. Returns false
    ///     when it isn't started, e.g. UAC is cancelled.
    /// </summary>
    private bool RestartAsAdministrator(Pane pane, IVolumeProvider volumeProvider, string? path = null)
    {
        var isRight = pane == _rightPane;
        return StartAsAdministratorAndQuit(StartupOptions.FromPanes(_leftPane.NavigationContext,
            _rightPane.NavigationContext, isRight, isRight, volumeProvider, path));
    }

    /// <summary>
    ///     Starts Hst Commander as administrator or root with both panes where they are and the same pane active, from
    ///     File > Restart as administrator/root, and quits.
    /// </summary>
    private void RestartElevated()
    {
        if (TuiMessageBox.Query(Elevation.AdministratorName, Elevation.GetRestartMenuQuestion(), "_Restart",
                "_Cancel") == 0)
        {
            StartAsAdministratorAndQuit(StartupOptions.FromPanes(_leftPane.NavigationContext,
                _rightPane.NavigationContext, _activePane == _rightPane));
        }
    }

    /// <summary>
    ///     Starts Hst Commander as administrator with the startup options and quits: on Windows with UAC, elsewhere with
    ///     sudo in the terminal while the TUI is shut down. Returns false when it isn't started, e.g. UAC is cancelled.
    /// </summary>
    private bool StartAsAdministratorAndQuit(StartupOptions options)
    {
        if (!Elevation.CanRestartWithoutTerminal)
        {
            RequestTerminalAction(new TerminalAction.RestartAsRoot(options));
            return true;
        }

        try
        {
            if (!Elevation.StartAsAdministrator(options))
            {
                return false;
            }
        }
        catch (IOException exception)
        {
            ErrorBox.Show(exception);
            return false;
        }

        _restarting = true;
        Application.RequestStop(this);
        return true;
    }

    /// <summary>
    ///     Shows the volume provider list with the bookmarks centered over a pane, returning the chosen volume provider
    ///     or bookmark, or null.
    /// </summary>
    private VolumeChoice? ChooseVolumeProvider(Pane pane)
    {
        var dialog = new VolumeProviderDialog(pane == _leftPane ? "Left" : "Right", VolumeProviders.All,
            BookmarkStore.Default, pane.View.Frame, _palette);
        Application.Run(dialog);
        var volumeProvider = dialog.Result;
        dialog.Dispose();
        return volumeProvider;
    }

    /// <summary>
    ///     Deletes the marked entries, or the entry under the cursor, after confirming, showing the progress while
    ///     deleting. The other pane is refreshed too, as it may show the same location.
    /// </summary>
    private async Task DeleteAsync(Pane pane, Pane otherPane)
    {
        var entries = pane.NavigationContext.GetOperationEntries(GetCurrentEntry(pane));
        if (entries.Count == 0)
        {
            return;
        }

        // esc answers no
        if (TuiMessageBox.Query("Delete", NavigationContext.GetDeleteDescription(entries), "_Yes", "_No") != 0)
        {
            pane.TableView.SetFocus();
            return;
        }

        // delete in the background and update the dialog on the UI thread; it closes when the delete has stopped
        var progressDialog = new DeleteProgressDialog(_palette);
        var progress = new UiProgress<DeleteProgress>(value => UiThread.Post(() => progressDialog.Update(value)));
        var cancellationToken = progressDialog.CancellationToken;
        var deleteTask = Task.Run(() => pane.NavigationContext.DeleteAsync(entries, progress, cancellationToken));
        _ = deleteTask.ContinueWith(_ => UiThread.Post(progressDialog.Complete), TaskScheduler.Default);
        Application.Run(progressDialog);
        progressDialog.Dispose();

        try
        {
            await deleteTask;
            pane.NavigationContext.ClearMarks();
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            ErrorBox.Show(exception);
        }

        await InitializePaneAsync(pane);
        await InitializePaneAsync(otherPane);
        pane.TableView.SetFocus();
    }

    /// <summary>
    ///     Creates a directory in the current location of a pane with the name entered in the make directory dialog and
    ///     selects it. The other pane is refreshed too, as it may show the same location.
    /// </summary>
    private async Task MakeDirectoryAsync(Pane pane, Pane otherPane)
    {
        if (pane.NavigationContext.CreateDirectoryNotSupportedMessage is { } message)
        {
            ErrorBox.ShowNotice(message);
            pane.TableView.SetFocus();
            return;
        }

        var dialog = new MakeDirectoryDialog(_palette);
        Application.Run(dialog);
        var name = dialog.Result;
        dialog.Dispose();

        pane.TableView.SetFocus();
        if (name == null)
        {
            return;
        }

        await RunNavigationAsync(pane, () => pane.NavigationContext.CreateDirectoryAsync(name));
        await InitializePaneAsync(otherPane);
    }

    /// <summary>
    ///     Creates a volume as a file in the current location of a pane: the volume to create is chosen in the create
    ///     dialog, then its file name and fields are entered. When creating fails, the error is shown and the fields are
    ///     asked again with the input entered. The created file is selected and the other pane is refreshed too, as it
    ///     may show the same location.
    /// </summary>
    private async Task CreateVolumeAsync(Pane pane, Pane otherPane)
    {
        var creators = VolumeProviders.Creators;
        if (pane.NavigationContext.GetCreateVolumeNotSupportedMessage(creators) is { } message)
        {
            ErrorBox.ShowNotice(message);
            pane.TableView.SetFocus();
            return;
        }

        var dialog = new CreateVolumeDialog(creators, _palette);
        Application.Run(dialog);
        var creator = dialog.Result;
        dialog.Dispose();

        VolumeCreatorInput? input = null;
        while (creator != null)
        {
            var fieldsDialog = new CreateVolumeFieldsDialog(creator, input, _palette);
            Application.Run(fieldsDialog);
            input = fieldsDialog.Result;
            fieldsDialog.Dispose();
            if (input == null)
            {
                break;
            }

            try
            {
                await pane.NavigationContext.CreateVolumeAsync(creator, input.FileName, input.Values);
            }
            catch (Exception exception)
            {
                ErrorBox.Show(exception);
                continue;
            }

            await InitializePaneAsync(pane);
            await InitializePaneAsync(otherPane);
            break;
        }

        pane.TableView.SetFocus();
    }

    private static IEntry? GetCurrentEntry(Pane pane)
    {
        var selectedRow = pane.TableView.SelectedRow;
        return selectedRow >= 0 && selectedRow < pane.Entries.Count ? pane.Entries[selectedRow] : null;
    }

    /// <summary>
    ///     Copies the marked entries, or the entry under the cursor, to the other pane after confirming the destination
    ///     and filter in the copy dialog, showing the progress while copying.
    /// </summary>
    private async Task CopyAsync(Pane pane, Pane otherPane)
    {
        var entries = pane.NavigationContext.GetOperationEntries(GetCurrentEntry(pane));
        if (entries.Count == 0)
        {
            return;
        }

        var copyDialog = new CopyDialog(NavigationContext.GetCopyDescription(entries),
            otherPane.NavigationContext.CurrentPath, _palette);
        Application.Run(copyDialog);
        var request = copyDialog.Result;
        copyDialog.Dispose();
        if (request == null)
        {
            return;
        }

        // copy in the background and update the dialog on the UI thread; it closes when the copy has stopped
        var progressDialog = new CopyProgressDialog(_palette);
        var progress = new UiProgress<CopyProgress>(value => UiThread.Post(() => progressDialog.Update(value)));
        var cancellationToken = progressDialog.CancellationToken;
        var copyTask = Task.Run(() => pane.NavigationContext.CopyAsync(entries, otherPane.NavigationContext,
            request.Destination, request.Filter, progress, ConfirmOverwriteAsync, cancellationToken));
        _ = copyTask.ContinueWith(_ => UiThread.Post(progressDialog.Complete), TaskScheduler.Default);
        Application.Run(progressDialog);
        progressDialog.Dispose();

        try
        {
            await copyTask;
            pane.NavigationContext.ClearMarks();
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            ErrorBox.Show(exception);
        }

        await InitializePaneAsync(pane);
        await InitializePaneAsync(otherPane);
        pane.TableView.SetFocus();
    }

    /// <summary>
    ///     Moves the marked entries, or the entry under the cursor, to the other pane after confirming the destination
    ///     and filter in the move dialog, showing the progress while moving. A single entry moved to a name without
    ///     path separators is renamed instead and selected.
    /// </summary>
    private async Task MoveAsync(Pane pane, Pane otherPane)
    {
        var entries = pane.NavigationContext.GetOperationEntries(GetCurrentEntry(pane));
        if (entries.Count == 0)
        {
            return;
        }

        if (pane.NavigationContext.MoveNotSupportedMessage is { } message)
        {
            ErrorBox.ShowNotice(message);
            pane.TableView.SetFocus();
            return;
        }

        var moveDialog = new CopyDialog(NavigationContext.GetMoveDescription(entries),
            otherPane.NavigationContext.CurrentPath, _palette, "Move");
        Application.Run(moveDialog);
        var request = moveDialog.Result;
        moveDialog.Dispose();
        pane.TableView.SetFocus();
        if (request == null)
        {
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
            ErrorBox.Show(exception);
            return;
        }

        if (newName != null)
        {
            await RunNavigationAsync(pane, () => pane.NavigationContext.RenameAsync(entries[0], newName));
            await InitializePaneAsync(otherPane);
            return;
        }

        // move in the background and update the dialog on the UI thread; it closes when the move has stopped
        var progressDialog = new CopyProgressDialog(_palette, "Move");
        var progress = new UiProgress<CopyProgress>(value => UiThread.Post(() => progressDialog.Update(value)));
        var cancellationToken = progressDialog.CancellationToken;
        var moveTask = Task.Run(() => pane.NavigationContext.MoveAsync(entries, otherPane.NavigationContext,
            request.Destination, request.Filter, progress, ConfirmOverwriteAsync, cancellationToken));
        _ = moveTask.ContinueWith(_ => UiThread.Post(progressDialog.Complete), TaskScheduler.Default);
        Application.Run(progressDialog);
        progressDialog.Dispose();

        try
        {
            await moveTask;
            pane.NavigationContext.ClearMarks();
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            ErrorBox.Show(exception);
        }

        await InitializePaneAsync(pane);
        await InitializePaneAsync(otherPane);
        pane.TableView.SetFocus();
    }

    private Pane GetOtherPane(Pane pane) => pane == _leftPane ? _rightPane : _leftPane;

    /// <summary>
    ///     Shows the context menu of the entries of a pane with the keyboard (Shift+F10), below the current entry, or at
    ///     the top left of the table when the current entry isn't visible.
    /// </summary>
    private void ShowEntryMenu(Pane pane)
    {
        var tableView = pane.TableView;
        var row = tableView.HeaderHeight + tableView.SelectedRow - tableView.RowOffset;
        var position = row >= tableView.HeaderHeight && row < tableView.Viewport.Height
            ? tableView.ViewportToScreen(new System.Drawing.Point(0, row))
            : tableView.ViewportToScreen(System.Drawing.Point.Empty);
        ShowEntryMenu(pane, null, position);
    }

    /// <summary>
    ///     Shows the context menu of the entries of a pane at the mouse (right-click): the row clicked becomes the
    ///     current entry, and the menu acts on the marked entries or, when none are marked, the current entry, like the
    ///     function keys. Copy, Cut, Rename and Delete need an entry (not ".."), drives only have properties.
    ///     PowerShell here, Terminal here and Command prompt here (only Terminal here on macOS and Linux) open a shell
    ///     in a new window in the pane's directory when it's local, see <see cref="ShellHere" />.
    /// </summary>
    private void ShowEntryMenu(Pane pane, int? row, System.Drawing.Point screenPosition)
    {
        pane.TableView.SetFocus();
        if (row is { } index && index >= 0 && index < pane.Entries.Count)
        {
            pane.TableView.SelectedRow = index;
        }

        var currentEntry = GetCurrentEntry(pane);
        var hasEntries = pane.NavigationContext.GetOperationEntries(currentEntry).Count > 0;
        var canChange = hasEntries && !pane.NavigationContext.IsDriveList;
        var canRename = canChange && currentEntry != null && currentEntry.Name != Paths.Parent;
        var canPaste = !pane.NavigationContext.IsDriveList;
        var isLocal = pane.NavigationContext.CurrentHostDirectory != null;
        var otherPane = GetOtherPane(pane);
        var items = new List<View>
        {
            CreateMenuItem("_Copy", "Ctrl+C", () => _ = CopyToClipboardAsync(pane, false), canChange),
            CreateMenuItem("Cu_t", "Ctrl+X", () => _ = CopyToClipboardAsync(pane, true), canChange),
            CreateMenuItem("_Paste", "Alt+V", () => _ = PasteAsync(pane, otherPane), canPaste),
            new Line(),
            CreateMenuItem("_Rename...", "Shift+F6", () => _ = RenameAsync(pane, otherPane), canRename),
            CreateMenuItem("_Delete...", "F8", () => _ = DeleteAsync(pane, otherPane), canChange),
            new Line()
        };
        foreach (var kind in ShellHere.Available)
        {
            items.Add(CreateMenuItem(ShellHere.GetHeader(kind), string.Empty, () => OpenShellHere(pane, kind),
                isLocal));
        }

        items.Add(new Line());
        items.Add(CreateMenuItem("Pr_operties...", "Ctrl+P", () => _ = ShowPropertiesAsync(pane), hasEntries));
        if (_entryMenu != null)
        {
            Application.Popovers?.DeRegister(_entryMenu);
            _entryMenu.Dispose();
        }

        _entryMenu = new PopoverMenu(items);
        Application.Popovers?.Register(_entryMenu);
        _entryMenu.MakeVisible(screenPosition);
    }

    /// <summary>Creates an item of a context menu showing its key at the right, disabled when it can't be chosen.</summary>
    private static MenuItem CreateMenuItem(string title, string keyText, Action action, bool enabled) =>
        new(title, keyText, action) { Enabled = enabled };

    /// <summary>
    ///     Opens PowerShell, a terminal or the command prompt in a new window in a pane's directory from its context menu
    ///     (<see cref="ShellHere" />), without waiting for it.
    /// </summary>
    private static void OpenShellHere(Pane pane, ShellHereKind kind)
    {
        if (pane.NavigationContext.CurrentHostDirectory is not { } directory)
        {
            return;
        }

        try
        {
            ShellHere.Start(kind, directory);
        }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or IOException
                                              or InvalidOperationException)
        {
            ErrorBox.Show(exception);
        }

        pane.TableView.SetFocus();
    }

    /// <summary>
    ///     Renames the entry under the cursor of a pane to the name entered (Shift+F6), see
    ///     <see cref="NavigationContext.RenameAsync" />. The other pane is refreshed too, as it may show the same
    ///     location.
    /// </summary>
    private async Task RenameAsync(Pane pane, Pane otherPane)
    {
        if (GetCurrentEntry(pane) is not { } entry || entry.Name == Paths.Parent ||
            pane.NavigationContext.IsDriveList)
        {
            return;
        }

        if (pane.NavigationContext.MoveNotSupportedMessage is { } message)
        {
            ErrorBox.ShowNotice(message);
            pane.TableView.SetFocus();
            return;
        }

        var dialog = new MakeDirectoryDialog(_palette, "Rename", $"Rename '{entry.Name}' to:", entry.Name);
        Application.Run(dialog);
        var newName = dialog.Result;
        dialog.Dispose();

        pane.TableView.SetFocus();
        if (newName == null || newName == entry.Name)
        {
            return;
        }

        await RunNavigationAsync(pane, () => pane.NavigationContext.RenameAsync(entry, newName));
        await InitializePaneAsync(otherPane);
    }

    /// <summary>
    ///     Copies or cuts the marked entries, or the entry under the cursor, of a pane to the clipboard (Ctrl+C,
    ///     Ctrl+X), see <see cref="EntryClipboard" />. On Windows, the entries are also put on the Windows clipboard,
    ///     so Explorer can paste them (<see cref="EntryClipboard.SetWindowsClipboard" />); a terminal can't do that on
    ///     other systems.
    /// </summary>
    private async Task CopyToClipboardAsync(Pane pane, bool isCut)
    {
        var entries = pane.NavigationContext.GetOperationEntries(GetCurrentEntry(pane));
        if (entries.Count == 0 || pane.NavigationContext.IsDriveList)
        {
            return;
        }

        try
        {
            var clipboard = await EntryClipboard.SetAsync(pane.NavigationContext, entries, isCut);
            if (OperatingSystem.IsWindows())
            {
                clipboard.SetWindowsClipboard(IntPtr.Zero,
                    exception => UiThread.PostIfRunning(() => ErrorBox.Show(exception)),
                    moved => UiThread.PostIfRunning(() => _ = ClipboardEntriesMovedAsync(moved)));
            }
        }
        catch (Exception exception)
        {
            ErrorBox.Show(exception);
        }

        pane.TableView.SetFocus();
    }

    /// <summary>
    ///     Pastes the clipboard in the current location of a pane (Ctrl+V): entries copied or cut in the panes, or on
    ///     Windows files copied or cut in another application, copied or moved without asking for the destination,
    ///     showing the progress. Pasted cut entries are removed from the clipboard.
    /// </summary>
    private async Task PasteAsync(Pane pane, Pane otherPane)
    {
        if (pane.NavigationContext.IsDriveList)
        {
            return;
        }

        PasteSource? source;
        try
        {
            source = OperatingSystem.IsWindows()
                ? await EntryClipboard.GetWindowsPasteSourceAsync(pane.NavigationContext)
                : await EntryClipboard.GetPasteSourceAsync(true, null, false, pane.NavigationContext);
        }
        catch (Exception exception)
        {
            ErrorBox.Show(exception);
            return;
        }

        if (source == null)
        {
            return;
        }

        using (source)
        {
            // paste in the background and update the dialog on the UI thread; it closes when pasting has stopped
            var progressDialog = new CopyProgressDialog(_palette, source.Title);
            var progress =
                new UiProgress<CopyProgress>(value => UiThread.Post(() => progressDialog.Update(value)));
            var cancellationToken = progressDialog.CancellationToken;
            var pasteTask = Task.Run(() => source.PasteAsync(pane.NavigationContext, progress,
                ConfirmOverwriteAsync, cancellationToken));
            _ = pasteTask.ContinueWith(_ => UiThread.Post(progressDialog.Complete), TaskScheduler.Default);
            Application.Run(progressDialog);
            progressDialog.Dispose();

            try
            {
                await pasteTask;
                if (source.IsMove)
                {
                    if (!source.IsFromHost)
                    {
                        EntryClipboard.Clear();
                    }

                    if (OperatingSystem.IsWindows())
                    {
                        WindowsFileClipboard.Clear();
                    }
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception exception)
            {
                ErrorBox.Show(exception);
            }
        }

        await InitializePaneAsync(pane);
        await InitializePaneAsync(otherPane);
        pane.TableView.SetFocus();
    }

    private async Task ActivateSelectedEntryAsync(Pane pane)
    {
        var selectedRow = pane.TableView.SelectedRow;
        if (selectedRow < 0 || selectedRow >= pane.Entries.Count)
        {
            return;
        }

        await RunNavigationAsync(pane, () => pane.NavigationContext.OpenEntryAsync(pane.Entries[selectedRow]));
    }

    private void OpenAsVolumeInActivePane() => _ = OpenSelectedEntryAsVolumeAsync(_activePane);

    /// <summary>
    ///     Opens the entry under the cursor as a volume (Ctrl+PageDown), e.g. a .docx document as a zip archive.
    /// </summary>
    private async Task OpenSelectedEntryAsVolumeAsync(Pane pane)
    {
        var selectedRow = pane.TableView.SelectedRow;
        if (selectedRow < 0 || selectedRow >= pane.Entries.Count)
        {
            return;
        }

        await RunNavigationAsync(pane, () => pane.NavigationContext.OpenAsVolumeAsync(pane.Entries[selectedRow]));
    }

    private async Task RunNavigationAsync(Pane pane, Func<Task> action)
    {
        try
        {
            await action();
            await RefreshPaneAsync(pane);
        }
        catch (OperationCanceledException)
        {
            // cancelled by the user, e.g. making a temporary copy of an image inside another image
        }
        catch (Exception exception)
        {
            ErrorBox.Show(exception);
        }
    }

    private static async Task InitializePaneAsync(Pane pane)
    {
        try
        {
            await RefreshPaneAsync(pane);
        }
        catch (Exception exception)
        {
            ErrorBox.Show(exception);
        }
    }

    private static async Task RefreshPaneAsync(Pane pane)
    {
        StopQuickSearch(pane);
        pane.VolumeDetection?.Cancel();
        var previousPath = pane.PathPanel.Path;
        pane.Entries = await ListingProgress.ListEntriesAsync(pane.NavigationContext, pane.Progress.Show,
            pane.Progress.Hide);
        pane.PathPanel.Path = pane.NavigationContext.CurrentPath;
        pane.PathPanel.DiskSpace = pane.NavigationContext.DiskSpace;
        pane.PathPanel.Summary = pane.NavigationContext.MarkedSummary;
        pane.PathPanel.SetNeedsDraw();

        ShowEntries(pane, pane.NavigationContext.GetSelectedIndex(pane.Entries, previousPath,
            pane.TableView.SelectedRow));
    }

    /// <summary>
    ///     Sorts the entries of a pane by a column whose header was clicked (ascending, or reversed when already sorted
    ///     by it), keeping the entry under the cursor. The order is remembered in the settings.
    /// </summary>
    private void SortPane(Pane pane, int column)
    {
        if (column < 0 || column >= EntrySort.Columns.Count)
        {
            return;
        }

        var selectedRow = pane.TableView.SelectedRow;
        var selectedEntry = selectedRow >= 0 && selectedRow < pane.Entries.Count ? pane.Entries[selectedRow] : null;
        pane.VolumeDetection?.Cancel();
        pane.Entries = pane.NavigationContext.ToggleSort(EntrySort.Columns[column]);
        ShowEntries(pane, selectedEntry == null ? 0 : Math.Max(0, IndexOfEntry(pane.Entries, selectedEntry)));
        SaveSorts();
    }

    /// <summary>Shows the entries of a pane in its table, with the sorting in the column headers.</summary>
    private static void ShowEntries(Pane pane, int selectedRow)
    {
        var table = new DataTable();
        var sort = pane.NavigationContext.Sort;
        foreach (var column in EntrySort.Columns)
        {
            table.Columns.Add(sort.GetHeader(column), typeof(string));
        }

        foreach (var entry in pane.Entries)
        {
            var size = SizeFormatter.FormatEntrySize(entry);
            var modifiedDate = entry.ModifiedDate?.ToString("yyyy-MM-dd HH:mm") ?? string.Empty;
            table.Rows.Add(GetEntryMarker(pane, entry) + entry.DisplayName, size, modifiedDate);
        }

        pane.TableView.Table = new DataTableSource(table);
        pane.DataTable = table;
        if (pane.Entries.Count > 0)
        {
            pane.TableView.SelectedRow = selectedRow;
            pane.TableView.EnsureSelectedCellIsVisible();
        }

        UpdatePaneColumns(pane);
        DetectVolumeFiles(pane);
    }

    /// <summary>
    ///     Detects the files of the pane that can be opened as volumes in the background, showing their marker when
    ///     detected (see <see cref="GetEntryMarker" />).
    /// </summary>
    private static async void DetectVolumeFiles(Pane pane)
    {
        var cancellation = pane.VolumeDetection = new CancellationTokenSource();
        var entries = pane.Entries;
        await pane.NavigationContext.DetectVolumeFilesAsync(entries, entry =>
        {
            var rowIndex = IndexOfEntry(entries, entry);
            if (cancellation.IsCancellationRequested || pane.DataTable == null || rowIndex < 0)
            {
                return;
            }

            pane.DataTable.Rows[rowIndex][0] = FormatEntryName(pane, entry, GetColumnWidths(
                pane.TableView.Viewport.Width).Name);
            pane.TableView.SetNeedsDraw();
        }, cancellation.Token);
    }

    private static int IndexOfEntry(IReadOnlyList<IEntry> entries, IEntry entry)
    {
        for (var index = 0; index < entries.Count; index++)
        {
            if (ReferenceEquals(entries[index], entry))
            {
                return index;
            }
        }

        return -1;
    }

    /// <summary>
    ///     First character of an entry's name in the panes: the directory separator of the OS for directories, '>'
    ///     for files that can be opened as volumes (e.g. ISO images and zip archives) and a space for other files.
    /// </summary>
    private static char GetEntryMarker(Pane pane, IEntry entry) => entry.Type == EntryType.Container
        ? Path.DirectorySeparatorChar
        : pane.NavigationContext.IsVolumeFile(entry)
            ? VolumeFileMarker
            : ' ';

    private const char VolumeFileMarker = '>';

    private static void UpdatePaneColumns(Pane pane)
    {
        var paneWidth = pane.TableView.Viewport.Width;
        pane.TableView.SeparatorSymbol = paneWidth < 5 ? '\0' : ' ';
        var widths = GetColumnWidths(paneWidth);
        pane.TableView.Style.ColumnStyles = new Dictionary<int, ColumnStyle>
        {
            [0] = new() { MinWidth = widths.Name, MaxWidth = widths.Name },
            [1] = new() { MinWidth = widths.Size, MaxWidth = widths.Size, Alignment = Alignment.End },
            [2] = new() { MinWidth = widths.Modified, MaxWidth = widths.Modified }
        };
        pane.TableView.ColumnWidths = [widths.Name, widths.Size, widths.Modified];
        pane.BottomLine.Text = CreateBottomLine(pane.TableView);
        pane.PathPanel.Dividers = pane.TableView.GetDividerPositions();
        pane.PathPanel.SetNeedsDraw();

        if (pane.DataTable == null)
        {
            return;
        }

        for (var rowIndex = 0; rowIndex < pane.Entries.Count; rowIndex++)
        {
            pane.DataTable.Rows[rowIndex][0] = FormatEntryName(pane, pane.Entries[rowIndex], widths.Name);
        }
        pane.TableView.SetNeedsDraw();
    }

    private static string CreateBottomLine(EntryTableView tableView)
    {
        var width = tableView.Viewport.Width;
        if (width <= 0)
        {
            return string.Empty;
        }

        var line = new string(Glyphs.HLine.ToString()[0], width).ToCharArray();
        var dividers = tableView.GetDividerPositions();
        for (var index = 0; index < dividers.Count; index++)
        {
            if (dividers[index] < 0 || dividers[index] >= width)
            {
                continue;
            }

            var glyph = index == 0
                ? Glyphs.LLCorner
                : index == dividers.Count - 1 ? Glyphs.LRCorner : Glyphs.BottomTee;
            line[dividers[index]] = glyph.ToString()[0];
        }

        return new string(line);
    }

    private static (int Name, int Size, int Modified) GetColumnWidths(int paneWidth)
    {
        // TableView reserves one leading cell for header lines and adds one cell of padding after every column
        var availableWidth = Math.Max(3, paneWidth - 1 - ColumnCount);
        var modifiedWidth = Math.Min(ModifiedColumnWidth, availableWidth - 2);
        var sizeWidth = Math.Min(SizeColumnWidth, availableWidth - modifiedWidth - 1);
        var nameWidth = Math.Max(1, availableWidth - sizeWidth - modifiedWidth);

        return (nameWidth, sizeWidth, modifiedWidth);
    }

    /// <summary>Name of an entry shown in a pane column, its marker followed by its name shortened to fit.</summary>
    private static string FormatEntryName(Pane pane, IEntry entry, int columnWidth) =>
        GetEntryMarker(pane, entry) + FormatEntryName(entry.DisplayName, entry.Type, Math.Max(1, columnWidth - 1));

    private static string FormatEntryName(string name, EntryType type, int columnWidth)
    {
        if (name.Length <= columnWidth)
        {
            return name;
        }

        var extension = type == EntryType.Item ? Path.GetExtension(name) : string.Empty;
        var stem = type == EntryType.Item ? Path.GetFileNameWithoutExtension(name) : name;
        var tailLength = Math.Min(3, stem.Length);
        var availableTailLength = Math.Max(0, columnWidth - extension.Length - 1);
        tailLength = Math.Min(tailLength, availableTailLength);

        if (extension.Length + tailLength + 1 > columnWidth)
        {
            extension = extension[..Math.Max(0, columnWidth - 1)];
        }

        var tail = tailLength == 0 ? string.Empty : stem[^tailLength..];
        var suffix = tail + extension;
        var prefixLength = Math.Max(0, columnWidth - suffix.Length - 1);
        return $"{name[..prefixLength]}~{suffix}";
    }

    private static int GetPageSize(Pane pane) => pane.TableView.VisibleRows;

    /// <summary>
    ///     Typing / or \ (like the root path), or Ctrl+\, goes to the root of the volume. The typed characters work with
    ///     every keyboard layout and terminal; \ is typed with AltGr on e.g. Danish and German layouts, which the Windows
    ///     driver may report with Ctrl and Alt. Terminals send Ctrl+\ as the control character 0x1C, also sent for
    ///     Ctrl+4, which may be decoded as either key, but can't send it on layouts without a \ key. The Windows driver
    ///     decodes Ctrl with the key's character in the current layout, so the key left of Z (the \ key on US layouts) is
    ///     Ctrl+&lt; on e.g. Danish and German layouts.
    /// </summary>
    private static bool IsGoRootKey(Key key)
    {
        var character = key.KeyCode & ~(KeyCode.CtrlMask | KeyCode.AltMask);
        var modifiers = key.KeyCode & (KeyCode.CtrlMask | KeyCode.AltMask);
        return (character is (KeyCode)'/' or (KeyCode)'\\' && modifiers is 0 or (KeyCode.CtrlMask | KeyCode.AltMask)) ||
               key == new Key('\\').WithCtrl || key == new Key('<').WithCtrl || key == Key.D4.WithCtrl ||
               key.KeyCode == (KeyCode)0x1C ||
               key.KeyCode == ((KeyCode)0x1C | KeyCode.CtrlMask);
    }

    private static void MoveSelection(Pane pane, int offset, bool absolute = false)
    {
        if (pane.Entries.Count == 0)
        {
            return;
        }

        var selectedRow = pane.TableView.SelectedRow;
        var newRow = absolute ? offset : Math.Clamp(selectedRow + offset, 0, pane.Entries.Count - 1);
        newRow = Math.Clamp(newRow, 0, pane.Entries.Count - 1);
        pane.TableView.SelectedRow = newRow;
        pane.TableView.EnsureSelectedCellIsVisible();
        pane.TableView.SetNeedsDraw();
    }

    /// <summary>
    ///     Toggles the mark of the entry under the cursor and moves the cursor by an offset: to the next entry with
    ///     Insert, Space and Shift+Down, to the previous one with Shift+Up.
    /// </summary>
    private static void ToggleMarkAndMove(Pane pane, int offset)
    {
        var selectedRow = pane.TableView.SelectedRow;
        if (selectedRow < 0 || selectedRow >= pane.Entries.Count)
        {
            return;
        }

        pane.NavigationContext.ToggleMark(pane.Entries[selectedRow]);
        pane.PathPanel.Summary = pane.NavigationContext.MarkedSummary;
        pane.PathPanel.SetNeedsDraw();
        MoveSelection(pane, offset);
    }

    private Scheme GetRowColorScheme(Pane pane, int rowIndex)
    {
        if (rowIndex < 0 || rowIndex >= pane.Entries.Count)
        {
            return CreateColorScheme(_palette.PaneNormal, _palette.Cursor);
        }

        var entry = pane.Entries[rowIndex];
        var isMarked = pane.NavigationContext.IsMarked(entry);
        var foreground = isMarked
            ? _palette.Marked
            : entry.Type == EntryType.Container
                ? _palette.Container
                : pane.NavigationContext.IsExecutable(entry) ? _palette.Executable : _palette.Item;
        // themes may give marked entries a background of their own, e.g. Directory Opus 4
        var background = isMarked
            ? (entry.Type == EntryType.Container ? _palette.MarkedContainerBackground : _palette.MarkedItemBackground)
              ?? _palette.PaneBackground
            : _palette.PaneBackground;
        var normal = new TuiAttribute(foreground, background);
        // the current entry is only shown in the active pane
        var selected = pane == _activePane && rowIndex == pane.TableView.SelectedRow
            ? isMarked ? _palette.MarkedCursor : _palette.Cursor
            : normal;

        return CreateColorScheme(normal, selected);
    }

    private static Scheme CreateColorScheme(
        TuiAttribute normal,
        TuiAttribute focus,
        TuiAttribute? hotNormal = null,
        TuiAttribute? hotFocus = null) =>
        // the current entry of a pane without focus is drawn like the other entries (Active)
        TuiPalette.CreateScheme(normal, focus, hotNormal, hotFocus, hotNormal ?? normal);

    /// <summary>
    ///     Asks on the UI thread whether to overwrite an existing file, called from the copy running in the background.
    ///     Esc answers no.
    /// </summary>
    private static Task<OverwriteAnswer> ConfirmOverwriteAsync(OverwriteRequest request, CancellationToken _)
    {
        var answer = new TaskCompletionSource<OverwriteAnswer>(TaskCreationOptions.RunContinuationsAsynchronously);
        UiThread.Post(() =>
        {
            try
            {
                var button = TuiMessageBox.Query("Overwrite", request.Message, "_Yes", "_No", "_All");
                answer.SetResult(button switch
                {
                    0 => OverwriteAnswer.Yes,
                    2 => OverwriteAnswer.All,
                    _ => OverwriteAnswer.No
                });
            }
            catch (Exception exception)
            {
                answer.SetException(exception);
            }
        });
        return answer.Task;
    }

    /// <summary>Progress calling the handler on the reporting thread; the handler invokes the UI thread.</summary>
    private sealed class UiProgress<T>(Action<T> handler) : IProgress<T>
    {
        public void Report(T value) => handler(value);
    }

    private sealed class Pane(
        NavigationContext navigationContext,
        View view,
        PathPanelView pathPanel,
        EntryTableView tableView,
        Label bottomLine,
        ListingProgressView progress,
        ParentStripView parentStrip,
        bool isLeft)
    {
        /// <summary>Parent button at the outer side of the pane, shown by themes of plugins.</summary>
        public ParentStripView ParentStrip { get; } = parentStrip;

        /// <summary>True for the left pane, whose parent button is at its left side.</summary>
        public bool IsLeft { get; } = isLeft;

        public NavigationContext NavigationContext { get; } = navigationContext;
        public View View { get; } = view;
        public PathPanelView PathPanel { get; } = pathPanel;
        public EntryTableView TableView { get; } = tableView;
        public Label BottomLine { get; } = bottomLine;

        /// <summary>The quick search text on the bottom border, shown while <see cref="QuickSearch" /> is active.</summary>
        public required Label QuickSearchLabel { get; init; }

        public QuickSearch QuickSearch { get; } = new();
        public ListingProgressView Progress { get; } = progress;
        public IReadOnlyList<IEntry> Entries { get; set; } = [];
        public DataTable? DataTable { get; set; }

        /// <summary>Cancels detecting the files that can be opened as volumes, e.g. when listed again.</summary>
        public CancellationTokenSource? VolumeDetection { get; set; }
    }
}
