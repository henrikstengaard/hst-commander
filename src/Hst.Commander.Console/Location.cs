namespace Hst.Commander.Console;

public class Location
{
    public readonly string Path;
    public readonly int CursorPosition;

    public Location()
        : this(string.Empty, 0)
    {
    }

    public Location(string path, int cursorPosition)
    {
        Path = path;
        CursorPosition = cursorPosition;
    }
}