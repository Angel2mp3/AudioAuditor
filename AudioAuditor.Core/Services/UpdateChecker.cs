using System;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace AudioQualityChecker.Services
{
    public static class UpdateChecker
    {
        private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(30) };
        private const string LatestReleaseUrl = "https://api.github.com/repos/Angel2mp3/AudioAuditor/releases/latest";
        private const long MaxUpdateBytes = 512L * 1024 * 1024;

        public static string? LatestVersion { get; private set; }
        public static string? ReleaseUrl { get; private set; }
        public static string? LatestDownloadUrl { get; private set; }
        public static string? LatestSha256Url { get; private set; }

        private const string RepoPrefix = "https://github.com/Angel2mp3/AudioAuditor/";

        /// <summary>
        /// Release assets must come from GitHub over HTTPS. The API response is untrusted input,
        /// so an attacker who can influence it must not be able to point the downloader — or the
        /// hash it is checked against — at a host of their choosing.
        /// </summary>
        internal static bool IsTrustedAssetUrl(string? url) =>
            Uri.TryCreate(url, UriKind.Absolute, out var u)
            && u.Scheme == Uri.UriSchemeHttps
            && (u.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase)
             || u.Host.Equals("objects.githubusercontent.com", StringComparison.OrdinalIgnoreCase)
             || u.Host.Equals("release-assets.githubusercontent.com", StringComparison.OrdinalIgnoreCase));

        internal static bool IsTrustedReleaseAssetUrl(string? url) =>
            Uri.TryCreate(url, UriKind.Absolute, out var uri)
            && uri.Scheme == Uri.UriSchemeHttps
            && uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase)
            && uri.AbsolutePath.StartsWith(
                "/Angel2mp3/AudioAuditor/releases/download/", StringComparison.OrdinalIgnoreCase);

        internal static bool TryParseSha256(string? content, out string hash)
        {
            hash = "";
            if (string.IsNullOrWhiteSpace(content)) return false;

            var parts = content.Split(new[] { ' ', '\t', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length is < 1 or > 2 || parts[0].Length != 64) return false;
            if (parts[0].Any(c => !Uri.IsHexDigit(c))) return false;
            if (parts.Length == 2 && !parts[1].Equals("AudioAuditor.exe", StringComparison.OrdinalIgnoreCase))
                return false;

            hash = parts[0].ToUpperInvariant();
            return true;
        }

        /// <summary>
        /// Reads a response body but abandons it past <paramref name="maxBytes"/>, so a hostile or
        /// broken endpoint cannot stream an unbounded amount into memory.
        /// </summary>
        private static async Task<string?> ReadCappedAsync(HttpContent content, int maxBytes, CancellationToken ct)
        {
            using var stream = await content.ReadAsStreamAsync(ct);
            var buffer = new byte[maxBytes + 1];
            int total = 0;
            while (total < buffer.Length)
            {
                int read = await stream.ReadAsync(buffer.AsMemory(total, buffer.Length - total), ct);
                if (read == 0) break;
                total += read;
            }
            if (total > maxBytes) return null; // over the cap — treat as untrustworthy
            return System.Text.Encoding.UTF8.GetString(buffer, 0, total);
        }

        /// <summary>
        /// Silently checks GitHub for the latest release. Returns true if a newer version is available.
        /// </summary>
        public static async Task<bool> CheckForUpdateAsync(string currentVersion)
        {
            if (AudioAuditorSettings.OfflineMode) return false;
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, LatestReleaseUrl);
                request.Headers.Add("User-Agent", "AudioAuditor-UpdateCheck");

                using var response = await _http.SendAsync(request);
                if (!response.IsSuccessStatusCode) return false;

                // The release JSON is a few KB; anything far past that is not a release listing.
                var json = await ReadCappedAsync(response.Content, 512 * 1024, CancellationToken.None);
                if (json == null) return false;
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                if (!root.TryGetProperty("tag_name", out var tagEl)) return false;
                string tag = tagEl.GetString() ?? "";
                // Strip leading 'v' if present
                string latest = tag.StartsWith("v", StringComparison.OrdinalIgnoreCase) ? tag[1..] : tag;

                LatestVersion = latest;

                // The release page link is opened in the user's browser, so it must actually point
                // at this repository rather than wherever the response says.
                string resolvedUrl = root.TryGetProperty("html_url", out var urlEl) ? urlEl.GetString() ?? "" : "";
                if (!resolvedUrl.StartsWith(RepoPrefix, StringComparison.OrdinalIgnoreCase))
                    resolvedUrl = RepoPrefix + "releases";
                ReleaseUrl = resolvedUrl;

                // Find asset download URLs
                LatestDownloadUrl = null;
                LatestSha256Url = null;
                if (root.TryGetProperty("assets", out var assetsEl) && assetsEl.ValueKind == JsonValueKind.Array)
                {
                    foreach (var asset in assetsEl.EnumerateArray())
                    {
                        string? name = asset.TryGetProperty("name", out var nameProp)
                            ? nameProp.GetString() : null;
                        string? dl = asset.TryGetProperty("browser_download_url", out var dlProp)
                            ? dlProp.GetString() : null;
                        if (name != null && dl != null)
                        {
                            if (name.Equals("AudioAuditor.exe", StringComparison.OrdinalIgnoreCase))
                                LatestDownloadUrl = dl;
                            else if (name.Equals("AudioAuditor.exe.sha256", StringComparison.OrdinalIgnoreCase))
                                LatestSha256Url = dl;
                        }
                    }
                }

                // Both the binary and the hash it is checked against arrive in this same response,
                // so the hash alone proves nothing about origin. Requiring both to be GitHub-hosted
                // is what keeps the pair anchored. If either fails, drop both and fall back to
                // sending the user to the release page instead of downloading anything.
                if (!IsTrustedReleaseAssetUrl(LatestDownloadUrl) ||
                    !IsTrustedReleaseAssetUrl(LatestSha256Url))
                {
                    LatestDownloadUrl = null;
                    LatestSha256Url = null;
                }

                return IsNewer(latest, currentVersion);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Downloads the update .exe to the specified path, reporting progress (0.0–1.0).
        /// </summary>
        public static async Task<bool> DownloadAssetAsync(string destPath, IProgress<double>? progress, CancellationToken ct = default)
        {
            if (!IsTrustedReleaseAssetUrl(LatestDownloadUrl)) return false;
            try
            {
                using var response = await _http.GetAsync(LatestDownloadUrl, HttpCompletionOption.ResponseHeadersRead, ct);
                if (!response.IsSuccessStatusCode) return false;

                // GitHub redirects release assets to its CDN, and redirects are followed
                // automatically — so the host that actually served this has to be checked too,
                // not just the one that was requested.
                if (!IsTrustedAssetUrl(response.RequestMessage?.RequestUri?.ToString())) return false;

                long totalBytes = response.Content.Headers.ContentLength ?? -1;
                if (totalBytes > MaxUpdateBytes) return false;
                using var source = await response.Content.ReadAsStreamAsync(ct);
                using var dest = new FileStream(destPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);

                byte[] buffer = new byte[81920];
                long readBytes = 0;
                int read;
                while ((read = await source.ReadAsync(buffer, ct)) > 0)
                {
                    if (readBytes + read > MaxUpdateBytes)
                        throw new InvalidDataException("Update exceeds the maximum allowed size.");
                    await dest.WriteAsync(buffer.AsMemory(0, read), ct);
                    readBytes += read;
                    if (totalBytes > 0)
                        progress?.Report((double)readBytes / totalBytes);
                }
                progress?.Report(1.0);
                return true;
            }
            catch
            {
                try { File.Delete(destPath); } catch { }
                return false;
            }
        }

        /// <summary>
        /// Fetches the SHA256 hash from the release asset and compares it to the downloaded file.
        /// </summary>
        public static async Task<bool> VerifySha256Async(string filePath, CancellationToken ct = default)
        {
            if (!IsTrustedReleaseAssetUrl(LatestSha256Url) || !File.Exists(filePath)) return false;
            try
            {
                using var response = await _http.GetAsync(LatestSha256Url, ct);
                if (!response.IsSuccessStatusCode) return false;
                if (!IsTrustedAssetUrl(response.RequestMessage?.RequestUri?.ToString())) return false;

                // A ".sha256" file is a single hash line; anything larger is not one.
                string? hashText = await ReadCappedAsync(response.Content, 4096, ct);
                if (string.IsNullOrWhiteSpace(hashText)) return false;

                if (!TryParseSha256(hashText, out string expected)) return false;

                using var sha = SHA256.Create();
                await using var stream = File.OpenRead(filePath);
                byte[] actualBytes = await sha.ComputeHashAsync(stream, ct);
                string actual = Convert.ToHexString(actualBytes);
                return actual.Equals(expected, StringComparison.OrdinalIgnoreCase);
            }
            catch { return false; }
        }

        private static bool IsNewer(string latest, string current)
        {
            if (Version.TryParse(latest, out var vLatest) && Version.TryParse(current, out var vCurrent))
                return vLatest > vCurrent;
            return false;
        }
    }
}
