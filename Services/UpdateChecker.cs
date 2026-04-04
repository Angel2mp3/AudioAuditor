using System;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace AudioQualityChecker.Services
{
    public static class UpdateChecker
    {
        private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(10) };
        private const string LatestReleaseUrl = "https://api.github.com/repos/Angel2mp3/AudioAuditor/releases/latest";

        public static string? LatestVersion { get; private set; }
        public static string? ReleaseUrl { get; private set; }

        /// <summary>
        /// Silently checks GitHub for the latest release. Returns true if a newer version is available.
        /// </summary>
        public static async Task<bool> CheckForUpdateAsync(string currentVersion)
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, LatestReleaseUrl);
                request.Headers.Add("User-Agent", "AudioAuditor-UpdateCheck");

                using var response = await _http.SendAsync(request);
                if (!response.IsSuccessStatusCode) return false;

                var json = await response.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                if (!root.TryGetProperty("tag_name", out var tagEl)) return false;
                string tag = tagEl.GetString() ?? "";
                // Strip leading 'v' if present
                string latest = tag.StartsWith("v", StringComparison.OrdinalIgnoreCase) ? tag[1..] : tag;

                LatestVersion = latest;
                ReleaseUrl = root.TryGetProperty("html_url", out var urlEl) ? urlEl.GetString() : null;

                return IsNewer(latest, currentVersion);
            }
            catch
            {
                return false;
            }
        }

        private static bool IsNewer(string latest, string current)
        {
            if (Version.TryParse(latest, out var vLatest) && Version.TryParse(current, out var vCurrent))
                return vLatest > vCurrent;
            return false;
        }
    }
}
