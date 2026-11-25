namespace Hst.Commander.Plugins.Disk;

/// <summary>
/// Stream of the disk of a virtual disk image (<see cref="DiscUtils.VirtualDisk.Content" />), passing everything to it
/// except the asynchronous reads and writes of arrays, which are passed as memory: DiscUtils' VHDX disk stream reads
/// and writes the wrong bytes with <see cref="Stream.ReadAsync(byte[], int, int, CancellationToken)" /> and
/// <see cref="Stream.WriteAsync(byte[], int, int, CancellationToken)" /> of more than a sector (LTRData.DiscUtils.Vhdx
/// 1.0.90), e.g. when the Amiga plugin reads a rigid disk block. The disk stream is left open when disposed, the virtual
/// disk owns it.
/// </summary>
internal sealed class VirtualDiskStream(Stream stream) : Stream
{
    public override bool CanRead => stream.CanRead;

    public override bool CanSeek => stream.CanSeek;

    public override bool CanWrite => stream.CanWrite;

    public override long Length => stream.Length;

    public override long Position
    {
        get => stream.Position;
        set => stream.Position = value;
    }

    public override void Flush() => stream.Flush();

    public override Task FlushAsync(CancellationToken cancellationToken) => stream.FlushAsync(cancellationToken);

    public override int Read(byte[] buffer, int offset, int count) => stream.Read(buffer, offset, count);

    public override int Read(Span<byte> buffer) => stream.Read(buffer);

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        stream.ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
        stream.ReadAsync(buffer, cancellationToken);

    public override void Write(byte[] buffer, int offset, int count) => stream.Write(buffer, offset, count);

    public override void Write(ReadOnlySpan<byte> buffer) => stream.Write(buffer);

    public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        stream.WriteAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

    public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default) =>
        stream.WriteAsync(buffer, cancellationToken);

    public override long Seek(long offset, SeekOrigin origin) => stream.Seek(offset, origin);

    public override void SetLength(long value) => stream.SetLength(value);
}
