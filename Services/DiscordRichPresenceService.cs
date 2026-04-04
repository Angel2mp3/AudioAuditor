using System;
using System.Collections.Concurrent;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using DiscordRPC;
using DiscordRPC.Logging;

namespace AudioQualityChecker.Services
{
    public class DiscordRichPresenceService : IDisposable
    {
        private DiscordRpcClient? _client;
        private string? _currentClientId;
        private bool _enabled;
        private bool _disposed;
        private bool _isReady;
        private System.Timers.Timer? _invokeTimer;
        private System.Timers.Timer? _reconnectTimer;

        // Throttle: minimum 10 seconds between presence updates (unless state changed)
        private DateTime _lastUpdate = DateTime.MinValue;
        private static readonly TimeSpan UpdateCooldown = TimeSpan.FromSeconds(10);

        // Track current state to avoid duplicate updates
        private string? _lastDetails;
        private string? _lastState;
        private bool _lastPaused;

        // Album art cache: "artist|title" → image URL (from Last.fm)
        private static readonly ConcurrentDictionary<string, string?> _artCache = new();
        private static readonly HttpClient _httpClient = new() { Timeout = TimeSpan.FromSeconds(5) };

        public bool IsEnabled => _enabled;
        public bool IsReady => _isReady;

        public void Enable()
        {
            string clientId = ThemeManager.DiscordRpcClientId;
            if (string.IsNullOrWhiteSpace(clientId))
                return;

            if (_enabled && _currentClientId == clientId)
                return;

            if (_enabled)
                Disable();

            try
            {
                _isReady = false;
                _currentClientId = clientId;
                _client = new DiscordRpcClient(clientId)
                {
                    Logger = new ConsoleLogger { Level = LogLevel.None }
                };
                _client.OnReady += (_, _) =>
                {
                    _isReady = true;
                    StopReconnectTimer();
                    SetIdlePresence();
                };
                _client.OnConnectionFailed += (_, _) =>
                {
                    _isReady = false;
                    StartReconnectTimer();
                };
                _client.OnError += (_, _) => { };
                _client.Initialize();
                _enabled = true;

                _invokeTimer = new System.Timers.Timer(5000);
                _invokeTimer.Elapsed += (_, _) =>
                {
                    try { _client?.Invoke(); } catch { }
                };
                _invokeTimer.AutoReset = true;
                _invokeTimer.Start();
            }
            catch
            {
                _client?.Dispose();
                _client = null;
            }
        }

        private void StartReconnectTimer()
        {
            if (_reconnectTimer != null) return;
            _reconnectTimer = new System.Timers.Timer(30000); // retry every 30s
            _reconnectTimer.Elapsed += (_, _) =>
            {
                if (_isReady || !_enabled) { StopReconnectTimer(); return; }
                try
                {
                    _client?.Dispose();
                    _client = null;
                    string? id = _currentClientId;
                    if (!string.IsNullOrWhiteSpace(id))
                    {
                        _client = new DiscordRpcClient(id)
                        {
                            Logger = new ConsoleLogger { Level = LogLevel.None }
                        };
                        _client.OnReady += (_, _) => { _isReady = true; StopReconnectTimer(); SetIdlePresence(); };
                        _client.OnConnectionFailed += (_, _) => _isReady = false;
                        _client.OnError += (_, _) => { };
                        _client.Initialize();
                    }
                }
                catch { }
            };
            _reconnectTimer.AutoReset = true;
            _reconnectTimer.Start();
        }

        private void StopReconnectTimer()
        {
            _reconnectTimer?.Stop();
            _reconnectTimer?.Dispose();
            _reconnectTimer = null;
        }

        public void Disable()
        {
            _enabled = false;
            _isReady = false;
            _currentClientId = null;
            _lastDetails = null;
            _lastState = null;
            try
            {
                StopReconnectTimer();
                _invokeTimer?.Stop();
                _invokeTimer?.Dispose();
                _invokeTimer = null;
                _client?.ClearPresence();
                _client?.Dispose();
            }
            catch { }
            _client = null;
        }

        public void SetIdlePresence()
        {
            if (!_enabled || _client == null) return;
            try
            {
                _client.Invoke();
                _client.SetPresence(new RichPresence
                {
                    Details = "Browsing library",
                    State = "Idle",
                    Assets = new Assets
                    {
                        LargeImageKey = "audioauditor",
                        LargeImageText = "AudioAuditor"
                    },
                    Buttons = new Button[]
                    {
                        new Button { Label = "AudioAuditor", Url = "https://audioauditor.org/" }
                    }
                });
                _lastDetails = "Browsing library";
                _lastState = "Idle";
                _lastPaused = false;
                _lastUpdate = DateTime.UtcNow;
            }
            catch { }
        }

        public void UpdatePresence(string? artist, string? title, string? fileName,
            TimeSpan? duration = null, TimeSpan? position = null, bool isPaused = false)
        {
            if (!_enabled || _client == null || !_isReady) return;

            string displayMode = ThemeManager.DiscordRpcDisplayMode;
            bool showElapsed = ThemeManager.DiscordRpcShowElapsed;

            string details;
            string state;

            switch (displayMode)
            {
                case "FileName":
                    details = fileName ?? "Unknown";
                    state = isPaused ? "Paused" : "Listening";
                    break;
                default: // TrackDetails
                    details = !string.IsNullOrEmpty(title) ? title : fileName ?? "Unknown";
                    state = isPaused ? "Paused" :
                        (!string.IsNullOrEmpty(artist) ? $"by {artist}" : "Unknown Artist");
                    break;
            }

            // Bypass throttle on play/pause state changes
            bool stateChanged = isPaused != _lastPaused || details != _lastDetails || state != _lastState;
            var now = DateTime.UtcNow;
            if (!stateChanged && (now - _lastUpdate) < UpdateCooldown)
                return;

            try
            {
                _client.Invoke();

                if (details.Length < 2) details = details + " ";
                if (state.Length < 2) state = state + " ";

                var presence = new RichPresence
                {
                    Details = Truncate(details, 128),
                    State = Truncate(state, 128),
                    Assets = new Assets
                    {
                        LargeImageKey = "audioauditor",
                        LargeImageText = "AudioAuditor",
                        SmallImageKey = isPaused ? "pause" : "play",
                        SmallImageText = isPaused ? "Paused" : "Listening"
                    },
                    Buttons = new Button[]
                    {
                        new Button { Label = "AudioAuditor", Url = "https://audioauditor.org/" }
                    }
                };

                // Try to use album art from Last.fm as large image
                if (!string.IsNullOrEmpty(artist) && !string.IsNullOrEmpty(title))
                {
                    string cacheKey = $"{artist}|{title}";
                    if (_artCache.TryGetValue(cacheKey, out string? cachedUrl))
                    {
                        if (!string.IsNullOrEmpty(cachedUrl))
                        {
                            presence.Assets.LargeImageKey = cachedUrl;
                            presence.Assets.LargeImageText = $"{title} - {artist}";
                        }
                    }
                    else
                    {
                        // Fire-and-forget: fetch art in background, will appear on next update
                        _ = FetchAlbumArtAsync(artist, title);
                    }
                }

                // Timestamps: show elapsed and song duration bar
                if (showElapsed && !isPaused && position.HasValue)
                {
                    var start = DateTime.UtcNow.Subtract(position.Value);
                    presence.Timestamps = new Timestamps { Start = start };

                    // Only set Start — Discord shows elapsed time. Setting End causes countdown display.
                }

                _client.SetPresence(presence);
                _lastDetails = details;
                _lastState = state;
                _lastPaused = isPaused;
                _lastUpdate = now;
            }
            catch { }
        }

        /// <summary>
        /// Fetches album art URL from Last.fm track.getInfo API and caches it.
        /// </summary>
        private static async Task FetchAlbumArtAsync(string artist, string title)
        {
            string cacheKey = $"{artist}|{title}";
            if (_artCache.ContainsKey(cacheKey)) return;

            string apiKey = ThemeManager.LastFmApiKey;
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                _artCache[cacheKey] = null;
                return;
            }

            try
            {
                string url = $"https://ws.audioscrobbler.com/2.0/?method=track.getInfo&api_key={Uri.EscapeDataString(apiKey)}&artist={Uri.EscapeDataString(artist)}&track={Uri.EscapeDataString(title)}&format=json";
                string json = await _httpClient.GetStringAsync(url).ConfigureAwait(false);

                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("track", out var track) &&
                    track.TryGetProperty("album", out var album) &&
                    album.TryGetProperty("image", out var images))
                {
                    // Get the largest image (last in array)
                    string? imageUrl = null;
                    foreach (var img in images.EnumerateArray())
                    {
                        if (img.TryGetProperty("#text", out var textEl))
                        {
                            string? txt = textEl.GetString();
                            if (!string.IsNullOrEmpty(txt))
                                imageUrl = txt;
                        }
                    }
                    _artCache[cacheKey] = imageUrl;
                }
                else
                {
                    _artCache[cacheKey] = null;
                }
            }
            catch
            {
                _artCache[cacheKey] = null;
            }
        }

        public void ClearPresence()
        {
            if (!_enabled || _client == null) return;
            try
            {
                _client.Invoke();
                SetIdlePresence();
            }
            catch { }
        }

        private static string Truncate(string s, int max)
            => s.Length <= max ? s : s[..(max - 3)] + "...";

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Disable();
        }
    }
}
