using System;
using System.Reflection;

namespace AudioQualityChecker.Services;

/// <summary>
/// The app's version, read once from the Core assembly. Services that identify themselves to a
/// remote API use <see cref="UserAgent"/> so a release bump touches only the .csproj files —
/// these strings used to be hardcoded per service and drifted several versions apart.
/// </summary>
public static class AppVersion
{
    private static readonly Lazy<string> _display = new(() =>
    {
        var version = typeof(AppVersion).Assembly.GetName().Version;
        // Trim the .0 revision MSBuild appends so "2.0.0.0" reads as "2.0.0".
        return version == null
            ? "0.0.0"
            : $"{version.Major}.{version.Minor}.{version.Build}";
    });

    /// <summary>Version as "major.minor.patch", e.g. "2.0.0".</summary>
    public static string Display => _display.Value;

    /// <summary>
    /// A User-Agent string for outbound HTTP. MusicBrainz and friends reject or throttle clients
    /// that don't identify themselves, so <paramref name="component"/> names the caller.
    /// </summary>
    public static string UserAgent(string component) =>
        $"AudioAuditor/{Display} ({component})";
}
