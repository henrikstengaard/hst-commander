namespace Hst.Commander.Core;

/// <summary>
///     What opening a file with Enter or double-click does, chosen in the settings of both front-ends.
/// </summary>
public enum EnterFileAction
{
    /// <summary>
    ///     Files detected as images by any file volume provider, regardless of their extension, e.g. ISO images and zip
    ///     archives (also .nupkg or .docx files), are browsed as volumes; other local files are started by the host
    ///     operating system.
    /// </summary>
    OpenAsVolume,

    /// <summary>
    ///     Local files are always started by the host operating system, e.g. a zip archive opens in its associated
    ///     application. Files inside images, which can't be started, are still browsed as volumes when detected.
    ///     Ctrl+PageDown opens a file as a volume.
    /// </summary>
    StartWithHost
}

public static class EnterFileActions
{
    public static IReadOnlyList<EnterFileAction> All { get; } =
        [EnterFileAction.OpenAsVolume, EnterFileAction.StartWithHost];

    /// <summary>Name shown in the settings.</summary>
    public static string GetName(EnterFileAction action) => action switch
    {
        EnterFileAction.StartWithHost => "Start with host OS",
        _ => "Open as volume"
    };
}
