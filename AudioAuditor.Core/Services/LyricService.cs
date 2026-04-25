using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace AudioQualityChecker.Services;

/// <summary>
/// Represents a single timed lyric line.
/// </summary>
public record LyricLine(TimeSpan Time, string Text);

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
        RegexOptions.Compiled);

    // Enhanced LRC word-level timestamps: <mm:ss.xx>
    private static readonly Regex WordTimeTagRegex = new(
        @"<(\d{1,3}):(\d{2})(?:[.:](\d{1,3}))?>",
        RegexOptions.Compiled);

    private static readonly Regex IdTagRegex = new(
        @"\[(\w+):(.+?)\]",
        RegexOptions.Compiled);

    /// <summary>
    /// Checks whether a block of text is likely LRC format vs. plain lyrics
    /// that happen to contain brackets. Requires at least 3 lines starting
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
                if (timedLines >= 3) return true;
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
            var line = rawLine.Trim();
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
                        int.TryParse(value, out offsetMs);
                        break;
                }
                continue;
            }

            // Parse time tags — one line can have multiple [mm:ss.xx] tags
            var timeMatches = TimeTagRegex.Matches(line);
            if (timeMatches.Count == 0) continue;

            // Strip all time tags to get the lyric text
            // Remove both line-level [mm:ss] and word-level <mm:ss> timestamps
            string text = TimeTagRegex.Replace(line, "");
            text = WordTimeTagRegex.Replace(text, "").Trim();
            if (string.IsNullOrEmpty(text)) continue;

            foreach (Match tm in timeMatches)
            {
                int min = int.Parse(tm.Groups[1].Value);
                int sec = int.Parse(tm.Groups[2].Value);
                int ms = 0;
                if (tm.Groups[3].Success)
                {
                    string msStr = tm.Groups[3].Value;
                    ms = msStr.Length switch
                    {
                        1 => int.Parse(msStr) * 100,
                        2 => int.Parse(msStr) * 10,
                        _ => int.Parse(msStr)
                    };
                }

                var time = TimeSpan.FromMilliseconds(min * 60000.0 + sec * 1000.0 + ms + offsetMs);
                if (time < TimeSpan.Zero) time = TimeSpan.Zero;
                lines.Add(new LyricLine(time, text));
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
        double durationSeconds = 0)
    {
        if (string.IsNullOrEmpty(audioFilePath) || !File.Exists(audioFilePath))
            return LyricsResult.Empty;

        bool isOnlineOnly = preferred == LyricProvider.LrcLib || preferred == LyricProvider.Netease || preferred == LyricProvider.Musixmatch;

        // Try local first
        var local = GetLyrics(audioFilePath, isOnlineOnly ? LyricProvider.Auto : preferred);
        if (local.HasLyrics && !isOnlineOnly)
            return local;

        // If preferred is specifically a local source and we didn't find anything, return empty
        if (preferred == LyricProvider.LrcFile || preferred == LyricProvider.Embedded)
            return local;

        // Read tags if not provided
        if (string.IsNullOrEmpty(artist) || string.IsNullOrEmpty(title)
            || string.IsNullOrEmpty(album) || durationSeconds <= 0)
        {
            try
            {
                using var tagFile = TagLib.File.Create(audioFilePath);
                if (tagFile?.Tag == null) throw new InvalidOperationException();
                artist ??= tagFile.Tag.FirstPerformer;
                title ??= tagFile.Tag.Title;
                album ??= tagFile.Tag.Album;
                if (durationSeconds <= 0)
                    durationSeconds = tagFile.Properties.Duration.TotalSeconds;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[LyricService.tagRead] {ex.GetType().Name}: {ex.Message}");
            }
        }

        if (string.IsNullOrEmpty(artist) || string.IsNullOrEmpty(title))
            return local.HasLyrics ? local : LyricsResult.Empty;

        // Offline mode: skip all network providers
        if (AudioAuditorSettings.OfflineMode)
            return local.HasLyrics ? local : LyricsResult.Empty;

        // Build the list of online providers to try
        var onlineProviders = preferred switch
        {
            LyricProvider.LrcLib => new Func<Task<LyricsResult>>[] { () => FetchFromLrcLib(artist, title, album, durationSeconds) },
            LyricProvider.Netease => new Func<Task<LyricsResult>>[] { () => FetchFromNetease(artist, title) },
            LyricProvider.Musixmatch => new Func<Task<LyricsResult>>[] { () => FetchFromMusixmatch(artist, title) },
            _ => new Func<Task<LyricsResult>>[]
            {
                () => FetchFromLrcLib(artist, title, album, durationSeconds),
                () => FetchFromNetease(artist, title),
            }
        };

        foreach (var provider in onlineProviders)
        {
            try
            {
                var result = await provider();
                if (result.HasLyrics) return result;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[LyricService.online] {ex.GetType().Name}: {ex.Message}");
            }
        }

        return local.HasLyrics ? local : LyricsResult.Empty;
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
    {
        var results = new List<LrcLibSearchResult>();
        try
        {
            var url = $"https://lrclib.net/api/search?artist_name={Uri.EscapeDataString(artist)}&track_name={Uri.EscapeDataString(title)}";
            var response = await Http.GetAsync(url);
            if (!response.IsSuccessStatusCode) return results;

            var json = await response.Content.ReadAsStringAsync();
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
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[LyricService.SearchLrcLib] {ex.GetType().Name}: {ex.Message}");
        }
        return results;
    }

    /// <summary>Apply a search result as the current lyrics.</summary>
    public static LyricsResult ApplySearchResult(LrcLibSearchResult result)
    {
        if (result.HasSyncedLyrics && !string.IsNullOrEmpty(result.SyncedLyrics))
        {
            var parsed = LrcParser.Parse(result.SyncedLyrics);
            if (parsed.HasLyrics)
                return parsed with { Source = "LRCLIB (synced)" };
        }
        if (result.HasPlainLyrics && !string.IsNullOrEmpty(result.PlainLyrics))
        {
            var lines = result.PlainLyrics.Split('\n')
                .Select(l => l.Trim())
                .Where(l => !string.IsNullOrEmpty(l))
                .Select(l => new LyricLine(TimeSpan.Zero, l))
                .ToList();
            return new LyricsResult { Lines = lines, IsTimed = false, Source = "LRCLIB (plain)" };
        }
        return LyricsResult.Empty;
    }

    private static async Task<LyricsResult> FetchFromLrcLib(string artist, string title, string? album, double duration)
    {
        try
        {
            // Try exact match first
            var url = $"https://lrclib.net/api/get?artist_name={Uri.EscapeDataString(artist)}&track_name={Uri.EscapeDataString(title)}";
            if (!string.IsNullOrEmpty(album))
                url += $"&album_name={Uri.EscapeDataString(album)}";
            if (duration > 0)
                url += $"&duration={duration:F0}";

            var response = await Http.GetAsync(url);
            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                // Prefer synced lyrics
                if (root.TryGetProperty("syncedLyrics", out var synced) &&
                    synced.ValueKind == JsonValueKind.String &&
                    !string.IsNullOrEmpty(synced.GetString()))
                {
                    var parsed = LrcParser.Parse(synced.GetString()!);
                    if (parsed.HasLyrics)
                        return parsed with { Source = "LRCLIB (synced)" };
                }

                // Fall back to plain
                if (root.TryGetProperty("plainLyrics", out var plain) &&
                    plain.ValueKind == JsonValueKind.String &&
                    !string.IsNullOrEmpty(plain.GetString()))
                {
                    var lines = plain.GetString()!.Split('\n')
                        .Select(l => l.Trim())
                        .Where(l => !string.IsNullOrEmpty(l))
                        .Select(l => new LyricLine(TimeSpan.Zero, l))
                        .ToList();
                    return new LyricsResult { Lines = lines, IsTimed = false, Source = "LRCLIB (plain)" };
                }
            }

            // Try search as fallback
            var searchResults = await SearchLrcLibAsync(artist, title);
            if (searchResults.Count > 0)
            {
                // Pick the first result with synced lyrics, or first with any lyrics
                var best = searchResults.FirstOrDefault(r => r.HasSyncedLyrics)
                           ?? searchResults.FirstOrDefault(r => r.HasPlainLyrics);
                if (best != null)
                    return ApplySearchResult(best);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[LyricService.LrcLib] {ex.GetType().Name}: {ex.Message}");
        }

        return LyricsResult.Empty;
    }

    /// <summary>Fetch lyrics from Netease Music (music.163.com) — free API, supports synced lyrics.</summary>
    private static async Task<LyricsResult> FetchFromNetease(string artist, string title)
    {
        // Search for the track (cloudsearch endpoint returns plain JSON; the legacy
        // /api/search/get/web endpoint now returns encrypted payloads)
        var searchUrl = $"https://music.163.com/api/cloudsearch/pc?s={Uri.EscapeDataString($"{artist} {title}")}&type=1&limit=5";
        var request = new HttpRequestMessage(HttpMethod.Get, searchUrl);
        request.Headers.Add("Referer", "https://music.163.com");
        var searchResp = await Http.SendAsync(request);
        if (!searchResp.IsSuccessStatusCode) return LyricsResult.Empty;

        var searchJson = await searchResp.Content.ReadAsStringAsync();
        using var searchDoc = JsonDocument.Parse(searchJson);

        if (!searchDoc.RootElement.TryGetProperty("result", out var result) ||
            !result.TryGetProperty("songs", out var songs))
            return LyricsResult.Empty;

        foreach (var song in songs.EnumerateArray())
        {
            if (!song.TryGetProperty("id", out var idProp)) continue;
            long songId = idProp.GetInt64();

            // Fetch lyrics for this song ID
            var lyricUrl = $"https://music.163.com/api/song/lyric?id={songId}&lv=1&tv=1";
            var lyricReq = new HttpRequestMessage(HttpMethod.Get, lyricUrl);
            lyricReq.Headers.Add("Referer", "https://music.163.com");
            var lyricResp = await Http.SendAsync(lyricReq);
            if (!lyricResp.IsSuccessStatusCode) continue;

            var lyricJson = await lyricResp.Content.ReadAsStringAsync();
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
    private static async Task<LyricsResult> FetchFromMusixmatch(string artist, string title)
    {
        // Musixmatch matcher API required a valid API key. The previously hardcoded
        // free-tier key has been revoked (returns 401). Even when it worked,
        // the free tier only returned ~30% of lyrics with a disclaimer footer.
        // Keeping this stub so the enum value doesn't break existing callers.
        await Task.CompletedTask;
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
