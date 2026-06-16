using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace AudioQualityChecker.Services.Scrobbling
{
    /// <summary>
    /// Scrobbles to a self-hosted <see href="https://github.com/krateng/maloja">Maloja</see> server
    /// via its ListenBrainz-compatible submit endpoint (<c>{server}/apis/listenbrainz/1/submit-listens</c>),
    /// authenticated with the Maloja API key. The JSON payload is identical to
    /// <see cref="ListenBrainzScrobbler"/>; only the endpoint and credentials differ.
    /// </summary>
    public sealed class MalojaScrobbler : IScrobbler
    {
        private readonly HttpClient _http = new();
        private string _serverUrl = "";
        private string _apiKey = "";
        private string _username = "";
        private bool _enabled;
        private bool _disposed;
        private string? _lastError;

        public string? LastError => _lastError;
        public string ServiceName => "Maloja";
        public bool IsEnabled => _enabled;
        public bool IsAuthenticated => !string.IsNullOrEmpty(_serverUrl) && !string.IsNullOrEmpty(_apiKey);
        public string Username => _username;
        public string ProfileUrl => _serverUrl; // the Maloja web UI lives at the server root

        public void Configure(string serverUrl, string apiKey, string username = "", bool enabled = true)
        {
            _serverUrl = NormalizeServer(serverUrl);
            _apiKey = apiKey?.Trim() ?? "";
            _username = username?.Trim() ?? "";
            _enabled = enabled;
        }

        /// <summary>Trims whitespace and any trailing slashes so the endpoint can be appended cleanly.</summary>
        private static string NormalizeServer(string? url)
        {
            var s = url?.Trim() ?? "";
            while (s.EndsWith("/", StringComparison.Ordinal)) s = s[..^1];
            return s;
        }

        private string SubmitUrl => _serverUrl + "/apis/listenbrainz/1/submit-listens";

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
                    if (track.DurationSeconds > 0)
                    {
                        w.WriteStartObject("additional_info");
                        w.WriteNumber("duration_ms", (long)(track.DurationSeconds * 1000));
                        w.WriteEndObject();
                    }
                    w.WriteEndObject();
                    w.WriteEndObject();
                    w.WriteEndArray();
                    w.WriteEndObject();
                }

                using var req = new HttpRequestMessage(HttpMethod.Post, SubmitUrl)
                {
                    Content = new ByteArrayContent(ms.ToArray())
                };
                req.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
                req.Headers.Authorization = new AuthenticationHeaderValue("Token", _apiKey);

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

        /// <summary>Reads a Maloja/ListenBrainz "error" message from a response body, if present.</summary>
        private static string ExtractApiErrorMessage(string body)
        {
            if (string.IsNullOrWhiteSpace(body)) return "";
            try
            {
                using var doc = JsonDocument.Parse(body);
                var root = doc.RootElement;
                if (root.TryGetProperty("error", out var err))
                {
                    if (err.ValueKind == JsonValueKind.String) return " — " + err.GetString();
                    if (err.ValueKind == JsonValueKind.Object && err.TryGetProperty("desc", out var desc) && desc.ValueKind == JsonValueKind.String)
                        return " — " + desc.GetString();
                }
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
