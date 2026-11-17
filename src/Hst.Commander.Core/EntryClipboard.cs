using System.Runtime.Versioning;
using Hst.Commander.Abstractions;
using Hst.Commander.Core.Volumes;

namespace Hst.Commander.Core;

/// <summary>
///     Entries copied or cut to the clipboard (Ctrl+C, Ctrl+X) and pasted (Ctrl+V) in a pane, shared by both panes
///     (see <see cref="Current" />). The entries' location is opened again as a context of its own
///     (<see cref="NavigationContext.CloneAsync" />), so they can be pasted after the pane has changed location, also
///     from inside images. The front-ends also put local entries on the clipboard of the operating system as files,
///     so other applications (e.g. Explorer) can paste them, and paste files from other applications with
///     <see cref="GetPasteSourceAsync" />. Disposing closes the location.
/// </summary>
public sealed class EntryClipboard : IDisposable
{
    private EntryClipboard(NavigationContext context, IReadOnlyList<IEntry> entries, bool isCut)
    {
        Context = context;
        Entries = entries;
        IsCut = isCut;
        LocalPaths = context.GetLocalPaths(entries);
    }

    /// <summary>The entries on the clipboard, null when nothing has been copied or cut, or after a cut was pasted.</summary>
    public static EntryClipboard? Current { get; private set; }

    /// <summary>Context at the location of the entries, of its own.</summary>
    public NavigationContext Context { get; }

    /// <summary>The copied or cut entries.</summary>
    public IReadOnlyList<IEntry> Entries { get; }

    /// <summary>True when the entries were cut, so pasting moves them.</summary>
    public bool IsCut { get; }

    /// <summary>Full local paths of the entries, put on the clipboard of the operating system; null when not local.</summary>
    public IReadOnlyList<string>? LocalPaths { get; }

    /// <summary>
    ///     What the front-end put on the clipboard of the operating system for these entries, e.g. the Windows
    ///     clipboard sequence number after setting it, to tell whether it still holds them.
    /// </summary>
    public object? HostData { get; set; }

    /// <summary>
    ///     Puts entries of a context's current location on the clipboard (copied, or cut when
    ///     <paramref name="isCut" /> is true), replacing what was on it. ".." is left out.
    /// </summary>
    public static async Task<EntryClipboard> SetAsync(NavigationContext context, IReadOnlyCollection<IEntry> entries,
        bool isCut, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(entries);

        var clipboardEntries = entries
            .Where(entry => !entry.Name.Equals(Paths.Parent, StringComparison.Ordinal))
            .ToList();
        if (clipboardEntries.Count == 0)
        {
            throw new ArgumentException("No entries to put on the clipboard", nameof(entries));
        }

        if (isCut && context.MoveNotSupportedMessage is { } message)
        {
            throw new NotSupportedException(message);
        }

        var clone = await context.CloneAsync(cancellationToken);
        var clipboard = new EntryClipboard(clone, clipboardEntries, isCut);
        Clear();
        Current = clipboard;
        return clipboard;
    }

    /// <summary>Removes the entries from the clipboard, closing their location.</summary>
    public static void Clear()
    {
        Current?.Dispose();
        Current = null;
    }

    /// <summary>
    ///     What Ctrl+V pastes in a pane: the entries of <see cref="Current" /> when the clipboard of the operating system
    ///     still holds them (<paramref name="isCurrentHeld" />), otherwise local files put on it by another application
    ///     (<paramref name="hostFiles" />, moved when <paramref name="isHostMove" /> is true, e.g. cut in Explorer),
    ///     opened like dropped files (<see cref="DroppedEntries" />) with the settings of <paramref name="settings" />.
    ///     Null when there is nothing to paste.
    /// </summary>
    public static async Task<PasteSource?> GetPasteSourceAsync(bool isCurrentHeld, IReadOnlyList<string>? hostFiles,
        bool isHostMove, NavigationContext? settings = null, CancellationToken cancellationToken = default)
    {
        if (isCurrentHeld && Current is { } clipboard)
        {
            // pasting cut entries moves them away, which isn't another application moving them (CheckCutMoved)
            Interlocked.Increment(ref clipboard._pasteCount);
            return new PasteSource(clipboard.Entries, clipboard.IsCut, false,
                (destination, progress, confirmOverwrite, token) => clipboard.IsCut
                    ? clipboard.Context.MoveAsync(clipboard.Entries, destination, destination.CurrentPath,
                        EntryFilter.All, progress, confirmOverwrite, token)
                    : clipboard.Context.CopyAsync(clipboard.Entries, destination, destination.CurrentPath,
                        EntryFilter.All, progress, confirmOverwrite, token),
                new PasteEnd(() => Interlocked.Decrement(ref clipboard._pasteCount)));
        }

        if (hostFiles is not { Count: > 0 })
        {
            return null;
        }

        var dropped = await DroppedEntries.OpenAsync(hostFiles, settings, cancellationToken);
        return new PasteSource(dropped.Entries, isHostMove, true,
            (destination, progress, confirmOverwrite, token) => isHostMove
                ? dropped.MoveAsync(destination, destination.CurrentPath, EntryFilter.All, progress,
                    confirmOverwrite, token)
                : dropped.CopyAsync(destination, destination.CurrentPath, EntryFilter.All, progress,
                    confirmOverwrite, token), dropped);
    }

    /// <summary>
    ///     Puts the entries on the Windows clipboard, owned by a window (zero for none), and remembers what was put on it
    ///     in <see cref="HostData" /> for <see cref="GetWindowsPasteSourceAsync" />:
    ///     <list type="bullet">
    ///         <item>
    ///             local entries as files (<see cref="WindowsFileClipboard" />), so Explorer pastes them and moves cut
    ///             files itself (see <see cref="CheckCutMoved" />);
    ///         </item>
    ///         <item>
    ///             entries of images and other volumes as virtual files (<see cref="WindowsVirtualFileDrag.SetClipboard" />),
    ///             read when they are pasted; cut entries are deleted when the application pasting them says it has
    ///             moved them, and then <paramref name="moved" /> is called on any thread.
    ///         </item>
    ///     </list>
    ///     Errors reading or deleting entries pasted by another application are reported to
    ///     <paramref name="failed" /> on any thread.
    /// </summary>
    [SupportedOSPlatform("windows")]
    public void SetWindowsClipboard(IntPtr owner, Action<Exception> failed, Action<EntryClipboard> moved)
    {
        ArgumentNullException.ThrowIfNull(failed);
        ArgumentNullException.ThrowIfNull(moved);

        if (LocalPaths is { } paths)
        {
            HostData = WindowsFileClipboard.SetFiles(owner, paths, IsCut);
            return;
        }

        HostData = WindowsVirtualFileDrag.SetClipboard(VolumeDragSource.OpenAsync(Context, Entries), IsCut, failed,
            () => Task.Run(async () =>
            {
                try
                {
                    await Context.DeleteAsync(Entries);
                    moved(this);
                }
                catch (Exception exception)
                {
                    failed(exception);
                }
            }));
    }

    /// <summary>
    ///     Checks whether cut local entries of <see cref="Current" /> have been moved away since the last check, e.g.
    ///     pasted in Explorer, which moves them itself, so the front-ends poll this and list the panes again when it
    ///     returns true. The clipboard is emptied when they are all gone.
    /// </summary>
    public static bool CheckCutMoved()
    {
        if (Current is not { IsCut: true, LocalPaths: { } paths } clipboard ||
            Volatile.Read(ref clipboard._pasteCount) > 0)
        {
            return false;
        }

        var missing = paths.Count(path => !File.Exists(path) && !Directory.Exists(path));
        if (missing == clipboard._missingCount)
        {
            return false;
        }

        clipboard._missingCount = missing;
        if (missing == paths.Count)
        {
            Clear();
        }

        return true;
    }

    /// <summary>Number of local paths found missing by the last <see cref="CheckCutMoved" />.</summary>
    private int _missingCount;

    /// <summary>Number of pastes of the entries in progress, see <see cref="GetPasteSourceAsync" />.</summary>
    private int _pasteCount;

    /// <summary>Ends a paste of the entries when the paste source is disposed.</summary>
    private sealed class PasteEnd(Action end) : IDisposable
    {
        private int _isDisposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _isDisposed, 1) == 0)
            {
                end();
            }
        }
    }

    /// <summary>
    ///     What pasting pastes on Windows, see <see cref="GetPasteSourceAsync" />: <see cref="Current" /> while the
    ///     Windows clipboard still holds what <see cref="SetWindowsClipboard" /> put on it (<see cref="HostData" />),
    ///     otherwise files copied or cut in another application, e.g. Explorer.
    /// </summary>
    [SupportedOSPlatform("windows")]
    public static Task<PasteSource?> GetWindowsPasteSourceAsync(NavigationContext? settings = null,
        CancellationToken cancellationToken = default)
    {
        var isHeld = Current?.HostData switch
        {
            uint sequenceNumber => sequenceNumber == WindowsFileClipboard.SequenceNumber,
            { } virtualFiles => WindowsVirtualFileDrag.IsClipboardCurrent(virtualFiles),
            null => false
        };
        IReadOnlyList<string>? files = null;
        var isMove = false;
        if (!isHeld && WindowsFileClipboard.TryGetFiles(out var paths, out isMove))
        {
            files = paths;
        }

        return GetPasteSourceAsync(isHeld, files, isMove, settings, cancellationToken);
    }

    public void Dispose() => Context.Dispose();
}

/// <summary>
///     Entries pasted in a pane (see <see cref="EntryClipboard.GetPasteSourceAsync" />): from
///     <see cref="EntryClipboard.Current" />, or files put on the clipboard of the operating system by another
///     application (<see cref="IsFromHost" />). Disposing closes their locations.
/// </summary>
public sealed class PasteSource(
    IReadOnlyList<IEntry> entries,
    bool isMove,
    bool isFromHost,
    Func<NavigationContext, IProgress<CopyProgress>?, Func<OverwriteRequest, CancellationToken, Task<OverwriteAnswer>>?,
        CancellationToken, Task> paste,
    IDisposable? resources) : IDisposable
{
    /// <summary>The pasted entries.</summary>
    public IReadOnlyList<IEntry> Entries { get; } = entries;

    /// <summary>True when pasting moves the entries (cut), false when it copies them.</summary>
    public bool IsMove { get; } = isMove;

    /// <summary>True for files of another application, false for entries of <see cref="EntryClipboard.Current" />.</summary>
    public bool IsFromHost { get; } = isFromHost;

    /// <summary>Title of the progress dialog, "Move" or "Copy".</summary>
    public string Title => IsMove ? "Move" : "Copy";

    /// <summary>
    ///     Copies or moves the entries to the current location of a context (the pane pasted in), see
    ///     <see cref="NavigationContext.CopyAsync" /> and <see cref="NavigationContext.MoveAsync" />.
    /// </summary>
    public Task PasteAsync(NavigationContext destination, IProgress<CopyProgress>? progress = null,
        Func<OverwriteRequest, CancellationToken, Task<OverwriteAnswer>>? confirmOverwrite = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(destination);
        return paste(destination, progress, confirmOverwrite, cancellationToken);
    }

    public void Dispose() => resources?.Dispose();
}
