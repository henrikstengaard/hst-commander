using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Hst.Commander.Abstractions;
using Hst.Commander.Core;

namespace Hst.Commander.Gui;

/// <summary>
///     Modal list of Bookmarks followed by the volume providers a pane can show, opened with Alt+F1 (left pane) or
///     Alt+F2 (right pane) and centered over the pane with Bookmarks selected. Like the command palette, a search box
///     above the list filters the items while typing, selecting the first match, and Up and Down move between them.
///     The dialog returns the chosen volume
///     provider or bookmark on OK, Enter or double-click, or null when cancelled. Choosing Bookmarks opens the bookmark list
///     (<see cref="BookmarksDialog" />), or says there are no bookmarks; cancelling it goes back to this list.
/// </summary>
public partial class VolumeProviderDialog : Window
{
    private readonly BookmarkStore? _bookmarkStore;
    private readonly Control? _pane;
    private readonly IReadOnlyList<VolumeChoice> _choices;

    public VolumeProviderDialog() : this("Volume", [], null, null)
    {
    }

    /// <param name="title">Title of the dialog, e.g. "Left".</param>
    /// <param name="volumeProviders">Volume providers to choose from.</param>
    /// <param name="bookmarkStore">Bookmarks listed when choosing Bookmarks, null for none.</param>
    /// <param name="pane">Pane the dialog is centered over, null to center it over the owner.</param>
    public VolumeProviderDialog(string title, IReadOnlyList<IVolumeProvider> volumeProviders,
        BookmarkStore? bookmarkStore, Control? pane)
    {
        InitializeComponent();
        _bookmarkStore = bookmarkStore;
        _pane = pane;
        Title = title;
        WindowStartupLocation = pane == null ? WindowStartupLocation.CenterOwner : WindowStartupLocation.Manual;
        _choices = VolumeChoice.List(volumeProviders);
        VolumeProvidersList.DoubleTapped += async (_, _) => await ChooseSelectedAsync();
        VolumeProvidersList.SelectionChanged += (_, _) => OkButton.IsEnabled = VolumeProvidersList.SelectedIndex >= 0;
        SearchBox.TextChanged += (_, _) =>
            ShowFiltered(VolumeProvidersList, VolumeChoice.Filter(_choices, SearchBox.Text));
        // tunnelling, before the search box, the list and the default OK button handle the keys themselves
        AddHandler(KeyDownEvent, async (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                e.Handled = true;
                await ChooseSelectedAsync();
                return;
            }

            HandleMoveKey(VolumeProvidersList, e);
        }, RoutingStrategies.Tunnel);
        OkButton.Click += async (_, _) => await ChooseSelectedAsync();
        CancelButton.Click += (_, _) => Close(null);
        // Bookmarks, the first item, is selected
        ShowFiltered(VolumeProvidersList, _choices);
        Opened += (_, _) =>
        {
            if (pane != null)
            {
                CenterOver(this, pane);
            }

            SearchBox.Focus();
        };
    }

    /// <summary>Closes with the selected volume provider, or lets a bookmark be chosen for Bookmarks.</summary>
    private async Task ChooseSelectedAsync()
    {
        if (VolumeProvidersList.SelectedItem is not VolumeChoice choice)
        {
            return;
        }

        if (choice.IsBookmarks)
        {
            if (await ChooseBookmarkAsync() is not { } bookmark)
            {
                SearchBox.Focus();
                return;
            }

            choice = new VolumeChoice(null, bookmark);
        }

        Close(choice);
    }

    /// <summary>Shows the bookmark list read from the store, or says there are no bookmarks.</summary>
    private async Task<Bookmark?> ChooseBookmarkAsync()
    {
        var bookmarks = _bookmarkStore?.Load() ?? [];
        if (_bookmarkStore == null || bookmarks.Count == 0)
        {
            await new ConfirmDialog("Bookmarks", VolumeChoice.NoBookmarksMessage, "_OK", null).ShowDialog<bool?>(this);
            return null;
        }

        return await new BookmarksDialog(bookmarks, _bookmarkStore, _pane).ShowDialog<Bookmark?>(this);
    }

    /// <summary>Centers a dialog horizontally over a pane, in the upper third vertically.</summary>
    internal static void CenterOver(Window window, Control pane)
    {
        var paneTopLeft = pane.PointToScreen(new Point(0, 0));
        var paneBottomRight = pane.PointToScreen(new Point(pane.Bounds.Width, pane.Bounds.Height));
        var width = (int)(window.Bounds.Width * window.RenderScaling);
        var height = (int)(window.Bounds.Height * window.RenderScaling);
        window.Position = new PixelPoint(
            paneTopLeft.X + Math.Max(0, (paneBottomRight.X - paneTopLeft.X - width) / 2),
            paneTopLeft.Y + Math.Max(0, (paneBottomRight.Y - paneTopLeft.Y - height) / 3));
    }

    /// <summary>
    ///     Shows the items of a list filtered by a search box, selecting the first. Used by the lists with a search box
    ///     above them (the volume provider and create volume lists).
    /// </summary>
    internal static void ShowFiltered<T>(ListBox listBox, IReadOnlyList<T> items)
    {
        listBox.ItemsSource = items;
        listBox.SelectedIndex = items.Count > 0 ? 0 : -1;
        if (items.Count > 0)
        {
            listBox.ScrollIntoView(0);
        }
    }

    /// <summary>
    ///     Up, Down, PgUp and PgDn move the selection of a list filtered by a search box, which keeps the focus.
    /// </summary>
    internal static void HandleMoveKey(ListBox listBox, KeyEventArgs e)
    {
        int rows;
        switch (e.Key)
        {
            case Key.Up:
            case Key.Down:
                rows = e.Key == Key.Up ? -1 : 1;
                break;
            case Key.PageUp:
            case Key.PageDown:
                // about a page of rows, each a name and a description
                var page = Math.Max(1, (int)(listBox.Bounds.Height / 44) - 1);
                rows = e.Key == Key.PageUp ? -page : page;
                break;
            default:
                return;
        }

        e.Handled = true;
        if (listBox.ItemCount == 0)
        {
            return;
        }

        var index = Math.Clamp(Math.Max(0, listBox.SelectedIndex) + rows, 0, listBox.ItemCount - 1);
        listBox.SelectedIndex = index;
        listBox.ScrollIntoView(index);
    }
}
