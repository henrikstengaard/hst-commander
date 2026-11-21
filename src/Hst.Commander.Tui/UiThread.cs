namespace Hst.Commander.Tui;

/// <summary>
///     Runs actions on the UI thread in the next iteration of the main loop, like <c>Application.Invoke</c> did in
///     earlier Terminal.Gui versions. Terminal.Gui v2's <c>Application.Invoke</c> runs an action at once when called on
///     the UI thread, so a dialog shown by it would run inside the caller (e.g. the main loop's error handler) and an
///     exception would escape the caller instead of reaching the main loop's error handler.
/// </summary>
public static class UiThread
{
    /// <summary>Runs an action on the UI thread in the next iteration of the main loop.</summary>
    public static void Post(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);

        Application.AddTimeout(TimeSpan.Zero, () =>
        {
            action();
            return false;
        });
    }

    /// <summary>
    ///     Runs an action on the UI thread like <see cref="Post" /> unless the TUI is shut down, e.g. while a program
    ///     runs in the terminal or the output screen is shown (Ctrl+O). For actions that can be skipped then, e.g.
    ///     checks a timer repeats.
    /// </summary>
    public static void PostIfRunning(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);

        if (Application.Initialized)
        {
            Post(action);
        }
    }
}
