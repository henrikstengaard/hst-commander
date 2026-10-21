using Hst.Compression.Lha;

namespace Hst.Commander.Plugins.LhaLzx;

/// <summary>
/// Read-only volume browsing the entries of a lha archive (.lha, .lzh), read with Hst.Compression.
/// </summary>
public class LhaVolume : ArchiveVolume<LzHeader>
{
    // entries for directories, also used for symbolic links (without a trailing path separator)
    private const string DirectoryMethod = "-lhd-";

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

            lhaVolume.AddEntry(path, header.OriginalSize, header.UnixLastModifiedStamp, header);
        }

        return lhaVolume;
    }

    /// <summary>Path of a lha entry: Amiga lha archives store a comment after a zero character in the name.</summary>
    private static string GetPath(string name)
    {
        var index = name.IndexOf('\0');
        return index >= 0 ? name[..index] : name;
    }

    protected override Task<Stream> OpenArchiveEntry(LzHeader archiveEntry) =>
        Task.FromResult<Stream>(_lhaArchive.OpenEntry(archiveEntry));
}
