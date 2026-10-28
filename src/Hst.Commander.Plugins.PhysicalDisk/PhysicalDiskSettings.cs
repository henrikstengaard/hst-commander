using System.Text.Json;

namespace Hst.Commander.Plugins.PhysicalDisk;

/// <summary>
/// Settings of the PhysicalDisk plugin, read from "settings.json" in the plugin's directory, e.g.
/// "~/.hst-commander/plugins/physicaldisk/settings.json" with <c>{ "CacheSize": 64 }</c>.
/// </summary>
public sealed record PhysicalDiskSettings
{
    /// <summary>Default size of the cache of each disk opened in bytes.</summary>
    public const long DefaultCacheSize = 32L * 1024 * 1024;

    /// <summary>Name of the settings file in the plugin's directory.</summary>
    public const string FileName = "settings.json";

    /// <summary>Most megabytes cached for each disk opened (<see cref="CachedDiskStream" />), 0 to not cache.</summary>
    public int CacheSize { get; init; } = (int)(DefaultCacheSize / (1024 * 1024));

    public long CacheSizeInBytes => Math.Max(0, CacheSize) * 1024L * 1024;

    /// <summary>Settings read from the plugin's directory, the defaults when there's no settings file or it's invalid.</summary>
    public static PhysicalDiskSettings Load(string? pluginDirectory)
    {
        if (pluginDirectory == null)
        {
            return new PhysicalDiskSettings();
        }

        try
        {
            using var stream = File.OpenRead(Path.Combine(pluginDirectory, FileName));
            return JsonSerializer.Deserialize<PhysicalDiskSettings>(stream,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new PhysicalDiskSettings();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return new PhysicalDiskSettings();
        }
    }
}
