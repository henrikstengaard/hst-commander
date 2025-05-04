namespace Hst.Commander.Console;

public class NavigationContext
{
    public readonly Stack<Provider> Providers;

    public NavigationContext()
    {
        Providers = new Stack<Provider>();
    }

    public bool HasParent => Providers.Count > 0 && Providers.Peek().HasLocations;

    public Location GetLocation()
    {
        var provider = Providers.Peek();
        return provider.GetLocation();
    }

    public Location Parent()
    {
        var provider = Providers.Peek();

        if (provider.HasLocations)
        {
            return provider.RemoveLocation();
        }

        if (Providers.Count <= 1)
        {
            return new Location();
        }

        provider = Providers.Pop();
        return provider.RemoveLocation();
    }
}