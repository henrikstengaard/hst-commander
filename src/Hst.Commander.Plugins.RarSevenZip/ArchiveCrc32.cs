using System.Buffers.Binary;

namespace Hst.Commander.Plugins.RarSevenZip;

/// <summary>
/// CRC32 (the polynomial of zip, gzip and rar) computed with slicing-by-8, 8 bytes per step with 8 tables, several
/// times faster than a byte per step.
/// </summary>
internal static class ArchiveCrc32
{
    /// <summary>Value to start with; the CRC is the value after the last <see cref="Update" /> xor 0xFFFFFFFF.</summary>
    public const uint Initial = 0xFFFFFFFF;

    private static readonly uint[] Table = CreateTable();

    /// <summary>Updates a CRC started with <see cref="Initial" /> with data.</summary>
    public static uint Update(uint crc, ReadOnlySpan<byte> data)
    {
        var table = Table;
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

        return crc;
    }

    /// <summary>CRC32 tables for slicing-by-8: table 0 for a byte, table n for a byte followed by n zero bytes.</summary>
    private static uint[] CreateTable()
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
}
