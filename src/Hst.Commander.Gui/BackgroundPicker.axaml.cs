using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Media;

namespace Hst.Commander.Gui;

/// <summary>
///     Button choosing the background of an image preview (the icon viewer and the image view of the viewer), showing
///     a swatch and the name of the background. Its flyout has rows of swatches of common colors (the name as tooltip)
///     and a custom color entered as red, green and blue values or hex text (#RRGGBB, #RGB, # optional), applied with
///     Apply or Enter. The first swatch is transparent (a checkerboard), or with <see cref="IncludeThemeBackground" />
///     the theme's background, which is then the default.
/// </summary>
public partial class BackgroundPicker : UserControl
{
    /// <summary>A background, a null color is transparent (the checkerboard) or the theme's background.</summary>
    private sealed record PreviewBackground(string Name, Color? Color, bool IsTheme = false);

    private static readonly PreviewBackground Transparent = new("Transparent", null);

    private static readonly PreviewBackground ThemeBackground = new("Theme", null, true);

    /// <summary>Resource of the theme's background, the background of the panes.</summary>
    private const string ThemeBackgroundResource = "PaneBackground";

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

    /// <summary>Checkerboard showing the transparent parts of an image.</summary>
    public static readonly IBrush CheckerboardBrush = CreateCheckerboardBrush();

    private PreviewBackground _background = Transparent;
    private Color _customColor = Colors.White;
    private bool _updatingCustomColor;
    private bool _includeThemeBackground;
    private IDisposable? _swatchBinding;

    /// <summary>Palette of backgrounds shown by the background button (flyouts have no generated name field).</summary>
    private Flyout BackgroundFlyout => (Flyout)BackgroundButton.Flyout!;

    public BackgroundPicker()
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
    }

    /// <summary>
    ///     Whether the first swatch is the theme's background (the background of the panes), chosen by default, e.g. the
    ///     image view of the viewer; otherwise transparent is the default, e.g. the icon viewer.
    /// </summary>
    public bool IncludeThemeBackground
    {
        get => _includeThemeBackground;
        set
        {
            _includeThemeBackground = value;
            SwatchesPanel.Children.Clear();
            AddSwatches();
            SetBackground(value ? ThemeBackground : Transparent);
        }
    }

    /// <summary>Whether the button can have the focus; false keeps the focus in e.g. the viewer's view.</summary>
    public bool IsButtonFocusable
    {
        get => BackgroundButton.Focusable;
        set => BackgroundButton.Focusable = value;
    }

    /// <summary>Where the palette opens, above the button by default (the button is at the bottom of the window).</summary>
    public PlacementMode FlyoutPlacement
    {
        get => BackgroundFlyout.Placement;
        set => BackgroundFlyout.Placement = value;
    }

    /// <summary>Raised when the palette is closed, e.g. to give the focus back.</summary>
    public event EventHandler? FlyoutClosed
    {
        add => BackgroundFlyout.Closed += value;
        remove => BackgroundFlyout.Closed -= value;
    }

    /// <summary>Brush of the background chosen, null for the theme's background.</summary>
    public IBrush? SelectedBrush => _background.IsTheme ? null : GetBrush(_background);

    /// <summary>Raised when another background is chosen.</summary>
    public event EventHandler? BackgroundChanged;

    /// <summary>Adds a row of swatch buttons to the palette for each row of common backgrounds.</summary>
    private void AddSwatches()
    {
        foreach (var row in BackgroundRows)
        {
            var rowPanel = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal };
            if (_includeThemeBackground && row == BackgroundRows[0])
            {
                rowPanel.Children.Add(CreateSwatch(ThemeBackground));
            }

            foreach (var background in row)
            {
                rowPanel.Children.Add(CreateSwatch(background));
            }

            SwatchesPanel.Children.Add(rowPanel);
        }
    }

    private Button CreateSwatch(PreviewBackground background)
    {
        var swatchBorder = new Border
        {
            BorderBrush = Brushes.Gray,
            BorderThickness = new Thickness(1),
            Width = 24,
            Height = 24
        };
        SetSwatchBackground(swatchBorder, background);
        var swatch = new Button
        {
            Width = 32,
            Height = 32,
            Margin = new Thickness(0, 0, 6, 6),
            Padding = new Thickness(0),
            HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Center,
            VerticalContentAlignment = Avalonia.Layout.VerticalAlignment.Center,
            Content = swatchBorder
        };
        ToolTip.SetTip(swatch, background.IsTheme ? "Theme background" : background.Name);
        swatch.Click += (_, _) =>
        {
            SetBackground(background);
            BackgroundFlyout.Hide();
        };
        return swatch;
    }

    /// <summary>
    ///     Shows a background in a swatch, the theme's background bound to the resource so it follows theme changes
    ///     (the binding is returned, null for other backgrounds).
    /// </summary>
    private static IDisposable? SetSwatchBackground(Border border, PreviewBackground background)
    {
        if (background.IsTheme)
        {
            return border.Bind(Border.BackgroundProperty, border.GetResourceObservable(ThemeBackgroundResource,
                resource => resource as IBrush));
        }

        border.Background = GetBrush(background);
        return null;
    }

    private void SetBackground(PreviewBackground background)
    {
        var changed = background != _background;
        _background = background;
        _swatchBinding?.Dispose();
        _swatchBinding = SetSwatchBackground(BackgroundSwatch, background);
        BackgroundText.Text = background.Name;
        if (changed)
        {
            BackgroundChanged?.Invoke(this, EventArgs.Empty);
        }
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
