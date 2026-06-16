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
        //  Analyze
        // ═══════════════════════════════════════════

        static async Task<int> RunAnalyze(string[] args)
        {
            if (args.Length == 0 || args.Contains("--help"))
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

  UTILITY:
  --no-config       Ignore saved CLI config defaults for this run
  --no-update-check Skip background update check
  --no-tips         Disable random scan tips
  --no-fun          Disable scan annotations, tips, and completion messages
  --eta             Show estimated time remaining during scan
  --no-eta          Accepted for compatibility; ETA is off unless --eta is used

  ANALYSIS TOGGLES:
  Fast scan is the default. Full-track detectors are opt-in.
  --thorough        Enable silence, DR, true peak, LUFS, BPM, and rip quality analysis
  --silence         Enable silence detection
  --dynamic-range   Enable dynamic range measurement
  --true-peak       Enable true peak measurement
  --lufs            Enable integrated LUFS measurement
  --bpm             Enable BPM detection
  --experimental-ai Enable experimental spectral AI detection (off by default)
  --rip-quality     Enable rip/encode quality detection (off by default)
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
                ExportService.Export(results, outputPath);
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

ENRICH OPTIONS:
  --all                    Overwrite existing tags too (default: missing only)
  --acoustid               Also match by AcoustID fingerprint (needs --api-key/ACOUSTID_API_KEY)
  --api-key <key>          AcoustID API key
  --dry-run                Preview proposed changes without writing
  -y, --yes                Apply without the confirmation prompt
  --no-recursive           Do not recurse into subfolders

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
  --thorough         Enable silence, DR, true peak, LUFS, BPM, and rip quality analysis
  --silence          Enable silence detection
  --dynamic-range    Enable dynamic range measurement
  --true-peak        Enable true peak measurement
  --lufs             Enable LUFS measurement
  --bpm              Enable BPM detection
  --experimental-ai  Enable experimental spectral AI detection
  --rip-quality      Enable rip/encode quality detection
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
                case "--rip-quality":
                    settings = settings with { EnableRipQuality = true };
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
                        EnableBpmDetection = true,
                        EnableRipQuality = true
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
                        EnableRipQuality = false,
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
