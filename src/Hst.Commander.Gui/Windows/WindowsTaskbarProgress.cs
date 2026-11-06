using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Hst.Commander.Core;

namespace Hst.Commander.Gui.Windows;

/// <summary>
///     Shows the progress of a file operation on a window's taskbar button on Windows with the shell's
///     <c>ITaskbarList3</c>: green while running, yellow when paused (cancelling) and red on errors. Avalonia uses
///     <c>ITaskbarList3</c> only internally and has no API for progress. The window is the main window, as the
///     taskbar button belongs to it and not to the dialog it owns.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class WindowsTaskbarProgress(IntPtr hwnd) : ITaskbarProgress
{
    private const int Total = 1000;

    /// <summary>The shell's taskbar list, created once; null when it couldn't be created (e.g. without Explorer).</summary>
    private static ITaskbarList3? s_taskbarList;

    private static bool s_created;

    public void Set(TaskbarProgressState state, double fraction)
    {
        if (hwnd == IntPtr.Zero || GetTaskbarList() is not { } taskbarList)
        {
            return;
        }

        try
        {
            taskbarList.SetProgressState(hwnd, state switch
            {
                TaskbarProgressState.Indeterminate => TaskbarProgressFlags.Indeterminate,
                TaskbarProgressState.Normal => TaskbarProgressFlags.Normal,
                TaskbarProgressState.Paused => TaskbarProgressFlags.Paused,
                TaskbarProgressState.Error => TaskbarProgressFlags.Error,
                _ => TaskbarProgressFlags.NoProgress
            });
            if (state is TaskbarProgressState.Normal or TaskbarProgressState.Paused or TaskbarProgressState.Error)
            {
                taskbarList.SetProgressValue(hwnd, (ulong)Math.Round(Math.Clamp(fraction, 0, 1) * Total), Total);
            }
        }
        catch (COMException)
        {
            // progress on the taskbar is a nicety, the operation goes on without it
        }
    }

    private static ITaskbarList3? GetTaskbarList()
    {
        if (s_created)
        {
            return s_taskbarList;
        }

        s_created = true;
        try
        {
            var taskbarList = (ITaskbarList3)new TaskbarList();
            taskbarList.HrInit();
            s_taskbarList = taskbarList;
        }
        catch (Exception exception) when (exception is COMException or InvalidCastException)
        {
            s_taskbarList = null;
        }

        return s_taskbarList;
    }

    private enum TaskbarProgressFlags
    {
        NoProgress = 0,
        Indeterminate = 1,
        Normal = 2,
        Error = 4,
        Paused = 8
    }

    [ComImport]
    [Guid("56FDF344-FD6D-11d0-958A-006097C9A090")]
    [ClassInterface(ClassInterfaceType.None)]
    private class TaskbarList;

    /// <summary><c>ITaskbarList3</c> with the methods it inherits from <c>ITaskbarList</c> and <c>ITaskbarList2</c>, in vtable order.</summary>
    [ComImport]
    [Guid("ea1afb91-9e28-4b86-90e9-9e9f8a5eefaf")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ITaskbarList3
    {
        // ITaskbarList
        void HrInit();
        void AddTab(IntPtr hwnd);
        void DeleteTab(IntPtr hwnd);
        void ActivateTab(IntPtr hwnd);
        void SetActiveAlt(IntPtr hwnd);

        // ITaskbarList2
        void MarkFullscreenWindow(IntPtr hwnd, [MarshalAs(UnmanagedType.Bool)] bool fullscreen);

        // ITaskbarList3
        void SetProgressValue(IntPtr hwnd, ulong completed, ulong total);
        void SetProgressState(IntPtr hwnd, TaskbarProgressFlags flags);
    }
}
