using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Hst.Commander.Core;

namespace Hst.Commander.Gui;

public partial class App : Application
{
    /// <summary>True while an <see cref="UnexpectedErrorDialog" /> is shown, so further exceptions are only logged.</summary>
    private bool _showingUnexpectedError;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        // exceptions on the UI thread (e.g. from async void event handlers) are logged and shown, and the GUI keeps
        // running
        Dispatcher.UIThread.UnhandledException += (_, args) =>
        {
            args.Handled = true;
            ErrorLog.Unhandled(args.Exception, "UI thread");
            Dispatcher.UIThread.Post(() => _ = ShowUnexpectedErrorAsync(args.Exception));
        };

        AddContextMenuKeys();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // plugins are loaded before the panes are created, so their volume providers and themes are used; the main
            // window applies the theme
            var pluginErrors = Core.Plugins.FormatErrors(Core.Plugins.Load());
            // fonts downloaded for themes (e.g. Topaz for Directory Opus 4), before the window uses the theme's font
            GuiFonts.LoadDownloadedFonts();
            // both panes start where they were when last quit, see StartupOptions.WithSavedPanes
            var startupOptions = StartupOptions.Parse(desktop.Args ?? [])
                .WithSavedPanes(GuiSettings.Load().Panes, true, NavigationContext.HomeDirectory);
            desktop.MainWindow =
                new MainWindow(Environment.CurrentDirectory)
                {
                    StartupError = pluginErrors,
                    LastCrashReport = CrashMarker.TakePending(),
                    StartupOptions = startupOptions
                };
            CrashHandler.StartTestCrashThread();
        }

        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>
    ///     Shift+F10 and Ctrl+F10 open the context menu on every system, like the menu key. Avalonia only adds Shift+F10
    ///     on Windows; Ctrl+F10 is the same key in the TUI, for terminals taking Shift+F10 themselves.
    /// </summary>
    private void AddContextMenuKeys()
    {
        if (PlatformSettings?.HotkeyConfiguration.OpenContextMenu is not { } keys)
        {
            return;
        }

        foreach (var gesture in new[]
                 {
                     new KeyGesture(Key.F10, KeyModifiers.Shift), new KeyGesture(Key.F10, KeyModifiers.Control)
                 })
        {
            if (!keys.Contains(gesture))
            {
                keys.Add(gesture);
            }
        }
    }

    private async Task ShowUnexpectedErrorAsync(Exception exception)
    {
        if (_showingUnexpectedError ||
            ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime { MainWindow: { IsVisible: true } owner })
        {
            return;
        }

        _showingUnexpectedError = true;
        try
        {
            // shown over the topmost dialog, as a modal dialog over the main window would leave the other one usable
            Window topmost = owner;
            while (topmost.OwnedWindows.LastOrDefault(window => window.IsVisible) is { } owned)
            {
                topmost = owned;
            }

            await UnexpectedErrorDialog.ForException(exception).ShowDialog(topmost);
        }
        catch (Exception dialogException)
        {
            ErrorLog.Unhandled(dialogException, "showing an unexpected error");
        }
        finally
        {
            _showingUnexpectedError = false;
        }
    }
}
