using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text.Json;
using SharpCompress.Archives;
using SharpCompress.Common;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SkiaSharp;
using AudioQualityChecker.Models;
using AudioQualityChecker.Services;

namespace AudioQualityChecker.CLI
{
    partial class Program
    {        // ═══════════════════════════════════════════
        //  Credits
        // ═══════════════════════════════════════════

        /// <summary>
        /// Runs the Core self-checks — smoke-tests the tag/rename/junk/AI-scoring logic against the
        /// build in hand. Listed in --help because the changelog tells users to run it to confirm a
        /// portable download isn't corrupt.
        /// </summary>
        /// <summary>
        /// Guards <see cref="RejoinArgs"/>, which reassembles an unquoted path the shell already
        /// split on spaces. Two ways it has broken: a "-" inside "Artist - Title.flac" read as a
        /// flag, and a real flag swallowed into the path. Both fail silently as "Path not found",
        /// so the check uses real temp files — the rejoin logic probes the filesystem.
        /// </summary>
        static void ArgParsingSelfCheck()
        {
            string dir = Path.Combine(Path.GetTempPath(), "aa-argcheck-" + Guid.NewGuid().ToString("N"));
            string nested = Path.Combine(dir, "My Music");
            Directory.CreateDirectory(nested);
            string file = Path.Combine(nested, "Some Artist - A Title.flac");
            File.WriteAllBytes(file, Array.Empty<byte>());

            try
            {
                // How the shell hands over `analyze <dir>\My Music\Some Artist - A Title.flac -v`.
                var rejoined = RejoinArgs(new[]
                {
                    "analyze", Path.Combine(dir, "My"), "Music\\Some", "Artist", "-", "A", "Title.flac", "-v"
                });

                if (rejoined.Length != 3)
                    throw new Exception($"expected command + path + flag, got [{string.Join("] [", rejoined)}]");
                if (rejoined[1] != file)
                    throw new Exception($"path not reassembled: '{rejoined[1]}' != '{file}'");
                if (rejoined[2] != "-v")
                    throw new Exception($"flag lost: '{rejoined[2]}'");

                if (!IsFlagToken("--json") || !IsFlagToken("-o"))
                    throw new Exception("real flags must be recognised");
                if (IsFlagToken("-") || IsFlagToken("--"))
                    throw new Exception("a bare dash is path text, not a flag");
            }
            finally
            {
                try { Directory.Delete(dir, recursive: true); } catch { /* temp cleanup is best-effort */ }
            }
        }

        static int RunSelfCheck(string[] args)
        {
            var failures = new List<string>(AudioQualityChecker.Services.SelfChecks.RunAll());
            try { ArgParsingSelfCheck(); }
            catch (Exception ex) { failures.Add($"ArgParsing: {ex.Message}"); }

            if (failures.Count == 0)
            {
                SetColor(ConsoleColor.Green);
                Console.WriteLine("  All self-checks passed.");
                ResetColor();
                return 0;
            }

            SetColor(ConsoleColor.Red);
            Console.WriteLine($"  {failures.Count} self-check failure(s):");
            ResetColor();
            foreach (var failure in failures)
                Console.WriteLine($"    {failure}");
            return 1;
        }

        static int RunCredits(string[] args)
        {
            if (args.Contains("--help"))
            {
                Console.WriteLine(@"
USAGE: audioauditorcli credits

Show the open-source libraries AudioAuditor ships with, their authors,
licenses, and project links. Covers the whole project, so some entries
(WPF, Discord Rich Presence) belong to the Windows GUI rather than the CLI.
");
                return 0;
            }

            Console.WriteLine();
            Console.WriteLine("  Open-Source Credits & Licenses");
            Console.WriteLine("  ───────────────────────────────");
            Console.WriteLine("  Everything AudioAuditor ships, across both the CLI and the Windows GUI.");
            Console.WriteLine();

            foreach (var credit in OpenSourceCredit.All)
            {
                SetColor(ConsoleColor.Cyan);
                Console.Write($"  {credit.Name}");
                ResetColor();
                SetColor(ConsoleColor.DarkGray);
                Console.WriteLine($"  [{credit.License}]");
                ResetColor();
                Console.WriteLine($"    by {credit.By}");
                Console.WriteLine($"    {credit.Usage}");
                SetColor(ConsoleColor.DarkCyan);
                Console.WriteLine($"    {credit.Url}");
                ResetColor();
                Console.WriteLine();
            }

            return 0;
        }

        // ═══════════════════════════════════════════
        //  CD Rip Checker (checklog)
        // ═══════════════════════════════════════════

        static async Task<int> RunCheckLog(string[] args)
        {
            var paths = args.Where(a => !a.StartsWith("-")).ToArray();
            if (paths.Length == 0 || args.Contains("--help"))
            {
                Console.WriteLine(@"
USAGE: audioauditorcli checklog <log-or-folder> [options]

Score a CD ripping log (EAC / XLD / whipper) using the bundled cambia checker.
Pass a .log file, or a folder to check the first rip log found inside it.

OPTIONS:
  --json            Output the raw cambia JSON instead of a formatted report
  --help            Show this help

ALIASES: riplog
");
                return 0;
            }

            if (!RipLogCheckService.IsAvailable)
                return Error("cambia was not found. It ships with the Windows build under third-party/cambia/.");

            bool json = args.Contains("--json");
            int worst = 0;

            foreach (var path in paths)
            {
                RipLogResult? result;
                if (Directory.Exists(path))
                    result = await RipLogCheckService.CheckFolderAsync(path);
                else
                    result = await RipLogCheckService.CheckLogAsync(path);

                if (result == null)
                {
                    SetColor(ConsoleColor.DarkGray);
                    Console.WriteLine($"  {path}: no rip log found");
                    ResetColor();
                    continue;
                }

                if (json)
                {
                    Console.WriteLine(JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
                    continue;
                }

                Console.WriteLine();
                Console.WriteLine($"  {Path.GetFileName(result.SourceFile)}");
                if (!result.IsParsed)
                {
                    SetColor(ConsoleColor.Yellow);
                    Console.WriteLine($"    {result.Error}");
                    ResetColor();
                    worst = Math.Max(worst, 1);
                    continue;
                }

                var verdictColor = result.Verdict switch
                {
                    "Perfect" or "Good" => ConsoleColor.Green,
                    "Suspect" => ConsoleColor.Yellow,
                    _ => ConsoleColor.Red
                };
                SetColor(verdictColor);
                Console.WriteLine($"    Score: {result.Score}/100  ({result.Verdict})");
                ResetColor();
                if (!string.IsNullOrEmpty(result.Ripper))
                    Console.WriteLine($"    Ripper: {result.Ripper} {result.RipperVersion}".TrimEnd());
                if (!string.IsNullOrEmpty(result.Drive))
                    Console.WriteLine($"    Drive: {result.Drive}");

                if (result.Deductions.Count > 0)
                {
                    Console.WriteLine("    Findings:");
                    foreach (var d in result.Deductions)
                    {
                        var c = d.Class switch
                        {
                            "Critical" or "Bad" => ConsoleColor.Red,
                            "Neutral" => ConsoleColor.DarkGray,
                            _ => ConsoleColor.Green
                        };
                        SetColor(c);
                        Console.WriteLine($"      [{d.Score}] {d.Message}");
                        ResetColor();
                    }
                }

                if (result.Verdict == "Bad") worst = Math.Max(worst, 1);
            }

            return worst;
        }

        // ═══════════════════════════════════════════
        //  Analyze
        // ═══════════════════════════════════════════

        static async Task<int> RunAnalyze(string[] args)
        {
            // `find . -name '*.flac' | audioauditorcli analyze` — a documented startup tip — passes
            // no arguments at all; the paths arrive on stdin and are read further down. Printing
            // help for an empty argv made that unreachable and left the stdin reader as dead code.
            if ((args.Length == 0 && !Console.IsInputRedirected) || args.Contains("--help"))
            {
                Console.WriteLine(@"
USAGE: audioauditorcli analyze <path> [options]

Analyze audio files for quality (fake lossless, clipping, MQA, AI detection).

OPTIONS:
  --verbose, -v     Show detailed per-file analysis
  --status <s>      Filter output by status: real, fake, unknown, corrupt, optimized
  --threads <n>     Max parallel threads (default: auto)
  --cpu <mode>      CPU preset: auto, low (2), medium (4), high (8), max (16)
  --memory <mb>     Memory limit in MB (512-8192), or preset: auto, low, medium, high, max
  --recursive, -r   Recurse into subdirectories (default for folders)
  --no-recursive    Do not recurse into subdirectories
  --json            Output results as JSON
  --rip-log         Score the EAC/XLD/whipper log next to the files and show a Rip Log
                    column. One cambia run per folder; silently skipped if cambia
                    isn't bundled with this build.

  UTILITY:
  --no-config       Ignore saved CLI config defaults for this run
  --no-update-check Skip background update check
  --no-tips         Disable random scan tips
  --no-fun          Disable scan annotations, tips, and completion messages
  --eta             Show estimated time remaining during scan
  --no-eta          Accepted for compatibility; ETA is off unless --eta is used

  ANALYSIS TOGGLES:
  Fast scan is the default. Full-track detectors are opt-in.
  --thorough        Enable silence, DR, true peak, LUFS, and BPM analysis
  --silence         Enable silence detection
  --dynamic-range   Enable dynamic range measurement
  --true-peak       Enable true peak measurement
  --lufs            Enable integrated LUFS measurement
  --bpm             Enable BPM detection
  --experimental-ai Enable experimental spectral AI detection (off by default)
  --shlabs          Enable SH Labs AI detection (uses quota: 15/day, 100/month)
  --no-ai           Disable the standard AI watermark detector
  --always-full     Always run a full-file pass even when detectors are off
  --cutoff-allow <hz> Don't flag as fake when frequency cutoff >= this Hz (default 19600)
  --no-cutoff-allow Turn off the frequency-cutoff allowance
  --no-clipping     Disable clipping detection
  --no-mqa          Disable MQA detection
  --no-silence      Disable silence detection
  --no-fake-stereo  Disable fake stereo detection
  --no-dynamic-range Disable dynamic range measurement
  --no-true-peak    Disable true peak measurement
  --no-lufs         Disable integrated LUFS measurement
  --no-bpm          Disable BPM detection
  --fast            Force fast scan by disabling full-track detectors
");
                return 0;
            }

            bool verbose = false;
            var cf = CommonFlags.Default();
            bool json = false;
            var analysisSettings = CreateAnalysisSettingsSnapshot();
            bool shLabs = false;

            for (int i = 0; i < args.Length; i++)
            {
                if (TryParseCommonFlag(args, ref i, ref cf, out var err))
                {
                    if (err != null) return Error(err);
                    continue;
                }
                if (TryApplyAnalysisToggle(args[i], ref analysisSettings, out bool enableShLabs))
                {
                    shLabs |= enableShLabs;
                    continue;
                }
                switch (args[i].ToLowerInvariant())
                {
                    case "--verbose" or "-v": verbose = true; break;
                    case "--json": json = true; break;
                    case "--cutoff-allow" when i + 1 < args.Length:
                        if (int.TryParse(args[++i], out int caHz))
                            analysisSettings = analysisSettings with { FrequencyCutoffAllowEnabled = true, FrequencyCutoffAllowHz = caHz };
                        else return Error($"Invalid value for --cutoff-allow: {args[i]}");
                        break;
                }
            }

            if (cf.Paths.Count == 0 && Console.IsInputRedirected)
            {
                const int MaxStdinPaths = 50_000;
                string? line;
                while ((line = Console.ReadLine()) != null && cf.Paths.Count < MaxStdinPaths)
                {
                    line = line.Trim().Trim('"');
                    if (!string.IsNullOrEmpty(line))
                        cf.Paths.Add(line);
                }
            }

            if (cf.Paths.Count == 0)
                return Error("No input path specified.");

            var files = CollectFiles(cf.Paths, cf.Recursive);
            if (files.Count == 0)
                return Error("No supported audio files found.");

            if (!json)
            {
                Console.WriteLine($"Analyzing {files.Count} file(s) with {cf.Threads} thread(s)...");
                if (cf.MemoryLimitMb > 0)
                    Console.WriteLine($"Memory limit: {cf.MemoryLimitMb} MB");
                Console.WriteLine();
            }

            ScanCacheService.EnsureLoaded();
            var results = AnalyzeFiles(files, analysisSettings, cf.Threads, !json, cf.MemoryLimitMb, out bool userStopped);

            // Run SH Labs AI detection if requested (async, rate-limited)
            if (shLabs)
            {
                var (daily, monthly) = SHLabsDetectionService.GetQuota();
                int filesToScan = Math.Min(results.Count, Math.Min(daily, monthly));
                if (filesToScan > 0)
                {
                    if (!json)
                    {
                        Console.WriteLine($"\nRunning SH Labs AI detection ({filesToScan}/{results.Count} files, quota: {daily}/day, {monthly}/month)...");
                    }
                    int scanned = 0;
                    foreach (var r in results)
                    {
                        if (scanned >= filesToScan) break;
                        try
                        {
                            var shResult = await SHLabsDetectionService.AnalyzeAsync(r.FilePath);
                            if (shResult != null)
                            {
                                r.SHLabsScanned = true;
                                r.SHLabsPrediction = shResult.Prediction;
                                r.SHLabsProbability = shResult.Probability;
                                r.SHLabsConfidence = shResult.Confidence;
                                r.SHLabsAiType = shResult.MostLikelyAiType;
                                scanned++;
                                if (!json)
                                    WriteProgress($"  [{scanned}/{filesToScan}] {scanned * 100 / filesToScan}%");
                            }
                        }
                        catch { }
                    }
                    if (!json) Console.WriteLine();
                }
                else if (!json)
                {
                    Console.WriteLine($"\nSH Labs: No quota remaining (daily: {daily}, monthly: {monthly}). Skipping.");
                }
            }

            await BackfillRipLogsAsync(results, cf.RipLog, json);

            // Apply status filter
            if (cf.StatusFilter != null)
            {
                var filterStatus = ParseStatus(cf.StatusFilter);
                if (filterStatus.HasValue)
                    results = results.Where(r => r.Status == filterStatus.Value).ToList();
            }

            if (json)
            {
                PrintJson(results);
            }
            else
            {
                PrintAnalysisResults(results, verbose);
                if (!userStopped) PrintCompletionMessage();
            }

            ScanCacheService.SaveToDisk();
            CleanupTempDirs();
            return 0;
        }

        /// <summary>
        /// Stamps the CD rip-log verdict onto every scanned row, mirroring the GUI's post-scan
        /// backfill (Windows/Refresh.cs BackfillRipLogsAsync). cambia is run once per distinct
        /// folder, not once per file. No-op when the flag is off or the binary isn't bundled.
        /// </summary>
        static async Task BackfillRipLogsAsync(IReadOnlyList<AudioFileInfo> results, bool enabled, bool quiet)
        {
            if (!enabled || results.Count == 0) return;
            if (!RipLogCheckService.IsAvailable)
            {
                if (!quiet) Console.WriteLine("Rip log: cambia not found — skipping (--rip-log).");
                return;
            }

            var folders = results.Select(r => r.FolderPath)
                                 .Where(s => !string.IsNullOrEmpty(s))
                                 .Distinct(StringComparer.OrdinalIgnoreCase)
                                 .ToList();
            if (folders.Count == 0) return;

            Dictionary<string, RipLogResult> map;
            try { map = await RipLogCheckService.CheckFoldersAsync(folders); }
            catch { return; }

            foreach (var r in results)
                if (!string.IsNullOrEmpty(r.FolderPath) && map.TryGetValue(r.FolderPath, out var res))
                    r.SetRipLog(res.Score, res.Verdict);
        }

        // ═══════════════════════════════════════════
        //  Export
        // ═══════════════════════════════════════════

        static int RunExport(string[] args)
        {
            if (args.Length == 0 || args.Contains("--help"))
            {
                Console.WriteLine(@"
USAGE: audioauditorcli export <path> -o <output> [options]

Analyze and export results to a file.

OPTIONS:
  -o, --output <file>   Output file path (required)
  --format <fmt>        Export format: csv, txt, pdf, xlsx, docx (auto-detected from extension)
  --status <s>          Filter results: real, fake, unknown, corrupt, optimized
  --threads <n>         Max parallel threads (default: auto)
  --cpu <mode>          CPU preset: auto, low (2), medium (4), high (8), max (16)
  --memory <mb>         Memory limit in MB (512-8192), or preset: auto, low, medium, high, max
  --recursive, -r       Recurse into subdirectories (default)
  --no-recursive        Do not recurse
  --rip-log             Fill the Rip Log Score column from the EAC/XLD/whipper log
                        next to the files (one cambia run per folder)
");
                return 0;
            }

            string? output = null;
            string? format = null;
            var cf = CommonFlags.Default();

            for (int i = 0; i < args.Length; i++)
            {
                if (TryParseCommonFlag(args, ref i, ref cf, out var err))
                {
                    if (err != null) return Error(err);
                    continue;
                }
                switch (args[i].ToLowerInvariant())
                {
                    case "-o" or "--output" when i + 1 < args.Length: output = args[++i]; break;
                    case "--format" when i + 1 < args.Length: format = args[++i].ToLowerInvariant(); break;
                }
            }

            if (cf.Paths.Count == 0 && Console.IsInputRedirected)
            {
                const int MaxStdinPaths = 50_000;
                string? line;
                while ((line = Console.ReadLine()) != null && cf.Paths.Count < MaxStdinPaths)
                {
                    line = line.Trim().Trim('"');
                    if (!string.IsNullOrEmpty(line))
                        cf.Paths.Add(line);
                }
            }

            if (cf.Paths.Count == 0)
                return Error("No input path specified.");
            if (string.IsNullOrEmpty(output))
                return Error("No output file specified. Use -o <file>.");

            // Auto-detect format from extension if not specified
            if (format == null)
            {
                format = Path.GetExtension(output).TrimStart('.').ToLowerInvariant();
                if (string.IsNullOrEmpty(format)) format = "csv";
            }

            var files = CollectFiles(cf.Paths, cf.Recursive);
            if (files.Count == 0)
                return Error("No supported audio files found.");

            Console.WriteLine($"Analyzing {files.Count} file(s)...");
            var results = AnalyzeFiles(files, CreateAnalysisSettingsSnapshot(), cf.Threads, true, cf.MemoryLimitMb, out _);

            BackfillRipLogsAsync(results, cf.RipLog, quiet: false).GetAwaiter().GetResult();

            // Apply status filter
            if (cf.StatusFilter != null)
            {
                var filterStatus = ParseStatus(cf.StatusFilter);
                if (filterStatus.HasValue)
                    results = results.Where(r => r.Status == filterStatus.Value).ToList();
            }

            ScanCacheService.EnsureLoaded();
            Console.WriteLine($"Exporting to {output} ({format})...");

            try
            {
                string outputPath = Path.GetFullPath(output);
                ExportService.Export(results, outputPath, columns: null, format: format);
                Console.WriteLine($"Exported {results.Count} results to {outputPath}");
            }
            catch (Exception ex)
            {
                return Error($"Export failed: {ex.Message}");
            }

            ScanCacheService.SaveToDisk();
            CleanupTempDirs();
            return 0;
        }

        // ═══════════════════════════════════════════
        //  Metadata
        // ═══════════════════════════════════════════

        static int RunMetadata(string[] args)
        {
            if (args.Length == 0 || args.Contains("--help"))
            {
                Console.WriteLine(@"
USAGE: audioauditorcli metadata <action> <file> [options]

View or edit audio file metadata.

ACTIONS:
  show <file>              Display all metadata tags
  set <file> [options]     Set metadata fields
  enrich <path> [options]  Auto-fill missing tags from online sources (MusicBrainz, Cover Art)
  remove-cover <file>      Remove embedded album cover
  strip <file>             Remove ALL metadata tags
  backups <path>           List the .audioauditor-backup-* copies taken before past edits
  restore <path> [options] Put a file back from its backup copy

RESTORE OPTIONS:
  --newest                 Restore the most recent backup (default: the oldest, i.e. the original)
  --dry-run                Show what would be restored without writing
  -y, --yes                Restore without the confirmation prompt
  --no-recursive           Do not recurse into subfolders

ENRICH OPTIONS:
  --all                    Overwrite existing tags too (default: missing only)
  --acoustid               Also match by AcoustID fingerprint (needs --api-key/ACOUSTID_API_KEY)
  --api-key <key>          AcoustID API key
  --dry-run                Preview proposed changes without writing
  -y, --yes                Apply without the confirmation prompt
  --include-uncertain      Also write matches that need review (default: high-confidence only)
  --backup                 Copy each file to a .audioauditor-backup-* sibling before writing
  --no-recursive           Do not recurse into subfolders

  Extra metadata sources (MusicBrainz + iTunes + Cover Art Archive are always on):
  --deezer                 Also search Deezer (no key needed)
  --theaudiodb             Also search TheAudioDB (no key needed)
  --discogs-token <t>      Enable Discogs with this token (env: DISCOGS_TOKEN)
  --fanarttv-key <k>       Enable fanart.tv with this key (env: FANARTTV_API_KEY)

  Streaming link -> Comment field (opt-in, appended without clobbering an existing comment):
  --streaming-link <p>     Platform: deezer, apple, spotify, or youtube
  --spotify-id <id>        Spotify Client ID     (env: SPOTIFY_CLIENT_ID)
  --spotify-secret <s>     Spotify Client Secret (env: SPOTIFY_CLIENT_SECRET)
  --youtube-key <k>        YouTube Data API key  (env: YOUTUBE_API_KEY)

SET OPTIONS:
  --title <text>           Set title
  --artist <text>          Set artist
  --album <text>           Set album
  --album-artist <text>    Set album artist
  --year <year>            Set year
  --track <n>              Set track number
  --track-count <n>        Set total tracks
  --disc <n>               Set disc number
  --disc-count <n>         Set total discs
  --genre <text>           Set genre
  --bpm <n>                Set BPM (beats per minute)
  --composer <text>        Set composer
  --conductor <text>       Set conductor
  --grouping <text>        Set grouping
  --copyright <text>       Set copyright
  --comment <text>         Set comment
  --lyrics <text>          Set lyrics
  --cover <image-path>     Set album cover from image file
");
                return 0;
            }

            string action = args[0].ToLowerInvariant();
            if (args.Length < 2)
                return Error("File path required.");

            string filePath = Path.GetFullPath(args[1]);

            // 'enrich' supports a file or a folder and runs online lookups (async).
            if (action == "enrich")
                return RunMetadataEnrich(filePath, args.Skip(2).ToArray());

            // 'backups' and 'restore' take a file or a folder: a bad batch edit is the case that
            // needs undoing, and that is never one file.
            if (action is "backups" or "restore")
                return RunMetadataRestore(filePath, action == "backups", args.Skip(2).ToArray());

            // For 'set' action, support directories for batch editing
            if (action == "set" && Directory.Exists(filePath))
            {
                bool recursive = true;
                bool dryRun = false;
                var metaArgs = new List<string>();
                for (int i = 2; i < args.Length; i++)
                {
                    switch (args[i].ToLowerInvariant())
                    {
                        case "--no-recursive": recursive = false; break;
                        case "--recursive" or "-r": recursive = true; break;
                        case "--dry-run": dryRun = true; break;
                        default: metaArgs.Add(args[i]); break;
                    }
                }
                var files = CollectFiles(new List<string> { filePath }, recursive);
                if (files.Count == 0)
                    return Error("No supported audio files found.");
                Console.WriteLine($"{(dryRun ? "[DRY RUN] " : "")}Batch metadata set for {files.Count} file(s)...");
                int success = 0;
                for (int i = 0; i < files.Count; i++)
                {
                    WriteProgress($"  [{i + 1}/{files.Count}] {Path.GetFileName(files[i])}");
                    if (dryRun)
                    {
                        Console.WriteLine();
                        PrintDryRunChanges(files[i], metaArgs.ToArray());
                        success++;
                    }
                    else
                    {
                        int r = MetadataSet(files[i], metaArgs.ToArray());
                        if (r == 0) success++;
                    }
                }
                Console.WriteLine($"\n  {success}/{files.Count} file(s) updated.");
                return 0;
            }

            if (!File.Exists(filePath))
                return Error($"File not found: {filePath}");

            // Single-file dry-run for set
            if (action == "set")
            {
                bool dryRun = args.Skip(2).Any(a => a.Equals("--dry-run", StringComparison.OrdinalIgnoreCase));
                if (dryRun)
                {
                    PrintDryRunChanges(filePath, args.Skip(2).Where(a => !a.Equals("--dry-run", StringComparison.OrdinalIgnoreCase)).ToArray());
                    return 0;
                }
            }

            return action switch
            {
                "show" => MetadataShow(filePath),
                "set" => MetadataSet(filePath, args.Skip(2).ToArray()),
                "remove-cover" => MetadataRemoveCover(filePath),
                "strip" => MetadataStrip(filePath),
                _ => Error($"Unknown metadata action: {action}")
            };
        }

        static int MetadataShow(string filePath)
        {
            try
            {
                using var tagFile = TagLib.File.Create(filePath);
                var tag = tagFile.Tag;
                var props = tagFile.Properties;

                Console.WriteLine($"File:          {Path.GetFileName(filePath)}");
                Console.WriteLine($"Title:         {tag.Title ?? "(none)"}");
                Console.WriteLine($"Artist:        {tag.FirstPerformer ?? "(none)"}");
                Console.WriteLine($"Album:         {tag.Album ?? "(none)"}");
                Console.WriteLine($"Album Artist:  {tag.FirstAlbumArtist ?? "(none)"}");
                Console.WriteLine($"Year:          {(tag.Year > 0 ? tag.Year.ToString() : "(none)")}");
                Console.WriteLine($"Track:         {(tag.Track > 0 ? tag.Track.ToString() : "(none)")}");
                Console.WriteLine($"Disc:          {(tag.Disc > 0 ? tag.Disc.ToString() : "(none)")}");
                Console.WriteLine($"Genre:         {tag.FirstGenre ?? "(none)"}");
                Console.WriteLine($"Composer:      {tag.FirstComposer ?? "(none)"}");
                Console.WriteLine($"Comment:       {tag.Comment ?? "(none)"}");
                Console.WriteLine($"Conductor:     {tag.Conductor ?? "(none)"}");
                Console.WriteLine($"Grouping:      {tag.Grouping ?? "(none)"}");
                Console.WriteLine($"Copyright:     {tag.Copyright ?? "(none)"}");
                Console.WriteLine($"BPM:           {(tag.BeatsPerMinute > 0 ? tag.BeatsPerMinute.ToString() : "(none)")}");
                Console.WriteLine($"Album Cover:   {(tag.Pictures?.Length > 0 ? $"Yes ({tag.Pictures[0].Data.Count:N0} bytes)" : "No")}");
                Console.WriteLine($"Lyrics:        {(string.IsNullOrEmpty(tag.Lyrics) ? "(none)" : tag.Lyrics.Length > 80 ? tag.Lyrics[..77] + "..." : tag.Lyrics)}");
                Console.WriteLine();
                Console.WriteLine($"Sample Rate:   {props.AudioSampleRate:N0} Hz");
                Console.WriteLine($"Bit Depth:     {props.BitsPerSample}-bit");
                Console.WriteLine($"Channels:      {props.AudioChannels}");
                Console.WriteLine($"Bitrate:       {props.AudioBitrate} kbps");
                Console.WriteLine($"Duration:      {props.Duration:hh\\:mm\\:ss}");
            }
            catch (Exception ex)
            {
                return Error($"Cannot read metadata: {ex.Message}");
            }

            return 0;
        }

        static int MetadataSet(string filePath, string[] args)
        {
            try
            {
                using var tagFile = TagLib.File.Create(filePath);
                var tag = tagFile.Tag;
                bool changed = false;

                for (int i = 0; i < args.Length; i++)
                {
                    if (i + 1 >= args.Length) break;
                    string value = args[i + 1];

                    switch (args[i].ToLowerInvariant())
                    {
                        case "--title": tag.Title = value; changed = true; i++; break;
                        case "--artist": tag.Performers = new[] { value }; changed = true; i++; break;
                        case "--album": tag.Album = value; changed = true; i++; break;
                        case "--album-artist": tag.AlbumArtists = new[] { value }; changed = true; i++; break;
                        case "--year" when uint.TryParse(value, out uint y): tag.Year = y; changed = true; i++; break;
                        case "--track" when uint.TryParse(value, out uint t): tag.Track = t; changed = true; i++; break;
                        case "--disc" when uint.TryParse(value, out uint d): tag.Disc = d; changed = true; i++; break;
                        case "--track-count" when uint.TryParse(value, out uint tc): tag.TrackCount = tc; changed = true; i++; break;
                        case "--disc-count" when uint.TryParse(value, out uint dc): tag.DiscCount = dc; changed = true; i++; break;
                        case "--genre": tag.Genres = new[] { value }; changed = true; i++; break;
                        case "--bpm" when uint.TryParse(value, out uint bpm): tag.BeatsPerMinute = bpm; changed = true; i++; break;
                        case "--composer": tag.Composers = new[] { value }; changed = true; i++; break;
                        case "--conductor": tag.Conductor = value; changed = true; i++; break;
                        case "--grouping": tag.Grouping = value; changed = true; i++; break;
                        case "--copyright": tag.Copyright = value; changed = true; i++; break;
                        case "--comment": tag.Comment = value; changed = true; i++; break;
                        case "--lyrics": tag.Lyrics = value; changed = true; i++; break;
                        case "--cover":
                            string coverPath = Path.GetFullPath(value);
                            if (!File.Exists(coverPath))
                            {
                                Console.Error.WriteLine($"Cover image not found: {coverPath}");
                                break;
                            }
                            var coverData = File.ReadAllBytes(coverPath);
                            string ext = Path.GetExtension(coverPath).ToLowerInvariant();
                            string mime = ext switch { ".png" => "image/png", ".bmp" => "image/bmp", ".gif" => "image/gif", _ => "image/jpeg" };
                            tag.Pictures = new TagLib.IPicture[]
                            {
                                new TagLib.Picture(new TagLib.ByteVector(coverData)) { Type = TagLib.PictureType.FrontCover, MimeType = mime }
                            };
                            changed = true;
                            i++;
                            break;
                    }
                }

                if (changed)
                {
                    tagFile.Save();
                    Console.WriteLine("Metadata updated successfully.");
                }
                else
                {
                    Console.WriteLine("No changes specified.");
                }
            }
            catch (Exception ex)
            {
                return Error($"Failed to set metadata: {ex.Message}");
            }

            return 0;
        }

        static int MetadataRemoveCover(string filePath)
        {
            try
            {
                using var tagFile = TagLib.File.Create(filePath);
                tagFile.Tag.Pictures = Array.Empty<TagLib.IPicture>();
                tagFile.Save();
                Console.WriteLine("Album cover removed.");
            }
            catch (Exception ex)
            {
                return Error($"Failed to remove cover: {ex.Message}");
            }
            return 0;
        }

        static int MetadataStrip(string filePath)
        {
            try
            {
                using var tagFile = TagLib.File.Create(filePath);
                tagFile.RemoveTags(TagLib.TagTypes.AllTags);
                tagFile.Save();
                Console.WriteLine("All metadata stripped.");
            }
            catch (Exception ex)
            {
                return Error($"Failed to strip metadata: {ex.Message}");
            }
            return 0;
        }

        static void PrintDryRunChanges(string filePath, string[] args)
        {
            SetColor(ConsoleColor.DarkGray);
            Console.Write("  [DRY RUN] ");
            ResetColor();
            Console.Write(Path.GetFileName(filePath) + ": ");
            var changes = new List<string>();
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (args[i].StartsWith("--"))
                {
                    string field = args[i][2..];
                    changes.Add($"{field} → \"{args[i + 1]}\"");
                    i++;
                }
            }
            Console.WriteLine(changes.Count > 0 ? string.Join(", ", changes) : "(no changes)");
        }

        // ═══════════════════════════════════════════
        //  Info (single file detailed)
        // ═══════════════════════════════════════════

        static async Task<int> RunInfo(string[] args)
        {
            if (args.Length == 0 || args.Contains("--help"))
            {
                Console.WriteLine(@"
USAGE: audioauditorcli info <file> [options]

Show detailed analysis for a single audio file.

OPTIONS:
  --thorough         Enable silence, DR, true peak, LUFS, and BPM analysis
  --silence          Enable silence detection
  --dynamic-range    Enable dynamic range measurement
  --true-peak        Enable true peak measurement
  --lufs             Enable LUFS measurement
  --bpm              Enable BPM detection
  --experimental-ai  Enable experimental spectral AI detection
  --shlabs           Enable SH Labs AI detection
  --no-ai            Disable the standard AI watermark detector
  --always-full      Always run a full-file pass even when detectors are off
  --cutoff-allow <hz> Don't flag as fake when frequency cutoff >= this Hz (default 19600)
  --no-cutoff-allow  Turn off the frequency-cutoff allowance
  --no-clipping      Disable clipping detection
  --no-mqa           Disable MQA detection
  --no-silence       Disable silence detection
  --no-fake-stereo   Disable fake stereo detection
  --no-dynamic-range Disable dynamic range measurement
  --no-true-peak     Disable true peak measurement
  --no-lufs          Disable LUFS measurement
  --no-bpm           Disable BPM detection
  --fast             Force fast scan by disabling full-track detectors
");
                return 0;
            }

            var analysisSettings = CreateAnalysisSettingsSnapshot();
            bool shLabs = false;
            string? fileArg = null;
            for (int i = 0; i < args.Length; i++)
            {
                if (TryApplyAnalysisToggle(args[i], ref analysisSettings, out bool enableShLabs))
                {
                    shLabs |= enableShLabs;
                    continue;
                }
                if (args[i].Equals("--cutoff-allow", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                {
                    if (int.TryParse(args[++i], out int caHz))
                        analysisSettings = analysisSettings with { FrequencyCutoffAllowEnabled = true, FrequencyCutoffAllowHz = caHz };
                    else return Error($"Invalid value for --cutoff-allow: {args[i]}");
                    continue;
                }
                if (!args[i].StartsWith("-") && fileArg == null)
                    fileArg = args[i];
            }

            if (fileArg == null)
                return Error("No file specified. Usage: info <file> [options]");
            string filePath = Path.GetFullPath(fileArg);
            if (!File.Exists(filePath))
                return Error($"File not found: {filePath}");

            Console.WriteLine($"Analyzing: {Path.GetFileName(filePath)}...\n");

            var result = AnalyzeFileWithTimeout(filePath, analysisSettings, CancellationToken.None, out _);

            // Run SH Labs if requested
            if (shLabs)
            {
                var (daily, monthly) = SHLabsDetectionService.GetQuota();
                if (daily > 0 && monthly > 0)
                {
                    Console.WriteLine("Running SH Labs AI detection...");
                    try
                    {
                        var shResult = await SHLabsDetectionService.AnalyzeAsync(result.FilePath);
                        if (shResult != null)
                        {
                            result.SHLabsScanned = true;
                            result.SHLabsPrediction = shResult.Prediction;
                            result.SHLabsProbability = shResult.Probability;
                            result.SHLabsConfidence = shResult.Confidence;
                            result.SHLabsAiType = shResult.MostLikelyAiType;
                        }
                    }
                    catch { }
                    Console.WriteLine();
                }
                else
                {
                    Console.WriteLine($"SH Labs: No quota remaining (daily: {daily}, monthly: {monthly}).\n");
                }
            }

            PrintDetailedInfo(result);
            return 0;
        }

        static bool TryApplyAnalysisToggle(string arg, ref AnalysisSettingsSnapshot settings, out bool enableShLabs)
        {
            enableShLabs = false;
            switch (arg.ToLowerInvariant())
            {
                case "--experimental-ai":
                    settings = settings with { EnableExperimentalAi = true };
                    return true;
                case "--shlabs":
                    enableShLabs = true;
                    return true;
                case "--silence":
                    settings = settings with { EnableSilenceDetection = true };
                    return true;
                case "--dynamic-range":
                    settings = settings with { EnableDynamicRange = true };
                    return true;
                case "--true-peak":
                    settings = settings with { EnableTruePeak = true };
                    return true;
                case "--lufs":
                    settings = settings with { EnableLufs = true };
                    return true;
                case "--bpm":
                    settings = settings with { EnableBpmDetection = true };
                    return true;
                case "--thorough":
                    settings = settings with
                    {
                        EnableSilenceDetection = true,
                        EnableDynamicRange = true,
                        EnableTruePeak = true,
                        EnableLufs = true,
                        EnableBpmDetection = true
                    };
                    return true;
                case "--no-clipping":
                    settings = settings with { EnableClippingDetection = false };
                    return true;
                case "--no-mqa":
                    settings = settings with { EnableMqaDetection = false };
                    return true;
                case "--no-silence":
                    settings = settings with { EnableSilenceDetection = false };
                    return true;
                case "--no-fake-stereo":
                    settings = settings with { EnableFakeStereoDetection = false };
                    return true;
                case "--no-dynamic-range":
                    settings = settings with { EnableDynamicRange = false };
                    return true;
                case "--no-true-peak":
                    settings = settings with { EnableTruePeak = false };
                    return true;
                case "--no-lufs":
                    settings = settings with { EnableLufs = false };
                    return true;
                case "--no-bpm":
                    settings = settings with { EnableBpmDetection = false };
                    return true;
                case "--no-ai":
                    settings = settings with { EnableDefaultAiDetection = false };
                    return true;
                case "--always-full":
                    settings = settings with { AlwaysFullAnalysis = true };
                    return true;
                case "--no-cutoff-allow":
                    settings = settings with { FrequencyCutoffAllowEnabled = false };
                    return true;
                case "--fast":
                    settings = settings with
                    {
                        EnableDynamicRange = false,
                        EnableTruePeak = false,
                        EnableLufs = false,
                        EnableSilenceDetection = false,
                        EnableBpmDetection = false
                    };
                    return true;
                default:
                    return false;
            }
        }

    }
}
