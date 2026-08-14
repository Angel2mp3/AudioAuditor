using System;
using System.Collections.Generic;
using System.Globalization;

namespace AudioQualityChecker.Services
{
    /// <summary>Now Playing lyric display modes. Standard = active bright, inactive dimmed gray;
    /// Blur = inactive lines blurred/faded; Uniform = all lines the same near-white, active just larger.</summary>
    public enum NpLyricDisplayMode { Standard, Blur, Uniform }

    /// <summary>Which surface categories ColorMatch is allowed to recolor. Lets a user keep, say,
    /// text readable while still tinting backgrounds/buttons with album-art colors.</summary>
    [Flags]
    public enum ColorMatchTarget
    {
        None = 0,
        Backgrounds = 1,
        ButtonsAndIcons = 2,
        Text = 4,
        All = Backgrounds | ButtonsAndIcons | Text
    }

    /// <summary>How the album-cover glow moves behind the artwork.</summary>
    public enum GlowMotionMode { Swirl, LinearLR, LinearRL, Random, DiagonalSweep, Orbit, ColorDrift }

    /// <summary>
    /// Every Now Playing / background / mini-player preference, plus the read and write halves of
    /// their <c>options.txt</c> persistence.
    ///
    /// This lives in Core because both UI builds need the identical set. Holding it in each build's
    /// own theme manager meant one copy could gain a property, a clamp bound or a migration the
    /// other never got, and the two would then disagree about the same file. Every value here is
    /// formatted and parsed with <see cref="CultureInfo.InvariantCulture"/> on both sides, so a
    /// comma-decimal locale round-trips the same as an en-US one.
    /// </summary>
    public static class NowPlayingSettings
    {
        // ─── Now Playing panel preferences ───

        public static bool NpVisualizerEnabled { get; set; }
        public static bool NpColorMatchEnabled { get; set; }
        public static ColorMatchTarget NpColorMatchTargets { get; set; } = ColorMatchTarget.All;
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

        // ─── Queue / Settings window ColorMatch: independent instead of always inheriting Main ───
        public static bool QueueColorMatchEnabled { get; set; } = true;
        public static bool SettingsColorMatchEnabled { get; set; } = true;
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
        // different set. Seeded from the main config on first run (see SeedNpSearchServices),
        // then edited separately in Settings → Now Playing.
        public static string[] NpSearchServiceSlots { get; } = new string[6];
        public static bool[] NpSearchServiceSlotVisible { get; } = new bool[6] { true, true, true, true, true, true };
        public static string[] NpSearchCustomServiceUrls { get; } = new string[6] { "", "", "", "", "", "" };
        public static string[] NpSearchCustomServiceIcons { get; } = new string[6] { "", "", "", "", "", "" };
        // False until the NP slots have been seeded/saved at least once, so a first run
        // copies the user's existing main-window services instead of showing blanks.
        public static bool NpSearchServicesConfigured { get; set; }

        /// <summary>
        /// One-time copy of the host build's main-window service config into the NP slots, used when
        /// the user hasn't customized NP search yet. Idempotent: only copies when not already
        /// configured. The source arrays are passed in because each build owns its own main-window
        /// slots.
        /// </summary>
        public static void SeedNpSearchServices(
            string[] slots, bool[] slotVisible, string[] customUrls, string[] customIcons, bool force = false)
        {
            if (NpSearchServicesConfigured && !force)
                return;
            for (int i = 0; i < 6; i++)
            {
                NpSearchServiceSlots[i] = slots[i];
                NpSearchServiceSlotVisible[i] = slotVisible[i];
                NpSearchCustomServiceUrls[i] = customUrls[i];
                NpSearchCustomServiceIcons[i] = customIcons[i];
            }
        }

        // NP bottom-bar optional-button customization. Order = comma-joined stable button IDs in
        // display order; Hidden = comma-joined IDs the user chose to hide. Empty = use defaults.
        // Only the optional buttons participate; transport/volume/back are always shown and fixed.
        public static string NpButtonOrder { get; set; } = "";
        public static string NpButtonHidden { get; set; } = "";
        // Transport (shuffle/loop/prev/play/next) display order — reorderable but never removable.
        public static string NpTransportOrder { get; set; } = "";

        // NP song-info row customization — same scheme as the bottom-bar buttons. Order = comma-joined
        // stable item IDs in display order; Hidden = comma-joined IDs the user chose to hide. Empty =
        // use defaults. Covers both the text specs and the MQA/ALAC/AI/Fake-Stereo tag pills.
        public static string NpSongInfoOrder { get; set; } = "";
        public static string NpSongInfoHidden { get; set; } = "";

        // When true, the big NP title may wrap to a second line (within the same vertical envelope,
        // so the layout never shifts) instead of being squished onto one line.
        public static bool NpTitleWrapEnabled { get; set; }

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

        // ─── Normalizers and clamp bounds ───

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

        // ─── Persistence ───

        /// <summary>
        /// Which of the four per-context layout bundles the file actually carried. A bundle that was
        /// absent gets seeded from an older one by <see cref="ApplyPostLoadMigrations"/>, so upgrading
        /// users keep the layout they had instead of dropping to defaults.
        /// </summary>
        public struct LayoutSeen
        {
            public bool Fullscreen;
            public bool FullscreenVizPlacement;
            public bool VizOn;
            public bool FullscreenVizOn;

            /// <summary>The file carried an explicit <c>NpLyricMode</c>.</summary>
            public bool LyricMode;
            /// <summary>The file carried a truthy legacy <c>NpFocusedLyricsEnabled</c>.</summary>
            public bool LegacyFocusedLyrics;
        }

        private static string Inv(double v) => v.ToString(CultureInfo.InvariantCulture);

        /// <summary>
        /// The <c>key=value</c> lines this store owns. Callers append these to whatever else they
        /// persist and hand the lot to <see cref="OptionsFileStore.Merge"/> — never a whole-file write.
        /// </summary>
        public static IEnumerable<string> SaveLines() => new[]
        {
            $"NpVisualizerEnabled={NpVisualizerEnabled}",
            $"NpColorMatchEnabled={NpColorMatchEnabled}",
            $"NpColorMatchTargets={NpColorMatchTargets}",
            $"NpRememberManualColorPicks={NpRememberManualColorPicks}",
            $"NpColorPickerMaxColors={NpColorPickerMaxColors}",
            $"NpAlbumBackdropEnabled={NpAlbumBackdropEnabled}",
            $"NpBackgroundMode={NpBackgroundMode}",
            $"NpCustomBackgroundImagePath={NpCustomBackgroundImagePath}",
            $"NpBackgroundBlur={Inv(NpBackgroundBlur)}",
            $"NpBackgroundOpacity={Inv(NpBackgroundOpacity)}",
            $"NpBackgroundHorizontalPosition={Inv(NpBackgroundHorizontalPosition)}",
            $"NpBackgroundVerticalPosition={Inv(NpBackgroundVerticalPosition)}",
            $"NpBackgroundZoom={Inv(NpBackgroundZoom)}",
            $"NpBackgroundBrightness={Inv(NpBackgroundBrightness)}",
            $"NpBackgroundAnimationMode={NpBackgroundAnimationMode}",
            $"NpColorDriftBackgroundEnabled={NpColorDriftBackgroundEnabled}",
            $"NpBackgroundUseAlbumColors={NpBackgroundUseAlbumColors}",
            $"NpBackgroundCycleEnabled={NpBackgroundCycleEnabled}",
            $"NpBackgroundCycleSpeed={Inv(NpBackgroundCycleSpeed)}",
            $"NpBackgroundCycleOnSongChange={NpBackgroundCycleOnSongChange}",
            $"NpStarDensity={Inv(NpStarDensity)}",
            $"NpShootingStarDensity={Inv(NpShootingStarDensity)}",
            $"NpShootingStarsEnabled={NpShootingStarsEnabled}",
            $"NpRainIntensity={Inv(NpRainIntensity)}",
            $"NpRainLightningEnabled={NpRainLightningEnabled}",
            $"NpRainLightningPromptShown={NpRainLightningPromptShown}",
            $"NpRainLightningAmount={Inv(NpRainLightningAmount)}",
            $"NpSnowDensity={Inv(NpSnowDensity)}",
            $"NpSnowflakeAmount={Inv(NpSnowflakeAmount)}",
            $"NpUnderwaterBubbleDensity={Inv(NpUnderwaterBubbleDensity)}",
            $"NpUnderwaterCausticIntensity={Inv(NpUnderwaterCausticIntensity)}",
            $"NpUnderwaterFishEnabled={NpUnderwaterFishEnabled}",
            $"NpUnderwaterSeaweedEnabled={NpUnderwaterSeaweedEnabled}",
            $"NpBackgroundAnimationSpeed={Inv(NpBackgroundAnimationSpeed)}",
            $"MainBackgroundImagePath={MainBackgroundImagePath}",
            $"MainBackgroundOpacity={Inv(MainBackgroundOpacity)}",
            $"MainBackgroundBlur={Inv(MainBackgroundBlur)}",
            $"NpCoverShapeMode={NpCoverShapeMode}",
            $"MiniCoverShapeMode={MiniCoverShapeMode}",
            $"MiniPlayerAlwaysOnTop={MiniPlayerAlwaysOnTop}",
            $"MiniVisualizerStyle={MiniVisualizerStyle}",
            $"MiniColorMatchEnabled={MiniColorMatchEnabled}",
            $"QueueColorMatchEnabled={QueueColorMatchEnabled}",
            $"SettingsColorMatchEnabled={SettingsColorMatchEnabled}",
            $"MiniPlayerLeft={Inv(MiniPlayerLeft)}",
            $"MiniPlayerTop={Inv(MiniPlayerTop)}",
            $"MiniPlayerWidth={Inv(MiniPlayerWidth)}",
            $"MiniPlayerBaseHeight={Inv(MiniPlayerBaseHeight)}",
            $"ShowWrappedButton={ShowWrappedButton}",
            $"ShowMiniPlayerButton={ShowMiniPlayerButton}",
            $"ShowMusicServiceButtons={ShowMusicServiceButtons}",
            $"NpLyricsHidden={NpLyricsHidden}",
            $"NpTranslateEnabled={NpTranslateEnabled}",
            $"NpAutoSaveLyricsEnabled={NpAutoSaveLyricsEnabled}",
            $"NpKaraokeEnabled={NpKaraokeEnabled}",
            $"NpLyricMode={NpLyricMode}",
            $"NpFocusedLyricsBlurRadius={Inv(NpFocusedLyricsBlurRadius)}",
            $"NpCoverGlowMotionEnabled={NpCoverGlowMotionEnabled}",
            $"NpGlowMotionMode={NpGlowMotionMode}",
            $"NpVisualizerStyle={NpVisualizerStyle}",
            $"NpVizPlacement={NpVizPlacement}",
            $"NpSubCoverShowArtist={NpSubCoverShowArtist}",
            $"NpButtonOrder={NpButtonOrder}",
            $"NpButtonHidden={NpButtonHidden}",
            $"NpTransportOrder={NpTransportOrder}",
            $"NpSongInfoOrder={NpSongInfoOrder}",
            $"NpSongInfoHidden={NpSongInfoHidden}",
            $"NpTitleWrapEnabled={NpTitleWrapEnabled}",
            $"NpCoverSize={NpCoverSize}",
            $"NpTitleSize={NpTitleSize}",
            $"NpSubTextSize={NpSubTextSize}",
            $"NpLyricsSize={NpLyricsSize}",
            $"NpVizSize={NpVizSize}",
            $"NpCoverGlowSize={Inv(NpCoverGlowSize)}",
            $"NpLyricsOffsetX={NpLyricsOffsetX}",
            $"NpCoverOffsetX={NpCoverOffsetX}",
            $"NpCoverOffsetY={NpCoverOffsetY}",
            $"NpTitleOffsetX={NpTitleOffsetX}",
            $"NpTitleOffsetY={NpTitleOffsetY}",
            $"NpArtistOffsetX={NpArtistOffsetX}",
            $"NpArtistOffsetY={NpArtistOffsetY}",
            $"NpVizOffsetY={NpVizOffsetY}",
            $"NpFullscreenCoverSize={NpFullscreenCoverSize}",
            $"NpFullscreenTitleSize={NpFullscreenTitleSize}",
            $"NpFullscreenSubTextSize={NpFullscreenSubTextSize}",
            $"NpFullscreenLyricsSize={NpFullscreenLyricsSize}",
            $"NpFullscreenVizSize={NpFullscreenVizSize}",
            $"NpFullscreenLyricsOffsetX={NpFullscreenLyricsOffsetX}",
            $"NpFullscreenCoverOffsetX={NpFullscreenCoverOffsetX}",
            $"NpFullscreenCoverOffsetY={NpFullscreenCoverOffsetY}",
            $"NpFullscreenTitleOffsetX={NpFullscreenTitleOffsetX}",
            $"NpFullscreenTitleOffsetY={NpFullscreenTitleOffsetY}",
            $"NpFullscreenArtistOffsetX={NpFullscreenArtistOffsetX}",
            $"NpFullscreenArtistOffsetY={NpFullscreenArtistOffsetY}",
            $"NpFullscreenVizOffsetY={NpFullscreenVizOffsetY}",
            $"NpFullscreenVizPlacement={NpFullscreenVizPlacement}",
            $"NpVizOnCoverSize={NpVizOnCoverSize}",
            $"NpVizOnTitleSize={NpVizOnTitleSize}",
            $"NpVizOnSubTextSize={NpVizOnSubTextSize}",
            $"NpVizOnLyricsSize={NpVizOnLyricsSize}",
            $"NpVizOnVizSize={NpVizOnVizSize}",
            $"NpVizOnLyricsOffsetX={NpVizOnLyricsOffsetX}",
            $"NpVizOnCoverOffsetX={NpVizOnCoverOffsetX}",
            $"NpVizOnCoverOffsetY={NpVizOnCoverOffsetY}",
            $"NpVizOnTitleOffsetX={NpVizOnTitleOffsetX}",
            $"NpVizOnTitleOffsetY={NpVizOnTitleOffsetY}",
            $"NpVizOnArtistOffsetX={NpVizOnArtistOffsetX}",
            $"NpVizOnArtistOffsetY={NpVizOnArtistOffsetY}",
            $"NpVizOnVizOffsetY={NpVizOnVizOffsetY}",
            $"NpVizOnPlacement={NpVizOnPlacement}",
            $"NpFullscreenVizOnCoverSize={NpFullscreenVizOnCoverSize}",
            $"NpFullscreenVizOnTitleSize={NpFullscreenVizOnTitleSize}",
            $"NpFullscreenVizOnSubTextSize={NpFullscreenVizOnSubTextSize}",
            $"NpFullscreenVizOnLyricsSize={NpFullscreenVizOnLyricsSize}",
            $"NpFullscreenVizOnVizSize={NpFullscreenVizOnVizSize}",
            $"NpFullscreenVizOnLyricsOffsetX={NpFullscreenVizOnLyricsOffsetX}",
            $"NpFullscreenVizOnCoverOffsetX={NpFullscreenVizOnCoverOffsetX}",
            $"NpFullscreenVizOnCoverOffsetY={NpFullscreenVizOnCoverOffsetY}",
            $"NpFullscreenVizOnTitleOffsetX={NpFullscreenVizOnTitleOffsetX}",
            $"NpFullscreenVizOnTitleOffsetY={NpFullscreenVizOnTitleOffsetY}",
            $"NpFullscreenVizOnArtistOffsetX={NpFullscreenVizOnArtistOffsetX}",
            $"NpFullscreenVizOnArtistOffsetY={NpFullscreenVizOnArtistOffsetY}",
            $"NpFullscreenVizOnVizOffsetY={NpFullscreenVizOnVizOffsetY}",
            $"NpFullscreenVizOnPlacement={NpFullscreenVizOnPlacement}",
        };

        /// <summary><see cref="SaveLines"/> split into the key/value pairs
        /// <see cref="OptionsFileStore.Merge"/> takes.</summary>
        public static IEnumerable<KeyValuePair<string, string?>> SaveEntries()
        {
            foreach (var line in SaveLines())
            {
                int eq = line.IndexOf('=');
                yield return new KeyValuePair<string, string?>(line[..eq], line[(eq + 1)..]);
            }
        }

        private static bool Int(string? val, int min, int max, out int result) =>
            int.TryParse(val, NumberStyles.Integer, CultureInfo.InvariantCulture, out result)
            && result >= min && result <= max;

        private static bool Dbl(string? val, out double result) =>
            double.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out result);

        /// <summary>
        /// Applies one <c>options.txt</c> line to this store. Returns false when the key is not one
        /// of ours, so a caller's own switch can go on to handle it.
        /// </summary>
        public static bool TryLoad(string key, string val, ref LayoutSeen seen)
        {
            switch (key)
            {
                case "NpVisualizerEnabled": NpVisualizerEnabled = bool.TryParse(val, out var bNpViz) && bNpViz; return true;
                case "NpColorMatchEnabled": NpColorMatchEnabled = bool.TryParse(val, out var bNpCm) && bNpCm; return true;
                case "NpColorMatchTargets": NpColorMatchTargets = Enum.TryParse<ColorMatchTarget>(val, out var npCmt) ? npCmt : ColorMatchTarget.All; return true;
                case "NpRememberManualColorPicks": NpRememberManualColorPicks = !bool.TryParse(val, out var bNpRmcp) || bNpRmcp; return true;
                case "NpColorPickerMaxColors": if (Int(val, int.MinValue, int.MaxValue, out var iNpCpmc)) NpColorPickerMaxColors = iNpCpmc; return true;
                case "NpAlbumBackdropEnabled": NpAlbumBackdropEnabled = bool.TryParse(val, out var bNpAbe) && bNpAbe; return true;
                case "NpBackgroundMode": NpBackgroundMode = string.IsNullOrWhiteSpace(val) ? "AlbumArt" : val; return true;
                case "NpCustomBackgroundImagePath": NpCustomBackgroundImagePath = val; return true;
                case "NpBackgroundBlur": if (Dbl(val, out var npbb)) NpBackgroundBlur = Math.Clamp(npbb, 0, 48); return true;
                case "NpBackgroundOpacity": if (Dbl(val, out var npbo)) NpBackgroundOpacity = Math.Clamp(npbo, 0, 0.8); return true;
                case "NpBackgroundHorizontalPosition":
                case "NpBackgroundFocusX": if (Dbl(val, out var npbfx)) NpBackgroundHorizontalPosition = Math.Clamp(npbfx, 0, 1); return true;
                case "NpBackgroundVerticalPosition":
                case "NpBackgroundFocusY": if (Dbl(val, out var npbfy)) NpBackgroundVerticalPosition = Math.Clamp(npbfy, 0, 1); return true;
                case "NpBackgroundZoom": if (Dbl(val, out var npbz)) NpBackgroundZoom = Math.Clamp(npbz, 1, 2.5); return true;
                case "NpBackgroundBrightness": if (Dbl(val, out var npbr)) NpBackgroundBrightness = Math.Clamp(npbr, 0.35, 1.6); return true;
                case "NpBackgroundAnimationMode": NpBackgroundAnimationMode = NormalizeNpBackgroundAnimationMode(val); return true;
                case "NpColorDriftBackgroundEnabled": NpColorDriftBackgroundEnabled = bool.TryParse(val, out var ncdbe) && ncdbe; return true;
                case "NpBackgroundUseAlbumColors": NpBackgroundUseAlbumColors = bool.TryParse(val, out var nbbac) && nbbac; return true;
                case "NpBackgroundCycleEnabled": NpBackgroundCycleEnabled = bool.TryParse(val, out var nbce) && nbce; return true;
                case "NpBackgroundCycleSpeed": if (Dbl(val, out var nbcs)) NpBackgroundCycleSpeed = Math.Clamp(nbcs, 0.25, 3.0); return true;
                case "NpBackgroundCycleOnSongChange": NpBackgroundCycleOnSongChange = bool.TryParse(val, out var nbcosc) && nbcosc; return true;
                case "NpStarDensity": if (Dbl(val, out var nsd)) NpStarDensity = ClampNpStarDensity(nsd); return true;
                case "NpShootingStarDensity": if (Dbl(val, out var nssd)) NpShootingStarDensity = ClampNpShootingStarDensity(nssd); return true;
                case "NpShootingStarsEnabled": NpShootingStarsEnabled = !bool.TryParse(val, out var nsse) || nsse; return true;
                case "NpRainIntensity": if (Dbl(val, out var nri)) NpRainIntensity = ClampNpRainIntensity(nri); return true;
                case "NpRainLightningEnabled": NpRainLightningEnabled = bool.TryParse(val, out var nrle) && nrle; return true;
                case "NpRainLightningPromptShown": NpRainLightningPromptShown = bool.TryParse(val, out var nrlps) && nrlps; return true;
                case "NpRainLightningAmount": if (Dbl(val, out var nrla)) NpRainLightningAmount = ClampNpRainLightningAmount(nrla); return true;
                case "NpSnowDensity": if (Dbl(val, out var nsdn)) NpSnowDensity = ClampNpSnowDensity(nsdn); return true;
                case "NpSnowflakeAmount": if (Dbl(val, out var nsfa)) NpSnowflakeAmount = ClampNpSnowflakeAmount(nsfa); return true;
                case "NpUnderwaterBubbleDensity": if (Dbl(val, out var nubd)) NpUnderwaterBubbleDensity = ClampNpUnderwaterBubbleDensity(nubd); return true;
                case "NpUnderwaterCausticIntensity": if (Dbl(val, out var nuci)) NpUnderwaterCausticIntensity = ClampNpUnderwaterCausticIntensity(nuci); return true;
                case "NpUnderwaterFishEnabled": NpUnderwaterFishEnabled = !bool.TryParse(val, out var nufe) || nufe; return true;
                case "NpUnderwaterSeaweedEnabled": NpUnderwaterSeaweedEnabled = !bool.TryParse(val, out var nuse) || nuse; return true;
                case "NpBackgroundAnimationSpeed": if (Dbl(val, out var nbas)) NpBackgroundAnimationSpeed = ClampNpBackgroundAnimationSpeed(nbas); return true;
                case "MainBackgroundImagePath": MainBackgroundImagePath = val; return true;
                case "MainBackgroundOpacity": if (Dbl(val, out var mbo)) MainBackgroundOpacity = Math.Clamp(mbo, 0, 0.8); return true;
                case "MainBackgroundBlur": if (Dbl(val, out var mbb)) MainBackgroundBlur = Math.Clamp(mbb, 0, 48); return true;
                case "NpCoverShapeMode": NpCoverShapeMode = NormalizeCoverShapeMode(val); return true;
                case "MiniCoverShapeMode": MiniCoverShapeMode = NormalizeCoverShapeMode(val) == "Default" ? "Rounded" : NormalizeCoverShapeMode(val); return true;
                case "MiniPlayerAlwaysOnTop": MiniPlayerAlwaysOnTop = !bool.TryParse(val, out var miniAlwaysOnTop) || miniAlwaysOnTop; return true;
                case "MiniVisualizerStyle": if (Int(val, -1, 4, out var mvs)) MiniVisualizerStyle = mvs; return true;
                case "MiniColorMatchEnabled": MiniColorMatchEnabled = bool.TryParse(val, out var mcme) && mcme; return true;
                case "QueueColorMatchEnabled": QueueColorMatchEnabled = !bool.TryParse(val, out var qcme) || qcme; return true;
                case "SettingsColorMatchEnabled": SettingsColorMatchEnabled = !bool.TryParse(val, out var scme) || scme; return true;
                case "MiniPlayerLeft": if (Dbl(val, out var mpl)) MiniPlayerLeft = mpl; return true;
                case "MiniPlayerTop": if (Dbl(val, out var mpt)) MiniPlayerTop = mpt; return true;
                case "MiniPlayerWidth": if (Dbl(val, out var mpw)) MiniPlayerWidth = mpw; return true;
                case "MiniPlayerBaseHeight": if (Dbl(val, out var mpbh)) MiniPlayerBaseHeight = mpbh; return true;
                case "ShowWrappedButton": ShowWrappedButton = !bool.TryParse(val, out var bSwb) || bSwb; return true;       // default true
                case "ShowMiniPlayerButton": ShowMiniPlayerButton = !bool.TryParse(val, out var bSmpb) || bSmpb; return true; // default true
                case "ShowMusicServiceButtons": ShowMusicServiceButtons = !bool.TryParse(val, out var bSmsb) || bSmsb; return true; // default true
                case "NpLyricsHidden": NpLyricsHidden = bool.TryParse(val, out var bNpLh) && bNpLh; return true;
                case "NpTranslateEnabled": NpTranslateEnabled = bool.TryParse(val, out var bNpTr) && bNpTr; return true;
                case "NpAutoSaveLyricsEnabled": NpAutoSaveLyricsEnabled = bool.TryParse(val, out var bNpAs) && bNpAs; return true;
                case "NpKaraokeEnabled": NpKaraokeEnabled = bool.TryParse(val, out var bNpKa) && bNpKa; return true;
                // "Uniform" was removed as an option — migrate any saved Uniform to Standard.
                case "NpLyricMode": seen.LyricMode = true; if (Enum.TryParse<NpLyricDisplayMode>(val, out var nlm)) NpLyricMode = nlm == NpLyricDisplayMode.Uniform ? NpLyricDisplayMode.Standard : nlm; return true;
                // Legacy (pre-3-mode) key meaning "Blur". No build writes it any more, so a copy left
                // in options.txt is stale. Applying it here would override a user who has since chosen
                // Standard whenever it happened to sit after NpLyricMode in the file, so it is only
                // recorded and then applied post-load, and only if no explicit NpLyricMode arrived.
                case "NpFocusedLyricsEnabled": seen.LegacyFocusedLyrics = bool.TryParse(val, out var bNpFl) && bNpFl; return true;
                case "NpFocusedLyricsBlurRadius": if (Dbl(val, out var nflb) && nflb >= 0 && nflb <= 16.0) NpFocusedLyricsBlurRadius = nflb; return true;
                case "NpCoverGlowMotionEnabled": NpCoverGlowMotionEnabled = !bool.TryParse(val, out var bNpGm) || bNpGm; return true;
                case "NpGlowMotionMode": if (Enum.TryParse<GlowMotionMode>(val, true, out var bNpGmm)) NpGlowMotionMode = bNpGmm; return true;
                // Migrate old Abstract style (index 5 was removed; 5 is now VU Meter)
                case "NpVisualizerStyle": if (Int(val, 0, 5, out var nvs)) NpVisualizerStyle = nvs == 5 ? 0 : nvs; return true;
                case "NpVizPlacement": if (Int(val, 0, 1, out var nvp)) NpVizPlacement = nvp; return true;
                case "NpSubCoverShowArtist": NpSubCoverShowArtist = !bool.TryParse(val, out var bNpSca) || bNpSca; return true; // default true
                case "NpButtonOrder": NpButtonOrder = val ?? ""; return true;
                case "NpButtonHidden": NpButtonHidden = val ?? ""; return true;
                case "NpTransportOrder": NpTransportOrder = val ?? ""; return true;
                case "NpSongInfoOrder": NpSongInfoOrder = val ?? ""; return true;
                case "NpSongInfoHidden": NpSongInfoHidden = val ?? ""; return true;
                case "NpTitleWrapEnabled": NpTitleWrapEnabled = bool.TryParse(val, out var bNpTw) && bNpTw; return true;
                case "NpCoverSize": if (Int(val, 0, 900, out var ncs)) NpCoverSize = ncs; return true;
                case "NpTitleSize": if (Int(val, 0, 72, out var nts)) NpTitleSize = nts; return true;
                case "NpSubTextSize": if (Int(val, 0, 36, out var nss)) NpSubTextSize = nss; return true;
                case "NpLyricsSize": if (Int(val, 0, 72, out var nls)) NpLyricsSize = nls; return true;
                case "NpVizSize": if (Int(val, 0, 400, out var nvz)) NpVizSize = nvz; return true;
                case "NpCoverGlowSize": if (Dbl(val, out var ncgs) && ncgs >= 0 && ncgs <= 2.0) NpCoverGlowSize = ncgs; return true;
                case "NpLyricsOffsetX": if (Int(val, 0, 500, out var nlx)) NpLyricsOffsetX = nlx; return true;
                case "NpCoverOffsetX": if (Int(val, -200, 200, out var ncox)) NpCoverOffsetX = ncox; return true;
                case "NpCoverOffsetY": if (Int(val, -200, 200, out var ncoy)) NpCoverOffsetY = ncoy; return true;
                case "NpTitleOffsetX": if (Int(val, -200, 200, out var ntox)) NpTitleOffsetX = ntox; return true;
                case "NpTitleOffsetY": if (Int(val, -200, 200, out var ntoy)) NpTitleOffsetY = ntoy; return true;
                case "NpArtistOffsetX": if (Int(val, -200, 200, out var naox)) NpArtistOffsetX = naox; return true;
                case "NpArtistOffsetY": if (Int(val, -200, 200, out var naoy)) NpArtistOffsetY = naoy; return true;
                case "NpVizOffsetY": if (Int(val, -200, 200, out var nvoy)) NpVizOffsetY = nvoy; return true;

                case "NpFullscreenCoverSize": seen.Fullscreen = true; if (Int(val, 0, 900, out var nfcs)) NpFullscreenCoverSize = nfcs; return true;
                case "NpFullscreenTitleSize": seen.Fullscreen = true; if (Int(val, 0, 72, out var nfts)) NpFullscreenTitleSize = nfts; return true;
                case "NpFullscreenSubTextSize": seen.Fullscreen = true; if (Int(val, 0, 36, out var nfss)) NpFullscreenSubTextSize = nfss; return true;
                case "NpFullscreenLyricsSize": seen.Fullscreen = true; if (Int(val, 0, 72, out var nfls)) NpFullscreenLyricsSize = nfls; return true;
                case "NpFullscreenVizSize": seen.Fullscreen = true; if (Int(val, 0, 400, out var nfvz)) NpFullscreenVizSize = nfvz; return true;
                case "NpFullscreenLyricsOffsetX": seen.Fullscreen = true; if (Int(val, 0, 500, out var nflx)) NpFullscreenLyricsOffsetX = nflx; return true;
                case "NpFullscreenCoverOffsetX": seen.Fullscreen = true; if (Int(val, -200, 200, out var nfcox)) NpFullscreenCoverOffsetX = nfcox; return true;
                case "NpFullscreenCoverOffsetY": seen.Fullscreen = true; if (Int(val, -200, 200, out var nfcoy)) NpFullscreenCoverOffsetY = nfcoy; return true;
                case "NpFullscreenTitleOffsetX": seen.Fullscreen = true; if (Int(val, -200, 200, out var nftox)) NpFullscreenTitleOffsetX = nftox; return true;
                case "NpFullscreenTitleOffsetY": seen.Fullscreen = true; if (Int(val, -200, 200, out var nftoy)) NpFullscreenTitleOffsetY = nftoy; return true;
                case "NpFullscreenArtistOffsetX": seen.Fullscreen = true; if (Int(val, -200, 200, out var nfaox)) NpFullscreenArtistOffsetX = nfaox; return true;
                case "NpFullscreenArtistOffsetY": seen.Fullscreen = true; if (Int(val, -200, 200, out var nfaoy)) NpFullscreenArtistOffsetY = nfaoy; return true;
                case "NpFullscreenVizOffsetY": seen.Fullscreen = true; if (Int(val, -200, 200, out var nfvoy)) NpFullscreenVizOffsetY = nfvoy; return true;
                case "NpFullscreenVizPlacement": seen.Fullscreen = true; seen.FullscreenVizPlacement = true; if (Int(val, 0, 1, out var nfvp)) NpFullscreenVizPlacement = nfvp; return true;

                case "NpVizOnCoverSize": seen.VizOn = true; if (Int(val, 0, 900, out var nvocs)) NpVizOnCoverSize = nvocs; return true;
                case "NpVizOnTitleSize": seen.VizOn = true; if (Int(val, 0, 72, out var nvots)) NpVizOnTitleSize = nvots; return true;
                case "NpVizOnSubTextSize": seen.VizOn = true; if (Int(val, 0, 36, out var nvoss)) NpVizOnSubTextSize = nvoss; return true;
                case "NpVizOnLyricsSize": seen.VizOn = true; if (Int(val, 0, 72, out var nvols)) NpVizOnLyricsSize = nvols; return true;
                case "NpVizOnVizSize": seen.VizOn = true; if (Int(val, 0, 400, out var nvovz)) NpVizOnVizSize = nvovz; return true;
                case "NpVizOnLyricsOffsetX": seen.VizOn = true; if (Int(val, 0, 500, out var nvolx)) NpVizOnLyricsOffsetX = nvolx; return true;
                case "NpVizOnCoverOffsetX": seen.VizOn = true; if (Int(val, -200, 200, out var nvocox)) NpVizOnCoverOffsetX = nvocox; return true;
                case "NpVizOnCoverOffsetY": seen.VizOn = true; if (Int(val, -200, 200, out var nvocoy)) NpVizOnCoverOffsetY = nvocoy; return true;
                case "NpVizOnTitleOffsetX": seen.VizOn = true; if (Int(val, -200, 200, out var nvotox)) NpVizOnTitleOffsetX = nvotox; return true;
                case "NpVizOnTitleOffsetY": seen.VizOn = true; if (Int(val, -200, 200, out var nvotoy)) NpVizOnTitleOffsetY = nvotoy; return true;
                case "NpVizOnArtistOffsetX": seen.VizOn = true; if (Int(val, -200, 200, out var nvoaox)) NpVizOnArtistOffsetX = nvoaox; return true;
                case "NpVizOnArtistOffsetY": seen.VizOn = true; if (Int(val, -200, 200, out var nvoaoy)) NpVizOnArtistOffsetY = nvoaoy; return true;
                case "NpVizOnVizOffsetY": seen.VizOn = true; if (Int(val, -200, 200, out var nvovoy)) NpVizOnVizOffsetY = nvovoy; return true;
                case "NpVizOnPlacement": seen.VizOn = true; if (Int(val, 0, 1, out var nvop)) NpVizOnPlacement = nvop; return true;

                case "NpFullscreenVizOnCoverSize": seen.FullscreenVizOn = true; if (Int(val, 0, 900, out var nfvocs)) NpFullscreenVizOnCoverSize = nfvocs; return true;
                case "NpFullscreenVizOnTitleSize": seen.FullscreenVizOn = true; if (Int(val, 0, 72, out var nfvots)) NpFullscreenVizOnTitleSize = nfvots; return true;
                case "NpFullscreenVizOnSubTextSize": seen.FullscreenVizOn = true; if (Int(val, 0, 36, out var nfvoss)) NpFullscreenVizOnSubTextSize = nfvoss; return true;
                case "NpFullscreenVizOnLyricsSize": seen.FullscreenVizOn = true; if (Int(val, 0, 72, out var nfvols)) NpFullscreenVizOnLyricsSize = nfvols; return true;
                case "NpFullscreenVizOnVizSize": seen.FullscreenVizOn = true; if (Int(val, 0, 400, out var nfvovz)) NpFullscreenVizOnVizSize = nfvovz; return true;
                case "NpFullscreenVizOnLyricsOffsetX": seen.FullscreenVizOn = true; if (Int(val, 0, 500, out var nfvolx)) NpFullscreenVizOnLyricsOffsetX = nfvolx; return true;
                case "NpFullscreenVizOnCoverOffsetX": seen.FullscreenVizOn = true; if (Int(val, -200, 200, out var nfvocox)) NpFullscreenVizOnCoverOffsetX = nfvocox; return true;
                case "NpFullscreenVizOnCoverOffsetY": seen.FullscreenVizOn = true; if (Int(val, -200, 200, out var nfvocoy)) NpFullscreenVizOnCoverOffsetY = nfvocoy; return true;
                case "NpFullscreenVizOnTitleOffsetX": seen.FullscreenVizOn = true; if (Int(val, -200, 200, out var nfvotox)) NpFullscreenVizOnTitleOffsetX = nfvotox; return true;
                case "NpFullscreenVizOnTitleOffsetY": seen.FullscreenVizOn = true; if (Int(val, -200, 200, out var nfvotoy)) NpFullscreenVizOnTitleOffsetY = nfvotoy; return true;
                case "NpFullscreenVizOnArtistOffsetX": seen.FullscreenVizOn = true; if (Int(val, -200, 200, out var nfvoaox)) NpFullscreenVizOnArtistOffsetX = nfvoaox; return true;
                case "NpFullscreenVizOnArtistOffsetY": seen.FullscreenVizOn = true; if (Int(val, -200, 200, out var nfvoaoy)) NpFullscreenVizOnArtistOffsetY = nfvoaoy; return true;
                case "NpFullscreenVizOnVizOffsetY": seen.FullscreenVizOn = true; if (Int(val, -200, 200, out var nfvovoy)) NpFullscreenVizOnVizOffsetY = nfvovoy; return true;
                case "NpFullscreenVizOnPlacement": seen.FullscreenVizOn = true; if (Int(val, 0, 1, out var nfvop)) NpFullscreenVizOnPlacement = nfvop; return true;

                default: return false;
            }
        }

        /// <summary>
        /// Run once after the whole file has been read. Converts the legacy "Color Drift" background
        /// mode and seeds each per-context layout bundle the file did not carry from the older bundle
        /// it superseded.
        /// </summary>
        public static void ApplyPostLoadMigrations(in LayoutSeen seen)
        {
            // "Color Drift" is no longer a mutually-exclusive background mode — it's now controlled
            // solely by the NpColorDriftBackgroundEnabled toggle (which can run under any effect).
            // Convert a legacy saved mode of "Color Drift" to Off + drift glow enabled.
            if (NormalizeNpBackgroundAnimationMode(NpBackgroundAnimationMode) == "Color Drift")
            {
                NpBackgroundAnimationMode = "Off";
                NpColorDriftBackgroundEnabled = true;
            }

            // Upgrade from before lyric display was a three-way mode. Only honoured when the file
            // carried no NpLyricMode of its own, so a stale leftover can never outrank a real choice.
            if (!seen.LyricMode && seen.LegacyFocusedLyrics)
                NpLyricMode = NpLyricDisplayMode.Blur;

            if (!seen.Fullscreen)
            {
                NpFullscreenCoverSize = NpCoverSize;
                NpFullscreenTitleSize = NpTitleSize;
                NpFullscreenSubTextSize = NpSubTextSize;
                NpFullscreenLyricsSize = NpLyricsSize;
                NpFullscreenVizSize = NpVizSize;
                NpFullscreenLyricsOffsetX = NpLyricsOffsetX;
                NpFullscreenCoverOffsetX = NpCoverOffsetX;
                NpFullscreenCoverOffsetY = NpCoverOffsetY;
                NpFullscreenTitleOffsetX = NpTitleOffsetX;
                NpFullscreenTitleOffsetY = NpTitleOffsetY;
                NpFullscreenArtistOffsetX = NpArtistOffsetX;
                NpFullscreenArtistOffsetY = NpArtistOffsetY;
                NpFullscreenVizOffsetY = NpVizOffsetY;
                NpFullscreenVizPlacement = NpVizPlacement;
            }
            else if (!seen.FullscreenVizPlacement)
            {
                NpFullscreenVizPlacement = NpVizPlacement;
            }

            if (!seen.VizOn)
            {
                NpVizOnCoverSize = NpCoverSize;
                NpVizOnTitleSize = NpTitleSize;
                NpVizOnSubTextSize = NpSubTextSize;
                NpVizOnLyricsSize = NpLyricsSize;
                NpVizOnVizSize = NpVizSize;
                NpVizOnLyricsOffsetX = NpLyricsOffsetX;
                NpVizOnCoverOffsetX = NpCoverOffsetX;
                NpVizOnCoverOffsetY = NpCoverOffsetY;
                NpVizOnTitleOffsetX = NpTitleOffsetX;
                NpVizOnTitleOffsetY = NpTitleOffsetY;
                NpVizOnArtistOffsetX = NpArtistOffsetX;
                NpVizOnArtistOffsetY = NpArtistOffsetY;
                NpVizOnVizOffsetY = NpVizOffsetY;
                NpVizOnPlacement = NpVizPlacement;
            }

            if (!seen.FullscreenVizOn)
            {
                NpFullscreenVizOnCoverSize = NpFullscreenCoverSize;
                NpFullscreenVizOnTitleSize = NpFullscreenTitleSize;
                NpFullscreenVizOnSubTextSize = NpFullscreenSubTextSize;
                NpFullscreenVizOnLyricsSize = NpFullscreenLyricsSize;
                NpFullscreenVizOnVizSize = NpFullscreenVizSize;
                NpFullscreenVizOnLyricsOffsetX = NpFullscreenLyricsOffsetX;
                NpFullscreenVizOnCoverOffsetX = NpFullscreenCoverOffsetX;
                NpFullscreenVizOnCoverOffsetY = NpFullscreenCoverOffsetY;
                NpFullscreenVizOnTitleOffsetX = NpFullscreenTitleOffsetX;
                NpFullscreenVizOnTitleOffsetY = NpFullscreenTitleOffsetY;
                NpFullscreenVizOnArtistOffsetX = NpFullscreenArtistOffsetX;
                NpFullscreenVizOnArtistOffsetY = NpFullscreenArtistOffsetY;
                NpFullscreenVizOnVizOffsetY = NpFullscreenVizOffsetY;
                NpFullscreenVizOnPlacement = NpFullscreenVizPlacement;
            }
        }
    }
}
