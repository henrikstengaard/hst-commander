namespace Hst.Commander.Abstractions;

/// <summary>
/// A column of the panes the user can add in the settings, e.g. the Amiga protection bits. Hst Commander has the
/// built-in Name, Size, Modified and Attributes columns; plugins add theirs with <see cref="IPlugin.EntryColumns" />.
/// The panes show the columns the user chose for each pane, in the user's order, and sort by a column when its header
/// is clicked.
/// </summary>
public interface IEntryColumn
{
    /// <summary>
    /// Id of the column stored in the settings, unique among all columns, e.g. "Amiga.ProtectionBits". Prefix it with
    /// the plugin's name.
    /// </summary>
    string Id { get; }

    /// <summary>Title of the column in the settings, e.g. "Amiga attributes".</summary>
    string Title { get; }

    /// <summary>Header of the column in the panes, e.g. "Attributes". Defaults to <see cref="Title" />.</summary>
    string Header => Title;

    /// <summary>
    /// Width of the column's texts in characters, e.g. 8 for "hsparwed"; the column is made wide enough for its header
    /// with the sort arrow.
    /// </summary>
    int Width { get; }

    /// <summary>True when the texts are aligned to the end (right) of the column, like sizes. Defaults to false.</summary>
    bool AlignEnd => false;

    /// <summary>
    /// True when the column uses <see cref="IEntryColumnContext.GetAmigaMetadata" />, so Hst Commander reads the UAE
    /// metadata of the listed directory while the column is shown. Defaults to false.
    /// </summary>
    bool UsesAmigaMetadata => false;

    /// <summary>Text of the column for an entry, empty when it has none. Called on the UI thread, so it must be quick.</summary>
    string GetText(IEntry entry, IEntryColumnContext context);

    /// <summary>
    /// Compares two entries for sorting by the column, ascending. Directories are always listed before files and ".."
    /// first, and entries comparing equal stay in the order they were listed in. Defaults to comparing the texts.
    /// </summary>
    int Compare(IEntry x, IEntry y, IEntryColumnContext context) =>
        string.CompareOrdinal(GetText(x, context), GetText(y, context));
}

/// <summary>
/// The location of the entries a column shows, given to <see cref="IEntryColumn.GetText" /> and
/// <see cref="IEntryColumn.Compare" />.
/// </summary>
public interface IEntryColumnContext
{
    /// <summary>True when the location stores Amiga metadata, e.g. an Amiga file system (see <see cref="IVolume.SupportsAmigaMetadata" />).</summary>
    bool SupportsAmigaMetadata { get; }

    /// <summary>
    /// Amiga protection bits and comment of an entry: those of the entry (<see cref="IEntry.AmigaMetadata" />) or those
    /// stored as UAE metadata, e.g. for an Amiga file copied to a local directory. Null when it has none. UAE metadata
    /// is only read while a column with <see cref="IEntryColumn.UsesAmigaMetadata" /> is shown.
    /// </summary>
    AmigaMetadata? GetAmigaMetadata(IEntry entry);

    /// <summary>
    /// Text of the built-in Attributes column for an entry: its DOS/Windows attributes (<see cref="IEntry.Attributes" />)
    /// or the permissions of a local file on Linux and macOS, empty when it has none.
    /// </summary>
    string GetAttributesText(IEntry entry);
}
