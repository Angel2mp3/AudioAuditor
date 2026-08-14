using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using AudioQualityChecker.Models;

namespace AudioQualityChecker.Services;

/// <summary>
/// What to strip in a clean-up pass. <see cref="RemoveAllComments"/> wipes the Comment field
/// outright; the Clean* flags run the junk detector on that field instead.
/// </summary>
public sealed class JunkCleanOptions
{
    public bool CleanComment { get; set; } = true;
    public bool CleanTitle { get; set; } = true;

    /// <summary>Wipe the Comment field entirely, regardless of junk detection. Overrides CleanComment.</summary>
    public bool RemoveAllComments { get; set; }

    public bool HasAnyAction => RemoveAllComments || CleanComment || CleanTitle;
}

/// <summary>One proposed field change for a file (used to preview a clean-up before writing).</summary>
public sealed class JunkCleanChange
{
    public AudioFileInfo File { get; init; } = new();
    public string FileName { get; init; } = "";
    public string Field { get; init; } = "";   // "Comment" or "Title"
    public string OldValue { get; init; } = "";
    public string NewValue { get; init; } = "";
}

public sealed class JunkCleanSummary
{
    public int FilesChanged;
    public int FieldsChanged;
    public int FailedFiles;
    public List<string> Errors { get; } = new();
}

/// <summary>
/// Removes download / source / sponsor junk ("downloaded from X", "ripped using X", bare website
/// names, promo calls-to-action) from free-text tag fields. Conservative by design: only matched
/// spans are removed — never the whole field — and a Title is never emptied by cleaning. The pure
/// <see cref="TryClean"/> transform is shared by the preview and the writer so what you see is what
/// gets written.
/// </summary>
public static class SourceJunkCleaner
{
    private const RegexOptions Opt =
        RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant;

    // Explicit URLs.
    private static readonly Regex UrlRegex = new(@"\b(?:https?://|www\.)\S+", Opt);

    // Bare domains, but only with a recognizable TLD so "Mr.Brightside" / "feat.Drake" survive.
    // Two guards keep dot-separated scene titles ("Back.To.Black", "Artist.Title.Live") intact:
    //   1. TLDs that are also ordinary words are excluded — to, co, me, de, info, live, music,
    //      media, pro, store, club, online, download.
    //   2. The match must end the dot-run: a following ".word" means this is a dotted title, not a
    //      host. A "/path" is accepted instead, since that only ever appears on a real URL.
    // Known limitation: a dotted title whose LAST word is a kept TLD ("The.Safety.Net") still
    // matches. If that ever bites, require a scheme or a path segment for bare domains.
    private static readonly Regex DomainRegex = new(
        @"\b[a-z0-9][a-z0-9\-]*(?:\.[a-z0-9\-]+)*\.(?:com|net|org|io|tv|fm|biz|ru|uk|fr|nl|pl|cc|xyz|app|link|site)\b(?:/\S*|(?!\.[a-z0-9]))",
        Opt);

    // "downloaded from X", "ripped using X", "extracted with X", etc. Verb + required connector,
    // then the source name up to the next sentence/separator boundary (so we don't eat unrelated
    // text that follows). Run after the domain pass so a removed domain doesn't truncate the match.
    private static readonly Regex SourcePhraseRegex = new(
        @"\b(?:downloaded|download|ripped|rip|extracted|extract|encoded|converted|grabbed|sourced)\b\s*(?:from|with|using|by|via|at|on|@)\b[^\r\n.,;:|•\-–—]*",
        Opt);

    // Promo calls-to-action.
    private static readonly Regex PromoRegex = new(
        @"\b(?:free\s+download|free\s+dl|visit|follow\s+us|subscribe|check\s+(?:us\s+)?out|join\s+us|like\s+and\s+share|for\s+more\s+(?:music|songs|tracks))\b[^\r\n.,;:|•\-–—]*",
        Opt);

    /// <summary>
    /// Returns true and the cleaned text when junk was removed. <paramref name="isTitle"/> guards
    /// against emptying a title. Whitespace-only differences do not count as a change.
    /// </summary>
    public static bool TryClean(string? value, bool isTitle, out string cleaned)
    {
        cleaned = value ?? "";
        if (string.IsNullOrWhiteSpace(value)) return false;

        string s = value;
        bool matched = false;
        s = StripIfMatch(UrlRegex, s, ref matched);
        s = StripIfMatch(DomainRegex, s, ref matched);
        // Promo before the source-phrase pass: "Free download at …" must match as a whole promo,
        // otherwise the phrase pass eats "download at" and leaves a stray "Free".
        s = StripIfMatch(PromoRegex, s, ref matched);
        s = StripIfMatch(SourcePhraseRegex, s, ref matched);

        // No junk pattern hit, so there is nothing to clean. Bailing out here matters: Tidy() strips
        // trailing punctuation, so running it on untouched text would turn "Great song." into
        // "Great song" and report that as a change, rewriting files that had no junk in them.
        if (!matched) return false;

        s = Tidy(s);

        if (isTitle && string.IsNullOrWhiteSpace(s))
        {
            cleaned = value;            // never wipe a title
            return false;
        }

        cleaned = s;
        return !string.Equals(s, value, StringComparison.Ordinal);
    }

    private static string StripIfMatch(Regex regex, string input, ref bool matched)
    {
        if (!regex.IsMatch(input)) return input;
        matched = true;
        return regex.Replace(input, " ");
    }

    private static string Tidy(string s)
    {
        s = Regex.Replace(s, @"[ \t]+", " ");
        s = Regex.Replace(s, @"[\(\[\{]\s*[\)\]\}]", " ");           // empty bracket pairs left behind
        s = Regex.Replace(s, @"(?:\s*[-–—|•]\s*){2,}", " - ");        // collapse separator runs
        s = Regex.Replace(s, @"[ \t]+", " ");
        return s.Trim(' ', '\t', '.', ',', ':', ';', '-', '–', '—', '|', '•');
    }

    /// <summary>Computes the proposed Comment/Title changes for each file (reads tags, no writes).</summary>
    public static IReadOnlyList<JunkCleanChange> BuildPreview(
        IReadOnlyList<AudioFileInfo> files, JunkCleanOptions options)
    {
        var changes = new List<JunkCleanChange>();
        if (!options.HasAnyAction) return changes;

        foreach (var file in files)
        {
            if (string.IsNullOrWhiteSpace(file.FilePath) || !File.Exists(file.FilePath)) continue;
            string comment, title;
            try
            {
                using var tagFile = TagLib.File.Create(file.FilePath);
                comment = tagFile.Tag.Comment ?? "";
                title = tagFile.Tag.Title ?? "";
            }
            catch { continue; }

            if (TryComputeChange(file, "Comment", comment, isTitle: false, options, out var cc)) changes.Add(cc);
            if (TryComputeChange(file, "Title", title, isTitle: true, options, out var tc)) changes.Add(tc);
        }
        return changes;
    }

    private static bool TryComputeChange(
        AudioFileInfo file, string field, string current, bool isTitle,
        JunkCleanOptions options, out JunkCleanChange change)
    {
        change = null!;
        string? next = null;

        if (field == "Comment")
        {
            if (options.RemoveAllComments)
                next = string.IsNullOrEmpty(current) ? null : "";
            else if (options.CleanComment && TryClean(current, isTitle: false, out var cleaned))
                next = cleaned;
        }
        else if (field == "Title" && options.CleanTitle && TryClean(current, isTitle: true, out var cleanedT))
        {
            next = cleanedT;
        }

        if (next == null) return false;
        change = new JunkCleanChange
        {
            File = file,
            FileName = file.FileName,
            Field = field,
            OldValue = current,
            NewValue = next
        };
        return true;
    }

    /// <summary>Applies the clean-up to disk. Recomputes deterministically so it matches the preview.</summary>
    public static async Task<JunkCleanSummary> ApplyAsync(
        IReadOnlyList<AudioFileInfo> files,
        JunkCleanOptions options,
        bool createBackups,
        IProgress<(int done, int total, string fileName)>? progress = null,
        CancellationToken ct = default)
    {
        var summary = new JunkCleanSummary();
        if (!options.HasAnyAction) return summary;

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
                await Task.Run(() =>
                {
                    using var tagFile = TagLib.File.Create(file.FilePath);
                    var tag = tagFile.Tag;
                    int fieldsBefore = summary.FieldsChanged;

                    string comment = tag.Comment ?? "";
                    string title = tag.Title ?? "";

                    string? newComment = null;
                    if (options.RemoveAllComments)
                        newComment = string.IsNullOrEmpty(comment) ? null : "";
                    else if (options.CleanComment && TryClean(comment, isTitle: false, out var cc))
                        newComment = cc;

                    string? newTitle = null;
                    if (options.CleanTitle && TryClean(title, isTitle: true, out var tc))
                        newTitle = tc;

                    if (newComment == null && newTitle == null) return;

                    if (createBackups) FileRenamer.CreateBackup(file.FilePath);

                    if (newComment != null)
                    {
                        tag.Comment = string.IsNullOrEmpty(newComment) ? null : newComment;
                        summary.FieldsChanged++;
                    }
                    if (newTitle != null)
                    {
                        tag.Title = newTitle;
                        summary.FieldsChanged++;
                        file.Title = newTitle;
                    }

                    tagFile.Save();
                    if (summary.FieldsChanged > fieldsBefore) summary.FilesChanged++;
                }, ct);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                summary.FailedFiles++;
                summary.Errors.Add($"{file.FileName}: {ex.Message}");
            }
        }

        progress?.Report((files.Count, files.Count, ""));
        return summary;
    }

    /// <summary>Assert-based check for the detection branches (no test framework in this repo).</summary>
    public static void SelfCheck()
    {
        // Clean text is never touched — Tidy() alone must not manufacture a change.
        Assert(!TryClean("Great song.", false, out _), "trailing period is not junk");
        Assert(!TryClean("Live at Wembley, 1985", false, out _), "ordinary comma text is not junk");
        Assert(!TryClean("  padded  ", false, out _), "whitespace-only difference is not a change");

        // Dot-separated titles survive: the TLD list excludes ordinary words, and a dot-run that
        // continues past the "TLD" is not a host.
        Assert(!TryClean("Back.To.Black", true, out var t1), "Back.To.Black must survive");
        Assert(t1 == "Back.To.Black", "Back.To.Black must be returned unmodified");
        Assert(!TryClean("Artist.Title.Live", true, out _), "a .Live suffix is not a domain");
        Assert(!TryClean("Mr.Brightside", true, out _), "Mr.Brightside must survive");

        // Real junk still goes.
        Assert(TryClean("Downloaded from example.com — enjoy", false, out var c1), "URL junk detected");
        Assert(c1 == "enjoy", $"expected 'enjoy', got '{c1}'");
        Assert(TryClean("https://rip.example.org/x My Song", false, out var c2), "explicit URL detected");
        Assert(c2 == "My Song", $"expected 'My Song', got '{c2}'");
        Assert(TryClean("Free download at somesite.net", false, out var c3), "promo detected");
        Assert(c3.Length == 0, $"expected empty, got '{c3}'");

        // A title is never emptied, even when the whole value is junk.
        Assert(!TryClean("somesite.net", true, out var t2), "a title made only of junk stays put");
        Assert(t2 == "somesite.net", "the original title must be returned unchanged");

        static void Assert(bool ok, string what)
        {
            if (!ok) throw new Exception("SourceJunkCleaner.SelfCheck failed: " + what);
        }
    }
}
