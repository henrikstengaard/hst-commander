namespace Hst.Commander.Core;

/// <summary>
///     Progress shown by a front-end in a pane, e.g. while making a temporary copy of a large image inside another
///     image (see <see cref="NavigationContext.ProgressIndicator" />). Called on the UI thread.
/// </summary>
public interface IProgressIndicator
{
    /// <summary>
    ///     Shows the progress with a message, a progress bar and a cancel button calling <paramref name="cancel" />.
    /// </summary>
    void Show(string message, Action cancel);

    /// <summary>
    ///     Shows the progress with a message, an indeterminate progress bar (or spinner) and a cancel button calling
    ///     <paramref name="cancel" />, e.g. while opening a large archive, whose message is changed with
    ///     <see cref="ReportMessage" />.
    /// </summary>
    void ShowIndeterminate(string message, Action cancel);

    /// <summary>Changes the message of the progress shown, e.g. the number of entries read.</summary>
    void ReportMessage(string message);

    /// <summary>Shows how much is done, from 0 to 1.</summary>
    void Report(double fraction);

    /// <summary>Hides the progress.</summary>
    void Hide();
}
