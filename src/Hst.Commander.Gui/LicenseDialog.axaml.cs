using Avalonia.Controls;
using Hst.Commander.Core;

namespace Hst.Commander.Gui;

/// <summary>Shows the text of a license notice from the About dialog, read-only and selectable to copy.</summary>
public partial class LicenseDialog : Window
{
    /// <summary>For the XAML previewer.</summary>
    public LicenseDialog()
    {
        InitializeComponent();
    }

    public LicenseDialog(LicenseNotice notice) : this()
    {
        Title = notice.Title;
        LicenseTextBox.Text = notice.Text;
        CloseButton.Click += (_, _) => Close();
        Opened += (_, _) => LicenseTextBox.Focus();
    }
}
