using Hst.Commander.Extensions.Volume.Abstractions;

namespace Hst.Commander.Core.Volumes;

public class NestedVolume(IVolume volume) : IVolume
{
    private readonly LinkedList<IVolume> _nestedVolumes = new([volume]);
    private IVolume _currentVolume = volume;
    
    /// <summary>
    /// Push volume to the list of nested volumes.
    /// Volume is added as the last one in the list and also set as the current volume.
    /// </summary>
    /// <param name="volume">Volume to push.</param>
    public void PushVolume(IVolume volume)
    {
        ArgumentNullException.ThrowIfNull(volume);
        _nestedVolumes.AddLast(volume);
        _currentVolume = volume;
    }

    /// <summary>
    /// Pop volume from the list of nested volumes.
    /// Volume is removed from the list and the last one in the list is set as the current volume.
    /// </summary>
    public void PopVolume()
    {
        if (_nestedVolumes.Count <= 1)
        {
            return;
        }
        _nestedVolumes.RemoveLast();
        _currentVolume = _nestedVolumes.Last?.Value ?? new NullVolume();
    }

    /// <summary>
    /// Current path components of all nested volumes.
    /// </summary>
    public IEnumerable<string> CurrentPathComponents => _nestedVolumes
        .SelectMany(p => p.CurrentPathComponents).ToList();

    /// <summary>
    /// List entries in current directory of the current volumes.
    /// </summary>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    public IAsyncEnumerable<IEntry> ListEntries(CancellationToken cancellationToken =
        default) =>
        _currentVolume.ListEntries(cancellationToken);

    public async Task ChangeDirectory(IEnumerable<string> pathComponents, CancellationToken cancellationToken = default)
    {
        //
        
        var pathComponentsArray = pathComponents.ToArray();
        if (pathComponentsArray.Length == 0)
        {
            return;
        }
        
        var currentPathComponents = _currentVolume.CurrentPathComponents.ToArray();

        foreach (var pathComponent in pathComponentsArray)
        {
            if (pathComponent.Equals(Paths.Root, StringComparison.OrdinalIgnoreCase))
            {
                while (_nestedVolumes.Count > 1)
                {
                    PopVolume();
                }
                
                await _currentVolume.ChangeDirectory([pathComponent], cancellationToken);
                continue;
            }
            
            if (pathComponent.Equals(Paths.Parent, StringComparison.OrdinalIgnoreCase) &&
                currentPathComponents.Length > 0)
            {
                if (!currentPathComponents.Any())
                {
                    await _currentVolume.ChangeDirectory([pathComponent], cancellationToken);
                }
                else
                {
                    PopVolume();
                }
            }
            
            await _currentVolume.ChangeDirectory([pathComponent], cancellationToken);
        }
    }

    public async Task<IEntry> GetEntry(string name, CancellationToken cancellationToken = default) => 
        await _currentVolume.OpenEntry(name, cancellationToken);
    
    public async Task<IEntry> OpenEntry(string name, CancellationToken cancellationToken = default) => 
        await _currentVolume.OpenEntry(name, cancellationToken);

    /// <summary>
    /// Name of the nested volume.
    /// </summary>
    public string Name => string.Join(", ", _nestedVolumes.Select(x => x.Name));
}