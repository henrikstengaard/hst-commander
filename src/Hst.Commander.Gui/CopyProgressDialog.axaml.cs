using Avalonia.Controls;
using Avalonia.Threading;
using Hst.Commander.Core.Volumes;

namespace Hst.Commander.Gui;

/// <summary>
///     Modal dialog running a copy or move in the background and showing its progress with progress bars for the current file
///     and in total, the copy speed and estimated time left. Existing files are confirmed with
///     <see cref="OverwriteDialog" />. Cancel (or closing the window) cancels the copy; the dialog closes when the
///     copy has stopped and <see cref="Exception" /> holds the error, if any.
/// </summary>
public partial class CopyProgressDialog : Window
{
    private readonly CancellationTokenSource _cancellationTokenSource = new();
    private bool _completed;

    public CopyProgressDialog() : this((_, _, _) => Task.CompletedTask)
    {
    }

    /// <param name="copy">
    ///     Copy (or move) to run with the progress, the overwrite confirmation and the cancellation token of the dialog.
    /// </param>
    /// <param name="title">Title of the dialog, e.g. "Move".</param>
    public CopyProgressDialog(
        Func<IProgress<CopyProgress>, Func<OverwriteRequest, CancellationToken, Task<OverwriteAnswer>>,
            CancellationToken, Task> copy, string title = "Copy")
    {
        InitializeComponent();

        Title = title;
        CancelButton.Click += (_, _) => Cancel();
        Closing += (_, e) =>
        {
            if (!_completed)
            {
                e.Cancel = true;
                Cancel();
            }
        };
        Closed += (_, _) => _cancellationTokenSource.Dispose();
        Opened += async (_, _) =>
        {
            CancelButton.Focus();

            // progress created on the UI thread reports on the UI thread
            var progress = new Progress<CopyProgress>(Update);
            var cancellationToken = _cancellationTokenSource.Token;
            try
            {
                await Task.Run(() => copy(progress, ConfirmOverwriteAsync, cancellationToken), cancellationToken);
            }
            catch (Exception exception)
            {
                Exception = exception;
            }

            _completed = true;
            Close();
        };
    }

    /// <summary>Exception the copy failed with or was cancelled with, null when the copy succeeded.</summary>
    public Exception? Exception { get; private set; }

    /// <summary>Asks on the UI thread whether to overwrite an existing file. Closing the dialog answers no.</summary>
    private Task<OverwriteAnswer> ConfirmOverwriteAsync(OverwriteRequest request, CancellationToken _) =>
        Dispatcher.UIThread.InvokeAsync(async () =>
            await new OverwriteDialog(request.Message).ShowDialog<OverwriteAnswer?>(this) ?? OverwriteAnswer.No);

    private void Update(CopyProgress progress)
    {
        if (_completed)
        {
            return;
        }

        // cached writes of a disk being written, e.g. of a physical disk, take the place of the current file
        if (progress.DiskWrite is { } diskWrite)
        {
            CurrentText.Text = diskWrite.Text;
            CurrentProgressBar.Value = diskWrite.Fraction;
            return;
        }

        if (!_cancellationTokenSource.IsCancellationRequested)
        {
            CurrentText.Text = progress.CurrentName;
        }

        CurrentProgressBar.Value = progress.CurrentFraction;
        TotalText.Text = $"Total {progress.TotalText}";
        TotalProgressBar.Value = progress.TotalFraction;
        SpeedText.Text = $"{progress.SpeedText}, ETA {progress.EtaText}";
    }

    private void Cancel()
    {
        if (_completed || _cancellationTokenSource.IsCancellationRequested)
        {
            return;
        }

        _cancellationTokenSource.Cancel();
        CancelButton.IsEnabled = false;
        CurrentText.Text = "Cancelling...";
    }
}
