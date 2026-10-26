using System.Buffers.Binary;

namespace Hst.Commander.Core.Tests;

/// <summary>Creates IFF ILBM pictures for tests.</summary>
public static class IlbmTestData
{
    /// <summary>
    /// Creates a FORM ILBM of pixels given as color indices (or HAM values), row by row, as bitplanes, uncompressed or
    /// ByteRun1 compressed, with a color map, view modes (CAMG) and a mask plane (pixels with a mask of false are
    /// transparent) when given.
    /// </summary>
    public static byte[] CreateIlbm(int width, int height, int planes, int[] pixels, byte[]? colorMap,
        bool compress = false, uint? viewModes = null, bool[]? mask = null)
    {
        var rowBytes = (width + 15) / 16 * 2;
        var body = new List<byte>();
        for (var y = 0; y < height; y++)
        {
            var rows = new List<byte[]>();
            for (var plane = 0; plane < planes; plane++)
            {
                rows.Add(CreatePlaneRow(width, rowBytes, x => ((pixels[y * width + x] >> plane) & 1) == 1));
            }

            if (mask != null)
            {
                rows.Add(CreatePlaneRow(width, rowBytes, x => mask[y * width + x]));
            }

            foreach (var row in rows)
            {
                body.AddRange(compress ? ByteRun1(row) : row);
            }
        }

        var header = new byte[20];
        BinaryPrimitives.WriteUInt16BigEndian(header, (ushort)width);
        BinaryPrimitives.WriteUInt16BigEndian(header.AsSpan(2), (ushort)height);
        header[8] = (byte)planes;
        header[9] = (byte)(mask != null ? 1 : 0);
        header[10] = (byte)(compress ? 1 : 0);
        header[14] = 10;
        header[15] = 11;

        var chunks = new List<byte>("ILBM"u8.ToArray());
        chunks.AddRange(Chunk("BMHD", header));
        if (colorMap != null)
        {
            chunks.AddRange(Chunk("CMAP", colorMap));
        }

        if (viewModes is { } modes)
        {
            var camg = new byte[4];
            BinaryPrimitives.WriteUInt32BigEndian(camg, modes);
            chunks.AddRange(Chunk("CAMG", camg));
        }

        chunks.AddRange(Chunk("BODY", body.ToArray()));
        return Chunk("FORM", chunks.ToArray());
    }

    /// <summary>Creates a LIST holding pictures (FORMs).</summary>
    public static byte[] CreateList(params byte[][] forms) =>
        Chunk("LIST", [.."ILBM"u8.ToArray(), ..forms.SelectMany(form => form)]);

    private static byte[] CreatePlaneRow(int width, int rowBytes, Func<int, bool> isSet)
    {
        var row = new byte[rowBytes];
        for (var x = 0; x < width; x++)
        {
            if (isSet(x))
            {
                row[x / 8] |= (byte)(0x80 >> (x % 8));
            }
        }

        return row;
    }

    /// <summary>ByteRun1: runs of 2 or more equal bytes as repeats, other bytes as literals.</summary>
    private static IEnumerable<byte> ByteRun1(byte[] data)
    {
        var index = 0;
        while (index < data.Length)
        {
            var run = 1;
            while (index + run < data.Length && data[index + run] == data[index] && run < 128)
            {
                run++;
            }

            if (run >= 2)
            {
                yield return (byte)(sbyte)-(run - 1);
                yield return data[index];
                index += run;
                continue;
            }

            yield return 0;
            yield return data[index];
            index++;
        }
    }

    private static byte[] Chunk(string id, byte[] content)
    {
        var chunk = new byte[8 + content.Length + (content.Length & 1)];
        System.Text.Encoding.ASCII.GetBytes(id).CopyTo(chunk, 0);
        BinaryPrimitives.WriteUInt32BigEndian(chunk.AsSpan(4), (uint)content.Length);
        content.CopyTo(chunk, 8);
        return chunk;
    }
}
