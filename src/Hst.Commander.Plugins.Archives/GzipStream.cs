using System.Buffers.Binary;
using System.Text;
using SharpCompress.Compressors;
using SharpCompress.Compressors.Deflate;

namespace Hst.Commander.Plugins.Archives;

/// <summary>
/// Stream decompressing a gzip file member by member, checking the CRC32 and size in the trailer of each member, so a
/// truncated or damaged file throws <see cref="InvalidDataException" />; used by <see cref="CheckedGzipStream" /> to check
/// files whose last trailer doesn't match all their data, as it's several times slower (.NET's <see cref="System.IO.Compression.GZipStream" />
/// ends a truncated file silently, SharpCompress' GZipStream reads only one member and fails when disposed before the
/// end). Each member is inflated with SharpCompress' <see cref="DeflateStream" />, whose
/// <see cref="DeflateStream.TotalIn" /> tells where its trailer is. Zeros after the last member are ignored, like gzip
/// does. The stream of the file is left open.
/// </summary>
internal sealed class GzipStream(Stream stream) : Stream
{
    private const int HeaderLength = 10;
    private const int TrailerLength = 8;
    private const byte HeaderCrcFlag = 0x02;
    private const byte ExtraFlag = 0x04;
    private const byte NameFlag = 0x08;
    private const byte CommentFlag = 0x10;

    private long _memberStart;
    private long _dataStart;
    private DeflateStream? _deflate;
    private uint _crc;
    private long _memberLength;
    private long _position;
    private bool _ended;

    public override bool CanRead => true;

    public override bool CanSeek => false;

    public override bool CanWrite => false;

    public override long Length => throw new NotSupportedException();

    public override long Position
    {
        get => _position;
        set => throw new NotSupportedException();
    }

    public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

    public override int Read(Span<byte> buffer)
    {
        while (!_ended && buffer.Length > 0)
        {
            if (_deflate == null && !StartMember())
            {
                _ended = true;
                break;
            }

            var length = _deflate!.Read(buffer);
            if (length > 0)
            {
                _crc = ArchiveCrc32.Update(_crc, buffer[..length]);
                _memberLength += length;
                _position += length;
                return length;
            }

            EndMember();
        }

        return 0;
    }

    /// <summary>
    /// Starts the member at <see cref="_memberStart" />, reading its header; false at the end of the file or when only
    /// zeros follow the last member.
    /// </summary>
    private bool StartMember()
    {
        var header = new byte[HeaderLength];
        stream.Position = _memberStart;
        var length = stream.ReadAtLeast(header, header.Length, false);
        if (length == 0 || (_memberStart > 0 && IsZerosToEnd(header.AsSpan(0, length))))
        {
            return false;
        }

        if (length < HeaderLength || header[0] != 0x1F || header[1] != 0x8B || header[2] != 8)
        {
            throw new InvalidDataException(_memberStart == 0
                ? "The file isn't a gzip file"
                : $"The gzip file is damaged: no gzip member at offset {_memberStart}");
        }

        var flags = header[3];
        if ((flags & ExtraFlag) != 0)
        {
            var extraLength = new byte[2];
            ReadExactlyOrThrow(extraLength);
            stream.Position += BinaryPrimitives.ReadUInt16LittleEndian(extraLength);
        }

        if ((flags & NameFlag) != 0)
        {
            SkipZeroTerminated();
        }

        if ((flags & CommentFlag) != 0)
        {
            SkipZeroTerminated();
        }

        if ((flags & HeaderCrcFlag) != 0)
        {
            stream.Position += 2;
        }

        _dataStart = stream.Position;
        _deflate = new DeflateStream(new SharedStream(stream, _dataStart), CompressionMode.Decompress,
            CompressionLevel.Default, Encoding.Latin1);
        _crc = ArchiveCrc32.Initial;
        _memberLength = 0;
        return true;
    }

    /// <summary>Checks the trailer of the member after its deflate data and moves to the next member.</summary>
    private void EndMember()
    {
        var trailerStart = _dataStart + _deflate!.TotalIn;
        _deflate.Dispose();
        _deflate = null;

        var trailer = new byte[TrailerLength];
        stream.Position = trailerStart;
        if (stream.ReadAtLeast(trailer, trailer.Length, false) < TrailerLength)
        {
            throw new InvalidDataException("The gzip file is damaged: it ends before the end of its compressed data");
        }

        if (BinaryPrimitives.ReadUInt32LittleEndian(trailer) != (_crc ^ ArchiveCrc32.Initial) ||
            BinaryPrimitives.ReadUInt32LittleEndian(trailer.AsSpan(4)) != (uint)_memberLength)
        {
            throw new InvalidDataException("The gzip file is damaged: its CRC or size doesn't match");
        }

        _memberStart = trailerStart + TrailerLength;
    }

    /// <summary>True when the bytes read and the rest of the file are zeros.</summary>
    private bool IsZerosToEnd(ReadOnlySpan<byte> start)
    {
        if (start.ContainsAnyExcept((byte)0))
        {
            return false;
        }

        var buffer = new byte[64 * 1024];
        int length;
        while ((length = stream.Read(buffer)) > 0)
        {
            if (buffer.AsSpan(0, length).ContainsAnyExcept((byte)0))
            {
                return false;
            }
        }

        return true;
    }

    private void ReadExactlyOrThrow(byte[] buffer)
    {
        if (stream.ReadAtLeast(buffer, buffer.Length, false) < buffer.Length)
        {
            throw new InvalidDataException("The gzip file is damaged: its header is incomplete");
        }
    }

    private void SkipZeroTerminated()
    {
        int value;
        while ((value = stream.ReadByte()) > 0)
        {
        }

        if (value < 0)
        {
            throw new InvalidDataException("The gzip file is damaged: its header is incomplete");
        }
    }

    public override void Flush()
    {
    }

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _deflate?.Dispose();
            _deflate = null;
        }

        base.Dispose(disposing);
    }
}
