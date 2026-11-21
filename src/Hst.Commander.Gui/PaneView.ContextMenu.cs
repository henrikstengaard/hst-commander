using Avalonia.Controls;
using Avalonia.Input;
using Hst.Commander.Abstractions;
using Hst.Commander.Core;

namespace Hst.Commander.Gui;

/// <summary>
///     Context menu of the entries (see docs/gui.md): right-clicking an entry makes it the current entry, and the menu
///     acts on the marked entries or, when none are marked, the current entry, like the function keys. Copy, Cut,
///     Rename, Delete and Properties need an entry (not ".."); Paste is always enabled. The commands are run by the window
///     (<see cref="ActionRequested" />). PowerShell here, Terminal here and Command prompt here (only Terminal here on
///     macOS and Linux) open a shell in the pane's directory when it's local (<see cref="ShellHereRequested" />).
/// </summary>
public partial class PaneView
{
    private ContextMenu? _entryMenu;
    private readonly Dictionary<string, MenuItem> _entryMenuItems = [];

    /// <summary>
    ///     Raised when a command of the context menu is chosen (a <see cref="CommanderActions" /> constant), so the
    ///     window runs it on this pane.
    /// </summary>
    public event EventHandler<string>? ActionRequested;

    private readonly List<MenuItem> _shellHereMenuItems = [];

    /// <summary>
    ///     Raised when PowerShell here, Terminal here or Command prompt here is chosen, so the window opens it in this
    ///     pane's directory (<see cref="NavigationContext.CurrentHostDirectory" />).
    /// </summary>
    public event EventHandler<ShellHereKind>? ShellHereRequested;

    /// <summary>Creates the context menu of the list, thumbnails and icons.</summary>
    private void InitializeContextMenu()
    {
        _entryMenu = new ContextMenu();
        AddMenuItem("_Copy", CommanderActions.ClipboardCopy, new KeyGesture(Key.C, KeyModifiers.Control), true);
        AddMenuItem("Cu_t", CommanderActions.ClipboardCut, new KeyGesture(Key.X, KeyModifiers.Control), true);
        AddMenuItem("_Paste", CommanderActions.ClipboardPaste, new KeyGesture(Key.V, KeyModifiers.Control), false);
        _entryMenu.Items.Add(new Separator());
        AddMenuItem("_Rename", CommanderActions.Rename, new KeyGesture(Key.F6, KeyModifiers.Shift), true);
        AddMenuItem("_Delete", CommanderActions.Delete, new KeyGesture(Key.Delete), true);
        _entryMenu.Items.Add(new Separator());
        foreach (var kind in ShellHere.Available)
        {
            var menuItem = new MenuItem { Header = ShellHere.GetHeader(kind) };
            menuItem.Click += (_, _) => ShellHereRequested?.Invoke(this, kind);
            _entryMenu.Items.Add(menuItem);
            _shellHereMenuItems.Add(menuItem);
        }

        _entryMenu.Items.Add(new Separator());
        AddMenuItem("Pr_operties", CommanderActions.Properties, new KeyGesture(Key.Enter, KeyModifiers.Alt), true);
        _entryMenu.Opening += (_, _) => UpdateContextMenu();

        EntriesList.ContextMenu = _entryMenu;
        IconLayout.ContextMenu = _entryMenu;
        IconLayout.ItemRightClicked += (_, item) => SelectForContextMenu(item);
    }

    private void AddMenuItem(string header, string action, KeyGesture gesture, bool needsEntry)
    {
        var menuItem = new MenuItem { Header = header, InputGesture = gesture };
        menuItem.Click += (_, _) => ActionRequested?.Invoke(this, action);
        _entryMenu!.Items.Add(menuItem);
        if (needsEntry)
        {
            _entryMenuItems[action] = menuItem;
        }
    }

    /// <summary>Right-click in the list: makes the row the current entry, see <see cref="OnEntriesPointerPressed" />.</summary>
    private void OnEntriesRightPressed(PointerPressedEventArgs e)
    {
        if (FindItem(e.Source) is { } item)
        {
            SelectForContextMenu(item);
        }
        else
        {
            // the space below the entries: only Paste
            EntriesList.SelectedItem = null;
        }
    }

    private void SelectForContextMenu(EntryItem item) => EntriesList.SelectedItem = item;

    /// <summary>
    ///     Enables the commands acting on entries when there are some: the marked entries or the current entry. Rename
    ///     renames the current entry; drives only have properties.
    /// </summary>
    private void UpdateContextMenu()
    {
        var hasEntries = NavigationContext.GetOperationEntries(SelectedEntry).Count > 0;
        var canChange = hasEntries && !NavigationContext.IsDriveList;
        foreach (var (action, menuItem) in _entryMenuItems)
        {
            menuItem.IsEnabled = action switch
            {
                CommanderActions.Properties => hasEntries,
                CommanderActions.Rename => canChange && SelectedEntry is { } entry && entry.Name != Paths.Parent,
                _ => canChange
            };
        }

        // shells only open in local directories, not inside images or on servers
        var isLocal = NavigationContext.CurrentHostDirectory != null;
        foreach (var menuItem in _shellHereMenuItems)
        {
            menuItem.IsEnabled = isLocal;
        }
    }

}
