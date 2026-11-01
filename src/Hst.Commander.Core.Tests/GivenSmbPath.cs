using Hst.Commander.Abstractions;
using Hst.Commander.Plugins.Smb;

namespace Hst.Commander.Core.Tests;

public class GivenSmbPath
{
    [Theory]
    [InlineData(@"\\server\share\dir1\dir2")]
    [InlineData("//server/share/dir1/dir2")]
    [InlineData(@"\\server/share\dir1/dir2\")]
    [InlineData("smb://server/share/dir1/dir2")]
    [InlineData("SMB://server/share/dir1/dir2/")]
    [InlineData(@"smb://server\share\dir1\dir2")]
    [InlineData(@"  server\share\dir1\dir2  ")]
    [InlineData(@"\\server:445\share\dir1\dir2")]
    public void When_ParsingPath_Then_ServerShareAndDirectoriesAreReturned(string path)
    {
        // act
        var smbPath = SmbPath.Parse(path);

        // assert
        Assert.Equal("server", smbPath.Server);
        Assert.Equal("share", smbPath.Share);
        Assert.Equal(["dir1", "dir2"], smbPath.Directories);
        Assert.Equal(["share", "dir1", "dir2"], smbPath.PathComponents);
        Assert.Null(smbPath.Credentials);
    }

    [Theory]
    [InlineData(@"\\server")]
    [InlineData(@"\\server\")]
    [InlineData("smb://server")]
    [InlineData("smb://server/")]
    public void When_ParsingServer_Then_ShareIsNull(string path)
    {
        // act
        var smbPath = SmbPath.Parse(path);

        // assert
        Assert.Equal("server", smbPath.Server);
        Assert.Null(smbPath.Share);
        Assert.Empty(smbPath.PathComponents);
    }

    [Fact]
    public void When_ParsingUrlWithUserAndPassword_Then_CredentialsAreReturned()
    {
        // act
        var smbPath = SmbPath.Parse("smb://DOMAIN%5Cuser:pa%40ss@nas/My%20Share/dir");

        // assert
        Assert.Equal("nas", smbPath.Server);
        Assert.Equal("My Share", smbPath.Share);
        Assert.Equal(["dir"], smbPath.Directories);
        Assert.Equal(new VolumeCredentials(@"DOMAIN\user", "pa@ss"), smbPath.Credentials);
    }

    [Fact]
    public void When_ParsingUrlWithUser_Then_PasswordIsEmpty()
    {
        // act
        var smbPath = SmbPath.Parse("smb://user@nas/share");

        // assert
        Assert.Equal(new VolumeCredentials("user", string.Empty), smbPath.Credentials);
    }

    [Fact]
    public void When_ParsingUncPathWithPercent_Then_PercentIsKept()
    {
        // act
        var smbPath = SmbPath.Parse(@"\\nas\100%20share");

        // assert
        Assert.Equal("100%20share", smbPath.Share);
    }

    [Theory]
    [InlineData("smb://[fe80::1]/share", "fe80::1")]
    [InlineData("smb://[fe80::1]:445/share", "fe80::1")]
    [InlineData(@"\\192.168.1.10\share", "192.168.1.10")]
    public void When_ParsingAddress_Then_ServerIsTheAddress(string path, string server) =>
        Assert.Equal(server, SmbPath.Parse(path).Server);

    [Theory]
    [InlineData("")]
    [InlineData(@"\\")]
    [InlineData("smb://")]
    [InlineData(@"\\server:139\share")]
    [InlineData(@"\\server\share\..\other")]
    [InlineData("smb://user@/share")]
    public void When_ParsingInvalidPath_Then_IOExceptionIsThrown(string path) =>
        Assert.Throws<IOException>(() => SmbPath.Parse(path));

    [Fact]
    public void When_FormattingPath_Then_UncPathIsReturned()
    {
        // act
        var text = SmbPath.Parse("smb://server/share/dir").ToString();

        // assert
        var separator = Path.DirectorySeparatorChar;
        Assert.Equal($"{separator}{separator}server{separator}share{separator}dir", text);
    }

    [Theory]
    [InlineData(@"DOMAIN\user", "DOMAIN", "user")]
    [InlineData("user", "", "user")]
    [InlineData("user@domain.local", "", "user@domain.local")]
    public void When_SplittingUserName_Then_DomainAndUserNameAreReturned(string userName, string domain,
        string expectedUserName) =>
        Assert.Equal((domain, expectedUserName), SmbClientConnector.SplitUserName(userName));
}
