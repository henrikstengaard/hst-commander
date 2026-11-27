namespace Hst.Commander.Plugins.Zip;

/// <summary>CRC-32 of zip archives (polynomial 0xEDB88320).</summary>
internal sealed class Crc32
{
    private static readonly uint[] Table = CreateTable();

    private uint _crc = 0xffffffff;

    public uint Value => ~_crc;

    public void Append(ReadOnlySpan<byte> data)
    {
        var crc = _crc;
        foreach (var value in data)
        {
            crc = Table[(crc ^ value) & 0xff] ^ (crc >> 8);
        }

        _crc = crc;
    }

    private static uint[] CreateTable()
    {
        var table = new uint[256];
        for (uint index = 0; index < table.Length; index++)
        {
            var value = index;
            for (var bit = 0; bit < 8; bit++)
            {
                value = (value & 1) != 0 ? 0xedb88320 ^ (value >> 1) : value >> 1;
            }

            table[index] = value;
        }

        return table;
    }
}
