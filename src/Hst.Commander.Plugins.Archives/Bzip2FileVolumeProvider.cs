using SharpCompress.Compressors;
using SharpCompress.Compressors.BZip2;

namespace Hst.Commander.Plugins.Archives;

/// <summary>
/// File volume provider opening bzip2 files ("BZh", the block size '1' to '9' and the magic of the first block, or of
/// the end of an empty file), e.g. "disk.img.bz2" as a volume with "disk.img", or a bzip2 compressed tar archive
/// (.tar.bz2, .tbz2, .tbz, .tb2) as the tar archive (see <see cref="CompressedFileVolumeProvider" />), decompressed with
/// SharpCompress' <see cref="BZip2Stream" />, which reads joined bzip2 files and throws for truncated ones. Bzip2 stores
/// neither the name nor the size of the file, so it's named after the bzip2 file and its size is unknown.
/// </summary>
public class Bzip2FileVolumeProvider : CompressedFileVolumeProvider
{
    private static readonly byte[] BlockMagic = [0x31, 0x41, 0x59, 0x26, 0x53, 0x59];
    private static readonly byte[] EndMagic = [0x17, 0x72, 0x45, 0x38, 0x50, 0x90];

    public override string Name => "Bzip2";

    public override string Description => "Bzip2 files and compressed tar archives (.tar.bz2, .tbz2)";

    protected override byte[] Signature { get; } = "BZh"u8.ToArray();

    protected override int SignatureLength => 10;

    protected override bool IsSignature(ReadOnlySpan<byte> start) =>
        start[..3].SequenceEqual(Signature) && start[3] is >= (byte)'1' and <= (byte)'9' &&
        (start[4..10].SequenceEqual(BlockMagic) || start[4..10].SequenceEqual(EndMagic));

    protected override IReadOnlyList<(string Extension, string DecompressedExtension)> Extensions { get; } =
        [(".tbz2", ".tar"), (".tbz", ".tar"), (".tb2", ".tar"), (".bz2", ""), (".bz", "")];

    protected override Stream Decompress(Stream stream) =>
        BZip2Stream.Create(stream, CompressionMode.Decompress, decompressConcatenated: true, leaveOpen: false,
            tolerateTruncatedStream: false);

    protected override CompressedFileInfo ReadInfo(Stream stream) => new(null, null, null);
}
