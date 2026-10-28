using System.Collections.ObjectModel;
using System.Drawing;
using Hst.Commander.Abstractions;
using Terminal.Gui;

namespace Hst.Commander.Tui;

/// <summary>
///     Modal list of the volume providers a pane can show, opened with Alt+F1 (left pane) or Alt+F2 (right pane) and
///     centered over the pane. OK, Enter or double-click chooses the selected volume provider, Cancel or Esc cancels.
///     <see cref="Result" /> holds the chosen volume provider or null on cancel.
/// </summary>
public sealed class VolumeProviderDialog : Dialog
{
    // rows below the list taken by the OK and Cancel buttons and their shadow
    private const int ButtonsHeight = 2;

    // the OK and Cancel buttons with their shadows and spacing
    private const int ButtonsWidth = 22;

    private readonly IReadOnlyList<IVolumeProvider> _volumeProviders;
    private readonly ListView _listView;

    public VolumeProviderDialog(string title, IReadOnlyList<IVolumeProvider> volumeProviders,
        IVolumeProvider currentVolumeProvider, Rectangle paneFrame, TuiPalette palette)
    {
        _volumeProviders = volumeProviders;
        var nameWidth = volumeProviders.Max(volumeProvider => volumeProvider.Name.Length);
        var items = volumeProviders
            .Select(volumeProvider => $"{volumeProvider.Name.PadRight(nameWidth)}  {volumeProvider.Description}")
            .ToList();
        Title = title;

        // the dialog's border, padding and shadow take space around the list and buttons
        var adornments = GetAdornmentsThickness();
        var width = Math.Min(
            Math.Max(Math.Max(items.Max(item => item.Length) + 2, title.Length + 4), ButtonsWidth) +
            adornments.Horizontal,
            Math.Max(paneFrame.Width, 10));
        var height = Math.Min(volumeProviders.Count + ButtonsHeight + adornments.Vertical,
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
        _listView.SelectedItem = Math.Max(0, volumeProviders.ToList().IndexOf(currentVolumeProvider));
        _listView.OpenSelectedItem += (_, e) => CloseWith(e.Item);
        _listView.KeyDown += (_, e) =>
        {
            if (e != Key.Enter)
            {
                return;
            }

            e.Handled = true;
            CloseWith(_listView.SelectedItem);
        };

        var okButton = new Button { Text = "OK", IsDefault = true };
        okButton.Accepting += (_, e) =>
        {
            e.Cancel = true;
            CloseWith(_listView.SelectedItem);
        };
        var cancelButton = new Button { Text = "Cancel" };
        cancelButton.Accepting += (_, e) =>
        {
            e.Cancel = true;
            Application.RequestStop(this);
        };

        Add(_listView);
        AddButton(okButton);
        AddButton(cancelButton);
        Initialized += (_, _) => _listView.SetFocus();
    }

    /// <summary>Chosen volume provider, null when cancelled.</summary>
    public IVolumeProvider? Result { get; private set; }

    private void CloseWith(int index)
    {
        if (index < 0 || index >= _volumeProviders.Count)
        {
            return;
        }

        Result = _volumeProviders[index];
        Application.RequestStop(this);
    }
}
