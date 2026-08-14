using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;

namespace AudioQualityChecker.Services;

/// <summary>
/// Serialises a <see cref="LyricsResult"/> back out as an .lrc sidecar — the write half of
/// <see cref="LrcParser"/>. Both UI builds save lyrics next to the audio file, so the formatting
/// lives here rather than in either window's code-behind.
///
/// Every number is written with <see cref="CultureInfo.InvariantCulture"/>. A comma-decimal
/// locale would otherwise emit timestamps this app's own parser cannot read back.
/// </summary>
public static class LrcWriter
{
    /// <summary>Collapses runs of whitespace, matching what the WPF build writes.</summary>
    private static readonly Regex Whitespace = new(@"\s+", RegexOptions.Compiled);

    /// <summary>The .lrc path for an audio file: same directory, same stem.</summary>
    public static string SidecarPath(string audioFilePath)
    {
        if (string.IsNullOrWhiteSpace(audioFilePath))
            throw new ArgumentException("Audio file path is required.", nameof(audioFilePath));

        string directory = Path.GetDirectoryName(audioFilePath) ?? "";
        return Path.Combine(directory, Path.GetFileNameWithoutExtension(audioFilePath) + ".lrc");
    }

    /// <summary>
    /// Formats lyrics as .lrc lines: [ti:]/[ar:]/[al:] tags for whatever metadata is present,
    /// then one line per lyric — <c>[mm:ss.cc]text</c> when timed, bare text when not.
    /// </summary>
    public static List<string> FormatLines(LyricsResult lyrics)
    {
        var lines = new List<string>();
        if (lyrics is null) return lines;

        if (!string.IsNullOrEmpty(lyrics.Title)) lines.Add($"[ti:{lyrics.Title}]");
        if (!string.IsNullOrEmpty(lyrics.Artist)) lines.Add($"[ar:{lyrics.Artist}]");
        if (!string.IsNullOrEmpty(lyrics.Album)) lines.Add($"[al:{lyrics.Album}]");

        foreach (var line in lyrics.Lines)
        {
            string text = Whitespace.Replace(line.Text ?? "", " ").Trim();
            lines.Add(lyrics.IsTimed ? FormatTimestamp(line.Time) + text : text);
        }

        return lines;
    }

    /// <summary>The whole file as one string, newline-separated.</summary>
    public static string Format(LyricsResult lyrics) =>
        string.Join(Environment.NewLine, FormatLines(lyrics));

    /// <summary>
    /// Writes the sidecar next to <paramref name="audioFilePath"/>. Returns the path written,
    /// or null when there was nothing to write.
    /// </summary>
    public static string? Save(LyricsResult lyrics, string audioFilePath)
    {
        if (lyrics is null || !lyrics.HasLyrics) return null;

        string lrcPath = SidecarPath(audioFilePath);
        File.WriteAllLines(lrcPath, FormatLines(lyrics));
        return lrcPath;
    }

    /// <summary>
    /// <c>[mm:ss.cc]</c>. Minutes are not wrapped at 60 — LRC has no hour field, so a 75-minute
    /// timestamp is written as <c>[75:00.00]</c>, which is what LrcParser reads back.
    /// Negative times clamp to zero rather than emitting an unparseable "-01".
    /// </summary>
    private static string FormatTimestamp(TimeSpan time)
    {
        if (time < TimeSpan.Zero) time = TimeSpan.Zero;

        int minutes = (int)time.TotalMinutes;
        int seconds = time.Seconds;
        int centiseconds = time.Milliseconds / 10;

        return string.Format(CultureInfo.InvariantCulture, "[{0:D2}:{1:D2}.{2:D2}]",
            minutes, seconds, centiseconds);
    }
}
