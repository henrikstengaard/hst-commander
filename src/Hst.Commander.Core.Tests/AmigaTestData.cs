using DiscUtils.Streams;
using Hst.Amiga;
using Hst.Amiga.Extensions;
using Hst.Amiga.FileSystems.FastFileSystem;
using Hst.Amiga.FileSystems.Pfs3;
using Hst.Amiga.RigidDiskBlocks;
using File = System.IO.File;

namespace Hst.Commander.Core.Tests;

/// <summary>Creates Amiga disk files and hard disk images with a rigid disk block for tests.</summary>
public static class AmigaTestData
{
    public const long AdfSize = 901120;
    private const long DiskSize = 20 * 1024 * 1024;
    private const long PartitionSize = 8 * 1024 * 1024;

    public static readonly byte[] Dos3DosType = [0x44, 0x4f, 0x53, 0x3];
    public static readonly byte[] Pfs3DosType = [0x50, 0x46, 0x53, 0x3];

    /// <summary>Creates a double density adf formatted with the Fast File System.</summary>
    public static async Task CreateAdf(string path)
    {
        await using var stream = File.Create(path);
        stream.SetLength(AdfSize);
        await FastFileSystemFormatter.Format(stream, FloppyDiskConstants.DoubleDensity.LowCyl,
            FloppyDiskConstants.DoubleDensity.HighCyl, FloppyDiskConstants.DoubleDensity.ReservedBlocks,
            FloppyDiskConstants.DoubleDensity.Heads, FloppyDiskConstants.DoubleDensity.Sectors,
            FloppyDiskConstants.BlockSize, FloppyDiskConstants.BlockSize, Dos3DosType, "Workbench");
    }

    /// <summary>
    /// Creates a raw hard disk image with a rigid disk block and partitions of the dos types, the Fast File System
    /// (DOS\3) and PFS3 partitions formatted.
    /// </summary>
    public static async Task CreateRdbImage(string path, params byte[][] dosTypes)
    {
        await using var stream = File.Create(path);
        await WriteRdb(stream, dosTypes);
    }

    /// <summary>Creates a dynamic vhd image with a rigid disk block like <see cref="CreateRdbImage" />.</summary>
    public static async Task CreateVhdRdbImage(string path, params byte[][] dosTypes)
    {
        await using var stream = File.Create(path);
        using var disk = DiscUtils.Vhd.Disk.InitializeDynamic(stream, Ownership.None, DiskSize);
        await WriteRdb(disk.Content, dosTypes, false);
    }

    private static async Task WriteRdb(Stream stream, byte[][] dosTypes, bool setLength = true)
    {
        var rigidDiskBlock = DiskSize.CreateRigidDiskBlock();
        for (var index = 0; index < dosTypes.Length; index++)
        {
            rigidDiskBlock.AddPartition(dosTypes[index], $"DH{index}", PartitionSize);
        }

        if (setLength)
        {
            await rigidDiskBlock.WriteToStream(stream);
        }
        else
        {
            await RigidDiskBlockWriter.WriteBlock(rigidDiskBlock, stream);
        }

        foreach (var partitionBlock in rigidDiskBlock.PartitionBlocks)
        {
            if (partitionBlock.DosType.SequenceEqual(Pfs3DosType))
            {
                await Pfs3Formatter.FormatPartition(stream, partitionBlock, partitionBlock.DriveName);
            }
            else if (partitionBlock.DosType.SequenceEqual(Dos3DosType))
            {
                await FastFileSystemFormatter.FormatPartition(stream, partitionBlock, partitionBlock.DriveName);
            }
        }
    }

    /// <summary>Names of the entries in the root of an adf, mounted again from its file.</summary>
    public static async Task<IReadOnlyList<string>> ListAdfNames(string path, params string[] directories)
    {
        await using var stream = File.OpenRead(path);
        var volume = await FastFileSystemVolume.MountAdf(stream);
        foreach (var directory in directories)
        {
            await volume.ChangeDirectory(directory);
        }

        return (await volume.ListEntries()).Select(entry => entry.Name).Order(StringComparer.Ordinal).ToList();
    }
}
