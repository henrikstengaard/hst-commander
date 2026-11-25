using System.Buffers.Binary;
using System.Text;

namespace Hst.Commander.Plugins.RarSevenZip;

/// <summary>
/// File volume provider opening gzip files (1F 8B 08 at the start), e.g. "disk.img.gz" as a volume with "disk.img", or
/// a gzip compressed tar archive (.tar.gz, .tgz) as the tar archive (see <see cref="CompressedFileVolumeProvider" />).
/// The decompressed file is named by the name in the gzip header, or else after the gzip file. Gzip only stores the size
/// modulo 4 GB (of the last member), so it's shown but the file isn't checked against it; <see cref="CheckedGzipStream" />
/// checks the CRC32 and size of each member instead, also reading files of several members.
/// </summary>
public class GzipFileVolumeProvider : CompressedFileVolumeProvider
{
    private const int HeaderLength = 10;
    private const byte ExtraFlag = 0x04;
    private const byte NameFlag = 0x08;

    public override string Name => "Gzip";

    public override string Description => "Gzip files and compressed tar archives (.tar.gz, .tgz)";

    protected override byte[] Signature { get; } = [0x1F, 0x8B, 0x08];

    protected override IReadOnlyList<(string Extension, string DecompressedExtension)> Extensions { get; } =
        [(".tgz", ".tar"), (".gz", ""), (".gzip", ""), (".z", "")];

    protected override Stream Decompress(Stream stream) => new CheckedGzipStream(stream);

    /// <summary>
    /// Reads the name (Latin-1, flag 0x08) and modified date (seconds since 1970 at 4, 0 when unknown) from the gzip
    /// header and the size modulo 4 GB from the last 4 bytes.
    /// </summary>
    protected override CompressedFileInfo ReadInfo(Stream stream)
    {
        var header = new byte[HeaderLength];
        stream.ReadExactly(header);
        var flags = header[3];
        var modifiedTime = BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(4));

        if ((flags & ExtraFlag) != 0)
        {
            var extraLength = new byte[2];
            stream.ReadExactly(extraLength);
            stream.Position += BinaryPrimitives.ReadUInt16LittleEndian(extraLength);
        }

        string? name = null;
        if ((flags & NameFlag) != 0)
        {
            var nameBytes = new List<byte>();
            int value;
            while ((value = stream.ReadByte()) > 0)
            {
                nameBytes.Add((byte)value);
            }

            name = nameBytes.Count > 0 ? Encoding.Latin1.GetString(nameBytes.ToArray()) : null;
        }

        long? storedSize = null;
        if (stream.Length >= HeaderLength + 8)
        {
            var size = new byte[4];
            stream.Position = stream.Length - size.Length;
            stream.ReadExactly(size);
            storedSize = BinaryPrimitives.ReadUInt32LittleEndian(size);
        }

        return new CompressedFileInfo(name,
            modifiedTime != 0 ? DateTime.UnixEpoch.AddSeconds(modifiedTime) : null, null, storedSize);
    }
}
