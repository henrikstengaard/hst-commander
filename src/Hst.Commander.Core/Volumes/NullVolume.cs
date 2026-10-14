using Hst.Commander.Extensions.Volume.Abstractions;

namespace Hst.Commander.Core.Volumes;

/// <summary>
/// Null volume implementation that does nothing
/// </summary>
public class NullVolume : IVolume
{
    public string Name => string.Empty;
    
    public IEnumerable<string> CurrentPathComponents => Enumerable.Empty<string>();

    public IAsyncEnumerable<IEntry> ListEntries(CancellationToken cancellationToken = default) =>
        AsyncEnumerable.Empty<IEntry>();

    public Task ChangeDirectory(IEnumerable<string> pathComponents, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    public Task<IEntry> OpenEntry(string name, CancellationToken cancellationToken = default) =>
        Task.FromResult<IEntry>(new Entry(".", EntryType.Container, 0, new DateTime()));

    public Task<Stream> OpenRead(string name, CancellationToken cancellationToken = default) =>
        throw new IOException($"File not found '{name}'");

    public Task<Stream> OpenWrite(string name, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Null volume is read-only");

    public Task CreateContainer(string name, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Null volume is read-only");

    public Task Delete(string name, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Null volume is read-only");

    public Task Rename(string name, string newName, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Null volume is read-only");

    public Task SetModifiedDate(string name, DateTime modifiedDate, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Null volume is read-only");
}