namespace Hst.Commander.Plugins.RarSevenZip;

/// <summary>
/// Detects tar archives by their first header block of 512 bytes: a name and a valid checksum (the sum of the bytes of
/// the block with the checksum field counted as spaces, stored as octal digits at 148), which also covers old (v7) tar
/// archives without the "ustar" magic at 257 of POSIX and GNU tar.
/// </summary>
internal static class TarHeader
{
    /// <summary>Size of a tar header block.</summary>
    public const int Size = 512;

    private const int ChecksumOffset = 148;
    private const int ChecksumLength = 8;

    /// <summary>True when the bytes start with a tar header block.</summary>
    public static bool IsTarHeader(ReadOnlySpan<byte> block)
    {
        if (block.Length < Size || block[0] == 0)
        {
            return false;
        }

        if (ParseOctal(block.Slice(ChecksumOffset, ChecksumLength)) is not { } storedChecksum)
        {
            return false;
        }

        long unsignedSum = 0;
        long signedSum = 0;
        for (var index = 0; index < Size; index++)
        {
            var value = index is >= ChecksumOffset and < ChecksumOffset + ChecksumLength ? (byte)' ' : block[index];
            unsignedSum += value;
            signedSum += (sbyte)value;
        }

        // some old tar programs summed signed bytes
        return storedChecksum == unsignedSum || storedChecksum == signedSum;
    }

    /// <summary>Octal number of a header field, padded with spaces or zeros; null when it has no digits or others.</summary>
    private static long? ParseOctal(ReadOnlySpan<byte> field)
    {
        long value = 0;
        var digits = 0;
        foreach (var character in field)
        {
            if (character is >= (byte)'0' and <= (byte)'7')
            {
                value = value * 8 + (character - '0');
                digits++;
            }
            else if (character is (byte)' ' or 0)
            {
                if (digits > 0)
                {
                    break;
                }
            }
            else
            {
                return null;
            }
        }

        return digits > 0 ? value : null;
    }
}
