using System.Globalization;
using Hst.Commander.Core;

namespace Hst.Commander.Tui;

/// <summary>
///     Modal search dialog (Ctrl+S): what to search for (wildcards or a regular expression), where to start (the active
///     pane's location), the contents of files (text, a regular expression or hex values), how deep and whether to
///     search inside images and archives. Search runs the search in the background, listing entries as they're found
///     in a table of path, size and modified date (Stop or Esc stops it); clicking a column header sorts by it, again
///     in descending order (<see cref="SearchResultSort" />). Enter on a result or Go to closes the dialog with the
///     result in <see cref="Result" />.
/// </summary>
public sealed class SearchDialog : TuiDialog
{
    private const int SizeWidth = 9;
    private const int ModifiedWidth = 16;

    private static readonly string[] ContentLabels = ["None", "Text", "Regular expression", "Hex"];
    private static readonly string[] DepthLabels = ["All subdirectories", "Current directory only", "Levels:"];

    private readonly Func<SearchOptions, IProgress<SearchResult>, IProgress<SearchProgress>, CancellationToken, Task>
        _search;

    private readonly TextField _nameField;
    private readonly TextField _startField;
    private readonly CheckBox _regexCheckBox;
    private readonly CheckBox _caseSensitiveCheckBox;
    private readonly CheckBox _imagesCheckBox;
    private readonly OptionSelector _contentRadioGroup;
    private readonly TextField _contentField;
    private readonly OptionSelector _depthRadioGroup;
    private readonly TextField _levelsField;
    private readonly Label _statusLabel;
    private readonly ResultsTableView _resultsView;
    private readonly ResultsTableSource _resultsSource;
    private readonly Button _searchButton;
    private readonly List<SearchResult> _results = [];
    private CancellationTokenSource? _cancellation;

    /// <param name="options">Options to start with; the start path is the active pane's location.</param>
    /// <param name="sort">Order of the results to start with.</param>
    /// <param name="search">Searches with the options, reporting results and progress (see <see cref="NavigationContext.SearchAsync" />).</param>
    /// <param name="palette">Palette of the theme.</param>
    public SearchDialog(SearchOptions options, SearchResultSort sort,
        Func<SearchOptions, IProgress<SearchResult>, IProgress<SearchProgress>, CancellationToken, Task> search,
        TuiPalette palette)
    {
        _search = search;
        Options = options;
        Sort = sort;
        Title = "Search";
        Width = Dim.Percent(80);
        Height = Dim.Percent(90);
        SetScheme(palette.DialogColorScheme);

        var nameLabel = new Label { X = 1, Y = 1, Text = "Search for (wildcards or regular expression):" };
        _nameField = new TextField { X = 1, Y = 2, Width = Dim.Fill(1), Text = options.NamePattern };
        var startLabel = new Label { X = 1, Y = 3, Text = "Start in:" };
        _startField = new TextField { X = 1, Y = 4, Width = Dim.Fill(1), Text = options.StartPath };
        _regexCheckBox = CreateCheckBox(1, 6, "_Regular expression", options.NameIsRegex);
        _caseSensitiveCheckBox = CreateCheckBox(Pos.Right(_regexCheckBox) + 3, 6, "C_ase sensitive",
            options.CaseSensitive);
        _imagesCheckBox = CreateCheckBox(Pos.Right(_caseSensitiveCheckBox) + 3, 6, "Search in _images and archives",
            options.SearchInImages);

        var contentLabel = new Label { X = 1, Y = 8, Text = "Contents:" };
        _contentRadioGroup = new OptionSelector
        {
            X = 11,
            Y = 8,
            Orientation = Orientation.Horizontal,
            Labels = ContentLabels,
            Value = (int)options.ContentType
        };
        _contentField = new TextField { X = 11, Y = 9, Width = Dim.Fill(1), Text = options.Content };

        var depthLabel = new Label { X = 1, Y = 11, Text = "Depth:" };
        _depthRadioGroup = new OptionSelector
        {
            X = 11,
            Y = 11,
            Orientation = Orientation.Horizontal,
            Labels = DepthLabels,
            Value = (int)options.Depth
        };
        _levelsField = new TextField
        {
            X = Pos.Right(_depthRadioGroup) + 1,
            Y = 11,
            Width = 5,
            Text = options.Levels.ToString(CultureInfo.InvariantCulture)
        };

        _statusLabel = new Label { X = 1, Y = 13, Width = Dim.Fill(1), Text = string.Empty };
        _resultsSource = new ResultsTableSource(_results) { Sort = sort };
        _resultsView = new ResultsTableView
        {
            X = 1,
            Y = 14,
            Width = Dim.Fill(1),
            Height = Dim.Fill(2),
            FullRowSelect = true,
            MaxCellWidth = int.MaxValue,
            Table = _resultsSource,
            Style = new TableStyle
            {
                ShowVerticalCellLines = true,
                ShowHorizontalHeaderOverline = false,
                AlwaysShowHeaders = true,
                ExpandLastColumn = false,
                // the headers are drawn in the dialog's color, also while the results have focus
                HeaderScheme = TuiPalette.CreateScheme(palette.Dialog, palette.Dialog)
            }
        };
        _resultsView.CellActivated += (_, _) => GoTo(_resultsView.SelectedRow);
        _resultsView.HeaderClicked += (_, column) => SortBy(SearchResultSort.Columns[column]);
        _resultsView.FrameChanged += (_, _) => UpdateColumns();

        _searchButton = new Button { Text = "Searc_h", IsDefault = true };
        _searchButton.Accepting += (_, e) =>
        {
            e.Handled = true;
            if (_cancellation != null)
            {
                _cancellation.Cancel();
            }
            else
            {
                StartSearch();
            }
        };
        var goToButton = new Button { Text = "_Go to" };
        goToButton.Accepting += (_, e) =>
        {
            e.Handled = true;
            GoTo(_resultsView.SelectedRow);
        };
        var closeButton = new Button { Text = "Close" };
        closeButton.Accepting += (_, e) =>
        {
            e.Handled = true;
            Application.RequestStop(this);
        };

        Add(nameLabel, _nameField, startLabel, _startField, _regexCheckBox, _caseSensitiveCheckBox, _imagesCheckBox,
            contentLabel, _contentRadioGroup, _contentField, depthLabel, _depthRadioGroup, _levelsField, _statusLabel,
            _resultsView);
        AddButton(_searchButton);
        AddButton(goToButton);
        AddButton(closeButton);
        IsRunningChanging += (_, e) =>
        {
            if (!e.NewValue)
            {
                _cancellation?.Cancel();
            }
        };
        Initialized += (_, _) => _nameField.SetFocus();
    }

    /// <summary>Options of the last search, or those the dialog started with when not searched.</summary>
    public SearchOptions Options { get; private set; }

    /// <summary>Order of the results, changed by clicking the column headers.</summary>
    public SearchResultSort Sort { get; private set; }

    /// <summary>Result to go to when Go to or Enter was pressed on a result, otherwise null.</summary>
    public new SearchResult? Result { get; private set; }

    private SearchOptions ReadOptions()
    {
        var levels = int.TryParse(_levelsField.Text, NumberStyles.Integer, CultureInfo.InvariantCulture,
            out var value) && value >= 0
            ? value
            : throw new IOException("The number of levels must be 0 or more");
        return new SearchOptions(_startField.Text, _nameField.Text,
            _regexCheckBox.Value == CheckState.Checked,
            _caseSensitiveCheckBox.Value == CheckState.Checked,
            (SearchContentType)Math.Clamp((_contentRadioGroup.Value ?? 0), 0, ContentLabels.Length - 1),
            _contentField.Text,
            (SearchDepth)Math.Clamp((_depthRadioGroup.Value ?? 0), 0, DepthLabels.Length - 1),
            levels,
            _imagesCheckBox.Value == CheckState.Checked);
    }

    private void StartSearch()
    {
        SearchOptions options;
        try
        {
            options = ReadOptions();
        }
        catch (IOException exception)
        {
            ErrorBox.Show(exception);
            return;
        }

        Options = options;
        _results.Clear();
        _resultsView.SelectedRow = 0;
        _resultsView.Update();
        var cancellation = _cancellation = new CancellationTokenSource();
        var token = cancellation.Token;
        _searchButton.Text = "_Stop";
        _statusLabel.Text = "Searching...";

        // results and progress are reported on the thread pool and shown on the UI thread
        var results = new UiProgress<SearchResult>(result => UiThread.Post(() =>
        {
            if (!token.IsCancellationRequested)
            {
                AddResult(result);
            }
        }));
        var progress = new UiProgress<SearchProgress>(value => UiThread.Post(() =>
        {
            if (_cancellation == cancellation)
            {
                _statusLabel.Text = value.Text;
            }
        }));
        _ = Task.Run(() => _search(options, results, progress, token), token).ContinueWith(task =>
            UiThread.Post(() =>
            {
                if (_cancellation != cancellation)
                {
                    return;
                }

                _cancellation = null;
                cancellation.Dispose();
                _searchButton.Text = "Searc_h";
                if (task.IsCanceled || task.Exception?.GetBaseException() is OperationCanceledException)
                {
                    _statusLabel.Text = $"Stopped: {_results.Count} found";
                }
                else if (task.Exception?.GetBaseException() is { } exception)
                {
                    _statusLabel.Text = string.Empty;
                    ErrorBox.Show(exception);
                }

                if (_results.Count > 0)
                {
                    _resultsView.SetFocus();
                }
            }), CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default);
    }

    /// <summary>Inserts a result in order, keeping the selected result selected.</summary>
    private void AddResult(SearchResult result)
    {
        var selected = _results.Count > 0 ? _results[Math.Clamp(_resultsView.SelectedRow, 0, _results.Count - 1)] : null;
        _results.Insert(Sort.IndexToInsert(_results, result), result);
        _resultsView.Update();
        _resultsView.SelectedRow = selected == null ? 0 : _results.IndexOf(selected);
        _resultsView.EnsureSelectedCellIsVisible();
    }

    private void SortBy(SearchResultColumn column)
    {
        var selected = _results.Count > 0 ? _results[Math.Clamp(_resultsView.SelectedRow, 0, _results.Count - 1)] : null;
        Sort = _resultsSource.Sort = Sort.Toggle(column);
        var sorted = Sort.Sort(_results);
        _results.Clear();
        _results.AddRange(sorted);
        _resultsView.Update();
        if (selected != null)
        {
            _resultsView.SelectedRow = _results.IndexOf(selected);
            _resultsView.EnsureSelectedCellIsVisible();
        }

        _resultsView.SetNeedsDraw();
    }

    /// <summary>Sizes the columns to the width of the table: the path takes what the size and date don't.</summary>
    private void UpdateColumns()
    {
        // two separators between the three columns
        var pathWidth = Math.Max(10, _resultsView.Viewport.Width - SizeWidth - ModifiedWidth - 4);
        _resultsSource.PathWidth = pathWidth;
        _resultsView.Style.ColumnStyles = new Dictionary<int, ColumnStyle>
        {
            [0] = new() { MinWidth = pathWidth, MaxWidth = pathWidth },
            [1] = new() { MinWidth = SizeWidth, MaxWidth = SizeWidth, Alignment = Alignment.End },
            [2] = new() { MinWidth = ModifiedWidth, MaxWidth = ModifiedWidth }
        };
        _resultsView.SetNeedsDraw();
    }

    private void GoTo(int index)
    {
        if (index < 0 || index >= _results.Count)
        {
            return;
        }

        Result = _results[index];
        Application.RequestStop(this);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _cancellation?.Cancel();
        }

        base.Dispose(disposing);
    }

    private static CheckBox CreateCheckBox(Pos x, int y, string text, bool isChecked) => new()
    {
        X = x,
        Y = y,
        Text = text,
        Value = isChecked ? CheckState.Checked : CheckState.UnChecked
    };

    /// <summary>
    ///     Results as table rows: the path (shortened at the start when it doesn't fit, keeping the name), size and
    ///     modified date, with the column headers showing the sorting.
    /// </summary>
    private sealed class ResultsTableSource(List<SearchResult> results) : ITableSource
    {
        public SearchResultSort Sort { get; set; } = SearchResultSort.Default;

        public int PathWidth { get; set; } = int.MaxValue;

        public object this[int row, int col]
        {
            get
            {
                var result = results[row];
                return col switch
                {
                    1 => result.SizeText,
                    2 => result.ModifiedText,
                    _ => ShortenPath(result.FullPath)
                };
            }
        }

        public string[] ColumnNames => SearchResultSort.Columns.Select(Sort.GetHeader).ToArray();

        public int Columns => SearchResultSort.Columns.Count;

        public int Rows => results.Count;

        private string ShortenPath(string path) =>
            path.Length <= PathWidth || PathWidth < 2 ? path : "…" + path[^(PathWidth - 1)..];
    }

    /// <summary>TableView raising <see cref="HeaderClicked" /> with the column of a clicked header.</summary>
    private sealed class ResultsTableView : CursorTableView
    {
        public event EventHandler<int>? HeaderClicked;

        protected override bool OnMouseEvent(Mouse mouseEvent)
        {
            if (mouseEvent.Flags.HasFlag(MouseFlags.LeftButtonClicked) &&
                ScreenToCell(mouseEvent.Position ?? System.Drawing.Point.Empty, out int? headerColumn) == null &&
                headerColumn is { } column && column >= 0 && column < SearchResultSort.Columns.Count)
            {
                HeaderClicked?.Invoke(this, column);
                return true;
            }

            return base.OnMouseEvent(mouseEvent);
        }
    }

    private sealed class UiProgress<T>(Action<T> handler) : IProgress<T>
    {
        public void Report(T value) => handler(value);
    }
}
