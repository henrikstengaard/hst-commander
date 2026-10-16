using Hst.Commander.Abstractions;

namespace Hst.Commander.Core.Tests;

public class TestEntry(string name, EntryType type, long? size = null, DateTime? date = null, byte[]? data = null)
    : IEntry
{
    public string Name { get; set; } = name;
    public EntryType Type { get; } = type;
    public long? Size { get; set; } = size;
    public DateTime? ModifiedDate { get; set; } = date;
    public byte[]? Data { get; set; } = data;
}