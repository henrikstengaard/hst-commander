using System.Buffers.Binary;
using SharpCompress.Compressors.ZStandard;

namespace Hst.Commander.Plugins.Archives;

/// <summary>
/// File volume provider opening Zstandard files (28 B5 2F FD at the start), e.g. "disk.img.zst" as a volume with
/// "disk.img", or a zstd compressed tar archive (.tar.zst, .tzst) as the tar archive (see
/// <see cref="CompressedFileVolumeProvider" />), decompressed with SharpCompress' <see cref="DecompressionStream" />,
/// which reads files of several frames and throws for truncated ones. Zstd doesn't store the name of the file, so it's
/// named after the zstd file; its size is the sum of the content sizes in the frame headers, unknown when a frame
/// doesn't store it (e.g. compressed from a pipe).
/// </summary>
public class ZstdFileVolumeProvider : CompressedFileVolumeProvider
{
    private const uint FrameMagic = 0xFD2FB528;
    private const uint SkippableFrameMagic = 0x184D2A50;
    private const uint SkippableFrameMask = 0xFFFFFFF0;
    private const int BlockHeaderLength = 3;
    private const int ChecksumLength = 4;

    public override string Name => "Zstd";

    public override string Description => "Zstandard files and compressed tar archives (.tar.zst, .tzst)";

    protected override byte[] Signature { get; } = [0x28, 0xB5, 0x2F, 0xFD];

    protected override IReadOnlyList<(string Extension, string DecompressedExtension)> Extensions { get; } =
        [(".tzst", ".tar"), (".zst", ""), (".zstd", "")];

    protected override Stream Decompress(Stream stream) =>
        new DecompressionStream(stream, checkEndOfStream: true, leaveOpen: false);

    protected override CompressedFileInfo ReadInfo(Stream stream) => new(null, null, ReadContentSize(stream));

    /// <summary>
    /// Size of the decompressed file: the sum of the content sizes in the headers of the frames, found by walking the
    /// blocks of each frame (skippable frames are skipped); null when a frame doesn't store its content size or the
    /// frames can't be read.
    /// </summary>
    internal static long? ReadContentSize(Stream stream)
    {
        long size = 0;
        var header = new byte[18];
        while (stream.Position < stream.Length)
        {
            var frameStart = stream.Position;
            var length = stream.ReadAtLeast(header, header.Length, false);
            if (length < 8)
            {
                return null;
            }

            var magic = BinaryPrimitives.ReadUInt32LittleEndian(header);
            if ((magic & SkippableFrameMask) == SkippableFrameMagic)
            {
                stream.Position = frameStart + 8 + BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(4));
                continue;
            }

            if (magic != FrameMagic)
            {
                return null;
            }

            var descriptor = header[4];
            var contentSizeFlag = descriptor >> 6;
            var singleSegment = (descriptor & 0x20) != 0;
            var hasChecksum = (descriptor & 0x04) != 0;
            var dictionaryIdLength = (descriptor & 0x03) switch { 0 => 0, 1 => 1, 2 => 2, _ => 4 };
            var contentSizeLength = contentSizeFlag switch { 0 => singleSegment ? 1 : 0, 1 => 2, 2 => 4, _ => 8 };
            if (contentSizeLength == 0)
            {
                return null;
            }

            var offset = 5 + (singleSegment ? 0 : 1) + dictionaryIdLength;
            var field = header.AsSpan(offset, contentSizeLength);
            size += contentSizeLength switch
            {
                1 => field[0],
                2 => BinaryPrimitives.ReadUInt16LittleEndian(field) + 256,
                4 => BinaryPrimitives.ReadUInt32LittleEndian(field),
                _ => (long)BinaryPrimitives.ReadUInt64LittleEndian(field)
            };

            // the blocks: a header of 3 bytes (last block, type and size), raw and compressed blocks of that size,
            // RLE blocks of one byte
            var position = frameStart + offset + contentSizeLength;
            var blockHeader = new byte[BlockHeaderLength];
            bool lastBlock;
            do
            {
                stream.Position = position;
                if (stream.ReadAtLeast(blockHeader, blockHeader.Length, false) < BlockHeaderLength)
                {
                    return null;
                }

                var value = blockHeader[0] | (blockHeader[1] << 8) | (blockHeader[2] << 16);
                lastBlock = (value & 1) != 0;
                var blockType = (value >> 1) & 3;
                var blockSize = value >> 3;
                if (blockType == 3)
                {
                    // reserved, a damaged file, which fails when it's read
                    return null;
                }

                position += BlockHeaderLength + (blockType == 1 ? 1 : blockSize);
            } while (!lastBlock);

            stream.Position = position + (hasChecksum ? ChecksumLength : 0);
        }

        return size;
    }
}
