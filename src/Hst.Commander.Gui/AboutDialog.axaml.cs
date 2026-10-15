using Avalonia.Controls;
using Hst.Commander.Core;

namespace Hst.Commander.Gui;

public partial class AboutDialog : Window
{
    public AboutDialog()
    {
        InitializeComponent();
        var appInfo = AppInfo.Current;
        NameAndVersionText.Text = appInfo.NameAndVersion;
        AuthorText.Text = $"Created by {appInfo.Author}";
        OkButton.Click += (_, _) => Close();
        Opened += (_, _) => OkButton.Focus();
    }
}
