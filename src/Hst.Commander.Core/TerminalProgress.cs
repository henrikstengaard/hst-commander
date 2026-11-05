using System.Globalization;

namespace Hst.Commander.Core;

/// <summary>
///     Progress shown by the terminal (in its tab or taskbar button) with the ConEmu escape sequence
///     <c>ESC ] 9 ; 4 ; state ; percent BEL</c>, used by the TUI to show the progress of file operations. Only written
///     to terminals known to support it, because other terminals (e.g. iTerm2 before 3.6) show any <c>OSC 9</c> as a
///     desktop notification.
/// </summary>
public static class TerminalProgress
{
    /// <summary>
    ///     Returns true when the terminal is known to show progress: Windows Terminal, ConEmu, WezTerm, Ghostty and
    ///     iTerm2 3.6 or later.
    /// </summary>
    /// <param name="getEnvironmentVariable">Reads an environment variable, e.g. <see cref="Environment.GetEnvironmentVariable(string)" />.</param>
    /// <param name="isWindowsTerminal">True when running in Windows Terminal.</param>
    public static bool IsSupported(Func<string, string?> getEnvironmentVariable, bool isWindowsTerminal)
    {
        ArgumentNullException.ThrowIfNull(getEnvironmentVariable);

        if (isWindowsTerminal ||
            string.Equals(getEnvironmentVariable("ConEmuANSI"), "ON", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var termProgram = getEnvironmentVariable("TERM_PROGRAM");
        if (string.Equals(termProgram, "WezTerm", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(termProgram, "ghostty", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return string.Equals(termProgram, "iTerm.app", StringComparison.OrdinalIgnoreCase) &&
               TryParseVersion(getEnvironmentVariable("TERM_PROGRAM_VERSION"), out var version) &&
               version >= new Version(3, 6);
    }

    /// <summary>Returns the escape sequence showing a state with a fraction from 0 to 1.</summary>
    public static string Sequence(TaskbarProgressState state, double fraction)
    {
        var (code, percent) = state switch
        {
            TaskbarProgressState.Normal => (1, Percent(fraction)),
            TaskbarProgressState.Error => (2, Percent(fraction)),
            TaskbarProgressState.Indeterminate => (3, 0),
            TaskbarProgressState.Paused => (4, Percent(fraction)),
            _ => (0, 0)
        };
        return string.Create(CultureInfo.InvariantCulture, $"\u001b]9;4;{code};{percent}\u0007");
    }

    private static int Percent(double fraction) =>
        (int)Math.Round(Math.Clamp(double.IsNaN(fraction) ? 0 : fraction, 0, 1) * 100);

    /// <summary>Parses the leading numbers of a version, e.g. "3.6.0beta1" as 3.6.0.</summary>
    private static bool TryParseVersion(string? text, out Version version)
    {
        version = new Version();
        if (string.IsNullOrEmpty(text))
        {
            return false;
        }

        var end = 0;
        while (end < text.Length && (char.IsAsciiDigit(text[end]) || text[end] == '.'))
        {
            end++;
        }

        var numbers = text[..end].TrimEnd('.');
        if (!numbers.Contains('.'))
        {
            numbers += ".0";
        }

        if (!Version.TryParse(numbers, out var parsed))
        {
            return false;
        }

        version = parsed;
        return true;
    }
}
