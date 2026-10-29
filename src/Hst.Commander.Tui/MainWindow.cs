using System.Data;
using Hst.Commander.Core;
using Hst.Commander.Core.Volumes;
using Hst.Commander.Abstractions;
using Terminal.Gui;
using TuiAttribute = Terminal.Gui.Attribute;

namespace Hst.Commander.Tui;

public sealed class MainWindow : Window
{
    private const int SizeColumnWidth = 8;
    private const int ModifiedColumnWidth = 16;
    private const int ColumnCount = 3;

    private readonly MenuBar _menuBar;
    private readonly Label? _administratorLabel;
    private readonly FunctionKeyBarView _functionKeyBar;
    private readonly Pane _leftPane;
    private readonly Pane _rightPane;
    private readonly MenuItem _leftVolumeMenuItem;
    private readonly MenuItem _rightVolumeMenuItem;
    private VolumeProviderKeys _volumeProviderKeys;
    private TuiPalette _palette;
    private Pane _activePane;
    private TerminalAction? _pendingTerminalAction;
    private string? _pendingError;
    private bool _restarting;
    private string _selectGroupFilter = SelectGroupOptions.Default.Filter;

    public MainWindow(string initialPath, TuiSettings settings)
    {
        var theme = settings.Theme;
        BorderStyle = LineStyle.None;
        X = 0;
        Y = 0;
        Width = Dim.Fill();
        Height = Dim.Fill();
        _palette = TuiPalette.For(theme);

        _leftVolumeMenuItem = new MenuItem("_Left volume...", string.Empty, ChooseLeftVolumeProvider);
        _rightVolumeMenuItem = new MenuItem("_Right volume...", string.Empty, ChooseRightVolumeProvider);
        _menuBar = new MenuBar
        {
            Menus =
            [
                new MenuBarItem("_File", new MenuItem[]
                {
                    new("Vie_w...", "F3", ViewInActivePane),
                    new("_Copy...", "F5", CopyFromActivePane),
                    new("Mo_ve/rename...", "F6", MoveFromActivePane),
                    new("_Make directory...", "F7", MakeDirectoryInActivePane),
                    new("Cr_eate volume...", "F9", CreateVolumeInActivePane),
                    new("_Delete...", "F8", DeleteFromActivePane),
                    new("_Properties...", "Ctrl+P", ShowPropertiesOfActivePane),
                    new("Ope_n as volume", "Ctrl+PgDn", OpenAsVolumeInActivePane),
                    null!,
                    new("Select _group...", "+", SelectGroupInActivePane),
                    new("_Unselect group...", "-", UnselectGroupInActivePane),
                    new("_Invert selection", "*", InvertSelectionInActivePane),
                    null!,
                    _leftVolumeMenuItem,
                    _rightVolumeMenuItem,
                    new("L_ocation...", "Ctrl+L", ShowLocationOfActivePane),
                    null!,
                    new("_Settings...", string.Empty, ShowSettings),
                    null!,
                    new("_Quit", "F10/Ctrl+Q", Quit)
                }),
                new MenuBarItem("_Help", new MenuItem[]
                {
                    new("_About...", string.Empty, ShowAbout)
                })
            ]
        };
        _functionKeyBar = new FunctionKeyBarView
        {
            X = 0,
            Y = Pos.AnchorEnd(1),
            Width = Dim.Fill(),
            Height = 1,
            Items =
            [
                new FunctionKeyItem("F3", "View", ViewInActivePane),
                new FunctionKeyItem("F4", "Edit"),
                new FunctionKeyItem("F5", "Copy", CopyFromActivePane),
                new FunctionKeyItem("F6", "Move", MoveFromActivePane),
                new FunctionKeyItem("F7", "Mkdir", MakeDirectoryInActivePane),
                new FunctionKeyItem("F8", "Delete", DeleteFromActivePane),
                new FunctionKeyItem("F9", "Create", CreateVolumeInActivePane),
                new FunctionKeyItem("F10", "Quit", Quit)
            ]
        };
        // the panes handle f9 (create volume) and f10 (quit) before the menu bar's key, so alt+f opens the menu
        Closing += (_, e) => e.Cancel = !ConfirmClosing();
        _leftPane = CreatePane(new NavigationContext(initialPath, StartFile), 0);
        _rightPane = CreatePane(new NavigationContext(initialPath, StartFile), 50);
        _activePane = _leftPane;

        Add(_menuBar, _leftPane.View, _rightPane.View, _functionKeyBar);

        // running as administrator or root is shown at the right of the menu bar
        if (Elevation.IsElevated)
        {
            var text = $" {Elevation.AdministratorName} ";
            _menuBar.Width = Dim.Fill(text.Length);
            _administratorLabel = new Label { X = Pos.AnchorEnd(text.Length), Y = 0, Text = text };
            Add(_administratorLabel);
        }

        ApplyTheme(theme);
        ApplyVolumeProviderKeys(settings.EffectiveVolumeProviderKeys);
        ApplyEnterFileAction(settings.EnterFileAction);
        ApplyUaeMetadata(settings.UaeMetadata);
        ConfigurePaneKeys(_leftPane, _rightPane);
        ConfigurePaneKeys(_rightPane, _leftPane);
        // also when run again after a program has run in the terminal. The panes are listed once the window runs:
        // Application.Init installs Terminal.Gui's synchronization context, so awaiting a listing that isn't done at
        // once (e.g. the physical disks) before Application.Run would wait for a main loop that isn't running yet
        Loaded += async (_, _) =>
        {
            _activePane.TableView.SetFocus();
            ShowPendingError();
            await InitializeAsync();
        };
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
        Application.Invoke(() => MessageBox.ErrorQuery("Error", message, "OK"));
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
                                     MessageBox.Query("Quit", AppInfo.Current.QuitQuestion, "_Yes", "_No") == 0;

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

    public void DisposeNavigationContexts()
    {
        _leftPane.NavigationContext.Dispose();
        _rightPane.NavigationContext.Dispose();
    }

    /// <summary>Applies the colors of a theme to the menu bar, both panes and the function key bar.</summary>
    public void ApplyTheme(TuiTheme theme)
    {
        _palette = TuiPalette.For(theme);
        _menuBar.ColorScheme = _palette.MenuColorScheme;
        _menuBar.NeedsDraw = true;
        if (_administratorLabel != null)
        {
            _administratorLabel.ColorScheme = _palette.AdministratorColorScheme;
        }

        _functionKeyBar.KeyAttribute = _palette.FunctionKey;
        _functionKeyBar.TitleAttribute = _palette.Menu;
        _functionKeyBar.NeedsDraw = true;
        foreach (var pane in new[] { _leftPane, _rightPane })
        {
            ApplyPaneColors(pane);
        }

        NeedsDraw = true;
    }

    /// <summary>Sets the keys opening the volume provider lists, shown in the File menu.</summary>
    private void ApplyVolumeProviderKeys(VolumeProviderKeys keys)
    {
        _volumeProviderKeys = keys;
        _leftVolumeMenuItem.Help = VolumeProviderKeyBindings.GetLeftKeyText(keys);
        _rightVolumeMenuItem.Help = VolumeProviderKeyBindings.GetRightKeyText(keys);
    }

    /// <summary>Sets what Enter does with files in both panes.</summary>
    private void ApplyEnterFileAction(EnterFileAction action)
    {
        _leftPane.NavigationContext.EnterFileAction = action;
        _rightPane.NavigationContext.EnterFileAction = action;
    }

    /// <summary>Sets how both panes store Amiga names, protection bits and comments when copying from Amiga volumes.</summary>
    private void ApplyUaeMetadata(UaeMetadataFormat format)
    {
        _leftPane.NavigationContext.UaeMetadata = format;
        _rightPane.NavigationContext.UaeMetadata = format;
    }

    private Key LeftVolumeProviderKey => WithVolumeProviderModifier(Key.F1);

    private Key RightVolumeProviderKey => WithVolumeProviderModifier(Key.F2);

    private Key WithVolumeProviderModifier(Key key) => _volumeProviderKeys == VolumeProviderKeys.ShiftFunctionKeys
        ? key.WithShift
        : key.WithAlt;

    private void ApplyPaneColors(Pane pane)
    {
        var normal = _palette.PaneNormal;
        pane.PathPanel.BorderAttribute = normal;
        pane.PathPanel.PathAttribute = new TuiAttribute(_palette.Path, _palette.PaneBackground);
        pane.PathPanel.ActivePathAttribute = _palette.ActivePath;
        pane.PathPanel.NeedsDraw = true;
        pane.TableView.ColorScheme = CreateColorScheme(
            normal,
            _palette.Cursor,
            new TuiAttribute(_palette.Header, _palette.PaneBackground),
            _palette.Cursor);
        pane.TableView.NeedsDraw = true;
        pane.BottomLine.ColorScheme = CreateColorScheme(normal, normal);
        pane.BottomLine.NeedsDraw = true;
    }

    private static void ShowAbout() => MessageBox.Query("About", AppInfo.Current.AboutText, "OK");

    private void ShowSettings()
    {
        var settings = TuiSettings.Load();
        var dialog = new SettingsDialog(settings, ApplyTheme, _palette);
        Application.Run(dialog);
        var result = dialog.Result;
        dialog.Dispose();

        if (result == null)
        {
            ApplyTheme(settings.Theme);
            return;
        }

        ApplyTheme(result.Theme);
        ApplyVolumeProviderKeys(result.EffectiveVolumeProviderKeys);
        ApplyEnterFileAction(result.EnterFileAction);
        ApplyUaeMetadata(result.UaeMetadata);
        try
        {
            result.Save();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            MessageBox.ErrorQuery("Error", $"Failed to save settings to '{TuiSettings.FilePath}': {exception.Message}", "OK");
        }
    }

    private Pane CreatePane(NavigationContext navigationContext, int xPercent)
    {
        var view = new View
        {
            X = Pos.Percent(xPercent),
            Y = 1,
            Width = Dim.Percent(50),
            Height = Dim.Fill(1),
            CanFocus = true
        };
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
            CellActivationKey = KeyCode.Enter
        };

        var bottomLine = new Label
        {
            X = 0,
            Y = Pos.AnchorEnd(),
            Width = Dim.Fill()
        };

        var progress = new ListingProgressView();
        var pane = new Pane(navigationContext, view, pathPanel, tableView, bottomLine, progress);
        tableView.Style = new TableStyle
        {
            ShowVerticalCellLines = true,
            ShowHorizontalHeaderOverline = false,
            AlwaysShowHeaders = true,
            ExpandLastColumn = false,
            RowColorGetter = args => GetRowColorScheme(pane, args.RowIndex)
        };
        tableView.FrameChanged += (_, _) => UpdatePaneColumns(pane);

        view.Add(pathPanel, tableView, bottomLine, progress);

        return pane;
    }

    private void ConfigurePaneKeys(Pane pane, Pane otherPane)
    {
        pane.TableView.CellActivated += async (_, _) => await ActivateSelectedEntryAsync(pane);
        pane.PathPanel.PathDoubleClicked += async (_, _) => await ShowLocationAsync(pane);
        pane.TableView.HasFocusChanged += (_, _) =>
        {
            if (pane.TableView.HasFocus)
            {
                _activePane = pane;
            }

            pane.TableView.NeedsDraw = true;
            pane.PathPanel.IsActive = pane.TableView.HasFocus;
            pane.PathPanel.NeedsDraw = true;
        };
        pane.TableView.SelectedCellChanged += (_, _) => pane.TableView.NeedsDraw = true;
        pane.TableView.KeyDown += (_, key) =>
        {
            if (key.KeyCode == KeyCode.Tab)
            {
                otherPane.TableView.SetFocus();
                key.Handled = true;
            }
            else if (key.KeyCode == KeyCode.CursorUp || key.KeyCode == KeyCode.CursorDown)
            {
                MoveSelection(pane, key.KeyCode == KeyCode.CursorUp ? -1 : 1);
                key.Handled = true;
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
                ToggleMarkAndMoveDown(pane);
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
            else if (key == Key.L.WithCtrl)
            {
                key.Handled = true;
                _ = ShowLocationAsync(pane);
            }
            else if (key == LeftVolumeProviderKey)
            {
                key.Handled = true;
                ChooseLeftVolumeProvider();
            }
            else if (key == RightVolumeProviderKey)
            {
                key.Handled = true;
                ChooseRightVolumeProvider();
            }
            else if (key.KeyCode == KeyCode.F3)
            {
                key.Handled = true;
                _ = ViewAsync(pane);
            }
            else if (key.KeyCode == KeyCode.F5)
            {
                key.Handled = true;
                _ = CopyAsync(pane, otherPane);
            }
            else if (key.KeyCode == KeyCode.F6)
            {
                key.Handled = true;
                _ = MoveAsync(pane, otherPane);
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
            // terminals like gnome terminal use f10 for their menu, so ctrl+q too
            else if (key.KeyCode == KeyCode.F10 || key == Key.Q.WithCtrl)
            {
                key.Handled = true;
                Quit();
            }
        };
    }

    private void ViewInActivePane() => _ = ViewAsync(_activePane);

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

    private void ChooseLeftVolumeProvider() => _ = ChooseVolumeProviderAsync(_leftPane);

    private void ChooseRightVolumeProvider() => _ = ChooseVolumeProviderAsync(_rightPane);

    private void ShowLocationOfActivePane() => _ = ShowLocationAsync(_activePane);

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
        catch (Exception exception)
        {
            MessageBox.ErrorQuery("Error", exception.Message, "OK");
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
            MessageBox.ErrorQuery("Error", exception.Message, "OK");
            return;
        }

        var dialog = new PropertiesDialog(properties, _palette);
        if (properties.HasContents)
        {
            // count in the background and update the dialog on the UI thread
            var progress = new UiProgress<EntryContents>(value => Application.Invoke(() => dialog.Update(value)));
            var cancellationToken = dialog.CancellationToken;
            var countTask = Task.Run(() => pane.NavigationContext.CountContentsAsync(entries, progress,
                cancellationToken), cancellationToken);
            _ = countTask.ContinueWith(task => Application.Invoke(() =>
                    dialog.ShowError(task.Exception?.GetBaseException().Message ?? "Counting failed")),
                CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted, TaskScheduler.Default);
        }

        Application.Run(dialog);
        var checkedFlags = dialog.CheckedFlags;
        dialog.Dispose();
        if (checkedFlags != null)
        {
            await ChangeFlagsAsync(pane, properties, checkedFlags);
        }

        pane.TableView.SetFocus();
    }

    /// <summary>
    ///     Changes the attributes and Amiga protection bits of the entry of the properties dialog to its check boxes,
    ///     refreshing both panes when changed, as the other pane may show the same location.
    /// </summary>
    private async Task ChangeFlagsAsync(Pane pane, EntryProperties properties,
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
            MessageBox.ErrorQuery("Error", exception.Message, "OK");
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
            MessageBox.ErrorQuery("Error", exception.Message, "OK");
            return;
        }

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
        pane.PathPanel.NeedsDraw = true;
        pane.TableView.NeedsDraw = true;
    }

    /// <summary>
    ///     Shows the location dialog of a pane with its current path, changing to the entered path on OK, showing the
    ///     volume provider chosen with Volumes or going to the home directory with Home. The pane gets the focus.
    /// </summary>
    private async Task ShowLocationAsync(Pane pane)
    {
        var dialog = new LocationDialog(pane.NavigationContext.CurrentPath, () => ChooseVolumeProvider(pane), _palette);
        Application.Run(dialog);
        var result = dialog.Result;
        dialog.Dispose();

        pane.TableView.SetFocus();
        if (result?.VolumeProvider is { } volumeProvider)
        {
            await ChangeVolumeProviderAsync(pane, volumeProvider);
        }
        else if (result?.IsHome == true)
        {
            await RunNavigationAsync(pane, () => pane.NavigationContext.GoHomeAsync());
        }
        else if (result?.Path is { } path)
        {
            await RunNavigationAsync(pane, () => pane.NavigationContext.ChangeLocationAsync(path));
        }
    }

    /// <summary>
    ///     Lets the user choose the volume provider a pane shows from the list of volume providers, centered over the
    ///     pane, and shows the root of a new volume from it. The pane gets the focus.
    /// </summary>
    private async Task ChooseVolumeProviderAsync(Pane pane)
    {
        var volumeProvider = ChooseVolumeProvider(pane);
        pane.TableView.SetFocus();
        if (volumeProvider != null)
        {
            await ChangeVolumeProviderAsync(pane, volumeProvider);
        }
    }

    /// <summary>
    ///     Shows the root of a new volume from a volume provider in a pane. A volume provider needing administrator
    ///     rights offers to restart Hst Commander as administrator first, with the pane showing the volume provider;
    ///     declined or cancelled, the volume provider is shown without them.
    /// </summary>
    private async Task ChangeVolumeProviderAsync(Pane pane, IVolumeProvider volumeProvider)
    {
        if (Elevation.NeedsRestart(volumeProvider) &&
            MessageBox.Query(Elevation.AdministratorName, Elevation.GetRestartQuestion(volumeProvider), "_Restart",
                "_Continue") == 0 &&
            RestartAsAdministrator(pane, volumeProvider))
        {
            return;
        }

        await RunNavigationAsync(pane, () => pane.NavigationContext.ChangeVolumeProviderAsync(volumeProvider));
    }

    /// <summary>
    ///     Starts Hst Commander as administrator with the panes where they are and the pane showing the volume provider,
    ///     and quits: on Windows with UAC, elsewhere with sudo in the terminal while the TUI is shut down. Returns false
    ///     when it isn't started, e.g. UAC is cancelled.
    /// </summary>
    private bool RestartAsAdministrator(Pane pane, IVolumeProvider volumeProvider)
    {
        var isRight = pane == _rightPane;
        var options = StartupOptions.FromPanes(_leftPane.NavigationContext, _rightPane.NavigationContext, isRight,
            isRight, volumeProvider);
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
            MessageBox.ErrorQuery("Error", exception.Message, "OK");
            return false;
        }

        _restarting = true;
        Application.RequestStop(this);
        return true;
    }

    /// <summary>Shows the volume provider list centered over a pane, returning the chosen one or null.</summary>
    private IVolumeProvider? ChooseVolumeProvider(Pane pane)
    {
        var dialog = new VolumeProviderDialog(pane == _leftPane ? "Left" : "Right", VolumeProviders.All,
            pane.NavigationContext.VolumeProvider, pane.View.Frame, _palette);
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
        if (MessageBox.Query("Delete", NavigationContext.GetDeleteDescription(entries), "_Yes", "_No") != 0)
        {
            pane.TableView.SetFocus();
            return;
        }

        // delete in the background and update the dialog on the UI thread; it closes when the delete has stopped
        var progressDialog = new DeleteProgressDialog(_palette);
        var progress = new UiProgress<DeleteProgress>(value => Application.Invoke(() => progressDialog.Update(value)));
        var cancellationToken = progressDialog.CancellationToken;
        var deleteTask = Task.Run(() => pane.NavigationContext.DeleteAsync(entries, progress, cancellationToken));
        _ = deleteTask.ContinueWith(_ => Application.Invoke(progressDialog.Complete), TaskScheduler.Default);
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
            MessageBox.ErrorQuery("Error", exception.Message, "OK");
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
            MessageBox.ErrorQuery("Error", message, "OK");
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
            MessageBox.ErrorQuery("Error", message, "OK");
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
                MessageBox.ErrorQuery("Error", exception.Message, "OK");
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
        var progress = new UiProgress<CopyProgress>(value => Application.Invoke(() => progressDialog.Update(value)));
        var cancellationToken = progressDialog.CancellationToken;
        var copyTask = Task.Run(() => pane.NavigationContext.CopyAsync(entries, otherPane.NavigationContext,
            request.Destination, request.Filter, progress, ConfirmOverwriteAsync, cancellationToken));
        _ = copyTask.ContinueWith(_ => Application.Invoke(progressDialog.Complete), TaskScheduler.Default);
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
            MessageBox.ErrorQuery("Error", exception.Message, "OK");
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
            MessageBox.ErrorQuery("Error", message, "OK");
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
            MessageBox.ErrorQuery("Error", exception.Message, "OK");
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
        var progress = new UiProgress<CopyProgress>(value => Application.Invoke(() => progressDialog.Update(value)));
        var cancellationToken = progressDialog.CancellationToken;
        var moveTask = Task.Run(() => pane.NavigationContext.MoveAsync(entries, otherPane.NavigationContext,
            request.Destination, request.Filter, progress, ConfirmOverwriteAsync, cancellationToken));
        _ = moveTask.ContinueWith(_ => Application.Invoke(progressDialog.Complete), TaskScheduler.Default);
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
            MessageBox.ErrorQuery("Error", exception.Message, "OK");
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
        catch (Exception exception)
        {
            MessageBox.ErrorQuery("Error", exception.Message, "OK");
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
            MessageBox.ErrorQuery("Error", exception.Message, "OK");
        }
    }

    private static async Task RefreshPaneAsync(Pane pane)
    {
        var previousPath = pane.PathPanel.Path;
        pane.Entries = await ListingProgress.ListEntriesAsync(pane.NavigationContext, pane.Progress.Show,
            pane.Progress.Hide);
        pane.PathPanel.Path = pane.NavigationContext.CurrentPath;
        pane.PathPanel.DiskSpace = pane.NavigationContext.DiskSpace;
        pane.PathPanel.Summary = pane.NavigationContext.MarkedSummary;
        pane.PathPanel.NeedsDraw = true;

        var table = new DataTable();
        table.Columns.Add("Name", typeof(string));
        table.Columns.Add("Size", typeof(string));
        table.Columns.Add("Modified", typeof(string));

        foreach (var entry in pane.Entries)
        {
            var size = SizeFormatter.FormatEntrySize(entry);
            var modifiedDate = entry.ModifiedDate?.ToString("yyyy-MM-dd HH:mm") ?? string.Empty;
            table.Rows.Add(entry.DisplayName, size, modifiedDate);
        }

        pane.TableView.Table = new DataTableSource(table);
        pane.DataTable = table;
        if (pane.Entries.Count > 0)
        {
            pane.TableView.SelectedRow = pane.NavigationContext.GetSelectedIndex(
                pane.Entries, previousPath, pane.TableView.SelectedRow);
            pane.TableView.EnsureSelectedCellIsVisible();
        }

        UpdatePaneColumns(pane);
    }

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
        pane.PathPanel.NeedsDraw = true;

        if (pane.DataTable == null)
        {
            return;
        }

        for (var rowIndex = 0; rowIndex < pane.Entries.Count; rowIndex++)
        {
            pane.DataTable.Rows[rowIndex][0] = FormatEntryName(
                pane.Entries[rowIndex].DisplayName,
                pane.Entries[rowIndex].Type,
                widths.Name);
        }
        pane.TableView.NeedsDraw = true;
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
    ///     Ctrl+\ going to the root of the volume. Terminals send it as the control character 0x1C, also sent for
    ///     Ctrl+4, which may be decoded as either key. The Windows driver decodes Ctrl with the key's character in the
    ///     current layout, so the key left of Z (the \ key on US layouts) is Ctrl+&lt; on e.g. Danish and German layouts.
    /// </summary>
    private static bool IsGoRootKey(Key key) =>
        key == new Key('\\').WithCtrl || key == new Key('<').WithCtrl || key == Key.D4.WithCtrl ||
        key.KeyCode == (KeyCode)0x1C ||
        key.KeyCode == ((KeyCode)0x1C | KeyCode.CtrlMask);

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
        pane.TableView.NeedsDraw = true;
    }

    /// <summary>Toggles the mark of the entry under the cursor and moves the cursor to the next entry.</summary>
    private static void ToggleMarkAndMoveDown(Pane pane)
    {
        var selectedRow = pane.TableView.SelectedRow;
        if (selectedRow < 0 || selectedRow >= pane.Entries.Count)
        {
            return;
        }

        pane.NavigationContext.ToggleMark(pane.Entries[selectedRow]);
        pane.PathPanel.Summary = pane.NavigationContext.MarkedSummary;
        pane.PathPanel.NeedsDraw = true;
        MoveSelection(pane, 1);
    }

    private ColorScheme GetRowColorScheme(Pane pane, int rowIndex)
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
        var normal = new TuiAttribute(foreground, _palette.PaneBackground);
        // the current entry is only shown in the active pane
        var selected = pane == _activePane && rowIndex == pane.TableView.SelectedRow
            ? isMarked ? _palette.MarkedCursor : _palette.Cursor
            : normal;

        return CreateColorScheme(normal, selected);
    }

    private static ColorScheme CreateColorScheme(
        TuiAttribute normal,
        TuiAttribute focus,
        TuiAttribute? hotNormal = null,
        TuiAttribute? hotFocus = null) =>
        new()
        {
            Normal = normal,
            Focus = focus,
            HotNormal = hotNormal ?? normal,
            HotFocus = hotFocus ?? focus,
            Disabled = normal
        };

    /// <summary>
    ///     Asks on the UI thread whether to overwrite an existing file, called from the copy running in the background.
    ///     Esc answers no.
    /// </summary>
    private static Task<OverwriteAnswer> ConfirmOverwriteAsync(OverwriteRequest request, CancellationToken _)
    {
        var answer = new TaskCompletionSource<OverwriteAnswer>(TaskCreationOptions.RunContinuationsAsynchronously);
        Application.Invoke(() =>
        {
            try
            {
                var button = MessageBox.Query("Overwrite", request.Message, "_Yes", "_No", "_All");
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
        ListingProgressView progress)
    {
        public NavigationContext NavigationContext { get; } = navigationContext;
        public View View { get; } = view;
        public PathPanelView PathPanel { get; } = pathPanel;
        public EntryTableView TableView { get; } = tableView;
        public Label BottomLine { get; } = bottomLine;
        public ListingProgressView Progress { get; } = progress;
        public IReadOnlyList<IEntry> Entries { get; set; } = [];
        public DataTable? DataTable { get; set; }
    }
}
