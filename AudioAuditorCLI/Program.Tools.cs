using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using AudioQualityChecker.Models;
using AudioQualityChecker.Services;

namespace AudioQualityChecker.CLI
{
    partial class Program
    {
        // ═══════════════════════════════════════════
        //  Rename (batch / smart rename from tags)
        // ═══════════════════════════════════════════

        static int RunRename(string[] args)
        {
            if (args.Length == 0 || args.Contains("--help"))
            {
                Console.WriteLine(@"
USAGE: audioauditorcli rename <path> [options]

Batch-rename files from their tags. Shows a preview first; nothing is renamed
without your confirmation (or --dry-run to only preview).

OPTIONS:
  --style <s>         AlbumSafe (default), ArtistTitle, TitleArtist,
                      TrackArtistTitle, AlbumArtistTitle, Custom
  --pattern <p>       Custom name pattern, e.g. ""{artist} - {title}"" (sets --style Custom)
  --folder-mode <m>   KeepCurrent (default), ArtistAlbum, Album, Custom
  --folder-pattern <p> Custom folder pattern, e.g. ""{artist}/{album}"" (sets folder-mode Custom)
  --no-track-numbers  Don't prefix track numbers
  --rename-clean      Also rename files that are already clean
  --conflict <c>      On name clash: skip (default) or number
  --include-review    Also apply lower-confidence ('Review') matches
  --dry-run           Preview only, never rename
  -y, --yes           Apply without the confirmation prompt
  --no-recursive      Do not recurse into subfolders

EXAMPLES:
  audioauditorcli rename ""C:\Music\album"" --dry-run
  audioauditorcli rename ""C:\Music"" --style ArtistTitle --conflict number -y
");
                return 0;
            }

            var paths = new List<string>();
            var options = SmartRenameOptions.CreateDefault();
            bool recursive = true, dryRun = false, assumeYes = false, includeReview = false;

            for (int i = 0; i < args.Length; i++)
            {
                switch (args[i].ToLowerInvariant())
                {
                    case "--style" when i + 1 < args.Length:
                        if (Enum.TryParse<SmartRenameStyle>(args[++i], true, out var st)) options.Style = st;
                        else return Error($"Unknown --style: {args[i]}");
                        break;
                    case "--folder-mode" when i + 1 < args.Length:
                        if (Enum.TryParse<SmartRenameFolderMode>(args[++i], true, out var fm)) options.FolderMode = fm;
                        else return Error($"Unknown --folder-mode: {args[i]}");
                        break;
                    case "--pattern" when i + 1 < args.Length:
                        options.CustomPattern = args[++i]; options.Style = SmartRenameStyle.Custom; break;
                    case "--folder-pattern" when i + 1 < args.Length:
                        options.CustomFolderPattern = args[++i]; options.FolderMode = SmartRenameFolderMode.Custom; break;
                    case "--no-track-numbers": options.IncludeTrackNumbers = false; break;
                    case "--rename-clean": options.RenameCleanFiles = true; break;
                    case "--conflict" when i + 1 < args.Length:
                        string c = args[++i].ToLowerInvariant();
                        options.ConflictBehavior = c is "number" or "append" or "appendnumber"
                            ? SmartRenameConflictBehavior.AppendNumber : SmartRenameConflictBehavior.Skip;
                        break;
                    case "--include-review": includeReview = true; break;
                    case "--dry-run": dryRun = true; break;
                    case "--yes" or "-y": assumeYes = true; break;
                    case "--no-recursive": recursive = false; break;
                    case "--recursive" or "-r": recursive = true; break;
                    default:
                        if (!args[i].StartsWith("-")) paths.Add(args[i]);
                        break;
                }
            }

            if (paths.Count == 0) return Error("No input path specified.");
            var files = CollectFiles(paths, recursive);
            if (files.Count == 0) return Error("No supported audio files found.");

            Console.WriteLine($"Reading tags for {files.Count} file(s)...");
            ScanCacheService.EnsureLoaded();
            var results = AnalyzeFiles(files, CreateAnalysisSettingsSnapshot(),
                Math.Max(1, Environment.ProcessorCount / 2), true, 0, out _);
            ScanCacheService.SaveToDisk();

            var preview = SmartRenameService.BuildPreview(results, options);

            bool WillApply(SmartRenamePreviewItem it) =>
                it.Confidence == SmartRenameConfidence.High ||
                (includeReview && it.Confidence == SmartRenameConfidence.Review);

            Console.WriteLine();
            foreach (var item in preview)
            {
                if (item.Confidence == SmartRenameConfidence.Skip) continue;
                SetColor(item.Confidence == SmartRenameConfidence.High ? ConsoleColor.Green : ConsoleColor.Yellow);
                Console.Write($"  [{item.Confidence}]");
                ResetColor();
                Console.WriteLine($" {item.CurrentName}");
                Console.WriteLine($"        -> {item.NewName}");
                if (item.Warnings.Count > 0)
                    Console.WriteLine($"        ({string.Join(", ", item.Warnings)})");
            }

            int renamable = preview.Count(WillApply);
            int reviewCount = preview.Count(p => p.Confidence == SmartRenameConfidence.Review);
            int skipped = preview.Count(p => p.Confidence == SmartRenameConfidence.Skip);
            Console.WriteLine();
            Console.WriteLine($"  {renamable} to rename, {reviewCount} need review" +
                $"{(includeReview ? " (included)" : " — use --include-review to apply")}, {skipped} skipped.");

            if (dryRun)
            {
                Console.WriteLine("  [DRY RUN] No files changed.");
                CleanupTempDirs();
                return 0;
            }
            if (renamable == 0)
            {
                Console.WriteLine("  Nothing to rename.");
                CleanupTempDirs();
                return 0;
            }
            if (!assumeYes)
            {
                Console.Write($"  Apply {renamable} rename(s)? [y/N] ");
                string? ans = Console.ReadLine()?.Trim().ToLowerInvariant();
                if (ans != "y" && ans != "yes")
                {
                    Console.WriteLine("  Cancelled.");
                    CleanupTempDirs();
                    return 0;
                }
            }

            int done = 0, failed = 0;
            foreach (var item in preview)
            {
                if (!WillApply(item)) continue;
                try
                {
                    if (string.Equals(item.FilePath, item.TargetPath, StringComparison.OrdinalIgnoreCase)) continue;
                    string? dir = Path.GetDirectoryName(item.TargetPath);
                    if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                    if (File.Exists(item.TargetPath))
                    {
                        Console.Error.WriteLine($"  Skip (target exists): {item.NewName}");
                        failed++;
                        continue;
                    }
                    File.Move(item.FilePath, item.TargetPath);
                    done++;
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"  Failed: {item.CurrentName} — {ex.Message}");
                    failed++;
                }
            }
            Console.WriteLine($"  Renamed {done}, {failed} skipped/failed.");
            CleanupTempDirs();
            return 0;
        }

        // ═══════════════════════════════════════════
        //  Duplicates
        // ═══════════════════════════════════════════

        static int RunDuplicates(string[] args)
        {
            if (args.Length == 0 || args.Contains("--help"))
            {
                Console.WriteLine(@"
USAGE: audioauditorcli duplicates <path> [options]

Find duplicate tracks in a folder (by tags and/or by size+duration).

OPTIONS:
  --by <how>        Match by: both (default), metadata, size
  --no-recursive    Do not recurse into subfolders
");
                return 0;
            }

            var paths = new List<string>();
            bool recursive = true;
            var strategy = DuplicateStrategy.Both;

            for (int i = 0; i < args.Length; i++)
            {
                switch (args[i].ToLowerInvariant())
                {
                    case "--by" when i + 1 < args.Length:
                        strategy = args[++i].ToLowerInvariant() switch
                        {
                            "metadata" or "meta" or "tags" => DuplicateStrategy.Metadata,
                            "size" or "duration" or "sizeduration" => DuplicateStrategy.SizeDuration,
                            _ => DuplicateStrategy.Both
                        };
                        break;
                    case "--no-recursive": recursive = false; break;
                    case "--recursive" or "-r": recursive = true; break;
                    default:
                        if (!args[i].StartsWith("-")) paths.Add(args[i]);
                        break;
                }
            }

            if (paths.Count == 0) return Error("No input path specified.");
            var files = CollectFiles(paths, recursive);
            if (files.Count == 0) return Error("No supported audio files found.");

            Console.WriteLine($"Analyzing {files.Count} file(s) for duplicates...");
            ScanCacheService.EnsureLoaded();
            var results = AnalyzeFiles(files, CreateAnalysisSettingsSnapshot(),
                Math.Max(1, Environment.ProcessorCount / 2), true, 0, out _);
            ScanCacheService.SaveToDisk();

            var groups = DuplicateFinder.FindDuplicates(results, strategy);
            Console.WriteLine();
            if (groups.Count == 0)
            {
                Console.WriteLine("  No duplicates found.");
                CleanupTempDirs();
                return 0;
            }

            int n = 0;
            foreach (var g in groups)
            {
                n++;
                SetColor(ConsoleColor.Cyan);
                Console.WriteLine($"  Group {n} ({g.Count()} files): {g.Key}");
                ResetColor();
                foreach (var f in g)
                    Console.WriteLine($"      {f.FilePath}");
            }
            Console.WriteLine();
            Console.WriteLine($"  {groups.Count} duplicate group(s) across {groups.Sum(g => g.Count())} files.");
            CleanupTempDirs();
            return 0;
        }

        // ═══════════════════════════════════════════
        //  Identify (AcoustID fingerprint)
        // ═══════════════════════════════════════════

        static async Task<int> RunIdentify(string[] args)
        {
            if (args.Length == 0 || args.Contains("--help"))
            {
                Console.WriteLine(@"
USAGE: audioauditorcli identify <file> [options]

Identify a track by its audio fingerprint (AcoustID + MusicBrainz).

OPTIONS:
  --api-key <key>   AcoustID API key (or set ACOUSTID_API_KEY).
                    Free key: https://acoustid.org/new-application
  --apply           Write the top match's title/artist/album to the file's tags

Note: on Linux/macOS the 'fpcalc' tool (chromaprint) must be on your PATH.
");
                return 0;
            }

            string? file = null, apiKey = null;
            bool apply = false;
            for (int i = 0; i < args.Length; i++)
            {
                switch (args[i].ToLowerInvariant())
                {
                    case "--api-key" when i + 1 < args.Length: apiKey = args[++i]; break;
                    case "--apply": apply = true; break;
                    default:
                        if (!args[i].StartsWith("-") && file == null) file = args[i];
                        break;
                }
            }

            if (file == null) return Error("No file specified. Usage: identify <file> [--api-key <key>]");
            string filePath = Path.GetFullPath(file);
            if (!File.Exists(filePath)) return Error($"File not found: {filePath}");

            apiKey ??= Environment.GetEnvironmentVariable("ACOUSTID_API_KEY");
            if (string.IsNullOrWhiteSpace(apiKey))
                return Error("No AcoustID API key. Pass --api-key <key> or set the ACOUSTID_API_KEY " +
                             "environment variable.\n  Get a free key at https://acoustid.org/new-application");

            Console.WriteLine($"Fingerprinting {Path.GetFileName(filePath)}...");
            List<AcoustIdResult> results;
            try
            {
                results = await AcoustIdService.Identify(filePath, apiKey);
            }
            catch (Exception ex)
            {
                return Error($"Identify failed: {ex.Message}");
            }

            if (results.Count == 0)
            {
                ColorWriteLine(ConsoleColor.Yellow, "  No match found.");
                if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                    Console.WriteLine("  Note: AcoustID needs the 'fpcalc' tool (chromaprint) on your PATH on Linux/macOS.");
                return 0;
            }

            Console.WriteLine();
            foreach (var r in results.Take(5).Select((r, idx) => (r, idx)))
            {
                SetColor(r.idx == 0 ? ConsoleColor.Green : ConsoleColor.Gray);
                Console.Write($"  [{r.r.Score:P0}]");
                ResetColor();
                string album = string.IsNullOrEmpty(r.r.Album) ? "" : $" ({r.r.Album})";
                Console.WriteLine($" {r.r.Artist} — {r.r.Title}{album}");
                if (!string.IsNullOrEmpty(r.r.MusicBrainzRecordingId))
                    Console.WriteLine($"         MBID: {r.r.MusicBrainzRecordingId}");
            }

            if (apply)
            {
                var best = results[0];
                try
                {
                    using var tagFile = TagLib.File.Create(filePath);
                    if (!string.IsNullOrEmpty(best.Title)) tagFile.Tag.Title = best.Title;
                    if (!string.IsNullOrEmpty(best.Artist)) tagFile.Tag.Performers = new[] { best.Artist };
                    if (!string.IsNullOrEmpty(best.Album)) tagFile.Tag.Album = best.Album;
                    tagFile.Save();
                    Console.WriteLine("  Applied top match to tags.");
                }
                catch (Exception ex)
                {
                    return Error($"Failed to write tags: {ex.Message}");
                }
            }
            return 0;
        }

        // ═══════════════════════════════════════════
        //  Metadata enrich (online auto-fill) — invoked from RunMetadata
        // ═══════════════════════════════════════════

        static int RunMetadataEnrich(string path, string[] args)
            => RunMetadataEnrichAsync(path, args).GetAwaiter().GetResult();

        static async Task<int> RunMetadataEnrichAsync(string path, string[] args)
        {
            bool recursive = true, dryRun = false, all = false, useAcoustId = false, assumeYes = false;
            string? apiKey = null;
            for (int i = 0; i < args.Length; i++)
            {
                switch (args[i].ToLowerInvariant())
                {
                    case "--dry-run": dryRun = true; break;
                    case "--all": all = true; break;
                    case "--acoustid": useAcoustId = true; break;
                    case "--api-key" when i + 1 < args.Length: apiKey = args[++i]; break;
                    case "--yes" or "-y": assumeYes = true; break;
                    case "--no-recursive": recursive = false; break;
                    case "--recursive" or "-r": recursive = true; break;
                }
            }

            var files = CollectFiles(new List<string> { path }, recursive);
            if (files.Count == 0) return Error("No supported audio files found.");

            Console.WriteLine($"Reading {files.Count} file(s)...");
            ScanCacheService.EnsureLoaded();
            var results = AnalyzeFiles(files, CreateAnalysisSettingsSnapshot(),
                Math.Max(1, Environment.ProcessorCount / 2), true, 0, out _);
            ScanCacheService.SaveToDisk();

            var service = new MetadataEnrichmentService();
            var options = MetadataEnrichmentOptions.CreateDefault();
            options.MissingOnly = !all;
            if (useAcoustId)
            {
                apiKey ??= Environment.GetEnvironmentVariable("ACOUSTID_API_KEY");
                if (string.IsNullOrWhiteSpace(apiKey))
                    return Error("--acoustid needs --api-key <key> or the ACOUSTID_API_KEY environment variable.");
                options.UseAcoustId = true;
                options.AcoustIdApiKey = apiKey;
            }

            Console.WriteLine("Searching online metadata (MusicBrainz, Cover Art Archive)...");
            var previews = await service.PreviewAsync(results, options);

            var toApply = new List<MetadataEnrichmentChange>();
            int filesWithChanges = 0;
            foreach (var p in previews)
            {
                if (p.Changes.Count == 0) continue;
                filesWithChanges++;
                Console.WriteLine();
                SetColor(ConsoleColor.Cyan);
                Console.WriteLine($"  {p.File.FileName}  [{p.Status}]");
                ResetColor();
                bool confident = p.Confidence >= MetadataEnrichmentService.ReviewConfidenceThreshold;
                foreach (var ch in p.Changes)
                {
                    string oldV = string.IsNullOrEmpty(ch.OldValue) ? "(empty)" : ch.OldValue;
                    Console.WriteLine($"      {ch.Field}: \"{oldV}\" -> \"{ch.NewValue}\"  ({ch.Provider})");
                    if (confident)
                    {
                        ch.IsSelected = true;
                        toApply.Add(ch);
                    }
                }
            }

            Console.WriteLine();
            if (filesWithChanges == 0)
            {
                Console.WriteLine("  No metadata to add.");
                CleanupTempDirs();
                return 0;
            }
            Console.WriteLine($"  {toApply.Count} change(s) to apply across {filesWithChanges} file(s).");

            if (dryRun)
            {
                Console.WriteLine("  [DRY RUN] No files changed.");
                CleanupTempDirs();
                return 0;
            }
            if (toApply.Count == 0)
            {
                Console.WriteLine("  Nothing confident enough to apply automatically.");
                CleanupTempDirs();
                return 0;
            }
            if (!assumeYes)
            {
                Console.Write($"  Write {toApply.Count} tag change(s)? [y/N] ");
                string? ans = Console.ReadLine()?.Trim().ToLowerInvariant();
                if (ans != "y" && ans != "yes")
                {
                    Console.WriteLine("  Cancelled.");
                    CleanupTempDirs();
                    return 0;
                }
            }

            var summary = await service.ApplyAsync(toApply, createBackups: false);
            Console.WriteLine($"  Applied {summary.ChangesApplied} change(s) to {summary.FilesChanged} file(s)" +
                (summary.FailedFiles > 0 ? $", {summary.FailedFiles} failed" : "") + ".");
            foreach (var e in summary.Errors)
                Console.Error.WriteLine($"    {e}");
            CleanupTempDirs();
            return 0;
        }
    }
}
