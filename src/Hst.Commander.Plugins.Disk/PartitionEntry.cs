using Hst.Commander.Abstractions;

namespace Hst.Commander.Plugins.Disk;

/// <summary>
/// Partition listed as a directory, e.g. "1" shown as "1 - EMU68BOOT". It is a volume of its own, so its icon can be
/// inside it.
/// </summary>
internal sealed record PartitionEntry(string Name, string DisplayName, long? Size, EntryDetails? Details) : IEntry
{
    public EntryType Type => EntryType.Container;

    public DateTime? ModifiedDate => null;

    public bool IsVolume => true;
}

/// <summary>Partition table listed as a directory listing its partitions, e.g. "mbr" shown as "MBR".</summary>
internal sealed record PartitionTableEntry(string Name, string DisplayName, EntryDetails Details) : IEntry
{
    public EntryType Type => EntryType.Container;

    public long? Size => null;

    public DateTime? ModifiedDate => null;
}
