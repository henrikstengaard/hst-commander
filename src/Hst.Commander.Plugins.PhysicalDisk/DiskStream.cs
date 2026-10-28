using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Hst.Commander.Plugins.PhysicalDisk;

/// <summary>
/// Seekable stream of a physical disk. Devices only read and write whole sectors at sector offsets (e.g. Windows
/// physical drives and macOS raw disks), so all reads and writes go through a buffer aligned in memory, and partial
/// sectors are written by reading the sectors first. Not buffered: changes are written to the disk right away.
/// </summary>
public sealed class DiskStream : Stream
{
    /// <summary>Bytes read or written at most at a time, a multiple of the sector sizes.</summary>
    private const int ChunkSize = 1024 * 1024;

    /// <summary>Alignment of the buffer in memory, the largest sector size.</summary>
    private const int Alignment = 4096;

    private readonly SafeFileHandle _handle;
    private readonly int _sectorSize;
    private readonly bool _canWrite;
    private readonly IDisposable? _resource;
    private readonly byte[] _array;
    private readonly int _bufferOffset;
    private long _position;
    private bool _written;

    /// <param name="handle">Handle of the device, disposed with the stream.</param>
    /// <param name="length">Size of the disk in bytes.</param>
    /// <param name="sectorSize">Size of the sectors in bytes, a power of two of at most 4096.</param>
    /// <param name="canWrite">True when the handle is opened for writing.</param>
    /// <param name="resource">Resource disposed after the handle, e.g. locked volumes of the disk.</param>
    public DiskStream(SafeFileHandle handle, long length, int sectorSize, bool canWrite,
        IDisposable? resource = null)
    {
        ArgumentNullException.ThrowIfNull(handle);
        if (sectorSize is <= 0 or > Alignment || (sectorSize & (sectorSize - 1)) != 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sectorSize), sectorSize, "Invalid sector size");
        }

        _handle = handle;
        Length = length;
        _sectorSize = sectorSize;
        _canWrite = canWrite;
        _resource = resource;
        _array = GC.AllocateUninitializedArray<byte>(ChunkSize + Alignment, true);
        var address = Marshal.UnsafeAddrOfPinnedArrayElement(_array, 0);
        _bufferOffset = (int)((Alignment - address % Alignment) % Alignment);
    }

    public override bool CanRead => !_handle.IsClosed;

    public override bool CanSeek => !_handle.IsClosed;

    public override bool CanWrite => _canWrite && !_handle.IsClosed;

    public override long Length { get; }

    public override long Position
    {
        get => _position;
        set
        {
            ArgumentOutOfRangeException.ThrowIfNegative(value);
            _position = value;
        }
    }

    private Span<byte> Buffer => _array.AsSpan(_bufferOffset, ChunkSize);

    public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

    public override int Read(Span<byte> buffer)
    {
        ObjectDisposedException.ThrowIf(_handle.IsClosed, this);

        var total = 0;
        while (buffer.Length > 0 && _position < Length)
        {
            var (alignedOffset, offsetInChunk, count, alignedLength) =
                GetChunk((int)Math.Min(buffer.Length, Length - _position));
            var read = ReadSectors(alignedOffset, alignedLength);
            count = (int)Math.Min(count, read - offsetInChunk);
            if (count <= 0)
            {
                break;
            }

            Buffer.Slice(offsetInChunk, count).CopyTo(buffer);
            buffer = buffer[count..];
            _position += count;
            total += count;
        }

        return total;
    }

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(Read(buffer, offset, count));
    }

    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(Read(buffer.Span));
    }

    public override void Write(byte[] buffer, int offset, int count) => Write(buffer.AsSpan(offset, count));

    public override void Write(ReadOnlySpan<byte> buffer)
    {
        ObjectDisposedException.ThrowIf(_handle.IsClosed, this);
        if (!_canWrite)
        {
            throw new NotSupportedException("The disk is opened read-only");
        }

        if (_position + buffer.Length > Length)
        {
            throw new IOException("Writing past the end of the disk");
        }

        while (buffer.Length > 0)
        {
            var (alignedOffset, offsetInChunk, count, alignedLength) = GetChunk(buffer.Length);

            // partial sectors keep the rest of their bytes
            if (offsetInChunk != 0 || count != alignedLength)
            {
                if (ReadSectors(alignedOffset, alignedLength) < alignedLength)
                {
                    throw new IOException("The sectors written can't be read");
                }
            }

            buffer[..count].CopyTo(Buffer.Slice(offsetInChunk, count));
            RandomAccess.Write(_handle, Buffer[..alignedLength], alignedOffset);
            _written = true;
            buffer = buffer[count..];
            _position += count;
        }
    }

    public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Write(buffer, offset, count);
        return Task.CompletedTask;
    }

    public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Write(buffer.Span);
        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// The sectors of the next chunk at the position: their offset, the offset of the position in them, the number of
    /// bytes of at most <paramref name="count" /> in them and their length.
    /// </summary>
    private (long AlignedOffset, int OffsetInChunk, int Count, int AlignedLength) GetChunk(int count)
    {
        var offsetInChunk = (int)(_position % _sectorSize);
        var alignedOffset = _position - offsetInChunk;
        count = Math.Min(count, ChunkSize - offsetInChunk);
        var alignedLength = (offsetInChunk + count + _sectorSize - 1) / _sectorSize * _sectorSize;
        return (alignedOffset, offsetInChunk, count, alignedLength);
    }

    /// <summary>Reads sectors into the buffer, returning the bytes read, less at the end of the disk.</summary>
    private int ReadSectors(long offset, int length)
    {
        var total = 0;
        while (total < length)
        {
            var read = RandomAccess.Read(_handle, Buffer.Slice(total, length - total), offset + total);
            if (read == 0)
            {
                break;
            }

            total += read;
        }

        return total;
    }

    public override long Seek(long offset, SeekOrigin origin) => Position = origin switch
    {
        SeekOrigin.Begin => offset,
        SeekOrigin.Current => _position + offset,
        SeekOrigin.End => Length + offset,
        _ => throw new ArgumentOutOfRangeException(nameof(origin), origin, null)
    };

    /// <summary>Flushes the writes to the disk, e.g. from the page cache of a Linux block device.</summary>
    public override void Flush()
    {
        if (_written && !_handle.IsClosed)
        {
            NativeMethods.FlushToDisk(_handle);
            _written = false;
        }
    }

    public override void SetLength(long value) => throw new NotSupportedException("The size of a disk can't be changed");

    protected override void Dispose(bool disposing)
    {
        if (disposing && !_handle.IsClosed)
        {
            try
            {
                Flush();
            }
            finally
            {
                _handle.Dispose();
                _resource?.Dispose();
            }
        }

        base.Dispose(disposing);
    }
}
