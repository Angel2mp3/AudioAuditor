using System;
using DiscordRPC;
using DiscordRPC.Logging;

namespace AudioQualityChecker.Services
{
    public class DiscordRichPresenceService : IDisposable
    {
        // Discord Application ID for AudioAuditor
        private const string ApplicationId = "1339103939694018570";
        private DiscordRpcClient? _client;
        private bool _enabled;
        private bool _disposed;
        private bool _isReady;
        private System.Timers.Timer? _invokeTimer;

        // Throttle: minimum 15 seconds between presence updates
        private DateTime _lastUpdate = DateTime.MinValue;
        private static readonly TimeSpan UpdateCooldown = TimeSpan.FromSeconds(15);

        // Track current state to avoid duplicate updates
        private string? _lastDetails;
        private string? _lastState;

        public bool IsEnabled => _enabled;
        public bool IsReady => _isReady;

        public void Enable()
        {
            if (_enabled) return;
            try
            {
                _isReady = false;
                _client = new DiscordRpcClient(ApplicationId)
                {
                    Logger = new ConsoleLogger { Level = LogLevel.None }
                };
                _client.OnReady += (_, _) =>
                {
                    _isReady = true;
                    // Set idle presence as soon as we connect
                    SetIdlePresence();
                };
                _client.OnConnectionFailed += (_, _) => _isReady = false;
                _client.OnError += (_, _) => { };
                _client.Initialize();
                _enabled = true;

                // Pump the message queue every 5 seconds
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

        public void Disable()
        {
            _enabled = false;
            _isReady = false;
            _lastDetails = null;
            _lastState = null;
            try
            {
                _invokeTimer?.Stop();
                _invokeTimer?.Dispose();
                _invokeTimer = null;
                _client?.ClearPresence();
                _client?.Dispose();
            }
            catch { }
            _client = null;
        }

        /// <summary>
        /// Show idle status — AudioAuditor icon with "Browsing library" text.
        /// </summary>
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
                    }
                });
                _lastDetails = "Browsing library";
                _lastState = "Idle";
                _lastUpdate = DateTime.UtcNow;
            }
            catch { }
        }

        public void UpdatePresence(string? artist, string? title, string? fileName,
            TimeSpan? duration = null, TimeSpan? position = null, bool isPaused = false)
        {
            if (!_enabled || _client == null || !_isReady) return;

            string details = !string.IsNullOrEmpty(title) ? title : fileName ?? "Unknown";
            string state = isPaused ? "Paused" :
                (!string.IsNullOrEmpty(artist) ? $"by {artist}" : "Unknown Artist");

            // Throttle: skip if we updated too recently AND the data hasn't changed
            var now = DateTime.UtcNow;
            if ((now - _lastUpdate) < UpdateCooldown && details == _lastDetails && state == _lastState)
                return;

            try
            {
                _client.Invoke();

                // Discord requires Details and State to be at least 2 characters
                if (details.Length < 2) details = details + " ";
                if (state.Length < 2) state = state + " ";

                var presence = new RichPresence
                {
                    Details = Truncate(details, 128),
                    State = Truncate(state, 128),
                    Assets = new Assets
                    {
                        LargeImageKey = "audioauditor",
                        LargeImageText = "AudioAuditor"
                    }
                };

                // Add elapsed time when playing (shows as "XX:XX elapsed")
                if (!isPaused && position.HasValue)
                {
                    presence.Timestamps = new Timestamps
                    {
                        Start = DateTime.UtcNow.Subtract(position.Value)
                    };
                }

                _client.SetPresence(presence);
                _lastDetails = details;
                _lastState = state;
                _lastUpdate = now;
            }
            catch { }
        }

        public void ClearPresence()
        {
            if (!_enabled || _client == null) return;
            try
            {
                _client.Invoke();
                // Return to idle instead of fully clearing (keeps the icon visible)
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
