using System.Net.Http.Headers;
using System.Security.Cryptography;
using Hst.Commander.Abstractions;

namespace Hst.Commander.Core;

/// <summary>
///     Downloads the font files of a theme (<see cref="ITheme.FontDownloads" />) chosen by the user to a directory only
///     Hst Commander loads fonts from (<see cref="DefaultDirectory" />), so nothing is installed in the operating system.
///     Files are only written when all of them have the expected SHA-256 hash.
/// </summary>
public sealed class FontDownloader
{
    private static readonly Lazy<HttpClient> SharedClient = new(() => CreateClient(new HttpClientHandler()));

    private readonly HttpClient _client;

    /// <param name="directory">Directory the files are saved in, <see cref="DefaultDirectory" /> when null.</param>
    /// <param name="handler">Handler sending the requests, e.g. a fake in tests; a shared client when null.</param>
    public FontDownloader(string? directory = null, HttpMessageHandler? handler = null)
    {
        Directory = directory ?? DefaultDirectory;
        _client = handler == null ? SharedClient.Value : CreateClient(handler);
    }

    /// <summary>Directory <c>fonts</c> in the settings directory, the fonts in it are loaded by the GUI.</summary>
    public static string DefaultDirectory => Path.Combine(SettingsPaths.Directory, "fonts");

    /// <summary>Directory the files are saved in.</summary>
    public string Directory { get; }

    /// <summary>True when the file has been downloaded and still has the expected hash.</summary>
    public bool IsDownloaded(ThemeFontDownload download)
    {
        ArgumentNullException.ThrowIfNull(download);
        var path = GetPath(download);
        try
        {
            return File.Exists(path) && HasHash(File.ReadAllBytes(path), download.Sha256);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>
    ///     Downloads the files and saves them when all of them have their expected hash, replacing earlier versions.
    /// </summary>
    /// <exception cref="InvalidDataException">A file doesn't have its expected hash; nothing is saved.</exception>
    /// <exception cref="HttpRequestException">A file can't be downloaded; nothing is saved.</exception>
    public async Task DownloadAsync(IEnumerable<ThemeFontDownload> downloads, IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(downloads);
        var files = new List<(ThemeFontDownload Download, byte[] Data)>();
        foreach (var download in downloads)
        {
            _ = GetPath(download); // rejects names with a directory before downloading anything
            if (!Uri.TryCreate(download.Url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
            {
                throw new ArgumentException($"'{download.Url}' isn't an https address.", nameof(downloads));
            }

            progress?.Report($"Downloading {download.Name}...");
            var data = await _client.GetByteArrayAsync(uri, cancellationToken);
            if (!HasHash(data, download.Sha256))
            {
                throw new InvalidDataException(
                    $"'{download.FileName}' downloaded from '{download.Url}' isn't the expected file, so it wasn't saved.");
            }

            files.Add((download, data));
        }

        System.IO.Directory.CreateDirectory(Directory);
        foreach (var (download, data) in files)
        {
            var path = GetPath(download);
            var temporaryPath = path + ".tmp";
            await File.WriteAllBytesAsync(temporaryPath, data, cancellationToken);
            File.Move(temporaryPath, path, true);
        }

        progress?.Report("Downloaded.");
    }

    /// <summary>Path of a downloaded file; its name must not contain a directory.</summary>
    private string GetPath(ThemeFontDownload download)
    {
        var fileName = download.FileName;
        if (string.IsNullOrWhiteSpace(fileName) || Path.GetFileName(fileName) != fileName || fileName is "." or "..")
        {
            throw new ArgumentException($"'{fileName}' isn't a file name.", nameof(download));
        }

        return Path.Combine(Directory, fileName);
    }

    private static bool HasHash(byte[] data, string sha256) =>
        string.Equals(Convert.ToHexString(SHA256.HashData(data)), sha256, StringComparison.OrdinalIgnoreCase);

    private static HttpClient CreateClient(HttpMessageHandler handler)
    {
        var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(60) };
        // a generic client name, no details of the user
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("HstCommander", AppInfo.Current.Version));
        return client;
    }
}
