using Hst.Commander.Abstractions;
using TuiAttribute = Terminal.Gui.Drawing.Attribute;

namespace Hst.Commander.Tui;

public enum TuiColorMode
{
    Auto,
    Colors16,
    Colors256,
    TrueColor
}

/// <summary>One color policy for palettes, custom buttons and terminal output.</summary>
public static class TuiColors
{
    private static readonly Color[] IndexedColors = CreateIndexedColors();

    public static TuiColorMode RequestedMode { get; private set; }
    public static TuiColorMode EffectiveMode { get; private set; } = TuiColorMode.Colors16;
    public static string? Notice { get; private set; }

    public static string GetName(TuiColorMode mode) => mode switch
    {
        TuiColorMode.Auto => "Auto",
        TuiColorMode.Colors16 => "16 colors",
        TuiColorMode.Colors256 => "256 colors",
        TuiColorMode.TrueColor => "True color",
        _ => throw new ArgumentOutOfRangeException(nameof(mode))
    };

    public static TuiColorMode Resolve(TuiColorMode requested, string? term, string? colorTerm,
        bool windowsTerminal, bool legacyConsole = false, bool indexedOutput = true)
    {
        var detected = requested == TuiColorMode.Auto ? Detect(term, colorTerm, windowsTerminal) : requested;
        return legacyConsole || (detected == TuiColorMode.Colors256 && !indexedOutput)
            ? TuiColorMode.Colors16
            : detected;
    }

    private static TuiColorMode Detect(string? term, string? colorTerm, bool windowsTerminal)
    {
        if (string.Equals(term, "dumb", StringComparison.OrdinalIgnoreCase))
        {
            return TuiColorMode.Colors16;
        }

        if (windowsTerminal ||
            string.Equals(colorTerm, "truecolor", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(colorTerm, "24bit", StringComparison.OrdinalIgnoreCase) ||
            (term?.EndsWith("-direct", StringComparison.OrdinalIgnoreCase) ?? false))
        {
            return TuiColorMode.TrueColor;
        }

        return (term?.EndsWith("-256color", StringComparison.OrdinalIgnoreCase) ?? false)
            ? TuiColorMode.Colors256
            : TuiColorMode.Colors16;
    }

    public static void Apply(TuiColorMode requested, IOutput? output = null)
    {
        RequestedMode = requested;
        var term = Environment.GetEnvironmentVariable("TERM");
        var colorTerm = Environment.GetEnvironmentVariable("COLORTERM");
        var windowsTerminal = OperatingSystem.IsWindows() &&
                              !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WT_SESSION"));
        var desired = Resolve(requested, term, colorTerm, windowsTerminal);
        EffectiveMode = Resolve(requested, term, colorTerm, windowsTerminal,
            output?.IsLegacyConsole ?? false,
            output == null || output is TuiAnsiOutput or TuiNetOutput);
        Notice = desired == EffectiveMode
            ? null
            : output?.IsLegacyConsole == true
                ? "This legacy console only supports 16 colors."
                : "This terminal driver cannot output indexed colors; using 16 colors.";
        if (output != null)
        {
            output.Force16Colors = EffectiveMode == TuiColorMode.Colors16;
        }
    }

    public static Color Parse(string? color, TuiColorMode? mode = null) =>
        Convert(color != null && Color.TryParse(color, out Color? parsed) && parsed is { } value
            ? value
            : new Color(ColorName16.Gray), mode ?? EffectiveMode);

    public static Color Convert(Color color, TuiColorMode mode) => mode switch
    {
        TuiColorMode.Colors16 => new Color(color.GetClosestNamedColor16()),
        TuiColorMode.Colors256 => IndexedColors[GetIndex(color) - 16],
        TuiColorMode.TrueColor => color,
        _ => throw new ArgumentOutOfRangeException(nameof(mode), "Resolve the color mode before converting colors.")
    };

    public static TuiAttribute ButtonAttribute(PanelButton button, TuiAttribute normal) =>
        new(button.Foreground != null ? Parse(button.Foreground) : normal.Foreground,
            button.Background != null ? Parse(button.Background) : normal.Background, normal.Style);

    /// <summary>Uses the stable xterm cube and grayscale, not the terminal-customizable indices 0-15.</summary>
    public static int GetIndex(Color color)
    {
        var nearest = 0;
        var distance = int.MaxValue;
        for (var i = 0; i < IndexedColors.Length; i++)
        {
            var candidate = IndexedColors[i];
            var red = color.R - candidate.R;
            var green = color.G - candidate.G;
            var blue = color.B - candidate.B;
            var nextDistance = red * red + green * green + blue * blue;
            if (nextDistance >= distance)
            {
                continue;
            }

            nearest = i;
            distance = nextDistance;
            if (distance == 0)
            {
                break;
            }
        }

        return nearest + 16;
    }

    private static Color[] CreateIndexedColors()
    {
        int[] levels = [0, 95, 135, 175, 215, 255];
        var colors = new Color[240];
        for (var i = 0; i < 216; i++)
        {
            colors[i] = new Color(levels[i / 36], levels[i / 6 % 6], levels[i % 6]);
        }

        for (var i = 0; i < 24; i++)
        {
            var gray = 8 + i * 10;
            colors[216 + i] = new Color(gray, gray, gray);
        }

        return colors;
    }
}
