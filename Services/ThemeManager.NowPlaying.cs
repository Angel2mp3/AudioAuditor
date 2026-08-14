namespace AudioQualityChecker.Services
{
    /// <summary>
    /// Now Playing preferences. The values, their clamp bounds and their <c>options.txt</c>
    /// persistence all live in <see cref="NowPlayingSettings"/> in Core so the WPF and Avalonia
    /// builds cannot drift apart on them; what follows is only a forwarding surface, kept so the
    /// several hundred existing <c>ThemeManager.Np…</c> call sites read unchanged.
    ///
    /// New Now Playing settings go in Core, not here.
    /// </summary>
    public static partial class ThemeManager
    {
        public static bool NpVisualizerEnabled { get => NowPlayingSettings.NpVisualizerEnabled; set => NowPlayingSettings.NpVisualizerEnabled = value; }
        public static bool NpColorMatchEnabled { get => NowPlayingSettings.NpColorMatchEnabled; set => NowPlayingSettings.NpColorMatchEnabled = value; }
        public static ColorMatchTarget NpColorMatchTargets { get => NowPlayingSettings.NpColorMatchTargets; set => NowPlayingSettings.NpColorMatchTargets = value; }
        public static bool NpRememberManualColorPicks { get => NowPlayingSettings.NpRememberManualColorPicks; set => NowPlayingSettings.NpRememberManualColorPicks = value; }
        public static int NpColorPickerMaxColors { get => NowPlayingSettings.NpColorPickerMaxColors; set => NowPlayingSettings.NpColorPickerMaxColors = value; }
        public static bool NpAlbumBackdropEnabled { get => NowPlayingSettings.NpAlbumBackdropEnabled; set => NowPlayingSettings.NpAlbumBackdropEnabled = value; }
        public static string NpBackgroundMode { get => NowPlayingSettings.NpBackgroundMode; set => NowPlayingSettings.NpBackgroundMode = value; }
        public static string NpCustomBackgroundImagePath { get => NowPlayingSettings.NpCustomBackgroundImagePath; set => NowPlayingSettings.NpCustomBackgroundImagePath = value; }
        public static double NpBackgroundBlur { get => NowPlayingSettings.NpBackgroundBlur; set => NowPlayingSettings.NpBackgroundBlur = value; }
        public static double NpBackgroundOpacity { get => NowPlayingSettings.NpBackgroundOpacity; set => NowPlayingSettings.NpBackgroundOpacity = value; }
        public static double NpBackgroundHorizontalPosition { get => NowPlayingSettings.NpBackgroundHorizontalPosition; set => NowPlayingSettings.NpBackgroundHorizontalPosition = value; }
        public static double NpBackgroundVerticalPosition { get => NowPlayingSettings.NpBackgroundVerticalPosition; set => NowPlayingSettings.NpBackgroundVerticalPosition = value; }
        public static double NpBackgroundFocusX { get => NowPlayingSettings.NpBackgroundFocusX; set => NowPlayingSettings.NpBackgroundFocusX = value; }
        public static double NpBackgroundFocusY { get => NowPlayingSettings.NpBackgroundFocusY; set => NowPlayingSettings.NpBackgroundFocusY = value; }
        public static double NpBackgroundZoom { get => NowPlayingSettings.NpBackgroundZoom; set => NowPlayingSettings.NpBackgroundZoom = value; }
        public static double NpBackgroundBrightness { get => NowPlayingSettings.NpBackgroundBrightness; set => NowPlayingSettings.NpBackgroundBrightness = value; }
        public static string NpBackgroundAnimationMode { get => NowPlayingSettings.NpBackgroundAnimationMode; set => NowPlayingSettings.NpBackgroundAnimationMode = value; }
        public static bool NpColorDriftBackgroundEnabled { get => NowPlayingSettings.NpColorDriftBackgroundEnabled; set => NowPlayingSettings.NpColorDriftBackgroundEnabled = value; }
        public static bool NpBackgroundUseAlbumColors { get => NowPlayingSettings.NpBackgroundUseAlbumColors; set => NowPlayingSettings.NpBackgroundUseAlbumColors = value; }
        public static bool NpBackgroundCycleEnabled { get => NowPlayingSettings.NpBackgroundCycleEnabled; set => NowPlayingSettings.NpBackgroundCycleEnabled = value; }
        public static double NpBackgroundCycleSpeed { get => NowPlayingSettings.NpBackgroundCycleSpeed; set => NowPlayingSettings.NpBackgroundCycleSpeed = value; }
        public static bool NpBackgroundCycleOnSongChange { get => NowPlayingSettings.NpBackgroundCycleOnSongChange; set => NowPlayingSettings.NpBackgroundCycleOnSongChange = value; }
        public static double NpStarDensity { get => NowPlayingSettings.NpStarDensity; set => NowPlayingSettings.NpStarDensity = value; }
        public static double NpShootingStarDensity { get => NowPlayingSettings.NpShootingStarDensity; set => NowPlayingSettings.NpShootingStarDensity = value; }
        public static bool NpShootingStarsEnabled { get => NowPlayingSettings.NpShootingStarsEnabled; set => NowPlayingSettings.NpShootingStarsEnabled = value; }
        public static double NpRainIntensity { get => NowPlayingSettings.NpRainIntensity; set => NowPlayingSettings.NpRainIntensity = value; }
        public static bool NpRainLightningEnabled { get => NowPlayingSettings.NpRainLightningEnabled; set => NowPlayingSettings.NpRainLightningEnabled = value; }
        public static bool NpRainLightningPromptShown { get => NowPlayingSettings.NpRainLightningPromptShown; set => NowPlayingSettings.NpRainLightningPromptShown = value; }
        public static double NpRainLightningAmount { get => NowPlayingSettings.NpRainLightningAmount; set => NowPlayingSettings.NpRainLightningAmount = value; }
        public static double NpSnowDensity { get => NowPlayingSettings.NpSnowDensity; set => NowPlayingSettings.NpSnowDensity = value; }
        public static double NpSnowflakeAmount { get => NowPlayingSettings.NpSnowflakeAmount; set => NowPlayingSettings.NpSnowflakeAmount = value; }
        public static double NpUnderwaterBubbleDensity { get => NowPlayingSettings.NpUnderwaterBubbleDensity; set => NowPlayingSettings.NpUnderwaterBubbleDensity = value; }
        public static double NpUnderwaterCausticIntensity { get => NowPlayingSettings.NpUnderwaterCausticIntensity; set => NowPlayingSettings.NpUnderwaterCausticIntensity = value; }
        public static bool NpUnderwaterFishEnabled { get => NowPlayingSettings.NpUnderwaterFishEnabled; set => NowPlayingSettings.NpUnderwaterFishEnabled = value; }
        public static bool NpUnderwaterSeaweedEnabled { get => NowPlayingSettings.NpUnderwaterSeaweedEnabled; set => NowPlayingSettings.NpUnderwaterSeaweedEnabled = value; }
        public static double NpBackgroundAnimationSpeed { get => NowPlayingSettings.NpBackgroundAnimationSpeed; set => NowPlayingSettings.NpBackgroundAnimationSpeed = value; }
        public static string MainBackgroundImagePath { get => NowPlayingSettings.MainBackgroundImagePath; set => NowPlayingSettings.MainBackgroundImagePath = value; }
        public static double MainBackgroundOpacity { get => NowPlayingSettings.MainBackgroundOpacity; set => NowPlayingSettings.MainBackgroundOpacity = value; }
        public static double MainBackgroundBlur { get => NowPlayingSettings.MainBackgroundBlur; set => NowPlayingSettings.MainBackgroundBlur = value; }
        public static string NpCoverShapeMode { get => NowPlayingSettings.NpCoverShapeMode; set => NowPlayingSettings.NpCoverShapeMode = value; }
        public static string MiniCoverShapeMode { get => NowPlayingSettings.MiniCoverShapeMode; set => NowPlayingSettings.MiniCoverShapeMode = value; }
        public static bool MiniPlayerAlwaysOnTop { get => NowPlayingSettings.MiniPlayerAlwaysOnTop; set => NowPlayingSettings.MiniPlayerAlwaysOnTop = value; }

        // ─── Mini Player remembered state (persisted independently of the main window) ───
        public static int MiniVisualizerStyle { get => NowPlayingSettings.MiniVisualizerStyle; set => NowPlayingSettings.MiniVisualizerStyle = value; }
        public static bool MiniColorMatchEnabled { get => NowPlayingSettings.MiniColorMatchEnabled; set => NowPlayingSettings.MiniColorMatchEnabled = value; }

        // ─── Queue / Settings window ColorMatch: independent instead of always inheriting Main ───
        public static bool QueueColorMatchEnabled { get => NowPlayingSettings.QueueColorMatchEnabled; set => NowPlayingSettings.QueueColorMatchEnabled = value; }
        public static bool SettingsColorMatchEnabled { get => NowPlayingSettings.SettingsColorMatchEnabled; set => NowPlayingSettings.SettingsColorMatchEnabled = value; }
        public static double MiniPlayerLeft { get => NowPlayingSettings.MiniPlayerLeft; set => NowPlayingSettings.MiniPlayerLeft = value; }
        public static double MiniPlayerTop { get => NowPlayingSettings.MiniPlayerTop; set => NowPlayingSettings.MiniPlayerTop = value; }
        public static double MiniPlayerWidth { get => NowPlayingSettings.MiniPlayerWidth; set => NowPlayingSettings.MiniPlayerWidth = value; }
        public static double MiniPlayerBaseHeight { get => NowPlayingSettings.MiniPlayerBaseHeight; set => NowPlayingSettings.MiniPlayerBaseHeight = value; }

        // ─── Main toolbar button visibility ───
        public static bool ShowWrappedButton { get => NowPlayingSettings.ShowWrappedButton; set => NowPlayingSettings.ShowWrappedButton = value; }
        public static bool ShowMiniPlayerButton { get => NowPlayingSettings.ShowMiniPlayerButton; set => NowPlayingSettings.ShowMiniPlayerButton = value; }
        public static bool ShowMusicServiceButtons { get => NowPlayingSettings.ShowMusicServiceButtons; set => NowPlayingSettings.ShowMusicServiceButtons = value; }
        public static bool NpLyricsHidden { get => NowPlayingSettings.NpLyricsHidden; set => NowPlayingSettings.NpLyricsHidden = value; }
        public static bool NpTranslateEnabled { get => NowPlayingSettings.NpTranslateEnabled; set => NowPlayingSettings.NpTranslateEnabled = value; }
        public static bool NpAutoSaveLyricsEnabled { get => NowPlayingSettings.NpAutoSaveLyricsEnabled; set => NowPlayingSettings.NpAutoSaveLyricsEnabled = value; }
        public static bool NpKaraokeEnabled { get => NowPlayingSettings.NpKaraokeEnabled; set => NowPlayingSettings.NpKaraokeEnabled = value; }
        public static NpLyricDisplayMode NpLyricMode { get => NowPlayingSettings.NpLyricMode; set => NowPlayingSettings.NpLyricMode = value; }
        public static double NpFocusedLyricsBlurRadius { get => NowPlayingSettings.NpFocusedLyricsBlurRadius; set => NowPlayingSettings.NpFocusedLyricsBlurRadius = value; }
        public static bool NpCoverGlowMotionEnabled { get => NowPlayingSettings.NpCoverGlowMotionEnabled; set => NowPlayingSettings.NpCoverGlowMotionEnabled = value; }
        public static GlowMotionMode NpGlowMotionMode { get => NowPlayingSettings.NpGlowMotionMode; set => NowPlayingSettings.NpGlowMotionMode = value; }
        public static int NpVisualizerStyle { get => NowPlayingSettings.NpVisualizerStyle; set => NowPlayingSettings.NpVisualizerStyle = value; }
        public static int NpVizPlacement { get => NowPlayingSettings.NpVizPlacement; set => NowPlayingSettings.NpVizPlacement = value; }
        public static bool NpSubCoverShowArtist { get => NowPlayingSettings.NpSubCoverShowArtist; set => NowPlayingSettings.NpSubCoverShowArtist = value; }

        // ─── NP "look up this song" search services ───
        public static string[] NpSearchServiceSlots => NowPlayingSettings.NpSearchServiceSlots;
        public static bool[] NpSearchServiceSlotVisible => NowPlayingSettings.NpSearchServiceSlotVisible;
        public static string[] NpSearchCustomServiceUrls => NowPlayingSettings.NpSearchCustomServiceUrls;
        public static string[] NpSearchCustomServiceIcons => NowPlayingSettings.NpSearchCustomServiceIcons;
        public static bool NpSearchServicesConfigured { get => NowPlayingSettings.NpSearchServicesConfigured; set => NowPlayingSettings.NpSearchServicesConfigured = value; }

        /// <summary>
        /// One-time copy of the main-window service config into the NP slots, used when the user
        /// hasn't customized NP search yet. Idempotent: only copies when not already configured.
        /// </summary>
        public static void SeedNpSearchServicesFromMain(bool force = false) =>
            NowPlayingSettings.SeedNpSearchServices(
                MusicServiceSlots, MusicServiceSlotVisible, CustomServiceUrls, CustomServiceIcons, force);

        public static string NpButtonOrder { get => NowPlayingSettings.NpButtonOrder; set => NowPlayingSettings.NpButtonOrder = value; }
        public static string NpButtonHidden { get => NowPlayingSettings.NpButtonHidden; set => NowPlayingSettings.NpButtonHidden = value; }
        public static string NpTransportOrder { get => NowPlayingSettings.NpTransportOrder; set => NowPlayingSettings.NpTransportOrder = value; }
        public static string NpSongInfoOrder { get => NowPlayingSettings.NpSongInfoOrder; set => NowPlayingSettings.NpSongInfoOrder = value; }
        public static string NpSongInfoHidden { get => NowPlayingSettings.NpSongInfoHidden; set => NowPlayingSettings.NpSongInfoHidden = value; }
        public static bool NpTitleWrapEnabled { get => NowPlayingSettings.NpTitleWrapEnabled; set => NowPlayingSettings.NpTitleWrapEnabled = value; }

        // NP custom layout sizes (0 = use default for current window state)
        public static int NpCoverSize { get => NowPlayingSettings.NpCoverSize; set => NowPlayingSettings.NpCoverSize = value; }
        public static int NpTitleSize { get => NowPlayingSettings.NpTitleSize; set => NowPlayingSettings.NpTitleSize = value; }
        public static int NpSubTextSize { get => NowPlayingSettings.NpSubTextSize; set => NowPlayingSettings.NpSubTextSize = value; }
        public static int NpLyricsSize { get => NowPlayingSettings.NpLyricsSize; set => NowPlayingSettings.NpLyricsSize = value; }
        public static int NpVizSize { get => NowPlayingSettings.NpVizSize; set => NowPlayingSettings.NpVizSize = value; }
        public static double NpCoverGlowSize { get => NowPlayingSettings.NpCoverGlowSize; set => NowPlayingSettings.NpCoverGlowSize = value; }
        public static int NpLyricsOffsetX { get => NowPlayingSettings.NpLyricsOffsetX; set => NowPlayingSettings.NpLyricsOffsetX = value; }

        // NP element position offsets (px, 0 = default)
        public static int NpCoverOffsetX { get => NowPlayingSettings.NpCoverOffsetX; set => NowPlayingSettings.NpCoverOffsetX = value; }
        public static int NpCoverOffsetY { get => NowPlayingSettings.NpCoverOffsetY; set => NowPlayingSettings.NpCoverOffsetY = value; }
        public static int NpTitleOffsetX { get => NowPlayingSettings.NpTitleOffsetX; set => NowPlayingSettings.NpTitleOffsetX = value; }
        public static int NpTitleOffsetY { get => NowPlayingSettings.NpTitleOffsetY; set => NowPlayingSettings.NpTitleOffsetY = value; }
        public static int NpArtistOffsetX { get => NowPlayingSettings.NpArtistOffsetX; set => NowPlayingSettings.NpArtistOffsetX = value; }
        public static int NpArtistOffsetY { get => NowPlayingSettings.NpArtistOffsetY; set => NowPlayingSettings.NpArtistOffsetY = value; }
        public static int NpVizOffsetY { get => NowPlayingSettings.NpVizOffsetY; set => NowPlayingSettings.NpVizOffsetY = value; }

        // Fullscreen NP layout preset
        public static int NpFullscreenCoverSize { get => NowPlayingSettings.NpFullscreenCoverSize; set => NowPlayingSettings.NpFullscreenCoverSize = value; }
        public static int NpFullscreenTitleSize { get => NowPlayingSettings.NpFullscreenTitleSize; set => NowPlayingSettings.NpFullscreenTitleSize = value; }
        public static int NpFullscreenSubTextSize { get => NowPlayingSettings.NpFullscreenSubTextSize; set => NowPlayingSettings.NpFullscreenSubTextSize = value; }
        public static int NpFullscreenLyricsSize { get => NowPlayingSettings.NpFullscreenLyricsSize; set => NowPlayingSettings.NpFullscreenLyricsSize = value; }
        public static int NpFullscreenVizSize { get => NowPlayingSettings.NpFullscreenVizSize; set => NowPlayingSettings.NpFullscreenVizSize = value; }
        public static int NpFullscreenLyricsOffsetX { get => NowPlayingSettings.NpFullscreenLyricsOffsetX; set => NowPlayingSettings.NpFullscreenLyricsOffsetX = value; }
        public static int NpFullscreenCoverOffsetX { get => NowPlayingSettings.NpFullscreenCoverOffsetX; set => NowPlayingSettings.NpFullscreenCoverOffsetX = value; }
        public static int NpFullscreenCoverOffsetY { get => NowPlayingSettings.NpFullscreenCoverOffsetY; set => NowPlayingSettings.NpFullscreenCoverOffsetY = value; }
        public static int NpFullscreenTitleOffsetX { get => NowPlayingSettings.NpFullscreenTitleOffsetX; set => NowPlayingSettings.NpFullscreenTitleOffsetX = value; }
        public static int NpFullscreenTitleOffsetY { get => NowPlayingSettings.NpFullscreenTitleOffsetY; set => NowPlayingSettings.NpFullscreenTitleOffsetY = value; }
        public static int NpFullscreenArtistOffsetX { get => NowPlayingSettings.NpFullscreenArtistOffsetX; set => NowPlayingSettings.NpFullscreenArtistOffsetX = value; }
        public static int NpFullscreenArtistOffsetY { get => NowPlayingSettings.NpFullscreenArtistOffsetY; set => NowPlayingSettings.NpFullscreenArtistOffsetY = value; }
        public static int NpFullscreenVizOffsetY { get => NowPlayingSettings.NpFullscreenVizOffsetY; set => NowPlayingSettings.NpFullscreenVizOffsetY = value; }
        public static int NpFullscreenVizPlacement { get => NowPlayingSettings.NpFullscreenVizPlacement; set => NowPlayingSettings.NpFullscreenVizPlacement = value; }

        // Visualizer-on layout presets (legacy windowed/fullscreen = viz-off)
        public static int NpVizOnCoverSize { get => NowPlayingSettings.NpVizOnCoverSize; set => NowPlayingSettings.NpVizOnCoverSize = value; }
        public static int NpVizOnTitleSize { get => NowPlayingSettings.NpVizOnTitleSize; set => NowPlayingSettings.NpVizOnTitleSize = value; }
        public static int NpVizOnSubTextSize { get => NowPlayingSettings.NpVizOnSubTextSize; set => NowPlayingSettings.NpVizOnSubTextSize = value; }
        public static int NpVizOnLyricsSize { get => NowPlayingSettings.NpVizOnLyricsSize; set => NowPlayingSettings.NpVizOnLyricsSize = value; }
        public static int NpVizOnVizSize { get => NowPlayingSettings.NpVizOnVizSize; set => NowPlayingSettings.NpVizOnVizSize = value; }
        public static int NpVizOnLyricsOffsetX { get => NowPlayingSettings.NpVizOnLyricsOffsetX; set => NowPlayingSettings.NpVizOnLyricsOffsetX = value; }
        public static int NpVizOnCoverOffsetX { get => NowPlayingSettings.NpVizOnCoverOffsetX; set => NowPlayingSettings.NpVizOnCoverOffsetX = value; }
        public static int NpVizOnCoverOffsetY { get => NowPlayingSettings.NpVizOnCoverOffsetY; set => NowPlayingSettings.NpVizOnCoverOffsetY = value; }
        public static int NpVizOnTitleOffsetX { get => NowPlayingSettings.NpVizOnTitleOffsetX; set => NowPlayingSettings.NpVizOnTitleOffsetX = value; }
        public static int NpVizOnTitleOffsetY { get => NowPlayingSettings.NpVizOnTitleOffsetY; set => NowPlayingSettings.NpVizOnTitleOffsetY = value; }
        public static int NpVizOnArtistOffsetX { get => NowPlayingSettings.NpVizOnArtistOffsetX; set => NowPlayingSettings.NpVizOnArtistOffsetX = value; }
        public static int NpVizOnArtistOffsetY { get => NowPlayingSettings.NpVizOnArtistOffsetY; set => NowPlayingSettings.NpVizOnArtistOffsetY = value; }
        public static int NpVizOnVizOffsetY { get => NowPlayingSettings.NpVizOnVizOffsetY; set => NowPlayingSettings.NpVizOnVizOffsetY = value; }
        public static int NpVizOnPlacement { get => NowPlayingSettings.NpVizOnPlacement; set => NowPlayingSettings.NpVizOnPlacement = value; }
        public static int NpFullscreenVizOnCoverSize { get => NowPlayingSettings.NpFullscreenVizOnCoverSize; set => NowPlayingSettings.NpFullscreenVizOnCoverSize = value; }
        public static int NpFullscreenVizOnTitleSize { get => NowPlayingSettings.NpFullscreenVizOnTitleSize; set => NowPlayingSettings.NpFullscreenVizOnTitleSize = value; }
        public static int NpFullscreenVizOnSubTextSize { get => NowPlayingSettings.NpFullscreenVizOnSubTextSize; set => NowPlayingSettings.NpFullscreenVizOnSubTextSize = value; }
        public static int NpFullscreenVizOnLyricsSize { get => NowPlayingSettings.NpFullscreenVizOnLyricsSize; set => NowPlayingSettings.NpFullscreenVizOnLyricsSize = value; }
        public static int NpFullscreenVizOnVizSize { get => NowPlayingSettings.NpFullscreenVizOnVizSize; set => NowPlayingSettings.NpFullscreenVizOnVizSize = value; }
        public static int NpFullscreenVizOnLyricsOffsetX { get => NowPlayingSettings.NpFullscreenVizOnLyricsOffsetX; set => NowPlayingSettings.NpFullscreenVizOnLyricsOffsetX = value; }
        public static int NpFullscreenVizOnCoverOffsetX { get => NowPlayingSettings.NpFullscreenVizOnCoverOffsetX; set => NowPlayingSettings.NpFullscreenVizOnCoverOffsetX = value; }
        public static int NpFullscreenVizOnCoverOffsetY { get => NowPlayingSettings.NpFullscreenVizOnCoverOffsetY; set => NowPlayingSettings.NpFullscreenVizOnCoverOffsetY = value; }
        public static int NpFullscreenVizOnTitleOffsetX { get => NowPlayingSettings.NpFullscreenVizOnTitleOffsetX; set => NowPlayingSettings.NpFullscreenVizOnTitleOffsetX = value; }
        public static int NpFullscreenVizOnTitleOffsetY { get => NowPlayingSettings.NpFullscreenVizOnTitleOffsetY; set => NowPlayingSettings.NpFullscreenVizOnTitleOffsetY = value; }
        public static int NpFullscreenVizOnArtistOffsetX { get => NowPlayingSettings.NpFullscreenVizOnArtistOffsetX; set => NowPlayingSettings.NpFullscreenVizOnArtistOffsetX = value; }
        public static int NpFullscreenVizOnArtistOffsetY { get => NowPlayingSettings.NpFullscreenVizOnArtistOffsetY; set => NowPlayingSettings.NpFullscreenVizOnArtistOffsetY = value; }
        public static int NpFullscreenVizOnVizOffsetY { get => NowPlayingSettings.NpFullscreenVizOnVizOffsetY; set => NowPlayingSettings.NpFullscreenVizOnVizOffsetY = value; }
        public static int NpFullscreenVizOnPlacement { get => NowPlayingSettings.NpFullscreenVizOnPlacement; set => NowPlayingSettings.NpFullscreenVizOnPlacement = value; }

        public static string NormalizeNpBackgroundAnimationMode(string? mode) => NowPlayingSettings.NormalizeNpBackgroundAnimationMode(mode);
        public static string NormalizeCoverShapeMode(string? mode) => NowPlayingSettings.NormalizeCoverShapeMode(mode);
        public static double ClampNpStarDensity(double value) => NowPlayingSettings.ClampNpStarDensity(value);
        public static double ClampNpShootingStarDensity(double value) => NowPlayingSettings.ClampNpShootingStarDensity(value);
        public static double ClampNpRainIntensity(double value) => NowPlayingSettings.ClampNpRainIntensity(value);
        public static double ClampNpRainLightningAmount(double value) => NowPlayingSettings.ClampNpRainLightningAmount(value);
        public static double ClampNpSnowDensity(double value) => NowPlayingSettings.ClampNpSnowDensity(value);
        public static double ClampNpSnowflakeAmount(double value) => NowPlayingSettings.ClampNpSnowflakeAmount(value);
        public static double ClampNpUnderwaterBubbleDensity(double value) => NowPlayingSettings.ClampNpUnderwaterBubbleDensity(value);
        public static double ClampNpUnderwaterCausticIntensity(double value) => NowPlayingSettings.ClampNpUnderwaterCausticIntensity(value);
        public static double ClampNpBackgroundAnimationSpeed(double value) => NowPlayingSettings.ClampNpBackgroundAnimationSpeed(value);
    }
}
