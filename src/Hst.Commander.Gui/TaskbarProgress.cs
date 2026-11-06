using Avalonia.Controls;
using Avalonia.Platform;
using Hst.Commander.Core;
using Hst.Commander.Gui.MacOS;
using Hst.Commander.Gui.Windows;

namespace Hst.Commander.Gui;

/// <summary>
///     Creates the reporter showing the progress of a file operation outside the window: on the taskbar button on
///     Windows (<see cref="WindowsTaskbarProgress" />) and on the Dock tile on macOS (<see cref="MacDockProgress" />).
///     Elsewhere nothing is shown.
/// </summary>
internal static class TaskbarProgress
{
    private static MacDockProgress? s_macDockProgress;

    /// <summary>Creates a reporter for the taskbar button of a window, which should be the main window.</summary>
    public static TaskbarProgressReporter Create(TopLevel window)
    {
        ArgumentNullException.ThrowIfNull(window);

        if (OperatingSystem.IsWindows())
        {
            return new TaskbarProgressReporter(
                new WindowsTaskbarProgress(window.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero));
        }

        if (OperatingSystem.IsMacOS())
        {
            // the app has one Dock tile
            return new TaskbarProgressReporter(s_macDockProgress ??= new MacDockProgress(ReadAppIcon()));
        }

        return new TaskbarProgressReporter(null);
    }

    /// <summary>Reads the app icon file (also the main window's icon), empty when it can't be read.</summary>
    private static byte[] ReadAppIcon()
    {
        try
        {
            using var stream = AssetLoader.Open(new Uri("avares://Hst.Commander.Gui/Assets/hst-commander.ico"));
            using var memory = new MemoryStream();
            stream.CopyTo(memory);
            return memory.ToArray();
        }
        catch (Exception exception) when (exception is IOException or FileNotFoundException)
        {
            return [];
        }
    }
}
