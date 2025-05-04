namespace Hst.Commander.Plugins;

public interface IEntry
{
    string Name { get; }
    EntryType Type { get; }
    long Size { get; }
    DateTime Date { get; }
}

public enum EntryType
{
    Container,
    Item
}