namespace Hst.Commander.Abstractions;

/// <summary>
/// Commands of Hst Commander a <see cref="PanelButton" /> can run, by their <see cref="PanelButton.Action" />. Commands
/// working on entries use the active pane.
/// </summary>
public static class CommanderActions
{
    public const string LeftVolume = "LeftVolume";
    public const string RightVolume = "RightVolume";
    public const string View = "View";
    public const string Edit = "Edit";
    public const string Copy = "Copy";
    public const string Move = "Move";
    public const string MakeDirectory = "MakeDirectory";
    public const string Delete = "Delete";
    public const string CreateVolume = "CreateVolume";
    public const string Properties = "Properties";
    public const string Rename = "Rename";
    public const string ClipboardCopy = "ClipboardCopy";
    public const string ClipboardCut = "ClipboardCut";
    public const string ClipboardPaste = "ClipboardPaste";
    public const string OpenAsVolume = "OpenAsVolume";
    public const string Parent = "Parent";
    public const string Root = "Root";
    public const string SelectAll = "SelectAll";
    public const string SelectNone = "SelectNone";
    public const string SelectGroup = "SelectGroup";
    public const string UnselectGroup = "UnselectGroup";
    public const string InvertSelection = "InvertSelection";
    public const string Refresh = "Refresh";
    public const string SwapPanes = "SwapPanes";
    public const string SameLocation = "SameLocation";
    public const string Location = "Location";
    public const string AddBookmark = "AddBookmark";
    public const string Search = "Search";
    public const string QuickSearch = "QuickSearch";
    public const string Settings = "Settings";
    public const string Quit = "Quit";

    /// <summary>All commands with their descriptions, in the order they are listed when choosing a command.</summary>
    public static IReadOnlyList<CommanderAction> All { get; } =
    [
        new(LeftVolume, "Choose left volume"),
        new(RightVolume, "Choose right volume"),
        new(View, "View"),
        new(Edit, "Edit"),
        new(Copy, "Copy"),
        new(Move, "Move/rename"),
        new(MakeDirectory, "Make directory"),
        new(Delete, "Delete"),
        new(CreateVolume, "Create volume"),
        new(Properties, "Properties"),
        new(Rename, "Rename"),
        new(ClipboardCopy, "Copy to clipboard"),
        new(ClipboardCut, "Cut to clipboard"),
        new(ClipboardPaste, "Paste from clipboard"),
        new(OpenAsVolume, "Open as volume"),
        new(Parent, "Parent directory"),
        new(Root, "Root directory"),
        new(SelectAll, "Select all"),
        new(SelectNone, "Select none"),
        new(SelectGroup, "Select group"),
        new(UnselectGroup, "Unselect group"),
        new(InvertSelection, "Invert selection"),
        new(Refresh, "Refresh"),
        new(SwapPanes, "Swap panes"),
        new(SameLocation, "Same location in other pane"),
        new(Location, "Location"),
        new(AddBookmark, "Add bookmark"),
        new(Search, "Search"),
        new(QuickSearch, "Quick search"),
        new(Settings, "Settings"),
        new(Quit, "Quit")
    ];

    /// <summary>The description of a command, the command itself when unknown.</summary>
    public static string GetDescription(string action) =>
        All.FirstOrDefault(command => command.Id == action)?.Description ?? action;
}

/// <summary>Command of <see cref="CommanderActions" />.</summary>
/// <param name="Id">Identifier used as <see cref="PanelButton.Action" />, e.g. "Copy".</param>
/// <param name="Description">Description shown when choosing a command, e.g. "Copy".</param>
public sealed record CommanderAction(string Id, string Description);
