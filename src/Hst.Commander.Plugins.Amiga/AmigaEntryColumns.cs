using Hst.Commander.Abstractions;

namespace Hst.Commander.Plugins.Amiga;

/// <summary>
/// Columns of the panes added by the Amiga plugin: the protection bits of Amiga entries and of entries with UAE
/// metadata, and Amiga attributes showing the protection bits inside Amiga volumes and for entries with UAE metadata,
/// otherwise the regular attributes.
/// </summary>
public static class AmigaEntryColumns
{
    public static IEntryColumn ProtectionBits { get; } = new ProtectionBitsColumn();

    public static IEntryColumn Attributes { get; } = new AttributesColumn();

    public static IReadOnlyList<IEntryColumn> All { get; } = [ProtectionBits, Attributes];

    /// <summary>
    /// Protection bits like the Amiga List command, e.g. "----rwed": h, s, p and a are shown when set, r, w, e and d when
    /// reading, writing, executing or deleting is allowed (their bits are set when it isn't).
    /// </summary>
    public static string FormatProtectionBits(int protectionBits)
    {
        const string letters = "hsparwed";
        var characters = new char[letters.Length];
        for (var index = 0; index < letters.Length; index++)
        {
            var bit = letters.Length - 1 - index;
            var isSet = (protectionBits & (1 << bit)) != 0;
            characters[index] = isSet == bit >= 4 ? letters[index] : '-';
        }

        return new string(characters);
    }

    private sealed class ProtectionBitsColumn : IEntryColumn
    {
        public string Id => "Amiga.ProtectionBits";
        public string Title => "Protection bits";
        public string Header => "Protection";
        public int Width => 8;
        public bool UsesAmigaMetadata => true;

        public string GetText(IEntry entry, IEntryColumnContext context) =>
            context.GetAmigaMetadata(entry) is { } metadata
                ? FormatProtectionBits(metadata.ProtectionBits)
                : string.Empty;
    }

    private sealed class AttributesColumn : IEntryColumn
    {
        public string Id => "Amiga.Attributes";
        public string Title => "Amiga attributes";
        public string Header => "Attributes";
        public int Width => 9;
        public bool UsesAmigaMetadata => true;

        // inside an Amiga volume entries without metadata (e.g. partitions) show nothing rather than DOS attributes
        public string GetText(IEntry entry, IEntryColumnContext context) =>
            context.GetAmigaMetadata(entry) is { } metadata
                ? FormatProtectionBits(metadata.ProtectionBits)
                : context.SupportsAmigaMetadata
                    ? string.Empty
                    : context.GetAttributesText(entry);
    }
}
