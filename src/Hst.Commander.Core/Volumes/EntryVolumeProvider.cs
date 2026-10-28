using Hst.Commander.Abstractions;

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

    public bool IsAtRoot
    {
        get
        {
            var pathComponents = volumeProvider.CurrentPathComponents.ToArray();
            return pathComponents.Length == 0 ||
                   pathComponents.Length == 1 && pathComponents[0].Equals(Paths.Root, StringComparison.OrdinalIgnoreCase);
        }
    }

    public IAsyncEnumerable<IEntry> ListEntries(CancellationToken cancellationToken = default) =>
        volumeProvider.ListEntries(cancellationToken);

    public Task ChangeDirectory(IEnumerable<string> pathComponents, CancellationToken cancellationToken = default) =>
        volumeProvider.ChangeDirectory(pathComponents, cancellationToken);

    public Task<IEntry> OpenEntry(string name, CancellationToken cancellationToken = default) =>
        volumeProvider.OpenEntry(name, cancellationToken);

    public Task<Stream> OpenRead(string name, CancellationToken cancellationToken = default) =>
        volumeProvider.OpenRead(name, cancellationToken);

    public Task<Stream> OpenWrite(string name, CancellationToken cancellationToken = default) =>
        volumeProvider.OpenWrite(name, cancellationToken);

    public Task CreateContainer(string name, CancellationToken cancellationToken = default) =>
        volumeProvider.CreateContainer(name, cancellationToken);

    public Task Delete(string name, CancellationToken cancellationToken = default) =>
        volumeProvider.Delete(name, cancellationToken);

    public Task Rename(string name, string newName, CancellationToken cancellationToken = default) =>
        volumeProvider.Rename(name, newName, cancellationToken);

    public Task SetModifiedDate(string name, DateTime modifiedDate, CancellationToken cancellationToken = default) =>
        volumeProvider.SetModifiedDate(name, modifiedDate, cancellationToken);

    public Task<bool> SupportsAmigaMetadata(CancellationToken cancellationToken = default) =>
        volumeProvider.SupportsAmigaMetadata(cancellationToken);

    public Task SetAmigaMetadata(string name, AmigaMetadata metadata, CancellationToken cancellationToken = default) =>
        volumeProvider.SetAmigaMetadata(name, metadata, cancellationToken);

    public Task<bool> CanSetAmigaMetadata(CancellationToken cancellationToken = default) =>
        volumeProvider.CanSetAmigaMetadata(cancellationToken);

    public Task<EntryAttributes> GetChangeableAttributes(CancellationToken cancellationToken = default) =>
        volumeProvider.GetChangeableAttributes(cancellationToken);

    public Task SetAttributes(string name, EntryAttributes attributes, CancellationToken cancellationToken = default) =>
        volumeProvider.SetAttributes(name, attributes, cancellationToken);

    public string? GetWritableImageId(string name) => volumeProvider.GetWritableImageId(name);

    public Task<Stream> OpenReadWrite(string name, CancellationToken cancellationToken = default) =>
        volumeProvider.OpenReadWrite(name, cancellationToken);
}