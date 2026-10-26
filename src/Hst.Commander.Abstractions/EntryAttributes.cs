namespace Hst.Commander.Abstractions;

/// <summary>
/// Attributes of an entry in file systems with DOS/Windows attributes, e.g. local files on Windows and FAT or NTFS
/// partitions (see <see cref="IEntry.Attributes" /> and <see cref="IVolume.SetAttributes" />). The values are the
/// same as those of <see cref="FileAttributes" />.
/// </summary>
[Flags]
public enum EntryAttributes
{
    None = 0,
    ReadOnly = 1,
    Hidden = 2,
    System = 4,
    Archive = 32
}
