using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using Hst.Commander.Abstractions;
using Hst.Commander.Core;

namespace Hst.Commander.Gui;

/// <summary>
///     Applies a theme (<see cref="ThemeChoice" />) to the application. Each theme gets a theme variant of its own based
///     on Light or Dark (<see cref="ITheme.IsDark" />), which colors the controls the theme doesn't, with a theme
///     dictionary of brushes made from its GUI colors (<see cref="ThemeChoice.GuiColors" />) under the keys the views
///     use. Unless the theme keeps the system look (<see cref="ThemeChoice.SystemChrome" />, like the built-in themes),
///     <see cref="ThemeChromeStyles" /> colors the menu bar, the bottom area and the buttons with them too.
/// </summary>
public static class ThemeManager
{
    private static readonly Dictionary<string, ThemeVariant> Variants = new(StringComparer.OrdinalIgnoreCase);
    private static ThemeChromeStyles? _chromeStyles;

    public static void Apply(ThemeChoice theme)
    {
        ArgumentNullException.ThrowIfNull(theme);
        var application = Application.Current ?? throw new InvalidOperationException("Application is not initialized.");
        application.RequestedThemeVariant = GetVariant(application, theme);

        var chromeStyles = _chromeStyles ??= new ThemeChromeStyles();
        if (!theme.SystemChrome)
        {
            if (!application.Styles.Contains(chromeStyles))
            {
                application.Styles.Add(chromeStyles);
            }
        }
        else
        {
            application.Styles.Remove(chromeStyles);
        }
    }

    /// <summary>
    ///     The theme variant of a theme, created the first time with a theme dictionary holding the brushes of its
    ///     colors.
    /// </summary>
    private static ThemeVariant GetVariant(Application application, ThemeChoice theme)
    {
        if (Variants.TryGetValue(theme.Id, out var variant))
        {
            return variant;
        }

        variant = new ThemeVariant($"Theme:{theme.Id}", theme.Theme.IsDark ? ThemeVariant.Dark : ThemeVariant.Light);
        var colors = theme.GuiColors;
        var dictionary = new ResourceDictionary
        {
            ["PaneBackground"] = Brush(colors.PaneBackground),
            ["PaneBorder"] = Brush(colors.PaneBorder),
            ["HeaderForeground"] = Brush(colors.Header),
            ["ContainerForeground"] = Brush(colors.Container),
            ["ItemForeground"] = Brush(colors.Item),
            ["ExecutableForeground"] = Brush(colors.Executable),
            ["PathForeground"] = Brush(colors.Path),
            ["ActivePathBackground"] = Brush(colors.ActivePathBackground),
            ["ActivePathForeground"] = Brush(colors.ActivePathForeground),
            ["SelectedEntryForeground"] = Brush(colors.CursorForeground),
            ["MarkedEntryForeground"] = Brush(colors.Marked),
            ["MarkedContainerBackground"] = colors.MarkedContainerBackground == null
                ? Brushes.Transparent
                : Brush(colors.MarkedContainerBackground),
            ["MarkedItemBackground"] = colors.MarkedItemBackground == null
                ? Brushes.Transparent
                : Brush(colors.MarkedItemBackground),
            ["AdministratorBackground"] = Brush("#C62828"),
            ["AdministratorForeground"] = Brush("#FFFFFF"),
            ["ViewerSelectionBackground"] = Brush(colors.ViewerSelectionBackground ?? colors.CursorBackground),
            ["ViewerSelectionForeground"] = Brush(colors.ViewerSelectionForeground ?? colors.CursorForeground),
            ["CursorBackground"] = Brush(colors.CursorBackground)
        };
        // the chrome's colors, only for themes coloring it: ButtonBackground and ButtonForeground are also the keys of
        // Fluent's buttons, so they color all buttons, including those of dialogs
        if (!theme.SystemChrome)
        {
            dictionary["MenuBackground"] = Brush(colors.MenuBackground);
            dictionary["MenuForeground"] = Brush(colors.MenuForeground);
            dictionary["BottomBackground"] = Brush(colors.BottomBackground);
            dictionary["ButtonBackground"] = Brush(colors.ButtonBackground);
            dictionary["ButtonForeground"] = Brush(colors.ButtonForeground);
            AddDialogResources(dictionary, colors);
        }

        application.Resources.ThemeDictionaries[variant] = dictionary;
        Variants[theme.Id] = variant;
        return variant;
    }

    /// <summary>
    ///     Colors dialogs (and the other windows Fluent draws) with the theme's dialog colors by replacing the Fluent
    ///     resources their controls use: the window, text fields, combo boxes, check boxes, radio buttons, lists, the
    ///     dialogs' tabs and buttons. Text fields and lists get the pane background, borders the pane border, checked
    ///     boxes and focused fields the cursor color and selected list items the viewer's selection colors.
    /// </summary>
    private static void AddDialogResources(ResourceDictionary dictionary, ThemeColors colors)
    {
        var background = colors.DialogBackground;
        var foreground = colors.DialogForeground;
        var input = colors.PaneBackground;
        var border = colors.PaneBorder;
        var accent = colors.CursorBackground;
        var accentText = colors.CursorForeground;
        var selection = colors.ViewerSelectionBackground ?? accent;
        var selectionText = colors.ViewerSelectionForeground ?? accentText;
        var dimmed = Mix(foreground, background, 0.7);
        var disabled = Mix(foreground, background, 0.45);
        var hover = Mix(foreground, input, 0.08);
        var buttonHover = Mix(colors.ButtonForeground, colors.ButtonBackground, 0.12);
        var buttonPressed = Mix(colors.ButtonForeground, colors.ButtonBackground, 0.22);
        const string transparent = "#00000000";

        void Set(string color, params string[] keys)
        {
            foreach (var key in keys)
            {
                dictionary[key] = color == transparent ? Brushes.Transparent : Brush(color);
            }
        }

        // windows and the dialogs' tabs (App.axaml)
        Set(background, "SystemRegionBrush", "SystemControlBackgroundAltHighBrush");
        Set(foreground, "SystemControlForegroundBaseHighBrush");
        Set(dimmed, "SystemControlForegroundBaseMediumBrush");
        Set(border, "SystemControlForegroundBaseMediumLowBrush");
        Set(Mix(foreground, background, 0.1), "SystemControlBackgroundBaseLowBrush");
        Set(disabled, "SystemControlDisabledBaseMediumLowBrush");
        if (Color.TryParse(accent, out var accentColor))
        {
            dictionary["SystemAccentColor"] = accentColor;
        }

        // text fields, also of numeric fields
        Set(input, "TextControlBackground", "TextControlBackgroundPointerOver", "TextControlBackgroundFocused");
        Set(background, "TextControlBackgroundDisabled");
        Set(foreground, "TextControlForeground", "TextControlForegroundPointerOver", "TextControlForegroundFocused");
        Set(disabled, "TextControlForegroundDisabled", "TextControlBorderBrushDisabled");
        Set(dimmed, "TextControlPlaceholderForeground", "TextControlButtonForeground");
        Set(border, "TextControlBorderBrush", "TextControlBorderBrushPointerOver");
        Set(accent, "TextControlBorderBrushFocused");
        Set(selection, "TextControlSelectionHighlightColor");
        Set(input, "TextControlButtonBackground");
        Set(hover, "TextControlButtonBackgroundPointerOver", "TextControlButtonBackgroundPressed");
        Set(foreground, "TextControlButtonForegroundPointerOver", "TextControlButtonForegroundPressed");

        // combo boxes and their drop-downs
        Set(input, "ComboBoxBackground", "ComboBoxBackgroundUnfocused", "ComboBoxDropDownBackground",
            "ComboBoxItemBackground");
        Set(hover, "ComboBoxBackgroundPointerOver", "ComboBoxBackgroundPressed", "ComboBoxItemBackgroundPointerOver",
            "ComboBoxItemBackgroundPressed");
        Set(background, "ComboBoxBackgroundDisabled");
        Set(foreground, "ComboBoxForeground", "ComboBoxForegroundFocused", "ComboBoxForegroundFocusedPressed",
            "ComboBoxDropDownGlyphForeground", "ComboBoxDropDownGlyphForegroundFocused",
            "ComboBoxDropDownGlyphForegroundFocusedPressed", "ComboBoxItemForeground",
            "ComboBoxItemForegroundPointerOver", "ComboBoxItemForegroundPressed");
        Set(disabled, "ComboBoxForegroundDisabled", "ComboBoxDropDownGlyphForegroundDisabled",
            "ComboBoxBorderBrushDisabled", "ComboBoxItemForegroundDisabled");
        Set(dimmed, "ComboBoxPlaceHolderForeground", "ComboBoxPlaceHolderForegroundFocusedPressed");
        Set(border, "ComboBoxBorderBrush", "ComboBoxBorderBrushPointerOver", "ComboBoxDropDownBorderBrush",
            "ComboBoxBackgroundBorderBrushUnfocused");
        Set(accent, "ComboBoxBorderBrushPressed", "ComboBoxBackgroundBorderBrushFocused");
        Set(selection, "ComboBoxItemBackgroundSelected", "ComboBoxItemBackgroundSelectedPointerOver",
            "ComboBoxItemBackgroundSelectedPressed");
        Set(selectionText, "ComboBoxItemForegroundSelected", "ComboBoxItemForegroundSelectedPointerOver",
            "ComboBoxItemForegroundSelectedPressed");

        // check boxes
        foreach (var state in new[] { "Unchecked", "Checked", "Indeterminate" })
        {
            Set(foreground, $"CheckBoxForeground{state}", $"CheckBoxForeground{state}PointerOver",
                $"CheckBoxForeground{state}Pressed");
            Set(disabled, $"CheckBoxForeground{state}Disabled");
            Set(transparent, $"CheckBoxBackground{state}", $"CheckBoxBackground{state}PointerOver",
                $"CheckBoxBackground{state}Pressed", $"CheckBoxBorderBrush{state}",
                $"CheckBoxBorderBrush{state}PointerOver", $"CheckBoxBorderBrush{state}Pressed");
        }

        Set(input, "CheckBoxCheckBackgroundFillUnchecked");
        Set(hover, "CheckBoxCheckBackgroundFillUncheckedPointerOver", "CheckBoxCheckBackgroundFillUncheckedPressed");
        Set(border, "CheckBoxCheckBackgroundStrokeUnchecked", "CheckBoxCheckBackgroundStrokeUncheckedPointerOver",
            "CheckBoxCheckBackgroundStrokeUncheckedPressed");
        Set(accent, "CheckBoxCheckBackgroundFillChecked", "CheckBoxCheckBackgroundFillCheckedPointerOver",
            "CheckBoxCheckBackgroundFillCheckedPressed", "CheckBoxCheckBackgroundStrokeChecked",
            "CheckBoxCheckBackgroundStrokeCheckedPointerOver", "CheckBoxCheckBackgroundStrokeCheckedPressed",
            "CheckBoxCheckBackgroundFillIndeterminate", "CheckBoxCheckBackgroundFillIndeterminatePointerOver",
            "CheckBoxCheckBackgroundFillIndeterminatePressed", "CheckBoxCheckBackgroundStrokeIndeterminate",
            "CheckBoxCheckBackgroundStrokeIndeterminatePointerOver",
            "CheckBoxCheckBackgroundStrokeIndeterminatePressed");
        Set(accentText, "CheckBoxCheckGlyphForegroundChecked", "CheckBoxCheckGlyphForegroundCheckedPointerOver",
            "CheckBoxCheckGlyphForegroundCheckedPressed", "CheckBoxCheckGlyphForegroundIndeterminate",
            "CheckBoxCheckGlyphForegroundIndeterminatePointerOver", "CheckBoxCheckGlyphForegroundIndeterminatePressed");

        // radio buttons
        Set(foreground, "RadioButtonForeground", "RadioButtonForegroundPointerOver", "RadioButtonForegroundPressed");
        Set(disabled, "RadioButtonForegroundDisabled", "RadioButtonOuterEllipseStrokeDisabled");
        Set(transparent, "RadioButtonBackground", "RadioButtonBackgroundPointerOver", "RadioButtonBackgroundPressed",
            "RadioButtonBorderBrush", "RadioButtonBorderBrushPointerOver", "RadioButtonBorderBrushPressed");
        Set(input, "RadioButtonOuterEllipseFill");
        Set(hover, "RadioButtonOuterEllipseFillPointerOver", "RadioButtonOuterEllipseFillPressed");
        Set(border, "RadioButtonOuterEllipseStroke", "RadioButtonOuterEllipseStrokePointerOver",
            "RadioButtonOuterEllipseStrokePressed");
        Set(accent, "RadioButtonOuterEllipseCheckedFill", "RadioButtonOuterEllipseCheckedFillPointerOver",
            "RadioButtonOuterEllipseCheckedFillPressed", "RadioButtonOuterEllipseCheckedStroke",
            "RadioButtonOuterEllipseCheckedStrokePointerOver", "RadioButtonOuterEllipseCheckedStrokePressed");
        Set(accentText, "RadioButtonCheckGlyphFill", "RadioButtonCheckGlyphFillPointerOver",
            "RadioButtonCheckGlyphFillPressed", "RadioButtonCheckGlyphStroke", "RadioButtonCheckGlyphStrokePointerOver",
            "RadioButtonCheckGlyphStrokePressed");

        // lists (the panes draw their rows themselves)
        Set(input, "SystemControlBackgroundChromeMediumLowBrush");
        Set(hover, "SystemControlHighlightListLowBrush", "SystemControlHighlightListMediumBrush");
        Set(selection, "SystemControlHighlightListAccentLowBrush", "SystemControlHighlightListAccentMediumBrush",
            "SystemControlHighlightListAccentHighBrush");
        Set(selectionText, "SystemControlHighlightAltBaseHighBrush");

        // buttons and the spin buttons of numeric fields, around the theme's button colors
        Set(buttonHover, "ButtonBackgroundPointerOver", "RepeatButtonBackgroundPointerOver");
        Set(buttonPressed, "ButtonBackgroundPressed", "RepeatButtonBackgroundPressed");
        Set(colors.ButtonBackground, "RepeatButtonBackground");
        Set(colors.ButtonForeground, "ButtonForegroundPointerOver", "ButtonForegroundPressed", "RepeatButtonForeground",
            "RepeatButtonForegroundPointerOver", "RepeatButtonForegroundPressed");
        Set(background, "ButtonBackgroundDisabled", "RepeatButtonBackgroundDisabled");
        Set(disabled, "ButtonForegroundDisabled", "RepeatButtonForegroundDisabled", "ButtonBorderBrushDisabled",
            "RepeatButtonBorderBrushDisabled");
        Set(border, "ButtonBorderBrush", "ButtonBorderBrushPointerOver", "ButtonBorderBrushPressed",
            "RepeatButtonBorderBrush", "RepeatButtonBorderBrushPointerOver", "RepeatButtonBorderBrushPressed");
    }

    /// <summary>Mixes two "#RRGGBB" colors, gray when either is invalid.</summary>
    private static string Mix(string first, string second, double amount) =>
        ThemeColor.TryNormalize(first, out var a) && ThemeColor.TryNormalize(second, out var b)
            ? ThemeColor.Mix(a, b, amount)
            : "#808080";

    /// <summary>A brush of a "#RRGGBB" color, gray when the color is invalid.</summary>
    public static IBrush Brush(string? color) =>
        new SolidColorBrush(Color.TryParse(color, out var parsed) ? parsed : Colors.Gray);
}
