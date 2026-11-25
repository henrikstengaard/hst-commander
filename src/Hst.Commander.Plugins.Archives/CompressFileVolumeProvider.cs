using SharpCompress.Compressors.Lzw;

namespace Hst.Commander.Plugins.Archives;

/// <summary>
/// File volume provider opening files compressed with Unix compress (LZW, 1F 9D at the start), e.g. "disk.img.Z" as a
/// volume with "disk.img", or a compressed tar archive (.tar.Z, .taz) as the tar archive (see
/// <see cref="CompressedFileVolumeProvider" />), decompressed with SharpCompress' <see cref="LzwStream" />. Compress
/// stores neither the name nor the size of the file, so it's named after the compressed file and its size is unknown,
/// and it has no checksum, so a damaged file can't be told from an intact one.
/// </summary>
public class CompressFileVolumeProvider : CompressedFileVolumeProvider
{
    public override string Name => "Compress";

    public override string Description => "Files compressed with compress (.Z) and compressed tar archives (.tar.Z)";

    protected override byte[] Signature { get; } = [0x1F, 0x9D];

    protected override IReadOnlyList<(string Extension, string DecompressedExtension)> Extensions { get; } =
        [(".taz", ".tar"), (".tz", ".tar"), (".z", "")];

    protected override Stream Decompress(Stream stream) => new LzwStream(stream);

    protected override CompressedFileInfo ReadInfo(Stream stream) => new(null, null, null);
}
