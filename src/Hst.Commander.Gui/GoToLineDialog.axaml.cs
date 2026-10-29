using Avalonia.Controls;

namespace Hst.Commander.Gui;

/// <summary>
///     Modal dialog asking for the line to go to in the editor (Ctrl+G). The dialog returns the one-based line on OK or
///     null on cancel.
/// </summary>
public partial class GoToLineDialog : Window
{
    public GoToLineDialog() : this(1)
    {
    }

    public GoToLineDialog(int line)
    {
        InitializeComponent();

        LineTextBox.Text = line.ToString();
        OkButton.Click += (_, _) =>
        {
            if (int.TryParse(LineTextBox.Text?.Trim(), out var number) && number >= 1)
            {
                Close(number);
                return;
            }

            ErrorText.IsVisible = true;
            LineTextBox.Focus();
        };
        CancelButton.Click += (_, _) => Close();
        Opened += (_, _) =>
        {
            LineTextBox.Focus();
            LineTextBox.SelectAll();
        };
    }
}
