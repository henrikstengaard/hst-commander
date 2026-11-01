using Avalonia.Controls;
using Hst.Commander.Core;

namespace Hst.Commander.Gui;

/// <summary>
///     About dialog (Help > About): the name, version and author, and the Licenses section listing the license notices
///     (<see cref="Licenses.GetNotices" />) of Hst Commander, its third-party components and the loaded plugins. View
///     license or double-clicking a notice shows its text.
/// </summary>
public partial class AboutDialog : Window
{
    public AboutDialog()
    {
        InitializeComponent();
        var appInfo = AppInfo.Current;
        NameAndVersionText.Text = appInfo.NameAndVersion;
        AuthorText.Text = $"Created by {appInfo.Author}";
        LicensesSummaryText.Text = Licenses.Summary;
        LicensesList.ItemsSource = Licenses.GetNotices();
        LicensesList.SelectedIndex = 0;

        LicensesList.DoubleTapped += async (_, _) => await ViewLicenseAsync();
        ViewLicenseButton.Click += async (_, _) => await ViewLicenseAsync();
        OkButton.Click += (_, _) => Close();
        Opened += (_, _) => OkButton.Focus();
    }

    private async Task ViewLicenseAsync()
    {
        if (LicensesList.SelectedItem is LicenseNotice notice)
        {
            await new LicenseDialog(notice).ShowDialog(this);
        }
    }
}
