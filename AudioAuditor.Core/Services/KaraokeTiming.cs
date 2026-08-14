using System;
using System.Collections.Generic;

namespace AudioQualityChecker.Services;

/// <summary>
/// Works out how far through a karaoke word playback is, so the UI can light words up one at a
/// time. Two paths: real per-word timings when the provider supplied them
/// (<see cref="KaraokeWord"/>, Enhanced LRC), and a character-count estimate when only the line
/// start is known.
///
/// Pure maths, no UI — the caller maps the 0‥1 result onto a colour.
/// </summary>
public static class KaraokeTiming
{
    /// <summary>
    /// Audio-buffer lookahead. Output lags the reported position by roughly this much, so
    /// timings are read slightly ahead to keep the highlight on the beat.
    /// </summary>
    public static readonly TimeSpan Lookahead = TimeSpan.FromMilliseconds(200);

    /// <summary>Fallback duration for a word whose timings are missing or inverted.</summary>
    private static readonly TimeSpan DefaultWordDuration = TimeSpan.FromMilliseconds(500);

    /// <summary>Fallback duration for a line with no following line to bound it.</summary>
    public static readonly TimeSpan DefaultLineDuration = TimeSpan.FromSeconds(4);

    /// <summary>
    /// Progress through a word with known timings, 0 (not started) to 1 (fully lit).
    /// Scaled slightly past linear so a word reads as lit just before it finishes.
    /// </summary>
    public static double WordProgress(TimeSpan position, KaraokeWord word)
    {
        var lookAhead = position + Lookahead;

        double durationMs = (word.End - word.Start).TotalMilliseconds;
        if (durationMs <= 0) durationMs = DefaultWordDuration.TotalMilliseconds;

        double elapsedMs = (lookAhead - word.Start).TotalMilliseconds;
        return Math.Clamp(Math.Clamp(elapsedMs / durationMs, 0, 1) * 1.2, 0, 1);
    }

    /// <summary>
    /// Progress through word <paramref name="wordIndex"/> when only the line's start and end are
    /// known. Each word is given a share of the line proportional to its character count, and the
    /// highlight sweeps across that share.
    /// </summary>
    /// <param name="words">Every word on the line, in order.</param>
    public static double WordProgress(TimeSpan position, TimeSpan lineStart, TimeSpan lineEnd,
        int wordIndex, IReadOnlyList<string> words)
    {
        if (words is null || words.Count == 0) return 0;
        if (wordIndex < 0 || wordIndex >= words.Count) return 0;

        var lookAhead = position + Lookahead;

        double lineDurationMs = (lineEnd - lineStart).TotalMilliseconds;
        if (lineDurationMs <= 0) lineDurationMs = DefaultLineDuration.TotalMilliseconds;

        double progress = Math.Clamp((lookAhead - lineStart).TotalMilliseconds / lineDurationMs, 0, 1);

        // Each word occupies a slice of the line sized by its character count. An empty word
        // still counts as one character so it cannot collapse to a zero-width slice.
        int totalChars = 0;
        foreach (var word in words) totalChars += Math.Max(1, word?.Length ?? 1);

        double startFraction = 0;
        for (int i = 0; i < wordIndex; i++) startFraction += Math.Max(1, words[i]?.Length ?? 1);
        startFraction /= totalChars;

        double endFraction = startFraction + Math.Max(1, words[wordIndex]?.Length ?? 1) / (double)totalChars;
        if (wordIndex == words.Count - 1) endFraction = 1.0;

        double center = (startFraction + endFraction) / 2.0;

        // Widen the ramp on short lines so a two-word line still fades rather than snapping.
        double transitionWidth = Math.Max(0.05, 1.0 / words.Count);

        return Math.Clamp((progress - center + transitionWidth / 2) / transitionWidth, 0, 1);
    }

    /// <summary>
    /// Where a line ends: the next line's start, or <see cref="DefaultLineDuration"/> past its own
    /// start when it is the last line.
    /// </summary>
    public static TimeSpan LineEnd(IReadOnlyList<LyricLine> lines, int lineIndex)
    {
        if (lines is null || lineIndex < 0 || lineIndex >= lines.Count) return TimeSpan.Zero;

        return lineIndex + 1 < lines.Count
            ? lines[lineIndex + 1].Time
            : lines[lineIndex].Time + DefaultLineDuration;
    }
}
