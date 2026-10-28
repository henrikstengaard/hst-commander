using System.Globalization;

namespace Hst.Commander.Core.Volumes;

/// <summary>
/// Progress of a copy reported by <see cref="VolumeCopy.CopyToAsync" />.
/// </summary>
/// <param name="CurrentName">Name of the item being copied.</param>
/// <param name="CurrentBytes">Bytes copied of the current item.</param>
/// <param name="CurrentSize">Size of the current item in bytes.</param>
/// <param name="CopiedBytes">Bytes copied of all items.</param>
/// <param name="TotalBytes">Total size of all items in bytes.</param>
/// <param name="CopiedItems">Number of items copied.</param>
/// <param name="TotalItems">Total number of items to copy.</param>
/// <param name="BytesPerSecond">Average copy speed since the copy started.</param>
/// <param name="Eta">Estimated time left, null when unknown.</param>
public record CopyProgress(
    string CurrentName,
    long CurrentBytes,
    long CurrentSize,
    long CopiedBytes,
    long TotalBytes,
    int CopiedItems,
    int TotalItems,
    double BytesPerSecond,
    TimeSpan? Eta)
{
    /// <summary>
    /// Progress of writing the cached writes of a disk while copying is waiting for it, null otherwise (see
    /// <see cref="DiskWriteProgress" />).
    /// </summary>
    public DiskWriteProgress? DiskWrite { get; init; }

    /// <summary>Fraction of the current item copied, between 0 and 1.</summary>
    public double CurrentFraction => GetFraction(CurrentBytes, CurrentSize);

    /// <summary>Fraction of all items copied, between 0 and 1.</summary>
    public double TotalFraction => GetFraction(CopiedBytes, TotalBytes);

    /// <summary>Copied and total size with copied and total items, e.g. "12M/40M, 3/10 files".</summary>
    public string TotalText =>
        $"{SizeFormatter.FormatHumanized(CopiedBytes)}/{SizeFormatter.FormatHumanized(TotalBytes)}, " +
        $"{CopiedItems}/{TotalItems} files";

    /// <summary>Copy speed, e.g. "12.3 MB/s".</summary>
    public string SpeedText => SizeFormatter.FormatSpeed(BytesPerSecond);

    /// <summary>Estimated time left as "h:mm:ss" or "--:--:--" when unknown.</summary>
    public string EtaText => FormatEta(Eta);

    /// <summary>Formats an estimated time left as "h:mm:ss" or "--:--:--" when unknown.</summary>
    internal static string FormatEta(TimeSpan? eta) => eta == null
        ? "--:--:--"
        : string.Create(CultureInfo.InvariantCulture,
            $"{(int)eta.Value.TotalHours}:{eta.Value.Minutes:00}:{eta.Value.Seconds:00}");

    private static double GetFraction(long bytes, long size) =>
        size <= 0 ? 1 : Math.Clamp((double)bytes / size, 0, 1);
}
