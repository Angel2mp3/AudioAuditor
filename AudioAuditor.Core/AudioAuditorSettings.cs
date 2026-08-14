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

    /// <summary>
    /// When true, online lyric providers that return obviously censored results
    /// (e.g. lots of asterisks/hashes covering profanity) are skipped in favor
    /// of the next provider. Mirrors ThemeManager.LyricsAvoidCensored.
    /// </summary>
    public static bool AvoidCensoredLyrics { get; set; } = false;

    /// <summary>
    /// When true, swallowed exceptions are appended to the local crash log.
    /// Mirrors ThemeManager.CrashLoggingEnabled.
    /// </summary>
    public static bool CrashLoggingEnabled { get; set; } = true;

    /// <summary>
    /// When true, listening stats are recorded for the Wrapped summary.
    /// Mirrors ThemeManager.StatsCollectionEnabled.
    /// </summary>
    public static bool StatsCollectionEnabled { get; set; } = false;

    /// <summary>
    /// Name of the active colour theme, recorded in crash reports so a theming bug
    /// can be reproduced. Mirrors ThemeManager.CurrentTheme.
    /// </summary>
    public static string? CurrentThemeName { get; set; }

    // ── Discord Rich Presence ──
    // The presence service reads these directly rather than taking them per call: Enable(),
    // UpdatePresence() and the album-art lookup each need a different subset, and threading
    // five parameters through every one of them buys nothing.

    /// <summary>Application ID the presence connects as. Mirrors ThemeManager.DiscordRpcClientId.</summary>
    public static string DiscordRpcClientId { get; set; } = "";

    /// <summary>What the status line shows. Mirrors ThemeManager.DiscordRpcDisplayMode.</summary>
    /// <remarks>Defaults match the WPF properties, whose field initializers bypass the mirror.</remarks>
    public static string DiscordRpcDisplayMode { get; set; } = "TrackDetails";

    /// <summary>Whether to show elapsed time. Mirrors ThemeManager.DiscordRpcShowElapsed.</summary>
    public static bool DiscordRpcShowElapsed { get; set; } = true;

    /// <summary>Used for the album-art lookup. Mirrors ThemeManager.LastFmApiKey.</summary>
    public static string LastFmApiKey { get; set; } = "";
}
