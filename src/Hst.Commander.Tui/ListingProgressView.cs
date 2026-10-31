using Hst.Commander.Core;
using Terminal.Gui;

namespace Hst.Commander.Tui;

/// <summary>
///     A framed spinner with a message, e.g. "Reading Physical disks...", shown in the middle of a pane while it's
///     listed when listing takes long (see <see cref="Core.ListingProgress" />). Shown with a progress bar and a
///     cancel button while the pane makes a temporary copy that takes long (see
///     <see cref="NavigationContext.ProgressIndicator" />); the cancel button has the focus until hidden.
/// </summary>
public sealed class ListingProgressView : View, IProgressIndicator
{
    private const int ProgressWidth = 40;

    private readonly SpinnerView _spinner;
    private readonly Label _label;
    private readonly ProgressBar _progressBar;
    private readonly Button _cancelButton;
    private Action? _cancel;

    public ListingProgressView()
    {
        X = Pos.Center();
        Y = Pos.Center();
        Height = 3;
        BorderStyle = LineStyle.Single;
        Visible = false;
        _spinner = new SpinnerView { X = 1, Y = 0 };
        _label = new Label { X = 3, Y = 0 };
        _progressBar = new ProgressBar
        {
            X = 1,
            Y = 1,
            Width = Dim.Fill(1),
            Height = 1,
            Visible = false,
            ProgressBarStyle = ProgressBarStyle.Continuous,
            ProgressBarFormat = ProgressBarFormat.SimplePlusPercentage
        };
        _cancelButton = new Button { X = Pos.Center(), Y = 2, Text = "Cancel", Visible = false };
        _cancelButton.Accepting += (_, e) =>
        {
            e.Cancel = true;
            Cancel();
        };
        Add(_spinner, _label, _progressBar, _cancelButton);
    }

    /// <summary>View getting the focus back when the progress with a cancel button is hidden, e.g. the pane's table.</summary>
    public View? FocusOnHide { get; set; }

    /// <summary>Shows the spinner with a message, e.g. while listing.</summary>
    public void Show(string message)
    {
        _cancel = null;
        _progressBar.Visible = false;
        _cancelButton.Visible = false;
        Height = 3;
        ShowMessage(message, message.Length + 6);
    }

    public void Show(string message, Action cancel)
    {
        _cancel = cancel;
        _progressBar.Fraction = 0;
        _progressBar.Visible = true;
        _cancelButton.Visible = true;
        _cancelButton.Enabled = true;
        Height = 5;
        ShowMessage(message, Math.Max(ProgressWidth, message.Length + 6));
        _cancelButton.SetFocus();
    }

    public void Report(double fraction)
    {
        if (!Visible || _cancel == null)
        {
            return;
        }

        _progressBar.Fraction = (float)Math.Clamp(fraction, 0, 1);
        Application.LayoutAndDraw();
    }

    public void Hide()
    {
        var hadCancel = _cancel != null;
        _cancel = null;
        _spinner.AutoSpin = false;
        _progressBar.Visible = false;
        _cancelButton.Visible = false;
        Visible = false;
        SuperView?.SetNeedsLayout();
        if (hadCancel)
        {
            FocusOnHide?.SetFocus();
        }

        Application.LayoutAndDraw(true);
    }

    private void ShowMessage(string message, int width)
    {
        _label.Text = message;
        Width = width;
        _spinner.AutoSpin = true;
        Visible = true;
        SuperView?.SetNeedsLayout();
        Application.LayoutAndDraw();
    }

    private void Cancel()
    {
        if (_cancel == null || !_cancelButton.Enabled)
        {
            return;
        }

        _cancelButton.Enabled = false;
        _label.Text = "Cancelling...";
        _cancel();
    }
}
