using Avalonia.Controls;

namespace Hst.Commander.Gui;

/// <summary>
///     Modal dialog asking whether to save the changes of a file when closing the editor. The dialog returns true on
///     save, false on discard or null on cancel (or when closed), which keeps editing.
/// </summary>
public partial class SaveChangesDialog : Window
{
    public SaveChangesDialog() : this(string.Empty)
    {
    }

    public SaveChangesDialog(string name)
    {
        InitializeComponent();
        MessageText.Text = $"Save the changes to '{name}'?";
        SaveButton.Click += (_, _) => Close(true);
        DiscardButton.Click += (_, _) => Close(false);
        CancelButton.Click += (_, _) => Close();
        Opened += (_, _) => SaveButton.Focus();
    }
}
