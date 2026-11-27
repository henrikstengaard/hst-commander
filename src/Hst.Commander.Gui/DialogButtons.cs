using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace Hst.Commander.Gui;

/// <summary>
///     Keyboard handling of the buttons in the dialogs: a dialog opens with the focus visibly on its main button, and
///     Left and Right move the focus between the buttons of a row (e.g. Yes, No and All), wrapping around.
/// </summary>
public static class DialogButtons
{
    /// <summary>Handles Left and Right on the buttons of every window, so all dialogs get it.</summary>
    public static void Register() =>
        InputElement.KeyDownEvent.AddClassHandler<Window>(OnKeyDown, RoutingStrategies.Tunnel);

    /// <summary>
    ///     Focuses a button as if it was reached with the keyboard: <see cref="InputElement.Focus" /> without a
    ///     navigation method doesn't set <c>:focus-visible</c>, so no focus ring would show which button has the focus.
    /// </summary>
    public static void Focus(Button button)
    {
        ArgumentNullException.ThrowIfNull(button);
        button.Focus(NavigationMethod.Tab);
    }

    private static void OnKeyDown(Window window, KeyEventArgs e)
    {
        if (e.Handled || e.KeyModifiers != KeyModifiers.None || e.Key is not (Key.Left or Key.Right) ||
            e.Source is not Button button || button is ToggleButton || button.Parent is not Panel panel)
        {
            return;
        }

        var buttons = panel.Children
            .OfType<Button>()
            .Where(b => b == button || (b is not ToggleButton && b.IsEffectivelyVisible && b.IsEffectivelyEnabled &&
                                        b.Focusable))
            .ToList();
        if (buttons.Count < 2)
        {
            return;
        }

        var step = e.Key == Key.Right ? 1 : -1;
        var index = (buttons.IndexOf(button) + step + buttons.Count) % buttons.Count;
        buttons[index].Focus(NavigationMethod.Directional);
        e.Handled = true;
    }
}
