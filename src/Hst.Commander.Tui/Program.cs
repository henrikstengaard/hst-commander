using Hst.Commander.Core;
using Hst.Commander.Tui;

// unhandled exceptions are logged; one ending the process is offered to be reported on the next start
CrashHandler.Install("tui");
// an exception on another thread ends the process without running the finally below, so the terminal is restored
// here (after the crash handler installed above has logged it)
AppDomain.CurrentDomain.UnhandledException += (_, _) =>
{
    try
    {
        if (Application.Initialized)
        {
            TerminalScreen.BeforeShutdown();
            Application.Shutdown();
            TerminalScreen.LeaveTui();
        }
    }
    catch (Exception)
    {
        // the process ends anyway
    }

    PrintCrashMessage();
};

// set before any palette is created, as attributes keep the RGB values of their colors
TuiTrueColors.Apply();
TuiColorOutput.Register();

// plugins are loaded before the panes are created, so their volume providers are used
var pluginErrors = Plugins.FormatErrors(Plugins.Load());
var settings = TuiSettings.Load();
// the right pane starts where it was when last quit and the left pane in the current directory, see
// StartupOptions.WithSavedPanes
var startupOptions = StartupOptions.Parse(args)
    .WithSavedPanes(settings.Panes, false, NavigationContext.HomeDirectory);
var mainWindow = new MainWindow(Environment.CurrentDirectory, settings);
// waited for here, so the TUI runs on the main thread (there's no synchronization context yet to deadlock)
mainWindow.ApplyStartupOptionsAsync(startupOptions).GetAwaiter().GetResult();
if (pluginErrors != null)
{
    mainWindow.ShowErrorWhenLoaded(pluginErrors);
}

if (CrashMarker.TakePending() is { } lastCrashReport)
{
    mainWindow.ShowLastCrashWhenLoaded(lastCrashReport);
}

CrashHandler.StartTestCrashThread();
var crashed = false;

try
{
    while (true)
    {
        TerminalScreen.EnterTui();
        Application.Init();
        // actions posted from other threads and await continuations run on the main loop
        UiThread.Attach();
        // function key sequences Terminal.Gui doesn't know, e.g. Shift+F10 in Terminal.app
        TerminalKeyPatterns.Install();
        TerminalScreen.DisableLineInput();
        TuiTrueColors.Apply();
        mainWindow.ApplyTerminalColors();
        // an exception escaping the main loop is logged and shown, and the loop resumes
        Application.Run(mainWindow, exception =>
        {
            ErrorLog.Unhandled(exception, "main loop");
            UiThread.Post(() => mainWindow.ShowUnexpectedError(exception));
            return true;
        });
        TerminalScreen.BeforeShutdown();
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
            case TerminalAction.RunCommand runCommand:
                if (TerminalScreen.RunCommand(runCommand.Command, runCommand.WorkingDirectory) is { } commandError)
                {
                    mainWindow.ShowErrorWhenLoaded(commandError);
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
                // quit, or restarting as administrator, where the restarted process saves the panes when it quits
                if (!mainWindow.IsRestarting)
                {
                    mainWindow.SavePaneLocations();
                }

                return;
        }
    }
}
catch (Exception exception)
{
    crashed = true;
    CrashHandler.ReportCrash(exception, "main");
}
finally
{
    mainWindow.DisposeNavigationContexts();
    if (Application.Initialized)
    {
        TerminalScreen.BeforeShutdown();
        Application.Shutdown();
        TerminalScreen.LeaveTui();
    }
}

// printed once the terminal is restored
if (crashed)
{
    PrintCrashMessage();
    Environment.ExitCode = 1;
}

return;

static void PrintCrashMessage() =>
    Console.Error.WriteLine(ErrorLog.CurrentFile is { } logFile
        ? $"{AppInfo.Current.Name} crashed, the error was logged to {logFile}"
        : $"{AppInfo.Current.Name} crashed");
