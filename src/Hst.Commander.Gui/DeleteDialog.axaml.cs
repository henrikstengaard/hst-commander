using Avalonia.Controls;

namespace Hst.Commander.Gui;

/// <summary>
///     Modal dialog confirming a delete. The dialog returns true on yes, false on no or null when closed, which is
///     treated as no.
/// </summary>
public partial class DeleteDialog : Window
{
    public DeleteDialog() : this(string.Empty)
    {
    }

    public DeleteDialog(string message)
    {
        InitializeComponent();
        MessageText.Text = message;
        YesButton.Click += (_, _) => Close(true);
        NoButton.Click += (_, _) => Close(false);
        Opened += (_, _) => YesButton.Focus();
    }
}
