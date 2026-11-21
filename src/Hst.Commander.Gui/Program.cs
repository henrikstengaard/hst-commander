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
            .LogToTrace();
}
