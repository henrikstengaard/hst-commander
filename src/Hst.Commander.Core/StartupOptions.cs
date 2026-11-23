using Hst.Commander.Abstractions;

namespace Hst.Commander.Core;

/// <summary>
///     Where the panes start, given on the command line, e.g. when restarted as administrator (see
///     <see cref="Elevation" />): "--left-volume", "--left-path", "--right-volume" and "--right-path" with the name of a
///     volume provider and a location (<see cref="NavigationContext.ChangeLocationAsync" />), and "--active" with "left"
///     or "right". "--restore-panes" (passed by the Start menu, desktop and app menu shortcuts) or the environment
///     variable <see cref="LaunchedFromAppVariable" /> (set by the macOS app bundle) tells it was started from a
///     shortcut, not a terminal (see <see cref="WithSavedPanes" />). Unknown arguments are ignored.
/// </summary>
public sealed record StartupOptions
{
    /// <summary>Argument of the shortcuts starting Hst Commander, e.g. in the Start menu or a Linux .desktop file.</summary>
    public const string RestorePanesOption = "--restore-panes";

    /// <summary>
    ///     Environment variable set to "1" by the macOS app bundle (LSEnvironment in Info.plist), as Finder and the Dock
    ///     pass no arguments.
    /// </summary>
    public const string LaunchedFromAppVariable = "HSTC_LAUNCHED_FROM_APP";

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

    /// <summary>
    ///     True when started from a shortcut (Start menu, desktop, app menu, Finder or the Dock), false when started from
    ///     a terminal or command prompt.
    /// </summary>
    public bool IsLaunchedFromShortcut { get; init; }

    /// <summary>True when a pane's volume provider or location is given, e.g. when restarted as administrator.</summary>
    public bool HasPaneOptions => !Left.IsEmpty || !Right.IsEmpty;

    /// <summary>
    ///     Parses the command line arguments, reading <see cref="LaunchedFromAppVariable" /> with
    ///     <paramref name="getEnvironmentVariable" /> (the process's environment by default).
    /// </summary>
    public static StartupOptions Parse(IReadOnlyList<string> args,
        Func<string, string?>? getEnvironmentVariable = null)
    {
        ArgumentNullException.ThrowIfNull(args);
        getEnvironmentVariable ??= Environment.GetEnvironmentVariable;

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
            IsRightActive = isRightActive,
            IsLaunchedFromShortcut = args.Contains(RestorePanesOption, StringComparer.OrdinalIgnoreCase) ||
                                     getEnvironmentVariable(LaunchedFromAppVariable) == "1"
        };
    }

    /// <summary>
    ///     Options starting the panes where they were when Hst Commander was last quit (see <see cref="PaneLocations" />),
    ///     unless panes are given (<see cref="HasPaneOptions" />, e.g. when restarted as administrator), which always
    ///     win:
    ///     <list type="bullet">
    ///         <item>started from a shortcut, both panes are restored, and the right pane is active when it was;</item>
    ///         <item>
    ///             started from a terminal, the left pane starts in the current directory (where it was started) and
    ///             the right pane is restored.
    ///         </item>
    ///     </list>
    ///     A pane that was on a volume provider connected to a location (e.g. an SMB server, saved without a location
    ///     as it needs the password) starts in the home directory, and so does a pane started from a shortcut with
    ///     nothing saved, instead of the working directory a shortcut gives (e.g. the install directory).
    /// </summary>
    /// <param name="saved">Pane locations saved when last quit, null when none were saved.</param>
    /// <param name="homeDirectory">Home directory of the user (see <see cref="NavigationContext.HomeDirectory" />).</param>
    public StartupOptions WithSavedPanes(PaneLocations? saved, string? homeDirectory)
    {
        if (HasPaneOptions)
        {
            return this;
        }

        var home = new PaneStartup(null, homeDirectory);
        var unchanged = new PaneStartup(null, null);
        PaneStartup Restore(PaneStartup? pane, PaneStartup fallback) => pane switch
        {
            null => fallback,
            { IsEmpty: true } => home,
            _ => pane
        };

        return this with
        {
            Left = IsLaunchedFromShortcut ? Restore(saved?.Left, home) : unchanged,
            Right = Restore(saved?.Right, IsLaunchedFromShortcut ? home : unchanged),
            IsRightActive = IsLaunchedFromShortcut && saved?.IsRightActive == true
        };
    }

    /// <summary>
    ///     Directory the panes are created in before the options are applied, where a pane stays when its location
    ///     can't be opened: the home directory when started from a shortcut (whose working directory is e.g. the
    ///     install directory), otherwise the current directory.
    /// </summary>
    public string GetInitialDirectory(string currentDirectory, string? homeDirectory) =>
        IsLaunchedFromShortcut && homeDirectory != null ? homeDirectory : currentDirectory;

    /// <summary>
    ///     Options starting the panes where they are: their volume providers (except Local) and current paths. A pane
    ///     changing to another volume provider starts at its root instead.
    /// </summary>
    /// <param name="left">Navigation context of the left pane.</param>
    /// <param name="right">Navigation context of the right pane.</param>
    /// <param name="isRightActive">True when the right pane is active.</param>
    /// <param name="changedPaneIsRight">The pane changing volume provider, true for the right pane.</param>
    /// <param name="volumeProvider">Volume provider the pane changes to, null when no pane changes.</param>
    /// <param name="path">Location in the volume provider the pane changes to, e.g. of a bookmark, null for its start.</param>
    public static StartupOptions FromPanes(NavigationContext left, NavigationContext right, bool isRightActive,
        bool changedPaneIsRight = false, IVolumeProvider? volumeProvider = null, string? path = null)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);

        return new StartupOptions
        {
            Left = volumeProvider != null && !changedPaneIsRight
                ? new PaneStartup(volumeProvider.Name, path)
                : PaneStartup.From(left),
            Right = volumeProvider != null && changedPaneIsRight
                ? new PaneStartup(volumeProvider.Name, path)
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
    /// <summary>True when neither a volume provider nor a location is given, so the pane isn't changed.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsEmpty => VolumeProviderName == null && Path == null;

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

/// <summary>
///     Where the panes were when Hst Commander was quit, saved in the settings of each front-end and restored on the
///     next start (see <see cref="StartupOptions.WithSavedPanes" />).
/// </summary>
/// <param name="Left">Where the left pane was (see <see cref="PaneStartup.From" />).</param>
/// <param name="Right">Where the right pane was.</param>
/// <param name="IsRightActive">True when the right pane was active.</param>
public sealed record PaneLocations(PaneStartup Left, PaneStartup Right, bool IsRightActive)
{
    /// <summary>Where the panes are now, to be saved when quitting.</summary>
    public static PaneLocations From(NavigationContext left, NavigationContext right, bool isRightActive) =>
        new(PaneStartup.From(left), PaneStartup.From(right), isRightActive);
}
