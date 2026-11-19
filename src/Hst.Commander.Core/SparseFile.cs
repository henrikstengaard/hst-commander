using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Hst.Commander.Core;

/// <summary>
///     Makes files sparse, so the parts never written take no disk space, e.g. a large disk image created with F9 that
///     is mostly empty (see <see cref="Hst.Commander.Abstractions.IVolumeCreator.CreatesSparseFile" />).
/// </summary>
public static class SparseFile
{
    private const uint FsctlSetSparse = 0x000900C4;

    /// <summary>
    ///     Makes an empty file sparse before its length is set. On Windows the file is marked sparse (NTFS and ReFS
    ///     support it, FAT and exFAT don't). On Linux and macOS no marking is needed: setting the length of a file leaves
    ///     a hole where the file system supports it (e.g. ext4, btrfs, APFS). Returns false when the file can't be sparse,
    ///     it is then a regular file.
    /// </summary>
    public static bool TryMakeSparse(FileStream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);

        if (!OperatingSystem.IsWindows())
        {
            return true;
        }

        try
        {
            return DeviceIoControl(stream.SafeFileHandle, FsctlSetSparse, IntPtr.Zero, 0, IntPtr.Zero, 0, out _,
                IntPtr.Zero);
        }
        catch (Exception exception) when (exception is DllNotFoundException or EntryPointNotFoundException)
        {
            return false;
        }
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeviceIoControl(SafeFileHandle device, uint ioControlCode, IntPtr inBuffer,
        uint inBufferSize, IntPtr outBuffer, uint outBufferSize, out uint bytesReturned, IntPtr overlapped);
}
