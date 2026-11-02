using System.Collections.ObjectModel;
using System.Drawing;
using Hst.Commander.Abstractions;
using Hst.Commander.Core;
using Terminal.Gui;

namespace Hst.Commander.Tui;

/// <summary>
///     Modal list of the volume providers a pane can show, followed by Bookmarks, opened with Alt+F1 (left pane) or
///     Alt+F2 (right pane) and centered over the pane. OK, Enter or double-click chooses the selected item, Cancel or Esc
///     cancels. Choosing Bookmarks opens the bookmark list (<see cref="BookmarksDialog" />), or says there are no
///     bookmarks; cancelling it goes back to this list. <see cref="Result" /> holds the chosen volume provider or
///     bookmark, or null on cancel.
/// </summary>
public sealed class VolumeProviderDialog : Dialog
{
    // rows below the list taken by the OK and Cancel buttons and their shadow
    private const int ButtonsHeight = 2;

    // the OK and Cancel buttons with their shadows and spacing
    private const int ButtonsWidth = 22;

    private readonly IReadOnlyList<VolumeChoice> _choices;
    private readonly BookmarkStore _bookmarkStore;
    private readonly Rectangle _paneFrame;
    private readonly TuiPalette _palette;

    public VolumeProviderDialog(string title, IReadOnlyList<IVolumeProvider> volumeProviders,
        BookmarkStore bookmarkStore, IVolumeProvider currentVolumeProvider, Rectangle paneFrame, TuiPalette palette)
    {
        _choices = VolumeChoice.List(volumeProviders);
        _bookmarkStore = bookmarkStore;
        _paneFrame = paneFrame;
        _palette = palette;
        var nameWidth = _choices.Max(choice => choice.Name.Length);
        var items = _choices.Select(choice => $"{choice.Name.PadRight(nameWidth)}  {choice.Description}").ToList();
        Title = title;

        // the dialog's border, padding and shadow take space around the list and buttons
        var adornments = GetAdornmentsThickness();
        var width = Math.Min(
            Math.Max(Math.Max(items.Max(item => item.Length) + 2, title.Length + 4), ButtonsWidth) +
            adornments.Horizontal,
            Math.Max(paneFrame.Width, 10));
        var height = Math.Min(items.Count + ButtonsHeight + adornments.Vertical,
            Math.Max(paneFrame.Height, adornments.Vertical + ButtonsHeight + 1));

        X = paneFrame.X + Math.Max(0, (paneFrame.Width - width) / 2);
        Y = paneFrame.Y + Math.Max(0, (paneFrame.Height - height) / 3);
        Width = width;
        Height = height;
        ColorScheme = palette.DialogColorScheme;

        var listView = new ListView
        {
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Height = Dim.Fill(ButtonsHeight)
        };
        listView.SetSource(new ObservableCollection<string>(items));
        listView.SelectedItem = Math.Max(0,
            _choices.ToList().FindIndex(choice => choice.VolumeProvider == currentVolumeProvider));
        listView.OpenSelectedItem += (_, e) => Choose(e.Item);
        listView.KeyDown += (_, e) =>
        {
            if (e == Key.Enter)
            {
                e.Handled = true;
                Choose(listView.SelectedItem);
            }
        };

        var okButton = new Button { Text = "OK", IsDefault = true };
        okButton.Accepting += (_, e) =>
        {
            e.Cancel = true;
            Choose(listView.SelectedItem);
        };
        var cancelButton = new Button { Text = "Cancel" };
        cancelButton.Accepting += (_, e) =>
        {
            e.Cancel = true;
            Application.RequestStop(this);
        };

        Add(listView);
        AddButton(okButton);
        AddButton(cancelButton);
        Initialized += (_, _) => listView.SetFocus();
    }

    /// <summary>Chosen volume provider or bookmark, null when cancelled.</summary>
    public VolumeChoice? Result { get; private set; }

    /// <summary>Closes with the volume provider of a row, or lets a bookmark be chosen for the Bookmarks row.</summary>
    private void Choose(int index)
    {
        if (index < 0 || index >= _choices.Count)
        {
            return;
        }

        var choice = _choices[index];
        if (choice.IsBookmarks)
        {
            if (ChooseBookmark() is not { } bookmark)
            {
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
            MessageBox.Query("Bookmarks", VolumeChoice.NoBookmarksMessage, "OK");
            return null;
        }

        var dialog = new BookmarksDialog(bookmarks, _bookmarkStore, _paneFrame, _palette);
        Application.Run(dialog);
        var bookmark = dialog.Result;
        dialog.Dispose();
        return bookmark;
    }
}
