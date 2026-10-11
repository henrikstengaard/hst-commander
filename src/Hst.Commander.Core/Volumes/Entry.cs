using Hst.Commander.Extensions.Volume.Abstractions;

namespace Hst.Commander.Core.Volumes;

public class Entry(string name, EntryType type, long? size = null, DateTime? date = null)
    : IEntry
{
    public string Name { get; } = name;
    public EntryType Type { get; } = type;
    public long? Size { get; } = size;
    public DateTime? ModifiedDate { get; } = date;
}