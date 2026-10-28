namespace Hst.Commander.Core.Volumes;

/// <summary>
/// Progress of a delete reported by <see cref="VolumeDelete.DeleteAsync" />.
/// </summary>
/// <param name="CurrentName">Name of the entry being deleted.</param>
/// <param name="DeletedEntries">Number of entries deleted, items and containers.</param>
/// <param name="TotalEntries">Total number of entries to delete, items and containers.</param>
/// <param name="Eta">Estimated time left from the average delete speed since the delete started, null when unknown.</param>
public record DeleteProgress(string CurrentName, int DeletedEntries, int TotalEntries, TimeSpan? Eta = null)
{
    /// <summary>
    /// Progress of writing the cached writes of a disk while deleting is waiting for it, null otherwise (see
    /// <see cref="DiskWriteProgress" />).
    /// </summary>
    public DiskWriteProgress? DiskWrite { get; init; }

    /// <summary>Fraction of the entries deleted, between 0 and 1.</summary>
    public double TotalFraction => TotalEntries <= 0 ? 1 : Math.Clamp((double)DeletedEntries / TotalEntries, 0, 1);

    /// <summary>Deleted and total entries, e.g. "3/10 entries".</summary>
    public string TotalText => $"{DeletedEntries}/{TotalEntries} entries";

    /// <summary>Estimated time left as "h:mm:ss" or "--:--:--" when unknown.</summary>
    public string EtaText => CopyProgress.FormatEta(Eta);
}
