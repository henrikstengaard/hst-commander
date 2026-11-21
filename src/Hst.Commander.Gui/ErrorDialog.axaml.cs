using Avalonia.Controls;
using Hst.Commander.Core;

namespace Hst.Commander.Gui;

/// <summary>Shows an error to the user, logged to the <see cref="ErrorLog" /> with the exception causing it.</summary>
public partial class ErrorDialog : Window
{
    /// <summary>For the XAML previewer.</summary>
    public ErrorDialog()
    {
        InitializeComponent();
    }

    public ErrorDialog(string message, Exception? exception = null) : this(message, exception, true)
    {
    }

    private ErrorDialog(string message, Exception? exception, bool log) : this()
    {
        if (log)
        {
            ErrorLog.Error(message, exception);
        }

        MessageText.Text = message;
        OkButton.Click += (_, _) => Close();
        Opened += (_, _) => OkButton.Focus();
    }

    /// <summary>
    ///     Dialog of a message about what the user tried that isn't a failure, so it isn't logged: an action the
    ///     location doesn't support (e.g. making a directory in an archive) or a missing setting.
    /// </summary>
    public static ErrorDialog Notice(string message) => new(message, null, false);
}
