using System.ComponentModel;
using Avalonia.Media;
using Hst.Commander.Core;
using Hst.Commander.Gui.Icons;
using Hst.Commander.Abstractions;

namespace Hst.Commander.Gui;

/// <summary>
///     Display row for an entry in a pane, with the texts of the pane's columns (see <see cref="EntryRowCells" />).
/// </summary>
public sealed class EntryItem : INotifyPropertyChanged
{
    private bool _isMarked;
    private string _size;
    private bool _isVolumeFile;
    private IImage _icon;

    /// <param name="entry">The entry.</param>
    /// <param name="isMarked">True when the entry is marked.</param>
    /// <param name="isExecutable">True when the entry is an executable file.</param>
    /// <param name="columns">Columns of the pane, by default <see cref="EntryColumns.Default" />.</param>
    /// <param name="getText">Text of a column for the entry, by default without a pane's location.</param>
    public EntryItem(IEntry entry, bool isMarked = false, bool isExecutable = false,
        IReadOnlyList<IEntryColumn>? columns = null, Func<IEntryColumn, IEntry, string>? getText = null)
    {
        Entry = entry;
        _icon = FileIcons.GetInitialIcon(entry);
        _isMarked = isMarked;
        IsExecutable = isExecutable;
        Columns = columns ?? EntryColumns.Resolve(null);
        var context = new EntryColumnContext();
        getText ??= (column, listedEntry) => column.GetText(listedEntry, context);
        Texts = Columns.Select(column => column == EntryColumns.Name ? string.Empty : getText(column, entry))
            .ToArray();
        _size = EntryColumns.IndexOf(Columns, EntryColumns.Size) is var sizeIndex and >= 0
            ? Texts[sizeIndex]
            : SizeFormatter.FormatEntrySize(entry);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public IEntry Entry { get; }

    /// <summary>Columns of the pane the entry is shown in, shared by its rows.</summary>
    public IReadOnlyList<IEntryColumn> Columns { get; }

    /// <summary>Texts of the columns, empty for the Name column, which shows the icon and <see cref="DisplayName" />.</summary>
    public IReadOnlyList<string> Texts { get; private set; }

    public string Name => Entry.Name;

    /// <summary>
    /// Name shown in the pane, e.g. "1 - DH0 - Workbench" for an RDB partition (<see cref="IEntry.DisplayName" />),
    /// or the name when the display name is empty, e.g. the root drive "/" on macOS and Linux, shown as "" in the drive
    /// list as the TUI shows a slash before directories.
    /// </summary>
    public string DisplayName => string.IsNullOrEmpty(Entry.DisplayName) ? Entry.Name : Entry.DisplayName;

    public bool IsContainer => Entry.Type == EntryType.Container;

    /// <summary>True when the entry is an executable file, shown in the executable color.</summary>
    public bool IsExecutable { get; }

    /// <summary>True when the entry is selected (marked) by the user, shown in the marked entry color.</summary>
    public bool IsMarked
    {
        get => _isMarked;
        set
        {
            if (_isMarked == value)
            {
                return;
            }

            _isMarked = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsMarked)));
        }
    }

    /// <summary>
    ///     True when the entry is a file that can be opened as a volume (<see cref="NavigationContext.IsVolumeFile" />),
    ///     shown with an arrow over its icon.
    /// </summary>
    public bool IsVolumeFile
    {
        get => _isVolumeFile;
        set
        {
            if (_isVolumeFile == value)
            {
                return;
            }

            _isVolumeFile = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Icon)));
        }
    }

    /// <summary>
    ///     Icon shown left of the name, replaced when <see cref="FileIcons" /> has loaded the actual icon. Files that
    ///     can be opened as volumes have an arrow over it (<see cref="FileIcons.GetVolumeIcon" />).
    /// </summary>
    public IImage Icon
    {
        get => _isVolumeFile ? FileIcons.GetVolumeIcon(_icon) : _icon;
        set
        {
            if (ReferenceEquals(_icon, value))
            {
                return;
            }

            _icon = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Icon)));
        }
    }

    /// <summary>
    ///     Size column, "..." while the size of a directory marked with Space is calculated and then its size (see
    ///     <see cref="NavigationContext.FormatEntrySize" />).
    /// </summary>
    public string Size
    {
        get => _size;
        set
        {
            if (_size == value)
            {
                return;
            }

            _size = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Size)));
            if (EntryColumns.IndexOf(Columns, EntryColumns.Size) is var sizeIndex and >= 0)
            {
                var texts = Texts.ToArray();
                texts[sizeIndex] = value;
                Texts = texts;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Texts)));
            }
        }
    }
}
