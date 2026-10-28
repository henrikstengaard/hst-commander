using Hst.Commander.Abstractions;

namespace Hst.Commander.Plugins.PhysicalDisk;

/// <summary>
/// Stream caching blocks of a disk in memory, so the many small reads and writes of file systems (e.g. sectors of
/// allocation tables and directories) read and write the disk in large blocks. Reads and writes of whole blocks not in
/// the cache, e.g. reading a whole disk or the data of a large file, go to the disk directly so they don't push out
/// the cached blocks. Writes are cached until committed (see <see cref="IWriteCache" />), and when the cache is full
/// the least recently used block not written is dropped, or all writes are written when every block holds writes.
/// <see cref="Flush" /> doesn't write them, as file systems flush after each change, e.g. PFS3. Not thread safe.
/// </summary>
public sealed class CachedDiskStream : Stream, IWriteCache
{
    /// <summary>Size of the blocks cached, a multiple of the sector sizes.</summary>
    public const int BlockSize = 64 * 1024;

    /// <summary>Bytes written at most at a time when writing consecutive blocks.</summary>
    private const int MaxWriteLength = 1024 * 1024;

    private readonly Stream _stream;
    private readonly int _maxBlocks;
    private readonly Dictionary<long, LinkedListNode<Block>> _blocks = new();

    /// <summary>The cached blocks, most recently used first.</summary>
    private readonly LinkedList<Block> _usage = new();

    private byte[]? _writeBuffer;
    private int _dirtyBlocks;
    private long _dirtyBytes;
    private long _position;
    private bool _disposed;

    /// <param name="stream">Seekable stream of the disk, disposed with the stream.</param>
    /// <param name="cacheSize">Most bytes cached, at least <see cref="BlockSize" />.</param>
    public CachedDiskStream(Stream stream, long cacheSize)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentOutOfRangeException.ThrowIfLessThan(cacheSize, BlockSize);

        _stream = stream;
        _maxBlocks = (int)Math.Min(cacheSize / BlockSize, int.MaxValue);
        Length = stream.Length;
    }

    public override bool CanRead => !_disposed && _stream.CanRead;

    public override bool CanSeek => !_disposed && _stream.CanSeek;

    public override bool CanWrite => !_disposed && _stream.CanWrite;

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

    public bool HasCachedWrites => _dirtyBlocks > 0;

    public long CachedWriteBytes => _dirtyBytes;

    /// <summary>True when half of the cache holds writes.</summary>
    public bool ShouldFlush => _dirtyBlocks > 0 && _dirtyBlocks >= _maxBlocks / 2;

    public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

    public override int Read(Span<byte> buffer)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var total = (int)Math.Clamp(Length - _position, 0, buffer.Length);
        buffer = buffer[..total];
        while (buffer.Length > 0)
        {
            var (index, offsetInBlock) = GetBlockPosition();
            if (offsetInBlock == 0 && GetUncachedLength(index, buffer.Length) is var uncachedLength and > 0)
            {
                _stream.Position = _position;
                _stream.ReadExactly(buffer[..uncachedLength]);
                buffer = buffer[uncachedLength..];
                _position += uncachedLength;
                continue;
            }

            var block = GetBlock(index, true);
            var count = Math.Min(buffer.Length, block.Length - offsetInBlock);
            block.Data.AsSpan(offsetInBlock, count).CopyTo(buffer);
            buffer = buffer[count..];
            _position += count;
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
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!_stream.CanWrite)
        {
            throw new NotSupportedException("The disk is opened read-only");
        }

        if (_position + buffer.Length > Length)
        {
            throw new IOException("Writing past the end of the disk");
        }

        while (buffer.Length > 0)
        {
            var (index, offsetInBlock) = GetBlockPosition();
            if (offsetInBlock == 0 && GetUncachedLength(index, buffer.Length) is var uncachedLength and > 0)
            {
                _stream.Position = _position;
                _stream.Write(buffer[..uncachedLength]);
                buffer = buffer[uncachedLength..];
                _position += uncachedLength;
                continue;
            }

            // a block written in part keeps the rest of its bytes
            var count = Math.Min(buffer.Length, GetBlockLength(index) - offsetInBlock);
            var block = GetBlock(index, offsetInBlock != 0 || count != GetBlockLength(index));
            buffer[..count].CopyTo(block.Data.AsSpan(offsetInBlock, count));
            if (!block.IsDirty)
            {
                block.IsDirty = true;
                _dirtyBlocks++;
                _dirtyBytes += block.Length;
            }

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

    private (long Index, int OffsetInBlock) GetBlockPosition() =>
        (_position / BlockSize, (int)(_position % BlockSize));

    /// <summary>Length of a block, less for the last block when the disk isn't a multiple of the block size.</summary>
    private int GetBlockLength(long index) => (int)Math.Min(BlockSize, Length - index * BlockSize);

    /// <summary>Bytes of the whole blocks from a block on that aren't cached, of at most <paramref name="count" />.</summary>
    private int GetUncachedLength(long index, int count)
    {
        var length = 0;
        while (length < count && !_blocks.ContainsKey(index))
        {
            var blockLength = GetBlockLength(index);
            if (length + blockLength > count)
            {
                break;
            }

            length += blockLength;
            index++;
        }

        return length;
    }

    /// <summary>A block from the cache, or added to it, read from the disk when <paramref name="read" /> is true.</summary>
    private Block GetBlock(long index, bool read)
    {
        if (_blocks.TryGetValue(index, out var node))
        {
            _usage.Remove(node);
            _usage.AddFirst(node);
            return node.Value;
        }

        node = _blocks.Count >= _maxBlocks ? RemoveBlock() : new LinkedListNode<Block>(new Block());
        var block = node.Value;
        block.Index = index;
        block.Length = GetBlockLength(index);
        if (read)
        {
            _stream.Position = index * BlockSize;
            _stream.ReadExactly(block.Data.AsSpan(0, block.Length));
        }

        _blocks[index] = node;
        _usage.AddFirst(node);
        return block;
    }

    /// <summary>Removes the least recently used block without writes, writing the writes first when all have.</summary>
    private LinkedListNode<Block> RemoveBlock()
    {
        var node = _usage.Last;
        while (node is { Value.IsDirty: true })
        {
            node = node.Previous;
        }

        if (node == null)
        {
            WriteDirtyBlocks();
            node = _usage.Last!;
        }

        _usage.Remove(node);
        _blocks.Remove(node.Value.Index);
        return node;
    }

    /// <summary>Writes the blocks with writes in order of their position, consecutive blocks at once.</summary>
    /// <param name="progress">Reports the bytes written so far.</param>
    private void WriteDirtyBlocks(IProgress<long>? progress = null)
    {
        if (_dirtyBlocks == 0)
        {
            return;
        }

        _writeBuffer ??= GC.AllocateUninitializedArray<byte>(MaxWriteLength);
        var dirty = _usage.Where(block => block.IsDirty).OrderBy(block => block.Index).ToList();
        var written = 0L;
        var start = 0;
        while (start < dirty.Count)
        {
            var end = start;
            var length = dirty[start].Length;
            while (end + 1 < dirty.Count && dirty[end + 1].Index == dirty[end].Index + 1 &&
                   length + dirty[end + 1].Length <= MaxWriteLength)
            {
                end++;
                length += dirty[end].Length;
            }

            var offset = 0;
            for (var i = start; i <= end; i++)
            {
                dirty[i].Data.AsSpan(0, dirty[i].Length).CopyTo(_writeBuffer.AsSpan(offset));
                offset += dirty[i].Length;
            }

            _stream.Position = dirty[start].Index * BlockSize;
            _stream.Write(_writeBuffer.AsSpan(0, length));

            // only marked as written when written, so a failed write is written again by the next flush
            for (var i = start; i <= end; i++)
            {
                dirty[i].IsDirty = false;
                _dirtyBlocks--;
                _dirtyBytes -= dirty[i].Length;
            }

            written += length;
            progress?.Report(written);
            start = end + 1;
        }
    }

    public override long Seek(long offset, SeekOrigin origin) => Position = origin switch
    {
        SeekOrigin.Begin => offset,
        SeekOrigin.Current => _position + offset,
        SeekOrigin.End => Length + offset,
        _ => throw new ArgumentOutOfRangeException(nameof(origin), origin, null)
    };

    /// <summary>Writes the cached writes and flushes the disk.</summary>
    public void Commit(IProgress<long>? progress = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        WriteDirtyBlocks(progress);
        _stream.Flush();
    }

    /// <summary>
    /// Does nothing: the cached writes are written when Hst Commander commits them while the file system is consistent
    /// (<see cref="Commit" />), when the cache is full and when the stream is disposed.
    /// </summary>
    public override void Flush()
    {
    }

    public override Task FlushAsync(CancellationToken cancellationToken) =>
        cancellationToken.IsCancellationRequested ? Task.FromCanceled(cancellationToken) : Task.CompletedTask;

    public override void SetLength(long value) => throw new NotSupportedException("The size of a disk can't be changed");

    protected override void Dispose(bool disposing)
    {
        if (disposing && !_disposed)
        {
            try
            {
                Commit();
            }
            finally
            {
                _disposed = true;
                _blocks.Clear();
                _usage.Clear();
                _stream.Dispose();
            }
        }

        base.Dispose(disposing);
    }

    private sealed class Block
    {
        public byte[] Data { get; } = GC.AllocateUninitializedArray<byte>(BlockSize);

        public long Index { get; set; }

        public int Length { get; set; }

        public bool IsDirty { get; set; }
    }
}
