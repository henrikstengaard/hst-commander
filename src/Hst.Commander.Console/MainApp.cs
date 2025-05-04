namespace Hst.Commander.Console;

using System.Data;
using System.Diagnostics;
using Plugins;
using Terminal.Gui;

public class MainApp
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
    private DataTable rightDataTable;

    private Stack<string> leftLocations;

    private EntryListDataSource leftDataSource;
    private EntryListDataSource rightDataSource;

    public MainApp()
    {
        var d = new DiskDriveContainerProvider();

        leftContext = new NavigationContext();
        leftContext.Providers.Push(new Provider(d));

        rightContext = new NavigationContext();
        rightContext.Providers.Push(new Provider(d));

        var colorScheme = Colors.Base;

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

        rightDataTable = new DataTable();
        rightDataTable.Columns.Add(new DataColumn("Name", typeof(string)));
        rightDataTable.Columns.Add(new DataColumn("Size", typeof(string)));
        rightDataTable.Columns.Add(new DataColumn("Date", typeof(string)));

        var label1 = new Label
        {
            X = 0,
            Y = 0,
            Width = Dim.Percent(50),
            Height = 1,
            ColorScheme = colorScheme
        };

        var label2 = new Label
        {
            X = Pos.Percent(50),
            Y = 0,
            Width = Dim.Percent(50),
            Height = 1,
            ColorScheme = colorScheme
        };

        leftTable = new TableView(leftDataTable)
        {
            X = 0,
            Y = 1,
            Width = Dim.Percent(50),
            Height = Dim.Fill(),
            FullRowSelect = true,
            MultiSelect = true,
            Style = new TableView.TableStyle
            {
                AlwaysShowHeaders = true,
                
                ShowHorizontalHeaderOverline = false,
                ShowHorizontalScrollIndicators = true,
                SmoothHorizontalScrolling = true,
                ColumnStyles = new Dictionary<DataColumn, TableView.ColumnStyle>
                {
                    {
                        leftDataTable.Columns["Name"], new TableView.ColumnStyle
                        {
                            Alignment = TextAlignment.Left,
                        }
                    },
                    {
                        leftDataTable.Columns["Size"], new TableView.ColumnStyle
                        {
                            Alignment = TextAlignment.Right,
                            MinWidth = 5,
                            MaxWidth = 5
                        }
                    },
                    {
                        leftDataTable.Columns["Date"], new TableView.ColumnStyle
                        {
                            Alignment = TextAlignment.Left,
                            MinWidth = 10,
                            MaxWidth = 10
                        }
                    }
                }
            },
            ColorScheme = colorScheme
        };
        leftTable.KeyDown += async args =>
        {
            switch (args.KeyEvent.Key)
            {
                case Key.Enter:
                    if (leftTable.SelectedRow == 0 && leftContext.HasParent)
                    {
                        await this.Parent(leftTable, leftDataTable, leftContext);
                        return;
                    }

                    var row = leftDataTable.Rows[leftTable.SelectedRow];
                    var name = row.ItemArray[0] as string ?? string.Empty;
                    if (name.StartsWith("/"))
                    {
                        await this.Enter(leftTable, leftDataTable, leftContext, name.Substring(1), leftTable.SelectedRow);
                        return;
                    }
                    
                    Process.Start(new ProcessStartInfo
                    {
                        UseShellExecute = true,
                        FileName = name.Substring(1),
                        WorkingDirectory = leftContext.Providers.Peek().GetLocation().Path
                    });

                    break;
                case Key.Backspace:
                    await this.Parent(leftTable, leftDataTable, leftContext);
                    break;
            }
        };

        rightTable = new TableView(rightDataTable)
        {
            X = Pos.Percent(50),
            Y = 1,
            Width = Dim.Percent(50),
            Height = Dim.Fill(),
            FullRowSelect = true,
            MultiSelect = true,
            Style = new TableView.TableStyle
            {
                AlwaysShowHeaders = true,
                ShowHorizontalHeaderOverline = false,
                ShowHorizontalScrollIndicators = true,
                SmoothHorizontalScrolling = true,
                ColumnStyles = new Dictionary<DataColumn, TableView.ColumnStyle>
                {
                    {
                        rightDataTable.Columns["Name"], new TableView.ColumnStyle
                        {
                            Alignment = TextAlignment.Left
                        }
                    },
                    {
                        rightDataTable.Columns["Size"], new TableView.ColumnStyle
                        {
                            Alignment = TextAlignment.Right,
                            MinWidth = 5,
                            MaxWidth = 5
                        }
                    },
                    {
                        rightDataTable.Columns["Date"], new TableView.ColumnStyle
                        {
                            Alignment = TextAlignment.Left,
                            MinWidth = 10,
                            MaxWidth = 10
                        }
                    }
                }
            },
            ColorScheme = colorScheme
        };
        rightTable.KeyDown += async args =>
        {
            switch (args.KeyEvent.Key)
            {
                case Key.Enter:
                    if (rightTable.SelectedRow == 0 && rightContext.HasParent)
                    {
                        await this.Parent(rightTable, rightDataTable, rightContext);
                        return;
                    }

                    var row = rightDataTable.Rows[rightTable.SelectedRow];
                    var name = row.ItemArray[0] as string ?? string.Empty;
                    if (name.StartsWith("/"))
                    {
                        await this.Enter(rightTable, rightDataTable, rightContext, name.Substring(1), rightTable.SelectedRow);
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

                    break;
                case Key.Backspace:
                    await this.Parent(rightTable, rightDataTable, rightContext);
                    break;
            }
        };


        //window.Add(table);

        var top = Application.Top;
// _top.KeyDown += KeyDownHandler;
//top.Add (window);
        top.Add(label1);
        top.Add(leftTable);
        top.Add(label2);
        top.Add(rightTable);
// _top.Add (_statusBar);

        //Application.Run(top);
    }

    public void Adjust()
    {
        Application.MainLoop.Invoke(() =>
        {
            var leftNameStyle = leftTable.Style.ColumnStyles[leftTable.Table.Columns["Name"]];
            leftNameStyle.MinWidth = leftTable.Bounds.Width - 5 - 10 - 4;
            leftNameStyle.MaxWidth = leftTable.Bounds.Width - 5 - 10 - 4;
        
            var rightNameStyle = rightTable.Style.ColumnStyles[rightTable.Table.Columns["Name"]];
            rightNameStyle.MinWidth = rightTable.Bounds.Width - 5 - 10 - 4;
            rightNameStyle.MaxWidth = rightTable.Bounds.Width - 5 - 10 - 4;
        });
    }

    public async Task Enter(TableView table, DataTable dataTable, NavigationContext context, string path, int cursorPosition)
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

    public async Task Parent(TableView table, DataTable dataTable, NavigationContext context)
    {
        var prevLocation = context.Parent();
        var provider = context.Providers.Peek();
        var currentLocation = provider.GetLocation();
        var entries = await provider.ContainerProvider.GetEntries(currentLocation.Path);
        UpdateDataTable(table, dataTable, context, entries, prevLocation.CursorPosition);
    }

    public async Task Refresh(TableView table, DataTable dataTable, NavigationContext context)
    {
        var provider = context.Providers.Peek();
        var location = provider.GetLocation();
        var entries = await provider.ContainerProvider.GetEntries(location.Path);
        UpdateDataTable(table, dataTable, context, entries, location.CursorPosition);
    }

    public void UpdateDataTable(TableView tableView, DataTable dataTable, NavigationContext context,
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
            row[2] = entry.Date.ToShortDateString();
            dataTable.Rows.Add(row);
        }

        tableView.SelectedRow = currentEntry;

        Application.MainLoop.Invoke(tableView.SetNeedsDisplay);
    }

    public async Task Refresh()
    {
        await this.Refresh(leftTable, leftDataTable, leftContext);
        await this.Refresh(rightTable, rightDataTable, rightContext);
    }

    // public Task Refresh()
    // {
    //     Application.MainLoop.Invoke(Action);
    //     return Task.CompletedTask;
    // }
    //
    // private async Task RefreshLeft()
    // {
    //     leftDataSource.SetEntries(await leftContext.Locations.Peek().Provider.GetEntries());
    //     Application.MainLoop.Invoke(() =>
    //     {
    //         _leftEntryListView.SetNeedsDisplay();
    //     });
    // }
    //
    // private async Task RefreshRight()
    // {
    //     rightDataSource.SetEntries(await rightContext.Locations.Peek().Provider.GetEntries());
    //     Application.MainLoop.Invoke(() =>
    //     {
    //         _rightEntryListView.SetNeedsDisplay();
    //     });
    // }
    //
    // private async void Action()
    // {
    //     leftDataSource.SetEntries(await leftContext.Locations.Peek().Provider.GetEntries());
    //     rightDataSource.SetEntries(await rightContext.Locations.Peek().Provider.GetEntries());
    //
    //     _leftEntryListView.SetNeedsDisplay();
    //     _rightEntryListView.SetNeedsDisplay();
    // }
}