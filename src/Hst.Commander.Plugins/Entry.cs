namespace Hst.Commander.Plugins;

public class Entry : IEntry
{
    public string Name { get; }
    public EntryType Type { get; }
    public long Size { get; }
    public DateTime Date { get; }

    public Entry(string name, EntryType type, long size, DateTime date)
    {
        Name = name;
        Type = type;
        Size = size;
        Date = date;
    }

    public override string ToString()
    {
        return Name;
    }
}