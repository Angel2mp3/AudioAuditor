using System;
using System.IO;
using System.Text.RegularExpressions;
using AudioQualityChecker.Models;

namespace AudioQualityChecker.Services
{
    /// <summary>
    /// Builds the target file names for the context-menu rename actions.
    ///
    /// Naming only — nothing here touches the disk, which is what makes it testable and what
    /// keeps the WPF and Avalonia menus producing byte-identical names.
    /// </summary>
    public static class RenameNaming
    {
        /// <summary>Leading track number such as "01 - ", "01. " or "1) ".</summary>
        private static readonly Regex TrackPrefix = new(@"^\s*\d{1,3}\s*[-.\)]\s+", RegexOptions.Compiled);

        private static readonly Regex RepeatedWhitespace = new(@"\s+", RegexOptions.Compiled);

        /// <summary>Short verdict word used in the quick-rename suffix.</summary>
        public static string StatusWord(AudioStatus status) => status switch
        {
            AudioStatus.Valid => "REAL",
            AudioStatus.Fake => "FAKE",
            AudioStatus.Corrupt => "CORRUPT",
            AudioStatus.Optimized => "OPTIMIZED",
            _ => "UNKNOWN"
        };

        /// <summary>
        /// Quick-rename target file name for <paramref name="file"/> under the given pattern,
        /// or null when the file lacks the bitrate the pattern needs and should be skipped.
        /// </summary>
        /// <param name="patternIndex">
        /// 0 = reported bitrate only, 1 = actual bitrate, 2 = both.
        /// </param>
        public static string? QuickRenameFileName(AudioFileInfo file, int patternIndex)
        {
            string status = StatusWord(file.Status);

            string suffix;
            switch (patternIndex)
            {
                case 0:
                    if (file.ReportedBitrate <= 0) return null;
                    suffix = $"[FAKE {file.ReportedBitrate}kbps]";
                    break;
                case 1:
                    if (file.ActualBitrate <= 0) return null;
                    suffix = $"[{status} {file.ActualBitrate}kbps]";
                    break;
                case 2:
                    if (file.ReportedBitrate <= 0 || file.ActualBitrate <= 0) return null;
                    suffix = $"[{status} {file.ReportedBitrate}kbps {file.ActualBitrate}kbps]";
                    break;
                default:
                    return null;
            }

            string name = Path.GetFileNameWithoutExtension(file.FilePath);
            string ext = Path.GetExtension(file.FilePath);
            return $"{name} {suffix}{ext}";
        }

        /// <summary>
        /// Canonical "Artist - Title.ext" (or "Title - Artist.ext") built from the file's tags,
        /// never from its current name. Returns null when either tag is missing — a guessed
        /// rename is worse than none — or when the file is already in the target form.
        ///
        /// A leading track number is preserved, so "03 - whatever.flac" stays track 3.
        /// </summary>
        public static string? AutoRenameFileName(AudioFileInfo file, bool artistFirst)
        {
            string artist = (file.Artist ?? "").Trim();
            string title = (file.Title ?? "").Trim();
            if (artist.Length == 0 || title.Length == 0) return null;

            string currentName = Path.GetFileNameWithoutExtension(file.FilePath);
            string ext = Path.GetExtension(file.FilePath);

            var prefixMatch = TrackPrefix.Match(currentName);
            string trackPrefix = prefixMatch.Success ? prefixMatch.Value : "";

            string body = SanitizeForFilename(artistFirst ? $"{artist} - {title}" : $"{title} - {artist}");
            string desired = $"{trackPrefix}{body}{ext}";

            // Compared case-sensitively on purpose: a file differing only in casing is still
            // worth renaming to the canonical form.
            return string.Equals(Path.GetFileName(file.FilePath), desired, StringComparison.Ordinal)
                ? null
                : desired;
        }

        /// <summary>
        /// Replaces characters the filesystem rejects with underscores and collapses the runs of
        /// whitespace that substitution tends to leave behind.
        /// </summary>
        public static string SanitizeForFilename(string value)
        {
            foreach (char c in Path.GetInvalidFileNameChars())
                value = value.Replace(c, '_');

            return RepeatedWhitespace.Replace(value, " ").Trim();
        }
    }
}
