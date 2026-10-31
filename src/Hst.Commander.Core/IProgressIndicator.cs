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

    /// <summary>Shows how much is done, from 0 to 1.</summary>
    void Report(double fraction);

    /// <summary>Hides the progress.</summary>
    void Hide();
}
