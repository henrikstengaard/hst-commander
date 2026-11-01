using SMBLibrary;

namespace Hst.Commander.Plugins.Smb;

/// <summary>
/// Seekable stream reading and writing a file opened on an SMB share, closing it when disposed. Reads and writes go to
/// the server right away in requests of at most the connection's sizes, so nothing is buffered.
/// </summary>
public sealed class SmbFileStream : Stream
{
    private readonly INTFileStore _fileStore;
    private readonly object _handle;
    private readonly string _path;
    private readonly bool _canRead;
    private readonly bool _canWrite;
    private readonly int _maxReadSize;
    private readonly int _maxWriteSize;
    private long _length;
    private long _position;
    private bool _disposed;

    /// <param name="fileStore">Share the file is opened on.</param>
    /// <param name="handle">Handle of the opened file, closed when the stream is disposed.</param>
    /// <param name="path">Path of the file in the share, used in error messages.</param>
    /// <param name="length">Length of the file.</param>
    /// <param name="canRead">True when the file is opened for reading.</param>
    /// <param name="canWrite">True when the file is opened for writing.</param>
    /// <param name="maxReadSize">Most bytes read with one request.</param>
    /// <param name="maxWriteSize">Most bytes written with one request.</param>
    public SmbFileStream(INTFileStore fileStore, object handle, string path, long length, bool canRead, bool canWrite,
        int maxReadSize, int maxWriteSize)
    {
        ArgumentNullException.ThrowIfNull(fileStore);
        ArgumentNullException.ThrowIfNull(handle);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxReadSize);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxWriteSize);

        _fileStore = fileStore;
        _handle = handle;
        _path = path;
        _length = length;
        _canRead = canRead;
        _canWrite = canWrite;
        _maxReadSize = maxReadSize;
        _maxWriteSize = maxWriteSize;
    }

    public override bool CanRead => !_disposed && _canRead;

    public override bool CanSeek => !_disposed;

    public override bool CanWrite => !_disposed && _canWrite;

    public override long Length
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return _length;
        }
    }

    public override long Position
    {
        get => _position;
        set
        {
            ArgumentOutOfRangeException.ThrowIfNegative(value);
            _position = value;
        }
    }

    public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

    public override int Read(Span<byte> buffer)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!_canRead)
        {
            throw new NotSupportedException("The stream can't be read");
        }

        if (buffer.Length == 0 || _position >= _length)
        {
            return 0;
        }

        var count = (int)Math.Min(Math.Min(buffer.Length, _maxReadSize), _length - _position);
        var status = _fileStore.ReadFile(out var data, _handle, _position, count);
        if (status == NTStatus.STATUS_END_OF_FILE)
        {
            return 0;
        }

        SmbStatus.ThrowIfFailed(status, $"Reading '{_path}'");
        var read = Math.Min(data.Length, buffer.Length);
        data.AsSpan(0, read).CopyTo(buffer);
        _position += read;
        return read;
    }

    public override void Write(byte[] buffer, int offset, int count) => Write(buffer.AsSpan(offset, count));

    public override void Write(ReadOnlySpan<byte> buffer)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!_canWrite)
        {
            throw new NotSupportedException("The stream can't be written");
        }

        while (buffer.Length > 0)
        {
            var chunk = buffer[..Math.Min(buffer.Length, _maxWriteSize)].ToArray();
            var status = _fileStore.WriteFile(out var written, _handle, _position, chunk);
            SmbStatus.ThrowIfFailed(status, $"Writing '{_path}'");
            if (written <= 0)
            {
                throw new IOException($"Writing '{_path}' failed: nothing was written");
            }

            _position += written;
            _length = Math.Max(_length, _position);
            buffer = buffer[written..];
        }
    }

    public override long Seek(long offset, SeekOrigin origin)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        Position = origin switch
        {
            SeekOrigin.Begin => offset,
            SeekOrigin.Current => _position + offset,
            SeekOrigin.End => _length + offset,
            _ => throw new ArgumentOutOfRangeException(nameof(origin))
        };
        return _position;
    }

    public override void SetLength(long value)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentOutOfRangeException.ThrowIfNegative(value);
        if (!_canWrite)
        {
            throw new NotSupportedException("The stream can't be written");
        }

        var status = _fileStore.SetFileInformation(_handle, new FileEndOfFileInformation { EndOfFile = value });
        SmbStatus.ThrowIfFailed(status, $"Setting the length of '{_path}'");
        _length = value;
    }

    /// <summary>Writes go to the server right away, so there is nothing to flush.</summary>
    public override void Flush()
    {
    }

    protected override void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            _disposed = true;
            try
            {
                _fileStore.CloseFile(_handle);
            }
            catch (Exception)
            {
                // the connection may be closed already
            }
        }

        base.Dispose(disposing);
    }
}
