using System.Buffers.Binary;

namespace Hst.Commander.Core.Tests;

public class GivenEmbeddedIconReader
{
    [Fact]
    public void When_ReadIconFromIcoFile_Then_SmallestImageAtLeastWantedSizeIsReturned()
    {
        // arrange
        var ico = CreateIco(Create32BitDib(16, 0x80), CreatePng(32));

        // act
        var small = EmbeddedIconReader.ReadIcon("app.ico", new MemoryStream(ico), 16);
        var large = EmbeddedIconReader.ReadIcon("app.ico", new MemoryStream(ico), 20);

        // assert
        Assert.NotNull(small);
        Assert.False(small.IsPng);
        Assert.Equal(16, small.Width);
        Assert.Equal(16, small.Height);
        Assert.Equal(16 * 16 * 4, small.BgraPixels!.Length);
        Assert.Equal(0x80, small.BgraPixels[3]);
        Assert.NotNull(large);
        Assert.True(large.IsPng);
        Assert.Equal(32, large.Width);
    }

    [Fact]
    public void When_ReadIconFromIcoFileSmallerThanWanted_Then_LargestImageIsReturned()
    {
        // arrange
        var ico = CreateIco(Create32BitDib(16, 0xff), CreatePng(32));

        // act
        var icon = EmbeddedIconReader.ReadIcon("app.ico", new MemoryStream(ico), 64);

        // assert
        Assert.NotNull(icon);
        Assert.Equal(32, icon.Width);
    }

    [Fact]
    public void When_ReadIconFromExecutable_Then_FirstIconGroupImageIsDecodedWithMask()
    {
        // arrange
        var exe = CreateExecutable(Create24BitDib());

        // act
        var icon = EmbeddedIconReader.ReadIcon("app.exe", new MemoryStream(exe), 16);

        // assert
        Assert.NotNull(icon);
        Assert.Equal(2, icon.Width);
        Assert.Equal(2, icon.Height);
        // top-left red and opaque, bottom-right transparent by the mask
        Assert.Equal(new byte[] { 0, 0, 255, 255 }, icon.BgraPixels![..4]);
        Assert.Equal(0, icon.BgraPixels[15]);
    }

    [Fact]
    public void When_ReadIconFromExecutableWithoutResources_Then_NullIsReturned()
    {
        // arrange
        var exe = CreateExecutable(Create24BitDib());
        // clear the resource table directory
        BinaryPrimitives.WriteUInt32LittleEndian(exe.AsSpan(64 + 24 + 112 + 16), 0);

        // act
        var icon = EmbeddedIconReader.ReadIcon("app.exe", new MemoryStream(exe), 16);

        // assert
        Assert.Null(icon);
    }

    [Fact]
    public void When_ReadIconFromInvalidFile_Then_NullIsReturned()
    {
        // act
        var exeIcon = EmbeddedIconReader.ReadIcon("app.exe", new MemoryStream("MZ"u8.ToArray()), 16);
        var icoIcon = EmbeddedIconReader.ReadIcon("app.ico", new MemoryStream(new byte[100]), 16);

        // assert
        Assert.Null(exeIcon);
        Assert.Null(icoIcon);
    }

    [Theory]
    [InlineData("app.exe", true)]
    [InlineData("APP.ICO", true)]
    [InlineData("readme.txt", false)]
    public void When_HasEmbeddedIcon_Then_ExtensionIsChecked(string name, bool expected) =>
        Assert.Equal(expected, EmbeddedIconReader.HasEmbeddedIcon(name));

    private static byte[] CreatePng(int size)
    {
        var png = new byte[24];
        new byte[] { 0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a }.CopyTo(png, 0);
        "IHDR"u8.CopyTo(png.AsSpan(12));
        BinaryPrimitives.WriteInt32BigEndian(png.AsSpan(16), size);
        BinaryPrimitives.WriteInt32BigEndian(png.AsSpan(20), size);
        return png;
    }

    private static byte[] CreateDibHeader(int width, int height, int bitCount, int dataSize)
    {
        var dib = new byte[40 + dataSize];
        BinaryPrimitives.WriteInt32LittleEndian(dib, 40);
        BinaryPrimitives.WriteInt32LittleEndian(dib.AsSpan(4), width);
        BinaryPrimitives.WriteInt32LittleEndian(dib.AsSpan(8), height * 2);
        BinaryPrimitives.WriteUInt16LittleEndian(dib.AsSpan(12), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(dib.AsSpan(14), (ushort)bitCount);
        return dib;
    }

    private static byte[] Create32BitDib(int size, byte alpha)
    {
        var dib = CreateDibHeader(size, size, 32, size * size * 4 + 4 * size);
        for (var pixel = 0; pixel < size * size; pixel++)
        {
            dib[40 + pixel * 4 + 3] = alpha;
        }

        return dib;
    }

    /// <summary>2x2 24-bit image, top-left red, bottom-right masked out.</summary>
    private static byte[] Create24BitDib()
    {
        // color rows of 8 bytes and mask rows of 4 bytes, bottom-up
        var dib = CreateDibHeader(2, 2, 24, 2 * 8 + 2 * 4);
        dib[40 + 8 + 2] = 255; // top row (stored second), first pixel red
        dib[40 + 16] = 0x40; // bottom row mask (stored first), second pixel transparent
        return dib;
    }

    private static byte[] CreateIco(params byte[][] images)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write((ushort)0);
        writer.Write((ushort)1);
        writer.Write((ushort)images.Length);
        var offset = 6 + images.Length * 16;
        foreach (var image in images)
        {
            var size = image[0] == 0x89
                ? BinaryPrimitives.ReadInt32BigEndian(image.AsSpan(16))
                : BinaryPrimitives.ReadInt32LittleEndian(image.AsSpan(4));
            writer.Write((byte)size);
            writer.Write((byte)size);
            writer.Write((byte)0);
            writer.Write((byte)0);
            writer.Write((ushort)1);
            writer.Write((ushort)32);
            writer.Write(image.Length);
            writer.Write(offset);
            offset += image.Length;
        }

        foreach (var image in images)
        {
            writer.Write(image);
        }

        return stream.ToArray();
    }

    /// <summary>Minimal PE32+ file with a resource section holding one icon group with one icon.</summary>
    private static byte[] CreateExecutable(byte[] icon)
    {
        const int resourceRva = 0x1000;
        const int resourceFileOffset = 0x200;
        const int iconOffset = 160;
        var groupOffset = iconOffset + icon.Length;
        var resourceSize = groupOffset + 20;
        var exe = new byte[resourceFileOffset + resourceSize];
        var span = exe.AsSpan();

        // DOS header, PE signature and file header
        exe[0] = (byte)'M';
        exe[1] = (byte)'Z';
        BinaryPrimitives.WriteUInt32LittleEndian(span[0x3c..], 64);
        BinaryPrimitives.WriteUInt32LittleEndian(span[64..], 0x4550);
        BinaryPrimitives.WriteUInt16LittleEndian(span[(64 + 4)..], 0x8664);
        BinaryPrimitives.WriteUInt16LittleEndian(span[(64 + 6)..], 1);
        BinaryPrimitives.WriteUInt16LittleEndian(span[(64 + 20)..], 240);

        // optional header with the resource table directory
        const int optionalHeader = 64 + 24;
        BinaryPrimitives.WriteUInt16LittleEndian(span[optionalHeader..], 0x20b);
        BinaryPrimitives.WriteUInt32LittleEndian(span[(optionalHeader + 112 + 16)..], resourceRva);
        BinaryPrimitives.WriteUInt32LittleEndian(span[(optionalHeader + 112 + 20)..], (uint)resourceSize);

        // section table
        const int section = optionalHeader + 240;
        ".rsrc"u8.CopyTo(span[section..]);
        BinaryPrimitives.WriteUInt32LittleEndian(span[(section + 8)..], (uint)resourceSize);
        BinaryPrimitives.WriteUInt32LittleEndian(span[(section + 12)..], resourceRva);
        BinaryPrimitives.WriteUInt32LittleEndian(span[(section + 16)..], (uint)resourceSize);
        BinaryPrimitives.WriteUInt32LittleEndian(span[(section + 20)..], resourceFileOffset);

        // resource directories: type, id, language
        var resources = span[resourceFileOffset..];
        WriteDirectory(resources, 0, (3, 32 | 0x80000000), (14, 80 | 0x80000000));
        WriteDirectory(resources, 32, (1, 56 | 0x80000000));
        WriteDirectory(resources, 56, (1033, 128));
        WriteDirectory(resources, 80, (1, 104 | 0x80000000));
        WriteDirectory(resources, 104, (1033, 144));

        // data entries
        BinaryPrimitives.WriteUInt32LittleEndian(resources[128..], (uint)(resourceRva + iconOffset));
        BinaryPrimitives.WriteUInt32LittleEndian(resources[132..], (uint)icon.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(resources[144..], (uint)(resourceRva + groupOffset));
        BinaryPrimitives.WriteUInt32LittleEndian(resources[148..], 20);

        // icon and icon group
        icon.CopyTo(resources[iconOffset..]);
        var group = resources[groupOffset..];
        BinaryPrimitives.WriteUInt16LittleEndian(group[2..], 1);
        BinaryPrimitives.WriteUInt16LittleEndian(group[4..], 1);
        group[6] = 2;
        group[7] = 2;
        BinaryPrimitives.WriteUInt16LittleEndian(group[(6 + 4)..], 1);
        BinaryPrimitives.WriteUInt16LittleEndian(group[(6 + 6)..], 24);
        BinaryPrimitives.WriteUInt32LittleEndian(group[(6 + 8)..], (uint)icon.Length);
        BinaryPrimitives.WriteUInt16LittleEndian(group[(6 + 12)..], 1);

        return exe;
    }

    private static void WriteDirectory(Span<byte> resources, int offset, params (uint Id, uint Offset)[] entries)
    {
        BinaryPrimitives.WriteUInt16LittleEndian(resources[(offset + 14)..], (ushort)entries.Length);
        for (var index = 0; index < entries.Length; index++)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(resources[(offset + 16 + index * 8)..], entries[index].Id);
            BinaryPrimitives.WriteUInt32LittleEndian(resources[(offset + 20 + index * 8)..], entries[index].Offset);
        }
    }
}
