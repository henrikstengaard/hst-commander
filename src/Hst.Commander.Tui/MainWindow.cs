using System.Data;
using Hst.Commander.Core;
using Hst.Commander.Core.Volumes;
using Hst.Commander.Extensions.Volume.Abstractions;
using Terminal.Gui;
using TuiAttribute = Terminal.Gui.Attribute;

namespace Hst.Commander.Tui;

public sealed class MainWindow : Window
{
    private const int SizeColumnWidth = 8;
    private const int ModifiedColumnWidth = 16;
    private const int ColumnCount = 3;

    private readonly MenuBar _menuBar;
    private readonly Pane _leftPane;
    private readonly Pane _rightPane;
    private TuiPalette _palette;
    private Pane _activePane;

    public MainWindow(string initialPath, TuiTheme theme)
    {
        BorderStyle = LineStyle.None;
        X = 0;
        Y = 0;
        Width = Dim.Fill();
        Height = Dim.Fill();
        _palette = TuiPalette.For(theme);

        _menuBar = new MenuBar
        {
            Menus =
            [
                new MenuBarItem("_File", new MenuItem[]
                {
                    new("_Copy...", "F5", CopyFromActivePane),
                    null!,
                    new("_Settings...", string.Empty, ShowSettings),
                    null!,
                    new("_Quit", string.Empty, () => Application.RequestStop())
                })
            ]
        };
        _leftPane = CreatePane(new NavigationContext(initialPath), 0);
        _rightPane = CreatePane(new NavigationContext(initialPath), 50);
        _activePane = _leftPane;

        Add(_menuBar, _leftPane.View, _rightPane.View);
        ApplyTheme(theme);
        ConfigurePaneKeys(_leftPane, _rightPane);
        ConfigurePaneKeys(_rightPane, _leftPane);
        Initialized += (_, _) => _leftPane.TableView.SetFocus();
    }

    public async Task InitializeAsync()
    {
        await InitializePaneAsync(_leftPane);
        await InitializePaneAsync(_rightPane);
    }

    public void DisposeNavigationContexts()
    {
        _leftPane.NavigationContext.Dispose();
        _rightPane.NavigationContext.Dispose();
    }

    /// <summary>Applies the colors of a theme to the menu bar and both panes.</summary>
    public void ApplyTheme(TuiTheme theme)
    {
        _palette = TuiPalette.For(theme);
        _menuBar.ColorScheme = _palette.MenuColorScheme;
        _menuBar.NeedsDraw = true;
        foreach (var pane in new[] { _leftPane, _rightPane })
        {
            ApplyPaneColors(pane);
        }

        NeedsDraw = true;
    }

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
            Height = Dim.Fill(),
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

        var pane = new Pane(navigationContext, view, pathPanel, tableView, bottomLine);
        tableView.Style = new TableStyle
        {
            ShowVerticalCellLines = true,
            ShowHorizontalHeaderOverline = false,
            AlwaysShowHeaders = true,
            ExpandLastColumn = false,
            RowColorGetter = args => GetRowColorScheme(pane, args.RowIndex)
        };
        tableView.FrameChanged += (_, _) => UpdatePaneColumns(pane);

        view.Add(pathPanel, tableView, bottomLine);

        return pane;
    }

    private void ConfigurePaneKeys(Pane pane, Pane otherPane)
    {
        pane.TableView.CellActivated += async (_, _) => await ActivateSelectedEntryAsync(pane);
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
            else if (key.KeyCode == KeyCode.Backspace)
            {
                key.Handled = true;
                _ = RunNavigationAsync(pane, () => pane.NavigationContext.GoParentAsync());
            }
            else if (key.KeyCode == KeyCode.F5)
            {
                key.Handled = true;
                _ = CopyAsync(pane, otherPane);
            }
        };
    }

    private void CopyFromActivePane() => _ = CopyAsync(_activePane, _activePane == _leftPane ? _rightPane : _leftPane);

    /// <summary>
    ///     Copies the marked entries, or the entry under the cursor, to the other pane after confirming the destination
    ///     and filter in the copy dialog, showing the progress while copying.
    /// </summary>
    private async Task CopyAsync(Pane pane, Pane otherPane)
    {
        var selectedRow = pane.TableView.SelectedRow;
        var currentEntry = selectedRow >= 0 && selectedRow < pane.Entries.Count ? pane.Entries[selectedRow] : null;
        var entries = pane.NavigationContext.GetOperationEntries(currentEntry);
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

    private async Task ActivateSelectedEntryAsync(Pane pane)
    {
        var selectedRow = pane.TableView.SelectedRow;
        if (selectedRow < 0 || selectedRow >= pane.Entries.Count)
        {
            return;
        }

        await RunNavigationAsync(pane, () => pane.NavigationContext.OpenEntryAsync(pane.Entries[selectedRow]));
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
        pane.Entries = await pane.NavigationContext.ListEntriesAsync();
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
            table.Rows.Add(entry.Name, size, modifiedDate);
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
                pane.Entries[rowIndex].Name,
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
            : entry.Type == EntryType.Container ? _palette.Container : _palette.Item;
        var normal = new TuiAttribute(foreground, _palette.PaneBackground);
        var selected = rowIndex == pane.TableView.SelectedRow
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
        Label bottomLine)
    {
        public NavigationContext NavigationContext { get; } = navigationContext;
        public View View { get; } = view;
        public PathPanelView PathPanel { get; } = pathPanel;
        public EntryTableView TableView { get; } = tableView;
        public Label BottomLine { get; } = bottomLine;
        public IReadOnlyList<IEntry> Entries { get; set; } = [];
        public DataTable? DataTable { get; set; }
    }
}
