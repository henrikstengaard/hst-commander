using Avalonia.Controls;
using Hst.Commander.Core;

namespace Hst.Commander.Gui;

/// <summary>
///     Modal dialog confirming quitting the application. The dialog returns true on yes, false on no or null when
///     closed, which is treated as no.
/// </summary>
public partial class QuitDialog : Window
{
    public QuitDialog()
    {
        InitializeComponent();
        MessageText.Text = AppInfo.Current.QuitQuestion;
        YesButton.Click += (_, _) => Close(true);
        NoButton.Click += (_, _) => Close(false);
        Opened += (_, _) => YesButton.Focus();
    }
}
