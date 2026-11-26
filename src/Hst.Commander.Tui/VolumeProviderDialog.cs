using System.Collections.ObjectModel;
using System.Drawing;
using Hst.Commander.Abstractions;
using Hst.Commander.Core;
using TuiAttribute = Terminal.Gui.Drawing.Attribute;

namespace Hst.Commander.Tui;

/// <summary>
///     Modal list of Bookmarks followed by the volume providers a pane can show, opened with Alt+F1 (left pane) or
///     Alt+F2 (right pane) and centered over the pane with Bookmarks selected. Like the command palette, a search field
///     above the list filters the items while typing (<see cref="VolumeChoice.Filter(IEnumerable{VolumeChoice}, string?)" />),
///     selecting the first match, and Up and Down move between them. OK, Enter or double-click chooses the selected
///     item, Cancel or Esc cancels. Choosing Bookmarks opens the bookmark list (<see cref="BookmarksDialog" />), or says
///     there are no bookmarks; cancelling it goes back to this list. <see cref="Result" /> holds the chosen volume
///     provider or bookmark, or null on cancel.
/// </summary>
public sealed class VolumeProviderDialog : TuiDialog
{
    // rows below the list taken by the OK and Cancel buttons and their shadow
    private const int ButtonsHeight = 2;

    // rows above the list taken by the search field and the empty row below it
    private const int SearchHeight = 2;

    // the OK and Cancel buttons with their shadows and spacing
    private const int ButtonsWidth = 22;

    private readonly IReadOnlyList<VolumeChoice> _choices;
    private readonly BookmarkStore _bookmarkStore;
    private readonly Rectangle _paneFrame;
    private readonly TuiPalette _palette;
    private readonly TextField _searchField;
    private readonly ListView _listView;
    private readonly int _nameWidth;
    private IReadOnlyList<VolumeChoice> _shown = [];

    public VolumeProviderDialog(string title, IReadOnlyList<IVolumeProvider> volumeProviders,
        BookmarkStore bookmarkStore, Rectangle paneFrame, TuiPalette palette)
    {
        _choices = VolumeChoice.List(volumeProviders);
        _bookmarkStore = bookmarkStore;
        _paneFrame = paneFrame;
        _palette = palette;
        _nameWidth = _choices.Max(choice => choice.Name.Length);
        var items = CreateItems(_choices);
        Title = title;

        // the dialog's border, padding and shadow take space around the search field, list and buttons
        var adornments = GetAdornmentsThickness();
        var width = Math.Min(
            Math.Max(Math.Max(items.Max(item => item.Length) + 2, title.Length + 4), ButtonsWidth) +
            adornments.Horizontal,
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
            ChooseSelected();
        };
        _listView.KeyDown += (_, key) => HandleKey(key);
        _searchField.KeyDown += (_, key) => HandleKey(key);
        _searchField.TextChanged += (_, _) => ShowChoices();

        var okButton = new Button { Text = "OK", IsDefault = true };
        okButton.Accepting += (_, e) =>
        {
            e.Handled = true;
            ChooseSelected();
        };
        var cancelButton = new Button { Text = "Cancel" };
        cancelButton.Accepting += (_, e) =>
        {
            e.Handled = true;
            Application.RequestStop(this);
        };

        Add(_searchField, _listView);
        AddButton(okButton);
        AddButton(cancelButton);
        Initialized += (_, _) =>
        {
            // Bookmarks, the first item, is selected
            ShowChoices();
            _searchField.SetFocus();
        };
    }

    /// <summary>Chosen volume provider or bookmark, null when cancelled.</summary>
    public new VolumeChoice? Result { get; private set; }

    /// <summary>Rows of the items: the name followed by the description, aligned for all items.</summary>
    private List<string> CreateItems(IReadOnlyList<VolumeChoice> choices) =>
        choices.Select(choice => $"{choice.Name.PadRight(_nameWidth)}  {choice.Description}").ToList();

    /// <summary>Lists the items matching the typed text, selecting the first.</summary>
    private void ShowChoices()
    {
        _shown = VolumeChoice.Filter(_choices, _searchField.Text);
        _listView.SetSource(new ObservableCollection<string>(CreateItems(_shown)));
        _listView.SelectedItem = _shown.Count == 0 ? null : 0;
        _listView.EnsureSelectedItemVisible();
        _listView.SetNeedsDraw();
    }

    /// <summary>Up, Down, PgUp and PgDn move between the items and Enter chooses the selected one.</summary>
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
            ChooseSelected();
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

    /// <summary>
    ///     Closes with the selected volume provider, or lets a bookmark be chosen for Bookmarks; stays open when no item
    ///     matches.
    /// </summary>
    private void ChooseSelected()
    {
        if (_listView.SelectedItem is not { } index || index < 0 || index >= _shown.Count)
        {
            return;
        }

        var choice = _shown[index];
        if (choice.IsBookmarks)
        {
            if (ChooseBookmark() is not { } bookmark)
            {
                _searchField.SetFocus();
                return;
            }

            choice = new VolumeChoice(null, bookmark);
        }

        Result = choice;
        Application.RequestStop(this);
    }

    /// <summary>Shows the bookmark list read from the store, or says there are no bookmarks.</summary>
    private Bookmark? ChooseBookmark()
    {
        var bookmarks = _bookmarkStore.Load();
        if (bookmarks.Count == 0)
        {
            TuiMessageBox.Query("Bookmarks", VolumeChoice.NoBookmarksMessage, "OK");
            return null;
        }

        var dialog = new BookmarksDialog(bookmarks, _bookmarkStore, _paneFrame, _palette);
        Application.Run(dialog);
        var bookmark = dialog.Result;
        dialog.Dispose();
        return bookmark;
    }
}
