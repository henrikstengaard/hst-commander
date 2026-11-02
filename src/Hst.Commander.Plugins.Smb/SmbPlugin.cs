using Hst.Commander.Abstractions;

namespace Hst.Commander.Plugins.Smb;

/// <summary>
/// Plugin browsing Windows file shares and other SMB 2 and 3 servers (e.g. Samba and network attached storage) as
/// volumes. Choosing "SMB" in the volume provider list asks for an SMB path like "\\server\share" or
/// "smb://server/share", and for a user name and password when the server needs them. An external plugin, not included
/// with Hst Commander, as it uses SMBLibrary (LGPL-3.0-or-later), whose notices are shown in the About dialog.
/// </summary>
public class SmbPlugin : IPlugin
{
    private const string NoticesResourceName = "THIRD-PARTY-NOTICES.txt";

    private static readonly Lazy<string?> Notices = new(ReadNotices);

    public string Name => "Smb";

    public IEnumerable<IVolumeProvider> VolumeProviders { get; } = [new SmbVolumeProvider()];

    /// <summary>The notices of SMBLibrary and its licenses, the LGPL and GPL version 3.</summary>
    public string? ThirdPartyNotices => Notices.Value;

    private static string? ReadNotices()
    {
        using var stream = typeof(SmbPlugin).Assembly.GetManifestResourceStream(NoticesResourceName);
        if (stream == null)
        {
            return null;
        }

        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
