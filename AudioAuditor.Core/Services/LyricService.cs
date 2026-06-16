using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace AudioQualityChecker.Services;

/// <summary>
/// Hint indicating whether a track is the Explicit, Clean, Radio Edit, or Album version.
/// Used to bias lyric search candidate ranking so explicit tracks don't get clean lyrics (and vice versa).
/// </summary>
public enum LyricVersionHint { Unknown, Explicit, Clean, RadioEdit, AlbumVersion }

/// <summary>
/// Represents a single word in a karaoke (word-timed) lyric line.
/// </summary>
public record KaraokeWord(TimeSpan Start, TimeSpan End, string Text);

/// <summary>
/// Represents a single timed lyric line.
/// </summary>
public record LyricLine(TimeSpan Time, string Text)
{
    /// <summary>
    /// Word-level timings when available (Enhanced LRC / karaoke sources).
    /// Null when only line-level timing is known.
    /// </summary>
    public List<KaraokeWord>? Words { get; init; }
}

/// <summary>
/// Parsed lyrics result with metadata.
/// </summary>
public record LyricsResult
{
    public List<LyricLine> Lines { get; init; } = new();
    public bool IsTimed { get; init; }
    public string Source { get; init; } = "";
    public string? Title { get; init; }
    public string? Artist { get; init; }
    public string? Album { get; init; }

    public static LyricsResult Empty => new() { Lines = new(), IsTimed = false, Source = "none" };

    public bool HasLyrics => Lines.Count > 0;
}

/// <summary>
/// Parses .lrc (synchronized lyrics) files. Supports:
/// - Standard LRC [mm:ss.xx] or [mm:ss:xx]
/// - Enhanced LRC with word-level timing
/// - ID tags [ar:], [ti:], [al:], [offset:]
/// </summary>
public static class LrcParser
{
    private static readonly Regex TimeTagRegex = new(
        @"\[(\d{1,3}):(\d{2})(?:[.:](\d{1,3}))?\]",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    // Enhanced LRC word-level timestamps: <mm:ss.xx>
    private static readonly Regex WordTimeTagRegex = new(
        @"<(\d{1,3}):(\d{2})(?:[.:](\d{1,3}))?>",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex IdTagRegex = new(
        @"\[(\w+):(.+?)\]",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>
    /// Checks whether a block of text is likely LRC format vs. plain lyrics
    /// that happen to contain brackets. Requires multiple lines starting
    /// with a valid [mm:ss] timestamp to avoid false positives.
    /// </summary>
    public static bool LooksLikeLrc(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        int timedLines = 0;
        foreach (var rawLine in text.Split('\n'))
        {
            var line = rawLine.TrimStart();
            if (line.Length > 0 && TimeTagRegex.Match(line) is { Success: true } m && m.Index == 0)
            {
                timedLines++;
                if (timedLines >= 2) return true;
            }
        }
        return false;
    }

    public static LyricsResult Parse(string lrcContent)
    {
        if (string.IsNullOrWhiteSpace(lrcContent))
            return LyricsResult.Empty;

        var lines = new List<LyricLine>();
        string? title = null, artist = null, album = null;
        int offsetMs = 0;

        foreach (var rawLine in lrcContent.Split('\n'))
        {
            var line = rawLine.Trim().TrimStart('\uFEFF');
            if (string.IsNullOrEmpty(line)) continue;

            // Check for ID tags
            var idMatch = IdTagRegex.Match(line);
            if (idMatch.Success && !TimeTagRegex.IsMatch(line))
            {
                var tag = idMatch.Groups[1].Value.ToLowerInvariant();
                var value = idMatch.Groups[2].Value.Trim();
                switch (tag)
                {
                    case "ti": title = value; break;
                    case "ar": artist = value; break;
                    case "al": album = value; break;
                    case "offset":
                        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out offsetMs);
                        break;
                }
                continue;
            }

            // Parse time tags — one line can have multiple [mm:ss.xx] tags
            var timeMatches = TimeTagRegex.Matches(line);
            if (timeMatches.Count == 0) continue;

            // Extract text after line-level tags
            string rawText = TimeTagRegex.Replace(line, "").Trim();
            if (string.IsNullOrEmpty(rawText)) continue;

            // Check for Enhanced LRC word-level timestamps
            List<KaraokeWord>? karaokeWords = TryParseEnhancedLrcWords(rawText, offsetMs);

            // For display, strip word-level tags from the text
            string displayText = WordTimeTagRegex.Replace(rawText, "").Trim();
            if (string.IsNullOrEmpty(displayText)) continue;

            foreach (Match tm in timeMatches)
            {
                int min = int.Parse(tm.Groups[1].Value, CultureInfo.InvariantCulture);
                int sec = int.Parse(tm.Groups[2].Value, CultureInfo.InvariantCulture);
                int ms = 0;
                if (tm.Groups[3].Success)
                {
                    string msStr = tm.Groups[3].Value;
                    ms = msStr.Length switch
                    {
                        1 => int.Parse(msStr, CultureInfo.InvariantCulture) * 100,
                        2 => int.Parse(msStr, CultureInfo.InvariantCulture) * 10,
                        _ => int.Parse(msStr, CultureInfo.InvariantCulture)
                    };
                }

                var time = TimeSpan.FromMilliseconds(min * 60000.0 + sec * 1000.0 + ms + offsetMs);
                if (time < TimeSpan.Zero) time = TimeSpan.Zero;
                lines.Add(new LyricLine(time, displayText) { Words = karaokeWords });
            }
        }

        lines.Sort((a, b) => a.Time.CompareTo(b.Time));

        return new LyricsResult
        {
            Lines = lines,
            IsTimed = lines.Count > 0,
            Source = "lrc",
            Title = title,
            Artist = artist,
            Album = album
        };
    }

    /// <summary>
    /// Attempts to parse Enhanced LRC word-level timestamps from a line fragment.
    /// Returns null if no word tags are found.
    /// </summary>
    private static List<KaraokeWord>? TryParseEnhancedLrcWords(string rawText, int offsetMs)
    {
        var wordMatches = WordTimeTagRegex.Matches(rawText);
        if (wordMatches.Count == 0) return null;

        var words = new List<KaraokeWord>();
        for (int i = 0; i < wordMatches.Count; i++)
        {
            var match = wordMatches[i];
            int min = int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
            int sec = int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture);
            int ms = 0;
            if (match.Groups[3].Success)
            {
                string msStr = match.Groups[3].Value;
                ms = msStr.Length switch
                {
                    1 => int.Parse(msStr, CultureInfo.InvariantCulture) * 100,
                    2 => int.Parse(msStr, CultureInfo.InvariantCulture) * 10,
                    _ => int.Parse(msStr, CultureInfo.InvariantCulture)
                };
            }

            var start = TimeSpan.FromMilliseconds(min * 60000.0 + sec * 1000.0 + ms + offsetMs);
            if (start < TimeSpan.Zero) start = TimeSpan.Zero;

            // Extract the word text between this tag and the next tag (or end of string)
            int textStart = match.Index + match.Length;
            int textEnd = (i + 1 < wordMatches.Count) ? wordMatches[i + 1].Index : rawText.Length;
            string wordText = rawText[textStart..textEnd].Trim();

            words.Add(new KaraokeWord(start, TimeSpan.Zero, wordText));
        }

        // Compute end times: each word ends when the next word starts
        for (int i = 0; i < words.Count - 1; i++)
        {
            words[i] = words[i] with { End = words[i + 1].Start };
        }
        // Last word gets a default duration if no next word
        if (words.Count > 0)
        {
            var last = words[^1];
            words[^1] = last with { End = last.Start + TimeSpan.FromSeconds(1.5) };
        }

        return words;
    }

    public static LyricsResult ParseFile(string lrcPath)
    {
        if (!File.Exists(lrcPath)) return LyricsResult.Empty;
        return Parse(File.ReadAllText(lrcPath));
    }
}

/// <summary>
/// Extracts lyrics from embedded metadata (TagLib).
/// </summary>
public static class EmbeddedLyricsProvider
{
    public static LyricsResult Extract(string audioFilePath)
    {
        try
        {
            using var tagFile = TagLib.File.Create(audioFilePath);

            // Try synced lyrics first (ID3v2 SYLT frames)
            if (tagFile.Tag is TagLib.Id3v2.Tag id3Tag)
            {
                foreach (var frame in id3Tag.GetFrames())
                {
                    if (frame is TagLib.Id3v2.SynchronisedLyricsFrame sylt)
                    {
                        var lines = new List<LyricLine>();
                        foreach (var entry in sylt.Text)
                        {
                            lines.Add(new LyricLine(
                                TimeSpan.FromMilliseconds(entry.Time),
                                entry.Text));
                        }
                        if (lines.Count > 0)
                        {
                            lines.Sort((a, b) => a.Time.CompareTo(b.Time));
                            return new LyricsResult
                            {
                                Lines = lines,
                                IsTimed = true,
                                Source = "embedded-synced"
                            };
                        }
                    }
                }
            }

            // Try unsynced lyrics (USLT / Lyrics tag)
            string? lyrics = tagFile.Tag.Lyrics;
            if (!string.IsNullOrWhiteSpace(lyrics))
            {
                // Check if it's actually LRC format stored as lyrics —
                // require multiple lines starting with [mm:ss to avoid false positives
                // from plain lyrics containing brackets (e.g. [Chorus], [1:23] references)
                if (LrcParser.LooksLikeLrc(lyrics)
                    && LrcParser.Parse(lyrics) is { HasLyrics: true } lrcResult)
                    return lrcResult with { Source = "embedded-lrc" };

                // Plain text lyrics — split on newlines
                var lines = lyrics.Split('\n')
                    .Select(l => l.Trim())
                    .Where(l => !string.IsNullOrEmpty(l))
                    .Select(l => new LyricLine(TimeSpan.Zero, l))
                    .ToList();

                return new LyricsResult
                {
                    Lines = lines,
                    IsTimed = false,
                    Source = "embedded"
                };
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[LyricService.embedded] {ex.GetType().Name}: {ex.Message}");
        }

        return LyricsResult.Empty;
    }
}

/// <summary>
/// Unified lyrics service that tries multiple providers in priority order:
/// 1. .lrc file alongside audio file
/// 2. Embedded synchronized lyrics (SYLT)
/// 3. Embedded unsynchronized lyrics (USLT)
/// 4. LRCLIB online service (free synced lyrics database)
/// </summary>
public static class LyricService
{
    public enum LyricProvider
    {
        Auto,
        LrcFile,
        Embedded,
        LrcLib,
        Netease,
        Musixmatch,
    }

    private static readonly HttpClient Http = new()
    {
        Timeout = TimeSpan.FromSeconds(8),
        DefaultRequestHeaders = { { "User-Agent", "AudioAuditor/1.5 (https://github.com)" } }
    };

    /// <summary>Synchronous lyrics fetch (local sources only).</summary>
    public static LyricsResult GetLyrics(string audioFilePath, LyricProvider preferred = LyricProvider.Auto)
    {
        if (string.IsNullOrEmpty(audioFilePath) || !File.Exists(audioFilePath))
            return LyricsResult.Empty;

        var providers = BuildLocalProviders(audioFilePath, preferred);
        foreach (var provider in providers)
        {
            try
            {
                var result = provider();
                if (result.HasLyrics) return result;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[LyricService.local] {ex.GetType().Name}: {ex.Message}");
            }
        }
        return LyricsResult.Empty;
    }

    /// <summary>Async lyrics fetch — tries local sources first, then online.</summary>
    public static async Task<LyricsResult> GetLyricsAsync(string audioFilePath,
        LyricProvider preferred = LyricProvider.Auto,
        string? artist = null, string? title = null, string? album = null,
        double durationSeconds = 0,
        LyricVersionHint? forceVersion = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(audioFilePath) || !File.Exists(audioFilePath))
            return LyricsResult.Empty;

        cancellationToken.ThrowIfCancellationRequested();
        bool isOnlineOnly = preferred == LyricProvider.LrcLib || preferred == LyricProvider.Netease || preferred == LyricProvider.Musixmatch;

        // Try local first
        var local = GetLyrics(audioFilePath, isOnlineOnly ? LyricProvider.Auto : preferred);
        if (local.HasLyrics && !isOnlineOnly && forceVersion == null
            && (preferred != LyricProvider.Auto || local.IsTimed))
            return local;
        var localFallback = local.HasLyrics ? local : LyricsResult.Empty;

        // If preferred is specifically a local source and we didn't find anything, return empty
        if (preferred == LyricProvider.LrcFile || preferred == LyricProvider.Embedded)
            return local;

        // Read tags if not provided, and detect explicit/clean version hint
        LyricVersionHint hint = LyricVersionHint.Unknown;
        try
        {
            using var tagFile = TagLib.File.Create(audioFilePath);
            if (tagFile?.Tag != null)
            {
                artist ??= tagFile.Tag.FirstPerformer;
                title ??= tagFile.Tag.Title;
                album ??= tagFile.Tag.Album;
                if (durationSeconds <= 0)
                    durationSeconds = tagFile.Properties.Duration.TotalSeconds;
                hint = DetectVersionHint(tagFile, title);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[LyricService.tagRead] {ex.GetType().Name}: {ex.Message}");
        }

        if (forceVersion.HasValue)
            hint = forceVersion.Value;

        if (string.IsNullOrEmpty(artist) || string.IsNullOrEmpty(title))
            return localFallback.HasLyrics ? localFallback : LyricsResult.Empty;

        cancellationToken.ThrowIfCancellationRequested();

        // Offline mode: skip all network providers
        if (AudioAuditorSettings.OfflineMode)
            return localFallback.HasLyrics ? localFallback : LyricsResult.Empty;

        var onlineProviders = BuildOnlineProviderRequests(
            preferred,
            artist,
            title,
            album,
            durationSeconds,
            hint,
            forceVersion.HasValue);

        return await LyricLookupPolicy.ResolveAsync(
            localFallback,
            onlineProviders,
            durationSeconds,
            AudioAuditorSettings.AvoidCensoredLyrics,
            cancellationToken).ConfigureAwait(false);
    }

    public static bool IsClearlyMismatchedTimedLyrics(LyricsResult result, double trackDurationSeconds)
    {
        if (!result.HasLyrics || !result.IsTimed || trackDurationSeconds <= 30)
            return false;

        var times = result.Lines
            .Select(line => line.Time.TotalSeconds)
            .Where(seconds => seconds >= 0)
            .OrderBy(seconds => seconds)
            .ToList();

        if (times.Count < 2)
            return false;

        double first = times[0];
        double last = times[^1];
        if (last <= 0)
            return false;

        double tailTolerance = Math.Clamp(trackDurationSeconds * 0.08, 8, 24);
        if (last > trackDurationSeconds + tailTolerance)
            return true;

        bool longTrack = trackDurationSeconds >= 90;
        bool endsFarTooEarly = last < trackDurationSeconds * 0.45
                               && trackDurationSeconds - last > 60;
        if (longTrack && endsFarTooEarly)
            return true;

        bool startsAfterLikelyVerse = first > 45 && first > trackDurationSeconds * 0.35;
        return longTrack && startsAfterLikelyVerse && last < trackDurationSeconds * 0.75;
    }

    /// <summary>
    /// Heuristic: detects lyrics that have been censored with strings of asterisks/hashes
    /// (e.g. <c>"f***"</c>, <c>"sh**"</c>, <c>"###"</c>). Used by <see cref="GetLyricsAsync"/>
    /// to skip censored results when <see cref="AudioAuditorSettings.AvoidCensoredLyrics"/> is on.
    /// Returns true only when repeated runs appear or censoring covers a meaningful share of text.
    /// </summary>
    public static bool IsLikelyCensored(LyricsResult result)
    {
        if (result.Lines == null || result.Lines.Count == 0) return false;
        int totalChars = 0;
        int censorChars = 0;
        int runCount = 0;
        foreach (var line in result.Lines)
        {
            string t = line.Text ?? "";
            totalChars += t.Length;
            int run = 0;
            foreach (char c in t)
            {
                if (c == '*' || c == '#')
                {
                    run++;
                    censorChars++;
                }
                else
                {
                    if (run >= 2) runCount++;
                    run = 0;
                }
            }
            if (run >= 2) runCount++;
        }
        if (runCount >= 6) return true;
        if (totalChars > 0 && censorChars >= 18 && censorChars * 100 > totalChars) return true;
        return false;
    }

    /// <summary>Load lyrics from a specific .lrc file path (e.g. drag-and-drop).</summary>
    public static LyricsResult LoadFromLrcFile(string lrcPath)
    {
        if (!File.Exists(lrcPath)) return LyricsResult.Empty;
        var result = LrcParser.ParseFile(lrcPath);
        return result.HasLyrics ? result with { Source = $"file: {Path.GetFileName(lrcPath)}" } : result;
    }

    /// <summary>Search LRCLIB for lyrics by artist + title.</summary>
    public static async Task<List<LrcLibSearchResult>> SearchLrcLibAsync(string artist, string title)
        => await SearchLrcLibAsync(artist, title, CancellationToken.None);

    public static async Task<List<LrcLibSearchResult>> SearchLrcLibAsync(string artist, string title, CancellationToken cancellationToken)
    {
        var results = new List<LrcLibSearchResult>();
        try
        {
            var url = $"https://lrclib.net/api/search?artist_name={Uri.EscapeDataString(artist)}&track_name={Uri.EscapeDataString(title)}";
            var response = await Http.GetAsync(url, cancellationToken);
            if (!response.IsSuccessStatusCode) return results;

            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            using var doc = JsonDocument.Parse(json);
            foreach (var item in doc.RootElement.EnumerateArray())
            {
                results.Add(new LrcLibSearchResult
                {
                    Id = item.TryGetProperty("id", out var id) ? id.GetInt64() : 0,
                    TrackName = item.TryGetProperty("trackName", out var tn) ? tn.GetString() ?? "" : "",
                    ArtistName = item.TryGetProperty("artistName", out var an) ? an.GetString() ?? "" : "",
                    AlbumName = item.TryGetProperty("albumName", out var aln) ? aln.GetString() ?? "" : "",
                    Duration = item.TryGetProperty("duration", out var dur) ? dur.GetDouble() : 0,
                    HasSyncedLyrics = item.TryGetProperty("syncedLyrics", out var sl) && sl.ValueKind == JsonValueKind.String && !string.IsNullOrEmpty(sl.GetString()),
                    HasPlainLyrics = item.TryGetProperty("plainLyrics", out var pl) && pl.ValueKind == JsonValueKind.String && !string.IsNullOrEmpty(pl.GetString()),
                    SyncedLyrics = item.TryGetProperty("syncedLyrics", out var sl2) && sl2.ValueKind == JsonValueKind.String ? sl2.GetString() : null,
                    PlainLyrics = item.TryGetProperty("plainLyrics", out var pl2) && pl2.ValueKind == JsonValueKind.String ? pl2.GetString() : null,
                });
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[LyricService.SearchLrcLib] {ex.GetType().Name}: {ex.Message}");
        }
        return results;
    }

    /// <summary>Apply a search result as the current lyrics.</summary>
    public static LyricsResult ApplySearchResult(LrcLibSearchResult result, double trackDurationSeconds = 0)
    {
        if (result.HasSyncedLyrics && !string.IsNullOrEmpty(result.SyncedLyrics))
        {
            var parsed = LrcParser.Parse(result.SyncedLyrics);
            if (parsed.HasLyrics && !IsClearlyMismatchedTimedLyrics(parsed, trackDurationSeconds))
                return parsed with { Source = "LRCLIB (synced)" };
        }
        if (result.HasPlainLyrics && !string.IsNullOrEmpty(result.PlainLyrics))
            return BuildPlainLyricsResult(result.PlainLyrics, "LRCLIB (plain)");
        return LyricsResult.Empty;
    }

    private static LyricsResult BuildPlainLyricsResult(string lyrics, string source)
    {
        var lines = lyrics.Split('\n')
            .Select(l => l.Trim())
            .Where(l => !string.IsNullOrEmpty(l))
            .Select(l => new LyricLine(TimeSpan.Zero, l))
            .ToList();
        return new LyricsResult { Lines = lines, IsTimed = false, Source = source };
    }

    private static IReadOnlyList<LyricProviderRequest> BuildOnlineProviderRequests(
        LyricProvider preferred,
        string artist,
        string title,
        string? album,
        double duration,
        LyricVersionHint hint,
        bool skipExact)
    {
        var providers = new List<LyricProviderRequest>();

        void AddLrcLib()
        {
            if (!skipExact)
            {
                providers.Add(new LyricProviderRequest(
                    "LRCLIB exact",
                    ct => FetchFromLrcLibExact(artist, title, album, duration, hint, ct),
                    TimeSpan.FromSeconds(5)));
            }

            providers.Add(new LyricProviderRequest(
                "LRCLIB search",
                ct => FetchFromLrcLibSearch(artist, title, album, duration, hint, ct),
                TimeSpan.FromSeconds(5)));
        }

        switch (preferred)
        {
            case LyricProvider.LrcLib:
                AddLrcLib();
                break;
            case LyricProvider.Netease:
                providers.Add(new LyricProviderRequest(
                    "Netease",
                    ct => FetchFromNetease(artist, title, hint, ct),
                    TimeSpan.FromSeconds(5)));
                break;
            case LyricProvider.Musixmatch:
                providers.Add(new LyricProviderRequest(
                    "Musixmatch",
                    ct => FetchFromMusixmatch(artist, title, ct),
                    TimeSpan.FromSeconds(5)));
                break;
            default:
                AddLrcLib();
                providers.Add(new LyricProviderRequest(
                    "Netease",
                    ct => FetchFromNetease(artist, title, hint, ct),
                    TimeSpan.FromSeconds(5)));
                break;
        }

        return providers;
    }

    private static async Task<LyricsResult> FetchFromLrcLibExact(string artist, string title, string? album,
        double duration, LyricVersionHint hint, CancellationToken cancellationToken)
    {
        try
        {
            var url = $"https://lrclib.net/api/get?artist_name={Uri.EscapeDataString(artist)}&track_name={Uri.EscapeDataString(title)}";
            if (!string.IsNullOrEmpty(album))
                url += $"&album_name={Uri.EscapeDataString(album)}";
            if (duration > 0)
                url += $"&duration={duration:F0}";

            var response = await Http.GetAsync(url, cancellationToken);
            if (!response.IsSuccessStatusCode)
                return LyricsResult.Empty;

            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            string candidateTitle = root.TryGetProperty("trackName", out var tnEl) && tnEl.ValueKind == JsonValueKind.String
                ? (tnEl.GetString() ?? title)
                : title;

            if (hint != LyricVersionHint.Unknown && ContradictsHint(candidateTitle, hint))
                return LyricsResult.Empty;

            var exactResult = new LrcLibSearchResult
            {
                TrackName = candidateTitle,
                ArtistName = root.TryGetProperty("artistName", out var anEl) && anEl.ValueKind == JsonValueKind.String
                    ? anEl.GetString() ?? artist
                    : artist,
                AlbumName = root.TryGetProperty("albumName", out var alEl) && alEl.ValueKind == JsonValueKind.String
                    ? alEl.GetString() ?? album ?? ""
                    : album ?? "",
                Duration = root.TryGetProperty("duration", out var durEl) && durEl.TryGetDouble(out var exactDuration)
                    ? exactDuration
                    : duration,
                HasSyncedLyrics = root.TryGetProperty("syncedLyrics", out var exactSynced) &&
                    exactSynced.ValueKind == JsonValueKind.String &&
                    !string.IsNullOrEmpty(exactSynced.GetString()),
                HasPlainLyrics = root.TryGetProperty("plainLyrics", out var exactPlain) &&
                    exactPlain.ValueKind == JsonValueKind.String &&
                    !string.IsNullOrEmpty(exactPlain.GetString()),
                SyncedLyrics = root.TryGetProperty("syncedLyrics", out var syncedValue) &&
                    syncedValue.ValueKind == JsonValueKind.String
                        ? syncedValue.GetString()
                        : null,
                PlainLyrics = root.TryGetProperty("plainLyrics", out var plainValue) &&
                    plainValue.ValueKind == JsonValueKind.String
                        ? plainValue.GetString()
                        : null,
            };

            return ApplySearchResult(exactResult, duration);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[LyricService.LrcLibExact] {ex.GetType().Name}: {ex.Message}");
        }

        return LyricsResult.Empty;
    }

    private static async Task<LyricsResult> FetchFromLrcLibSearch(string artist, string title, string? album,
        double duration, LyricVersionHint hint, CancellationToken cancellationToken)
    {
        try
        {
            var searchResults = await SearchLrcLibAsync(artist, title, cancellationToken);
            if (searchResults.Count > 0)
            {
                var ranked = searchResults
                    .Select(r => new
                    {
                        Result = r,
                        Score = RankCandidate(r.TrackName, r.AlbumName, r.Duration, album, duration, hint)
                                + (r.HasSyncedLyrics ? 10 : 0)
                                + (r.HasPlainLyrics ? 2 : 0)
                    })
                    .OrderByDescending(x => x.Score)
                    .ToList();

                foreach (var candidate in ranked.Select(x => x.Result))
                {
                    var applied = ApplySearchResult(candidate, duration);
                    if (!applied.HasLyrics)
                        continue;
                    if (!IsClearlyMismatchedTimedLyrics(applied, duration))
                        return applied;
                }
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[LyricService.LrcLibSearch] {ex.GetType().Name}: {ex.Message}");
        }

        return LyricsResult.Empty;
    }

    /// <summary>
    /// Detects whether a track is Explicit/Clean/Radio Edit/Album Version from TagLib metadata.
    /// Order: title substring → ID3v2 ITNU frame → MP4 rtng atom.
    /// </summary>
    internal static LyricVersionHint DetectVersionHint(TagLib.File tagFile, string? title)
    {
        try
        {
            // 1. Title substring check
            if (!string.IsNullOrEmpty(title))
            {
                string t = title.ToLowerInvariant();
                if (t.Contains("(explicit)") || t.Contains("[explicit]")) return LyricVersionHint.Explicit;
                if (t.Contains("(clean)") || t.Contains("[clean]")) return LyricVersionHint.Clean;
                if (t.Contains("(radio edit)") || t.Contains("[radio edit]") || t.Contains("(radio version)")) return LyricVersionHint.RadioEdit;
                if (t.Contains("(album version)") || t.Contains("[album version]")) return LyricVersionHint.AlbumVersion;
            }

            // 2. ID3v2 iTunesAdvisory frame (TXXX with description "iTunesAdvisory")
            var id3v2 = tagFile.GetTag(TagLib.TagTypes.Id3v2) as TagLib.Id3v2.Tag;
            if (id3v2 != null)
            {
                foreach (var frame in id3v2.GetFrames<TagLib.Id3v2.UserTextInformationFrame>())
                {
                    if (string.Equals(frame.Description, "iTunesAdvisory", StringComparison.OrdinalIgnoreCase)
                        && frame.Text != null && frame.Text.Length > 0)
                    {
                        if (frame.Text[0] == "1") return LyricVersionHint.Explicit;
                        if (frame.Text[0] == "2") return LyricVersionHint.Clean;
                    }
                }
            }

            // 3. MP4 rtng atom
            var mp4 = tagFile.GetTag(TagLib.TagTypes.Apple) as TagLib.Mpeg4.AppleTag;
            if (mp4 != null)
            {
                var rtng = mp4.GetText("rtng");
                if (rtng != null && rtng.Length > 0)
                {
                    if (rtng[0] == "1") return LyricVersionHint.Explicit;
                    if (rtng[0] == "2") return LyricVersionHint.Clean;
                }
            }
        }
        catch { }
        return LyricVersionHint.Unknown;
    }

    private static int RankCandidate(string candidateTitle, string? candidateAlbum, double candidateDuration,
        string? album, double duration, LyricVersionHint hint)
    {
        int score = 0;

        if (hint != LyricVersionHint.Unknown)
        {
            if (MatchesHint(candidateTitle, hint)) score += 100;
            if (ContradictsHint(candidateTitle, hint)) score -= 100;
        }

        if (!string.IsNullOrEmpty(album) && !string.IsNullOrEmpty(candidateAlbum)
            && string.Equals(album.Trim(), candidateAlbum.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            score += 50;
        }

        if (duration > 0 && candidateDuration > 0 && Math.Abs(duration - candidateDuration) <= 2.0)
        {
            score += 25;
        }

        return score;
    }

    private static bool MatchesHint(string title, LyricVersionHint hint)
    {
        if (string.IsNullOrEmpty(title)) return false;
        string t = title.ToLowerInvariant();
        return hint switch
        {
            LyricVersionHint.Explicit => t.Contains("explicit"),
            LyricVersionHint.Clean => t.Contains("clean"),
            LyricVersionHint.RadioEdit => t.Contains("radio edit") || t.Contains("radio version"),
            LyricVersionHint.AlbumVersion => t.Contains("album version"),
            _ => false
        };
    }

    private static bool ContradictsHint(string title, LyricVersionHint hint)
    {
        if (string.IsNullOrEmpty(title)) return false;
        string t = title.ToLowerInvariant();
        return hint switch
        {
            LyricVersionHint.Explicit => t.Contains("clean") || t.Contains("radio edit") || t.Contains("radio version"),
            LyricVersionHint.Clean => t.Contains("explicit"),
            LyricVersionHint.RadioEdit => t.Contains("album version"),
            LyricVersionHint.AlbumVersion => t.Contains("radio edit") || t.Contains("radio version"),
            _ => false
        };
    }

    /// <summary>Fetch lyrics from Netease Music (music.163.com) — free API, supports synced lyrics.</summary>
    private static async Task<LyricsResult> FetchFromNetease(string artist, string title, LyricVersionHint hint, CancellationToken cancellationToken)
    {
        // Search for the track (cloudsearch endpoint returns plain JSON; the legacy
        // /api/search/get/web endpoint now returns encrypted payloads)
        var searchUrl = $"https://music.163.com/api/cloudsearch/pc?s={Uri.EscapeDataString($"{artist} {title}")}&type=1&limit=5";
        var request = new HttpRequestMessage(HttpMethod.Get, searchUrl);
        request.Headers.Add("Referer", "https://music.163.com");
        var searchResp = await Http.SendAsync(request, cancellationToken);
        if (!searchResp.IsSuccessStatusCode) return LyricsResult.Empty;

        var searchJson = await searchResp.Content.ReadAsStringAsync(cancellationToken);
        using var searchDoc = JsonDocument.Parse(searchJson);

        if (!searchDoc.RootElement.TryGetProperty("result", out var result) ||
            !result.TryGetProperty("songs", out var songs))
            return LyricsResult.Empty;

        // Rank candidates by version hint so explicit/clean preference is respected.
        var ordered = new List<(JsonElement Song, int Score)>();
        foreach (var song in songs.EnumerateArray())
        {
            string songName = song.TryGetProperty("name", out var nm) && nm.ValueKind == JsonValueKind.String
                ? (nm.GetString() ?? "") : "";
            int score = 0;
            if (hint != LyricVersionHint.Unknown)
            {
                if (MatchesHint(songName, hint)) score += 100;
                if (ContradictsHint(songName, hint)) score -= 100;
            }
            ordered.Add((song, score));
        }
        ordered.Sort((a, b) => b.Score.CompareTo(a.Score));

        foreach (var (song, _) in ordered)
        {
            if (!song.TryGetProperty("id", out var idProp)) continue;
            long songId = idProp.GetInt64();

            // Fetch lyrics for this song ID
            var lyricUrl = $"https://music.163.com/api/song/lyric?id={songId}&lv=1&tv=1";
            var lyricReq = new HttpRequestMessage(HttpMethod.Get, lyricUrl);
            lyricReq.Headers.Add("Referer", "https://music.163.com");
            var lyricResp = await Http.SendAsync(lyricReq, cancellationToken);
            if (!lyricResp.IsSuccessStatusCode) continue;

            var lyricJson = await lyricResp.Content.ReadAsStringAsync(cancellationToken);
            using var lyricDoc = JsonDocument.Parse(lyricJson);

            // Try synced lyrics first (lrc property)
            if (lyricDoc.RootElement.TryGetProperty("lrc", out var lrc) &&
                lrc.TryGetProperty("lyric", out var lrcText) &&
                lrcText.ValueKind == JsonValueKind.String &&
                !string.IsNullOrWhiteSpace(lrcText.GetString()))
            {
                var parsed = LrcParser.Parse(lrcText.GetString()!);
                if (parsed.HasLyrics)
                    return parsed with { Source = "Netease (synced)" };
            }
        }

        return LyricsResult.Empty;
    }

    /// <summary>Fetch lyrics from Musixmatch — REMOVED: free API key revoked (401), only returned 30% of lyrics.</summary>
    private static async Task<LyricsResult> FetchFromMusixmatch(string artist, string title, CancellationToken cancellationToken)
    {
        // Musixmatch matcher API required a valid API key. The previously hardcoded
        // free-tier key has been revoked (returns 401). Even when it worked,
        // the free tier only returned ~30% of lyrics with a disclaimer footer.
        // Keeping this stub so the enum value doesn't break existing callers.
        await Task.CompletedTask.WaitAsync(cancellationToken);
        return LyricsResult.Empty;
    }

    private static Func<LyricsResult>[] BuildLocalProviders(string audioFilePath, LyricProvider preferred)
    {
        return preferred switch
        {
            LyricProvider.LrcFile => new Func<LyricsResult>[] { () => TryLrcFile(audioFilePath), () => EmbeddedLyricsProvider.Extract(audioFilePath) },
            LyricProvider.Embedded => new Func<LyricsResult>[] { () => EmbeddedLyricsProvider.Extract(audioFilePath), () => TryLrcFile(audioFilePath) },
            _ => new Func<LyricsResult>[] { () => TryLrcFile(audioFilePath), () => EmbeddedLyricsProvider.Extract(audioFilePath) }
        };
    }

    private static LyricsResult TryLrcFile(string audioFilePath)
    {
        var dir = Path.GetDirectoryName(audioFilePath);
        var nameNoExt = Path.GetFileNameWithoutExtension(audioFilePath);
        if (dir == null || nameNoExt == null) return LyricsResult.Empty;

        var lrcPath = Path.Combine(dir, nameNoExt + ".lrc");
        if (File.Exists(lrcPath))
            return LrcParser.ParseFile(lrcPath);

        try
        {
            var candidates = Directory.GetFiles(dir, nameNoExt + ".lrc", SearchOption.TopDirectoryOnly);
            if (candidates.Length == 0)
                candidates = Directory.GetFiles(dir, nameNoExt + ".LRC", SearchOption.TopDirectoryOnly);
            if (candidates.Length > 0)
                return LrcParser.ParseFile(candidates[0]);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[LyricService.localFallback] {ex.GetType().Name}: {ex.Message}");
        }

        return LyricsResult.Empty;
    }
}

/// <summary>A search result from LRCLIB.</summary>
public class LrcLibSearchResult
{
    public long Id { get; set; }
    public string TrackName { get; set; } = "";
    public string ArtistName { get; set; } = "";
    public string AlbumName { get; set; } = "";
    public double Duration { get; set; }
    public bool HasSyncedLyrics { get; set; }
    public bool HasPlainLyrics { get; set; }
    public string? SyncedLyrics { get; set; }
    public string? PlainLyrics { get; set; }

    public string Display => $"{ArtistName} — {TrackName}" +
                             (string.IsNullOrEmpty(AlbumName) ? "" : $" ({AlbumName})") +
                             (HasSyncedLyrics ? " [synced]" : HasPlainLyrics ? " [plain]" : "");
}
