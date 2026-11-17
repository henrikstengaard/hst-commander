using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Platform.Storage;
using Hst.Commander.Abstractions;
using Hst.Commander.Core;

namespace Hst.Commander.Gui;

/// <summary>
///     Copies and cuts entries of a pane to the clipboard (Ctrl+C, Ctrl+X) and finds what to paste (Ctrl+V), see
///     <see cref="EntryClipboard" /> and docs/file-operations.md. Besides <see cref="EntryClipboard.Current" />, the
///     clipboard of the operating system gets the entries, so other applications can paste them:
///     <list type="bullet">
///         <item>
///             local entries as files, on Windows with the drop effect, so Explorer moves cut files itself
///             (<see cref="WindowsFileClipboard" />);
///         </item>
///         <item>
///             entries of images and other volumes on Windows as virtual files read when they are pasted, cut ones
///             deleted when they have been (<see cref="EntryClipboard.SetWindowsClipboard" />);
///         </item>
///         <item>otherwise nothing, they are only pasted in the panes.</item>
///     </list>
///     Pasting pastes <see cref="EntryClipboard.Current" /> while the clipboard of the operating system still holds
///     what was put on it, otherwise files copied or cut in another application.
/// </summary>
internal static class GuiClipboard
{
    private static readonly DataFormat<string> MarkerFormat =
        DataFormat.CreateInProcessFormat<string>("hst-commander-clipboard");

    /// <summary>
    ///     Puts entries of a pane's current location on the clipboard. Errors reading entries pasted by another
    ///     application later are reported to <paramref name="failed" /> on any thread, and
    ///     <paramref name="moved" /> is called on any thread when cut entries of an image pasted by another application
    ///     have been deleted (Windows).
    /// </summary>
    public static async Task SetAsync(TopLevel topLevel, NavigationContext context, IReadOnlyList<IEntry> entries,
        bool isCut, Action<Exception> failed, Action<EntryClipboard> moved)
    {
        var clipboard = await EntryClipboard.SetAsync(context, entries, isCut);
        if (OperatingSystem.IsWindows())
        {
            clipboard.SetWindowsClipboard(topLevel.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero, failed, moved);
            return;
        }

        if (topLevel.Clipboard is not { } hostClipboard)
        {
            return;
        }

        var dataTransfer = new DataTransfer();
        var item = new DataTransferItem();
        item.Set(MarkerFormat, Guid.NewGuid().ToString("N"));
        dataTransfer.Add(item);
        foreach (var path in clipboard.LocalPaths ?? [])
        {
            IStorageItem? storageItem = Directory.Exists(path)
                ? await topLevel.StorageProvider.TryGetFolderFromPathAsync(path)
                : await topLevel.StorageProvider.TryGetFileFromPathAsync(path);
            if (storageItem != null)
            {
                var fileItem = new DataTransferItem();
                fileItem.SetFile(storageItem);
                dataTransfer.Add(fileItem);
            }
        }

        await hostClipboard.SetDataAsync(dataTransfer);
        clipboard.HostData = dataTransfer;
    }

    /// <summary>
    ///     What pasting in a pane pastes, see <see cref="EntryClipboard.GetPasteSourceAsync" />; null when there is
    ///     nothing to paste. Local files of another application are opened with the settings of the pane pasted in.
    /// </summary>
    public static async Task<PasteSource?> GetPasteSourceAsync(TopLevel topLevel, NavigationContext context)
    {
        if (OperatingSystem.IsWindows())
        {
            return await EntryClipboard.GetWindowsPasteSourceAsync(context);
        }

        if (topLevel.Clipboard is not { } hostClipboard)
        {
            return await EntryClipboard.GetPasteSourceAsync(true, null, false, context);
        }

        var isCurrent = EntryClipboard.Current is { HostData: { } hostData } &&
                        ReferenceEquals(await hostClipboard.TryGetInProcessDataAsync(), hostData);
        IReadOnlyList<string>? localPaths = null;
        if (!isCurrent && await hostClipboard.TryGetFilesAsync() is { } storageItems)
        {
            localPaths = storageItems.Select(storageItem => storageItem.TryGetLocalPath()).OfType<string>().ToList();
        }

        return await EntryClipboard.GetPasteSourceAsync(isCurrent, localPaths, false, context);
    }

    /// <summary>
    ///     Empties the clipboard after pasting cut entries, which were moved: <see cref="EntryClipboard.Current" />,
    ///     and the clipboard of the operating system when it holds them or files cut in another application.
    /// </summary>
    public static async Task ClearAfterMoveAsync(TopLevel topLevel, PasteSource pasted)
    {
        if (!pasted.IsFromHost)
        {
            EntryClipboard.Clear();
        }

        if (OperatingSystem.IsWindows())
        {
            WindowsFileClipboard.Clear();
        }
        else if (topLevel.Clipboard is { } hostClipboard)
        {
            await hostClipboard.ClearAsync();
        }
    }
}
