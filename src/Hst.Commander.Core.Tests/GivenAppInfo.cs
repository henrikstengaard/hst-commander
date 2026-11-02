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
    public void When_FromExecutableAssembly_Then_DescriptionAndBuildDateAreRead()
    {
        // act - the test assembly is an executable like the apps, so Directory.Build.targets sets both
        var appInfo = AppInfo.FromAssembly(typeof(GivenAppInfo).Assembly);

        // assert
        Assert.StartsWith("A dual-pane file manager", appInfo.Description);
        Assert.NotNull(appInfo.BuildDate);
        Assert.Equal(TimeSpan.Zero, appInfo.BuildDate.Value.Offset);
        Assert.InRange(appInfo.BuildDate.Value, DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddMinutes(1));
        var localDate = appInfo.BuildDate.Value.ToLocalTime();
        Assert.Equal($"Built {localDate:yyyy}-{localDate:MM}-{localDate:dd} {localDate:HH}:{localDate:mm}",
            appInfo.BuildDateText);
    }

    [Fact]
    public void When_AssemblyIsALibrary_Then_DescriptionAndBuildDateAreEmpty()
    {
        // act - only executables get them, so libraries compile once
        var appInfo = AppInfo.FromAssembly(typeof(AppInfo).Assembly);

        // assert
        Assert.Equal(string.Empty, appInfo.Description);
        Assert.Null(appInfo.BuildDate);
        Assert.Equal(string.Empty, appInfo.BuildDateText);
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
