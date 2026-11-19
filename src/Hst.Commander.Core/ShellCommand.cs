using System.Diagnostics;

namespace Hst.Commander.Core;

/// <summary>Runs a command typed in the command line with the shell of the host: cmd.exe on Windows, the user's shell elsewhere.</summary>
public static class ShellCommand
{
    /// <summary>
    ///     Start info running the command with the shell in a directory: <c>cmd.exe /d /s /c "command"</c> on Windows
    ///     (<c>/s</c> keeps the quotes inside the command), <c>$SHELL -c command</c> elsewhere (<c>/bin/sh</c> without
    ///     <c>SHELL</c>).
    /// </summary>
    /// <param name="command">The command as typed.</param>
    /// <param name="workingDirectory">The directory to run it in, the active pane's directory.</param>
    public static ProcessStartInfo CreateStartInfo(string command, string workingDirectory) =>
        CreateStartInfo(command, workingDirectory, OperatingSystem.IsWindows(),
            Environment.GetEnvironmentVariable(OperatingSystem.IsWindows() ? "ComSpec" : "SHELL"));

    /// <summary>Start info for cmd.exe (<paramref name="windows" />) or a Unix shell, see the overload without them.</summary>
    public static ProcessStartInfo CreateStartInfo(string command, string workingDirectory, bool windows,
        string? shell)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(workingDirectory);

        ProcessStartInfo startInfo;
        if (windows)
        {
            // cmd.exe parses its own command line, so the arguments are given as a string, not escaped
            startInfo = new ProcessStartInfo(string.IsNullOrWhiteSpace(shell) ? "cmd.exe" : shell)
            {
                Arguments = $"/d /s /c \"{command}\""
            };
        }
        else
        {
            startInfo = new ProcessStartInfo(string.IsNullOrWhiteSpace(shell) ? "/bin/sh" : shell);
            startInfo.ArgumentList.Add("-c");
            startInfo.ArgumentList.Add(command);
        }

        startInfo.UseShellExecute = false;
        startInfo.WorkingDirectory = workingDirectory;
        return startInfo;
    }
}
