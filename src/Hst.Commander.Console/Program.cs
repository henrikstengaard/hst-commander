using Hst.Commander.Console;
using Terminal.Gui;

Application.Init();

var mainApp = new MainApp();
await mainApp.Refresh();
mainApp.Adjust();

Application.Run(Application.Top);

// hstCommanderApp.Setup();
// hstCommanderApp.Run();

// Console.OutputEncoding = Encoding.Default;

Application.Shutdown();
