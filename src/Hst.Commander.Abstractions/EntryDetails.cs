namespace Hst.Commander.Abstractions;

/// <summary>
/// Line of the properties dialog given by a volume, e.g. "Drive name" and "DH0". A size has <see cref="Bytes" />,
/// formatted by Hst Commander like its own sizes, e.g. "1.17 MB (1,234,567 bytes)".
/// </summary>
public sealed record EntryProperty(string Label, string Value)
{
    /// <summary>Size in bytes shown instead of <see cref="Value" />, null when the value is text.</summary>
    public long? Bytes { get; init; }

    /// <summary>Creates a line showing a size in bytes.</summary>
    public static EntryProperty FromBytes(string label, long bytes) => new(label, string.Empty) { Bytes = bytes };
}

/// <summary>
/// Details of an entry shown in the properties dialog that only its volume knows, e.g. the drive name, volume name
/// and file system of an Amiga partition (see <see cref="IEntry.Details" />).
/// </summary>
/// <param name="TypeName">Type shown instead of the file type, e.g. "Amiga partition"; null for the file type.</param>
/// <param name="Sections">Sections of lines shown after the type, separated by lines.</param>
/// <param name="UsedFraction">Fraction of the space used between 0 and 1, shown as a bar like drives; null for none.</param>
public sealed record EntryDetails(
    string? TypeName,
    IReadOnlyList<IReadOnlyList<EntryProperty>> Sections,
    double? UsedFraction = null);
