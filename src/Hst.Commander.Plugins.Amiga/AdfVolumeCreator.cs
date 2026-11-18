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
    public const string HighDensityField = "HighDensity";
    public const string FormatField = "Format";
    public const string FileSystemField = "FileSystem";
    public const string BootableField = "Bootable";

    private const int MaxVolumeNameLength = 30;
    private const int BootBlockSize = 1024;
    private const string DefaultFileSystem = "DOS3";

    private static readonly VolumeCreatorChoice[] FileSystems =
    [
        new("DOS0", "DOS0 (OFS)"),
        new("DOS1", "DOS1 (FFS)"),
        new("DOS2", "DOS2 (OFS, international)"),
        new("DOS3", "DOS3 (FFS, international)"),
        new("DOS4", "DOS4 (OFS, directory cache)"),
        new("DOS5", "DOS5 (FFS, directory cache)"),
        new("DOS6", "DOS6 (OFS, long file names)"),
        new("DOS7", "DOS7 (FFS, long file names)")
    ];

    public string Name => "Amiga disk file";

    public string Description => "Double density (880 KB) or high density (1.76 MB) floppy disk";

    public string FileExtension => ".adf";

    public IReadOnlyList<VolumeCreatorField> Fields { get; } =
    [
        new(VolumeNameField, "Volume name", VolumeCreatorFieldType.Text, true, "Empty", EnabledByField: FormatField),
        new(HighDensityField, "High density (1.76 MB)", VolumeCreatorFieldType.Boolean, false, false),
        new(FormatField, "Format", VolumeCreatorFieldType.Boolean, false, true),
        new(BootableField, "Bootable", VolumeCreatorFieldType.Boolean, false, false, EnabledByField: FormatField),
        new(FileSystemField, "File system", VolumeCreatorFieldType.Choice, false, DefaultFileSystem, FileSystems,
            FormatField)
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

        var highDensity = values.GetBoolean(HighDensityField);
        stream.SetLength(highDensity ? FloppyDiskConstants.HighDensity.Size : FloppyDiskConstants.DoubleDensity.Size);
        if (!format)
        {
            return;
        }

        var dosType = GetDosType(values.GetText(FileSystemField));
        if (highDensity)
        {
            await FastFileSystemFormatter.Format(stream, FloppyDiskConstants.HighDensity.LowCyl,
                FloppyDiskConstants.HighDensity.HighCyl, FloppyDiskConstants.HighDensity.ReservedBlocks,
                FloppyDiskConstants.HighDensity.Heads, FloppyDiskConstants.HighDensity.Sectors,
                FloppyDiskConstants.BlockSize, FloppyDiskConstants.FileSystemBlockSize, dosType, volumeName,
                cancellationToken);
        }
        else
        {
            await FastFileSystemFormatter.Format(stream, FloppyDiskConstants.DoubleDensity.LowCyl,
                FloppyDiskConstants.DoubleDensity.HighCyl, FloppyDiskConstants.DoubleDensity.ReservedBlocks,
                FloppyDiskConstants.DoubleDensity.Heads, FloppyDiskConstants.DoubleDensity.Sectors,
                FloppyDiskConstants.BlockSize, FloppyDiskConstants.FileSystemBlockSize, dosType, volumeName,
                cancellationToken);
        }

        if (values.GetBoolean(BootableField))
        {
            // boot block with the dos type, root block offset and the standard boot code
            var bootBlock = BootBlockBuilder.Build(new BootBlock { DosType = dosType }, BootBlockSize);
            stream.Position = 0;
            await stream.WriteAsync(bootBlock, cancellationToken);
        }

        await stream.FlushAsync(cancellationToken);
    }

    private static byte[] GetDosType(string fileSystem)
    {
        // DOS0-DOS7, DOS3 when not chosen
        if (fileSystem.Length == 0)
        {
            fileSystem = DefaultFileSystem;
        }

        if (fileSystem.Length != 4 || !fileSystem.StartsWith("DOS", StringComparison.Ordinal) ||
            fileSystem[3] < '0' || fileSystem[3] > '7')
        {
            throw new IOException($"Invalid file system '{fileSystem}', it must be DOS0 to DOS7");
        }

        return [(byte)'D', (byte)'O', (byte)'S', (byte)(fileSystem[3] - '0')];
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
