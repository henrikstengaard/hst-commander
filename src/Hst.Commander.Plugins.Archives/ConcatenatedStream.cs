namespace Hst.Commander.Plugins.Archives;

/// <summary>
/// Read-only stream reading streams one after another, each opened when the one before it ends and disposed then,
/// e.g. the streams of an xz file decompressed one by one.
/// </summary>
internal sealed class ConcatenatedStream(IEnumerable<Func<Stream>> streams) : Stream
{
    private readonly IEnumerator<Func<Stream>> _streams = streams.GetEnumerator();
    private Stream? _current;
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
            if (_current == null)
            {
                if (!_streams.MoveNext())
                {
                    _ended = true;
                    break;
                }

                _current = _streams.Current();
            }

            var length = _current.Read(buffer);
            if (length > 0)
            {
                _position += length;
                return length;
            }

            _current.Dispose();
            _current = null;
        }

        return 0;
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
            _current?.Dispose();
            _current = null;
            _streams.Dispose();
        }

        base.Dispose(disposing);
    }
}
