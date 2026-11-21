namespace Hst.Commander.Abstractions;

/// <summary>
/// Type of an <see cref="IEntry" />.
/// </summary>
public enum EntryType
{
    /// <summary>Entry holding other entries, e.g. a directory, a partition or a share.</summary>
    Container,

    /// <summary>Entry with contents read with <see cref="IVolume.OpenRead" />, e.g. a file.</summary>
    Item
}
