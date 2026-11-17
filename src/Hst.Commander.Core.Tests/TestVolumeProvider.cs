using Hst.Commander.Abstractions;

namespace Hst.Commander.Core.Tests;

/// <summary>
/// Test volume provider creating test volumes with the entries added by a setup action, optionally with host paths
/// (<see cref="TestVolume.HostPathRoot" />).
/// </summary>
public class TestVolumeProvider(Action<TestVolume> setup, string? hostPathRoot = null) : IVolumeProvider
{
    public string Name => "Test";

    public string Description => "Test volumes";

    public Task<IVolume> CreateVolume(CancellationToken cancellationToken = default)
    {
        var volume = new TestVolume { HostPathRoot = hostPathRoot };
        setup(volume);
        return Task.FromResult<IVolume>(volume);
    }
}
