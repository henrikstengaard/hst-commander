using System.Collections.ObjectModel;
using System.Drawing;
using Hst.Commander.Core;
using Terminal.Gui;

namespace Hst.Commander.Tui;

/// <summary>
///     Modal list of the bookmarks, opened by choosing Bookmarks in the volume provider list and centered over the pane.
///     OK, Enter or double-click opens the selected bookmark, Cancel or Esc goes back to the volume provider list. Del
///     or Delete deletes the selected bookmark after confirming and F2 or Rename changes its description; the dialog
///     closes when the last bookmark is deleted. <see cref="Result" /> holds the chosen bookmark, or null on cancel.
/// </summary>
public sealed class BookmarksDialog : Dialog
{
    // rows below the list taken by the buttons and their shadow
    private const int ButtonsHeight = 2;

    // the OK, Rename, Delete and Cancel buttons with their shadows and spacing
    private const int ButtonsWidth = 46;

    private readonly BookmarkStore _bookmarkStore;
    private readonly TuiPalette _palette;
    private readonly ListView _listView;
    private IReadOnlyList<Bookmark> _bookmarks;

    /// <param name="bookmarks">Bookmarks listed initially, at least one.</param>
    /// <param name="bookmarkStore">Store the bookmarks are deleted from and renamed in.</param>
    /// <param name="paneFrame">Frame of the pane the dialog is centered over.</param>
    /// <param name="palette">Palette of the theme.</param>
    public BookmarksDialog(IReadOnlyList<Bookmark> bookmarks, BookmarkStore bookmarkStore, Rectangle paneFrame,
        TuiPalette palette)
    {
        _bookmarks = bookmarks;
        _bookmarkStore = bookmarkStore;
        _palette = palette;
        var items = CreateItems();
        Title = "Bookmarks";

        // the dialog's border, padding and shadow take space around the list and buttons
        var adornments = GetAdornmentsThickness();
        var width = Math.Min(
            Math.Max(Math.Max(items.Select(item => item.Length).DefaultIfEmpty(0).Max() + 2, Title.Length + 4),
                ButtonsWidth) + adornments.Horizontal,
            Math.Max(paneFrame.Width, 10));
        var height = Math.Min(items.Count + ButtonsHeight + adornments.Vertical,
            Math.Max(paneFrame.Height, adornments.Vertical + ButtonsHeight + 1));

        X = paneFrame.X + Math.Max(0, (paneFrame.Width - width) / 2);
        Y = paneFrame.Y + Math.Max(0, (paneFrame.Height - height) / 3);
        Width = width;
        Height = height;
        ColorScheme = palette.DialogColorScheme;

        _listView = new ListView
        {
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Height = Dim.Fill(ButtonsHeight)
        };
        _listView.SetSource(new ObservableCollection<string>(items));
        _listView.SelectedItem = 0;
        _listView.OpenSelectedItem += (_, e) => CloseWith(e.Item);
        _listView.KeyDown += (_, e) =>
        {
            if (e == Key.Enter)
            {
                e.Handled = true;
                CloseWith(_listView.SelectedItem);
            }
            else if (e.KeyCode == KeyCode.Delete)
            {
                e.Handled = true;
                DeleteSelectedBookmark();
            }
            else if (e.KeyCode == KeyCode.F2)
            {
                e.Handled = true;
                RenameSelectedBookmark();
            }
        };

        var okButton = new Button { Text = "OK", IsDefault = true };
        okButton.Accepting += (_, e) =>
        {
            e.Cancel = true;
            CloseWith(_listView.SelectedItem);
        };
        var renameButton = new Button { Text = "_Rename" };
        renameButton.Accepting += (_, e) =>
        {
            e.Cancel = true;
            RenameSelectedBookmark();
        };
        var deleteButton = new Button { Text = "_Delete" };
        deleteButton.Accepting += (_, e) =>
        {
            e.Cancel = true;
            DeleteSelectedBookmark();
        };
        var cancelButton = new Button { Text = "Cancel" };
        cancelButton.Accepting += (_, e) =>
        {
            e.Cancel = true;
            Application.RequestStop(this);
        };

        Add(_listView);
        AddButton(okButton);
        AddButton(renameButton);
        AddButton(deleteButton);
        AddButton(cancelButton);
        Initialized += (_, _) => _listView.SetFocus();
    }

    /// <summary>Chosen bookmark, null when cancelled.</summary>
    public Bookmark? Result { get; private set; }

    /// <summary>Rows of the bookmarks: the description followed by the path.</summary>
    private List<string> CreateItems()
    {
        var choices = VolumeChoice.List(_bookmarks);
        var nameWidth = choices.Select(choice => choice.Name.Length).DefaultIfEmpty(0).Max();
        return choices.Select(choice => $"{choice.Name.PadRight(nameWidth)}  {choice.Description}").ToList();
    }

    private Bookmark? SelectedBookmark => _listView.SelectedItem >= 0 && _listView.SelectedItem < _bookmarks.Count
        ? _bookmarks[_listView.SelectedItem]
        : null;

    private void DeleteSelectedBookmark()
    {
        if (SelectedBookmark is not { } bookmark ||
            MessageBox.Query("Delete bookmark", $"Delete bookmark '{bookmark.Description}'?", "_Yes", "_No") != 0)
        {
            return;
        }

        ChangeAndReload(() => _bookmarkStore.Delete(bookmark.Id), null);
    }

    private void RenameSelectedBookmark()
    {
        if (SelectedBookmark is not { } bookmark)
        {
            return;
        }

        var dialog = new BookmarkDialog("Rename bookmark", bookmark.Path, bookmark.Description, _palette);
        Application.Run(dialog);
        var description = dialog.Result;
        dialog.Dispose();
        if (description != null)
        {
            ChangeAndReload(() => _bookmarkStore.Rename(bookmark.Id, description), bookmark.Id);
        }
    }

    /// <summary>
    ///     Changes the bookmarks and lists them again from the store, selecting a bookmark by id or the row at the same
    ///     index, and closes the dialog when no bookmarks are left.
    /// </summary>
    private void ChangeAndReload(Action change, string? selectId)
    {
        try
        {
            change();
        }
        catch (IOException exception)
        {
            MessageBox.ErrorQuery("Error", exception.Message, "OK");
        }

        var row = _listView.SelectedItem;
        _bookmarks = _bookmarkStore.Load();
        if (_bookmarks.Count == 0)
        {
            Application.RequestStop(this);
            return;
        }

        _listView.SetSource(new ObservableCollection<string>(CreateItems()));
        var selectedRow = selectId != null ? _bookmarks.ToList().FindIndex(bookmark => bookmark.Id == selectId) : -1;
        _listView.SelectedItem = selectedRow >= 0 ? selectedRow : Math.Clamp(row, 0, _bookmarks.Count - 1);
        _listView.SetFocus();
    }

    private void CloseWith(int index)
    {
        if (index < 0 || index >= _bookmarks.Count)
        {
            return;
        }

        Result = _bookmarks[index];
        Application.RequestStop(this);
    }
}
