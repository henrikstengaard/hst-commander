using Hst.Commander.Core.Volumes;
using Terminal.Gui;

namespace Hst.Commander.Tui;

/// <summary>
///     Modal dialog showing the progress of a delete with the entry being deleted and a total progress bar. Cancel (or
///     Esc) cancels the delete through <see cref="CancellationToken" />; the dialog stays open until the delete has
///     stopped and <see cref="Complete" /> is called.
/// </summary>
public sealed class DeleteProgressDialog : Dialog
{
    private readonly CancellationTokenSource _cancellationTokenSource = new();
    private readonly Label _currentLabel;
    private readonly Label _totalLabel;
    private readonly ProgressBar _totalProgressBar;
    private readonly Button _cancelButton;
    private bool _completed;

    public DeleteProgressDialog(TuiPalette palette)
    {
        Title = "Delete";
        Width = Dim.Percent(70);
        Height = 9;
        ColorScheme = palette.DialogColorScheme;

        _currentLabel = new Label { X = 1, Y = 1, Width = Dim.Fill(1), Text = "Preparing..." };
        _totalLabel = new Label { X = 1, Y = 2, Width = Dim.Fill(1), Text = "Total" };
        _totalProgressBar = new ProgressBar
        {
            X = 1,
            Y = 3,
            Width = Dim.Fill(1),
            Height = 1,
            ProgressBarStyle = ProgressBarStyle.Continuous,
            ProgressBarFormat = ProgressBarFormat.SimplePlusPercentage
        };

        _cancelButton = new Button { Text = "Cancel", IsDefault = true };
        _cancelButton.Accepting += (_, e) =>
        {
            e.Cancel = true;
            Cancel();
        };
        Closing += (_, e) =>
        {
            if (_completed)
            {
                return;
            }

            e.Cancel = true;
            Cancel();
        };

        Add(_currentLabel, _totalLabel, _totalProgressBar);
        AddButton(_cancelButton);
        Initialized += (_, _) => _cancelButton.SetFocus();
    }

    public CancellationToken CancellationToken => _cancellationTokenSource.Token;

    /// <summary>Shows the delete progress. Must be called on the UI thread.</summary>
    public void Update(DeleteProgress progress)
    {
        if (_completed)
        {
            return;
        }

        // cached writes of a disk being written, e.g. of a physical disk, take the place of the current entry
        if (progress.DiskWrite is { } diskWrite)
        {
            _currentLabel.Text = $"{diskWrite.Text} ({diskWrite.Fraction:P0})";
            return;
        }

        if (!_cancellationTokenSource.IsCancellationRequested)
        {
            _currentLabel.Text = progress.CurrentName;
        }

        _totalLabel.Text = $"Total {progress.TotalText}";
        _totalProgressBar.Fraction = (float)progress.TotalFraction;
    }

    /// <summary>Closes the dialog when the delete has stopped. Must be called on the UI thread.</summary>
    public void Complete()
    {
        _completed = true;
        Application.RequestStop(this);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _cancellationTokenSource.Dispose();
        }

        base.Dispose(disposing);
    }

    private void Cancel()
    {
        if (_cancellationTokenSource.IsCancellationRequested)
        {
            return;
        }

        _cancellationTokenSource.Cancel();
        _cancelButton.Enabled = false;
        _currentLabel.Text = "Cancelling...";
    }
}
