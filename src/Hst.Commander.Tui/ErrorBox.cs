using Hst.Commander.Core;

namespace Hst.Commander.Tui;

/// <summary>Shows an error to the user, logged to the <see cref="ErrorLog" /> with the exception causing it.</summary>
internal static class ErrorBox
{
    public static void Show(string message, Exception? exception = null)
    {
        ErrorLog.Error(message, exception);
        TuiMessageBox.ErrorQuery("Error", message, "OK");
    }

    public static void Show(Exception exception) => Show(exception.Message, exception);

    /// <summary>
    ///     Shows a message about what the user tried that isn't a failure, so it isn't logged: invalid input in a
    ///     dialog (e.g. a line number) or an action the location doesn't support (e.g. making a directory in an archive).
    /// </summary>
    public static void ShowNotice(string message) => TuiMessageBox.ErrorQuery("Error", message, "OK");
}
