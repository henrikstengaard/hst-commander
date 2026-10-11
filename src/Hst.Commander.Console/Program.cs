using Hst.Commander.Console;
using Terminal.Gui;

var mainWindow = new MainWindow(Environment.CurrentDirectory);

try
{
    Application.Init();
    await mainWindow.InitializeAsync();
    Application.Run(mainWindow);
}
finally
{
    mainWindow.DisposeNavigationContexts();
    if (Application.Initialized)
    {
        Application.Shutdown();
    }
}
