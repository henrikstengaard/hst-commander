using Hst.Commander.Abstractions;

namespace Hst.Commander.Core;

/// <summary>
///     The function key bar of the built-in themes (F1-F10), as the content of the bottom area: ten columns with one
///     button each, showing its key before its label.
/// </summary>
public static class FunctionKeyBar
{
    public static ButtonPanel Default { get; } = new(
    [
        Column("F1", "Left", CommanderActions.LeftVolume),
        Column("F2", "Right", CommanderActions.RightVolume),
        Column("F3", "View", CommanderActions.View),
        Column("F4", "Edit", CommanderActions.Edit),
        Column("F5", "Copy", CommanderActions.Copy),
        Column("F6", "Move", CommanderActions.Move),
        Column("F7", "Mkdir", CommanderActions.MakeDirectory),
        Column("F8", "Delete", CommanderActions.Delete),
        Column("F9", "Create", CommanderActions.CreateVolume),
        Column("F10", "Quit", CommanderActions.Quit)
    ]);

    private static ButtonColumn Column(string key, string label, string action) =>
        new([new PanelButton(label, action) { Key = key }]);
}
