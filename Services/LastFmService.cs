using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace AudioQualityChecker.Services
{
    public class LastFmService : IDisposable
    {
        private const string ApiUrl = "https://ws.audioscrobbler.com/2.0/";

        private readonly HttpClient _http = new();
        private string _apiKey = "";
        private string _apiSecret = "";
        private string _sessionKey = "";
        private bool _enabled;
        private bool _disposed;

        // Track currently scrobbling
        private string? _currentArtist;
        private string? _currentTitle;
        private DateTime _playStartTime;
        private double _trackDurationSeconds;
        private bool _scrobbled; // prevent double-scrobble
        private string _username = "";

        public bool IsEnabled => _enabled && !string.IsNullOrEmpty(_sessionKey);
        public bool HasApiKey => !string.IsNullOrEmpty(_apiKey) && !string.IsNullOrEmpty(_apiSecret);
        public string ApiKey => _apiKey;
        public string ApiSecret => _apiSecret;
        public string SessionKey => _sessionKey;
        public string Username => _username;

        public void Configure(string apiKey, string apiSecret, string sessionKey)
        {
            _apiKey = apiKey.Trim();
            _apiSecret = apiSecret.Trim();
            _sessionKey = sessionKey.Trim();
            _enabled = !string.IsNullOrEmpty(_apiKey) && !string.IsNullOrEmpty(_apiSecret)
                       && !string.IsNullOrEmpty(_sessionKey);
        }

        /// <summary>
        /// Gets a Last.fm auth token for the user to authorize.
        /// Returns (token, authUrl) — user opens authUrl to grant access.
        /// </summary>
        public async Task<(string token, string authUrl)?> GetAuthTokenAsync()
        {
            if (!HasApiKey) return null;

            try
            {
                string sig = GenerateSignature(new SortedDictionary<string, string>
                {
                    ["api_key"] = _apiKey,
                    ["method"] = "auth.getToken"
                });

                string url = $"{ApiUrl}?method=auth.getToken&api_key={_apiKey}&api_sig={sig}&format=json";
                var response = await _http.GetStringAsync(url);

                // Simple JSON parse for token
                int idx = response.IndexOf("\"token\":\"", StringComparison.Ordinal);
                if (idx < 0) return null;
                int start = idx + 9;
                int end = response.IndexOf('"', start);
                string token = response[start..end];

                string authUrl = $"https://www.last.fm/api/auth/?api_key={_apiKey}&token={token}";
                return (token, authUrl);
            }
            catch { return null; }
        }

        /// <summary>
        /// Exchanges an authorized token for a session key.
        /// Call this after user has visited the auth URL.
        /// </summary>
        public async Task<string?> GetSessionKeyAsync(string token)
        {
            if (!HasApiKey) return null;

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

                // Simple JSON parse for session key
                int idx = response.IndexOf("\"key\":\"", StringComparison.Ordinal);
                if (idx < 0) return null;
                int start = idx + 7;
                int end = response.IndexOf('"', start);
                string key = response[start..end];

                // Extract username from response
                int nameIdx = response.IndexOf("\"name\":\"", StringComparison.Ordinal);
                if (nameIdx >= 0)
                {
                    int nameStart = nameIdx + 8;
                    int nameEnd = response.IndexOf('"', nameStart);
                    if (nameEnd > nameStart)
                        _username = response[nameStart..nameEnd];
                }

                _sessionKey = key;
                _enabled = true;
                return key;
            }
            catch { return null; }
        }

        /// <summary>
        /// Call when a new track starts playing.
        /// Sends "now playing" update and prepares for scrobble.
        /// </summary>
        public void TrackStarted(string? artist, string? title, double durationSeconds)
        {
            _currentArtist = artist;
            _currentTitle = title;
            _trackDurationSeconds = durationSeconds;
            _playStartTime = DateTime.UtcNow;
            _scrobbled = false;

            if (IsEnabled && !string.IsNullOrEmpty(artist) && !string.IsNullOrEmpty(title))
            {
                _ = SendNowPlayingAsync(artist, title, (int)durationSeconds);
            }
        }

        /// <summary>
        /// Call periodically during playback. Auto-scrobbles when ≥50% played or ≥4 minutes.
        /// </summary>
        public void UpdatePlayback(double positionSeconds)
        {
            if (_scrobbled || !IsEnabled) return;
            if (string.IsNullOrEmpty(_currentArtist) || string.IsNullOrEmpty(_currentTitle)) return;

            bool shouldScrobble = false;
            if (_trackDurationSeconds > 30) // Last.fm requires track > 30s
            {
                // Scrobble at 50% or 4 minutes, whichever comes first
                double halfDuration = _trackDurationSeconds / 2;
                shouldScrobble = positionSeconds >= Math.Min(halfDuration, 240);
            }

            if (shouldScrobble)
            {
                _scrobbled = true;
                _ = ScrobbleAsync(_currentArtist!, _currentTitle!,
                    new DateTimeOffset(_playStartTime).ToUnixTimeSeconds());
            }
        }

        public void TrackStopped()
        {
            _currentArtist = null;
            _currentTitle = null;
            _scrobbled = false;
        }

        private async Task SendNowPlayingAsync(string artist, string title, int duration)
        {
            try
            {
                var parms = new SortedDictionary<string, string>
                {
                    ["api_key"] = _apiKey,
                    ["artist"] = artist,
                    ["duration"] = duration.ToString(),
                    ["method"] = "track.updateNowPlaying",
                    ["sk"] = _sessionKey,
                    ["track"] = title
                };
                string sig = GenerateSignature(parms);
                parms["api_sig"] = sig;
                parms["format"] = "json";

                await _http.PostAsync(ApiUrl, new FormUrlEncodedContent(parms!));
            }
            catch { }
        }

        private async Task ScrobbleAsync(string artist, string title, long timestamp)
        {
            try
            {
                var parms = new SortedDictionary<string, string>
                {
                    ["api_key"] = _apiKey,
                    ["artist"] = artist,
                    ["method"] = "track.scrobble",
                    ["sk"] = _sessionKey,
                    ["timestamp"] = timestamp.ToString(),
                    ["track"] = title
                };
                string sig = GenerateSignature(parms);
                parms["api_sig"] = sig;
                parms["format"] = "json";

                await _http.PostAsync(ApiUrl, new FormUrlEncodedContent(parms!));
            }
            catch { }
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
        }
    }
}
