using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Hst.Commander.Abstractions;
using Hst.Commander.Core;

namespace Hst.Commander.Gui;

/// <summary>
///     Modal list of the volume providers a pane can show, followed by Bookmarks, opened with Alt+F1 (left pane) or
///     Alt+F2 (right pane) and centered over the pane. The dialog returns the chosen volume provider or bookmark on OK,
///     Enter or double-click, or null when cancelled. Choosing Bookmarks opens the bookmark list
///     (<see cref="BookmarksDialog" />), or says there are no bookmarks; cancelling it goes back to this list.
/// </summary>
public partial class VolumeProviderDialog : Window
{
    private readonly BookmarkStore? _bookmarkStore;
    private readonly Control? _pane;

    public VolumeProviderDialog() : this("Volume", [], null, null, null)
    {
    }

    /// <param name="title">Title of the dialog, e.g. "Left".</param>
    /// <param name="volumeProviders">Volume providers to choose from.</param>
    /// <param name="bookmarkStore">Bookmarks listed when choosing Bookmarks, null for none.</param>
    /// <param name="currentVolumeProvider">Volume provider selected initially, the one the pane shows.</param>
    /// <param name="pane">Pane the dialog is centered over, null to center it over the owner.</param>
    public VolumeProviderDialog(string title, IReadOnlyList<IVolumeProvider> volumeProviders,
        BookmarkStore? bookmarkStore, IVolumeProvider? currentVolumeProvider, Control? pane)
    {
        InitializeComponent();
        _bookmarkStore = bookmarkStore;
        _pane = pane;
        Title = title;
        WindowStartupLocation = pane == null ? WindowStartupLocation.CenterOwner : WindowStartupLocation.Manual;
        var choices = VolumeChoice.List(volumeProviders).ToList();
        VolumeProvidersList.ItemsSource = choices;
        VolumeProvidersList.SelectedIndex =
            Math.Max(0, choices.FindIndex(choice => choice.VolumeProvider == currentVolumeProvider));
        VolumeProvidersList.DoubleTapped += async (_, _) => await ChooseSelectedAsync();
        // tunnelling, as the focused list item takes Enter before the default OK button sees it
        VolumeProvidersList.AddHandler(KeyDownEvent, async (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                e.Handled = true;
                await ChooseSelectedAsync();
            }
        }, RoutingStrategies.Tunnel);
        OkButton.Click += async (_, _) => await ChooseSelectedAsync();
        CancelButton.Click += (_, _) => Close(null);
        Opened += (_, _) =>
        {
            if (pane != null)
            {
                CenterOver(this, pane);
            }

            FocusSelected(VolumeProvidersList);
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
                FocusSelected(VolumeProvidersList);
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

    /// <summary>Focuses the selected item of a list, or the list when nothing is selected.</summary>
    internal static void FocusSelected(ListBox listBox)
    {
        if (listBox.SelectedIndex < 0)
        {
            listBox.Focus(NavigationMethod.Tab);
            return;
        }

        listBox.UpdateLayout();
        listBox.ContainerFromIndex(listBox.SelectedIndex)?.Focus(NavigationMethod.Tab);
    }
}
