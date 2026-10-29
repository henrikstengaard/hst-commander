using Avalonia.Controls;
using Hst.Commander.Core;

namespace Hst.Commander.Gui;

/// <summary>What to search for or replace in the editor, and whether to replace every match.</summary>
public sealed record EditorSearchRequest(EditorSearchOptions Options, bool ReplaceAll);

/// <summary>
///     Modal dialog asking what to search for in the editor (F7) or what to replace (F4): the text or regular
///     expression, the replacement, case sensitive, whole words and backwards. Replacing has Replace and Replace all
///     buttons. The dialog returns the request on OK or null on cancel.
/// </summary>
public partial class EditorSearchDialog : Window
{
    public EditorSearchDialog() : this(null, false)
    {
    }

    public EditorSearchDialog(EditorSearchOptions? options, bool replace)
    {
        InitializeComponent();

        Title = replace ? "Replace" : "Search";
        ReplacementLabel.IsVisible = replace;
        ReplacementTextBox.IsVisible = replace;
        AllButton.IsVisible = replace;
        OkButton.Content = replace ? "_Replace" : "OK";
        SearchTextBox.Text = options?.Text ?? string.Empty;
        ReplacementTextBox.Text = options?.Replacement ?? string.Empty;
        RegexCheckBox.IsChecked = options?.IsRegex == true;
        CaseSensitiveCheckBox.IsChecked = options?.CaseSensitive == true;
        WholeWordsCheckBox.IsChecked = options?.WholeWords == true;
        BackwardsCheckBox.IsChecked = options?.Backwards == true;
        OkButton.Click += (_, _) => Close(CreateRequest(replace, false));
        AllButton.Click += (_, _) => Close(CreateRequest(replace, true));
        CancelButton.Click += (_, _) => Close();
        Opened += (_, _) =>
        {
            SearchTextBox.Focus();
            SearchTextBox.SelectAll();
        };
    }

    private EditorSearchRequest CreateRequest(bool replace, bool replaceAll) => new(
        new EditorSearchOptions(SearchTextBox.Text ?? string.Empty,
            replace ? ReplacementTextBox.Text ?? string.Empty : null,
            RegexCheckBox.IsChecked == true, CaseSensitiveCheckBox.IsChecked == true,
            WholeWordsCheckBox.IsChecked == true, BackwardsCheckBox.IsChecked == true),
        replaceAll);
}
