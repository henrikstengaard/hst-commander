using Avalonia;

namespace Hst.Commander.Gui;

internal static class Program
{
    /// <summary>
    ///     Starts the GUI. The main thread must be STA on Windows: Avalonia only uses OLE there, which drag and drop
    ///     with other applications needs (without it, windows don't accept drops and dragging out fails).
    /// </summary>
    [STAThread]
    public static void Main(string[] args) => BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);

    private static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();
}
