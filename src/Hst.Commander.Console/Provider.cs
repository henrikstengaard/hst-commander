namespace Hst.Commander.Console;

using Plugins;

public class Provider
{
    public readonly IContainerProvider ContainerProvider;
    private readonly Stack<Location> locations;

    public Provider(IContainerProvider containerProvider)
    {
        ContainerProvider = containerProvider;
        locations = new Stack<Location>();
    }

    public bool HasLocations => locations.Count > 0;

    public Location GetLocation()
    {
        return HasLocations ? locations.Peek() : new Location();
    }

    public Location PrepareLocation(string path, int cursorPosition)
    {
        var newPath = HasLocations
            ? ContainerProvider.CombinePath(GetLocation().Path, path)
            : path;
        return new Location(newPath, cursorPosition);
    }

    public Location RemoveLocation()
    {
        return HasLocations ? locations.Pop() : new Location();
    }

    public void AddLocation(Location location)
    {
        locations.Push(location);
    }
}