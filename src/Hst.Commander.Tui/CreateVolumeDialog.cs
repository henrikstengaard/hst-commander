using System.Collections.ObjectModel;
using Hst.Commander.Abstractions;
using Terminal.Gui;

namespace Hst.Commander.Tui;

/// <summary>
///     Modal list of the volumes that can be created (F9), the volume creators of the plugins. OK, Enter or
///     double-click chooses the selected volume creator, Cancel or Esc cancels. <see cref="Result" /> holds the chosen
///     volume creator or null on cancel.
/// </summary>
public sealed class CreateVolumeDialog : Dialog
{
    // rows below the list taken by the OK and Cancel buttons and their shadow
    private const int ButtonsHeight = 2;

    // the OK and Cancel buttons with their shadows and spacing
    private const int ButtonsWidth = 22;

    private readonly IReadOnlyList<IVolumeCreator> _creators;
    private readonly ListView _listView;

    public CreateVolumeDialog(IReadOnlyList<IVolumeCreator> creators, TuiPalette palette)
    {
        _creators = creators;
        var nameWidth = creators.Max(creator => creator.Name.Length);
        var items = creators
            .Select(creator => $"{creator.Name.PadRight(nameWidth)}  {creator.Description}")
            .ToList();
        Title = "Create";

        // the dialog's border, padding and shadow take space around the list and buttons
        var adornments = GetAdornmentsThickness();
        Width = Math.Max(Math.Max(items.Max(item => item.Length) + 2, Title.Length + 4), ButtonsWidth) +
                adornments.Horizontal;
        Height = Math.Min(creators.Count + ButtonsHeight + adornments.Vertical, 20);
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

    /// <summary>Chosen volume creator, null when cancelled.</summary>
    public IVolumeCreator? Result { get; private set; }

    private void CloseWith(int index)
    {
        if (index < 0 || index >= _creators.Count)
        {
            return;
        }

        Result = _creators[index];
        Application.RequestStop(this);
    }
}
