namespace Hst.Commander.Core;

/// <summary>
///     How Amiga names, protection bits and comments are stored when copying from an Amiga volume to other volumes,
///     e.g. a local directory, chosen in the settings of both front-ends. Copying from other volumes to an Amiga volume
///     reads both kinds of UAE metadata.
/// </summary>
public enum UaeMetadataFormat
{
    /// <summary>
    ///     A "_UAEFSDB.___" file in each directory, used by the WinUAE and FS-UAE emulators. Names that can't be used
    ///     are stored as safe names starting with "__uae___".
    /// </summary>
    UaeFsDb,

    /// <summary>
    ///     A ".uaem" metafile next to each entry, used by the FS-UAE emulator. Characters that can't be used in names
    ///     are encoded as "%xx".
    /// </summary>
    UaeMetafile,

    /// <summary>No metadata: protection bits and comments are lost and invalid characters in names are replaced with "_".</summary>
    None
}

public static class UaeMetadataFormats
{
    public static IReadOnlyList<UaeMetadataFormat> All { get; } =
        [UaeMetadataFormat.UaeFsDb, UaeMetadataFormat.UaeMetafile, UaeMetadataFormat.None];

    /// <summary>Name shown in the settings.</summary>
    public static string GetName(UaeMetadataFormat format) => format switch
    {
        UaeMetadataFormat.UaeMetafile => "UAE metafiles (FS-UAE)",
        UaeMetadataFormat.None => "None",
        _ => "UAEFSDB (WinUAE, FS-UAE)"
    };
}
