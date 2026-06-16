using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using AudioQualityChecker.Models;

namespace AudioQualityChecker.Services
{
    /// <summary>
    /// Persists the user's named Now Playing layout profiles to JSON in AppData. Mirrors
    /// <see cref="CustomThemeStore"/>, but PRESERVES insertion order (no alphabetical sort) so the
    /// user can reorder profiles. Save/Delete/Reorder operate by name (case-insensitive).
    /// </summary>
    public static class NpLayoutProfileStore
    {
        private static readonly string SettingsDir =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AudioAuditor");

        private static readonly string ProfilesFile = Path.Combine(SettingsDir, "np-layout-profiles.json");

        public static List<NpLayoutProfile> LoadProfiles()
        {
            try
            {
                if (!File.Exists(ProfilesFile))
                    return new List<NpLayoutProfile>();

                var profiles = JsonSerializer.Deserialize<List<NpLayoutProfile>>(
                    File.ReadAllText(ProfilesFile),
                    NpLayoutProfile.JsonOptions) ?? new List<NpLayoutProfile>();

                // Drop nameless/duplicate entries, keep first occurrence to preserve order.
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var result = new List<NpLayoutProfile>();
                foreach (var p in profiles)
                {
                    if (string.IsNullOrWhiteSpace(p.Name)) continue;
                    if (seen.Add(p.Name)) result.Add(p);
                }
                return result;
            }
            catch
            {
                return new List<NpLayoutProfile>();
            }
        }

        public static NpLayoutProfile? FindProfile(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return null;
            return LoadProfiles().FirstOrDefault(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        }

        public static bool Exists(string name) => FindProfile(name) != null;

        /// <summary>
        /// Adds a new profile (appended to the end) or overwrites an existing one with the same
        /// name in place (preserving its position in the list).
        /// </summary>
        public static void SaveProfile(NpLayoutProfile profile)
        {
            if (profile == null || string.IsNullOrWhiteSpace(profile.Name)) return;

            var profiles = LoadProfiles();
            int idx = profiles.FindIndex(p => p.Name.Equals(profile.Name, StringComparison.OrdinalIgnoreCase));
            if (idx >= 0)
                profiles[idx] = profile;          // overwrite in place
            else
                profiles.Add(profile);            // append new

            SaveAll(profiles);
        }

        public static void DeleteProfile(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return;
            var profiles = LoadProfiles();
            profiles.RemoveAll(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            SaveAll(profiles);
        }

        public static void RenameProfile(string oldName, string newName)
        {
            if (string.IsNullOrWhiteSpace(oldName) || string.IsNullOrWhiteSpace(newName)) return;
            var profiles = LoadProfiles();
            var p = profiles.FirstOrDefault(x => x.Name.Equals(oldName, StringComparison.OrdinalIgnoreCase));
            if (p == null) return;
            // Remove any existing profile that would collide with the new name (other than self).
            profiles.RemoveAll(x => !ReferenceEquals(x, p) && x.Name.Equals(newName, StringComparison.OrdinalIgnoreCase));
            p.Name = newName;
            SaveAll(profiles);
        }

        /// <summary>Moves the named profile up or down one slot in the list.</summary>
        public static void MoveProfile(string name, int direction)
        {
            if (string.IsNullOrWhiteSpace(name) || direction == 0) return;
            var profiles = LoadProfiles();
            int idx = profiles.FindIndex(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (idx < 0) return;
            int target = idx + Math.Sign(direction);
            if (target < 0 || target >= profiles.Count) return;
            (profiles[idx], profiles[target]) = (profiles[target], profiles[idx]);
            SaveAll(profiles);
        }

        /// <summary>Persists an explicit ordering (used after a drag/reorder operation).</summary>
        public static void SaveOrder(IEnumerable<NpLayoutProfile> ordered) => SaveAll(ordered.ToList());

        private static void SaveAll(IReadOnlyCollection<NpLayoutProfile> profiles)
        {
            Directory.CreateDirectory(SettingsDir);
            File.WriteAllText(
                ProfilesFile,
                JsonSerializer.Serialize(profiles, NpLayoutProfile.JsonOptions));
        }
    }
}
