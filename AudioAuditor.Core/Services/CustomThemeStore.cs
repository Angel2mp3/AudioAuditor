using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using AudioQualityChecker.Models;

namespace AudioQualityChecker.Services
{
    public static class CustomThemeStore
    {
        private static readonly string SettingsDir =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AudioAuditor");

        private static readonly string ThemesFile = Path.Combine(SettingsDir, "custom-themes.json");

        public static IReadOnlyList<CustomThemeDefinition> LoadThemes()
        {
            try
            {
                if (!File.Exists(ThemesFile))
                    return Array.Empty<CustomThemeDefinition>();

                var themes = JsonSerializer.Deserialize<List<CustomThemeDefinition>>(
                    File.ReadAllText(ThemesFile),
                    CustomThemeDefinition.JsonOptions) ?? new List<CustomThemeDefinition>();

                return themes
                    .Select(t => t.Sanitize())
                    .Where(t => !t.Name.Equals("Liquid Glass", StringComparison.OrdinalIgnoreCase))
                    .GroupBy(t => t.Name, StringComparer.OrdinalIgnoreCase)
                    .Select(g => g.Last())
                    .OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase)
                    .ToArray();
            }
            catch
            {
                return Array.Empty<CustomThemeDefinition>();
            }
        }

        public static CustomThemeDefinition? FindTheme(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return null;

            return LoadThemes()
                .FirstOrDefault(t => t.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        }

        public static void SaveTheme(CustomThemeDefinition theme)
        {
            var sanitized = theme.Sanitize();
            if (sanitized.Name.Equals("Liquid Glass", StringComparison.OrdinalIgnoreCase))
                sanitized = sanitized with { Name = "Custom Theme" };

            var themes = LoadThemes()
                .Where(t => !t.Name.Equals(sanitized.Name, StringComparison.OrdinalIgnoreCase))
                .Append(sanitized)
                .OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            SaveThemes(themes);
        }

        public static void DeleteTheme(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return;

            var themes = LoadThemes()
                .Where(t => !t.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                .ToList();

            SaveThemes(themes);
        }

        public static CustomThemeDefinition ImportTheme(string filePath)
        {
            var theme = JsonSerializer.Deserialize<CustomThemeDefinition>(
                File.ReadAllText(filePath),
                CustomThemeDefinition.JsonOptions);

            if (theme == null)
                throw new InvalidDataException("The selected file does not contain a valid theme.");

            var sanitized = theme.Sanitize();
            SaveTheme(sanitized);
            return sanitized;
        }

        public static void ExportTheme(CustomThemeDefinition theme, string filePath)
        {
            File.WriteAllText(
                filePath,
                JsonSerializer.Serialize(theme.Sanitize(), CustomThemeDefinition.JsonOptions));
        }

        private static void SaveThemes(IReadOnlyCollection<CustomThemeDefinition> themes)
        {
            Directory.CreateDirectory(SettingsDir);
            File.WriteAllText(
                ThemesFile,
                JsonSerializer.Serialize(themes, CustomThemeDefinition.JsonOptions));
        }
    }
}
