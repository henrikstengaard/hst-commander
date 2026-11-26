using System.Collections.ObjectModel;
using System.Drawing;
using Hst.Commander.Core;
using TuiAttribute = Terminal.Gui.Drawing.Attribute;

namespace Hst.Commander.Tui;

/// <summary>
///     Modal list of the bookmarks, opened by choosing Bookmarks in the volume provider list and centered over the pane.
///     Like the command palette, a search field above the list filters the bookmarks while typing
///     (<see cref="VolumeChoice.Filter" />), selecting the first match, and Up and Down move between them. OK, Enter or
///     double-click opens the selected bookmark, Cancel or Esc goes back to the volume provider list. Shift+Del or
///     Delete deletes the selected bookmark after confirming and F4, F2 or Edit edits it
///     (<see cref="EditBookmarkDialog" />); the dialog closes when the last bookmark is deleted. <see cref="Result" />
///     holds the chosen bookmark, or null on cancel.
/// </summary>
public sealed class BookmarksDialog : TuiDialog
{
    // rows below the list taken by the buttons and their shadow
    private const int ButtonsHeight = 2;

    // rows above the list taken by the search field and the empty row below it
    private const int SearchHeight = 2;

    // the OK, Edit, Delete and Cancel buttons with their shadows and spacing
    private const int ButtonsWidth = 46;

    private readonly BookmarkStore _bookmarkStore;
    private readonly TuiPalette _palette;
    private readonly TextField _searchField;
    private readonly ListView _listView;
    private IReadOnlyList<Bookmark> _bookmarks;
    private IReadOnlyList<VolumeChoice> _shown = [];

    /// <param name="bookmarks">Bookmarks listed initially, at least one.</param>
    /// <param name="bookmarkStore">Store the bookmarks are deleted from and edited in.</param>
    /// <param name="paneFrame">Frame of the pane the dialog is centered over.</param>
    /// <param name="palette">Palette of the theme.</param>
    public BookmarksDialog(IReadOnlyList<Bookmark> bookmarks, BookmarkStore bookmarkStore, Rectangle paneFrame,
        TuiPalette palette)
    {
        _bookmarks = bookmarks;
        _bookmarkStore = bookmarkStore;
        _palette = palette;
        var items = CreateItems(VolumeChoice.List(bookmarks));
        Title = "Bookmarks";

        // the dialog's border, padding and shadow take space around the search field, list and buttons
        var adornments = GetAdornmentsThickness();
        var width = Math.Min(
            Math.Max(Math.Max(items.Select(item => item.Length).DefaultIfEmpty(0).Max() + 2, Title.Length + 4),
                ButtonsWidth) + adornments.Horizontal,
            Math.Max(paneFrame.Width, 10));
        var height = Math.Min(items.Count + SearchHeight + ButtonsHeight + adornments.Vertical,
            Math.Max(paneFrame.Height, adornments.Vertical + SearchHeight + ButtonsHeight + 1));

        X = paneFrame.X + Math.Max(0, (paneFrame.Width - width) / 2);
        Y = paneFrame.Y + Math.Max(0, (paneFrame.Height - height) / 3);
        Width = width;
        Height = height;
        SetScheme(palette.DialogColorScheme);

        _searchField = new TextField { X = 0, Y = 0, Width = Dim.Fill() };
        _listView = new ListView
        {
            X = 0,
            Y = SearchHeight,
            Width = Dim.Fill(),
            // the buttons are below the content, in the dialog's padding
            Height = Dim.Fill()
        };
        // the search field keeps the focus, so the list draws its selected item in the Active role: the dialog's
        // colors inverted
        var selected = new TuiAttribute(palette.Dialog.Background, palette.Dialog.Foreground);
        _listView.SetScheme(TuiPalette.CreateScheme(palette.Dialog, palette.DialogFocus, active: selected));
        _listView.Accepting += (_, e) =>
        {
            e.Handled = true;
            CloseWithSelected();
        };
        _listView.KeyDown += (_, key) => HandleKey(key);
        _searchField.KeyDown += (_, key) => HandleKey(key);
        _searchField.TextChanged += (_, _) => ShowBookmarks(null, 0);

        var okButton = new Button { Text = "OK", IsDefault = true };
        okButton.Accepting += (_, e) =>
        {
            e.Handled = true;
            CloseWithSelected();
        };
        var editButton = new Button { Text = "_Edit" };
        editButton.Accepting += (_, e) =>
        {
            e.Handled = true;
            EditSelectedBookmark();
        };
        var deleteButton = new Button { Text = "_Delete" };
        deleteButton.Accepting += (_, e) =>
        {
            e.Handled = true;
            DeleteSelectedBookmark();
        };
        var cancelButton = new Button { Text = "Cancel" };
        cancelButton.Accepting += (_, e) =>
        {
            e.Handled = true;
            Application.RequestStop(this);
        };

        Add(_searchField, _listView);
        AddButton(okButton);
        AddButton(editButton);
        AddButton(deleteButton);
        AddButton(cancelButton);
        Initialized += (_, _) =>
        {
            ShowBookmarks(null, 0);
            _searchField.SetFocus();
        };
    }

    /// <summary>Chosen bookmark, null when cancelled.</summary>
    public new Bookmark? Result { get; private set; }

    /// <summary>Rows of the bookmarks: the description followed by the path.</summary>
    private static List<string> CreateItems(IReadOnlyList<VolumeChoice> choices)
    {
        var nameWidth = choices.Select(choice => choice.Name.Length).DefaultIfEmpty(0).Max();
        return choices.Select(choice => $"{choice.Name.PadRight(nameWidth)}  {choice.Description}").ToList();
    }

    private Bookmark? SelectedBookmark => _listView.SelectedItem is { } row && row >= 0 && row < _shown.Count
        ? _shown[row].Bookmark
        : null;

    /// <summary>
    ///     Lists the bookmarks matching the typed text, selecting a bookmark by id or else the row at an index (kept
    ///     within the rows).
    /// </summary>
    private void ShowBookmarks(string? selectId, int row)
    {
        _shown = VolumeChoice.Filter(_bookmarks, _searchField.Text);
        _listView.SetSource(new ObservableCollection<string>(CreateItems(_shown)));
        var selectedRow = selectId != null
            ? _shown.ToList().FindIndex(choice => choice.Bookmark?.Id == selectId)
            : -1;
        _listView.SelectedItem = _shown.Count == 0
            ? null
            : selectedRow >= 0
                ? selectedRow
                : Math.Clamp(row, 0, _shown.Count - 1);
        _listView.EnsureSelectedItemVisible();
        _listView.SetNeedsDraw();
    }

    /// <summary>
    ///     Up, Down, PgUp and PgDn move between the bookmarks, Enter opens the selected one, Shift+Del deletes it and F4
    ///     or F2 edits it.
    /// </summary>
    private void HandleKey(Key key)
    {
        if (key.KeyCode is KeyCode.CursorUp or KeyCode.CursorDown)
        {
            key.Handled = true;
            Move(key.KeyCode == KeyCode.CursorUp ? -1 : 1);
        }
        else if (key.KeyCode is KeyCode.PageUp or KeyCode.PageDown)
        {
            key.Handled = true;
            var rows = Math.Max(1, _listView.Viewport.Height - 1);
            Move(key.KeyCode == KeyCode.PageUp ? -rows : rows);
        }
        else if (key == Key.Enter)
        {
            key.Handled = true;
            CloseWithSelected();
        }
        else if (key == Key.Delete.WithShift)
        {
            key.Handled = true;
            DeleteSelectedBookmark();
        }
        else if (key.KeyCode is KeyCode.F4 or KeyCode.F2)
        {
            key.Handled = true;
            EditSelectedBookmark();
        }
    }

    private void Move(int rows)
    {
        if (_shown.Count == 0)
        {
            return;
        }

        _listView.SelectedItem = Math.Clamp((_listView.SelectedItem ?? 0) + rows, 0, _shown.Count - 1);
        _listView.EnsureSelectedItemVisible();
        _listView.SetNeedsDraw();
    }

    private void DeleteSelectedBookmark()
    {
        if (SelectedBookmark is not { } bookmark ||
            TuiMessageBox.Query("Delete bookmark", $"Delete bookmark '{bookmark.Description}'?", "_Yes", "_No") != 0)
        {
            _searchField.SetFocus();
            return;
        }

        ChangeAndReload(() => _bookmarkStore.Delete(bookmark.Id), null);
    }

    private void EditSelectedBookmark()
    {
        if (SelectedBookmark is not { } bookmark)
        {
            return;
        }

        var dialog = new EditBookmarkDialog(bookmark, _bookmarkStore.CredentialStore.Name, _palette);
        Application.Run(dialog);
        var changes = dialog.Result;
        dialog.Dispose();
        if (changes != null)
        {
            ChangeAndReload(() => _bookmarkStore.Edit(bookmark.Id, changes), bookmark.Id);
        }
        else
        {
            _searchField.SetFocus();
        }
    }

    /// <summary>
    ///     Changes the bookmarks and lists them again from the store with the typed filter, selecting a bookmark by id
    ///     or the row at the same index, and closes the dialog when no bookmarks are left.
    /// </summary>
    private void ChangeAndReload(Action change, string? selectId)
    {
        try
        {
            change();
        }
        catch (IOException exception)
        {
            ErrorBox.Show(exception);
        }

        var row = _listView.SelectedItem ?? 0;
        _bookmarks = _bookmarkStore.Load();
        if (_bookmarks.Count == 0)
        {
            Application.RequestStop(this);
            return;
        }

        ShowBookmarks(selectId, row);
        _searchField.SetFocus();
    }

    /// <summary>Closes with the selected bookmark, staying open when no bookmark matches.</summary>
    private void CloseWithSelected()
    {
        if (SelectedBookmark is not { } bookmark)
        {
            return;
        }

        Result = bookmark;
        Application.RequestStop(this);
    }
}
