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

    /// <summary>
    ///     osascript of macOS, running a shell command as root with <c>do shell script</c>, where macOS asks for an
    ///     administrator's password in its own dialog.
    /// </summary>
    private const string OsascriptPath = "/usr/bin/osascript";

    /// <summary>AppleScript error number when the user cancels the password dialog.</summary>
    private const string UserCanceledError = "(-128)";

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
    ///     on Windows, with pkexec on Linux, which asks for the password with the desktop's polkit agent, and with
    ///     osascript on macOS, which asks for it in macOS's own dialog.
    /// </summary>
    public static bool CanStartAsAdministrator =>
        OperatingSystem.IsWindows() || (OperatingSystem.IsLinux() && FindPkexec() != null) ||
        (OperatingSystem.IsMacOS() && File.Exists(OsascriptPath));

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
    ///     itself, e.g. the GUI on Linux without pkexec.
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
    ///     pkexec on Linux or osascript on macOS, returning true once the password is given and the program runs as root,
    ///     or false when the password dialog is dismissed. The caller quits after it's started. Throws
    ///     <see cref="IOException" /> when it can't be started, e.g. without a polkit agent.
    /// </summary>
    public static async Task<bool> StartAsAdministratorAsync(StartupOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (OperatingSystem.IsMacOS())
        {
            return await StartAsRootWithOsascriptAsync(options);
        }

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
    ///     Starts this program as root on macOS with osascript's <c>do shell script ... with administrator
    ///     privileges</c>, where macOS asks for an administrator's password. The program runs in the background, so
    ///     osascript exits once it's started. Returns false when the password dialog is cancelled.
    /// </summary>
    private static async Task<bool> StartAsRootWithOsascriptAsync(StartupOptions options)
    {
        var (fileName, arguments) = GetProgram(options);
        var startInfo = new ProcessStartInfo(OsascriptPath, ["-e", GetAdministratorScript(fileName, arguments)])
        {
            UseShellExecute = false,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            WorkingDirectory = Environment.CurrentDirectory
        };

        Process process;
        try
        {
            process = Process.Start(startInfo) ?? throw new Win32Exception("osascript can't be started");
        }
        catch (Win32Exception exception)
        {
            throw new IOException($"{AppInfo.Current.Name} can't be started as root: {exception.Message}", exception);
        }

        using (process)
        {
            var error = process.StandardError.ReadToEndAsync();
            await process.StandardOutput.ReadToEndAsync();
            await process.WaitForExitAsync();
            var message = (await error).Trim();
            if (process.ExitCode == 0)
            {
                return true;
            }

            return message.Contains(UserCanceledError, StringComparison.Ordinal)
                ? false
                : throw new IOException($"{AppInfo.Current.Name} can't be started as root: {message}");
        }
    }

    /// <summary>
    ///     AppleScript running a program with its arguments as root in the background with <c>do shell script</c>, its
    ///     output discarded so osascript doesn't wait for it, e.g.
    ///     <c>do shell script "'/Applications/Hst Commander.app/Contents/MacOS/hst.commander.gui' --active right &gt;
    ///     /dev/null 2&gt;&amp;1 &amp;" with prompt "..." with administrator privileges</c>.
    /// </summary>
    public static string GetAdministratorScript(string fileName, IEnumerable<string> arguments)
    {
        var command = string.Join(' ', new[] { fileName }.Concat(arguments).Select(QuoteShellArgument)) +
                      " > /dev/null 2>&1 &";
        var prompt = $"{AppInfo.Current.Name} restarts as root.";
        return $"do shell script {QuoteAppleScriptString(command)} with prompt {QuoteAppleScriptString(prompt)} " +
               "with administrator privileges";
    }

    /// <summary>Quotes an argument for a POSIX shell in single quotes, e.g. <c>it's</c> as <c>'it'\''s'</c>.</summary>
    private static string QuoteShellArgument(string argument) => $"'{argument.Replace("'", @"'\''")}'";

    /// <summary>Quotes a text as an AppleScript string, escaping backslashes and double quotes.</summary>
    private static string QuoteAppleScriptString(string text) =>
        $"\"{text.Replace(@"\", @"\\").Replace("\"", "\\\"")}\"";

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
