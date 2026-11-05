namespace Hst.Commander.Core;

/// <summary>
///     Shows the progress of a file operation outside the window, e.g. on the Windows taskbar button, the macOS Dock
///     tile or the terminal's tab. Implemented by the front-ends and driven by <see cref="TaskbarProgressReporter" />.
///     Called on the UI thread.
/// </summary>
public interface ITaskbarProgress
{
    /// <summary>
    ///     Shows a state with a fraction from 0 to 1, which is ignored for <see cref="TaskbarProgressState.None" /> and
    ///     <see cref="TaskbarProgressState.Indeterminate" />.
    /// </summary>
    void Set(TaskbarProgressState state, double fraction);
}
