using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using AudioQualityChecker.Models;

// NOTICE: Embedding malicious code in theme/settings files or their loading
// routines is still malware distribution and a criminal offense.
// Legitimate forks: rename assemblies in .csproj to disable integrity checks.

namespace AudioQualityChecker.Services
{
    public enum LoopMode { Off, All, One }

    /// <summary>
    /// Animation pattern for the Now Playing cover glow.
    /// Swirl rotates the gradient endpoints around the cover (legacy behavior).
    /// LinearLR / LinearRL sweep the gradient horizontally.
    /// Random eases between hues sampled from the extracted palette.
    /// </summary>
    public enum GlowMotionMode { Swirl, LinearLR, LinearRL, Random, DiagonalSweep, Orbit, ColorDrift }

    public enum PlaybarAnimationStyle
    {
        Regular,
        Wave
    }

    public static partial class ThemeManager
    {
        private static readonly string SettingsDir =
            ResolveSettingsDir();
        private static readonly string ThemeFile = Path.Combine(SettingsDir, "theme.txt");
        private static readonly string OptionsFile = Path.Combine(SettingsDir, "options.txt");
        private static readonly string SensitiveFile = ResolveSensitiveFile();

        public static readonly List<string> AvailableThemes = new() { "Dark", "Ocean", "Light", "Amethyst", "Dreamsicle", "Goldenrod", "Emerald", "Blurple", "Crimson", "Brown" };
        public static readonly List<string> AvailablePlaybarThemes = new() { "Follow Theme", "Blue Fire", "Neon Pulse", "Sunset Glow", "Purple Haze", "Minimal", "Golden Wave", "Emerald Wave", "Blurple Wave", "Crimson Wave", "Brown Wave", "Rainbow Bars" };

        public static readonly List<string> AvailableMusicServices = new()
        {
            "Spotify", "YouTube Music", "Tidal", "Qobuz", "Amazon Music",
            "Apple Music", "Deezer", "SoundCloud", "Bandcamp", "Last.fm", "Custom..."
        };

        private static string _currentTheme = "Blurple";
        public static string CurrentTheme => _currentTheme;

        private static string _currentPlaybarTheme = ""; // empty = follow color theme
        public static string CurrentPlaybarTheme => string.IsNullOrEmpty(_currentPlaybarTheme) ? ResolveFollowPlaybarTheme() : _currentPlaybarTheme;
        public static bool IsPlaybarFollowingTheme => string.IsNullOrEmpty(_currentPlaybarTheme);

        // All 6 configurable music service slots
        public static string[] MusicServiceSlots { get; } = new string[6];

        // Visibility toggles for each slot
        public static bool[] MusicServiceSlotVisible { get; } = new bool[6] { true, true, true, true, true, true };

        // Play Options
        private static int _crossfadeDuration = 5;
        public static bool AutoPlayNext { get; set; } = true;
        public static bool AudioNormalization { get; set; }
        public static bool Crossfade { get; set; }
        public static int CrossfadeDuration
        {
            get => _crossfadeDuration;
            set => _crossfadeDuration = Math.Clamp(value, 1, 30);
        }
        public static CrossfadeType CrossfadeCurve { get; set; } = CrossfadeType.EqualPower;
        public static bool CrossfadeOnManualSkip { get; set; } = false;
        public static bool GaplessEnabled { get; set; }
        public static PlaybarAnimationStyle MainPlaybarAnimationStyle { get; set; } = PlaybarAnimationStyle.Regular;
        public static PlaybarAnimationStyle NpPlaybarAnimationStyle { get; set; } = PlaybarAnimationStyle.Regular;

        // Loop mode: Off, All (loop playlist), One (loop single track)
        public static LoopMode LoopMode { get; set; } = LoopMode.Off;

        // Auto-update check: silently checks GitHub on startup (on by default)
        public static bool CheckForUpdates { get; set; } = true;

        public static IReadOnlyList<string> GetAvailableThemeNames()
        {
            return AvailableThemes
                .Concat(CustomThemeStore.LoadThemes().Select(t => t.Name))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        private static string ResolveSettingsDir()
        {
            var overrideDir = Environment.GetEnvironmentVariable("AUDIOAUDITOR_SETTINGS_DIR");
            if (!string.IsNullOrWhiteSpace(overrideDir))
            {
                try { return Path.GetFullPath(Environment.ExpandEnvironmentVariables(overrideDir)); }
                catch { }
            }

            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AudioAuditor");
        }

        private static string ResolveSensitiveFile()
        {
            var overrideFile = Environment.GetEnvironmentVariable("AUDIOAUDITOR_SENSITIVE_FILE");
            if (!string.IsNullOrWhiteSpace(overrideFile))
            {
                try { return Path.GetFullPath(Environment.ExpandEnvironmentVariables(overrideFile)); }
                catch { }
            }

            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                "AudioAuditor",
                "session.dat");
        }

        public static IReadOnlyList<CustomThemeDefinition> GetCustomThemes() => CustomThemeStore.LoadThemes();

        public static CustomThemeDefinition? GetThemeDefinition(string themeName)
        {
            return CustomThemeStore.FindTheme(themeName);
        }

        // Custom service settings (for Custom... slots — 6 slots)
        public static string[] CustomServiceUrls { get; } = new string[6] { "", "", "", "", "", "" };
        public static string[] CustomServiceIcons { get; } = new string[6] { "", "", "", "", "", "" };

        // Streaming service region settings
        public static bool RegionAwareSearchEnabled { get; set; } = true;
        public static string StreamingRegion { get; set; } = "us";

        // Equalizer
        public static bool EqualizerEnabled { get; set; }
        public static float[] EqualizerGains { get; set; } = new float[10]; // 10 bands

        // Discord RPC + scrobbling — see ThemeManager.Scrobbling.cs

        // Export format
        public static string ExportFormat { get; set; } = "csv";

        // Spatial Audio
        public static bool SpatialAudioEnabled { get; set; }

        // Experimental AI Detection (spectral analysis — opt-in, higher false positives)
        public static bool ExperimentalAiDetection { get; set; }

        // Rip/Encode Quality Check (experimental — opt-in)
        public static bool RipQualityEnabled { get; set; }

        private const string CurrentScanPerformanceDefaultsVersion = "1.7.0-fast-scan-columns";

        // Fast scan is the default. Full-file detectors are opt-in because they decode
        // a large portion of every track and can make library scans painfully slow.
        public static bool SilenceDetectionEnabled { get; set; }
        public static bool FakeStereoDetectionEnabled { get; set; } = true;
        public static bool DynamicRangeEnabled { get; set; }
        public static bool TruePeakEnabled { get; set; }
        public static bool LufsEnabled { get; set; }
        public static bool ClippingDetectionEnabled { get; set; } = true;
        public static bool MqaDetectionEnabled { get; set; } = true;
        public static bool DefaultAiDetectionEnabled { get; set; } = true;
        public static bool BpmDetectionEnabled { get; set; }
        public static string ScanPerformanceDefaultsVersion { get; set; } = "";

        // SH Labs AI Detection (API-based — opt-in, uses rate-limited proxy)
        public static bool SHLabsAiDetection { get; set; }

        // SH Labs privacy notice accepted — must be true before SH Labs can be used
        public static bool SHLabsPrivacyAccepted { get; set; }

        // User's own SH Labs API key (bypasses proxy, no rate limits, stored locally)
        public static string SHLabsCustomApiKey { get; set; } = "";

        // AI Detection config popup dismissed — shown once to new/upgrading users
        public static bool AiConfigDismissed { get; set; }

        // Feature config popup version — tracks which version's popup has been shown.
        // Compared against app version; shown once per major version update.
        public static string FeatureConfigVersion { get; set; } = "";

        // Welcome dialog version — tracks which version's welcome screen the user has seen.
        // Shown on first install and again on version updates.
        public static string WelcomeVersionSeen { get; set; } = "";

        // Visualizer full-volume mode: renders visualizer as if volume is at 100%
        public static bool VisualizerFullVolume { get; set; } = true;

        // Persisted volume slider value (0–100). Restored on startup.
        public static double Volume { get; set; } = 100.0;

        // Silence detection fine-tuning (all off by default)
        public static bool SilenceMinGapEnabled { get; set; }
        public static double SilenceMinGapSeconds { get; set; } = 0.5;
        public static bool SilenceSkipEdgesEnabled { get; set; }
        public static double SilenceSkipEdgeSeconds { get; set; } = 5.0;

        // Crash logging — ON by default. The first-run/upgrade Welcome dialog lets the
        // user opt out; Settings has a toggle too. Logs are local-only and path-sanitized.
        public static bool CrashLoggingEnabled { get; set; } = true;

        // Local stats collection — OFF by default, user must explicitly opt in
        public static bool StatsCollectionEnabled { get; set; }

        // Always run full audio file pass even when all detectors are disabled
        public static bool AlwaysFullAnalysis { get; set; }

        // Spectrogram export quality settings (off by default)
        public static bool SpectrogramHiFiMode { get; set; }
        public static bool SpectrogramMagmaColormap { get; set; }
        public static int LastSettingsTab { get; set; }

        // Frequency cutoff allow-listing: files with cutoff >= threshold won't be flagged
        public static bool FrequencyCutoffAllowEnabled { get; set; }       // default false
        public static int FrequencyCutoffAllowHz { get; set; } = 19600;    // default 19,600 Hz

        // UI animations — decorative animations (glow motion, playbar pulse, lyric transitions)
        // On by default to preserve the standard UI; visualizer and waveform are unaffected.
        public static bool AnimationsEnabled { get; set; } = true;

        // Scan cache — remember previously analyzed files
        public static bool ScanCacheEnabled { get; set; }
        public static bool FocusNewlyAddedFilesEnabled { get; set; } = true;

        // Restore last session — when ON, the app remembers which files/folders were
        // loaded and offers to repopulate them on next launch. Pairs with ScanCacheEnabled
        // (turning this on auto-enables that, with a one-time popup).
        public static bool RestoreLastSessionEnabled { get; set; }

        // One-time flag: have we shown the "we also turned scan cache on" popup yet?
        public static bool RestoreSessionCacheNoticeShown { get; set; }

        // NP panel preferences — see ThemeManager.NowPlaying.cs


        // Donation popup dismissed — never show again once dismissed
        public static bool DonationDismissed { get; set; }

        // 30-day usage-based donation popup — shown once after 30 days of actual use
        public static bool Donation30DayShown { get; set; }
        public static bool FeedbackOneHourShown { get; set; }
        public static double FeedbackActiveUsageSeconds { get; set; }
        public static DateTime FirstScanDate { get; set; }
        public static int TotalFilesScannedLifetime { get; set; }
        public static double TotalListeningSecondsLifetime { get; set; }

        // Footer support link dismissed — never show again
        public static bool FooterSupportDismissed { get; set; }

        // Close to system tray instead of exiting (off by default)
        public static bool CloseToTray { get; set; }

        // Preload next track data (cover, colors, lyrics) in background for faster transitions
        public static bool PreloadNextTrackEnabled { get; set; } = true;

        // ─── File operation defaults ───
        public static int RenamePatternIndex { get; set; }
        public static int SmartRenameStyleIndex { get; set; }
        public static int SmartRenameFolderIndex { get; set; }
        public static bool SmartRenameIncludeTrackNumbers { get; set; } = true;
        public static bool SmartRenameAppendDuplicateNumbers { get; set; }
        public static bool SmartRenameRenameCleanFiles { get; set; }
        public static string DefaultCopyFolder { get; set; } = "";
        public static string DefaultMoveFolder { get; set; } = "";
        public static string DefaultPlaylistFolder { get; set; } = "";

        // ─── Main window color match ───
        public static bool MainColorMatchEnabled { get; set; }

        // ─── Offline / online mode ───
        private static bool _offlineModeEnabled;
        public static bool OfflineModeEnabled
        {
            get => _offlineModeEnabled;
            set
            {
                _offlineModeEnabled = value;
                // Keep the Core shim in sync so services don't need a ThemeManager reference
                AudioQualityChecker.AudioAuditorSettings.OfflineMode = value;
            }
        }

        // ─── Avoid censored lyrics (auto-fallback to next provider when result is censored) ───
        private static bool _lyricsAvoidCensored;
        public static bool LyricsAvoidCensored
        {
            get => _lyricsAvoidCensored;
            set
            {
                _lyricsAvoidCensored = value;
                AudioQualityChecker.AudioAuditorSettings.AvoidCensoredLyrics = value;
            }
        }

        // Whether the user has seen the first-launch online/offline dialog (persisted in Registry)
        public static bool FirstLaunchComplete { get; set; }

        // Registry key path for cross-install persistence
        private const string RegistryKeyPath = @"Software\AudioAuditor";

        /// <summary>Write a flag to the Windows registry so it survives reinstalls.</summary>
        public static void SetRegistryFlag(string name, bool value)
        {
            try
            {
                using var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(RegistryKeyPath);
                key?.SetValue(name, value ? 1 : 0, Microsoft.Win32.RegistryValueKind.DWord);
            }
            catch { }
        }

        /// <summary>Read a flag from the Windows registry.</summary>
        public static bool GetRegistryFlag(string name)
        {
            try
            {
                using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(RegistryKeyPath);
                return key?.GetValue(name) is int i && i != 0;
            }
            catch { return false; }
        }

        // DataGrid column layout — serialized as Header:DisplayIndex:Width;...
        public static string ColumnLayout { get; set; } = "";

        public static readonly string[] ColumnHeaderOrder =
        {
            "★", "Status", "Title", "Artist", "Filename", "Path", "Sample Rate", "Bits", "Ch",
            "Duration", "Size", "Bitrate", "Actual BR", "Format", "Max Freq", "Clipping", "BPM",
            "Replay Gain", "DR", "MQA", "AI", "Fake Stereo", "Silence", "Date Modified",
            "Date Created", "True Peak", "LUFS", "Rip Quality"
        };

        private static readonly string[] DefaultHiddenColumnHeaders =
        {
            "★", "BPM", "DR", "Date Created", "True Peak", "LUFS", "Rip Quality", "Silence"
        };

        private static readonly HashSet<string> AnalysisColumnHeaders = new(StringComparer.OrdinalIgnoreCase)
        {
            "BPM", "DR", "True Peak", "LUFS", "Rip Quality", "Silence",
            "Clipping", "MQA", "AI", "Fake Stereo"
        };

        private const int MinimumUsableVisibleColumns = 4;

        public static string DefaultHiddenColumns => string.Join(",", DefaultHiddenColumnHeaders);

        // Hidden columns — comma-separated canonical column headers that are permanently hidden
        public static string HiddenColumns { get; set; } = DefaultHiddenColumns;

        // Default-hidden, non-analysis columns the user has explicitly chosen to SHOW.
        //
        // Such columns (★, "Date Created", …) live in DefaultHiddenColumnHeaders but have no
        // feature flag to anchor them, so encoding "shown" merely as absence from HiddenColumns
        // is fragile: every default re-application (EnsureUsableColumnSet, applyDefaultHidden-
        // Columns, the scan-defaults migration, etc.) re-adds the whole default set and silently
        // re-hides them. Recording the user's choice here makes it survive — for EVERY such
        // column, not just ★. The user's choice always overrides the default.
        public static HashSet<string> UserShownColumns { get; } = new(StringComparer.OrdinalIgnoreCase);

        // Back-compat alias: the ★ favorites column is just one entry in UserShownColumns.
        // Kept so existing call sites / saved-file keys keep working.
        public static bool ShowFavoritesColumn
        {
            get => UserShownColumns.Contains("★");
            set { if (value) UserShownColumns.Add("★"); else UserShownColumns.Remove("★"); }
        }

        // Default-hidden columns that have NO feature flag (★, Date Created). Visibility for
        // these is owned entirely by the user's UserShownColumns choice.
        private static IEnumerable<string> FlaglessDefaultHiddenHeaders =>
            DefaultHiddenColumnHeaders.Where(h => !AnalysisColumnHeaders.Contains(h));

        // Records (or clears) a user's explicit "show this default-hidden column" choice.
        public static void SetColumnUserShown(string header, bool shown)
        {
            var normalized = NormalizeColumnHeader(header);
            if (shown) UserShownColumns.Add(normalized);
            else UserShownColumns.Remove(normalized);
        }

        // Replaces the whole UserShownColumns set from a persisted comma-separated list.
        public static void SetUserShownColumns(string value)
        {
            UserShownColumns.Clear();
            if (string.IsNullOrWhiteSpace(value)) return;
            foreach (var item in value.Split(',', StringSplitOptions.RemoveEmptyEntries))
            {
                var header = NormalizeColumnHeader(item);
                if (!string.IsNullOrWhiteSpace(header))
                    UserShownColumns.Add(header);
            }
        }

        public static string FormatUserShownColumns() =>
            string.Join(",", UserShownColumns.OrderBy(h => h, StringComparer.OrdinalIgnoreCase));

        // Marks every flagless default-hidden column (★, Date Created) as user-shown — used by
        // the "Show All Columns" action so those opt-in columns are revealed with the rest.
        public static void ShowAllFlaglessDefaultColumns()
        {
            foreach (var header in FlaglessDefaultHiddenHeaders)
                UserShownColumns.Add(header);
        }

        // Legacy fallback for saved files written before UserShownColumns existed: a flagless
        // default-hidden column that is ABSENT from HiddenColumns was being shown by the user.
        // An explicit UserShownColumns= line, when present, overrides this afterwards.
        internal static void DeriveUserShownColumnsFromHidden(string hiddenCsv)
        {
            var hidden = ParseHiddenColumns(hiddenCsv);
            foreach (var header in FlaglessDefaultHiddenHeaders)
            {
                if (hidden.Contains(header)) UserShownColumns.Remove(header);
                else UserShownColumns.Add(header);
            }
        }

        public static string NormalizeColumnHeader(string header)
        {
            var normalized = (header ?? "").Trim();
            return normalized.Equals("File Name", StringComparison.OrdinalIgnoreCase) ? "Filename" : normalized;
        }

        public static HashSet<string> GetHiddenColumnSet()
        {
            var hidden = ParseHiddenColumns(HiddenColumns);

            foreach (var header in AnalysisColumnHeaders)
            {
                if (IsAnalysisColumnEnabled(header))
                    hidden.Remove(header);
                else
                    hidden.Add(header);
            }

            EnsureUsableColumnSet(hidden);
            ApplyUserShownColumnPreferences(hidden);
            return hidden;
        }

        // Re-asserts the user's explicit choice for every flagless default-hidden column AFTER
        // any default re-application, so user choice always wins: shown → unhide, not shown →
        // keep hidden. This generalizes the old ★-only flag to ALL such columns.
        private static void ApplyUserShownColumnPreferences(HashSet<string> hidden)
        {
            foreach (var header in FlaglessDefaultHiddenHeaders)
            {
                if (UserShownColumns.Contains(header))
                    hidden.Remove(header);
                else
                    hidden.Add(header);
            }
        }

        public static bool IsAnalysisColumn(string header) =>
            AnalysisColumnHeaders.Contains(NormalizeColumnHeader(header));

        public static bool IsAnalysisColumnEnabled(string header)
        {
            return NormalizeColumnHeader(header) switch
            {
                "BPM" => BpmDetectionEnabled,
                "DR" => DynamicRangeEnabled,
                "True Peak" => TruePeakEnabled,
                "LUFS" => LufsEnabled,
                "Rip Quality" => RipQualityEnabled,
                "Silence" => SilenceDetectionEnabled,
                "Clipping" => ClippingDetectionEnabled,
                "MQA" => MqaDetectionEnabled,
                "AI" => DefaultAiDetectionEnabled,
                "Fake Stereo" => FakeStereoDetectionEnabled,
                _ => true
            };
        }

        public static void SetAnalysisColumnEnabled(string header, bool enabled)
        {
            switch (NormalizeColumnHeader(header))
            {
                case "BPM":
                    BpmDetectionEnabled = enabled;
                    AudioAnalyzer.EnableBpmDetection = enabled;
                    break;
                case "DR":
                    DynamicRangeEnabled = enabled;
                    AudioAnalyzer.EnableDynamicRange = enabled;
                    break;
                case "True Peak":
                    TruePeakEnabled = enabled;
                    AudioAnalyzer.EnableTruePeak = enabled;
                    break;
                case "LUFS":
                    LufsEnabled = enabled;
                    AudioAnalyzer.EnableLufs = enabled;
                    break;
                case "Rip Quality":
                    RipQualityEnabled = enabled;
                    AudioAnalyzer.EnableRipQuality = enabled;
                    break;
                case "Silence":
                    SilenceDetectionEnabled = enabled;
                    AudioAnalyzer.EnableSilenceDetection = enabled;
                    break;
                case "Clipping":
                    ClippingDetectionEnabled = enabled;
                    AudioAnalyzer.EnableClippingDetection = enabled;
                    break;
                case "MQA":
                    MqaDetectionEnabled = enabled;
                    AudioAnalyzer.EnableMqaDetection = enabled;
                    break;
                case "AI":
                    DefaultAiDetectionEnabled = enabled;
                    AudioAnalyzer.EnableDefaultAiDetection = enabled;
                    break;
                case "Fake Stereo":
                    FakeStereoDetectionEnabled = enabled;
                    AudioAnalyzer.EnableFakeStereoDetection = enabled;
                    break;
            }
        }

        public static bool SyncHiddenColumnsWithAnalysisOptions(bool applyDefaultHiddenColumns = false)
        {
            var hidden = ParseHiddenColumns(HiddenColumns);

            if (applyDefaultHiddenColumns)
            {
                foreach (var header in DefaultHiddenColumnHeaders)
                    hidden.Add(header);
            }

            foreach (var header in AnalysisColumnHeaders)
            {
                if (IsAnalysisColumnEnabled(header))
                    hidden.Remove(header);
                else
                    hidden.Add(header);
            }

            EnsureUsableColumnSet(hidden);
            ApplyUserShownColumnPreferences(hidden);
            var synced = FormatHiddenColumns(hidden);
            if (string.Equals(HiddenColumns, synced, StringComparison.Ordinal))
                return false;

            HiddenColumns = synced;
            return true;
        }

        private static bool EnsureUsableColumnSet(HashSet<string> hidden)
        {
            int visibleCount = ColumnHeaderOrder.Count(header => !hidden.Contains(header));
            if (visibleCount >= MinimumUsableVisibleColumns)
                return false;

            hidden.Clear();
            foreach (var header in DefaultHiddenColumnHeaders)
                hidden.Add(header);

            foreach (var header in AnalysisColumnHeaders)
            {
                if (IsAnalysisColumnEnabled(header))
                    hidden.Remove(header);
                else
                    hidden.Add(header);
            }

            return true;
        }

        public static HashSet<string> ParseHiddenColumns(string value)
        {
            var hidden = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrWhiteSpace(value))
                return hidden;

            foreach (var item in value.Split(',', StringSplitOptions.RemoveEmptyEntries))
            {
                var header = NormalizeColumnHeader(item);
                if (!string.IsNullOrWhiteSpace(header))
                    hidden.Add(header);
            }

            return hidden;
        }

        private static string FormatHiddenColumns(HashSet<string> hidden)
        {
            var ordered = new List<string>();
            foreach (var header in ColumnHeaderOrder)
            {
                if (hidden.Contains(header))
                    ordered.Add(header);
            }

            ordered.AddRange(hidden
                .Where(h => !ColumnHeaderOrder.Contains(h, StringComparer.OrdinalIgnoreCase))
                .OrderBy(h => h, StringComparer.OrdinalIgnoreCase));

            return string.Join(",", ordered);
        }

        // Performance — max parallel analysis threads (0 = auto)
        // Auto: half of logical processors, clamped 1–16
        private static int _maxConcurrency;
        public static int MaxConcurrency
        {
            get => _maxConcurrency > 0 ? _maxConcurrency : DefaultConcurrency;
            set => _maxConcurrency = Math.Clamp(value, 0, Environment.ProcessorCount);
        }
        // True when the user has Auto selected (raw field is 0). Distinct from MaxConcurrency,
        // which always returns a usable thread count by falling back to DefaultConcurrency.
        public static bool IsConcurrencyAuto => _maxConcurrency <= 0;
        public static int DefaultConcurrency => Math.Max(1, Math.Min(Environment.ProcessorCount / 2, 16));
        /// <summary>Available presets shown in the Settings UI. Values scale to the user's CPU.</summary>
        public static (string Label, int Value)[] ConcurrencyPresets => GetConcurrencyPresets();

        private static (string Label, int Value)[] GetConcurrencyPresets()
        {
            int cores = Environment.ProcessorCount;
            return new[]
            {
                ("Auto (Balanced)", 0),
                ($"Low (25% — {Math.Max(1, cores / 4)} threads)", Math.Max(1, cores / 4)),
                ($"Medium (50% — {Math.Max(1, cores / 2)} threads)", Math.Max(1, cores / 2)),
                ($"High (75% — {Math.Max(1, cores * 3 / 4)} threads)", Math.Max(1, cores * 3 / 4)),
                ($"Maximum (100% — {cores} threads)", cores),
                ("Custom", -1),
            };
        }

        // Performance — memory limit in MB (0 = auto)
        // Auto: 25% of total system memory, clamped 512–8192 MB
        private static int _maxMemoryMB;
        public static int MaxMemoryMB
        {
            get => _maxMemoryMB > 0 ? _maxMemoryMB : DefaultMemoryMB;
            set => _maxMemoryMB = Math.Clamp(value, 0, (int)Math.Min(TotalSystemMemoryMB, 65536));
        }
        public static bool IsMemoryAuto => _maxMemoryMB <= 0;
        public static long TotalSystemMemoryMB
        {
            get
            {
                try { return GC.GetGCMemoryInfo().TotalAvailableMemoryBytes / (1024 * 1024); }
                catch { return 4096; }
            }
        }
        public static int DefaultMemoryMB => (int)Math.Clamp(TotalSystemMemoryMB / 4, 512, 8192);
        /// <summary>Available memory presets shown in the Settings UI. Values scale to the user's RAM.</summary>
        public static (string Label, int ValueMB)[] MemoryPresets => GetMemoryPresets();

        private static (string Label, int ValueMB)[] GetMemoryPresets()
        {
            long totalMB = TotalSystemMemoryMB;
            return new[]
            {
                ("Auto (Balanced)", 0),
                ("Low (512 MB)", 512),
                ("Medium (1 GB)", 1024),
                ($"High (25% RAM — {(int)Math.Max(512, totalMB / 4):N0} MB)", (int)Math.Max(512, totalMB / 4)),
                ($"Very High (50% RAM — {(int)Math.Max(1024, totalMB / 2):N0} MB)", (int)Math.Max(1024, totalMB / 2)),
                ($"Maximum (75% RAM — {(int)Math.Max(2048, totalMB * 3 / 4):N0} MB)", (int)Math.Max(2048, totalMB * 3 / 4)),
                ("Custom", -1),
            };
        }

        /// <summary>
        /// Returns true if the current process memory usage is within the configured limit.
        /// Call this before starting memory-heavy operations.
        /// </summary>
        private static bool _memoryOk = true;
        private static long _memoryCheckTick;
        private const long MemoryCheckIntervalMs = 400;

        public static bool IsMemoryWithinLimit()
        {
            long limitBytes = (long)MaxMemoryMB * 1024 * 1024;
            if (limitBytes == 0) return true; // no limit
            long now = Environment.TickCount64;
            if (now - _memoryCheckTick < MemoryCheckIntervalMs) return _memoryOk;
            _memoryCheckTick = now;
            _memoryOk = System.Diagnostics.Process.GetCurrentProcess().WorkingSet64 < limitBytes;
            return _memoryOk;
        }

        /// <summary>
        /// Lightweight memory hint — does NOT block scans.
        /// A single gen-0 GC is triggered if over limit, then execution continues immediately.
        /// Blocking loops with GC.Collect(2) destroy scan throughput; we let the .NET GC manage memory.
        /// </summary>
        public static async Task WaitForMemoryAsync(CancellationToken ct = default)
        {
            if (IsMemoryWithinLimit()) return;
            // One quick gen-0 collection, then move on. No blocking loop.
            GC.Collect(0, GCCollectionMode.Optimized, false);
            await Task.CompletedTask;
        }

        public static void Initialize()
        {
            string saved = LoadSavedTheme();
            ApplyTheme(saved);
            LoadPlayOptions();

            // Cross-install persistence: registry flags override options.txt
            if (GetRegistryFlag("DonationDismissed")) DonationDismissed = true;
            if (GetRegistryFlag("FooterSupportDismissed")) FooterSupportDismissed = true;
            if (GetRegistryFlag("AiConfigDismissed")) AiConfigDismissed = true;
            if (GetRegistryFlag("FirstLaunchComplete")) FirstLaunchComplete = true;

            // Re-sync playbar accent after playbar theme is loaded from options
            UpdatePlaybarAccentResource();

            // One-time migration: strip any leftover sensitive data from options.txt
            CleanSensitiveDataFromOptions();

            // Migrate session data from old temp location to Documents
            MigrateSessionFromTemp();
        }

        /// <summary>
        /// Migrates session.dat from the old %TEMP% location to Documents/AudioAuditor if it exists.
        /// </summary>
        private static void MigrateSessionFromTemp()
        {
            try
            {
                string oldFile = Path.Combine(Path.GetTempPath(), "AudioAuditor_session.dat");
                if (File.Exists(oldFile) && !File.Exists(SensitiveFile))
                {
                    var dir = Path.GetDirectoryName(SensitiveFile)!;
                    if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                    File.Move(oldFile, SensitiveFile);
                    // Reload after migration
                    LoadSensitiveData();
                }
            }
            catch { }
        }

        private static void LoadSensitiveData()
        {
            try
            {
                if (!File.Exists(SensitiveFile)) return;

                string content;
                var rawBytes = File.ReadAllBytes(SensitiveFile);
                try
                {
                    // Try DPAPI-decrypted first
                    var decrypted = System.Security.Cryptography.ProtectedData.Unprotect(
                        rawBytes, null, System.Security.Cryptography.DataProtectionScope.CurrentUser);
                    content = System.Text.Encoding.UTF8.GetString(decrypted);
                }
                catch
                {
                    // Fallback: legacy plaintext file
                    content = System.Text.Encoding.UTF8.GetString(rawBytes);
                }

                foreach (var line in content.Split('\n', StringSplitOptions.RemoveEmptyEntries))
                {
                    var sp = line.TrimEnd('\r').Split('=', 2);
                    if (sp.Length != 2) continue;
                    switch (sp[0])
                    {
                        case "LastFmApiKey": LastFmApiKey = sp[1]; break;
                        case "LastFmApiSecret": LastFmApiSecret = sp[1]; break;
                        case "LastFmSessionKey": LastFmSessionKey = sp[1]; break;
                        case "LastFmUsername": LastFmUsername = sp[1]; break;
                        case "LibreFmApiKey": LibreFmApiKey = sp[1]; break;
                        case "LibreFmApiSecret": LibreFmApiSecret = sp[1]; break;
                        case "LibreFmSessionKey": LibreFmSessionKey = sp[1]; break;
                        case "LibreFmUsername": LibreFmUsername = sp[1]; break;
                        case "ListenBrainzUserToken": ListenBrainzUserToken = sp[1]; break;
                        case "ListenBrainzUsername": ListenBrainzUsername = sp[1]; break;
                        case "MalojaServerUrl": MalojaServerUrl = sp[1]; break;
                        case "MalojaApiKey": MalojaApiKey = sp[1]; break;
                        case "MalojaUsername": MalojaUsername = sp[1]; break;
                        case "DiscordRpcClientId": DiscordRpcClientId = sp[1]; break;
                        case "AcoustIdApiKey": AcoustIdApiKey = sp[1]; break;
                    }
                }
            }
            catch { }
        }

        /// <summary>
        /// Removes any legacy Last.fm keys that may have been saved in options.txt (AppData).
        /// Sensitive data is now stored separately in Documents/AudioAuditor/session.dat.
        /// </summary>
        private static void CleanSensitiveDataFromOptions()
        {
            try
            {
                if (!File.Exists(OptionsFile))
                {
                    LoadSensitiveData();
                    ApplyScanPerformanceDefaultsMigration();
                    return;
                }
                var lines = File.ReadAllLines(OptionsFile);
                var cleanLines = lines.Where(l =>
                    !l.StartsWith("LastFmApiKey=", StringComparison.Ordinal) &&
                    !l.StartsWith("LastFmApiSecret=", StringComparison.Ordinal) &&
                    !l.StartsWith("LastFmSessionKey=", StringComparison.Ordinal) &&
                    !l.StartsWith("LastFmUsername=", StringComparison.Ordinal) &&
                    !l.StartsWith("DiscordRpcClientId=", StringComparison.Ordinal)).ToArray();
                if (cleanLines.Length < lines.Length)
                    File.WriteAllLines(OptionsFile, cleanLines);
            }
            catch { }
        }

        /// <summary>
        /// Raised after a theme has been applied to <see cref="Application.Current.Resources"/>.
        /// MainWindow subscribes so that, when ColorMatch is active on the Now Playing screen,
        /// it can re-apply album-derived scoped colors that the global theme write just clobbered.
        /// </summary>
        public static event Action? ThemeChanged;

        public static void ApplyTheme(string themeName)
        {
            if (themeName.Equals("Liquid Glass", StringComparison.OrdinalIgnoreCase))
                themeName = "Blurple";

            if (!AvailableThemes.Contains(themeName) && GetThemeDefinition(themeName) == null)
                themeName = "Blurple";

            _currentTheme = themeName;
            var customTheme = GetThemeDefinition(themeName);
            var colors = customTheme != null
                ? GetThemeColors(customTheme)
                : GetThemeColors(themeName);

            var res = Application.Current.Resources;
            foreach (var kvp in colors)
            {
                res[kvp.Key] = kvp.Value;
            }
            ApplyGlassResources(res, colors, customTheme);

            // Keep playbar accent in sync
            UpdatePlaybarAccentResource();

            SaveTheme(themeName);

            // Let listeners (MainWindow) restore any ColorMatch overrides the global
            // resource write above replaced. Never let a handler break theme application.
            try { ThemeChanged?.Invoke(); } catch { }
        }

        public static void SetPlaybarTheme(string playbarTheme)
        {
            if (!AvailablePlaybarThemes.Contains(playbarTheme))
                playbarTheme = "Blue Fire";
            _currentPlaybarTheme = playbarTheme == "Follow Theme" ? "" : playbarTheme;
            // Invalidate cached colors so GetPlaybarColors() recalculates
            _cachedPlaybarColors = null;
            _cachedPlaybarThemeName = null;
            UpdatePlaybarAccentResource();
            SavePlayOptions();
        }

        /// <summary>Maps the current color theme to its closest playbar theme when "Follow Theme" is selected.</summary>
        private static string ResolveFollowPlaybarTheme()
        {
            return _currentTheme switch
            {
                "Light" => "Minimal",
                "Amethyst" => "Purple Haze",
                "Dreamsicle" => "Sunset Glow",
                "Goldenrod" => "Golden Wave",
                "Emerald" => "Emerald Wave",
                "Blurple" => "Blurple Wave",
                "Crimson" => "Crimson Wave",
                "Brown" => "Brown Wave",
                _ => "Blue Fire", // Dark, Ocean, fallback
            };
        }

        /// <summary>
        /// Updates the PlaybarAccentColor resource to match the current playbar theme's primary color.
        /// This keeps the seek slider, volume slider, and shuffle icon in sync with the playbar theme.
        /// </summary>
        public static void UpdatePlaybarAccentResource()
        {
            var colors = GetPlaybarColors();
            // Use the middle gradient color (primary accent of the playbar theme) at full opacity
            var primary = colors.ProgressGradient[1];
            primary.A = 255;
            var brush = new SolidColorBrush(primary);
            brush.Freeze();
            Application.Current.Resources["PlaybarAccentColor"] = brush;
            var secondary = colors.ProgressGradient.Length > 2 ? colors.ProgressGradient[2] : colors.ProgressGradient[0];
            secondary.A = 255;
            var secBrush = new SolidColorBrush(secondary);
            secBrush.Freeze();
            Application.Current.Resources["PlaybarSecondaryColor"] = secBrush;
        }

        private static PlaybarColors? _cachedPlaybarColors;
        private static string? _cachedPlaybarThemeName;

        /// <summary>
        /// Returns playbar color config: (bgColor, progressColors[], waveAnimSpeed)
        /// Cached to avoid allocations on every visualizer frame.
        /// </summary>
        public static PlaybarColors GetPlaybarColors()
        {
            bool followsTheme = string.IsNullOrEmpty(_currentPlaybarTheme);
            string effective = followsTheme ? $"theme:{_currentTheme}" : _currentPlaybarTheme;
            if (_cachedPlaybarColors != null && _cachedPlaybarThemeName == effective)
                return _cachedPlaybarColors;

            _cachedPlaybarThemeName = effective;
            var customTheme = followsTheme
                ? GetThemeDefinition(_currentTheme)
                : GetThemeDefinition(_currentPlaybarTheme);
            if (customTheme != null)
            {
                _cachedPlaybarColors = ColorsFromThemePalette(customTheme, useVisualizerColors: false);
                return _cachedPlaybarColors;
            }

            string resolved = followsTheme ? ResolveFollowPlaybarTheme() : _currentPlaybarTheme;
            _cachedPlaybarColors = resolved switch
            {
                "Neon Pulse" => new PlaybarColors(
                    Color.FromArgb(40, 0, 255, 128),
                    new[] {
                        Color.FromArgb(180, 0, 180, 80),
                        Color.FromArgb(220, 0, 255, 128),
                        Color.FromArgb(255, 80, 255, 180)
                    }, 2.5),
                "Sunset Glow" => new PlaybarColors(
                    Color.FromArgb(40, 255, 140, 50),
                    new[] {
                        Color.FromArgb(180, 200, 60, 20),
                        Color.FromArgb(220, 255, 140, 50),
                        Color.FromArgb(255, 255, 200, 100)
                    }, 1.8),
                "Purple Haze" => new PlaybarColors(
                    Color.FromArgb(40, 160, 80, 220),
                    new[] {
                        Color.FromArgb(180, 100, 30, 160),
                        Color.FromArgb(220, 160, 80, 220),
                        Color.FromArgb(255, 200, 140, 255)
                    }, 2.0),
                "Minimal" => new PlaybarColors(
                    Color.FromArgb(25, 128, 128, 128),
                    new[] {
                        Color.FromArgb(140, 100, 100, 100),
                        Color.FromArgb(180, 160, 160, 160),
                        Color.FromArgb(200, 200, 200, 200)
                    }, 1.0),
                "Golden Wave" => new PlaybarColors(
                    Color.FromArgb(40, 212, 160, 23),
                    new[] {
                        Color.FromArgb(180, 160, 120, 10),
                        Color.FromArgb(220, 212, 160, 23),
                        Color.FromArgb(255, 255, 210, 80)
                    }, 1.6),
                "Emerald Wave" => new PlaybarColors(
                    Color.FromArgb(40, 46, 204, 113),
                    new[] {
                        Color.FromArgb(180, 20, 140, 60),
                        Color.FromArgb(220, 46, 204, 113),
                        Color.FromArgb(255, 100, 240, 160)
                    }, 2.0),
                "Blurple Wave" => new PlaybarColors(
                    Color.FromArgb(40, 88, 101, 242),
                    new[] {
                        Color.FromArgb(180, 60, 70, 180),
                        Color.FromArgb(220, 88, 101, 242),
                        Color.FromArgb(255, 140, 150, 255)
                    }, 2.2),
                "Crimson Wave" => new PlaybarColors(
                    Color.FromArgb(40, 220, 20, 60),
                    new[] {
                        Color.FromArgb(180, 160, 10, 30),
                        Color.FromArgb(220, 220, 20, 60),
                        Color.FromArgb(255, 255, 80, 100)
                    }, 1.8),
                "Brown Wave" => new PlaybarColors(
                    Color.FromArgb(40, 160, 110, 60),
                    new[] {
                        Color.FromArgb(180, 110, 70, 30),
                        Color.FromArgb(220, 160, 110, 60),
                        Color.FromArgb(255, 210, 170, 110)
                    }, 1.4),
                "Rainbow Bars" => new PlaybarColors(
                    Color.FromArgb(40, 128, 128, 128),
                    new[] {
                        Color.FromArgb(200, 255, 50, 50),
                        Color.FromArgb(200, 50, 255, 50),
                        Color.FromArgb(200, 50, 50, 255)
                    }, 2.0),
                _ => new PlaybarColors( // Blue Fire (default)
                    Color.FromArgb(40, 77, 168, 218),
                    new[] {
                        Color.FromArgb(180, 30, 120, 180),
                        Color.FromArgb(220, 77, 168, 218),
                        Color.FromArgb(255, 120, 200, 240)
                    }, 1.5),
            };
            return _cachedPlaybarColors;
        }

        public static string GetMusicServiceUrl(string serviceName, string query)
        {
            string encoded = Uri.EscapeDataString(query);
            string region = StreamingRegion?.ToLowerInvariant() ?? "us";
            bool aware = RegionAwareSearchEnabled;

            return serviceName switch
            {
                "Spotify" => $"https://open.spotify.com/search/{encoded}",
                "YouTube Music" => $"https://music.youtube.com/search?q={encoded}",
                "Tidal" => $"https://listen.tidal.com/search?q={encoded}",
                "Qobuz" => aware
                    ? $"https://www.qobuz.com/{GetQobuzRegion(region)}/search/tracks/{encoded}"
                    : $"https://www.qobuz.com/us-en/search/tracks/{encoded}",
                "Amazon Music" => aware
                    ? $"https://music.amazon.{GetAmazonTld(region)}/search/{encoded}"
                    : $"https://music.amazon.com/search/{encoded}",
                "Apple Music" => aware && region != "us"
                    ? $"https://music.apple.com/{region}/search?term={encoded}"
                    : $"https://music.apple.com/us/search?term={encoded}",
                "Deezer" => $"https://www.deezer.com/search/{encoded}",
                "SoundCloud" => $"https://soundcloud.com/search?q={encoded}",
                "Bandcamp" => $"https://bandcamp.com/search?q={encoded}",
                "Last.fm" => $"https://www.last.fm/search?q={encoded}",
                _ => $"https://www.google.com/search?q={encoded}"
            };
        }

        private static string GetAmazonTld(string region)
        {
            return region switch
            {
                "uk" => "co.uk",
                "jp" => "co.jp",
                "au" => "com.au",
                "br" => "com.br",
                "mx" => "com.mx",
                "in" => "in",
                "ca" => "ca",
                "de" => "de",
                "fr" => "fr",
                _ => "com"
            };
        }

        private static string GetQobuzRegion(string region)
        {
            return region switch
            {
                "us" => "us-en",
                "uk" => "uk-en",
                "ca" => "ca-en",
                "au" => "au-en",
                "de" => "de-de",
                "fr" => "fr-fr",
                "jp" => "jp-ja",
                "br" => "br-pt",
                "mx" => "mx-es",
                "in" => "in-en",
                _ => "us-en"
            };
        }

        /// <summary>
        /// Returns COLORREF (0x00BBGGRR) for the current theme's title bar caption color.
        /// </summary>
        public static int GetTitleBarColorRef()
        {
            var customTheme = GetThemeDefinition(_currentTheme);
            if (customTheme != null)
                return ColorToRef(HexToColor(customTheme.ToolbarBackground));

            // Use ToolbarBg color for each theme so the title bar matches the toolbar
            return _currentTheme switch
            {
                "Ocean"      => ColorToRef(0x13, 0x22, 0x38),
                "Light"      => ColorToRef(0xE8, 0xE8, 0xEC),
                "Amethyst"   => ColorToRef(0x22, 0x18, 0x38),
                "Dreamsicle" => ColorToRef(0x2E, 0x1E, 0x14),
                "Goldenrod"  => ColorToRef(0x38, 0x30, 0x10),
                "Emerald"    => ColorToRef(0x14, 0x28, 0x1C),
                "Blurple"    => ColorToRef(0x2C, 0x2D, 0x56),
                "Crimson"    => ColorToRef(0x2E, 0x14, 0x18),
                "Brown"      => ColorToRef(0x2E, 0x22, 0x16),
                _            => ColorToRef(0x2D, 0x2D, 0x30), // Dark
            };
        }

        private static int ColorToRef(byte r, byte g, byte b) => r | (g << 8) | (b << 16);
        private static int ColorToRef(Color color) => ColorToRef(color.R, color.G, color.B);

        private static string LoadSavedTheme()
        {
            try
            {
                if (File.Exists(ThemeFile))
                    return File.ReadAllText(ThemeFile).Trim();
            }
            catch { }
            return "Blurple";
        }

        private static void SaveTheme(string theme)
        {
            try
            {
                EnsureDir();
                File.WriteAllText(ThemeFile, theme);
            }
            catch { }
        }

        private static void EnsureDir()
        {
            if (!Directory.Exists(SettingsDir))
                Directory.CreateDirectory(SettingsDir);
        }
    }

    public class PlaybarColors
    {
        public Color BackgroundColor { get; }
        public Color[] ProgressGradient { get; }
        public double AnimationSpeed { get; }

        public PlaybarColors(Color bg, Color[] gradient, double speed)
        {
            BackgroundColor = bg;
            ProgressGradient = gradient;
            AnimationSpeed = speed;
        }
    }
}
