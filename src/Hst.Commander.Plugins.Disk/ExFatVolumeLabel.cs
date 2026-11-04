using System.Buffers.Binary;
using System.Text;

namespace Hst.Commander.Plugins.Disk;

/// <summary>
/// Reads the volume label of an exFAT file system, which DiscUtils' exFAT file system doesn't return: the volume label
/// directory entry (type 0x83) in the root directory, found by following the root directory's clusters in the FAT.
/// </summary>
internal static class ExFatVolumeLabel
{
    private const int DirectoryEntrySize = 32;
    private const byte EndOfDirectoryType = 0x00;
    private const byte VolumeLabelType = 0x83;
    private const int MaxLabelLength = 11;
    private const uint FirstCluster = 2;
    private const uint LastValidCluster = 0xFFFFFFF6;

    /// <summary>Most clusters read from the root directory before giving up, guarding against FAT loops.</summary>
    private const int MaxRootDirectoryClusters = 256;

    /// <summary>
    /// Volume label of the exFAT file system in a seekable stream (e.g. a partition), the empty string when it has
    /// none or the file system can't be read.
    /// </summary>
    public static string Read(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);

        try
        {
            stream.Position = 0;
            var bootSector = new byte[512];
            stream.ReadExactly(bootSector);
            var fatOffset = BinaryPrimitives.ReadUInt32LittleEndian(bootSector.AsSpan(0x50));
            var clusterHeapOffset = BinaryPrimitives.ReadUInt32LittleEndian(bootSector.AsSpan(0x58));
            var clusterCount = BinaryPrimitives.ReadUInt32LittleEndian(bootSector.AsSpan(0x5C));
            var rootDirectoryCluster = BinaryPrimitives.ReadUInt32LittleEndian(bootSector.AsSpan(0x60));
            int bytesPerSectorShift = bootSector[0x6C];
            int sectorsPerClusterShift = bootSector[0x6D];
            if (bytesPerSectorShift is < 9 or > 12 || sectorsPerClusterShift > 25 - bytesPerSectorShift)
            {
                return string.Empty;
            }

            var sectorSize = 1L << bytesPerSectorShift;
            var clusterSize = 1 << (bytesPerSectorShift + sectorsPerClusterShift);
            var cluster = new byte[clusterSize];
            var clusterNumber = rootDirectoryCluster;
            for (var read = 0;
                 read < MaxRootDirectoryClusters && clusterNumber >= FirstCluster &&
                 clusterNumber < FirstCluster + clusterCount;
                 read++)
            {
                stream.Position = clusterHeapOffset * sectorSize + (clusterNumber - FirstCluster) * (long)clusterSize;
                stream.ReadExactly(cluster);
                for (var offset = 0; offset < clusterSize; offset += DirectoryEntrySize)
                {
                    switch (cluster[offset])
                    {
                        case EndOfDirectoryType:
                            return string.Empty;
                        case VolumeLabelType:
                            var length = Math.Min((int)cluster[offset + 1], MaxLabelLength);
                            return Encoding.Unicode.GetString(cluster, offset + 2, length * 2);
                    }
                }

                // the next cluster of the root directory in the FAT
                var fatEntry = new byte[4];
                stream.Position = fatOffset * sectorSize + clusterNumber * 4L;
                stream.ReadExactly(fatEntry);
                clusterNumber = BinaryPrimitives.ReadUInt32LittleEndian(fatEntry);
                if (clusterNumber > LastValidCluster)
                {
                    break;
                }
            }
        }
        catch (EndOfStreamException)
        {
        }

        return string.Empty;
    }
}
