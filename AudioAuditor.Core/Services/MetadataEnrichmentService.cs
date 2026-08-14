using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AudioQualityChecker.Models;

namespace AudioQualityChecker.Services;

public enum MetadataEnrichmentField
{
    Title,
    Artist,
    Album,
    AlbumArtist,
    Year,
    TrackNumber,
    DiscNumber,
    Genre,
    Composer,
    Comment,
    Lyrics,
    Copyright,
    CoverArt,
    StreamingLink,
    /// <summary>Not a tag — a request to rename the file on disk. Handled outside the tag-write path.</summary>
    FileName
}

/// <summary>Which streaming service's track URL to embed when the Streaming-link field is enabled.</summary>
public enum StreamingLinkPlatform
{
    Deezer,
    Apple,
    Spotify,
    YouTube
}

/// <summary>Result of searching a single file, used for live per-file reporting.</summary>
public enum EnrichmentOutcome
{
    Matched,
    LowConfidence,
    NoMatch,
    Error
}

/// <summary>
/// Live progress for a single file as it finishes searching, so the UI/CLI can show
/// success/failure as each file completes instead of waiting for the whole batch.
/// </summary>
public sealed record EnrichmentProgress(
    int Done,
    int Total,
    string FileName,
    EnrichmentOutcome Outcome,
    string Message);

public sealed class MetadataEnrichmentOptions
{
    public bool MissingOnly { get; set; } = true;
    public bool ReplaceExistingCover { get; set; }
    public bool UseMusicBrainz { get; set; } = true;
    public bool UseCoverArtArchive { get; set; } = true;
    public bool UseITunes { get; set; }
    public bool UseAcoustId { get; set; }
    public string AcoustIdApiKey { get; set; } = "";

    // Keyless sources — on by default. Deezer is strong on covers and soundtracks;
    // TheAudioDB adds genre/soundtrack data.
    public bool UseDeezer { get; set; } = true;
    public bool UseTheAudioDb { get; set; } = true;

    // Key-gated sources. They run only when a token/key resolves (option → env var).
    public bool UseDiscogs { get; set; }
    public string DiscogsToken { get; set; } = "";
    public bool UseFanartTv { get; set; }
    public string FanartTvApiKey { get; set; } = "";

    // Streaming-link lookup. Deezer/Apple links come free from their existing searches; Spotify and
    // YouTube need credentials/keys (option → env var), like Discogs/fanart.
    public StreamingLinkPlatform StreamingLinkPlatform { get; set; } = StreamingLinkPlatform.Deezer;
    public string SpotifyClientId { get; set; } = "";
    public string SpotifyClientSecret { get; set; } = "";
    public string YouTubeApiKey { get; set; } = "";

    /// <summary>How many files to search concurrently. Per-provider rate limits still apply.</summary>
    public int MaxConcurrency { get; set; } = 4;

    public HashSet<MetadataEnrichmentField> EnabledFields { get; set; } = new(Enum.GetValues<MetadataEnrichmentField>());

    public static MetadataEnrichmentOptions CreateDefault() => new();

    public bool IsEnabled(MetadataEnrichmentField field) => EnabledFields.Contains(field);
}

public sealed record MetadataTrackSnapshot(
    string FileName,
    string Title,
    string Artist,
    string Album,
    double DurationSeconds,
    int TrackNumber,
    int Year,
    bool HasAlbumCover);

public sealed class MetadataCandidate
{
    public string Provider { get; set; } = "";
    public string Title { get; set; } = "";
    public string Artist { get; set; } = "";
    public string Album { get; set; } = "";
    public string AlbumArtist { get; set; } = "";
    public int Year { get; set; }
    public int TrackNumber { get; set; }
    public int DiscNumber { get; set; }
    public string Genre { get; set; } = "";
    public string Composer { get; set; } = "";
    public string Comment { get; set; } = "";
    public string Lyrics { get; set; } = "";
    public string Copyright { get; set; } = "";
    public string CoverUrl { get; set; } = "";
    public string ReleaseId { get; set; } = "";
    public string ReleaseGroupId { get; set; } = "";
    public double DurationSeconds { get; set; }

    /// <summary>Public streaming-service URL for this track (Deezer/Apple/Spotify/YouTube), when resolved.</summary>
    public string Link { get; set; } = "";
}

public sealed class MetadataEnrichmentChange
{
    public string FilePath { get; set; } = "";
    public string FileName { get; set; } = "";
    public MetadataEnrichmentField Field { get; set; }
    public string OldValue { get; set; } = "";
    public string NewValue { get; set; } = "";
    public string Provider { get; set; } = "";
    public string Reason { get; set; } = "";
    public double Confidence { get; set; }
    public bool IsSelected { get; set; }
    public string CoverUrl { get; set; } = "";

    /// <summary>Small preview thumbnail bytes for cover-art changes, fetched during preview so the
    /// proposed cover can be vetted before the full-size image is written on apply.</summary>
    public byte[]? CoverThumb { get; set; }
}

public sealed class MetadataEnrichmentPreview
{
    public AudioFileInfo File { get; init; } = new();
    public MetadataCandidate? Candidate { get; init; }
    public double Confidence { get; init; }
    public string Status { get; init; } = "";
    public List<MetadataEnrichmentChange> Changes { get; init; } = new();

    /// <summary>
    /// True when at least one provider threw rather than simply finding nothing. Kept separate from
    /// <see cref="Status"/> so "every provider errored" is distinguishable from "the providers
    /// answered and none of them knew this track" without string-matching the status text.
    /// </summary>
    public bool HadProviderErrors { get; init; }
}

public sealed class MetadataEnrichmentApplySummary
{
    public int FilesChanged { get; set; }
    public int ChangesApplied { get; set; }
    public int FailedFiles { get; set; }
    public List<string> Errors { get; } = new();

    /// <summary>
    /// Paths whose tags actually reached disk. Callers mirror changes onto their in-memory rows, and
    /// need to know which files to mirror — updating the whole selection after a partial failure put
    /// values in the grid that were never written.
    /// </summary>
    public HashSet<string> WrittenPaths { get; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class MetadataEnrichmentService
{
    public const double HighConfidenceThreshold = 0.88;
    public const double ReviewConfidenceThreshold = 0.62;

    /// <summary>Status text for "the providers answered and none of them knew this track".</summary>
    public const string NoMatchStatus = "No match found";

    private static readonly SemaphoreSlim MusicBrainzThrottleLock = new(1, 1);
    private static DateTime _lastMusicBrainzRequestUtc = DateTime.MinValue;

    // Light per-provider rate limiters so parallel file searches stay polite to each API.
    private static readonly RateLimiter DeezerLimiter = new(200);
    private static readonly RateLimiter TheAudioDbLimiter = new(350);
    private static readonly RateLimiter DiscogsLimiter = new(1100);

    private static readonly System.Text.RegularExpressions.Regex SoundtrackRegex = new(
        @"(?ix)\b(soundtrack|ost|o\.s\.t|original\s+score|original\s+motion\s+picture|motion\s+picture|original\s+series|original\s+television|television\s+score|film\s+score|game\s+soundtrack|original\s+game|vgm|original\s+soundtrack)\b",
        System.Text.RegularExpressions.RegexOptions.Compiled);

    private static readonly System.Text.RegularExpressions.Regex ComposerRoleRegex = new(
        @"(?i)(compos|music\s+by|score|written-by|writer|songwriter)",
        System.Text.RegularExpressions.RegexOptions.Compiled);

    private static readonly System.Text.RegularExpressions.Regex DiscogsDisambigRegex = new(
        @"\s*\(\d+\)\s*$", System.Text.RegularExpressions.RegexOptions.Compiled);

    private readonly HttpClient _http;

    public MetadataEnrichmentService(HttpClient? http = null)
    {
        // Only the fallback needs a budget — an injected client brings its own.
        _http = http ?? new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        if (!_http.DefaultRequestHeaders.UserAgent.Any())
            _http.DefaultRequestHeaders.UserAgent.ParseAdd(AppVersion.UserAgent("metadata-enrichment"));
    }

    /// <summary>
    /// Searches every file for online metadata. Results are reported live: <paramref name="onResult"/>
    /// fires once per file as soon as it finishes (so a grid can fill in progressively), and
    /// <paramref name="progress"/> carries the per-file outcome (matched / low-confidence / no-match /
    /// error) instead of only a filename. Files are searched concurrently up to
    /// <see cref="MetadataEnrichmentOptions.MaxConcurrency"/>; per-provider rate limits still apply.
    /// </summary>
    public async Task<IReadOnlyList<MetadataEnrichmentPreview>> PreviewAsync(
        IReadOnlyList<AudioFileInfo> files,
        MetadataEnrichmentOptions options,
        IProgress<EnrichmentProgress>? progress = null,
        IProgress<MetadataEnrichmentPreview>? onResult = null,
        CancellationToken ct = default)
    {
        // Offline mode promises "disable all network calls". Every provider below is an online
        // lookup, so the whole sweep is skipped rather than failing one request at a time.
        if (AudioAuditorSettings.OfflineMode) return Array.Empty<MetadataEnrichmentPreview>();

        var results = new MetadataEnrichmentPreview[files.Count];
        int done = 0;
        using var gate = new SemaphoreSlim(Math.Max(1, options.MaxConcurrency));

        async Task ProcessAsync(int index)
        {
            await gate.WaitAsync(ct);
            try
            {
                ct.ThrowIfCancellationRequested();
                var file = files[index];
                MetadataEnrichmentPreview preview;
                try
                {
                    // Read the file's own tags BEFORE searching: the snapshot built from them is what
                    // ranks the providers' candidates. Deriving it from the AudioFileInfo instead meant
                    // the release year came from the filesystem creation date, so a 1975 album ripped
                    // last year scored against its correct match — and the confidence displayed here
                    // was not the score that picked the candidate.
                    var existing = ReadExistingMetadata(file);
                    var snapshot = existing.ToSnapshot(file);
                    bool soundtrack = IsSoundtrackContext(file, existing.Album, existing.AlbumArtist);

                    var search = await SearchProvidersAsync(file, options, snapshot, soundtrack, ct);
                    var candidate = search.Candidate;
                    if (candidate == null)
                    {
                        preview = new MetadataEnrichmentPreview
                        {
                            File = file,
                            Status = search.Errors.Count > 0
                                ? "All providers failed — " + search.Errors[0]
                                : NoMatchStatus,
                            HadProviderErrors = search.Errors.Count > 0
                        };
                    }
                    else
                    {
                        double score = ScoreCandidate(snapshot, candidate, soundtrack);
                        var changes = BuildChanges(file, candidate, score, options, existing);
                        await AttachCoverThumbsAsync(changes, ct);
                        preview = new MetadataEnrichmentPreview
                        {
                            File = file,
                            Candidate = candidate,
                            Confidence = score,
                            Status = score >= HighConfidenceThreshold
                                ? "High confidence"
                                : score >= ReviewConfidenceThreshold
                                    ? "Needs review"
                                    : "Low confidence",
                            Changes = changes,
                            HadProviderErrors = search.Errors.Count > 0
                        };
                    }
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    preview = new MetadataEnrichmentPreview
                    {
                        File = file,
                        Status = ex.Message,
                        HadProviderErrors = true
                    };
                }

                results[index] = preview;
                onResult?.Report(preview);
                int n = Interlocked.Increment(ref done);
                progress?.Report(new EnrichmentProgress(
                    n, files.Count, file.FileName, OutcomeOf(preview), DescribeOutcome(preview)));
            }
            finally
            {
                gate.Release();
            }
        }

        var tasks = new List<Task>(files.Count);
        for (int i = 0; i < files.Count; i++)
            tasks.Add(ProcessAsync(i));
        await Task.WhenAll(tasks);

        return results.Where(r => r != null).ToList();
    }

    private static EnrichmentOutcome OutcomeOf(MetadataEnrichmentPreview preview)
    {
        if (preview.Candidate == null)
            return preview.HadProviderErrors ? EnrichmentOutcome.Error : EnrichmentOutcome.NoMatch;
        return preview.Confidence >= ReviewConfidenceThreshold
            ? EnrichmentOutcome.Matched
            : EnrichmentOutcome.LowConfidence;
    }

    private static string DescribeOutcome(MetadataEnrichmentPreview preview) => OutcomeOf(preview) switch
    {
        EnrichmentOutcome.Matched => $"Matched ({preview.Candidate!.Provider})",
        EnrichmentOutcome.LowConfidence => $"Low confidence ({preview.Candidate!.Provider})",
        EnrichmentOutcome.NoMatch => "No match found",
        _ => $"Error: {preview.Status}"
    };

    public async Task<MetadataEnrichmentApplySummary> ApplyAsync(
        IEnumerable<MetadataEnrichmentChange> selectedChanges,
        bool createBackups,
        CancellationToken ct = default)
    {
        var summary = new MetadataEnrichmentApplySummary();
        var grouped = selectedChanges
            .Where(c => c.IsSelected)
            .GroupBy(c => c.FilePath, StringComparer.OrdinalIgnoreCase);

        foreach (var group in grouped)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                if (createBackups)
                    FileRenamer.CreateBackup(group.Key);

                using var tagFile = TagLib.File.Create(group.Key);
                int staged = 0;
                foreach (var change in group)
                {
                    await ApplyChangeAsync(tagFile, change, ct);
                    staged++;
                }

                // Count only after Save() commits. Tallying inside the loop meant a failed save (or a
                // cover download that threw partway) reported its changes as applied *and* the file
                // as failed.
                tagFile.Save();
                summary.ChangesApplied += staged;
                summary.FilesChanged++;
                summary.WrittenPaths.Add(group.Key);
            }
            catch (Exception ex)
            {
                summary.FailedFiles++;
                summary.Errors.Add($"{Path.GetFileName(group.Key)}: {ex.Message}");
            }
        }

        return summary;
    }

    /// <summary>
    /// One-click auto-tag: writes only the high-confidence changes from a set of previews
    /// (those at or above <see cref="HighConfidenceThreshold"/>), skipping anything that needs review.
    /// </summary>
    public Task<MetadataEnrichmentApplySummary> AutoApplyHighConfidenceAsync(
        IEnumerable<MetadataEnrichmentPreview> previews,
        bool createBackups,
        CancellationToken ct = default)
    {
        var highConfidence = previews
            .SelectMany(p => p.Changes)
            .Where(c => c.Confidence >= HighConfidenceThreshold)
            .ToList();
        foreach (var change in highConfidence)
            change.IsSelected = true;
        return ApplyAsync(highConfidence, createBackups, ct);
    }

    /// <summary>
    /// Searches every enabled provider and returns the best-scoring candidate, plus one message per
    /// provider that threw. Convenience overload: builds the ranking snapshot from the file's own
    /// tags. Prefer <see cref="SearchProvidersAsync"/> when the caller has already read them.
    /// </summary>
    public async Task<MetadataCandidate?> FindBestCandidateAsync(
        AudioFileInfo file,
        MetadataEnrichmentOptions options,
        CancellationToken ct = default)
    {
        var existing = ReadExistingMetadata(file);
        var result = await SearchProvidersAsync(
            file,
            options,
            existing.ToSnapshot(file),
            IsSoundtrackContext(file, existing.Album, existing.AlbumArtist),
            ct);
        return result.Candidate;
    }

    /// <summary>Outcome of one file's provider sweep: the winner, and why any provider produced nothing.</summary>
    public sealed record ProviderSearchResult(MetadataCandidate? Candidate, IReadOnlyList<string> Errors);

    /// <summary>
    /// Runs every enabled provider against <paramref name="file"/>, ranking results with
    /// <paramref name="snapshot"/>. Each provider is isolated: these APIs return 5xx often enough
    /// (MusicBrainz especially) that letting one throw out of here used to abandon the remaining
    /// providers and report the file as unmatched when four others would have matched it.
    /// </summary>
    public async Task<ProviderSearchResult> SearchProvidersAsync(
        AudioFileInfo file,
        MetadataEnrichmentOptions options,
        MetadataTrackSnapshot snapshot,
        bool soundtrack,
        CancellationToken ct = default)
    {
        // The other half of the offline gate: FindBestCandidateAsync and the Batch Editor's
        // cover fetch reach the providers through here without going via PreviewAsync.
        if (AudioAuditorSettings.OfflineMode)
            return new ProviderSearchResult(null, Array.Empty<string>());

        var candidates = new List<MetadataCandidate>();
        var errors = new List<string>();

        async Task TryProvider(string name, Func<Task<MetadataCandidate?>> search)
        {
            try
            {
                var candidate = await search();
                if (candidate != null) candidates.Add(candidate);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                errors.Add($"{name}: {ex.Message}");
            }
        }

        if (options.UseMusicBrainz)
            await TryProvider("MusicBrainz", () => SearchMusicBrainzAsync(file, options, snapshot, soundtrack, ct));

        if (options.UseDeezer)
            await TryProvider("Deezer", () => SearchDeezerAsync(file, snapshot, ct));

        if (options.UseITunes)
            await TryProvider("Apple/iTunes", () => SearchITunesAsync(file, snapshot, ct));

        if (options.UseTheAudioDb)
            await TryProvider("TheAudioDB", () => SearchTheAudioDbAsync(file, ct));

        string discogsToken = ResolveDiscogsToken(options);
        if (options.UseDiscogs && !string.IsNullOrWhiteSpace(discogsToken))
            await TryProvider("Discogs", () => SearchDiscogsAsync(file, discogsToken, options, soundtrack, ct));

        if (options.UseAcoustId && !string.IsNullOrWhiteSpace(options.AcoustIdApiKey) && File.Exists(file.FilePath))
            await TryProvider("AcoustID", () => SearchAcoustIdAsync(file, options.AcoustIdApiKey, ct));

        if (candidates.Count == 0) return new ProviderSearchResult(null, errors);

        var best = candidates
            .OrderByDescending(c => ScoreCandidate(snapshot, c, soundtrack))
            .First();

        // Borrow a cover/genre/composer from any other candidate when the best is missing one,
        // so a strong metadata match isn't penalised for lacking artwork.
        if (string.IsNullOrWhiteSpace(best.CoverUrl))
            best.CoverUrl = candidates.Select(c => c.CoverUrl).FirstOrDefault(u => !string.IsNullOrWhiteSpace(u)) ?? "";
        if (string.IsNullOrWhiteSpace(best.Genre))
            best.Genre = candidates.Select(c => c.Genre).FirstOrDefault(g => !string.IsNullOrWhiteSpace(g)) ?? "";
        if (string.IsNullOrWhiteSpace(best.Composer))
            best.Composer = candidates.Select(c => c.Composer).FirstOrDefault(c => !string.IsNullOrWhiteSpace(c)) ?? "";

        // High-resolution artwork from fanart.tv (keyed) when we have a MusicBrainz release-group id.
        string fanartKey = ResolveFanartKey(options);
        if (options.UseFanartTv && !string.IsNullOrWhiteSpace(fanartKey) && !string.IsNullOrWhiteSpace(best.ReleaseGroupId))
        {
            var art = await ResolveFanartCoverAsync(best.ReleaseGroupId, fanartKey, ct);
            if (!string.IsNullOrWhiteSpace(art)) best.CoverUrl = art;
        }

        // Soundtracks/OSTs are compilations — the album artist should read "Various Artists"
        // rather than copying one track's performer.
        if (soundtrack && string.IsNullOrWhiteSpace(best.AlbumArtist))
            best.AlbumArtist = "Various Artists";

        // Streaming-service URL for the Comment field, from the user's preferred platform.
        if (options.IsEnabled(MetadataEnrichmentField.StreamingLink))
            best.Link = await ResolveStreamingLinkAsync(file, candidates, snapshot, options, ct);

        return new ProviderSearchResult(best, errors);
    }

    // ─────────────────────────── Streaming link (Comment) ───────────────────────────

    private async Task<string> ResolveStreamingLinkAsync(
        AudioFileInfo file, List<MetadataCandidate> candidates, MetadataTrackSnapshot snapshot,
        MetadataEnrichmentOptions options, CancellationToken ct)
    {
        try
        {
            switch (options.StreamingLinkPlatform)
            {
                case StreamingLinkPlatform.Deezer:
                {
                    string link = FirstLink(candidates, "Deezer");
                    if (!string.IsNullOrWhiteSpace(link)) return link;
                    return (await SearchDeezerAsync(file, snapshot, ct))?.Link ?? "";
                }
                case StreamingLinkPlatform.Apple:
                {
                    string link = FirstLink(candidates, "Apple/iTunes");
                    if (!string.IsNullOrWhiteSpace(link)) return link;
                    return (await SearchITunesAsync(file, snapshot, ct))?.Link ?? "";
                }
                case StreamingLinkPlatform.Spotify:
                    return await SearchSpotifyLinkAsync(file, snapshot, options, ct);
                case StreamingLinkPlatform.YouTube:
                    return await SearchYouTubeLinkAsync(file, options, ct);
                default:
                    return "";
            }
        }
        catch
        {
            return "";
        }
    }

    private static string FirstLink(IEnumerable<MetadataCandidate> candidates, string provider)
        => candidates.FirstOrDefault(c => c.Provider == provider && !string.IsNullOrWhiteSpace(c.Link))?.Link ?? "";

    private static readonly SemaphoreSlim SpotifyTokenLock = new(1, 1);
    private static string _spotifyToken = "";
    private static DateTime _spotifyTokenExpiresUtc = DateTime.MinValue;

    private async Task<string> ResolveSpotifyTokenAsync(string id, string secret, CancellationToken ct)
    {
        await SpotifyTokenLock.WaitAsync(ct);
        try
        {
            if (!string.IsNullOrEmpty(_spotifyToken) && DateTime.UtcNow < _spotifyTokenExpiresUtc)
                return _spotifyToken;

            using var req = new HttpRequestMessage(HttpMethod.Post, "https://accounts.spotify.com/api/token");
            string basic = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes($"{id}:{secret}"));
            req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", basic);
            req.Content = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("grant_type", "client_credentials")
            });
            using var resp = await _http.SendAsync(req, ct);
            if (!resp.IsSuccessStatusCode) return "";
            using var stream = await resp.Content.ReadAsStreamAsync(ct);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
            _spotifyToken = GetString(doc.RootElement, "access_token");
            int expires = GetInt(doc.RootElement, "expires_in");
            _spotifyTokenExpiresUtc = DateTime.UtcNow.AddSeconds(Math.Max(60, expires - 30));
            return _spotifyToken;
        }
        catch
        {
            return "";
        }
        finally
        {
            SpotifyTokenLock.Release();
        }
    }

    private async Task<string> SearchSpotifyLinkAsync(
        AudioFileInfo file, MetadataTrackSnapshot snapshot, MetadataEnrichmentOptions options, CancellationToken ct)
    {
        string id = FirstNonEmpty(options.SpotifyClientId, Environment.GetEnvironmentVariable("SPOTIFY_CLIENT_ID"));
        string secret = FirstNonEmpty(options.SpotifyClientSecret, Environment.GetEnvironmentVariable("SPOTIFY_CLIENT_SECRET"));
        if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(secret)) return "";

        string token = await ResolveSpotifyTokenAsync(id, secret, ct);
        if (string.IsNullOrWhiteSpace(token)) return "";

        var (title, artist, _) = ResolveQueryFields(file);
        if (string.IsNullOrWhiteSpace(title)) return "";
        string q = string.IsNullOrWhiteSpace(artist) ? $"track:{title}" : $"track:{title} artist:{artist}";
        string url = "https://api.spotify.com/v1/search?type=track&limit=5&q=" + Uri.EscapeDataString(q);

        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        using var resp = await _http.SendAsync(req, ct);
        if (!resp.IsSuccessStatusCode) return "";
        using var stream = await resp.Content.ReadAsStreamAsync(ct);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
        if (!doc.RootElement.TryGetProperty("tracks", out var tracks)
            || !tracks.TryGetProperty("items", out var items)
            || items.ValueKind != JsonValueKind.Array || items.GetArrayLength() == 0)
            return "";

        string bestUrl = "";
        double bestScore = -1;
        foreach (var item in items.EnumerateArray())
        {
            string itemArtist = "";
            if (item.TryGetProperty("artists", out var arts) && arts.ValueKind == JsonValueKind.Array && arts.GetArrayLength() > 0)
                itemArtist = GetString(arts.EnumerateArray().First(), "name");
            var candidate = new MetadataCandidate { Title = GetString(item, "name"), Artist = itemArtist };
            double score = ScoreCandidate(snapshot, candidate);
            if (score > bestScore && item.TryGetProperty("external_urls", out var ext))
            {
                bestScore = score;
                bestUrl = GetString(ext, "spotify");
            }
        }

        return bestUrl;
    }

    private async Task<string> SearchYouTubeLinkAsync(AudioFileInfo file, MetadataEnrichmentOptions options, CancellationToken ct)
    {
        string key = FirstNonEmpty(options.YouTubeApiKey, Environment.GetEnvironmentVariable("YOUTUBE_API_KEY"));
        if (string.IsNullOrWhiteSpace(key)) return "";

        var (title, artist, _) = ResolveQueryFields(file);
        if (string.IsNullOrWhiteSpace(title)) return "";
        string term = $"{artist} {title}".Trim();
        string url = "https://www.googleapis.com/youtube/v3/search?part=snippet&type=video&maxResults=1&q="
            + Uri.EscapeDataString(term) + "&key=" + Uri.EscapeDataString(key);

        using var stream = await _http.GetStreamAsync(url, ct);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
        if (!doc.RootElement.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array || items.GetArrayLength() == 0)
            return "";

        var first = items.EnumerateArray().First();
        if (first.TryGetProperty("id", out var idEl))
        {
            string videoId = GetString(idEl, "videoId");
            if (!string.IsNullOrWhiteSpace(videoId)) return "https://www.youtube.com/watch?v=" + videoId;
        }

        return "";
    }

    public static List<MetadataEnrichmentChange> BuildChanges(
        AudioFileInfo file,
        MetadataCandidate candidate,
        double confidence,
        MetadataEnrichmentOptions options)
    {
        return BuildChanges(file, candidate, confidence, options, ExistingMetadataValues.FromFile(file));
    }

    private static List<MetadataEnrichmentChange> BuildChanges(
        AudioFileInfo file,
        MetadataCandidate candidate,
        double confidence,
        MetadataEnrichmentOptions options,
        ExistingMetadataValues existing)
    {
        var changes = new List<MetadataEnrichmentChange>();
        AddTextChange(changes, file, MetadataEnrichmentField.Title, existing.Title, candidate.Title, candidate, confidence, options);
        AddTextChange(changes, file, MetadataEnrichmentField.Artist, existing.Artist, candidate.Artist, candidate, confidence, options);
        AddTextChange(changes, file, MetadataEnrichmentField.Album, existing.Album, candidate.Album, candidate, confidence, options);
        AddTextChange(changes, file, MetadataEnrichmentField.AlbumArtist, existing.AlbumArtist, candidate.AlbumArtist, candidate, confidence, options);
        AddNumberChange(changes, file, MetadataEnrichmentField.Year, existing.Year, candidate.Year, candidate, confidence, options);
        AddNumberChange(changes, file, MetadataEnrichmentField.TrackNumber, existing.TrackNumber, candidate.TrackNumber, candidate, confidence, options);
        AddNumberChange(changes, file, MetadataEnrichmentField.DiscNumber, existing.DiscNumber, candidate.DiscNumber, candidate, confidence, options);
        AddTextChange(changes, file, MetadataEnrichmentField.Genre, existing.Genre, candidate.Genre, candidate, confidence, options);
        AddTextChange(changes, file, MetadataEnrichmentField.Composer, existing.Composer, candidate.Composer, candidate, confidence, options);
        AddTextChange(changes, file, MetadataEnrichmentField.Comment, existing.Comment, candidate.Comment, candidate, confidence, options);
        AddTextChange(changes, file, MetadataEnrichmentField.Lyrics, existing.Lyrics, candidate.Lyrics, candidate, confidence, options);
        AddTextChange(changes, file, MetadataEnrichmentField.Copyright, existing.Copyright, candidate.Copyright, candidate, confidence, options);

        if (options.IsEnabled(MetadataEnrichmentField.CoverArt)
            && !string.IsNullOrWhiteSpace(candidate.CoverUrl)
            && (!existing.HasAlbumCover || options.ReplaceExistingCover))
        {
            var coverChange = CreateChange(
                file,
                MetadataEnrichmentField.CoverArt,
                existing.HasAlbumCover ? "Existing cover" : "No cover",
                candidate.CoverUrl,
                candidate,
                confidence,
                existing.HasAlbumCover ? "Replace selected existing cover" : "Add missing front cover");
            coverChange.CoverUrl = candidate.CoverUrl;
            changes.Add(coverChange);
        }

        if (options.IsEnabled(MetadataEnrichmentField.StreamingLink)
            && !string.IsNullOrWhiteSpace(candidate.Link)
            && !existing.Comment.Contains(candidate.Link, StringComparison.OrdinalIgnoreCase))
        {
            changes.Add(CreateChange(
                file,
                MetadataEnrichmentField.StreamingLink,
                existing.Comment,
                candidate.Link,
                candidate,
                confidence,
                $"{candidate.Provider} link → Comment"));
        }

        return changes;
    }

    public static double ScoreCandidate(MetadataTrackSnapshot track, MetadataCandidate candidate, bool soundtrackContext = false)
    {
        double score = 0;
        double weight = 0;
        int terms = 0;

        // A term counts only when BOTH sides have something to compare — but once it counts, a score
        // of 0 counts too. Dropping zero-valued terms from the weight (the old behaviour) meant a
        // wrong artist or a two-minute duration gap was ignored instead of penalised, so a candidate
        // that merely shared a title could reach 1.00 and auto-apply.
        AddTerm(ref score, ref weight, ref terms,
            Present(track.Title) && Present(candidate.Title), Similarity(track.Title, candidate.Title), 0.32);
        AddTerm(ref score, ref weight, ref terms,
            Present(track.Artist) && Present(candidate.Artist), Similarity(track.Artist, candidate.Artist), 0.28);
        AddTerm(ref score, ref weight, ref terms,
            Present(track.Album) && Present(candidate.Album), Similarity(track.Album, candidate.Album), 0.16);

        double durationDelta = Math.Abs(track.DurationSeconds - candidate.DurationSeconds);
        AddTerm(ref score, ref weight, ref terms,
            track.DurationSeconds > 0 && candidate.DurationSeconds > 0,
            durationDelta <= 2 ? 1 : durationDelta <= 8 ? 0.75 : durationDelta <= 20 ? 0.35 : 0, 0.12);

        AddTerm(ref score, ref weight, ref terms,
            track.TrackNumber > 0 && candidate.TrackNumber > 0,
            track.TrackNumber == candidate.TrackNumber ? 1 : 0.35, 0.06);

        AddTerm(ref score, ref weight, ref terms,
            track.Year > 0 && candidate.Year > 0,
            Math.Abs(track.Year - candidate.Year) <= 1 ? 1 : 0.4, 0.06);

        if (weight <= 0) return 0;
        double result = Math.Clamp(score / weight, 0, 1);

        // Soundtrack/score tie-break: nudge candidates that look like soundtracks when the file's
        // context (album/folder/"Various Artists") implies one, so an OST match wins over a same-
        // titled pop single.
        if (soundtrackContext && LooksLikeSoundtrack(candidate))
            result = Math.Clamp(result + 0.03, 0, 1);

        // One matching field is a coincidence, not a match. A lone term can still rank candidates
        // against each other and show up for review — it just can't clear the auto-apply bar.
        if (terms < 2)
            result = Math.Min(result, HighConfidenceThreshold - 0.01);

        return result;
    }

    private static bool Present(string? value) => !string.IsNullOrWhiteSpace(value);

    private static bool LooksLikeSoundtrack(MetadataCandidate candidate)
        => SoundtrackRegex.IsMatch($"{candidate.Genre} {candidate.Album}");

    private async Task<MetadataCandidate?> SearchMusicBrainzAsync(
        AudioFileInfo file,
        MetadataEnrichmentOptions options,
        MetadataTrackSnapshot snapshot,
        bool soundtrack,
        CancellationToken ct)
    {
        var (title, artist, _) = ResolveQueryFields(file);
        string query = string.IsNullOrWhiteSpace(artist)
            ? $"recording:\"{title}\""
            : $"recording:\"{title}\" AND artist:\"{artist}\"";
        string url = "https://musicbrainz.org/ws/2/recording/?query="
            + Uri.EscapeDataString(query)
            + "&fmt=json&limit=5&inc=artists+releases+release-groups+media";

        await ThrottleMusicBrainzAsync(ct);
        using var stream = await _http.GetStreamAsync(url, ct);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
        if (!doc.RootElement.TryGetProperty("recordings", out var recordings) || recordings.GetArrayLength() == 0)
            return null;

        MetadataCandidate? best = null;
        double bestScore = -1;
        foreach (var recording in recordings.EnumerateArray())
        {
            var candidate = CandidateFromMusicBrainz(recording, soundtrack);
            if (candidate == null) continue;
            if (options.UseCoverArtArchive)
                candidate.CoverUrl = await ResolveCoverArtUrlAsync(candidate, ct);

            double score = ScoreCandidate(snapshot, candidate, soundtrack);
            if (score > bestScore)
            {
                bestScore = score;
                best = candidate;
            }
        }

        return best;
    }

    private async Task<MetadataCandidate?> SearchITunesAsync(
        AudioFileInfo file, MetadataTrackSnapshot snapshot, CancellationToken ct)
    {
        var (title, artist, _) = ResolveQueryFields(file);
        string term = Uri.EscapeDataString($"{artist} {title}".Trim());
        string url = $"https://itunes.apple.com/search?term={term}&entity=song&limit=8";
        using var stream = await _http.GetStreamAsync(url, ct);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
        if (!doc.RootElement.TryGetProperty("results", out var results) || results.GetArrayLength() == 0)
            return null;

        MetadataCandidate? best = null;
        double bestScore = -1;
        foreach (var result in results.EnumerateArray())
        {
            var candidate = new MetadataCandidate
            {
                Provider = "Apple/iTunes",
                Link = GetString(result, "trackViewUrl"),
                Title = GetString(result, "trackName"),
                Artist = GetString(result, "artistName"),
                Album = GetString(result, "collectionName"),
                Genre = GetString(result, "primaryGenreName"),
                TrackNumber = GetInt(result, "trackNumber"),
                DiscNumber = GetInt(result, "discNumber"),
                Year = ParseYear(GetString(result, "releaseDate")),
                DurationSeconds = GetInt(result, "trackTimeMillis") / 1000d,
                CoverUrl = UpgradeITunesArtwork(GetString(result, "artworkUrl100"))
            };
            double score = ScoreCandidate(snapshot, candidate);
            if (score > bestScore)
            {
                bestScore = score;
                best = candidate;
            }
        }

        return best;
    }

    private static async Task<MetadataCandidate?> SearchAcoustIdAsync(
        AudioFileInfo file,
        string apiKey,
        CancellationToken ct)
    {
        var results = await AcoustIdService.Identify(file.FilePath, apiKey, ct);
        var best = results.FirstOrDefault();
        if (best == null) return null;
        return new MetadataCandidate
        {
            Provider = "AcoustID",
            Title = best.Title,
            Artist = best.Artist,
            Album = best.Album,
            TrackNumber = best.TrackNumber ?? 0,
            Year = best.Year ?? 0
        };
    }

    // ─────────────────────────── Deezer (keyless) ───────────────────────────

    private async Task<MetadataCandidate?> SearchDeezerAsync(
        AudioFileInfo file, MetadataTrackSnapshot snapshot, CancellationToken ct)
    {
        var (title, artist, _) = ResolveQueryFields(file);
        if (string.IsNullOrWhiteSpace(title)) return null;

        string query = string.IsNullOrWhiteSpace(artist)
            ? $"track:\"{title}\""
            : $"track:\"{title}\" artist:\"{artist}\"";
        string url = "https://api.deezer.com/search?limit=8&q=" + Uri.EscapeDataString(query);

        await DeezerLimiter.WaitAsync(ct);
        using var stream = await _http.GetStreamAsync(url, ct);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
        if (!doc.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array || data.GetArrayLength() == 0)
            return null;

        MetadataCandidate? best = null;
        double bestScore = -1;
        foreach (var item in data.EnumerateArray())
        {
            var album = item.TryGetProperty("album", out var al) ? al : default;
            var artistEl = item.TryGetProperty("artist", out var ar) ? ar : default;
            var candidate = new MetadataCandidate
            {
                Provider = "Deezer",
                Title = GetString(item, "title"),
                Link = GetString(item, "link"),
                Artist = artistEl.ValueKind == JsonValueKind.Object ? GetString(artistEl, "name") : "",
                Album = album.ValueKind == JsonValueKind.Object ? GetString(album, "title") : "",
                DurationSeconds = GetInt(item, "duration"),
                CoverUrl = album.ValueKind == JsonValueKind.Object
                    ? FirstNonEmpty(GetString(album, "cover_xl"), GetString(album, "cover_big"), GetString(album, "cover_medium"))
                    : ""
            };
            double score = ScoreCandidate(snapshot, candidate);
            if (score > bestScore)
            {
                bestScore = score;
                best = candidate;
            }
        }

        return best;
    }

    // ─────────────────────────── TheAudioDB (keyless public key) ───────────────────────────

    private async Task<MetadataCandidate?> SearchTheAudioDbAsync(AudioFileInfo file, CancellationToken ct)
    {
        var (title, artist, _) = ResolveQueryFields(file);
        // searchtrack requires both an artist and a track name.
        if (string.IsNullOrWhiteSpace(artist) || string.IsNullOrWhiteSpace(title)) return null;

        string url = "https://www.theaudiodb.com/api/v1/json/2/searchtrack.php?s="
            + Uri.EscapeDataString(artist) + "&t=" + Uri.EscapeDataString(title);

        await TheAudioDbLimiter.WaitAsync(ct);
        using var stream = await _http.GetStreamAsync(url, ct);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
        if (!doc.RootElement.TryGetProperty("track", out var tracks) || tracks.ValueKind != JsonValueKind.Array || tracks.GetArrayLength() == 0)
            return null;

        var t = tracks.EnumerateArray().First();
        return new MetadataCandidate
        {
            Provider = "TheAudioDB",
            Title = GetString(t, "strTrack"),
            Artist = GetString(t, "strArtist"),
            Album = GetString(t, "strAlbum"),
            Genre = GetString(t, "strGenre"),
            TrackNumber = ParseTrackNumber(GetString(t, "intTrackNumber")),
            DurationSeconds = GetInt(t, "intDuration") / 1000d,
            CoverUrl = GetString(t, "strTrackThumb")
        };
    }

    // ─────────────────────────── Discogs (token) ───────────────────────────

    private async Task<MetadataCandidate?> SearchDiscogsAsync(
        AudioFileInfo file, string token, MetadataEnrichmentOptions options, bool soundtrack, CancellationToken ct)
    {
        var (title, artist, _) = ResolveQueryFields(file);
        string term = $"{artist} {title}".Trim();
        if (string.IsNullOrWhiteSpace(term)) return null;

        string url = "https://api.discogs.com/database/search?per_page=8&type=release&token="
            + Uri.EscapeDataString(token) + "&q=" + Uri.EscapeDataString(term);

        await DiscogsLimiter.WaitAsync(ct);
        using var stream = await _http.GetStreamAsync(url, ct);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
        if (!doc.RootElement.TryGetProperty("results", out var results) || results.ValueKind != JsonValueKind.Array || results.GetArrayLength() == 0)
            return null;

        var r = results.EnumerateArray().First();
        // Discogs release "title" is formatted "Artist - Album".
        string combined = GetString(r, "title");
        string dcArtist = artist, dcAlbum = combined;
        int dash = combined.IndexOf(" - ", StringComparison.Ordinal);
        if (dash > 0)
        {
            dcArtist = combined[..dash].Trim();
            dcAlbum = combined[(dash + 3)..].Trim();
        }

        string genre = JoinArray(r, "genre");
        if (string.IsNullOrWhiteSpace(genre)) genre = JoinArray(r, "style");

        // No Title: a Discogs *release* result names an album, not a track. Copying the query title
        // in gave every Discogs candidate a similarity of 1.00 against the file it came from, which
        // sent Discogs to the top of the ranking whether or not the release actually matched.
        // An empty title simply drops that term from the score (see ScoreCandidate).
        var candidate = new MetadataCandidate
        {
            Provider = "Discogs",
            Artist = dcArtist,
            Album = dcAlbum,
            Genre = genre,
            Year = ParseYear(GetString(r, "year")),
            CoverUrl = GetString(r, "cover_image")
        };

        int releaseId = GetInt(r, "id");
        if (releaseId > 0 && (soundtrack || options.IsEnabled(MetadataEnrichmentField.Composer)))
            candidate.Composer = await FetchDiscogsComposerAsync(releaseId, token, ct);

        return candidate;
    }

    private async Task<string> FetchDiscogsComposerAsync(int releaseId, string token, CancellationToken ct)
    {
        try
        {
            string url = $"https://api.discogs.com/releases/{releaseId}?token=" + Uri.EscapeDataString(token);
            await DiscogsLimiter.WaitAsync(ct);
            using var stream = await _http.GetStreamAsync(url, ct);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
            if (!doc.RootElement.TryGetProperty("extraartists", out var ea) || ea.ValueKind != JsonValueKind.Array)
                return "";

            var names = ea.EnumerateArray()
                .Where(a => ComposerRoleRegex.IsMatch(GetString(a, "role")))
                .Select(a => CleanDiscogsName(GetString(a, "name")))
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .Distinct(StringComparer.OrdinalIgnoreCase);
            return string.Join("; ", names);
        }
        catch
        {
            return "";
        }
    }

    private static string CleanDiscogsName(string name)
        => string.IsNullOrWhiteSpace(name) ? "" : DiscogsDisambigRegex.Replace(name, "").Trim();

    // ─────────────────────────── fanart.tv (key) ───────────────────────────

    private async Task<string> ResolveFanartCoverAsync(string releaseGroupId, string key, CancellationToken ct)
    {
        try
        {
            string url = $"https://webservice.fanart.tv/v3/music/albums/{releaseGroupId}?api_key={Uri.EscapeDataString(key)}";
            using var stream = await _http.GetStreamAsync(url, ct);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
            if (doc.RootElement.TryGetProperty("albums", out var albums) && albums.ValueKind == JsonValueKind.Object)
            {
                foreach (var album in albums.EnumerateObject())
                {
                    if (album.Value.TryGetProperty("albumcover", out var covers)
                        && covers.ValueKind == JsonValueKind.Array
                        && covers.GetArrayLength() > 0)
                    {
                        string coverUrl = GetString(covers.EnumerateArray().First(), "url");
                        if (!string.IsNullOrWhiteSpace(coverUrl)) return coverUrl;
                    }
                }
            }
        }
        catch
        {
        }

        return "";
    }

    // ─────────────────────────── Soundtrack / key helpers ───────────────────────────

    /// <summary>True when the file looks like part of a soundtrack/OST/score — detected from the
    /// album/folder/file name, or a "Various Artists" performer.</summary>
    private static bool IsSoundtrackContext(AudioFileInfo file, string album, string albumArtist)
    {
        string haystack = $"{album} {file.Album} {file.FolderPath} {file.FileName}";
        if (SoundtrackRegex.IsMatch(haystack)) return true;

        foreach (var value in new[] { file.Artist, albumArtist })
        {
            string v = value?.Trim() ?? "";
            if (v.Equals("Various Artists", StringComparison.OrdinalIgnoreCase)
                || v.Equals("Various", StringComparison.OrdinalIgnoreCase)
                || v.Equals("VA", StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private static bool IsSoundtrackRelease(JsonElement release)
    {
        if (!release.TryGetProperty("release-group", out var group) || group.ValueKind != JsonValueKind.Object)
            return false;
        if (!group.TryGetProperty("secondary-types", out var types) || types.ValueKind != JsonValueKind.Array)
            return false;
        return types.EnumerateArray().Any(t => t.ToString().Contains("Soundtrack", StringComparison.OrdinalIgnoreCase));
    }

    private static string ResolveDiscogsToken(MetadataEnrichmentOptions options)
        => FirstNonEmpty(options.DiscogsToken, Environment.GetEnvironmentVariable("DISCOGS_TOKEN"));

    private static string ResolveFanartKey(MetadataEnrichmentOptions options)
        => FirstNonEmpty(options.FanartTvApiKey, Environment.GetEnvironmentVariable("FANARTTV_API_KEY"));

    // ─────────────────────────── Cover thumbnails (preview) ───────────────────────────

    private async Task AttachCoverThumbsAsync(List<MetadataEnrichmentChange> changes, CancellationToken ct)
    {
        if (AudioAuditorSettings.OfflineMode) return;

        foreach (var change in changes)
        {
            if (change.Field != MetadataEnrichmentField.CoverArt || string.IsNullOrWhiteSpace(change.CoverUrl))
                continue;
            try
            {
                change.CoverThumb = await _http.GetByteArrayAsync(ToThumbUrl(change.CoverUrl), ct);
            }
            catch
            {
                // Non-fatal: the full-size image is still fetched on apply.
            }
        }
    }

    private static string ToThumbUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url)) return url;
        return url
            .Replace("/front-500", "/front-250", StringComparison.OrdinalIgnoreCase)
            .Replace("/front-1200", "/front-250", StringComparison.OrdinalIgnoreCase)
            .Replace("/1000x1000bb.", "/250x250bb.", StringComparison.OrdinalIgnoreCase)
            .Replace("/600x600bb.", "/250x250bb.", StringComparison.OrdinalIgnoreCase)
            .Replace("/1000x1000-", "/250x250-", StringComparison.OrdinalIgnoreCase)
            .Replace("/500x500-", "/250x250-", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Serialises calls to a single provider with a minimum gap between requests.</summary>
    private sealed class RateLimiter
    {
        private readonly SemaphoreSlim _lock = new(1, 1);
        private readonly int _minMs;
        private DateTime _last = DateTime.MinValue;

        public RateLimiter(int minIntervalMs) => _minMs = minIntervalMs;

        public async Task WaitAsync(CancellationToken ct)
        {
            await _lock.WaitAsync(ct);
            try
            {
                var wait = TimeSpan.FromMilliseconds(_minMs) - (DateTime.UtcNow - _last);
                if (wait > TimeSpan.Zero) await Task.Delay(wait, ct);
                _last = DateTime.UtcNow;
            }
            finally
            {
                _lock.Release();
            }
        }
    }

    private async Task<string> ResolveCoverArtUrlAsync(MetadataCandidate candidate, CancellationToken ct)
    {
        foreach (var url in CoverArtUrls(candidate))
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Head, url);
                using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
                if (response.IsSuccessStatusCode)
                    return url;
            }
            catch
            {
            }
        }

        return "";
    }

    private static IEnumerable<string> CoverArtUrls(MetadataCandidate candidate)
    {
        if (!string.IsNullOrWhiteSpace(candidate.ReleaseId))
            yield return $"https://coverartarchive.org/release/{candidate.ReleaseId}/front-500";
        if (!string.IsNullOrWhiteSpace(candidate.ReleaseGroupId))
            yield return $"https://coverartarchive.org/release-group/{candidate.ReleaseGroupId}/front-500";
    }

    private static MetadataCandidate? CandidateFromMusicBrainz(JsonElement recording, bool soundtrack)
    {
        string title = GetString(recording, "title");
        if (string.IsNullOrWhiteSpace(title)) return null;

        string artist = "";
        if (recording.TryGetProperty("artist-credit", out var credits) && credits.ValueKind == JsonValueKind.Array)
            artist = string.Join(", ", credits.EnumerateArray().Select(c => GetString(c, "name")).Where(s => !string.IsNullOrWhiteSpace(s)));

        JsonElement? release = null;
        if (recording.TryGetProperty("releases", out var releases) && releases.ValueKind == JsonValueKind.Array)
        {
            // When the file is a soundtrack, prefer a release whose release-group is typed
            // "Soundtrack"; otherwise fall back to the first release as before.
            JsonElement chosen = default;
            if (soundtrack)
                chosen = releases.EnumerateArray().FirstOrDefault(IsSoundtrackRelease);
            if (chosen.ValueKind != JsonValueKind.Object)
                chosen = releases.EnumerateArray().FirstOrDefault();
            if (chosen.ValueKind == JsonValueKind.Object)
                release = chosen;
        }

        var candidate = new MetadataCandidate
        {
            Provider = "MusicBrainz",
            Title = title,
            Artist = artist,
            Album = release.HasValue ? GetString(release.Value, "title") : "",
            ReleaseId = release.HasValue ? GetString(release.Value, "id") : "",
            Year = release.HasValue ? ParseYear(GetString(release.Value, "date")) : 0,
            DurationSeconds = GetInt(recording, "length") / 1000d
        };

        if (recording.TryGetProperty("release-groups", out var groups) && groups.ValueKind == JsonValueKind.Array)
            candidate.ReleaseGroupId = GetString(groups.EnumerateArray().FirstOrDefault(), "id");

        if (release.HasValue && release.Value.TryGetProperty("media", out var media) && media.ValueKind == JsonValueKind.Array)
        {
            var firstMedium = media.EnumerateArray().FirstOrDefault();
            candidate.DiscNumber = GetInt(firstMedium, "position");
            if (firstMedium.TryGetProperty("tracks", out var tracks) && tracks.ValueKind == JsonValueKind.Array)
            {
                var matchingTrack = tracks.EnumerateArray()
                    .FirstOrDefault(t => string.Equals(GetString(t, "title"), candidate.Title, StringComparison.OrdinalIgnoreCase));
                candidate.TrackNumber = ParseTrackNumber(GetString(matchingTrack, "number"));
            }
        }

        return candidate;
    }

    /// <summary>
    /// Resolves the title / artist / track used to query providers. Falls back to parsing the
    /// file name (junk stripped, "Artist - Title" split) when the tagged fields are empty — this
    /// is what lets untagged / messy-named files (e.g. "Pantheon Music S1 E4 01 Chanda's Revenge")
    /// still match online.
    /// </summary>
    private static (string Title, string Artist, int Track) ResolveQueryFields(AudioFileInfo file)
    {
        string title = file.Title?.Trim() ?? "";
        string artist = file.Artist?.Trim() ?? "";
        int track = 0;

        if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(artist))
        {
            var parsed = FilenameMetadataParser.Parse(file.FileName);
            if (string.IsNullOrWhiteSpace(title)) title = parsed.Title;
            if (string.IsNullOrWhiteSpace(artist)) artist = parsed.Artist;
            track = parsed.TrackNumber;
        }

        if (string.IsNullOrWhiteSpace(title))
            title = Path.GetFileNameWithoutExtension(file.FileName);

        return (title, artist, track);
    }

    private static ExistingMetadataValues ReadExistingMetadata(AudioFileInfo file)
    {
        if (!string.IsNullOrWhiteSpace(file.FilePath) && File.Exists(file.FilePath))
        {
            try
            {
                using var tagFile = TagLib.File.Create(file.FilePath);
                return ExistingMetadataValues.FromTags(file, tagFile.Tag);
            }
            catch
            {
            }
        }

        return ExistingMetadataValues.FromFile(file);
    }

    private sealed class ExistingMetadataValues
    {
        public string Title { get; init; } = "";
        public string Artist { get; init; } = "";
        public string Album { get; init; } = "";
        public string AlbumArtist { get; init; } = "";
        public int Year { get; init; }
        public int TrackNumber { get; init; }
        public int DiscNumber { get; init; }
        public string Genre { get; init; } = "";
        public string Composer { get; init; } = "";
        public string Comment { get; init; } = "";
        public string Lyrics { get; init; } = "";
        public string Copyright { get; init; } = "";
        public bool HasAlbumCover { get; init; }

        /// <summary>
        /// Fallback for a file whose tags could not be read. Year stays 0 ("unknown") on purpose:
        /// AudioFileInfo carries no release year, and the filesystem creation date that used to
        /// stand in for one is the date the file was ripped or downloaded, not the year the record
        /// came out. Feeding that into the scorer actively favoured re-releases over originals.
        /// A 0 is excluded from the weighted score, which is the honest answer.
        /// </summary>
        public static ExistingMetadataValues FromFile(AudioFileInfo file) => new()
        {
            Title = file.Title,
            Artist = file.Artist,
            Album = file.Album,
            Year = 0,
            HasAlbumCover = file.HasAlbumCover
        };

        public static ExistingMetadataValues FromTags(AudioFileInfo file, TagLib.Tag tag) => new()
        {
            Title = FirstNonEmpty(tag.Title, file.Title),
            Artist = FirstNonEmpty(string.Join("; ", tag.Performers), file.Artist),
            Album = FirstNonEmpty(tag.Album, file.Album),
            AlbumArtist = string.Join("; ", tag.AlbumArtists),
            Year = tag.Year > 0 ? (int)tag.Year : 0,
            TrackNumber = tag.Track > 0 ? (int)tag.Track : 0,
            DiscNumber = tag.Disc > 0 ? (int)tag.Disc : 0,
            Genre = string.Join("; ", tag.Genres),
            Composer = string.Join("; ", tag.Composers),
            Comment = tag.Comment ?? "",
            Lyrics = tag.Lyrics ?? "",
            Copyright = tag.Copyright ?? "",
            HasAlbumCover = tag.Pictures.Length > 0 || file.HasAlbumCover
        };

        /// <summary>
        /// The snapshot candidates are ranked against. Empty tag fields fall back to the same
        /// filename-derived values the providers were queried with (<see cref="ResolveQueryFields"/>),
        /// so an untagged file is scored on what we actually asked for rather than on blanks.
        /// </summary>
        public MetadataTrackSnapshot ToSnapshot(AudioFileInfo file)
        {
            var (queryTitle, queryArtist, queryTrack) = ResolveQueryFields(file);
            return new MetadataTrackSnapshot(
                file.FileName,
                FirstNonEmpty(Title, queryTitle, Path.GetFileNameWithoutExtension(file.FileName)),
                FirstNonEmpty(Artist, queryArtist),
                Album,
                file.DurationSeconds,
                TrackNumber > 0 ? TrackNumber : queryTrack,
                Year,
                HasAlbumCover);
        }
    }

    // Instance (not static) so the cover-art download can use the shared _http client.
    private async Task ApplyChangeAsync(TagLib.File tagFile, MetadataEnrichmentChange change, CancellationToken ct)
    {
        switch (change.Field)
        {
            case MetadataEnrichmentField.Title:
                tagFile.Tag.Title = EmptyToNull(change.NewValue);
                break;
            case MetadataEnrichmentField.Artist:
                tagFile.Tag.Performers = Values(change.NewValue);
                break;
            case MetadataEnrichmentField.Album:
                tagFile.Tag.Album = EmptyToNull(change.NewValue);
                break;
            case MetadataEnrichmentField.AlbumArtist:
                tagFile.Tag.AlbumArtists = Values(change.NewValue);
                break;
            case MetadataEnrichmentField.Year:
                tagFile.Tag.Year = uint.TryParse(change.NewValue, out var year) ? year : 0;
                break;
            case MetadataEnrichmentField.TrackNumber:
                tagFile.Tag.Track = uint.TryParse(change.NewValue, out var track) ? track : 0;
                break;
            case MetadataEnrichmentField.DiscNumber:
                tagFile.Tag.Disc = uint.TryParse(change.NewValue, out var disc) ? disc : 0;
                break;
            case MetadataEnrichmentField.Genre:
                tagFile.Tag.Genres = Values(change.NewValue);
                break;
            case MetadataEnrichmentField.Composer:
                tagFile.Tag.Composers = Values(change.NewValue);
                break;
            case MetadataEnrichmentField.Comment:
                tagFile.Tag.Comment = EmptyToNull(change.NewValue);
                break;
            case MetadataEnrichmentField.Lyrics:
                tagFile.Tag.Lyrics = EmptyToNull(change.NewValue);
                break;
            case MetadataEnrichmentField.Copyright:
                tagFile.Tag.Copyright = EmptyToNull(change.NewValue);
                break;
            case MetadataEnrichmentField.StreamingLink:
            {
                string link = change.NewValue?.Trim() ?? "";
                if (!string.IsNullOrWhiteSpace(link))
                {
                    // Append to the Comment (portable across formats), avoiding duplicates.
                    string existingComment = tagFile.Tag.Comment ?? "";
                    if (!existingComment.Contains(link, StringComparison.OrdinalIgnoreCase))
                        tagFile.Tag.Comment = string.IsNullOrWhiteSpace(existingComment)
                            ? link
                            : existingComment.TrimEnd() + " | " + link;
                    // For MP3s, also record it in a proper ID3v2 user URL frame (WXXX).
                    TrySetId3UserUrl(tagFile, link);
                }
                break;
            }
            case MetadataEnrichmentField.CoverArt:
                // A preview taken before offline mode was switched on still carries cover URLs;
                // applying it must not reach out for the bytes.
                if (!string.IsNullOrWhiteSpace(change.CoverUrl) && !AudioAuditorSettings.OfflineMode)
                {
                    // Use the shared client. This runs once per change per file, so a per-call
                    // HttpClient meant one socket pool per track, each lingering in TIME_WAIT —
                    // enough to exhaust ports on a large enrichment batch.
                    var bytes = await _http.GetByteArrayAsync(change.CoverUrl, ct);
                    tagFile.Tag.Pictures = new TagLib.IPicture[]
                    {
                        new TagLib.Picture(new TagLib.ByteVector(bytes))
                        {
                            Type = TagLib.PictureType.FrontCover,
                            MimeType = GuessMimeType(change.CoverUrl)
                        }
                    };
                }
                break;
        }
    }

    private static void AddTextChange(
        List<MetadataEnrichmentChange> changes,
        AudioFileInfo file,
        MetadataEnrichmentField field,
        string oldValue,
        string newValue,
        MetadataCandidate candidate,
        double confidence,
        MetadataEnrichmentOptions options)
    {
        if (!options.IsEnabled(field) || string.IsNullOrWhiteSpace(newValue)) return;
        if (options.MissingOnly && !string.IsNullOrWhiteSpace(oldValue)) return;
        if (string.Equals(oldValue?.Trim(), newValue.Trim(), StringComparison.OrdinalIgnoreCase)) return;
        changes.Add(CreateChange(file, field, oldValue, newValue, candidate, confidence, "Matched online metadata"));
    }

    private static void AddNumberChange(
        List<MetadataEnrichmentChange> changes,
        AudioFileInfo file,
        MetadataEnrichmentField field,
        int oldValue,
        int newValue,
        MetadataCandidate candidate,
        double confidence,
        MetadataEnrichmentOptions options)
    {
        if (!options.IsEnabled(field) || newValue <= 0) return;
        if (options.MissingOnly && oldValue > 0) return;
        if (oldValue == newValue) return;
        changes.Add(CreateChange(
            file,
            field,
            oldValue > 0 ? oldValue.ToString(CultureInfo.InvariantCulture) : "",
            newValue.ToString(CultureInfo.InvariantCulture),
            candidate,
            confidence,
            "Matched online metadata"));
    }

    private static MetadataEnrichmentChange CreateChange(
        AudioFileInfo file,
        MetadataEnrichmentField field,
        string? oldValue,
        string newValue,
        MetadataCandidate candidate,
        double confidence,
        string reason)
    {
        return new MetadataEnrichmentChange
        {
            FilePath = file.FilePath,
            FileName = file.FileName,
            Field = field,
            OldValue = oldValue ?? "",
            NewValue = newValue,
            Provider = candidate.Provider,
            Reason = reason,
            Confidence = confidence,
            IsSelected = confidence >= HighConfidenceThreshold
        };
    }

    /// <summary>
    /// Folds one comparison into the weighted average. <paramref name="applicable"/> decides whether
    /// the term participates at all; the value itself is never used to opt out, so a 0 drags the
    /// score down the way a mismatch should.
    /// </summary>
    private static void AddTerm(
        ref double score, ref double weight, ref int terms, bool applicable, double value, double valueWeight)
    {
        if (!applicable) return;
        score += Math.Clamp(value, 0, 1) * valueWeight;
        weight += valueWeight;
        terms++;
    }

    /// <summary>Public 0..1 fuzzy name similarity, reused by the paste-metadata matcher.</summary>
    public static double NameSimilarity(string? a, string? b) => Similarity(a, b);

    private static double Similarity(string? a, string? b)
    {
        string left = Normalize(a);
        string right = Normalize(b);
        if (left.Length == 0 || right.Length == 0) return 0;
        if (left == right) return 1;
        int distance = Levenshtein(left, right);
        int max = Math.Max(left.Length, right.Length);
        return Math.Clamp(1.0 - (double)distance / max, 0, 1);
    }

    private static int Levenshtein(string a, string b)
    {
        var costs = new int[b.Length + 1];
        for (int j = 0; j <= b.Length; j++) costs[j] = j;
        for (int i = 1; i <= a.Length; i++)
        {
            int previous = costs[0];
            costs[0] = i;
            for (int j = 1; j <= b.Length; j++)
            {
                int current = costs[j];
                costs[j] = a[i - 1] == b[j - 1]
                    ? previous
                    : Math.Min(Math.Min(costs[j - 1], costs[j]), previous) + 1;
                previous = current;
            }
        }

        return costs[b.Length];
    }

    private static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "";
        return new string(value
            .ToLowerInvariant()
            .Where(char.IsLetterOrDigit)
            .ToArray());
    }

    private static string GetString(JsonElement element, string property)
    {
        return element.ValueKind == JsonValueKind.Object
               && element.TryGetProperty(property, out var value)
               && value.ValueKind != JsonValueKind.Null
            ? value.ToString()
            : "";
    }

    private static string JoinArray(JsonElement element, string property)
    {
        if (element.ValueKind != JsonValueKind.Object
            || !element.TryGetProperty(property, out var array)
            || array.ValueKind != JsonValueKind.Array)
            return "";
        return string.Join("; ", array.EnumerateArray()
            .Select(x => x.ToString())
            .Where(s => !string.IsNullOrWhiteSpace(s)));
    }

    private static int GetInt(JsonElement element, string property)
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(property, out var value))
            return 0;
        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number))
            return number;
        return int.TryParse(value.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ? parsed : 0;
    }

    private static int ParseTrackNumber(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return 0;
        string first = value.Split('/')[0].Trim();
        return int.TryParse(first, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ? parsed : 0;
    }

    private static int ParseYear(string value)
    {
        return value.Length >= 4 && int.TryParse(value[..4], NumberStyles.Integer, CultureInfo.InvariantCulture, out var year)
            ? year
            : 0;
    }

    private static string UpgradeITunesArtwork(string url)
    {
        if (string.IsNullOrWhiteSpace(url)) return "";
        return url
            .Replace("/100x100bb.", "/1000x1000bb.", StringComparison.OrdinalIgnoreCase)
            .Replace("/600x600bb.", "/1000x1000bb.", StringComparison.OrdinalIgnoreCase);
    }

    private static string FirstNonEmpty(params string?[] values)
    {
        return values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v))?.Trim() ?? "";
    }

    private static string? EmptyToNull(string value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string[] Values(string value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? Array.Empty<string>()
            : value.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    private static string GuessMimeType(string url)
    {
        string path = url.Split('?')[0];
        if (path.EndsWith(".png", StringComparison.OrdinalIgnoreCase)) return "image/png";
        if (path.EndsWith(".gif", StringComparison.OrdinalIgnoreCase)) return "image/gif";
        if (path.EndsWith(".webp", StringComparison.OrdinalIgnoreCase)) return "image/webp";
        return "image/jpeg";
    }

    /// <summary>Records a streaming URL in an ID3v2 user URL frame (WXXX) when the file already has an
    /// ID3v2 tag (MP3/AIFF/WAV). No-op for formats without ID3v2 — the Comment carries the link there.</summary>
    private static void TrySetId3UserUrl(TagLib.File tagFile, string url)
    {
        try
        {
            if (tagFile.GetTag(TagLib.TagTypes.Id3v2, false) is not TagLib.Id3v2.Tag id3) return;
            var frame = TagLib.Id3v2.UserUrlLinkFrame.Get(id3, "AudioAuditor", true);
            frame.Text = new[] { url };
        }
        catch
        {
        }
    }

    /// <summary>Assert-based checks for the scoring rules (no test framework in this repo).</summary>
    public static void SelfCheck()
    {
        var track = new MetadataTrackSnapshot(
            FileName: "05 Wish You Were Here.flac",
            Title: "Wish You Were Here",
            Artist: "Pink Floyd",
            Album: "Wish You Were Here",
            DurationSeconds: 334,
            TrackNumber: 5,
            Year: 1975,
            HasAlbumCover: false);

        // The release the file actually came from must beat a same-titled later reissue. The year
        // used to come from the file's creation date, which inverted this whenever the rip was recent.
        var original = new MetadataCandidate
        {
            Provider = "MusicBrainz", Title = "Wish You Were Here", Artist = "Pink Floyd",
            Album = "Wish You Were Here", Year = 1975, TrackNumber = 5, DurationSeconds = 334
        };
        var reissue = new MetadataCandidate
        {
            Provider = "MusicBrainz", Title = "Wish You Were Here", Artist = "Pink Floyd",
            Album = "Wish You Were Here", Year = 2023, TrackNumber = 5, DurationSeconds = 334
        };
        Assert(ScoreCandidate(track, original) > ScoreCandidate(track, reissue),
            "the original-year release must outscore a later reissue");

        // A wrong artist has to cost something. Zero-valued terms used to be dropped from the
        // weight, so a shared title alone could score a perfect 1.00.
        var wrongArtist = new MetadataCandidate
        {
            Provider = "Deezer", Title = "Wish You Were Here", Artist = "Incubus",
            Album = "Morning View", Year = 2001, DurationSeconds = 334
        };
        Assert(ScoreCandidate(track, wrongArtist) < ScoreCandidate(track, original),
            "a mismatched artist must score below a matching one");

        // Title alone is a coincidence, not a match: it must not clear the auto-apply bar.
        var titleOnlyTrack = new MetadataTrackSnapshot(
            "Hurt.mp3", "Hurt", "", "", 0, 0, 0, false);
        var titleOnlyCandidate = new MetadataCandidate { Provider = "Deezer", Title = "Hurt" };
        Assert(ScoreCandidate(titleOnlyTrack, titleOnlyCandidate) < HighConfidenceThreshold,
            "a single matching field must stay below the auto-apply threshold");

        // A full match still has to reach it, or nothing would ever auto-apply.
        Assert(ScoreCandidate(track, original) >= HighConfidenceThreshold,
            "an exact match must still reach the auto-apply threshold");

        static void Assert(bool ok, string what)
        {
            if (!ok) throw new Exception("MetadataEnrichmentService.SelfCheck failed: " + what);
        }
    }

    private static async Task ThrottleMusicBrainzAsync(CancellationToken ct)
    {
        await MusicBrainzThrottleLock.WaitAsync(ct);
        try
        {
            var elapsed = DateTime.UtcNow - _lastMusicBrainzRequestUtc;
            var wait = TimeSpan.FromMilliseconds(1100) - elapsed;
            if (wait > TimeSpan.Zero)
                await Task.Delay(wait, ct);
            _lastMusicBrainzRequestUtc = DateTime.UtcNow;
        }
        finally
        {
            MusicBrainzThrottleLock.Release();
        }
    }
}
