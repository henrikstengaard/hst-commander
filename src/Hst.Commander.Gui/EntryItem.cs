using Hst.Commander.Core;
using Hst.Commander.Extensions.Volume.Abstractions;

namespace Hst.Commander.Gui;

/// <summary>
///     Display row for an entry in a pane.
/// </summary>
public sealed class EntryItem(IEntry entry)
{
    public IEntry Entry { get; } = entry;

    public string Name => Entry.Name;

    public bool IsContainer => Entry.Type == EntryType.Container;

    public string Size => SizeFormatter.FormatEntrySize(Entry);

    public string Modified => Entry.ModifiedDate?.ToString("yyyy-MM-dd HH:mm") ?? string.Empty;
}
