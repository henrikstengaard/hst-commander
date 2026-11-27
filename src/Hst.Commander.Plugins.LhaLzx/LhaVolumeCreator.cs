using Hst.Commander.Abstractions;

namespace Hst.Commander.Plugins.LhaLzx;

/// <summary>
/// Creates an empty lha archive (F9): only the end of archive marker, a zero byte. Files copied into the archive are
/// added as Amiga entries with header level 2 (see <see cref="LhaVolume" />), as an empty archive can't store the
/// header level or compression method to use.
/// </summary>
public class LhaVolumeCreator : IVolumeCreator
{
    public string Name => "Lha archive";

    public string Description => "Empty lha archive (Amiga format)";

    public string FileExtension => ".lha";

    public IReadOnlyList<VolumeCreatorField> Fields { get; } = [];

    public async Task Create(Stream stream, VolumeCreatorValues values, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);

        await stream.WriteAsync(new byte[] { 0 }, cancellationToken);
    }
}
