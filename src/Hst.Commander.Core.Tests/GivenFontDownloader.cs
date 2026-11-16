using System.Net;
using System.Security.Cryptography;
using Hst.Commander.Abstractions;
using Hst.Commander.Core;

namespace Hst.Commander.Core.Tests;

public class GivenFontDownloader : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"hst-commander-fonts-{Guid.NewGuid():N}");

    private static readonly byte[] Font = [1, 2, 3, 4];

    private static readonly byte[] Readme = "license"u8.ToArray();

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, true);
        }
    }

    [Fact]
    public async Task When_HashesMatch_Then_FilesAreSaved()
    {
        // arrange
        var downloader = new FontDownloader(_directory, new FakeHandler());

        // act
        await downloader.DownloadAsync([Download("font.ttf", "/font.ttf", Font), Download("readme.txt", "/readme", Readme)]);

        // assert
        Assert.Equal(Font, await File.ReadAllBytesAsync(Path.Combine(_directory, "font.ttf")));
        Assert.Equal(Readme, await File.ReadAllBytesAsync(Path.Combine(_directory, "readme.txt")));
        Assert.Empty(Directory.GetFiles(_directory, "*.tmp"));
        Assert.True(downloader.IsDownloaded(Download("font.ttf", "/font.ttf", Font)));
    }

    [Fact]
    public async Task When_AHashDoesNotMatch_Then_NothingIsSaved()
    {
        // arrange
        var downloader = new FontDownloader(_directory, new FakeHandler());
        var wrong = Download("readme.txt", "/readme", Readme) with { Sha256 = Hash(Font) };

        // act
        await Assert.ThrowsAsync<InvalidDataException>(() =>
            downloader.DownloadAsync([Download("font.ttf", "/font.ttf", Font), wrong]));

        // assert
        Assert.False(Directory.Exists(_directory) && Directory.EnumerateFiles(_directory).Any());
    }

    [Theory]
    [InlineData("../font.ttf")]
    [InlineData("fonts/font.ttf")]
    [InlineData("..")]
    public async Task When_FileNameHasADirectory_Then_ItIsRejected(string fileName)
    {
        // arrange
        var downloader = new FontDownloader(_directory, new FakeHandler());

        // act and assert
        await Assert.ThrowsAsync<ArgumentException>(() =>
            downloader.DownloadAsync([Download(fileName, "/font.ttf", Font)]));
    }

    [Fact]
    public async Task When_UrlIsNotHttps_Then_ItIsRejected()
    {
        // arrange
        var downloader = new FontDownloader(_directory, new FakeHandler());

        // act and assert
        await Assert.ThrowsAsync<ArgumentException>(() =>
            downloader.DownloadAsync([Download("font.ttf", "/font.ttf", Font) with { Url = "http://example.com/font.ttf" }]));
    }

    [Fact]
    public async Task When_FileIsChanged_Then_ItIsNotDownloaded()
    {
        // arrange
        var downloader = new FontDownloader(_directory, new FakeHandler());
        var download = Download("font.ttf", "/font.ttf", Font);
        await downloader.DownloadAsync([download]);

        // act
        await File.WriteAllBytesAsync(Path.Combine(_directory, "font.ttf"), [9, 9]);

        // assert
        Assert.False(downloader.IsDownloaded(download));
    }

    private static ThemeFontDownload Download(string fileName, string path, byte[] data) =>
        new(fileName, $"https://example.com{path}", Hash(data), fileName, "license", "https://example.com");

    private static string Hash(byte[] data) => Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();

    /// <summary>Returns the font for /font.ttf and the readme for /readme.</summary>
    private sealed class FakeHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(request.RequestUri?.AbsolutePath switch
            {
                "/font.ttf" => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(Font) },
                "/readme" => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(Readme) },
                _ => new HttpResponseMessage(HttpStatusCode.NotFound)
            });
    }
}
