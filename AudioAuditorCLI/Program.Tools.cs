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
                    var outcome = FileRenamer.Rename(item.FilePath, item.TargetPath);
                    if (outcome == RenameOutcome.TargetExists)
                    {
                        Console.Error.WriteLine($"  Skip (target exists): {item.NewName}");
                        failed++;
                        continue;
                    }
                    if (outcome == RenameOutcome.Unchanged) continue;
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

        /// <summary>
        /// Lists or applies the <c>.audioauditor-backup-*</c> copies taken before past tag writes.
        /// The GUI equivalent is Windows/RestoreBackupWindow; both go through FileRenamer so they
        /// agree on which copy counts as "the original".
        /// </summary>
        static int RunMetadataRestore(string path, bool listOnly, string[] args)
        {
            bool recursive = true, dryRun = false, assumeYes = false, newest = false;
            for (int i = 0; i < args.Length; i++)
            {
                switch (args[i].ToLowerInvariant())
                {
                    case "--dry-run": dryRun = true; break;
                    case "--yes" or "-y": assumeYes = true; break;
                    case "--newest": newest = true; break;
                    case "--no-recursive": recursive = false; break;
                    case "--recursive" or "-r": recursive = true; break;
                }
            }

            var files = CollectFiles(new List<string> { path }, recursive);
            if (files.Count == 0) return Error("No supported audio files found.");

            // One entry per file: the backup that would be restored, plus how many exist.
            var candidates = new List<(string file, string backup, int total)>();
            foreach (string file in files)
            {
                var backups = FileRenamer.FindBackups(file);
                if (backups.Count == 0) continue;
                // FindBackups is oldest first; the oldest predates every edit, so it is the default.
                candidates.Add((file, newest ? backups[^1] : backups[0], backups.Count));
            }

            if (candidates.Count == 0)
            {
                Console.WriteLine($"  No backups found for {files.Count} file(s).");
                Console.WriteLine("  Backups are only written when the --backup flag (or the GUI's " +
                                  "\"Back up first\" box) was used for the edit.");
                return 0;
            }

            Console.WriteLine($"  {candidates.Count} file(s) with backups:");
            foreach (var (file, backup, total) in candidates)
            {
                string stamp = BackupTimestamp(backup);
                string extra = total > 1 ? $"  ({total} backups, using the {(newest ? "newest" : "oldest")})" : "";
                Console.WriteLine($"    {Path.GetFileName(file)}  <-  {stamp}{extra}");
            }

            if (listOnly) return 0;

            if (dryRun)
            {
                Console.WriteLine("  [DRY RUN] No files changed.");
                return 0;
            }

            if (!assumeYes)
            {
                Console.Write($"  Overwrite {candidates.Count} file(s) with these backups? [y/N] ");
                string? ans = Console.ReadLine()?.Trim().ToLowerInvariant();
                if (ans != "y" && ans != "yes")
                {
                    Console.WriteLine("  Cancelled.");
                    return 0;
                }
            }

            int restored = 0, failed = 0;
            foreach (var (file, backup, _) in candidates)
            {
                try
                {
                    var outcome = FileRenamer.Restore(backup, file);
                    if (outcome == RestoreOutcome.Restored) { restored++; continue; }

                    failed++;
                    Console.Error.WriteLine($"    {Path.GetFileName(file)}: {outcome}");
                }
                catch (Exception ex)
                {
                    failed++;
                    Console.Error.WriteLine($"    {Path.GetFileName(file)}: {ex.Message}");
                }
            }

            Console.WriteLine($"  Restored {restored} file(s)" + (failed > 0 ? $", {failed} failed" : "") + ".");
            Console.WriteLine("  The backup copies were kept.");
            return failed > 0 ? 1 : 0;
        }

        /// <summary>The UTC stamp embedded in a backup's name, or its filename when unparseable.</summary>
        static string BackupTimestamp(string backupPath)
        {
            string name = Path.GetFileName(backupPath);
            int at = name.LastIndexOf(FileRenamer.BackupSuffix, StringComparison.OrdinalIgnoreCase);
            if (at < 0) return name;

            string stamp = name[(at + FileRenamer.BackupSuffix.Length)..];
            return DateTime.TryParseExact(stamp, "yyyyMMddHHmmss",
                    System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.AssumeUniversal |
                    System.Globalization.DateTimeStyles.AdjustToUniversal, out var parsed)
                ? parsed.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss")
                : name;
        }

        static async Task<int> RunMetadataEnrichAsync(string path, string[] args)
        {
            bool recursive = true, dryRun = false, all = false, useAcoustId = false, assumeYes = false;
            bool useDeezer = false, useTheAudioDb = false;
            // Match the GUI: only high-confidence matches are written unless the user opts in.
            bool includeUncertain = false, backup = false;
            string? apiKey = null, discogsToken = null, fanartKey = null;
            string? streamingLink = null, spotifyId = null, spotifySecret = null, youTubeKey = null;
            for (int i = 0; i < args.Length; i++)
            {
                switch (args[i].ToLowerInvariant())
                {
                    case "--dry-run": dryRun = true; break;
                    case "--all": all = true; break;
                    case "--acoustid": useAcoustId = true; break;
                    case "--api-key" when i + 1 < args.Length: apiKey = args[++i]; break;
                    case "--deezer": useDeezer = true; break;
                    case "--theaudiodb": useTheAudioDb = true; break;
                    case "--discogs-token" when i + 1 < args.Length: discogsToken = args[++i]; break;
                    case "--fanarttv-key" when i + 1 < args.Length: fanartKey = args[++i]; break;
                    case "--streaming-link" when i + 1 < args.Length: streamingLink = args[++i]; break;
                    case "--spotify-id" when i + 1 < args.Length: spotifyId = args[++i]; break;
                    case "--spotify-secret" when i + 1 < args.Length: spotifySecret = args[++i]; break;
                    case "--youtube-key" when i + 1 < args.Length: youTubeKey = args[++i]; break;
                    case "--yes" or "-y": assumeYes = true; break;
                    case "--include-uncertain": includeUncertain = true; break;
                    case "--backup": backup = true; break;
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
            options.UseDeezer = useDeezer;
            options.UseTheAudioDb = useTheAudioDb;
            if (useAcoustId)
            {
                apiKey ??= Environment.GetEnvironmentVariable("ACOUSTID_API_KEY");
                if (string.IsNullOrWhiteSpace(apiKey))
                    return Error("--acoustid needs --api-key <key> or the ACOUSTID_API_KEY environment variable.");
                options.UseAcoustId = true;
                options.AcoustIdApiKey = apiKey;
            }

            discogsToken ??= Environment.GetEnvironmentVariable("DISCOGS_TOKEN");
            if (!string.IsNullOrWhiteSpace(discogsToken))
            {
                options.UseDiscogs = true;
                options.DiscogsToken = discogsToken;
            }
            fanartKey ??= Environment.GetEnvironmentVariable("FANARTTV_API_KEY");
            if (!string.IsNullOrWhiteSpace(fanartKey))
            {
                options.UseFanartTv = true;
                options.FanartTvApiKey = fanartKey;
            }

            // Streaming link → Comment is opt-in via --streaming-link <platform>; otherwise leave it off.
            if (string.IsNullOrWhiteSpace(streamingLink))
            {
                options.EnabledFields.Remove(MetadataEnrichmentField.StreamingLink);
            }
            else
            {
                options.StreamingLinkPlatform = streamingLink.Trim().ToLowerInvariant() switch
                {
                    "apple" or "applemusic" or "itunes" => StreamingLinkPlatform.Apple,
                    "spotify" => StreamingLinkPlatform.Spotify,
                    "youtube" or "yt" => StreamingLinkPlatform.YouTube,
                    _ => StreamingLinkPlatform.Deezer
                };
                options.SpotifyClientId = spotifyId ?? Environment.GetEnvironmentVariable("SPOTIFY_CLIENT_ID") ?? "";
                options.SpotifyClientSecret = spotifySecret ?? Environment.GetEnvironmentVariable("SPOTIFY_CLIENT_SECRET") ?? "";
                options.YouTubeApiKey = youTubeKey ?? Environment.GetEnvironmentVariable("YOUTUBE_API_KEY") ?? "";
            }

            var sources = new List<string> { "MusicBrainz", "Cover Art Archive" };
            if (options.UseDeezer) sources.Add("Deezer");
            if (options.UseTheAudioDb) sources.Add("TheAudioDB");
            if (options.UseITunes) sources.Add("Apple/iTunes");
            if (options.UseAcoustId) sources.Add("AcoustID");
            if (options.UseDiscogs) sources.Add("Discogs");
            if (options.UseFanartTv) sources.Add("fanart.tv");
            Console.WriteLine($"Searching online metadata ({string.Join(", ", sources)})...");
            Console.WriteLine();

            // Live per-file result lines as each file finishes (serialised so they don't interleave).
            var consoleLock = new object();
            var failures = new List<string>();
            // Not Progress<T>: with no SynchronizationContext (there never is one in a console app)
            // it queues each callback to the thread pool, so these per-file lines raced the main
            // thread and printed *after* the "No metadata to add." summary. Reporting inline keeps
            // the transcript in order; consoleLock still serialises the concurrent workers.
            var progress = new InlineProgress<EnrichmentProgress>(p =>
            {
                lock (consoleLock)
                {
                    switch (p.Outcome)
                    {
                        case EnrichmentOutcome.Matched: SetColor(ConsoleColor.Green); Console.Write("  ✓ "); break;
                        case EnrichmentOutcome.LowConfidence: SetColor(ConsoleColor.Yellow); Console.Write("  ~ "); break;
                        case EnrichmentOutcome.NoMatch: SetColor(ConsoleColor.DarkGray); Console.Write("  ✗ "); break;
                        default: SetColor(ConsoleColor.Red); Console.Write("  ! "); break;
                    }
                    ResetColor();
                    Console.WriteLine($"[{p.Done}/{p.Total}] {p.FileName} — {p.Message}");
                    if (p.Outcome is EnrichmentOutcome.NoMatch or EnrichmentOutcome.Error)
                        failures.Add($"{p.FileName} — {p.Message}");
                }
            });

            var previews = await service.PreviewAsync(results, options, progress);

            Console.WriteLine();
            if (failures.Count > 0)
            {
                SetColor(ConsoleColor.Yellow);
                Console.WriteLine($"  {failures.Count} file(s) could not be matched:");
                ResetColor();
                foreach (var f in failures)
                    Console.WriteLine($"    - {f}");
                Console.WriteLine();
            }

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
                // Same bar as the GUI's "Apply High-Confidence". This used to sit at the review
                // threshold, so `aa tag` wrote matches the GUI would have held back for a human.
                bool confident = p.Confidence >= (includeUncertain
                    ? MetadataEnrichmentService.ReviewConfidenceThreshold
                    : MetadataEnrichmentService.HighConfidenceThreshold);
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
                if (!includeUncertain)
                    Console.WriteLine("  Re-run with --include-uncertain to also write matches that need review.");
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

            var summary = await service.ApplyAsync(toApply, createBackups: backup);
            Console.WriteLine($"  Applied {summary.ChangesApplied} change(s) to {summary.FilesChanged} file(s)" +
                (summary.FailedFiles > 0 ? $", {summary.FailedFiles} failed" : "") + ".");
            foreach (var e in summary.Errors)
                Console.Error.WriteLine($"    {e}");
            CleanupTempDirs();
            return 0;
        }

        /// <summary>
        /// <see cref="IProgress{T}"/> that runs the handler on the thread that reported, instead of
        /// posting it to the thread pool the way <see cref="Progress{T}"/> does when no
        /// <see cref="System.Threading.SynchronizationContext"/> exists. Console output has to stay
        /// in the order it was written.
        /// </summary>
        private sealed class InlineProgress<T> : IProgress<T>
        {
            private readonly Action<T> _handler;
            public InlineProgress(Action<T> handler) => _handler = handler;
            public void Report(T value) => _handler(value);
        }
    }
}
