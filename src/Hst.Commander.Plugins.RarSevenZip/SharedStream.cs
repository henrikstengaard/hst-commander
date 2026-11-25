namespace Hst.Commander.Plugins.RarSevenZip;

/// <summary>
/// Read-only stream of a part of a seekable stream (from <paramref name="start" />, <paramref name="length" /> bytes or
/// to its end) keeping a position of its own, so several streams can read the same stream one after another, e.g. a
/// decompressed file opened twice. The stream is left open when this one is disposed.
/// </summary>
internal sealed class SharedStream(Stream stream, long start = 0, long? length = null) : Stream
{
    private long _position;

    public override bool CanRead => true;

    public override bool CanSeek => true;

    public override bool CanWrite => false;

    public override long Length => length ?? stream.Length - start;

    public override long Position
    {
        get => _position;
        set => _position = value;
    }

    public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

    public override int Read(Span<byte> buffer)
    {
        var remaining = Length - _position;
        if (remaining <= 0)
        {
            return 0;
        }

        if (buffer.Length > remaining)
        {
            buffer = buffer[..(int)remaining];
        }

        stream.Position = start + _position;
        var read = stream.Read(buffer);
        _position += read;
        return read;
    }

    public override long Seek(long offset, SeekOrigin origin) => _position = origin switch
    {
        SeekOrigin.Begin => offset,
        SeekOrigin.Current => _position + offset,
        SeekOrigin.End => Length + offset,
        _ => throw new ArgumentOutOfRangeException(nameof(origin), origin, null)
    };

    public override void Flush()
    {
    }

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}
