using Avalonia.Controls;
using Avalonia.Input;
using Hst.Commander.Abstractions;

namespace Hst.Commander.Gui;

/// <summary>
///     Context menu of the entries (see docs/gui.md): right-clicking an entry makes it the current entry, and the menu
///     acts on the marked entries or, when none are marked, the current entry, like the function keys. Copy, Cut,
///     Rename, Delete and Properties need an entry (not ".."); Paste is always enabled. The commands are run by the window
///     (<see cref="ActionRequested" />).
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
    }

    /// <summary>Opens the context menu with the keyboard (the menu key or Shift+F10) at the current entry.</summary>
    public void OpenContextMenu()
    {
        Control target = NavigationMode.PluginMode != null
            ? IconLayout
            : EntriesList.SelectedItem is { } item && EntriesList.ContainerFromItem(item) is { } container
                ? container
                : EntriesList;
        _entryMenu?.Open(target);
    }
}
