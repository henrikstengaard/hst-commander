using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Hst.Commander.Core;

namespace Hst.Commander.Gui;

public partial class MainWindow : Window
{
    public MainWindow() : this(Environment.CurrentDirectory)
    {
    }

    public MainWindow(string initialPath)
    {
        InitializeComponent();

        LeftPane.NavigationContext = new NavigationContext(initialPath);
        RightPane.NavigationContext = new NavigationContext(initialPath);
        LeftPane.NavigationFailed += async (_, exception) => await ShowErrorAsync(exception);
        RightPane.NavigationFailed += async (_, exception) => await ShowErrorAsync(exception);

        AddHandler(KeyDownEvent, OnWindowKeyDown, RoutingStrategies.Tunnel);
        Opened += async (_, _) =>
        {
            await LeftPane.InitializeAsync();
            await RightPane.InitializeAsync();
            LeftPane.FocusEntries();
        };
        Closed += (_, _) =>
        {
            LeftPane.NavigationContext.Dispose();
            RightPane.NavigationContext.Dispose();
        };
    }

    private void OnWindowKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Tab)
        {
            return;
        }

        // tab always switches between the panes instead of moving focus through the controls
        e.Handled = true;
        (LeftPane.IsKeyboardFocusWithin ? RightPane : LeftPane).FocusEntries();
    }

    private Task ShowErrorAsync(Exception exception) => new ErrorDialog(exception.Message).ShowDialog(this);
}
