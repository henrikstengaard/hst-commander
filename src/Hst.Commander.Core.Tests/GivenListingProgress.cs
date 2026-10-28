namespace Hst.Commander.Core.Tests;

public class GivenListingProgress
{
    [Fact]
    public async Task When_TaskIsDoneBeforeTheDelay_Then_ProgressIsNotShown()
    {
        // arrange
        var shown = false;

        // act
        var result = await ListingProgress.RunAsync(Task.FromResult(1), "Reading", _ => shown = true, () => { },
            TimeSpan.FromSeconds(10));

        // assert
        Assert.Equal(1, result);
        Assert.False(shown);
    }

    [Fact]
    public async Task When_TaskTakesLongerThanTheDelay_Then_ProgressIsShownAndHidden()
    {
        // arrange
        var completion = new TaskCompletionSource<int>();
        string? shownMessage = null;
        var hidden = false;

        // act
        var listing = ListingProgress.RunAsync(completion.Task, "Reading Physical disks...",
            message => shownMessage = message, () => hidden = true, TimeSpan.FromMilliseconds(10));
        await Task.Delay(200);
        var shownWhileRunning = shownMessage;
        var hiddenWhileRunning = hidden;
        completion.SetResult(2);
        var result = await listing;

        // assert
        Assert.Equal("Reading Physical disks...", shownWhileRunning);
        Assert.False(hiddenWhileRunning);
        Assert.True(hidden);
        Assert.Equal(2, result);
    }

    [Fact]
    public async Task When_TaskFailsAfterTheDelay_Then_ProgressIsHiddenAndTheErrorIsThrown()
    {
        // arrange
        var completion = new TaskCompletionSource<int>();
        var hidden = false;

        // act
        var listing = ListingProgress.RunAsync(completion.Task, "Reading", _ => { }, () => hidden = true,
            TimeSpan.FromMilliseconds(10));
        await Task.Delay(200);
        completion.SetException(new IOException("failed"));

        // assert
        await Assert.ThrowsAsync<IOException>(() => listing);
        Assert.True(hidden);
    }
}
