namespace Hst.Commander.Core;

/// <summary>
///     The command line of a main window, shared by both panes like in Midnight Commander and Total Commander: a shell
///     command typed by the user and kept until it runs or is cleared, with entry names inserted with Ctrl+Enter and the
///     commands run during the session in a history.
/// </summary>
public class CommandLine
{
    /// <summary>Shown when Enter is pressed where the shell can't run, e.g. inside an image or on an FTP server.</summary>
    public const string NotLocalMessage = "Commands can only run in local directories";

    private readonly List<string> _history = [];
    private int _historyIndex;
    private string _draft = string.Empty;

    /// <summary>The command being typed.</summary>
    public string Text { get; set; } = string.Empty;

    /// <summary>Commands run during the session, oldest first.</summary>
    public IReadOnlyList<string> History => _history;

    /// <summary>
    ///     Inserts a name or path at a position of <see cref="Text" />, quoted for the shell when needed, with a space
    ///     before it when it would otherwise join the text before it and a space after it, so the next name inserted
    ///     is separated.
    /// </summary>
    /// <returns>The position after the inserted text, e.g. for the cursor.</returns>
    public int Insert(string value, int position)
    {
        ArgumentNullException.ThrowIfNull(value);
        position = Math.Clamp(position, 0, Text.Length);
        var before = position > 0 && !char.IsWhiteSpace(Text[position - 1]) ? " " : string.Empty;
        var after = position < Text.Length && char.IsWhiteSpace(Text[position]) ? string.Empty : " ";
        var inserted = before + Quote(value) + after;
        Text = Text.Insert(position, inserted);
        return position + inserted.Length;
    }

    /// <summary>Inserts a name or path at the end of <see cref="Text" />, see <see cref="Insert(string, int)" />.</summary>
    public int Insert(string value) => Insert(value, Text.Length);

    /// <summary>Clears the command and stops browsing the history.</summary>
    public void Clear()
    {
        Text = string.Empty;
        _historyIndex = _history.Count;
        _draft = string.Empty;
    }

    /// <summary>
    ///     Takes the command to run: adds it to the history unless it's the last command run, and clears it. Null when
    ///     the command is empty.
    /// </summary>
    public string? TakeCommand()
    {
        var command = Text.Trim();
        if (command.Length == 0)
        {
            return null;
        }

        if (_history.Count == 0 || _history[^1] != command)
        {
            _history.Add(command);
        }

        Clear();
        return command;
    }

    /// <summary>
    ///     Shows the previous command of the history in <see cref="Text" /> (Up), keeping the command being typed to
    ///     return to with <see cref="Next" />. False at the oldest command.
    /// </summary>
    public bool Previous()
    {
        if (_historyIndex <= 0)
        {
            return false;
        }

        if (_historyIndex == _history.Count)
        {
            _draft = Text;
        }

        _historyIndex--;
        Text = _history[_historyIndex];
        return true;
    }

    /// <summary>
    ///     Shows the next command of the history in <see cref="Text" /> (Down), or the command that was being typed after
    ///     the newest one. False when not browsing the history.
    /// </summary>
    public bool Next()
    {
        if (_historyIndex >= _history.Count)
        {
            return false;
        }

        _historyIndex++;
        Text = _historyIndex == _history.Count ? _draft : _history[_historyIndex];
        return true;
    }

    /// <summary>
    ///     Quotes a name or path for the shell when it has spaces or characters the shell interprets: with double quotes
    ///     for cmd.exe on Windows (names can't contain double quotes there), with single quotes elsewhere.
    /// </summary>
    public static string Quote(string value) => Quote(value, OperatingSystem.IsWindows());

    /// <summary>Quotes a name or path for cmd.exe (<paramref name="windows" />) or a Unix shell, see <see cref="Quote(string)" />.</summary>
    public static string Quote(string value, bool windows)
    {
        if (windows)
        {
            return value.Length == 0 || value.Any(c => char.IsWhiteSpace(c) || "&|<>^(),;=%!'`\"".Contains(c))
                ? $"\"{value}\""
                : value;
        }

        return value.Length == 0 || value.Any(c => !char.IsAsciiLetterOrDigit(c) && !"._-+/:,@=%".Contains(c))
            ? $"'{value.Replace("'", @"'\''")}'"
            : value;
    }
}
