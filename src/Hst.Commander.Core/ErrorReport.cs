using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace Hst.Commander.Core;

/// <summary>
///     Report of an unexpected error: the environment and the exception, with the user's home directory replaced by
///     <c>~</c>. It can be copied, saved as a crash marker and reported as a new GitHub issue the user reviews before
///     submitting it.
/// </summary>
/// <param name="Title">Title of the GitHub issue, e.g. <c>Crash: NullReferenceException in the GUI</c>.</param>
/// <param name="Environment">Lines describing the version, build date, OS, .NET runtime, front-end and plugins.</param>
/// <param name="Details">The exception with its stack trace and inner exceptions.</param>
public sealed record ErrorReport(string Title, IReadOnlyList<string> Environment, string Details)
{
    /// <summary>Repository the issues are created in.</summary>
    public const string IssuesUrl = "https://github.com/henrikstengaard/hst-commander/issues/new";

    /// <summary>Longest issue URL created, longer URLs fail in browsers or on GitHub.</summary>
    public const int MaxUrlLength = 8000;

    private const string TruncatedNote = "\n... (cut, please attach the log file)";

    /// <summary>Creates the report of an exception in a front-end (<c>tui</c> or <c>gui</c>).</summary>
    public static ErrorReport FromException(Exception exception, string frontEnd) =>
        FromException(exception, frontEnd,
            System.Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile));

    /// <summary>Creates the report of an exception, replacing a home directory with <c>~</c>.</summary>
    public static ErrorReport FromException(Exception exception, string frontEnd, string? homeDirectory)
    {
        ArgumentNullException.ThrowIfNull(exception);
        ArgumentNullException.ThrowIfNull(frontEnd);
        var app = AppInfo.Current;
        string[] environment =
        [
            $"Version: {app.NameAndVersion}",
            $"Build date: {(app.BuildDate is { } date ? date.ToString("u") : "unknown")}",
            $"Front-end: {FrontEndName(frontEnd)}",
            $"OS: {RuntimeInformation.OSDescription} ({RuntimeInformation.OSArchitecture})",
            $".NET: {RuntimeInformation.FrameworkDescription} ({RuntimeInformation.ProcessArchitecture})",
            $"Plugins: {PluginNames()}"
        ];
        return new ErrorReport(
            Redact($"Crash: {exception.GetType().Name} in the {FrontEndName(frontEnd)}", homeDirectory),
            environment.Select(line => Redact(line, homeDirectory)).ToArray(),
            Redact(exception.ToString(), homeDirectory));
    }

    /// <summary>The whole report as plain text, for copying and the crash marker.</summary>
    public string Text => $"{Title}\n\n{string.Join("\n", Environment)}\n\n{Details}\n";

    /// <summary>Reads a report from its <see cref="Text" />, e.g. a crash marker.</summary>
    public static ErrorReport Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var parts = text.ReplaceLineEndings("\n").Split("\n\n", 3);
        return parts.Length switch
        {
            3 => new ErrorReport(parts[0], parts[1].Split('\n'), parts[2].TrimEnd('\n')),
            2 => new ErrorReport(parts[0], [], parts[1].TrimEnd('\n')),
            _ => new ErrorReport("Crash", [], text.TrimEnd('\n'))
        };
    }

    /// <summary>
    ///     URL of a new GitHub issue with the title and the report as its body, the details cut so the URL isn't
    ///     longer than <see cref="MaxUrlLength" />.
    /// </summary>
    public string GitHubIssueUrl
    {
        get
        {
            var details = Details;
            var url = CreateUrl(details, false);
            while (url.Length > MaxUrlLength && details.Length > 0)
            {
                // cut by the characters too many, encoded characters take up to 9, so this may take a few rounds
                var excess = Math.Max(1, (url.Length - MaxUrlLength) / 3);
                details = details[..Math.Max(0, details.Length - excess)];
                url = CreateUrl(details, true);
            }

            return url;
        }
    }

    /// <summary>
    ///     Opens the new GitHub issue in the browser. Returns an error message when no browser could be started
    ///     (e.g. on a Linux system without a desktop), else null.
    /// </summary>
    public string? OpenGitHubIssue()
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo(GitHubIssueUrl) { UseShellExecute = true });
            return null;
        }
        catch (Exception exception)
        {
            return $"Failed to open the browser: {exception.Message}";
        }
    }

    private string CreateUrl(string details, bool truncated)
    {
        var body = new StringBuilder()
            .AppendLine("**What happened**")
            .AppendLine()
            .AppendLine("<!-- Describe what you did when the error occurred. -->")
            .AppendLine()
            .AppendLine("**Environment**")
            .AppendLine();
        foreach (var line in Environment)
        {
            body.Append("- ").AppendLine(line);
        }

        body.AppendLine()
            .AppendLine("**Error**")
            .AppendLine()
            .AppendLine("```")
            .Append(details).AppendLine(truncated ? TruncatedNote : string.Empty)
            .AppendLine("```");
        return $"{IssuesUrl}?title={Uri.EscapeDataString(Title)}&body={Uri.EscapeDataString(body.ToString())}";
    }

    private static string FrontEndName(string frontEnd) => frontEnd switch
    {
        "tui" => "TUI",
        "gui" => "GUI",
        _ => frontEnd
    };

    private static string PluginNames()
    {
        try
        {
            return string.Join(", ", Plugins.All.Select(plugin =>
                $"{plugin.Name} {plugin.GetType().Assembly.GetName().Version?.ToString(3)}".TrimEnd()));
        }
        catch (Exception)
        {
            return "unknown";
        }
    }

    /// <summary>Replaces a home directory with <c>~</c>, so reports don't show the user name.</summary>
    public static string Redact(string text, string? homeDirectory)
    {
        if (string.IsNullOrEmpty(homeDirectory) || homeDirectory.Length < 2)
        {
            return text;
        }

        var trimmed = homeDirectory.TrimEnd('/', '\\');
        var result = text.Replace(trimmed, "~",
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
        // stack traces of builds on Windows have the other kind of separator
        var otherSeparators = trimmed.Contains('\\') ? trimmed.Replace('\\', '/') : trimmed.Replace('/', '\\');
        return result.Replace(otherSeparators, "~", StringComparison.OrdinalIgnoreCase);
    }
}
