using DiscUtils.Streams;
using Hst.Amiga.RigidDiskBlocks;
using Hst.Commander.Abstractions;
using VhdDisk = DiscUtils.Vhd.Disk;

namespace Hst.Commander.Plugins.Amiga;

/// <summary>
/// File volume provider opening Amiga hard disk images with a rigid disk block (RDB) as writable volumes listing their
/// partitions (see <see cref="RdbVolume" />). Detected by the signature "RDSK" at the start of one of the first 16
/// blocks of the disk, where the rigid disk block is searched for. Raw disk images (e.g. .img or .hdf) and fixed size
/// VHD images are read directly, dynamic and differencing VHD images (signature "conectix" at the start) through their
/// virtual disk.
/// </summary>
public class RdbFileVolumeProvider : IFileVolumeProvider
{
    private const int BlockSize = 512;
    private const int RdbLocationLimit = 16;
    private static readonly byte[] RdbSignature = "RDSK"u8.ToArray();
    private static readonly byte[] VhdSignature = "conectix"u8.ToArray();

    public string Name => "Rdb";

    public string Description => "Amiga hard disk images with rigid disk block";

    public bool CanWrite => true;

    public async Task<bool> IsMatch(string name, Stream stream, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);

        var blocks = new byte[BlockSize * RdbLocationLimit];
        var length = await stream.ReadAtLeastAsync(blocks, blocks.Length, false, cancellationToken);
        if (HasRdbSignature(blocks.AsSpan(0, length)))
        {
            return true;
        }

        // the disk of a dynamic vhd image is read through the virtual disk, which needs a seekable stream
        if (length < VhdSignature.Length || !blocks.AsSpan(0, VhdSignature.Length).SequenceEqual(VhdSignature) ||
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
            length = await diskStream.ReadAtLeastAsync(blocks, blocks.Length, false, cancellationToken);
            return HasRdbSignature(blocks.AsSpan(0, length));
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return false;
        }
    }

    private static bool HasRdbSignature(ReadOnlySpan<byte> blocks)
    {
        for (var offset = 0; offset + RdbSignature.Length <= blocks.Length; offset += BlockSize)
        {
            if (blocks.Slice(offset, RdbSignature.Length).SequenceEqual(RdbSignature))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Creates a volume listing the partitions of the rigid disk block. Throws <see cref="IOException" /> when the
    /// rigid disk block can't be read or has no partitions.
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
            var diskStream = disk?.Content ?? stream;
            RigidDiskBlock? rigidDiskBlock;
            try
            {
                rigidDiskBlock = await RigidDiskBlockReader.Read(diskStream);
            }
            catch (Exception exception) when (exception is not IOException)
            {
                throw new IOException($"Rigid disk block can't be read: {exception.Message}", exception);
            }

            if (rigidDiskBlock == null)
            {
                throw new IOException("No rigid disk block found");
            }

            var partitionBlocks = rigidDiskBlock.PartitionBlocks.ToList();
            if (partitionBlocks.Count == 0)
            {
                throw new IOException("The rigid disk block has no partitions");
            }

            return new RdbVolume(diskStream, partitionBlocks, disk);
        }
        catch
        {
            disk?.Dispose();
            throw;
        }
    }
}
