using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;

namespace Hst.Commander.Core;

/// <summary>
///     Local files on the Windows clipboard, as Explorer copies and cuts them: "CF_HDROP" with the paths and
///     "Preferred DropEffect" saying whether they were copied or cut (moved when pasted). Works in console
///     applications too, so the TUI can exchange files with Explorer. See <see cref="EntryClipboard" />.
/// </summary>
[SupportedOSPlatform("windows")]
public static class WindowsFileClipboard
{
    private const uint CfHdrop = 15;
    private const int DropEffectCopy = 1;
    private const int DropEffectMove = 2;
    private const uint GmemMoveable = 0x0002;
    private const uint GmemZeroInit = 0x0040;

    private static readonly uint PreferredDropEffectFormat = RegisterClipboardFormat("Preferred DropEffect");

    /// <summary>
    ///     Number changing each time the clipboard changes, to tell whether it still holds what was put on it.
    /// </summary>
    public static uint SequenceNumber => GetClipboardSequenceNumber();

    /// <summary>
    ///     Puts local files on the clipboard, copied or cut (<paramref name="isCut" />), owned by a window (zero for
    ///     none), returning <see cref="SequenceNumber" /> afterwards. Virtual files put on the clipboard before
    ///     (<see cref="WindowsVirtualFileDrag.SetClipboard" />) are released. Throws <see cref="IOException" /> when the
    ///     clipboard can't be opened.
    /// </summary>
    public static uint SetFiles(IntPtr owner, IReadOnlyCollection<string> paths, bool isCut)
    {
        ArgumentNullException.ThrowIfNull(paths);
        WindowsVirtualFileDrag.ReleaseClipboard();

        // DROPFILES: offset of the file list, point, non-client flag and wide characters, then the paths
        var text = string.Concat(paths.Select(path => path + '\0')) + '\0';
        var dropFiles = new byte[20 + Encoding.Unicode.GetByteCount(text)];
        BitConverter.GetBytes(20).CopyTo(dropFiles, 0);
        BitConverter.GetBytes(1).CopyTo(dropFiles, 16);
        Encoding.Unicode.GetBytes(text).CopyTo(dropFiles, 20);
        var dropEffect = BitConverter.GetBytes(isCut ? DropEffectMove : DropEffectCopy);

        Open(owner);
        try
        {
            EmptyClipboard();
            SetData(CfHdrop, dropFiles);
            SetData(PreferredDropEffectFormat, dropEffect);
        }
        finally
        {
            CloseClipboard();
        }

        return SequenceNumber;
    }

    /// <summary>
    ///     Gets local files on the clipboard, e.g. copied or cut in Explorer, and whether they were cut
    ///     (<paramref name="isMove" />). False when the clipboard has no files or can't be opened.
    /// </summary>
    public static bool TryGetFiles(out IReadOnlyList<string> paths, out bool isMove)
    {
        paths = [];
        isMove = false;
        if (!IsClipboardFormatAvailable(CfHdrop) || !TryOpen(IntPtr.Zero))
        {
            return false;
        }

        try
        {
            var drop = GetClipboardData(CfHdrop);
            if (drop == IntPtr.Zero)
            {
                return false;
            }

            var count = DragQueryFile(drop, uint.MaxValue, null, 0);
            var list = new List<string>();
            for (var index = 0u; index < count; index++)
            {
                var length = DragQueryFile(drop, index, null, 0);
                var buffer = new StringBuilder((int)length + 1);
                DragQueryFile(drop, index, buffer, length + 1);
                list.Add(buffer.ToString());
            }

            var effect = GetClipboardData(PreferredDropEffectFormat);
            if (effect != IntPtr.Zero && GlobalLock(effect) is var pointer && pointer != IntPtr.Zero)
            {
                isMove = (Marshal.ReadInt32(pointer) & DropEffectMove) != 0;
                GlobalUnlock(effect);
            }

            paths = list;
            return list.Count > 0;
        }
        finally
        {
            CloseClipboard();
        }
    }

    /// <summary>
    ///     Empties the clipboard, e.g. after pasting files cut in another application, releasing virtual files put on it
    ///     (<see cref="WindowsVirtualFileDrag.SetClipboard" />). Errors are ignored.
    /// </summary>
    public static void Clear()
    {
        WindowsVirtualFileDrag.ReleaseClipboard();
        if (!TryOpen(IntPtr.Zero))
        {
            return;
        }

        EmptyClipboard();
        CloseClipboard();
    }

    private static void Open(IntPtr owner)
    {
        if (!TryOpen(owner))
        {
            throw new IOException("The clipboard can't be opened, another application is using it");
        }
    }

    /// <summary>Opens the clipboard, trying again for a while when another application has it open.</summary>
    private static bool TryOpen(IntPtr owner)
    {
        for (var attempt = 0; attempt < 10; attempt++)
        {
            if (OpenClipboard(owner))
            {
                return true;
            }

            Thread.Sleep(20);
        }

        return false;
    }

    private static void SetData(uint format, byte[] bytes)
    {
        var memory = GlobalAlloc(GmemMoveable | GmemZeroInit, (UIntPtr)bytes.Length);
        if (memory == IntPtr.Zero)
        {
            throw new OutOfMemoryException();
        }

        Marshal.Copy(bytes, 0, GlobalLock(memory), bytes.Length);
        GlobalUnlock(memory);
        if (SetClipboardData(format, memory) == IntPtr.Zero)
        {
            // the clipboard owns the memory only when it was set
            GlobalFree(memory);
            throw new IOException($"Failed to set the clipboard (error {Marshal.GetLastWin32Error()})");
        }
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool OpenClipboard(IntPtr owner);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseClipboard();

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EmptyClipboard();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetClipboardData(uint format, IntPtr memory);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr GetClipboardData(uint format);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsClipboardFormatAvailable(uint format);

    [DllImport("user32.dll")]
    private static extern uint GetClipboardSequenceNumber();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern uint RegisterClipboardFormat(string format);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern uint DragQueryFile(IntPtr drop, uint index, StringBuilder? file, uint length);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GlobalAlloc(uint flags, UIntPtr bytes);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GlobalFree(IntPtr memory);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GlobalLock(IntPtr memory);

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalUnlock(IntPtr memory);
}
