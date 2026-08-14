using System;
using System.Collections.Generic;
using AudioQualityChecker.Abstractions;

namespace AudioQualityChecker.Theming
{
    /// <summary>
    /// The built-in playbar gradients, shared by every UI that draws the progress bar.
    ///
    /// Like <see cref="ThemePalettes"/> these lived only in the WPF theme manager; the
    /// Avalonia port had reimplemented 4 of the 12 under different names and dropped the
    /// rest. Colours are <see cref="AppColor"/> so nothing here depends on a UI toolkit.
    /// </summary>
    public static class PlaybarPalettes
    {
        /// <summary>Playbar theme that derives its gradient from the active colour theme.</summary>
        public const string FollowTheme = "Follow Theme";
        public const string DefaultTheme = "Blue Fire";

        /// <summary>Names offered to the user, including the derived "Follow Theme" entry.</summary>
        public static IReadOnlyList<string> Names { get; } = new[]
        {
            FollowTheme,
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
            "Rainbow Bars",
        };

        /// <summary>Gradient used when the playbar follows the active colour theme.</summary>
        public static string ResolveFollowTheme(string? colorTheme) =>
            colorTheme is not null && FollowMap.TryGetValue(colorTheme, out var playbar)
                ? playbar
                : DefaultTheme; // Ocean and anything unrecognised

        /// <summary>The palette for <paramref name="playbarTheme"/>, defaulting to Blue Fire.</summary>
        public static PlaybarPalette Get(string? playbarTheme) =>
            playbarTheme is not null && All.TryGetValue(playbarTheme, out var p) ? p : All[DefaultTheme];

        public static bool IsBuiltIn(string? playbarTheme) =>
            playbarTheme is not null && All.ContainsKey(playbarTheme);

        private static readonly IReadOnlyDictionary<string, string> FollowMap =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Dark"] = "Minimal",
                ["Light"] = "Minimal",
                ["Amethyst"] = "Purple Haze",
                ["Dreamsicle"] = "Sunset Glow",
                ["Goldenrod"] = "Golden Wave",
                ["Emerald"] = "Emerald Wave",
                ["Blurple"] = "Blurple Wave",
                ["Crimson"] = "Crimson Wave",
                ["Brown"] = "Brown Wave",
            };

        public static IReadOnlyDictionary<string, PlaybarPalette> All { get; } =
            new Dictionary<string, PlaybarPalette>(StringComparer.OrdinalIgnoreCase)
            {
                ["Blue Fire"] = new PlaybarPalette(
                    new AppColor(40, 77, 168, 218),
                    new[]
                    {
                        new AppColor(180, 30, 120, 180),
                        new AppColor(220, 77, 168, 218),
                        new AppColor(255, 120, 200, 240),
                    },
                    1.5),
                ["Neon Pulse"] = new PlaybarPalette(
                    new AppColor(40, 0, 255, 128),
                    new[]
                    {
                        new AppColor(180, 0, 180, 80),
                        new AppColor(220, 0, 255, 128),
                        new AppColor(255, 80, 255, 180),
                    },
                    2.5),
                ["Sunset Glow"] = new PlaybarPalette(
                    new AppColor(40, 255, 140, 50),
                    new[]
                    {
                        new AppColor(180, 200, 60, 20),
                        new AppColor(220, 255, 140, 50),
                        new AppColor(255, 255, 200, 100),
                    },
                    1.8),
                ["Purple Haze"] = new PlaybarPalette(
                    new AppColor(40, 160, 80, 220),
                    new[]
                    {
                        new AppColor(180, 100, 30, 160),
                        new AppColor(220, 160, 80, 220),
                        new AppColor(255, 200, 140, 255),
                    },
                    2.0),
                ["Minimal"] = new PlaybarPalette(
                    new AppColor(25, 128, 128, 128),
                    new[]
                    {
                        new AppColor(140, 100, 100, 100),
                        new AppColor(180, 160, 160, 160),
                        new AppColor(200, 200, 200, 200),
                    },
                    1.0),
                ["Golden Wave"] = new PlaybarPalette(
                    new AppColor(40, 212, 160, 23),
                    new[]
                    {
                        new AppColor(180, 160, 120, 10),
                        new AppColor(220, 212, 160, 23),
                        new AppColor(255, 255, 210, 80),
                    },
                    1.6),
                ["Emerald Wave"] = new PlaybarPalette(
                    new AppColor(40, 46, 204, 113),
                    new[]
                    {
                        new AppColor(180, 20, 140, 60),
                        new AppColor(220, 46, 204, 113),
                        new AppColor(255, 100, 240, 160),
                    },
                    2.0),
                ["Blurple Wave"] = new PlaybarPalette(
                    new AppColor(40, 88, 101, 242),
                    new[]
                    {
                        new AppColor(180, 60, 70, 180),
                        new AppColor(220, 88, 101, 242),
                        new AppColor(255, 140, 150, 255),
                    },
                    2.2),
                ["Crimson Wave"] = new PlaybarPalette(
                    new AppColor(40, 220, 20, 60),
                    new[]
                    {
                        new AppColor(180, 160, 10, 30),
                        new AppColor(220, 220, 20, 60),
                        new AppColor(255, 255, 80, 100),
                    },
                    1.8),
                ["Brown Wave"] = new PlaybarPalette(
                    new AppColor(40, 160, 110, 60),
                    new[]
                    {
                        new AppColor(180, 110, 70, 30),
                        new AppColor(220, 160, 110, 60),
                        new AppColor(255, 210, 170, 110),
                    },
                    1.4),
                ["Rainbow Bars"] = new PlaybarPalette(
                    new AppColor(40, 128, 128, 128),
                    new[]
                    {
                        new AppColor(200, 255, 50, 50),
                        new AppColor(200, 50, 255, 50),
                        new AppColor(200, 50, 50, 255),
                    },
                    2.0),
            };
    }
}
