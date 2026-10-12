namespace Hst.Commander.Core.Tests;

public class GivenNavigationContext
{
    [Fact]
    public void When_DiskSpaceInLocalDirectory_Then_FreeAndTotalSpaceAreReturned()
    {
        // arrange
        using var navigationContext = new NavigationContext(Directory.GetCurrentDirectory());

        // act
        var diskSpace = navigationContext.DiskSpace;

        // assert
        Assert.NotNull(diskSpace);
        Assert.Matches(@"^\d+[BKMGTPE]/\d+[BKMGTPE]$", diskSpace);
    }

    [Fact]
    public void When_DiskSpaceAtDriveList_Then_NoDiskSpaceIsReturned()
    {
        // arrange
        using var navigationContext = new NavigationContext(string.Empty);

        // act
        var diskSpace = navigationContext.DiskSpace;

        // assert
        Assert.Null(diskSpace);
    }
}
