using DiscUtils.ExFat;
using DiscUtils.Ext;
using DiscUtils.Fat;
using DiscUtils.Ntfs;

namespace Hst.Commander.Plugins.Disk;

/// <summary>
/// Mounts the file systems read by DiscUtils: FAT, exFAT, NTFS and ext2/3/4 (read-only), in partitions
/// (<see cref="PartitionTableVolume" />) and in images without a partition table
/// (<see cref="FileSystemFileVolumeProvider" />).
/// </summary>
internal static class FileSystems
{
    /// <summary>Offset of the ext2/3/4 superblock in a partition.</summary>
    internal const int ExtSuperblockOffset = 1024;

    /// <summary>Offset of the magic number (0xEF53, little-endian) in the ext2/3/4 superblock.</summary>
    internal const int ExtMagicOffset = 0x38;

    /// <summary>Path separators of exFAT paths, '\' first as <see cref="DiscFileSystemVolume" /> joins paths with it.</summary>
    private static readonly char[] ExFatPathSeparators = ['\\', '/'];

    /// <summary>
    /// Mounts the file systems read by DiscUtils, FAT, exFAT, NTFS and ext2/3/4 (read-only), in a seekable stream; null
    /// for other file systems.
    /// </summary>
    internal static DiscFileSystemVolume? MountFileSystem(string name, Stream stream)
    {
        stream.Position = 0;
        if (FatFileSystem.Detect(stream))
        {
            stream.Position = 0;
            return new DiscFileSystemVolume(name, new FatFileSystem(stream));
        }

        stream.Position = 0;
        if (ExFatFileSystem.Detect(stream))
        {
            return MountExFat(name, stream);
        }

        stream.Position = 0;
        if (NtfsFileSystem.Detect(stream))
        {
            stream.Position = 0;
            return new DiscFileSystemVolume(name, new NtfsFileSystem(stream));
        }

        stream.Position = 0;
        if (IsExtFileSystem(stream))
        {
            stream.Position = 0;
            return new DiscFileSystemVolume(name, new ExtFileSystem(stream), caseSensitive: true);
        }

        stream.Position = 0;
        return null;
    }

    /// <summary>
    /// Mounts the exFAT file system in a seekable stream, e.g. a partition, with its volume label, which DiscUtils
    /// doesn't read (see <see cref="ExFatVolumeLabel" />).
    /// </summary>
    internal static DiscFileSystemVolume MountExFat(string name, Stream stream)
    {
        stream.Position = 0;
        var volumeLabel = ExFatVolumeLabel.Read(stream);
        stream.Position = 0;
        // the separators default to the OS's (only '/' on macOS and Linux), DiscFileSystemVolume joins paths with '\'
        return new DiscFileSystemVolume(name, new ExFatFileSystem(stream, ExFatPathSeparators),
            volumeLabel: volumeLabel);
    }

    /// <summary>True when the stream has an ext2/3/4 superblock, detected by its magic number.</summary>
    private static bool IsExtFileSystem(Stream stream)
    {
        if (stream.Length < ExtSuperblockOffset + 1024)
        {
            return false;
        }

        stream.Position = ExtSuperblockOffset + ExtMagicOffset;
        Span<byte> magic = stackalloc byte[2];
        stream.ReadExactly(magic);
        return magic[0] == 0x53 && magic[1] == 0xEF;
    }
}
