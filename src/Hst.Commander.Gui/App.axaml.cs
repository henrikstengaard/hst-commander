using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

namespace Hst.Commander.Gui;

public partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        ThemeManager.Apply(GuiSettings.Load().Theme);

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // plugins are loaded before the panes are created, so their volume providers are used
            var pluginErrors = Hst.Commander.Core.Plugins.FormatErrors(Hst.Commander.Core.Plugins.Load());
            desktop.MainWindow = new MainWindow(Environment.CurrentDirectory)
            {
                StartupError = pluginErrors,
                StartupOptions = Hst.Commander.Core.StartupOptions.Parse(desktop.Args ?? [])
            };
        }

        base.OnFrameworkInitializationCompleted();
    }
}
