using Hst.Commander.Core.Volumes;
using Terminal.Gui;

namespace Hst.Commander.Tui;

/// <summary>
///     Modal dialog showing the progress of a copy or move with progress bars for the current file and in total, the copy
///     speed and estimated time left. Cancel (or Esc) cancels the copy through <see cref="CancellationToken" />; the
///     dialog stays open until the copy has stopped and <see cref="Complete" /> is called.
/// </summary>
public sealed class CopyProgressDialog : Dialog
{
    private readonly CancellationTokenSource _cancellationTokenSource = new();
    private readonly Label _currentLabel;
    private readonly ProgressBar _currentProgressBar;
    private readonly Label _totalLabel;
    private readonly ProgressBar _totalProgressBar;
    private readonly Label _speedLabel;
    private readonly Button _cancelButton;
    private bool _completed;

    public CopyProgressDialog(TuiPalette palette, string title = "Copy")
    {
        Title = title;
        Width = Dim.Percent(70);
        Height = 13;
        ColorScheme = palette.DialogColorScheme;

        _currentLabel = new Label { X = 1, Y = 1, Width = Dim.Fill(1), Text = "Preparing..." };
        _currentProgressBar = CreateProgressBar(2);
        _totalLabel = new Label { X = 1, Y = 4, Width = Dim.Fill(1), Text = "Total" };
        _totalProgressBar = CreateProgressBar(5);
        _speedLabel = new Label { X = 1, Y = 7, Width = Dim.Fill(1), Text = string.Empty };

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

        Add(_currentLabel, _currentProgressBar, _totalLabel, _totalProgressBar, _speedLabel);
        AddButton(_cancelButton);
        Initialized += (_, _) => _cancelButton.SetFocus();
    }

    public CancellationToken CancellationToken => _cancellationTokenSource.Token;

    /// <summary>Shows the copy progress. Must be called on the UI thread.</summary>
    public void Update(CopyProgress progress)
    {
        if (_completed)
        {
            return;
        }

        // cached writes of a disk being written, e.g. of a physical disk, take the place of the current file
        if (progress.DiskWrite is { } diskWrite)
        {
            _currentLabel.Text = diskWrite.Text;
            _currentProgressBar.Fraction = (float)diskWrite.Fraction;
            _speedLabel.Text = $"{diskWrite.SpeedText}, ETA {diskWrite.EtaText}";
            return;
        }

        if (!_cancellationTokenSource.IsCancellationRequested)
        {
            _currentLabel.Text = progress.CurrentName;
        }

        _currentProgressBar.Fraction = (float)progress.CurrentFraction;
        _totalLabel.Text = $"Total {progress.TotalText}";
        _totalProgressBar.Fraction = (float)progress.TotalFraction;
        _speedLabel.Text = $"{progress.SpeedText}, ETA {progress.EtaText}";
    }

    /// <summary>Closes the dialog when the copy has stopped. Must be called on the UI thread.</summary>
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

    private static ProgressBar CreateProgressBar(int y) => new()
    {
        X = 1,
        Y = y,
        Width = Dim.Fill(1),
        Height = 1,
        ProgressBarStyle = ProgressBarStyle.Continuous,
        ProgressBarFormat = ProgressBarFormat.SimplePlusPercentage
    };
}
