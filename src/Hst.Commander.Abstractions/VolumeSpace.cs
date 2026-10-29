namespace Hst.Commander.Abstractions;

/// <summary>
/// Free and total space in bytes of the file system of a volume (see <see cref="IVolume.GetSpace" />), shown by Hst
/// Commander as "free/total" next to the path like local drives, e.g. "87G/256G".
/// </summary>
/// <param name="Free">Free space in bytes.</param>
/// <param name="Total">Total space in bytes.</param>
public sealed record VolumeSpace(long Free, long Total);
