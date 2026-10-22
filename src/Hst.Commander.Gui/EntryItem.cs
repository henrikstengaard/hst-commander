using System.ComponentModel;
using Avalonia.Media;
using Hst.Commander.Core;
using Hst.Commander.Gui.Icons;
using Hst.Commander.Abstractions;

namespace Hst.Commander.Gui;

/// <summary>
///     Display row for an entry in a pane.
/// </summary>
public sealed class EntryItem(IEntry entry, bool isMarked = false, bool isExecutable = false) : INotifyPropertyChanged
{
    private bool _isMarked = isMarked;
    private IImage _icon = FileIcons.GetInitialIcon(entry);

    public event PropertyChangedEventHandler? PropertyChanged;

    public IEntry Entry { get; } = entry;

    public string Name => Entry.Name;

    /// <summary>Name shown in the pane, e.g. "DH0" for an RDB partition (<see cref="IEntry.DisplayName" />).</summary>
    public string DisplayName => Entry.DisplayName;

    public bool IsContainer => Entry.Type == EntryType.Container;

    /// <summary>True when the entry is an executable file, shown in the executable color.</summary>
    public bool IsExecutable { get; } = isExecutable;

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

    /// <summary>Icon shown left of the name, replaced when <see cref="FileIcons" /> has loaded the actual icon.</summary>
    public IImage Icon
    {
        get => _icon;
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

    public string Size => SizeFormatter.FormatEntrySize(Entry);

    public string Modified => Entry.ModifiedDate?.ToString("yyyy-MM-dd HH:mm") ?? string.Empty;
}
