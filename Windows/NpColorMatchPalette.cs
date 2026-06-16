using System.Windows.Media;

namespace AudioQualityChecker
{
    /// <summary>
    /// Single source of truth for the Now Playing screen's ColorMatch palette.
    ///
    /// When ColorMatch is ON, the NP screen must use ONLY album-extracted colors with
    /// ZERO influence from the app theme or playbar theme. The problem this solves: many
    /// individual consumers (playbar, icons, EQ sliders, background animations) used to
    /// fall back to theme colors whenever the album palette wasn't ready (still extracting,
    /// no cover art, or a grayscale cover). That let the theme "bleed through".
    ///
    /// The fix routes every consumer through <see cref="NpGetEffectiveColorMatchPalette"/>,
    /// which ALWAYS yields a theme-independent palette while ColorMatch is on: the real
    /// album colors when available, otherwise a deterministic NEUTRAL palette. Album
    /// extraction is async — the neutral palette shows immediately and a re-render is
    /// triggered (see NpResetColorMatchCaches / NpApplyColorMatchMode) once colors arrive.
    /// </summary>
    public partial class MainWindow
    {
        /// <summary>
        /// Theme-independent neutral palette used as the ColorMatch fallback. Cool, near-gray
        /// tones that read well on the dark NP backdrop and never reference theme resources.
        /// Deliberately desaturated so it's clearly a "no album colors yet" state, not a tint.
        /// </summary>
        private static readonly Color NeutralPrimary    = Color.FromRgb(150, 156, 168);
        private static readonly Color NeutralSecondary  = Color.FromRgb(120, 126, 140);
        private static readonly Color NeutralTertiary   = Color.FromRgb(96, 102, 116);
        private static readonly Color NeutralBackground = Color.FromRgb(40, 43, 52);

        /// <summary>
        /// Returns the palette the NP screen should paint with right now.
        ///
        /// Contract: when ColorMatch is ON this NEVER returns theme-derived colors — it
        /// returns the album/manual palette if resolved, else the neutral fallback, and
        /// <paramref name="fromAlbum"/> reports which. Callers that previously fell back to
        /// theme colors should instead use this and treat it as authoritative.
        ///
        /// When ColorMatch is OFF, returns false and outputs the neutral palette only as a
        /// safe default; OFF-mode callers should keep using theme colors as before and ignore
        /// these outputs (they should gate on the return value).
        /// </summary>
        /// <returns>true if ColorMatch is ON (NP should use the out palette); false if OFF.</returns>
        private bool NpGetEffectiveColorMatchPalette(
            out Color primary,
            out Color secondary,
            out Color tertiary,
            out Color background,
            out bool fromAlbum)
        {
            if (!_npColorMatchEnabled)
            {
                primary = NeutralPrimary;
                secondary = NeutralSecondary;
                tertiary = NeutralTertiary;
                background = NeutralBackground;
                fromAlbum = false;
                return false;
            }

            if (NpTryResolveActiveColorMatchPalette(out primary, out secondary, out tertiary, out background))
            {
                fromAlbum = true;
                return true;
            }

            // ColorMatch is on but album colors aren't available (loading, no art, or
            // grayscale cover that sanitization rejected). Use the neutral palette — never
            // the theme — so the NP look stays theme-independent.
            primary = NeutralPrimary;
            secondary = NeutralSecondary;
            tertiary = NeutralTertiary;
            background = NeutralBackground;
            fromAlbum = false;
            return true;
        }
    }
}
