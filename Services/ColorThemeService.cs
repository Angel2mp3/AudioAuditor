using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Media;
using AudioQualityChecker.Services;
using MediaColor = System.Windows.Media.Color;

namespace AudioQualityChecker.Services
{
    public sealed class ColorThemeService
    {
        private const int MaxColorCacheEntries = 50;

        private readonly Dictionary<string, AlbumColorExtractor.DominantColors> _colorCache =
            new(StringComparer.OrdinalIgnoreCase);
        private readonly LinkedList<string> _colorCacheLru = new();
        private readonly Dictionary<string, List<MediaColor>> _manualPicks =
            new(StringComparer.OrdinalIgnoreCase);
        private readonly object _lock = new();

        private string CacheFilePath =>
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "AudioAuditor",
                "np_color_cache.json");

        public static string HashPath(string path)
        {
            using var sha = SHA256.Create();
            var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(path.ToLowerInvariant()));
            return Convert.ToHexString(bytes);
        }

        public void StoreForFilePath(string filePath, AlbumColorExtractor.DominantColors colors)
        {
            StoreByKey(HashPath(filePath), colors);
        }

        public bool TryGetByKey(string key, out AlbumColorExtractor.DominantColors colors)
        {
            colors = null!;
            lock (_lock)
            {
                if (!_colorCache.TryGetValue(key, out var cachedColors) || cachedColors == null)
                    return false;

                colors = AlbumColorExtractor.SanitizeDominantColors(cachedColors);
                _colorCacheLru.Remove(key);
                _colorCacheLru.AddLast(key);
                return true;
            }
        }

        public bool ContainsByKey(string key)
        {
            lock (_lock)
                return _colorCache.ContainsKey(key);
        }

        public List<MediaColor>? GetManualPicksForFilePath(string filePath)
        {
            string key = HashPath(filePath);
            lock (_lock)
            {
                return _manualPicks.TryGetValue(key, out var picks)
                    ? picks.Take(3).ToList()
                    : null;
            }
        }

        public void SetManualPicksForFilePath(string filePath, IReadOnlyList<MediaColor> picks)
        {
            string key = HashPath(filePath);
            lock (_lock)
            {
                if (picks.Count == 0)
                    _manualPicks.Remove(key);
                else
                    _manualPicks[key] = picks.Take(3).ToList();
            }
        }

        public void LoadFromDisk(bool includeAutoColors = true)
        {
            try
            {
                var path = CacheFilePath;
                if (!File.Exists(path)) return;
                var json = File.ReadAllText(path);
                var cache = ColorThemeCacheStore.Parse(json);

                if (includeAutoColors)
                {
                    foreach (var entry in cache.Entries)
                        StoreByKey(entry.Key, entry.Colors);
                }

                foreach (var pickEntry in cache.ManualPicks)
                {
                    var colors = pickEntry.Colors
                        .Take(3)
                        .Select(c => MediaColor.FromRgb(c.R, c.G, c.B))
                        .ToList();

                    if (colors.Count > 0)
                    {
                        lock (_lock)
                            _manualPicks[pickEntry.Key] = colors;
                    }
                }
            }
            catch { }
        }

        public void SaveToDisk(bool includeAutoColors = true)
        {
            try
            {
                List<KeyValuePair<string, AlbumColorExtractor.DominantColors>> colorEntries;
                List<KeyValuePair<string, List<MediaColor>>> manualPicks;
                lock (_lock)
                {
                    colorEntries = includeAutoColors ? _colorCache.ToList() : new();
                    manualPicks = _manualPicks
                        .Select(kvp => new KeyValuePair<string, List<MediaColor>>(
                            kvp.Key, kvp.Value.ToList()))
                        .ToList();
                }

                var json = ColorThemeCacheStore.Serialize(
                    colorEntries.Select(kvp => new ColorThemeCacheEntry(kvp.Key, kvp.Value)),
                    manualPicks.Select(kvp => new ColorThemeManualPickEntry(
                        kvp.Key,
                        kvp.Value
                            .Take(3)
                            .Select(c => new AlbumColorExtractor.Color(c.R, c.G, c.B))
                            .ToList())));

                var path = CacheFilePath;
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllText(path, json);
            }
            catch { }
        }

        private void StoreByKey(string key, AlbumColorExtractor.DominantColors colors)
        {
            lock (_lock)
            {
                if (_colorCacheLru.Count >= MaxColorCacheEntries)
                {
                    string? oldest = _colorCacheLru.First?.Value;
                    if (oldest != null)
                    {
                        _colorCache.Remove(oldest);
                        _colorCacheLru.RemoveFirst();
                    }
                }

                _colorCache[key] = AlbumColorExtractor.SanitizeDominantColors(colors);
                _colorCacheLru.Remove(key);
                _colorCacheLru.AddLast(key);
            }
        }
    }
}
