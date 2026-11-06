using System.Text;
using Hst.Commander.Core;

namespace Hst.Commander.Tui;

/// <summary>
///     Shows the progress of a file operation in the terminal's tab or taskbar button by writing the
///     <see cref="TerminalProgress" /> escape sequence to the standard output, in terminals known to show it. Called on
///     the UI thread (through <c>Application.Invoke</c>), where Terminal.Gui draws too, so the sequence isn't written
///     in the middle of its output. The standard output is opened for each write, as <see cref="TerminalScreen" />
///     swaps its handle on Windows.
/// </summary>
public sealed class TerminalProgressOutput : ITaskbarProgress
{
    private TerminalProgressOutput()
    {
    }

    /// <summary>Creates a reporter showing progress in the terminal, which shows nothing when the terminal can't.</summary>
    public static TaskbarProgressReporter CreateReporter() => new(
        TerminalProgress.IsSupported(Environment.GetEnvironmentVariable,
            OperatingSystem.IsWindows() && TerminalScreen.IsWindowsTerminal)
            ? new TerminalProgressOutput()
            : null);

    public void Set(TaskbarProgressState state, double fraction)
    {
        try
        {
            using var output = Console.OpenStandardOutput();
            output.Write(Encoding.ASCII.GetBytes(TerminalProgress.Sequence(state, fraction)));
            output.Flush();
        }
        catch (IOException)
        {
            // progress in the terminal is a nicety, the operation goes on without it
        }
    }
}
