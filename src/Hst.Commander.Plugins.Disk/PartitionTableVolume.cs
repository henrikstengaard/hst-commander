using System.Globalization;
using DiscUtils.Fat;
using DiscUtils.Ntfs;
using DiscUtils.Partitions;
using Hst.Commander.Abstractions;

namespace Hst.Commander.Plugins.Disk;

/// <summary>
/// Volume browsing the partitions of a master boot record (MBR) or a GUID partition table (GPT). The root lists the
/// partition table as a directory ("mbr" or "gpt", shown as "MBR" or "GPT"), listing the partitions as directories
/// named by their partition number (1, 2, ...), in the order of the partition table, so paths show the partition
/// table, e.g. "mbr\1". The partitions are shown with their number and volume label when they have one, or
/// "PiStorm RDB" for PiStorm partitions (MBR type 0x76 or GPT type 3F82EEBC-87C9-4097-8165-89D6540557C0, e.g. "1 - EMU68BOOT" and "2 - PiStorm RDB", see
/// <see cref="IEntry.DisplayName" />) and their partition details in the properties dialog
/// (<see cref="IEntry.Details" />). Partitions with FAT or NTFS are mounted with
/// DiscUtils, other partitions with the file volume providers of the host, e.g. a PiStorm partition with a rigid disk
/// block by the Amiga plugin. The partitions can't be changed, their file systems are writable when the stream is.
/// </summary>
public sealed class PartitionTableVolume : VolumeListVolume
{
    /// <summary>MBR partition type of PiStorm partitions holding a rigid disk block.</summary>
    public const byte PiStormRdbType = 0x76;

    /// <summary>GPT partition type of PiStorm partitions holding a rigid disk block.</summary>
    public static readonly Guid PiStormRdbGuidType = new("3F82EEBC-87C9-4097-8165-89D6540557C0");

    private const string PiStormRdbName = "PiStorm RDB";

    private readonly IReadOnlyList<PartitionInfo> _partitions;
    private readonly IPluginHost? _host;
    private readonly IDisposable? _resource;
    private readonly int _sectorSize;
    private readonly Dictionary<int, Stream> _partitionStreams = new();

    /// <param name="name">Name of the partition table, "MBR" or "GPT".</param>
    /// <param name="partitions">Partitions of the partition table, at least one.</param>
    /// <param name="host">Host opening partitions not mounted by DiscUtils, null to only mount FAT and NTFS.</param>
    /// <param name="resource">Resource disposed with the volume after the partitions, e.g. a virtual disk.</param>
    /// <param name="sectorSize">Sector size of the disk in bytes, 512 or 4096.</param>
    public PartitionTableVolume(string name, IReadOnlyList<PartitionInfo> partitions, IPluginHost? host = null,
        IDisposable? resource = null, int sectorSize = 512)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(partitions);

        Name = name;
        _partitions = partitions;
        _host = host;
        _resource = resource;
        _sectorSize = sectorSize;
    }

    public override string Name { get; }

    protected override int Count => _partitions.Count;

    protected override string ReadOnlyMessage =>
        "The partition list is read-only, open a partition to change its files";

    /// <summary>Name of a partition: the partition number, e.g. "1".</summary>
    protected override string GetEntryName(int index) => (index + 1).ToString(CultureInfo.InvariantCulture);

    protected override IEntry CreateListEntry() => new PartitionTableEntry(ListName, Name, new EntryDetails("Partition table",
    [
        [
            new EntryProperty("Partition table", Name == "GPT" ? "GUID partition table (GPT)" : "Master boot record (MBR)"),
            new EntryProperty("Partitions", _partitions.Count.ToString(CultureInfo.InvariantCulture)),
            new EntryProperty("Sector size", FormatSectorSize())
        ]
    ]));

    private string FormatSectorSize() => string.Create(CultureInfo.InvariantCulture, $"{_sectorSize} bytes");

    protected override async Task<IVolume> Mount(int index, CancellationToken cancellationToken)
    {
        var name = GetEntryName(index);
        var partition = _partitions[index];
        Stream stream;
        try
        {
            stream = partition.Open();
        }
        catch (Exception exception) when (exception is not IOException)
        {
            throw new IOException($"Partition '{name}' can't be opened: {exception.Message}", exception);
        }

        try
        {
            var volume = MountFileSystem(name, stream) ??
                         (_host != null ? await _host.OpenVolume(name, stream, null, cancellationToken) : null) ??
                         throw new IOException(
                             $"Partition '{name}' ({GetTypeName(partition)}) can't be mounted, its file system is not supported");
            _partitionStreams[index] = stream;
            return volume;
        }
        catch (Exception exception)
        {
            await stream.DisposeAsync();
            if (exception is IOException or OperationCanceledException)
            {
                throw;
            }

            throw new IOException($"Partition '{name}' can't be mounted: {exception.Message}", exception);
        }
    }

    /// <summary>Mounts the file systems read by DiscUtils, FAT and NTFS; null for other file systems.</summary>
    private static DiscFileSystemVolume? MountFileSystem(string name, Stream stream)
    {
        stream.Position = 0;
        if (FatFileSystem.Detect(stream))
        {
            stream.Position = 0;
            return new DiscFileSystemVolume(name, new FatFileSystem(stream));
        }

        stream.Position = 0;
        if (NtfsFileSystem.Detect(stream))
        {
            stream.Position = 0;
            return new DiscFileSystemVolume(name, new NtfsFileSystem(stream));
        }

        stream.Position = 0;
        return null;
    }

    protected override IEntry CreateEntry(int index, IVolume? volume)
    {
        var name = GetEntryName(index);
        var partition = _partitions[index];
        var gptName = (partition as GuidPartitionInfo)?.Name.Trim();
        var volumeLabel = (volume as DiscFileSystemVolume)?.VolumeLabel.Trim();

        var label = !string.IsNullOrEmpty(volumeLabel) ? volumeLabel
            : IsPiStormRdb(partition) ? PiStormRdbName
            : null;
        var displayName = label != null ? $"{name} - {label}" : name;
        return new PartitionEntry(name, displayName, partition.SectorCount * _sectorSize,
            CreateDetails(index, gptName, volumeLabel, volume));
    }

    private EntryDetails CreateDetails(int index, string? gptName, string? volumeLabel, IVolume? volume)
    {
        var partition = _partitions[index];
        var partitionSection = new List<EntryProperty>
        {
            new("Partition", GetEntryName(index)),
            new("Partition table", Name),
            new("Partition type", FormatType(partition))
        };

        if (!string.IsNullOrEmpty(gptName))
        {
            partitionSection.Add(new EntryProperty("Name", gptName));
        }

        if (!string.IsNullOrEmpty(volumeLabel))
        {
            partitionSection.Add(new EntryProperty("Volume name", volumeLabel));
        }

        if (volume != null)
        {
            partitionSection.Add(new EntryProperty("File system",
                volume is DiscFileSystemVolume fileSystemVolume ? fileSystemVolume.FileSystemName : volume.Name));
        }

        var locationSection = new List<EntryProperty>();
        switch (partition)
        {
            case BiosPartitionInfo biosPartition:
                locationSection.Add(new EntryProperty("Active", biosPartition.IsActive ? "Yes" : "No"));
                locationSection.Add(new EntryProperty("Primary", biosPartition.IsPrimary ? "Yes" : "No"));
                break;
            case GuidPartitionInfo guidPartition:
                locationSection.Add(new EntryProperty("Partition id", guidPartition.Identity.ToString()));
                break;
        }

        locationSection.Add(new EntryProperty("Sectors", string.Create(CultureInfo.InvariantCulture,
            $"{partition.FirstSector} - {partition.LastSector}")));
        locationSection.Add(new EntryProperty("Sector size", FormatSectorSize()));
        locationSection.Add(EntryProperty.FromBytes("Partition size", partition.SectorCount * _sectorSize));

        var typeName = $"{Name} partition";
        if (volume is not DiscFileSystemVolume diskVolume)
        {
            return new EntryDetails(typeName, [partitionSection, locationSection]);
        }

        var capacity = diskVolume.Size;
        var free = Math.Clamp(diskVolume.Free, 0, capacity);
        List<EntryProperty> spaceSection =
        [
            EntryProperty.FromBytes("Used space", capacity - free),
            EntryProperty.FromBytes("Free space", free),
            EntryProperty.FromBytes("Capacity", capacity)
        ];
        return new EntryDetails(typeName, [partitionSection, locationSection, spaceSection],
            capacity > 0 ? (double)(capacity - free) / capacity : null);
    }

    private static bool IsPiStormRdb(PartitionInfo partition) =>
        partition is BiosPartitionInfo ? partition.BiosType == PiStormRdbType
        : partition is GuidPartitionInfo && partition.GuidType == PiStormRdbGuidType;

    /// <summary>Name of the type of a partition, e.g. "FAT32 (LBA)" or "PiStorm RDB".</summary>
    private static string GetTypeName(PartitionInfo partition)
    {
        if (IsPiStormRdb(partition))
        {
            return PiStormRdbName;
        }

        var typeName = partition.TypeAsString;
        return string.IsNullOrWhiteSpace(typeName) || typeName.Equals("Unknown", StringComparison.OrdinalIgnoreCase)
            ? FormatTypeId(partition)
            : typeName;
    }

    /// <summary>Type of a partition with its id, e.g. "FAT32 (LBA) (0x0C)".</summary>
    private static string FormatType(PartitionInfo partition)
    {
        var typeName = GetTypeName(partition);
        var typeId = FormatTypeId(partition);
        return typeName == typeId ? typeId : $"{typeName} ({typeId})";
    }

    private static string FormatTypeId(PartitionInfo partition) => partition is GuidPartitionInfo
        ? partition.GuidType.ToString()
        : string.Create(CultureInfo.InvariantCulture, $"0x{partition.BiosType:X2}");

    protected override void DisposeResources()
    {
        foreach (var stream in _partitionStreams.Values)
        {
            stream.Dispose();
        }

        _partitionStreams.Clear();
        _resource?.Dispose();
    }
}
