namespace Hst.Commander.Abstractions;

public interface IEntry
{
    string Name { get; }
    EntryType Type { get; }
    long? Size { get; }
    DateTime? ModifiedDate { get; }

    /// <summary>
    /// Name shown in the panes, e.g. "DH0" for the RDB partition named "1" with drive name DH0. Paths and file
    /// operations always use <see cref="Name" />. Defaults to the name.
    /// </summary>
    string DisplayName => Name;

    /// <summary>
    /// True for a container that is the root of a volume of its own, e.g. a partition of a hard disk image, which can
    /// hold its own icon inside it (see <see cref="IIconProvider.GetVolumeIconFileName" />). Defaults to false.
    /// </summary>
    bool IsVolume => false;
}
