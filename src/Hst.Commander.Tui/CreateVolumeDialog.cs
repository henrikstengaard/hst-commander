using System.Collections.ObjectModel;
using Hst.Commander.Abstractions;
using Terminal.Gui;

namespace Hst.Commander.Tui;

/// <summary>
///     Modal list of the volumes that can be created (F9), the volume creators of the plugins. Enter or double-click
///     chooses a volume creator, Esc cancels. <see cref="Result" /> holds the chosen volume creator or null on cancel.
/// </summary>
public sealed class CreateVolumeDialog : Dialog
{
    public CreateVolumeDialog(IReadOnlyList<IVolumeCreator> creators, TuiPalette palette)
    {
        var nameWidth = creators.Max(creator => creator.Name.Length);
        var items = creators
            .Select(creator => $"{creator.Name.PadRight(nameWidth)}  {creator.Description}")
            .ToList();
        Title = "Create";

        // the dialog's border, padding and shadow take space around the list
        var adornments = GetAdornmentsThickness();
        Width = Math.Max(items.Max(item => item.Length) + 2, Title.Length + 4) + adornments.Horizontal;
        Height = Math.Min(creators.Count + adornments.Vertical, 20);
        ColorScheme = palette.DialogColorScheme;

        var listView = new ListView
        {
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Height = Dim.Fill()
        };
        listView.SetSource(new ObservableCollection<string>(items));
        listView.SelectedItem = 0;
        listView.OpenSelectedItem += (_, e) =>
        {
            Result = creators[e.Item];
            Application.RequestStop(this);
        };

        Add(listView);
        Initialized += (_, _) => listView.SetFocus();
    }

    /// <summary>Chosen volume creator, null when cancelled.</summary>
    public IVolumeCreator? Result { get; private set; }
}
