using Terminal.Gui;
using TuiAttribute = Terminal.Gui.Attribute;

namespace Hst.Commander.Tui;

/// <summary>
///     Narrow button one column wide at the outer side of a pane, going to the parent when clicked, shown by themes of
///     plugins (<see cref="Hst.Commander.Abstractions.ITheme.ShowParentButtons" />), e.g. Directory Opus 4.
/// </summary>
public sealed class ParentStripView : View
{
    public TuiAttribute Attribute { get; set; }

    /// <summary>Raised when clicked.</summary>
    public event EventHandler? Clicked;

    protected override bool OnDrawingContent()
    {
        var height = Viewport.Height;
        SetAttribute(Attribute);
        for (var row = 0; row < height; row++)
        {
            Move(0, row);
            AddRune(row == height / 2 ? '^' : ' ');
        }

        return true;
    }

    protected override bool OnMouseClick(MouseEventArgs mouseEvent)
    {
        Clicked?.Invoke(this, EventArgs.Empty);
        return true;
    }
}
