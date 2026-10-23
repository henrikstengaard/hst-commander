using Hst.Commander.Abstractions;
using Hst.Compression.Lzx;

namespace Hst.Commander.Plugins.LhaLzx;

/// <summary>
/// Read-only volume browsing the entries of a lzx archive (.lzx), read with Hst.Compression.
/// </summary>
public class LzxVolume : ArchiveVolume<LzxEntry>
{
    private readonly LzxArchive _lzxArchive;

    private LzxVolume(LzxArchive lzxArchive) : base("Lzx")
    {
        _lzxArchive = lzxArchive;
    }

    /// <summary>Creates a volume reading the entries of a lzx archive.</summary>
    /// <param name="stream">Readable and seekable stream with the archive, kept open while the volume is used.</param>
    public static async Task<LzxVolume> Create(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);

        // the archive isn't disposed by the volume, as it would dispose the stream owned by Hst Commander
        var lzxArchive = new LzxArchive(stream, LzxOptions.AmigaLzxOptions);
        var lzxVolume = new LzxVolume(lzxArchive);
        foreach (var lzxEntry in await lzxArchive.Entries())
        {
            // dates are stored in local time without a time zone
            lzxVolume.AddEntry(lzxEntry.Name, lzxEntry.UnpackedSize, lzxEntry.Date.ToUniversalTime(), lzxEntry,
                new AmigaMetadata(GetProtectionBits(lzxEntry.Attributes), lzxEntry.Comment ?? string.Empty));
        }

        return lzxVolume;
    }

    /// <summary>
    /// Amiga protection bits as stored on disk (bits 7-0 "hsparwed", "rwed" set when not allowed) of the attributes of
    /// a lzx entry, which are set when allowed.
    /// </summary>
    private static int GetProtectionBits(AttributesEnum attributes)
    {
        var protectionBits = 0;
        AddBit(AttributesEnum.HeldResident, 7, true);
        AddBit(AttributesEnum.Script, 6, true);
        AddBit(AttributesEnum.Pure, 5, true);
        AddBit(AttributesEnum.Archive, 4, true);
        AddBit(AttributesEnum.Read, 3, false);
        AddBit(AttributesEnum.Write, 2, false);
        AddBit(AttributesEnum.Executable, 1, false);
        AddBit(AttributesEnum.Delete, 0, false);
        return protectionBits;

        void AddBit(AttributesEnum attribute, int bit, bool setWhenAttribute)
        {
            if (attributes.HasFlag(attribute) == setWhenAttribute)
            {
                protectionBits |= 1 << bit;
            }
        }
    }

    protected override async Task<Stream> OpenArchiveEntry(LzxEntry archiveEntry) =>
        await _lzxArchive.OpenEntry(archiveEntry);
}
