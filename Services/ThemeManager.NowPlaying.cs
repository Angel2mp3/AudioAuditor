using System;

namespace AudioQualityChecker.Services
{
    /// <summary>Now Playing lyric display modes. Standard = active bright, inactive dimmed gray;
    /// Blur = inactive lines blurred/faded; Uniform = all lines the same near-white, active just larger.</summary>
    public enum NpLyricDisplayMode { Standard, Blur, Uniform }

    public static partial class ThemeManager
    {
        // ─── Now Playing panel preferences ───

        public static bool NpVisualizerEnabled { get; set; }
        public static bool NpColorMatchEnabled { get; set; }
        public static bool NpColorCacheEnabled { get; set; } = true;
        public static bool NpColorCachePersist { get; set; } = true;
        public static bool NpRememberManualColorPicks { get; set; } = true;
        // How many colors the NP eyedropper picker collects per session (default 3, max 6).
        // The first three drive the primary/secondary/tertiary palette (icons, glow, viz);
        // extras enrich the NP background gradient.
        private static int _npColorPickerMaxColors = 3;
        public static int NpColorPickerMaxColors
        {
            get => _npColorPickerMaxColors;
            set => _npColorPickerMaxColors = Math.Clamp(value, 3, 6);
        }
        public static bool NpAlbumBackdropEnabled { get; set; }
        public static string NpBackgroundMode { get; set; } = "AlbumArt";
        public static string NpCustomBackgroundImagePath { get; set; } = "";
        public static string NpCustomBackgroundColors { get; set; } = "";
        public static double NpBackgroundBlur { get; set; } = 24.0;
        public static double NpBackgroundOpacity { get; set; } = 0.32;
        public static double NpBackgroundHorizontalPosition { get; set; } = 0.5;
        public static double NpBackgroundVerticalPosition { get; set; } = 0.5;
        public static double NpBackgroundFocusX
        {
            get => NpBackgroundHorizontalPosition;
            set => NpBackgroundHorizontalPosition = Math.Clamp(value, 0, 1);
        }
        public static double NpBackgroundFocusY
        {
            get => NpBackgroundVerticalPosition;
            set => NpBackgroundVerticalPosition = Math.Clamp(value, 0, 1);
        }
        public static double NpBackgroundZoom { get; set; } = 1.0;
        public static double NpBackgroundBrightness { get; set; } = 1.0;
        public static string NpBackgroundAnimationMode { get; set; } = "Off";
        // Color Drift (animated gradient "glow" background) can run UNDER a particle
        // mode. It lives on its own layer (NpBgGradient) separate from the particle
        // canvas, so it's a standalone toggle rather than a mutually-exclusive mode.
        // When the mode picker itself is set to "Color Drift", that still works and
        // implies this on; this flag lets it also pair with Stars/Rain/Snow/etc.
        public static bool NpColorDriftBackgroundEnabled { get; set; }
        public static bool NpBackgroundUseAlbumColors { get; set; }
        public static bool NpBackgroundCycleEnabled { get; set; }
        public static double NpBackgroundCycleSpeed { get; set; } = 1.0;
        public static bool NpBackgroundCycleOnSongChange { get; set; }
        public static double NpStarDensity { get; set; } = 1.0;
        public static double NpShootingStarDensity { get; set; } = 1.0;
        public static bool NpShootingStarsEnabled { get; set; } = true;
        public static double NpRainIntensity { get; set; } = 1.0;
        public static bool NpRainLightningEnabled { get; set; }
        public static bool NpRainLightningPromptShown { get; set; }
        public static double NpRainLightningAmount { get; set; } = 1.0;
        public static double NpSnowDensity { get; set; } = 1.0;
        public static double NpSnowflakeAmount { get; set; } = 1.0;
        public static double NpUnderwaterBubbleDensity { get; set; } = 1.0;
        public static double NpUnderwaterCausticIntensity { get; set; } = 1.0;
        public static bool NpUnderwaterFishEnabled { get; set; } = true;
        public static bool NpUnderwaterSeaweedEnabled { get; set; } = true;
        public static double NpBackgroundAnimationSpeed { get; set; } = 1.0;
        public static string MainBackgroundImagePath { get; set; } = "";
        public static double MainBackgroundOpacity { get; set; } = 0.18;
        public static double MainBackgroundBlur { get; set; } = 16.0;
        public static string NpCoverShapeMode { get; set; } = "Default";
        public static string MiniCoverShapeMode { get; set; } = "Rounded";
        public static bool MiniPlayerAlwaysOnTop { get; set; } = true;

        // ─── Mini Player remembered state (persisted independently of the main window) ───
        public static int MiniVisualizerStyle { get; set; } = -1; // -1 = unset (seed from main on first run); 0=Bars,1=Mirror,2=Scope,3=Off,4=Circles
        public static bool MiniColorMatchEnabled { get; set; }
        public static double MiniPlayerLeft { get; set; } = double.NaN;   // NaN = no saved position
        public static double MiniPlayerTop { get; set; } = double.NaN;
        public static double MiniPlayerWidth { get; set; }               // 0 = use default width
        public static double MiniPlayerBaseHeight { get; set; }          // 0 = use content default (no-visualizer height)

        // ─── Main toolbar button visibility ───
        public static bool ShowWrappedButton { get; set; } = true;
        public static bool ShowMiniPlayerButton { get; set; } = true;
        public static bool ShowMusicServiceButtons { get; set; } = true;
        public static bool NpLyricsHidden { get; set; }
        public static bool NpTranslateEnabled { get; set; }
        public static bool NpAutoSaveLyricsEnabled { get; set; }
        public static bool NpKaraokeEnabled { get; set; }
        public static NpLyricDisplayMode NpLyricMode { get; set; } = NpLyricDisplayMode.Standard;
        public static double NpFocusedLyricsBlurRadius { get; set; } = 6.5;
        public static bool NpCoverGlowMotionEnabled { get; set; } = true;
        public static GlowMotionMode NpGlowMotionMode { get; set; } = GlowMotionMode.Swirl;
        public static int NpVisualizerStyle { get; set; }
        public static int NpVizPlacement { get; set; } // 0=full-width, 1=under-cover
        public static bool NpSubCoverShowArtist { get; set; } = true;

        // ─── NP "look up this song" search services ───
        // Independent of the main-window service slots so the NP screen can offer a
        // different set. Seeded from the main config on first run (see SeedNpSearchServicesFromMain),
        // then edited separately in Settings → Now Playing.
        public static string[] NpSearchServiceSlots { get; } = new string[6];
        public static bool[] NpSearchServiceSlotVisible { get; } = new bool[6] { true, true, true, true, true, true };
        public static string[] NpSearchCustomServiceUrls { get; } = new string[6] { "", "", "", "", "", "" };
        public static string[] NpSearchCustomServiceIcons { get; } = new string[6] { "", "", "", "", "", "" };
        // False until the NP slots have been seeded/saved at least once, so a first run
        // copies the user's existing main-window services instead of showing blanks.
        public static bool NpSearchServicesConfigured { get; set; }

        /// <summary>
        /// One-time copy of the main-window service config into the NP slots, used when
        /// the user hasn't customized NP search yet. Idempotent: only copies when not
        /// already configured.
        /// </summary>
        public static void SeedNpSearchServicesFromMain(bool force = false)
        {
            if (NpSearchServicesConfigured && !force)
                return;
            for (int i = 0; i < 6; i++)
            {
                NpSearchServiceSlots[i] = MusicServiceSlots[i];
                NpSearchServiceSlotVisible[i] = MusicServiceSlotVisible[i];
                NpSearchCustomServiceUrls[i] = CustomServiceUrls[i];
                NpSearchCustomServiceIcons[i] = CustomServiceIcons[i];
            }
        }

        // NP bottom-bar optional-button customization. Order = comma-joined stable button IDs in
        // display order; Hidden = comma-joined IDs the user chose to hide. Empty = use defaults.
        // Only the optional buttons participate; transport/volume/back are always shown and fixed.
        public static string NpButtonOrder { get; set; } = "";
        public static string NpButtonHidden { get; set; } = "";
        // Transport (shuffle/loop/prev/play/next) display order — reorderable but never removable.
        public static string NpTransportOrder { get; set; } = "";

        // NP custom layout sizes (0 = use default for current window state)
        public static int NpCoverSize { get; set; }
        public static int NpTitleSize { get; set; }
        public static int NpSubTextSize { get; set; }
        public static int NpLyricsSize { get; set; }
        public static int NpVizSize { get; set; }
        public static double NpCoverGlowSize { get; set; } = 1.0;
        public static int NpLyricsOffsetX { get; set; }

        // NP element position offsets (px, 0 = default)
        public static int NpCoverOffsetX { get; set; }
        public static int NpCoverOffsetY { get; set; }
        public static int NpTitleOffsetX { get; set; }
        public static int NpTitleOffsetY { get; set; }
        public static int NpArtistOffsetX { get; set; }
        public static int NpArtistOffsetY { get; set; }
        public static int NpVizOffsetY { get; set; }

        // Fullscreen NP layout preset
        public static int NpFullscreenCoverSize { get; set; }
        public static int NpFullscreenTitleSize { get; set; }
        public static int NpFullscreenSubTextSize { get; set; }
        public static int NpFullscreenLyricsSize { get; set; }
        public static int NpFullscreenVizSize { get; set; }
        public static int NpFullscreenLyricsOffsetX { get; set; }
        public static int NpFullscreenCoverOffsetX { get; set; }
        public static int NpFullscreenCoverOffsetY { get; set; }
        public static int NpFullscreenTitleOffsetX { get; set; }
        public static int NpFullscreenTitleOffsetY { get; set; }
        public static int NpFullscreenArtistOffsetX { get; set; }
        public static int NpFullscreenArtistOffsetY { get; set; }
        public static int NpFullscreenVizOffsetY { get; set; }
        public static int NpFullscreenVizPlacement { get; set; }

        // Visualizer-on layout presets (legacy windowed/fullscreen = viz-off)
        public static int NpVizOnCoverSize { get; set; }
        public static int NpVizOnTitleSize { get; set; }
        public static int NpVizOnSubTextSize { get; set; }
        public static int NpVizOnLyricsSize { get; set; }
        public static int NpVizOnVizSize { get; set; }
        public static int NpVizOnLyricsOffsetX { get; set; }
        public static int NpVizOnCoverOffsetX { get; set; }
        public static int NpVizOnCoverOffsetY { get; set; }
        public static int NpVizOnTitleOffsetX { get; set; }
        public static int NpVizOnTitleOffsetY { get; set; }
        public static int NpVizOnArtistOffsetX { get; set; }
        public static int NpVizOnArtistOffsetY { get; set; }
        public static int NpVizOnVizOffsetY { get; set; }
        public static int NpVizOnPlacement { get; set; }
        public static int NpFullscreenVizOnCoverSize { get; set; }
        public static int NpFullscreenVizOnTitleSize { get; set; }
        public static int NpFullscreenVizOnSubTextSize { get; set; }
        public static int NpFullscreenVizOnLyricsSize { get; set; }
        public static int NpFullscreenVizOnVizSize { get; set; }
        public static int NpFullscreenVizOnLyricsOffsetX { get; set; }
        public static int NpFullscreenVizOnCoverOffsetX { get; set; }
        public static int NpFullscreenVizOnCoverOffsetY { get; set; }
        public static int NpFullscreenVizOnTitleOffsetX { get; set; }
        public static int NpFullscreenVizOnTitleOffsetY { get; set; }
        public static int NpFullscreenVizOnArtistOffsetX { get; set; }
        public static int NpFullscreenVizOnArtistOffsetY { get; set; }
        public static int NpFullscreenVizOnVizOffsetY { get; set; }
        public static int NpFullscreenVizOnPlacement { get; set; }

        public static string NormalizeNpBackgroundAnimationMode(string? mode)
        {
            if (string.IsNullOrWhiteSpace(mode))
                return "Off";

            return mode.Trim().ToLowerInvariant() switch
            {
                "color drift" or "colordrift" or "drift" => "Color Drift",
                "stars" or "starfield" => "Stars",
                "rain" or "rainfall" => "Rain",
                "snow" or "snowfall" => "Snow",
                "leaves" or "leaf" => "Leaves",
                "underwater" or "under the sea" or "ocean" or "sea" => "Underwater",
                _ => "Off"
            };
        }

        public static string NormalizeCoverShapeMode(string? mode)
        {
            if (string.IsNullOrWhiteSpace(mode))
                return "Default";

            return mode.Trim().ToLowerInvariant() switch
            {
                "rounded" => "Rounded",
                "circle" or "circular" => "Circle",
                _ => "Default"
            };
        }

        public static double ClampNpStarDensity(double value) => Math.Clamp(value, 0.25, 2.5);

        public static double ClampNpShootingStarDensity(double value) => Math.Clamp(value, 0.25, 4.0);

        public static double ClampNpRainIntensity(double value) => Math.Clamp(value, 0.25, 2.5);

        public static double ClampNpRainLightningAmount(double value) => Math.Clamp(value, 0.0, 3.0);

        public static double ClampNpSnowDensity(double value) => Math.Clamp(value, 0.25, 2.5);

        public static double ClampNpSnowflakeAmount(double value) => Math.Clamp(value, 0.25, 2.5);

        public static double ClampNpUnderwaterBubbleDensity(double value) => Math.Clamp(value, 0.25, 2.5);

        public static double ClampNpUnderwaterCausticIntensity(double value) => Math.Clamp(value, 0.0, 2.0);

        public static double ClampNpBackgroundAnimationSpeed(double value) => Math.Clamp(value, 0.4, 2.5);
    }
}
