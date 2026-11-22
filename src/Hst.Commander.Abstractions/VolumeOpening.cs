using System.Diagnostics;

namespace Hst.Commander.Abstractions;

/// <summary>
///     Progress of opening a file as a volume (<see cref="IFileVolumeProvider.CreateVolume" />): Hst Commander creates
///     volumes on a background thread inside <see cref="RunAsync{T}" />, shows the number of entries read when opening
///     takes long, e.g. an archive with many files, and can cancel it. A volume reading its entries when created reports
///     each one with <see cref="ReportEntryRead" />; <see cref="ArchiveVolume{TArchiveEntry}" /> does it in
///     <c>AddEntry</c>.
/// </summary>
public static class VolumeOpening
{
    private static readonly TimeSpan ReportInterval = TimeSpan.FromMilliseconds(100);
    private static readonly AsyncLocal<Session?> CurrentSession = new();

    /// <summary>
    ///     Reports an entry read while opening a volume and throws <see cref="OperationCanceledException" /> when
    ///     opening is cancelled. Does nothing outside <see cref="RunAsync{T}" />, e.g. when a volume is opened again for
    ///     copying.
    /// </summary>
    public static void ReportEntryRead()
    {
        if (CurrentSession.Value is { } session)
        {
            session.EntryRead();
        }
    }

    /// <summary>
    ///     Runs <paramref name="open" /> (e.g. <see cref="IFileVolumeProvider.CreateVolume" />) reporting the number of
    ///     entries read to <paramref name="entriesRead" /> at most every 100 ms. Used by Hst Commander.
    /// </summary>
    public static async Task<T> RunAsync<T>(Func<Task<T>> open, IProgress<long>? entriesRead,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(open);

        // set in this async method, so it's restored when the method returns
        CurrentSession.Value = new Session(entriesRead, cancellationToken);
        return await open();
    }

    private sealed class Session(IProgress<long>? entriesRead, CancellationToken cancellationToken)
    {
        private readonly Stopwatch _sinceReport = Stopwatch.StartNew();
        private long _count;

        public void EntryRead()
        {
            cancellationToken.ThrowIfCancellationRequested();
            _count++;
            if (entriesRead != null && _sinceReport.Elapsed >= ReportInterval)
            {
                entriesRead.Report(_count);
                _sinceReport.Restart();
            }
        }
    }
}
