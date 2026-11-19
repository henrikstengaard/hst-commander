using System.Globalization;
using Hst.Commander.Abstractions;

namespace Hst.Commander.Plugins.Disk;

/// <summary>
/// Volume creator creating an exFAT file system without a partition table as a file, opened by
/// <see cref="ExFatFileVolumeProvider" />, formatted by <see cref="ExFatFormatter" />. The file is a sparse file where
/// supported, as only the parts of the boot regions, the allocation table, the allocation bitmap, the up-case table and
/// the root directory that aren't zeros are written, so a large volume takes little disk space until files are added.
/// </summary>
public class ExFatVolumeCreator : IVolumeCreator
{
    public const string SizeField = "Size";
    public const string VolumeLabelField = "VolumeLabel";
    public const string ClusterSizeField = "ClusterSize";
    public const string DefaultClusterSize = "Default";

    /// <summary>Sector size of the file system.</summary>
    public const int SectorSize = 512;

    /// <summary>Smallest size of an exFAT volume, 1 MB.</summary>
    public const long MinSize = ExFatFormatter.MinVolumeSize;

    private const long DefaultSize = 1L << 30;
    private const int MaxVolumeLabelLength = ExFatFormatter.MaxVolumeLabelLength;

    private static readonly VolumeCreatorChoice[] ClusterSizes =
    [
        new(DefaultClusterSize, "Default"),
        ..Enumerable.Range(12, 14).Select(shift => new VolumeCreatorChoice(
            (1L << shift).ToString(CultureInfo.InvariantCulture), FormatClusterSize(1L << shift)))
    ];

    public string Name => "exFAT file system";

    public string Description => "exFAT file system without a partition table, as a sparse file";

    public string FileExtension => ".img";

    public bool CreatesSparseFile => true;

    public IReadOnlyList<VolumeCreatorField> Fields { get; } =
    [
        new(SizeField, "Size", VolumeCreatorFieldType.Size, true, DefaultSize),
        new(VolumeLabelField, "Volume label", VolumeCreatorFieldType.Text),
        new(ClusterSizeField, "Cluster size", VolumeCreatorFieldType.Choice, false, DefaultClusterSize, ClusterSizes)
    ];

    public async Task Create(Stream stream, VolumeCreatorValues values, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(values);

        // whole sectors, e.g. 1.3 GB is rounded down
        var size = (values.GetNumber(SizeField) ?? DefaultSize) / SectorSize * SectorSize;
        if (size < MinSize)
        {
            throw new IOException("Size must be at least 1 MB");
        }

        var volumeLabel = values.GetText(VolumeLabelField);
        ValidateVolumeLabel(volumeLabel);

        var clusterSize = GetClusterSize(values.GetText(ClusterSizeField), size);
        try
        {
            // checked before the file gets its length
            ExFatFormatter.CreateLayout(size, SectorSize, (int)clusterSize);
        }
        catch (ArgumentOutOfRangeException exception)
        {
            throw new IOException(GetMessage(exception), exception);
        }

        cancellationToken.ThrowIfCancellationRequested();

        // the length is set without writing and blocks of zeros aren't written, so a sparse file stays small
        stream.SetLength(size);
        await ExFatFormatter.FormatPartition(stream, 0, size, SectorSize, volumeLabel, (int)clusterSize,
            skipZeroBlocks: true);
        await stream.FlushAsync(cancellationToken);
    }

    private static long GetClusterSize(string clusterSize, long size)
    {
        if (clusterSize.Length == 0 || clusterSize == DefaultClusterSize)
        {
            // Windows' default: 4 KB up to 256 MB, 32 KB up to 32 GB and 128 KB above
            return ExFatFormatter.CalculateClusterSize(size);
        }

        return ClusterSizes.Any(choice => choice.Value == clusterSize) &&
               long.TryParse(clusterSize, NumberStyles.None, CultureInfo.InvariantCulture, out var bytes)
            ? bytes
            : throw new IOException($"Invalid cluster size '{clusterSize}'");
    }

    /// <summary>Message of an exception without the parameter name added by <see cref="ArgumentException" />.</summary>
    private static string GetMessage(ArgumentException exception) =>
        exception.ParamName is { } paramName
            ? exception.Message.Replace($" (Parameter '{paramName}')", string.Empty, StringComparison.Ordinal)
            : exception.Message;

    private static void ValidateVolumeLabel(string volumeLabel)
    {
        if (volumeLabel.Length > MaxVolumeLabelLength)
        {
            throw new IOException(
                $"Volume label '{volumeLabel}' is longer than the maximum of {MaxVolumeLabelLength} characters");
        }

        if (volumeLabel.Any(char.IsControl))
        {
            throw new IOException($"Invalid volume label '{volumeLabel}', it can't contain control characters");
        }
    }

    private static string FormatClusterSize(long bytes) => bytes >= 1L << 20
        ? string.Create(CultureInfo.InvariantCulture, $"{bytes >> 20} MB")
        : string.Create(CultureInfo.InvariantCulture, $"{bytes >> 10} KB");
}
