using System.Globalization;
using Hst.Commander.Abstractions;

namespace Hst.Commander.Core.Volumes;

/// <summary>Answer to <see cref="CopyOptions.ConfirmOverwrite" />.</summary>
public enum OverwriteAnswer
{
    /// <summary>Overwrite this item.</summary>
    Yes,

    /// <summary>Skip this item, keeping the existing item.</summary>
    No,

    /// <summary>Overwrite this and all following existing items without asking.</summary>
    All
}

/// <summary>
/// Request to confirm overwriting an item that already exists in the destination of a copy.
/// </summary>
/// <param name="Name">Name of the item in the destination.</param>
/// <param name="Source">Item being copied.</param>
/// <param name="Existing">Item that already exists in the destination.</param>
public record OverwriteRequest(string Name, IEntry Source, IEntry Existing)
{
    /// <summary>
    /// Message shown in the overwrite dialogs of the front-ends, with sizes in bytes and dates of both items,
    /// e.g. "New:      1,234,567 bytes, 2026-10-13 12:00".
    /// </summary>
    public string Message =>
        $"File '{Name}' already exists. Overwrite?\n\n" +
        $"New:      {Describe(Source)}\n" +
        $"Existing: {Describe(Existing)}";

    private static string Describe(IEntry entry)
    {
        var size = entry.Type == EntryType.Container
            ? SizeFormatter.DirectorySize
            : entry.Size == null
                ? "unknown size"
                : string.Create(CultureInfo.InvariantCulture, $"{entry.Size.Value:N0} bytes");
        var date = entry.ModifiedDate?.ToString("yyyy-MM-dd HH:mm");
        return date == null ? size : $"{size}, {date}";
    }
}
