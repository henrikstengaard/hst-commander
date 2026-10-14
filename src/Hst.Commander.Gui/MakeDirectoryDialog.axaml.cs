using Avalonia.Controls;

namespace Hst.Commander.Gui;

/// <summary>
///     Modal dialog asking for the name of the directory to create (F7). The dialog returns the name on OK or null on
///     cancel.
/// </summary>
public partial class MakeDirectoryDialog : Window
{
    public MakeDirectoryDialog()
    {
        InitializeComponent();

        OkButton.Click += (_, _) => Close(NameTextBox.Text ?? string.Empty);
        CancelButton.Click += (_, _) => Close();
        Opened += (_, _) => NameTextBox.Focus();
    }
}
