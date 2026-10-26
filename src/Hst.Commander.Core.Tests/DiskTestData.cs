using DiscUtils;
using DiscUtils.Fat;
using DiscUtils.Ntfs;
using DiscUtils.Partitions;
using DiscUtils.Streams;
using Hst.Commander.Plugins.Disk;

namespace Hst.Commander.Core.Tests;

/// <summary>
/// Creates disk images with a master boot record (MBR) or a GUID partition table (GPT) for tests: FAT and NTFS
/// partitions, PiStorm partitions with a rigid disk block and hybrid disks with both an MBR and a rigid disk block.
/// </summary>
public static class DiskTestData
{
    private const int SectorSize = 512;
    private const long DiskSize = 48 * 1024 * 1024;
    private const int FirstSector = 2048;
    private const int FatSectorCount = 16 * 1024 * 1024 / SectorSize;
    private const long HybridDiskSize = 20 * 1024 * 1024;
    private const int HybridFatFirstSector = 12 * 1024 * 1024 / SectorSize;
    private const int HybridFatSectorCount = 8 * 1024 * 1024 / SectorSize;

    public const int LargeSectorPartitionSize = 160 * 1024 * 1024;
    public const string FatLabel = "FATPART";
    public const string NtfsLabel = "NTFSPART";
    public const string GptFatName = "EFI boot";

    /// <summary>Creates an MBR disk with a FAT partition.</summary>
    public static Stream CreateMbrDisk()
    {
        var stream = CreateStream(DiskSize);
        var geometry = Geometry.FromCapacity(DiskSize);
        var partitionTable = BiosPartitionTable.Initialize(stream, geometry);
        partitionTable.CreatePrimaryBySector(FirstSector, FirstSector + FatSectorCount - 1, BiosPartitionTypes.Fat16,
            true);
        FormatFat(stream, geometry, FirstSector, FatSectorCount);
        stream.Position = 0;
        return stream;
    }

    /// <summary>
    /// Creates a disk with sectors of 4096 bytes (4Kn) and a GPT or an MBR with a FAT partition of 160 MB starting at
    /// sector 256 (1 MB), large enough for the FAT formatter counting sectors.
    /// </summary>
    public static Stream CreateLargeSectorDisk(bool gpt)
    {
        const int largeSectorSize = 4096;
        const int firstSector = 256;
        const int sectorCount = LargeSectorPartitionSize / largeSectorSize;
        const long diskSize = LargeSectorPartitionSize + 2 * 1024 * 1024;
        var stream = CreateStream(diskSize);
        var geometry = Geometry.FromCapacity(diskSize, largeSectorSize);
        if (gpt)
        {
            GuidPartitionTable.Initialize(stream, geometry).Create(firstSector, firstSector + sectorCount - 1,
                GuidPartitionTypes.WindowsBasicData, 0, GptFatName);
        }
        else
        {
            BiosPartitionTable.Initialize(stream, geometry).CreatePrimaryBySector(firstSector,
                firstSector + sectorCount - 1, BiosPartitionTypes.Fat16, true);
        }

        FormatFat(stream, geometry, firstSector, sectorCount);
        stream.Position = 0;
        return stream;
    }

    /// <summary>
    /// Creates an MBR disk with sectors of 512 bytes and a small FAT partition starting at sector 64, which would also
    /// fit on the disk with sectors of 4096 bytes.
    /// </summary>
    public static Stream CreateSmallPartitionMbrDisk()
    {
        const int firstSector = 64;
        const int sectorCount = 8 * 1024 * 1024 / SectorSize;
        var stream = CreateStream(DiskSize);
        var geometry = Geometry.FromCapacity(DiskSize);
        BiosPartitionTable.Initialize(stream, geometry).CreatePrimaryBySector(firstSector,
            firstSector + sectorCount - 1, BiosPartitionTypes.Fat16, true);
        FormatFat(stream, geometry, firstSector, sectorCount);
        stream.Position = 0;
        return stream;
    }

    /// <summary>Creates a GPT disk with a FAT partition named <see cref="GptFatName" /> and an NTFS partition.</summary>
    public static Stream CreateGptDisk()
    {
        var stream = CreateStream(DiskSize);
        var geometry = Geometry.FromCapacity(DiskSize);
        var partitionTable = GuidPartitionTable.Initialize(stream, geometry);
        var ntfsFirstSector = FirstSector + FatSectorCount;
        var ntfsSectorCount = (int)(partitionTable.LastUsableSector - ntfsFirstSector + 1);
        partitionTable.Create(FirstSector, FirstSector + FatSectorCount - 1, GuidPartitionTypes.WindowsBasicData, 0,
            GptFatName);
        partitionTable.Create(ntfsFirstSector, ntfsFirstSector + ntfsSectorCount - 1,
            GuidPartitionTypes.WindowsBasicData, 0, "Data");
        FormatFat(stream, geometry, FirstSector, FatSectorCount);
        using (var partitionStream = OpenPartition(stream, ntfsFirstSector, ntfsSectorCount))
        {
            NtfsFileSystem.Format(partitionStream, NtfsLabel, geometry, ntfsFirstSector, ntfsSectorCount).Dispose();
        }
        stream.Position = 0;
        return stream;
    }

    /// <summary>
    /// Creates a PiStorm MBR disk with a FAT partition and a partition of type 0x76 holding a rigid disk block with
    /// partitions of the dos types (see <see cref="AmigaTestData.WriteRdbToStream" />).
    /// </summary>
    public static async Task<Stream> CreatePiStormDisk(params byte[][] dosTypes)
    {
        var stream = CreateStream(DiskSize);
        var geometry = Geometry.FromCapacity(DiskSize);
        var partitionTable = BiosPartitionTable.Initialize(stream, geometry);
        var rdbFirstSector = FirstSector + FatSectorCount;
        var rdbLastSector = DiskSize / SectorSize - 1;
        partitionTable.CreatePrimaryBySector(FirstSector, FirstSector + FatSectorCount - 1, BiosPartitionTypes.Fat16,
            true);
        partitionTable.CreatePrimaryBySector(rdbFirstSector, rdbLastSector, PartitionTableVolume.PiStormRdbType,
            false);
        FormatFat(stream, geometry, FirstSector, FatSectorCount);
        await WriteRdb(stream, rdbFirstSector, rdbLastSector, dosTypes);
        return stream;
    }

    /// <summary>
    /// Creates a PiStorm GPT disk with a FAT partition and a partition of type
    /// <see cref="PartitionTableVolume.PiStormRdbGuidType" /> holding a rigid disk block with partitions of the dos
    /// types (see <see cref="AmigaTestData.WriteRdbToStream" />).
    /// </summary>
    public static async Task<Stream> CreateGptPiStormDisk(params byte[][] dosTypes)
    {
        var stream = CreateStream(DiskSize);
        var geometry = Geometry.FromCapacity(DiskSize);
        var partitionTable = GuidPartitionTable.Initialize(stream, geometry);
        var rdbFirstSector = FirstSector + FatSectorCount;
        var rdbLastSector = partitionTable.LastUsableSector;
        partitionTable.Create(FirstSector, FirstSector + FatSectorCount - 1, GuidPartitionTypes.WindowsBasicData, 0,
            GptFatName);
        partitionTable.Create(rdbFirstSector, rdbLastSector, PartitionTableVolume.PiStormRdbGuidType, 0, "Amiga");
        FormatFat(stream, geometry, FirstSector, FatSectorCount);
        await WriteRdb(stream, rdbFirstSector, rdbLastSector, dosTypes);
        return stream;
    }

    /// <summary>Writes a rigid disk block with partitions of the dos types into the sectors of a partition.</summary>
    private static async Task WriteRdb(Stream stream, long firstSector, long lastSector, byte[][] dosTypes)
    {
        await using (var rdbStream = new SubStream(stream, Ownership.None, firstSector * SectorSize,
                         (lastSector - firstSector + 1) * SectorSize))
        {
            await AmigaTestData.WriteRdbToStream(rdbStream, dosTypes);
        }

        stream.Position = 0;
    }

    /// <summary>
    /// Creates a hybrid disk: a rigid disk block with partitions of the dos types (see
    /// <see cref="AmigaTestData.CreateRdbImage" />) moved to block 2 and an MBR in block 0 with a FAT partition after
    /// the Amiga partitions.
    /// </summary>
    public static async Task CreateHybridDisk(string path, params byte[][] dosTypes)
    {
        await AmigaTestData.CreateRdbImage(path, dosTypes);

        await using var stream = new FileStream(path, FileMode.Open, FileAccess.ReadWrite);
        stream.SetLength(HybridDiskSize);

        // the rigid disk block is found in the first 16 blocks, block 0 is the MBR
        var rdbBlock = new byte[SectorSize];
        await stream.ReadExactlyAsync(rdbBlock);
        stream.Position = 2 * SectorSize;
        await stream.WriteAsync(rdbBlock);
        stream.Position = 0;
        await stream.WriteAsync(new byte[SectorSize]);

        var geometry = Geometry.FromCapacity(HybridDiskSize);
        var partitionTable = BiosPartitionTable.Initialize(stream, geometry);
        partitionTable.CreatePrimaryBySector(HybridFatFirstSector, HybridFatFirstSector + HybridFatSectorCount - 1,
            BiosPartitionTypes.Fat16, false);
        FormatFat(stream, geometry, HybridFatFirstSector, HybridFatSectorCount);
    }

    /// <summary>Writes a stream to a file, e.g. a disk created in memory.</summary>
    public static async Task WriteToFile(Stream stream, string path)
    {
        await using var fileStream = File.Create(path);
        stream.Position = 0;
        await stream.CopyToAsync(fileStream);
    }

    /// <summary>Formats a partition with FAT and the label <see cref="FatLabel" />.</summary>
    private static void FormatFat(Stream stream, Geometry geometry, int firstSector, int sectorCount)
    {
        using var partitionStream = OpenPartition(stream, firstSector, sectorCount, geometry.BytesPerSector);
        FatFileSystem.FormatPartition(partitionStream, FatLabel, geometry, firstSector, sectorCount, 0).Dispose();
    }

    private static SubStream OpenPartition(Stream stream, long firstSector, long sectorCount,
        int sectorSize = SectorSize) =>
        new(stream, Ownership.None, firstSector * sectorSize, sectorCount * sectorSize);

    private static MemoryStream CreateStream(long size)
    {
        var stream = new MemoryStream();
        stream.SetLength(size);
        return stream;
    }
}
