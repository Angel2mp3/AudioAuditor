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
    CoverArt
}

public sealed class MetadataEnrichmentOptions
{
    public bool MissingOnly { get; set; } = true;
    public bool ReplaceExistingCover { get; set; }
    public bool UseMusicBrainz { get; set; } = true;
    public bool UseCoverArtArchive { get; set; } = true;
    public bool UseITunes { get; set; }
    public bool UseAcoustId { get; set; }
    public string AcoustIdApiKey { get; set; } = "";
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
}

public sealed class MetadataEnrichmentPreview
{
    public AudioFileInfo File { get; init; } = new();
    public MetadataCandidate? Candidate { get; init; }
    public double Confidence { get; init; }
    public string Status { get; init; } = "";
    public List<MetadataEnrichmentChange> Changes { get; init; } = new();
}

public sealed class MetadataEnrichmentApplySummary
{
    public int FilesChanged { get; set; }
    public int ChangesApplied { get; set; }
    public int FailedFiles { get; set; }
    public List<string> Errors { get; } = new();
}

public sealed class MetadataEnrichmentService
{
    public const double HighConfidenceThreshold = 0.88;
    public const double ReviewConfidenceThreshold = 0.62;

    private static readonly SemaphoreSlim MusicBrainzThrottleLock = new(1, 1);
    private static DateTime _lastMusicBrainzRequestUtc = DateTime.MinValue;
    private readonly HttpClient _http;

    public MetadataEnrichmentService(HttpClient? http = null)
    {
        _http = http ?? new HttpClient();
        if (!_http.DefaultRequestHeaders.UserAgent.Any())
            _http.DefaultRequestHeaders.UserAgent.ParseAdd("AudioAuditor/1.8 (metadata-enrichment)");
    }

    public async Task<IReadOnlyList<MetadataEnrichmentPreview>> PreviewAsync(
        IReadOnlyList<AudioFileInfo> files,
        MetadataEnrichmentOptions options,
        IProgress<(int done, int total, string fileName)>? progress = null,
        CancellationToken ct = default)
    {
        var previews = new List<MetadataEnrichmentPreview>(files.Count);
        for (int i = 0; i < files.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var file = files[i];
            progress?.Report((i, files.Count, file.FileName));

            try
            {
                var candidate = await FindBestCandidateAsync(file, options, ct);
                if (candidate == null)
                {
                    previews.Add(new MetadataEnrichmentPreview
                    {
                        File = file,
                        Status = "No match found"
                    });
                    continue;
                }

                var existing = ReadExistingMetadata(file);
                double score = ScoreCandidate(existing.ToSnapshot(file), candidate);
                var changes = BuildChanges(file, candidate, score, options, existing);
                previews.Add(new MetadataEnrichmentPreview
                {
                    File = file,
                    Candidate = candidate,
                    Confidence = score,
                    Status = score >= HighConfidenceThreshold
                        ? "High confidence"
                        : score >= ReviewConfidenceThreshold
                            ? "Needs review"
                            : "Low confidence",
                    Changes = changes
                });
            }
            catch (Exception ex)
            {
                previews.Add(new MetadataEnrichmentPreview
                {
                    File = file,
                    Status = ex.Message
                });
            }
        }

        progress?.Report((files.Count, files.Count, ""));
        return previews;
    }

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
                    CreateBackup(group.Key);

                using var tagFile = TagLib.File.Create(group.Key);
                foreach (var change in group)
                {
                    await ApplyChangeAsync(tagFile, change, ct);
                    summary.ChangesApplied++;
                }

                tagFile.Save();
                summary.FilesChanged++;
            }
            catch (Exception ex)
            {
                summary.FailedFiles++;
                summary.Errors.Add($"{Path.GetFileName(group.Key)}: {ex.Message}");
            }
        }

        return summary;
    }

    public async Task<MetadataCandidate?> FindBestCandidateAsync(
        AudioFileInfo file,
        MetadataEnrichmentOptions options,
        CancellationToken ct = default)
    {
        var candidates = new List<MetadataCandidate>();

        if (options.UseMusicBrainz)
        {
            var mb = await SearchMusicBrainzAsync(file, options, ct);
            if (mb != null) candidates.Add(mb);
        }

        if (options.UseITunes)
        {
            var itunes = await SearchITunesAsync(file, ct);
            if (itunes != null) candidates.Add(itunes);
        }

        if (options.UseAcoustId && !string.IsNullOrWhiteSpace(options.AcoustIdApiKey) && File.Exists(file.FilePath))
        {
            var acoust = await SearchAcoustIdAsync(file, options.AcoustIdApiKey, ct);
            if (acoust != null) candidates.Add(acoust);
        }

        if (candidates.Count == 0) return null;

        var snapshot = CreateSnapshot(file);
        return candidates
            .OrderByDescending(c => ScoreCandidate(snapshot, c))
            .First();
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

        return changes;
    }

    public static double ScoreCandidate(MetadataTrackSnapshot track, MetadataCandidate candidate)
    {
        double score = 0;
        double weight = 0;

        AddWeighted(ref score, ref weight, Similarity(track.Title, candidate.Title), 0.32);
        AddWeighted(ref score, ref weight, Similarity(track.Artist, candidate.Artist), 0.28);
        AddWeighted(ref score, ref weight, Similarity(track.Album, candidate.Album), 0.16);

        if (track.DurationSeconds > 0 && candidate.DurationSeconds > 0)
        {
            double delta = Math.Abs(track.DurationSeconds - candidate.DurationSeconds);
            AddWeighted(ref score, ref weight, delta <= 2 ? 1 : delta <= 8 ? 0.75 : delta <= 20 ? 0.35 : 0, 0.12);
        }

        if (track.TrackNumber > 0 && candidate.TrackNumber > 0)
            AddWeighted(ref score, ref weight, track.TrackNumber == candidate.TrackNumber ? 1 : 0.35, 0.06);

        if (track.Year > 0 && candidate.Year > 0)
            AddWeighted(ref score, ref weight, Math.Abs(track.Year - candidate.Year) <= 1 ? 1 : 0.4, 0.06);

        if (weight <= 0) return 0;
        return Math.Clamp(score / weight, 0, 1);
    }

    private async Task<MetadataCandidate?> SearchMusicBrainzAsync(
        AudioFileInfo file,
        MetadataEnrichmentOptions options,
        CancellationToken ct)
    {
        string title = FirstNonEmpty(file.Title, Path.GetFileNameWithoutExtension(file.FileName));
        string artist = file.Artist;
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
        var snapshot = CreateSnapshot(file);
        foreach (var recording in recordings.EnumerateArray())
        {
            var candidate = CandidateFromMusicBrainz(recording);
            if (candidate == null) continue;
            if (options.UseCoverArtArchive)
                candidate.CoverUrl = await ResolveCoverArtUrlAsync(candidate, ct);

            double score = ScoreCandidate(snapshot, candidate);
            if (score > bestScore)
            {
                bestScore = score;
                best = candidate;
            }
        }

        return best;
    }

    private async Task<MetadataCandidate?> SearchITunesAsync(AudioFileInfo file, CancellationToken ct)
    {
        string title = FirstNonEmpty(file.Title, Path.GetFileNameWithoutExtension(file.FileName));
        string term = Uri.EscapeDataString($"{file.Artist} {title}".Trim());
        string url = $"https://itunes.apple.com/search?term={term}&entity=song&limit=8";
        using var stream = await _http.GetStreamAsync(url, ct);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
        if (!doc.RootElement.TryGetProperty("results", out var results) || results.GetArrayLength() == 0)
            return null;

        MetadataCandidate? best = null;
        double bestScore = -1;
        var snapshot = CreateSnapshot(file);
        foreach (var result in results.EnumerateArray())
        {
            var candidate = new MetadataCandidate
            {
                Provider = "Apple/iTunes",
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

    private static MetadataCandidate? CandidateFromMusicBrainz(JsonElement recording)
    {
        string title = GetString(recording, "title");
        if (string.IsNullOrWhiteSpace(title)) return null;

        string artist = "";
        if (recording.TryGetProperty("artist-credit", out var credits) && credits.ValueKind == JsonValueKind.Array)
            artist = string.Join(", ", credits.EnumerateArray().Select(c => GetString(c, "name")).Where(s => !string.IsNullOrWhiteSpace(s)));

        JsonElement? release = null;
        if (recording.TryGetProperty("releases", out var releases) && releases.ValueKind == JsonValueKind.Array)
            release = releases.EnumerateArray().FirstOrDefault();

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

    private static MetadataTrackSnapshot CreateSnapshot(AudioFileInfo file)
    {
        return new MetadataTrackSnapshot(
            file.FileName,
            FirstNonEmpty(file.Title, Path.GetFileNameWithoutExtension(file.FileName)),
            file.Artist,
            file.Album,
            file.DurationSeconds,
            0,
            file.DateCreated.Year > 1900 ? file.DateCreated.Year : 0,
            file.HasAlbumCover);
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

        public static ExistingMetadataValues FromFile(AudioFileInfo file) => new()
        {
            Title = file.Title,
            Artist = file.Artist,
            Album = file.Album,
            Year = file.DateCreated.Year > 1900 ? file.DateCreated.Year : 0,
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

        public MetadataTrackSnapshot ToSnapshot(AudioFileInfo file)
        {
            return new MetadataTrackSnapshot(
                file.FileName,
                FirstNonEmpty(Title, Path.GetFileNameWithoutExtension(file.FileName)),
                Artist,
                Album,
                file.DurationSeconds,
                TrackNumber,
                Year,
                HasAlbumCover);
        }
    }

    private static async Task ApplyChangeAsync(TagLib.File tagFile, MetadataEnrichmentChange change, CancellationToken ct)
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
            case MetadataEnrichmentField.CoverArt:
                if (!string.IsNullOrWhiteSpace(change.CoverUrl))
                {
                    using var http = new HttpClient();
                    var bytes = await http.GetByteArrayAsync(change.CoverUrl, ct);
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

    private static void AddWeighted(ref double score, ref double weight, double value, double valueWeight)
    {
        if (value <= 0) return;
        score += value * valueWeight;
        weight += valueWeight;
    }

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

    private static void CreateBackup(string filePath)
    {
        if (!File.Exists(filePath)) return;
        string backup = filePath + $".audioauditor-backup-{DateTime.UtcNow:yyyyMMddHHmmss}";
        File.Copy(filePath, backup, overwrite: false);
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
