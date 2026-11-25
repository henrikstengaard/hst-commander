using System.IO.Compression;
using System.Text;
using DiscUtils;
using DiscUtils.ExFat;
using DiscUtils.ExFat.Internal;
using DiscUtils.Fat;
using DiscUtils.Ntfs;
using DiscUtils.Partitions;
using DiscUtils.SquashFs;
using DiscUtils.Streams;
using Hst.Commander.Plugins.Disk;

namespace Hst.Commander.Core.Tests;

/// <summary>
/// Creates disk images with a master boot record (MBR) or a GUID partition table (GPT) for tests: FAT, exFAT, NTFS and
/// ext4 partitions, PiStorm partitions with a rigid disk block and hybrid disks with both an MBR and a rigid disk block.
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
    public const string ExFatLabel = "EXFATPART";

    /// <summary>
    /// Volume label of the ext4 file system in TestData/Disks/ext4.img.gz, made with mke2fs 1.47.0 in Debian 12:
    /// "mkfs.ext4 -L EXT4PART -b 1024 -N 64 -E root_owner=0:0 -d src ext4.img 4M", with src holding file1.txt
    /// ("Hello ext4", modified 2024-05-06 07:08:10 UTC), File1.txt ("Case"), a:b?.txt ("colon") and dir1/file2.txt
    /// ("Nested").
    /// </summary>
    public const string ExtLabel = "EXT4PART";

    /// <summary>Modified date of file1.txt in the ext4 file system.</summary>
    public static readonly DateTime ExtFile1ModifiedDate = new(2024, 5, 6, 7, 8, 10, DateTimeKind.Utc);

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

    /// <summary>Creates an MBR disk with an exFAT partition (type 0x07) labelled <see cref="ExFatLabel" />.</summary>
    public static Stream CreateExFatDisk()
    {
        var stream = CreateStream(DiskSize);
        var geometry = Geometry.FromCapacity(DiskSize);
        BiosPartitionTable.Initialize(stream, geometry).CreatePrimaryBySector(FirstSector,
            FirstSector + FatSectorCount - 1, BiosPartitionTypes.Ntfs, true);
        var physicalVolume = new VolumeManager(stream).GetPhysicalVolumes().Single();
        ExFatFileSystem.Format(physicalVolume, new ExFatFormatOptions(), ExFatLabel).Dispose();
        stream.Position = 0;
        return stream;
    }

    /// <summary>
    /// Creates an MBR disk with a Linux partition (type 0x83) holding the ext4 file system of
    /// TestData/Disks/ext4.img.gz (see <see cref="ExtLabel" />).
    /// </summary>
    public static async Task<Stream> CreateExtDisk()
    {
        var stream = CreateStream(DiskSize);
        var geometry = Geometry.FromCapacity(DiskSize);
        BiosPartitionTable.Initialize(stream, geometry).CreatePrimaryBySector(FirstSector,
            FirstSector + FatSectorCount - 1, BiosPartitionTypes.LinuxNative, false);
        await using (var imageStream = new GZipStream(
                         File.OpenRead(Path.Combine(AppContext.BaseDirectory, "TestData", "Disks", "ext4.img.gz")),
                         CompressionMode.Decompress))
        await using (var partitionStream = OpenPartition(stream, FirstSector, FatSectorCount))
        {
            await imageStream.CopyToAsync(partitionStream);
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

    /// <summary>
    /// Creates a FAT file system without a partition table of 16 MB labelled <see cref="FatLabel" />, like an image
    /// of a partition.
    /// </summary>
    public static Stream CreateFatImage()
    {
        var stream = CreateStream(FatSectorCount * (long)SectorSize);
        FatFileSystem.FormatPartition(stream, FatLabel, Geometry.FromCapacity(stream.Length), 0, FatSectorCount, 0)
            .Dispose();
        stream.Position = 0;
        return stream;
    }

    /// <summary>Creates an NTFS file system without a partition table of 16 MB labelled <see cref="NtfsLabel" />.</summary>
    public static Stream CreateNtfsImage()
    {
        var stream = CreateStream(FatSectorCount * (long)SectorSize);
        NtfsFileSystem.Format(stream, NtfsLabel, Geometry.FromCapacity(stream.Length), 0, FatSectorCount).Dispose();
        stream.Position = 0;
        return stream;
    }

    /// <summary>
    /// Creates the ext4 file system of TestData/Disks/ext4.img.gz (see <see cref="ExtLabel" />) without a partition
    /// table.
    /// </summary>
    public static async Task<Stream> CreateExtImage()
    {
        var stream = new MemoryStream();
        await using (var imageStream = new GZipStream(
                         File.OpenRead(Path.Combine(AppContext.BaseDirectory, "TestData", "Disks", "ext4.img.gz")),
                         CompressionMode.Decompress))
        {
            await imageStream.CopyToAsync(stream);
        }

        stream.Position = 0;
        return stream;
    }

    /// <summary>
    /// Reads a gzipped image of TestData/Disks made with the real tools of each file system by
    /// TestData/Disks/make/make-images.sh (in Ubuntu 24.04), each file system holding hello.txt ("Hello from" its
    /// name, e.g. "Hello from xfs", with a line break) and dir/nested.txt ("Nested file", with a line break):
    /// xfs.img.gz (XFSTEST), btrfs.img.gz (BTRFSTEST, also with zlib/, lzo/ and zstd/ holding compressed.txt compressed
    /// with each), udf.img.gz (UDFTEST), hfsplus.img.gz (HFSTEST, empty, as nothing here can write HFS+),
    /// squashfs-gzip/-xz/-zstd/-lzo.img.gz, disk-mbr.img.gz (FATTEST, NTFSTEST, EXFATTEST and EXT4TEST partitions),
    /// disk-gpt.img.gz (FAT, NTFS, exFAT, ext4, XFS, HFS+ and SquashFS partitions) and the GPT disk as dynamic VHD,
    /// VHDX and VDI images made by qemu-img (disk-gpt.vhd.gz, .vhdx.gz, .vdi.gz) and as a zlib compressed DMG made by
    /// make/make-dmg.py (disk-gpt.dmg.gz). ext4.img.gz is older, see <see cref="ExtLabel" />.
    /// </summary>
    public static async Task<Stream> OpenGzippedImage(string fileName)
    {
        var stream = new MemoryStream();
        await using (var imageStream = new GZipStream(
                         File.OpenRead(Path.Combine(AppContext.BaseDirectory, "TestData", "Disks", fileName)),
                         CompressionMode.Decompress))
        {
            await imageStream.CopyToAsync(stream);
        }

        stream.Position = 0;
        return stream;
    }

    /// <summary>
    /// Creates a SquashFS file system holding hello.txt ("Hello from squashfs", with a line break) and dir/nested.txt
    /// ("Nested file", with a line break) like the images of <see cref="OpenGzippedImage" />.
    /// </summary>
    public static Stream CreateSquashFsImage()
    {
        var builder = new SquashFileSystemBuilder();
        builder.AddFile("hello.txt", Encoding.ASCII.GetBytes("Hello from squashfs\n"));
        builder.AddDirectory("dir");
        builder.AddFile(@"dir\nested.txt", Encoding.ASCII.GetBytes("Nested file\n"));
        var stream = new MemoryStream();
        builder.Build(stream);
        stream.Position = 0;
        return stream;
    }

    /// <summary>
    /// Creates a dynamic VHD, VHDX or VDI image whose disk holds a disk or file system image, skipping its blocks of
    /// zeros so the image stays small.
    /// </summary>
    public static async Task<Stream> CreateVirtualDisk(string format, Stream content)
    {
        var stream = new MemoryStream();
        // a VHD disk is rounded down to its geometry, so the disk is made larger than the content
        var capacity = content.Length + 1024 * 1024;
        using (VirtualDisk disk = format switch
               {
                   "VHD" => DiscUtils.Vhd.Disk.InitializeDynamic(stream, Ownership.None, capacity),
                   "VHDX" => DiscUtils.Vhdx.Disk.InitializeDynamic(stream, Ownership.None, capacity),
                   "VDI" => DiscUtils.Vdi.Disk.InitializeDynamic(stream, Ownership.None, capacity),
                   _ => throw new ArgumentOutOfRangeException(nameof(format), format, null)
               })
        {
            var diskStream = disk.Content;
            var buffer = new byte[64 * 1024];
            content.Position = 0;
            int length;
            while ((length = await content.ReadAtLeastAsync(buffer, buffer.Length, false)) > 0)
            {
                if (buffer.AsSpan(0, length).ContainsAnyExcept((byte)0))
                {
                    diskStream.Position = content.Position - length;
                    await diskStream.WriteAsync(buffer.AsMemory(0, length));
                }
            }
        }

        stream.Position = 0;
        return stream;
    }

    /// <summary>
    /// Creates an MBR disk with a Linux partition (type 0x83) holding a file system image, e.g. of
    /// <see cref="OpenGzippedImage" />.
    /// </summary>
    public static async Task<Stream> CreateLinuxPartitionDisk(Stream image)
    {
        var sectorCount = (int)(image.Length / SectorSize);
        var stream = CreateStream((FirstSector + sectorCount + 1) * (long)SectorSize);
        var geometry = Geometry.FromCapacity(stream.Length);
        BiosPartitionTable.Initialize(stream, geometry).CreatePrimaryBySector(FirstSector,
            FirstSector + sectorCount - 1, BiosPartitionTypes.LinuxNative, false);
        await using (var partitionStream = OpenPartition(stream, FirstSector, sectorCount))
        {
            image.Position = 0;
            await image.CopyToAsync(partitionStream);
        }

        stream.Position = 0;
        return stream;
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
