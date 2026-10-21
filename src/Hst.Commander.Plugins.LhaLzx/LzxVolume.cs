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
            lzxVolume.AddEntry(lzxEntry.Name, lzxEntry.UnpackedSize, lzxEntry.Date.ToUniversalTime(), lzxEntry);
        }

        return lzxVolume;
    }

    protected override async Task<Stream> OpenArchiveEntry(LzxEntry archiveEntry) =>
        await _lzxArchive.OpenEntry(archiveEntry);
}
