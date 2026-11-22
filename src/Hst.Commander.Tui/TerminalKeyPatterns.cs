using System.Reflection;
using System.Text.RegularExpressions;
using Terminal.Gui.Drivers;

namespace Hst.Commander.Tui;

/// <summary>
///     Adds the function key sequences Terminal.Gui 2.5 doesn't know to its parser of the keys terminals send on Linux
///     and macOS, see <see cref="ExtraFunctionKeyPattern" />. Called after each <c>Application.Init</c>.
/// </summary>
public static class TerminalKeyPatterns
{
    /// <summary>
    ///     Adds <see cref="ExtraFunctionKeyPattern" /> before Terminal.Gui's patterns. Terminal.Gui doesn't let patterns
    ///     be added, so the private list of its keyboard parser is changed; when that isn't found (another Terminal.Gui
    ///     version, or the Windows driver reading key events), nothing is changed.
    /// </summary>
    public static void Install()
    {
        try
        {
            var parser = Application.Driver?.GetInputProcessor().GetParser();
            var keyboardParser = FindField(parser?.GetType(), "_keyboardParser")?.GetValue(parser);
            if (FindField(keyboardParser?.GetType(), "_patterns")?.GetValue(keyboardParser) is
                    List<AnsiKeyboardParserPattern> patterns &&
                !patterns.OfType<ExtraFunctionKeyPattern>().Any())
            {
                patterns.Insert(0, new ExtraFunctionKeyPattern(
                    Environment.GetEnvironmentVariable("TERM_PROGRAM") == "Apple_Terminal"));
            }
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            // without the pattern only the extra keys are missing
        }
    }

    private static FieldInfo? FindField(Type? type, string name)
    {
        for (; type != null; type = type.BaseType)
        {
            if (type.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic) is { } field)
            {
                return field;
            }
        }

        return null;
    }
}

/// <summary>
///     The keys of <c>ESC [ code ~</c> sequences with codes Terminal.Gui's <c>CsiKeyPattern</c> doesn't know. It
///     matches every such sequence and throws a <see cref="NullReferenceException" /> for codes it doesn't know,
///     e.g. when Ctrl+F10 is pressed in Terminal.app. The VT220 codes of F13 to F20 (25, 26, 28, 29, 31, 32, 33, 34)
///     are shifted function keys: Terminal.app sends them for Shift+F5 to Shift+F12, rxvt for Shift+F3 to Shift+F10.
///     Other codes are no key.
/// </summary>
internal sealed class ExtraFunctionKeyPattern(bool appleTerminal) : AnsiKeyboardParserPattern
{
    private static readonly Regex Pattern = new(@"^\u001b\[(\d+)(?:;(\d+(?::\d+)*))?~$");

    /// <summary>The codes <c>CsiKeyPattern</c> knows (Home, End, Page Up/Down, Insert, Delete, F1 to F12).</summary>
    private static readonly HashSet<int> KnownCodes = [1, 2, 3, 4, 5, 6, 11, 12, 13, 14, 15, 17, 18, 19, 20, 21, 23, 24];

    private static readonly int[] ShiftedCodes = [25, 26, 28, 29, 31, 32, 33, 34];

    private static readonly Key[] FunctionKeys =
        [Key.F1, Key.F2, Key.F3, Key.F4, Key.F5, Key.F6, Key.F7, Key.F8, Key.F9, Key.F10, Key.F11, Key.F12];

    public override bool IsMatch(string? input) =>
        input != null && Pattern.Match(input) is { Success: true } match &&
        int.TryParse(match.Groups[1].Value, out var code) && !KnownCodes.Contains(code);

    protected override Key? GetKeyImpl(string? input)
    {
        var match = Pattern.Match(input ?? string.Empty);
        if (!match.Success || !int.TryParse(match.Groups[1].Value, out var code))
        {
            return new Key(Key.Empty);
        }

        var index = Array.IndexOf(ShiftedCodes, code);
        if (index < 0)
        {
            return new Key(Key.Empty);
        }

        // Shift+F5 and up in Terminal.app, Shift+F3 and up in rxvt
        var key = new Key(FunctionKeys[index + (appleTerminal ? 4 : 2)]).WithShift;
        var modifiers = match.Groups[2].Value;
        return string.IsNullOrEmpty(modifiers) ? key : ApplyModifiersAndEventType(modifiers, key);
    }
}
