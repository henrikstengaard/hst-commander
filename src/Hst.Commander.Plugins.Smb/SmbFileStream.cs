using SMBLibrary;

namespace Hst.Commander.Plugins.Smb;

/// <summary>
/// Seekable stream reading and writing a file opened on an SMB share, closing it when disposed. Reads and writes go to
/// the server right away in requests of at most the connection's sizes, so nothing is buffered. A stream with a
/// connection of its own (writable images, used by both panes and file operations as long as the image is open)
/// closes it when disposed, and connects again and reopens the file when the server closed the connection, e.g. after
/// being idle.
/// </summary>
public sealed class SmbFileStream : Stream
{
    private readonly Func<SmbOpenedFile>? _reopen;
    private INTFileStore _fileStore;
    private object _handle;
    private ISmbConnection? _connection;
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

    /// <summary>Stream of a file opened on a connection of its own, reopened with <paramref name="reopen" />.</summary>
    /// <param name="file">The opened file and its connection, owned by the stream.</param>
    /// <param name="path">Path of the file, used in error messages.</param>
    /// <param name="length">Length of the file.</param>
    /// <param name="canWrite">True when the file is opened for writing.</param>
    /// <param name="reopen">Connects again and opens the file the same way, after the server closed the connection.</param>
    public SmbFileStream(SmbOpenedFile file, string path, long length, bool canWrite, Func<SmbOpenedFile> reopen)
        : this(file.FileStore, file.Handle, path, length, true, canWrite, file.Connection.MaxReadSize,
            file.Connection.MaxWriteSize)
    {
        ArgumentNullException.ThrowIfNull(reopen);
        _connection = file.Connection;
        _reopen = reopen;
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

        // reads until the buffer is full or the end of the file, as images read blocks with one read (e.g. the Amiga
        // file systems), and a request reads at most the connection's read size
        var total = 0;
        while (total < buffer.Length && _position < _length)
        {
            var count = (int)Math.Min(Math.Min(buffer.Length - total, _maxReadSize), _length - _position);
            byte[] data = [];
            var status = Request(() => _fileStore.ReadFile(out data, _handle, _position, count));
            if (status == NTStatus.STATUS_END_OF_FILE)
            {
                break;
            }

            SmbStatus.ThrowIfFailed(status, $"Reading '{_path}'");
            if (data.Length == 0)
            {
                break;
            }

            var read = Math.Min(data.Length, buffer.Length - total);
            data.AsSpan(0, read).CopyTo(buffer[total..]);
            _position += read;
            total += read;
        }

        return total;
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
            var written = 0;
            var status = Request(() => _fileStore.WriteFile(out written, _handle, _position, chunk));
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

        var status = Request(() =>
            _fileStore.SetFileInformation(_handle, new FileEndOfFileInformation { EndOfFile = value }));
        SmbStatus.ThrowIfFailed(status, $"Setting the length of '{_path}'");
        _length = value;
    }

    /// <summary>
    ///     Sends a request, and with a connection of its own sends it again on a new connection when it failed because
    ///     the server closed the connection.
    /// </summary>
    private NTStatus Request(Func<NTStatus> request)
    {
        var status = request();
        if (status is NTStatus.STATUS_SUCCESS or NTStatus.STATUS_END_OF_FILE || _reopen == null ||
            _connection is not { IsConnected: false })
        {
            return status;
        }

        _connection.Dispose();
        _connection = null;
        var file = _reopen();
        _connection = file.Connection;
        _fileStore = file.FileStore;
        _handle = file.Handle;
        return request();
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

            _connection?.Dispose();
            _connection = null;
        }

        base.Dispose(disposing);
    }
}

/// <summary>A file opened on a share of a connection used only for it.</summary>
/// <param name="Connection">Connection the file is opened on, closed with the file's stream.</param>
/// <param name="FileStore">Share the file is opened on.</param>
/// <param name="Handle">Handle of the opened file.</param>
public sealed record SmbOpenedFile(ISmbConnection Connection, INTFileStore FileStore, object Handle);
