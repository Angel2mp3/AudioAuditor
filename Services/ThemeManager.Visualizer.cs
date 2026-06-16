using System;
using System.Collections.Generic;
using System.Linq;

namespace AudioQualityChecker.Services
{
    public static partial class ThemeManager
    {
        public static bool VisualizerMode { get; set; }

        public static bool SpectrogramLinearScale { get; set; }
        public static bool SpectrogramDifferenceChannel { get; set; }

        public static bool RainbowVisualizerEnabled { get; set; }

        public static int VisualizerStyle { get; set; }

        public static int VisualizerCycleSpeed { get; set; } = 10;
        public static string VisualizerCycleList { get; set; } = "";

        private static string _currentVisualizerTheme = "";
        public static string CurrentVisualizerTheme => string.IsNullOrEmpty(_currentVisualizerTheme) ? _currentPlaybarTheme : _currentVisualizerTheme;
        public static bool IsVisualizerFollowingPlaybar => string.IsNullOrEmpty(_currentVisualizerTheme);

        public static readonly List<string> AvailableVisualizerThemes = new()
        {
            "Follow Playbar",
            "Blue Fire",
            "Neon Pulse",
            "Sunset Glow",
            "Purple Haze",
            "Minimal",
            "Golden Wave",
            "Emerald Wave",
            "Blurple Wave",
            "Crimson Wave",
            "Brown Wave",
            "Rainbow Bars"
        };

        private static PlaybarColors? _cachedVisualizerColors;
        private static string? _cachedVisualizerThemeName;

        public static IReadOnlyList<string> GetAvailableVisualizerThemeNames()
        {
            return AvailableVisualizerThemes
                .Concat(CustomThemeStore.LoadThemes().Select(t => t.Name))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        public static void SetVisualizerTheme(string theme)
        {
            if (theme != "Follow Playbar"
                && !AvailableVisualizerThemes.Contains(theme)
                && GetThemeDefinition(theme) == null)
            {
                theme = "Follow Playbar";
            }

            _currentVisualizerTheme = theme == "Follow Playbar" ? "" : theme;
            _cachedVisualizerColors = null;
            _cachedVisualizerThemeName = null;
            SavePlayOptions();
        }

        public static PlaybarColors GetVisualizerColors()
        {
            if (IsVisualizerFollowingPlaybar)
                return GetPlaybarColors();

            string effectiveTheme = CurrentVisualizerTheme;
            if (_cachedVisualizerColors != null && _cachedVisualizerThemeName == effectiveTheme)
                return _cachedVisualizerColors;
            _cachedVisualizerThemeName = effectiveTheme;

            var custom = GetThemeDefinition(effectiveTheme);
            if (custom != null)
            {
                _cachedVisualizerColors = ColorsFromThemePalette(custom, useVisualizerColors: true);
                return _cachedVisualizerColors;
            }

            string savedPlaybar = _currentPlaybarTheme;
            _currentPlaybarTheme = effectiveTheme;
            _cachedPlaybarColors = null;
            _cachedPlaybarThemeName = null;
            _cachedVisualizerColors = GetPlaybarColors();
            _currentPlaybarTheme = savedPlaybar;
            _cachedPlaybarColors = null;
            _cachedPlaybarThemeName = null;
            return _cachedVisualizerColors;
        }

        public static bool VisualizerRainbowEnabled =>
            CurrentVisualizerTheme == "Rainbow Bars";
    }
}
