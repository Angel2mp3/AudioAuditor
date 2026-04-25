using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using AudioQualityChecker.Models;

namespace AudioQualityChecker.Services
{
    public static class FavoritesService
    {
        private static readonly string DataDir =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AudioAuditor");
        private static readonly string FavFile = Path.Combine(DataDir, "favorites.json");

        // path → order (1-based; 0 means not in set)
        private static Dictionary<string, int> _map = new(StringComparer.OrdinalIgnoreCase);
        private static bool _loaded;

        private record FavoriteEntry(string FilePath, int Order);

        // ── Load ──────────────────────────────────────────────────────────────
        private static void EnsureLoaded()
        {
            if (_loaded) return;
            _loaded = true;
            try
            {
                if (!File.Exists(FavFile)) return;
                var list = JsonSerializer.Deserialize<List<FavoriteEntry>>(File.ReadAllText(FavFile));
                if (list == null) return;
                foreach (var e in list)
                    _map[e.FilePath] = e.Order;
            }
            catch { }
        }

        // ── Save ──────────────────────────────────────────────────────────────
        private static void Save()
        {
            try
            {
                Directory.CreateDirectory(DataDir);
                var list = _map.Select(kv => new FavoriteEntry(kv.Key, kv.Value))
                               .OrderBy(e => e.Order)
                               .ToList();
                File.WriteAllText(FavFile, JsonSerializer.Serialize(list,
                    new JsonSerializerOptions { WriteIndented = true }));
            }
            catch { }
        }

        // ── Apply saved favorites to a loaded file list ────────────────────────
        public static void Apply(IEnumerable<AudioFileInfo> files)
        {
            EnsureLoaded();
            foreach (var f in files)
            {
                if (_map.TryGetValue(f.FilePath, out int order))
                {
                    f.IsFavorite = true;
                    f.FavoriteOrder = order;
                }
                else
                {
                    f.IsFavorite = false;
                    f.FavoriteOrder = 0;
                }
            }
        }

        // ── Toggle favorite on/off ─────────────────────────────────────────────
        public static void Toggle(AudioFileInfo file)
        {
            EnsureLoaded();
            if (file.IsFavorite)
            {
                // Remove
                _map.Remove(file.FilePath);
                file.IsFavorite = false;
                file.FavoriteOrder = 0;
                Reorder();
            }
            else
            {
                // Add at the end
                int next = _map.Count > 0 ? _map.Values.Max() + 1 : 1;
                _map[file.FilePath] = next;
                file.IsFavorite = true;
                file.FavoriteOrder = next;
            }
            Save();
        }

        // ── Move up/down in favorite order ────────────────────────────────────
        public static void MoveUp(AudioFileInfo file, IEnumerable<AudioFileInfo> allFiles)
        {
            EnsureLoaded();
            if (!file.IsFavorite) return;
            var favorites = allFiles.Where(f => f.IsFavorite)
                                    .OrderBy(f => f.FavoriteOrder)
                                    .ToList();
            int idx = favorites.IndexOf(file);
            if (idx <= 0) return;
            SwapOrder(favorites[idx - 1], file);
            Save();
        }

        public static void MoveDown(AudioFileInfo file, IEnumerable<AudioFileInfo> allFiles)
        {
            EnsureLoaded();
            if (!file.IsFavorite) return;
            var favorites = allFiles.Where(f => f.IsFavorite)
                                    .OrderBy(f => f.FavoriteOrder)
                                    .ToList();
            int idx = favorites.IndexOf(file);
            if (idx < 0 || idx >= favorites.Count - 1) return;
            SwapOrder(file, favorites[idx + 1]);
            Save();
        }

        private static void SwapOrder(AudioFileInfo a, AudioFileInfo b)
        {
            int tmp = a.FavoriteOrder;
            a.FavoriteOrder = b.FavoriteOrder;
            b.FavoriteOrder = tmp;
            _map[a.FilePath] = a.FavoriteOrder;
            _map[b.FilePath] = b.FavoriteOrder;
        }

        // ── Clear all favorites ────────────────────────────────────────────────
        public static void ClearAll(IEnumerable<AudioFileInfo>? allFiles = null)
        {
            EnsureLoaded();
            _map.Clear();
            if (allFiles != null)
                foreach (var f in allFiles) { f.IsFavorite = false; f.FavoriteOrder = 0; }
            Save();
        }

        public static string FavoritesFilePath => FavFile;

        public static int Count { get { EnsureLoaded(); return _map.Count; } }

        public static long GetFileSizeBytes()
        {
            try { return File.Exists(FavFile) ? new FileInfo(FavFile).Length : 0; }
            catch { return 0; }
        }

        // Re-number to fill gaps after a removal
        private static void Reorder()
        {
            int n = 1;
            foreach (var key in _map.Keys.OrderBy(k => _map[k]).ToList())
                _map[key] = n++;
        }
    }
}
