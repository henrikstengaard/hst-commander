using System.ComponentModel;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using Hst.Commander.Core;

namespace Hst.Commander.Tui;

/// <summary>What to do in the terminal while the TUI is shut down.</summary>
public abstract record TerminalAction
{
    /// <summary>Runs a program opened in a pane.</summary>
    public sealed record RunProgram(string Path) : TerminalAction;

    /// <summary>Shows the output screen (Ctrl+O) until a key is pressed.</summary>
    public sealed record ShowOutput : TerminalAction;

    /// <summary>Runs a command typed in the command line with the shell in a directory.</summary>
    public sealed record RunCommand(string Command, string WorkingDirectory) : TerminalAction;

    /// <summary>Runs an external editor (F4) with a file, e.g. nano, until it exits.</summary>
    public sealed record RunEditor(ExternalEditorCommand Command, string Path) : TerminalAction;

    /// <summary>Runs Hst Commander again as root with sudo in the terminal (Linux and macOS), then quits.</summary>
    public sealed record RestartAsRoot(StartupOptions Options) : TerminalAction;
}

/// <summary>
///     The terminal's output screen, used while the TUI is shut down like Midnight Commander does: programs run on it
///     with the terminal and keyboard to themselves, and Ctrl+O shows it with the output of the programs run.
/// </summary>
public static class TerminalScreen
{
    private const int StdInputHandle = -10;
    private const int StdOutputHandle = -11;
    private const uint GenericRead = 0x80000000;
    private const uint GenericWrite = 0x40000000;
    private const uint FileShareRead = 1;
    private const uint FileShareWrite = 2;
    private const uint ConsoleTextModeBuffer = 1;

    private static IntPtr _outputHandle;
    private static IntPtr _tuiHandle;

    /// <summary>True when the TUI was shut down without switching the active screen buffer, see <see cref="BeforeShutdown" />.</summary>
    private static bool _screenBufferKept;

    /// <summary>
    ///     Whether the terminal says it shows 24-bit colors, with <c>COLORTERM</c> set to <c>truecolor</c> or
    ///     <c>24bit</c> (e.g. iTerm2, GNOME Terminal and Konsole; not Terminal.app before macOS 26).
    /// </summary>
    public static bool SupportsTrueColor =>
        Environment.GetEnvironmentVariable("COLORTERM") is { } colorTerm &&
        (colorTerm.Equals("truecolor", StringComparison.OrdinalIgnoreCase) ||
         colorTerm.Equals("24bit", StringComparison.OrdinalIgnoreCase));

    /// <summary>
    ///     Called before the TUI is started, so it doesn't draw over the output screen. Windows Terminal and Unix
    ///     terminals draw the TUI on the alternate screen, but the classic Windows console draws it on the standard
    ///     output's screen buffer, so there the TUI gets a screen buffer of its own.
    /// </summary>
    public static void EnterTui()
    {
        if (!OperatingSystem.IsWindows() || IsWindowsTerminal)
        {
            return;
        }

        _outputHandle = GetStdHandle(StdOutputHandle);
        if (_tuiHandle == IntPtr.Zero)
        {
            _tuiHandle = CreateConsoleScreenBuffer(GenericRead | GenericWrite, FileShareRead | FileShareWrite,
                IntPtr.Zero, ConsoleTextModeBuffer, IntPtr.Zero);
        }

        if (!IsValid(_outputHandle) || !IsValid(_tuiHandle))
        {
            return;
        }

        SetStdHandle(StdOutputHandle, _tuiHandle);
        SetConsoleActiveScreenBuffer(_tuiHandle);
    }

    /// <summary>
    ///     Called before <c>Application.Shutdown</c>. In Windows Terminal, Terminal.Gui's Windows driver switches to a
    ///     new, empty screen buffer when shut down (<c>WindowsConsole.Cleanup</c>), and each switch of the active screen
    ///     buffer makes the pseudo console repaint the screen, which Windows Terminal scrolls into its scrollback: a
    ///     screen of empty lines above the output screen each time the TUI was shut down, e.g. for Ctrl+O. So the
    ///     driver's console is cleaned up here without switching (restoring the console mode and cursor and stopping
    ///     reading input), and removed from the driver, which then only leaves the alternate screen. When Terminal.Gui's
    ///     internals differ, nothing is done and the driver cleans up itself.
    /// </summary>
    public static void BeforeShutdown()
    {
        if (!OperatingSystem.IsWindows() || !IsWindowsTerminal)
        {
            return;
        }

        try
        {
            var driver = Terminal.Gui.Application.Driver;
            var winConsoleProperty = driver?.GetType().GetProperty("WinConsole",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            var winConsole = winConsoleProperty?.GetValue(driver);
            var setWinConsole = winConsoleProperty?.GetSetMethod(true);
            if (winConsole == null || setWinConsole == null)
            {
                return;
            }

            var consoleType = winConsole.GetType();
            const BindingFlags fieldFlags = BindingFlags.Instance | BindingFlags.NonPublic;
            var originalModeField = consoleType.GetField("_originalConsoleMode", fieldFlags);
            var cursorField = consoleType.GetField("_initialCursorVisibility", fieldFlags);
            var cancellationField = consoleType.GetField("_inputReadyCancellationTokenSource", fieldFlags);
            var setCursorVisibility = consoleType.GetMethod("SetCursorVisibility");
            if (originalModeField?.GetValue(winConsole) is not uint originalMode || cursorField == null ||
                cancellationField == null || setCursorVisibility == null)
            {
                return;
            }

            if (cursorField.GetValue(winConsole) is { } cursorVisibility)
            {
                setCursorVisibility.Invoke(winConsole, [cursorVisibility]);
            }

            SetConsoleMode(GetStdHandle(StdInputHandle), originalMode);
            if (cancellationField.GetValue(winConsole) is CancellationTokenSource cancellation)
            {
                cancellation.Cancel();
                cancellation.Dispose();
                cancellationField.SetValue(winConsole, null);
            }

            setWinConsole.Invoke(driver, [null]);
            _screenBufferKept = true;
        }
        catch (Exception exception) when (exception is TargetInvocationException or ArgumentException
                                              or MemberAccessException or InvalidOperationException)
        {
            // the driver cleans up itself
        }
    }

    /// <summary>
    ///     Called after the TUI is shut down to show the output screen again. Terminal.Gui's Windows driver activates a
    ///     new, empty screen buffer when shut down, so the standard output's screen buffer is activated again, unless
    ///     it's still active (see <see cref="BeforeShutdown" />), as activating it repaints the screen.
    /// </summary>
    public static void LeaveTui()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        if (_screenBufferKept)
        {
            _screenBufferKept = false;
            return;
        }

        if (IsValid(_outputHandle))
        {
            SetStdHandle(StdOutputHandle, _outputHandle);
        }

        var handle = GetStdHandle(StdOutputHandle);
        if (IsValid(handle))
        {
            SetConsoleActiveScreenBuffer(handle);
        }
    }

    /// <summary>
    ///     Runs the program in its directory and waits for it to exit, then returns directly so the TUI is shown again,
    ///     like Midnight Commander. Ctrl+C stops the program, not Hst Commander.
    /// </summary>
    /// <returns>The error message when the program couldn't be started, otherwise null.</returns>
    public static string? Run(string path) => Run(new ProcessStartInfo(path)
    {
        UseShellExecute = false,
        WorkingDirectory = Path.GetDirectoryName(path) ?? string.Empty
    }, path);

    /// <summary>
    ///     Runs a command typed in the command line with the shell (<see cref="ShellCommand" />) in a directory and
    ///     waits for it to exit, like <see cref="Run(string)" />.
    /// </summary>
    /// <returns>The error message when the shell couldn't be started, otherwise null.</returns>
    public static string? RunCommand(string command, string workingDirectory) =>
        Run(ShellCommand.CreateStartInfo(command, workingDirectory), $"{workingDirectory}> {command}");

    /// <summary>
    ///     Runs an external editor with a file and waits for it to exit, like <see cref="Run(string)" />. An editor
    ///     exiting at once has likely handed the file to an instance already running (see
    ///     <see cref="ExternalEditor.HandOffTime" />), so then it waits for a key, as the file may still be edited.
    /// </summary>
    /// <returns>The error message when the editor couldn't be started, otherwise null.</returns>
    public static string? RunEditor(ExternalEditorCommand command, string path)
    {
        var started = DateTime.Now;
        if (Run(command.CreateStartInfo(path), $"{command.Name} {path}") is { } error)
        {
            return $"The editor '{command.FileName}' couldn't be started: {error}";
        }

        if (DateTime.Now - started < ExternalEditor.HandOffTime)
        {
            Console.WriteLine($"Press a key when done editing in {command.Name}...");
            Console.ReadKey(true);
        }

        return null;
    }

    private static string? Run(ProcessStartInfo startInfo, string description)
    {
        Console.ResetColor();
        Console.WriteLine();
        Console.WriteLine(description);

        // Ctrl+C is also sent to this process, which must keep running
        ConsoleCancelEventHandler ignoreCancel = (_, e) => e.Cancel = true;
        Console.CancelKeyPress += ignoreCancel;
        try
        {
            using var process = Process.Start(startInfo);
            process?.WaitForExit();
            return null;
        }
        catch (Exception exception) when (exception is Win32Exception or IOException or UnauthorizedAccessException)
        {
            return exception.Message;
        }
        finally
        {
            Console.CancelKeyPress -= ignoreCancel;
        }
    }

    /// <summary>Shows the output screen until a key is pressed (Ctrl+O), like Midnight Commander.</summary>
    public static void ShowOutput() => Console.ReadKey(true);

    /// <summary>
    ///     True in Windows Terminal, Visual Studio's and VS Code's terminals, which show the console through a pseudo
    ///     console and not its screen buffers. The environment variables aren't enough: a program started as
    ///     administrator doesn't get them, but is still shown in Windows Terminal when it's the default terminal.
    /// </summary>
    internal static bool IsWindowsTerminal => Environment.GetEnvironmentVariable("WT_SESSION") != null ||
                                            Environment.GetEnvironmentVariable("VSAPPIDNAME") != null ||
                                            IsPseudoConsole;

    private static bool IsPseudoConsole
    {
        get
        {
            var window = GetConsoleWindow();
            if (window == IntPtr.Zero)
            {
                return false;
            }

            var className = new StringBuilder(256);
            return GetClassName(window, className, className.Capacity) > 0 &&
                   className.ToString() == "PseudoConsoleWindow";
        }
    }

    private static bool IsValid(IntPtr handle) => handle != IntPtr.Zero && handle != new IntPtr(-1);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GetStdHandle(int stdHandle);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetConsoleWindow();

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetConsoleMode(IntPtr consoleHandle, uint mode);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr window, StringBuilder className, int maxCount);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetStdHandle(int stdHandle, IntPtr handle);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr CreateConsoleScreenBuffer(uint desiredAccess, uint shareMode,
        IntPtr securityAttributes, uint flags, IntPtr screenBufferData);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetConsoleActiveScreenBuffer(IntPtr consoleOutput);
}
