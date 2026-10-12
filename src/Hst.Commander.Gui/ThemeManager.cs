using Avalonia;
using Avalonia.Styling;

namespace Hst.Commander.Gui;

/// <summary>
///     Applies a <see cref="GuiTheme" /> to the application. Light and Dark use the Fluent control styling, while
///     Midnight Commander is a custom theme variant based on Dark with additional styles.
/// </summary>
public static class ThemeManager
{
    public static readonly ThemeVariant MidnightCommanderVariant = new("MidnightCommander", ThemeVariant.Dark);

    private static MidnightCommanderStyles? _midnightCommanderStyles;

    public static void Apply(GuiTheme theme)
    {
        var application = Application.Current ?? throw new InvalidOperationException("Application is not initialized.");

        application.RequestedThemeVariant = theme switch
        {
            GuiTheme.Light => ThemeVariant.Light,
            GuiTheme.Dark => ThemeVariant.Dark,
            _ => MidnightCommanderVariant
        };

        var midnightCommanderStyles = _midnightCommanderStyles ??= new MidnightCommanderStyles();
        if (theme == GuiTheme.MidnightCommander)
        {
            if (!application.Styles.Contains(midnightCommanderStyles))
            {
                application.Styles.Add(midnightCommanderStyles);
            }
        }
        else
        {
            application.Styles.Remove(midnightCommanderStyles);
        }
    }
}
