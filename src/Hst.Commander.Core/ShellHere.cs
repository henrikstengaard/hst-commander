using System.Diagnostics;

namespace Hst.Commander.Core;

/// <summary>A shell or terminal opened in a pane's directory from the GUI's context menu, see <see cref="ShellHere" />.</summary>
public enum ShellHereKind
{
    /// <summary>PowerShell (pwsh when installed, otherwise Windows PowerShell), Windows only.</summary>
    PowerShell,

    /// <summary>Windows Terminal on Windows, Terminal on macOS and the user's terminal emulator on Linux.</summary>
    Terminal,

    /// <summary>The command prompt (cmd.exe), Windows only.</summary>
    CommandPrompt
}

/// <summary>
///     Opens a shell or terminal in a directory (PowerShell here, Terminal here and Command prompt here in the GUI's
///     context menu of the entries), without waiting for it.
/// </summary>
public static class ShellHere
{
    /// <summary>Terminal emulators tried in order on Linux after the TERMINAL environment variable.</summary>
    public static readonly IReadOnlyList<string> LinuxTerminals =
    [
        "x-terminal-emulator", "gnome-terminal", "konsole", "xfce4-terminal", "mate-terminal", "tilix", "kitty",
        "alacritty", "xterm"
    ];

    /// <summary>The kinds available on the host: all on Windows, only the terminal on macOS and Linux.</summary>
    public static IReadOnlyList<ShellHereKind> Available => GetAvailable(OperatingSystem.IsWindows());

    /// <summary>The kinds available on Windows (<paramref name="windows" />) or macOS and Linux.</summary>
    public static IReadOnlyList<ShellHereKind> GetAvailable(bool windows) => windows
        ? [ShellHereKind.PowerShell, ShellHereKind.Terminal, ShellHereKind.CommandPrompt]
        : [ShellHereKind.Terminal];

    /// <summary>The menu header of a kind, with its access key.</summary>
    public static string GetHeader(ShellHereKind kind) => kind switch
    {
        ShellHereKind.PowerShell => "Power_Shell here",
        ShellHereKind.Terminal => "Termi_nal here",
        ShellHereKind.CommandPrompt => "Co_mmand prompt here",
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };

    /// <summary>Starts a shell or terminal in a directory on the host.</summary>
    /// <exception cref="InvalidOperationException">The program isn't installed.</exception>
    public static void Start(ShellHereKind kind, string directory) =>
        Process.Start(CreateStartInfo(kind, directory))?.Dispose();

    /// <summary>Start info opening a shell or terminal in a directory on the host.</summary>
    /// <exception cref="InvalidOperationException">The program isn't installed.</exception>
    public static ProcessStartInfo CreateStartInfo(ShellHereKind kind, string directory) =>
        CreateStartInfo(kind, directory, OperatingSystem.IsWindows(), OperatingSystem.IsMacOS(), FindProgram,
            Environment.GetEnvironmentVariable(OperatingSystem.IsWindows() ? "ComSpec" : "TERMINAL"));

    /// <summary>
    ///     Start info opening a shell or terminal in a directory on Windows (<paramref name="windows" />), macOS
    ///     (<paramref name="macOs" />) or Linux:
    ///     <list type="bullet">
    ///         <item>PowerShell: pwsh.exe when found, otherwise powershell.exe.</item>
    ///         <item>Command prompt: <c>ComSpec</c> (<paramref name="variable" />) or cmd.exe.</item>
    ///         <item>Terminal on Windows: Windows Terminal, <c>wt.exe -d directory</c>.</item>
    ///         <item>Terminal on macOS: <c>open -a Terminal directory</c>.</item>
    ///         <item>
    ///             Terminal on Linux: <c>TERMINAL</c> (<paramref name="variable" />) or the first of
    ///             <see cref="LinuxTerminals" /> found, started in the directory.
    ///         </item>
    ///     </list>
    ///     On Windows the program is started with the shell, so it gets its own console window even when Hst Commander
    ///     was started from a console.
    /// </summary>
    /// <param name="findProgram">Finds the full path of a program by name (with .exe on Windows), null when not found.</param>
    /// <exception cref="InvalidOperationException">The program isn't installed or the kind isn't available.</exception>
    public static ProcessStartInfo CreateStartInfo(ShellHereKind kind, string directory, bool windows, bool macOs,
        Func<string, string?> findProgram, string? variable)
    {
        ArgumentNullException.ThrowIfNull(directory);
        ArgumentNullException.ThrowIfNull(findProgram);

        if (!GetAvailable(windows).Contains(kind))
        {
            throw new InvalidOperationException($"{kind} isn't available on this operating system");
        }

        ProcessStartInfo startInfo;
        if (windows)
        {
            startInfo = kind switch
            {
                ShellHereKind.PowerShell => new ProcessStartInfo(findProgram("pwsh.exe") ?? "powershell.exe"),
                ShellHereKind.CommandPrompt => new ProcessStartInfo(string.IsNullOrWhiteSpace(variable)
                    ? "cmd.exe"
                    : variable),
                _ => new ProcessStartInfo(findProgram("wt.exe") ??
                                          throw new InvalidOperationException("Windows Terminal isn't installed"))
            };
            if (kind == ShellHereKind.Terminal)
            {
                // wt.exe takes ";" as a separator of its commands, "\;" is a literal ";"
                startInfo.ArgumentList.Add("-d");
                startInfo.ArgumentList.Add(directory.Replace(";", "\\;"));
            }

            startInfo.UseShellExecute = true;
        }
        else if (macOs)
        {
            startInfo = new ProcessStartInfo("open");
            startInfo.ArgumentList.Add("-a");
            startInfo.ArgumentList.Add("Terminal");
            startInfo.ArgumentList.Add(directory);
            startInfo.UseShellExecute = false;
        }
        else
        {
            var terminal = !string.IsNullOrWhiteSpace(variable)
                ? variable
                : LinuxTerminals.Select(findProgram).FirstOrDefault(path => path != null) ??
                  throw new InvalidOperationException(
                      "No terminal found, set the TERMINAL environment variable to the terminal to use");
            startInfo = new ProcessStartInfo(terminal) { UseShellExecute = false };
        }

        startInfo.WorkingDirectory = directory;
        return startInfo;
    }

    /// <summary>Full path of a program found in the directories of the PATH environment variable.</summary>
    private static string? FindProgram(string fileName)
    {
        var directories = (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var directory in directories)
        {
            try
            {
                var path = Path.Combine(directory, fileName);
                if (File.Exists(path))
                {
                    return path;
                }
            }
            catch (ArgumentException)
            {
            }
        }

        return null;
    }
}
