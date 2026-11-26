using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Hst.Commander.Core;

namespace Hst.Commander.Gui;

/// <summary>
///     Modal list of the bookmarks, opened by choosing Bookmarks in the volume provider list and centered over the pane.
///     Like the command palette, a search box above the list filters the bookmarks while typing
///     (<see cref="VolumeChoice.Filter" />), selecting the first match, and Up and Down move between them. The dialog
///     returns the chosen bookmark on OK, Enter or double-click, or null when cancelled. Shift+Del or Delete deletes the
///     selected bookmark after confirming and Edit, F4 or F2 edits it (<see cref="EditBookmarkDialog" />); the dialog
///     closes when the last bookmark is deleted.
/// </summary>
public partial class BookmarksDialog : Window
{
    private readonly BookmarkStore? _bookmarkStore;
    private IReadOnlyList<Bookmark> _bookmarks;
    private IReadOnlyList<VolumeChoice> _shown = [];

    public BookmarksDialog() : this([], null, null)
    {
    }

    /// <param name="bookmarks">Bookmarks listed initially.</param>
    /// <param name="bookmarkStore">Store the bookmarks are deleted from and edited in, null for none.</param>
    /// <param name="pane">Pane the dialog is centered over, null to center it over the owner.</param>
    public BookmarksDialog(IReadOnlyList<Bookmark> bookmarks, BookmarkStore? bookmarkStore, Control? pane)
    {
        InitializeComponent();
        _bookmarks = bookmarks;
        _bookmarkStore = bookmarkStore;
        WindowStartupLocation = pane == null ? WindowStartupLocation.CenterOwner : WindowStartupLocation.Manual;
        BookmarksList.SelectionChanged += (_, _) => UpdateButtons();
        BookmarksList.DoubleTapped += (_, _) => CloseWithSelected();
        SearchBox.TextChanged += (_, _) => ShowBookmarks(null, 0);
        // tunnelling, before the search box, the list and the default OK button handle the keys themselves
        AddHandler(KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
        EditButton.Click += async (_, _) => await EditSelectedBookmarkAsync();
        DeleteButton.Click += async (_, _) => await DeleteSelectedBookmarkAsync();
        OkButton.Click += (_, _) => CloseWithSelected();
        CancelButton.Click += (_, _) => Close(null);
        ShowBookmarks(null, 0);
        Opened += (_, _) =>
        {
            if (pane != null)
            {
                VolumeProviderDialog.CenterOver(this, pane);
            }

            SearchBox.Focus();
        };
    }

    private Bookmark? SelectedBookmark => (BookmarksList.SelectedItem as VolumeChoice)?.Bookmark;

    /// <summary>
    ///     Lists the bookmarks matching the typed text, selecting a bookmark by id or else the row at an index (kept
    ///     within the rows).
    /// </summary>
    private void ShowBookmarks(string? selectId, int index)
    {
        _shown = VolumeChoice.Filter(_bookmarks, SearchBox.Text);
        BookmarksList.ItemsSource = _shown;
        var selectedIndex = selectId != null
            ? _shown.ToList().FindIndex(choice => choice.Bookmark?.Id == selectId)
            : -1;
        Select(_shown.Count == 0 ? -1 : selectedIndex >= 0 ? selectedIndex : Math.Clamp(index, 0, _shown.Count - 1));
        UpdateButtons();
    }

    private void Select(int index)
    {
        BookmarksList.SelectedIndex = index;
        if (index >= 0)
        {
            BookmarksList.ScrollIntoView(index);
        }
    }

    /// <summary>
    ///     Up, Down, PgUp and PgDn move between the bookmarks, Enter opens the selected one, Shift+Del deletes it and F4
    ///     or F2 edits it.
    /// </summary>
    private async void OnKeyDown(object? sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Up:
            case Key.Down:
                e.Handled = true;
                Move(e.Key == Key.Up ? -1 : 1);
                break;
            case Key.PageUp:
            case Key.PageDown:
                e.Handled = true;
                // about a page of rows, each a name and a description
                var rows = Math.Max(1, (int)(BookmarksList.Bounds.Height / 44) - 1);
                Move(e.Key == Key.PageUp ? -rows : rows);
                break;
            case Key.Enter:
                e.Handled = true;
                CloseWithSelected();
                break;
            case Key.Delete when e.KeyModifiers == KeyModifiers.Shift:
                e.Handled = true;
                await DeleteSelectedBookmarkAsync();
                break;
            case Key.F4 or Key.F2 when e.KeyModifiers == KeyModifiers.None:
                e.Handled = true;
                await EditSelectedBookmarkAsync();
                break;
        }
    }

    private void Move(int rows)
    {
        if (_shown.Count == 0)
        {
            return;
        }

        Select(Math.Clamp(Math.Max(0, BookmarksList.SelectedIndex) + rows, 0, _shown.Count - 1));
    }

    private void UpdateButtons()
    {
        EditButton.IsEnabled = SelectedBookmark != null;
        DeleteButton.IsEnabled = SelectedBookmark != null;
        OkButton.IsEnabled = SelectedBookmark != null;
    }

    private async Task DeleteSelectedBookmarkAsync()
    {
        if (_bookmarkStore is not { } store || SelectedBookmark is not { } bookmark)
        {
            return;
        }

        if (await new ConfirmDialog("Delete bookmark", $"Delete bookmark '{bookmark.Description}'?", "_Yes", "_No")
                .ShowDialog<bool?>(this) != true)
        {
            SearchBox.Focus();
            return;
        }

        await ChangeAndReloadAsync(() => store.Delete(bookmark.Id), null);
    }

    private async Task EditSelectedBookmarkAsync()
    {
        if (_bookmarkStore is not { } store || SelectedBookmark is not { } bookmark)
        {
            return;
        }

        var changes = await new EditBookmarkDialog(bookmark, store.CredentialStore.Name)
            .ShowDialog<BookmarkChanges?>(this);
        if (changes == null)
        {
            SearchBox.Focus();
            return;
        }

        await ChangeAndReloadAsync(() => store.Edit(bookmark.Id, changes), bookmark.Id);
    }

    /// <summary>
    ///     Changes the bookmarks and lists them again from the store with the typed filter, selecting a bookmark by id
    ///     or the row at the same index, and closes the dialog when no bookmarks are left.
    /// </summary>
    private async Task ChangeAndReloadAsync(Action change, string? selectId)
    {
        try
        {
            change();
        }
        catch (IOException exception)
        {
            await new ErrorDialog(exception.Message).ShowDialog(this);
        }

        var index = BookmarksList.SelectedIndex;
        _bookmarks = _bookmarkStore?.Load() ?? [];
        if (_bookmarks.Count == 0)
        {
            Close(null);
            return;
        }

        ShowBookmarks(selectId, index);
        SearchBox.Focus();
    }

    private void CloseWithSelected()
    {
        if (SelectedBookmark is { } bookmark)
        {
            Close(bookmark);
        }
    }
}
