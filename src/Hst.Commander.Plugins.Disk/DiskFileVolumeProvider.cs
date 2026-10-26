using System.Buffers.Binary;
using DiscUtils;
using DiscUtils.Partitions;
using DiscUtils.Streams;
using Hst.Commander.Abstractions;
using VhdDisk = DiscUtils.Vhd.Disk;

namespace Hst.Commander.Plugins.Disk;

/// <summary>
/// File volume provider opening disk images with a master boot record (MBR) or a GUID partition table (GPT) as
/// writable volumes listing their partitions (see <see cref="PartitionTableVolume" />). Detected by the signature
/// "EFI PART" in the second sector (GPT, the protective MBR is not shown) or a valid partition table in the first
/// sector ending with 0x55 0xAA (MBR); boot sectors of unpartitioned FAT, exFAT and NTFS volumes are not detected.
/// Disks with sectors of 512 bytes and of 4096 bytes (4Kn, e.g. images of some USB drives) are supported: GPT by where
/// its header is, MBR by where the partitions start (see <see cref="DetectMbrSectorSize" />). Raw disk images (e.g.
/// .img) and fixed size VHD images are read directly, dynamic and differencing VHD images (signature "conectix" at the
/// start) through their virtual disk. A disk where the host finds another volume on the whole disk too, e.g. an Amiga
/// rigid disk block after the MBR, is a hybrid disk (see <see cref="HybridDiskVolume" />).
/// </summary>
public class DiskFileVolumeProvider : IFileVolumeProvider
{
    private const int SectorSize = 512;
    private const int LargeSectorSize = 4096;
    private const int PartitionTableOffset = 0x1BE;
    private const int PartitionEntrySize = 16;
    private const int PartitionEntryCount = 4;
    private const int VolumeStartLength = 16 * SectorSize;
    private const int ExtSuperblockMagicOffset = 1024 + 56;
    private const string MbrName = "MBR";
    private const string GptName = "GPT";
    private static readonly byte[] GptSignature = "EFI PART"u8.ToArray();
    private static readonly byte[] RdbSignature = "RDSK"u8.ToArray();
    private static readonly byte[] VhdSignature = "conectix"u8.ToArray();

    /// <param name="host">Host opening partitions not mounted by the plugin and finding hybrid disks.</param>
    public DiskFileVolumeProvider(IPluginHost? host = null)
    {
        Host = host;
    }

    /// <summary>
    /// Host opening partitions not mounted by the plugin, e.g. PiStorm partitions with a rigid disk block, and finding
    /// other volumes on hybrid disks; null to only mount FAT and NTFS partitions.
    /// </summary>
    public IPluginHost? Host { get; set; }

    public string Name => "Disk";

    public string Description => "Disk images with master boot record or GUID partition table";

    public bool CanWrite => true;

    public async Task<bool> IsMatch(string name, Stream stream, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);

        var sectors = new byte[LargeSectorSize * 2];
        var length = await stream.ReadAtLeastAsync(sectors, sectors.Length, false, cancellationToken);
        if (GetPartitionTable(sectors.AsSpan(0, length)) != null)
        {
            return true;
        }

        // the disk of a dynamic vhd image is read through the virtual disk, which needs a seekable stream
        if (length < VhdSignature.Length || !sectors.AsSpan(0, VhdSignature.Length).SequenceEqual(VhdSignature) ||
            !stream.CanSeek)
        {
            return false;
        }

        try
        {
            stream.Position = 0;
            using var disk = new VhdDisk(stream, Ownership.None);
            var diskStream = disk.Content;
            diskStream.Position = 0;
            length = await diskStream.ReadAtLeastAsync(sectors, sectors.Length, false, cancellationToken);
            return GetPartitionTable(sectors.AsSpan(0, length)) != null;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return false;
        }
    }

    /// <summary>
    /// Partition table at the start of a disk: "GPT" with the sector size of its header (in the second sector of 512
    /// or 4096 bytes), or "MBR" (sector size unknown, see <see cref="DetectMbrSectorSize" />); null when there is none.
    /// </summary>
    private static (string Name, int? SectorSize)? GetPartitionTable(ReadOnlySpan<byte> sectors)
    {
        if (sectors.Length < SectorSize || sectors[510] != 0x55 || sectors[511] != 0xAA)
        {
            return null;
        }

        foreach (var sectorSize in (int[])[SectorSize, LargeSectorSize])
        {
            if (sectors.Length >= sectorSize + GptSignature.Length &&
                sectors.Slice(sectorSize, GptSignature.Length).SequenceEqual(GptSignature))
            {
                return (GptName, sectorSize);
            }
        }

        return HasMasterBootRecord(sectors) ? (MbrName, null) : null;
    }

    /// <summary>
    /// True when the first sector has a valid MBR partition table with at least one partition and isn't the boot
    /// sector of an unpartitioned FAT, exFAT or NTFS volume.
    /// </summary>
    private static bool HasMasterBootRecord(ReadOnlySpan<byte> sector)
    {
        if (IsBootSector(sector))
        {
            return false;
        }

        var partitionCount = 0;
        for (var index = 0; index < PartitionEntryCount; index++)
        {
            var entry = sector.Slice(PartitionTableOffset + index * PartitionEntrySize, PartitionEntrySize);
            if (entry[0] is not (0x00 or 0x80))
            {
                return false;
            }

            if (entry[4] == 0)
            {
                continue;
            }

            var firstSector = BinaryPrimitives.ReadUInt32LittleEndian(entry[8..]);
            var sectorCount = BinaryPrimitives.ReadUInt32LittleEndian(entry[12..]);
            if (firstSector == 0 || sectorCount == 0)
            {
                return false;
            }

            partitionCount++;
        }

        return partitionCount > 0;
    }

    /// <summary>First sector and number of sectors of the partitions in the MBR partition table.</summary>
    private static List<(long FirstSector, long SectorCount)> GetMbrPartitions(ReadOnlySpan<byte> sector)
    {
        var partitions = new List<(long, long)>();
        for (var index = 0; index < PartitionEntryCount; index++)
        {
            var entry = sector.Slice(PartitionTableOffset + index * PartitionEntrySize, PartitionEntrySize);
            if (entry[4] != 0)
            {
                partitions.Add((BinaryPrimitives.ReadUInt32LittleEndian(entry[8..]),
                    BinaryPrimitives.ReadUInt32LittleEndian(entry[12..])));
            }
        }

        return partitions;
    }

    private static bool IsBootSector(ReadOnlySpan<byte> sector) =>
        sector.Slice(3, 8).SequenceEqual("NTFS    "u8) || sector.Slice(3, 8).SequenceEqual("EXFAT   "u8) ||
        sector.Slice(0x36, 3).SequenceEqual("FAT"u8) || sector.Slice(0x52, 5).SequenceEqual("FAT32"u8);

    /// <summary>
    /// Sector size of an MBR disk, which the MBR doesn't store: 512 bytes, unless the partitions only fit on the disk
    /// with 4096 byte sectors, or they also fit with 4096 byte sectors and more of them start with a volume there (a
    /// boot sector, an extended boot record, an Amiga rigid disk block or an ext file system).
    /// </summary>
    private static async Task<int> DetectMbrSectorSize(Stream diskStream, byte[] masterBootRecord,
        CancellationToken cancellationToken)
    {
        var partitions = GetMbrPartitions(masterBootRecord);
        var diskSize = diskStream.Length;
        bool Fits(int sectorSize) => partitions.All(partition =>
            (partition.FirstSector + partition.SectorCount) * sectorSize <= diskSize);

        if (!Fits(LargeSectorSize))
        {
            return SectorSize;
        }

        if (!Fits(SectorSize))
        {
            return LargeSectorSize;
        }

        var volumeStarts = 0;
        var largeVolumeStarts = 0;
        foreach (var (firstSector, _) in partitions)
        {
            volumeStarts += await HasVolumeStart(diskStream, firstSector * SectorSize, cancellationToken) ? 1 : 0;
            largeVolumeStarts +=
                await HasVolumeStart(diskStream, firstSector * LargeSectorSize, cancellationToken) ? 1 : 0;
        }

        return largeVolumeStarts > volumeStarts ? LargeSectorSize : SectorSize;
    }

    /// <summary>
    /// True when a volume starts at an offset: a boot sector or extended boot record (0x55 0xAA at offset 510), an
    /// Amiga rigid disk block ("RDSK" in one of the first 16 blocks) or an ext file system (superblock magic).
    /// </summary>
    private static async Task<bool> HasVolumeStart(Stream diskStream, long offset,
        CancellationToken cancellationToken)
    {
        var blocks = new byte[VolumeStartLength];
        diskStream.Position = offset;
        var length = await diskStream.ReadAtLeastAsync(blocks, blocks.Length, false, cancellationToken);
        if (length >= SectorSize && blocks[510] == 0x55 && blocks[511] == 0xAA)
        {
            return true;
        }

        for (var blockOffset = 0; blockOffset + RdbSignature.Length <= length; blockOffset += SectorSize)
        {
            if (blocks.AsSpan(blockOffset, RdbSignature.Length).SequenceEqual(RdbSignature))
            {
                return true;
            }
        }

        return length >= ExtSuperblockMagicOffset + 2 && blocks[ExtSuperblockMagicOffset] == 0x53 &&
               blocks[ExtSuperblockMagicOffset + 1] == 0xEF;
    }

    /// <summary>
    /// Creates a volume listing the partitions, or the partition tables of a hybrid disk. Throws
    /// <see cref="IOException" /> when the partition table can't be read or has no partitions.
    /// </summary>
    public async Task<IVolume> CreateVolume(Stream stream, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);

        var header = new byte[VhdSignature.Length];
        var length = await stream.ReadAtLeastAsync(header, header.Length, false, cancellationToken);
        stream.Position = 0;

        var disk = length == header.Length && header.AsSpan().SequenceEqual(VhdSignature)
            ? new VhdDisk(stream, Ownership.None)
            : null;
        try
        {
            return await CreateDiskVolume(disk?.Content ?? stream, disk, cancellationToken);
        }
        catch
        {
            disk?.Dispose();
            throw;
        }
    }

    private async Task<IVolume> CreateDiskVolume(Stream diskStream, IDisposable? resource,
        CancellationToken cancellationToken)
    {
        var sectors = new byte[LargeSectorSize * 2];
        diskStream.Position = 0;
        var length = await diskStream.ReadAtLeastAsync(sectors, sectors.Length, false, cancellationToken);

        var (partitionTableName, gptSectorSize) = GetPartitionTable(sectors.AsSpan(0, length)) ??
                                                  throw new IOException(
                                                      "No master boot record or GUID partition table found");
        var sectorSize = gptSectorSize ?? await DetectMbrSectorSize(diskStream, sectors, cancellationToken);
        diskStream.Position = 0;

        List<PartitionInfo> partitions;
        try
        {
            var geometry = Geometry.FromCapacity(diskStream.Length, sectorSize);
            PartitionTable partitionTable = partitionTableName == GptName
                ? new GuidPartitionTable(diskStream, geometry)
                : new BiosPartitionTable(diskStream, geometry);
            partitions = partitionTable.Partitions.ToList();
        }
        catch (Exception exception) when (exception is not IOException)
        {
            throw new IOException($"The {partitionTableName} partition table can't be read: {exception.Message}",
                exception);
        }

        if (partitions.Count == 0)
        {
            throw new IOException($"The {partitionTableName} partition table has no partitions");
        }

        var otherVolume = await FindOtherVolume(diskStream, cancellationToken);
        if (otherVolume == null)
        {
            return new PartitionTableVolume(partitionTableName, partitions, Host, resource, sectorSize);
        }

        return new HybridDiskVolume(
            [new PartitionTableVolume(partitionTableName, partitions, Host, null, sectorSize), otherVolume], resource);
    }

    /// <summary>
    /// Another volume on the whole disk found by the host, e.g. an Amiga rigid disk block of a hybrid disk; null when
    /// there is none or it can't be opened.
    /// </summary>
    private async Task<IVolume?> FindOtherVolume(Stream diskStream, CancellationToken cancellationToken)
    {
        if (Host == null)
        {
            return null;
        }

        try
        {
            return await Host.OpenVolume("disk", diskStream, this, cancellationToken);
        }
        catch (IOException)
        {
            return null;
        }
        finally
        {
            diskStream.Position = 0;
        }
    }
}
