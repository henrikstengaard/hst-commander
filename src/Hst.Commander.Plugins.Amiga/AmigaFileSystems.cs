using Hst.Amiga.FileSystems.FastFileSystem;
using Hst.Amiga.FileSystems.Pfs3;
using Hst.Amiga.RigidDiskBlocks;

namespace Hst.Commander.Plugins.Amiga;

/// <summary>
/// Mounts the Amiga file systems supported: Fast File System (DOS\0 to DOS\7, long file names with DOS\6 and DOS\7)
/// and PFS3 (PFS\3 and PDS\3).
/// </summary>
public static class AmigaFileSystems
{
    private const int MaxNameLength = 30;
    private const int MaxLongNameLength = 107;

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
    /// system isn't supported, e.g. SFS.
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
            var pfs3Volume = await Pfs3Volume.Mount(stream, partitionBlock);

            // the file name size includes the length of the name
            return new AmigaFileSystemVolume(name, pfs3Volume, stream.CanWrite, pfs3Volume.g.fnsize - 1);
        }

        throw new IOException(
            $"Partition '{name}' has the file system '{partitionBlock.DosTypeFormatted}', which is not supported");
    }

    private static bool IsFastFileSystem(byte[] dosType) =>
        dosType.Length == 4 && dosType[0] == 'D' && dosType[1] == 'O' && dosType[2] == 'S' && dosType[3] <= 7;

    private static bool IsPfs3(byte[] dosType) =>
        dosType.Length == 4 && (dosType[0] == 'P' && dosType[1] is (byte)'F' or (byte)'D') && dosType[2] == 'S' &&
        dosType[3] == 3;

    private static int GetFastFileSystemMaxNameLength(byte[] dosType) =>
        dosType[3] is 6 or 7 ? MaxLongNameLength : MaxNameLength;
}
