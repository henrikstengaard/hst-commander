using Avalonia.Controls;
using Hst.Commander.Core;

namespace Hst.Commander.Gui;

/// <summary>
///     Modal dialog asking for the filter of the entries to select (mark) with + or unselect with -, titled Select group
///     or Unselect group: wildcards like "*.txt" or a regular expression, case sensitive and files only. The dialog
///     returns the options on OK or null on cancel.
/// </summary>
public partial class SelectGroupDialog : Window
{
    public SelectGroupDialog() : this(SelectGroupOptions.Default)
    {
    }

    public SelectGroupDialog(SelectGroupOptions options, string title = "Select group")
    {
        InitializeComponent();

        Title = title;
        FilterTextBox.Text = options.Filter;
        CaseSensitiveCheckBox.IsChecked = options.CaseSensitive;
        FilesOnlyCheckBox.IsChecked = options.FilesOnly;
        OkButton.Click += (_, _) => Close(new SelectGroupOptions(FilterTextBox.Text ?? string.Empty,
            CaseSensitiveCheckBox.IsChecked == true, FilesOnlyCheckBox.IsChecked == true));
        CancelButton.Click += (_, _) => Close();
        Opened += (_, _) =>
        {
            FilterTextBox.Focus();
            FilterTextBox.SelectAll();
        };
    }
}
