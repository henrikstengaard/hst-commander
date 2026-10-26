using System.Collections.Concurrent;

namespace Hst.Commander.Core.Tests;

/// <summary>
/// Synchronization context running continuations on one thread, like the UI thread of a front-end, to test that
/// nothing blocks the thread while waiting for a continuation queued to it.
/// </summary>
public sealed class SingleThreadSynchronizationContext : SynchronizationContext
{
    private readonly BlockingCollection<(SendOrPostCallback Callback, object? State)> _queue = new();

    public override void Post(SendOrPostCallback d, object? state) => _queue.Add((d, state));

    public override void Send(SendOrPostCallback d, object? state) =>
        throw new NotSupportedException("Send is not supported");

    /// <summary>
    /// Runs an operation on a thread of its own with this context, throwing <see cref="TimeoutException" /> when it
    /// hasn't completed in time, e.g. a deadlock.
    /// </summary>
    public static void Run(Func<Task> operation, TimeSpan timeout)
    {
        var context = new SingleThreadSynchronizationContext();
        Exception? exception = null;
        var thread = new Thread(() =>
        {
            SetSynchronizationContext(context);
            try
            {
                var task = operation();
                task.ContinueWith(_ => context._queue.CompleteAdding(), TaskScheduler.Default);
                foreach (var (callback, state) in context._queue.GetConsumingEnumerable())
                {
                    callback(state);
                }

                task.GetAwaiter().GetResult();
            }
            catch (Exception e)
            {
                exception = e;
            }
        }) { IsBackground = true };

        thread.Start();
        if (!thread.Join(timeout))
        {
            throw new TimeoutException($"The operation didn't complete within {timeout}, e.g. a deadlock");
        }

        if (exception != null)
        {
            throw exception;
        }
    }
}
