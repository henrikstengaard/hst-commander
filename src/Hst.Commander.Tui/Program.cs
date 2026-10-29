using Hst.Commander.Core;
using Hst.Commander.Tui;
using Terminal.Gui;

// plugins are loaded before the panes are created, so their volume providers are used
var pluginErrors = Plugins.FormatErrors(Plugins.Load());
var mainWindow = new MainWindow(Environment.CurrentDirectory, TuiSettings.Load());
// waited for here, so the TUI runs on the main thread (there's no synchronization context yet to deadlock)
mainWindow.ApplyStartupOptionsAsync(StartupOptions.Parse(args)).GetAwaiter().GetResult();
if (pluginErrors != null)
{
    mainWindow.ShowErrorWhenLoaded(pluginErrors);
}

try
{
    while (true)
    {
        TerminalScreen.EnterTui();
        Application.Init();
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
            case TerminalAction.RunEditor runEditor:
                if (TerminalScreen.RunEditor(runEditor.Command, runEditor.Path) is { } editorError)
                {
                    mainWindow.ShowErrorWhenLoaded(editorError);
                }

                break;
            case TerminalAction.ShowOutput:
                TerminalScreen.ShowOutput();
                break;
            case TerminalAction.RestartAsRoot restartAsRoot:
                // quits after Hst Commander ran as root, or shows why sudo failed
                if (Elevation.RunAsRootInTerminal(restartAsRoot.Options) is not { } restartError)
                {
                    return;
                }

                mainWindow.ShowErrorWhenLoaded(restartError);
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
