namespace Hst.Commander.Core;

/// <summary>
///     Key combinations opening the volume provider list of the left and right pane, chosen in the settings of both
///     front-ends.
/// </summary>
public enum VolumeProviderKeys
{
    /// <summary>Alt+F1 and Alt+F2, like Midnight Commander, Far Manager and Total Commander.</summary>
    AltFunctionKeys,

    /// <summary>
    ///     Shift+F1 and Shift+F2, for desktops that take Alt+F1/Alt+F2 themselves, e.g. Alt+F2 opening the run
    ///     dialog on GNOME, KDE and XFCE.
    /// </summary>
    ShiftFunctionKeys,

    /// <summary>
    ///     F1 and F2, the default: they reach the TUI in Windows terminals and Terminal.app (which sends nothing for
    ///     Shift+F1/Shift+F2), but not F1 in Linux terminals using it for their help, e.g. GNOME Terminal.
    /// </summary>
    FunctionKeys
}

/// <summary>
///     The chosen <see cref="VolumeProviderKeys" />. Both front-ends also open the lists with plain F1 and F2 when
///     other keys are chosen, for terminals that don't send them (e.g. Terminal.app sends nothing for
///     Shift+F1/Shift+F2, and Alt only with Option set as Meta key).
/// </summary>
public static class VolumeProviderKeyBindings
{
    public static IReadOnlyList<VolumeProviderKeys> All { get; } =
        [VolumeProviderKeys.FunctionKeys, VolumeProviderKeys.AltFunctionKeys, VolumeProviderKeys.ShiftFunctionKeys];

    /// <summary>Keys used when none are chosen in the settings: F1/F2 on every operating system.</summary>
    public static VolumeProviderKeys Default => VolumeProviderKeys.FunctionKeys;

    /// <summary>Text of the key opening the left pane's volume provider list, e.g. "Alt+F1".</summary>
    public static string GetLeftKeyText(VolumeProviderKeys keys) => $"{GetModifierText(keys)}F1";

    /// <summary>Text of the key opening the right pane's volume provider list, e.g. "Alt+F2".</summary>
    public static string GetRightKeyText(VolumeProviderKeys keys) => $"{GetModifierText(keys)}F2";

    /// <summary>Name shown in the settings, e.g. "Alt+F1/Alt+F2".</summary>
    public static string GetName(VolumeProviderKeys keys) => $"{GetLeftKeyText(keys)}/{GetRightKeyText(keys)}";

    private static string GetModifierText(VolumeProviderKeys keys) => keys switch
    {
        VolumeProviderKeys.FunctionKeys => string.Empty,
        VolumeProviderKeys.ShiftFunctionKeys => "Shift+",
        _ => "Alt+"
    };
}
