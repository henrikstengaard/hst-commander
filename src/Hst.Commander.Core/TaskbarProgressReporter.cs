namespace Hst.Commander.Core;

/// <summary>
///     Shows the progress of a file operation outside the window with an <see cref="ITaskbarProgress" />, e.g. while a
///     progress dialog is open. Only changes of the state or of the whole percent are passed on, as progress is
///     reported far more often than it can be seen and redrawing it isn't free (e.g. the macOS Dock tile, or escape
///     sequences written to the terminal). Disposing clears the progress. Called on the UI thread.
/// </summary>
public sealed class TaskbarProgressReporter(ITaskbarProgress? taskbarProgress) : IDisposable
{
    private TaskbarProgressState _state = TaskbarProgressState.None;
    private int _percent = -1;
    private bool _paused;
    private bool _cleared;

    /// <summary>Shows busy without a fraction until progress is reported.</summary>
    public void Start() => Set(TaskbarProgressState.Indeterminate, 0);

    /// <summary>Shows how much is done, from 0 to 1, paused once <see cref="Pause" /> has been called.</summary>
    public void Report(double fraction) =>
        Set(_paused ? TaskbarProgressState.Paused : TaskbarProgressState.Normal, fraction);

    /// <summary>Shows the progress as paused from now on, e.g. while cancelling.</summary>
    public void Pause()
    {
        _paused = true;
        if (_state != TaskbarProgressState.None)
        {
            Set(TaskbarProgressState.Paused, _percent < 0 ? 0 : _percent / 100.0);
        }
    }

    /// <summary>Hides the progress; nothing is shown afterwards.</summary>
    public void Clear()
    {
        if (_cleared)
        {
            return;
        }

        if (_state != TaskbarProgressState.None)
        {
            taskbarProgress?.Set(TaskbarProgressState.None, 0);
            _state = TaskbarProgressState.None;
        }

        _cleared = true;
    }

    public void Dispose() => Clear();

    private void Set(TaskbarProgressState state, double fraction)
    {
        if (_cleared)
        {
            return;
        }

        var percent = state is TaskbarProgressState.None or TaskbarProgressState.Indeterminate
            ? -1
            : (int)Math.Round(Math.Clamp(double.IsNaN(fraction) ? 0 : fraction, 0, 1) * 100);
        if (state == _state && percent == _percent)
        {
            return;
        }

        _state = state;
        _percent = percent;
        taskbarProgress?.Set(state, percent < 0 ? 0 : percent / 100.0);
    }
}
