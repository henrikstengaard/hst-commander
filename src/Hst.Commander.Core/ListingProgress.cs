using Hst.Commander.Abstractions;

namespace Hst.Commander.Core;

/// <summary>
///     Progress shown by the front-ends while a pane is listed, only when listing takes longer than <see cref="Delay" />,
///     e.g. the physical disks while a sleeping disk wakes up, so quick listings don't flicker.
/// </summary>
public static class ListingProgress
{
    /// <summary>How long listing takes before the progress is shown.</summary>
    public static readonly TimeSpan Delay = TimeSpan.FromSeconds(2);

    /// <summary>Text shown with the progress, e.g. "Reading Physical disks...".</summary>
    public static string GetMessage(NavigationContext navigationContext)
    {
        ArgumentNullException.ThrowIfNull(navigationContext);

        return $"Reading {navigationContext.CurrentPath}...";
    }

    /// <summary>
    ///     Lists the entries of a navigation context, calling <paramref name="show" /> with the message when it takes
    ///     longer than <see cref="Delay" /> and <paramref name="hide" /> when it's done. Called on the UI thread, so both
    ///     are called on it.
    /// </summary>
    public static Task<IReadOnlyList<IEntry>> ListEntriesAsync(NavigationContext navigationContext,
        Action<string> show, Action hide, CancellationToken cancellationToken = default) =>
        RunAsync(navigationContext.ListEntriesAsync(cancellationToken), GetMessage(navigationContext), show, hide,
            Delay);

    /// <summary>Waits for a task, calling show when it takes longer than the delay and hide when it's done.</summary>
    public static async Task<T> RunAsync<T>(Task<T> task, string message, Action<string> show, Action hide,
        TimeSpan delay)
    {
        ArgumentNullException.ThrowIfNull(task);
        ArgumentNullException.ThrowIfNull(show);
        ArgumentNullException.ThrowIfNull(hide);

        if (task.IsCompleted || await Task.WhenAny(task, Task.Delay(delay)) == task)
        {
            return await task;
        }

        show(message);
        try
        {
            return await task;
        }
        finally
        {
            hide();
        }
    }
}
