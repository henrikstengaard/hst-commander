using System.Collections.ObjectModel;
using System.Drawing;
using Hst.Commander.Abstractions;
using Terminal.Gui;

namespace Hst.Commander.Tui;

/// <summary>
///     Modal list of the volume providers a pane can show, opened with Alt+F1 (left pane) or Alt+F2 (right pane) and
///     centered over the pane. Enter or double-click chooses a volume provider, Esc cancels. <see cref="Result" /> holds
///     the chosen volume provider or null on cancel.
/// </summary>
public sealed class VolumeProviderDialog : Dialog
{
    public VolumeProviderDialog(string title, IReadOnlyList<IVolumeProvider> volumeProviders,
        IVolumeProvider currentVolumeProvider, Rectangle paneFrame, TuiPalette palette)
    {
        var nameWidth = volumeProviders.Max(volumeProvider => volumeProvider.Name.Length);
        var items = volumeProviders
            .Select(volumeProvider => $"{volumeProvider.Name.PadRight(nameWidth)}  {volumeProvider.Description}")
            .ToList();
        Title = title;

        // the dialog's border, padding and shadow take space around the list
        var adornments = GetAdornmentsThickness();
        var width = Math.Min(Math.Max(items.Max(item => item.Length) + 2, title.Length + 4) + adornments.Horizontal,
            Math.Max(paneFrame.Width, 10));
        var height = Math.Min(volumeProviders.Count + adornments.Vertical,
            Math.Max(paneFrame.Height, adornments.Vertical + 1));

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
            Height = Dim.Fill()
        };
        listView.SetSource(new ObservableCollection<string>(items));
        listView.SelectedItem = Math.Max(0, volumeProviders.ToList().IndexOf(currentVolumeProvider));
        listView.OpenSelectedItem += (_, e) =>
        {
            Result = volumeProviders[e.Item];
            Application.RequestStop(this);
        };

        Add(listView);
        Initialized += (_, _) => listView.SetFocus();
    }

    /// <summary>Chosen volume provider, null when cancelled.</summary>
    public IVolumeProvider? Result { get; private set; }
}
