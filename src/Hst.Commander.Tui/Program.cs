using Hst.Commander.Tui;
using Terminal.Gui;

var mainWindow = new MainWindow(Environment.CurrentDirectory, TuiSettings.Load().Theme);

try
{
    while (true)
    {
        TerminalScreen.EnterTui();
        Application.Init();
        await mainWindow.InitializeAsync();
        Application.Run(mainWindow);
        Application.Shutdown();
        TerminalScreen.LeaveTui();

        // like Midnight Commander, the TUI is shut down while a program opened in a pane runs in the terminal or
        // the output screen is shown (Ctrl+O), then started again with the panes refreshed
        switch (mainWindow.TakePendingTerminalAction())
        {
            case TerminalAction.RunProgram runProgram:
                if (TerminalScreen.Run(runProgram.Path) is { } error)
                {
                    mainWindow.ShowErrorWhenLoaded(error);
                }

                break;
            case TerminalAction.ShowOutput:
                TerminalScreen.ShowOutput();
                break;
            default:
                return;
        }
    }
}
finally
{
    mainWindow.DisposeNavigationContexts();
    if (Application.Initialized)
    {
        Application.Shutdown();
        TerminalScreen.LeaveTui();
    }
}
