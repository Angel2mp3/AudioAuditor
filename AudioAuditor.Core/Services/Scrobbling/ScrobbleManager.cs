using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AudioQualityChecker.Models;

namespace AudioQualityChecker.Services.Scrobbling
{
    /// <summary>
    /// Orchestrates one or more <see cref="IScrobbler"/> implementations.
    /// Owns the user-configurable threshold logic, blacklist, and pause toggle.
    /// </summary>
    public sealed class ScrobbleManager : IDisposable
    {
        private readonly List<IScrobbler> _scrobblers = new();
        private readonly HashSet<string> _blacklist = new(StringComparer.OrdinalIgnoreCase);
        private readonly object _lock = new();
        private CancellationTokenSource _cts = new();

        // Threshold settings — set by the host app on load and whenever the user changes them.
        // 0 disables that threshold; both can be active (whichever fires first wins).
        public int ScrobbleAtPercent { get; set; } = 50;
        public int ScrobbleAtSeconds { get; set; } = 240;
        public int MinScrobbleTrackSeconds { get; set; } = 30;
        public bool PauseScrobbling { get; set; }

        // Per-track session state
        private ScrobbleTrack? _currentTrack;
        private DateTimeOffset _currentTrackStartedAt;
        private double _maxPositionReached;
        private bool _alreadyScrobbled;
        private bool _suppressedThisPlay;

        public IReadOnlyList<IScrobbler> Scrobblers => _scrobblers;
        public ScrobbleTrack? CurrentTrack => _currentTrack;
        public bool HasCurrentTrack => _currentTrack != null;
        public bool CurrentTrackSuppressed => _suppressedThisPlay;
        public bool CurrentTrackAlreadyScrobbled => _alreadyScrobbled;

        /// <summary>Fires whenever pause / blacklist / authentication state changes.</summary>
        public event EventHandler? StateChanged;

        /// <summary>
        /// Fires (on a background thread) when a now-playing or scrobble submission fails, with a
        /// human-readable message. Lets the host log it and show a status instead of silently
        /// dropping the scrobble. Marshal to the UI thread before touching UI.
        /// </summary>
        public event EventHandler<string>? SubmissionFailed;

        // Awaits a fire-and-forget submission so its failure (a thrown exception or a recorded
        // IScrobbler.LastError) surfaces through SubmissionFailed instead of vanishing.
        private void Observe(IScrobbler scrobbler, Task task, string op)
        {
            _ = task.ContinueWith(t =>
            {
                string? error = t.IsFaulted
                    ? t.Exception?.GetBaseException().Message
                    : scrobbler.LastError;
                if (!string.IsNullOrEmpty(error))
                    SubmissionFailed?.Invoke(this, $"{scrobbler.ServiceName} {op} failed: {error}");
            }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        }

        public void Register(IScrobbler scrobbler)
        {
            if (scrobbler == null) return;
            lock (_lock) _scrobblers.Add(scrobbler);
            StateChanged?.Invoke(this, EventArgs.Empty);
        }

        public void RaiseStateChanged() => StateChanged?.Invoke(this, EventArgs.Empty);

        public bool AnyAuthenticated => _scrobblers.Any(s => s.IsAuthenticated);
        public bool AnyEnabled => _scrobblers.Any(s => s.IsEnabled && s.IsAuthenticated);

        public IReadOnlyCollection<string> Blacklist
        {
            get { lock (_lock) return _blacklist.ToArray(); }
        }

        public void LoadBlacklist(IEnumerable<string> entries)
        {
            lock (_lock)
            {
                _blacklist.Clear();
                foreach (var e in entries)
                {
                    if (!string.IsNullOrWhiteSpace(e))
                        _blacklist.Add(e);
                }
            }
            StateChanged?.Invoke(this, EventArgs.Empty);
        }

        public bool IsBlacklisted(string? artist, string? title)
        {
            string key = MakeKey(artist, title);
            if (string.IsNullOrEmpty(key)) return false;
            lock (_lock) return _blacklist.Contains(key);
        }

        public void OnTrackStarted(AudioFileInfo file, double durationSeconds)
        {
            if (file == null) return;
            var track = new ScrobbleTrack(
                Artist: file.Artist ?? "",
                Title: file.Title ?? "",
                Album: file.Album ?? "",
                DurationSeconds: durationSeconds);

            _currentTrack = track;
            _currentTrackStartedAt = DateTimeOffset.UtcNow;
            _maxPositionReached = 0;
            _alreadyScrobbled = false;
            _suppressedThisPlay = IsBlacklisted(track.Artist, track.Title);
            StateChanged?.Invoke(this, EventArgs.Empty);

            // Now playing notification (best effort, fire-and-forget)
            if (PauseScrobbling || _suppressedThisPlay) return;
            var ct = _cts.Token;
            foreach (var s in _scrobblers.Where(s => s.IsEnabled && s.IsAuthenticated))
            {
                Observe(s, s.TrackStartedAsync(track, ct), "now playing");
            }
        }

        public void OnPositionUpdate(double positionSeconds)
        {
            if (_currentTrack == null || _alreadyScrobbled || _suppressedThisPlay) return;
            if (PauseScrobbling) return;

            var t = _currentTrack;
            if (t.DurationSeconds < MinScrobbleTrackSeconds) return;

            if (positionSeconds > _maxPositionReached)
                _maxPositionReached = positionSeconds;

            bool met = false;
            if (ScrobbleAtPercent > 0 && t.DurationSeconds > 0)
            {
                double percent = _maxPositionReached / t.DurationSeconds * 100.0;
                if (percent >= ScrobbleAtPercent) met = true;
            }
            if (!met && ScrobbleAtSeconds > 0 && _maxPositionReached >= ScrobbleAtSeconds)
                met = true;

            if (!met) return;

            _alreadyScrobbled = true;
            StateChanged?.Invoke(this, EventArgs.Empty);
            var ct = _cts.Token;
            foreach (var s in _scrobblers.Where(s => s.IsEnabled && s.IsAuthenticated))
            {
                Observe(s, s.ScrobbleAsync(t, _currentTrackStartedAt, ct), "scrobble");
            }
        }

        public void OnTrackEnded()
        {
            var ct = _cts.Token;
            foreach (var s in _scrobblers)
            {
                _ = s.TrackStoppedAsync(ct);
            }
            _currentTrack = null;
            _alreadyScrobbled = false;
            _suppressedThisPlay = false;
            _maxPositionReached = 0;
        }

        /// <summary>User-triggered scrobble of the current track regardless of threshold.</summary>
        public void ScrobbleNow()
        {
            if (_currentTrack == null || _alreadyScrobbled) return;
            _alreadyScrobbled = true;
            var t = _currentTrack;
            var ct = _cts.Token;
            foreach (var s in _scrobblers.Where(s => s.IsEnabled && s.IsAuthenticated))
            {
                Observe(s, s.ScrobbleAsync(t, _currentTrackStartedAt, ct), "scrobble");
            }
        }

        /// <summary>Skip scrobbling for just the current play (does not blacklist).</summary>
        public void DontScrobbleCurrent()
        {
            _suppressedThisPlay = true;
            StateChanged?.Invoke(this, EventArgs.Empty);
        }

        public void BlacklistCurrent()
        {
            if (_currentTrack == null) return;
            string key = MakeKey(_currentTrack.Artist, _currentTrack.Title);
            if (string.IsNullOrEmpty(key)) return;
            lock (_lock) _blacklist.Add(key);
            _suppressedThisPlay = true;
            StateChanged?.Invoke(this, EventArgs.Empty);
        }

        public void UnblacklistCurrent()
        {
            if (_currentTrack == null) return;
            string key = MakeKey(_currentTrack.Artist, _currentTrack.Title);
            if (string.IsNullOrEmpty(key)) return;
            lock (_lock) _blacklist.Remove(key);
            _suppressedThisPlay = false;
            StateChanged?.Invoke(this, EventArgs.Empty);
        }

        public bool IsCurrentBlacklisted()
        {
            if (_currentTrack == null) return false;
            return IsBlacklisted(_currentTrack.Artist, _currentTrack.Title);
        }

        private static string MakeKey(string? artist, string? title)
        {
            if (string.IsNullOrWhiteSpace(artist) || string.IsNullOrWhiteSpace(title)) return "";
            return $"{artist.Trim()}|{title.Trim()}".ToLowerInvariant();
        }

        public void Dispose()
        {
            try { _cts.Cancel(); } catch { }
            _cts.Dispose();
            _cts = new CancellationTokenSource();
            foreach (var s in _scrobblers)
            {
                try { s.Dispose(); } catch { }
            }
            _scrobblers.Clear();
        }
    }
}
