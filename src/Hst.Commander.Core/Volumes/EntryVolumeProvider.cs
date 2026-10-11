using Hst.Commander.Extensions.Volume.Abstractions;

namespace Hst.Commander.Core.Volumes;

/// <summary>
/// Entry volume provider makes an entry appear as a volume provider with the volume providers beneath it.
/// </summary>
public class EntryVolumeProvider(IEntry entry, IVolume volumeProvider) : IVolume
{
    /// <summary>
    /// Name of the entry volume provider.
    /// </summary>
    public string Name => volumeProvider.Name;

    public IEnumerable<string> CurrentPathComponents => new []{entry.Name}.Concat(volumeProvider.CurrentPathComponents);

    public IAsyncEnumerable<IEntry> ListEntries(CancellationToken cancellationToken = default) =>
        volumeProvider.ListEntries(cancellationToken);

    public Task ChangeDirectory(IEnumerable<string> pathComponents, CancellationToken cancellationToken = default) =>
        volumeProvider.ChangeDirectory(pathComponents, cancellationToken);

    public Task<IEntry> OpenEntry(string name, CancellationToken cancellationToken = default) =>
        volumeProvider.OpenEntry(name, cancellationToken);
}