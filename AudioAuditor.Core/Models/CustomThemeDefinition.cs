using System.Text.Json;
using System.Text.RegularExpressions;

namespace AudioQualityChecker.Models;

public sealed record CustomThemeDefinition
{
    private static readonly Regex HexColorPattern = new("^#[0-9A-Fa-f]{6}$", RegexOptions.Compiled);

    public int SchemaVersion { get; init; } = 1;
    public string Name { get; init; } = "Custom Theme";
    public string BaseTheme { get; init; } = "Blurple";
    public string WindowBackground { get; init; } = "#14162A";
    public string PanelBackground { get; init; } = "#171A33";
    public string ToolbarBackground { get; init; } = "#20244A";
    public string HeaderBackground { get; init; } = "#272C5C";
    public string GridBackground { get; init; } = "#151831";
    public string GridRowBackground { get; init; } = "#191C38";
    public string GridAltRowBackground { get; init; } = "#20244A";
    public string BorderColor { get; init; } = "#7180FF";
    public string InputBackground { get; init; } = "#1D2142";
    public string SelectionColor { get; init; } = "#5865F2";
    public string ButtonBackground { get; init; } = "#262B58";
    public string ButtonBorder { get; init; } = "#7D89FF";
    public string ButtonHover { get; init; } = "#333A78";
    public string ButtonPressed { get; init; } = "#8C96FF";
    public string AccentColor { get; init; } = "#5865F2";
    public string TextPrimary { get; init; } = "#F2F4FF";
    public string TextSecondary { get; init; } = "#C4CAFF";
    public string TextMuted { get; init; } = "#8E96D8";
    public string TextDim { get; init; } = "#5E659E";
    public string GlassTint { get; init; } = "#171A33";
    public double GlassOpacity { get; init; } = 1.0;
    public double GlassBlur { get; init; } = 0;
    public double GlassHighlight { get; init; } = 0;
    public double GlassShadow { get; init; } = 0;
    public double CornerSoftness { get; init; } = 8;
    public string[] PlaybarColors { get; init; } = ["#3C46B6", "#5865F2", "#8C96FF"];
    public string[] VisualizerColors { get; init; } = ["#3C46B6", "#5865F2", "#8C96FF"];

    public static JsonSerializerOptions JsonOptions { get; } = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    public static CustomThemeDefinition CreateDefault() => new()
    {
        Name = "Custom Theme",
        BaseTheme = "Blurple",
        WindowBackground = "#0F1328",
        PanelBackground = "#171C3A",
        ToolbarBackground = "#20285A",
        HeaderBackground = "#2A3270",
        GridBackground = "#11152C",
        GridRowBackground = "#171C3A",
        GridAltRowBackground = "#20264F",
        BorderColor = "#9AA6FF",
        InputBackground = "#1C2348",
        SelectionColor = "#5865F2",
        ButtonBackground = "#283064",
        ButtonBorder = "#AAB2FF",
        ButtonHover = "#35407D",
        ButtonPressed = "#AAB2FF",
        AccentColor = "#5865F2",
        TextPrimary = "#F4F6FF",
        TextSecondary = "#C9CEFF",
        TextMuted = "#9AA2E8",
        TextDim = "#6870AD",
        GlassTint = "#171C3A",
        GlassOpacity = 1.0,
        GlassBlur = 0,
        GlassHighlight = 0,
        GlassShadow = 0,
        CornerSoftness = 8,
        PlaybarColors = ["#3C46B6", "#5865F2", "#8C96FF"],
        VisualizerColors = ["#3C46B6", "#5865F2", "#8C96FF"]
    };

    public CustomThemeDefinition Sanitize()
    {
        var fallback = CreateDefault();
        return this with
        {
            SchemaVersion = SchemaVersion <= 0 ? 1 : SchemaVersion,
            Name = string.IsNullOrWhiteSpace(Name) ? "Custom Theme" : Name.Trim(),
            BaseTheme = string.IsNullOrWhiteSpace(BaseTheme) ? "Blurple" : BaseTheme.Trim(),
            WindowBackground = ColorOr(WindowBackground, fallback.WindowBackground),
            PanelBackground = ColorOr(PanelBackground, fallback.PanelBackground),
            ToolbarBackground = ColorOr(ToolbarBackground, fallback.ToolbarBackground),
            HeaderBackground = ColorOr(HeaderBackground, fallback.HeaderBackground),
            GridBackground = ColorOr(GridBackground, fallback.GridBackground),
            GridRowBackground = ColorOr(GridRowBackground, fallback.GridRowBackground),
            GridAltRowBackground = ColorOr(GridAltRowBackground, fallback.GridAltRowBackground),
            BorderColor = ColorOr(BorderColor, fallback.BorderColor),
            InputBackground = ColorOr(InputBackground, fallback.InputBackground),
            SelectionColor = ColorOr(SelectionColor, fallback.SelectionColor),
            ButtonBackground = ColorOr(ButtonBackground, fallback.ButtonBackground),
            ButtonBorder = ColorOr(ButtonBorder, fallback.ButtonBorder),
            ButtonHover = ColorOr(ButtonHover, fallback.ButtonHover),
            ButtonPressed = ColorOr(ButtonPressed, fallback.ButtonPressed),
            AccentColor = ColorOr(AccentColor, fallback.AccentColor),
            TextPrimary = ColorOr(TextPrimary, fallback.TextPrimary),
            TextSecondary = ColorOr(TextSecondary, fallback.TextSecondary),
            TextMuted = ColorOr(TextMuted, fallback.TextMuted),
            TextDim = ColorOr(TextDim, fallback.TextDim),
            GlassTint = ColorOr(GlassTint, fallback.GlassTint),
            GlassOpacity = Clamp(GlassOpacity, 0, 1, fallback.GlassOpacity),
            GlassBlur = Clamp(GlassBlur, 0, 40, fallback.GlassBlur),
            GlassHighlight = Clamp(GlassHighlight, 0, 1, fallback.GlassHighlight),
            GlassShadow = Clamp(GlassShadow, 0, 1, fallback.GlassShadow),
            CornerSoftness = Clamp(CornerSoftness, 2, 16, fallback.CornerSoftness),
            PlaybarColors = NormalizePalette(PlaybarColors, fallback.PlaybarColors),
            VisualizerColors = NormalizePalette(VisualizerColors, fallback.VisualizerColors)
        };
    }

    private static string ColorOr(string? value, string fallback)
    {
        var candidate = value?.Trim();
        return candidate != null && HexColorPattern.IsMatch(candidate)
            ? candidate.ToUpperInvariant()
            : fallback;
    }

    private static string[] NormalizePalette(string[]? colors, string[] fallback)
    {
        if (colors == null || colors.Length < 3)
            return [.. fallback];

        var normalized = colors
            .Take(3)
            .Select((color, index) => ColorOr(color, fallback[index]))
            .ToArray();

        return normalized.Length == 3 ? normalized : [.. fallback];
    }

    private static double Clamp(double value, double min, double max, double fallback)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
            return fallback;
        return Math.Clamp(value, min, max);
    }
}
