using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using AudioQualityChecker.Models;

namespace AudioQualityChecker.Services;

public enum PastedMetadataKind
{
    Empty,
    SingleBlock,
    Tracklist,
    Csv
}

/// <summary>
/// Result of parsing a pasted blob: a set of reviewable <see cref="MetadataEnrichmentChange"/> rows
/// (so the existing apply pipeline / review grid can be reused), plus a short human summary.
/// </summary>
public sealed class PastedMetadataResult
{
    public PastedMetadataKind Kind { get; set; }
    public List<MetadataEnrichmentChange> Changes { get; } = new();
    /// <summary>Source→target file pairs whose cover art should be copied (folder-transfer mode only).</summary>
    public List<(string SourcePath, string TargetPath)> CoverPairs { get; } = new();
    public int MatchedFiles { get; set; }
    public string Summary { get; set; } = "";
}

/// <summary>
/// Parses a pasted block of text and maps it onto the selected files. Auto-detects three shapes —
/// a single "Field: value" block (applied to every file), a numbered/"Artist - Title" tracklist
/// (fuzzy-matched per file), and a CSV/TSV table (one row per file) — and can also copy shared
/// album-level metadata from one "master" file to the rest. Output is expressed as
/// <see cref="MetadataEnrichmentChange"/> rows so the Auto-Tag apply path writes them unchanged.
/// </summary>
public static class PastedMetadataService
{
    /// <summary>
    /// Minimum fuzzy similarity for a match to be trusted on its own. Below this, a match is only
    /// used when the source and target counts line up and positional order can vouch for it —
    /// otherwise the file is left untouched rather than tagged from an unrelated source.
    /// </summary>
    private const double MinMatchScore = 0.4;


    public static PastedMetadataKind DetectKind(string text)
    {
        var lines = SplitLines(text);
        if (lines.Count == 0) return PastedMetadataKind.Empty;

        // CSV/TSV: a delimited header row that names at least one known column.
        char delim = DetectDelimiter(lines[0]);
        if (delim != '\0')
        {
            var header = SplitRow(lines[0], delim);
            int known = header.Count(h => MapField(h) != null || IsFileColumn(h));
            if (header.Count >= 2 && known >= 1) return PastedMetadataKind.Csv;
        }

        // Single block: most lines are "Field: value" with a recognized field, and it's short.
        int blockLines = lines.Count(l =>
        {
            int i = l.IndexOf(':');
            return i > 0 && MapField(l[..i]) != null;
        });
        if (blockLines >= 1 && blockLines >= lines.Count - 1 && lines.Count <= 14)
            return PastedMetadataKind.SingleBlock;

        return PastedMetadataKind.Tracklist;
    }

    public static PastedMetadataResult Parse(IReadOnlyList<AudioFileInfo> files, string text, PastedMetadataKind? forceKind = null)
    {
        var lines = SplitLines(text);
        var kind = forceKind ?? DetectKind(text);
        return kind switch
        {
            PastedMetadataKind.Csv => ParseCsv(files, lines),
            PastedMetadataKind.SingleBlock => ParseSingleBlock(files, lines),
            PastedMetadataKind.Tracklist => ParseTracklist(files, lines),
            _ => new PastedMetadataResult { Kind = PastedMetadataKind.Empty, Summary = "Nothing to parse." }
        };
    }

    // ─────────────────────────── Single block ───────────────────────────

    private static PastedMetadataResult ParseSingleBlock(IReadOnlyList<AudioFileInfo> files, IReadOnlyList<string> lines)
    {
        var result = new PastedMetadataResult { Kind = PastedMetadataKind.SingleBlock };
        var values = new Dictionary<MetadataEnrichmentField, string>();
        foreach (var line in lines)
        {
            int idx = line.IndexOf(':');
            if (idx <= 0) continue;
            var field = MapField(line[..idx]);
            if (field == null) continue;
            string val = line[(idx + 1)..].Trim();
            if (!string.IsNullOrWhiteSpace(val)) values[field.Value] = val;
        }

        if (values.Count == 0)
        {
            result.Summary = "No recognizable \"Field: value\" lines found.";
            return result;
        }

        foreach (var file in files)
            foreach (var kv in values)
                result.Changes.Add(Change(file, kv.Key, kv.Value, "Pasted block"));

        result.MatchedFiles = files.Count;
        result.Summary = $"Applying {values.Count} field(s) to all {files.Count} file(s).";
        return result;
    }

    // ─────────────────────────── Tracklist ───────────────────────────

    private sealed record TrackEntry(int TrackNumber, string Artist, string Title);

    private static PastedMetadataResult ParseTracklist(IReadOnlyList<AudioFileInfo> files, IReadOnlyList<string> lines)
    {
        var result = new PastedMetadataResult { Kind = PastedMetadataKind.Tracklist };
        var entries = lines.Select(ParseTrackLine).Where(e => !string.IsNullOrWhiteSpace(e.Title)).ToList();
        if (entries.Count == 0)
        {
            result.Summary = "No tracklist lines could be parsed.";
            return result;
        }

        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        bool sameCount = entries.Count == files.Count;
        int matched = 0;

        for (int i = 0; i < entries.Count; i++)
        {
            var entry = entries[i];
            var file = BestFileFor(entry, files, used, out double score);
            bool weak = file == null || score < MinMatchScore;

            // Fall back to positional matching when fuzzy matching is weak and the counts line up.
            bool byPosition = false;
            if (weak && sameCount && !used.Contains(files[i].FilePath))
            {
                file = files[i];
                byPosition = true;
            }

            // Weak match with no positional fallback → not a match. See ApplyFromFolder for why.
            if (file == null || (weak && !byPosition)) continue;
            if (used.Contains(file.FilePath)) continue;

            used.Add(file.FilePath);
            matched++;
            result.Changes.Add(Change(file, MetadataEnrichmentField.Title, entry.Title, "Pasted tracklist"));
            if (!string.IsNullOrWhiteSpace(entry.Artist))
                result.Changes.Add(Change(file, MetadataEnrichmentField.Artist, entry.Artist, "Pasted tracklist"));
            if (entry.TrackNumber > 0)
                result.Changes.Add(Change(file, MetadataEnrichmentField.TrackNumber, entry.TrackNumber.ToString(CultureInfo.InvariantCulture), "Pasted tracklist"));
        }

        result.MatchedFiles = matched;
        result.Summary = $"Matched {matched} of {files.Count} file(s) from {entries.Count} pasted line(s).";
        return result;
    }

    private static readonly Regex DurationRegex = new(@"\s*[\(\[]?\b\d{1,2}:\d{2}\b[\)\]]?\s*$", RegexOptions.Compiled);
    private static readonly Regex LeadingNumberRegex = new(@"^\s*(\d{1,3})[\.\)\-\s]+", RegexOptions.Compiled);

    private static TrackEntry ParseTrackLine(string raw)
    {
        string line = (raw ?? "").Trim();
        line = DurationRegex.Replace(line, "").Trim();

        int track = 0;
        var m = LeadingNumberRegex.Match(line);
        if (m.Success)
        {
            int.TryParse(m.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out track);
            line = line[m.Length..].Trim();
        }

        string artist = "", title = line;
        int dash = line.IndexOf(" - ", StringComparison.Ordinal);
        if (dash <= 0) dash = line.IndexOf('\t');
        if (dash > 0)
        {
            artist = line[..dash].Trim(" -\t".ToCharArray());
            title = line[(dash + 3)..].Trim(" -\t".ToCharArray());
            if (string.IsNullOrWhiteSpace(title)) { title = artist; artist = ""; }
        }

        return new TrackEntry(track, artist, title);
    }

    private static AudioFileInfo? BestFileFor(TrackEntry entry, IReadOnlyList<AudioFileInfo> files, HashSet<string> used, out double bestScore)
    {
        AudioFileInfo? best = null;
        bestScore = -1;
        foreach (var f in files)
        {
            if (used.Contains(f.FilePath)) continue;
            string fileTitle = !string.IsNullOrWhiteSpace(f.Title) ? f.Title : FilenameMetadataParser.Parse(f.FileName).Title;
            double score = MetadataEnrichmentService.NameSimilarity(entry.Title, fileTitle);
            if (score > bestScore)
            {
                bestScore = score;
                best = f;
            }
        }

        return best;
    }

    // ─────────────────────────── CSV / TSV ───────────────────────────

    private static PastedMetadataResult ParseCsv(IReadOnlyList<AudioFileInfo> files, IReadOnlyList<string> lines)
    {
        var result = new PastedMetadataResult { Kind = PastedMetadataKind.Csv };
        if (lines.Count < 2)
        {
            result.Summary = "CSV needs a header row plus at least one data row.";
            return result;
        }

        char delim = DetectDelimiter(lines[0]);
        if (delim == '\0') delim = ',';
        var header = SplitRow(lines[0], delim);

        int fileCol = -1;
        var map = new List<(int col, MetadataEnrichmentField field)>();
        for (int c = 0; c < header.Count; c++)
        {
            if (IsFileColumn(header[c])) { fileCol = c; continue; }
            var field = MapField(header[c]);
            if (field != null) map.Add((c, field.Value));
        }

        if (map.Count == 0)
        {
            result.Summary = "No recognizable columns in the CSV header.";
            return result;
        }

        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        int matched = 0;
        for (int i = 1; i < lines.Count; i++)
        {
            var row = SplitRow(lines[i], delim);
            AudioFileInfo? file = fileCol >= 0 && fileCol < row.Count
                ? MatchByFilename(files, row[fileCol], used)
                : (i - 1 < files.Count ? files[i - 1] : null);
            if (file == null || used.Contains(file.FilePath)) continue;

            used.Add(file.FilePath);
            matched++;
            foreach (var (col, field) in map)
            {
                if (col >= row.Count) continue;
                string val = row[col].Trim();
                if (!string.IsNullOrWhiteSpace(val))
                    result.Changes.Add(Change(file, field, val, "Pasted CSV"));
            }
        }

        result.MatchedFiles = matched;
        result.Summary = $"Matched {matched} of {files.Count} file(s) across {lines.Count - 1} CSV row(s).";
        return result;
    }

    private static AudioFileInfo? MatchByFilename(IReadOnlyList<AudioFileInfo> files, string value, HashSet<string> used)
    {
        string needle = (value ?? "").Trim().Trim('"');
        if (string.IsNullOrWhiteSpace(needle)) return null;
        string needleNoExt = Path.GetFileNameWithoutExtension(needle);

        return files.FirstOrDefault(f => !used.Contains(f.FilePath) &&
                   (string.Equals(f.FileName, needle, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(Path.GetFileNameWithoutExtension(f.FileName), needleNoExt, StringComparison.OrdinalIgnoreCase)))
               ?? files.FirstOrDefault(f => !used.Contains(f.FilePath) &&
                   f.FileName.Contains(needleNoExt, StringComparison.OrdinalIgnoreCase));
    }

    // ─────────────────────────── Copy from master ───────────────────────────

    /// <summary>Builds reviewable changes that copy shared album-level text fields from one file to
    /// the others. Cover art is copied separately by <see cref="CopyCoverFromMasterAsync"/>.</summary>
    public static PastedMetadataResult BuildCopyFromMasterChanges(AudioFileInfo master, IReadOnlyList<AudioFileInfo> targets)
    {
        var result = new PastedMetadataResult { Kind = PastedMetadataKind.SingleBlock };
        var values = ReadMasterValues(master);
        int count = 0;
        foreach (var file in targets)
        {
            if (ReferenceEquals(file, master) || string.Equals(file.FilePath, master.FilePath, StringComparison.OrdinalIgnoreCase))
                continue;
            count++;
            foreach (var kv in values)
                result.Changes.Add(Change(file, kv.Key, kv.Value, "Copied from master"));
        }

        result.MatchedFiles = count;
        result.Summary = $"Copying {values.Count} field(s) from \"{master.FileName}\" to {count} other file(s).";
        return result;
    }

    /// <summary>
    /// How a cover-copy pass went. <see cref="Failed"/> exists because these writes used to be
    /// swallowed whole: a file skipped for a failed backup, a read-only target, or an unsupported
    /// container came back as a smaller <see cref="Copied"/> and nothing else, which reads as
    /// "there was nothing to do" rather than "some of this did not happen".
    /// </summary>
    public readonly record struct CoverCopyResult(int Copied, int Failed);

    /// <summary>Copies the master file's embedded front cover onto each target file (direct tag write).</summary>
    public static Task<CoverCopyResult> CopyCoverFromMasterAsync(
        AudioFileInfo master, IReadOnlyList<AudioFileInfo> targets, bool createBackups, CancellationToken ct = default)
    {
        return Task.Run(() =>
        {
            TagLib.IPicture[] pictures;
            try
            {
                using var masterTag = TagLib.File.Create(master.FilePath);
                pictures = masterTag.Tag.Pictures;
                if (pictures == null || pictures.Length == 0) return new CoverCopyResult(0, 0);
            }
            catch
            {
                return new CoverCopyResult(0, 0);
            }

            int copied = 0, failed = 0;
            foreach (var file in targets)
            {
                ct.ThrowIfCancellationRequested();
                if (string.Equals(file.FilePath, master.FilePath, StringComparison.OrdinalIgnoreCase)) continue;
                if (string.IsNullOrWhiteSpace(file.FilePath) || !File.Exists(file.FilePath)) continue;
                try
                {
                    // Before the open: a backup that cannot be taken must stop this file's write.
                    if (createBackups) FileRenamer.CreateBackup(file.FilePath);
                    using var tagFile = TagLib.File.Create(file.FilePath);
                    tagFile.Tag.Pictures = pictures;
                    tagFile.Save();
                    copied++;
                }
                catch
                {
                    // Skip this file; others still proceed — but it is counted, not swallowed.
                    failed++;
                }
            }

            return new CoverCopyResult(copied, failed);
        }, ct);
    }

    private static Dictionary<MetadataEnrichmentField, string> ReadMasterValues(AudioFileInfo master)
    {
        var values = new Dictionary<MetadataEnrichmentField, string>();
        try
        {
            using var tagFile = TagLib.File.Create(master.FilePath);
            var tag = tagFile.Tag;
            Add(values, MetadataEnrichmentField.Album, tag.Album);
            Add(values, MetadataEnrichmentField.AlbumArtist, string.Join("; ", tag.AlbumArtists));
            Add(values, MetadataEnrichmentField.Genre, string.Join("; ", tag.Genres));
            Add(values, MetadataEnrichmentField.Composer, string.Join("; ", tag.Composers));
            if (tag.Year > 0) Add(values, MetadataEnrichmentField.Year, tag.Year.ToString(CultureInfo.InvariantCulture));
            if (tag.Disc > 0) Add(values, MetadataEnrichmentField.DiscNumber, tag.Disc.ToString(CultureInfo.InvariantCulture));
        }
        catch
        {
            // Fall back to whatever the snapshot already knows.
            Add(values, MetadataEnrichmentField.Album, master.Album);
        }

        return values;
    }

    private static void Add(Dictionary<MetadataEnrichmentField, string> values, MetadataEnrichmentField field, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value)) values[field] = value.Trim();
    }

    // ─────────────────────────── Copy from another folder (set → set) ───────────────────────────

    /// <summary>
    /// A source file's tag values plus a best-guess (title, artist, track) parsed from its file name,
    /// used to match a separate well-tagged folder against the loaded target files.
    /// </summary>
    public sealed class SourceTagSet
    {
        public string FilePath { get; init; } = "";
        public string FileName { get; init; } = "";
        public string Title { get; init; } = "";       // tag title, else parsed from file name
        public int TrackNumber { get; init; }
        public Dictionary<MetadataEnrichmentField, string> Values { get; } = new();
        public bool HasCover { get; init; }
    }

    /// <summary>Reads tag values (and a name-parse fallback) for every audio file under a source folder.</summary>
    public static List<SourceTagSet> ReadSourceTagSets(IEnumerable<string> paths)
    {
        var list = new List<SourceTagSet>();
        foreach (var path in paths)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) continue;
            var parsed = FilenameMetadataParser.Parse(Path.GetFileName(path));
            var values = new Dictionary<MetadataEnrichmentField, string>();
            string title = parsed.Title;
            int track = parsed.TrackNumber;
            bool hasCover = false;
            try
            {
                using var tagFile = TagLib.File.Create(path);
                var tag = tagFile.Tag;
                Add(values, MetadataEnrichmentField.Title, tag.Title);
                Add(values, MetadataEnrichmentField.Artist, string.Join("; ", tag.Performers));
                Add(values, MetadataEnrichmentField.Album, tag.Album);
                Add(values, MetadataEnrichmentField.AlbumArtist, string.Join("; ", tag.AlbumArtists));
                Add(values, MetadataEnrichmentField.Genre, string.Join("; ", tag.Genres));
                Add(values, MetadataEnrichmentField.Composer, string.Join("; ", tag.Composers));
                Add(values, MetadataEnrichmentField.Comment, tag.Comment);
                Add(values, MetadataEnrichmentField.Lyrics, tag.Lyrics);
                Add(values, MetadataEnrichmentField.Copyright, tag.Copyright);
                if (tag.Year > 0) Add(values, MetadataEnrichmentField.Year, tag.Year.ToString(CultureInfo.InvariantCulture));
                if (tag.Track > 0) { Add(values, MetadataEnrichmentField.TrackNumber, tag.Track.ToString(CultureInfo.InvariantCulture)); track = (int)tag.Track; }
                if (tag.Disc > 0) Add(values, MetadataEnrichmentField.DiscNumber, tag.Disc.ToString(CultureInfo.InvariantCulture));
                if (!string.IsNullOrWhiteSpace(tag.Title)) title = tag.Title.Trim();
                hasCover = tag.Pictures is { Length: > 0 };
            }
            catch
            {
                // Unreadable source file — keep the name-parsed guesses so it can still match.
            }

            var set = new SourceTagSet
            {
                FilePath = path,
                FileName = Path.GetFileName(path),
                Title = title,
                TrackNumber = track,
                HasCover = hasCover,
            };
            foreach (var kv in values) set.Values[kv.Key] = kv.Value;
            list.Add(set);
        }
        return list;
    }

    /// <summary>
    /// Matches each loaded target file to its best source from a separate folder (name/title-fuzzy,
    /// extension-agnostic) and builds reviewable changes for only the user-selected fields. Cover art
    /// is reported as <see cref="PastedMetadataResult.CoverPairs"/> for a separate copy pass.
    /// </summary>
    public static PastedMetadataResult BuildCopyFromFolderChanges(
        IReadOnlyList<SourceTagSet> sources,
        IReadOnlyList<AudioFileInfo> targets,
        ISet<MetadataEnrichmentField> fields)
    {
        var result = new PastedMetadataResult { Kind = PastedMetadataKind.Tracklist };
        if (sources.Count == 0)
        {
            result.Summary = "No readable audio files found in the source folder.";
            return result;
        }
        if (fields.Count == 0)
        {
            result.Summary = "Pick at least one field to copy.";
            return result;
        }

        bool wantCover = fields.Contains(MetadataEnrichmentField.CoverArt);
        bool wantRename = fields.Contains(MetadataEnrichmentField.FileName);
        var usedSources = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        bool sameCount = sources.Count == targets.Count;
        int matched = 0;

        for (int i = 0; i < targets.Count; i++)
        {
            var target = targets[i];
            var source = BestSourceFor(target, sources, usedSources, out double score);
            bool weak = source == null || score < MinMatchScore;

            // Counts line up but fuzzy matching is weak → trust the on-disk order.
            bool byPosition = false;
            if (weak && sameCount && i < sources.Count
                && !usedSources.Contains(sources[i].FilePath))
            {
                source = sources[i];
                byPosition = true;
            }

            // A weak match with no positional fallback to fall back on is not a match. Applying it
            // anyway would copy an unrelated file's tags onto this one.
            if (source == null || (weak && !byPosition)) continue;
            if (usedSources.Contains(source.FilePath)) continue;

            usedSources.Add(source.FilePath);
            matched++;

            foreach (var field in fields)
            {
                if (field is MetadataEnrichmentField.CoverArt or MetadataEnrichmentField.FileName) continue;
                if (source.Values.TryGetValue(field, out var val) && !string.IsNullOrWhiteSpace(val))
                    result.Changes.Add(Change(target, field, val, $"Copied from {source.FileName}"));
            }
            if (wantCover && source.HasCover)
                result.CoverPairs.Add((source.FilePath, target.FilePath));
            if (wantRename)
            {
                // Mirror the source's base name onto the target, keeping the target's own extension
                // (source and target may be different formats).
                string newName = Path.GetFileNameWithoutExtension(source.FileName) + Path.GetExtension(target.FileName);
                if (!string.IsNullOrWhiteSpace(Path.GetFileNameWithoutExtension(source.FileName))
                    && !string.Equals(newName, target.FileName, StringComparison.OrdinalIgnoreCase))
                    result.Changes.Add(RenameChange(target, newName, $"Rename to match {source.FileName}"));
            }
        }

        result.MatchedFiles = matched;
        string coverNote = wantCover ? $", {result.CoverPairs.Count} with cover art" : "";
        int unmatched = targets.Count - matched;
        string unmatchedNote = unmatched > 0
            ? $" {unmatched} file(s) had no close enough match and were left untouched."
            : "";
        result.Summary =
            $"Matched {matched} of {targets.Count} file(s) from {sources.Count} source file(s){coverNote}.{unmatchedNote}";
        return result;
    }

    private static MetadataEnrichmentChange RenameChange(AudioFileInfo file, string newName, string reason)
        => new()
        {
            FilePath = file.FilePath,
            FileName = file.FileName,
            Field = MetadataEnrichmentField.FileName,
            OldValue = file.FileName,
            NewValue = newName,
            Provider = "Pasted",
            Reason = reason,
            Confidence = 1.0,
            IsSelected = true
        };

    /// <summary>
    /// Renames each target file to the requested name (in its own folder), preserving uniqueness.
    /// Updates the in-memory <see cref="AudioFileInfo"/> path/name in place. Returns the count renamed.
    /// </summary>
    public static Task<int> ApplyRenamesAsync(
        IReadOnlyList<(AudioFileInfo file, string newName)> renames, CancellationToken ct = default)
    {
        return Task.Run(() =>
        {
            int renamed = 0;
            foreach (var (file, newName) in renames)
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    if (string.IsNullOrWhiteSpace(file.FilePath) || !File.Exists(file.FilePath)) continue;
                    string dir = Path.GetDirectoryName(file.FilePath) ?? "";
                    string safe = SanitizeFileName(newName);
                    if (string.IsNullOrWhiteSpace(safe)) continue;

                    string target = Path.Combine(dir, safe);
                    // Ordinal, so a rename that only changes letter case still goes through.
                    if (string.Equals(target, file.FilePath, StringComparison.Ordinal)) continue;
                    if (!string.Equals(target, file.FilePath, StringComparison.OrdinalIgnoreCase))
                        target = EnsureUniquePath(target);

                    if (FileRenamer.Rename(file.FilePath, target) != RenameOutcome.Renamed) continue;
                    file.FilePath = target;
                    file.FileName = Path.GetFileName(target);
                    renamed++;
                }
                catch
                {
                    // Skip this file; others still proceed.
                }
            }
            return renamed;
        }, ct);
    }

    private static string SanitizeFileName(string name)
    {
        foreach (char c in Path.GetInvalidFileNameChars())
            name = name.Replace(c, '_');
        return name.Trim().TrimEnd('.', ' ');
    }

    private static string EnsureUniquePath(string path)
    {
        if (!File.Exists(path)) return path;
        string dir = Path.GetDirectoryName(path) ?? "";
        string stem = Path.GetFileNameWithoutExtension(path);
        string ext = Path.GetExtension(path);
        for (int n = 2; ; n++)
        {
            string candidate = Path.Combine(dir, $"{stem} ({n}){ext}");
            if (!File.Exists(candidate)) return candidate;
        }
    }

    private static SourceTagSet? BestSourceFor(
        AudioFileInfo target, IReadOnlyList<SourceTagSet> sources, HashSet<string> used, out double bestScore)
    {
        var targetParsed = FilenameMetadataParser.Parse(target.FileName);
        string targetTitle = !string.IsNullOrWhiteSpace(target.Title) ? target.Title : targetParsed.Title;
        string targetStem = Path.GetFileNameWithoutExtension(target.FileName);
        int targetTrack = targetParsed.TrackNumber;

        SourceTagSet? best = null;
        bestScore = -1;
        foreach (var s in sources)
        {
            if (used.Contains(s.FilePath)) continue;
            double titleScore = MetadataEnrichmentService.NameSimilarity(targetTitle, s.Title);
            double nameScore = MetadataEnrichmentService.NameSimilarity(
                targetStem, Path.GetFileNameWithoutExtension(s.FileName));
            double score = Math.Max(titleScore, nameScore * 0.95);
            if (s.TrackNumber > 0 && s.TrackNumber == targetTrack) score += 0.05;
            if (score > bestScore)
            {
                bestScore = score;
                best = s;
            }
        }
        return best;
    }

    /// <summary>Copies the embedded front cover from each matched source file onto its target (direct tag write).</summary>
    public static Task<CoverCopyResult> CopyCoverPairsAsync(
        IReadOnlyList<(string sourcePath, string targetPath)> pairs, bool createBackups, CancellationToken ct = default)
    {
        return Task.Run(() =>
        {
            int copied = 0, failed = 0;
            foreach (var (sourcePath, targetPath) in pairs)
            {
                ct.ThrowIfCancellationRequested();
                if (string.IsNullOrWhiteSpace(targetPath) || !File.Exists(targetPath)) continue;
                try
                {
                    TagLib.IPicture[] pictures;
                    using (var src = TagLib.File.Create(sourcePath))
                    {
                        pictures = src.Tag.Pictures;
                        // Nothing to copy is not a failure — the source simply has no art.
                        if (pictures == null || pictures.Length == 0) continue;
                    }
                    if (createBackups) FileRenamer.CreateBackup(targetPath);
                    using var dst = TagLib.File.Create(targetPath);
                    dst.Tag.Pictures = pictures;
                    dst.Save();
                    copied++;
                }
                catch
                {
                    // Skip this pair; others still proceed — but it is counted, not swallowed.
                    failed++;
                }
            }
            return new CoverCopyResult(copied, failed);
        }, ct);
    }

    // ─────────────────────────── Shared helpers ───────────────────────────

    private static MetadataEnrichmentChange Change(AudioFileInfo file, MetadataEnrichmentField field, string newValue, string reason)
        => new()
        {
            FilePath = file.FilePath,
            FileName = file.FileName,
            Field = field,
            OldValue = OldValueFor(file, field),
            NewValue = newValue.Trim(),
            Provider = "Pasted",
            Reason = reason,
            Confidence = 1.0,
            IsSelected = true
        };

    private static string OldValueFor(AudioFileInfo file, MetadataEnrichmentField field) => field switch
    {
        MetadataEnrichmentField.Title => file.Title ?? "",
        MetadataEnrichmentField.Artist => file.Artist ?? "",
        MetadataEnrichmentField.Album => file.Album ?? "",
        _ => ""
    };

    private static MetadataEnrichmentField? MapField(string? key)
    {
        string k = (key ?? "").Trim().Trim('"').ToLowerInvariant().Replace('_', ' ').Replace('-', ' ');
        k = Regex.Replace(k, @"\s+", " ").Trim();
        return k switch
        {
            "title" or "song" or "track name" or "name" or "track title" => MetadataEnrichmentField.Title,
            "artist" or "artists" or "performer" or "performers" => MetadataEnrichmentField.Artist,
            "album" => MetadataEnrichmentField.Album,
            "album artist" or "albumartist" or "band" => MetadataEnrichmentField.AlbumArtist,
            "year" or "date" => MetadataEnrichmentField.Year,
            "track" or "track number" or "trackno" or "track no" or "no" or "#" => MetadataEnrichmentField.TrackNumber,
            "disc" or "disk" or "disc number" or "disc no" => MetadataEnrichmentField.DiscNumber,
            "genre" or "genres" => MetadataEnrichmentField.Genre,
            "composer" or "composers" or "written by" or "writer" => MetadataEnrichmentField.Composer,
            "comment" or "comments" or "note" or "notes" => MetadataEnrichmentField.Comment,
            "lyrics" => MetadataEnrichmentField.Lyrics,
            "copyright" => MetadataEnrichmentField.Copyright,
            _ => null
        };
    }

    private static bool IsFileColumn(string? header)
    {
        string h = (header ?? "").Trim().Trim('"').ToLowerInvariant();
        return h is "file" or "filename" or "file name" or "path" or "filepath";
    }

    private static List<string> SplitLines(string text)
        => (text ?? "")
            .Replace("\r\n", "\n").Replace('\r', '\n')
            .Split('\n')
            .Select(l => l.TrimEnd())
            .Where(l => !string.IsNullOrWhiteSpace(l))
            .ToList();

    private static char DetectDelimiter(string headerLine)
    {
        if (headerLine.Contains('\t')) return '\t';
        if (headerLine.Contains(',')) return ',';
        if (headerLine.Contains(';')) return ';';
        return '\0';
    }

    /// <summary>Splits a delimited row, honoring simple double-quoted fields.</summary>
    private static List<string> SplitRow(string line, char delim)
    {
        var result = new List<string>();
        var current = new System.Text.StringBuilder();
        bool inQuotes = false;
        foreach (char c in line)
        {
            if (c == '"') { inQuotes = !inQuotes; continue; }
            if (c == delim && !inQuotes)
            {
                result.Add(current.ToString().Trim());
                current.Clear();
                continue;
            }
            current.Append(c);
        }

        result.Add(current.ToString().Trim());
        return result;
    }

}
