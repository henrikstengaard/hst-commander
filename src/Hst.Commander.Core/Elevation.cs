using System.ComponentModel;
using System.Diagnostics;
using System.Reflection;
using System.Text;
using Hst.Commander.Abstractions;

namespace Hst.Commander.Core;

/// <summary>
///     Running as administrator (Windows) or root (Linux and macOS), which volume providers like the physical disks need
///     (<see cref="IVolumeProvider.RequiresAdministrator" />): whether Hst Commander does, shown by both front-ends, and
///     starting it again as administrator with the panes where they are (<see cref="StartupOptions" />).
/// </summary>
public static class Elevation
{
    /// <summary>Error code of ShellExecute when the user cancels the UAC prompt.</summary>
    private const int ErrorCancelled = 1223;

    /// <summary>Exit code of pkexec when the user dismisses the password dialog or isn't allowed.</summary>
    private const int PkexecNotAuthorized = 126;

    /// <summary>Paths pkexec is looked for at.</summary>
    private static readonly string[] PkexecPaths = ["/usr/bin/pkexec", "/bin/pkexec", "/usr/local/bin/pkexec"];

    /// <summary>
    ///     Environment variables passed to the GUI started as root with pkexec, which clears the environment, so it shows
    ///     its window on the user's display.
    /// </summary>
    private static readonly string[] DisplayVariables =
        ["DISPLAY", "XAUTHORITY", "WAYLAND_DISPLAY", "XDG_RUNTIME_DIR", "XDG_SESSION_TYPE", "LANG", "DOTNET_ROOT"];

    /// <summary>True when running as administrator (elevated) on Windows or as root on Linux and macOS.</summary>
    public static bool IsElevated { get; } = Environment.IsPrivilegedProcess;

    /// <summary>Name of the rights, "Administrator" on Windows and "root" elsewhere, e.g. shown as the indicator.</summary>
    public static string AdministratorName => OperatingSystem.IsWindows() ? "Administrator" : "root";

    /// <summary>
    ///     True when Hst Commander can start itself as administrator without a terminal: Windows asks with UAC. On Linux
    ///     and macOS only the TUI restarts as root, with sudo in its terminal.
    /// </summary>
    public static bool CanRestartWithoutTerminal => OperatingSystem.IsWindows();

    /// <summary>
    ///     True when the GUI can start Hst Commander as administrator (<see cref="StartAsAdministratorAsync" />): with UAC
    ///     on Windows and with pkexec on Linux, which asks for the password with the desktop's polkit agent.
    /// </summary>
    public static bool CanStartAsAdministrator =>
        OperatingSystem.IsWindows() || (OperatingSystem.IsLinux() && FindPkexec() != null);

    /// <summary>True when choosing a volume provider should offer to restart as administrator.</summary>
    public static bool NeedsRestart(IVolumeProvider volumeProvider) =>
        volumeProvider.RequiresAdministrator && !IsElevated;

    /// <summary>Question asked when choosing a volume provider that needs administrator rights.</summary>
    /// <param name="volumeProvider">Volume provider chosen.</param>
    /// <param name="withSudo">True when restarted as root with sudo (the TUI), false with pkexec (the GUI).</param>
    public static string GetRestartQuestion(IVolumeProvider volumeProvider, bool withSudo = true) =>
        OperatingSystem.IsWindows()
            ? $"{volumeProvider.Name} needs administrator rights.\n\nRestart {AppInfo.Current.Name} as administrator?"
            : $"{volumeProvider.Name} needs root rights.\n\nRestart {AppInfo.Current.Name} as root{(withSudo ? " with sudo" : string.Empty)}?";

    /// <summary>
    ///     Message shown when choosing a volume provider that needs administrator rights and Hst Commander can't restart
    ///     itself, e.g. the GUI on macOS and on Linux without pkexec.
    /// </summary>
    public static string GetRestartManuallyMessage(IVolumeProvider volumeProvider) =>
        $"{volumeProvider.Name} needs root rights. Start {AppInfo.Current.Name} as root, e.g. with sudo, to open them.";

    /// <summary>
    ///     Starts this program again as administrator with UAC on Windows, returning false when the user cancels. The
    ///     caller quits after it's started. Throws <see cref="IOException" /> when it can't be started.
    /// </summary>
    public static bool StartAsAdministrator(StartupOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var (fileName, arguments) = GetProgram(options);
        var startInfo = new ProcessStartInfo(fileName, JoinWindowsArguments(arguments))
        {
            UseShellExecute = true,
            Verb = "runas",
            WorkingDirectory = Environment.CurrentDirectory
        };

        try
        {
            using var process = Process.Start(startInfo);
            return true;
        }
        catch (Win32Exception exception) when (exception.NativeErrorCode == ErrorCancelled)
        {
            return false;
        }
        catch (Win32Exception exception)
        {
            throw new IOException($"{AppInfo.Current.Name} can't be started as administrator: {exception.Message}",
                exception);
        }
    }

    /// <summary>
    ///     Starts this program again as administrator: with UAC on Windows (<see cref="StartAsAdministrator" />) and with
    ///     pkexec on Linux, returning true once the password is given and the program runs as root, or false when the
    ///     password dialog is dismissed. The caller quits after it's started. Throws <see cref="IOException" /> when it
    ///     can't be started, e.g. without a polkit agent.
    /// </summary>
    public static async Task<bool> StartAsAdministratorAsync(StartupOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (!OperatingSystem.IsLinux())
        {
            return StartAsAdministrator(options);
        }

        var pkexec = FindPkexec() ?? throw new IOException("pkexec isn't installed");
        var (fileName, arguments) = GetProgram(options);
        var environment = GetDisplayVariables().Select(variable => $"{variable.Name}={variable.Value}");
        Process process;
        try
        {
            process = Process.Start(
                          new ProcessStartInfo(pkexec, ["/usr/bin/env", ..environment, fileName, ..arguments])
                          {
                              UseShellExecute = false,
                              WorkingDirectory = Environment.CurrentDirectory
                          }) ??
                      throw new Win32Exception("pkexec can't be started");
        }
        catch (Win32Exception exception)
        {
            throw new IOException($"{AppInfo.Current.Name} can't be started as root: {exception.Message}", exception);
        }

        using (process)
        {
            // pkexec runs the program in its own process once the password is given, which changes the process name
            while (!process.HasExited)
            {
                if (ReadProcessName(process.Id) is { } name && name != "pkexec")
                {
                    return true;
                }

                await Task.Delay(TimeSpan.FromMilliseconds(100));
            }

            return process.ExitCode is 0 or PkexecNotAuthorized
                ? false
                : throw new IOException($"pkexec can't start {AppInfo.Current.Name} as root (exit code " +
                                        $"{process.ExitCode}), e.g. no polkit authentication agent is running");
        }
    }

    private static string? FindPkexec() => PkexecPaths.FirstOrDefault(File.Exists);

    /// <summary>
    ///     The display variables that are set, with the user's Xauthority file when <c>XAUTHORITY</c> isn't set, as root
    ///     would look for its own.
    /// </summary>
    private static IEnumerable<(string Name, string Value)> GetDisplayVariables()
    {
        foreach (var name in DisplayVariables)
        {
            var value = Environment.GetEnvironmentVariable(name);
            if (string.IsNullOrEmpty(value) && name == "XAUTHORITY")
            {
                var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".Xauthority");
                value = File.Exists(path) ? path : null;
            }

            if (!string.IsNullOrEmpty(value))
            {
                yield return (name, value);
            }
        }
    }

    /// <summary>Name of a running process read from /proc, null when it has exited.</summary>
    private static string? ReadProcessName(int processId)
    {
        try
        {
            return File.ReadAllText($"/proc/{processId}/comm").Trim();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>
    ///     Runs this program again as root with sudo in the terminal (Linux and macOS), waiting until it exits, after
    ///     sudo has checked the password. Returns null when it ran, or an error when sudo failed, e.g. a wrong password,
    ///     so the caller continues instead of quitting.
    /// </summary>
    public static string? RunAsRootInTerminal(StartupOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var (fileName, arguments) = GetProgram(options);
        try
        {
            // the password is asked first, so a failure isn't mistaken for the exit code of the program
            if (RunSudo(["-v"]) != 0)
            {
                return "sudo failed, Hst Commander is not running as root";
            }

            RunSudo([fileName, ..arguments]);
            return null;
        }
        catch (Win32Exception exception)
        {
            return $"sudo can't be started: {exception.Message}";
        }
    }

    private static int RunSudo(IEnumerable<string> arguments)
    {
        using var process = Process.Start(new ProcessStartInfo("sudo", arguments) { UseShellExecute = false }) ??
                            throw new Win32Exception("sudo can't be started");
        process.WaitForExit();
        return process.ExitCode;
    }

    /// <summary>
    ///     The program to start again with its arguments: the AppImage when running from one (root can't read the user's
    ///     mount of the AppImage), the executable, or the dotnet host with the application's assembly when started with
    ///     "dotnet app.dll".
    /// </summary>
    private static (string FileName, IReadOnlyList<string> Arguments) GetProgram(StartupOptions options)
    {
        var processPath = Environment.ProcessPath ?? throw new IOException("The program path is unknown");
        var arguments = options.ToArguments();
        if (OperatingSystem.IsLinux() && Environment.GetEnvironmentVariable("APPIMAGE") is { Length: > 0 } appImage)
        {
            return (appImage, arguments);
        }

        return Path.GetFileNameWithoutExtension(processPath).Equals("dotnet", StringComparison.OrdinalIgnoreCase) &&
               Assembly.GetEntryAssembly()?.Location is { Length: > 0 } assemblyPath
            ? (processPath, [assemblyPath, ..arguments])
            : (processPath, arguments);
    }

    /// <summary>
    ///     Joins arguments into a Windows command line, quoted so each is read back as it is, e.g. "C:\" as
    ///     <c>"C:\\"</c>.
    /// </summary>
    public static string JoinWindowsArguments(IEnumerable<string> arguments) =>
        string.Join(' ', arguments.Select(QuoteWindowsArgument));

    private static string QuoteWindowsArgument(string argument)
    {
        if (argument.Length > 0 && !argument.Any(c => char.IsWhiteSpace(c) || c == '"'))
        {
            return argument;
        }

        // backslashes are only special before a quote, where they are doubled
        var quoted = new StringBuilder("\"");
        var backslashes = 0;
        foreach (var c in argument)
        {
            if (c == '\\')
            {
                backslashes++;
                continue;
            }

            quoted.Append('\\', c == '"' ? backslashes * 2 + 1 : backslashes);
            quoted.Append(c);
            backslashes = 0;
        }

        return quoted.Append('\\', backslashes * 2).Append('"').ToString();
    }
}
