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

    /// <summary>True when running as administrator (elevated) on Windows or as root on Linux and macOS.</summary>
    public static bool IsElevated { get; } = Environment.IsPrivilegedProcess;

    /// <summary>Name of the rights, "Administrator" on Windows and "root" elsewhere, e.g. shown as the indicator.</summary>
    public static string AdministratorName => OperatingSystem.IsWindows() ? "Administrator" : "root";

    /// <summary>
    ///     True when Hst Commander can start itself as administrator without a terminal: Windows asks with UAC. On Linux
    ///     and macOS only the TUI restarts as root, with sudo in its terminal.
    /// </summary>
    public static bool CanRestartWithoutTerminal => OperatingSystem.IsWindows();

    /// <summary>True when choosing a volume provider should offer to restart as administrator.</summary>
    public static bool NeedsRestart(IVolumeProvider volumeProvider) =>
        volumeProvider.RequiresAdministrator && !IsElevated;

    /// <summary>Question asked when choosing a volume provider that needs administrator rights.</summary>
    public static string GetRestartQuestion(IVolumeProvider volumeProvider) => OperatingSystem.IsWindows()
        ? $"{volumeProvider.Name} needs administrator rights.\n\nRestart {AppInfo.Current.Name} as administrator?"
        : $"{volumeProvider.Name} needs root rights.\n\nRestart {AppInfo.Current.Name} as root with sudo?";

    /// <summary>
    ///     Message shown when choosing a volume provider that needs administrator rights and Hst Commander can't restart
    ///     itself, e.g. the GUI on Linux and macOS.
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
    ///     The program to start again with its arguments: the executable, or the dotnet host with the application's
    ///     assembly when started with "dotnet app.dll".
    /// </summary>
    private static (string FileName, IReadOnlyList<string> Arguments) GetProgram(StartupOptions options)
    {
        var processPath = Environment.ProcessPath ?? throw new IOException("The program path is unknown");
        var arguments = options.ToArguments();
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
