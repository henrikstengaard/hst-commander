using Terminal.Gui;

namespace Hst.Commander.Tui;

/// <summary>
///     A framed spinner with a message, e.g. "Reading Physical disks...", shown in the middle of a pane while it's
///     listed when listing takes long (see <see cref="Core.ListingProgress" />).
/// </summary>
public sealed class ListingProgressView : View
{
    private readonly SpinnerView _spinner;
    private readonly Label _label;

    public ListingProgressView()
    {
        X = Pos.Center();
        Y = Pos.Center();
        Height = 3;
        BorderStyle = LineStyle.Single;
        Visible = false;
        _spinner = new SpinnerView { X = 1, Y = 0 };
        _label = new Label { X = 3, Y = 0 };
        Add(_spinner, _label);
    }

    public void Show(string message)
    {
        _label.Text = message;
        Width = message.Length + 6;
        _spinner.AutoSpin = true;
        Visible = true;
        SuperView?.SetNeedsLayout();
        Application.LayoutAndDraw();
    }

    public void Hide()
    {
        _spinner.AutoSpin = false;
        Visible = false;
        SuperView?.SetNeedsLayout();
        Application.LayoutAndDraw(true);
    }
}
