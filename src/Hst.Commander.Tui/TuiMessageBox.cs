namespace Hst.Commander.Tui;

/// <summary>
///     Message boxes with the first button as the default, like earlier Terminal.Gui versions (Terminal.Gui v2's
///     <see cref="MessageBox" /> makes the last button the default). They use Terminal.Gui's "Dialog" and "Error" schemes,
///     which <c>MainWindow</c> sets to the theme's colors.
/// </summary>
public static class TuiMessageBox
{
    /// <summary>Asks a question, returning the index of the button pressed or -1 when closed with Esc.</summary>
    public static int Query(string title, string message, params string[] buttons) =>
        Show(false, title, message, buttons);

    /// <summary>Shows an error, returning the index of the button pressed or -1 when closed with Esc.</summary>
    public static int ErrorQuery(string title, string message, params string[] buttons) =>
        Show(true, title, message, buttons);

    private static int Show(bool error, string title, string message, string[] buttons)
    {
        using var dialog = new TuiDialog
        {
            Title = title,
            Text = message,
            TextAlignment = Alignment.Center,
            VerticalTextAlignment = Alignment.Center,
            SchemeName = SchemeManager.SchemesToSchemeName(error ? Schemes.Error : Schemes.Dialog),
            // no hot keys from the message's text
            HotKeySpecifier = new System.Text.Rune('\xFFFF')
        };
        dialog.TextFormatter.WordWrap = true;

        var result = -1;
        for (var index = 0; index < buttons.Length; index++)
        {
            var button = new Button { Text = buttons[index], IsDefault = index == 0 };
            var buttonIndex = index;
            button.Accepting += (_, e) =>
            {
                e.Handled = true;
                result = buttonIndex;
                Application.RequestStop(dialog);
            };
            dialog.AddButton(button);
        }

        dialog.Initialized += (_, _) => dialog.Buttons.FirstOrDefault()?.SetFocus();
        Application.Run(dialog);
        return result;
    }
}
