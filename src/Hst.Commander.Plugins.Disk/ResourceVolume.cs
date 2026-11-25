using Hst.Commander.Abstractions;

namespace Hst.Commander.Plugins.Disk;

/// <summary>
/// Volume passing everything to another volume and disposing a resource after it, e.g. a volume opened by the host in the
/// disk of a virtual disk image (see <see cref="DiskFileVolumeProvider" />), which disposes the virtual disk.
/// </summary>
internal sealed class ResourceVolume(IVolume volume, IDisposable resource) : IVolume, IDisposable
{
    public string Name => volume.Name;

    public IReadOnlyList<string> CurrentPathComponents => volume.CurrentPathComponents;

    public IAsyncEnumerable<IEntry> ListEntries(CancellationToken cancellationToken = default) =>
        volume.ListEntries(cancellationToken);

    public Task ChangeDirectory(IEnumerable<string> pathComponents, CancellationToken cancellationToken = default) =>
        volume.ChangeDirectory(pathComponents, cancellationToken);

    public Task<Stream> OpenRead(string name, CancellationToken cancellationToken = default) =>
        volume.OpenRead(name, cancellationToken);

    public Task<Stream> OpenWrite(string name, CancellationToken cancellationToken = default) =>
        volume.OpenWrite(name, cancellationToken);

    public Task<Stream> OpenReadWrite(string name, CancellationToken cancellationToken = default) =>
        volume.OpenReadWrite(name, cancellationToken);

    public Task CreateContainer(string name, CancellationToken cancellationToken = default) =>
        volume.CreateContainer(name, cancellationToken);

    public Task Delete(string name, CancellationToken cancellationToken = default) =>
        volume.Delete(name, cancellationToken);

    public Task Rename(string name, string newName, CancellationToken cancellationToken = default) =>
        volume.Rename(name, newName, cancellationToken);

    public Task SetModifiedDate(string name, DateTime modifiedDate, CancellationToken cancellationToken = default) =>
        volume.SetModifiedDate(name, modifiedDate, cancellationToken);

    public Task<bool> SupportsAmigaMetadata(CancellationToken cancellationToken = default) =>
        volume.SupportsAmigaMetadata(cancellationToken);

    public Task SetAmigaMetadata(string name, AmigaMetadata metadata, CancellationToken cancellationToken = default) =>
        volume.SetAmigaMetadata(name, metadata, cancellationToken);

    public Task<bool> CanSetAmigaMetadata(CancellationToken cancellationToken = default) =>
        volume.CanSetAmigaMetadata(cancellationToken);

    public Task<EntryAttributes> GetChangeableAttributes(CancellationToken cancellationToken = default) =>
        volume.GetChangeableAttributes(cancellationToken);

    public Task SetAttributes(string name, EntryAttributes attributes, CancellationToken cancellationToken = default) =>
        volume.SetAttributes(name, attributes, cancellationToken);

    public Task<VolumeSpace?> GetSpace(CancellationToken cancellationToken = default) =>
        volume.GetSpace(cancellationToken);

    public string? GetWritableImageId(string name) => volume.GetWritableImageId(name);

    public string? GetHostPath(string name) => volume.GetHostPath(name);

    public long? GetReadOrder(string name) => volume.GetReadOrder(name);

    public void Dispose()
    {
        (volume as IDisposable)?.Dispose();
        resource.Dispose();
    }
}
