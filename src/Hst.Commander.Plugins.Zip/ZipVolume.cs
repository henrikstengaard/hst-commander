using System.IO.Compression;
using Hst.Commander.Abstractions;

namespace Hst.Commander.Plugins.Zip;

/// <summary>
/// Read-only volume browsing the entries of a zip archive. Directories are the directory entries of the archive and the
/// directories in the paths of its files, as archives don't need to contain entries for directories.
/// </summary>
public class ZipVolume : ArchiveVolume<ZipArchiveEntry>, IDisposable
{
    private readonly ZipArchive _zipArchive;

    /// <param name="zipArchive">Archive opened for reading, disposed with the volume.</param>
    public ZipVolume(ZipArchive zipArchive) : base("Zip")
    {
        ArgumentNullException.ThrowIfNull(zipArchive);
        _zipArchive = zipArchive;

        foreach (var zipArchiveEntry in zipArchive.Entries)
        {
            AddEntry(zipArchiveEntry.FullName, zipArchiveEntry.Length, GetModifiedDate(zipArchiveEntry),
                zipArchiveEntry);
        }
    }

    /// <summary>Modified date of an archive entry, stored in local time without a time zone in zip archives.</summary>
    private static DateTime GetModifiedDate(ZipArchiveEntry zipArchiveEntry) =>
        zipArchiveEntry.LastWriteTime.UtcDateTime;

    protected override Task<Stream> OpenArchiveEntry(string name, ZipArchiveEntry archiveEntry,
        CancellationToken cancellationToken) =>
        Task.FromResult(archiveEntry.Open());

    public void Dispose()
    {
        _zipArchive.Dispose();
        GC.SuppressFinalize(this);
    }
}
