using System.Globalization;
using System.Reflection;

namespace Hst.Commander.Core;

/// <summary>
/// Name, author, version, description and build date of the application, read from the assembly attributes set by
/// Directory.Build.props and Directory.Build.targets.
/// </summary>
/// <param name="Description">Short description of Hst Commander, shown in Help > About.</param>
/// <param name="BuildDate">When the app was built (UTC), null when the assembly doesn't have it.</param>
public record AppInfo(string Name, string Author, string Version, string Description = "",
    DateTimeOffset? BuildDate = null)
{
    /// <summary>Key of the <see cref="AssemblyMetadataAttribute" /> with the build date, set by Directory.Build.targets.</summary>
    public const string BuildDateKey = "BuildDate";

    public static AppInfo Current { get; } = FromAssembly(Assembly.GetEntryAssembly() ?? typeof(AppInfo).Assembly);

    public static AppInfo FromAssembly(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        var buildDate = assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(attribute => attribute.Key == BuildDateKey)?.Value;
        return new AppInfo(
            assembly.GetCustomAttribute<AssemblyProductAttribute>()?.Product ?? assembly.GetName().Name ?? string.Empty,
            assembly.GetCustomAttribute<AssemblyCompanyAttribute>()?.Company ?? string.Empty,
            assembly.GetName().Version?.ToString(3) ?? "0.0.0",
            assembly.GetCustomAttribute<AssemblyDescriptionAttribute>()?.Description ?? string.Empty,
            DateTimeOffset.TryParse(buildDate, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var date)
                ? date
                : null);
    }

    public string NameAndVersion => $"{Name} v{Version}";

    /// <summary>Build date shown in Help > About in local time, e.g. <c>Built 2026-11-01 21:15</c>, or empty.</summary>
    public string BuildDateText => BuildDate is { } date
        ? $"Built {date.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)}"
        : string.Empty;

    public string AboutText => $"{NameAndVersion}\n\nCreated by {Author}";

    /// <summary>Question confirming quitting the application, e.g. <c>Do you want to quit Hst Commander?</c></summary>
    public string QuitQuestion => $"Do you want to quit {Name}?";
}
