using Avalonia.Controls;
using Hst.Commander.Core.Volumes;

namespace Hst.Commander.Gui;

/// <summary>
///     Modal dialog asking whether to overwrite a file that already exists when copying. The dialog returns the
///     answer, or null when closed, which is treated as no.
/// </summary>
public partial class OverwriteDialog : Window
{
    public OverwriteDialog() : this(string.Empty)
    {
    }

    public OverwriteDialog(string message)
    {
        InitializeComponent();
        MessageText.Text = message;
        YesButton.Click += (_, _) => Close(OverwriteAnswer.Yes);
        NoButton.Click += (_, _) => Close(OverwriteAnswer.No);
        AllButton.Click += (_, _) => Close(OverwriteAnswer.All);
        Opened += (_, _) => YesButton.Focus();
    }
}
