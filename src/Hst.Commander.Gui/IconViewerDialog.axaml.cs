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

    private readonly CancellationTokenSource _cancellationTokenSource = new();
    private bool _closed;

    public IconViewerDialog()
    {
        InitializeComponent();

        ImageBorder.Background = BackgroundPicker.SelectedBrush;
        BackgroundPicker.BackgroundChanged += (_, _) => ImageBorder.Background = BackgroundPicker.SelectedBrush;
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
}
