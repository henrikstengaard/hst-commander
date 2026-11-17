using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

namespace Hst.Commander.Gui;

public partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // plugins are loaded before the panes are created, so their volume providers and themes are used; the main
            // window applies the theme
            var pluginErrors = Hst.Commander.Core.Plugins.FormatErrors(Hst.Commander.Core.Plugins.Load());
            // fonts downloaded for themes (e.g. Topaz for Directory Opus 4), before the window uses the theme's font
            GuiFonts.LoadDownloadedFonts();
            desktop.MainWindow = new MainWindow(Environment.CurrentDirectory)
            {
                StartupError = pluginErrors,
                StartupOptions = Hst.Commander.Core.StartupOptions.Parse(desktop.Args ?? [])
            };
        }

        base.OnFrameworkInitializationCompleted();
    }
}
