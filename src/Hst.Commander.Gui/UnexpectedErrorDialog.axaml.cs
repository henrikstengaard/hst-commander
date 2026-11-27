using Avalonia.Controls;
using Avalonia.Input.Platform;
using Hst.Commander.Core;

namespace Hst.Commander.Gui;

/// <summary>
///     Shows an unhandled exception, or the crash of the last run, with its report, the log file and buttons to report
///     it as a new GitHub issue or copy it.
/// </summary>
public partial class UnexpectedErrorDialog : Window
{
    /// <summary>For the XAML previewer.</summary>
    public UnexpectedErrorDialog()
    {
        InitializeComponent();
    }

    /// <param name="heading">E.g. <c>An unexpected error occurred</c>.</param>
    /// <param name="message">The exception's message.</param>
    /// <param name="report">Report of the error.</param>
    /// <param name="lastRun">True for the crash of the last run, which may be in an older log file.</param>
    public UnexpectedErrorDialog(string heading, string message, ErrorReport report, bool lastRun = false) : this()
    {
        HeadingText.Text = heading;
        MessageText.Text = message;
        MessageText.IsVisible = message.Length > 0;
        DetailsTextBox.Text = report.Text;
        LogText.Text = FormatLogText(lastRun);
        LogText.IsVisible = LogText.Text.Length > 0;
        ReportButton.Click += async (_, _) =>
        {
            if (report.OpenGitHubIssue() is { } error)
            {
                await new ErrorDialog(error).ShowDialog(this);
            }
        };
        CopyButton.Click += async (_, _) =>
        {
            if (Clipboard is { } clipboard)
            {
                await clipboard.SetTextAsync(report.Text);
            }
        };
        CloseButton.Click += (_, _) => Close();
        Opened += (_, _) => DialogButtons.Focus(ReportButton);
    }

    /// <summary>Dialog of an unhandled exception.</summary>
    public static UnexpectedErrorDialog ForException(Exception exception) =>
        new("An unexpected error occurred", exception.Message, ErrorReport.FromException(exception, "gui"));

    /// <summary>Dialog of the crash of the last run, from its crash marker.</summary>
    public static UnexpectedErrorDialog ForLastCrash(string crashReport) =>
        new($"{AppInfo.Current.Name} closed unexpectedly the last time it ran", string.Empty,
            ErrorReport.Parse(crashReport), true);

    private static string FormatLogText(bool lastRun) => lastRun
        ? ErrorLog.LogDirectory is { } directory ? $"Errors are logged in {directory}" : string.Empty
        : ErrorLog.CurrentFile is { } logFile ? $"The error was logged to {logFile}" : string.Empty;
}
