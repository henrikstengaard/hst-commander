namespace Hst.Commander.Extensions.Volume.Abstractions;

/// <summary>
/// Interface for providing a volume to Hst Commander.
/// </summary>
public interface IVolume
{
    /// <summary>
    /// Name of volume.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Path components of current directory.
    /// </summary>
    public IEnumerable<string> CurrentPathComponents { get; }

    /// <summary>
    /// List entries in current directory.
    /// </summary>
    /// <returns></returns>
    IAsyncEnumerable<IEntry> ListEntries(CancellationToken cancellationToken = default);

    /// <summary>
    /// Change directory with absolute or relative path components.
    /// </summary>
    /// <param name="pathComponents">Path components to change to.</param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    Task ChangeDirectory(IEnumerable<string> pathComponents, CancellationToken cancellationToken = default);
    
    Task<IEntry> OpenEntry(string name, CancellationToken cancellationToken = default);
}