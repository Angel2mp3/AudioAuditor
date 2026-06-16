using System;
using System.Collections.Generic;
using System.Linq;
using AudioQualityChecker.Models;

namespace AudioQualityChecker.Services
{
    /// <summary>How duplicate tracks are matched.</summary>
    public enum DuplicateStrategy { Both, Metadata, SizeDuration }

    /// <summary>
    /// Headless duplicate-track finder shared by the desktop Duplicate Detection window and the
    /// CLI. Matches by metadata (artist + title, case-insensitive) and/or by exact rounded
    /// duration + file size. Metadata matches take precedence; size/duration groups are only
    /// added when none of their files already appeared in a metadata group.
    /// </summary>
    public static class DuplicateFinder
    {
        public static List<IGrouping<string, AudioFileInfo>> FindDuplicates(
            IReadOnlyList<AudioFileInfo> files,
            DuplicateStrategy strategy = DuplicateStrategy.Both)
        {
            var groups = new List<IGrouping<string, AudioFileInfo>>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (strategy is DuplicateStrategy.Both or DuplicateStrategy.Metadata)
            {
                var byMetadata = files
                    .Where(f => !string.IsNullOrWhiteSpace(f.Artist) && !string.IsNullOrWhiteSpace(f.Title))
                    .GroupBy(f => $"{f.Artist.Trim()} – {f.Title.Trim()}", StringComparer.OrdinalIgnoreCase)
                    .Where(g => g.Count() > 1);
                foreach (var g in byMetadata)
                {
                    groups.Add(g);
                    foreach (var f in g)
                        seen.Add(f.FilePath);
                }
            }

            if (strategy is DuplicateStrategy.Both or DuplicateStrategy.SizeDuration)
            {
                var bySize = files
                    .Where(f => f.DurationSeconds > 0 && f.FileSizeBytes > 0)
                    .GroupBy(f => $"{Math.Round(f.DurationSeconds, 1)}s / {f.FileSizeBytes}b")
                    .Where(g => g.Count() > 1);
                foreach (var g in bySize)
                {
                    if (g.All(f => !seen.Contains(f.FilePath)))
                        groups.Add(g);
                }
            }

            return groups;
        }
    }
}
