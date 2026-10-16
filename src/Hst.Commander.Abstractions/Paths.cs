namespace Hst.Commander.Abstractions;

/// <summary>
/// Special path components used with <see cref="IVolume.ChangeDirectory" />.
/// </summary>
public static class Paths
{
    /// <summary>Root of a volume, e.g. changing to [Root, "dir1"] changes to "dir1" from the root.</summary>
    public static string Root => "/";

    /// <summary>Parent of the current directory.</summary>
    public static string Parent => "..";
}
