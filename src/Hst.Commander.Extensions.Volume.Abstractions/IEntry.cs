namespace Hst.Commander.Extensions.Volume.Abstractions;

public interface IEntry
{
    string Name { get; }
    EntryType Type { get; }
    long? Size { get; }
    DateTime? ModifiedDate { get; }
}