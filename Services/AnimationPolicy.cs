namespace AudioQualityChecker.Services
{
    /// <summary>Animated regions that Reduce Motion / Battery Saver can gate independently.</summary>
    public enum AnimationArea
    {
        NpBackground,
        Visualizer,
        CoverGlow,
        Lyrics,
        Playbar
    }

    /// <summary>
    /// Single source of truth for "should this animation run right now?". Folds
    /// together Reduce Motion (ThemeManager.AnimationsEnabled) and the Battery Saver
    /// area flags so callers don't repeat the compound condition at ~60 sites.
    /// </summary>
    public static class AnimationPolicy
    {
        /// <summary>
        /// True when ambient motion for <paramref name="area"/> is allowed. False if
        /// Reduce Motion is on, or Battery Saver suppresses this area (or the whole app).
        /// </summary>
        public static bool IsMotionAllowed(AnimationArea area)
        {
            if (!ThemeManager.AnimationsEnabled)
                return false;
            if (BatterySaverSuppresses(area))
                return false;
            return true;
        }

        // Battery Saver removes motion in an area when its master switch is on AND
        // (it targets the whole program OR the specific area flag is set). Disabling —
        // not throttling — keeps every per-area checkbox meaning the same thing and
        // gives the real power win the mode exists for.
        private static bool BatterySaverSuppresses(AnimationArea area)
        {
            if (!ThemeManager.BatterySaverEnabled)
                return false;
            // Explicit user override: the visualizer can be kept running even when Battery Saver
            // (incl. "Entire Program") would otherwise suppress it. Checked before the blanket
            // EntireProgram return so it actually wins.
            if (area == AnimationArea.Visualizer && ThemeManager.BatterySaverKeepVisualizer)
                return false;
            if (ThemeManager.BatterySaverEntireProgram)
                return true;
            return area switch
            {
                AnimationArea.NpBackground => ThemeManager.BatterySaverNpBackground,
                AnimationArea.Visualizer => ThemeManager.BatterySaverVisualizer,
                AnimationArea.CoverGlow => ThemeManager.BatterySaverCoverGlow,
                AnimationArea.Lyrics => ThemeManager.BatterySaverLyrics,
                AnimationArea.Playbar => ThemeManager.BatterySaverPlaybar,
                _ => false
            };
        }
    }
}
