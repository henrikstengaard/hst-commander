using Hst.Commander.Abstractions;

namespace Hst.Commander.Plugins.Zip;

/// <summary>
/// Creates an empty zip archive (F9): only the end of central directory record. Files copied into the archive are
/// added compressed with deflate (see <see cref="ZipVolume" />).
/// </summary>
public class ZipVolumeCreator : IVolumeCreator
{
    public string Name => "Zip archive";

    public string Description => "Empty zip archive";

    public string FileExtension => ".zip";

    public IReadOnlyList<VolumeCreatorField> Fields { get; } = [];

    public Task Create(Stream stream, VolumeCreatorValues values, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);

        ZipFormat.WriteCentralDirectory(stream, [], 0, []);
        return Task.CompletedTask;
    }
}
