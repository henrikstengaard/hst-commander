using System.Buffers.Binary;
using System.IO.Compression;

namespace Hst.Commander.Plugins.Archives;

/// <summary>
/// Stream decompressing a gzip file with .NET's <see cref="System.IO.Compression.GZipStream" /> (native zlib, several
/// times faster than <see cref="GzipStream" />), which checks the CRC32 and size of each complete member but ends a
/// truncated file silently. So at the end the CRC32 and size of all the data read are compared with the trailer at the
/// end of the file, which match for a file of one member, the usual case. When they don't (a file of several members,
/// zeros after the last member, or a truncated file), the file is decompressed again with <see cref="GzipStream" />,
/// checking the trailer of each member, which throws <see cref="InvalidDataException" /> for a damaged file. The stream
/// of the file, seekable, is left open.
/// </summary>
internal sealed class CheckedGzipStream(Stream stream) : Stream
{
    private const int TrailerLength = 8;
    private const int MinimumLength = 18;

    private readonly GZipStream _gzip = new(new SharedStream(stream), CompressionMode.Decompress);
    private uint _crc = ArchiveCrc32.Initial;
    private long _position;
    private bool _checked;

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
        var length = _gzip.Read(buffer);
        if (length > 0)
        {
            _crc = ArchiveCrc32.Update(_crc, buffer[..length]);
            _position += length;
        }
        else if (buffer.Length > 0 && !_checked)
        {
            _checked = true;
            Check();
        }

        return length;
    }

    /// <summary>Checks the data read against the trailer at the end of the file, or else the whole file member by member.</summary>
    private void Check()
    {
        if (stream.Length >= MinimumLength)
        {
            var trailer = new byte[TrailerLength];
            stream.Position = stream.Length - TrailerLength;
            stream.ReadExactly(trailer);
            if (BinaryPrimitives.ReadUInt32LittleEndian(trailer) == (_crc ^ ArchiveCrc32.Initial) &&
                BinaryPrimitives.ReadUInt32LittleEndian(trailer.AsSpan(4)) == (uint)_position)
            {
                return;
            }
        }

        long length = 0;
        using (var members = new GzipStream(new SharedStream(stream)))
        {
            var buffer = new byte[1024 * 1024];
            int read;
            while ((read = members.Read(buffer)) > 0)
            {
                length += read;
            }
        }

        if (length != _position)
        {
            throw new InvalidDataException(
                $"The gzip file is damaged: {_position} of {length} bytes could be decompressed");
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
            _gzip.Dispose();
        }

        base.Dispose(disposing);
    }
}
