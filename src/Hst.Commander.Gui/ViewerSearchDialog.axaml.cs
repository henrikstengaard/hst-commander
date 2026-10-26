using Avalonia.Controls;
using Hst.Commander.Core;

namespace Hst.Commander.Gui;

/// <summary>
///     Modal dialog asking what to search for in the viewer (F7): the text or regular expression, case sensitive and
///     backwards. The dialog returns the options on OK or null on cancel.
/// </summary>
public partial class ViewerSearchDialog : Window
{
    public ViewerSearchDialog() : this(null)
    {
    }

    public ViewerSearchDialog(ViewerSearchOptions? options)
    {
        InitializeComponent();

        SearchTextBox.Text = options?.Text ?? string.Empty;
        RegexCheckBox.IsChecked = options?.IsRegex == true;
        CaseSensitiveCheckBox.IsChecked = options?.CaseSensitive == true;
        BackwardsCheckBox.IsChecked = options?.Backwards == true;
        OkButton.Click += (_, _) => Close(new ViewerSearchOptions(SearchTextBox.Text ?? string.Empty,
            RegexCheckBox.IsChecked == true, CaseSensitiveCheckBox.IsChecked == true,
            BackwardsCheckBox.IsChecked == true));
        CancelButton.Click += (_, _) => Close();
        Opened += (_, _) =>
        {
            SearchTextBox.Focus();
            SearchTextBox.SelectAll();
        };
    }
}
