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
    private static readonly Regex LeadingTrackRegex = new(
        @"^\s*(?:disc\s*\d+\s*)?(?:track\s*)?(?<track>\d{1,3})\s*(?:[-._)]\s*)?",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex DomainRegex = new(
        @"(?ix)\b(?:https?://)?(?:www\.)?[a-z0-9][a-z0-9-]*(?:\.[a-z0-9][a-z0-9-]*)+(?:/\S*)?\b",
        RegexOptions.Compiled);

    private static readonly Regex JunkBracketRegex = new(
        @"(?ix)[\[\(]\s*(?:official\s+audio|audio\s+only|lyrics?|lyric\s+video|320\s*kbps|256\s*kbps|192\s*kbps|128\s*kbps|mp3|flac|wav|ytmp3|youtube|download|free\s+download)\s*[\]\)]",
        RegexOptions.Compiled);

    private static readonly Regex LooseJunkRegex = new(
        @"(?ix)\b(?:official\s+audio|audio\s+only|lyrics?|lyric\s+video|ytmp3|youtube\s+music|youtube|download|free\s+download|soundcloud|spotify|deezer|tidal|qobuz|bandcamp|mp3\s*download)\b",
        RegexOptions.Compiled);

    public static IReadOnlyList<SmartRenamePreviewItem> BuildPreview(
        IReadOnlyList<AudioFileInfo> files,
        SmartRenameOptions options,
        Func<string, bool>? targetExists = null)
    {
        options ??= SmartRenameOptions.CreateDefault();
        targetExists ??= File.Exists;
        var contexts = BuildAlbumContexts(files);
        var usedTargets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<SmartRenamePreviewItem>(files.Count);

        foreach (var file in files)
        {
            var preview = BuildItem(file, files, contexts, options);
            ResolveTarget(preview, options, usedTargets, targetExists);
            result.Add(preview);
        }

        return result;
    }

    private static SmartRenamePreviewItem BuildItem(
        AudioFileInfo file,
        IReadOnlyList<AudioFileInfo> files,
        Dictionary<string, AlbumContext> contexts,
        SmartRenameOptions options)
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

        var values = RenameValues.FromFile(file);
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

        if (!options.RenameCleanFiles && string.Equals(currentName, relativeTarget, StringComparison.OrdinalIgnoreCase))
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
        string track = values.TrackNumber > 0
            ? values.TrackNumber.ToString("D2", CultureInfo.InvariantCulture)
            : "";

        return options.Style switch
        {
            SmartRenameStyle.ArtistTitle => JoinParts(" - ", values.Artist, values.Title),
            SmartRenameStyle.TitleArtist => JoinParts(" - ", values.Title, values.Artist),
            SmartRenameStyle.TrackArtistTitle => JoinParts(" - ", track, values.Artist, values.Title),
            SmartRenameStyle.AlbumArtistTitle => JoinParts(" - ", values.Album, values.Artist, values.Title),
            SmartRenameStyle.Custom => ApplyPattern(options.CustomPattern, values),
            _ => BuildAlbumSafeName(values, context, options, track)
        };
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
            SmartRenameFolderMode.Custom => Path.Combine(SanitizeRelativeFolder(ApplyPattern(options.CustomFolderPattern, values)), fileName),
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
        string name = Path.GetFileNameWithoutExtension(fileName);
        string original = name;
        name = name.Replace('_', ' ')
                   .Replace('|', '-')
                   .Replace('–', '-')
                   .Replace('—', '-');

        int trackNumber = 0;
        var trackMatch = LeadingTrackRegex.Match(name);
        if (trackMatch.Success)
        {
            _ = int.TryParse(trackMatch.Groups["track"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out trackNumber);
            name = name[trackMatch.Length..];
        }

        name = DomainRegex.Replace(name, " ");
        name = JunkBracketRegex.Replace(name, " ");
        name = LooseJunkRegex.Replace(name, " ");
        name = Regex.Replace(name, @"\b\d{2,4}\s*kbps\b", " ", RegexOptions.IgnoreCase);
        name = Regex.Replace(name, @"\s+", " ").Trim(' ', '-', '.', '_');
        if (trackNumber <= 0)
        {
            trackMatch = LeadingTrackRegex.Match(name);
            if (trackMatch.Success)
            {
                _ = int.TryParse(trackMatch.Groups["track"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out trackNumber);
                name = name[trackMatch.Length..].Trim(' ', '-', '.', '_');
            }
        }

        var parts = name.Split('-', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        string artist = "";
        string title = "";

        if (parts.Length >= 3)
        {
            artist = parts[^2];
            title = parts[^1];
        }
        else if (parts.Length == 2)
        {
            artist = parts[0];
            title = parts[1];
        }
        else if (parts.Length == 1)
        {
            title = parts[0];
        }

        artist = CleanRepeatedWhitespace(artist);
        title = CleanRepeatedWhitespace(title);

        return new ParsedFilename
        {
            Artist = artist,
            Title = title,
            TrackNumber = trackNumber,
            JunkRemoved = !string.Equals(original, name, StringComparison.Ordinal)
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

    private static string ApplyPattern(string pattern, RenameValues values)
    {
        return (pattern ?? "")
            .Replace("{artist}", values.Artist, StringComparison.OrdinalIgnoreCase)
            .Replace("{title}", values.Title, StringComparison.OrdinalIgnoreCase)
            .Replace("{album}", values.Album, StringComparison.OrdinalIgnoreCase)
            .Replace("{albumArtist}", values.AlbumArtist, StringComparison.OrdinalIgnoreCase)
            .Replace("{year}", values.Year > 0 ? values.Year.ToString(CultureInfo.InvariantCulture) : "", StringComparison.OrdinalIgnoreCase)
            .Replace("{track}", values.TrackNumber > 0 ? values.TrackNumber.ToString("D2", CultureInfo.InvariantCulture) : "", StringComparison.OrdinalIgnoreCase)
            .Replace("{disc}", values.DiscNumber > 0 ? values.DiscNumber.ToString(CultureInfo.InvariantCulture) : "", StringComparison.OrdinalIgnoreCase);
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

    private static string CleanRepeatedWhitespace(string value)
    {
        return Regex.Replace(value ?? "", @"\s+", " ").Trim();
    }

    private static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "";
        return new string(value.ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());
    }

    private sealed class RenameValues
    {
        public string Title { get; set; } = "";
        public string Artist { get; set; } = "";
        public string Album { get; set; } = "";
        public string AlbumArtist { get; set; } = "";
        public int TrackNumber { get; set; }
        public int DiscNumber { get; set; }
        public int Year { get; set; }

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
