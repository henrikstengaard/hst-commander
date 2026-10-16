using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Hst.Commander.Core;
using Hst.Commander.Core.Volumes;

namespace Hst.Commander.Gui;

/// <summary>
///     Modal properties dialog (Alt+Enter) showing the name and the sections of lines of <see cref="EntryProperties" />
///     separated by lines, and the used space of drives as a progress bar. When opened it counts the size and contents
///     in the background and updates the lines; closing the dialog cancels counting.
/// </summary>
public partial class PropertiesDialog : Window
{
    private readonly CancellationTokenSource _cancellationTokenSource = new();
    private readonly List<SelectableTextBlock> _valueTexts = [];
    private bool _closed;

    public PropertiesDialog()
    {
        InitializeComponent();

        OkButton.Click += (_, _) => Close();
        Closed += (_, _) =>
        {
            _closed = true;
            _cancellationTokenSource.Cancel();
            _cancellationTokenSource.Dispose();
        };
        Opened += (_, _) => OkButton.Focus();
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
    public PropertiesDialog(EntryProperties properties,
        Func<IProgress<EntryContents>, CancellationToken, Task> countContents, IImage? initialIcon = null,
        Func<int, CancellationToken, Task<IImage>>? loadIcon = null) : this()
    {
        NameText.Text = properties.Name;
        if (initialIcon != null)
        {
            EntryIcon.Source = initialIcon;
            EntryIcon.IsVisible = true;
            if (loadIcon != null)
            {
                Opened += (_, _) => LoadIcon(loadIcon);
            }
        }

        AddLines(properties.GetSections());
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

    private void AddLines(IReadOnlyList<IReadOnlyList<PropertyLine>> sections)
    {
        foreach (var section in sections)
        {
            AddRow(new Separator { Margin = new Thickness(0, 4) }, 0, 2);
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
