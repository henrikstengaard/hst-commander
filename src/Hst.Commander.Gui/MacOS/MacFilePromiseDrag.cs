using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Hst.Commander.Abstractions;
using Hst.Commander.Core;

namespace Hst.Commander.Gui.MacOS;

/// <summary>
///     Drags entries of an image or another volume that isn't a local directory to Finder and other applications on
///     macOS as file promises (<c>NSFilePromiseProvider</c>): the application dropped on asks for each entry to be
///     written where it was dropped, so nothing is copied first. Avalonia has no file promises, so this starts a
///     dragging session of its own on the window's <c>NSView</c>, with Objective-C classes for the promise delegate and
///     the dragging source registered through the Objective-C runtime.
/// </summary>
[SupportedOSPlatform("macos")]
internal static unsafe class MacFilePromiseDrag
{
    private const string ObjCLibrary = "/usr/lib/libobjc.A.dylib";

    /// <summary>The drag in progress or whose promises may still be written, see <see cref="DragState" />.</summary>
    private static DragState? s_lastDrag;

    /// <summary>Dragged entries by their file promise provider, for the delegate's callbacks.</summary>
    private static readonly ConcurrentDictionary<IntPtr, (DragState Drag, IEntry Entry)> Providers = new();

    private static IntPtr s_delegate;
    private static IntPtr s_draggingSource;
    private static IntPtr s_operationQueue;

    /// <summary>
    ///     Starts dragging entries from the view, on the UI thread while a mouse dragged event is handled. Errors
    ///     writing the entries are reported to <paramref name="failed" /> on any thread, and <paramref name="ended" />
    ///     is called when the dragging session has ended (promises may be written afterwards). Returns false when the
    ///     dragging session couldn't be started.
    /// </summary>
    public static bool Begin(IntPtr view, Task<VolumeDragSource> source, IReadOnlyList<IEntry> entries,
        Action<Exception> failed, Action ended)
    {
        // promises of an earlier drag being written keep their drag source until they are done
        s_lastDrag?.Release();
        var drag = new DragState(source, failed, ended);
        s_lastDrag = drag;
        try
        {
            EnsureClasses();
            var application = Send(Class("NSApplication"), Selector("sharedApplication"));
            var currentEvent = Send(application, Selector("currentEvent"));
            if (view == IntPtr.Zero || currentEvent == IntPtr.Zero)
            {
                drag.Release();
                return false;
            }

            var location = SendPoint(view, Selector("convertPoint:fromView:"),
                SendPoint(currentEvent, Selector("locationInWindow")), IntPtr.Zero);
            var workspace = Send(Class("NSWorkspace"), Selector("sharedWorkspace"));
            var items = Send(Class("NSMutableArray"), Selector("array"));
            var offset = 0.0;
            foreach (var entry in entries)
            {
                var fileType = NSString(entry.Type == EntryType.Container ? "public.folder" : "public.data");
                var provider = Send(Send(Class("NSFilePromiseProvider"), Selector("alloc")),
                    Selector("initWithFileType:delegate:"), fileType, s_delegate);
                Providers[provider] = (drag, entry);
                drag.AddProvider(provider);

                var draggingItem = Send(Send(Class("NSDraggingItem"), Selector("alloc")),
                    Selector("initWithPasteboardWriter:"), provider);
                var icon = Send(workspace, Selector("iconForFileType:"), fileType);
                SendRect(draggingItem, Selector("setDraggingFrame:contents:"),
                    new CGRect(location.X - 16 + offset, location.Y - 16 + offset, 32, 32), icon);
                Send(items, Selector("addObject:"), draggingItem);
                Send(draggingItem, Selector("release"));
                offset += 4;
            }

            var session = Send(view, Selector("beginDraggingSessionWithItems:event:source:"), items, currentEvent,
                s_draggingSource);
            if (session == IntPtr.Zero)
            {
                drag.Release();
                return false;
            }

            return true;
        }
        catch (Exception exception)
        {
            drag.Release();
            failed(exception);
            return false;
        }
    }

    /// <summary>
    ///     A drag's source and promise providers, kept until the dragging session has ended and no promise is being
    ///     written, and the next drag has started (promises are written after the drop, which ends the session).
    /// </summary>
    private sealed class DragState(Task<VolumeDragSource> source, Action<Exception> failed, Action ended)
    {
        private readonly object _lock = new();
        private readonly List<IntPtr> _providers = [];
        private int _writing;
        private bool _isReleased;
        private bool _isDisposed;
        private bool _hasEnded;

        public Task<VolumeDragSource> Source { get; } = source;

        public Action<Exception> Failed { get; } = failed;

        public void AddProvider(IntPtr provider) => _providers.Add(provider);

        public void SessionEnded()
        {
            lock (_lock)
            {
                if (_hasEnded)
                {
                    return;
                }

                _hasEnded = true;
            }

            ended();
        }

        public bool BeginWrite()
        {
            lock (_lock)
            {
                if (_isDisposed)
                {
                    return false;
                }

                _writing++;
                return true;
            }
        }

        public void EndWrite()
        {
            lock (_lock)
            {
                _writing--;
            }

            DisposeWhenDone();
        }

        /// <summary>No more promises are expected, e.g. another drag has started.</summary>
        public void Release()
        {
            lock (_lock)
            {
                _isReleased = true;
            }

            SessionEnded();
            DisposeWhenDone();
        }

        private void DisposeWhenDone()
        {
            lock (_lock)
            {
                if (_isDisposed || !_isReleased || _writing > 0)
                {
                    return;
                }

                _isDisposed = true;
            }

            foreach (var provider in _providers)
            {
                Providers.TryRemove(provider, out _);
                Send(provider, Selector("release"));
            }

            Source.ContinueWith(task =>
            {
                if (task.IsCompletedSuccessfully)
                {
                    task.Result.Dispose();
                }
            }, TaskScheduler.Default);
        }
    }

    /// <summary>
    ///     Registers the Objective-C classes once: "HstFilePromiseDelegate" (<c>NSFilePromiseProviderDelegate</c>)
    ///     and "HstDraggingSource" (<c>NSDraggingSource</c>), each with one instance, and the operation queue promises
    ///     are written on.
    /// </summary>
    private static void EnsureClasses()
    {
        if (s_delegate != IntPtr.Zero)
        {
            return;
        }

        var delegateClass = objc_allocateClassPair(Class("NSObject"), "HstFilePromiseDelegate", IntPtr.Zero);
        class_addProtocol(delegateClass, objc_getProtocol("NSFilePromiseProviderDelegate"));
        class_addMethod(delegateClass, Selector("filePromiseProvider:fileNameForType:"),
            (IntPtr)(delegate* unmanaged<IntPtr, IntPtr, IntPtr, IntPtr, IntPtr>)&FileNameForType, "@@:@@");
        class_addMethod(delegateClass, Selector("filePromiseProvider:writePromiseToURL:completionHandler:"),
            (IntPtr)(delegate* unmanaged<IntPtr, IntPtr, IntPtr, IntPtr, IntPtr, void>)&WritePromise, "v@:@@@?");
        class_addMethod(delegateClass, Selector("operationQueueForFilePromiseProvider:"),
            (IntPtr)(delegate* unmanaged<IntPtr, IntPtr, IntPtr, IntPtr>)&OperationQueue, "@@:@");
        objc_registerClassPair(delegateClass);

        var sourceClass = objc_allocateClassPair(Class("NSObject"), "HstDraggingSource", IntPtr.Zero);
        class_addProtocol(sourceClass, objc_getProtocol("NSDraggingSource"));
        class_addMethod(sourceClass, Selector("draggingSession:sourceOperationMaskForDraggingContext:"),
            (IntPtr)(delegate* unmanaged<IntPtr, IntPtr, IntPtr, nint, nuint>)&SourceOperationMask, "Q@:@q");
        class_addMethod(sourceClass, Selector("draggingSession:endedAtPoint:operation:"),
            (IntPtr)(delegate* unmanaged<IntPtr, IntPtr, IntPtr, CGPoint, nuint, void>)&SessionEnded,
            "v@:@{CGPoint=dd}Q");
        objc_registerClassPair(sourceClass);

        s_operationQueue = Send(Send(Class("NSOperationQueue"), Selector("alloc")), Selector("init"));
        s_draggingSource = Send(Send(sourceClass, Selector("alloc")), Selector("init"));
        s_delegate = Send(Send(delegateClass, Selector("alloc")), Selector("init"));
    }

    /// <summary>Name of the promised file: the entry's local name, e.g. "file_1" for the Amiga name "file*1".</summary>
    [UnmanagedCallersOnly]
    private static IntPtr FileNameForType(IntPtr self, IntPtr command, IntPtr provider, IntPtr fileType)
    {
        try
        {
            return Providers.TryGetValue(provider, out var dragged)
                ? NSString(VolumeDragSource.GetLocalName(dragged.Entry))
                : NSString("Untitled");
        }
        catch
        {
            return NSString("Untitled");
        }
    }

    /// <summary>
    ///     Writes a promised entry where it was dropped, on the operation queue, and calls the completion handler with
    ///     nil or an error.
    /// </summary>
    [UnmanagedCallersOnly]
    private static void WritePromise(IntPtr self, IntPtr command, IntPtr provider, IntPtr url, IntPtr completion)
    {
        string? error = null;
        if (!Providers.TryGetValue(provider, out var dragged) || !dragged.Drag.BeginWrite())
        {
            error = "The dragged entry is no longer available";
        }
        else
        {
            try
            {
                var path = Marshal.PtrToStringUTF8(Send(Send(url, Selector("path")), Selector("UTF8String"))) ??
                           throw new IOException("The drop location has no path");
                Task.Run(() => dragged.Drag.Source.GetAwaiter().GetResult().CopyToLocalAsync(dragged.Entry, path))
                    .GetAwaiter().GetResult();
            }
            catch (Exception exception)
            {
                error = exception.Message;
                dragged.Drag.Failed(exception);
            }
            finally
            {
                dragged.Drag.EndWrite();
            }
        }

        // a block's invoke function is after its isa pointer, flags and reserved fields
        var invoke = (delegate* unmanaged<IntPtr, IntPtr, void>)Marshal.ReadIntPtr(completion, 16);
        invoke(completion, error == null ? IntPtr.Zero : CreateError(error));
    }

    [UnmanagedCallersOnly]
    private static IntPtr OperationQueue(IntPtr self, IntPtr command, IntPtr provider) => s_operationQueue;

    /// <summary>Promises are copied, never moved.</summary>
    [UnmanagedCallersOnly]
    private static nuint SourceOperationMask(IntPtr self, IntPtr command, IntPtr session, nint context) => 1;

    [UnmanagedCallersOnly]
    private static void SessionEnded(IntPtr self, IntPtr command, IntPtr session, CGPoint point, nuint operation)
    {
        try
        {
            s_lastDrag?.SessionEnded();
        }
        catch
        {
            // nothing to report, the drag has ended
        }
    }

    private static IntPtr CreateError(string message)
    {
        var userInfo = Send(Class("NSDictionary"), Selector("dictionaryWithObject:forKey:"), NSString(message),
            NSString("NSLocalizedDescription"));
        return SendError(Class("NSError"), Selector("errorWithDomain:code:userInfo:"), NSString("HstCommander"), 1,
            userInfo);
    }

    private static IntPtr NSString(string value)
    {
        var utf8 = Marshal.StringToCoTaskMemUTF8(value);
        try
        {
            return Send(Class("NSString"), Selector("stringWithUTF8String:"), utf8);
        }
        finally
        {
            Marshal.FreeCoTaskMem(utf8);
        }
    }

    private static IntPtr Class(string name) => objc_getClass(name);

    private static IntPtr Selector(string name) => sel_registerName(name);

    [StructLayout(LayoutKind.Sequential)]
    private readonly record struct CGPoint(double X, double Y);

    [StructLayout(LayoutKind.Sequential)]
    private readonly record struct CGRect(double X, double Y, double Width, double Height);

    [DllImport(ObjCLibrary)]
    private static extern IntPtr objc_getClass(string name);

    [DllImport(ObjCLibrary)]
    private static extern IntPtr objc_getProtocol(string name);

    [DllImport(ObjCLibrary)]
    private static extern IntPtr sel_registerName(string name);

    [DllImport(ObjCLibrary)]
    private static extern IntPtr objc_allocateClassPair(IntPtr superclass, string name, IntPtr extraBytes);

    [DllImport(ObjCLibrary)]
    private static extern void objc_registerClassPair(IntPtr cls);

    [DllImport(ObjCLibrary)]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool class_addMethod(IntPtr cls, IntPtr selector, IntPtr implementation, string types);

    [DllImport(ObjCLibrary)]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool class_addProtocol(IntPtr cls, IntPtr protocol);

    [DllImport(ObjCLibrary, EntryPoint = "objc_msgSend")]
    private static extern IntPtr Send(IntPtr receiver, IntPtr selector);

    [DllImport(ObjCLibrary, EntryPoint = "objc_msgSend")]
    private static extern IntPtr Send(IntPtr receiver, IntPtr selector, IntPtr argument);

    [DllImport(ObjCLibrary, EntryPoint = "objc_msgSend")]
    private static extern IntPtr Send(IntPtr receiver, IntPtr selector, IntPtr argument1, IntPtr argument2);

    [DllImport(ObjCLibrary, EntryPoint = "objc_msgSend")]
    private static extern IntPtr Send(IntPtr receiver, IntPtr selector, IntPtr argument1, IntPtr argument2,
        IntPtr argument3);

    [DllImport(ObjCLibrary, EntryPoint = "objc_msgSend")]
    private static extern IntPtr SendError(IntPtr receiver, IntPtr selector, IntPtr domain, nint code,
        IntPtr userInfo);

    [DllImport(ObjCLibrary, EntryPoint = "objc_msgSend")]
    private static extern CGPoint SendPoint(IntPtr receiver, IntPtr selector);

    [DllImport(ObjCLibrary, EntryPoint = "objc_msgSend")]
    private static extern CGPoint SendPoint(IntPtr receiver, IntPtr selector, CGPoint point, IntPtr view);

    [DllImport(ObjCLibrary, EntryPoint = "objc_msgSend")]
    private static extern void SendRect(IntPtr receiver, IntPtr selector, CGRect rect, IntPtr contents);
}
