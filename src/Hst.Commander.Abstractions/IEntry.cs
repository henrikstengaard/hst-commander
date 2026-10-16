namespace Hst.Commander.Abstractions;

public interface IEntry
{
    string Name { get; }
    EntryType Type { get; }
    long? Size { get; }
    DateTime? ModifiedDate { get; }
}