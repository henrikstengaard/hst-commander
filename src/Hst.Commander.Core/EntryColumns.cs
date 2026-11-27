using Hst.Commander.Abstractions;
using Hst.Commander.Core.Volumes;

namespace Hst.Commander.Core;

/// <summary>
///     Columns of the panes, shared by the front-ends: the built-in Name, Size, Modified and Attributes columns and those
///     of the plugins (<see cref="IPlugin.EntryColumns" />). Each pane shows the columns chosen in the settings, by id
///     (<see cref="Resolve" />); Name is always shown, as the panes need it.
/// </summary>
public static class EntryColumns
{
    public const string NameId = "Name";
    public const string SizeId = "Size";
    public const string ModifiedId = "Modified";
    public const string AttributesId = "Attributes";

    /// <summary>Format of the dates of the Modified column.</summary>
    public const string DateFormat = "yyyy-MM-dd HH:mm";

    public static IEntryColumn Name { get; } = new NameColumn();
    public static IEntryColumn Size { get; } = new SizeColumn();
    public static IEntryColumn Modified { get; } = new ModifiedColumn();
    public static IEntryColumn Attributes { get; } = new AttributesColumn();

    /// <summary>The built-in columns, in the order they're listed in the settings.</summary>
    public static IReadOnlyList<IEntryColumn> BuiltIn { get; } = [Name, Size, Modified, Attributes];

    /// <summary>Ids of the columns shown when none are chosen in the settings.</summary>
    public static IReadOnlyList<string> Default { get; } = [NameId, SizeId, ModifiedId];

    /// <summary>The built-in columns followed by those of the plugins, in plugin order.</summary>
    public static IReadOnlyList<IEntryColumn> All => [..BuiltIn, ..VolumeProviders.EntryColumns];

    /// <summary>The column with an id, null when there is none, e.g. of a plugin that isn't loaded.</summary>
    public static IEntryColumn? Find(string? id) =>
        id == null ? null : All.FirstOrDefault(column => column.Id.Equals(id, StringComparison.Ordinal));

    /// <summary>
    ///     The columns shown for ids from the settings, null for <see cref="Default" />: unknown ids (e.g. of a plugin
    ///     that isn't loaded) and duplicates are left out, and Name is added first when missing.
    /// </summary>
    public static IReadOnlyList<IEntryColumn> Resolve(IReadOnlyList<string>? ids)
    {
        var all = All;
        var columns = new List<IEntryColumn>();
        foreach (var id in ids ?? Default)
        {
            if (all.FirstOrDefault(column => column.Id.Equals(id, StringComparison.Ordinal)) is { } column &&
                !columns.Contains(column))
            {
                columns.Add(column);
            }
        }

        if (!columns.Contains(Name))
        {
            columns.Insert(0, Name);
        }

        return columns;
    }

    /// <summary>
    ///     Width of a column (except Name) in characters in the panes: its <see cref="IEntryColumn.Width" />, widened
    ///     for its header with the sort arrow, e.g. "Size ▲".
    /// </summary>
    public static int GetWidth(IEntryColumn column) => Math.Max(column.Width, column.Header.Length + 2);

    /// <summary>Index of a column in a list of columns, -1 when it isn't in it.</summary>
    public static int IndexOf(IReadOnlyList<IEntryColumn> columns, IEntryColumn column)
    {
        for (var index = 0; index < columns.Count; index++)
        {
            if (columns[index] == column)
            {
                return index;
            }
        }

        return -1;
    }

    /// <summary>Ids of columns, as saved in the settings.</summary>
    public static IReadOnlyList<string> GetIds(IEnumerable<IEntryColumn> columns) =>
        columns.Select(column => column.Id).ToList();

    /// <summary>
    ///     DOS/Windows attributes as the letters "rhsa" (read-only, hidden, system, archive), "-" for those not set, like
    ///     the attributes columns of other file managers.
    /// </summary>
    public static string FormatAttributes(EntryAttributes attributes) => new([
        attributes.HasFlag(EntryAttributes.ReadOnly) ? 'r' : '-',
        attributes.HasFlag(EntryAttributes.Hidden) ? 'h' : '-',
        attributes.HasFlag(EntryAttributes.System) ? 's' : '-',
        attributes.HasFlag(EntryAttributes.Archive) ? 'a' : '-'
    ]);

    /// <summary>
    ///     Text of the Attributes column: the DOS/Windows attributes of an entry (<see cref="FormatAttributes" />), the
    ///     permissions of a local file on Linux and macOS (e.g. "rwxr-xr-x"), otherwise empty.
    /// </summary>
    public static string GetAttributesText(IEntry entry) => entry switch
    {
        { Attributes: { } attributes } => FormatAttributes(attributes),
        Entry { UnixMode: { } mode } => FileSystemDetails.FormatUnixPermissions(mode),
        _ => string.Empty
    };

    private sealed class NameColumn : IEntryColumn
    {
        public string Id => NameId;
        public string Title => "Name";
        public int Width => 0;
        public string GetText(IEntry entry, IEntryColumnContext context) => entry.DisplayName;

        // the order the volume lists the entries in, see EntrySort
        public int Compare(IEntry x, IEntry y, IEntryColumnContext context) => 0;
    }

    private sealed class SizeColumn : IEntryColumn
    {
        public string Id => SizeId;
        public string Title => "Size";
        public int Width => 8;
        public bool AlignEnd => true;

        public string GetText(IEntry entry, IEntryColumnContext context) => context is EntryColumnContext columnContext
            ? columnContext.FormatEntrySize(entry)
            : SizeFormatter.FormatEntrySize(entry);

        public int Compare(IEntry x, IEntry y, IEntryColumnContext context) => Nullable.Compare(x.Size, y.Size);
    }

    private sealed class ModifiedColumn : IEntryColumn
    {
        public string Id => ModifiedId;
        public string Title => "Modified";
        public int Width => 16;

        public string GetText(IEntry entry, IEntryColumnContext context) =>
            entry.ModifiedDate?.ToString(DateFormat) ?? string.Empty;

        public int Compare(IEntry x, IEntry y, IEntryColumnContext context) =>
            Nullable.Compare(x.ModifiedDate, y.ModifiedDate);
    }

    private sealed class AttributesColumn : IEntryColumn
    {
        public string Id => AttributesId;
        public string Title => "Attributes";
        public int Width => 9;
        public string GetText(IEntry entry, IEntryColumnContext context) => context.GetAttributesText(entry);
    }
}

/// <summary>
///     The location of the entries of a pane given to the columns (<see cref="IEntryColumn.GetText" />), updated by
///     <see cref="NavigationContext" /> when it lists entries. A context without a navigation context (e.g. in tests)
///     has no UAE metadata.
/// </summary>
public class EntryColumnContext : IEntryColumnContext
{
    private IReadOnlyDictionary<string, AmigaMetadata> _uaeMetadata = new Dictionary<string, AmigaMetadata>();

    public bool SupportsAmigaMetadata { get; internal set; }

    public AmigaMetadata? GetAmigaMetadata(IEntry entry) =>
        entry.AmigaMetadata ?? (_uaeMetadata.TryGetValue(entry.Name, out var metadata) ? metadata : null);

    public string GetAttributesText(IEntry entry) => EntryColumns.GetAttributesText(entry);

    /// <summary>Size column of an entry, see <see cref="NavigationContext.FormatEntrySize" />.</summary>
    public virtual string FormatEntrySize(IEntry entry) => SizeFormatter.FormatEntrySize(entry);

    internal void SetUaeMetadata(IReadOnlyDictionary<string, AmigaMetadata> uaeMetadata) => _uaeMetadata = uaeMetadata;
}
