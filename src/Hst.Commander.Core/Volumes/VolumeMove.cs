using Hst.Commander.Extensions.Volume.Abstractions;

namespace Hst.Commander.Core.Volumes;

/// <summary>
/// Moves entries from one volume to another by copying them with <see cref="VolumeCopy" /> and deleting them from
/// the source, so any writable volume can be moved to any writable volume.
/// </summary>
public static class VolumeMove
{
    /// <summary>
    /// Moves entries in the current directory of the source volume to the current directory of the destination
    /// volume. Entries are copied like <see cref="VolumeCopy.CopyToAsync" /> (same options and progress) and each item
    /// is deleted from the source right after it is copied. Containers are deleted from the source after everything
    /// is copied when they are empty, so items not overwritten or not matching the filter are kept in the source with
    /// their containers. Both volumes are changed back to their current directories when the move is done.
    /// </summary>
    /// <param name="source">Volume to move entries from.</param>
    /// <param name="entries">Entries in the current directory of the source volume to move.</param>
    /// <param name="destination">Volume to move entries to.</param>
    /// <param name="options">Copy options, e.g. the filter of items to move.</param>
    /// <param name="progress">Receives the progress, at most every 100 ms and when an item is moved.</param>
    /// <param name="cancellationToken"></param>
    public static Task MoveToAsync(this IVolume source, IEnumerable<IEntry> entries, IVolume destination,
        CopyOptions? options = null, IProgress<CopyProgress>? progress = null,
        CancellationToken cancellationToken = default) =>
        VolumeCopy.TransferAsync(source, entries, destination, options, progress, deleteSource: true,
            cancellationToken);
}
