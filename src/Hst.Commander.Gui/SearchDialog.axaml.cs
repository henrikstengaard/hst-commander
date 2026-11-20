using System.Collections.ObjectModel;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Hst.Commander.Core;

namespace Hst.Commander.Gui;

/// <summary>
///     Modal search dialog (Ctrl+S): what to search for (wildcards or a regular expression), where to start (the active
///     pane's location), the contents of files (text, a regular expression or hex values), how deep and whether to
///     search inside images and archives. Search runs the search in the background, listing entries as they're found
///     (Stop stops it, closing the dialog too); Enter or double-click on a result or Go to closes the dialog returning
///     the result, Close returns null. <see cref="Options" /> holds the options of the last search.
/// </summary>
public partial class SearchDialog : Window
{
    private readonly Func<SearchOptions, IProgress<SearchResult>, IProgress<SearchProgress>, CancellationToken, Task>?
        _search;

    private readonly ObservableCollection<SearchResult> _results = [];
    private CancellationTokenSource? _cancellation;

    public SearchDialog() : this(SearchOptions.Default, SearchResultSort.Default, null)
    {
    }

    /// <param name="options">Options to start with; the start path is the active pane's location.</param>
    /// <param name="sort">Order of the results to start with.</param>
    /// <param name="search">Searches with the options, reporting results and progress (see <see cref="NavigationContext.SearchAsync" />).</param>
    public SearchDialog(SearchOptions options, SearchResultSort sort,
        Func<SearchOptions, IProgress<SearchResult>, IProgress<SearchProgress>, CancellationToken, Task>? search)
    {
        InitializeComponent();
        _search = search;
        Options = options;
        Sort = sort;
        PathHeaderButton.Click += (_, _) => SortBy(SearchResultColumn.Path);
        SizeHeaderButton.Click += (_, _) => SortBy(SearchResultColumn.Size);
        ModifiedHeaderButton.Click += (_, _) => SortBy(SearchResultColumn.Modified);
        UpdateHeaders();
        // Up and Down outside the results move to them, e.g. right after searching with Enter in a text box
        AddHandler(KeyDownEvent, (_, e) =>
        {
            if (e.Key is Key.Up or Key.Down && e.KeyModifiers == KeyModifiers.None && MoveToResults(e.Key == Key.Down))
            {
                e.Handled = true;
            }
        }, RoutingStrategies.Tunnel);

        NameTextBox.Text = options.NamePattern;
        StartTextBox.Text = options.StartPath;
        RegexCheckBox.IsChecked = options.NameIsRegex;
        CaseSensitiveCheckBox.IsChecked = options.CaseSensitive;
        ImagesCheckBox.IsChecked = options.SearchInImages;
        ContentRadioButtons[(int)options.ContentType].IsChecked = true;
        ContentTextBox.Text = options.Content;
        DepthRadioButtons[(int)options.Depth].IsChecked = true;
        LevelsUpDown.Value = options.Levels;
        LevelsUpDown.ValueChanged += (_, _) => DepthLevelsRadioButton.IsChecked = true;

        ResultsList.ItemsSource = _results;
        ResultsList.DoubleTapped += (_, _) => GoToSelected();
        ResultsList.SelectionChanged += (_, _) => GoToButton.IsEnabled = ResultsList.SelectedItem != null;
        // tunnelling, as the focused list item takes Enter before the default Search button sees it
        ResultsList.AddHandler(KeyDownEvent, (_, e) =>
        {
            if (e.Key == Key.Enter && ResultsList.SelectedItem != null)
            {
                e.Handled = true;
                GoToSelected();
            }
        }, RoutingStrategies.Tunnel);
        GoToButton.IsEnabled = false;

        SearchButton.Click += async (_, _) =>
        {
            if (_cancellation != null)
            {
                await _cancellation.CancelAsync();
            }
            else
            {
                await SearchAsync();
            }
        };
        GoToButton.Click += (_, _) => GoToSelected();
        CloseButton.Click += (_, _) => Close(null);
        Closing += (_, _) => _cancellation?.Cancel();
        Opened += (_, _) =>
        {
            NameTextBox.Focus();
            NameTextBox.SelectAll();
        };
    }

    /// <summary>Options of the last search, or those the dialog started with when not searched.</summary>
    public SearchOptions Options { get; private set; }

    /// <summary>Order of the results, changed by clicking the column headers.</summary>
    public SearchResultSort Sort { get; private set; }

    private void SortBy(SearchResultColumn column)
    {
        Sort = Sort.Toggle(column);
        var selected = ResultsList.SelectedItem;
        var sorted = Sort.Sort(_results);
        _results.Clear();
        foreach (var result in sorted)
        {
            _results.Add(result);
        }

        UpdateHeaders();
        if (selected != null)
        {
            ResultsList.SelectedItem = selected;
            ResultsList.ScrollIntoView(selected);
        }
    }

    private void UpdateHeaders()
    {
        PathHeaderButton.Content = Sort.GetHeader(SearchResultColumn.Path);
        SizeHeaderButton.Content = Sort.GetHeader(SearchResultColumn.Size);
        ModifiedHeaderButton.Content = Sort.GetHeader(SearchResultColumn.Modified);
    }

    /// <summary>
    ///     Moves the focus to the results, selecting the next or previous result, unless the focus is in the results, the
    ///     number of levels or a radio button (which use Up and Down themselves) or there are no results.
    /// </summary>
    private bool MoveToResults(bool down)
    {
        if (_results.Count == 0 || ResultsList.IsKeyboardFocusWithin || LevelsUpDown.IsKeyboardFocusWithin ||
            FocusManager?.GetFocusedElement() is RadioButton)
        {
            return false;
        }

        var index = ResultsList.SelectedIndex;
        ResultsList.SelectedIndex = index < 0
            ? down ? 0 : _results.Count - 1
            : Math.Clamp(index + (down ? 1 : -1), 0, _results.Count - 1);
        FocusResults();
        return true;
    }

    /// <summary>Focuses the selected result, so Up and Down move through the results.</summary>
    private void FocusResults()
    {
        if (ResultsList.SelectedIndex < 0)
        {
            ResultsList.SelectedIndex = 0;
        }

        ResultsList.ScrollIntoView(ResultsList.SelectedIndex);
        if (ResultsList.ContainerFromIndex(ResultsList.SelectedIndex) is { } container)
        {
            container.Focus(NavigationMethod.Directional);
        }
        else
        {
            ResultsList.Focus(NavigationMethod.Directional);
        }
    }

    private RadioButton[] ContentRadioButtons =>
        [ContentNoneRadioButton, ContentTextRadioButton, ContentRegexRadioButton, ContentHexRadioButton];

    private RadioButton[] DepthRadioButtons => [DepthAllRadioButton, DepthCurrentRadioButton, DepthLevelsRadioButton];

    private SearchOptions ReadOptions() => new(
        StartTextBox.Text ?? string.Empty,
        NameTextBox.Text ?? string.Empty,
        RegexCheckBox.IsChecked == true,
        CaseSensitiveCheckBox.IsChecked == true,
        (SearchContentType)Math.Max(0, Array.FindIndex(ContentRadioButtons, button => button.IsChecked == true)),
        ContentTextBox.Text ?? string.Empty,
        (SearchDepth)Math.Max(0, Array.FindIndex(DepthRadioButtons, button => button.IsChecked == true)),
        (int)Math.Max(0, LevelsUpDown.Value ?? 0),
        ImagesCheckBox.IsChecked == true);

    private async Task SearchAsync()
    {
        if (_search == null)
        {
            return;
        }

        var options = Options = ReadOptions();
        _results.Clear();
        var cancellation = _cancellation = new CancellationTokenSource();
        SearchButton.Content = "_Stop";
        StatusText.Text = "Searching...";

        // created on the UI thread, so results and progress are shown on it
        var results = new Progress<SearchResult>(result =>
        {
            if (cancellation.IsCancellationRequested)
            {
                return;
            }

            _results.Insert(Sort.IndexToInsert(_results, result), result);
            if (ResultsList.SelectedIndex < 0)
            {
                ResultsList.SelectedIndex = 0;
            }
        });
        var progress = new Progress<SearchProgress>(value =>
        {
            if (_cancellation == cancellation)
            {
                StatusText.Text = value.Text;
            }
        });
        try
        {
            await Task.Run(() => _search(options, results, progress, cancellation.Token), cancellation.Token);
        }
        catch (OperationCanceledException)
        {
            StatusText.Text = $"Stopped: {_results.Count} found";
        }
        catch (Exception exception)
        {
            StatusText.Text = string.Empty;
            await new ErrorDialog(exception.Message).ShowDialog(this);
        }
        finally
        {
            _cancellation = null;
            cancellation.Dispose();
            SearchButton.Content = "Searc_h";
        }

        if (_results.Count > 0 && IsActive)
        {
            FocusResults();
        }
    }

    private void GoToSelected()
    {
        if (ResultsList.SelectedItem is SearchResult result)
        {
            Close(result);
        }
    }
}
