using Hst.Commander.Abstractions;
using SharpCompress.Archives;

namespace Hst.Commander.Plugins.RarSevenZip;

/// <summary>
/// Read-only volume browsing the entries of a rar or 7z archive read with SharpCompress. The archive is disposed with
/// the volume, leaving the stream owned by Hst Commander open. Files of solid archives are read with a
/// <see cref="SolidArchiveReader" />, so reading many of them doesn't decompress their solid block over and over.
/// </summary>
public class SharpCompressVolume : ArchiveVolume<IArchiveEntry>, IDisposable
{
    private readonly IArchive _archive;
    private readonly SolidArchiveReader? _solidArchiveReader;

    /// <param name="name">Name of the volume, e.g. "Rar".</param>
    /// <param name="archive">Archive opened with <c>LeaveStreamOpen</c>, disposed with the volume.</param>
    public SharpCompressVolume(string name, IArchive archive) : base(name)
    {
        ArgumentNullException.ThrowIfNull(archive);

        _archive = archive;
        var archiveEntries = archive.Entries.ToList();
        foreach (var archiveEntry in archiveEntries)
        {
            AddEntry(archiveEntry.Key ?? string.Empty, archiveEntry.Size, GetModifiedDate(archiveEntry), archiveEntry,
                isDirectory: archiveEntry.IsDirectory);
        }

        if (archive.IsSolid)
        {
            _solidArchiveReader = new SolidArchiveReader(archive, archiveEntries);
        }
    }

    /// <summary>
    /// Files of solid archives are read faster in the archive's order, as reading a file decompresses the files before
    /// it in its solid block, so copying reads them in this order.
    /// </summary>
    protected override bool ReadsInArchiveOrder => _solidArchiveReader != null;

    /// <summary>Reader of the files of a solid archive, null for other archives; for tests.</summary>
    internal SolidArchiveReader? SolidArchiveReader => _solidArchiveReader;

    /// <summary>Modified date in UTC: dates without a time zone (e.g. dos dates of rar 4 archives) are local time.</summary>
    private static DateTime? GetModifiedDate(IArchiveEntry archiveEntry) =>
        archiveEntry.LastModifiedTime is { } modifiedDate
            ? modifiedDate.Kind == DateTimeKind.Utc ? modifiedDate : modifiedDate.ToUniversalTime()
            : null;

    /// <summary>
    /// Opens a file for reading, decompressing it as it's read (synchronously and checking its size and CRC, see
    /// <see cref="ArchiveEntryStream" />).
    /// Files of solid archives are read with the <see cref="SolidArchiveReader" />, on the thread pool as it may
    /// decompress the files before it.
    /// </summary>
    protected override async Task<Stream> OpenArchiveEntry(string name, IArchiveEntry archiveEntry,
        CancellationToken cancellationToken)
    {
        if (archiveEntry.IsEncrypted)
        {
            throw new NotSupportedException($"File '{name}' is encrypted");
        }

        // only the opened volume of a multi-volume archive is read
        if (archiveEntry.IsSplitAfter)
        {
            throw new NotSupportedException($"File '{name}' continues in the next volume of the archive");
        }

        var stream = await Task.Run(() =>
        {
            try
            {
                return _solidArchiveReader != null
                    ? _solidArchiveReader.Open(archiveEntry, cancellationToken)
                    : archiveEntry.OpenEntryStream();
            }
            catch (Exception e) when (ArchiveEntryStream.IsDamage(e))
            {
                // e.g. a file before it in its solid block can't be decompressed
                throw ArchiveEntryStream.CreateDamagedException(name, e);
            }
        }, cancellationToken);
        return new ArchiveEntryStream(stream, name, archiveEntry.Size, archiveEntry.Crc);
    }

    public void Dispose()
    {
        _solidArchiveReader?.Dispose();
        _archive.Dispose();
        GC.SuppressFinalize(this);
    }
}
