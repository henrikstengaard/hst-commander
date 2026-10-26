using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Hst.Commander.Gui.Icons;

namespace Hst.Commander.Gui;

/// <summary>
///     Modal icon viewer opened from the properties dialog, showing the icons of an entry in a higher resolution: all
///     icons of the file (e.g. the normal and selected images of an Amiga icon or the icons of a Windows program) are
///     listed when there are several, each with its images in their own sizes (e.g. the sizes and color depths of a
///     Windows icon, or the sizes the OS has of a type icon). The image is shown fitted to the window or zoomed in
///     whole steps of its pixels.
///     Small icons are scaled up without smoothing, so their pixels stay sharp. The background of the preview is
///     transparent (the checkerboard) by default and can be changed to a common color or a custom color.
/// </summary>
public partial class IconViewerDialog : Window
{
    /// <summary>Icons up to this size are pixel art, scaled up without smoothing.</summary>
    private const int MaxPixelArtSize = 256;

    private static readonly (string Name, int Zoom)[] Zooms =
        [("Fit", 0), ("100%", 1), ("200%", 2), ("400%", 4), ("800%", 8)];

    /// <summary>Background of the preview, a null color is transparent (the checkerboard).</summary>
    private sealed record PreviewBackground(string Name, Color? Color);

    private static readonly PreviewBackground Transparent = new("Transparent", null);

    /// <summary>
    ///     Rows of swatches in the palette: transparent and the Amiga and Midnight Commander backgrounds, then a basic
    ///     color palette of grays and colors with a 60% and a 20% tint each.
    /// </summary>
    private static readonly PreviewBackground[][] BackgroundRows =
    [
        [
            Transparent,
            Swatch("Workbench gray", 0xaaaaaa),
            Swatch("Workbench 1.3 blue", 0x0055aa),
            Swatch("Midnight Commander blue", 0x0000aa)
        ],
        [
            Swatch("Black", 0x000000),
            Swatch("Charcoal", 0x333333),
            Swatch("Dark gray", 0x666666),
            Swatch("Gray", 0x999999),
            Swatch("Light gray", 0xcccccc),
            Swatch("White", 0xffffff)
        ],
        [
            Swatch("Purple", 0x7a3ca1),
            Swatch("Light purple", 0xb08ac7),
            Swatch("Pale purple", 0xe4d8ec),
            Swatch("Navy", 0x000088),
            Swatch("Light navy", 0x6566b8),
            Swatch("Pale navy", 0xcccce8)
        ],
        [
            Swatch("Blue", 0x0066b3),
            Swatch("Light blue", 0x66a3d2),
            Swatch("Pale blue", 0xcde0ef),
            Swatch("Green", 0x007f4d),
            Swatch("Light green", 0x67b293),
            Swatch("Pale green", 0xcbe5da)
        ],
        [
            Swatch("Yellow", 0xfff400),
            Swatch("Light yellow", 0xfff967),
            Swatch("Pale yellow", 0xfffdcc),
            Swatch("Orange", 0xf57631),
            Swatch("Light orange", 0xf7ac82),
            Swatch("Pale orange", 0xfde3d6)
        ],
        [
            Swatch("Red", 0xff0000),
            Swatch("Light red", 0xff6666),
            Swatch("Pale red", 0xffcccc),
            Swatch("Maroon", 0xa8272b),
            Swatch("Light maroon", 0xc97d7f),
            Swatch("Pale maroon", 0xedd3d4)
        ],
        [
            Swatch("Brown", 0x7f6848),
            Swatch("Light brown", 0xb1a491),
            Swatch("Pale brown", 0xe5e0da)
        ]
    ];

    private static readonly PreviewBackground[] CommonBackgrounds = [..BackgroundRows.SelectMany(row => row)];

    private static PreviewBackground Swatch(string name, uint rgb) =>
        new(name, Color.FromUInt32(0xff000000 | rgb));

    private static readonly IBrush CheckerboardBrush = CreateCheckerboardBrush();

    private readonly CancellationTokenSource _cancellationTokenSource = new();
    private bool _closed;
    private PreviewBackground _background = Transparent;
    private Color _customColor = Colors.White;
    private bool _updatingCustomColor;

    /// <summary>Palette of backgrounds shown by the background button (flyouts have no generated name field).</summary>
    private Flyout BackgroundFlyout => (Flyout)BackgroundButton.Flyout!;

    public IconViewerDialog()
    {
        InitializeComponent();

        AddSwatches();
        SetBackground(Transparent);
        BackgroundFlyout.Opening += (_, _) => SetCustomColor(_background.Color ?? Colors.White);
        RedUpDown.ValueChanged += (_, _) => UpdateCustomColorFromValues();
        GreenUpDown.ValueChanged += (_, _) => UpdateCustomColorFromValues();
        BlueUpDown.ValueChanged += (_, _) => UpdateCustomColorFromValues();
        HexTextBox.TextChanged += (_, _) => UpdateCustomColorFromHex();
        HexTextBox.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                e.Handled = true;
                ApplyCustomColor();
            }
        };
        ApplyCustomButton.Click += (_, _) => ApplyCustomColor();
        ZoomComboBox.ItemsSource = Zooms.Select(zoom => zoom.Name).ToList();
        ZoomComboBox.SelectedIndex = 0;
        ZoomComboBox.SelectionChanged += (_, _) => UpdateZoom();
        IconsList.SelectionChanged += (_, _) =>
        {
            if (IconsList.SelectedItem is ViewerIcon icon)
            {
                ShowIcon(icon);
            }
        };
        ImagesComboBox.SelectionChanged += (_, _) =>
        {
            if (ImagesComboBox.SelectedItem is ViewerIconImage image)
            {
                ShowImage(image);
            }
        };
        OkButton.Click += (_, _) => Close();
        Closed += (_, _) =>
        {
            _closed = true;
            _cancellationTokenSource.Cancel();
            _cancellationTokenSource.Dispose();
        };
        Opened += (_, _) => OkButton.Focus();
    }

    /// <param name="name">Name of the entry, shown in the title.</param>
    /// <param name="initialIcon">Icon shown until the icons are loaded.</param>
    /// <param name="loadIcons">
    ///     Loads all icons of the entry with their images, e.g. with <see cref="FileIcons.LoadIconsAsync" />. Null
    ///     to show the initial icon.
    /// </param>
    internal IconViewerDialog(string name, IImage initialIcon,
        Func<CancellationToken, Task<IReadOnlyList<ViewerIcon>>>? loadIcons) : this()
    {
        Title = $"Icon - {name}";
        SetIcons([new ViewerIcon("Icon", [CreateViewerImage(initialIcon)])]);
        if (loadIcons != null)
        {
            Opened += (_, _) => LoadIcons(loadIcons);
        }
    }

    private static ViewerIconImage CreateViewerImage(IImage image)
    {
        var (width, height) = GetPixelSize(image);
        return new ViewerIconImage(width, height, null, () => image);
    }

    private async void LoadIcons(Func<CancellationToken, Task<IReadOnlyList<ViewerIcon>>> loadIcons)
    {
        try
        {
            var icons = await loadIcons(_cancellationTokenSource.Token);
            if (!_closed && icons.Count > 0)
            {
                SetIcons(icons);
            }
        }
        catch (Exception)
        {
            // the initial icon is kept
        }
    }

    /// <summary>Shows the icons, the list only when there is more than one, and selects the first.</summary>
    private void SetIcons(IReadOnlyList<ViewerIcon> icons)
    {
        IconsList.ItemsSource = icons;
        IconsList.IsVisible = icons.Count > 1;
        IconsList.SelectedIndex = 0;
        ShowIcon(icons[0]);
    }

    /// <summary>Lists the images of an icon and shows the first (largest).</summary>
    private void ShowIcon(ViewerIcon icon)
    {
        ImagesComboBox.ItemsSource = icon.Images;
        ImagesComboBox.IsEnabled = icon.Images.Count > 1;
        ImagesComboBox.SelectedIndex = 0;
        ImagesText.Text = icon.Images.Count > 1 ? $"{icon.Images.Count} images" : string.Empty;
        ShowImage(icon.Images[0]);
    }

    private void ShowImage(ViewerIconImage image)
    {
        IconImage.Source = image.Image;
        RenderOptions.SetBitmapInterpolationMode(IconImage, Math.Max(image.Width, image.Height) <= MaxPixelArtSize
            ? BitmapInterpolationMode.None
            : BitmapInterpolationMode.HighQuality);
        UpdateZoom();
    }

    /// <summary>Size of the icon in pixels, the size in device independent units for drawings.</summary>
    private static (int Width, int Height) GetPixelSize(IImage icon) => icon is Bitmap bitmap
        ? (bitmap.PixelSize.Width, bitmap.PixelSize.Height)
        : ((int)Math.Round(icon.Size.Width), (int)Math.Round(icon.Size.Height));

    private void UpdateZoom()
    {
        if (IconImage.Source is not { } icon)
        {
            return;
        }

        var zoom = Zooms[Math.Max(ZoomComboBox.SelectedIndex, 0)].Zoom;
        if (zoom == 0)
        {
            // fit: the scroll viewer constrains the image, which is scaled to the available space
            ImageScrollViewer.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
            ImageScrollViewer.VerticalScrollBarVisibility = ScrollBarVisibility.Disabled;
            IconImage.Width = double.NaN;
            IconImage.Height = double.NaN;
            IconImage.Stretch = Stretch.Uniform;
            return;
        }

        // zoomed in whole steps of the icon's pixels on the screen
        var (width, height) = GetPixelSize(icon);
        var scaling = icon is Bitmap ? RenderScaling : 1;
        ImageScrollViewer.HorizontalScrollBarVisibility = ScrollBarVisibility.Auto;
        ImageScrollViewer.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        IconImage.Stretch = Stretch.Fill;
        IconImage.Width = width * zoom / scaling;
        IconImage.Height = height * zoom / scaling;
    }

    /// <summary>Adds a row of swatch buttons to the palette for each row of common backgrounds.</summary>
    private void AddSwatches()
    {
        foreach (var row in BackgroundRows)
        {
            var rowPanel = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal };
            foreach (var background in row)
            {
                rowPanel.Children.Add(CreateSwatch(background));
            }

            SwatchesPanel.Children.Add(rowPanel);
        }
    }

    private Button CreateSwatch(PreviewBackground background)
    {
        var swatch = new Button
        {
            Width = 32,
            Height = 32,
            Margin = new Thickness(0, 0, 6, 6),
            Padding = new Thickness(0),
            HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Center,
            VerticalContentAlignment = Avalonia.Layout.VerticalAlignment.Center,
            Content = new Border
            {
                Background = GetBrush(background),
                BorderBrush = Brushes.Gray,
                BorderThickness = new Thickness(1),
                Width = 24,
                Height = 24
            }
        };
        ToolTip.SetTip(swatch, background.Name);
        swatch.Click += (_, _) =>
        {
            SetBackground(background);
            BackgroundFlyout.Hide();
        };
        return swatch;
    }

    private void SetBackground(PreviewBackground background)
    {
        _background = background;
        var brush = GetBrush(background);
        ImageBorder.Background = brush;
        BackgroundSwatch.Background = brush;
        BackgroundText.Text = background.Name;
    }

    private static IBrush GetBrush(PreviewBackground background) =>
        background.Color is { } color ? new SolidColorBrush(color) : CheckerboardBrush;

    private static string FormatHex(Color color) => $"#{color.R:X2}{color.G:X2}{color.B:X2}";

    /// <summary>Shows a color in the custom color fields and swatch.</summary>
    private void SetCustomColor(Color color, bool updateHex = true)
    {
        _customColor = color;
        _updatingCustomColor = true;
        try
        {
            RedUpDown.Value = color.R;
            GreenUpDown.Value = color.G;
            BlueUpDown.Value = color.B;
            if (updateHex)
            {
                HexTextBox.Text = FormatHex(color);
            }
        }
        finally
        {
            _updatingCustomColor = false;
        }

        CustomSwatch.Background = new SolidColorBrush(color);
        ApplyCustomButton.IsEnabled = true;
    }

    private void UpdateCustomColorFromValues()
    {
        if (!_updatingCustomColor)
        {
            SetCustomColor(Color.FromRgb(ToByte(RedUpDown.Value), ToByte(GreenUpDown.Value),
                ToByte(BlueUpDown.Value)));
        }
    }

    private static byte ToByte(decimal? value) => (byte)Math.Clamp(Math.Round(value ?? 0), 0, 255);

    /// <summary>
    ///     Parses the hex text, e.g. "#0055AA", "0055AA" or "#05A"; while it isn't a valid color, it can't be applied.
    /// </summary>
    private void UpdateCustomColorFromHex()
    {
        if (_updatingCustomColor)
        {
            return;
        }

        var text = HexTextBox.Text?.Trim() ?? string.Empty;
        if (!text.StartsWith('#'))
        {
            text = "#" + text;
        }

        if (text.Length is 4 or 7 && text.Skip(1).All(char.IsAsciiHexDigit) && Color.TryParse(text, out var color))
        {
            SetCustomColor(color, false);
        }
        else
        {
            ApplyCustomButton.IsEnabled = false;
        }
    }

    private void ApplyCustomColor()
    {
        if (!ApplyCustomButton.IsEnabled)
        {
            return;
        }

        SetBackground(CommonBackgrounds.FirstOrDefault(background => background.Color == _customColor) ??
                      new PreviewBackground(FormatHex(_customColor), _customColor));
        BackgroundFlyout.Hide();
    }

    private static DrawingBrush CreateCheckerboardBrush()
    {
        var dark = new GeometryDrawing
        {
            Brush = new SolidColorBrush(Color.FromRgb(0xcc, 0xcc, 0xcc)),
            Geometry = new GeometryGroup
            {
                Children = [new RectangleGeometry(new Rect(0, 0, 8, 8)), new RectangleGeometry(new Rect(8, 8, 8, 8))]
            }
        };
        var light = new GeometryDrawing
        {
            Brush = Brushes.White,
            Geometry = new RectangleGeometry(new Rect(0, 0, 16, 16))
        };
        return new DrawingBrush(new DrawingGroup { Children = [light, dark] })
        {
            TileMode = TileMode.Tile,
            DestinationRect = new RelativeRect(0, 0, 16, 16, RelativeUnit.Absolute),
            Stretch = Stretch.None
        };
    }
}
