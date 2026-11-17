using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Hst.Commander.Abstractions;
using Hst.Commander.Core;

namespace Hst.Commander.Gui;

/// <summary>
///     Asks before downloading the font files of a theme (<see cref="ITheme.FontDownloads" />), showing their licenses
///     and where they come from, and downloads them with <see cref="FontDownloader" />. Closes with true when the fonts
///     were downloaded, after loading them (<see cref="GuiFonts.LoadDownloadedFonts" />).
/// </summary>
public partial class FontDownloadDialog : Window
{
    private CancellationTokenSource? _cancellation;

    public FontDownloadDialog() : this("Theme", [])
    {
    }

    public FontDownloadDialog(string themeName, IReadOnlyList<ThemeFontDownload> downloads)
    {
        InitializeComponent();
        var downloader = new FontDownloader();
        IntroText.Text =
            $"{themeName} uses a font that isn't installed. Hst Commander doesn't include it because of its license, but you can download it from the project that made it:";
        FilesList.ItemsSource = downloads.Select(CreateFileView).ToList();
        LocationText.Text =
            $"The files are saved in {downloader.Directory} and only used by Hst Commander. Nothing is installed in the operating system; delete the files to remove them.";

        DownloadButton.Click += async (_, _) =>
        {
            DownloadButton.IsEnabled = false;
            StatusText.IsVisible = true;
            StatusText.Foreground = null;
            _cancellation = new CancellationTokenSource();
            try
            {
                await downloader.DownloadAsync(downloads, new Progress<string>(text => StatusText.Text = text),
                    _cancellation.Token);
                GuiFonts.LoadDownloadedFonts();
                Close(true);
            }
            catch (OperationCanceledException)
            {
                // closed while downloading
            }
            catch (Exception exception) when (exception is HttpRequestException or InvalidDataException
                                                  or IOException or UnauthorizedAccessException)
            {
                StatusText.Text = $"The font couldn't be downloaded: {exception.Message}";
                StatusText.Foreground = Brushes.IndianRed;
                DownloadButton.IsEnabled = true;
            }
        };
        CancelButton.Click += (_, _) => Close(false);
        Closed += (_, _) => _cancellation?.Cancel();
        Opened += (_, _) => DownloadButton.Focus();
    }

    /// <summary>Name, license and project of a file, the project as a link.</summary>
    private Control CreateFileView(ThemeFontDownload download)
    {
        var link = new HyperlinkButton
        {
            Content = download.SourceUrl,
            Padding = new Avalonia.Thickness(0),
            HorizontalAlignment = HorizontalAlignment.Left
        };
        link.Click += async (_, _) =>
        {
            if (Uri.TryCreate(download.SourceUrl, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps)
            {
                await Launcher.LaunchUriAsync(uri);
            }
        };
        return new StackPanel
        {
            Spacing = 2,
            Children =
            {
                new TextBlock { Text = download.Name, FontWeight = FontWeight.Bold },
                new TextBlock { Text = download.License, TextWrapping = TextWrapping.Wrap },
                link
            }
        };
    }
}
