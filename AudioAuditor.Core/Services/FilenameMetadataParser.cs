using System;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;

namespace AudioQualityChecker.Services;

/// <summary>
/// Parsed best-guess metadata extracted from a (often messy) audio file name.
/// </summary>
public readonly record struct ParsedFilenameMetadata(
    string Artist,
    string Title,
    int TrackNumber,
    bool JunkRemoved);

/// <summary>
/// Extracts artist / title / track-number guesses from a file name, stripping common
/// download/source junk (website domains, "official audio", bitrate tags, etc.).
/// Shared by <see cref="SmartRenameService"/> and <see cref="MetadataEnrichmentService"/> so
/// rename previews and online lookups parse names the same way.
/// </summary>
public static class FilenameMetadataParser
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

    public static ParsedFilenameMetadata Parse(string fileName)
    {
        string name = Path.GetFileNameWithoutExtension(fileName ?? "");
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

        return new ParsedFilenameMetadata(
            artist,
            title,
            trackNumber,
            !string.Equals(original, name, StringComparison.Ordinal));
    }

    private static string CleanRepeatedWhitespace(string value)
    {
        return Regex.Replace(value ?? "", @"\s+", " ").Trim();
    }
}
