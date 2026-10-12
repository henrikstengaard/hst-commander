using System.Globalization;
using Hst.Commander.Extensions.Volume.Abstractions;

namespace Hst.Commander.Gui;

/// <summary>
///     Display row for an entry in a pane.
/// </summary>
public sealed class EntryItem(IEntry entry)
{
    private static readonly string[] Units = ["B", "KB", "MB", "GB", "TB", "PB", "EB"];

    public IEntry Entry { get; } = entry;

    public string Name => Entry.Name;

    public bool IsContainer => Entry.Type == EntryType.Container;

    public string Size => FormatSize(Entry.Size);

    public string Modified => Entry.ModifiedDate?.ToString("yyyy-MM-dd HH:mm") ?? string.Empty;

    private static string FormatSize(long? size)
    {
        if (size == null)
        {
            return string.Empty;
        }

        var value = (double)size.Value;
        var unitIndex = 0;
        while (Math.Abs(value) >= 1024 && unitIndex < Units.Length - 1)
        {
            value /= 1024;
            unitIndex++;
        }

        var numberFormat = value % 1 == 0 ? "0" : "0.#";
        return $"{value.ToString(numberFormat, CultureInfo.InvariantCulture)} {Units[unitIndex]}";
    }
}
