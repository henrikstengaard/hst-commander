using System.Globalization;
using Hst.Commander.Extensions.Volume.Abstractions;

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
}
