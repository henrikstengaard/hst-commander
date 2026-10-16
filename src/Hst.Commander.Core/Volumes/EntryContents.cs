namespace Hst.Commander.Core.Volumes;

/// <summary>
/// Size and number of entries of the entries shown in the properties dialog, counted by
/// <see cref="VolumeContents.CountContentsAsync" /> or for local files by the file system.
/// </summary>
/// <param name="Size">Total size of the items in bytes, also items within containers.</param>
/// <param name="SizeOnDisk">Space the items use on the disk in bytes, null when unknown (not a local file on Windows).</param>
/// <param name="Files">Number of items counted.</param>
/// <param name="Folders">Number of containers counted.</param>
/// <param name="IsComplete">False while still counting.</param>
public record EntryContents(long Size, long? SizeOnDisk, int Files, int Folders, bool IsComplete);
