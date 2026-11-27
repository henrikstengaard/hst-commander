using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Hst.Commander.Abstractions;
using Hst.Commander.Core;
using Hst.Commander.Core.Volumes;

namespace Hst.Commander.Gui;

/// <summary>
///     Modal properties dialog (Alt+Enter) showing the name and, in the General tab, the sections of lines of
///     <see cref="EntryProperties" /> separated by lines, the used space of drives as a progress bar and the check boxes
///     of <see cref="EntryProperties.FlagGroups" /> (attributes, permissions and Amiga protection bits), enabled when they can be
///     changed, followed by the tabs of the plugins (<see cref="EntryProperties.Tabs" />, see
///     <see cref="PropertiesTabView" />). Clicking the icon shows it in a higher resolution in the
///     <see cref="IconViewerDialog" />. When opened it counts the size and contents in the background and updates the
///     lines; closing the dialog cancels counting. When something can be changed the dialog has Save and Cancel, and
///     Save sets <see cref="CheckedFlags" /> and <see cref="TabTexts" />; nothing is changed before.
/// </summary>
public partial class PropertiesDialog : Window
{
    private readonly CancellationTokenSource _cancellationTokenSource = new();
    private readonly List<SelectableTextBlock> _valueTexts = [];
    private readonly List<List<CheckBox>> _flagCheckBoxes = [];
    private readonly List<PropertiesTabView> _tabViews = [];
    private bool _closed;

    public PropertiesDialog()
    {
        InitializeComponent();

        OkButton.Click += (_, _) =>
        {
            if (CancelButton.IsVisible)
            {
                CheckedFlags = _flagCheckBoxes
                    .Select(checkBoxes => (IReadOnlyList<bool>)checkBoxes
                        .Select(checkBox => checkBox.IsChecked == true)
                        .ToList())
                    .ToList();
                TabTexts = _tabViews.Select(tabView => tabView.EditedText).ToList();
            }

            Close();
        };
        CancelButton.Click += (_, _) => Close();
        Closed += (_, _) =>
        {
            _closed = true;
            _cancellationTokenSource.Cancel();
            _cancellationTokenSource.Dispose();
        };
        Opened += (_, _) => DialogButtons.Focus(OkButton);
    }

    /// <param name="properties">Properties to show.</param>
    /// <param name="countContents">
    ///     Counts the size and contents with the progress and the cancellation token of the dialog, see
    ///     <see cref="NavigationContext.CountContentsAsync" />. Only run when <see cref="EntryProperties.HasContents" />.
    /// </param>
    /// <param name="initialIcon">Icon shown left of the name until the icon is loaded, null for no icon.</param>
    /// <param name="loadIcon">
    ///     Loads the icon shown left of the name when opened, given its size in pixels, e.g. with
    ///     <see cref="Icons.FileIcons.LoadIconAsync" />. Null to keep the initial icon.
    /// </param>
    /// <param name="loadIcons">
    ///     Loads all icons of the entry for the <see cref="IconViewerDialog" />, e.g. with
    ///     <see cref="Icons.FileIcons.LoadIconsAsync" />. Null to view the icon shown.
    /// </param>
    internal PropertiesDialog(EntryProperties properties,
        Func<IProgress<EntryContents>, CancellationToken, Task> countContents, IImage? initialIcon = null,
        Func<int, CancellationToken, Task<IImage>>? loadIcon = null,
        Func<CancellationToken, Task<IReadOnlyList<Icons.ViewerIcon>>>? loadIcons = null) : this()
    {
        NameText.Text = properties.Name;
        if (initialIcon != null)
        {
            EntryIcon.Source = initialIcon;
            IconButton.IsVisible = true;
            IconButton.Click += async (_, _) =>
                await new IconViewerDialog(properties.Name, EntryIcon.Source ?? initialIcon, loadIcons)
                    .ShowDialog(this);
            if (loadIcon != null)
            {
                Opened += (_, _) => LoadIcon(loadIcon);
            }
        }

        AddLines(properties.GetSections());
        AddFlagGroups(properties.FlagGroups);
        AddTabs(properties.Tabs);
        if (properties.CanChange)
        {
            // Esc cancels instead of saving
            OkButton.Content = "Save";
            OkButton.IsCancel = false;
            CancelButton.IsCancel = true;
            CancelButton.IsVisible = true;
        }

        // the tabs are at least as high as the General tab, so the dialog doesn't shrink when changing tab
        Opened += (_, _) => Dispatcher.UIThread.Post(() => TabControl.MinHeight = TabControl.Bounds.Height,
            DispatcherPriority.Loaded);
        if (properties.UsedFraction is { } usedFraction)
        {
            UsedProgressBar.Value = usedFraction;
            UsedProgressBar.IsVisible = true;
        }

        if (!properties.HasContents)
        {
            return;
        }

        Opened += async (_, _) =>
        {
            // progress created on the UI thread reports on the UI thread
            var progress = new Progress<EntryContents>(contents => Update(properties, contents));
            var cancellationToken = _cancellationTokenSource.Token;
            try
            {
                await Task.Run(() => countContents(progress, cancellationToken), cancellationToken);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception exception)
            {
                if (!_closed)
                {
                    await new ErrorDialog(exception.Message).ShowDialog(this);
                }
            }
        };
    }

    /// <summary>Loads the icon in the size of the icon in pixels, keeping the initial icon when it fails.</summary>
    private async void LoadIcon(Func<int, CancellationToken, Task<IImage>> loadIcon)
    {
        var size = (int)Math.Ceiling(EntryIcon.Width * RenderScaling);
        try
        {
            var icon = await loadIcon(size, _cancellationTokenSource.Token);
            if (!_closed)
            {
                EntryIcon.Source = icon;
            }
        }
        catch (Exception)
        {
            // the icon is not essential, the initial icon is kept
        }
    }

    private void AddTabs(IReadOnlyList<PropertiesTab> tabs)
    {
        foreach (var tab in tabs)
        {
            var tabView = new PropertiesTabView(tab);
            _tabViews.Add(tabView);
            TabControl.Items.Add(new TabItem { Header = tab.Title, Content = tabView });
        }
    }

    /// <summary>Adds a separator between sections, not above the first one.</summary>
    private void AddSeparator()
    {
        if (LinesGrid.RowDefinitions.Count > 0)
        {
            AddRow(new Separator { Margin = new Thickness(0, 4) }, 0, 2);
        }
    }

    private void AddLines(IReadOnlyList<IReadOnlyList<PropertyLine>> sections)
    {
        foreach (var section in sections)
        {
            AddSeparator();
            foreach (var line in section)
            {
                var row = AddRow(new TextBlock
                {
                    Text = $"{line.Label}:",
                    Margin = new Thickness(0, 2, 16, 2),
                    VerticalAlignment = VerticalAlignment.Top
                }, 0, 1);
                var valueText = new SelectableTextBlock
                {
                    Text = line.Value,
                    Margin = new Thickness(0, 2),
                    TextWrapping = Avalonia.Media.TextWrapping.Wrap
                };
                Grid.SetRow(valueText, row);
                Grid.SetColumn(valueText, 1);
                LinesGrid.Children.Add(valueText);
                _valueTexts.Add(valueText);
            }
        }
    }

    /// <summary>
    ///     Whether each check box of each flag group is checked when Save was pressed, for
    ///     <see cref="NavigationContext.ChangePropertiesAsync" />. Null when not saved.
    /// </summary>
    public IReadOnlyList<IReadOnlyList<bool>>? CheckedFlags { get; private set; }

    /// <summary>
    ///     Text of each of <see cref="EntryProperties.Tabs" /> when Save was pressed, null for a tab without text, for
    ///     <see cref="NavigationContext.ChangePropertiesAsync" />. Null when not saved.
    /// </summary>
    public IReadOnlyList<string?>? TabTexts { get; private set; }

    private void AddFlagGroups(IReadOnlyList<PropertyFlagGroup> groups)
    {
        if (groups.Count == 0)
        {
            return;
        }

        AddSeparator();
        foreach (var group in groups)
        {
            var row = AddRow(new TextBlock
            {
                Text = $"{group.Label}:",
                Margin = new Thickness(0, 2, 16, 2),
                VerticalAlignment = VerticalAlignment.Center
            }, 0, 1);
            var panel = new WrapPanel();
            var checkBoxes = new List<CheckBox>();
            foreach (var flag in group.Flags)
            {
                var checkBox = new CheckBox
                {
                    Content = flag.Label,
                    IsChecked = flag.IsSet,
                    IsEnabled = group.CanChange,
                    Margin = new Thickness(0, 0, 12, 0),
                    MinWidth = 0
                };
                ToolTip.SetTip(checkBox, flag.Description);
                checkBoxes.Add(checkBox);
                panel.Children.Add(checkBox);
            }

            Grid.SetRow(panel, row);
            Grid.SetColumn(panel, 1);
            LinesGrid.Children.Add(panel);
            _flagCheckBoxes.Add(checkBoxes);
        }
    }

    /// <summary>Adds a control in a new row of the lines grid, returning the row.</summary>
    private int AddRow(Control control, int column, int columnSpan)
    {
        var row = LinesGrid.RowDefinitions.Count;
        LinesGrid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        Grid.SetRow(control, row);
        Grid.SetColumn(control, column);
        Grid.SetColumnSpan(control, columnSpan);
        LinesGrid.Children.Add(control);
        return row;
    }

    private void Update(EntryProperties properties, EntryContents contents)
    {
        if (_closed)
        {
            return;
        }

        var lines = properties.GetSections(contents).SelectMany(section => section).ToList();
        for (var index = 0; index < lines.Count && index < _valueTexts.Count; index++)
        {
            _valueTexts[index].Text = lines[index].Value;
        }
    }
}
