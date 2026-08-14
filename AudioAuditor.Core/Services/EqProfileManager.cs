using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace AudioQualityChecker.Services
{
    /// <summary>
    /// Built-in and user-saved EQ profiles. Built-ins are read-only;
    /// user profiles are persisted as JSON in %APPDATA%/AudioAuditor/eq-profiles.json.
    /// </summary>
    public static class EqProfileManager
    {
        public sealed record EqProfile(string Name, float[] Gains, bool IsBuiltIn);

        // 10 bands; matches AudioPlayer's 10-band Equalizer
        public static IReadOnlyList<EqProfile> BuiltIn { get; } = new List<EqProfile>
        {
            new("Flat",       new float[10],                                            true),
            new("Bass Boost", new[] { 6f, 5f, 4f, 2f, 0f, 0f, 0f, 0f, 0f, 0f },         true),
            new("Vocal",      new[] {-2f,-1f, 0f, 1f, 3f, 3f, 2f, 1f, 0f,-1f },         true),
            new("Rock",       new[] { 4f, 3f, 1f,-1f,-1f, 1f, 3f, 4f, 4f, 4f },         true),
            new("Pop",        new[] {-1f, 0f, 2f, 4f, 4f, 2f, 0f,-1f,-1f,-1f },         true),
            new("Jazz",       new[] { 3f, 2f, 1f, 2f,-1f,-1f, 0f, 1f, 2f, 3f },         true),
            new("Classical",  new[] { 4f, 3f, 2f, 1f, 0f, 0f,-1f,-1f, 0f, 1f },         true),
            new("Electronic", new[] { 4f, 3f, 1f, 0f,-2f, 2f, 1f, 1f, 3f, 4f },         true),
        };

        private static List<EqProfile> _custom = new();
        public static IReadOnlyList<EqProfile> Custom => _custom;

        private static string SettingsDir =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AudioAuditor");
        private static string ProfilesFile => Path.Combine(SettingsDir, "eq-profiles.json");

        public static IEnumerable<EqProfile> All() => BuiltIn.Concat(_custom);

        public static EqProfile? FindByName(string name) =>
            All().FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));

        /// <summary>
        /// Snapshot the given gains into a custom profile and persist. Replaces an
        /// existing custom profile with the same name. Built-in names cannot be overwritten.
        /// </summary>
        public static EqProfile? SaveCustom(string name, float[] gains)
        {
            if (string.IsNullOrWhiteSpace(name)) return null;
            name = name.Trim();
            if (BuiltIn.Any(b => string.Equals(b.Name, name, StringComparison.OrdinalIgnoreCase)))
                return null;
            if (gains == null || gains.Length != 10) return null;

            var copy = new float[10];
            Array.Copy(gains, copy, 10);

            var existing = _custom.FindIndex(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
            var profile = new EqProfile(name, copy, false);
            if (existing >= 0)
                _custom[existing] = profile;
            else
                _custom.Add(profile);

            Save();
            return profile;
        }

        public static bool DeleteCustom(string name)
        {
            int removed = _custom.RemoveAll(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
            if (removed > 0)
            {
                Save();
                return true;
            }
            return false;
        }

        public static void Load()
        {
            try
            {
                if (!File.Exists(ProfilesFile))
                {
                    _custom = new List<EqProfile>();
                    return;
                }
                string json = File.ReadAllText(ProfilesFile);
                using var doc = JsonDocument.Parse(json);
                var list = new List<EqProfile>();
                foreach (var el in doc.RootElement.EnumerateArray())
                {
                    if (!el.TryGetProperty("name", out var nameEl)) continue;
                    if (!el.TryGetProperty("gains", out var gainsEl)) continue;
                    string name = nameEl.GetString() ?? "";
                    if (string.IsNullOrWhiteSpace(name)) continue;
                    var gains = new float[10];
                    int i = 0;
                    foreach (var g in gainsEl.EnumerateArray())
                    {
                        if (i >= 10) break;
                        gains[i++] = (float)g.GetDouble();
                    }
                    list.Add(new EqProfile(name, gains, false));
                }
                _custom = list;
            }
            catch
            {
                _custom = new List<EqProfile>();
            }
        }

        public static void Save()
        {
            try
            {
                if (!Directory.Exists(SettingsDir))
                    Directory.CreateDirectory(SettingsDir);

                using var ms = new MemoryStream();
                using (var w = new Utf8JsonWriter(ms, new JsonWriterOptions { Indented = true }))
                {
                    w.WriteStartArray();
                    foreach (var p in _custom)
                    {
                        w.WriteStartObject();
                        w.WriteString("name", p.Name);
                        w.WriteStartArray("gains");
                        foreach (var g in p.Gains) w.WriteNumberValue(g);
                        w.WriteEndArray();
                        w.WriteEndObject();
                    }
                    w.WriteEndArray();
                }
                File.WriteAllBytes(ProfilesFile, ms.ToArray());
            }
            catch { }
        }
    }
}
