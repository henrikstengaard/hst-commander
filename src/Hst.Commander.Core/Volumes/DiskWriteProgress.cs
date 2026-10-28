namespace Hst.Commander.Core.Volumes;

/// <summary>
/// Progress of writing the cached writes of a disk (see <see cref="Hst.Commander.Abstractions.IWriteCache" />), e.g.
/// of a physical disk at the end of a copy, shown in the progress dialogs with <see cref="CopyProgress.DiskWrite" />
/// and <see cref="DeleteProgress.DiskWrite" />.
/// </summary>
/// <param name="WrittenBytes">Bytes of the cached writes written so far.</param>
/// <param name="TotalBytes">Bytes of the cached writes when writing started.</param>
/// <param name="BytesPerSecond">Average write speed since writing started.</param>
/// <param name="Eta">Estimated time left of writing, null when unknown.</param>
public record DiskWriteProgress(long WrittenBytes, long TotalBytes, double BytesPerSecond = 0, TimeSpan? Eta = null)
{
    /// <summary>Fraction of the cached writes written, between 0 and 1.</summary>
    public double Fraction => TotalBytes <= 0 ? 1 : Math.Clamp((double)WrittenBytes / TotalBytes, 0, 1);

    /// <summary>Written and total size, e.g. "Writing to disk 12M/16M".</summary>
    public string Text =>
        $"Writing to disk {SizeFormatter.FormatHumanized(WrittenBytes)}/{SizeFormatter.FormatHumanized(TotalBytes)}";

    /// <summary>Write speed, e.g. "12.3 MB/s".</summary>
    public string SpeedText => SizeFormatter.FormatSpeed(BytesPerSecond);

    /// <summary>Estimated time left as "h:mm:ss" or "--:--:--" when unknown.</summary>
    public string EtaText => CopyProgress.FormatEta(Eta);
}
