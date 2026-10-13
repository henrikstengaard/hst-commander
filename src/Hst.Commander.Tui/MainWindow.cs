using System.Data;
using Hst.Commander.Core;
using Hst.Commander.Extensions.Volume.Abstractions;
using Terminal.Gui;
using TuiAttribute = Terminal.Gui.Attribute;

namespace Hst.Commander.Tui;

public sealed class MainWindow : Window
{
    private const int SizeColumnWidth = 8;
    private const int ModifiedColumnWidth = 16;
    private const int ColumnCount = 3;

    private readonly Pane _leftPane;
    private readonly Pane _rightPane;

    public MainWindow(string initialPath)
    {
        BorderStyle = LineStyle.None;
        X = 0;
        Y = 0;
        Width = Dim.Fill();
        Height = Dim.Fill();

        _leftPane = CreatePane(new NavigationContext(initialPath), 0);
        _rightPane = CreatePane(new NavigationContext(initialPath), 50);

        Add(_leftPane.View, _rightPane.View);
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

    private static Pane CreatePane(NavigationContext navigationContext, int xPercent)
    {
        var view = new View
        {
            X = Pos.Percent(xPercent),
            Y = 0,
            Width = Dim.Percent(50),
            Height = Dim.Fill(),
            CanFocus = true
        };
        var pathBorder = new PathBorderView
        {
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Height = 2,
            Path = navigationContext.CurrentPath,
            BorderAttribute = new TuiAttribute(ColorName16.White, ColorName16.Blue),
            PathAttribute = new TuiAttribute(ColorName16.BrightYellow, ColorName16.Blue),
            ActivePathAttribute = new TuiAttribute(ColorName16.Black, ColorName16.Gray)
        };
        var tableView = new EntryTableView
        {
            X = 0,
            Y = 2,
            Width = Dim.Fill(),
            Height = Dim.Fill(1),
            FullRowSelect = true,
            MaxCellWidth = int.MaxValue,
            CellActivationKey = KeyCode.Enter,
            ColorScheme = CreateColorScheme(
                new TuiAttribute(ColorName16.White, ColorName16.Blue),
                new TuiAttribute(ColorName16.Black, ColorName16.BrightCyan),
                new TuiAttribute(ColorName16.BrightYellow, ColorName16.Blue),
                new TuiAttribute(ColorName16.Black, ColorName16.BrightCyan))
        };

        var bottomLine = new Label
        {
            X = 0,
            Y = Pos.AnchorEnd(),
            Width = Dim.Fill(),
            ColorScheme = CreateColorScheme(
                new TuiAttribute(ColorName16.White, ColorName16.Blue),
                new TuiAttribute(ColorName16.White, ColorName16.Blue))
        };

        var pane = new Pane(navigationContext, view, pathBorder, tableView, bottomLine);
        tableView.Style = new TableStyle
        {
            ShowVerticalCellLines = true,
            ShowHorizontalHeaderOverline = false,
            AlwaysShowHeaders = true,
            ExpandLastColumn = false,
            RowColorGetter = args => GetRowColorScheme(pane, args.RowIndex)
        };
        tableView.FrameChanged += (_, _) => UpdatePaneColumns(pane);

        view.Add(pathBorder, tableView, bottomLine);

        return pane;
    }

    private void ConfigurePaneKeys(Pane pane, Pane otherPane)
    {
        pane.TableView.CellActivated += async (_, _) => await ActivateSelectedEntryAsync(pane);
        pane.TableView.HasFocusChanged += (_, _) =>
        {
            pane.TableView.NeedsDraw = true;
            pane.PathBorder.IsActive = pane.TableView.HasFocus;
            pane.PathBorder.NeedsDraw = true;
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
            else if (key.KeyCode == KeyCode.Backspace)
            {
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
        var previousPath = pane.PathBorder.Path;
        pane.Entries = await pane.NavigationContext.ListEntriesAsync();
        pane.PathBorder.Path = pane.NavigationContext.CurrentPath;
        pane.PathBorder.DiskSpace = pane.NavigationContext.DiskSpace;
        pane.PathBorder.NeedsDraw = true;

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
        pane.PathBorder.Dividers = pane.TableView.GetDividerPositions();
        pane.PathBorder.NeedsDraw = true;

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
        NavigationContext navigationContext,
        View view,
        PathBorderView pathBorder,
        EntryTableView tableView,
        Label bottomLine)
    {
        public NavigationContext NavigationContext { get; } = navigationContext;
        public View View { get; } = view;
        public PathBorderView PathBorder { get; } = pathBorder;
        public EntryTableView TableView { get; } = tableView;
        public Label BottomLine { get; } = bottomLine;
        public IReadOnlyList<IEntry> Entries { get; set; } = [];
        public DataTable? DataTable { get; set; }
    }
}
