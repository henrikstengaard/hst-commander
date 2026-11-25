using DiscUtils;
using DiscUtils.Streams;
using DmgDisk = DiscUtils.Dmg.Disk;
using VdiDisk = DiscUtils.Vdi.Disk;
using VhdDisk = DiscUtils.Vhd.Disk;
using VhdxDisk = DiscUtils.Vhdx.Disk;

namespace Hst.Commander.Plugins.Disk;

/// <summary>
/// Virtual disk images opened with DiscUtils, whose disk is read through the virtual disk: dynamic and differencing VHD
/// ("conectix" at the start; fixed VHD images are raw disks with a footer and read directly), VHDX ("vhdxfile" at the
/// start), VDI (0xBEDA107F at 0x40) and DMG ("koly" at the start of the last 512 bytes). VHD, VHDX and VDI are writable
/// when the image stream is, DMG is read-only. Differencing images other than VHD can't be opened from a stream.
/// </summary>
internal static class VirtualDisks
{
    internal const string Vhd = "VHD";
    internal const string Vhdx = "VHDX";
    internal const string Vdi = "VDI";
    internal const string Dmg = "DMG";

    /// <summary>Number of bytes at the start of an image needed by <see cref="Detect" />.</summary>
    internal const int HeaderLength = VdiSignatureOffset + 4;

    private const int VdiSignatureOffset = 0x40;
    private const int DmgTrailerLength = 512;
    private static readonly byte[] VhdSignature = "conectix"u8.ToArray();
    private static readonly byte[] VhdxSignature = "vhdxfile"u8.ToArray();
    private static readonly byte[] VdiSignature = [0x7F, 0x10, 0xDA, 0xBE];
    private static readonly byte[] DmgSignature = "koly"u8.ToArray();

    /// <summary>
    /// Format of the virtual disk image starting with the bytes (at least <see cref="HeaderLength" /> bytes when the
    /// image is that long), reading the DMG trailer at the end of the stream when it's seekable; null when it's none of
    /// them. Keeps the stream's position.
    /// </summary>
    internal static async Task<string?> Detect(ReadOnlyMemory<byte> start, Stream stream,
        CancellationToken cancellationToken)
    {
        if (StartsWith(start.Span, 0, VhdSignature))
        {
            return Vhd;
        }

        if (StartsWith(start.Span, 0, VhdxSignature))
        {
            return Vhdx;
        }

        if (StartsWith(start.Span, VdiSignatureOffset, VdiSignature))
        {
            return Vdi;
        }

        if (!stream.CanSeek || stream.Length < DmgTrailerLength * 2)
        {
            return null;
        }

        var position = stream.Position;
        try
        {
            var trailer = new byte[DmgSignature.Length];
            stream.Position = stream.Length - DmgTrailerLength;
            await stream.ReadExactlyAsync(trailer, cancellationToken);
            return trailer.AsSpan().SequenceEqual(DmgSignature) ? Dmg : null;
        }
        finally
        {
            stream.Position = position;
        }
    }

    /// <summary>
    /// Opens the virtual disk of an image of a format detected by <see cref="Detect" />, leaving the stream open when
    /// it's disposed. Throws <see cref="IOException" /> when it can't be opened.
    /// </summary>
    internal static VirtualDisk Open(string format, Stream stream)
    {
        stream.Position = 0;
        try
        {
            return format switch
            {
                Vhd => new VhdDisk(stream, Ownership.None),
                Vhdx => new VhdxDisk(stream, Ownership.None),
                Vdi => new VdiDisk(stream, Ownership.None),
                Dmg => new DmgDisk(stream, Ownership.None),
                _ => throw new ArgumentOutOfRangeException(nameof(format), format, null)
            };
        }
        catch (Exception exception) when (exception is not (IOException or OperationCanceledException))
        {
            throw new IOException($"The {format} image can't be read: {exception.Message}", exception);
        }
    }

    private static bool StartsWith(ReadOnlySpan<byte> bytes, int offset, ReadOnlySpan<byte> signature) =>
        bytes.Length >= offset + signature.Length && bytes.Slice(offset, signature.Length).SequenceEqual(signature);
}
