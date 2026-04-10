using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
namespace AudioQualityChecker.Services
{
    /// <summary>
    /// SH Labs AI audio detection service. Routes requests through a Cloudflare Worker proxy
    /// that holds the API key. Local + server-side rate limiting (15/day, 100/month per install).
    /// Results are cached locally by file hash to avoid re-scanning.
    /// </summary>
    public static class SHLabsDetectionService
    {
        // ── Proxy endpoint (Cloudflare Worker — holds the real API key) ──
        private const string ProxyBaseUrl = "https://shlabs-proxy.angelmakessoftware.workers.dev";

        // ── HMAC key for request signing (proves request is from AudioAuditor) ──
        // This is NOT the API key — it's a shared secret between app and proxy.
        // The actual value lives in SHLabsSecrets.cs which is gitignored.
        private static readonly byte[] HmacKey = Convert.FromBase64String(SHLabsSecrets.HmacKeyBase64);

        // ── Local rate limits (mirrors server-side enforcement) ──
        private const int DailyLimit = 15;
        private const int MonthlyLimit = 100;

        // SH Labs direct API URL (used when user has their own key)
        private const string SHLabsDirectUrl = "https://shlabs.music/api/v1/detect";

        /// <summary>
        /// Set by the UI layer (ThemeManager) to provide the user's custom SH Labs API key.
        /// When non-empty, the proxy is bypassed entirely.
        /// </summary>
        public static string? CustomApiKey { get; set; }

        private const string InstallIdKey = "InstallId";

        // ── Local cache file paths ──
        private static readonly string CacheDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "AudioAuditor");
        private static readonly string ResultCacheFile = Path.Combine(CacheDir, "shlabs_cache.dat");
        private static readonly string UsageFile = Path.Combine(CacheDir, "shlabs_usage.dat");

        // In-memory caches (loaded from disk on first use)
        private static readonly ConcurrentDictionary<string, SHLabsCachedResult> _resultCache = new();
        private static bool _cacheLoaded;
        private static readonly object _cacheLock = new();

        // Usage tracking
        private static int _dailyUsed;
        private static int _monthlyUsed;
        private static int _usageDay;   // day-of-year when daily counter was set
        private static int _usageMonth; // month when monthly counter was set
        private static int _usageYear;
        private static readonly object _usageLock = new();

        private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(30) };

        // ═══════════════════════════════════════════
        //  Public API
        // ═══════════════════════════════════════════

        /// <summary>
        /// Returns the remaining daily and monthly quota for this installation.
        /// </summary>
        public static (int DailyRemaining, int MonthlyRemaining) GetQuota()
        {
            EnsureUsageLoaded();
            lock (_usageLock)
            {
                RolloverIfNeeded();
                return (Math.Max(0, DailyLimit - _dailyUsed),
                        Math.Max(0, MonthlyLimit - _monthlyUsed));
            }
        }

        /// <summary>
        /// Returns true if there is at least one request available today AND this month.
        /// </summary>
        public static bool HasQuota()
        {
            var (daily, monthly) = GetQuota();
            return daily > 0 && monthly > 0;
        }

        /// <summary>
        /// Check if we have a cached result for the given file.
        /// </summary>
        public static SHLabsCachedResult? GetCachedResult(string filePath)
        {
            EnsureCacheLoaded();
            string hash = ComputeFileHash(filePath);
            return _resultCache.TryGetValue(hash, out var cached) ? cached : null;
        }

        /// <summary>
        /// Returns true if the user has configured their own SH Labs API key.
        /// When set, the proxy is bypassed entirely — no rate limits, no data sent to proxy.
        /// </summary>
        public static bool HasCustomApiKey => !string.IsNullOrWhiteSpace(CustomApiKey);

        /// <summary>
        /// Analyze a file via the SH Labs proxy (or directly if custom API key is set).
        /// Returns null on failure. Automatically caches results and tracks usage.
        /// </summary>
        public static async Task<SHLabsResult?> AnalyzeAsync(string filePath, CancellationToken ct = default)
        {
            EnsureCacheLoaded();

            // Check local cache first (shared between proxy and direct modes)
            string fileHash = ComputeFileHash(filePath);
            if (_resultCache.TryGetValue(fileHash, out var cached))
            {
                return new SHLabsResult
                {
                    Prediction = cached.Prediction,
                    Probability = cached.Probability,
                    Confidence = cached.Confidence,
                    MostLikelyAiType = cached.MostLikelyAiType,
                    FromCache = true
                };
            }

            // If user has their own API key, call SH Labs directly (no proxy, no rate limits)
            if (HasCustomApiKey)
                return await AnalyzeDirectAsync(filePath, fileHash, ct);

            // Otherwise use the proxy with rate limiting
            return await AnalyzeViaProxyAsync(filePath, fileHash, ct);
        }

        /// <summary>
        /// Calls SH Labs API directly with the user's own key. No proxy, no rate limits, no data collection.
        /// </summary>
        private static async Task<SHLabsResult?> AnalyzeDirectAsync(string filePath, string fileHash, CancellationToken ct)
        {
            try
            {
                // SH Labs API requires an audioUrl — we can't upload a file directly.
                // For direct mode, we send the file as multipart with the user's own key.
                // Note: If SH Labs doesn't accept file uploads, the user will need an accessible URL.
                // For now, we try multipart POST which some API versions support.
                using var form = new MultipartFormDataContent();
                var fileBytes = await File.ReadAllBytesAsync(filePath, ct);
                var fileContent = new ByteArrayContent(fileBytes);
                string ext = Path.GetExtension(filePath).ToLowerInvariant();
                string mime = ext switch
                {
                    ".mp3" => "audio/mpeg",
                    ".wav" => "audio/wav",
                    ".flac" => "audio/flac",
                    ".m4a" or ".aac" => "audio/mp4",
                    ".ogg" => "audio/ogg",
                    _ => "application/octet-stream"
                };
                fileContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(mime);
                form.Add(fileContent, "file", Path.GetFileName(filePath));

                using var request = new HttpRequestMessage(HttpMethod.Post, SHLabsDirectUrl);
                request.Headers.Add("X-API-Key", CustomApiKey!.Trim());
                request.Content = form;

                using var response = await _http.SendAsync(request, ct);
                var json = await response.Content.ReadAsStringAsync(ct);

                if (!response.IsSuccessStatusCode)
                    return null;

                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                if (!root.TryGetProperty("result", out var result))
                    return null;

                var shlResult = new SHLabsResult
                {
                    Prediction = result.TryGetProperty("prediction", out var pred) ? pred.GetString() ?? "Unknown" : "Unknown",
                    Probability = result.TryGetProperty("probability_ai_generated", out var prob) ? prob.GetDouble() : 0,
                    Confidence = result.TryGetProperty("confidence_score", out var conf) ? conf.GetDouble() : 0,
                    MostLikelyAiType = result.TryGetProperty("most_likely_ai_type", out var aiType) ? aiType.GetString() ?? "" : "",
                    FromCache = false
                };

                // Cache result locally
                var cacheEntry = new SHLabsCachedResult
                {
                    FileHash = fileHash,
                    Prediction = shlResult.Prediction,
                    Probability = shlResult.Probability,
                    Confidence = shlResult.Confidence,
                    MostLikelyAiType = shlResult.MostLikelyAiType,
                    ScannedUtc = DateTime.UtcNow
                };
                _resultCache[fileHash] = cacheEntry;
                SaveCache();

                return shlResult;
            }
            catch (OperationCanceledException) { throw; }
            catch { return null; }
        }

        /// <summary>
        /// Analyze via the AudioAuditor proxy (rate-limited).
        /// </summary>
        private static async Task<SHLabsResult?> AnalyzeViaProxyAsync(string filePath, string fileHash, CancellationToken ct)
        {
            EnsureUsageLoaded();

            // Check quota
            if (!HasQuota())
                return null;

            // Build signed request
            string installId = GetOrCreateInstallId();
            long timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            string nonce = Guid.NewGuid().ToString("N")[..16];

            // Upload the file as multipart to the proxy
            try
            {
                string signature = ComputeHmac($"{installId}:{timestamp}:{nonce}");

                using var form = new MultipartFormDataContent();
                var fileBytes = await File.ReadAllBytesAsync(filePath, ct);
                var fileContent = new ByteArrayContent(fileBytes);
                string ext = Path.GetExtension(filePath).ToLowerInvariant();
                string mime = ext switch
                {
                    ".mp3" => "audio/mpeg",
                    ".wav" => "audio/wav",
                    ".flac" => "audio/flac",
                    ".m4a" or ".aac" => "audio/mp4",
                    ".ogg" => "audio/ogg",
                    _ => "application/octet-stream"
                };
                fileContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(mime);
                form.Add(fileContent, "file", Path.GetFileName(filePath));
                form.Add(new StringContent(installId), "installId");
                form.Add(new StringContent(timestamp.ToString()), "timestamp");
                form.Add(new StringContent(nonce), "nonce");
                form.Add(new StringContent(signature), "signature");
                form.Add(new StringContent(fileHash), "fileHash");

                using var response = await _http.PostAsync($"{ProxyBaseUrl}/api/detect", form, ct);
                var json = await response.Content.ReadAsStringAsync(ct);

                if (!response.IsSuccessStatusCode)
                {
                    // Check for rate limit from server
                    if ((int)response.StatusCode == 429)
                        return null; // server says rate limited

                    return null;
                }

                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                if (!root.TryGetProperty("result", out var result))
                    return null;

                var shlResult = new SHLabsResult
                {
                    Prediction = result.TryGetProperty("prediction", out var pred) ? pred.GetString() ?? "Unknown" : "Unknown",
                    Probability = result.TryGetProperty("probability_ai_generated", out var prob) ? prob.GetDouble() : 0,
                    Confidence = result.TryGetProperty("confidence_score", out var conf) ? conf.GetDouble() : 0,
                    MostLikelyAiType = result.TryGetProperty("most_likely_ai_type", out var aiType) ? aiType.GetString() ?? "" : "",
                    FromCache = false
                };

                // Increment local usage
                lock (_usageLock)
                {
                    RolloverIfNeeded();
                    _dailyUsed++;
                    _monthlyUsed++;
                    SaveUsage();
                }

                // Cache the result
                var cacheEntry = new SHLabsCachedResult
                {
                    FileHash = fileHash,
                    Prediction = shlResult.Prediction,
                    Probability = shlResult.Probability,
                    Confidence = shlResult.Confidence,
                    MostLikelyAiType = shlResult.MostLikelyAiType,
                    ScannedUtc = DateTime.UtcNow
                };
                _resultCache[fileHash] = cacheEntry;
                SaveCache();

                return shlResult;
            }
            catch (OperationCanceledException) { throw; }
            catch
            {
                return null;
            }
        }

        // ═══════════════════════════════════════════
        //  Installation ID (file-based — cross-platform)
        // ═══════════════════════════════════════════

        public static string GetOrCreateInstallId()
        {
            try
            {
                string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AudioAuditor");
                string idFile = Path.Combine(dir, "install_id.txt");
                if (File.Exists(idFile))
                {
                    string existing = File.ReadAllText(idFile).Trim();
                    if (!string.IsNullOrEmpty(existing) && existing.Length >= 32)
                        return existing;
                }

                // Generate a random GUID-based ID (not predictable from machine info)
                string id = Guid.NewGuid().ToString("N");
                Directory.CreateDirectory(dir);
                File.WriteAllText(idFile, id);
                return id;
            }
            catch
            {
                // Fallback: random ID (not persisted)
                return Guid.NewGuid().ToString("N");
            }
        }

        // ═══════════════════════════════════════════
        //  HMAC Signing
        // ═══════════════════════════════════════════

        private static string ComputeHmac(string message)
        {
            using var hmac = new HMACSHA256(HmacKey);
            var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(message));
            return Convert.ToHexString(hash).ToLowerInvariant();
        }

        // ═══════════════════════════════════════════
        //  File Hashing (SHA-256 of audio content)
        // ═══════════════════════════════════════════

        private static string ComputeFileHash(string filePath)
        {
            using var stream = System.IO.File.OpenRead(filePath);
            var hash = SHA256.HashData(stream);
            return Convert.ToHexString(hash).ToLowerInvariant();
        }

        // ═══════════════════════════════════════════
        //  Usage Tracking (persisted to disk)
        // ═══════════════════════════════════════════

        private static void EnsureUsageLoaded()
        {
            lock (_usageLock)
            {
                if (_usageYear > 0) return; // already loaded
                LoadUsage();
            }
        }

        private static void RolloverIfNeeded()
        {
            var now = DateTime.UtcNow;
            if (now.Year != _usageYear || now.Month != _usageMonth)
            {
                _monthlyUsed = 0;
                _dailyUsed = 0;
                _usageYear = now.Year;
                _usageMonth = now.Month;
                _usageDay = now.DayOfYear;
            }
            else if (now.DayOfYear != _usageDay)
            {
                _dailyUsed = 0;
                _usageDay = now.DayOfYear;
            }
        }

        private static void LoadUsage()
        {
            try
            {
                if (!System.IO.File.Exists(UsageFile))
                {
                    var now = DateTime.UtcNow;
                    _usageYear = now.Year;
                    _usageMonth = now.Month;
                    _usageDay = now.DayOfYear;
                    return;
                }

                foreach (var line in System.IO.File.ReadAllLines(UsageFile))
                {
                    var parts = line.Split('=', 2);
                    if (parts.Length != 2) continue;
                    switch (parts[0])
                    {
                        case "DailyUsed": int.TryParse(parts[1], out _dailyUsed); break;
                        case "MonthlyUsed": int.TryParse(parts[1], out _monthlyUsed); break;
                        case "UsageDay": int.TryParse(parts[1], out _usageDay); break;
                        case "UsageMonth": int.TryParse(parts[1], out _usageMonth); break;
                        case "UsageYear": int.TryParse(parts[1], out _usageYear); break;
                    }
                }
                RolloverIfNeeded();
            }
            catch
            {
                var now = DateTime.UtcNow;
                _usageYear = now.Year;
                _usageMonth = now.Month;
                _usageDay = now.DayOfYear;
            }
        }

        private static void SaveUsage()
        {
            try
            {
                if (!Directory.Exists(CacheDir)) Directory.CreateDirectory(CacheDir);
                var lines = new[]
                {
                    $"DailyUsed={_dailyUsed}",
                    $"MonthlyUsed={_monthlyUsed}",
                    $"UsageDay={_usageDay}",
                    $"UsageMonth={_usageMonth}",
                    $"UsageYear={_usageYear}"
                };
                System.IO.File.WriteAllLines(UsageFile, lines);
            }
            catch { }
        }

        // ═══════════════════════════════════════════
        //  Result Cache (persisted to disk)
        // ═══════════════════════════════════════════

        private static void EnsureCacheLoaded()
        {
            if (_cacheLoaded) return;
            lock (_cacheLock)
            {
                if (_cacheLoaded) return;
                LoadCache();
                _cacheLoaded = true;
            }
        }

        private static void LoadCache()
        {
            try
            {
                if (!System.IO.File.Exists(ResultCacheFile)) return;
                foreach (var line in System.IO.File.ReadAllLines(ResultCacheFile))
                {
                    // Format: hash|prediction|probability|confidence|aiType|utcTicks
                    var parts = line.Split('|');
                    if (parts.Length < 6) continue;
                    var entry = new SHLabsCachedResult
                    {
                        FileHash = parts[0],
                        Prediction = parts[1],
                        Probability = double.TryParse(parts[2], out var p) ? p : 0,
                        Confidence = double.TryParse(parts[3], out var c) ? c : 0,
                        MostLikelyAiType = parts[4],
                        ScannedUtc = long.TryParse(parts[5], out var t) ? new DateTime(t, DateTimeKind.Utc) : DateTime.UtcNow
                    };
                    _resultCache[entry.FileHash] = entry;
                }
            }
            catch { }
        }

        private static void SaveCache()
        {
            try
            {
                if (!Directory.Exists(CacheDir)) Directory.CreateDirectory(CacheDir);
                var lines = _resultCache.Values.Select(e =>
                    $"{e.FileHash}|{e.Prediction}|{e.Probability:F1}|{e.Confidence:F1}|{e.MostLikelyAiType}|{e.ScannedUtc.Ticks}");
                System.IO.File.WriteAllLines(ResultCacheFile, lines);
            }
            catch { }
        }
    }

    // ── Result types ──

    public class SHLabsResult
    {
        public string Prediction { get; set; } = ""; // "Human Made", "Pure AI", "Processed AI"
        public double Probability { get; set; }       // 0–100
        public double Confidence { get; set; }        // 0–100
        public string MostLikelyAiType { get; set; } = "";
        public bool FromCache { get; set; }
    }

    public class SHLabsCachedResult
    {
        public string FileHash { get; set; } = "";
        public string Prediction { get; set; } = "";
        public double Probability { get; set; }
        public double Confidence { get; set; }
        public string MostLikelyAiType { get; set; } = "";
        public DateTime ScannedUtc { get; set; }
    }
}
