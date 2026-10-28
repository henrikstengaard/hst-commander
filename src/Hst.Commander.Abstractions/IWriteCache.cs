namespace Hst.Commander.Abstractions;

/// <summary>
/// Stream caching writes in memory until flushed, e.g. the stream of a physical disk returned by
/// <see cref="IVolume.OpenReadWrite" />. File systems write a change as several small writes, e.g. a directory, the
/// allocation table and the file's data, and the disk is only consistent when all are written. Hst Commander flushes
/// the stream of a writable image (see <see cref="IVolume.GetWritableImageId" />) with <see cref="Commit" /> when its
/// file system is consistent: between operations with no file open for writing, when the cache asks for it with
/// <see cref="ShouldFlush" /> or when no operation has been done for a moment. <see cref="Stream.Flush" /> doesn't
/// write the cached writes, as file systems flush their stream after each change (e.g. PFS3), so the cache would be
/// written after each file. The stream writes the cached writes itself only when its cache is full and when it's
/// disposed.
/// </summary>
public interface IWriteCache
{
    /// <summary>True when the cache holds writes not written yet.</summary>
    bool HasCachedWrites { get; }

    /// <summary>True when the cache is filling up with writes and should be flushed at the next consistent point.</summary>
    bool ShouldFlush { get; }

    /// <summary>Writes the cached writes and flushes them to the disk, called when the file system is consistent.</summary>
    void Commit();
}
