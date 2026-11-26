using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Hst.Commander.Abstractions;
using Hst.Commander.Core;

namespace Hst.Commander.Gui;

/// <summary>
///     Modal list of the volumes that can be created (F9), the volume creators of the plugins. Like the command palette,
///     a search box above the list filters the creators while typing (<see cref="VolumeCreation.Filter" />), selecting
///     the first match, and Up and Down move between them. The dialog returns the chosen volume creator on OK, Enter or
///     double-click, or null when cancelled.
/// </summary>
public partial class CreateVolumeDialog : Window
{
    public CreateVolumeDialog() : this([])
    {
    }

    public CreateVolumeDialog(IReadOnlyList<IVolumeCreator> creators)
    {
        InitializeComponent();
        CreatorsList.DoubleTapped += (_, _) => CloseWithSelected();
        CreatorsList.SelectionChanged += (_, _) => OkButton.IsEnabled = CreatorsList.SelectedIndex >= 0;
        SearchBox.TextChanged += (_, _) =>
            VolumeProviderDialog.ShowFiltered(CreatorsList, VolumeCreation.Filter(creators, SearchBox.Text));
        // tunnelling, before the search box, the list and the default OK button handle the keys themselves
        AddHandler(KeyDownEvent, (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                e.Handled = true;
                CloseWithSelected();
                return;
            }

            VolumeProviderDialog.HandleMoveKey(CreatorsList, e);
        }, RoutingStrategies.Tunnel);
        OkButton.Click += (_, _) => CloseWithSelected();
        CancelButton.Click += (_, _) => Close(null);
        VolumeProviderDialog.ShowFiltered(CreatorsList, creators);
        Opened += (_, _) => SearchBox.Focus();
    }

    private void CloseWithSelected()
    {
        if (CreatorsList.SelectedItem is IVolumeCreator creator)
        {
            Close(creator);
        }
    }
}
