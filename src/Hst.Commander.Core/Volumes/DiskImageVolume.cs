using DiscUtils.Raw;
using DiscUtils.Streams;
using Hst.Commander.Abstractions;

namespace Hst.Commander.Core.Volumes;

public class DiskImageVolume(Stream stream) : IVolume
{
    private readonly Disk disk = new(stream, Ownership.None);
    private IList<string> _currentPathComponents = [];

    public string Name => "Disk Image";
    
    public IEnumerable<string> CurrentPathComponents { get; }
    
    public IAsyncEnumerable<IEntry> ListEntries(CancellationToken cancellationToken = default)
    {
        // list partition tables at top level: MBR, GPT, RDB
        // list partitions inside partition tables: 1, 2, 3.
        //
        throw new NotImplementedException();
    }

    public Task ChangeDirectory(IEnumerable<string> pathComponents, CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException();
    }

    public Task<IEntry> OpenEntry(string name, CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException();
    }

    public Task<Stream> OpenRead(string name, CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException();
    }

    public Task<Stream> OpenWrite(string name, CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException();
    }

    public Task CreateContainer(string name, CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException();
    }

    public Task Delete(string name, CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException();
    }

    public Task Rename(string name, string newName, CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException();
    }

    public Task SetModifiedDate(string name, DateTime modifiedDate, CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException();
    }
}