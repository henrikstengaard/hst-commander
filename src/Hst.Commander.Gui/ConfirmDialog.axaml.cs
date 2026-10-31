using Avalonia.Controls;

namespace Hst.Commander.Gui;

/// <summary>
///     Modal dialog asking a question, e.g. whether to restart as administrator. The dialog returns true on the yes
///     button, false on the no button or null when closed, which is treated as no. Without a no button it only shows a
///     message, without the question icon.
/// </summary>
public partial class ConfirmDialog : Window
{
    public ConfirmDialog() : this(string.Empty, string.Empty, "_OK", null)
    {
    }

    /// <param name="title">Title of the dialog.</param>
    /// <param name="message">Question or message shown.</param>
    /// <param name="yesText">Text of the yes button, e.g. "_Restart".</param>
    /// <param name="noText">Text of the no button, e.g. "_Continue"; null for none.</param>
    public ConfirmDialog(string title, string message, string yesText, string? noText)
    {
        InitializeComponent();
        Title = title;
        MessageText.Text = message;
        YesButton.Content = yesText;
        NoButton.Content = noText;
        NoButton.IsVisible = noText != null;
        QuestionIcon.IsVisible = noText != null;
        YesButton.Click += (_, _) => Close(true);
        NoButton.Click += (_, _) => Close(false);
        Opened += (_, _) => YesButton.Focus();
    }
}
