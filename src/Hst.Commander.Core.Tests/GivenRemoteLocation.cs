using Hst.Commander.Abstractions;
using Hst.Commander.Plugins.Ftp;

namespace Hst.Commander.Core.Tests;

public class GivenRemoteLocation
{
    [Fact]
    public void When_ParsingFtpUrlWithPortAndPath_Then_ServerAndDirectoriesAreReturned()
    {
        // act
        var location = RemoteLocation.Parse("ftp://server:2121/pub/dir");

        // assert
        Assert.Equal(new RemoteServer(RemoteProtocol.Ftp, "server", 2121), location.Server);
        Assert.Equal(["pub", "dir"], location.Directories);
        Assert.Null(location.Credentials);
    }

    [Theory]
    [InlineData("sftp://server", RemoteProtocol.Sftp, 22)]
    [InlineData("SFTP://server", RemoteProtocol.Sftp, 22)]
    [InlineData("ftps://server", RemoteProtocol.Ftps, 21)]
    [InlineData("ftp://server", RemoteProtocol.Ftp, 21)]
    public void When_ParsingUrlWithoutPath_Then_DefaultPortAndHomeDirectoryAreUsed(string text,
        RemoteProtocol protocol, int port)
    {
        // act
        var location = RemoteLocation.Parse(text);

        // assert
        Assert.Equal(new RemoteServer(protocol, "server", port), location.Server);
        Assert.Null(location.Directories);
    }

    [Fact]
    public void When_ParsingUrlWithRootPath_Then_DirectoriesAreEmpty() =>
        Assert.Empty(RemoteLocation.Parse("sftp://server/").Directories!);

    [Fact]
    public void When_ParsingPathWithBackslashes_Then_TheySeparateDirectories() =>
        Assert.Equal(["home", "user"], RemoteLocation.Parse(@"sftp://server\home\user").Directories);

    [Fact]
    public void When_ParsingUrlWithCredentialsAndEscapes_Then_TheyAreUnescaped()
    {
        // act
        var location = RemoteLocation.Parse("sftp://us%40er:p%3Ass@server/My%20Files");

        // assert
        Assert.Equal(new VolumeCredentials("us@er", "p:ss"), location.Credentials);
        Assert.Equal(["My Files"], location.Directories);
        Assert.Equal("sftp://server/My%20Files", location.ToString());
    }

    [Fact]
    public void When_ParsingIpv6Address_Then_BracketsAreRemoved()
    {
        // act
        var location = RemoteLocation.Parse("sftp://[::1]:2222/dir");

        // assert
        Assert.Equal(new RemoteServer(RemoteProtocol.Sftp, "::1", 2222), location.Server);
        Assert.Equal("sftp://[::1]:2222", location.Server.PathRoot);
    }

    [Theory]
    [InlineData("server/dir")]
    [InlineData("http://server")]
    [InlineData("sftp://")]
    [InlineData("sftp://server:0")]
    [InlineData("sftp://server:abc")]
    [InlineData("sftp://server/dir/../etc")]
    public void When_ParsingInvalidLocation_Then_IOExceptionIsThrown(string text) =>
        Assert.Throws<IOException>(() => RemoteLocation.Parse(text));

    [Fact]
    public void When_FormattingDetailsWithDefaultPort_Then_PortIsLeftOut() =>
        Assert.Equal("sftp://server/home/user",
            RemoteLocation.Format(new ConnectionDetails("SFTP", " server ", 22, "/home/user")));

    [Fact]
    public void When_FormattingDetailsWithOtherPortAndNoPath_Then_PortIsIncludedAndHomeIsUsed()
    {
        // act
        var text = RemoteLocation.Format(new ConnectionDetails("FTPS", "server", 2121, ""));

        // assert
        Assert.Equal("ftps://server:2121", text);
        Assert.Null(RemoteLocation.Parse(text).Directories);
    }

    [Theory]
    [InlineData("", null, "")]
    [InlineData("server", 70000, "")]
    [InlineData("user@server", null, "")]
    [InlineData("server", null, "../etc")]
    public void When_FormattingInvalidDetails_Then_IOExceptionIsThrown(string host, int? port, string path) =>
        Assert.Throws<IOException>(() => RemoteLocation.Format(new ConnectionDetails("FTP", host, port, path)));

    [Fact]
    public void When_FormattingUnknownProtocol_Then_IOExceptionIsThrown() =>
        Assert.Throws<IOException>(() => RemoteLocation.Format(new ConnectionDetails("HTTP", "server", null, "")));

    [Theory]
    [InlineData("ftp://server:2121/pub", "FTP", 2121, "/pub")]
    [InlineData("sftp://server", "SFTP", null, "")]
    [InlineData("sftp://server/", "SFTP", null, "/")]
    public void When_LocationIsConvertedToDetails_Then_ItIsFormattedAgain(string text, string protocol, int? port,
        string path)
    {
        // act
        var details = RemoteLocation.Parse(text).ToConnectionDetails();

        // assert
        Assert.Equal(new ConnectionDetails(protocol, "server", port, path), details);
        Assert.Equal(text, RemoteLocation.Format(details));
    }
}
