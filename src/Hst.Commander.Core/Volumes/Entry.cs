using Hst.Commander.Abstractions;

namespace Hst.Commander.Core.Volumes;

public class Entry(string name, EntryType type, long? size = null, DateTime? date = null, string? displayName = null)
    : IEntry
{
    public string Name { get; } = name;
    public string DisplayName { get; } = displayName ?? name;
    public EntryType Type { get; } = type;
    public long? Size { get; } = size;
    public DateTime? ModifiedDate { get; } = date;
}