namespace AudioQualityChecker;

/// <summary>
/// Shared runtime settings accessible by Core services without coupling to ThemeManager.
/// Set by ThemeManager on load and whenever settings change.
/// </summary>
public static class AudioAuditorSettings
{
    /// <summary>
    /// When true, all network calls inside Core services are suppressed.
    /// Mirrors ThemeManager.OfflineModeEnabled.
    /// </summary>
    public static bool OfflineMode { get; set; } = false;
}
