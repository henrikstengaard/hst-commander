using Hst.Amiga;
using Hst.Amiga.FileSystems.FastFileSystem;
using Hst.Amiga.FileSystems.FastFileSystem.Blocks;
using Hst.Commander.Abstractions;

namespace Hst.Commander.Plugins.Amiga;

/// <summary>
/// Volume creator creating a double density (880 KB) Amiga disk file (adf), formatted with the Fast File System
/// (DOS\3) and optionally bootable, or unformatted (all zeros) when format isn't checked.
/// </summary>
public class AdfVolumeCreator : IVolumeCreator
{
    public const string VolumeNameField = "VolumeName";
    public const string HighDensityField = "HighDensity";
    public const string FormatField = "Format";
    public const string FileSystemField = "FileSystem";
    public const string BootableField = "Bootable";

    private const int MaxVolumeNameLength = 30;
    private const int BootBlockSize = 1024;
    private const string DefaultFileSystem = "DOS3";

    private static readonly VolumeCreatorChoice[] FileSystems =
    [
        new("DOS0", "DOS0 (OFS)"),
        new("DOS1", "DOS1 (FFS)"),
        new("DOS2", "DOS2 (OFS, international)"),
        new("DOS3", "DOS3 (FFS, international)"),
        new("DOS4", "DOS4 (OFS, directory cache)"),
        new("DOS5", "DOS5 (FFS, directory cache)"),
        new("DOS6", "DOS6 (OFS, long file names)"),
        new("DOS7", "DOS7 (FFS, long file names)")
    ];

    public string Name => "Amiga disk file";

    public string Description => "Double density (880 KB) or high density (1.76 MB) floppy disk";

    public string FileExtension => ".adf";

    public IReadOnlyList<VolumeCreatorField> Fields { get; } =
    [
        new(VolumeNameField, "Volume name", VolumeCreatorFieldType.Text, true, "Empty", EnabledByField: FormatField),
        new(HighDensityField, "High density (1.76 MB)", VolumeCreatorFieldType.Boolean, false, false),
        new(FormatField, "Format", VolumeCreatorFieldType.Boolean, false, true),
        new(BootableField, "Bootable", VolumeCreatorFieldType.Boolean, false, false, EnabledByField: FormatField),
        new(FileSystemField, "File system", VolumeCreatorFieldType.Choice, false, DefaultFileSystem, FileSystems,
            FormatField)
    ];

    public async Task Create(Stream stream, VolumeCreatorValues values, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(values);

        var format = values.GetBoolean(FormatField);
        var volumeName = values.GetText(VolumeNameField);
        if (format)
        {
            ValidateVolumeName(volumeName);
        }

        var highDensity = values.GetBoolean(HighDensityField);
        stream.SetLength(highDensity ? FloppyDiskConstants.HighDensity.Size : FloppyDiskConstants.DoubleDensity.Size);
        if (!format)
        {
            return;
        }

        var dosType = GetDosType(values.GetText(FileSystemField));
        if (highDensity)
        {
            await FastFileSystemFormatter.Format(stream, FloppyDiskConstants.HighDensity.LowCyl,
                FloppyDiskConstants.HighDensity.HighCyl, FloppyDiskConstants.HighDensity.ReservedBlocks,
                FloppyDiskConstants.HighDensity.Heads, FloppyDiskConstants.HighDensity.Sectors,
                FloppyDiskConstants.BlockSize, FloppyDiskConstants.FileSystemBlockSize, dosType, volumeName);
        }
        else
        {
            await FastFileSystemFormatter.Format(stream, FloppyDiskConstants.DoubleDensity.LowCyl,
                FloppyDiskConstants.DoubleDensity.HighCyl, FloppyDiskConstants.DoubleDensity.ReservedBlocks,
                FloppyDiskConstants.DoubleDensity.Heads, FloppyDiskConstants.DoubleDensity.Sectors,
                FloppyDiskConstants.BlockSize, FloppyDiskConstants.FileSystemBlockSize, dosType, volumeName);
        }
        cancellationToken.ThrowIfCancellationRequested();

        if (dosType[3] is 4 or 5)
        {
            var sectors = highDensity ? FloppyDiskConstants.HighDensity.Sectors : FloppyDiskConstants.DoubleDensity.Sectors;
            var blocks = (int)(FloppyDiskConstants.DoubleDensity.Cylinders * FloppyDiskConstants.DoubleDensity.Heads *
                               sectors);
            await CreateRootDirCacheBlock(stream, blocks, cancellationToken);
        }

        if (values.GetBoolean(BootableField))
        {
            // boot block with the dos type, root block offset and the standard boot code
            var bootBlock = BootBlockBuilder.Build(new BootBlock { DosType = dosType }, BootBlockSize);
            stream.Position = 0;
            await stream.WriteAsync(bootBlock, cancellationToken);
        }

        await stream.FlushAsync(cancellationToken);
    }

    /// <summary>
    /// Adds an empty directory cache block to the root block of a disk formatted with directory cache (DOS and
    /// DOS), as the formatter of Hst.Amiga points the root block's directory cache to the root block itself: the first
    /// free block after the root block is marked used in the bitmap and the root block points to it.
    /// </summary>
    private static async Task CreateRootDirCacheBlock(Stream stream, int blocks, CancellationToken cancellationToken)
    {
        const int blockSize = FloppyDiskConstants.BlockSize;
        const int reservedBlocks = 2;
        const int dirCacheType = 33;
        const int rootBitmapPagesOffset = blockSize - 49 * 4;
        const int rootExtensionOffset = blockSize - 2 * 4;
        var rootSector = blocks / 2;

        var root = await ReadBlock(stream, rootSector, cancellationToken);
        var bitmapSector = (int)ReadUInt32(root, rootBitmapPagesOffset);
        var bitmap = await ReadBlock(stream, bitmapSector, cancellationToken);

        // bits set for free blocks, the first long is the checksum
        bool IsFree(int sector)
        {
            var index = sector - reservedBlocks;
            return (ReadUInt32(bitmap, 4 + index / 32 * 4) & (1u << (index % 32))) != 0;
        }

        var dirCacheSector = Enumerable.Range(rootSector + 1, blocks - rootSector - 1)
            .Concat(Enumerable.Range(reservedBlocks, rootSector - reservedBlocks))
            .FirstOrDefault(IsFree);
        if (dirCacheSector == 0)
        {
            throw new IOException("No free block for the directory cache");
        }

        var dirCacheIndex = dirCacheSector - reservedBlocks;
        var mapOffset = 4 + dirCacheIndex / 32 * 4;
        WriteUInt32(bitmap, mapOffset, ReadUInt32(bitmap, mapOffset) & ~(1u << (dirCacheIndex % 32)));
        UpdateChecksum(bitmap, 0);

        // empty directory cache block: type, header key, parent, number of records and next directory cache block
        var dirCache = new byte[blockSize];
        WriteUInt32(dirCache, 0, dirCacheType);
        WriteUInt32(dirCache, 4, (uint)dirCacheSector);
        WriteUInt32(dirCache, 8, (uint)rootSector);
        UpdateChecksum(dirCache, 20);

        WriteUInt32(root, rootExtensionOffset, (uint)dirCacheSector);
        UpdateChecksum(root, 20);

        await WriteBlock(stream, dirCacheSector, dirCache, cancellationToken);
        await WriteBlock(stream, bitmapSector, bitmap, cancellationToken);
        await WriteBlock(stream, rootSector, root, cancellationToken);
    }

    private static async Task<byte[]> ReadBlock(Stream stream, int sector, CancellationToken cancellationToken)
    {
        var block = new byte[FloppyDiskConstants.BlockSize];
        stream.Position = (long)sector * FloppyDiskConstants.BlockSize;
        await stream.ReadExactlyAsync(block, cancellationToken);
        return block;
    }

    private static async Task WriteBlock(Stream stream, int sector, byte[] block, CancellationToken cancellationToken)
    {
        stream.Position = (long)sector * FloppyDiskConstants.BlockSize;
        await stream.WriteAsync(block, cancellationToken);
    }

    private static uint ReadUInt32(byte[] block, int offset) =>
        System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(block.AsSpan(offset));

    private static void WriteUInt32(byte[] block, int offset, uint value) =>
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(block.AsSpan(offset), value);

    /// <summary>Sets the checksum at an offset, so the sum of all longs of the block is zero.</summary>
    private static void UpdateChecksum(byte[] block, int offset)
    {
        WriteUInt32(block, offset, 0);
        uint sum = 0;
        for (var i = 0; i < block.Length; i += 4)
        {
            sum += ReadUInt32(block, i);
        }

        WriteUInt32(block, offset, unchecked(0u - sum));
    }

    private static byte[] GetDosType(string fileSystem)
    {
        // DOS0-DOS7, DOS3 when not chosen
        if (fileSystem.Length == 0)
        {
            fileSystem = DefaultFileSystem;
        }

        if (fileSystem.Length != 4 || !fileSystem.StartsWith("DOS", StringComparison.Ordinal) ||
            fileSystem[3] < '0' || fileSystem[3] > '7')
        {
            throw new IOException($"Invalid file system '{fileSystem}', it must be DOS0 to DOS7");
        }

        return [(byte)'D', (byte)'O', (byte)'S', (byte)(fileSystem[3] - '0')];
    }

    private static void ValidateVolumeName(string volumeName)
    {
        if (volumeName.Length == 0)
        {
            throw new IOException("Volume name is empty");
        }

        if (volumeName.IndexOfAny(['/', ':']) >= 0)
        {
            throw new IOException($"Invalid volume name '{volumeName}', it can't contain '/' or ':'");
        }

        if (volumeName.Length > MaxVolumeNameLength)
        {
            throw new IOException(
                $"Volume name '{volumeName}' is longer than the maximum of {MaxVolumeNameLength} characters");
        }
    }
}
