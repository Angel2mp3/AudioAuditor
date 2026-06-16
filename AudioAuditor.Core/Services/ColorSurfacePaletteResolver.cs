namespace AudioQualityChecker.Services;

internal static class ColorSurfacePaletteResolver
{
    public static AlbumColorExtractor.DominantColors Resolve(AlbumColorExtractor.DominantColors colors) =>
        AlbumColorExtractor.SanitizeDominantColors(colors);

    public static AlbumColorExtractor.Color[] ResolveVisualizerGradient(AlbumColorExtractor.DominantColors colors)
    {
        var sanitized = Resolve(colors);
        return [sanitized.Primary, sanitized.Secondary, sanitized.Tertiary];
    }

    public static AlbumColorExtractor.Color ResolveTitleBarColor(
        bool nowPlayingVisible,
        AlbumColorExtractor.Color? bottomBarColor,
        AlbumColorExtractor.Color? mainToolbarColor)
    {
        if (nowPlayingVisible && bottomBarColor is { A: > 0 } bottom)
            return bottom;

        if (mainToolbarColor is { A: > 0 } toolbar)
            return toolbar;

        return Resolve(new AlbumColorExtractor.DominantColors(
            new AlbumColorExtractor.Color(128, 128, 128),
            new AlbumColorExtractor.Color(166, 166, 166),
            new AlbumColorExtractor.Color(202, 202, 202),
            new AlbumColorExtractor.Color(22, 22, 22),
            new AlbumColorExtractor.Color(240, 240, 240))).Background;
    }
}
