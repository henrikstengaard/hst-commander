namespace Hst.Commander.Core;

/// <summary>State of the progress shown outside the window, see <see cref="ITaskbarProgress" />.</summary>
public enum TaskbarProgressState
{
    /// <summary>No progress shown.</summary>
    None,

    /// <summary>Busy without a known fraction.</summary>
    Indeterminate,

    /// <summary>Progress with a fraction.</summary>
    Normal,

    /// <summary>Progress with a fraction, paused, e.g. while cancelling (yellow on the Windows taskbar).</summary>
    Paused,

    /// <summary>Progress with a fraction that failed (red on the Windows taskbar).</summary>
    Error
}
