using System.Data;
using System.Diagnostics;
using Hst.Commander.Core;
using Hst.Commander.Extensions.Volume.Abstractions;
using Terminal.Gui;
using TuiAttribute = Terminal.Gui.Attribute;

namespace Hst.Commander.Console;

public sealed class MainWindow : Window
{
    private readonly Pane _leftPane;
    private readonly Pane _rightPane;

    public MainWindow(string initialPath)
    {
        Title = "HST Commander - Enter opens, Backspace goes up, Tab switches panes, Ctrl+Q quits";
        X = 0;
        Y = 0;
        Width = Dim.Fill();
        Height = Dim.Fill();

        _leftPane = CreatePane("Left", new NavigationContext(initialPath), 0);
        _rightPane = CreatePane("Right", new NavigationContext(initialPath), 50);

        var help = new Label
        {
            X = 0,
            Y = Pos.AnchorEnd(),
            Width = Dim.Fill(),
            Text = "Up/Down: select (hold to accelerate)    PgUp/PgDn: page    Home/End: first/last    Enter: open    Backspace: parent    Tab: pane    Ctrl+Q: quit",
            ColorScheme = CreateColorScheme(
                new TuiAttribute(ColorName16.White, ColorName16.Blue),
                new TuiAttribute(ColorName16.BrightYellow, ColorName16.Blue))
        };

        Add(_leftPane.View, _rightPane.View, help);
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

    private static Pane CreatePane(string name, NavigationContext navigationContext, int xPercent)
    {
        var view = new View
        {
            X = Pos.Percent(xPercent),
            Y = 0,
            Width = Dim.Percent(50),
            Height = Dim.Fill(1),
            CanFocus = true
        };
        var pathLabel = new Label
        {
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Text = $"{name}: {navigationContext.CurrentPath}",
            ColorScheme = CreateColorScheme(
                new TuiAttribute(ColorName16.BrightYellow, ColorName16.Blue),
                new TuiAttribute(ColorName16.BrightYellow, ColorName16.Blue))
        };
        var tableView = new TableView
        {
            X = 0,
            Y = 1,
            Width = Dim.Fill(),
            Height = Dim.Fill(),
            FullRowSelect = true,
            CellActivationKey = KeyCode.Enter,
            ColorScheme = CreateColorScheme(
                new TuiAttribute(ColorName16.White, ColorName16.Blue),
                new TuiAttribute(ColorName16.Black, ColorName16.BrightCyan),
                new TuiAttribute(ColorName16.BrightYellow, ColorName16.Blue),
                new TuiAttribute(ColorName16.Black, ColorName16.BrightCyan))
        };

        var pane = new Pane(name, navigationContext, view, pathLabel, tableView);
        tableView.Style = new TableStyle
        {
            ShowVerticalCellLines = false,
            RowColorGetter = args => GetRowColorScheme(pane, args.RowIndex)
        };

        view.Add(pathLabel, tableView);

        return pane;
    }

    private void ConfigurePaneKeys(Pane pane, Pane otherPane)
    {
        pane.TableView.CellActivated += async (_, _) => await ActivateSelectedEntryAsync(pane);
        pane.TableView.HasFocusChanged += (_, _) =>
        {
            pane.TableView.NeedsDraw = true;
        };
        pane.TableView.SelectedCellChanged += (_, _) => pane.TableView.NeedsDraw = true;
        pane.TableView.KeyUp += (_, key) =>
        {
            if (key.KeyCode == pane.LastMovementKey)
            {
                pane.LastMovementKey = null;
                pane.RepeatedKeyDowns = 0;
            }
        };
        pane.TableView.KeyDown += (_, key) =>
        {
            if (key.KeyCode == KeyCode.Tab)
            {
                otherPane.TableView.SetFocus();
                key.Handled = true;
            }
            else if (key.KeyCode == KeyCode.CursorUp || key.KeyCode == KeyCode.CursorDown)
            {
                var now = Stopwatch.GetTimestamp();
                var elapsed = Stopwatch.GetElapsedTime(pane.LastMovementAt, now);
                pane.RepeatedKeyDowns = pane.LastMovementKey == key.KeyCode &&
                                        elapsed <= TimeSpan.FromMilliseconds(250)
                    ? pane.RepeatedKeyDowns + 1
                    : 0;
                pane.LastMovementKey = key.KeyCode;
                pane.LastMovementAt = now;

                var repeatExponent = Math.Min(3, pane.RepeatedKeyDowns / 2);
                var movement = 1 << repeatExponent;
                MoveSelection(pane, key.KeyCode == KeyCode.CursorUp ? -movement : movement);
                key.Handled = true;
            }
            else if (key.KeyCode == KeyCode.PageUp || key.KeyCode == KeyCode.PageDown)
            {
                pane.LastMovementKey = null;
                pane.RepeatedKeyDowns = 0;
                MoveSelection(pane, GetPageSize(pane) * (key.KeyCode == KeyCode.PageUp ? -1 : 1));
                key.Handled = true;
            }
            else if (key.KeyCode == KeyCode.Home || key.KeyCode == KeyCode.End)
            {
                pane.LastMovementKey = null;
                pane.RepeatedKeyDowns = 0;
                MoveSelection(pane, key.KeyCode == KeyCode.Home ? 0 : pane.Entries.Count - 1,
                    key.KeyCode == KeyCode.Home || key.KeyCode == KeyCode.End);
                key.Handled = true;
            }
            else if (key.KeyCode == KeyCode.Backspace)
            {
                pane.LastMovementKey = null;
                pane.RepeatedKeyDowns = 0;
                key.Handled = true;
                _ = RunNavigationAsync(pane, () => pane.NavigationContext.GoParentAsync());
            }
        };
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
        pane.Entries = await pane.NavigationContext.ListEntriesAsync();
        pane.PathLabel.Text = $"{pane.Name}: {pane.NavigationContext.CurrentPath}";

        var table = new DataTable();
        table.Columns.Add("Name", typeof(string));
        table.Columns.Add("Size", typeof(string));
        table.Columns.Add("Modified", typeof(string));

        foreach (var entry in pane.Entries)
        {
            var displayName = entry.Name;
            var size = entry.Size?.ToString("N0") ?? string.Empty;
            var modifiedDate = entry.ModifiedDate?.ToString("yyyy-MM-dd HH:mm") ?? string.Empty;
            table.Rows.Add(displayName, size, modifiedDate);
        }

        pane.TableView.Table = new DataTableSource(table);
        if (pane.Entries.Count > 0)
        {
            pane.TableView.SelectedRow = Math.Clamp(pane.TableView.SelectedRow, 0, pane.Entries.Count - 1);
        }
    }

    private static int GetPageSize(Pane pane)
    {
        var headerRows = pane.TableView.Style.ShowHeaders || pane.TableView.Style.AlwaysShowHeaders ? 1 : 0;
        return Math.Max(1, pane.TableView.Viewport.Height - headerRows);
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
        pane.TableView.NeedsDraw = true;
    }

    private static ColorScheme GetRowColorScheme(Pane pane, int rowIndex)
    {
        if (rowIndex < 0 || rowIndex >= pane.Entries.Count)
        {
            return CreateColorScheme(
                new TuiAttribute(ColorName16.White, ColorName16.Blue),
                new TuiAttribute(ColorName16.Black, ColorName16.BrightCyan));
        }

        var entry = pane.Entries[rowIndex];
        var foreground = entry.Type == EntryType.Container ? ColorName16.White : ColorName16.Gray;
        var normal = new TuiAttribute(foreground, ColorName16.Blue);
        var selected = rowIndex == pane.TableView.SelectedRow
            ? new TuiAttribute(ColorName16.Black, ColorName16.Gray)
            : new TuiAttribute(foreground, ColorName16.Blue);

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

    private sealed class Pane(
        string name,
        NavigationContext navigationContext,
        View view,
        Label pathLabel,
        TableView tableView)
    {
        public string Name { get; } = name;
        public NavigationContext NavigationContext { get; } = navigationContext;
        public View View { get; } = view;
        public Label PathLabel { get; } = pathLabel;
        public TableView TableView { get; } = tableView;
        public IReadOnlyList<IEntry> Entries { get; set; } = [];
        public KeyCode? LastMovementKey { get; set; }
        public long LastMovementAt { get; set; }
        public int RepeatedKeyDowns { get; set; }
    }
}
