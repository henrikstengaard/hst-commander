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
    ShiftFunctionKeys
}

public static class VolumeProviderKeyBindings
{
    public static IReadOnlyList<VolumeProviderKeys> All { get; } =
        [VolumeProviderKeys.AltFunctionKeys, VolumeProviderKeys.ShiftFunctionKeys];

    /// <summary>
    ///     Keys best fitting the host operating system, used when none are chosen in the settings: Alt+F1/Alt+F2 on
    ///     Windows, Shift+F1/Shift+F2 elsewhere, as Linux desktops take Alt+F1/Alt+F2 and macOS terminals only send Alt
    ///     with Option set as Meta key.
    /// </summary>
    public static VolumeProviderKeys Default => GetDefault(OperatingSystem.IsWindows());

    public static VolumeProviderKeys GetDefault(bool isWindows) => isWindows
        ? VolumeProviderKeys.AltFunctionKeys
        : VolumeProviderKeys.ShiftFunctionKeys;

    /// <summary>Text of the key opening the left pane's volume provider list, e.g. "Alt+F1".</summary>
    public static string GetLeftKeyText(VolumeProviderKeys keys) => $"{GetModifierText(keys)}+F1";

    /// <summary>Text of the key opening the right pane's volume provider list, e.g. "Alt+F2".</summary>
    public static string GetRightKeyText(VolumeProviderKeys keys) => $"{GetModifierText(keys)}+F2";

    /// <summary>Name shown in the settings, e.g. "Alt+F1/Alt+F2".</summary>
    public static string GetName(VolumeProviderKeys keys) => $"{GetLeftKeyText(keys)}/{GetRightKeyText(keys)}";

    private static string GetModifierText(VolumeProviderKeys keys) => keys switch
    {
        VolumeProviderKeys.ShiftFunctionKeys => "Shift",
        _ => "Alt"
    };
}
