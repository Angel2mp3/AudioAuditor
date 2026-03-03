using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Media;

namespace AudioQualityChecker.Services
{
    public static class ThemeManager
    {
        private static readonly string SettingsDir =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AudioAuditor");
        private static readonly string ThemeFile = Path.Combine(SettingsDir, "theme.txt");
        private static readonly string OptionsFile = Path.Combine(SettingsDir, "options.txt");
        // Sensitive data stored in user's Documents folder (persistent)
        private static readonly string SensitiveFile = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "AudioAuditor", "session.dat");

        public static readonly List<string> AvailableThemes = new() { "Dark", "Ocean", "Light", "Amethyst", "Dreamsicle", "Goldenrod", "Emerald", "Blurple", "Crimson", "Brown" };
        public static readonly List<string> AvailablePlaybarThemes = new() { "Blue Fire", "Neon Pulse", "Sunset Glow", "Purple Haze", "Minimal", "Golden Wave", "Emerald Wave", "Blurple Wave", "Crimson Wave", "Brown Wave" };
        public static readonly List<string> AvailableMusicServices = new()
        {
            "Spotify", "YouTube Music", "Tidal", "Qobuz", "Amazon Music",
            "Apple Music", "Deezer", "SoundCloud", "Bandcamp", "Last.fm", "Custom..."
        };

        private static string _currentTheme = "Dark";
        public static string CurrentTheme => _currentTheme;

        private static string _currentPlaybarTheme = "Blue Fire";
        public static string CurrentPlaybarTheme => _currentPlaybarTheme;

        // All 6 configurable music service slots
        public static string[] MusicServiceSlots { get; } = new string[6];

        // Play Options
        public static bool AutoPlayNext { get; set; }
        public static bool AudioNormalization { get; set; }
        public static bool Crossfade { get; set; }
        public static int CrossfadeDuration { get; set; } = 3; // seconds, 1-10

        // Visualizer mode (false=spectrogram, true=visualizer)
        public static bool VisualizerMode { get; set; }

        // Rainbow Visualizer: each bar gets its own cycling spectrum color
        public static bool RainbowVisualizerEnabled { get; set; }

        // Custom service settings (for Custom... slots — 6 slots)
        public static string[] CustomServiceUrls { get; } = new string[6] { "", "", "", "", "", "" };
        public static string[] CustomServiceIcons { get; } = new string[6] { "", "", "", "", "", "" };

        // Equalizer
        public static bool EqualizerEnabled { get; set; }
        public static float[] EqualizerGains { get; set; } = new float[10]; // 10 bands

        // Discord Rich Presence
        public static bool DiscordRpcEnabled { get; set; }

        // Last.fm Scrobbling
        public static bool LastFmEnabled { get; set; }
        public static string LastFmApiKey { get; set; } = "";
        public static string LastFmApiSecret { get; set; } = "";
        public static string LastFmSessionKey { get; set; } = "";
        public static string LastFmUsername { get; set; } = "";

        // Export format
        public static string ExportFormat { get; set; } = "csv";

        // Spatial Audio
        public static bool SpatialAudioEnabled { get; set; }

        // DataGrid column layout — serialized as Header:DisplayIndex:Width;...
        public static string ColumnLayout { get; set; } = "";

        // Performance — max parallel analysis threads (0 = auto)
        // Auto: half of logical processors, clamped 1–16
        private static int _maxConcurrency;
        public static int MaxConcurrency
        {
            get => _maxConcurrency > 0 ? _maxConcurrency : DefaultConcurrency;
            set => _maxConcurrency = Math.Clamp(value, 0, 32);
        }
        public static int DefaultConcurrency => Math.Max(1, Math.Min(Environment.ProcessorCount / 2, 16));
        /// <summary>Available presets shown in the Settings UI.</summary>
        public static readonly (string Label, int Value)[] ConcurrencyPresets = new[]
        {
            ("Auto (Balanced)", 0),
            ("Low (2 threads)", 2),
            ("Medium (4 threads)", 4),
            ("High (8 threads)", 8),
            ("Maximum (16 threads)", 16),
        };

        // Performance — memory limit in MB (0 = auto)
        // Auto: 25% of total system memory, clamped 512–8192 MB
        private static int _maxMemoryMB;
        public static int MaxMemoryMB
        {
            get => _maxMemoryMB > 0 ? _maxMemoryMB : DefaultMemoryMB;
            set => _maxMemoryMB = Math.Clamp(value, 0, 16384);
        }
        public static long TotalSystemMemoryMB
        {
            get
            {
                try { return GC.GetGCMemoryInfo().TotalAvailableMemoryBytes / (1024 * 1024); }
                catch { return 4096; }
            }
        }
        public static int DefaultMemoryMB => (int)Math.Clamp(TotalSystemMemoryMB / 4, 512, 8192);
        /// <summary>Available memory presets shown in the Settings UI.</summary>
        public static readonly (string Label, int ValueMB)[] MemoryPresets = new[]
        {
            ("Auto (Balanced)", 0),
            ("Low (512 MB)", 512),
            ("Medium (1 GB)", 1024),
            ("High (2 GB)", 2048),
            ("Very High (4 GB)", 4096),
            ("Maximum (8 GB)", 8192),
        };

        /// <summary>
        /// Returns true if the current process memory usage is within the configured limit.
        /// Call this before starting memory-heavy operations.
        /// </summary>
        public static bool IsMemoryWithinLimit()
        {
            long limitBytes = (long)MaxMemoryMB * 1024 * 1024;
            long currentBytes = System.Diagnostics.Process.GetCurrentProcess().WorkingSet64;
            return currentBytes < limitBytes;
        }

        /// <summary>
        /// Waits asynchronously until memory usage drops below the configured limit.
        /// Triggers GC if over limit and polls every 200ms.
        /// </summary>
        public static async Task WaitForMemoryAsync(CancellationToken ct = default)
        {
            if (IsMemoryWithinLimit()) return;
            GC.Collect(2, GCCollectionMode.Forced, false);
            GC.WaitForPendingFinalizers();
            int waited = 0;
            while (!IsMemoryWithinLimit() && waited < 10_000)
            {
                await Task.Delay(200, ct);
                waited += 200;
            }
        }

        public static void Initialize()
        {
            string saved = LoadSavedTheme();
            ApplyTheme(saved);
            LoadPlayOptions();

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

                foreach (var line in File.ReadAllLines(SensitiveFile))
                {
                    var sp = line.Split('=', 2);
                    if (sp.Length != 2) continue;
                    switch (sp[0])
                    {
                        case "LastFmApiKey": LastFmApiKey = sp[1]; break;
                        case "LastFmApiSecret": LastFmApiSecret = sp[1]; break;
                        case "LastFmSessionKey": LastFmSessionKey = sp[1]; break;
                        case "LastFmUsername": LastFmUsername = sp[1]; break;
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
                if (!File.Exists(OptionsFile)) return;
                var lines = File.ReadAllLines(OptionsFile);
                var cleanLines = lines.Where(l =>
                    !l.StartsWith("LastFmApiKey=", StringComparison.Ordinal) &&
                    !l.StartsWith("LastFmApiSecret=", StringComparison.Ordinal) &&
                    !l.StartsWith("LastFmSessionKey=", StringComparison.Ordinal) &&
                    !l.StartsWith("LastFmUsername=", StringComparison.Ordinal)).ToArray();
                if (cleanLines.Length < lines.Length)
                    File.WriteAllLines(OptionsFile, cleanLines);
            }
            catch { }
        }

        public static void ApplyTheme(string themeName)
        {
            if (!AvailableThemes.Contains(themeName))
                themeName = "Dark";

            _currentTheme = themeName;
            var colors = GetThemeColors(themeName);

            var res = Application.Current.Resources;
            foreach (var kvp in colors)
            {
                res[kvp.Key] = kvp.Value;
            }

            SaveTheme(themeName);
        }

        public static void SetPlaybarTheme(string playbarTheme)
        {
            if (!AvailablePlaybarThemes.Contains(playbarTheme))
                playbarTheme = "Blue Fire";
            _currentPlaybarTheme = playbarTheme;
            SavePlayOptions();
        }

        /// <summary>
        /// Returns playbar color config: (bgColor, progressColors[], waveAnimSpeed)
        /// </summary>
        public static PlaybarColors GetPlaybarColors()
        {
            return _currentPlaybarTheme switch
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
                _ => new PlaybarColors( // Blue Fire (default)
                    Color.FromArgb(40, 77, 168, 218),
                    new[] {
                        Color.FromArgb(180, 30, 120, 180),
                        Color.FromArgb(220, 77, 168, 218),
                        Color.FromArgb(255, 120, 200, 240)
                    }, 1.5),
            };
        }

        public static string GetMusicServiceUrl(string serviceName, string query)
        {
            string encoded = Uri.EscapeDataString(query);
            return serviceName switch
            {
                "Spotify" => $"https://open.spotify.com/search/{encoded}",
                "YouTube Music" => $"https://music.youtube.com/search?q={encoded}",
                "Tidal" => $"https://listen.tidal.com/search?q={encoded}",
                "Qobuz" => $"https://www.qobuz.com/us-en/search/tracks/{encoded}",
                "Amazon Music" => $"https://music.amazon.com/search/{encoded}",
                "Apple Music" => $"https://music.apple.com/search?term={encoded}",
                "Deezer" => $"https://www.deezer.com/search/{encoded}",
                "SoundCloud" => $"https://soundcloud.com/search?q={encoded}",
                "Bandcamp" => $"https://bandcamp.com/search?q={encoded}",
                "Last.fm" => $"https://www.last.fm/search?q={encoded}",
                _ => $"https://www.google.com/search?q={encoded}"
            };
        }

        /// <summary>
        /// Returns COLORREF (0x00BBGGRR) for the current theme's title bar caption color.
        /// </summary>
        public static int GetTitleBarColorRef()
        {
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

        public static void SavePlayOptions()
        {
            try
            {
                EnsureDir();
                var lines = new List<string>
                {
                    $"AutoPlayNext={AutoPlayNext}",
                    $"AudioNormalization={AudioNormalization}",
                    $"Crossfade={Crossfade}",
                    $"CrossfadeDuration={CrossfadeDuration}",
                    $"PlaybarTheme={_currentPlaybarTheme}",
                    $"Service1={MusicServiceSlots[0]}",
                    $"Service2={MusicServiceSlots[1]}",
                    $"Service3={MusicServiceSlots[2]}",
                    $"Service4={MusicServiceSlots[3]}",
                    $"Service5={MusicServiceSlots[4]}",
                    $"Service6={MusicServiceSlots[5]}",
                    $"VisualizerMode={VisualizerMode}",
                    $"RainbowVisualizer={RainbowVisualizerEnabled}",
                    $"CustomUrl1={CustomServiceUrls[0]}",
                    $"CustomIcon1={CustomServiceIcons[0]}",
                    $"CustomUrl2={CustomServiceUrls[1]}",
                    $"CustomIcon2={CustomServiceIcons[1]}",
                    $"CustomUrl3={CustomServiceUrls[2]}",
                    $"CustomIcon3={CustomServiceIcons[2]}",
                    $"CustomUrl4={CustomServiceUrls[3]}",
                    $"CustomIcon4={CustomServiceIcons[3]}",
                    $"CustomUrl5={CustomServiceUrls[4]}",
                    $"CustomIcon5={CustomServiceIcons[4]}",
                    $"CustomUrl6={CustomServiceUrls[5]}",
                    $"CustomIcon6={CustomServiceIcons[5]}",
                    $"EqualizerEnabled={EqualizerEnabled}",
                    $"EqualizerGains={string.Join(";", EqualizerGains.Select(g => g.ToString("F1")))}",
                    $"DiscordRpc={DiscordRpcEnabled}",
                    $"LastFmEnabled={LastFmEnabled}",
                    $"ExportFormat={ExportFormat}",
                    $"SpatialAudio={SpatialAudioEnabled}",
                    $"ColumnLayout={ColumnLayout}",
                    $"MaxConcurrency={_maxConcurrency}",
                    $"MaxMemoryMB={_maxMemoryMB}"
                };
                File.WriteAllLines(OptionsFile, lines);
            }
            catch { }

            // Save sensitive Last.fm data to Documents
            try
            {
                var sensitiveDir = Path.GetDirectoryName(SensitiveFile)!;
                if (!Directory.Exists(sensitiveDir))
                    Directory.CreateDirectory(sensitiveDir);

                var sensitiveLines = new List<string>
                {
                    $"LastFmApiKey={LastFmApiKey}",
                    $"LastFmApiSecret={LastFmApiSecret}",
                    $"LastFmSessionKey={LastFmSessionKey}",
                    $"LastFmUsername={LastFmUsername}"
                };
                File.WriteAllLines(SensitiveFile, sensitiveLines);
            }
            catch { }
        }

        private static void LoadPlayOptions()
        {
            // Set fixed defaults
            MusicServiceSlots[0] = "Spotify";
            MusicServiceSlots[1] = "YouTube Music";
            MusicServiceSlots[2] = "Tidal";
            MusicServiceSlots[3] = "Qobuz";
            MusicServiceSlots[4] = "Amazon Music";
            MusicServiceSlots[5] = "Apple Music";

            try
            {
                if (!File.Exists(OptionsFile)) return;
                foreach (var line in File.ReadAllLines(OptionsFile))
                {
                    var parts = line.Split('=', 2);
                    if (parts.Length != 2) continue;
                    string key = parts[0], val = parts[1];

                    switch (key)
                    {
                        case "AutoPlayNext": AutoPlayNext = bool.TryParse(val, out var b1) && b1; break;
                        case "AudioNormalization": AudioNormalization = bool.TryParse(val, out var b2) && b2; break;
                        case "Crossfade": Crossfade = bool.TryParse(val, out var b3) && b3; break;
                        case "CrossfadeDuration":
                            if (int.TryParse(val, out var dur) && dur >= 1 && dur <= 10)
                                CrossfadeDuration = dur;
                            break;
                        case "PlaybarTheme":
                            if (AvailablePlaybarThemes.Contains(val)) _currentPlaybarTheme = val;
                            break;
                        case "Service1": if (AvailableMusicServices.Contains(val)) MusicServiceSlots[0] = val; break;
                        case "Service2": if (AvailableMusicServices.Contains(val)) MusicServiceSlots[1] = val; break;
                        case "Service3": if (AvailableMusicServices.Contains(val)) MusicServiceSlots[2] = val; break;
                        case "Service4": if (AvailableMusicServices.Contains(val)) MusicServiceSlots[3] = val; break;
                        case "Service5": if (AvailableMusicServices.Contains(val)) MusicServiceSlots[4] = val; break;
                        case "Service6": if (AvailableMusicServices.Contains(val)) MusicServiceSlots[5] = val; break;
                        case "VisualizerMode": VisualizerMode = bool.TryParse(val, out var bv) && bv; break;
                        case "RainbowVisualizer": RainbowVisualizerEnabled = bool.TryParse(val, out var brv) && brv; break;
                        case "CustomUrl1": CustomServiceUrls[0] = val; break;
                        case "CustomIcon1": CustomServiceIcons[0] = val; break;
                        case "CustomUrl2": CustomServiceUrls[1] = val; break;
                        case "CustomIcon2": CustomServiceIcons[1] = val; break;
                        case "CustomUrl3": CustomServiceUrls[2] = val; break;
                        case "CustomIcon3": CustomServiceIcons[2] = val; break;
                        case "CustomUrl4": CustomServiceUrls[3] = val; break;
                        case "CustomIcon4": CustomServiceIcons[3] = val; break;
                        case "CustomUrl5": CustomServiceUrls[4] = val; break;
                        case "CustomIcon5": CustomServiceIcons[4] = val; break;
                        case "CustomUrl6": CustomServiceUrls[5] = val; break;
                        case "CustomIcon6": CustomServiceIcons[5] = val; break;
                        // Legacy keys (migrate old Custom1/Custom2 to slot 4/5)
                        case "Custom1Url": if (string.IsNullOrEmpty(CustomServiceUrls[4])) CustomServiceUrls[4] = val; break;
                        case "Custom1Icon": if (string.IsNullOrEmpty(CustomServiceIcons[4])) CustomServiceIcons[4] = val; break;
                        case "Custom2Url": if (string.IsNullOrEmpty(CustomServiceUrls[5])) CustomServiceUrls[5] = val; break;
                        case "Custom2Icon": if (string.IsNullOrEmpty(CustomServiceIcons[5])) CustomServiceIcons[5] = val; break;
                        case "EqualizerEnabled": EqualizerEnabled = bool.TryParse(val, out var beq) && beq; break;
                        case "EqualizerGains":
                            var parts2 = val.Split(';');
                            for (int i = 0; i < Math.Min(parts2.Length, 10); i++)
                                if (float.TryParse(parts2[i], out var g)) EqualizerGains[i] = g;
                            break;
                        case "DiscordRpc": DiscordRpcEnabled = bool.TryParse(val, out var bdr) && bdr; break;
                        case "LastFmEnabled": LastFmEnabled = bool.TryParse(val, out var blf) && blf; break;
                        case "ExportFormat":
                            if (new[] { "csv", "txt", "pdf", "xlsx", "docx" }.Contains(val))
                                ExportFormat = val;
                            break;
                        case "SpatialAudio": SpatialAudioEnabled = bool.TryParse(val, out var bsa) && bsa; break;
                        case "ColumnLayout": ColumnLayout = val; break;
                        case "MaxConcurrency":
                            if (int.TryParse(val, out var mc) && mc >= 0 && mc <= 32)
                                _maxConcurrency = mc;
                            break;
                        case "MaxMemoryMB":
                            if (int.TryParse(val, out var mm) && mm >= 0 && mm <= 16384)
                                _maxMemoryMB = mm;
                            break;
                    }
                }
            }
            catch { }

            // Load sensitive Last.fm data from Documents
            LoadSensitiveData();
        }

        private static Dictionary<string, object> GetThemeColors(string theme)
        {
            return theme switch
            {
                "Ocean" => new Dictionary<string, object>
                {
                    ["WindowBg"]            = BrushFrom("#FF0D1B2A"),
                    ["PanelBg"]             = BrushFrom("#FF0A1628"),
                    ["ToolbarBg"]           = BrushFrom("#FF132238"),
                    ["HeaderBg"]            = BrushFrom("#FF1A2D47"),
                    ["GridBg"]              = BrushFrom("#FF0A1628"),
                    ["GridRowBg"]           = BrushFrom("#FF0D1B2A"),
                    ["GridAltRowBg"]        = BrushFrom("#FF112240"),
                    ["BorderColor"]         = BrushFrom("#FF1E3A5F"),
                    ["InputBg"]             = BrushFrom("#FF0F1D30"),
                    ["SelectionBg"]         = BrushFrom("#FF1A4B7A"),
                    ["ButtonBg"]            = BrushFrom("#FF162D4A"),
                    ["ButtonBorder"]        = BrushFrom("#FF1E3A5F"),
                    ["ButtonHover"]         = BrushFrom("#FF1E4468"),
                    ["ButtonPressed"]       = BrushFrom("#FF4DA8DA"),
                    ["AccentColor"]         = BrushFrom("#FF4DA8DA"),
                    ["TextPrimary"]         = BrushFrom("#FFD0E4F5"),
                    ["TextSecondary"]       = BrushFrom("#FF8BB8D6"),
                    ["TextMuted"]           = BrushFrom("#FF5A8AAD"),
                    ["TextDim"]             = BrushFrom("#FF2E5070"),
                    ["ScrollBg"]            = BrushFrom("#FF0F1D30"),
                    ["ScrollThumb"]         = BrushFrom("#FF3A6080"),
                    ["ScrollThumbHover"]    = BrushFrom("#FF4A7898"),
                    ["GridLineColor"]       = BrushFrom("#FF152A42"),
                    ["RowHoverBg"]          = BrushFrom("#FF142838"),
                    ["SplitterBg"]          = BrushFrom("#FF132238"),
                    ["ProgressBg"]          = BrushFrom("#FF162D4A"),
                },
                "Light" => new Dictionary<string, object>
                {
                    ["WindowBg"]            = BrushFrom("#FFF5F5F5"),
                    ["PanelBg"]             = BrushFrom("#FFFFFFFF"),
                    ["ToolbarBg"]           = BrushFrom("#FFE8E8EC"),
                    ["HeaderBg"]            = BrushFrom("#FFDCDCE0"),
                    ["GridBg"]              = BrushFrom("#FFFFFFFF"),
                    ["GridRowBg"]           = BrushFrom("#FFFFFFFF"),
                    ["GridAltRowBg"]        = BrushFrom("#FFF8F8FA"),
                    ["BorderColor"]         = BrushFrom("#FFCCCCCC"),
                    ["InputBg"]             = BrushFrom("#FFFFFFFF"),
                    ["SelectionBg"]         = BrushFrom("#FF0078D4"),
                    ["ButtonBg"]            = BrushFrom("#FFE1E1E1"),
                    ["ButtonBorder"]        = BrushFrom("#FFBBBBBB"),
                    ["ButtonHover"]         = BrushFrom("#FFD0D0D0"),
                    ["ButtonPressed"]       = BrushFrom("#FF0078D4"),
                    ["AccentColor"]         = BrushFrom("#FF0078D4"),
                    ["TextPrimary"]         = BrushFrom("#FF1E1E1E"),
                    ["TextSecondary"]       = BrushFrom("#FF444444"),
                    ["TextMuted"]           = BrushFrom("#FF888888"),
                    ["TextDim"]             = BrushFrom("#FFBBBBBB"),
                    ["ScrollBg"]            = BrushFrom("#FFE8E8E8"),
                    ["ScrollThumb"]         = BrushFrom("#FFA0A0A0"),
                    ["ScrollThumbHover"]    = BrushFrom("#FF808080"),
                    ["GridLineColor"]       = BrushFrom("#FFE0E0E0"),
                    ["RowHoverBg"]          = BrushFrom("#FFEAF1FB"),
                    ["SplitterBg"]          = BrushFrom("#FFDCDCE0"),
                    ["ProgressBg"]          = BrushFrom("#FFE0E0E0"),
                },
                "Amethyst" => new Dictionary<string, object>
                {
                    ["WindowBg"]            = BrushFrom("#FF1A1228"),
                    ["PanelBg"]             = BrushFrom("#FF150E22"),
                    ["ToolbarBg"]           = BrushFrom("#FF221838"),
                    ["HeaderBg"]            = BrushFrom("#FF2C2044"),
                    ["GridBg"]              = BrushFrom("#FF150E22"),
                    ["GridRowBg"]           = BrushFrom("#FF1A1228"),
                    ["GridAltRowBg"]        = BrushFrom("#FF201638"),
                    ["BorderColor"]         = BrushFrom("#FF3D2A5C"),
                    ["InputBg"]             = BrushFrom("#FF1E142E"),
                    ["SelectionBg"]         = BrushFrom("#FF5A2E8C"),
                    ["ButtonBg"]            = BrushFrom("#FF2A1E42"),
                    ["ButtonBorder"]        = BrushFrom("#FF4A3468"),
                    ["ButtonHover"]         = BrushFrom("#FF3A2858"),
                    ["ButtonPressed"]       = BrushFrom("#FF8B5CF6"),
                    ["AccentColor"]         = BrushFrom("#FF8B5CF6"),
                    ["TextPrimary"]         = BrushFrom("#FFE0D4F5"),
                    ["TextSecondary"]       = BrushFrom("#FFB8A0D6"),
                    ["TextMuted"]           = BrushFrom("#FF7860A0"),
                    ["TextDim"]             = BrushFrom("#FF463060"),
                    ["ScrollBg"]            = BrushFrom("#FF1E142E"),
                    ["ScrollThumb"]         = BrushFrom("#FF5A4480"),
                    ["ScrollThumbHover"]    = BrushFrom("#FF7860A0"),
                    ["GridLineColor"]       = BrushFrom("#FF251A3A"),
                    ["RowHoverBg"]          = BrushFrom("#FF241A36"),
                    ["SplitterBg"]          = BrushFrom("#FF221838"),
                    ["ProgressBg"]          = BrushFrom("#FF2A1E42"),
                },
                "Dreamsicle" => new Dictionary<string, object>
                {
                    ["WindowBg"]            = BrushFrom("#FF1F1510"),
                    ["PanelBg"]             = BrushFrom("#FF1A120C"),
                    ["ToolbarBg"]           = BrushFrom("#FF2E1E14"),
                    ["HeaderBg"]            = BrushFrom("#FF3A2818"),
                    ["GridBg"]              = BrushFrom("#FF1A120C"),
                    ["GridRowBg"]           = BrushFrom("#FF1F1510"),
                    ["GridAltRowBg"]        = BrushFrom("#FF2A1C12"),
                    ["BorderColor"]         = BrushFrom("#FF5A3820"),
                    ["InputBg"]             = BrushFrom("#FF241A12"),
                    ["SelectionBg"]         = BrushFrom("#FF8B4513"),
                    ["ButtonBg"]            = BrushFrom("#FF352414"),
                    ["ButtonBorder"]        = BrushFrom("#FF6B4228"),
                    ["ButtonHover"]         = BrushFrom("#FF45301C"),
                    ["ButtonPressed"]       = BrushFrom("#FFFF8C42"),
                    ["AccentColor"]         = BrushFrom("#FFFF8C42"),
                    ["TextPrimary"]         = BrushFrom("#FFF5E0CC"),
                    ["TextSecondary"]       = BrushFrom("#FFD6A87A"),
                    ["TextMuted"]           = BrushFrom("#FF9A7050"),
                    ["TextDim"]             = BrushFrom("#FF5A3E28"),
                    ["ScrollBg"]            = BrushFrom("#FF241A12"),
                    ["ScrollThumb"]         = BrushFrom("#FF7A5030"),
                    ["ScrollThumbHover"]    = BrushFrom("#FF9A6840"),
                    ["GridLineColor"]       = BrushFrom("#FF2E1E14"),
                    ["RowHoverBg"]          = BrushFrom("#FF2E2014"),
                    ["SplitterBg"]          = BrushFrom("#FF2E1E14"),
                    ["ProgressBg"]          = BrushFrom("#FF352414"),
                },
                "Goldenrod" => new Dictionary<string, object>
                {
                    ["WindowBg"]            = BrushFrom("#FF1E1C0E"),
                    ["PanelBg"]             = BrushFrom("#FF1A180A"),
                    ["ToolbarBg"]           = BrushFrom("#FF383010"),
                    ["HeaderBg"]            = BrushFrom("#FF4A4018"),
                    ["GridBg"]              = BrushFrom("#FF1A180A"),
                    ["GridRowBg"]           = BrushFrom("#FF1E1C0E"),
                    ["GridAltRowBg"]        = BrushFrom("#FF2E2810"),
                    ["BorderColor"]         = BrushFrom("#FF6B5A18"),
                    ["InputBg"]             = BrushFrom("#FF262010"),
                    ["SelectionBg"]         = BrushFrom("#FF9A8010"),
                    ["ButtonBg"]            = BrushFrom("#FF3E3510"),
                    ["ButtonBorder"]        = BrushFrom("#FF7A6820"),
                    ["ButtonHover"]         = BrushFrom("#FF504618"),
                    ["ButtonPressed"]       = BrushFrom("#FFE8B811"),
                    ["AccentColor"]         = BrushFrom("#FFE8B811"),
                    ["TextPrimary"]         = BrushFrom("#FFF5ECCC"),
                    ["TextSecondary"]       = BrushFrom("#FFDCC680"),
                    ["TextMuted"]           = BrushFrom("#FFAA9445"),
                    ["TextDim"]             = BrushFrom("#FF6A5828"),
                    ["ScrollBg"]            = BrushFrom("#FF262010"),
                    ["ScrollThumb"]         = BrushFrom("#FF8A7428"),
                    ["ScrollThumbHover"]    = BrushFrom("#FFAA9438"),
                    ["GridLineColor"]       = BrushFrom("#FF322C10"),
                    ["RowHoverBg"]          = BrushFrom("#FF322C14"),
                    ["SplitterBg"]          = BrushFrom("#FF383010"),
                    ["ProgressBg"]          = BrushFrom("#FF3E3510"),
                },
                "Emerald" => new Dictionary<string, object>
                {
                    ["WindowBg"]            = BrushFrom("#FF0F1C14"),
                    ["PanelBg"]             = BrushFrom("#FF0A1810"),
                    ["ToolbarBg"]           = BrushFrom("#FF14281C"),
                    ["HeaderBg"]            = BrushFrom("#FF1A3424"),
                    ["GridBg"]              = BrushFrom("#FF0A1810"),
                    ["GridRowBg"]           = BrushFrom("#FF0F1C14"),
                    ["GridAltRowBg"]        = BrushFrom("#FF12241A"),
                    ["BorderColor"]         = BrushFrom("#FF1E5A3A"),
                    ["InputBg"]             = BrushFrom("#FF0E2018"),
                    ["SelectionBg"]         = BrushFrom("#FF1A7A4A"),
                    ["ButtonBg"]            = BrushFrom("#FF162D20"),
                    ["ButtonBorder"]        = BrushFrom("#FF1E5A3A"),
                    ["ButtonHover"]         = BrushFrom("#FF1E4430"),
                    ["ButtonPressed"]       = BrushFrom("#FF2ECC71"),
                    ["AccentColor"]         = BrushFrom("#FF2ECC71"),
                    ["TextPrimary"]         = BrushFrom("#FFD0F5E0"),
                    ["TextSecondary"]       = BrushFrom("#FF8BD6AA"),
                    ["TextMuted"]           = BrushFrom("#FF5AAD7A"),
                    ["TextDim"]             = BrushFrom("#FF2E7050"),
                    ["ScrollBg"]            = BrushFrom("#FF0E2018"),
                    ["ScrollThumb"]         = BrushFrom("#FF3A8060"),
                    ["ScrollThumbHover"]    = BrushFrom("#FF4A9870"),
                    ["GridLineColor"]       = BrushFrom("#FF142A1E"),
                    ["RowHoverBg"]          = BrushFrom("#FF142820"),
                    ["SplitterBg"]          = BrushFrom("#FF14281C"),
                    ["ProgressBg"]          = BrushFrom("#FF162D20"),
                },
                "Blurple" => new Dictionary<string, object>
                {
                    ["WindowBg"]            = BrushFrom("#FF1E1F3B"),
                    ["PanelBg"]             = BrushFrom("#FF1A1B36"),
                    ["ToolbarBg"]           = BrushFrom("#FF2C2D56"),
                    ["HeaderBg"]            = BrushFrom("#FF353668"),
                    ["GridBg"]              = BrushFrom("#FF1A1B36"),
                    ["GridRowBg"]           = BrushFrom("#FF1E1F3B"),
                    ["GridAltRowBg"]        = BrushFrom("#FF272850"),
                    ["BorderColor"]         = BrushFrom("#FF4A4B8A"),
                    ["InputBg"]             = BrushFrom("#FF222344"),
                    ["SelectionBg"]         = BrushFrom("#FF4752C4"),
                    ["ButtonBg"]            = BrushFrom("#FF30325E"),
                    ["ButtonBorder"]        = BrushFrom("#FF5865F2"),
                    ["ButtonHover"]         = BrushFrom("#FF3D3F76"),
                    ["ButtonPressed"]       = BrushFrom("#FF7289DA"),
                    ["AccentColor"]         = BrushFrom("#FF5865F2"),
                    ["TextPrimary"]         = BrushFrom("#FFE0E1FF"),
                    ["TextSecondary"]       = BrushFrom("#FFA5A7D4"),
                    ["TextMuted"]           = BrushFrom("#FF7375B0"),
                    ["TextDim"]             = BrushFrom("#FF464878"),
                    ["ScrollBg"]            = BrushFrom("#FF222344"),
                    ["ScrollThumb"]         = BrushFrom("#FF5865F2"),
                    ["ScrollThumbHover"]    = BrushFrom("#FF7289DA"),
                    ["GridLineColor"]       = BrushFrom("#FF2A2B50"),
                    ["RowHoverBg"]          = BrushFrom("#FF2E2F58"),
                    ["SplitterBg"]          = BrushFrom("#FF2C2D56"),
                    ["ProgressBg"]          = BrushFrom("#FF30325E"),
                },
                "Crimson" => new Dictionary<string, object>
                {
                    ["WindowBg"]            = BrushFrom("#FF1E1012"),
                    ["PanelBg"]             = BrushFrom("#FF180C0E"),
                    ["ToolbarBg"]           = BrushFrom("#FF2E1418"),
                    ["HeaderBg"]            = BrushFrom("#FF3A1C22"),
                    ["GridBg"]              = BrushFrom("#FF180C0E"),
                    ["GridRowBg"]           = BrushFrom("#FF1E1012"),
                    ["GridAltRowBg"]        = BrushFrom("#FF281418"),
                    ["BorderColor"]         = BrushFrom("#FF5A2030"),
                    ["InputBg"]             = BrushFrom("#FF221014"),
                    ["SelectionBg"]         = BrushFrom("#FF8B1A2A"),
                    ["ButtonBg"]            = BrushFrom("#FF351820"),
                    ["ButtonBorder"]        = BrushFrom("#FF6B2838"),
                    ["ButtonHover"]         = BrushFrom("#FF452028"),
                    ["ButtonPressed"]       = BrushFrom("#FFDC143C"),
                    ["AccentColor"]         = BrushFrom("#FFDC143C"),
                    ["TextPrimary"]         = BrushFrom("#FFF5D0D4"),
                    ["TextSecondary"]       = BrushFrom("#FFD6909A"),
                    ["TextMuted"]           = BrushFrom("#FF9A5060"),
                    ["TextDim"]             = BrushFrom("#FF5A2838"),
                    ["ScrollBg"]            = BrushFrom("#FF221014"),
                    ["ScrollThumb"]         = BrushFrom("#FF7A3040"),
                    ["ScrollThumbHover"]    = BrushFrom("#FF9A4050"),
                    ["GridLineColor"]       = BrushFrom("#FF2E1418"),
                    ["RowHoverBg"]          = BrushFrom("#FF2E1820"),
                    ["SplitterBg"]          = BrushFrom("#FF2E1418"),
                    ["ProgressBg"]          = BrushFrom("#FF351820"),
                },
                "Brown" => new Dictionary<string, object>
                {
                    ["WindowBg"]            = BrushFrom("#FF1E1810"),
                    ["PanelBg"]             = BrushFrom("#FF1A140E"),
                    ["ToolbarBg"]           = BrushFrom("#FF2E2216"),
                    ["HeaderBg"]            = BrushFrom("#FF3A2C1E"),
                    ["GridBg"]              = BrushFrom("#FF1A140E"),
                    ["GridRowBg"]           = BrushFrom("#FF1E1810"),
                    ["GridAltRowBg"]        = BrushFrom("#FF281E14"),
                    ["BorderColor"]         = BrushFrom("#FF5A4228"),
                    ["InputBg"]             = BrushFrom("#FF221A12"),
                    ["SelectionBg"]         = BrushFrom("#FF7A5830"),
                    ["ButtonBg"]            = BrushFrom("#FF352818"),
                    ["ButtonBorder"]        = BrushFrom("#FF6B4E2E"),
                    ["ButtonHover"]         = BrushFrom("#FF453420"),
                    ["ButtonPressed"]       = BrushFrom("#FFC08040"),
                    ["AccentColor"]         = BrushFrom("#FFC08040"),
                    ["TextPrimary"]         = BrushFrom("#FFF0E0CC"),
                    ["TextSecondary"]       = BrushFrom("#FFD0B08A"),
                    ["TextMuted"]           = BrushFrom("#FF907050"),
                    ["TextDim"]             = BrushFrom("#FF584030"),
                    ["ScrollBg"]            = BrushFrom("#FF221A12"),
                    ["ScrollThumb"]         = BrushFrom("#FF7A5A38"),
                    ["ScrollThumbHover"]    = BrushFrom("#FF9A7048"),
                    ["GridLineColor"]       = BrushFrom("#FF2A2014"),
                    ["RowHoverBg"]          = BrushFrom("#FF2E2218"),
                    ["SplitterBg"]          = BrushFrom("#FF2E2216"),
                    ["ProgressBg"]          = BrushFrom("#FF352818"),
                },
                _ => new Dictionary<string, object> // Dark (default)
                {
                    ["WindowBg"]            = BrushFrom("#FF1E1E1E"),
                    ["PanelBg"]             = BrushFrom("#FF181818"),
                    ["ToolbarBg"]           = BrushFrom("#FF2D2D30"),
                    ["HeaderBg"]            = BrushFrom("#FF333337"),
                    ["GridBg"]              = BrushFrom("#FF181818"),
                    ["GridRowBg"]           = BrushFrom("#FF1E1E1E"),
                    ["GridAltRowBg"]        = BrushFrom("#FF252526"),
                    ["BorderColor"]         = BrushFrom("#FF3F3F46"),
                    ["InputBg"]             = BrushFrom("#FF2A2A2E"),
                    ["SelectionBg"]         = BrushFrom("#FF264F78"),
                    ["ButtonBg"]            = BrushFrom("#FF3C3C3C"),
                    ["ButtonBorder"]        = BrushFrom("#FF555555"),
                    ["ButtonHover"]         = BrushFrom("#FF505050"),
                    ["ButtonPressed"]       = BrushFrom("#FF007ACC"),
                    ["AccentColor"]         = BrushFrom("#FF007ACC"),
                    ["TextPrimary"]         = BrushFrom("#FFD4D4D4"),
                    ["TextSecondary"]       = BrushFrom("#FFB0B0B0"),
                    ["TextMuted"]           = BrushFrom("#FF888888"),
                    ["TextDim"]             = BrushFrom("#FF555555"),
                    ["ScrollBg"]            = BrushFrom("#FF2A2A2E"),
                    ["ScrollThumb"]         = BrushFrom("#FF686868"),
                    ["ScrollThumbHover"]    = BrushFrom("#FF888888"),
                    ["GridLineColor"]       = BrushFrom("#FF2A2A2E"),
                    ["RowHoverBg"]          = BrushFrom("#FF2A2D2E"),
                    ["SplitterBg"]          = BrushFrom("#FF2D2D30"),
                    ["ProgressBg"]          = BrushFrom("#FF333337"),
                },
            };
        }

        private static SolidColorBrush BrushFrom(string hex)
        {
            var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
            brush.Freeze();
            return brush;
        }

        private static string LoadSavedTheme()
        {
            try
            {
                if (File.Exists(ThemeFile))
                    return File.ReadAllText(ThemeFile).Trim();
            }
            catch { }
            return "Dark";
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
