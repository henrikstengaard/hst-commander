using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Hst.Commander.Abstractions;

namespace Hst.Commander.Gui;

/// <summary>
///     Modal list of the volume providers a pane can show, opened with Alt+F1 (left pane) or Alt+F2 (right pane) and
///     centered over the pane. The dialog returns the chosen volume provider on OK, Enter or double-click, or null when
///     cancelled.
/// </summary>
public partial class VolumeProviderDialog : Window
{
    public VolumeProviderDialog() : this("Volume", [], null, null)
    {
    }

    /// <param name="title">Title of the dialog, e.g. "Left".</param>
    /// <param name="volumeProviders">Volume providers to choose from.</param>
    /// <param name="currentVolumeProvider">Volume provider selected initially, the one the pane shows.</param>
    /// <param name="pane">Pane the dialog is centered over, null to center it over the owner.</param>
    public VolumeProviderDialog(string title, IReadOnlyList<IVolumeProvider> volumeProviders,
        IVolumeProvider? currentVolumeProvider, Control? pane)
    {
        InitializeComponent();
        Title = title;
        WindowStartupLocation = pane == null ? WindowStartupLocation.CenterOwner : WindowStartupLocation.Manual;
        VolumeProvidersList.ItemsSource = volumeProviders;
        VolumeProvidersList.SelectedItem =
            currentVolumeProvider != null && volumeProviders.Contains(currentVolumeProvider)
                ? currentVolumeProvider
                : volumeProviders.FirstOrDefault();
        VolumeProvidersList.DoubleTapped += (_, _) => CloseWithSelected();
        // tunnelling, as the focused list item takes Enter before the default OK button sees it
        VolumeProvidersList.AddHandler(KeyDownEvent, (_, e) =>
        {
            if (e.Key != Key.Enter)
            {
                return;
            }

            e.Handled = true;
            CloseWithSelected();
        }, RoutingStrategies.Tunnel);
        OkButton.Click += (_, _) => CloseWithSelected();
        CancelButton.Click += (_, _) => Close(null);
        Opened += (_, _) =>
        {
            if (pane != null)
            {
                CenterOver(pane);
            }

            FocusSelected();
        };
    }

    private void CloseWithSelected()
    {
        if (VolumeProvidersList.SelectedItem is IVolumeProvider volumeProvider)
        {
            Close(volumeProvider);
        }
    }

    /// <summary>Centers the dialog horizontally over the pane, in the upper third vertically.</summary>
    private void CenterOver(Control pane)
    {
        var paneTopLeft = pane.PointToScreen(new Point(0, 0));
        var paneBottomRight = pane.PointToScreen(new Point(pane.Bounds.Width, pane.Bounds.Height));
        var width = (int)(Bounds.Width * RenderScaling);
        var height = (int)(Bounds.Height * RenderScaling);
        Position = new PixelPoint(
            paneTopLeft.X + Math.Max(0, (paneBottomRight.X - paneTopLeft.X - width) / 2),
            paneTopLeft.Y + Math.Max(0, (paneBottomRight.Y - paneTopLeft.Y - height) / 3));
    }

    private void FocusSelected()
    {
        if (VolumeProvidersList.SelectedIndex < 0)
        {
            VolumeProvidersList.Focus(NavigationMethod.Tab);
            return;
        }

        VolumeProvidersList.UpdateLayout();
        VolumeProvidersList.ContainerFromIndex(VolumeProvidersList.SelectedIndex)?.Focus(NavigationMethod.Tab);
    }
}
