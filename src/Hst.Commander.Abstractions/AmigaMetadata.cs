namespace Hst.Commander.Abstractions;

/// <summary>
/// Amiga protection bits and comment of an entry, which only Amiga file systems store (see
/// <see cref="IEntry.AmigaMetadata" /> and <see cref="IVolume.SetAmigaMetadata" />). Copying from an Amiga volume to
/// other volumes stores them as UAE metadata, like the WinUAE and FS-UAE emulators.
/// </summary>
/// <param name="ProtectionBits">
/// Protection bits as stored by Amiga file systems: bits 7 to 0 are h, s, p, a, r, w, e and d, where h, s, p and a are
/// set when the flag is set and r, w, e and d are set when reading, writing, executing or deleting is not allowed, so
/// 0 is "----rwed".
/// </param>
/// <param name="Comment">Comment of the entry, empty when none.</param>
public sealed record AmigaMetadata(int ProtectionBits, string Comment)
{
    /// <summary>True when the protection bits are "----rwed" and there is no comment, as for new entries.</summary>
    public bool IsDefault => ProtectionBits == 0 && string.IsNullOrEmpty(Comment);
}
