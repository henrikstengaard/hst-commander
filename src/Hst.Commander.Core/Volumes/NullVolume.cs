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
}