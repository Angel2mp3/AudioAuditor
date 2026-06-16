using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace AudioQualityChecker.Services.Scrobbling
{
    public sealed class ListenBrainzScrobbler : IScrobbler
    {
        private const string ApiUrl = "https://api.listenbrainz.org/1/submit-listens";

        private readonly HttpClient _http = new();
        private string _userToken = "";
        private string _username = "";
        private bool _enabled;
        private bool _disposed;
        private string? _lastError;

        public string? LastError => _lastError;
        public string ServiceName => "ListenBrainz";
        public bool IsEnabled => _enabled;
        public bool IsAuthenticated => !string.IsNullOrEmpty(_userToken);
        public string Username => _username;
        public string ProfileUrl =>
            string.IsNullOrEmpty(_username) ? "https://listenbrainz.org" : $"https://listenbrainz.org/user/{Uri.EscapeDataString(_username)}";

        public void Configure(string userToken, string username = "", bool enabled = true)
        {
            _userToken = userToken?.Trim() ?? "";
            _username = username?.Trim() ?? "";
            _enabled = enabled;
        }

        public async Task TrackStartedAsync(ScrobbleTrack track, CancellationToken ct)
        {
            if (AudioAuditorSettings.OfflineMode || !IsEnabled || !IsAuthenticated) return;
            if (string.IsNullOrEmpty(track.Artist) || string.IsNullOrEmpty(track.Title)) return;
            await SendAsync("playing_now", track, listenedAt: null, ct);
        }

        public Task TrackStoppedAsync(CancellationToken ct) => Task.CompletedTask;

        public async Task ScrobbleAsync(ScrobbleTrack track, DateTimeOffset playedAt, CancellationToken ct)
        {
            if (AudioAuditorSettings.OfflineMode || !IsEnabled || !IsAuthenticated) return;
            if (string.IsNullOrEmpty(track.Artist) || string.IsNullOrEmpty(track.Title)) return;
            await SendAsync("single", track, playedAt.ToUnixTimeSeconds(), ct);
        }

        private async Task SendAsync(string listenType, ScrobbleTrack track, long? listenedAt, CancellationToken ct)
        {
            try
            {
                using var ms = new System.IO.MemoryStream();
                using (var w = new Utf8JsonWriter(ms))
                {
                    w.WriteStartObject();
                    w.WriteString("listen_type", listenType);
                    w.WriteStartArray("payload");
                    w.WriteStartObject();
                    if (listenedAt.HasValue)
                        w.WriteNumber("listened_at", listenedAt.Value);
                    w.WriteStartObject("track_metadata");
                    w.WriteString("artist_name", track.Artist);
                    w.WriteString("track_name", track.Title);
                    if (!string.IsNullOrEmpty(track.Album))
                        w.WriteString("release_name", track.Album);
                    if (track.DurationSeconds > 0 || !string.IsNullOrEmpty(track.MbId))
                    {
                        w.WriteStartObject("additional_info");
                        if (track.DurationSeconds > 0)
                            w.WriteNumber("duration_ms", (long)(track.DurationSeconds * 1000));
                        if (!string.IsNullOrEmpty(track.MbId))
                            w.WriteString("recording_mbid", track.MbId);
                        w.WriteEndObject();
                    }
                    w.WriteEndObject();
                    w.WriteEndObject();
                    w.WriteEndArray();
                    w.WriteEndObject();
                }

                using var req = new HttpRequestMessage(HttpMethod.Post, ApiUrl)
                {
                    Content = new ByteArrayContent(ms.ToArray())
                };
                req.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
                req.Headers.Authorization = new AuthenticationHeaderValue("Token", _userToken);

                using var response = await _http.SendAsync(req, ct);
                if (!response.IsSuccessStatusCode)
                {
                    string body = await response.Content.ReadAsStringAsync(ct);
                    string apiMsg = ExtractApiErrorMessage(body);
                    _lastError = $"{ServiceName} {listenType}: HTTP {(int)response.StatusCode} {response.ReasonPhrase}{apiMsg}";
                    return;
                }
                _lastError = null;
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { _lastError = $"{ServiceName} {listenType}: {ex.Message}"; }
        }

        /// <summary>Reads the ListenBrainz "error" message from a response body, if present.</summary>
        private static string ExtractApiErrorMessage(string body)
        {
            if (string.IsNullOrWhiteSpace(body)) return "";
            try
            {
                using var doc = JsonDocument.Parse(body);
                if (doc.RootElement.TryGetProperty("error", out var err) && err.ValueKind == JsonValueKind.String)
                    return " — " + err.GetString();
            }
            catch { }
            return "";
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
