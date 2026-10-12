namespace Hst.Commander.Core;

/// <summary>
///     Locations of the settings files used by the front-ends.
/// </summary>
public static class SettingsPaths
{
    /// <summary>Settings directory <c>.hst-commander</c> in the user's home directory.</summary>
    public static string Directory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".hst-commander");
}
