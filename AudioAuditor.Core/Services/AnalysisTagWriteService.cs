using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AudioQualityChecker.Models;

namespace AudioQualityChecker.Services;

/// <summary>The grouping a writable analysis field belongs to (drives the dialog layout).</summary>
public enum AnalysisFieldCategory
{
    CoreQuality,
    Detections,
    Musical,
    Other
}

/// <summary>
/// One writable analysis metric: a stable tag key, a short label (used in the dialog and the
/// Comment summary), its category, and an extractor that turns an <see cref="AudioFileInfo"/> into
/// the value to write (or null/empty when that file has no value for the metric, so it's skipped).
/// </summary>
public sealed record AnalysisFieldDef(
    string Key,
    string Label,
    AnalysisFieldCategory Category,
    Func<AudioFileInfo, string?> Extract);

public sealed class AnalysisTagWriteOptions
{
    /// <summary>Keys (see <see cref="AnalysisTagWriteService.Fields"/>) the user chose to write.</summary>
    public HashSet<string> Fields { get; } = new(StringComparer.OrdinalIgnoreCase);
    public bool WriteCustomTags { get; set; } = true;
    public bool WriteCommentSummary { get; set; } = true;
    public bool CreateBackups { get; set; }
}

public sealed class AnalysisTagWriteSummary
{
    public int FilesChanged { get; set; }
    public int FilesSkipped { get; set; }   // nothing to write for this file
    public int FailedFiles { get; set; }
    public List<string> Errors { get; } = new();
}

/// <summary>
/// Writes AudioAuditor's measured/derived analysis values back into the audio files: one dedicated
/// custom tag per metric (ID3v2 TXXX for MP3, Vorbis comments for FLAC/OGG/Opus, APE values, and
/// <c>----</c> atoms for M4A/ALAC) plus a single human-readable "AudioAuditor:" line in the Comment
/// field. Mirrors the TagLib write + backup pattern used elsewhere (see ReplayGain in
/// <c>AudioAnalyzer</c> and <see cref="BatchFieldEditService"/>).
/// </summary>
public sealed class AnalysisTagWriteService
{
    private const string CommentPrefix = "AudioAuditor:";
    private const string AppleMean = "com.audioauditor";

    /// <summary>
    /// Every numeric formatter below is explicitly InvariantCulture. These strings go into TXXX /
    /// Vorbis / APE / Apple "----" atoms that other tools — and AudioAuditor's own re-read — parse
    /// as invariant. On a comma-decimal machine the culture-sensitive versions wrote "-14,0 LUFS"
    /// and "+3,25 dB"; ReplayGain especially is a cross-tool interchange value.
    /// </summary>
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    /// <summary>All writable fields, in display order. The dialog enumerates this catalog.</summary>
    public static readonly IReadOnlyList<AnalysisFieldDef> Fields = new List<AnalysisFieldDef>
    {
        // ── Core quality ──
        new("AUDIOAUDITOR_ACTUAL_BITRATE", "Actual bitrate", AnalysisFieldCategory.CoreQuality,
            f => f.ActualBitrate > 0 ? $"{f.ActualBitrate} kbps" : null),
        new("AUDIOAUDITOR_STATED_BITRATE", "Stated bitrate", AnalysisFieldCategory.CoreQuality,
            f => f.ReportedBitrate > 0 ? $"{f.ReportedBitrate} kbps" : null),
        new("AUDIOAUDITOR_LUFS", "LUFS", AnalysisFieldCategory.CoreQuality,
            f => f.HasLufs ? f.IntegratedLufs.ToString("F1", Inv) + " LUFS" : null),
        new("AUDIOAUDITOR_TRUE_PEAK", "True peak", AnalysisFieldCategory.CoreQuality,
            f => f.HasTruePeak ? f.TruePeakDbTP.ToString("F1", Inv) + " dBTP" : null),
        new("AUDIOAUDITOR_DYNAMIC_RANGE", "Dynamic range", AnalysisFieldCategory.CoreQuality,
            f => f.HasDynamicRange ? "DR" + f.DynamicRange.ToString("F0", Inv) : null),
        new("AUDIOAUDITOR_SAMPLE_RATE", "Sample rate", AnalysisFieldCategory.CoreQuality,
            f => f.SampleRate > 0 ? $"{f.SampleRate} Hz" : null),
        new("AUDIOAUDITOR_BIT_DEPTH", "Bit depth", AnalysisFieldCategory.CoreQuality,
            f => f.BitsPerSample > 0 ? $"{f.BitsPerSample}-bit" : null),
        new("AUDIOAUDITOR_FREQ_CUTOFF", "Frequency cutoff", AnalysisFieldCategory.CoreQuality,
            f => f.EffectiveFrequency > 0 ? $"{f.EffectiveFrequency} Hz" : null),

        // ── Detections ──
        new("AUDIOAUDITOR_MQA", "MQA", AnalysisFieldCategory.Detections,
            f => f.IsMqa ? f.MqaDisplay : null),
        new("AUDIOAUDITOR_AI", "AI", AnalysisFieldCategory.Detections,
            f => f.IsAnyAiDetected ? f.AiDisplay : null),
        new("AUDIOAUDITOR_FAKE_STEREO", "Fake stereo", AnalysisFieldCategory.Detections,
            f => f.IsFakeStereo ? f.FakeStereoDisplay : null),
        new("AUDIOAUDITOR_CLIPPING", "Clipping", AnalysisFieldCategory.Detections,
            f => (f.HasClipping || f.HasScaledClipping) ? f.ClippingDisplay : null),
        new("AUDIOAUDITOR_RIP_LOG", "Rip log", AnalysisFieldCategory.Detections,
            f => f.HasRipLog ? f.RipLogDisplay : null),

        // ── Musical ──
        new("AUDIOAUDITOR_BPM", "BPM", AnalysisFieldCategory.Musical,
            f => f.Bpm > 0 ? f.Bpm.ToString(Inv) : null),
        new("AUDIOAUDITOR_REPLAY_GAIN", "ReplayGain", AnalysisFieldCategory.Musical,
            f => f.HasReplayGain ? f.ReplayGain.ToString("+0.00;-0.00;0.00", Inv) + " dB" : null),

        // ── Other ──
        new("AUDIOAUDITOR_CHANNELS", "Channels", AnalysisFieldCategory.Other,
            f => f.Channels > 0 ? f.ChannelsDisplay : null),
        // Correlation is a Pearson coefficient (-1…1). Testing "> 0" would drop every out-of-phase
        // file, which is the case worth recording. Exact 0.0 doubles as "not measured".
        new("AUDIOAUDITOR_STEREO_CORRELATION", "Stereo correlation", AnalysisFieldCategory.Other,
            f => f.StereoCorrelation != 0 ? f.StereoCorrelation.ToString("F2", Inv) : null),
    };

    public async Task<AnalysisTagWriteSummary> ApplyAsync(
        IReadOnlyList<AudioFileInfo> files,
        AnalysisTagWriteOptions options,
        IProgress<(int done, int total, string fileName)>? progress = null,
        CancellationToken ct = default)
    {
        var summary = new AnalysisTagWriteSummary();
        var selected = Fields.Where(f => options.Fields.Contains(f.Key)).ToList();
        if (selected.Count == 0 || (!options.WriteCustomTags && !options.WriteCommentSummary))
            return summary;

        await Task.Run(() =>
        {
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

                // Resolve the values for this file once; skip files that have nothing to write.
                var values = selected
                    .Select(def => (def, val: def.Extract(file)))
                    .Where(x => !string.IsNullOrWhiteSpace(x.val))
                    .ToList();
                if (values.Count == 0)
                {
                    summary.FilesSkipped++;
                    continue;
                }

                try
                {
                    if (options.CreateBackups)
                        FileRenamer.CreateBackup(file.FilePath);

                    using var tagFile = TagLib.File.Create(file.FilePath);

                    if (options.WriteCustomTags)
                        foreach (var (def, val) in values)
                            SetCustomField(tagFile, def.Key, val!);

                    if (options.WriteCommentSummary)
                    {
                        string line = CommentPrefix + " " +
                            string.Join(" | ", values.Select(x => $"{x.def.Label}={x.val}"));
                        tagFile.Tag.Comment = MergeCommentSummary(tagFile.Tag.Comment, line);
                    }

                    tagFile.Save();
                    summary.FilesChanged++;
                }
                catch (Exception ex)
                {
                    summary.FailedFiles++;
                    summary.Errors.Add($"{file.FileName}: {ex.Message}");
                }
            }

            progress?.Report((files.Count, files.Count, ""));
        }, ct);

        return summary;
    }

    /// <summary>
    /// Order matters for the create-a-tag fallback below: Xiph and Apple come first because only
    /// Ogg/FLAC and MP4 respectively accept them, so an untagged file lands on its container's
    /// native tag. Id3v2 is last-but-one because FLAC and WAV will happily create one otherwise.
    /// The read pass writes to every type present, so the order is irrelevant there.
    /// </summary>
    private static readonly TagLib.TagTypes[] CustomFieldTagTypes =
    {
        TagLib.TagTypes.Xiph, TagLib.TagTypes.Apple, TagLib.TagTypes.Id3v2, TagLib.TagTypes.Ape
    };

    /// <summary>
    /// Writes a custom key/value into the tags the file already carries, creating one only when the
    /// file has none of them. Asking TagLib to create every type would add an APE tag to every MP3
    /// and an ID3v2 + APE tag to every FLAC — containers accept far more tag types than they
    /// normally carry, and that cruft is exactly what the Metadata Strip tool exists to remove.
    /// </summary>
    private static void SetCustomField(TagLib.File tagFile, string key, string value)
    {
        bool wroteAny = false;
        foreach (var type in CustomFieldTagTypes)
            wroteAny |= TryWrite(tagFile.GetTag(type, false), key, value);

        if (wroteAny) return;

        // Untagged file: create the one tag type native to this container.
        foreach (var type in CustomFieldTagTypes)
            if (TryWrite(tagFile.GetTag(type, true), key, value))
                return;
        // Formats with none of the above still get the Comment summary (handled by the caller).
    }

    /// <summary>Writes the pair into <paramref name="tag"/>; false when it is null or an unhandled type.</summary>
    private static bool TryWrite(TagLib.Tag? tag, string key, string value)
    {
        switch (tag)
        {
            case TagLib.Id3v2.Tag id3: SetOrAddTxxx(id3, key, value); return true;
            case TagLib.Ogg.XiphComment xiph: xiph.SetField(key, value); return true;
            case TagLib.Ape.Tag ape: ape.SetValue(key, value); return true;
            case TagLib.Mpeg4.AppleTag apple: apple.SetDashBox(AppleMean, key, value); return true;
            default: return false;
        }
    }

    private static void SetOrAddTxxx(TagLib.Id3v2.Tag id3, string description, string value)
    {
        foreach (var frame in id3.GetFrames<TagLib.Id3v2.UserTextInformationFrame>().ToArray())
        {
            if (frame.Description != null &&
                frame.Description.Equals(description, StringComparison.OrdinalIgnoreCase))
                id3.RemoveFrame(frame);
        }
        id3.AddFrame(new TagLib.Id3v2.UserTextInformationFrame(description)
        {
            Text = new[] { value },
            TextEncoding = TagLib.StringType.UTF8
        });
    }

    /// <summary>
    /// Returns the comment with a single fresh AudioAuditor summary line: any existing
    /// AudioAuditor: line is removed first (so re-runs replace rather than stack) and the user's
    /// own comment text is preserved. Pure/static so it's trivially verifiable. <see cref="SelfCheck"/>.
    /// </summary>
    public static string MergeCommentSummary(string? existing, string summaryLine)
    {
        var kept = (existing ?? "")
            .Replace("\r\n", "\n").Replace('\r', '\n')
            .Split('\n')
            .Where(l => !l.TrimStart().StartsWith(CommentPrefix, StringComparison.OrdinalIgnoreCase))
            .ToList();

        // Drop trailing blank lines left behind so we don't accumulate whitespace on re-runs.
        while (kept.Count > 0 && string.IsNullOrWhiteSpace(kept[^1]))
            kept.RemoveAt(kept.Count - 1);

        kept.Add(summaryLine);
        return string.Join("\n", kept);
    }

    /// <summary>Assert-based check for the non-trivial comment-merge branch (no test framework in repo).</summary>
    public static void SelfCheck()
    {
        // Empty comment → just the summary line.
        Trace(MergeCommentSummary("", "AudioAuditor: LUFS=-14.0") == "AudioAuditor: LUFS=-14.0");
        // User comment preserved, summary appended.
        Trace(MergeCommentSummary("my note", "AudioAuditor: DR=DR9") == "my note\nAudioAuditor: DR=DR9");
        // Re-run replaces the old AudioAuditor line, keeps the user note (no stacking).
        Trace(MergeCommentSummary("my note\nAudioAuditor: old", "AudioAuditor: new") == "my note\nAudioAuditor: new");

        static void Trace(bool ok) { if (!ok) throw new Exception("AnalysisTagWriteService.SelfCheck failed"); }
    }
}
