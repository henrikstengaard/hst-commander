using System.Globalization;

namespace Hst.Commander.Core;

public static class SizeFormatter
{
    private static readonly string[] Units = ["B", "K", "M", "G", "T", "P", "E"];

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
