using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Interactivity;
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

    /// <summary>Values of the options of the navigation modes changed by the user, by mode and option name.</summary>
    private readonly Dictionary<INavigationMode, Dictionary<string, bool>> _navigationModeOptions = [];

    public PaneView()
    {
        InitializeComponent();

        _listItemTemplate = EntriesList.ItemTemplate;
        _listItemsPanel = EntriesList.ItemsPanel;
        _listHorizontalScrollBarVisibility = ScrollViewer.GetHorizontalScrollBarVisibility(EntriesList);
        EntriesList.AddHandler(KeyDownEvent, OnEntriesKeyDown, RoutingStrategies.Tunnel);
        EntriesList.DoubleTapped += async (_, _) => await ActivateSelectedEntryAsync();
        EntriesList.SelectionChanged += (_, _) => UpdateIconSelection();
        PathBadge.DoubleTapped += (_, _) => PathDoubleClicked?.Invoke(this, EventArgs.Empty);
        EntriesList.PropertyChanged += (_, e) =>
        {
            if (e.Property == IsKeyboardFocusWithinProperty)
            {
                UpdatePathBadge();
            }
        };

        IconLayout.AddHandler(KeyDownEvent, OnEntriesKeyDown, RoutingStrategies.Tunnel);
        IconLayout.PropertyChanged += (_, e) =>
        {
            if (e.Property == IsKeyboardFocusWithinProperty)
            {
                UpdatePathBadge();
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
        SavePositionsButton.Click += async (_, _) => await SaveIconPositionsAsync();
        ResetPositionsButton.Click += (_, _) =>
        {
            IconLayout.Reset();
            FocusEntries();
        };
    }

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

    private void UpdatePathBadge() =>
        PathBadge.Classes.Set("active", EntriesList.IsKeyboardFocusWithin || IconLayout.IsKeyboardFocusWithin);

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

    /// <summary>Raised when the path is double-clicked, so the window can show the location dialog.</summary>
    public event EventHandler? PathDoubleClicked;

    public NavigationContext NavigationContext
    {
        get => _navigationContext ?? throw new InvalidOperationException("Navigation context is not set.");
        set => _navigationContext = value;
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

    /// <summary>Shows the root of a new volume from a volume provider, e.g. chosen with Alt+F1/Alt+F2.</summary>
    public Task ChangeVolumeProviderAsync(IVolumeProvider volumeProvider) =>
        RunNavigationAsync(() => NavigationContext.ChangeVolumeProviderAsync(volumeProvider));

    /// <summary>Changes to a location entered in the location dialog, see <see cref="NavigationContext.ChangeLocationAsync" />.</summary>
    public Task ChangeLocationAsync(string path) =>
        RunNavigationAsync(() => NavigationContext.ChangeLocationAsync(path));

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

    private async void OnEntriesKeyDown(object? sender, KeyEventArgs e)
    {
        if (IsGoRootKey(e))
        {
            e.Handled = true;
            await RunNavigationAsync(() => NavigationContext.GoRootAsync());
            return;
        }

        switch (e.Key)
        {
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
            case Key.Enter:
                e.Handled = true;
                await ActivateSelectedEntryAsync();
                break;
            case Key.Insert:
            case Key.Space:
                e.Handled = true;
                ToggleMarkAndMoveDown();
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
    ///     Ctrl+\ going to the root of the volume, matched by the typed character or the backslash keys (on some
    ///     layouts, e.g. Danish, the key with backslash is the 102nd key).
    /// </summary>
    private static bool IsGoRootKey(KeyEventArgs e) =>
        e.KeyModifiers == KeyModifiers.Control &&
        (e.KeySymbol is "\\" or "\u001C" || e.Key is Key.OemPipe or Key.OemBackslash);

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

    /// <summary>Toggles the mark of the entry under the cursor and moves the cursor to the next entry.</summary>
    private void ToggleMarkAndMoveDown()
    {
        if (EntriesList.SelectedItem is not EntryItem item)
        {
            return;
        }

        item.IsMarked = NavigationContext.ToggleMark(item.Entry);
        UpdateSummary();
        MoveSelection(1);
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
        catch (Exception exception)
        {
            NavigationFailed?.Invoke(this, exception);
        }
    }

    private async Task RefreshAsync()
    {
        var hadFocus = EntriesList.IsKeyboardFocusWithin || IconLayout.IsKeyboardFocusWithin;
        var selectedIndex = EntriesList.SelectedIndex;
        var navigationEntries = await NavigationContext.ListEntriesAsync();
        var entries = navigationEntries
            .Select(entry => new EntryItem(entry, NavigationContext.IsMarked(entry), NavigationContext.IsExecutable(entry)))
            .ToList();
        var previousPath = PathText.Text;

        PathText.Text = NavigationContext.CurrentPath;
        DiskSpaceText.Text = NavigationContext.DiskSpace ?? string.Empty;
        UpdateSummary();
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
