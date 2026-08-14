using System;
using System.Globalization;
using AudioQualityChecker.Models;

namespace AudioQualityChecker.Services
{
    /// <summary>
    /// Snapshot and restore of the Now Playing layout as a named profile. Capture and apply both go
    /// through reflection over <see cref="NowPlayingSettings"/>, so adding a layout property to
    /// profiles is a one-line change to <see cref="ProfileKeys"/>.
    /// </summary>
    public static class NpLayoutProfileCapture
    {
        // The full set of NP-layout properties a saved profile captures: all four per-context
        // size/offset bundles plus the glow and backdrop settings.
        public static readonly string[] ProfileKeys =
        {
            // Glow + backdrop (context-agnostic)
            "NpCoverGlowSize", "NpCoverGlowMotionEnabled", "NpGlowMotionMode",
            "NpFocusedLyricsBlurRadius", "NpCoverShapeMode",
            "NpBackgroundHorizontalPosition", "NpBackgroundVerticalPosition",
            "NpBackgroundZoom", "NpBackgroundBlur", "NpBackgroundBrightness",
            // Bottom-bar button arrangement
            "NpButtonOrder", "NpButtonHidden",
            // Windowed (viz off)
            "NpCoverSize", "NpTitleSize", "NpSubTextSize", "NpLyricsSize", "NpVizSize",
            "NpLyricsOffsetX", "NpCoverOffsetX", "NpCoverOffsetY", "NpTitleOffsetX",
            "NpTitleOffsetY", "NpArtistOffsetX", "NpArtistOffsetY", "NpVizOffsetY", "NpVizPlacement",
            // Fullscreen (viz off)
            "NpFullscreenCoverSize", "NpFullscreenTitleSize", "NpFullscreenSubTextSize",
            "NpFullscreenLyricsSize", "NpFullscreenVizSize", "NpFullscreenLyricsOffsetX",
            "NpFullscreenCoverOffsetX", "NpFullscreenCoverOffsetY", "NpFullscreenTitleOffsetX",
            "NpFullscreenTitleOffsetY", "NpFullscreenArtistOffsetX", "NpFullscreenArtistOffsetY",
            "NpFullscreenVizOffsetY", "NpFullscreenVizPlacement",
            // Windowed (viz on)
            "NpVizOnCoverSize", "NpVizOnTitleSize", "NpVizOnSubTextSize", "NpVizOnLyricsSize",
            "NpVizOnVizSize", "NpVizOnLyricsOffsetX", "NpVizOnCoverOffsetX", "NpVizOnCoverOffsetY",
            "NpVizOnTitleOffsetX", "NpVizOnTitleOffsetY", "NpVizOnArtistOffsetX",
            "NpVizOnArtistOffsetY", "NpVizOnVizOffsetY", "NpVizOnPlacement",
            // Fullscreen (viz on)
            "NpFullscreenVizOnCoverSize", "NpFullscreenVizOnTitleSize", "NpFullscreenVizOnSubTextSize",
            "NpFullscreenVizOnLyricsSize", "NpFullscreenVizOnVizSize", "NpFullscreenVizOnLyricsOffsetX",
            "NpFullscreenVizOnCoverOffsetX", "NpFullscreenVizOnCoverOffsetY", "NpFullscreenVizOnTitleOffsetX",
            "NpFullscreenVizOnTitleOffsetY", "NpFullscreenVizOnArtistOffsetX", "NpFullscreenVizOnArtistOffsetY",
            "NpFullscreenVizOnVizOffsetY", "NpFullscreenVizOnPlacement",
        };

        /// <summary>Snapshots the current NP layout into a named profile.</summary>
        public static NpLayoutProfile Capture(string name)
        {
            var profile = new NpLayoutProfile { Name = name };
            foreach (var key in ProfileKeys)
            {
                var prop = typeof(NowPlayingSettings).GetProperty(key);
                var val = prop?.GetValue(null);
                if (val == null) continue;
                profile.Values[key] = val is IFormattable f
                    ? f.ToString(null, CultureInfo.InvariantCulture)
                    : val.ToString() ?? "";
            }
            return profile;
        }

        /// <summary>Applies a saved profile's values back onto the layout properties.</summary>
        public static void Apply(NpLayoutProfile profile)
        {
            if (profile?.Values == null) return;
            foreach (var key in ProfileKeys)
            {
                if (!profile.Values.TryGetValue(key, out var raw)) continue;
                var prop = typeof(NowPlayingSettings).GetProperty(key);
                if (prop == null || !prop.CanWrite) continue;

                try
                {
                    object? value = null;
                    var t = prop.PropertyType;
                    if (t == typeof(int) && int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i))
                        value = i;
                    else if (t == typeof(double) && double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var d))
                        value = d;
                    else if (t == typeof(bool) && bool.TryParse(raw, out var b))
                        value = b;
                    else if (t == typeof(string))
                        value = raw;
                    else if (t.IsEnum && Enum.TryParse(t, raw, ignoreCase: true, out var e))
                        value = e;

                    if (value != null)
                        prop.SetValue(null, value);
                }
                catch { /* skip any value that no longer parses into its property */ }
            }
        }
    }
}
