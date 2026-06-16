using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace AudioQualityChecker.Services.Scrobbling
{
    /// <summary>
    /// Audioscrobbler 2.0 client. Handles Last.fm by default; subclasses can target
    /// Libre.fm by overriding ApiUrl + ServiceName + ProfileUrl.
    /// </summary>
    public class LastFmScrobbler : IScrobbler
    {
        protected virtual string ApiUrl => "https://ws.audioscrobbler.com/2.0/";
        public virtual string ServiceName => "Last.fm";
        public virtual string ProfileUrl =>
            string.IsNullOrEmpty(_username) ? "https://www.last.fm" : $"https://www.last.fm/user/{Uri.EscapeDataString(_username)}";

        private readonly HttpClient _http = new();
        private string _apiKey = "";
        private string _apiSecret = "";
        private string _sessionKey = "";
        private string _username = "";
        private bool _enabled;
        private bool _disposed;
        private string? _lastError;

        public string? LastError => _lastError;
        public bool IsEnabled => _enabled;
        public bool IsAuthenticated =>
            !string.IsNullOrEmpty(_apiKey) && !string.IsNullOrEmpty(_apiSecret) && !string.IsNullOrEmpty(_sessionKey);
        public string Username => _username;
        public string ApiKey => _apiKey;
        public string ApiSecret => _apiSecret;
        public string SessionKey => _sessionKey;

        public void Configure(string apiKey, string apiSecret, string sessionKey, string username = "", bool enabled = true)
        {
            _apiKey = apiKey?.Trim() ?? "";
            _apiSecret = apiSecret?.Trim() ?? "";
            _sessionKey = sessionKey?.Trim() ?? "";
            _username = username?.Trim() ?? "";
            _enabled = enabled;
        }

        public async Task<(string token, string authUrl)?> GetAuthTokenAsync()
        {
            if (AudioAuditorSettings.OfflineMode || string.IsNullOrEmpty(_apiKey) || string.IsNullOrEmpty(_apiSecret))
                return null;

            try
            {
                string sig = GenerateSignature(new SortedDictionary<string, string>
                {
                    ["api_key"] = _apiKey,
                    ["method"] = "auth.getToken"
                });

                string url = $"{ApiUrl}?method=auth.getToken&api_key={_apiKey}&api_sig={sig}&format=json";
                var response = await _http.GetStringAsync(url);

                using var doc = JsonDocument.Parse(response);
                var root = doc.RootElement;
                if (root.TryGetProperty("error", out _))
                {
                    _lastError = $"{ServiceName}: {ExtractApiErrorMessage(root)}";
                    return null;
                }
                if (!root.TryGetProperty("token", out var tokenEl) || tokenEl.ValueKind != JsonValueKind.String)
                {
                    _lastError = $"{ServiceName}: auth.getToken returned no token";
                    return null;
                }

                string token = tokenEl.GetString()!;
                _lastError = null;
                string authUrl = $"https://www.last.fm/api/auth/?api_key={_apiKey}&token={token}";
                return (token, authUrl);
            }
            catch (Exception ex) { _lastError = $"{ServiceName}: {ex.Message}"; return null; }
        }

        public async Task<string?> GetSessionKeyAsync(string token)
        {
            if (AudioAuditorSettings.OfflineMode || string.IsNullOrEmpty(_apiKey) || string.IsNullOrEmpty(_apiSecret))
                return null;

            try
            {
                var parms = new SortedDictionary<string, string>
                {
                    ["api_key"] = _apiKey,
                    ["method"] = "auth.getSession",
                    ["token"] = token
                };
                string sig = GenerateSignature(parms);

                string url = $"{ApiUrl}?method=auth.getSession&api_key={_apiKey}&token={token}&api_sig={sig}&format=json";
                var response = await _http.GetStringAsync(url);

                using var doc = JsonDocument.Parse(response);
                var root = doc.RootElement;
                if (root.TryGetProperty("error", out _))
                {
                    _lastError = $"{ServiceName}: {ExtractApiErrorMessage(root)}";
                    return null;
                }
                if (!root.TryGetProperty("session", out var session) ||
                    !session.TryGetProperty("key", out var keyEl) || keyEl.ValueKind != JsonValueKind.String)
                {
                    _lastError = $"{ServiceName}: auth.getSession returned no session key";
                    return null;
                }

                if (session.TryGetProperty("name", out var nameEl) && nameEl.ValueKind == JsonValueKind.String)
                    _username = nameEl.GetString() ?? _username;

                _sessionKey = keyEl.GetString()!;
                _enabled = true;
                _lastError = null;
                return _sessionKey;
            }
            catch (Exception ex) { _lastError = $"{ServiceName}: {ex.Message}"; return null; }
        }

        /// <summary>Reads the "message" (or numeric "error" code) from a Last.fm error JSON object.</summary>
        private static string ExtractApiErrorMessage(JsonElement root)
        {
            if (root.TryGetProperty("message", out var msg) && msg.ValueKind == JsonValueKind.String)
                return msg.GetString() ?? "API error";
            if (root.TryGetProperty("error", out var code))
                return $"API error {code}";
            return "API error";
        }

        /// <summary>
        /// POSTs an Audioscrobbler request and records LastError on transport, HTTP, or API failure.
        /// Returns true only when the call clearly succeeded.
        /// </summary>
        private async Task<bool> PostSignedAsync(SortedDictionary<string, string> parms, string op, CancellationToken ct)
        {
            try
            {
                using var response = await _http.PostAsync(ApiUrl, new FormUrlEncodedContent(parms!), ct);
                string body = await response.Content.ReadAsStringAsync(ct);
                if (!response.IsSuccessStatusCode)
                {
                    string apiMsg = "";
                    try { using var doc = JsonDocument.Parse(body); apiMsg = " — " + ExtractApiErrorMessage(doc.RootElement); } catch { }
                    _lastError = $"{ServiceName} {op}: HTTP {(int)response.StatusCode} {response.ReasonPhrase}{apiMsg}";
                    return false;
                }
                // A 200 can still carry an Audioscrobbler error object.
                if (!string.IsNullOrEmpty(body) && body.Contains("\"error\"", StringComparison.Ordinal))
                {
                    try
                    {
                        using var doc = JsonDocument.Parse(body);
                        if (doc.RootElement.TryGetProperty("error", out _))
                        {
                            _lastError = $"{ServiceName} {op}: {ExtractApiErrorMessage(doc.RootElement)}";
                            return false;
                        }
                    }
                    catch { /* not JSON — treat 200 as success */ }
                }
                _lastError = null;
                return true;
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) { _lastError = $"{ServiceName} {op}: {ex.Message}"; return false; }
        }

        public async Task TrackStartedAsync(ScrobbleTrack track, CancellationToken ct)
        {
            if (AudioAuditorSettings.OfflineMode || !IsEnabled || !IsAuthenticated) return;
            if (string.IsNullOrEmpty(track.Artist) || string.IsNullOrEmpty(track.Title)) return;

            try
            {
                var parms = new SortedDictionary<string, string>
                {
                    ["api_key"] = _apiKey,
                    ["artist"] = track.Artist,
                    ["duration"] = ((int)track.DurationSeconds).ToString(),
                    ["method"] = "track.updateNowPlaying",
                    ["sk"] = _sessionKey,
                    ["track"] = track.Title
                };
                if (!string.IsNullOrEmpty(track.Album))
                    parms["album"] = track.Album;

                string sig = GenerateSignature(parms);
                parms["api_sig"] = sig;
                parms["format"] = "json";

                await PostSignedAsync(parms, "now playing", ct);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { _lastError = $"{ServiceName} now playing: {ex.Message}"; }
        }

        public Task TrackStoppedAsync(CancellationToken ct) => Task.CompletedTask;

        public async Task ScrobbleAsync(ScrobbleTrack track, DateTimeOffset playedAt, CancellationToken ct)
        {
            if (AudioAuditorSettings.OfflineMode || !IsEnabled || !IsAuthenticated) return;
            if (string.IsNullOrEmpty(track.Artist) || string.IsNullOrEmpty(track.Title)) return;

            try
            {
                var parms = new SortedDictionary<string, string>
                {
                    ["api_key"] = _apiKey,
                    ["artist"] = track.Artist,
                    ["method"] = "track.scrobble",
                    ["sk"] = _sessionKey,
                    ["timestamp"] = playedAt.ToUnixTimeSeconds().ToString(),
                    ["track"] = track.Title
                };
                if (!string.IsNullOrEmpty(track.Album))
                    parms["album"] = track.Album;
                if (track.DurationSeconds > 0)
                    parms["duration"] = ((int)track.DurationSeconds).ToString();

                string sig = GenerateSignature(parms);
                parms["api_sig"] = sig;
                parms["format"] = "json";

                await PostSignedAsync(parms, "scrobble", ct);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { _lastError = $"{ServiceName} scrobble: {ex.Message}"; }
        }

        private string GenerateSignature(SortedDictionary<string, string> parms)
        {
            var sb = new StringBuilder();
            foreach (var kvp in parms)
            {
                sb.Append(kvp.Key);
                sb.Append(kvp.Value);
            }
            sb.Append(_apiSecret);

            byte[] hash = MD5.HashData(Encoding.UTF8.GetBytes(sb.ToString()));
            return Convert.ToHexString(hash).ToLowerInvariant();
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _http.Dispose();
            GC.SuppressFinalize(this);
        }
    }
}
