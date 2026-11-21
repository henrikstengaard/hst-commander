namespace Hst.Commander.Tui;

/// <summary>
///     Base class of the dialogs, keeping the button layout of earlier Terminal.Gui versions: Terminal.Gui v2 makes the
///     last button added the default and leaves an empty row above the buttons, so the dialogs' heights would be one row
///     short and Enter would press e.g. Cancel. Here the button created with <see cref="Button.IsDefault" /> stays the
///     default (the first button otherwise) and the buttons are right above the bottom border.
/// </summary>
public class TuiDialog : Dialog
{
    private Button? _defaultButton;

    /// <summary>Adds a button to the bottom of the dialog, keeping the default button.</summary>
    public new void AddButton(Button button)
    {
        ArgumentNullException.ThrowIfNull(button);

        if (button.IsDefault || _defaultButton == null)
        {
            _defaultButton = button;
        }

        base.AddButton(button);

        // the buttons are on the first row of the button area instead of below an empty row
        button.Y = 0;
        Padding.Thickness = Padding.Thickness with { Bottom = Math.Max(0, Padding.Thickness.Bottom - 1) };

        foreach (var added in Buttons)
        {
            added.IsDefault = added == _defaultButton;
        }

        DefaultAcceptView = _defaultButton;
    }

    /// <summary>
    ///     Leaves closing the dialog to the buttons: Terminal.Gui v2's dialog stops when Enter is pressed in e.g. a text
    ///     field, before the default button handles it, so a dialog checking its input or staying open (e.g. searching)
    ///     would close. Accept is passed on to the default button, which closes the dialog when it's done.
    /// </summary>
    protected override bool OnAccepting(CommandEventArgs args) => false;
}
