using System.Text;
using Hst.Commander.Core;

namespace Hst.Commander.Tui;

/// <summary>
///     Shows an unhandled exception, or the crash of the last run, with its report and the log file, and offers to
///     report it as a new GitHub issue opened in the browser.
/// </summary>
public sealed class UnexpectedErrorDialog : TuiDialog
{
    /// <param name="heading">E.g. <c>An unexpected error occurred</c>.</param>
    /// <param name="message">The exception's message, or empty.</param>
    /// <param name="report">Report of the error.</param>
    /// <param name="palette">Colors of the theme.</param>
    /// <param name="lastRun">True for the crash of the last run, which may be in an older log file.</param>
    public UnexpectedErrorDialog(string heading, string message, ErrorReport report, TuiPalette palette,
        bool lastRun = false)
    {
        Title = "Unexpected error";
        Width = Dim.Percent(90);
        Height = Dim.Percent(90);
        SetScheme(palette.DialogColorScheme);

        var y = 0;
        Add(CreateLabel(y++, heading));
        if (message.Length > 0)
        {
            Add(CreateLabel(y++, message.ReplaceLineEndings(" ")));
        }

        Add(CreateLabel(y++, "Report it as a new GitHub issue, opened in the browser to review before submitting it."));
        var logFile = lastRun ? ErrorLog.LogDirectory : ErrorLog.CurrentFile;
        Add(CreateLabel(y++,
            logFile == null ? string.Empty : lastRun ? $"Errors are logged in {logFile}" : $"Logged to {logFile}"));

        var textView = new TextView
        {
            X = 0,
            Y = y + 1,
            Width = Dim.Fill(),
            Height = Dim.Fill(1),
            ReadOnly = true,
            WordWrap = false,
            Text = report.Text.ReplaceLineEndings("\n")
        };
        Add(textView);

        var reportButton = new Button { Text = "Report on GitHub", IsDefault = true };
        reportButton.Accepting += (_, e) =>
        {
            e.Handled = true;
            if (report.OpenGitHubIssue() is { } error)
            {
                // e.g. no browser on a Linux system without a desktop: the log file has the same report
                TuiMessageBox.ErrorQuery("Error",
                    logFile != null ? $"{error}\n\nThe report is in the log:\n{logFile}" : error, "OK");
            }
        };
        var closeButton = new Button { Text = "Close" };
        closeButton.Accepting += (_, e) =>
        {
            e.Handled = true;
            Application.RequestStop(this);
        };

        AddButton(reportButton);
        AddButton(closeButton);
        Initialized += (_, _) => reportButton.SetFocus();
    }

    /// <summary>Label showing a text as it is: an underscore isn't a hotkey (e.g. in an environment variable).</summary>
    private static Label CreateLabel(int y, string text) =>
        new() { X = 0, Y = y, Width = Dim.Fill(), HotKeySpecifier = (Rune)0xffff, Text = text };

    /// <summary>Dialog of an unhandled exception.</summary>
    public static UnexpectedErrorDialog ForException(Exception exception, TuiPalette palette) =>
        new("An unexpected error occurred", exception.Message, ErrorReport.FromException(exception, "tui"), palette);

    /// <summary>Dialog of the crash of the last run, from its crash marker.</summary>
    public static UnexpectedErrorDialog ForLastCrash(string crashReport, TuiPalette palette) =>
        new($"{AppInfo.Current.Name} closed unexpectedly the last time it ran", string.Empty,
            ErrorReport.Parse(crashReport), palette, true);
}
