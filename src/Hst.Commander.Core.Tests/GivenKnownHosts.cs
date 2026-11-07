using Hst.Commander.Plugins.Ftp;

namespace Hst.Commander.Core.Tests;

public class GivenKnownHosts : IDisposable
{
    private readonly string _rootPath =
        Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), $"hst-commander-{Guid.NewGuid()}")).FullName;

    private readonly KnownHosts _knownHosts;

    public GivenKnownHosts()
    {
        _knownHosts = new KnownHosts(Path.Combine(_rootPath, "settings", "ftp-known-hosts.txt"));
    }

    public void Dispose()
    {
        Directory.Delete(_rootPath, true);
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void When_ServerIsNew_Then_KeyIsTrustedAndStored()
    {
        // act
        var failure = _knownHosts.Check("sftp://server", "ssh-ed25519", "abc");

        // assert
        Assert.Null(failure);
        Assert.Equal(["sftp://server ssh-ed25519 abc"], File.ReadAllLines(_knownHosts.FilePath));
    }

    [Fact]
    public void When_KeyIsKnown_Then_ItIsTrusted()
    {
        // arrange
        _knownHosts.Check("sftp://server", "ssh-ed25519", "abc");

        // act and assert
        Assert.Null(_knownHosts.Check("SFTP://server", "ssh-ed25519", "abc"));
        Assert.Single(File.ReadAllLines(_knownHosts.FilePath));
    }

    [Fact]
    public void When_KeyChanged_Then_MessageSaysSoAndKeyIsntStored()
    {
        // arrange
        _knownHosts.Check("sftp://server", "ssh-ed25519", "abc");

        // act
        var failure = _knownHosts.Check("sftp://server", "ssh-ed25519", "def");

        // assert
        Assert.NotNull(failure);
        Assert.Contains("has changed", failure);
        Assert.Contains(_knownHosts.FilePath, failure);
        Assert.Single(File.ReadAllLines(_knownHosts.FilePath));
    }

    [Fact]
    public void When_ServerHasKeyOfAnotherType_Then_ItIsStoredToo()
    {
        // arrange
        _knownHosts.Check("sftp://server", "ssh-ed25519", "abc");

        // act and assert
        Assert.Null(_knownHosts.Check("sftp://server", "ssh-rsa", "def"));
        Assert.Null(_knownHosts.Check("sftp://other", "ssh-ed25519", "ghi"));
        Assert.Equal(3, File.ReadAllLines(_knownHosts.FilePath).Length);
    }
}
