using Hst.Commander.Abstractions;
using Hst.Compression.Lha;

namespace Hst.Commander.Plugins.LhaLzx;

/// <summary>
/// Read-only volume browsing the entries of a lha archive (.lha, .lzh), read with Hst.Compression.
/// </summary>
public class LhaVolume : ArchiveVolume<LzHeader>
{
    // entries for directories, also used for symbolic links (without a trailing path separator)
    private const string DirectoryMethod = "-lhd-";

    // operating systems of level 1 and 2 headers storing their own attributes instead of Amiga protection bits
    private static readonly int[] NonAmigaExtendTypes = ['M', 'U', 'w', 'W', 'm', '2', 'J'];

    private readonly LhaArchive _lhaArchive;

    private LhaVolume(LhaArchive lhaArchive) : base("Lha")
    {
        _lhaArchive = lhaArchive;
    }

    /// <summary>Creates a volume reading the entries of a lha archive.</summary>
    /// <param name="stream">Readable and seekable stream with the archive, kept open while the volume is used.</param>
    public static async Task<LhaVolume> Create(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);

        stream.Position = 0;

        // the archive isn't disposed by the volume, as it would dispose the stream owned by Hst Commander
        var lhaArchive = new LhaArchive(stream, LhaOptions.AmigaLhaOptions);
        var lhaVolume = new LhaVolume(lhaArchive);
        foreach (var header in await lhaArchive.Entries())
        {
            var path = GetPath(header.Name);
            var isDirectory = path.EndsWith('\\') || path.EndsWith('/');
            if (!isDirectory && header.Method == DirectoryMethod)
            {
                continue;
            }

            lhaVolume.AddEntry(path, header.OriginalSize, header.UnixLastModifiedStamp, header,
                GetAmigaMetadata(header));
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
    private static AmigaMetadata GetAmigaMetadata(LzHeader header)
    {
        var index = header.Name.IndexOf('\0');
        var comment = index >= 0 ? header.Name[(index + 1)..] : string.Empty;
        var protectionBits = NonAmigaExtendTypes.Contains(header.ExtendType) ? 0 : header.Attribute & 0xff;
        return new AmigaMetadata(protectionBits, comment);
    }

    protected override Task<Stream> OpenArchiveEntry(LzHeader archiveEntry) =>
        Task.FromResult<Stream>(_lhaArchive.OpenEntry(archiveEntry));
}
