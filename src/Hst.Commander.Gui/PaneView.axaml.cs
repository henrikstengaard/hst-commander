using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using System.Globalization;
using Avalonia.Interactivity;
using Avalonia.Media;
using Hst.Commander.Core;
using Hst.Commander.Abstractions;
using Hst.Commander.Gui.Icons;

namespace Hst.Commander.Gui;

/// <summary>
///     Navigation pane showing the entries of the current location of a <see cref="NavigationContext" /> in a
///     navigation mode (see <see cref="PaneNavigationMode" />): a list, thumbnails or the icons of a plugin's navigation
///     mode at their positions (<see cref="IconLayoutView" />). The list holds the entries and the current entry in all
///     modes.
/// </summary>
public partial class PaneView : UserControl
{
    private readonly IDataTemplate? _listItemTemplate;
    private readonly ITemplate<Panel?> _listItemsPanel;
    private readonly ScrollBarVisibility _listHorizontalScrollBarVisibility;
    private NavigationContext? _navigationContext;
    private CancellationTokenSource? _iconsCancellation;
    private CancellationTokenSource? _volumeFilesCancellation;
    private readonly QuickSearch _quickSearch = new();

    /// <summary>Values of the options of the navigation modes changed by the user, by mode and option name.</summary>
    private readonly Dictionary<INavigationMode, Dictionary<string, bool>> _navigationModeOptions = [];

    public PaneView()
    {
        InitializeComponent();
        UpdateColumns();
        ListingCancelButton.Click += (_, _) => CancelProgress();
        // the message is shortened to the pane's width again when it changes
        PropertyChanged += (_, e) =>
        {
            if (e.Property == BoundsProperty && ListingProgress.IsVisible)
            {
                FitProgressMessage();
            }
        };
        InitializeContextMenu();

        _listItemTemplate = EntriesList.ItemTemplate;
        _listItemsPanel = EntriesList.ItemsPanel;
        _listHorizontalScrollBarVisibility = ScrollViewer.GetHorizontalScrollBarVisibility(EntriesList);
        EntriesList.AddHandler(KeyDownEvent, OnEntriesKeyDown, RoutingStrategies.Tunnel);
        EntriesList.AddHandler(TextInputEvent, OnEntriesTextInput, RoutingStrategies.Tunnel);
        AddHandler(PointerPressedEvent, (_, _) => StopQuickSearch(), RoutingStrategies.Tunnel, true);
        EntriesList.DoubleTapped += async (_, _) => await ActivateSelectedEntryAsync();
        EntriesList.SelectionChanged += (_, _) => UpdateIconSelection();
        // anywhere in the panel, not only on the path, as the path is often short
        PathPanel.Tapped += (_, _) => PathClicked?.Invoke(this, EventArgs.Empty);
        EntriesList.PropertyChanged += (_, e) =>
        {
            if (e.Property == IsKeyboardFocusWithinProperty)
            {
                UpdatePathBadge();
                StopQuickSearchWithoutFocus();
            }
        };

        EntriesList.AddHandler(PointerPressedEvent, OnEntriesPointerPressed, RoutingStrategies.Tunnel, true);
        EntriesList.AddHandler(PointerMovedEvent, OnEntriesPointerMoved, RoutingStrategies.Tunnel, true);
        EntriesList.AddHandler(PointerReleasedEvent, OnEntriesPointerReleased, RoutingStrategies.Tunnel, true);
        DragDrop.SetAllowDrop(this, true);
        // Avalonia raises DragEnter instead of DragOver when the pointer moves onto another element, e.g. from a row to
        // the space below the entries, and uses its effect, so both are handled alike
        DragDrop.AddDragEnterHandler(this, OnDragOver);
        DragDrop.AddDragOverHandler(this, OnDragOver);
        DragDrop.AddDragLeaveHandler(this, (_, _) => ShowDropTarget(null, false));
        DragDrop.AddDropHandler(this, OnDrop);

        IconLayout.AddHandler(KeyDownEvent, OnEntriesKeyDown, RoutingStrategies.Tunnel);
        IconLayout.AddHandler(TextInputEvent, OnEntriesTextInput, RoutingStrategies.Tunnel);
        IconLayout.PropertyChanged += (_, e) =>
        {
            if (e.Property == IsKeyboardFocusWithinProperty)
            {
                UpdatePathBadge();
                StopQuickSearchWithoutFocus();
            }
        };
        IconLayout.ItemClicked += (_, item) => EntriesList.SelectedItem = item;
        IconLayout.ItemToggled += (_, item) =>
        {
            EntriesList.SelectedItem = item;
            item.IsMarked = NavigationContext.ToggleMark(item.Entry);
            UpdateSummary();
        };
        IconLayout.ItemActivated += async (_, item) =>
        {
            EntriesList.SelectedItem = item;
            await ActivateSelectedEntryAsync();
        };
        IconLayout.BackgroundClicked += (_, _) => EntriesList.SelectedItem = null;
        IconLayout.ParentClicked += async (_, _) =>
        {
            await RunNavigationAsync(() => NavigationContext.GoParentAsync());
            FocusEntries();
        };
        IconLayout.OptionChanged += async (_, change) =>
        {
            if (NavigationMode.PluginMode is not { } navigationMode)
            {
                return;
            }

            GetOptions(navigationMode)[change.Option.Name] = change.Value;
            await ShowLayoutAsync(navigationMode, EntriesList.ItemsSource as IReadOnlyList<EntryItem> ?? []);
            FocusEntries();
        };
        IconLayout.PositionsChanged += (_, _) => IconPositionsBar.IsVisible = IconLayout.ChangedPositions.Count > 0;
        LeftParentButton.Click += async (_, _) => await GoParentFromButtonAsync();
        RightParentButton.Click += async (_, _) => await GoParentFromButtonAsync();
        SavePositionsButton.Click += async (_, _) => await SaveIconPositionsAsync();
        ResetPositionsButton.Click += (_, _) =>
        {
            IconLayout.Reset();
            FocusEntries();
        };
    }

    /// <summary>
    ///     Side of the pane showing a narrow button going to the parent, null for none: the outer side of each pane when
    ///     the theme shows parent buttons (<see cref="ThemeChoice.ShowParentButtons" />).
    /// </summary>
    public ParentButtonSide? ParentButtonSide
    {
        get => LeftParentButton.IsVisible ? Gui.ParentButtonSide.Left :
            RightParentButton.IsVisible ? Gui.ParentButtonSide.Right : null;
        set
        {
            LeftParentButton.IsVisible = value == Gui.ParentButtonSide.Left;
            RightParentButton.IsVisible = value == Gui.ParentButtonSide.Right;
        }
    }

    /// <summary>
    ///     True to color the whole panel above the pane like the active path while the pane is active
    ///     (<see cref="ThemeChoice.HighlightActivePathPanel" />), not only the path.
    /// </summary>
    public bool HighlightActivePathPanel
    {
        get => _highlightActivePathPanel;
        set
        {
            _highlightActivePathPanel = value;
            UpdatePathBadge();
        }
    }

    private bool _highlightActivePathPanel;

    /// <summary>Goes to the parent when a parent button is clicked, making the pane active.</summary>
    private async Task GoParentFromButtonAsync()
    {
        FocusEntries();
        await GoParentAsync();
        FocusEntries();
    }

    /// <summary>Goes to the parent of the current directory, or out of an image at its root.</summary>
    public Task GoParentAsync() => RunNavigationAsync(() => NavigationContext.GoParentAsync());

    /// <summary>Goes to the root of the current volume.</summary>
    public Task GoRootAsync() => RunNavigationAsync(() => NavigationContext.GoRootAsync());

    /// <summary>Marks all entries.</summary>
    public void MarkAll() => ChangeMarks(entries => NavigationContext.MarkAll(entries));

    /// <summary>Unmarks all entries.</summary>
    public void UnmarkAll() => ChangeMarks(_ => NavigationContext.ClearMarks());

    /// <summary>Raised when entries of the current location were changed by the pane, e.g. icon files written.</summary>
    public event EventHandler? EntriesChanged;

    /// <summary>Navigation mode the entries are shown in, the list by default.</summary>
    public PaneNavigationMode NavigationMode { get; private set; } = PaneNavigationMode.List;

    /// <summary>Shows the entries in another navigation mode, listing them again.</summary>
    public Task SetNavigationModeAsync(PaneNavigationMode navigationMode)
    {
        ArgumentNullException.ThrowIfNull(navigationMode);

        var hadFocus = EntriesList.IsKeyboardFocusWithin || IconLayout.IsKeyboardFocusWithin;
        _iconsCancellation?.Cancel();
        NavigationMode = navigationMode;
        var isThumbnail = navigationMode == PaneNavigationMode.Thumbnail;
        var isPluginMode = navigationMode.PluginMode != null;
        EntriesList.ItemTemplate = isThumbnail ? (IDataTemplate)Resources["ThumbnailTemplate"]! : _listItemTemplate;
        EntriesList.ItemsPanel = isThumbnail ? (ITemplate<Panel?>)Resources["WrapPanelTemplate"]! : _listItemsPanel;
        // the wrap panel wraps at the width of the list, so it doesn't scroll horizontally
        ScrollViewer.SetHorizontalScrollBarVisibility(EntriesList,
            isThumbnail ? ScrollBarVisibility.Disabled : _listHorizontalScrollBarVisibility);
        HeaderGrid.IsVisible = DividersGrid.IsVisible = !isThumbnail && !isPluginMode;
        EntriesList.IsVisible = !isPluginMode;
        IconLayout.IsVisible = isPluginMode;
        IconPositionsBar.IsVisible = false;
        if (!isPluginMode)
        {
            IconLayout.Clear();
        }

        // the entries are listed again with the icons of the mode
        return hadFocus ? RefreshAndFocusAsync() : RefreshEntriesAsync();
    }

    private async Task RefreshAndFocusAsync()
    {
        await RefreshEntriesAsync();
        FocusEntries();
    }

    /// <summary>Widths of the size and modified columns at the default font size, scaled with the font size.</summary>
    private const double SizeColumnWidth = 90;

    private const double ModifiedColumnWidth = 150;

    /// <summary>Width of a character of other columns at the default font size, and the padding of a column.</summary>
    private const double CharacterWidth = 9;

    private const double ColumnPadding = 18;

    /// <summary>Columns the header, the dividers and <see cref="UpdateColumnWidths" /> were made for.</summary>
    private IReadOnlyList<IEntryColumn> _columns = [];

    private readonly List<TextBlock> _headerTexts = [];

    /// <summary>Key of the resource with the width of a column of the pane by index (not used for Name).</summary>
    public static string GetColumnWidthKey(int index) => $"EntryColumnWidth{index}";

    /// <summary>
    ///     Columns shown in the list (<see cref="NavigationContext.Columns" />), set from the settings. List the entries
    ///     again after changing them.
    /// </summary>
    public IReadOnlyList<IEntryColumn> Columns
    {
        get => NavigationContext.Columns;
        set
        {
            NavigationContext.Columns = value;
            UpdateColumns();
        }
    }

    /// <summary>
    ///     Creates the header and the dividers for the columns of the navigation context, the rows follow their entries
    ///     (<see cref="EntryRowCells" />).
    /// </summary>
    private void UpdateColumns()
    {
        var columns = _navigationContext?.Columns ?? EntryColumns.Resolve(null);
        if (_columns.SequenceEqual(columns))
        {
            return;
        }

        _columns = columns;
        HeaderGrid.Children.Clear();
        HeaderGrid.ColumnDefinitions.Clear();
        DividersGrid.Children.Clear();
        DividersGrid.ColumnDefinitions.Clear();
        _headerTexts.Clear();
        for (var index = 0; index < columns.Count; index++)
        {
            var column = columns[index];
            foreach (var grid in new[] { HeaderGrid, DividersGrid })
            {
                var definition = new ColumnDefinition(new GridLength(1, GridUnitType.Star));
                if (column != EntryColumns.Name)
                {
                    definition.Bind(ColumnDefinition.WidthProperty, grid.GetResourceObservable(GetColumnWidthKey(index)));
                }

                grid.ColumnDefinitions.Add(definition);
            }

            var text = new TextBlock { Text = column.Header };
            var header = new Border { Background = Brushes.Transparent, Cursor = new Cursor(StandardCursorType.Hand), Child = text };
            Grid.SetColumn(header, index);
            header.Tapped += (_, _) => SortBy(column.Id);
            HeaderGrid.Children.Add(header);
            _headerTexts.Add(text);
            if (index > 0)
            {
                var divider = new Border { BorderThickness = new Thickness(1, 0, 0, 0) };
                divider.Bind(Border.BorderBrushProperty, divider.GetResourceObservable("PaneBorder"));
                Grid.SetColumn(divider, index);
                DividersGrid.Children.Add(divider);
            }
        }

        UpdateColumnWidths();
        if (_navigationContext != null)
        {
            UpdateHeaders();
        }
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == FontSizeProperty)
        {
            UpdateColumnWidths();
        }
    }

    /// <summary>
    ///     Sets the widths of the columns but Name (the resources of <see cref="GetColumnWidthKey" /> used by the header,
    ///     the dividers and the rows) for the font size.
    /// </summary>
    private void UpdateColumnWidths()
    {
        var scale = FontSize / GuiFonts.DefaultSize;
        for (var index = 0; index < _columns.Count; index++)
        {
            var column = _columns[index];
            var width = column == EntryColumns.Size
                ? SizeColumnWidth
                : column == EntryColumns.Modified
                    ? ModifiedColumnWidth
                    : EntryColumns.GetWidth(column) * CharacterWidth + ColumnPadding;
            Resources[GetColumnWidthKey(index)] = new GridLength(Math.Round(width * scale));
        }
    }

    private void UpdatePathBadge()
    {
        var active = EntriesList.IsKeyboardFocusWithin || IconLayout.IsKeyboardFocusWithin;
        PathBadge.Classes.Set("active", active);
        PathPanel.Classes.Set("highlighted", active && _highlightActivePathPanel);
    }

    /// <summary>Shows the icon of the current entry selected in a navigation mode of a plugin.</summary>
    private void UpdateIconSelection()
    {
        if (NavigationMode.PluginMode != null)
        {
            IconLayout.UpdateSelection(EntriesList.SelectedItem as EntryItem, IsActive);
        }
    }

    /// <summary>
    ///     Saves the positions of the icons moved in a navigation mode of a plugin and shows the entries again, e.g.
    ///     with the .info files written by the Workbench navigation mode.
    /// </summary>
    private async Task SaveIconPositionsAsync()
    {
        if (NavigationMode.PluginMode is not { } navigationMode)
        {
            return;
        }

        var entries = (EntriesList.ItemsSource as IReadOnlyList<EntryItem> ?? []).Select(item => item.Entry).ToList();
        var positions = IconLayout.ChangedPositions;
        var saved = false;
        await RunNavigationAsync(async () =>
        {
            await NavigationContext.SaveIconPositionsAsync(navigationMode, entries, positions);
            saved = true;
        });
        if (saved)
        {
            EntriesChanged?.Invoke(this, EventArgs.Empty);
        }

        FocusEntries();
    }

    /// <summary>Raised when navigation fails, so the window can show the error.</summary>
    public event EventHandler<Exception>? NavigationFailed;

    /// <summary>
    ///     Raised when the path panel (the path, the marked summary or the disk space) is clicked, so the window can show
    ///     the location dialog.
    /// </summary>
    public event EventHandler? PathClicked;

    /// <summary>Raised when the entries were sorted by a clicked column header, so the window can save the order.</summary>
    public event EventHandler? SortChanged;

    public NavigationContext NavigationContext
    {
        get => _navigationContext ?? throw new InvalidOperationException("Navigation context is not set.");
        set
        {
            _navigationContext = value;
            value.ProgressIndicator = new PaneProgressIndicator(this);
            UpdateColumns();
        }
    }

    /// <summary>Shows the progress with an indeterminate progress bar, e.g. while listing.</summary>
    private void ShowProgress(string message)
    {
        _progressCancel = null;
        SetProgressMessage(message);
        ListingProgressBar.IsIndeterminate = true;
        ListingProgressBar.ShowProgressText = false;
        ListingCancelButton.IsVisible = false;
        ListingProgress.IsVisible = true;
    }

    private void HideProgress()
    {
        var hadCancel = _progressCancel != null;
        var hadFocus = ListingCancelButton.IsKeyboardFocusWithin;
        _progressCancel = null;
        ListingProgress.IsVisible = false;
        if (hadCancel && hadFocus)
        {
            FocusEntries();
        }
    }

    /// <summary>Message of the progress before it's shortened to fit the pane.</summary>
    private string _progressMessage = string.Empty;

    /// <summary>Space left and right of the progress message: the progress's padding and border and a margin.</summary>
    private const double ProgressMessageMargin = 2 * (16 + 1 + 12);

    private void SetProgressMessage(string message)
    {
        _progressMessage = message;
        FitProgressMessage();
    }

    /// <summary>
    ///     Shows the progress message shortened to the pane's width, e.g. "Reading ...\games\whdload..." for a long path,
    ///     like the path panel above the pane.
    /// </summary>
    private void FitProgressMessage()
    {
        var message = _progressMessage;
        var maxWidth = Bounds.Width - ProgressMessageMargin;
        if (maxWidth <= 0 || MeasureProgressText(message) <= maxWidth)
        {
            ListingProgressText.Text = message;
            return;
        }

        // the longest shortened message that fits
        var low = 0;
        var high = message.Length - 1;
        while (low < high)
        {
            var length = (low + high + 1) / 2;
            if (MeasureProgressText(Core.PathText.ShortenMessage(message, length)) <= maxWidth)
            {
                low = length;
            }
            else
            {
                high = length - 1;
            }
        }

        ListingProgressText.Text = Core.PathText.ShortenMessage(message, low);
    }

    private double MeasureProgressText(string text) =>
        new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            new Typeface(ListingProgressText.FontFamily, ListingProgressText.FontStyle, ListingProgressText.FontWeight),
            ListingProgressText.FontSize, Brushes.Black).WidthIncludingTrailingWhitespace;

    /// <summary>Cancels the progress shown with a cancel button, see <see cref="PaneProgressIndicator" />.</summary>
    private Action? _progressCancel;

    private void CancelProgress()
    {
        if (_progressCancel is not { } cancel || !ListingCancelButton.IsEnabled)
        {
            return;
        }

        ListingCancelButton.IsEnabled = false;
        SetProgressMessage("Cancelling...");
        cancel();
    }

    /// <summary>
    ///     Progress of the pane's navigation context, e.g. making a temporary copy of an image inside another image,
    ///     shown with a progress bar and a cancel button having the focus.
    /// </summary>
    private sealed class PaneProgressIndicator(PaneView pane) : IProgressIndicator
    {
        public void Show(string message, Action cancel)
        {
            pane._progressCancel = cancel;
            pane.SetProgressMessage(message);
            pane.ListingProgressBar.IsIndeterminate = false;
            pane.ListingProgressBar.ShowProgressText = true;
            pane.ListingProgressBar.Value = 0;
            pane.ListingCancelButton.IsEnabled = true;
            pane.ListingCancelButton.IsVisible = true;
            pane.ListingProgress.IsVisible = true;
            pane.ListingCancelButton.Focus();
        }

        public void ShowIndeterminate(string message, Action cancel)
        {
            Show(message, cancel);
            pane.ListingProgressBar.IsIndeterminate = true;
            pane.ListingProgressBar.ShowProgressText = false;
        }

        public void ReportMessage(string message)
        {
            if (pane._progressCancel != null && pane.ListingCancelButton.IsEnabled)
            {
                pane.SetProgressMessage(message);
            }
        }

        public void Report(double fraction)
        {
            if (pane._progressCancel != null)
            {
                pane.ListingProgressBar.Value = Math.Clamp(fraction, 0, 1) * 100;
            }
        }

        public void Hide() => pane.HideProgress();
    }

    /// <summary>Entry under the cursor, null when the list is empty.</summary>
    /// <summary>Whether the pane is the active pane, the current entry is only shown in the active pane.</summary>
    public bool IsActive
    {
        get => EntriesList.Classes.Contains("active");
        set
        {
            EntriesList.Classes.Set("active", value);
            UpdateIconSelection();
        }
    }

    public IEntry? SelectedEntry => (EntriesList.SelectedItem as EntryItem)?.Entry;

    /// <summary>Lists the entries of the current location again, e.g. after copying.</summary>
    public Task RefreshEntriesAsync() => RunNavigationAsync(() => Task.CompletedTask);

    /// <summary>
    ///     Swaps the location, entries, cursor and navigation mode with another pane (Ctrl+U), listing both again. The
    ///     window moves the focus to the other pane afterwards.
    /// </summary>
    public Task SwapWithAsync(PaneView other)
    {
        ArgumentNullException.ThrowIfNull(other);

        foreach (var pane in new[] { this, other })
        {
            pane._iconsCancellation?.Cancel();
            pane._volumeFilesCancellation?.Cancel();
        }

        var (navigationContext, navigationMode) = (NavigationContext, NavigationMode);
        var (items, selectedIndex, path) = (EntriesList.ItemsSource, EntriesList.SelectedIndex, PathText.Text);
        TakeOver(other.NavigationContext, other.EntriesList.ItemsSource, other.EntriesList.SelectedIndex,
            other.PathText.Text);
        other.TakeOver(navigationContext, items, selectedIndex, path);

        // setting the navigation modes lists the entries again, keeping the cursor as the path is unchanged
        var otherNavigationMode = other.NavigationMode;
        return Task.WhenAll(SetNavigationModeAsync(otherNavigationMode), other.SetNavigationModeAsync(navigationMode));
    }

    private void TakeOver(NavigationContext navigationContext, System.Collections.IEnumerable? items,
        int selectedIndex, string? path)
    {
        NavigationContext = navigationContext;
        EntriesList.ItemsSource = items;
        EntriesList.SelectedIndex = selectedIndex;
        PathText.Text = path;
    }

    /// <summary>Shows the root of a new volume from a volume provider, e.g. chosen with Alt+F1/Alt+F2.</summary>
    public Task ChangeVolumeProviderAsync(IVolumeProvider volumeProvider) =>
        RunNavigationAsync(() => NavigationContext.ChangeVolumeProviderAsync(volumeProvider));

    /// <summary>
    ///     Connects to the location of a volume provider needing one (e.g. an SMB path) and shows it, asking for
    ///     credentials with <paramref name="askCredentials" /> when needed (see <see cref="VolumeConnector" />). The
    ///     pane is unchanged when the credentials dialog is cancelled. <paramref name="credentials" /> are tried first,
    ///     e.g. those entered in the connection dialog.
    /// </summary>
    public Task ConnectAsync(ILocationVolumeProvider volumeProvider, string location,
        Func<CredentialsRequest, Task<VolumeCredentials?>> askCredentials, VolumeCredentials? credentials = null) =>
        RunNavigationAsync(async () =>
        {
            var connection = await VolumeConnector.ConnectAsync(volumeProvider, location, askCredentials,
                ShowProgress, HideProgress, credentials);
            if (connection != null)
            {
                await NavigationContext.ChangeVolumeProviderAsync(connection);
            }
        });

    /// <summary>
    ///     Opens a bookmark, connecting with its stored credentials and asking for them with
    ///     <paramref name="askCredentials" /> when needed (see <see cref="BookmarkOpener" />). The pane is unchanged
    ///     when the bookmark can't be opened or the credentials dialog is cancelled.
    /// </summary>
    public Task OpenBookmarkAsync(Bookmark bookmark, Func<CredentialsRequest, Task<VolumeCredentials?>> askCredentials) =>
        RunNavigationAsync(() => BookmarkOpener.OpenAsync(NavigationContext, bookmark, BookmarkStore.Default,
            askCredentials, ShowProgress, HideProgress));

    /// <summary>
    ///     Shows the location of another pane's navigation context (Ctrl+D), see
    ///     <see cref="NavigationContext.ChangeLocationAsync(NavigationContext, CancellationToken)" />.
    /// </summary>
    public Task ChangeLocationAsync(NavigationContext other) =>
        RunNavigationAsync(() => NavigationContext.ChangeLocationAsync(other));

    /// <summary>Changes to a location entered in the location dialog, see <see cref="NavigationContext.ChangeLocationAsync" />.</summary>
    public Task ChangeLocationAsync(string path) =>
        RunNavigationAsync(() => NavigationContext.ChangeLocationAsync(path));

    /// <summary>Goes to an entry found in the search dialog and selects it, see <see cref="NavigationContext.GoToSearchResultAsync" />.</summary>
    public Task GoToSearchResultAsync(SearchResult result) =>
        RunNavigationAsync(() => NavigationContext.GoToSearchResultAsync(result));

    /// <summary>Goes to the user's home directory (Home in the location dialog), see <see cref="NavigationContext.GoHomeAsync" />.</summary>
    public Task GoHomeAsync() => RunNavigationAsync(() => NavigationContext.GoHomeAsync());

    /// <summary>Creates a directory in the current location and selects it, see <see cref="NavigationContext.CreateDirectoryAsync" />.</summary>
    public Task CreateDirectoryAsync(string name) =>
        RunNavigationAsync(() => NavigationContext.CreateDirectoryAsync(name));

    /// <summary>
    ///     Creates a volume as a file in the current location and selects it, see
    ///     <see cref="NavigationContext.CreateVolumeAsync" />. Unlike navigating, an error is thrown, so the input can be
    ///     asked again.
    /// </summary>
    public async Task CreateVolumeAsync(IVolumeCreator creator, VolumeCreatorInput input)
    {
        // icons of files inside images are read from the pane's volume, so stop before writing to it
        _iconsCancellation?.Cancel();
        await NavigationContext.CreateVolumeAsync(creator, input.FileName, input.Values);
        await RefreshEntriesAsync();
    }

    /// <summary>Renames an entry in the current location and selects it, see <see cref="NavigationContext.RenameAsync" />.</summary>
    public Task RenameAsync(IEntry entry, string newName) =>
        RunNavigationAsync(() => NavigationContext.RenameAsync(entry, newName));

    public void FocusEntries()
    {
        // icons of a plugin's navigation mode have no current entry until one is clicked, like the Workbench
        if (NavigationMode.PluginMode != null)
        {
            IconLayout.Focus(NavigationMethod.Tab);
            return;
        }

        if (EntriesList.SelectedIndex < 0 && EntriesList.ItemCount > 0)
        {
            EntriesList.SelectedIndex = 0;
        }

        if (EntriesList.SelectedIndex < 0)
        {
            EntriesList.Focus(NavigationMethod.Tab);
            return;
        }

        // the list box itself is not focusable and arrow keys only move the selection when an item has focus,
        // so make sure the selected item's container is realized before focusing it
        EntriesList.ScrollIntoView(EntriesList.SelectedIndex);
        if (EntriesList.ContainerFromIndex(EntriesList.SelectedIndex) is null)
        {
            EntriesList.UpdateLayout();
        }

        EntriesList.ContainerFromIndex(EntriesList.SelectedIndex)?.Focus(NavigationMethod.Tab);
    }

    /// <summary>Starts the quick search (Ctrl+F) in the entries, see <see cref="Core.QuickSearch" />.</summary>
    public void StartQuickSearch()
    {
        FocusEntries();
        _quickSearch.Start();
        UpdateQuickSearchBar();

        // the bar makes the list shorter, so keep the current entry visible once laid out again
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            if (_quickSearch.IsActive && EntriesList.SelectedItem != null && NavigationMode.PluginMode == null)
            {
                EntriesList.ScrollIntoView(EntriesList.SelectedItem);
            }
        }, Avalonia.Threading.DispatcherPriority.Loaded);
    }

    private void StopQuickSearch()
    {
        if (!_quickSearch.IsActive)
        {
            return;
        }

        _quickSearch.Stop();
        UpdateQuickSearchBar();
    }

    /// <summary>
    ///     Stops the quick search when the focus has left the entries, e.g. to the other pane. Checked after the pending
    ///     focus changes: moving to an entry further away recycles the focused row's container, so the focus leaves the
    ///     list for a moment until <see cref="FocusEntries" /> focuses the new row.
    /// </summary>
    private void StopQuickSearchWithoutFocus()
    {
        if (!_quickSearch.IsActive || EntriesList.IsKeyboardFocusWithin || IconLayout.IsKeyboardFocusWithin)
        {
            return;
        }

        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            if (!EntriesList.IsKeyboardFocusWithin && !IconLayout.IsKeyboardFocusWithin)
            {
                StopQuickSearch();
            }
        }, Avalonia.Threading.DispatcherPriority.Input);
    }

    private void UpdateQuickSearchBar()
    {
        QuickSearchBar.IsVisible = _quickSearch.IsActive;
        QuickSearchText.Text = $"Find: {_quickSearch.Text}";
    }

    /// <summary>
    ///     Handles a key while the quick search is active, called by the window's tunnelling key handler before it handles
    ///     its own keys: Backspace, Ctrl+F, Up and Down search, Esc stops, typed characters are left to the text input.
    ///     Other keys stop the quick search and return false, so they do what they always do, e.g. Enter opens the entry
    ///     found. <c>+</c>, <c>-</c>, <c>*</c> and Space only stop it as the first character.
    /// </summary>
    public bool HandleQuickSearchKey(KeyEventArgs e)
    {
        if (!_quickSearch.IsActive)
        {
            return false;
        }

        int? index;
        switch (e.Key)
        {
            // pressing Shift for an upper case letter
            case Key.LeftShift or Key.RightShift or Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt
                or Key.LWin or Key.RWin:
                return true;
            case Key.Escape:
                e.Handled = true;
                StopQuickSearch();
                return true;
            case Key.Back:
                e.Handled = true;
                index = _quickSearch.RemoveLast(GetQuickSearchNames());
                break;
            case Key.Up or Key.Down when e.KeyModifiers == KeyModifiers.None:
            case Key.F when e.KeyModifiers == KeyModifiers.Control:
                e.Handled = true;
                index = _quickSearch.Next(GetQuickSearchNames(), EntriesList.SelectedIndex, e.Key != Key.Up);
                break;
            default:
                if (IsQuickSearchCharacter(e))
                {
                    // added by the text input that follows
                    return true;
                }

                StopQuickSearch();
                return false;
        }

        SelectQuickSearchMatch(index);
        return true;
    }

    /// <summary>
    ///     True for a key typing a character into the quick search: no Ctrl or Alt, except both for AltGr. <c>+</c>,
    ///     <c>-</c>, <c>*</c> and Space only after the first character, so they select entries otherwise.
    /// </summary>
    private bool IsQuickSearchCharacter(KeyEventArgs e)
    {
        var modifiers = e.KeyModifiers & (KeyModifiers.Control | KeyModifiers.Alt | KeyModifiers.Meta);
        return (modifiers == KeyModifiers.None || modifiers == (KeyModifiers.Control | KeyModifiers.Alt)) &&
               e.KeySymbol is { Length: > 0 } symbol && !char.IsControl(symbol[0]) &&
               (_quickSearch.Text.Length > 0 || symbol is not ("+" or "-" or "*" or " "));
    }

    private void OnEntriesTextInput(object? sender, TextInputEventArgs e)
    {
        // typing / or \ (like the root path) goes to the root, with every keyboard layout
        if (!_quickSearch.IsActive && e.Text is "/" or "\\")
        {
            e.Handled = true;
            _ = RunNavigationAsync(() => NavigationContext.GoRootAsync());
            return;
        }

        if (!_quickSearch.IsActive || string.IsNullOrEmpty(e.Text) || char.IsControl(e.Text[0]))
        {
            return;
        }

        // a character without a match is rejected
        e.Handled = true;
        SelectQuickSearchMatch(_quickSearch.TryAppend(e.Text, GetQuickSearchNames()));
    }

    private void SelectQuickSearchMatch(int? index)
    {
        if (index is { } selectedIndex)
        {
            EntriesList.SelectedIndex = selectedIndex;
            if (NavigationMode.PluginMode == null)
            {
                FocusEntries();
            }
        }

        UpdateQuickSearchBar();
    }

    /// <summary>The shown names of the entries for the quick search, null for the parent entry.</summary>
    private string?[] GetQuickSearchNames() =>
        (EntriesList.ItemsSource as IReadOnlyList<EntryItem> ?? [])
        .Select(item => item.Name == Paths.Parent ? null : item.DisplayName)
        .ToArray();

    private async void OnEntriesKeyDown(object? sender, KeyEventArgs e)
    {
        // typed characters of the quick search are added by the text input, e.g. Space doesn't mark
        if (_quickSearch.IsActive && IsQuickSearchCharacter(e))
        {
            return;
        }

        if (IsGoRootKey(e))
        {
            e.Handled = true;
            await RunNavigationAsync(() => NavigationContext.GoRootAsync());
            return;
        }

        switch (e.Key)
        {
            // Shift+arrows toggle the mark of the current entry and move like the arrow key, Shift+Left and
            // Shift+Right only where Left and Right move (thumbnails and plugin navigation modes)
            case Key.Up or Key.Down when e.KeyModifiers == KeyModifiers.Shift:
            case Key.Left or Key.Right when e.KeyModifiers == KeyModifiers.Shift &&
                                            (NavigationMode.PluginMode != null ||
                                             NavigationMode == PaneNavigationMode.Thumbnail):
                e.Handled = true;
                ToggleMarkAndMove(e.Key);
                break;
            // icons of a plugin's navigation mode are moved through by their positions
            case Key.Left or Key.Up or Key.Right or Key.Down when NavigationMode.PluginMode != null &&
                                                                 e.KeyModifiers == KeyModifiers.None:
                e.Handled = true;
                EntriesList.SelectedItem = IconLayout.GetItem(EntriesList.SelectedItem as EntryItem, e.Key switch
                {
                    Key.Left => NavigationDirection.Left,
                    Key.Up => NavigationDirection.Up,
                    Key.Right => NavigationDirection.Right,
                    _ => NavigationDirection.Down
                });
                break;
            // thumbnails move by rows and entries, the wrap panel's own navigation doesn't go up and down
            case Key.Up or Key.Down when NavigationMode == PaneNavigationMode.Thumbnail &&
                                         e.KeyModifiers == KeyModifiers.None:
                e.Handled = true;
                MoveThumbnailRow(e.Key == Key.Down);
                break;
            case Key.Left or Key.Right when NavigationMode == PaneNavigationMode.Thumbnail &&
                                            e.KeyModifiers == KeyModifiers.None:
                e.Handled = true;
                MoveSelection(e.Key == Key.Right ? 1 : -1);
                break;
            // Ctrl+Enter and Ctrl+Shift+Enter insert the entry in the command line (see MainWindow)
            case Key.Enter when !e.KeyModifiers.HasFlag(KeyModifiers.Control):
                e.Handled = true;
                await ActivateSelectedEntryAsync();
                break;
            // Space also calculates the size of a directory it marks, like Total Commander
            case Key.Insert:
            case Key.Space:
                e.Handled = true;
                ToggleMarkAndMoveDown(e.Key == Key.Space);
                break;
            case Key.Back:
                e.Handled = true;
                await RunNavigationAsync(() => NavigationContext.GoParentAsync());
                break;
            case Key.PageDown when e.KeyModifiers == KeyModifiers.Control:
                e.Handled = true;
                await OpenSelectedEntryAsVolumeAsync();
                break;
            // the list box scroll viewer handles these keys by only scrolling the view,
            // so move the selection instead
            case Key.PageUp:
                e.Handled = true;
                MoveSelection(-GetPageSize());
                break;
            case Key.PageDown:
                e.Handled = true;
                MoveSelection(GetPageSize());
                break;
            case Key.Home:
                e.Handled = true;
                MoveSelection(-EntriesList.ItemCount);
                break;
            case Key.End:
                e.Handled = true;
                MoveSelection(EntriesList.ItemCount);
                break;
        }
    }

    /// <summary>
    ///     Ctrl+\ going to the root of the volume, matched by the typed character or the backslash keys: on some layouts,
    ///     e.g. Danish, the key with backslash is the 102nd key (left of Z), which Windows reports as the backslash key
    ///     but Linux and macOS by its character (&lt;), so its physical key is matched too. On ISO keyboards of Macs
    ///     (e.g. Danish), macOS reports the key left of Z with the key code of the ` key of US keyboards, so it's
    ///     <see cref="PhysicalKey.Backquote" /> there (Ctrl+` on US Macs then goes to the root too); \ itself is
    ///     Shift+Option+7 on a Danish Mac. Typing / or \ also goes to the root (see <see cref="OnEntriesTextInput" />).
    /// </summary>
    private static bool IsGoRootKey(KeyEventArgs e) =>
        e.KeyModifiers == KeyModifiers.Control &&
        (e.KeySymbol is "\\" or "\u001C" or "<" || e.Key is Key.OemPipe or Key.OemBackslash ||
         e.PhysicalKey is PhysicalKey.Backslash or PhysicalKey.IntlBackslash ||
         OperatingSystem.IsMacOS() && e.PhysicalKey == PhysicalKey.Backquote);

    private int GetPageSize()
    {
        var container = EntriesList.ContainerFromIndex(Math.Max(EntriesList.SelectedIndex, 0));
        var itemHeight = container?.Bounds.Height ?? 0;
        // thumbnails show several entries per row
        var itemsPerRow = NavigationMode == PaneNavigationMode.Thumbnail ? GetThumbnailsPerRow() : 1;
        return itemHeight > 0 ? Math.Max(1, (int)(EntriesList.Bounds.Height / itemHeight) - 1) * itemsPerRow : 1;
    }

    /// <summary>
    ///     Moves the current thumbnail to the entry in the same column of the previous or next row. Moving down from the
    ///     row above a shorter last row goes to the last entry.
    /// </summary>
    private void MoveThumbnailRow(bool down)
    {
        var itemsPerRow = GetThumbnailsPerRow();
        var index = Math.Max(EntriesList.SelectedIndex, 0);
        var lastIndex = EntriesList.ItemCount - 1;
        if (down && index / itemsPerRow < lastIndex / itemsPerRow)
        {
            MoveSelection(Math.Min(index + itemsPerRow, lastIndex) - index);
        }
        else if (!down && index >= itemsPerRow)
        {
            MoveSelection(-itemsPerRow);
        }
    }

    /// <summary>Number of thumbnails in a row: the entries at the height of the first one.</summary>
    private int GetThumbnailsPerRow()
    {
        var top = EntriesList.ContainerFromIndex(0)?.Bounds.Top;
        var itemsPerRow = 0;
        while (top != null && itemsPerRow < EntriesList.ItemCount &&
               EntriesList.ContainerFromIndex(itemsPerRow) is { } container &&
               Math.Abs(container.Bounds.Top - top.Value) < 1)
        {
            itemsPerRow++;
        }

        return Math.Max(1, itemsPerRow);
    }

    private void MoveSelection(int offset)
    {
        if (NavigationMode.PluginMode != null)
        {
            EntriesList.SelectedItem = IconLayout.GetItem(EntriesList.SelectedItem as EntryItem, offset);
            return;
        }

        if (EntriesList.ItemCount == 0)
        {
            return;
        }

        EntriesList.SelectedIndex = Math.Clamp(EntriesList.SelectedIndex + offset, 0, EntriesList.ItemCount - 1);
        FocusEntries();
    }

    /// <summary>
    ///     Toggles the mark of the entry under the cursor and moves the cursor to the next entry. With
    ///     <paramref name="calculateSize" /> (Space) a directory it marks gets its size calculated in the background.
    /// </summary>
    private void ToggleMarkAndMoveDown(bool calculateSize = false)
    {
        var item = EntriesList.SelectedItem as EntryItem;
        if (!ToggleSelectedMark())
        {
            return;
        }

        if (calculateSize && item is { IsMarked: true })
        {
            CalculateDirectorySize(item);
        }

        MoveSelection(1);
    }

    /// <summary>
    ///     Calculates the size of a directory (see <see cref="NavigationContext.CalculateDirectorySizeAsync" />),
    ///     showing "..." in its size column until the size is shown, and updates the summary of the marked entries.
    ///     Errors raise <see cref="NavigationFailed" />.
    /// </summary>
    private async void CalculateDirectorySize(EntryItem item)
    {
        var navigationContext = NavigationContext;
        try
        {
            var calculation = navigationContext.CalculateDirectorySizeAsync(item.Entry);
            item.Size = navigationContext.FormatEntrySize(item.Entry);
            if (!await calculation)
            {
                return;
            }
        }
        catch (Exception exception)
        {
            UpdateSize(navigationContext, item);
            NavigationFailed?.Invoke(this, exception);
            return;
        }

        UpdateSize(navigationContext, item);
        if (ReferenceEquals(navigationContext, NavigationContext))
        {
            UpdateSummary();
        }
    }

    /// <summary>
    ///     Shows the size of an entry in its item and, while the pane shows the same navigation context, in the item
    ///     with its name, also after the entries were listed again.
    /// </summary>
    private void UpdateSize(NavigationContext navigationContext, EntryItem item)
    {
        item.Size = navigationContext.FormatEntrySize(item.Entry);
        if (!ReferenceEquals(navigationContext, NavigationContext))
        {
            return;
        }

        var listedItem = (EntriesList.ItemsSource as IReadOnlyList<EntryItem> ?? [])
            .FirstOrDefault(listed => listed.Name.Equals(item.Name, StringComparison.Ordinal));
        if (listedItem != null && !ReferenceEquals(listedItem, item))
        {
            listedItem.Size = navigationContext.FormatEntrySize(listedItem.Entry);
        }
    }

    /// <summary>
    ///     Toggles the mark of the entry under the cursor and moves the cursor like the arrow key: to the next or
    ///     previous entry in the list, a row or an entry of thumbnails, or the nearest icon of a plugin's navigation
    ///     mode in that direction.
    /// </summary>
    private void ToggleMarkAndMove(Key arrowKey)
    {
        if (!ToggleSelectedMark())
        {
            return;
        }

        if (NavigationMode.PluginMode != null)
        {
            EntriesList.SelectedItem = IconLayout.GetItem(EntriesList.SelectedItem as EntryItem, arrowKey switch
            {
                Key.Left => NavigationDirection.Left,
                Key.Up => NavigationDirection.Up,
                Key.Right => NavigationDirection.Right,
                _ => NavigationDirection.Down
            }) ?? EntriesList.SelectedItem;
        }
        else if (NavigationMode == PaneNavigationMode.Thumbnail && arrowKey is Key.Up or Key.Down)
        {
            MoveThumbnailRow(arrowKey == Key.Down);
        }
        else
        {
            MoveSelection(arrowKey is Key.Down or Key.Right ? 1 : -1);
        }
    }

    /// <summary>Toggles the mark of the entry under the cursor. Returns false when there's no entry under it.</summary>
    private bool ToggleSelectedMark()
    {
        if (EntriesList.SelectedItem is not EntryItem item)
        {
            return false;
        }

        item.IsMarked = NavigationContext.ToggleMark(item.Entry);
        UpdateSummary();
        return true;
    }

    /// <summary>
    ///     Marks the entries matching the select group options, see <see cref="NavigationContext.MarkGroup" />. Errors
    ///     like an invalid regular expression raise <see cref="NavigationFailed" />.
    /// </summary>
    public void MarkGroup(SelectGroupOptions options) =>
        ChangeMarks(entries => NavigationContext.MarkGroup(entries, options));

    /// <summary>
    ///     Unmarks the entries matching the unselect group options, see <see cref="NavigationContext.UnmarkGroup" />.
    ///     Errors like an invalid regular expression raise <see cref="NavigationFailed" />.
    /// </summary>
    public void UnmarkGroup(SelectGroupOptions options) =>
        ChangeMarks(entries => NavigationContext.UnmarkGroup(entries, options));

    /// <summary>Inverts the marks of the entries, see <see cref="NavigationContext.InvertMarks" />.</summary>
    public void InvertMarks() => ChangeMarks(NavigationContext.InvertMarks);

    /// <summary>Changes the marks of the listed entries and updates the rows and the summary.</summary>
    private void ChangeMarks(Action<IReadOnlyList<IEntry>> changeMarks)
    {
        var items = EntriesList.ItemsSource as IReadOnlyList<EntryItem> ?? [];
        try
        {
            changeMarks(items.Select(item => item.Entry).ToList());
        }
        catch (Exception exception)
        {
            NavigationFailed?.Invoke(this, exception);
            return;
        }

        foreach (var item in items)
        {
            item.IsMarked = NavigationContext.IsMarked(item.Entry);
        }

        UpdateSummary();
    }

    private void UpdateSummary()
    {
        // kept visible with empty text, so the panel's second line keeps its height
        SummaryText.Text = NavigationContext.MarkedSummary ?? string.Empty;
    }

    private async Task ActivateSelectedEntryAsync()
    {
        if (EntriesList.SelectedItem is not EntryItem item)
        {
            return;
        }

        await RunNavigationAsync(() => NavigationContext.OpenEntryAsync(item.Entry));
    }

    /// <summary>
    ///     Opens the entry under the cursor as a volume (Ctrl+PageDown), e.g. a .docx document as a zip archive, see
    ///     <see cref="NavigationContext.OpenAsVolumeAsync" />.
    /// </summary>
    public async Task OpenSelectedEntryAsVolumeAsync()
    {
        if (EntriesList.SelectedItem is not EntryItem item)
        {
            return;
        }

        await RunNavigationAsync(() => NavigationContext.OpenAsVolumeAsync(item.Entry));
    }

    private async Task RunNavigationAsync(Func<Task> action)
    {
        // icons of files inside images are read from the pane's volume, so stop before navigating
        _iconsCancellation?.Cancel();
        try
        {
            await action();
            await RefreshAsync();
        }
        catch (OperationCanceledException)
        {
            // cancelled by the user, e.g. making a temporary copy of an image inside another image: the location is
            // unchanged, listed again to load the icons stopped before navigating
            await RunNavigationAsync(() => Task.CompletedTask);
        }
        catch (Exception exception)
        {
            NavigationFailed?.Invoke(this, exception);
        }
    }

    /// <summary>
    ///     Sorts the entries by a column whose header was clicked (ascending, or reversed when already sorted by it),
    ///     keeping the entry under the cursor and the icons read.
    /// </summary>
    private void SortBy(string column)
    {
        var items = (EntriesList.ItemsSource as IReadOnlyList<EntryItem> ?? [])
            .ToDictionary<EntryItem, IEntry>(item => item.Entry, ReferenceEqualityComparer.Instance);
        StopQuickSearch();
        var selectedItem = EntriesList.SelectedItem;
        var sorted = NavigationContext.ToggleSort(column)
            .Select(entry => items.GetValueOrDefault(entry))
            .OfType<EntryItem>()
            .ToList();
        UpdateHeaders();
        EntriesList.ItemsSource = sorted;
        EntriesList.SelectedItem = selectedItem;
        if (EntriesList.SelectedItem != null)
        {
            EntriesList.ScrollIntoView(EntriesList.SelectedItem);
        }

        FocusEntries();
        SortChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Shows the sorting of the entries in the column headers, e.g. "Name ▲".</summary>
    private void UpdateHeaders()
    {
        var sort = NavigationContext.Sort;
        for (var index = 0; index < _columns.Count && index < _headerTexts.Count; index++)
        {
            _headerTexts[index].Text = sort.GetHeader(_columns[index]);
        }
    }

    private async Task RefreshAsync()
    {
        StopQuickSearch();
        var hadFocus = EntriesList.IsKeyboardFocusWithin || IconLayout.IsKeyboardFocusWithin;
        var selectedIndex = EntriesList.SelectedIndex;
        _volumeFilesCancellation?.Cancel();
        var navigationEntries = await Core.ListingProgress.ListEntriesAsync(NavigationContext, ShowProgress,
            HideProgress);
        UpdateColumns();
        var columns = NavigationContext.Columns;
        var entries = navigationEntries
            .Select(entry => new EntryItem(entry, NavigationContext.IsMarked(entry), NavigationContext.IsExecutable(entry),
                columns, NavigationContext.GetColumnText))
            .ToList();
        var previousPath = PathText.Text;
        DetectVolumeFiles(entries);

        PathText.Text = NavigationContext.CurrentPath;
        DiskSpaceText.Text = NavigationContext.DiskSpace ?? string.Empty;
        UpdateSummary();
        UpdateHeaders();
        EntriesList.ItemsSource = entries;
        EntriesList.SelectedIndex = NavigationContext.GetSelectedIndex(navigationEntries, previousPath, selectedIndex);
        if (EntriesList.SelectedItem != null && NavigationMode.PluginMode == null)
        {
            EntriesList.ScrollIntoView(EntriesList.SelectedItem);
        }

        if (NavigationMode.PluginMode is { } navigationMode)
        {
            // opening a directory shows its icons only
            await ShowLayoutAsync(navigationMode, entries);
            if (hadFocus)
            {
                FocusEntries();
            }

            return;
        }

        if (hadFocus)
        {
            EntriesList.UpdateLayout();
            FocusEntries();
        }

        LoadIcons(entries);
    }

    /// <summary>
    ///     Shows the icons of the entries laid out by a navigation mode of a plugin, replacing the icons shown. When
    ///     laying out fails, the error is shown and the entries are shown with their icons without positions.
    /// </summary>
    private async Task ShowLayoutAsync(INavigationMode navigationMode, IReadOnlyList<EntryItem> entries)
    {
        _iconsCancellation?.Cancel();
        var cancellation = _iconsCancellation = new CancellationTokenSource();
        IconLayout.Clear();
        IconPositionsBar.IsVisible = false;
        NavigationLayout layout;
        try
        {
            layout = await NavigationContext.LayoutAsync(navigationMode, entries.Select(item => item.Entry).ToList(),
                GetOptions(navigationMode), cancellation.Token);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception exception)
        {
            NavigationFailed?.Invoke(this, exception);
            layout = new NavigationLayout(entries
                .Where(item => item.Name != Hst.Commander.Abstractions.Paths.Parent)
                .Select(item => new NavigationIcon(item.Name, null, null, null, null))
                .ToList());
        }

        if (cancellation.IsCancellationRequested)
        {
            return;
        }

        IconLayout.CanMove = NavigationContext.CanSaveIconPositions;
        IconLayout.Show(navigationMode.Style, layout, entries);
        IconLayout.SetToolbar(NavigationContext.HasParent);
        IconLayout.SetOptions(navigationMode.Options, GetOptions(navigationMode));
        // like the Workbench, opening a drawer selects no icon (the list selects ".."), the current entry is only
        // kept when it's shown
        if (EntriesList.SelectedItem is EntryItem selectedItem && !IconLayout.Contains(selectedItem))
        {
            EntriesList.SelectedItem = null;
        }

        UpdateIconSelection();
        var namesWithoutImage = layout.Icons.Where(icon => icon.Image == null).Select(icon => icon.Name)
            .ToHashSet(StringComparer.Ordinal);
        if (namesWithoutImage.Count > 0)
        {
            LoadIcons(entries.Where(item => namesWithoutImage.Contains(item.Name)).ToList());
        }
    }

    /// <summary>Values of the options of a navigation mode changed in this pane, the defaults until changed.</summary>
    private Dictionary<string, bool> GetOptions(INavigationMode navigationMode)
    {
        if (!_navigationModeOptions.TryGetValue(navigationMode, out var options))
        {
            options = new Dictionary<string, bool>(StringComparer.Ordinal);
            _navigationModeOptions[navigationMode] = options;
        }

        return options;
    }

    /// <summary>
    ///     Detects the listed files that can be opened as volumes in the background, showing an arrow over their icons
    ///     when detected.
    /// </summary>
    private async void DetectVolumeFiles(IReadOnlyList<EntryItem> entries)
    {
        var cancellation = _volumeFilesCancellation = new CancellationTokenSource();
        var items = entries.ToDictionary<EntryItem, IEntry>(item => item.Entry, ReferenceEqualityComparer.Instance);
        await NavigationContext.DetectVolumeFilesAsync(entries.Select(item => item.Entry).ToList(), entry =>
        {
            if (!cancellation.IsCancellationRequested && items.TryGetValue(entry, out var item))
            {
                item.IsVolumeFile = true;
            }
        }, cancellation.Token);
    }

    /// <summary>
    ///     Loads the icons of the listed entries in the background, replacing the initial icons: small icons in the list,
    ///     large icons for thumbnails and icons without an image in a navigation mode of a plugin.
    /// </summary>
    private async void LoadIcons(IReadOnlyList<EntryItem> entries)
    {
        _iconsCancellation?.Cancel();
        var cancellation = _iconsCancellation = new CancellationTokenSource();
        var pixels = NavigationMode == PaneNavigationMode.Thumbnail ? 80 : NavigationMode.PluginMode != null ? 32 : 16;
        var size = (int)Math.Ceiling(pixels * (TopLevel.GetTopLevel(this)?.RenderScaling ?? 1));
        try
        {
            await FileIcons.LoadAsync(entries, NavigationContext, size, cancellation.Token, pixels > 16);
        }
        catch (Exception)
        {
            // icons are not essential, the initial icons are kept
        }
    }
}
