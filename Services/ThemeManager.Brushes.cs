using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using AudioQualityChecker.Models;
using AudioQualityChecker.Theming;

namespace AudioQualityChecker.Services
{
    // Theme color palettes, glass/color-match surface resources, and brush/color
    // helpers. Extracted verbatim from ThemeManager.cs (2026-06-05 large-file split).
    public static partial class ThemeManager
    {
        /// <summary>
        /// Built-in theme palette, as WPF brushes. The colour values live in
        /// <see cref="ThemePalettes"/> in the shared core so that the Avalonia front-end
        /// draws from exactly the same numbers; they were previously duplicated by hand
        /// here and had drifted apart in 8 of the 10 themes.
        /// </summary>
        private static Dictionary<string, object> GetThemeColors(string theme)
        {
            var palette = ThemePalettes.Get(theme);
            var colors = new Dictionary<string, object>(palette.Count);
            foreach (var pair in palette)
                colors[pair.Key] = BrushFrom(pair.Value);
            return colors;
        }

        private static Dictionary<string, object> GetThemeColors(CustomThemeDefinition theme)
        {
            var t = theme.Sanitize();
            return new Dictionary<string, object>
            {
                ["WindowBg"]            = BrushFromHex(t.WindowBackground),
                ["PanelBg"]             = BrushFromHex(t.PanelBackground),
                ["ToolbarBg"]           = BrushFromHex(t.ToolbarBackground),
                ["HeaderBg"]            = BrushFromHex(t.HeaderBackground),
                ["GridBg"]              = BrushFromHex(t.GridBackground),
                ["GridRowBg"]           = BrushFromHex(t.GridRowBackground),
                ["GridAltRowBg"]        = BrushFromHex(t.GridAltRowBackground),
                ["BorderColor"]         = BrushFromHex(t.BorderColor),
                ["InputBg"]             = BrushFromHex(t.InputBackground),
                ["SelectionBg"]         = BrushFromHex(t.SelectionColor, 210),
                ["ButtonBg"]            = BrushFromHex(t.ButtonBackground, 220),
                ["ButtonBorder"]        = BrushFromHex(t.ButtonBorder),
                ["ButtonHover"]         = BrushFromHex(t.ButtonHover, 235),
                ["ButtonPressed"]       = BrushFromHex(t.ButtonPressed),
                ["AccentColor"]         = BrushFromHex(t.AccentColor),
                ["TextPrimary"]         = BrushFromHex(t.TextPrimary),
                ["TextSecondary"]       = BrushFromHex(t.TextSecondary),
                ["TextMuted"]           = BrushFromHex(t.TextMuted),
                ["TextDim"]             = BrushFromHex(t.TextDim),
                ["ScrollBg"]            = BrushFromHex(t.InputBackground, 210),
                ["ScrollThumb"]         = BrushFromHex(t.AccentColor, 210),
                ["ScrollThumbHover"]    = BrushFromHex(t.ButtonPressed),
                ["GridLineColor"]       = BrushFromHex(t.BorderColor, 120),
                ["RowHoverBg"]          = BrushFromHex(t.ButtonHover, 160),
                ["SplitterBg"]          = BrushFromHex(t.ToolbarBackground),
                ["ProgressBg"]          = BrushFromHex(t.ButtonBackground, 200),
            };
        }

        private static void ApplyGlassResources(
            ResourceDictionary resources,
            IReadOnlyDictionary<string, object> colors,
            CustomThemeDefinition? customTheme)
        {
            var cornerSoftness = customTheme?.Sanitize().CornerSoftness ?? 5.0;
            resources["GlassPanelBg"] = colors.TryGetValue("PanelBg", out var panel) ? panel : BrushFrom("#FF1A1B36");
            resources["GlassToolbarBg"] = colors.TryGetValue("ToolbarBg", out var toolbar) ? toolbar : BrushFrom("#FF2C2D56");
            // Title-bar background for the secondary tool windows (Credits, Metadata editor,
            // Spectrogram/Waveform viewers, etc.). Matches the toolbar so their custom chrome
            // blends with the OS caption. Defined here so the key always resolves — several
            // windows call FindResource("TitleBarBg"), which THROWS on a missing key.
            resources["TitleBarBg"] = colors.TryGetValue("ToolbarBg", out var titleBar) ? titleBar : BrushFrom("#FF2C2D56");
            resources["GlassHeaderBg"] = colors.TryGetValue("HeaderBg", out var header) ? header : BrushFrom("#FF353668");
            resources["GlassFloatingBg"] = colors.TryGetValue("PanelBg", out var floating) ? floating : BrushFrom("#FF1A1B36");
            resources["GlassOverlayBg"] = colors.TryGetValue("WindowBg", out var overlay) ? overlay : BrushFrom("#FF101226");
            resources["GlassBorderBrush"] = colors.TryGetValue("BorderColor", out var border) ? border : BrushFrom("#FF4A4B8A");
            resources["GlassHighlightBrush"] = BrushFrom("#26FFFFFF");
            resources["GlassShadowBrush"] = BrushFrom("#99000000");
            resources["GlassOpacity"] = 1.0;
            resources["GlassBlurRadius"] = 0.0;
            resources["GlassCornerRadius"] = cornerSoftness;
        }

        public static void ApplyColorMatchSurfaceResources(Color primary, Color secondary, Color background)
        {
            if (Application.Current == null)
                return;

            var resources = Application.Current.Resources;
            var basePanel = BrushColor(resources["PanelBg"], Color.FromRgb(26, 27, 54));
            var baseToolbar = BrushColor(resources["ToolbarBg"], Color.FromRgb(44, 45, 86));
            var baseWindow = BrushColor(resources["WindowBg"], Color.FromRgb(16, 18, 38));
            var baseBorder = BrushColor(resources["BorderColor"], Color.FromRgb(74, 75, 138));
            var panel = MixColor(basePanel, DarkenForSurface(background, 0.34), 0.52);
            var toolbar = MixColor(baseToolbar, DarkenForSurface(primary, 0.30), 0.44);
            var overlay = MixColor(baseWindow, DarkenForSurface(background, 0.26), 0.40);
            var border = MixColor(baseBorder, secondary, 0.34);

            resources["GlassPanelBg"] = BrushFromColor(panel);
            resources["GlassToolbarBg"] = BrushFromColor(toolbar);
            resources["GlassHeaderBg"] = BrushFromColor(MixColor(baseToolbar, primary, 0.30));
            resources["GlassFloatingBg"] = BrushFromColor(MixColor(panel, basePanel, 0.55));
            resources["GlassOverlayBg"] = BrushFromColor(overlay);
            resources["GlassBorderBrush"] = BrushFromColor(WithAlpha(border, 224));
            resources["GlassHighlightBrush"] = BrushFromColor(WithAlpha(MixColor(Colors.White, primary, 0.24), 88));
        }

        private static SolidColorBrush BrushFrom(string hex)
        {
            var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
            brush.Freeze();
            return brush;
        }

        private static SolidColorBrush BrushFromColor(Color color)
        {
            var brush = new SolidColorBrush(color);
            brush.Freeze();
            return brush;
        }

        private static SolidColorBrush BrushFromHex(string hex, byte alpha = 255)
        {
            var color = HexToColor(hex);
            color.A = alpha;
            var brush = new SolidColorBrush(color);
            brush.Freeze();
            return brush;
        }

        private static Color BrushColor(object? resource, Color fallback)
        {
            return resource is SolidColorBrush brush ? brush.Color : fallback;
        }

        private static Color HexToColor(string hex)
        {
            var color = (Color)ColorConverter.ConvertFromString(hex)!;
            color.A = 255;
            return color;
        }

        private static Color MixColor(Color first, Color second, double secondWeight)
        {
            secondWeight = Math.Clamp(secondWeight, 0, 1);
            var firstWeight = 1 - secondWeight;
            return Color.FromRgb(
                (byte)Math.Round(first.R * firstWeight + second.R * secondWeight),
                (byte)Math.Round(first.G * firstWeight + second.G * secondWeight),
                (byte)Math.Round(first.B * firstWeight + second.B * secondWeight));
        }

        private static Color DarkenForSurface(Color color, double amount)
        {
            amount = Math.Clamp(amount, 0, 1);
            return Color.FromRgb(
                (byte)Math.Round(color.R * amount),
                (byte)Math.Round(color.G * amount),
                (byte)Math.Round(color.B * amount));
        }

        private static PlaybarColors ColorsFromThemePalette(CustomThemeDefinition theme, bool useVisualizerColors)
        {
            var t = theme.Sanitize();
            var palette = useVisualizerColors ? t.VisualizerColors : t.PlaybarColors;
            return new PlaybarColors(
                BrushFromHex(t.AccentColor, 64).Color,
                new[]
                {
                    WithAlpha(HexToColor(palette[0]), 190),
                    WithAlpha(HexToColor(palette[1]), 230),
                    WithAlpha(HexToColor(palette[2]), 255)
                },
                2.2);
        }

        private static Color WithAlpha(Color color, byte alpha)
        {
            color.A = alpha;
            return color;
        }
    }
}
