using System.Buffers.Binary;
using System.Numerics;
using DiscUtils.Btrfs;
using DiscUtils.ExFat;
using DiscUtils.Ext;
using DiscUtils.Fat;
using DiscUtils.HfsPlus;
using DiscUtils.Ntfs;
using DiscUtils.SquashFs;
using DiscUtils.Udf;
using DiscUtils.Xfs;
using Hst.Commander.Abstractions;

namespace Hst.Commander.Plugins.Disk;

/// <summary>
/// Mounts the file systems read by DiscUtils: FAT, exFAT and NTFS, and read-only ext2/3/4, XFS, Btrfs, HFS+, SquashFS
/// and UDF, in partitions (<see cref="PartitionTableVolume" />), in images without a partition table
/// (<see cref="FileSystemFileVolumeProvider" />) and in virtual disks (<see cref="DiskFileVolumeProvider" />).
/// </summary>
internal static class FileSystems
{
    /// <summary>Offset of the ext2/3/4 superblock in a partition.</summary>
    internal const int ExtSuperblockOffset = 1024;

    /// <summary>Offset of the magic number (0xEF53, little-endian) in the ext2/3/4 superblock.</summary>
    internal const int ExtMagicOffset = 0x38;

    /// <summary>Number of bytes at the start of a file system read to detect it, covering the Btrfs superblock.</summary>
    internal const int DetectLength = 68 * 1024;

    /// <summary>Names of the read-only file systems detected by <see cref="DetectReadOnlyFileSystem" />.</summary>
    internal const string Xfs = "XFS";

    internal const string Btrfs = "Btrfs";
    internal const string HfsPlus = "HFS+";
    internal const string Hfsx = "HFSX";
    internal const string SquashFs = "SquashFS";
    internal const string Udf = "UDF";

    private const int HfsPlusSignatureOffset = 1024;
    private const int BtrfsMagicOffset = 0x10040;
    private const int UdfFirstDescriptorOffset = 32768;
    private const int UdfDescriptorSize = 2048;
    private const int UdfDescriptorCount = 16;
    private static readonly byte[] XfsMagic = "XFSB"u8.ToArray();
    private static readonly byte[] SquashFsMagic = "hsqs"u8.ToArray();
    private static readonly byte[] HfsPlusSignature = [0x48, 0x2B, 0x00, 0x04];
    private static readonly byte[] HfsxSignature = [0x48, 0x58, 0x00, 0x05];
    private static readonly byte[] BtrfsMagic = "_BHRfS_M"u8.ToArray();

    /// <summary>Path separators of exFAT paths, '\' first as <see cref="DiscFileSystemVolume" /> joins paths with it.</summary>
    private static readonly char[] ExFatPathSeparators = ['\\', '/'];

    /// <summary>
    /// Mounts the file systems read by DiscUtils (see <see cref="FileSystems" />) in a seekable stream; null for other
    /// file systems.
    /// </summary>
    /// <param name="name">Name of the volume.</param>
    /// <param name="stream">Stream of the file system.</param>
    /// <param name="resource">Resource disposed with the volume after the file system, e.g. a virtual disk.</param>
    internal static DiscFileSystemVolume? MountFileSystem(string name, Stream stream, IDisposable? resource = null)
    {
        stream.Position = 0;
        if (FatFileSystem.Detect(stream))
        {
            stream.Position = 0;
            return new DiscFileSystemVolume(name, new FatFileSystem(stream), resource: resource);
        }

        stream.Position = 0;
        if (ExFatFileSystem.Detect(stream))
        {
            return MountExFat(name, stream, resource);
        }

        stream.Position = 0;
        if (NtfsFileSystem.Detect(stream))
        {
            stream.Position = 0;
            return new DiscFileSystemVolume(name, new NtfsFileSystem(stream), resource: resource);
        }

        stream.Position = 0;
        if (IsExtFileSystem(stream))
        {
            stream.Position = 0;
            return new DiscFileSystemVolume(name, new ExtFileSystem(stream), caseSensitive: true, resource: resource);
        }

        var start = new byte[DetectLength];
        stream.Position = 0;
        var length = stream.ReadAtLeast(start, start.Length, false);
        stream.Position = 0;
        return DetectReadOnlyFileSystem(start.AsSpan(0, length)) is { } fileSystemName
            ? MountReadOnlyFileSystem(name, stream, fileSystemName, resource)
            : null;
    }

    /// <summary>
    /// Mounts the exFAT file system in a seekable stream, e.g. a partition, with its volume label, which DiscUtils
    /// doesn't read (see <see cref="ExFatVolumeLabel" />).
    /// </summary>
    internal static DiscFileSystemVolume MountExFat(string name, Stream stream, IDisposable? resource = null)
    {
        stream.Position = 0;
        var volumeLabel = ExFatVolumeLabel.Read(stream);
        stream.Position = 0;
        // the separators default to the OS's (only '/' on macOS and Linux), DiscFileSystemVolume joins paths with '\'
        return new DiscFileSystemVolume(name, new ExFatFileSystem(stream, ExFatPathSeparators),
            volumeLabel: volumeLabel, resource: resource);
    }

    /// <summary>
    /// Mounts a read-only file system detected by <see cref="DetectReadOnlyFileSystem" />, throwing its errors as
    /// <see cref="IOException" />.
    /// </summary>
    private static DiscFileSystemVolume MountReadOnlyFileSystem(string name, Stream stream, string fileSystemName,
        IDisposable? resource)
    {
        try
        {
            stream.Position = 0;
            return fileSystemName switch
            {
                Xfs => new DiscFileSystemVolume(name, new XfsFileSystem(stream), true, resource: resource,
                    space: ReadXfsSpace(stream)),
                Btrfs => new DiscFileSystemVolume(name, new BtrfsFileSystem(stream), true, resource: resource),
                HfsPlus => new DiscFileSystemVolume(name, new HfsPlusFileSystem(stream), resource: resource),
                Hfsx => new DiscFileSystemVolume(name, new HfsPlusFileSystem(stream), true, resource: resource),
                SquashFs => new DiscFileSystemVolume(name, new SquashFileSystemReader(stream), true,
                    resource: resource),
                Udf => new DiscFileSystemVolume(name, new UdfReader(stream), resource: resource),
                _ => throw new ArgumentOutOfRangeException(nameof(fileSystemName), fileSystemName, null)
            };
        }
        catch (Exception exception) when (exception is not (IOException or OperationCanceledException))
        {
            throw new IOException($"The {fileSystemName} file system can't be read: {exception.Message}", exception);
        }
    }

    /// <summary>
    /// Name of the read-only file system other than ext2/3/4 starting with the bytes (<see cref="DetectLength" />
    /// bytes, or the whole file system when it's smaller): <see cref="Xfs" /> ("XFSB" and a block size at the start),
    /// <see cref="SquashFs" /> ("hsqs" version 4 at the start), <see cref="HfsPlus" /> or <see cref="Hfsx" /> ("H+" version 4 or
    /// "HX" version 5 at 1024), <see cref="Btrfs" /> ("_BHRfS_M" in the superblock at 64 KB) or <see cref="Udf" /> (a
    /// volume recognition sequence at 32 KB with "BEA01" and "NSR02" or "NSR03"); null for none of them.
    /// </summary>
    internal static string? DetectReadOnlyFileSystem(ReadOnlySpan<byte> start)
    {
        if (HasBytes(start, 0, XfsMagic) && IsXfsBlockSize(start))
        {
            return Xfs;
        }

        // SquashFS 4.0, the version DiscUtils reads, little-endian major version at 28
        if (HasBytes(start, 0, SquashFsMagic) && start.Length >= 30 && start[28] == 4 && start[29] == 0)
        {
            return SquashFs;
        }

        if (HasBytes(start, HfsPlusSignatureOffset, HfsPlusSignature))
        {
            return HfsPlus;
        }

        if (HasBytes(start, HfsPlusSignatureOffset, HfsxSignature))
        {
            return Hfsx;
        }

        if (HasBytes(start, BtrfsMagicOffset, BtrfsMagic))
        {
            return Btrfs;
        }

        return IsUdf(start) ? Udf : null;
    }

    /// <summary>
    /// True when the bytes have a UDF volume recognition sequence: descriptors of 2048 bytes from 32 KB with a "BEA01"
    /// descriptor followed by an "NSR02" or "NSR03" descriptor (ISO 9660 descriptors may come first), ended by the
    /// first descriptor that isn't one.
    /// </summary>
    private static bool IsUdf(ReadOnlySpan<byte> start)
    {
        var extendedArea = false;
        for (var index = 0; index < UdfDescriptorCount; index++)
        {
            var offset = UdfFirstDescriptorOffset + index * UdfDescriptorSize;
            if (start.Length < offset + 6)
            {
                return false;
            }

            var identifier = start.Slice(offset + 1, 5);
            if (identifier.SequenceEqual("BEA01"u8))
            {
                extendedArea = true;
            }
            else if (identifier.SequenceEqual("NSR02"u8) || identifier.SequenceEqual("NSR03"u8))
            {
                return extendedArea;
            }
            else if (!(identifier.SequenceEqual("CD001"u8) || identifier.SequenceEqual("BOOT2"u8) ||
                       identifier.SequenceEqual("CDW02"u8) || identifier.SequenceEqual("TEA01"u8)))
            {
                return false;
            }
        }

        return false;
    }

    /// <summary>
    /// Free space and size of an XFS file system from its superblock (big-endian: block size at 4, data blocks at 8,
    /// free data blocks at 0x90), as DiscUtils' XFS doesn't tell them.
    /// </summary>
    private static VolumeSpace ReadXfsSpace(Stream stream)
    {
        var superblock = new byte[0x98];
        stream.Position = 0;
        stream.ReadExactly(superblock);
        stream.Position = 0;
        var blockSize = (long)BinaryPrimitives.ReadUInt32BigEndian(superblock.AsSpan(4));
        var size = (long)BinaryPrimitives.ReadUInt64BigEndian(superblock.AsSpan(8)) * blockSize;
        var free = (long)BinaryPrimitives.ReadUInt64BigEndian(superblock.AsSpan(0x90)) * blockSize;
        return new VolumeSpace(Math.Clamp(free, 0, size), size);
    }

    /// <summary>True when the XFS superblock has a block size (big-endian at 4) of 512 bytes to 64 KB.</summary>
    private static bool IsXfsBlockSize(ReadOnlySpan<byte> start)
    {
        if (start.Length < 8)
        {
            return false;
        }

        var blockSize = BinaryPrimitives.ReadUInt32BigEndian(start[4..]);
        return blockSize is >= 512 and <= 65536 && BitOperations.IsPow2(blockSize);
    }

    private static bool HasBytes(ReadOnlySpan<byte> bytes, int offset, ReadOnlySpan<byte> expected) =>
        bytes.Length >= offset + expected.Length && bytes.Slice(offset, expected.Length).SequenceEqual(expected);

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
