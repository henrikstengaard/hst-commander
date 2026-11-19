using System.Buffers.Binary;
using Hst.Commander.Abstractions;

namespace Hst.Commander.Plugins.Disk;

/// <summary>
/// File volume provider opening FAT, exFAT, NTFS and ext2/3/4 file systems without a partition table (e.g. images of
/// partitions or floppy disks, or exFAT file systems created with <see cref="ExFatVolumeCreator" />) as writable
/// volumes, except ext2/3/4, which DiscUtils only reads. Detected by "NTFS    " at offset 3 of the boot sector, by a
/// boot sector ending with 0x55 0xAA holding "EXFAT   " at offset 3 or "FAT" at 0x36 or "FAT32" at 0x52 (boot sectors
/// <see cref="DiskFileVolumeProvider" /> doesn't match), or by an ext2/3/4 superblock (see <see cref="IsExtSuperblock" />).
/// </summary>
public class FileSystemFileVolumeProvider : IFileVolumeProvider
{
    private const int BootSectorSize = 512;
    private const int FileSystemNameOffset = 3;
    private const int FatNameOffset = 0x36;
    private const int Fat32NameOffset = 0x52;
    private const int DetectLength = 2048;
    private static readonly byte[] ExFatName = "EXFAT   "u8.ToArray();
    private static readonly byte[] NtfsName = "NTFS    "u8.ToArray();

    public string Name => "File system";

    public string Description => "FAT, exFAT, NTFS and ext2/3/4 file systems without a partition table";

    public bool CanWrite => true;

    public async Task<bool> IsMatch(string name, Stream stream, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);

        var start = new byte[DetectLength];
        var length = await stream.ReadAtLeastAsync(start, start.Length, false, cancellationToken);
        return Detect(start.AsSpan(0, length)) != null;
    }

    /// <summary>
    /// Name of the file system starting with the bytes, "FAT", "exFAT", "NTFS" or "ext"; null when none of them is
    /// detected.
    /// </summary>
    internal static string? Detect(ReadOnlySpan<byte> start)
    {
        // NTFS formatted by DiscUtils has no 0x55 0xAA, which DiscUtils doesn't check either
        if (start.Length >= BootSectorSize &&
            start.Slice(FileSystemNameOffset, NtfsName.Length).SequenceEqual(NtfsName))
        {
            return "NTFS";
        }

        if (start.Length >= BootSectorSize && start[510] == 0x55 && start[511] == 0xAA)
        {
            if (start.Slice(FileSystemNameOffset, ExFatName.Length).SequenceEqual(ExFatName))
            {
                return "exFAT";
            }

            if (start.Slice(FatNameOffset, 3).SequenceEqual("FAT"u8) ||
                start.Slice(Fat32NameOffset, 5).SequenceEqual("FAT32"u8))
            {
                return "FAT";
            }
        }

        return IsExtSuperblock(start) ? "ext" : null;
    }

    /// <summary>
    /// True when the bytes have an ext2/3/4 superblock: the magic number 0xEF53, and as the magic number is only two
    /// bytes, inodes, blocks per group and inodes per group, a block size of 1 to 64 KB (log block size 0 to 6) and
    /// revision 0 or 1.
    /// </summary>
    private static bool IsExtSuperblock(ReadOnlySpan<byte> start)
    {
        if (start.Length < FileSystems.ExtSuperblockOffset + 1024)
        {
            return false;
        }

        var superblock = start.Slice(FileSystems.ExtSuperblockOffset, 1024);
        return superblock[FileSystems.ExtMagicOffset] == 0x53 && superblock[FileSystems.ExtMagicOffset + 1] == 0xEF &&
               ReadUInt32(superblock, 0) != 0 && ReadUInt32(superblock, 24) <= 6 &&
               ReadUInt32(superblock, 32) != 0 && ReadUInt32(superblock, 40) != 0 && ReadUInt32(superblock, 76) <= 1;
    }

    private static uint ReadUInt32(ReadOnlySpan<byte> bytes, int offset) =>
        BinaryPrimitives.ReadUInt32LittleEndian(bytes[offset..]);

    /// <summary>
    /// Mounts the file system, named by its type (e.g. "FAT"), throwing <see cref="IOException" /> when it can't be
    /// read. The stream must be seekable.
    /// </summary>
    public async Task<IVolume> CreateVolume(Stream stream, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);

        var start = new byte[DetectLength];
        stream.Position = 0;
        var length = await stream.ReadAtLeastAsync(start, start.Length, false, cancellationToken);
        var fileSystemName = Detect(start.AsSpan(0, length)) ??
                             throw new IOException("No FAT, exFAT, NTFS or ext2/3/4 file system found");
        try
        {
            return FileSystems.MountFileSystem(fileSystemName, stream) ??
                   throw new IOException($"The {fileSystemName} file system can't be read");
        }
        catch (Exception exception) when (exception is not IOException)
        {
            throw new IOException($"The {fileSystemName} file system can't be read: {exception.Message}", exception);
        }
    }
}
