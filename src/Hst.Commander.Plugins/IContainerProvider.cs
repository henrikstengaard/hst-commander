namespace Hst.Commander.Plugins;

public interface IContainerProvider
{
    public Task<IEnumerable<IEntry>> GetEntries(string path);

    string CombinePath(string path1, string path2);

    string GetParentPath(string path); 
    Task NewContainer(string path);
}