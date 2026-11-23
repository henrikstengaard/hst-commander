using Avalonia;
using Hst.Commander.Core;

namespace Hst.Commander.Gui;

internal static class Program
{
    /// <summary>
    ///     Starts the GUI. The main thread must be STA on Windows: Avalonia only uses OLE there, which drag and drop
    ///     with other applications needs (without it, windows don't accept drops and dragging out fails).
    ///     An exception ending the GUI is logged and offered to be reported on the next start.
    /// </summary>
    [STAThread]
    public static int Main(string[] args)
    {
        CrashHandler.Install("gui");
        try
        {
            return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        catch (Exception exception)
        {
            CrashHandler.ReportCrash(exception, "main");
            return 1;
        }
    }

    private static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            // the X11 window class (WM_CLASS) is the desktop file's name and StartupWMClass of the Linux packages
            // (src/packaging/linux/package.sh), so desktops show the window with the app's icon, e.g. in the Ubuntu
            // dock; the default, the assembly name, matches no desktop file
            .With(new X11PlatformOptions { WmClass = LinuxDesktopFileName })
            .LogToTrace();

    /// <summary>Name of the GUI's desktop file in the Linux packages, without .desktop.</summary>
    private const string LinuxDesktopFileName = "hst-commander-gui";
}
