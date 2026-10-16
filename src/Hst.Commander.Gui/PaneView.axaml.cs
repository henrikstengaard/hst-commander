using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Hst.Commander.Core;
using Hst.Commander.Abstractions;
using Hst.Commander.Gui.Icons;

namespace Hst.Commander.Gui;

/// <summary>
///     Navigation pane listing the entries of the current location of a <see cref="NavigationContext" />.
/// </summary>
public partial class PaneView : UserControl
{
    private NavigationContext? _navigationContext;
    private CancellationTokenSource? _iconsCancellation;

    public PaneView()
    {
        InitializeComponent();

        EntriesList.AddHandler(KeyDownEvent, OnEntriesKeyDown, RoutingStrategies.Tunnel);
        EntriesList.DoubleTapped += async (_, _) => await ActivateSelectedEntryAsync();
        PathBadge.DoubleTapped += (_, _) => PathDoubleClicked?.Invoke(this, EventArgs.Empty);
        EntriesList.PropertyChanged += (_, e) =>
        {
            if (e.Property == IsKeyboardFocusWithinProperty)
            {
                PathBadge.Classes.Set("active", EntriesList.IsKeyboardFocusWithin);
            }
        };
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
        set => EntriesList.Classes.Set("active", value);
    }

    public IEntry? SelectedEntry => (EntriesList.SelectedItem as EntryItem)?.Entry;

    public Task InitializeAsync() => RefreshEntriesAsync();

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

    /// <summary>Renames an entry in the current location and selects it, see <see cref="NavigationContext.RenameAsync" />.</summary>
    public Task RenameAsync(IEntry entry, string newName) =>
        RunNavigationAsync(() => NavigationContext.RenameAsync(entry, newName));

    public void FocusEntries()
    {
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
        return itemHeight > 0 ? Math.Max(1, (int)(EntriesList.Bounds.Height / itemHeight) - 1) : 1;
    }

    private void MoveSelection(int offset)
    {
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
        SummaryText.Text = NavigationContext.MarkedSummary;
        SummaryText.IsVisible = SummaryText.Text != null;
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
        var hadFocus = EntriesList.IsKeyboardFocusWithin;
        var selectedIndex = EntriesList.SelectedIndex;
        var navigationEntries = await NavigationContext.ListEntriesAsync();
        var entries = navigationEntries
            .Select(entry => new EntryItem(entry, NavigationContext.IsMarked(entry), NavigationContext.IsExecutable(entry)))
            .ToList();
        var previousPath = PathText.Text;

        PathText.Text = NavigationContext.CurrentPath;
        DiskSpaceText.Text = NavigationContext.DiskSpace;
        DiskSpaceText.IsVisible = DiskSpaceText.Text != null;
        UpdateSummary();
        EntriesList.ItemsSource = entries;
        EntriesList.SelectedIndex = NavigationContext.GetSelectedIndex(navigationEntries, previousPath, selectedIndex);
        if (EntriesList.SelectedItem != null)
        {
            EntriesList.ScrollIntoView(EntriesList.SelectedItem);
        }

        if (hadFocus)
        {
            EntriesList.UpdateLayout();
            FocusEntries();
        }

        LoadIcons(entries);
    }

    /// <summary>Loads the icons of the listed entries in the background, replacing the initial icons.</summary>
    private async void LoadIcons(IReadOnlyList<EntryItem> entries)
    {
        _iconsCancellation?.Cancel();
        var cancellation = _iconsCancellation = new CancellationTokenSource();
        var size = (int)Math.Ceiling(16 * (TopLevel.GetTopLevel(this)?.RenderScaling ?? 1));
        try
        {
            await FileIcons.LoadAsync(entries, NavigationContext, size, cancellation.Token);
        }
        catch (Exception)
        {
            // icons are not essential, the initial icons are kept
        }
    }
}
