using Avalonia;
using Hst.Commander.Gui;

AppBuilder.Configure<App>()
    .UsePlatformDetect()
    .LogToTrace()
    .StartWithClassicDesktopLifetime(args);
