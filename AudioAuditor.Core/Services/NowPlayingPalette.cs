using Color = AudioQualityChecker.Services.AlbumColorExtractor.Color;
using DominantColors = AudioQualityChecker.Services.AlbumColorExtractor.DominantColors;

namespace AudioQualityChecker.Services
{
    /// <summary>
    /// What the Now Playing screen should paint with right now, and where it came from.
    /// </summary>
    /// <param name="UseColorMatch">
    /// False when ColorMatch is off. Callers must then keep using theme colours and ignore
    /// the rest of this record.
    /// </param>
    /// <param name="FromAlbum">
    /// True when the colours are the album's. False means the neutral fallback is in use
    /// because extraction has not finished, there is no cover, or the cover was greyscale.
    /// </param>
    public sealed record NowPlayingColors(
        bool UseColorMatch,
        bool FromAlbum,
        Color Primary,
        Color Secondary,
        Color Tertiary,
        Color Background);

    /// <summary>
    /// Single source of truth for the Now Playing ColorMatch palette.
    ///
    /// When ColorMatch is on, the screen must use only album-extracted colours with zero
    /// influence from the app or playbar theme. The problem this solves: each consumer
    /// (playbar, icons, EQ sliders, background effects) used to fall back to theme colours
    /// whenever the album palette was not ready — still extracting, no cover art, or a
    /// greyscale cover — which let the theme bleed through in exactly the cases where the
    /// user had asked for it not to.
    ///
    /// Routing every consumer through <see cref="Resolve"/> means a ColorMatch-on screen
    /// always gets a theme-independent palette: the album's when available, a deterministic
    /// neutral one otherwise. Extraction is async, so the neutral palette shows immediately
    /// and callers re-render once the real colours arrive.
    /// </summary>
    public static class NowPlayingPalette
    {
        // Cool, near-grey tones that read well on the dark backdrop and never reference a
        // theme resource. Deliberately desaturated so the fallback reads as "no album colours
        // yet" rather than as a tint someone chose.
        public static readonly Color NeutralPrimary = new(150, 156, 168);
        public static readonly Color NeutralSecondary = new(120, 126, 140);
        public static readonly Color NeutralTertiary = new(96, 102, 116);
        public static readonly Color NeutralBackground = new(40, 43, 52);

        /// <summary>
        /// Resolves the palette. <paramref name="album"/> is null until extraction finishes,
        /// or permanently for a cover the sanitiser rejected.
        /// </summary>
        public static NowPlayingColors Resolve(bool colorMatchEnabled, DominantColors? album)
        {
            if (!colorMatchEnabled)
                return Neutral(useColorMatch: false);

            if (album == null)
                return Neutral(useColorMatch: true);

            // Sanitising can still reject the palette — an all-grey cover yields nothing
            // worth painting with — which is also a fallback case, not an album one.
            var resolved = AlbumColorExtractor.SanitizeDominantColors(album);
            if (IsUnusable(resolved))
                return Neutral(useColorMatch: true);

            return new NowPlayingColors(
                UseColorMatch: true,
                FromAlbum: true,
                resolved.Primary,
                resolved.Secondary,
                resolved.Tertiary,
                resolved.Background);
        }

        private static NowPlayingColors Neutral(bool useColorMatch) => new(
            useColorMatch,
            FromAlbum: false,
            NeutralPrimary,
            NeutralSecondary,
            NeutralTertiary,
            NeutralBackground);

        /// <summary>
        /// A palette with a fully transparent primary carries no colour to paint with; the
        /// sanitiser uses that to signal "nothing usable here".
        /// </summary>
        private static bool IsUnusable(DominantColors colors) => colors.Primary.A == 0;
    }
}
