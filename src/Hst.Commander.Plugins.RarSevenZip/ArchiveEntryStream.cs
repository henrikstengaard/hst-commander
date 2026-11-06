using System.Buffers.Binary;

namespace Hst.Commander.Plugins.RarSevenZip;

/// <summary>
/// Read-only stream of a file in an archive read with SharpCompress:
/// <list type="bullet">
/// <item>It reads synchronously, also when read asynchronously. SharpCompress decompresses much slower when read
/// asynchronously (e.g. 17 times slower for lzma in 7z archives), and Hst Commander copies files with asynchronous
/// reads on the thread pool, where reading synchronously is fine: decompressing is work for the processor.</item>
/// <item>It checks the size and CRC of the file when read to the end, so a damaged archive fails the copy instead of
/// leaving a damaged file.</item>
/// <item>Errors decompressing a damaged archive (e.g. <see cref="IndexOutOfRangeException" /> of the lzma decoder)
/// are thrown as <see cref="InvalidDataException" /> naming the file.</item>
/// </list>
/// </summary>
internal sealed class ArchiveEntryStream : Stream
{
    private static readonly uint[] CrcTable = CreateCrcTable();

    private readonly Stream _stream;
    private readonly string _name;
    private readonly long _size;
    private readonly uint? _crc;
    private uint _currentCrc = 0xFFFFFFFF;
    private long _position;
    private bool _verified;

    /// <param name="stream">Entry stream, disposed with this stream.</param>
    /// <param name="name">Name of the file, for error messages.</param>
    /// <param name="size">Size of the file.</param>
    /// <param name="crc">CRC32 of the file, 0 when the archive has none (it isn't checked then).</param>
    public ArchiveEntryStream(Stream stream, string name, long size, long crc)
    {
        _stream = stream;
        _name = name;
        _size = size;
        _crc = crc != 0 ? (uint)crc : null;
    }

    /// <summary>True for exceptions of decompressing damaged data, as opposed to cancelling or reading the archive.</summary>
    public static bool IsDamage(Exception exception) =>
        exception is not (OperationCanceledException or IOException or NotSupportedException
            or UnauthorizedAccessException or ObjectDisposedException);

    public static InvalidDataException CreateDamagedException(string name, Exception? innerException = null) =>
        new($"File '{name}' can't be decompressed: the archive is damaged", innerException);

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
        int bytesRead;
        try
        {
            bytesRead = _stream.Read(buffer);
        }
        catch (Exception e) when (IsDamage(e))
        {
            throw CreateDamagedException(_name, e);
        }

        if (bytesRead > 0)
        {
            _position += bytesRead;
            UpdateCrc(buffer[..bytesRead]);
        }
        else if (buffer.Length > 0)
        {
            Verify();
        }

        return bytesRead;
    }

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(Read(buffer.AsSpan(offset, count)));
    }

    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(Read(buffer.Span));
    }

    /// <summary>Checks the size and CRC of the file once it's read to the end.</summary>
    private void Verify()
    {
        if (_verified)
        {
            return;
        }

        _verified = true;
        if (_position != _size)
        {
            throw new InvalidDataException(
                $"File '{_name}' is damaged: {_position} of {_size} bytes could be decompressed");
        }

        if (_crc is { } crc && (_currentCrc ^ 0xFFFFFFFF) != crc)
        {
            throw new InvalidDataException($"File '{_name}' is damaged: its CRC doesn't match");
        }
    }

    private void UpdateCrc(ReadOnlySpan<byte> data)
    {
        if (_crc == null)
        {
            return;
        }

        // slicing-by-8: 8 bytes per step with 8 tables, several times faster than a byte per step
        var crc = _currentCrc;
        var table = CrcTable;
        while (data.Length >= 8)
        {
            var low = crc ^ BinaryPrimitives.ReadUInt32LittleEndian(data);
            var high = BinaryPrimitives.ReadUInt32LittleEndian(data[4..]);
            crc = table[0x700 + (low & 0xFF)] ^ table[0x600 + ((low >> 8) & 0xFF)] ^
                  table[0x500 + ((low >> 16) & 0xFF)] ^ table[0x400 + (low >> 24)] ^
                  table[0x300 + (high & 0xFF)] ^ table[0x200 + ((high >> 8) & 0xFF)] ^
                  table[0x100 + ((high >> 16) & 0xFF)] ^ table[high >> 24];
            data = data[8..];
        }

        foreach (var value in data)
        {
            crc = table[(crc ^ value) & 0xFF] ^ (crc >> 8);
        }

        _currentCrc = crc;
    }

    /// <summary>CRC32 tables for slicing-by-8: table 0 for a byte, table n for a byte followed by n zero bytes.</summary>
    private static uint[] CreateCrcTable()
    {
        var table = new uint[8 * 256];
        for (uint index = 0; index < 256; index++)
        {
            var crc = index;
            for (var bit = 0; bit < 8; bit++)
            {
                crc = (crc & 1) != 0 ? 0xEDB88320 ^ (crc >> 1) : crc >> 1;
            }

            table[index] = crc;
        }

        for (var slice = 1; slice < 8; slice++)
        {
            for (var index = 0; index < 256; index++)
            {
                var previous = table[(slice - 1) * 256 + index];
                table[slice * 256 + index] = table[previous & 0xFF] ^ (previous >> 8);
            }
        }

        return table;
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
            try
            {
                // SharpCompress decompresses the rest of a file not read to the end, which fails in a damaged archive
                _stream.Dispose();
            }
            catch (Exception)
            {
                // a read-only stream: the error was thrown reading the file, or the rest of the file isn't needed
            }
        }

        base.Dispose(disposing);
    }
}
