using System.Buffers.Binary;
using SharpCompress.Compressors.Xz;

namespace Hst.Commander.Plugins.RarSevenZip;

/// <summary>
/// File volume provider opening xz files (FD "7zXZ" 00 at the start), e.g. "disk.img.xz" as a volume with "disk.img",
/// or an xz compressed tar archive (.tar.xz, .txz) as the tar archive (see <see cref="CompressedFileVolumeProvider" />),
/// decompressed with SharpCompress' <see cref="XZStream" />. Xz doesn't store the name of the file, so it's named after
/// the xz file; its size is the sum of the uncompressed sizes in the indexes of the streams. An xz file of several
/// streams (e.g. xz files joined) is decompressed stream by stream, as <see cref="XZStream" /> stops after the first;
/// the streams are found from the end of the file by their footers and indexes.
/// </summary>
public class XzFileVolumeProvider : CompressedFileVolumeProvider
{
    private const int StreamHeaderLength = 12;
    private const int StreamFooterLength = 12;
    private static readonly byte[] FooterMagic = "YZ"u8.ToArray();

    public override string Name => "Xz";

    public override string Description => "Xz files and compressed tar archives (.tar.xz, .txz)";

    protected override byte[] Signature { get; } = [0xFD, 0x37, 0x7A, 0x58, 0x5A, 0x00];

    protected override IReadOnlyList<(string Extension, string DecompressedExtension)> Extensions { get; } =
        [(".txz", ".tar"), (".xz", "")];

    protected override Stream Decompress(Stream stream)
    {
        if (ReadStreams(stream) is not { Count: > 1 } xzStreams)
        {
            stream.Position = 0;
            return new XZStream(stream);
        }

        return new ConcatenatedStream(xzStreams.Select(xzStream =>
            (Func<Stream>)(() => new XZStream(new SharedStream(stream, xzStream.Start, xzStream.Length)))));
    }

    protected override CompressedFileInfo ReadInfo(Stream stream) =>
        new(null, null, ReadStreams(stream)?.Sum(xzStream => xzStream.UncompressedSize));

    /// <summary>
    /// Offset, length and uncompressed size of the streams of an xz file in order, read from the last stream to the
    /// first by the footer and index of each (streams may be followed by padding of zeros); the uncompressed size of a
    /// stream is the sum of the uncompressed sizes of its blocks in its index. Null when a stream can't be read.
    /// </summary>
    internal static IReadOnlyList<(long Start, long Length, long UncompressedSize)>? ReadStreams(Stream stream)
    {
        var end = stream.Length;
        var streams = new List<(long Start, long Length, long UncompressedSize)>();
        var footer = new byte[StreamFooterLength];
        while (end > 0)
        {
            // stream padding, multiples of 4 zeros
            var padding = new byte[4];
            while (end >= 4)
            {
                stream.Position = end - 4;
                stream.ReadExactly(padding);
                if (padding.AsSpan().ContainsAnyExcept((byte)0))
                {
                    break;
                }

                end -= 4;
            }

            if (end < StreamHeaderLength + StreamFooterLength)
            {
                return null;
            }

            stream.Position = end - StreamFooterLength;
            stream.ReadExactly(footer);
            if (!footer.AsSpan(10, 2).SequenceEqual(FooterMagic))
            {
                return null;
            }

            var indexSize = ((long)BinaryPrimitives.ReadUInt32LittleEndian(footer.AsSpan(4)) + 1) * 4;
            var indexStart = end - StreamFooterLength - indexSize;
            if (indexStart < StreamHeaderLength || indexSize > int.MaxValue)
            {
                return null;
            }

            var index = new byte[indexSize];
            stream.Position = indexStart;
            stream.ReadExactly(index);
            if (ReadIndex(index) is not var (uncompressedSize, blocksSize))
            {
                return null;
            }

            var start = indexStart - blocksSize - StreamHeaderLength;
            if (start < 0)
            {
                return null;
            }

            streams.Insert(0, (start, end - start, uncompressedSize));
            end = start;
        }

        stream.Position = 0;
        return streams;
    }

    /// <summary>
    /// Sum of the uncompressed sizes and of the sizes of the blocks (unpadded sizes padded to 4 bytes) in an index:
    /// the indicator 00, the number of records and a record of an unpadded and an uncompressed size per block, as
    /// variable-length integers.
    /// </summary>
    private static (long UncompressedSize, long BlocksSize)? ReadIndex(ReadOnlySpan<byte> index)
    {
        if (index.Length == 0 || index[0] != 0)
        {
            return null;
        }

        var offset = 1;
        if (ReadVarint(index, ref offset) is not { } recordCount)
        {
            return null;
        }

        long uncompressedSize = 0;
        long blocksSize = 0;
        for (long record = 0; record < recordCount; record++)
        {
            if (ReadVarint(index, ref offset) is not { } unpaddedSize ||
                ReadVarint(index, ref offset) is not { } recordSize)
            {
                return null;
            }

            blocksSize += (unpaddedSize + 3) & ~3L;
            uncompressedSize += recordSize;
        }

        return (uncompressedSize, blocksSize);
    }

    /// <summary>Variable-length integer: 7 bits per byte, least significant first, at most 9 bytes.</summary>
    private static long? ReadVarint(ReadOnlySpan<byte> bytes, ref int offset)
    {
        long value = 0;
        for (var shift = 0; shift < 63; shift += 7)
        {
            if (offset >= bytes.Length)
            {
                return null;
            }

            var current = bytes[offset++];
            value |= (long)(current & 0x7F) << shift;
            if ((current & 0x80) == 0)
            {
                return value;
            }
        }

        return null;
    }
}
