using System.ComponentModel;
using Hst.Commander.Core;
using Hst.Commander.Extensions.Volume.Abstractions;

namespace Hst.Commander.Gui;

/// <summary>
///     Display row for an entry in a pane.
/// </summary>
public sealed class EntryItem(IEntry entry, bool isMarked = false) : INotifyPropertyChanged
{
    private bool _isMarked = isMarked;

    public event PropertyChangedEventHandler? PropertyChanged;

    public IEntry Entry { get; } = entry;

    public string Name => Entry.Name;

    public bool IsContainer => Entry.Type == EntryType.Container;

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

    public string Size => SizeFormatter.FormatEntrySize(Entry);

    public string Modified => Entry.ModifiedDate?.ToString("yyyy-MM-dd HH:mm") ?? string.Empty;
}
