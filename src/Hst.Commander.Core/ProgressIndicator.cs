namespace Hst.Commander.Core;

/// <summary>
///     Progress shown by a front-end in a pane, e.g. while making a temporary copy of a large image inside another
///     image (see <see cref="NavigationContext.ProgressIndicator" />). Called on the UI thread.
/// </summary>
/// <param name="Show">Shows the progress with a message.</param>
/// <param name="Hide">Hides the progress.</param>
public sealed record ProgressIndicator(Action<string> Show, Action Hide);
