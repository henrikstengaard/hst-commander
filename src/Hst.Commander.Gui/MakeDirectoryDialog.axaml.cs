using Avalonia.Controls;

namespace Hst.Commander.Gui;

/// <summary>
///     Modal dialog asking for a name: of the directory to create (F7), or the new name of an entry to rename
///     (Shift+F6) with its name filled in and selected without the extension. The dialog returns the name on OK or
///     null on cancel.
/// </summary>
public partial class MakeDirectoryDialog : Window
{
    public MakeDirectoryDialog() : this(null, null, null)
    {
    }

    public MakeDirectoryDialog(string? title, string? prompt, string? name)
    {
        InitializeComponent();

        if (title != null)
        {
            Title = title;
        }

        if (prompt != null)
        {
            PromptText.Text = prompt;
        }

        NameTextBox.Text = name ?? string.Empty;
        OkButton.Click += (_, _) => Close(NameTextBox.Text ?? string.Empty);
        CancelButton.Click += (_, _) => Close();
        Opened += (_, _) =>
        {
            NameTextBox.Focus();
            if (name is { Length: > 0 })
            {
                // the name without its extension, like Explorer
                var extension = System.IO.Path.GetExtension(name);
                NameTextBox.SelectionStart = 0;
                NameTextBox.SelectionEnd = extension.Length > 0 && extension.Length < name.Length
                    ? name.Length - extension.Length
                    : name.Length;
            }
        };
    }
}
