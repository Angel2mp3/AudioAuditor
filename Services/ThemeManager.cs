using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using AudioQualityChecker.Models;
using AudioQualityChecker.Abstractions;
using AudioQualityChecker.Theming;

// NOTICE: Embedding malicious code in theme/settings files or their loading
// routines is still malware distribution and a criminal offense.
// Legitimate forks: rename assemblies in .csproj to disable integrity checks.

namespace AudioQualityChecker.Services
{
    public enum LoopMode { Off, All, One }

    // GlowMotionMode, ColorMatchTarget and NpLyricDisplayMode moved to Core alongside the settings
    // they describe (AudioAuditor.Core/Services/NowPlayingSettings.cs) — same namespace, so no call
    // site changed.

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

        public static CustomThemeDefinition? GetThemeDefinition(string themeName)
        {
            return CustomThemeStore.FindTheme(themeName);
        }

        // Custom service settings (for Custom... slots — 6 slots)
        public static string[] CustomServiceUrls { get; } = new string[6] { "", "", "", "", "", "" };
        public static string[] CustomServiceIcons { get; } = new string[6] { "", "", "", "", "", "" };

        // Streaming service region settings
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

        // CD Rip Checker — scan-time cambia check of rip logs found next to files (opt-in)
        public static bool RipLogCheckEnabled { get; set; }

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
        private static bool _crashLoggingEnabled = true;
        public static bool CrashLoggingEnabled
        {
            get => _crashLoggingEnabled;
            set
            {
                _crashLoggingEnabled = value;
                AudioQualityChecker.AudioAuditorSettings.CrashLoggingEnabled = value;
            }
        }

        // Local stats collection — OFF by default, user must explicitly opt in
        private static bool _statsCollectionEnabled;
        public static bool StatsCollectionEnabled
        {
            get => _statsCollectionEnabled;
            set
            {
                _statsCollectionEnabled = value;
                AudioQualityChecker.AudioAuditorSettings.StatsCollectionEnabled = value;
            }
        }

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

        // ─── Batch Editor: rename name transforms ───
        public static int SmartRenameNameCaseIndex { get; set; }   // 0 None, 1 lower, 2 UPPER, 3 Title
        public static int SmartRenameSpaceModeIndex { get; set; }  // 0 Keep, 1 Underscores, 2 Spaces
        public static bool SmartRenameStripFeaturing { get; set; }

        // ─── Batch Editor: streaming-link platform preference (0 Deezer, 1 Apple, 2 Spotify, 3 YouTube) ───
        public static int StreamingLinkPlatformIndex { get; set; }

        public static string DefaultCopyFolder { get; set; } = "";
        public static string DefaultMoveFolder { get; set; } = "";
        public static string DefaultPlaylistFolder { get; set; } = "";

        // ─── Main window color match ───
        public static bool MainColorMatchEnabled { get; set; }
        public static ColorMatchTarget MainColorMatchTargets { get; set; } = ColorMatchTarget.All;

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
            catch (Exception ex)
            {
                // These flags survive reinstalls (DonationDismissed, FirstLaunchComplete, ...).
                // A silent write failure makes a dismissed prompt reappear forever.
                if (CrashLoggingEnabled) LocalCrashLogger.Write(ex);
            }
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
            "Date Created", "True Peak", "LUFS", "Rip Log"
        };

        private static readonly string[] DefaultHiddenColumnHeaders =
        {
            "★", "BPM", "DR", "Date Created", "True Peak", "LUFS", "Rip Log", "Silence"
        };

        private static readonly HashSet<string> AnalysisColumnHeaders = new(StringComparer.OrdinalIgnoreCase)
        {
            "BPM", "DR", "True Peak", "LUFS", "Rip Log", "Silence",
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

        public static bool IsAnalysisColumnEnabled(string header)
        {
            return NormalizeColumnHeader(header) switch
            {
                "BPM" => BpmDetectionEnabled,
                "DR" => DynamicRangeEnabled,
                "True Peak" => TruePeakEnabled,
                "LUFS" => LufsEnabled,
                "Rip Log" => RipLogCheckEnabled,
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
                case "Rip Log":
                    RipLogCheckEnabled = enabled;
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
            // Disposed rather than left to the finalizer — this runs every 400 ms for the whole
            // life of a scan and each Process object holds an OS handle. Deliberately NOT
            // Environment.WorkingSet: the CLI and the Avalonia build both gate on
            // Process.WorkingSet64, and a memory limit that means something different in the GUI
            // than in the CLI is exactly the kind of drift that is invisible until a user reports
            // it on one build only.
            using var proc = System.Diagnostics.Process.GetCurrentProcess();
            _memoryOk = proc.WorkingSet64 < limitBytes;
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

            // Apply the persisted app-wide font (built-in name or a previously-copied custom font file).
            try { ApplyFont(AppFontFamily); }
            catch { /* fall back silently to the default resource value already in App.xaml */ }

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
                    byte[] raw = File.ReadAllBytes(oldFile);
                    string? content = null;
                    try
                    {
                        var decrypted = System.Security.Cryptography.ProtectedData.Unprotect(
                            raw, null, System.Security.Cryptography.DataProtectionScope.CurrentUser);
                        content = System.Text.Encoding.UTF8.GetString(decrypted);
                    }
                    catch
                    {
                        string legacy = System.Text.Encoding.UTF8.GetString(raw);
                        if (CredentialStore.LooksLikeLegacyPlaintext(legacy)) content = legacy;
                    }

                    if (content == null) return;
                    var migrated = ParseKnownCredentials(content);
                    if (migrated.Count == 0) return;
                    CredentialStore.Save(migrated);
                    var stored = CredentialStore.Load();
                    if (migrated.Any(pair => !stored.TryGetValue(pair.Key, out var value) || value != pair.Value))
                        return;

                    File.Delete(oldFile);
                    LoadSensitiveData();
                }
            }
            catch (Exception ex)
            {
                // Failing here strands the session file at its old path, so every credential in
                // it looks lost on the next launch. Leave a trace instead of guessing later.
                if (CrashLoggingEnabled) LocalCrashLogger.Write(ex);
            }
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
                    if (!CredentialStore.LooksLikeLegacyPlaintext(content)) return;
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
                        case "DiscogsToken": DiscogsToken = sp[1]; break;
                        case "FanartTvApiKey": FanartTvApiKey = sp[1]; break;
                        case "SpotifyClientId": SpotifyClientId = sp[1]; break;
                        case "SpotifyClientSecret": SpotifyClientSecret = sp[1]; break;
                        case "YouTubeApiKey": YouTubeApiKey = sp[1]; break;
                        case "SHLabsCustomApiKey":
                            SHLabsCustomApiKey = sp[1];
                            SHLabsDetectionService.CustomApiKey = sp[1];
                            break;
                    }
                }
            }
            catch (Exception ex)
            {
                // Mirrors the save path's reasoning: this reads every scrobbler session key and
                // API token, so one DPAPI or parse failure logs the user out of Last.fm,
                // ListenBrainz, Discord, AcoustID, Discogs and Spotify at once with no trace.
                if (CrashLoggingEnabled) LocalCrashLogger.Write(ex);
            }
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
                var migrated = new Dictionary<string, string>(StringComparer.Ordinal);
                var storedBeforeMigration = CredentialStore.Load();
                foreach (var line in lines)
                {
                    int separator = line.IndexOf('=');
                    if (separator > 0 &&
                        CredentialStore.Keys.Contains(line[..separator], StringComparer.Ordinal) &&
                        !storedBeforeMigration.ContainsKey(line[..separator]))
                        migrated[line[..separator]] = line[(separator + 1)..];
                }

                if (migrated.Count > 0)
                {
                    CredentialStore.Save(migrated);
                    var stored = CredentialStore.Load();
                    if (migrated.Any(pair => !stored.TryGetValue(pair.Key, out var value) || value != pair.Value))
                        return;
                }

                var credentialKeys = CredentialStore.Keys.ToHashSet(StringComparer.Ordinal);
                if (lines.Any(l =>
                    l.IndexOf('=') is int separator && separator > 0 &&
                    credentialKeys.Contains(l[..separator])))
                {
                    OptionsFileStore.Merge(OptionsFile, CredentialStore.Keys.Select(key =>
                        new KeyValuePair<string, string?>(key, null)));
                }
            }
            catch (Exception ex)
            {
                // Legacy credentials stay in the plaintext options.txt if this fails.
                if (CrashLoggingEnabled) LocalCrashLogger.Write(ex);
            }
        }

        private static Dictionary<string, string> ParseKnownCredentials(string content)
        {
            var values = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var line in content.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                var pair = line.TrimEnd('\r').Split('=', 2);
                if (pair.Length == 2 && CredentialStore.Keys.Contains(pair[0], StringComparer.Ordinal))
                    values[pair[0]] = pair[1];
            }
            return values;
        }

        /// <summary>
        /// Raised after a theme has been applied to <see cref="Application.Current.Resources"/>.
        /// MainWindow subscribes so that, when ColorMatch is active on the Now Playing screen,
        /// it can re-apply album-derived scoped colors that the global theme write just clobbered.
        /// </summary>
        public static event Action? ThemeChanged;

        // ─── App-wide font ───
        // Either a built-in family name (e.g. "Segoe UI") or an absolute path to a custom
        // .ttf/.otf file the user added (copied into %APPDATA%\AudioAuditor\Fonts\ by ApplyFont).
        public static string AppFontFamily { get; set; } = "Segoe UI";

        // Stock Windows families only, so nothing has to be bundled — WPF resolves these from the
        // user's installed fonts. Names not present on the machine are filtered out of the picker.
        public static readonly string[] BuiltInFontFamilies =
            { "Segoe UI", "Segoe UI Variable", "Calibri", "Arial", "Verdana", "Tahoma",
              "Trebuchet MS", "Georgia", "Times New Roman", "Comic Sans MS", "Consolas", "Courier New" };

        private static string CustomFontsFolder =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AudioAuditor", "Fonts");

        /// <summary>Applies a built-in font by name, or a custom font file (copied into the app's
        /// font folder so it survives the original file moving/being deleted), app-wide via the
        /// "AppFontFamily" DynamicResource every window's FontFamily setters are bound to.</summary>
        public static void ApplyFont(string fontNameOrPath)
        {
            FontFamily resolved;
            string persistedValue;

            if (!string.IsNullOrWhiteSpace(fontNameOrPath) && Path.IsPathRooted(fontNameOrPath) && File.Exists(fontNameOrPath))
            {
                Directory.CreateDirectory(CustomFontsFolder);
                string destPath = Path.Combine(CustomFontsFolder, Path.GetFileName(fontNameOrPath));
                if (!string.Equals(Path.GetFullPath(fontNameOrPath), Path.GetFullPath(destPath), StringComparison.OrdinalIgnoreCase))
                    File.Copy(fontNameOrPath, destPath, overwrite: true);

                var folderUri = new Uri(Path.GetDirectoryName(destPath) + Path.DirectorySeparatorChar, UriKind.Absolute);
                var familyName = Fonts.GetFontFamilies(new Uri(destPath, UriKind.Absolute)).FirstOrDefault()?.Source
                    ?? Path.GetFileNameWithoutExtension(destPath);
                resolved = new FontFamily(folderUri, "./#" + familyName);
                persistedValue = destPath;
            }
            else
            {
                string name = string.IsNullOrWhiteSpace(fontNameOrPath) ? "Segoe UI" : fontNameOrPath;
                resolved = new FontFamily(name);
                persistedValue = name;
            }

            Application.Current.Resources["AppFontFamily"] = resolved;
            AppFontFamily = persistedValue;
            SavePlayOptions();
        }

        public static void ApplyTheme(string themeName)
        {
            if (themeName.Equals("Liquid Glass", StringComparison.OrdinalIgnoreCase))
                themeName = "Blurple";

            if (!AvailableThemes.Contains(themeName) && GetThemeDefinition(themeName) == null)
                themeName = "Blurple";

            _currentTheme = themeName;
            AudioQualityChecker.AudioAuditorSettings.CurrentThemeName = themeName;
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
        private static string ResolveFollowPlaybarTheme() =>
            PlaybarPalettes.ResolveFollowTheme(_currentTheme);

        private static Color ToMediaColor(AppColor c) => Color.FromArgb(c.A, c.R, c.G, c.B);

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
            // Gradient values live in PlaybarPalettes in the shared core so the Avalonia
            // front-end draws the same playbar; they were WPF-only before.
            var palette = PlaybarPalettes.Get(resolved);
            _cachedPlaybarColors = new PlaybarColors(
                ToMediaColor(palette.BackgroundColor),
                palette.ProgressGradient.Select(ToMediaColor).ToArray(),
                palette.AnimationSpeed);
            return _cachedPlaybarColors;
        }

        // Region-aware store URLs are always on: the opposite just points the user at the
        // wrong storefront. StreamingRegion is the real control.
        public static string GetMusicServiceUrl(string serviceName, string query) =>
            MusicServiceUrls.Build(serviceName, query, StreamingRegion, regionAware: true);

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
            catch (Exception ex)
            {
                // Falling through silently reverts the user's chosen theme to the default, which
                // reads as "the app reset my theme" with nothing to diagnose.
                if (CrashLoggingEnabled) LocalCrashLogger.Write(ex);
            }
            return "Blurple";
        }

        private static void SaveTheme(string theme)
        {
            try
            {
                EnsureDir();
                File.WriteAllText(ThemeFile, theme);
            }
            catch (Exception ex)
            {
                // Write side of LoadSavedTheme: the theme applies now but is gone next launch.
                if (CrashLoggingEnabled) LocalCrashLogger.Write(ex);
            }
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
