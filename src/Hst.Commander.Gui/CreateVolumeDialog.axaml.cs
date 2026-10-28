using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Hst.Commander.Abstractions;

namespace Hst.Commander.Gui;

/// <summary>
///     Modal list of the volumes that can be created (F9), the volume creators of the plugins. The dialog returns the
///     chosen volume creator on OK, Enter or double-click, or null when cancelled.
/// </summary>
public partial class CreateVolumeDialog : Window
{
    public CreateVolumeDialog() : this([])
    {
    }

    public CreateVolumeDialog(IReadOnlyList<IVolumeCreator> creators)
    {
        InitializeComponent();
        CreatorsList.ItemsSource = creators;
        CreatorsList.SelectedItem = creators.FirstOrDefault();
        CreatorsList.DoubleTapped += (_, _) => CloseWithSelected();
        // tunnelling, as the focused list item takes Enter before the default OK button sees it
        CreatorsList.AddHandler(KeyDownEvent, (_, e) =>
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
        Opened += (_, _) => FocusSelected();
    }

    private void CloseWithSelected()
    {
        if (CreatorsList.SelectedItem is IVolumeCreator creator)
        {
            Close(creator);
        }
    }

    private void FocusSelected()
    {
        if (CreatorsList.SelectedIndex < 0)
        {
            CreatorsList.Focus(NavigationMethod.Tab);
            return;
        }

        CreatorsList.UpdateLayout();
        CreatorsList.ContainerFromIndex(CreatorsList.SelectedIndex)?.Focus(NavigationMethod.Tab);
    }
}
