using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using AudioQualityChecker.Models;

namespace AudioQualityChecker.Services;

public enum BatchTrackMode
{
    None,
    Fixed,
    AutoIncrement
}

public enum BatchCoverAction
{
    None,
    SetFromBytes,
    FetchOnlinePerAlbum,
    Remove
}

/// <summary>
/// Describes a manual bulk edit: which tag fields to overwrite (and with what), how to handle
/// track numbers, and what to do with embedded cover art. Only fields whose Set* flag is true are
/// written, so unchecked fields are left untouched on every file.
/// </summary>
public sealed class BatchFieldEditOptions
{
    public bool SetTitle { get; set; }
    public string Title { get; set; } = "";

    public bool SetArtist { get; set; }
    public string Artist { get; set; } = "";

    public bool SetAlbum { get; set; }
    public string Album { get; set; } = "";

    public bool SetAlbumArtist { get; set; }
    public string AlbumArtist { get; set; } = "";

    public bool SetYear { get; set; }
    public string Year { get; set; } = "";

    public bool SetGenre { get; set; }
    public string Genre { get; set; } = "";

    public bool SetComposer { get; set; }
    public string Composer { get; set; } = "";

    public bool SetComment { get; set; }
    public string Comment { get; set; } = "";

    public bool SetDisc { get; set; }
    public string Disc { get; set; } = "";

    public BatchTrackMode TrackMode { get; set; } = BatchTrackMode.None;
    public string TrackFixed { get; set; } = "";
    public int TrackStart { get; set; } = 1;

    public BatchCoverAction CoverAction { get; set; } = BatchCoverAction.None;
    public byte[]? CoverBytes { get; set; }
    public string? CoverMime { get; set; }

    /// <summary>True when at least one field, the track number, or the cover will be written.</summary>
    public bool HasAnyChange =>
        SetTitle || SetArtist || SetAlbum || SetAlbumArtist || SetYear || SetGenre
        || SetComposer || SetComment || SetDisc
        || TrackMode != BatchTrackMode.None
        || CoverAction != BatchCoverAction.None;
}

public sealed class BatchFieldEditSummary
{
    public int FilesChanged { get; set; }
    public int FailedFiles { get; set; }
    public List<string> Errors { get; } = new();

    /// <summary>
    /// Paths whose tags actually reached disk, so callers can mirror the edit onto exactly those
    /// in-memory rows instead of the whole selection.
    /// </summary>
    public HashSet<string> WrittenPaths { get; } = new(StringComparer.OrdinalIgnoreCase);
}

/// <summary>
/// Applies a <see cref="BatchFieldEditOptions"/> to many files at once. Used by the Manual Edit tab
/// of the Batch Editor. Cover fetching reuses <see cref="MetadataEnrichmentService"/> so online
/// lookups behave consistently with the Auto-Tag tab.
/// </summary>
public sealed class BatchFieldEditService
{
    private readonly HttpClient _http;
    private readonly MetadataEnrichmentService _enrichment;

    public BatchFieldEditService(HttpClient? http = null, MetadataEnrichmentService? enrichment = null)
    {
        // Only the fallback needs a budget — an injected client brings its own.
        _http = http ?? new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        _enrichment = enrichment ?? new MetadataEnrichmentService(_http);
    }

    public async Task<BatchFieldEditSummary> ApplyAsync(
        IReadOnlyList<AudioFileInfo> files,
        BatchFieldEditOptions options,
        bool createBackups,
        IProgress<(int done, int total, string fileName)>? progress = null,
        CancellationToken ct = default)
    {
        var summary = new BatchFieldEditSummary();
        if (!options.HasAnyChange) return summary;

        // Reject bad numeric input before writing anything, so a typo can't wipe a field everywhere.
        var invalid = Validate(options);
        if (invalid.Count > 0)
        {
            summary.Errors.AddRange(invalid);
            return summary;
        }

        // Pre-resolve one cover per album group so we only hit the network once per album.
        Dictionary<string, (byte[] bytes, string mime)>? coversByAlbum = null;
        if (options.CoverAction == BatchCoverAction.FetchOnlinePerAlbum)
            coversByAlbum = await FetchCoversPerAlbumAsync(files, ct);

        for (int i = 0; i < files.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var file = files[i];
            progress?.Report((i, files.Count, file.FileName));

            if (string.IsNullOrWhiteSpace(file.FilePath) || !File.Exists(file.FilePath))
            {
                summary.FailedFiles++;
                summary.Errors.Add($"{file.FileName}: file not found");
                continue;
            }

            try
            {
                if (createBackups)
                    FileRenamer.CreateBackup(file.FilePath);

                using var tagFile = TagLib.File.Create(file.FilePath);
                var tag = tagFile.Tag;

                if (options.SetTitle) tag.Title = EmptyToNull(options.Title);
                if (options.SetArtist) tag.Performers = Values(options.Artist);
                if (options.SetAlbum) tag.Album = EmptyToNull(options.Album);
                if (options.SetAlbumArtist) tag.AlbumArtists = Values(options.AlbumArtist);
                if (options.SetGenre) tag.Genres = Values(options.Genre);
                if (options.SetComposer) tag.Composers = Values(options.Composer);
                if (options.SetComment) tag.Comment = EmptyToNull(options.Comment);
                if (options.SetYear) tag.Year = ParseUint(options.Year);
                if (options.SetDisc) tag.Disc = ParseUint(options.Disc);

                switch (options.TrackMode)
                {
                    case BatchTrackMode.Fixed:
                        tag.Track = ParseUint(options.TrackFixed);
                        break;
                    case BatchTrackMode.AutoIncrement:
                        // Numbered by position in the selection, not by how many writes succeeded —
                        // otherwise one unwritable file silently shifted every later track number
                        // down by one, off by one from what the user was shown.
                        tag.Track = (uint)(options.TrackStart + i);
                        break;
                }

                ApplyCover(tag, file, options, coversByAlbum);

                tagFile.Save();
                summary.FilesChanged++;
                summary.WrittenPaths.Add(file.FilePath);
            }
            catch (Exception ex)
            {
                summary.FailedFiles++;
                summary.Errors.Add($"{file.FileName}: {ex.Message}");
            }
        }

        progress?.Report((files.Count, files.Count, ""));
        return summary;
    }

    private static void ApplyCover(
        TagLib.Tag tag,
        AudioFileInfo file,
        BatchFieldEditOptions options,
        IReadOnlyDictionary<string, (byte[] bytes, string mime)>? coversByAlbum)
    {
        switch (options.CoverAction)
        {
            case BatchCoverAction.Remove:
                tag.Pictures = Array.Empty<TagLib.IPicture>();
                break;
            case BatchCoverAction.SetFromBytes:
                if (options.CoverBytes is { Length: > 0 })
                    tag.Pictures = MakeCover(options.CoverBytes, options.CoverMime ?? "image/jpeg");
                break;
            case BatchCoverAction.FetchOnlinePerAlbum:
                if (coversByAlbum != null && coversByAlbum.TryGetValue(AlbumKey(file), out var cover))
                    tag.Pictures = MakeCover(cover.bytes, cover.mime);
                break;
        }
    }

    private static TagLib.IPicture[] MakeCover(byte[] bytes, string mime) => new TagLib.IPicture[]
    {
        new TagLib.Picture(new TagLib.ByteVector(bytes))
        {
            Type = TagLib.PictureType.FrontCover,
            MimeType = mime
        }
    };

    private async Task<Dictionary<string, (byte[] bytes, string mime)>> FetchCoversPerAlbumAsync(
        IReadOnlyList<AudioFileInfo> files,
        CancellationToken ct)
    {
        var result = new Dictionary<string, (byte[], string)>(StringComparer.OrdinalIgnoreCase);
        var coverOptions = new MetadataEnrichmentOptions
        {
            UseMusicBrainz = true,
            UseCoverArtArchive = true,
            UseITunes = true,
            UseAcoustId = false
        };

        foreach (var group in files.GroupBy(AlbumKey, StringComparer.OrdinalIgnoreCase))
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var candidate = await _enrichment.FindBestCandidateAsync(group.First(), coverOptions, ct);
                if (candidate == null || string.IsNullOrWhiteSpace(candidate.CoverUrl)) continue;
                var bytes = await _http.GetByteArrayAsync(candidate.CoverUrl, ct);
                if (bytes.Length > 0)
                    result[group.Key] = (bytes, GuessMimeType(candidate.CoverUrl));
            }
            catch
            {
                // Leave this album without a fetched cover; other albums still proceed.
            }
        }

        return result;
    }

    /// <summary>
    /// Groups files that should share one fetched cover. The artist is part of the key because
    /// album titles collide constantly ("Greatest Hits") and two artists' compilations must not end
    /// up with the same artwork.
    /// </summary>
    private static string AlbumKey(AudioFileInfo file)
    {
        var album = (file.Album ?? "").Trim();
        if (!string.IsNullOrWhiteSpace(album))
        {
            var artist = (file.Artist ?? "").Trim();
            return "album:" + artist.ToLowerInvariant() + "" + album.ToLowerInvariant();
        }
        var folder = Path.GetDirectoryName(file.FilePath) ?? file.FolderPath ?? "";
        return "folder:" + folder.ToLowerInvariant();
    }

    /// <summary>
    /// Parses a numeric tag field. An empty box means "clear this field" (0). Anything non-numeric
    /// is rejected rather than silently coerced to 0 — a typo like "nineteen" used to wipe the year
    /// across the whole selection with no warning.
    /// </summary>
    private static uint ParseUint(string value) =>
        uint.TryParse((value ?? "").Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : 0;

    /// <summary>
    /// Checks the numeric boxes before a single file is touched. An empty box means "clear this
    /// field"; anything non-numeric is an error rather than a silent 0, which would otherwise wipe
    /// the year/disc/track across the whole selection.
    /// </summary>
    public static IReadOnlyList<string> Validate(BatchFieldEditOptions options)
    {
        var errors = new List<string>();
        Check(options.SetYear, options.Year, "Year");
        Check(options.SetDisc, options.Disc, "Disc");
        Check(options.TrackMode == BatchTrackMode.Fixed, options.TrackFixed, "Track number");

        // Auto-increment reaches here as an already-parsed int, so a typo in the box has been
        // silently coerced by the caller. A start below 1 is still catchable, and it matters: it
        // used to write track 0 to the first few files instead of failing.
        if (options.TrackMode == BatchTrackMode.AutoIncrement && options.TrackStart < 1)
            errors.Add($"Track start must be a whole number of 1 or more (got \"{options.TrackStart}\").");

        return errors;

        void Check(bool enabled, string value, string fieldName)
        {
            string text = (value ?? "").Trim();
            if (!enabled || text.Length == 0) return;
            if (!uint.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
                errors.Add($"{fieldName} must be a whole number (got \"{text}\").");
        }
    }

    /// <summary>
    /// Parses the auto-increment "Start at" box. Lives here so both front-ends reject the same
    /// input: the WPF editor used to fall back to 1 on any unparseable text, so "abc" quietly
    /// renumbered the whole selection from 1 with no warning.
    /// </summary>
    public static bool TryParseTrackStart(string? text, out int start, out string error)
    {
        start = 1;
        error = "";
        string trimmed = (text ?? "").Trim();
        if (trimmed.Length == 0) return true;   // empty box means "start at 1"

        if (!int.TryParse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
        {
            error = $"Track start must be a whole number (got \"{trimmed}\").";
            return false;
        }
        if (parsed < 1)
        {
            error = $"Track start must be 1 or more (got \"{trimmed}\").";
            return false;
        }

        start = parsed;
        return true;
    }

    private static string? EmptyToNull(string value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string[] Values(string value) =>
        string.IsNullOrWhiteSpace(value)
            ? Array.Empty<string>()
            : value.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    /// <summary>Assert-based checks for the input validation (no test framework in this repo).</summary>
    public static void SelfCheck()
    {
        // A typo in the auto-increment start must be rejected, not silently turned into 1.
        Assert(!TryParseTrackStart("abc", out _, out _), "non-numeric track start must be rejected");
        Assert(!TryParseTrackStart("-5", out _, out _), "negative track start must be rejected");
        Assert(!TryParseTrackStart("0", out _, out _), "zero track start must be rejected");
        Assert(TryParseTrackStart("", out var blank, out _) && blank == 1, "an empty box means start at 1");
        Assert(TryParseTrackStart(" 7 ", out var seven, out _) && seven == 7, "a padded number still parses");

        var bad = new BatchFieldEditOptions { TrackMode = BatchTrackMode.AutoIncrement, TrackStart = 0 };
        Assert(Validate(bad).Count > 0, "Validate must reject an auto-increment start below 1");

        var good = new BatchFieldEditOptions { TrackMode = BatchTrackMode.AutoIncrement, TrackStart = 1 };
        Assert(Validate(good).Count == 0, "Validate must accept a start of 1");

        // Year/disc typos are rejected; an empty box means "clear this field" and is allowed.
        Assert(Validate(new BatchFieldEditOptions { SetYear = true, Year = "nineteen" }).Count > 0,
            "a non-numeric year must be rejected");
        Assert(Validate(new BatchFieldEditOptions { SetYear = true, Year = "" }).Count == 0,
            "an empty year box means clear, not error");

        // The album grouping key must not run the artist and album together.
        var a = new AudioFileInfo { Artist = "Ab", Album = "bagold", FilePath = @"C:\a\1.flac" };
        var b = new AudioFileInfo { Artist = "Abba", Album = "Gold", FilePath = @"C:\b\1.flac" };
        Assert(AlbumKey(a) != AlbumKey(b), "artist/album must not concatenate into the same key");

        static void Assert(bool ok, string what)
        {
            if (!ok) throw new Exception("BatchFieldEditService.SelfCheck failed: " + what);
        }
    }

    private static string GuessMimeType(string url)
    {
        string path = url.Split('?')[0];
        if (path.EndsWith(".png", StringComparison.OrdinalIgnoreCase)) return "image/png";
        if (path.EndsWith(".gif", StringComparison.OrdinalIgnoreCase)) return "image/gif";
        if (path.EndsWith(".webp", StringComparison.OrdinalIgnoreCase)) return "image/webp";
        return "image/jpeg";
    }

}
