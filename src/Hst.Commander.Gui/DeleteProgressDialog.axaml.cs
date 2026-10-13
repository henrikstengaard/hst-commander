using Avalonia.Controls;
using Hst.Commander.Core.Volumes;

namespace Hst.Commander.Gui;

/// <summary>
///     Modal dialog running a delete in the background and showing its progress with the entry being deleted and a
///     total progress bar. Cancel (or closing the window) cancels the delete; the dialog closes when the delete has
///     stopped and <see cref="Exception" /> holds the error, if any.
/// </summary>
public partial class DeleteProgressDialog : Window
{
    private readonly CancellationTokenSource _cancellationTokenSource = new();
    private bool _completed;

    public DeleteProgressDialog() : this((_, _) => Task.CompletedTask)
    {
    }

    /// <param name="delete">Delete to run with the progress and the cancellation token of the dialog.</param>
    public DeleteProgressDialog(Func<IProgress<DeleteProgress>, CancellationToken, Task> delete)
    {
        InitializeComponent();

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
            var progress = new Progress<DeleteProgress>(Update);
            var cancellationToken = _cancellationTokenSource.Token;
            try
            {
                await Task.Run(() => delete(progress, cancellationToken), cancellationToken);
            }
            catch (Exception exception)
            {
                Exception = exception;
            }

            _completed = true;
            Close();
        };
    }

    /// <summary>Exception the delete failed with or was cancelled with, null when the delete succeeded.</summary>
    public Exception? Exception { get; private set; }

    private void Update(DeleteProgress progress)
    {
        if (_completed)
        {
            return;
        }

        if (!_cancellationTokenSource.IsCancellationRequested)
        {
            CurrentText.Text = progress.CurrentName;
        }

        TotalText.Text = $"Total {progress.TotalText}";
        TotalProgressBar.Value = progress.TotalFraction;
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
