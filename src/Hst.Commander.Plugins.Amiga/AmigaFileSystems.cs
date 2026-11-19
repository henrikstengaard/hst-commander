using System.Buffers.Binary;
using Hst.Amiga.FileSystems.FastFileSystem;
using Hst.Amiga.FileSystems.Pfs3;
using Hst.Amiga.FileSystems.SmartFileSystem;
using Hst.Amiga.RigidDiskBlocks;

namespace Hst.Commander.Plugins.Amiga;

/// <summary>
/// Mounts the Amiga file systems supported: Fast File System (DOS\0 to DOS\7, long file names with DOS\6 and DOS\7)
/// PFS3 (PFS\3 and PDS\3) and Smart File System (SFS\0; SFS\2 isn't supported by Hst.Amiga).
/// </summary>
public static class AmigaFileSystems
{
    private const int MaxNameLength = 30;
    private const int MaxLongNameLength = 107;
    private const int SmartFileSystemMaxNameLength = 100;

    /// <summary>Mounts the Fast File System of an adf, detected by <see cref="AdfFileVolumeProvider" />.</summary>
    public static async Task<AmigaFileSystemVolume> MountAdf(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);

        var dosType = new byte[4];
        stream.Position = 0;
        await stream.ReadExactlyAsync(dosType);
        stream.Position = 0;

        var fileSystemVolume = await FastFileSystemVolume.MountAdf(stream);
        return new AmigaFileSystemVolume("Adf", fileSystemVolume, stream.CanWrite,
            GetFastFileSystemMaxNameLength(dosType));
    }

    /// <summary>
    /// Mounts the file system of a partition in a rigid disk block. Throws <see cref="IOException" /> when the file
    /// system isn't supported, e.g. SFS\2.
    /// </summary>
    /// <param name="stream">Stream of the disk with the rigid disk block.</param>
    /// <param name="partitionBlock">Partition to mount.</param>
    /// <param name="name">Name of the volume.</param>
    public static async Task<AmigaFileSystemVolume> MountPartition(Stream stream, PartitionBlock partitionBlock,
        string name)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(partitionBlock);

        if (IsFastFileSystem(partitionBlock.DosType))
        {
            var fastFileSystemVolume = await FastFileSystemVolume.MountPartition(stream, partitionBlock);
            return new AmigaFileSystemVolume(name, fastFileSystemVolume, stream.CanWrite,
                GetFastFileSystemMaxNameLength(partitionBlock.DosType));
        }

        if (IsPfs3(partitionBlock.DosType))
        {
            var fileNameSize = await ReadPfs3FileNameSize(stream, partitionBlock);
            var pfs3Volume = await Pfs3Volume.Mount(stream, partitionBlock);

            // the file name size includes the length of the name
            return new AmigaFileSystemVolume(name, pfs3Volume, stream.CanWrite, fileNameSize - 1);
        }

        if (IsSmartFileSystem(partitionBlock.DosType))
        {
            var smartFileSystemVolume = await SmartFileSystemVolume.Mount(stream, partitionBlock);
            return new AmigaFileSystemVolume(name, smartFileSystemVolume, stream.CanWrite,
                SmartFileSystemMaxNameLength);
        }

        throw new IOException(
            $"Partition '{name}' has the file system '{partitionBlock.DosTypeFormatted}', which is not supported");
    }

    /// <summary>
    /// Mounts the file system of a partition image without a rigid disk block (e.g. a UAE hardfile), detected by
    /// <see cref="GetPartitionImageDosType" />, as a partition covering the whole stream with blocks of 512 bytes and 2
    /// reserved blocks. The Fast File System doesn't store its block size, so it's mounted with the first file system
    /// block size of 512 to 32768 bytes that works; PFS3 and Smart File System read theirs from their root blocks.
    /// Throws <see cref="IOException" /> when the file system isn't supported or can't be mounted.
    /// </summary>
    public static async Task<AmigaFileSystemVolume> MountPartitionImage(Stream stream, string name)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(name);

        var firstBlock = new byte[4];
        stream.Position = 0;
        var length = await stream.ReadAtLeastAsync(firstBlock, firstBlock.Length, false);
        var dosType = GetPartitionImageDosType(firstBlock.AsSpan(0, length)) ??
                      throw new IOException("No Amiga file system found");

        var blocks = stream.Length / PartitionImageBlockSize;
        if (blocks < 2 * PartitionImageReserved || blocks > uint.MaxValue)
        {
            throw new IOException("The size of the Amiga partition image is not supported");
        }

        if (!IsFastFileSystem(dosType))
        {
            return await MountPartitionImage(stream, name, dosType, blocks, PartitionImageBlockSize);
        }

        IOException? lastException = null;
        for (var fileSystemBlockSize = PartitionImageBlockSize;
             fileSystemBlockSize <= MaxFileSystemBlockSize;
             fileSystemBlockSize *= 2)
        {
            try
            {
                return await MountPartitionImage(stream, name, dosType, blocks, fileSystemBlockSize);
            }
            catch (Exception exception)
            {
                lastException = exception as IOException ??
                                new IOException($"The Amiga file system can't be mounted: {exception.Message}",
                                    exception);
            }
        }

        throw lastException!;
    }

    private static async Task<AmigaFileSystemVolume> MountPartitionImage(Stream stream, string name, byte[] dosType,
        long blocks, int fileSystemBlockSize)
    {
        var partitionBlock = new PartitionBlock
        {
            DriveName = name,
            DosType = dosType,
            LowCyl = 0,
            HighCyl = (uint)(blocks - 1),
            Surfaces = 1,
            BlocksPerTrack = 1,
            Sectors = 1,
            Reserved = PartitionImageReserved,
            BlockSize = PartitionImageBlockSize,
            FileSystemBlockSize = (uint)fileSystemBlockSize,
            SizeBlock = PartitionImageBlockSize / 4,
            PartitionSize = blocks * PartitionImageBlockSize
        };
        stream.Position = 0;
        return await MountPartition(stream, partitionBlock, name);
    }

    /// <summary>
    /// DOS type of a partition image without a rigid disk block by the id at the start of its first block: DOS\0 to
    /// DOS\7 (Fast File System), PFS\1, PFS\2 (the boot block ids of PFS3), PFS\3 and PDS\3 (returned as PFS\3) and
    /// SFS\0 (Smart File System); null when the first block has none of them.
    /// </summary>
    public static byte[]? GetPartitionImageDosType(ReadOnlySpan<byte> firstBlock)
    {
        if (firstBlock.Length < 4)
        {
            return null;
        }

        var dosType = firstBlock[..4].ToArray();
        if (IsFastFileSystem(dosType) || IsSmartFileSystem(dosType))
        {
            return dosType;
        }

        return IsPfs3(dosType) || (dosType[0] == 'P' && dosType[1] == 'F' && dosType[2] == 'S' && dosType[3] is 1 or 2)
            ? [(byte)'P', (byte)'F', (byte)'S', 3]
            : null;
    }

    private const int PartitionImageBlockSize = 512;
    private const int PartitionImageReserved = 2;
    private const int MaxFileSystemBlockSize = 32768;

    /// <summary>
    /// Size of file names of a PFS3 partition including their length byte, read from the root block extension (as
    /// Hst.Amiga doesn't make it public): <c>fnsize</c> when the root block has an extension (MODE_EXTENSION) with it
    /// set, otherwise PFS3's default of 32. The layout is PFS3's (pfs3aio blocks.h): the root block is the third block of
    /// the partition with the options at offset 4 and the extension's block at 88, and the extension has the id "EX" at
    /// offset 0 and fnsize at 56.
    /// </summary>
    private static async Task<int> ReadPfs3FileNameSize(Stream stream, PartitionBlock partitionBlock)
    {
        const int defaultFileNameSize = 32;
        const uint modeExtension = 64;
        const int sectorSize = 512;

        var partitionOffset = (long)partitionBlock.LowCyl * partitionBlock.Surfaces * partitionBlock.BlocksPerTrack *
                              partitionBlock.BlockSize;
        var block = new byte[sectorSize];
        stream.Position = partitionOffset + 2L * sectorSize;
        await stream.ReadExactlyAsync(block);
        var options = BinaryPrimitives.ReadUInt32BigEndian(block.AsSpan(4));
        var extension = BinaryPrimitives.ReadUInt32BigEndian(block.AsSpan(88));
        if ((options & modeExtension) == 0 || extension == 0)
        {
            return defaultFileNameSize;
        }

        stream.Position = partitionOffset + (long)extension * sectorSize;
        await stream.ReadExactlyAsync(block);
        if (block[0] != 'E' || block[1] != 'X')
        {
            return defaultFileNameSize;
        }

        var fileNameSize = BinaryPrimitives.ReadUInt16BigEndian(block.AsSpan(56));
        return fileNameSize > 1 ? fileNameSize : defaultFileNameSize;
    }

    /// <summary>
    /// Name of the file system of a DOS type with the DOS type, e.g. "Fast File System, international (DOS\3)", or
    /// just the DOS type when unknown.
    /// </summary>
    public static string FormatFileSystem(byte[] dosType)
    {
        ArgumentNullException.ThrowIfNull(dosType);

        if (dosType.Length != 4)
        {
            return Convert.ToHexString(dosType);
        }

        var formatted = $"{(char)dosType[0]}{(char)dosType[1]}{(char)dosType[2]}\\{dosType[3]}";
        string? name = formatted switch
        {
            "DOS\\0" => "Old File System",
            "DOS\\1" => "Fast File System",
            "DOS\\2" => "Old File System, international",
            "DOS\\3" => "Fast File System, international",
            "DOS\\4" => "Old File System, directory cache",
            "DOS\\5" => "Fast File System, directory cache",
            "DOS\\6" => "Old File System, long file names",
            "DOS\\7" => "Fast File System, long file names",
            "PFS\\3" => "Professional File System 3",
            "PDS\\3" => "Professional File System 3, direct SCSI",
            "SFS\\0" => "Smart File System",
            "SFS\\2" => "Smart File System 2",
            _ => null
        };
        return name != null ? $"{name} ({formatted})" : formatted;
    }

    private static bool IsFastFileSystem(byte[] dosType) =>
        dosType.Length == 4 && dosType[0] == 'D' && dosType[1] == 'O' && dosType[2] == 'S' && dosType[3] <= 7;

    private static bool IsPfs3(byte[] dosType) =>
        dosType.Length == 4 && (dosType[0] == 'P' && dosType[1] is (byte)'F' or (byte)'D') && dosType[2] == 'S' &&
        dosType[3] == 3;

    private static bool IsSmartFileSystem(byte[] dosType) =>
        dosType.Length == 4 && dosType[0] == 'S' && dosType[1] == 'F' && dosType[2] == 'S' && dosType[3] == 0;

    private static int GetFastFileSystemMaxNameLength(byte[] dosType) =>
        dosType[3] is 6 or 7 ? MaxLongNameLength : MaxNameLength;
}
