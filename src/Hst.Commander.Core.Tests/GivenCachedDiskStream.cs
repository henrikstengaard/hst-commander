using Hst.Commander.Plugins.PhysicalDisk;

namespace Hst.Commander.Core.Tests;

public class GivenCachedDiskStream
{
    private const int BlockSize = CachedDiskStream.BlockSize;

    // not a multiple of the block size, so the last block is shorter
    private const int DiskSize = 4 * BlockSize + 1024;

    private readonly byte[] _data = Enumerable.Range(0, DiskSize).Select(index => (byte)(index * 7)).ToArray();
    private readonly RecordingStream _disk;

    public GivenCachedDiskStream() => _disk = new RecordingStream(_data);

    private CachedDiskStream Open(int cacheBlocks = 8) => new(_disk, cacheBlocks * BlockSize);

    [Fact]
    public void When_ReadingSmallPartsOfABlock_Then_TheBlockIsReadOnce()
    {
        // arrange
        using var stream = Open();
        var buffer = new byte[512];

        // act
        stream.Position = 1000;
        stream.ReadExactly(buffer);
        var first = buffer.ToArray();
        stream.Position = 5000;
        stream.ReadExactly(buffer);

        // assert
        Assert.Equal(_data.AsSpan(1000, 512).ToArray(), first);
        Assert.Equal(_data.AsSpan(5000, 512).ToArray(), buffer);
        Assert.Equal([(0L, BlockSize)], _disk.Reads);
    }

    [Fact]
    public void When_WritingSmallParts_Then_TheyAreWrittenWhenCommittedAsOneWrite()
    {
        // arrange
        using var stream = Open();
        var bytes = Enumerable.Repeat((byte)0xff, 512).ToArray();

        // act
        stream.Position = 1000;
        stream.Write(bytes);
        stream.Position = BlockSize + 100;
        stream.Write(bytes);
        var writesBeforeFlush = _disk.Writes.Count;
        var hasCachedWrites = stream.HasCachedWrites;
        var cachedWriteBytes = stream.CachedWriteBytes;
        var written = new List<long>();
        stream.Commit(new SynchronousProgress<long>(written.Add));

        // assert
        Assert.Equal(0, writesBeforeFlush);
        Assert.True(hasCachedWrites);
        Assert.False(stream.HasCachedWrites);
        Assert.Equal(2 * BlockSize, cachedWriteBytes);
        Assert.Equal(0, stream.CachedWriteBytes);
        Assert.Equal([2L * BlockSize], written);
        Assert.Equal([(0L, 2 * BlockSize)], _disk.Writes);
        Assert.Equal(1, _disk.Flushes);

        var expected = _data.ToArray();
        bytes.CopyTo(expected, 1000);
        bytes.CopyTo(expected, BlockSize + 100);
        Assert.Equal(expected, _disk.Data);
    }

    [Fact]
    public async Task When_Flushed_Then_CachedWritesAreNotWritten()
    {
        // arrange
        using var stream = Open();
        stream.Position = 10;
        stream.Write([1, 2, 3]);

        // act
        stream.Flush();
        await stream.FlushAsync();

        // assert
        Assert.Empty(_disk.Writes);
        Assert.Equal(0, _disk.Flushes);
        Assert.True(stream.HasCachedWrites);
    }

    [Fact]
    public void When_ReadingCachedWrites_Then_WrittenBytesAreRead()
    {
        // arrange
        using var stream = Open();
        stream.Position = 10;
        stream.Write([1, 2, 3]);

        // act
        stream.Position = 8;
        var buffer = new byte[7];
        stream.ReadExactly(buffer);

        // assert
        Assert.Equal([_data[8], _data[9], 1, 2, 3, _data[13], _data[14]], buffer);
        Assert.Empty(_disk.Writes);
    }

    [Fact]
    public void When_WritingWholeBlocksNotCached_Then_TheyAreWrittenDirectly()
    {
        // arrange
        using var stream = Open();
        var bytes = Enumerable.Repeat((byte)0xaa, 2 * BlockSize).ToArray();

        // act
        stream.Position = BlockSize;
        stream.Write(bytes);

        // assert
        Assert.Empty(_disk.Reads);
        Assert.Equal([((long)BlockSize, 2 * BlockSize)], _disk.Writes);
        Assert.False(stream.HasCachedWrites);
    }

    [Fact]
    public void When_ReadingWholeBlocks_Then_CachedBlocksAreReadFromTheCache()
    {
        // arrange
        using var stream = Open();
        stream.Position = BlockSize + 10;
        stream.Write([1, 2, 3]);
        var buffer = new byte[DiskSize];

        // act
        stream.Position = 0;
        stream.ReadExactly(buffer);

        // assert
        var expected = _data.ToArray();
        expected[BlockSize + 10] = 1;
        expected[BlockSize + 11] = 2;
        expected[BlockSize + 12] = 3;
        Assert.Equal(expected, buffer);
        Assert.Equal([((long)BlockSize, BlockSize), (0L, BlockSize), (2L * BlockSize, 2 * BlockSize + 1024)],
            _disk.Reads);
    }

    [Fact]
    public void When_HalfTheCacheHoldsWrites_Then_ItShouldBeFlushed()
    {
        // arrange
        using var stream = Open(4);

        // act
        stream.Position = 0;
        stream.WriteByte(1);
        var shouldFlushWithOneBlock = stream.ShouldFlush;
        stream.Position = BlockSize;
        stream.WriteByte(1);

        // assert
        Assert.False(shouldFlushWithOneBlock);
        Assert.True(stream.ShouldFlush);
    }

    [Fact]
    public void When_EveryCachedBlockHoldsWrites_Then_TheWritesAreWrittenToCacheAnotherBlock()
    {
        // arrange
        using var stream = Open(2);

        // act
        stream.Position = 0;
        stream.WriteByte(1);
        stream.Position = 2 * BlockSize;
        stream.WriteByte(2);
        var writesWithFullCache = _disk.Writes.Count;
        stream.Position = 4 * BlockSize;
        stream.WriteByte(3);

        // assert
        Assert.Equal(0, writesWithFullCache);
        Assert.Equal([(0L, BlockSize), (2L * BlockSize, BlockSize)], _disk.Writes);
        Assert.Equal(1, _disk.Data[0]);
        Assert.Equal(2, _disk.Data[2 * BlockSize]);
        Assert.Equal(_data[4 * BlockSize], _disk.Data[4 * BlockSize]);
        Assert.True(stream.HasCachedWrites);
    }

    [Fact]
    public void When_TheCacheIsFull_Then_TheLeastRecentlyUsedBlockWithoutWritesIsDropped()
    {
        // arrange
        using var stream = Open(2);
        stream.Position = 0;
        stream.WriteByte(1);
        stream.Position = BlockSize;
        stream.ReadByte();

        // act
        stream.Position = 2 * BlockSize;
        stream.ReadByte();
        stream.Position = 0;
        stream.ReadByte();
        stream.Position = BlockSize;
        stream.ReadByte();

        // assert
        Assert.Empty(_disk.Writes);
        Assert.Equal([(0L, BlockSize), ((long)BlockSize, BlockSize), (2L * BlockSize, BlockSize),
            ((long)BlockSize, BlockSize)], _disk.Reads);
    }

    [Fact]
    public void When_WritingTheLastShorterBlock_Then_OnlyTheDiskIsWritten()
    {
        // arrange
        using var stream = Open();

        // act
        stream.Seek(-2, SeekOrigin.End);
        stream.Write([1, 2]);
        stream.Commit();

        // assert
        Assert.Equal([(4L * BlockSize, 1024)], _disk.Writes);
        Assert.Equal([1, 2], _disk.Data[^2..]);
        Assert.Throws<IOException>(() => stream.Write([1, 2, 3]));
    }

    [Fact]
    public void When_Disposed_Then_CachedWritesAreWrittenAndTheDiskIsDisposed()
    {
        // arrange
        var stream = Open();
        stream.Position = 3;
        stream.WriteByte(9);

        // act
        stream.Dispose();

        // assert
        Assert.Equal(9, _disk.Data[3]);
        Assert.True(_disk.IsDisposed);
    }

    private sealed class SynchronousProgress<T>(Action<T> handler) : IProgress<T>
    {
        public void Report(T value) => handler(value);
    }

    /// <summary>Stream of bytes in memory recording the reads and writes, like a disk.</summary>
    private sealed class RecordingStream(byte[] data) : MemoryStream(data.ToArray(), 0, data.Length, true, true)
    {
        public List<(long Offset, int Count)> Reads { get; } = [];

        public List<(long Offset, int Count)> Writes { get; } = [];

        public int Flushes { get; private set; }

        public bool IsDisposed { get; private set; }

        public byte[] Data => GetBuffer();

        // the span members of a derived memory stream call these
        public override int Read(byte[] buffer, int offset, int count)
        {
            Reads.Add((Position, count));
            return base.Read(buffer, offset, count);
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            Writes.Add((Position, count));
            base.Write(buffer, offset, count);
        }

        public override void Flush() => Flushes++;

        protected override void Dispose(bool disposing)
        {
            IsDisposed = true;
        }
    }
}
