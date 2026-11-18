using Hst.Commander.Abstractions;
using Hst.Compression.Lha;

namespace Hst.Commander.Plugins.LhaLzx;

/// <summary>
/// Read-only volume browsing the entries of a lha archive (.lha, .lzh), read with Hst.Compression.
/// </summary>
public class LhaVolume : ArchiveVolume<LhaEntry>
{
    // operating systems of level 1 and 2 headers storing their own attributes instead of Amiga protection bits
    private static readonly byte[] NonAmigaOsTypes =
    [
        LhaOsType.MsDos, LhaOsType.Unix, LhaOsType.Windows95, LhaOsType.WindowsNt, LhaOsType.MacOs, LhaOsType.Os2,
        LhaOsType.Java
    ];

    private readonly LhaArchive _lhaArchive;

    private LhaVolume(LhaArchive lhaArchive) : base("Lha", true)
    {
        _lhaArchive = lhaArchive;
    }

    /// <summary>Creates a volume reading the entries of a lha archive.</summary>
    /// <param name="stream">Readable and seekable stream with the archive, kept open while the volume is used.</param>
    public static async Task<LhaVolume> Create(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);

        stream.Position = 0;

        // the stream is left open, as it's owned by Hst Commander
        var lhaArchive = new LhaArchive(stream, LhaOptions.AmigaLhaOptions, leaveOpen: true);
        var lhaVolume = new LhaVolume(lhaArchive);
        foreach (var lhaEntry in await lhaArchive.EntriesAsync())
        {
            // directories end with a path separator; symbolic links are directory entries (-lhd-) without one
            var path = GetPath(lhaEntry.Name);
            var isDirectory = path.EndsWith('\\') || path.EndsWith('/');
            if (!isDirectory && (lhaEntry.Method == LhaMethod.Lhd || lhaEntry.LinkTarget != null))
            {
                continue;
            }

            lhaVolume.AddEntry(path, lhaEntry.UnpackedSize, lhaEntry.Date, lhaEntry, GetAmigaMetadata(lhaEntry));
        }

        return lhaVolume;
    }

    /// <summary>Path of a lha entry: Amiga lha archives store a comment after a zero character in the name.</summary>
    private static string GetPath(string name)
    {
        var index = name.IndexOf('\0');
        return index >= 0 ? name[..index] : name;
    }

    /// <summary>
    /// Amiga protection bits and comment of a lha entry: Amiga lha archives store the protection bits as they are on
    /// disk in the attribute and the comment after a zero character in the name.
    /// </summary>
    private static AmigaMetadata GetAmigaMetadata(LhaEntry lhaEntry)
    {
        var index = lhaEntry.Name.IndexOf('\0');
        var comment = index >= 0 ? lhaEntry.Name[(index + 1)..] : string.Empty;
        var protectionBits = NonAmigaOsTypes.Contains(lhaEntry.OsType) ? 0 : lhaEntry.Attributes & 0xff;
        return new AmigaMetadata(protectionBits, comment);
    }

    protected override async Task<Stream> OpenArchiveEntry(string name, LhaEntry archiveEntry,
        CancellationToken cancellationToken) =>
        await _lhaArchive.OpenEntryAsync(archiveEntry, cancellationToken);
}
