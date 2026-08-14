using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using AudioQualityChecker.Models;

namespace AudioQualityChecker.Services;

public enum SmartRenameStyle
{
    AlbumSafe,
    ArtistTitle,
    TitleArtist,
    TrackArtistTitle,
    AlbumArtistTitle,
    Custom
}

public enum SmartRenameFolderMode
{
    KeepCurrent,
    ArtistAlbum,
    Album,
    Custom
}

public enum SmartRenameConflictBehavior
{
    Skip,
    AppendNumber
}

/// <summary>Optional whole-name case transform applied after the name is built.</summary>
public enum SmartRenameNameCase
{
    None,
    Lower,
    Upper,
    Title
}

/// <summary>Optional space/underscore normalization applied to the built name.</summary>
public enum SmartRenameSpaceMode
{
    Keep,
    Underscores,
    Spaces
}

public enum SmartRenameConfidence
{
    High,
    Review,
    Skip
}

public enum SmartRenameWarning
{
    MissingTitle,
    MissingArtist,
    FilenameGuess,
    JunkRemoved,
    DuplicateTarget,
    TargetExists,
    CueVirtualTrack,
    AlreadyClean,
    ConflictingAlbum
}

public sealed class SmartRenameOptions
{
    public SmartRenameStyle Style { get; set; } = SmartRenameStyle.AlbumSafe;
    public SmartRenameFolderMode FolderMode { get; set; } = SmartRenameFolderMode.KeepCurrent;
    public SmartRenameConflictBehavior ConflictBehavior { get; set; } = SmartRenameConflictBehavior.Skip;
    public string CustomPattern { get; set; } = "{artist} - {title}";
    public string CustomFolderPattern { get; set; } = "{artist}/{album}";
    public bool IncludeTrackNumbers { get; set; } = true;
    public bool PreserveVersionInfo { get; set; } = true;
    public bool RenameCleanFiles { get; set; }

    /// <summary>Zero-pad width applied to the {track} token (and {track2}/{track3} overrides). Minimum 1.</summary>
    public int TrackPadWidth { get; set; } = 2;

    /// <summary>Optional literal text to find in the generated name (case-insensitive) and replace.</summary>
    public string FindText { get; set; } = "";

    /// <summary>Replacement for <see cref="FindText"/>. Ignored when FindText is empty.</summary>
    public string ReplaceText { get; set; } = "";

    // ─── Optional name transforms (applied to the built file name, not folders/extension) ───
    public SmartRenameNameCase NameCase { get; set; } = SmartRenameNameCase.None;
    public SmartRenameSpaceMode SpaceMode { get; set; } = SmartRenameSpaceMode.Keep;

    /// <summary>Strip "(feat. …)" / "ft." / "featuring …" segments from the generated name.</summary>
    public bool StripFeaturing { get; set; }

    public static SmartRenameOptions CreateDefault() => new();
}

public sealed class SmartRenamePreviewItem
{
    public AudioFileInfo File { get; init; } = new();
    public string FilePath { get; init; } = "";
    public string FileName { get; init; } = "";
    public string CurrentName { get; init; } = "";
    public string NewName { get; set; } = "";
    public string TargetPath { get; set; } = "";
    public SmartRenameConfidence Confidence { get; set; }
    public List<SmartRenameWarning> Warnings { get; } = new();
    public List<string> Reasons { get; } = new();
    public bool IsSelected { get; set; }
}

public static class SmartRenameService
{
    /// <summary>
    /// Tag values read from disk, kept across preview rebuilds. Building a preview is cheap except
    /// for the TagLib open per file, and the Batch Editor rebuilds on every keystroke in the pattern
    /// box — without this, typing one character reopened every selected file on the UI thread.
    /// Hold one per editor session; the tags can't change while the dialog owns them.
    /// </summary>
    public sealed class TagCache
    {
        private readonly Dictionary<string, RenameValues> _byPath = new(StringComparer.OrdinalIgnoreCase);

        internal RenameValues Get(AudioFileInfo file)
        {
            string key = file.FilePath ?? "";
            if (key.Length > 0 && _byPath.TryGetValue(key, out var cached)) return cached;

            var values = RenameValues.FromFile(file);
            if (key.Length > 0) _byPath[key] = values;
            return values;
        }

        /// <summary>Drops cached values for one file (call after its tags are rewritten).</summary>
        public void Invalidate(string filePath)
        {
            if (!string.IsNullOrEmpty(filePath)) _byPath.Remove(filePath);
        }

        public void Clear() => _byPath.Clear();
    }

    public static IReadOnlyList<SmartRenamePreviewItem> BuildPreview(
        IReadOnlyList<AudioFileInfo> files,
        SmartRenameOptions options,
        Func<string, bool>? targetExists = null,
        TagCache? tagCache = null)
    {
        options ??= SmartRenameOptions.CreateDefault();
        targetExists ??= File.Exists;
        tagCache ??= new TagCache();
        var contexts = BuildAlbumContexts(files);
        var usedTargets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<SmartRenamePreviewItem>(files.Count);

        foreach (var file in files)
        {
            var preview = BuildItem(file, files, contexts, options, tagCache);
            ResolveTarget(preview, options, usedTargets, targetExists);
            result.Add(preview);
        }

        return result;
    }

    private static SmartRenamePreviewItem BuildItem(
        AudioFileInfo file,
        IReadOnlyList<AudioFileInfo> files,
        Dictionary<string, AlbumContext> contexts,
        SmartRenameOptions options,
        TagCache tagCache)
    {
        var currentName = Path.GetFileName(file.FilePath);
        if (string.IsNullOrWhiteSpace(currentName))
            currentName = file.FileName;

        var preview = new SmartRenamePreviewItem
        {
            File = file,
            FilePath = file.FilePath,
            FileName = file.FileName,
            CurrentName = currentName
        };

        if (file.IsCueVirtualTrack)
        {
            preview.NewName = currentName;
            preview.TargetPath = file.FilePath;
            preview.Confidence = SmartRenameConfidence.Skip;
            preview.Warnings.Add(SmartRenameWarning.CueVirtualTrack);
            preview.Reasons.Add("CUE virtual tracks are not real files");
            return preview;
        }

        var values = tagCache.Get(file).Clone();
        var parsed = ParseFilenameFallback(currentName, options);
        if (string.IsNullOrWhiteSpace(values.Title) && !string.IsNullOrWhiteSpace(parsed.Title))
        {
            values.Title = parsed.Title;
            preview.Warnings.Add(SmartRenameWarning.FilenameGuess);
            preview.Reasons.Add("Title guessed from filename");
        }

        if (string.IsNullOrWhiteSpace(values.Artist) && !string.IsNullOrWhiteSpace(parsed.Artist))
        {
            values.Artist = parsed.Artist;
            preview.Warnings.Add(SmartRenameWarning.FilenameGuess);
            preview.Reasons.Add("Artist guessed from filename");
        }

        if (values.TrackNumber <= 0 && parsed.TrackNumber > 0)
        {
            values.TrackNumber = parsed.TrackNumber;
            preview.Reasons.Add("Track number found");
        }

        if (parsed.JunkRemoved)
        {
            preview.Warnings.Add(SmartRenameWarning.JunkRemoved);
            preview.Reasons.Add("Website/source junk removed");
        }

        if (string.IsNullOrWhiteSpace(values.Title))
        {
            preview.NewName = currentName;
            preview.TargetPath = file.FilePath;
            preview.Confidence = SmartRenameConfidence.Skip;
            preview.Warnings.Add(SmartRenameWarning.MissingTitle);
            preview.Reasons.Add("Missing title");
            return preview;
        }

        var context = contexts.TryGetValue(AlbumKey(file), out var foundContext)
            ? foundContext
            : AlbumContext.FromSingle(file);

        string baseName = BuildBaseName(values, context, options);
        baseName = SanitizeFileName(baseName);
        if (string.IsNullOrWhiteSpace(baseName))
            baseName = SanitizeFileName(values.Title);

        string ext = Path.GetExtension(currentName);
        if (string.IsNullOrWhiteSpace(ext) && !string.IsNullOrWhiteSpace(file.Extension))
            ext = "." + file.Extension.TrimStart('.');

        string relativeTarget = BuildRelativeTarget(values, baseName + ext, options);
        preview.NewName = relativeTarget;
        preview.TargetPath = BuildTargetPath(file, relativeTarget);

        if (context.IsMixedArtist && options.Style == SmartRenameStyle.AlbumSafe)
            preview.Reasons.Add("Mixed-artist album detected");

        if (values.TrackNumber > 0 && relativeTarget.Contains(values.TrackNumber.ToString("D2", CultureInfo.InvariantCulture), StringComparison.Ordinal))
            preview.Reasons.Add("Track number included");

        if (string.IsNullOrWhiteSpace(values.Artist) && NeedsArtist(options, context))
        {
            preview.Warnings.Add(SmartRenameWarning.MissingArtist);
            preview.Reasons.Add("Missing artist");
        }

        bool guessed = preview.Warnings.Contains(SmartRenameWarning.FilenameGuess);
        preview.Confidence = guessed || preview.Warnings.Contains(SmartRenameWarning.MissingArtist)
            ? SmartRenameConfidence.Review
            : SmartRenameConfidence.High;
        preview.IsSelected = preview.Confidence == SmartRenameConfidence.High;

        // Compare file name to file name. relativeTarget carries directory segments once a folder
        // mode is on, so comparing it whole never matched and every already-clean file was proposed
        // for a rename. A folder move is still a real change, so it has to keep its own check.
        bool sameName = string.Equals(currentName, Path.GetFileName(relativeTarget), StringComparison.OrdinalIgnoreCase);
        bool samePlace = string.Equals(preview.TargetPath, file.FilePath, StringComparison.OrdinalIgnoreCase);
        if (!options.RenameCleanFiles && sameName && samePlace)
        {
            preview.Confidence = SmartRenameConfidence.Skip;
            preview.IsSelected = false;
            preview.Warnings.Add(SmartRenameWarning.AlreadyClean);
            preview.Reasons.Add("Already clean");
        }

        return preview;
    }

    private static void ResolveTarget(
        SmartRenamePreviewItem preview,
        SmartRenameOptions options,
        HashSet<string> usedTargets,
        Func<string, bool> targetExists)
    {
        if (preview.Confidence == SmartRenameConfidence.Skip)
            return;

        string targetKey = preview.TargetPath;
        bool duplicate = usedTargets.Contains(targetKey);
        bool exists = targetExists(targetKey) && !string.Equals(targetKey, preview.FilePath, StringComparison.OrdinalIgnoreCase);

        if (!duplicate && !exists)
        {
            usedTargets.Add(targetKey);
            return;
        }

        if (options.ConflictBehavior == SmartRenameConflictBehavior.AppendNumber)
        {
            var directory = Path.GetDirectoryName(preview.TargetPath) ?? "";
            var relativeDirectory = Path.GetDirectoryName(preview.NewName) ?? "";
            var name = Path.GetFileNameWithoutExtension(preview.NewName);
            var ext = Path.GetExtension(preview.NewName);
            int suffix = 2;
            string candidateName;
            string candidateTarget;
            do
            {
                candidateName = Path.Combine(relativeDirectory, $"{name} ({suffix}){ext}");
                candidateTarget = Path.Combine(directory, $"{name} ({suffix}){ext}");
                suffix++;
            }
            while (usedTargets.Contains(candidateTarget) || targetExists(candidateTarget));

            preview.NewName = candidateName;
            preview.TargetPath = candidateTarget;
            preview.Warnings.Add(SmartRenameWarning.DuplicateTarget);
            preview.Reasons.Add("Duplicate target resolved with suffix");
            usedTargets.Add(candidateTarget);
            return;
        }

        preview.Confidence = SmartRenameConfidence.Skip;
        preview.IsSelected = false;
        preview.Warnings.Add(duplicate ? SmartRenameWarning.DuplicateTarget : SmartRenameWarning.TargetExists);
        preview.Reasons.Add(duplicate ? "Duplicate target skipped" : "Target already exists");
    }

    private static string BuildBaseName(RenameValues values, AlbumContext context, SmartRenameOptions options)
    {
        int pad = Math.Max(1, options.TrackPadWidth);
        string track = values.TrackNumber > 0
            ? values.TrackNumber.ToString("D" + pad.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture)
            : "";

        string baseName = options.Style switch
        {
            SmartRenameStyle.ArtistTitle => JoinParts(" - ", values.Artist, values.Title),
            SmartRenameStyle.TitleArtist => JoinParts(" - ", values.Title, values.Artist),
            SmartRenameStyle.TrackArtistTitle => JoinParts(" - ", track, values.Artist, values.Title),
            SmartRenameStyle.AlbumArtistTitle => JoinParts(" - ", values.Album, values.Artist, values.Title),
            SmartRenameStyle.Custom => ApplyPattern(options.CustomPattern, values, options),
            _ => BuildAlbumSafeName(values, context, options, track)
        };

        return ApplyTransforms(ApplyFindReplace(baseName, options), options);
    }

    private static string ApplyFindReplace(string value, SmartRenameOptions options)
    {
        if (string.IsNullOrEmpty(options.FindText))
            return value;
        return value.Replace(options.FindText, options.ReplaceText ?? "", StringComparison.OrdinalIgnoreCase);
    }

    private static readonly Regex FeaturingRegex = new(
        @"(?ix)\s*[\(\[]?\s*(feat\.?|ft\.?|featuring)\b[^)\]\-]*[\)\]]?",
        RegexOptions.Compiled);

    /// <summary>Applies the optional whole-name transforms (strip-feat, case, space mode).</summary>
    private static string ApplyTransforms(string value, SmartRenameOptions options)
    {
        if (string.IsNullOrWhiteSpace(value)) return value;

        if (options.StripFeaturing)
            value = FeaturingRegex.Replace(value, " ");

        value = options.NameCase switch
        {
            SmartRenameNameCase.Lower => value.ToLowerInvariant(),
            SmartRenameNameCase.Upper => value.ToUpperInvariant(),
            SmartRenameNameCase.Title => CultureInfo.CurrentCulture.TextInfo.ToTitleCase(value.ToLowerInvariant()),
            _ => value
        };

        value = options.SpaceMode switch
        {
            SmartRenameSpaceMode.Underscores => value.Replace(' ', '_'),
            SmartRenameSpaceMode.Spaces => value.Replace('_', ' '),
            _ => value
        };

        return value.Trim();
    }

    private static string BuildAlbumSafeName(RenameValues values, AlbumContext context, SmartRenameOptions options, string track)
    {
        if (context.IsAlbumGroup)
        {
            return context.IsMixedArtist
                ? JoinParts(" - ", options.IncludeTrackNumbers ? track : "", values.Artist, values.Title)
                : JoinParts(" - ", options.IncludeTrackNumbers ? track : "", values.Title);
        }

        return !string.IsNullOrWhiteSpace(values.Artist)
            ? JoinParts(" - ", values.Artist, values.Title)
            : values.Title;
    }

    private static string BuildRelativeTarget(RenameValues values, string fileName, SmartRenameOptions options)
    {
        return options.FolderMode switch
        {
            SmartRenameFolderMode.ArtistAlbum => Path.Combine(SanitizePathSegment(values.Artist, "Unknown Artist"), SanitizePathSegment(values.Album, "Unknown Album"), fileName),
            SmartRenameFolderMode.Album => Path.Combine(SanitizePathSegment(values.Album, "Unknown Album"), fileName),
            SmartRenameFolderMode.Custom => Path.Combine(SanitizeRelativeFolder(ApplyPattern(options.CustomFolderPattern, values, options)), fileName),
            _ => fileName
        };
    }

    private static string BuildTargetPath(AudioFileInfo file, string relativeTarget)
    {
        var sourceDir = Path.GetDirectoryName(file.FilePath) ?? "";
        var target = Path.GetFullPath(Path.Combine(sourceDir, relativeTarget));
        var baseDir = Path.GetFullPath(sourceDir);
        if (!baseDir.EndsWith(Path.DirectorySeparatorChar))
            baseDir += Path.DirectorySeparatorChar;
        if (!target.StartsWith(baseDir, StringComparison.OrdinalIgnoreCase))
            return Path.Combine(sourceDir, Path.GetFileName(relativeTarget));
        return target;
    }

    private static ParsedFilename ParseFilenameFallback(string fileName, SmartRenameOptions options)
    {
        var parsed = FilenameMetadataParser.Parse(fileName);
        return new ParsedFilename
        {
            Artist = parsed.Artist,
            Title = parsed.Title,
            TrackNumber = parsed.TrackNumber,
            JunkRemoved = parsed.JunkRemoved
        };
    }

    private static Dictionary<string, AlbumContext> BuildAlbumContexts(IReadOnlyList<AudioFileInfo> files)
    {
        return files
            .GroupBy(AlbumKey, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => AlbumContext.FromGroup(g.ToList()), StringComparer.OrdinalIgnoreCase);
    }

    private static string AlbumKey(AudioFileInfo file)
    {
        var album = (file.Album ?? "").Trim();
        if (!string.IsNullOrWhiteSpace(album))
            return "album:" + Normalize(album);

        var folder = Path.GetDirectoryName(file.FilePath) ?? file.FolderPath ?? "";
        return "folder:" + Normalize(folder);
    }

    private static bool NeedsArtist(SmartRenameOptions options, AlbumContext context)
    {
        return options.Style is SmartRenameStyle.ArtistTitle or SmartRenameStyle.TitleArtist or SmartRenameStyle.TrackArtistTitle or SmartRenameStyle.AlbumArtistTitle
               || (options.Style == SmartRenameStyle.AlbumSafe && !context.IsAlbumGroup);
    }

    private static readonly Regex TokenRegex = new(
        @"\{(?<name>[a-zA-Z][a-zA-Z0-9]*)(?::(?<mod>[a-zA-Z0-9]+))?\}",
        RegexOptions.Compiled);

    /// <summary>
    /// Expands tokens in a rename pattern. Supports {artist} {title} {album} {albumartist}
    /// {year} {track} {track2}/{track3}/... (zero-pad width) {disc} {genre}, plus a case modifier
    /// suffix: {title:upper}, {title:lower}, {title:title}.
    /// </summary>
    private static string ApplyPattern(string pattern, RenameValues values, SmartRenameOptions options)
    {
        int defaultPad = Math.Max(1, options.TrackPadWidth);

        return TokenRegex.Replace(pattern ?? "", match =>
        {
            string name = match.Groups["name"].Value.ToLowerInvariant();
            string mod = match.Groups["mod"].Success ? match.Groups["mod"].Value.ToLowerInvariant() : "";

            // {track2}, {track3} => explicit pad width; {track} => default pad width.
            if (name.StartsWith("track", StringComparison.Ordinal))
            {
                int pad = defaultPad;
                string suffix = name["track".Length..];
                if (suffix.Length > 0 && int.TryParse(suffix, NumberStyles.Integer, CultureInfo.InvariantCulture, out var w) && w > 0)
                    pad = w;
                return values.TrackNumber > 0
                    ? values.TrackNumber.ToString("D" + pad.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture)
                    : "";
            }

            string value = name switch
            {
                "artist" => values.Artist,
                "title" => values.Title,
                "album" => values.Album,
                "albumartist" => values.AlbumArtist,
                "year" => values.Year > 0 ? values.Year.ToString(CultureInfo.InvariantCulture) : "",
                "disc" => values.DiscNumber > 0 ? values.DiscNumber.ToString(CultureInfo.InvariantCulture) : "",
                "genre" => values.Genre,
                _ => match.Value // unknown token: leave as-is
            };

            return ApplyCaseModifier(value, mod);
        });
    }

    private static string ApplyCaseModifier(string value, string mod)
    {
        if (string.IsNullOrEmpty(value) || string.IsNullOrEmpty(mod)) return value;
        return mod switch
        {
            "upper" => value.ToUpperInvariant(),
            "lower" => value.ToLowerInvariant(),
            "title" => CultureInfo.CurrentCulture.TextInfo.ToTitleCase(value.ToLowerInvariant()),
            _ => value
        };
    }

    private static string JoinParts(string separator, params string[] parts)
    {
        return string.Join(separator, parts.Where(p => !string.IsNullOrWhiteSpace(p)).Select(p => p.Trim()));
    }

    private static string SanitizeFileName(string value)
    {
        foreach (char c in Path.GetInvalidFileNameChars())
            value = value.Replace(c, '_');
        value = Regex.Replace(value, @"[_\s]{2,}", " ").Trim();
        return value.TrimEnd('.', ' ');
    }

    private static string SanitizePathSegment(string value, string fallback)
    {
        var segment = SanitizeFileName(string.IsNullOrWhiteSpace(value) ? fallback : value);
        segment = segment.Replace("..", "__")
                         .Replace(Path.DirectorySeparatorChar, '_')
                         .Replace(Path.AltDirectorySeparatorChar, '_');
        return string.IsNullOrWhiteSpace(segment) ? "_" : segment;
    }

    private static string SanitizeRelativeFolder(string value)
    {
        var parts = value.Split(new[] { '/', '\\' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                         .Select(p => SanitizePathSegment(p, "_"))
                         .Where(p => p != ".");
        return Path.Combine(parts.ToArray());
    }

    private static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "";
        return new string(value.ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());
    }

    internal sealed class RenameValues
    {
        public string Title { get; set; } = "";
        public string Artist { get; set; } = "";
        public string Album { get; set; } = "";
        public string AlbumArtist { get; set; } = "";
        public string Genre { get; set; } = "";
        public int TrackNumber { get; set; }
        public int DiscNumber { get; set; }
        public int Year { get; set; }

        /// <summary>
        /// Per-file working copy. BuildItem fills blanks in from the filename, and the cached
        /// instance has to stay as it was read from disk.
        /// </summary>
        public RenameValues Clone() => (RenameValues)MemberwiseClone();

        public static RenameValues FromFile(AudioFileInfo file)
        {
            var values = new RenameValues
            {
                Title = file.Title?.Trim() ?? "",
                Artist = file.Artist?.Trim() ?? "",
                Album = file.Album?.Trim() ?? "",
                TrackNumber = file.CueTrackNumber
            };

            if (!string.IsNullOrWhiteSpace(file.FilePath) && File.Exists(file.FilePath))
            {
                try
                {
                    using var tagFile = TagLib.File.Create(file.FilePath);
                    values.Title = First(values.Title, tagFile.Tag.Title);
                    values.Artist = First(values.Artist, string.Join("; ", tagFile.Tag.Performers));
                    values.Album = First(values.Album, tagFile.Tag.Album);
                    values.AlbumArtist = string.Join("; ", tagFile.Tag.AlbumArtists);
                    values.Genre = tagFile.Tag.FirstGenre ?? "";
                    values.TrackNumber = values.TrackNumber > 0 ? values.TrackNumber : (int)tagFile.Tag.Track;
                    values.DiscNumber = (int)tagFile.Tag.Disc;
                    values.Year = (int)tagFile.Tag.Year;
                }
                catch
                {
                }
            }

            return values;
        }

        private static string First(params string?[] values)
        {
            return values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v))?.Trim() ?? "";
        }
    }

    private sealed class AlbumContext
    {
        public bool IsAlbumGroup { get; init; }
        public bool IsMixedArtist { get; init; }

        public static AlbumContext FromSingle(AudioFileInfo file) => FromGroup(new[] { file });

        public static AlbumContext FromGroup(IReadOnlyList<AudioFileInfo> files)
        {
            var artists = files.Select(f => f.Artist?.Trim())
                .Where(a => !string.IsNullOrWhiteSpace(a))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            var albums = files.Select(f => f.Album?.Trim())
                .Where(a => !string.IsNullOrWhiteSpace(a))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            return new AlbumContext
            {
                IsAlbumGroup = files.Count > 1 && (albums.Count == 1 || files.Select(f => Path.GetDirectoryName(f.FilePath)).Distinct(StringComparer.OrdinalIgnoreCase).Count() == 1),
                IsMixedArtist = artists.Count > 1
            };
        }
    }

    private sealed class ParsedFilename
    {
        public string Artist { get; init; } = "";
        public string Title { get; init; } = "";
        public int TrackNumber { get; init; }
        public bool JunkRemoved { get; init; }
    }
}
