using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Hst.Commander.Abstractions;
using Hst.Commander.Core;
using Hst.Commander.Gui.MacOS;
using Hst.Commander.Gui.Windows;

namespace Hst.Commander.Gui;

/// <summary>
///     Entries dropped on a pane: entries dragged from a pane (<paramref name="SourcePane" /> and
///     <paramref name="Entries" />) or local files and directories dragged from another application
///     (<paramref name="LocalPaths" />), copied or moved to <paramref name="Destination" />: the current path of the
///     pane or the directory they were dropped on.
/// </summary>
public sealed record PaneDropRequest(
    PaneView? SourcePane,
    IReadOnlyList<IEntry>? Entries,
    IReadOnlyList<string>? LocalPaths,
    string Destination,
    bool IsMove);

/// <summary>
///     Drag and drop of the list and thumbnail navigation modes (see docs/gui.md): local entries are dragged out as
///     their files, entries in images and other volumes as Windows virtual files or macOS file promises read when
///     they are dropped, and entries dragged from either pane or another application are dropped in the current
///     location or on a directory.
/// </summary>
public partial class PaneView
{
    /// <summary>Distance the pointer moves with the button pressed before dragging starts, like icons.</summary>
    private const double DragDistance = 4;

    /// <summary>Entries dragged from a pane, only seen by Hst Commander itself.</summary>
    private sealed record PaneDrag(PaneView Pane, IReadOnlyList<IEntry> Entries);

    private static readonly DataFormat<PaneDrag> PaneDragFormat =
        DataFormat.CreateInProcessFormat<PaneDrag>("hst-commander-pane-drag");

    /// <summary>
    ///     Entries of a pane dragged by a drag of the operating system (virtual files, file promises), which doesn't
    ///     carry Avalonia's in-process data, so drops on the panes find them here while the drag runs.
    /// </summary>
    private static PaneDrag? s_nativePaneDrag;

    private PointerPressedEventArgs? _dragPressed;
    private EntryItem? _dragItem;
    private Point _dragStart;
    private ListBoxItem? _dropTargetContainer;

    /// <summary>Raised when entries are dropped on the pane, so the window can copy or move them.</summary>
    public event EventHandler<PaneDropRequest>? EntriesDropped;

    private static EntryItem? FindItem(object? source) =>
        (source as Visual)?.FindAncestorOfType<ListBoxItem>(true)?.DataContext as EntryItem;

    private void OnEntriesPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        _dragPressed = null;
        if (!e.GetCurrentPoint(EntriesList).Properties.IsLeftButtonPressed ||
            FindItem(e.Source) is not { } item || item.Name == Paths.Parent)
        {
            return;
        }

        _dragPressed = e;
        _dragItem = item;
        _dragStart = e.GetPosition(EntriesList);
    }

    private async void OnEntriesPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_dragPressed is not { } pressed || _dragItem is not { } item)
        {
            return;
        }

        var delta = e.GetPosition(EntriesList) - _dragStart;
        if (Math.Abs(delta.X) < DragDistance && Math.Abs(delta.Y) < DragDistance)
        {
            return;
        }

        _dragPressed = null;
        _dragItem = null;
        await DragEntriesAsync(pressed, item);
    }

    private void OnEntriesPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        _dragPressed = null;
        _dragItem = null;
    }

    /// <summary>
    ///     Drags the marked entries when the pressed entry is marked, otherwise the pressed entry. Local entries are
    ///     dragged as their files. Entries in images and other volumes are dragged as virtual files on Windows and file
    ///     promises on macOS, read through a volume of their own (<see cref="VolumeDragSource" />) when they are
    ///     dropped, and only to the panes on other systems.
    /// </summary>
    private async Task DragEntriesAsync(PointerPressedEventArgs pressed, EntryItem item)
    {
        var entries = item.IsMarked ? NavigationContext.GetOperationEntries(item.Entry) : [item.Entry];
        if (entries.Count == 0)
        {
            return;
        }

        var paneDrag = new PaneDrag(this, entries);
        if (NavigationContext.GetLocalPaths(entries) is { } paths)
        {
            var effect = await DragDrop.DoDragDropAsync(pressed, await CreateDataTransferAsync(paneDrag, paths),
                DragDropEffects.Copy | DragDropEffects.Move);

            // e.g. moved by Explorer, which the other pane may show
            if (effect == DragDropEffects.Move)
            {
                await RefreshEntriesAsync();
                EntriesChanged?.Invoke(this, EventArgs.Empty);
            }

            return;
        }

        var platformHandle = TopLevel.GetTopLevel(this)?.TryGetPlatformHandle();
        if (OperatingSystem.IsWindows() && platformHandle is { HandleDescriptor: "HWND" })
        {
            // the drag starts at once, the location is opened again meanwhile
            var source = VolumeDragSource.OpenAsync(NavigationContext, entries);
            pressed.Pointer.Capture(null);
            s_nativePaneDrag = paneDrag;
            try
            {
                WindowsVirtualFileDrag.DoDragDrop(platformHandle.Handle, source, ReportDragFailure);
            }
            finally
            {
                s_nativePaneDrag = null;
            }

            return;
        }

        if (OperatingSystem.IsMacOS() && platformHandle is IMacOSTopLevelPlatformHandle macHandle)
        {
            var source = VolumeDragSource.OpenAsync(NavigationContext, entries);
            pressed.Pointer.Capture(null);
            s_nativePaneDrag = paneDrag;
            if (!MacFilePromiseDrag.Begin(macHandle.NSView, source, entries, ReportDragFailure,
                    () => Dispatcher.UIThread.Post(() =>
                    {
                        // a later drag may have started meanwhile
                        if (s_nativePaneDrag == paneDrag)
                        {
                            s_nativePaneDrag = null;
                        }
                    })))
            {
                s_nativePaneDrag = null;
            }

            return;
        }

        // other systems can't read files of a volume when dropped, so they are only dragged to the panes
        await DragDrop.DoDragDropAsync(pressed, await CreateDataTransferAsync(paneDrag, []),
            DragDropEffects.Copy | DragDropEffects.Move);
    }

    /// <summary>Shows an error reading dragged entries, e.g. when the drop target reads them, on any thread.</summary>
    private void ReportDragFailure(Exception exception) =>
        Dispatcher.UIThread.Post(() => NavigationFailed?.Invoke(this, exception));

    /// <summary>Data of dragged entries: the pane's entries for the panes, and the files for other applications.</summary>
    private async Task<DataTransfer> CreateDataTransferAsync(PaneDrag paneDrag, IReadOnlyList<string> paths)
    {
        var dataTransfer = new DataTransfer();
        var storageProvider = TopLevel.GetTopLevel(this)?.StorageProvider;
        var paneItem = new DataTransferItem();
        paneItem.Set(PaneDragFormat, paneDrag);
        var items = new List<DataTransferItem> { paneItem };
        foreach (var path in paths)
        {
            IStorageItem? storageItem = storageProvider == null
                ? null
                : Directory.Exists(path)
                    ? await storageProvider.TryGetFolderFromPathAsync(path)
                    : await storageProvider.TryGetFileFromPathAsync(path);
            if (storageItem == null)
            {
                continue;
            }

            // the pane's entries go with the first file, so they are dropped together
            var dataTransferItem = items.Count == 1 && !paneItem.Formats.Contains(DataFormat.File)
                ? paneItem
                : new DataTransferItem();
            dataTransferItem.SetFile(storageItem);
            if (dataTransferItem != paneItem)
            {
                items.Add(dataTransferItem);
            }
        }

        foreach (var dataTransferItem in items)
        {
            dataTransfer.Add(dataTransferItem);
        }

        return dataTransfer;
    }

    /// <summary>Entries of a pane dragged to this pane, null for files from other applications.</summary>
    private static PaneDrag? GetPaneDrag(DragEventArgs e) =>
        e.DataTransfer.TryGetValue(PaneDragFormat) ?? s_nativePaneDrag;

    /// <summary>
    ///     Where dragged entries are dropped: a directory row under the pointer in the list and thumbnails, otherwise
    ///     the current location. Null when they can't be dropped, e.g. in the drive list away from a drive, or
    ///     entries of this pane in their own location or on themselves.
    /// </summary>
    private (string Destination, ListBoxItem? Container)? GetDropTarget(DragEventArgs e)
    {
        var paneDrag = GetPaneDrag(e);
        if (paneDrag == null && !e.DataTransfer.Contains(DataFormat.File))
        {
            return null;
        }

        var container = NavigationMode.PluginMode == null
            ? (e.Source as Visual)?.FindAncestorOfType<ListBoxItem>(true)
            : null;
        // entries of this pane are only dropped on another directory, not on themselves
        if (container?.DataContext is EntryItem { IsContainer: true } item && item.Name != Paths.Parent &&
            !(paneDrag?.Pane == this && paneDrag.Entries.Any(entry => entry.Name == item.Name)))
        {
            return (GetDestination(item), container);
        }

        return paneDrag?.Pane == this || NavigationContext.IsDriveList
            ? null
            : (NavigationContext.CurrentPath, null);
    }

    /// <summary>Destination of entries dropped on a directory: its full path, or the path below the current path.</summary>
    private string GetDestination(EntryItem item) =>
        NavigationContext.GetLocalPath(item.Entry) ??
        NavigationContext.CurrentPath + Path.DirectorySeparatorChar + item.Name;

    private static DragDropEffects GetDropEffect(DragEventArgs e) =>
        e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? DragDropEffects.Move : DragDropEffects.Copy;

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        var target = GetDropTarget(e);
        e.DragEffects = target == null ? DragDropEffects.None : GetDropEffect(e) & e.DragEffects;
        ShowDropTarget(target?.Container, target != null && target.Value.Container == null);
        e.Handled = true;
    }

    private void OnDrop(object? sender, DragEventArgs e)
    {
        ShowDropTarget(null, false);
        e.Handled = true;
        if (GetDropTarget(e) is not { } target)
        {
            e.DragEffects = DragDropEffects.None;
            return;
        }

        var isMove = GetDropEffect(e) == DragDropEffects.Move;
        PaneDropRequest? request = null;
        if (GetPaneDrag(e) is { } paneDrag)
        {
            request = new PaneDropRequest(paneDrag.Pane, paneDrag.Entries, null, target.Destination, isMove);
        }
        else if (e.DataTransfer.TryGetFiles() is { } files &&
                 files.Select(file => file.TryGetLocalPath()).OfType<string>().ToList() is { Count: > 0 } paths)
        {
            request = new PaneDropRequest(null, null, paths, target.Destination, isMove);
        }

        // the entries are copied or moved after the drop by Hst Commander itself, so the application dragged from
        // must not delete them as moved (Explorer deletes the files of a move dropped with the move effect)
        e.DragEffects = request == null ? DragDropEffects.None : DragDropEffects.Copy;
        if (request != null)
        {
            // after the drag has ended, so the dialogs aren't shown inside the drag of the operating system
            Dispatcher.UIThread.Post(() => EntriesDropped?.Invoke(this, request));
        }
    }

    /// <summary>Highlights the directory row or the pane entries are dropped in while dragging over the pane.</summary>
    private void ShowDropTarget(ListBoxItem? container, bool isPane)
    {
        if (_dropTargetContainer != container)
        {
            _dropTargetContainer?.Classes.Remove("drop-target");
            container?.Classes.Add("drop-target");
            _dropTargetContainer = container;
        }

        EntriesBorder.Classes.Set("drop-target", isPane);
    }
}
