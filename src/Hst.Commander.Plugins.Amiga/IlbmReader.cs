using System.Buffers.Binary;
using Hst.Commander.Abstractions;

namespace Hst.Commander.Plugins.Amiga;

/// <summary>
/// Reads IFF ILBM pictures (interleaved bitmaps, e.g. drawn with Deluxe Paint) and PBM pictures (chunky bitmaps of
/// the PC version of Deluxe Paint) as 32-bit BGRA pixels: 1 to 8 bitplanes with a color map (scaled up when it only
/// has 4 bits per color, like the OCS chipset), extra half-brite (EHB), hold-and-modify (HAM6 and HAM8), 24 and 32-bit
/// deep ILBMs, uncompressed or ByteRun1 compressed, with a mask plane or a transparent color. A FORM ILBM or PBM is
/// one picture, a LIST or CAT has several, an ANIM shows its first frame (the other frames are deltas).
/// </summary>
internal static class IlbmReader
{
    private const int MaxWidth = 16384;
    private const long MaxPixels = 64L * 1024 * 1024;
    private const int MaxImages = 256;
    private const uint CamgExtraHalfBrite = 0x80;
    private const uint CamgHoldAndModify = 0x800;

    /// <summary>True when the data starts like an IFF file containing pictures (FORM, LIST or CAT).</summary>
    public static bool IsIff(ReadOnlySpan<byte> data) =>
        data.Length >= 12 && (data[..4].SequenceEqual("FORM"u8) || data[..4].SequenceEqual("LIST"u8) ||
                              data[..4].SequenceEqual("CAT "u8));

    /// <summary>Reads the pictures of an IFF file, empty when it has none or they are invalid.</summary>
    public static IReadOnlyList<(string Name, IconImage Image)> Read(ReadOnlySpan<byte> data)
    {
        var images = new List<(string Kind, IconImage Image)>();
        ReadContainer(data, images, 0);
        return images.Count == 1
            ? [(images[0].Kind, images[0].Image)]
            : images.Select((image, index) => ($"{image.Kind} {index + 1}", image.Image)).ToList();
    }

    /// <summary>Reads the chunks of an IFF container: a FORM is a picture, a LIST or CAT holds more containers.</summary>
    private static void ReadContainer(ReadOnlySpan<byte> data, List<(string, IconImage)> images, int depth)
    {
        var offset = 0;
        while (offset + 12 <= data.Length && images.Count < MaxImages && depth < 8)
        {
            var id = data.Slice(offset, 4);
            var size = (int)Math.Min(BinaryPrimitives.ReadUInt32BigEndian(data[(offset + 4)..]),
                data.Length - offset - 8);
            var content = data.Slice(offset + 8, size);
            if (id.SequenceEqual("FORM"u8) && content.Length >= 4)
            {
                var type = content[..4];
                if (type.SequenceEqual("ILBM"u8) || type.SequenceEqual("PBM "u8))
                {
                    if (ReadPicture(content[4..], type.SequenceEqual("PBM "u8)) is { } image)
                    {
                        images.Add((type.SequenceEqual("PBM "u8) ? "PBM" : "ILBM", image));
                    }
                }
                else if (type.SequenceEqual("ANIM"u8))
                {
                    // the first frame is a picture, the others only hold the changes
                    var frames = new List<(string, IconImage)>();
                    ReadContainer(content[4..], frames, depth + 1);
                    images.AddRange(frames.Take(1));
                }
            }
            else if ((id.SequenceEqual("LIST"u8) || id.SequenceEqual("CAT "u8)) && content.Length >= 4)
            {
                ReadContainer(content[4..], images, depth + 1);
            }

            offset += 8 + size + (size & 1);
        }
    }

    private static IconImage? ReadPicture(ReadOnlySpan<byte> chunks, bool isChunky)
    {
        Header? header = null;
        byte[]? colorMap = null;
        uint viewModes = 0;
        var body = ReadOnlySpan<byte>.Empty;
        var hasBody = false;
        var offset = 0;
        while (offset + 8 <= chunks.Length)
        {
            var id = chunks.Slice(offset, 4);
            var size = (int)Math.Min(BinaryPrimitives.ReadUInt32BigEndian(chunks[(offset + 4)..]),
                chunks.Length - offset - 8);
            var content = chunks.Slice(offset + 8, size);
            if (id.SequenceEqual("BMHD"u8) && content.Length >= 20)
            {
                header = new Header(BinaryPrimitives.ReadUInt16BigEndian(content),
                    BinaryPrimitives.ReadUInt16BigEndian(content[2..]), content[8], content[9], content[10],
                    BinaryPrimitives.ReadUInt16BigEndian(content[12..]));
            }
            else if (id.SequenceEqual("CMAP"u8))
            {
                colorMap = content.ToArray();
            }
            else if (id.SequenceEqual("CAMG"u8) && content.Length >= 4)
            {
                viewModes = BinaryPrimitives.ReadUInt32BigEndian(content);
            }
            else if (id.SequenceEqual("BODY"u8))
            {
                body = content;
                hasBody = true;
            }

            offset += 8 + size + (size & 1);
        }

        if (header is not { } bmhd || !hasBody || bmhd.Width is 0 or > MaxWidth || bmhd.Height == 0 ||
            (long)bmhd.Width * bmhd.Height > MaxPixels || bmhd.Compression > 1 ||
            !(bmhd.Planes is >= 1 and <= 8 or 24 or 32) || (isChunky && bmhd.Planes != 8))
        {
            return null;
        }

        return isChunky ? DecodeChunky(bmhd, colorMap, body) : DecodePlanar(bmhd, colorMap, viewModes, body);
    }

    private static IconImage DecodePlanar(Header header, byte[]? colorMap, uint viewModes, ReadOnlySpan<byte> body)
    {
        var width = header.Width;
        var rowBytes = (width + 15) / 16 * 2;
        var hasMaskPlane = header.Masking == 1;
        var planes = header.Planes + (hasMaskPlane ? 1 : 0);
        var data = Decompress(body, header.Compression, rowBytes * planes * header.Height);
        var isHam = (viewModes & CamgHoldAndModify) != 0 && header.Planes is 6 or 8;
        var palette = GetPalette(colorMap, header.Planes, (viewModes & CamgExtraHalfBrite) != 0);
        var pixels = new byte[width * header.Height * 4];
        for (var y = 0; y < header.Height; y++)
        {
            var row = y * rowBytes * planes;
            var (r, g, b) = palette.Length > 0 ? palette[0] : ((byte)0, (byte)0, (byte)0);
            for (var x = 0; x < width; x++)
            {
                var byteIndex = row + x / 8;
                var bit = 7 - x % 8;
                long value = 0;
                for (var plane = 0; plane < header.Planes; plane++)
                {
                    value |= (long)((data[byteIndex + plane * rowBytes] >> bit) & 1) << plane;
                }

                byte alpha = 255;
                if (header.Planes >= 24)
                {
                    (r, g, b) = ((byte)value, (byte)(value >> 8), (byte)(value >> 16));
                    alpha = header.Planes == 32 ? (byte)(value >> 24) : (byte)255;
                }
                else if (isHam)
                {
                    // the top 2 bits tell whether to use a color or change red, green or blue of the pixel before
                    var valueBits = header.Planes - 2;
                    var control = (int)(value >> valueBits);
                    var level = (int)(value & ((1 << valueBits) - 1));
                    var component = valueBits == 4
                        ? (byte)(level * 17)
                        : (byte)((level << 2) | (level >> 4));
                    switch (control)
                    {
                        case 0:
                            (r, g, b) = level < palette.Length ? palette[level] : ((byte)0, (byte)0, (byte)0);
                            break;
                        case 1:
                            b = component;
                            break;
                        case 2:
                            r = component;
                            break;
                        default:
                            g = component;
                            break;
                    }
                }
                else
                {
                    (r, g, b) = value < palette.Length ? palette[value] : ((byte)0, (byte)0, (byte)0);
                    if (header.Masking == 2 && value == header.TransparentColor)
                    {
                        alpha = 0;
                    }
                }

                if (hasMaskPlane && ((data[byteIndex + header.Planes * rowBytes] >> bit) & 1) == 0)
                {
                    alpha = 0;
                }

                SetPixel(pixels, (y * width + x) * 4, r, g, b, alpha);
            }
        }

        return new IconImage(width, header.Height, null, pixels, header.Planes);
    }

    private static IconImage DecodeChunky(Header header, byte[]? colorMap, ReadOnlySpan<byte> body)
    {
        var width = header.Width;
        var rowBytes = width + (width & 1);
        var data = Decompress(body, header.Compression, rowBytes * header.Height);
        var palette = GetPalette(colorMap, 8, false);
        var pixels = new byte[width * header.Height * 4];
        for (var y = 0; y < header.Height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var value = data[y * rowBytes + x];
                var (r, g, b) = value < palette.Length ? palette[value] : ((byte)0, (byte)0, (byte)0);
                var alpha = header.Masking == 2 && value == header.TransparentColor ? (byte)0 : (byte)255;
                SetPixel(pixels, (y * width + x) * 4, r, g, b, alpha);
            }
        }

        return new IconImage(width, header.Height, null, pixels, 8);
    }

    /// <summary>
    /// Colors of the color map, scaled up when they only use the upper 4 bits (OCS pictures), the extra half-brite
    /// colors added (half as bright as the first 32), gray levels when there is no color map.
    /// </summary>
    private static (byte R, byte G, byte B)[] GetPalette(byte[]? colorMap, int planes, bool extraHalfBrite)
    {
        if (colorMap == null || colorMap.Length < 3)
        {
            if (planes > 8)
            {
                return [];
            }

            var levels = (1 << planes) - 1;
            return Enumerable.Range(0, levels + 1)
                .Select(index => (byte)(index * 255 / levels))
                .Select(level => (level, level, level))
                .ToArray();
        }

        var count = colorMap.Length / 3;
        var isFourBit = colorMap.Take(count * 3).All(value => (value & 0x0F) == 0);
        var palette = new (byte R, byte G, byte B)[extraHalfBrite && planes == 6 ? Math.Max(count, 32) * 2 : count];
        for (var index = 0; index < count; index++)
        {
            palette[index] = (Scale(colorMap[index * 3]), Scale(colorMap[index * 3 + 1]),
                Scale(colorMap[index * 3 + 2]));
        }

        if (palette.Length > count)
        {
            for (var index = 0; index < palette.Length / 2; index++)
            {
                var (r, g, b) = palette[index];
                palette[palette.Length / 2 + index] = ((byte)(r >> 1), (byte)(g >> 1), (byte)(b >> 1));
            }
        }

        return palette;

        byte Scale(byte value) => isFourBit ? (byte)(value | (value >> 4)) : value;
    }

    /// <summary>The body uncompressed or ByteRun1 decompressed, padded with zeros when it is too short.</summary>
    private static byte[] Decompress(ReadOnlySpan<byte> body, byte compression, int length)
    {
        var data = new byte[length];
        if (compression == 0)
        {
            body[..Math.Min(body.Length, length)].CopyTo(data);
            return data;
        }

        var source = 0;
        var target = 0;
        while (source < body.Length && target < length)
        {
            var control = (sbyte)body[source++];
            if (control >= 0)
            {
                var count = Math.Min(Math.Min(control + 1, body.Length - source), length - target);
                body.Slice(source, count).CopyTo(data.AsSpan(target));
                source += control + 1;
                target += count;
            }
            else if (control != -128 && source < body.Length)
            {
                var count = Math.Min(-control + 1, length - target);
                data.AsSpan(target, count).Fill(body[source++]);
                target += count;
            }
        }

        return data;
    }

    private static void SetPixel(byte[] pixels, int index, byte r, byte g, byte b, byte a)
    {
        pixels[index] = b;
        pixels[index + 1] = g;
        pixels[index + 2] = r;
        pixels[index + 3] = a;
    }

    private readonly record struct Header(
        int Width,
        int Height,
        int Planes,
        byte Masking,
        byte Compression,
        int TransparentColor);
}
