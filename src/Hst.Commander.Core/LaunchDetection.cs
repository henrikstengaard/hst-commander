using System.Runtime.InteropServices;

namespace Hst.Commander.Core;

/// <summary>
///     Tells whether Hst Commander was started from a terminal or command prompt, used with the shortcuts' marker (see
///     <see cref="StartupOptions.IsLaunchedFromShortcut" />) for starts that pass no arguments, e.g. an AppImage or an
///     executable double-clicked in a file manager:
///     <list type="bullet">
///         <item>
///             the GUI on Windows: the parent process has a console (attaching to it succeeds, and it's detached at
///             once), which Explorer and the Start menu don't;
///         </item>
///         <item>
///             the TUI on Windows: another process uses its console (e.g. cmd.exe or PowerShell), while one started
///             from the Start menu gets a console of its own;
///         </item>
///         <item>the GUI on Linux and macOS: standard input is a terminal, desktops start apps without one;</item>
///         <item>the TUI on Linux and macOS: always, as a .desktop file's terminal looks the same as any other.</item>
///     </list>
/// </summary>
public static class LaunchDetection
{
    private const uint AttachParentProcess = unchecked((uint)-1);

    /// <summary>True when started from a terminal or command prompt, false when started from a shortcut or file manager.</summary>
    /// <param name="isGui">True for the GUI, false for the TUI.</param>
    public static bool IsStartedFromTerminal(bool isGui)
    {
        try
        {
            if (OperatingSystem.IsWindows())
            {
                if (!isGui)
                {
                    var processes = new uint[2];
                    return GetConsoleProcessList(processes, (uint)processes.Length) > 1;
                }

                if (!AttachConsole(AttachParentProcess))
                {
                    return false;
                }

                FreeConsole();
                return true;
            }

            return !isGui || !Console.IsInputRedirected;
        }
        catch (Exception exception) when (exception is DllNotFoundException or EntryPointNotFoundException or IOException)
        {
            // like a terminal, the panes start in the current directory
            return true;
        }
    }

    /// <summary>
    ///     Startup options of the command line (see <see cref="StartupOptions.Parse" />), launched from a shortcut also
    ///     when not started from a terminal (see <see cref="IsStartedFromTerminal" />).
    /// </summary>
    /// <param name="args">Command line arguments.</param>
    /// <param name="isGui">True for the GUI, false for the TUI.</param>
    public static StartupOptions ParseStartupOptions(IReadOnlyList<string> args, bool isGui)
    {
        var options = StartupOptions.Parse(args);
        return options.IsLaunchedFromShortcut || IsStartedFromTerminal(isGui)
            ? options
            : options with { IsLaunchedFromShortcut = true };
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AttachConsole(uint processId);

    [DllImport("kernel32.dll")]
    private static extern bool FreeConsole();

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint GetConsoleProcessList(uint[] processIds, uint count);
}
