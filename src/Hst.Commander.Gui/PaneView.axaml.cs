using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Hst.Commander.Core;

namespace Hst.Commander.Gui;

/// <summary>
///     Navigation pane listing the entries of the current location of a <see cref="NavigationContext" />.
/// </summary>
public partial class PaneView : UserControl
{
    private NavigationContext? _navigationContext;

    public PaneView()
    {
        InitializeComponent();

        EntriesList.AddHandler(KeyDownEvent, OnEntriesKeyDown, RoutingStrategies.Tunnel);
        EntriesList.DoubleTapped += async (_, _) => await ActivateSelectedEntryAsync();
        EntriesList.PropertyChanged += (_, e) =>
        {
            if (e.Property == IsKeyboardFocusWithinProperty)
            {
                PathBadge.Classes.Set("active", EntriesList.IsKeyboardFocusWithin);
            }
        };
    }

    /// <summary>Raised when navigation fails, so the window can show the error.</summary>
    public event EventHandler<Exception>? NavigationFailed;

    public NavigationContext NavigationContext
    {
        get => _navigationContext ?? throw new InvalidOperationException("Navigation context is not set.");
        set => _navigationContext = value;
    }

    public Task InitializeAsync() => RunNavigationAsync(() => Task.CompletedTask);

    public void FocusEntries()
    {
        if (EntriesList.SelectedIndex >= 0 && EntriesList.ContainerFromIndex(EntriesList.SelectedIndex) is { } container)
        {
            container.Focus(NavigationMethod.Tab);
            return;
        }

        EntriesList.Focus(NavigationMethod.Tab);
    }

    private async void OnEntriesKeyDown(object? sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Enter:
                e.Handled = true;
                await ActivateSelectedEntryAsync();
                break;
            case Key.Back:
                e.Handled = true;
                await RunNavigationAsync(() => NavigationContext.GoParentAsync());
                break;
        }
    }

    private async Task ActivateSelectedEntryAsync()
    {
        if (EntriesList.SelectedItem is not EntryItem item)
        {
            return;
        }

        await RunNavigationAsync(() => NavigationContext.OpenEntryAsync(item.Entry));
    }

    private async Task RunNavigationAsync(Func<Task> action)
    {
        try
        {
            await action();
            await RefreshAsync();
        }
        catch (Exception exception)
        {
            NavigationFailed?.Invoke(this, exception);
        }
    }

    private async Task RefreshAsync()
    {
        var hadFocus = EntriesList.IsKeyboardFocusWithin;
        var selectedIndex = EntriesList.SelectedIndex;
        var entries = (await NavigationContext.ListEntriesAsync()).Select(entry => new EntryItem(entry)).ToList();

        PathText.Text = NavigationContext.CurrentPath;
        EntriesList.ItemsSource = entries;
        EntriesList.SelectedIndex = entries.Count == 0 ? -1 : Math.Clamp(selectedIndex, 0, entries.Count - 1);
        if (EntriesList.SelectedItem != null)
        {
            EntriesList.ScrollIntoView(EntriesList.SelectedItem);
        }

        if (hadFocus)
        {
            EntriesList.UpdateLayout();
            FocusEntries();
        }
    }
}
