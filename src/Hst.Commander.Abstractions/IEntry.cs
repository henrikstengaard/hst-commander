namespace Hst.Commander.Abstractions;

public interface IEntry
{
    string Name { get; }
    EntryType Type { get; }
    long? Size { get; }
    DateTime? ModifiedDate { get; }

    /// <summary>
    /// Name shown in the panes and at the top of the properties dialog, e.g. "1 - DH0 - Workbench" for the RDB
    /// partition named "1" with drive name DH0 and volume name Workbench. Paths and file operations always use
    /// <see cref="Name" />. Defaults to the name.
    /// </summary>
    string DisplayName => Name;

    /// <summary>
    /// Details shown in the properties dialog that only the volume knows, e.g. the partition number, drive name and
    /// volume name of an RDB partition. Defaults to null (none).
    /// </summary>
    EntryDetails? Details => null;

    /// <summary>
    /// True for a container that is the root of a volume of its own, e.g. a partition of a hard disk image, which can
    /// hold its own icon inside it (see <see cref="IIconProvider.GetVolumeIconFileName" />). Defaults to false.
    /// </summary>
    bool IsVolume => false;
}
