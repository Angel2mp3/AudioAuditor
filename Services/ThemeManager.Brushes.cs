using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using AudioQualityChecker.Models;

namespace AudioQualityChecker.Services
{
    // Theme color palettes, glass/color-match surface resources, and brush/color
    // helpers. Extracted verbatim from ThemeManager.cs (2026-06-05 large-file split).
    public static partial class ThemeManager
    {
        private static Dictionary<string, object> GetThemeColors(string theme)
        {
            return theme switch
            {
                "Ocean" => new Dictionary<string, object>
                {
                    ["WindowBg"]            = BrushFrom("#FF0D1B2A"),
                    ["PanelBg"]             = BrushFrom("#FF0A1628"),
                    ["ToolbarBg"]           = BrushFrom("#FF132238"),
                    ["HeaderBg"]            = BrushFrom("#FF1A2D47"),
                    ["GridBg"]              = BrushFrom("#FF0A1628"),
                    ["GridRowBg"]           = BrushFrom("#FF0D1B2A"),
                    ["GridAltRowBg"]        = BrushFrom("#FF112240"),
                    ["BorderColor"]         = BrushFrom("#FF1E3A5F"),
                    ["InputBg"]             = BrushFrom("#FF0F1D30"),
                    ["SelectionBg"]         = BrushFrom("#FF1A4B7A"),
                    ["ButtonBg"]            = BrushFrom("#FF162D4A"),
                    ["ButtonBorder"]        = BrushFrom("#FF1E3A5F"),
                    ["ButtonHover"]         = BrushFrom("#FF1E4468"),
                    ["ButtonPressed"]       = BrushFrom("#FF4DA8DA"),
                    ["AccentColor"]         = BrushFrom("#FF4DA8DA"),
                    ["TextPrimary"]         = BrushFrom("#FFD0E4F5"),
                    ["TextSecondary"]       = BrushFrom("#FF8BB8D6"),
                    ["TextMuted"]           = BrushFrom("#FF5A8AAD"),
                    ["TextDim"]             = BrushFrom("#FF2E5070"),
                    ["ScrollBg"]            = BrushFrom("#FF0F1D30"),
                    ["ScrollThumb"]         = BrushFrom("#FF3A6080"),
                    ["ScrollThumbHover"]    = BrushFrom("#FF4A7898"),
                    ["GridLineColor"]       = BrushFrom("#FF152A42"),
                    ["RowHoverBg"]          = BrushFrom("#FF142838"),
                    ["SplitterBg"]          = BrushFrom("#FF132238"),
                    ["ProgressBg"]          = BrushFrom("#FF162D4A"),
                },
                "Light" => new Dictionary<string, object>
                {
                    ["WindowBg"]            = BrushFrom("#FFF5F5F5"),
                    ["PanelBg"]             = BrushFrom("#FFFFFFFF"),
                    ["ToolbarBg"]           = BrushFrom("#FFE8E8EC"),
                    ["HeaderBg"]            = BrushFrom("#FFDCDCE0"),
                    ["GridBg"]              = BrushFrom("#FFFFFFFF"),
                    ["GridRowBg"]           = BrushFrom("#FFFFFFFF"),
                    ["GridAltRowBg"]        = BrushFrom("#FFF8F8FA"),
                    ["BorderColor"]         = BrushFrom("#FFCCCCCC"),
                    ["InputBg"]             = BrushFrom("#FFFFFFFF"),
                    ["SelectionBg"]         = BrushFrom("#FF0078D4"),
                    ["ButtonBg"]            = BrushFrom("#FFE1E1E1"),
                    ["ButtonBorder"]        = BrushFrom("#FFBBBBBB"),
                    ["ButtonHover"]         = BrushFrom("#FFD0D0D0"),
                    ["ButtonPressed"]       = BrushFrom("#FF0078D4"),
                    ["AccentColor"]         = BrushFrom("#FF0078D4"),
                    ["TextPrimary"]         = BrushFrom("#FF1E1E1E"),
                    ["TextSecondary"]       = BrushFrom("#FF444444"),
                    ["TextMuted"]           = BrushFrom("#FF666666"),
                    ["TextDim"]             = BrushFrom("#FF777777"),
                    ["ScrollBg"]            = BrushFrom("#FFE8E8E8"),
                    ["ScrollThumb"]         = BrushFrom("#FFA0A0A0"),
                    ["ScrollThumbHover"]    = BrushFrom("#FF808080"),
                    ["GridLineColor"]       = BrushFrom("#FFE0E0E0"),
                    ["RowHoverBg"]          = BrushFrom("#FFEAF1FB"),
                    ["SplitterBg"]          = BrushFrom("#FFDCDCE0"),
                    ["ProgressBg"]          = BrushFrom("#FFE0E0E0"),
                },
                "Amethyst" => new Dictionary<string, object>
                {
                    ["WindowBg"]            = BrushFrom("#FF1A1228"),
                    ["PanelBg"]             = BrushFrom("#FF150E22"),
                    ["ToolbarBg"]           = BrushFrom("#FF221838"),
                    ["HeaderBg"]            = BrushFrom("#FF2C2044"),
                    ["GridBg"]              = BrushFrom("#FF150E22"),
                    ["GridRowBg"]           = BrushFrom("#FF1A1228"),
                    ["GridAltRowBg"]        = BrushFrom("#FF201638"),
                    ["BorderColor"]         = BrushFrom("#FF3D2A5C"),
                    ["InputBg"]             = BrushFrom("#FF1E142E"),
                    ["SelectionBg"]         = BrushFrom("#FF5A2E8C"),
                    ["ButtonBg"]            = BrushFrom("#FF2A1E42"),
                    ["ButtonBorder"]        = BrushFrom("#FF4A3468"),
                    ["ButtonHover"]         = BrushFrom("#FF3A2858"),
                    ["ButtonPressed"]       = BrushFrom("#FF8B5CF6"),
                    ["AccentColor"]         = BrushFrom("#FF8B5CF6"),
                    ["TextPrimary"]         = BrushFrom("#FFE0D4F5"),
                    ["TextSecondary"]       = BrushFrom("#FFB8A0D6"),
                    ["TextMuted"]           = BrushFrom("#FF7860A0"),
                    ["TextDim"]             = BrushFrom("#FF463060"),
                    ["ScrollBg"]            = BrushFrom("#FF1E142E"),
                    ["ScrollThumb"]         = BrushFrom("#FF5A4480"),
                    ["ScrollThumbHover"]    = BrushFrom("#FF7860A0"),
                    ["GridLineColor"]       = BrushFrom("#FF251A3A"),
                    ["RowHoverBg"]          = BrushFrom("#FF241A36"),
                    ["SplitterBg"]          = BrushFrom("#FF221838"),
                    ["ProgressBg"]          = BrushFrom("#FF2A1E42"),
                },
                "Dreamsicle" => new Dictionary<string, object>
                {
                    ["WindowBg"]            = BrushFrom("#FF1F1510"),
                    ["PanelBg"]             = BrushFrom("#FF1A120C"),
                    ["ToolbarBg"]           = BrushFrom("#FF2E1E14"),
                    ["HeaderBg"]            = BrushFrom("#FF3A2818"),
                    ["GridBg"]              = BrushFrom("#FF1A120C"),
                    ["GridRowBg"]           = BrushFrom("#FF1F1510"),
                    ["GridAltRowBg"]        = BrushFrom("#FF2A1C12"),
                    ["BorderColor"]         = BrushFrom("#FF5A3820"),
                    ["InputBg"]             = BrushFrom("#FF241A12"),
                    ["SelectionBg"]         = BrushFrom("#FF8B4513"),
                    ["ButtonBg"]            = BrushFrom("#FF352414"),
                    ["ButtonBorder"]        = BrushFrom("#FF6B4228"),
                    ["ButtonHover"]         = BrushFrom("#FF45301C"),
                    ["ButtonPressed"]       = BrushFrom("#FFFF8C42"),
                    ["AccentColor"]         = BrushFrom("#FFFF8C42"),
                    ["TextPrimary"]         = BrushFrom("#FFF5E0CC"),
                    ["TextSecondary"]       = BrushFrom("#FFD6A87A"),
                    ["TextMuted"]           = BrushFrom("#FF9A7050"),
                    ["TextDim"]             = BrushFrom("#FF5A3E28"),
                    ["ScrollBg"]            = BrushFrom("#FF241A12"),
                    ["ScrollThumb"]         = BrushFrom("#FF7A5030"),
                    ["ScrollThumbHover"]    = BrushFrom("#FF9A6840"),
                    ["GridLineColor"]       = BrushFrom("#FF2E1E14"),
                    ["RowHoverBg"]          = BrushFrom("#FF2E2014"),
                    ["SplitterBg"]          = BrushFrom("#FF2E1E14"),
                    ["ProgressBg"]          = BrushFrom("#FF352414"),
                },
                "Goldenrod" => new Dictionary<string, object>
                {
                    ["WindowBg"]            = BrushFrom("#FF1E1C0E"),
                    ["PanelBg"]             = BrushFrom("#FF1A180A"),
                    ["ToolbarBg"]           = BrushFrom("#FF383010"),
                    ["HeaderBg"]            = BrushFrom("#FF4A4018"),
                    ["GridBg"]              = BrushFrom("#FF1A180A"),
                    ["GridRowBg"]           = BrushFrom("#FF1E1C0E"),
                    ["GridAltRowBg"]        = BrushFrom("#FF2E2810"),
                    ["BorderColor"]         = BrushFrom("#FF6B5A18"),
                    ["InputBg"]             = BrushFrom("#FF262010"),
                    ["SelectionBg"]         = BrushFrom("#FF9A8010"),
                    ["ButtonBg"]            = BrushFrom("#FF3E3510"),
                    ["ButtonBorder"]        = BrushFrom("#FF7A6820"),
                    ["ButtonHover"]         = BrushFrom("#FF504618"),
                    ["ButtonPressed"]       = BrushFrom("#FFE8B811"),
                    ["AccentColor"]         = BrushFrom("#FFE8B811"),
                    ["TextPrimary"]         = BrushFrom("#FFF5ECCC"),
                    ["TextSecondary"]       = BrushFrom("#FFDCC680"),
                    ["TextMuted"]           = BrushFrom("#FFAA9445"),
                    ["TextDim"]             = BrushFrom("#FF6A5828"),
                    ["ScrollBg"]            = BrushFrom("#FF262010"),
                    ["ScrollThumb"]         = BrushFrom("#FF8A7428"),
                    ["ScrollThumbHover"]    = BrushFrom("#FFAA9438"),
                    ["GridLineColor"]       = BrushFrom("#FF322C10"),
                    ["RowHoverBg"]          = BrushFrom("#FF322C14"),
                    ["SplitterBg"]          = BrushFrom("#FF383010"),
                    ["ProgressBg"]          = BrushFrom("#FF3E3510"),
                },
                "Emerald" => new Dictionary<string, object>
                {
                    ["WindowBg"]            = BrushFrom("#FF0F1C14"),
                    ["PanelBg"]             = BrushFrom("#FF0A1810"),
                    ["ToolbarBg"]           = BrushFrom("#FF14281C"),
                    ["HeaderBg"]            = BrushFrom("#FF1A3424"),
                    ["GridBg"]              = BrushFrom("#FF0A1810"),
                    ["GridRowBg"]           = BrushFrom("#FF0F1C14"),
                    ["GridAltRowBg"]        = BrushFrom("#FF12241A"),
                    ["BorderColor"]         = BrushFrom("#FF1E5A3A"),
                    ["InputBg"]             = BrushFrom("#FF0E2018"),
                    ["SelectionBg"]         = BrushFrom("#FF1A7A4A"),
                    ["ButtonBg"]            = BrushFrom("#FF162D20"),
                    ["ButtonBorder"]        = BrushFrom("#FF1E5A3A"),
                    ["ButtonHover"]         = BrushFrom("#FF1E4430"),
                    ["ButtonPressed"]       = BrushFrom("#FF2ECC71"),
                    ["AccentColor"]         = BrushFrom("#FF2ECC71"),
                    ["TextPrimary"]         = BrushFrom("#FFD0F5E0"),
                    ["TextSecondary"]       = BrushFrom("#FF8BD6AA"),
                    ["TextMuted"]           = BrushFrom("#FF5AAD7A"),
                    ["TextDim"]             = BrushFrom("#FF2E7050"),
                    ["ScrollBg"]            = BrushFrom("#FF0E2018"),
                    ["ScrollThumb"]         = BrushFrom("#FF3A8060"),
                    ["ScrollThumbHover"]    = BrushFrom("#FF4A9870"),
                    ["GridLineColor"]       = BrushFrom("#FF142A1E"),
                    ["RowHoverBg"]          = BrushFrom("#FF142820"),
                    ["SplitterBg"]          = BrushFrom("#FF14281C"),
                    ["ProgressBg"]          = BrushFrom("#FF162D20"),
                },
                "Blurple" => new Dictionary<string, object>
                {
                    ["WindowBg"]            = BrushFrom("#FF1E1F3B"),
                    ["PanelBg"]             = BrushFrom("#FF1A1B36"),
                    ["ToolbarBg"]           = BrushFrom("#FF2C2D56"),
                    ["HeaderBg"]            = BrushFrom("#FF353668"),
                    ["GridBg"]              = BrushFrom("#FF1A1B36"),
                    ["GridRowBg"]           = BrushFrom("#FF1E1F3B"),
                    ["GridAltRowBg"]        = BrushFrom("#FF272850"),
                    ["BorderColor"]         = BrushFrom("#FF4A4B8A"),
                    ["InputBg"]             = BrushFrom("#FF222344"),
                    ["SelectionBg"]         = BrushFrom("#FF4752C4"),
                    ["ButtonBg"]            = BrushFrom("#FF30325E"),
                    ["ButtonBorder"]        = BrushFrom("#FF5865F2"),
                    ["ButtonHover"]         = BrushFrom("#FF3D3F76"),
                    ["ButtonPressed"]       = BrushFrom("#FF7289DA"),
                    ["AccentColor"]         = BrushFrom("#FF5865F2"),
                    ["TextPrimary"]         = BrushFrom("#FFE0E1FF"),
                    ["TextSecondary"]       = BrushFrom("#FFA5A7D4"),
                    ["TextMuted"]           = BrushFrom("#FF7375B0"),
                    ["TextDim"]             = BrushFrom("#FF464878"),
                    ["ScrollBg"]            = BrushFrom("#FF222344"),
                    ["ScrollThumb"]         = BrushFrom("#FF5865F2"),
                    ["ScrollThumbHover"]    = BrushFrom("#FF7289DA"),
                    ["GridLineColor"]       = BrushFrom("#FF2A2B50"),
                    ["RowHoverBg"]          = BrushFrom("#FF2E2F58"),
                    ["SplitterBg"]          = BrushFrom("#FF2C2D56"),
                    ["ProgressBg"]          = BrushFrom("#FF30325E"),
                },
                "Crimson" => new Dictionary<string, object>
                {
                    ["WindowBg"]            = BrushFrom("#FF1E1012"),
                    ["PanelBg"]             = BrushFrom("#FF180C0E"),
                    ["ToolbarBg"]           = BrushFrom("#FF2E1418"),
                    ["HeaderBg"]            = BrushFrom("#FF3A1C22"),
                    ["GridBg"]              = BrushFrom("#FF180C0E"),
                    ["GridRowBg"]           = BrushFrom("#FF1E1012"),
                    ["GridAltRowBg"]        = BrushFrom("#FF281418"),
                    ["BorderColor"]         = BrushFrom("#FF5A2030"),
                    ["InputBg"]             = BrushFrom("#FF221014"),
                    ["SelectionBg"]         = BrushFrom("#FF8B1A2A"),
                    ["ButtonBg"]            = BrushFrom("#FF351820"),
                    ["ButtonBorder"]        = BrushFrom("#FF6B2838"),
                    ["ButtonHover"]         = BrushFrom("#FF452028"),
                    ["ButtonPressed"]       = BrushFrom("#FFDC143C"),
                    ["AccentColor"]         = BrushFrom("#FFDC143C"),
                    ["TextPrimary"]         = BrushFrom("#FFF5D0D4"),
                    ["TextSecondary"]       = BrushFrom("#FFD6909A"),
                    ["TextMuted"]           = BrushFrom("#FF9A5060"),
                    ["TextDim"]             = BrushFrom("#FF5A2838"),
                    ["ScrollBg"]            = BrushFrom("#FF221014"),
                    ["ScrollThumb"]         = BrushFrom("#FF7A3040"),
                    ["ScrollThumbHover"]    = BrushFrom("#FF9A4050"),
                    ["GridLineColor"]       = BrushFrom("#FF2E1418"),
                    ["RowHoverBg"]          = BrushFrom("#FF2E1820"),
                    ["SplitterBg"]          = BrushFrom("#FF2E1418"),
                    ["ProgressBg"]          = BrushFrom("#FF351820"),
                },
                "Brown" => new Dictionary<string, object>
                {
                    ["WindowBg"]            = BrushFrom("#FF1E1810"),
                    ["PanelBg"]             = BrushFrom("#FF1A140E"),
                    ["ToolbarBg"]           = BrushFrom("#FF2E2216"),
                    ["HeaderBg"]            = BrushFrom("#FF3A2C1E"),
                    ["GridBg"]              = BrushFrom("#FF1A140E"),
                    ["GridRowBg"]           = BrushFrom("#FF1E1810"),
                    ["GridAltRowBg"]        = BrushFrom("#FF281E14"),
                    ["BorderColor"]         = BrushFrom("#FF5A4228"),
                    ["InputBg"]             = BrushFrom("#FF221A12"),
                    ["SelectionBg"]         = BrushFrom("#FF7A5830"),
                    ["ButtonBg"]            = BrushFrom("#FF352818"),
                    ["ButtonBorder"]        = BrushFrom("#FF6B4E2E"),
                    ["ButtonHover"]         = BrushFrom("#FF453420"),
                    ["ButtonPressed"]       = BrushFrom("#FFC08040"),
                    ["AccentColor"]         = BrushFrom("#FFC08040"),
                    ["TextPrimary"]         = BrushFrom("#FFF0E0CC"),
                    ["TextSecondary"]       = BrushFrom("#FFD0B08A"),
                    ["TextMuted"]           = BrushFrom("#FF907050"),
                    ["TextDim"]             = BrushFrom("#FF584030"),
                    ["ScrollBg"]            = BrushFrom("#FF221A12"),
                    ["ScrollThumb"]         = BrushFrom("#FF7A5A38"),
                    ["ScrollThumbHover"]    = BrushFrom("#FF9A7048"),
                    ["GridLineColor"]       = BrushFrom("#FF2A2014"),
                    ["RowHoverBg"]          = BrushFrom("#FF2E2218"),
                    ["SplitterBg"]          = BrushFrom("#FF2E2216"),
                    ["ProgressBg"]          = BrushFrom("#FF352818"),
                },
                _ => new Dictionary<string, object> // Dark (default)
                {
                    ["WindowBg"]            = BrushFrom("#FF1E1E1E"),
                    ["PanelBg"]             = BrushFrom("#FF181818"),
                    ["ToolbarBg"]           = BrushFrom("#FF2D2D30"),
                    ["HeaderBg"]            = BrushFrom("#FF333337"),
                    ["GridBg"]              = BrushFrom("#FF181818"),
                    ["GridRowBg"]           = BrushFrom("#FF1E1E1E"),
                    ["GridAltRowBg"]        = BrushFrom("#FF252526"),
                    ["BorderColor"]         = BrushFrom("#FF3F3F46"),
                    ["InputBg"]             = BrushFrom("#FF2A2A2E"),
                    ["SelectionBg"]         = BrushFrom("#FF264F78"),
                    ["ButtonBg"]            = BrushFrom("#FF3C3C3C"),
                    ["ButtonBorder"]        = BrushFrom("#FF555555"),
                    ["ButtonHover"]         = BrushFrom("#FF505050"),
                    ["ButtonPressed"]       = BrushFrom("#FF007ACC"),
                    ["AccentColor"]         = BrushFrom("#FF007ACC"),
                    ["TextPrimary"]         = BrushFrom("#FFD4D4D4"),
                    ["TextSecondary"]       = BrushFrom("#FFB0B0B0"),
                    ["TextMuted"]           = BrushFrom("#FF888888"),
                    ["TextDim"]             = BrushFrom("#FF555555"),
                    ["ScrollBg"]            = BrushFrom("#FF2A2A2E"),
                    ["ScrollThumb"]         = BrushFrom("#FF686868"),
                    ["ScrollThumbHover"]    = BrushFrom("#FF888888"),
                    ["GridLineColor"]       = BrushFrom("#FF2A2A2E"),
                    ["RowHoverBg"]          = BrushFrom("#FF2A2D2E"),
                    ["SplitterBg"]          = BrushFrom("#FF2D2D30"),
                    ["ProgressBg"]          = BrushFrom("#FF333337"),
                },
            };
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
