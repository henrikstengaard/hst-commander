using System.Runtime.InteropServices;

namespace Hst.Commander.Tui;

/// <summary>
///     Waits for a key in the terminal on Linux and macOS while the TUI is shut down (Ctrl+O, external editors), without
///     .NET's <see cref="Console.ReadKey(bool)" />: once that has configured the terminal for reading keys, .NET gives
///     each program started afterwards the terminal settings it saved while Terminal.Gui had the terminal in raw mode,
///     so commands run after Ctrl+O printed lines without returning to the first column (no output processing) and
///     ignored Ctrl+C. Here line input and echo are turned off only while waiting, and the settings are restored.
/// </summary>
internal static class UnixTerminalKey
{
    private const int StandardInput = 0;
    private const int SetNow = 0;

    /// <summary>
    ///     Waits for a key (reads the bytes of one key press). Returns false when the terminal's settings can't be read
    ///     (e.g. the input isn't a terminal), so the caller can wait another way.
    /// </summary>
    public static bool Wait()
    {
        // termios is 60 bytes on Linux and 72 on macOS; the flags are 4 bytes each on Linux and 8 on macOS
        var saved = new byte[256];
        if (tcgetattr(StandardInput, saved) != 0)
        {
            return false;
        }

        var macOs = OperatingSystem.IsMacOS();
        var localFlagsOffset = macOs ? 24 : 12;
        var lineInputAndEcho = macOs ? 0x100UL | 0x8UL : 0x2UL | 0x8UL; // ICANON | ECHO
        var settings = (byte[])saved.Clone();
        var localFlags = macOs
            ? BitConverter.ToUInt64(settings, localFlagsOffset)
            : BitConverter.ToUInt32(settings, localFlagsOffset);
        localFlags &= ~lineInputAndEcho;
        if (macOs)
        {
            BitConverter.TryWriteBytes(settings.AsSpan(localFlagsOffset, 8), localFlags);
        }
        else
        {
            BitConverter.TryWriteBytes(settings.AsSpan(localFlagsOffset, 4), (uint)localFlags);
        }

        try
        {
            if (tcsetattr(StandardInput, SetNow, settings) != 0)
            {
                return false;
            }

            // one read returns the bytes of one key press, also of an escape sequence
            var buffer = new byte[64];
            while (read(StandardInput, buffer, buffer.Length) < 0 && Marshal.GetLastPInvokeError() == 4)
            {
                // interrupted by a signal (EINTR), e.g. the terminal being resized
            }

            return true;
        }
        finally
        {
            tcsetattr(StandardInput, SetNow, saved);
        }
    }

    [DllImport("libc", SetLastError = true)]
    private static extern int tcgetattr(int fd, byte[] termios);

    [DllImport("libc", SetLastError = true)]
    private static extern int tcsetattr(int fd, int optionalActions, byte[] termios);

    [DllImport("libc", SetLastError = true)]
    private static extern nint read(int fd, byte[] buffer, nint count);
}
