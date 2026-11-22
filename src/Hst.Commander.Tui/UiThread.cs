using System.Collections.Concurrent;

namespace Hst.Commander.Tui;

/// <summary>
///     Runs actions on the UI thread at the start of the next iteration of the main loop, from any thread, and is the
///     UI thread's synchronization context, so <c>await</c> continues on it.
/// </summary>
/// <remarks>
///     Terminal.Gui's own ways, <c>Application.AddTimeout</c> and <c>Application.Invoke</c> (also used by its
///     synchronization context), can't be used from other threads: Terminal.Gui 2.5 holds the lock of its timeouts
///     while it runs a timeout, and when a timeout shows a dialog (e.g. an <c>await</c> continuing with
///     <c>Application.Run</c> after reading a file asynchronously, as on Linux and macOS), the lock is held until the
///     dialog closes. Adding a timeout from another thread then waits until the dialog closes, so the dialog's progress
///     (e.g. the size counted in the properties dialog) is never shown. Actions are queued instead and run by the main
///     loop's <c>Iteration</c> event, which is raised outside the lock in every iteration, also of the loops of
///     dialogs. <c>Application.Invoke</c> also runs an action at once when called on the UI thread, so a dialog shown
///     by it would run inside the caller.
/// </remarks>
public static class UiThread
{
    private static readonly ConcurrentQueue<Action> Pending = new();
    private static readonly SynchronizationContext Context = new UiSynchronizationContext();
    private static object? _attachedApplication;
    private static int _threadId;

    /// <summary>
    ///     Runs the queued actions on this thread's main loop and makes it the synchronization context of the thread,
    ///     also of the sessions of the dialogs, as <c>Application.Run</c> installs Terminal.Gui's. Called after each
    ///     <c>Application.Init</c>.
    /// </summary>
    public static void Attach()
    {
        _threadId = Environment.CurrentManagedThreadId;
        var application = Application.Instance;
        if (!ReferenceEquals(application, _attachedApplication))
        {
            _attachedApplication = application;
            application.Iteration += (_, _) => RunPending();
            // a session installs Terminal.Gui's synchronization context before raising SessionBegun, and restores the
            // previous one when it ends
            application.SessionBegun += (_, _) => SynchronizationContext.SetSynchronizationContext(Context);
        }

        SynchronizationContext.SetSynchronizationContext(Context);
    }

    /// <summary>Runs an action on the UI thread at the start of the next iteration of the main loop.</summary>
    public static void Post(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);

        Pending.Enqueue(action);
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

    /// <summary>
    ///     Runs the actions queued before this iteration one at a time in the order posted; actions posted meanwhile run
    ///     in the next iteration, after the input, so frequent progress reports taking long to show can't keep the main
    ///     loop from handling keys (e.g. Cancel). An action showing a dialog runs the following actions in the dialog's
    ///     main loop; an exception reaches the main loop's error handler and the other actions run in the next
    ///     iteration.
    /// </summary>
    private static void RunPending()
    {
        for (var count = Pending.Count; count > 0 && Pending.TryDequeue(out var action); count--)
        {
            action();
        }
    }

    private sealed class UiSynchronizationContext : SynchronizationContext
    {
        public override void Post(SendOrPostCallback d, object? state) => UiThread.Post(() => d(state));

        public override void Send(SendOrPostCallback d, object? state)
        {
            if (Environment.CurrentManagedThreadId == _threadId)
            {
                d(state);
                return;
            }

            using var done = new ManualResetEventSlim();
            Exception? exception = null;
            UiThread.Post(() =>
            {
                try
                {
                    d(state);
                }
                catch (Exception sendException)
                {
                    exception = sendException;
                }
                finally
                {
                    done.Set();
                }
            });
            done.Wait();
            if (exception != null)
            {
                throw new System.Reflection.TargetInvocationException(exception);
            }
        }

        public override SynchronizationContext CreateCopy() => this;
    }
}
