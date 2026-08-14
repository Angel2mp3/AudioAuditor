using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace AudioQualityChecker.Services
{
    /// <summary>A single sheet-music match found on IMSLP.</summary>
    public class SheetMusicResult
    {
        public string Title { get; set; } = "";
        public string PageUrl { get; set; } = "";
    }

    /// <summary>
    /// Looks up sheet music via IMSLP's public MediaWiki API (api.php) — real hits with real
    /// download links, but only for IMSLP's public-domain/classical catalog. There is no reliable
    /// public API covering modern/pop sheet music, so callers should fall back to a browser search
    /// (see MainWindow.SheetMusic.cs) when this returns no results.
    /// </summary>
    public static class SheetMusicLookupService
    {
        private static readonly HttpClient _http = new()
        {
            Timeout = TimeSpan.FromSeconds(15),
            DefaultRequestHeaders = { { "User-Agent", AppVersion.UserAgent("sheet-music-lookup") } }
        };

        public static async Task<IReadOnlyList<SheetMusicResult>> SearchImslpAsync(
            string artist, string title, CancellationToken ct = default)
        {
            // Offline mode promises no network calls; the caller's browser-search fallback is
            // itself a network action, so an empty result is the honest answer here.
            if (AudioAuditorSettings.OfflineMode) return Array.Empty<SheetMusicResult>();

            string query = string.IsNullOrWhiteSpace(artist) ? title : $"{artist} {title}";
            string url = "https://imslp.org/api.php?action=query&list=search&format=json&srlimit=5&srsearch="
                + Uri.EscapeDataString(query);

            try
            {
                using var stream = await _http.GetStreamAsync(url, ct);
                using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
                if (!doc.RootElement.TryGetProperty("query", out var q) ||
                    !q.TryGetProperty("search", out var search))
                    return Array.Empty<SheetMusicResult>();

                var results = new List<SheetMusicResult>();
                foreach (var item in search.EnumerateArray())
                {
                    string pageTitle = item.GetProperty("title").GetString() ?? "";
                    if (string.IsNullOrWhiteSpace(pageTitle)) continue;
                    results.Add(new SheetMusicResult
                    {
                        Title = pageTitle,
                        PageUrl = "https://imslp.org/wiki/" + Uri.EscapeDataString(pageTitle).Replace("%20", "_")
                    });
                }
                return results;
            }
            catch
            {
                // Network/parse failures just mean "no IMSLP hits" — caller falls back to browser search.
                return Array.Empty<SheetMusicResult>();
            }
        }
    }
}
