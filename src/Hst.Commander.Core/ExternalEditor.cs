using System.Diagnostics;
using System.Text;

namespace Hst.Commander.Core;

/// <summary>
///     A program editing files: its name shown in the settings, the program and its arguments, where
///     <see cref="ExternalEditor.FilePlaceholder" /> is replaced with the path of the file (or the path is added last).
/// </summary>
public sealed record ExternalEditorCommand(string Name, string FileName, IReadOnlyList<string> Arguments)
{
    public ProcessStartInfo CreateStartInfo(string path)
    {
        ArgumentNullException.ThrowIfNull(path);

        var startInfo = new ProcessStartInfo(FileName)
        {
            UseShellExecute = false,
            WorkingDirectory = Path.GetDirectoryName(path) ?? string.Empty
        };
        var hasPlaceholder = false;
        foreach (var argument in Arguments)
        {
            hasPlaceholder |= argument.Contains(ExternalEditor.FilePlaceholder, StringComparison.Ordinal);
            startInfo.ArgumentList.Add(argument.Replace(ExternalEditor.FilePlaceholder, path, StringComparison.Ordinal));
        }

        if (!hasPlaceholder)
        {
            startInfo.ArgumentList.Add(path);
        }

        return startInfo;
    }
}

/// <summary>
///     Editors other than the built-in one that F4 can open files in (see <see cref="EditorKind" />): the text editor of
///     the host operating system or a command entered in the settings. The system editor is found when asked for, so
///     an editor installed while running is used. In a terminal it's a terminal editor: Microsoft Edit
///     (<c>edit</c>) or Notepad on Windows, otherwise <c>$VISUAL</c>, <c>$EDITOR</c>, nano or vi. On the desktop it's
///     Notepad on Windows, TextEdit on macOS and the first of the common desktop text editors found on Linux (started
///     so they wait until the file is closed), otherwise <c>xdg-open</c>.
/// </summary>
public static class ExternalEditor
{
    /// <summary>Replaced with the path of the file in the arguments of a command.</summary>
    public const string FilePlaceholder = "%f";

    /// <summary>
    ///     An editor exiting sooner than this after it was started has most likely handed the file to an instance already
    ///     running (e.g. Notepad with tabs), so the file may still be edited and isn't written back yet.
    /// </summary>
    public static readonly TimeSpan HandOffTime = TimeSpan.FromSeconds(2);

    /// <summary>Linux desktop text editors, with arguments making them wait until the file is closed when they have one.</summary>
    private static readonly (string FileName, string Name, string[] Arguments)[] LinuxDesktopEditors =
    [
        ("gnome-text-editor", "Text Editor", ["--standalone"]),
        ("gedit", "gedit", ["--wait"]),
        ("kate", "Kate", ["--block"]),
        ("kwrite", "KWrite", []),
        ("mousepad", "Mousepad", ["--disable-server"]),
        ("xed", "Xed", []),
        ("pluma", "Pluma", []),
        ("featherpad", "FeatherPad", [])
    ];

    /// <summary>The editor to use for a choice in the settings, null when there's none (e.g. an empty command).</summary>
    public static ExternalEditorCommand? Get(EditorKind kind, string? customCommand, bool terminal) => kind switch
    {
        EditorKind.System => GetSystemEditor(terminal),
        EditorKind.Custom => customCommand == null ? null : ParseCommand(customCommand),
        _ => null
    };

    /// <summary>
    ///     The text editor of the host operating system, a terminal editor for a TUI. Null when none is found.
    /// </summary>
    public static ExternalEditorCommand? GetSystemEditor(bool terminal)
    {
        if (OperatingSystem.IsWindows())
        {
            // microsoft edit is included in recent versions of windows 11 and can be installed with winget
            return terminal && FindProgram("edit") is { } edit
                ? new ExternalEditorCommand("Microsoft Edit", edit, [])
                : new ExternalEditorCommand("Notepad", "notepad.exe", []);
        }

        if (terminal)
        {
            foreach (var variable in new[] { "VISUAL", "EDITOR" })
            {
                if (Environment.GetEnvironmentVariable(variable) is { Length: > 0 } value &&
                    ParseCommand(value) is { } command)
                {
                    return command;
                }
            }

            return FindProgram("nano") is { } nano
                ? new ExternalEditorCommand("nano", nano, [])
                : FindProgram("vi") is { } vi
                    ? new ExternalEditorCommand("vi", vi, [])
                    : null;
        }

        if (OperatingSystem.IsMacOS())
        {
            // a new instance of the default text editor, waiting until it quits
            return new ExternalEditorCommand("TextEdit", "open", ["-W", "-n", "-t", FilePlaceholder]);
        }

        foreach (var (fileName, name, arguments) in LinuxDesktopEditors)
        {
            if (FindProgram(fileName) is { } path)
            {
                return new ExternalEditorCommand(name, path, arguments);
            }
        }

        return FindProgram("xdg-open") is { } xdgOpen ? new ExternalEditorCommand("xdg-open", xdgOpen, []) : null;
    }

    /// <summary>
    ///     Parses a command entered in the settings, e.g. <c>code --wait</c> or <c>"C:\Program Files\Notepad++\notepad++.exe" -multiInst %f</c>:
    ///     the program and its arguments separated by spaces, with double quotes around those containing spaces. Null
    ///     for an empty command.
    /// </summary>
    public static ExternalEditorCommand? ParseCommand(string command)
    {
        ArgumentNullException.ThrowIfNull(command);

        var parts = SplitCommand(command);
        if (parts.Count == 0)
        {
            return null;
        }

        var fileName = parts[0];
        return new ExternalEditorCommand(Path.GetFileNameWithoutExtension(fileName), fileName, parts.Skip(1).ToList());
    }

    /// <summary>Starts the editor with a file, returning its process (null when started in a process already running).</summary>
    public static Process? Start(ExternalEditorCommand command, string path)
    {
        ArgumentNullException.ThrowIfNull(command);

        return Process.Start(command.CreateStartInfo(path));
    }

    private static List<string> SplitCommand(string command)
    {
        var parts = new List<string>();
        var part = new StringBuilder();
        var inQuotes = false;
        var hasPart = false;
        foreach (var character in command.Trim())
        {
            if (character == '"')
            {
                inQuotes = !inQuotes;
                hasPart = true;
            }
            else if (char.IsWhiteSpace(character) && !inQuotes)
            {
                if (hasPart)
                {
                    parts.Add(part.ToString());
                    part.Clear();
                    hasPart = false;
                }
            }
            else
            {
                part.Append(character);
                hasPart = true;
            }
        }

        if (hasPart)
        {
            parts.Add(part.ToString());
        }

        return parts;
    }

    /// <summary>Full path of a program found in the directories of the PATH environment variable.</summary>
    private static string? FindProgram(string name)
    {
        var directories = (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var fileName = OperatingSystem.IsWindows() ? $"{name}.exe" : name;
        foreach (var directory in directories)
        {
            try
            {
                var path = Path.Combine(directory, fileName);
                if (File.Exists(path))
                {
                    return path;
                }
            }
            catch (ArgumentException)
            {
            }
        }

        return null;
    }
}
