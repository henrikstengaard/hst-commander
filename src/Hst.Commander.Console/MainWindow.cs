namespace Hst.Commander.Console;

using System.Data;
using System.Diagnostics;
using Plugins;
using Terminal.Gui;

public sealed class MainWindow : Window
{
    //public readonly Toplevel Top;
    private MenuBar _menu;
    private int _nameColumnWidth;
    private FrameView _leftPane;
    private List<string> _categories;
    private ListView _leftEntryListView;

    private FrameView _rightPane;
    private ListView _rightEntryListView;

    private TableView leftTable;
    private TableView rightTable;

    // private static StatusBar _statusBar;
    // private static StatusItem _capslock;
    // private static StatusItem _numlock;
    // private static StatusItem _scrolllock;
    //private static int _leftEntryListViewItem;
    //private static int _rightEntryListViewItem;
    private NavigationContext leftContext;
    private NavigationContext rightContext;

    private DataTable leftDataTable;
    private DataTableSource leftDataTableSource;
    private DataTable rightDataTable;
    private DataTableSource rightDataTableSource;

    private Stack<string> leftLocations;

    private EntryListDataSource leftDataSource;
    private EntryListDataSource rightDataSource;

    public MainWindow()
    {
        var d = new DiskDriveContainerProvider();

        leftContext = new NavigationContext();
        leftContext.Providers.Push(new Provider(d));

        rightContext = new NavigationContext();
        rightContext.Providers.Push(new Provider(d));

        var colorScheme = Colors.ColorSchemes["Base"];

        // var _leftPane = new FrameView("Left")
        // {
        //     X = 0,
        //     Y = 0,
        //     Width = Dim.Percent(50),
        //     Height = Dim.Fill(),
        //     CanFocus = false,
        //     Shortcut = Key.CtrlMask | Key.L
        // };
        // _leftPane.Title = $"{_leftPane.Title} ({_leftPane.ShortcutTag})";
        // _leftPane.ShortcutAction = () => _leftPane.SetFocus();
        //
        // leftDataSource = new EntryListDataSource(Enumerable.Empty<IEntry>());
        // _leftEntryListView = new ListView(leftDataSource)
        // {
        //     X = 0,
        //     Y = 0,
        //     Width = Dim.Fill(),
        //     Height = Dim.Fill(),
        //     AllowsMarking = false,
        //     CanFocus = true,
        // };
        // _leftEntryListView.OpenSelectedItem += async args =>
        // {
        //     if (args.Value is not IEntry { Type: EntryType.Container } entry)
        //     {
        //         return;
        //     }
        //
        //     _leftEntryListView.SelectedItem = 0;
        //     await leftContext.Locations.Peek().Provider.ChangePath(entry.Name);
        //     await RefreshLeft();
        // };
        //
        // _leftPane.Add(_leftEntryListView);
        //
        //
        // var _rightPane = new FrameView("Right")
        // {
        //     X = Pos.Percent(50),
        //     Y = 0,
        //     Width = Dim.Percent(50),
        //     Height = Dim.Fill(),
        //     CanFocus = true,
        //     Shortcut = Key.CtrlMask | Key.R
        // };
        // _rightPane.Title = $"{_rightPane.Title} ({_rightPane.ShortcutTag})";
        // _rightPane.ShortcutAction = () => _rightPane.SetFocus();
        //
        // rightDataSource = new EntryListDataSource(Enumerable.Empty<IEntry>());
        // _rightEntryListView = new ListView(rightDataSource)
        // {
        //     X = 0,
        //     Y = 0,
        //     Width = Dim.Fill(),
        //     Height = Dim.Fill(),
        //     AllowsMarking = false,
        //     CanFocus = true,
        // };
        // _rightEntryListView.OpenSelectedItem += async args =>
        // {
        //     if (args.Value is not IEntry { Type: EntryType.Container } entry)
        //     {
        //         return;
        //     }
        //
        //     _rightEntryListView.SelectedItem = 0;
        //     await rightContext.Locations.Peek().Provider.ChangePath(entry.Name);
        //     await RefreshRight();
        // };

        //_rightPane.Add(_rightEntryListView);
        // _rightPane.Add(table);

        // var window = new Window($"Hst Commander")
        // {
        //     X = 0,
        //     Y = 0,
        //     Width = Dim.Fill(),
        //     Height = Dim.Fill(),
        //     //ColorScheme = colorScheme,
        // };
        //window.Add(_leftPane);


        leftDataTable = new DataTable();
        leftDataTable.Columns.Add(new DataColumn("Name", typeof(string)));
        leftDataTable.Columns.Add(new DataColumn("Size", typeof(string)));
        leftDataTable.Columns.Add(new DataColumn("Date", typeof(string)));

        leftDataTableSource = new DataTableSource(leftDataTable);

        rightDataTable = new DataTable();
        rightDataTable.Columns.Add(new DataColumn("Name", typeof(string)));
        rightDataTable.Columns.Add(new DataColumn("Size", typeof(string)));
        rightDataTable.Columns.Add(new DataColumn("Date", typeof(string)));

        rightDataTableSource = new DataTableSource(rightDataTable);

        var label1 = new Label
        {
            X = 0,
            Y = 0,
            Width = Dim.Percent(50),
            Height = 1,
            ColorScheme = colorScheme,
            Text = "Hst Commander"
        };

        var label2 = new Label
        {
            X = Pos.Percent(50),
            Y = 0,
            Width = Dim.Percent(50),
            Height = 1,
            ColorScheme = colorScheme
        };

        leftTable = new TableView(leftDataTableSource)
        {
            X = 0,
            Y = 1,
            Width = Dim.Percent(50),
            Height = Dim.Fill(),
            FullRowSelect = true,
            MultiSelect = true,
            Style = new TableStyle
            {
                AlwaysShowHeaders = true,
                ShowHorizontalHeaderOverline = false,
                ShowHorizontalScrollIndicators = true,
                SmoothHorizontalScrolling = true,
                ColumnStyles = new Dictionary<int, ColumnStyle>
                {
                    {
                        0, new ColumnStyle
                        {
                            Alignment = Alignment.Start,
                        }
                    },
                    {
                        1, new ColumnStyle
                        {
                            Alignment = Alignment.End,
                            MinWidth = 5,
                            MaxWidth = 5
                        }
                    },
                    {
                        2, new ColumnStyle
                        {
                            Alignment = Alignment.Start,
                            MinWidth = 10,
                            MaxWidth = 10
                        }
                    }
                }
            },
            ColorScheme = colorScheme
        };
        leftTable.KeyDown += async (_, key) =>
        {
            if (key == Key.Enter)
            {
                if (leftTable.SelectedRow == 0 && leftContext.HasParent)
                {
                    await Parent(leftTable, leftDataTable, leftContext);
                    return;
                }

                var row = leftDataTable.Rows[leftTable.SelectedRow];
                var name = row.ItemArray[0] as string ?? string.Empty;
                if (name.StartsWith("/"))
                {
                    await Enter(leftTable, leftDataTable, leftContext, name.Substring(1), leftTable.SelectedRow);
                    return;
                }

                Process.Start(new ProcessStartInfo
                {
                    UseShellExecute = true,
                    FileName = name.Substring(1),
                    WorkingDirectory = leftContext.Providers.Peek().GetLocation().Path
                });
            }

            if (key == Key.Backspace)
            {
                await Parent(leftTable, leftDataTable, leftContext);
            }
        };

        rightTable = new TableView(rightDataTableSource)
        {
            X = Pos.Percent(50),
            Y = 1,
            Width = Dim.Percent(50),
            Height = Dim.Fill(),
            FullRowSelect = true,
            MultiSelect = true,
            Style = new TableStyle
            {
                AlwaysShowHeaders = true,
                ShowHorizontalHeaderOverline = false,
                ShowHorizontalScrollIndicators = true,
                SmoothHorizontalScrolling = true,
                ColumnStyles = new Dictionary<int, ColumnStyle>
                {
                    {
                        0, new ColumnStyle
                        {
                            Alignment = Alignment.Start
                        }
                    },
                    {
                        1, new ColumnStyle
                        {
                            Alignment = Alignment.End,
                            MinWidth = 5,
                            MaxWidth = 5
                        }
                    },
                    {
                        2, new ColumnStyle
                        {
                            Alignment = Alignment.Start,
                            MinWidth = 10,
                            MaxWidth = 10
                        }
                    }
                }
            },
            ColorScheme = colorScheme
        };
        rightTable.KeyDown += async (_, key) =>
        {
            if (key == Key.Enter)
            {
                if (rightTable.SelectedRow == 0 && rightContext.HasParent)
                {
                    await Parent(rightTable, rightDataTable, rightContext);
                    return;
                }

                var row = rightDataTable.Rows[rightTable.SelectedRow];
                var name = row.ItemArray[0] as string ?? string.Empty;
                if (name.StartsWith("/"))
                {
                    await Enter(rightTable, rightDataTable, rightContext, name.Substring(1), rightTable.SelectedRow);
                    return;
                }
                    
                // is file a known container?
                var path = rightContext.Providers.Peek().GetLocation().Path;
                    
                Process.Start(new ProcessStartInfo
                {
                    UseShellExecute = true,
                    FileName = name.Substring(1),
                    WorkingDirectory = rightContext.Providers.Peek().GetLocation().Path
                });
            }
            
            if (key == Key.Backspace)
            {
                await Parent(rightTable, rightDataTable, rightContext);
            }
        };

        Add(label1);
        Add(leftTable);
        Add(label2);
        Add(rightTable);
        
        SetBorderStyle(LineStyle.None);
        
        Loaded += async (_, _) =>
        {
            await Refresh();
        };

        this.KeyUp += (_, e) =>
        {
            if (e.KeyCode == (KeyCode.CtrlMask | KeyCode.G))
            {
                var tf = new TextField();

                var ok = new Button
                {
                    Text = "OK"
                };
                
                var dialog = new Dialog()
                {
                    Title = "Goto location",
                    Text = "Enter location:",
                    Buttons = [ok],
                    ColorScheme = colorScheme
                };

                ok.Accepting += (sender, args) =>
                {
                    // close dialog
                    Application.RequestStop();
                    args.Cancel = true;
                };
                
                dialog.Add(tf);
                
                //MessageBox.Query ("Message", "Question?", "Yes", "No");

                Application.Run(dialog);
                dialog.Dispose();
            }
        };
    }

    public void Adjust()
    {
        var leftNameStyle = leftTable.Style.ColumnStyles[0];
        // leftNameStyle.MinWidth = leftTable.Bounds.Width - 5 - 10 - 4;
        // leftNameStyle.MaxWidth = leftTable.Bounds.Width - 5 - 10 - 4;
        
        var rightNameStyle = rightTable.Style.ColumnStyles[0];
        // rightNameStyle.MinWidth = rightTable.Bounds.Width - 5 - 10 - 4;
        // rightNameStyle.MaxWidth = rightTable.Bounds.Width - 5 - 10 - 4;
    }

    private async Task Enter(TableView table, DataTable dataTable, NavigationContext context, string path, int cursorPosition)
    {
        try
        {
            var provider = context.Providers.Peek();
            var location = provider.PrepareLocation(path, cursorPosition);
            var entries = await provider.ContainerProvider.GetEntries(location.Path);
            provider.AddLocation(location);
            UpdateDataTable(table, dataTable, context, entries, 0);
        }
        catch (Exception e)
        {
            MessageBox.ErrorQuery("Error", e.Message, "OK");
        }
    }

    private async Task Parent(TableView table, DataTable dataTable, NavigationContext context)
    {
        var prevLocation = context.Parent();
        var provider = context.Providers.Peek();
        var currentLocation = provider.GetLocation();
        var entries = await provider.ContainerProvider.GetEntries(currentLocation.Path);
        UpdateDataTable(table, dataTable, context, entries, prevLocation.CursorPosition);
    }

    private async Task Refresh(TableView table, DataTable dataTable, NavigationContext context)
    {
        var provider = context.Providers.Peek();
        var location = provider.GetLocation();
        var entries = await provider.ContainerProvider.GetEntries(location.Path);
        UpdateDataTable(table, dataTable, context, entries, location.CursorPosition);
    }

    private void UpdateDataTable(TableView tableView, DataTable dataTable, NavigationContext context,
        IEnumerable<IEntry> entries, int currentEntry)
    {
        dataTable.Clear();

        if (context.HasParent)
        {
            var row = dataTable.NewRow();
            row[0] = "/..";
            row[1] = "";
            row[2] = "";
            dataTable.Rows.Add(row);
        }

        foreach (var entry in entries)
        {
            var row = dataTable.NewRow();
            row[0] = string.Concat(entry.Type == EntryType.Container ? "/" : " ", entry.Name);
            row[1] = "";
            row[2] = entry.Date?.ToShortDateString() ?? string.Empty;
            dataTable.Rows.Add(row);
        }

        tableView.SelectedRow = currentEntry;

        SetNeedsLayout();
    }

    private async Task Refresh()
    {
        await Refresh(leftTable, leftDataTable, leftContext);
        await Refresh(rightTable, rightDataTable, rightContext);
    }
}