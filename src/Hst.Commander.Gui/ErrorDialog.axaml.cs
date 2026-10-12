using Avalonia.Controls;

namespace Hst.Commander.Gui;

public partial class ErrorDialog : Window
{
    public ErrorDialog() : this(string.Empty)
    {
    }

    public ErrorDialog(string message)
    {
        InitializeComponent();
        MessageText.Text = message;
        OkButton.Click += (_, _) => Close();
        Opened += (_, _) => OkButton.Focus();
    }
}
