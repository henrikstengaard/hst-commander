using Hst.Commander.Core;

namespace Hst.Commander.Core.Tests;

public class GivenAppInfo
{
    [Fact]
    public void When_FromAssembly_Then_NameAuthorAndVersionAreRead()
    {
        // act
        var appInfo = AppInfo.FromAssembly(typeof(AppInfo).Assembly);

        // assert
        Assert.Equal("Hst Commander", appInfo.Name);
        Assert.Equal("Henrik Nørfjand Stengaard", appInfo.Author);
        Assert.Matches(@"^\d+\.\d+\.\d+$", appInfo.Version);
        Assert.Equal(typeof(AppInfo).Assembly.GetName().Version!.ToString(3), appInfo.Version);
    }

    [Fact]
    public void When_AboutText_Then_NameVersionAndAuthorAreIncluded()
    {
        // arrange
        var appInfo = new AppInfo("Hst Commander", "Author", "0.1.29");

        // act & assert
        Assert.Equal("Hst Commander v0.1.29\n\nCreated by Author", appInfo.AboutText);
    }
}
