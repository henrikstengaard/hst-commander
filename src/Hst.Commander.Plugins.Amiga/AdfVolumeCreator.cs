using Hst.Amiga;
using Hst.Amiga.FileSystems.FastFileSystem;
using Hst.Amiga.FileSystems.FastFileSystem.Blocks;
using Hst.Commander.Abstractions;

namespace Hst.Commander.Plugins.Amiga;

/// <summary>
/// Volume creator creating a double density (880 KB) Amiga disk file (adf), formatted with the Fast File System
/// (DOS\3) and optionally bootable, or unformatted (all zeros) when format isn't checked.
/// </summary>
public class AdfVolumeCreator : IVolumeCreator
{
    public const string VolumeNameField = "VolumeName";
    public const string FormatField = "Format";
    public const string BootableField = "Bootable";

    private const int MaxVolumeNameLength = 30;
    private const int BootBlockSize = 1024;
    private static readonly byte[] Dos3DosType = [(byte)'D', (byte)'O', (byte)'S', 3];

    public string Name => "Amiga disk file";

    public string Description => "Double density floppy disk (880 KB)";

    public string FileExtension => ".adf";

    public IReadOnlyList<VolumeCreatorField> Fields { get; } =
    [
        new(VolumeNameField, "Volume name", VolumeCreatorFieldType.Text, true, "Empty"),
        new(FormatField, "Format", VolumeCreatorFieldType.Boolean, false, true),
        new(BootableField, "Bootable", VolumeCreatorFieldType.Boolean, false, false)
    ];

    public async Task Create(Stream stream, VolumeCreatorValues values, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(values);

        var format = values.GetBoolean(FormatField);
        var volumeName = values.GetText(VolumeNameField);
        if (format)
        {
            ValidateVolumeName(volumeName);
        }

        stream.SetLength(FloppyDiskConstants.DoubleDensity.Size);
        if (!format)
        {
            return;
        }

        await FastFileSystemFormatter.Format(stream, FloppyDiskConstants.DoubleDensity.LowCyl,
            FloppyDiskConstants.DoubleDensity.HighCyl, FloppyDiskConstants.DoubleDensity.ReservedBlocks,
            FloppyDiskConstants.DoubleDensity.Heads, FloppyDiskConstants.DoubleDensity.Sectors,
            FloppyDiskConstants.BlockSize, FloppyDiskConstants.FileSystemBlockSize, Dos3DosType, volumeName);
        cancellationToken.ThrowIfCancellationRequested();

        if (values.GetBoolean(BootableField))
        {
            // boot block with the dos type, root block offset and the standard boot code
            var bootBlock = BootBlockBuilder.Build(new BootBlock { DosType = Dos3DosType }, BootBlockSize);
            stream.Position = 0;
            await stream.WriteAsync(bootBlock, cancellationToken);
        }

        await stream.FlushAsync(cancellationToken);
    }

    private static void ValidateVolumeName(string volumeName)
    {
        if (volumeName.Length == 0)
        {
            throw new IOException("Volume name is empty");
        }

        if (volumeName.IndexOfAny(['/', ':']) >= 0)
        {
            throw new IOException($"Invalid volume name '{volumeName}', it can't contain '/' or ':'");
        }

        if (volumeName.Length > MaxVolumeNameLength)
        {
            throw new IOException(
                $"Volume name '{volumeName}' is longer than the maximum of {MaxVolumeNameLength} characters");
        }
    }
}
