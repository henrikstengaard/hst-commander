using Hst.Commander.Abstractions;

namespace Hst.Commander.Core;

/// <summary>
///     The command palette (Ctrl+Z), like opencode's: the menu commands grouped by menu, filtered by the typed text.
///     Front-ends show the rows of <see cref="Filter" />, with group headers that can't be selected, select the first
///     command and move between commands with <see cref="MoveIndex" />.
/// </summary>
public static class CommandPalette
{
    public const string FileGroup = "File";
    public const string MarkGroup = "Mark";
    public const string GoGroup = "Go";
    public const string CommandsGroup = "Commands";
    public const string ViewGroup = "View";
    public const string HelpGroup = "Help";

    /// <summary>
    ///     The commands of the menus in menu order (File, Mark, Go, Commands, Help), with the shortcuts both front-ends
    ///     share. Front-ends change differing shortcuts with <c>with</c> and add groups of their own (e.g. the GUI's View).
    /// </summary>
    /// <param name="includeRestartElevated">True to list restarting as administrator or root.</param>
    public static IReadOnlyList<PaletteCommand> MenuCommands(bool includeRestartElevated) =>
    [
        new(FileGroup, "View", CommanderActions.View, "F3"),
        new(FileGroup, "Edit", CommanderActions.Edit, "F4"),
        new(FileGroup, "Copy", CommanderActions.Copy, "F5"),
        new(FileGroup, "Move/rename", CommanderActions.Move, "F6"),
        new(FileGroup, "Make directory", CommanderActions.MakeDirectory, "F7"),
        new(FileGroup, "Delete", CommanderActions.Delete, "F8"),
        new(FileGroup, "Create volume", CommanderActions.CreateVolume, "F9"),
        new(FileGroup, "Properties", CommanderActions.Properties, "Ctrl+P"),
        new(FileGroup, "Open as volume", CommanderActions.OpenAsVolume, "Ctrl+PgDn"),
        ..(includeRestartElevated
            ? [new PaletteCommand(FileGroup, MenuText(Elevation.RestartMenuText), CommanderActions.RestartElevated, null)]
            : Array.Empty<PaletteCommand>()),
        new(FileGroup, "Settings", CommanderActions.Settings, null),
        new(FileGroup, "Quit", CommanderActions.Quit, "F10"),
        new(MarkGroup, "Select all", CommanderActions.SelectAll, "Ctrl+A"),
        new(MarkGroup, "Select group", CommanderActions.SelectGroup, "+"),
        new(MarkGroup, "Unselect group", CommanderActions.UnselectGroup, "-"),
        new(MarkGroup, "Invert selection", CommanderActions.InvertSelection, "*"),
        new(GoGroup, "Left volume", CommanderActions.LeftVolume, "F1"),
        new(GoGroup, "Right volume", CommanderActions.RightVolume, "F2"),
        new(GoGroup, "Location", CommanderActions.Location, "Ctrl+L"),
        new(GoGroup, "Home directory", CommanderActions.Home, "Ctrl+H"),
        new(GoGroup, "Add bookmark", CommanderActions.AddBookmark, "Ctrl+B"),
        new(GoGroup, "Same location in other pane", CommanderActions.SameLocation, "Ctrl+D"),
        new(GoGroup, "Swap panes", CommanderActions.SwapPanes, "Ctrl+U"),
        new(GoGroup, "Refresh", CommanderActions.Refresh, "Ctrl+R"),
        new(CommandsGroup, "Search", CommanderActions.Search, "Ctrl+S"),
        new(CommandsGroup, "Quick search", CommanderActions.QuickSearch, "Ctrl+F"),
        new(CommandsGroup, "Command line", CommanderActions.CommandLine, "Ctrl+T"),
        new(HelpGroup, "About", CommanderActions.About, null)
    ];

    /// <summary>
    ///     The rows listing the commands matching a query: each group with matches gets a header row followed by its
    ///     matching commands, in the order of the commands. A command matches when every word of the query is part of its
    ///     name or group, case-insensitively; an empty query matches every command.
    /// </summary>
    public static IReadOnlyList<PaletteRow> Filter(IReadOnlyList<PaletteCommand> commands, string? query)
    {
        ArgumentNullException.ThrowIfNull(commands);
        var words = (query ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var rows = new List<PaletteRow>();
        // groups are listed in the order of their first command
        foreach (var group in commands.Select(command => command.Group).Distinct())
        {
            var matches = commands.Where(command => command.Group == group && IsMatch(command, words)).ToList();
            if (matches.Count == 0)
            {
                continue;
            }

            rows.Add(new PaletteRow(group, null));
            rows.AddRange(matches.Select(command => new PaletteRow(command.Name, command)));
        }

        return rows;
    }

    /// <summary>The index of the first command row, -1 when there is none.</summary>
    public static int FirstCommandIndex(IReadOnlyList<PaletteRow> rows) => MoveIndex(rows, -1, 1);

    /// <summary>
    ///     The index of the command row before (<paramref name="direction" /> negative) or after (positive) a row,
    ///     skipping headers. The index is kept at the first and last command, and is -1 when there are no commands.
    /// </summary>
    public static int MoveIndex(IReadOnlyList<PaletteRow> rows, int index, int direction)
    {
        ArgumentNullException.ThrowIfNull(rows);
        var step = direction < 0 ? -1 : 1;
        for (var i = index + step; i >= 0 && i < rows.Count; i += step)
        {
            if (rows[i].IsCommand)
            {
                return i;
            }
        }

        if (index >= 0 && index < rows.Count && rows[index].IsCommand)
        {
            return index;
        }

        // from a header or outside the rows, the nearest command in the other direction
        for (var i = Math.Clamp(index, 0, Math.Max(0, rows.Count - 1)); i >= 0 && i < rows.Count; i -= step)
        {
            if (rows[i].IsCommand)
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>A menu item's text without its hot key underscore and trailing dots, e.g. "Settings" for "_Settings...".</summary>
    public static string MenuText(string text) => text.Replace("_", string.Empty).TrimEnd('.');

    private static bool IsMatch(PaletteCommand command, string[] words) =>
        words.All(word => command.Name.Contains(word, StringComparison.OrdinalIgnoreCase) ||
                          command.Group.Contains(word, StringComparison.OrdinalIgnoreCase));
}

/// <summary>A command of the command palette (<see cref="CommandPalette" />).</summary>
/// <param name="Group">Group the command is listed under, its menu, e.g. "File".</param>
/// <param name="Name">Name of the command, e.g. "Copy".</param>
/// <param name="Action">
///     Command run when chosen, a <see cref="CommanderActions" /> id or one of the front-end's own (e.g. the GUI's
///     navigation modes).
/// </param>
/// <param name="Shortcut">Key shown at the right of the command, null for none.</param>
public sealed record PaletteCommand(string Group, string Name, string Action, string? Shortcut);

/// <summary>A row of the command palette: a group header (<see cref="Command" /> null) or a command.</summary>
/// <param name="Text">Group name of a header, command name of a command.</param>
/// <param name="Command">The command, null for a header.</param>
public sealed record PaletteRow(string Text, PaletteCommand? Command)
{
    /// <summary>True for a command, false for a group header.</summary>
    public bool IsCommand => Command != null;
}
