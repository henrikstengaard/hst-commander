using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Hst.Commander.Plugins.PhysicalDisk;

/// <summary>Functions of the operating systems used to list, open and flush physical disks.</summary>
internal static class NativeMethods
{
    public const uint GenericRead = 0x80000000;
    public const uint GenericWrite = 0x40000000;
    public const uint FileShareReadWrite = 0x00000003;
    public const uint OpenExisting = 3;

    public const uint IoctlDiskGetDriveGeometryEx = 0x000700A0;
    public const uint IoctlStorageQueryProperty = 0x002D1400;
    public const uint IoctlVolumeGetVolumeDiskExtents = 0x00560000;
    public const uint FsctlLockVolume = 0x00090018;
    public const uint FsctlDismountVolume = 0x00090020;

    public const int ErrorAccessDenied = 5;
    public const int ErrorNoMoreFiles = 18;

    [DllImport("kernel32.dll", EntryPoint = "CreateFileW", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern SafeFileHandle CreateFile(string fileName, uint desiredAccess, uint shareMode,
        IntPtr securityAttributes, uint creationDisposition, uint flagsAndAttributes, IntPtr templateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool DeviceIoControl(SafeFileHandle device, uint ioControlCode, byte[]? inBuffer,
        int inBufferSize, [Out] byte[]? outBuffer, int outBufferSize, out int bytesReturned, IntPtr overlapped);

    [DllImport("kernel32.dll", EntryPoint = "FindFirstVolumeW", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern IntPtr FindFirstVolume([Out] char[] volumeName, int bufferLength);

    [DllImport("kernel32.dll", EntryPoint = "FindNextVolumeW", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern bool FindNextVolume(IntPtr findVolume, [Out] char[] volumeName, int bufferLength);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool FindVolumeClose(IntPtr findVolume);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool FlushFileBuffers(SafeFileHandle file);

    [DllImport("libc", EntryPoint = "fsync", SetLastError = true)]
    private static extern int Fsync(int fileDescriptor);

    /// <summary>Calls DeviceIoControl, returning the bytes returned, or null when it fails.</summary>
    public static int? DeviceIoControl(SafeFileHandle device, uint ioControlCode, byte[]? inBuffer, byte[]? outBuffer) =>
        DeviceIoControl(device, ioControlCode, inBuffer, inBuffer?.Length ?? 0, outBuffer, outBuffer?.Length ?? 0,
            out var bytesReturned, IntPtr.Zero)
            ? bytesReturned
            : null;

    /// <summary>Writes the cached writes of a device to the disk.</summary>
    public static void FlushToDisk(SafeFileHandle handle)
    {
        if (OperatingSystem.IsWindows())
        {
            if (!FlushFileBuffers(handle))
            {
                throw new IOException(new Win32Exception(Marshal.GetLastWin32Error()).Message);
            }
        }
        else if (Fsync((int)handle.DangerousGetHandle()) != 0)
        {
            throw new IOException($"The disk can't be flushed (error {Marshal.GetLastPInvokeError()})");
        }
    }
}
