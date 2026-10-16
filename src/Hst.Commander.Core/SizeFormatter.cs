using System.Globalization;
using Hst.Commander.Abstractions;

namespace Hst.Commander.Core;

public static class SizeFormatter
{
    /// <summary>Text shown in the size column for directories.</summary>
    public const string DirectorySize = "<DIR>";

    private static readonly string[] Units = ["B", "K", "M", "G", "T", "P", "E"];

    /// <summary>
    ///     Formats the size column of an entry: "&lt;DIR&gt;" for directories, otherwise the humanized size,
    ///     e.g. "87G" (see <see cref="FormatHumanized" />). Empty when the size is unknown.
    /// </summary>
    public static string FormatEntrySize(IEntry entry)
    {
        if (entry.Type == EntryType.Container)
        {
            return DirectorySize;
        }

        return entry.Size == null ? string.Empty : FormatHumanized(entry.Size.Value);
    }

    /// <summary>Formats a size as a rounded integer with a single letter unit, e.g. "87G".</summary>
    public static string FormatHumanized(long size)
    {
        var value = (double)Math.Max(0, size);
        var unitIndex = 0;
        while (Math.Round(value, MidpointRounding.AwayFromZero) >= 1024 && unitIndex < Units.Length - 1)
        {
            value /= 1024;
            unitIndex++;
        }

        return Math.Round(value, MidpointRounding.AwayFromZero).ToString("0", CultureInfo.InvariantCulture) +
               Units[unitIndex];
    }

    /// <summary>
    ///     Formats a size like the properties of Windows Explorer: with three significant digits (truncated) and the
    ///     exact number of bytes, e.g. "1.17 MB (1,234,567 bytes)", or only bytes below 1 KB, e.g. "512 bytes".
    /// </summary>
    public static string FormatWithBytes(long size)
    {
        size = Math.Max(0, size);
        var bytes = string.Create(CultureInfo.InvariantCulture, $"{size:N0} {(size == 1 ? "byte" : "bytes")}");
        if (size < 1024)
        {
            return bytes;
        }

        var value = (double)size;
        var unitIndex = 0;
        while (value >= 1024 && unitIndex < Units.Length - 1)
        {
            value /= 1024;
            unitIndex++;
        }

        var decimals = value >= 100 ? 0 : value >= 10 ? 1 : 2;
        var factor = Math.Pow(10, decimals);
        var truncated = (Math.Floor(value * factor) / factor).ToString(
            decimals == 0 ? "0" : "0." + new string('0', decimals), CultureInfo.InvariantCulture);
        return $"{truncated} {Units[unitIndex]}B ({bytes})";
    }

    /// <summary>Formats a speed in bytes per second with one decimal and a 1024-based unit, e.g. "12.3 MB/s".</summary>
    public static string FormatSpeed(double bytesPerSecond)
    {
        var value = Math.Max(0, bytesPerSecond);
        var unitIndex = 0;
        while (Math.Round(value, 1, MidpointRounding.AwayFromZero) >= 1024 && unitIndex < Units.Length - 1)
        {
            value /= 1024;
            unitIndex++;
        }

        var unit = unitIndex == 0 ? "B" : Units[unitIndex] + "B";
        return Math.Round(value, 1, MidpointRounding.AwayFromZero).ToString("0.0", CultureInfo.InvariantCulture) +
               $" {unit}/s";
    }
}
