using Hst.Commander.Extensions.Volume.Abstractions;

namespace Hst.Commander.Core.Tests;

/// <summary>
/// Test volume provider creating test volumes with the entries added by a setup action.
/// </summary>
public class TestVolumeProvider(Action<TestVolume> setup) : IVolumeProvider
{
    public string Name => "Test";

    public string Description => "Test volumes";

    public Task<IVolume> CreateVolume(CancellationToken cancellationToken = default)
    {
        var volume = new TestVolume();
        setup(volume);
        return Task.FromResult<IVolume>(volume);
    }
}
