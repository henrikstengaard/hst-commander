using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Hst.Commander.Core;

namespace Hst.Commander.Gui;

/// <summary>
///     Modal list of the bookmarks, opened by choosing Bookmarks in the volume provider list and centered over the pane.
///     The dialog returns the chosen bookmark on OK, Enter or double-click, or null when cancelled. Delete or Del
///     deletes the selected bookmark after confirming and Rename or F2 changes its description; the dialog closes when
///     the last bookmark is deleted.
/// </summary>
public partial class BookmarksDialog : Window
{
    private readonly BookmarkStore? _bookmarkStore;
    private IReadOnlyList<Bookmark> _bookmarks;

    public BookmarksDialog() : this([], null, null)
    {
    }

    /// <param name="bookmarks">Bookmarks listed initially.</param>
    /// <param name="bookmarkStore">Store the bookmarks are deleted from and renamed in, null for none.</param>
    /// <param name="pane">Pane the dialog is centered over, null to center it over the owner.</param>
    public BookmarksDialog(IReadOnlyList<Bookmark> bookmarks, BookmarkStore? bookmarkStore, Control? pane)
    {
        InitializeComponent();
        _bookmarks = bookmarks;
        _bookmarkStore = bookmarkStore;
        WindowStartupLocation = pane == null ? WindowStartupLocation.CenterOwner : WindowStartupLocation.Manual;
        BookmarksList.ItemsSource = VolumeChoice.List(bookmarks);
        BookmarksList.SelectedIndex = bookmarks.Count > 0 ? 0 : -1;
        BookmarksList.SelectionChanged += (_, _) => UpdateButtons();
        BookmarksList.DoubleTapped += (_, _) => CloseWithSelected();
        // tunnelling, as the focused list item takes Enter before the default OK button sees it
        BookmarksList.AddHandler(KeyDownEvent, async (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                e.Handled = true;
                CloseWithSelected();
            }
            else if (e.Key == Key.Delete && e.KeyModifiers == KeyModifiers.None)
            {
                e.Handled = true;
                await DeleteSelectedBookmarkAsync();
            }
            else if (e.Key == Key.F2 && e.KeyModifiers == KeyModifiers.None)
            {
                e.Handled = true;
                await RenameSelectedBookmarkAsync();
            }
        }, RoutingStrategies.Tunnel);
        RenameButton.Click += async (_, _) => await RenameSelectedBookmarkAsync();
        DeleteButton.Click += async (_, _) => await DeleteSelectedBookmarkAsync();
        OkButton.Click += (_, _) => CloseWithSelected();
        CancelButton.Click += (_, _) => Close(null);
        UpdateButtons();
        Opened += (_, _) =>
        {
            if (pane != null)
            {
                VolumeProviderDialog.CenterOver(this, pane);
            }

            VolumeProviderDialog.FocusSelected(BookmarksList);
        };
    }

    private Bookmark? SelectedBookmark => (BookmarksList.SelectedItem as VolumeChoice)?.Bookmark;

    private void UpdateButtons()
    {
        RenameButton.IsEnabled = SelectedBookmark != null;
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
            VolumeProviderDialog.FocusSelected(BookmarksList);
            return;
        }

        await ChangeAndReloadAsync(() => store.Delete(bookmark.Id), null);
    }

    private async Task RenameSelectedBookmarkAsync()
    {
        if (_bookmarkStore is not { } store || SelectedBookmark is not { } bookmark)
        {
            return;
        }

        var description = await new BookmarkDialog("Rename bookmark", bookmark.Path, bookmark.Description)
            .ShowDialog<string?>(this);
        if (description == null)
        {
            VolumeProviderDialog.FocusSelected(BookmarksList);
            return;
        }

        await ChangeAndReloadAsync(() => store.Rename(bookmark.Id, description), bookmark.Id);
    }

    /// <summary>
    ///     Changes the bookmarks and lists them again from the store, selecting a bookmark by id or the row at the same
    ///     index, and closes the dialog when no bookmarks are left.
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

        var selectedIndex = selectId != null ? _bookmarks.ToList().FindIndex(bookmark => bookmark.Id == selectId) : -1;
        BookmarksList.ItemsSource = VolumeChoice.List(_bookmarks);
        BookmarksList.SelectedIndex = selectedIndex >= 0 ? selectedIndex : Math.Clamp(index, 0, _bookmarks.Count - 1);
        UpdateButtons();
        VolumeProviderDialog.FocusSelected(BookmarksList);
    }

    private void CloseWithSelected()
    {
        if (SelectedBookmark is { } bookmark)
        {
            Close(bookmark);
        }
    }
}
