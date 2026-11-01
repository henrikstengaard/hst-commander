using Hst.Commander.Abstractions;

namespace Hst.Commander.Core;

/// <summary>
///     Where the panes start, given on the command line, e.g. when restarted as administrator (see
///     <see cref="Elevation" />): "--left-volume", "--left-path", "--right-volume" and "--right-path" with the name of a
///     volume provider and a location (<see cref="NavigationContext.ChangeLocationAsync" />), and "--active" with "left"
///     or "right". Unknown arguments are ignored.
/// </summary>
public sealed record StartupOptions
{
    private const string LeftVolumeOption = "--left-volume";
    private const string LeftPathOption = "--left-path";
    private const string RightVolumeOption = "--right-volume";
    private const string RightPathOption = "--right-path";
    private const string ActiveOption = "--active";
    private const string RightValue = "right";

    /// <summary>Where the left pane starts.</summary>
    public PaneStartup Left { get; init; } = new(null, null);

    /// <summary>Where the right pane starts.</summary>
    public PaneStartup Right { get; init; } = new(null, null);

    /// <summary>True when the right pane is the active pane, otherwise the left pane is.</summary>
    public bool IsRightActive { get; init; }

    public static StartupOptions Parse(IReadOnlyList<string> args)
    {
        ArgumentNullException.ThrowIfNull(args);

        string? leftVolume = null, leftPath = null, rightVolume = null, rightPath = null;
        var isRightActive = false;
        for (var index = 0; index + 1 < args.Count; index++)
        {
            var value = args[index + 1];
            switch (args[index])
            {
                case LeftVolumeOption:
                    leftVolume = value;
                    break;
                case LeftPathOption:
                    leftPath = value;
                    break;
                case RightVolumeOption:
                    rightVolume = value;
                    break;
                case RightPathOption:
                    rightPath = value;
                    break;
                case ActiveOption:
                    isRightActive = value.Equals(RightValue, StringComparison.OrdinalIgnoreCase);
                    break;
                default:
                    continue;
            }

            index++;
        }

        return new StartupOptions
        {
            Left = new PaneStartup(leftVolume, leftPath),
            Right = new PaneStartup(rightVolume, rightPath),
            IsRightActive = isRightActive
        };
    }

    /// <summary>
    ///     Options starting the panes where they are: their volume providers (except Local) and current paths. A pane
    ///     changing to another volume provider starts at its root instead.
    /// </summary>
    /// <param name="left">Navigation context of the left pane.</param>
    /// <param name="right">Navigation context of the right pane.</param>
    /// <param name="isRightActive">True when the right pane is active.</param>
    /// <param name="changedPaneIsRight">The pane changing volume provider, true for the right pane.</param>
    /// <param name="volumeProvider">Volume provider the pane changes to, null when no pane changes.</param>
    public static StartupOptions FromPanes(NavigationContext left, NavigationContext right, bool isRightActive,
        bool changedPaneIsRight = false, IVolumeProvider? volumeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);

        return new StartupOptions
        {
            Left = volumeProvider != null && !changedPaneIsRight
                ? new PaneStartup(volumeProvider.Name, null)
                : PaneStartup.From(left),
            Right = volumeProvider != null && changedPaneIsRight
                ? new PaneStartup(volumeProvider.Name, null)
                : PaneStartup.From(right),
            IsRightActive = isRightActive
        };
    }

    /// <summary>The options as command line arguments, read again with <see cref="Parse" />.</summary>
    public IReadOnlyList<string> ToArguments()
    {
        var arguments = new List<string>();
        void Add(string option, string? value)
        {
            if (value != null)
            {
                arguments.AddRange([option, value]);
            }
        }

        Add(LeftVolumeOption, Left.VolumeProviderName);
        Add(LeftPathOption, Left.Path);
        Add(RightVolumeOption, Right.VolumeProviderName);
        Add(RightPathOption, Right.Path);
        Add(ActiveOption, IsRightActive ? RightValue : null);
        return arguments;
    }
}

/// <summary>Where a pane starts: a volume provider by name (null for Local) and a location in it (null for its start).</summary>
public sealed record PaneStartup(string? VolumeProviderName, string? Path)
{
    /// <summary>
    ///     Where a pane is: its volume provider, unless Local, and its current path. A volume provider connected to a
    ///     location (e.g. an SMB server) isn't listed and needs the credentials entered, so the pane starts locally.
    /// </summary>
    public static PaneStartup From(NavigationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (context.VolumeProvider == VolumeProviders.Local)
        {
            return new PaneStartup(null, context.CurrentPath);
        }

        return VolumeProviders.All.Contains(context.VolumeProvider)
            ? new PaneStartup(context.VolumeProvider.Name, context.CurrentPath)
            : new PaneStartup(null, null);
    }

    /// <summary>
    ///     Changes a pane's navigation context to the volume provider and location. A volume provider that doesn't
    ///     exist (e.g. of a plugin not loaded) or a location that can't be opened is skipped, so the pane still starts.
    /// </summary>
    public async Task ApplyAsync(NavigationContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        try
        {
            if (VolumeProviderName != null && VolumeProviders.All.FirstOrDefault(provider =>
                    provider.Name.Equals(VolumeProviderName, StringComparison.OrdinalIgnoreCase)) is { } provider)
            {
                await context.ChangeVolumeProviderAsync(provider, cancellationToken);
            }

            if (Path != null)
            {
                await context.ChangeLocationAsync(Path, cancellationToken);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
                                              or NotSupportedException)
        {
        }
    }
}
