namespace Hst.Commander.Core;

/// <summary>Which editor F4 opens files in, chosen in the settings of both front-ends.</summary>
public enum EditorKind
{
    /// <summary>The editor built into Hst Commander, the default.</summary>
    BuiltIn,

    /// <summary>
    ///     The text editor of the host operating system (see <see cref="ExternalEditor.GetSystemEditor" />), e.g. Notepad
    ///     on Windows, TextEdit on macOS and the desktop's text editor on Linux; in a terminal a terminal editor.
    /// </summary>
    System,

    /// <summary>A command entered in the settings, see <see cref="ExternalEditor.ParseCommand" />.</summary>
    Custom
}

public static class EditorKinds
{
    public static IReadOnlyList<EditorKind> All { get; } = [EditorKind.BuiltIn, EditorKind.System, EditorKind.Custom];

    /// <summary>Name shown in the settings; the system editor with the name of the editor used.</summary>
    public static string GetName(EditorKind kind, bool terminal) => kind switch
    {
        EditorKind.System => ExternalEditor.GetSystemEditor(terminal) is { } editor
            ? $"System default ({editor.Name})"
            : "System default (none found)",
        EditorKind.Custom => "Custom command",
        _ => "Built-in editor"
    };
}
