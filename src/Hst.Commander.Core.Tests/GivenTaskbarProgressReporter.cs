namespace Hst.Commander.Core.Tests;

public class GivenTaskbarProgressReporter
{
    private readonly RecordingTaskbarProgress _taskbarProgress = new();

    [Fact]
    public void When_Started_Then_IndeterminateIsShown()
    {
        // arrange
        using var reporter = new TaskbarProgressReporter(_taskbarProgress);

        // act
        reporter.Start();

        // assert
        Assert.Equal([(TaskbarProgressState.Indeterminate, 0.0)], _taskbarProgress.Calls);
    }

    [Fact]
    public void When_ReportingWithinTheSamePercent_Then_ProgressIsShownOnce()
    {
        // arrange
        using var reporter = new TaskbarProgressReporter(_taskbarProgress);

        // act
        reporter.Report(0.101);
        reporter.Report(0.102);
        reporter.Report(0.104);
        reporter.Report(0.11);

        // assert
        Assert.Equal([(TaskbarProgressState.Normal, 0.1), (TaskbarProgressState.Normal, 0.11)],
            _taskbarProgress.Calls);
    }

    [Fact]
    public void When_Paused_Then_LaterProgressIsShownAsPaused()
    {
        // arrange
        using var reporter = new TaskbarProgressReporter(_taskbarProgress);
        reporter.Report(0.25);

        // act
        reporter.Pause();
        reporter.Report(0.5);

        // assert
        Assert.Equal(
        [
            (TaskbarProgressState.Normal, 0.25), (TaskbarProgressState.Paused, 0.25),
            (TaskbarProgressState.Paused, 0.5)
        ], _taskbarProgress.Calls);
    }

    [Fact]
    public void When_Disposed_Then_ProgressIsClearedOnceAndNothingIsShownAfterwards()
    {
        // arrange
        var reporter = new TaskbarProgressReporter(_taskbarProgress);
        reporter.Report(0.5);

        // act
        reporter.Dispose();
        reporter.Dispose();
        reporter.Report(0.75);

        // assert
        Assert.Equal([(TaskbarProgressState.Normal, 0.5), (TaskbarProgressState.None, 0.0)],
            _taskbarProgress.Calls);
    }

    [Fact]
    public void When_DisposedWithoutProgress_Then_NothingIsShown()
    {
        // act
        new TaskbarProgressReporter(_taskbarProgress).Dispose();

        // assert
        Assert.Empty(_taskbarProgress.Calls);
    }

    [Fact]
    public void When_ReportingOutOfRange_Then_FractionIsClamped()
    {
        // arrange
        using var reporter = new TaskbarProgressReporter(_taskbarProgress);

        // act
        reporter.Report(1.5);
        reporter.Report(double.NaN);

        // assert
        Assert.Equal([(TaskbarProgressState.Normal, 1.0), (TaskbarProgressState.Normal, 0.0)],
            _taskbarProgress.Calls);
    }

    [Fact]
    public void When_UsingNoTaskbarProgress_Then_NothingThrows()
    {
        // arrange
        var reporter = new TaskbarProgressReporter(null);

        // act
        reporter.Start();
        reporter.Report(0.5);
        reporter.Pause();
        reporter.Dispose();
    }

    private sealed class RecordingTaskbarProgress : ITaskbarProgress
    {
        public List<(TaskbarProgressState State, double Fraction)> Calls { get; } = [];

        public void Set(TaskbarProgressState state, double fraction) => Calls.Add((state, fraction));
    }
}
