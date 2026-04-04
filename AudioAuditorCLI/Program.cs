using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
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
    class Program
    {
        private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".mp3", ".flac", ".wav", ".ogg", ".aac", ".m4a", ".wma",
            ".aiff", ".aif", ".ape", ".wv", ".opus", ".dsf", ".dff"
        };

        private static readonly HashSet<string> ArchiveExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".zip", ".rar", ".7z", ".tar", ".gz", ".bz2", ".xz", ".tgz"
        };

        private static readonly List<string> _tempDirs = new();

        private static bool _noColor = false;

        static int Main(string[] args)
        {
            // Rejoin args to handle unquoted paths with spaces, then re-split properly
            args = RejoinArgs(args);

            // Check for global flags before command parsing
            if (args.Contains("--no-color") || args.Contains("--no-colour"))
            {
                _noColor = true;
                args = args.Where(a => a != "--no-color" && a != "--no-colour").ToArray();
            }

            // Detect NO_COLOR environment variable (https://no-color.org/)
            if (Environment.GetEnvironmentVariable("NO_COLOR") != null)
                _noColor = true;

            // Non-blocking update check — starts in background, prints result if available
            var updateCheck = !args.Contains("--no-update-check")
                ? Task.Run(async () =>
                {
                    try { return await AudioQualityChecker.Services.UpdateChecker.CheckForUpdateAsync(GetVersion()); }
                    catch { return false; }
                })
                : Task.FromResult(false);

            if (args.Length == 0 || args[0] == "--help" || args[0] == "-h")
            {
                PrintHelp();
                return 0;
            }

            if (args[0] == "--version" || args[0] == "-V")
            {
                Console.WriteLine($"AudioAuditor CLI v{GetVersion()}");
                Console.WriteLine($"Runtime: {System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription}");
                Console.WriteLine($"OS: {System.Runtime.InteropServices.RuntimeInformation.OSDescription}");
                return 0;
            }

            string command = args[0].ToLowerInvariant();

            int result = command switch
            {
                "analyze" => RunAnalyze(args.Skip(1).ToArray()),
                "export" => RunExport(args.Skip(1).ToArray()),
                "metadata" => RunMetadata(args.Skip(1).ToArray()),
                "info" => RunInfo(args.Skip(1).ToArray()),
                "spectrogram" or "spectro" => RunSpectrogram(args.Skip(1).ToArray()),
                _ => Error($"Unknown command: {args[0]}. Use --help for usage.")
            };

            // Print update notification if the background check found one
            if (updateCheck.IsCompleted && updateCheck.Result)
            {
                var ver = AudioQualityChecker.Services.UpdateChecker.LatestVersion;
                Console.WriteLine();
                if (!_noColor) Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine($"  Update available: v{ver} (current: v{GetVersion()})");
                Console.WriteLine($"  https://github.com/Angel2mp3/AudioAuditor/releases/latest");
                if (!_noColor) Console.ResetColor();
            }

            return result;
        }

        static string GetVersion()
        {
            var asm = System.Reflection.Assembly.GetExecutingAssembly();
            var ver = asm.GetName().Version;
            return ver != null ? $"{ver.Major}.{ver.Minor}.{ver.Build}" : "1.4.0";
        }

        /// <summary>
        /// Re-joins arguments that were split by spaces but actually form a single path.
        /// e.g. ["analyze", "Cool", "Music", "Folder"] → ["analyze", "Cool Music Folder"]
        /// Keeps flags (--flag) and their values intact.
        /// </summary>
        static string[] RejoinArgs(string[] args)
        {
            if (args.Length <= 1) return args;

            var result = new List<string>();
            // First arg is always the command
            result.Add(args[0]);

            var pathParts = new List<string>();
            bool expectingFlagValue = false;

            for (int i = 1; i < args.Length; i++)
            {
                string arg = args[i];

                if (arg.StartsWith("-"))
                {
                    // Flush any accumulated path parts
                    if (pathParts.Count > 0)
                    {
                        result.Add(string.Join(" ", pathParts));
                        pathParts.Clear();
                    }
                    result.Add(arg);
                    // Check if this flag expects a value argument
                    expectingFlagValue = arg is "--status" or "--threads" or "-o" or "--output" or "--format"
                        or "--title" or "--artist" or "--album" or "--album-artist" or "--year"
                        or "--track" or "--track-count" or "--disc" or "--disc-count" or "--genre"
                        or "--bpm" or "--composer" or "--conductor" or "--grouping" or "--copyright"
                        or "--comment" or "--lyrics" or "--cover"
                        or "--cpu" or "--memory" or "--width" or "--height";
                }
                else if (expectingFlagValue)
                {
                    result.Add(arg);
                    expectingFlagValue = false;
                }
                else
                {
                    // Check if joining with previous parts forms a valid path
                    string joined = pathParts.Count > 0 ? string.Join(" ", pathParts) + " " + arg : arg;
                    if (pathParts.Count > 0 && (File.Exists(string.Join(" ", pathParts)) || Directory.Exists(string.Join(" ", pathParts))))
                    {
                        // Previous parts already form a valid path, flush and start new
                        result.Add(string.Join(" ", pathParts));
                        pathParts.Clear();
                        pathParts.Add(arg);
                    }
                    else
                    {
                        pathParts.Add(arg);
                    }
                }
            }

            // Flush remaining path parts
            if (pathParts.Count > 0)
                result.Add(string.Join(" ", pathParts));

            return result.ToArray();
        }

        // ═══════════════════════════════════════════
        //  Help
        // ═══════════════════════════════════════════

        static void PrintHelp()
        {
            bool isWindows = System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(
                System.Runtime.InteropServices.OSPlatform.Windows);
            string exe = isWindows ? ".\\AudioAuditorCLI" : "./AudioAuditorCLI";
            string musicPath = isWindows ? "C:\\Music\\album" : "~/Music/album";
            string songPath = isWindows ? "C:\\Music\\song.flac" : "~/Music/song.flac";

            Console.WriteLine($@"
AudioAuditor CLI — Audio quality analysis from the command line

USAGE:
  {exe} <command> [options]

COMMANDS:
  analyze      Analyze audio files or folders for quality
  export       Analyze and export results to a file
  metadata     View or edit audio file metadata
  info         Show detailed info for a single file
  spectrogram  Generate and save spectrograms as PNG images

GLOBAL OPTIONS:
  --cpu <mode>     CPU usage mode: auto, low (2), medium (4), high (8), max (16)
  --memory <mb>    Memory limit in MB (512-8192), or: auto, low, medium, high, max
  --no-color       Disable colored output
  --version, -V    Show version information

EXAMPLES:
  {exe} analyze ""{musicPath}""
  {exe} analyze file.flac --verbose
  {exe} analyze ""{musicPath}"" --status fake
  {exe} export ""{musicPath}"" -o results.csv
  {exe} spectrogram ""{songPath}"" -o spectrograms/
  {exe} metadata show ""{songPath}""
  {exe} info ""{songPath}""

Use <command> --help for detailed command help.
");
        }

        // ═══════════════════════════════════════════
        //  Analyze
        // ═══════════════════════════════════════════

        static int RunAnalyze(string[] args)
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
  --experimental-ai Enable experimental spectral AI detection (more false positives)
");
                return 0;
            }

            var paths = new List<string>();
            bool verbose = false;
            string? statusFilter = null;
            int threads = Math.Max(1, Environment.ProcessorCount / 2);
            int memoryLimitMb = 0;
            bool recursive = true;
            bool json = false;
            bool experimentalAi = false;

            for (int i = 0; i < args.Length; i++)
            {
                switch (args[i].ToLowerInvariant())
                {
                    case "--verbose" or "-v": verbose = true; break;
                    case "--status" when i + 1 < args.Length: statusFilter = args[++i].ToLowerInvariant(); break;
                    case "--threads" when i + 1 < args.Length: threads = Math.Clamp(int.Parse(args[++i]), 1, 32); break;
                    case "--cpu" when i + 1 < args.Length: threads = ParseCpuPreset(args[++i]); break;
                    case "--memory" when i + 1 < args.Length: memoryLimitMb = ParseMemoryPreset(args[++i]); break;
                    case "--no-recursive": recursive = false; break;
                    case "--recursive" or "-r": recursive = true; break;
                    case "--json": json = true; break;
                    case "--experimental-ai": experimentalAi = true; break;
                    default:
                        if (!args[i].StartsWith("-"))
                            paths.Add(args[i]);
                        break;
                }
            }

            if (paths.Count == 0)
                return Error("No input path specified.");

            var files = CollectFiles(paths, recursive);
            if (files.Count == 0)
                return Error("No supported audio files found.");

            if (!json)
            {
                Console.WriteLine($"Analyzing {files.Count} file(s) with {threads} thread(s)...");
                if (memoryLimitMb > 0)
                    Console.WriteLine($"Memory limit: {memoryLimitMb} MB");
                Console.WriteLine();
            }

            // Enable experimental AI if flag passed
            if (experimentalAi)
                AudioAnalyzer.EnableExperimentalAi = true;

            var results = AnalyzeFiles(files, threads, !json, memoryLimitMb);

            // Apply status filter
            if (statusFilter != null)
            {
                var filterStatus = ParseStatus(statusFilter);
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
            }

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
  --threads <n>         Max parallel threads (default: auto)
  --cpu <mode>          CPU preset: auto, low (2), medium (4), high (8), max (16)
  --memory <mb>         Memory limit in MB (512-8192), or preset: auto, low, medium, high, max
  --recursive, -r       Recurse into subdirectories (default)
  --no-recursive        Do not recurse
");
                return 0;
            }

            var paths = new List<string>();
            string? output = null;
            string? format = null;
            int threads = Math.Max(1, Environment.ProcessorCount / 2);
            int memoryLimitMb = 0;
            bool recursive = true;

            for (int i = 0; i < args.Length; i++)
            {
                switch (args[i].ToLowerInvariant())
                {
                    case "-o" or "--output" when i + 1 < args.Length: output = args[++i]; break;
                    case "--format" when i + 1 < args.Length: format = args[++i].ToLowerInvariant(); break;
                    case "--threads" when i + 1 < args.Length: threads = Math.Clamp(int.Parse(args[++i]), 1, 32); break;
                    case "--cpu" when i + 1 < args.Length: threads = ParseCpuPreset(args[++i]); break;
                    case "--memory" when i + 1 < args.Length: memoryLimitMb = ParseMemoryPreset(args[++i]); break;
                    case "--no-recursive": recursive = false; break;
                    case "--recursive" or "-r": recursive = true; break;
                    default:
                        if (!args[i].StartsWith("-"))
                            paths.Add(args[i]);
                        break;
                }
            }

            if (paths.Count == 0)
                return Error("No input path specified.");
            if (string.IsNullOrEmpty(output))
                return Error("No output file specified. Use -o <file>.");

            // Auto-detect format from extension if not specified
            if (format == null)
            {
                format = Path.GetExtension(output).TrimStart('.').ToLowerInvariant();
                if (string.IsNullOrEmpty(format)) format = "csv";
            }

            var files = CollectFiles(paths, recursive);
            if (files.Count == 0)
                return Error("No supported audio files found.");

            Console.WriteLine($"Analyzing {files.Count} file(s)...");
            var results = AnalyzeFiles(files, threads, showProgress: true, memoryLimitMb);

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
  remove-cover <file>      Remove embedded album cover
  strip <file>             Remove ALL metadata tags

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
            if (!File.Exists(filePath))
                return Error($"File not found: {filePath}");

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

        // ═══════════════════════════════════════════
        //  Info (single file detailed)
        // ═══════════════════════════════════════════

        static int RunInfo(string[] args)
        {
            if (args.Length == 0 || args.Contains("--help"))
            {
                Console.WriteLine("USAGE: audioauditorcli info <file> [--experimental-ai]\n\nShow detailed analysis for a single audio file.");
                return 0;
            }

            bool experimentalAi = args.Contains("--experimental-ai");
            string filePath = Path.GetFullPath(args.Where(a => a != "--experimental-ai").First());
            if (!File.Exists(filePath))
                return Error($"File not found: {filePath}");

            if (experimentalAi)
                AudioAnalyzer.EnableExperimentalAi = true;

            Console.WriteLine($"Analyzing: {Path.GetFileName(filePath)}...\n");

            var result = AudioAnalyzer.AnalyzeFile(filePath);
            PrintDetailedInfo(result);
            return 0;
        }

        // ═══════════════════════════════════════════
        //  Helpers
        // ═══════════════════════════════════════════

        static List<string> CollectFiles(List<string> paths, bool recursive)
        {
            var files = new List<string>();
            var searchOption = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;

            foreach (var path in paths)
            {
                string fullPath = Path.GetFullPath(path);
                if (File.Exists(fullPath))
                {
                    string ext = Path.GetExtension(fullPath);
                    if (SupportedExtensions.Contains(ext))
                        files.Add(fullPath);
                    else if (ArchiveExtensions.Contains(ext))
                        files.AddRange(ExtractAudioFromArchive(fullPath));
                }
                else if (Directory.Exists(fullPath))
                {
                    foreach (var f in Directory.EnumerateFiles(fullPath, "*.*", searchOption))
                    {
                        string fExt = Path.GetExtension(f);
                        if (SupportedExtensions.Contains(fExt))
                            files.Add(f);
                        else if (ArchiveExtensions.Contains(fExt))
                            files.AddRange(ExtractAudioFromArchive(f));
                    }
                }
                else
                {
                    Console.Error.WriteLine($"Warning: Path not found: {fullPath}");
                }
            }

            return files.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }

        static List<string> ExtractAudioFromArchive(string archivePath)
        {
            var result = new List<string>();
            string? tempDir = null;
            try
            {
                tempDir = Path.Combine(Path.GetTempPath(), "AudioAuditor_" + Guid.NewGuid().ToString("N")[..8]);
                Directory.CreateDirectory(tempDir);

                string ext = Path.GetExtension(archivePath);
                if (ext.Equals(".zip", StringComparison.OrdinalIgnoreCase))
                {
                    ZipFile.ExtractToDirectory(archivePath, tempDir);
                }
                else
                {
                    // Use SharpCompress for RAR, 7z, tar, gz, etc.
                    using var archive = ArchiveFactory.Open(archivePath);
                    foreach (var entry in archive.Entries.Where(e => !e.IsDirectory))
                    {
                        entry.WriteToDirectory(tempDir, new ExtractionOptions
                        {
                            ExtractFullPath = true,
                            Overwrite = true
                        });
                    }
                }

                result.AddRange(
                    Directory.EnumerateFiles(tempDir, "*.*", SearchOption.AllDirectories)
                        .Where(f => SupportedExtensions.Contains(Path.GetExtension(f))));
                if (result.Count > 0)
                    Console.WriteLine($"  Extracted {result.Count} audio file(s) from {Path.GetFileName(archivePath)}");
                
                // Track temp directory for cleanup after analysis
                lock (_tempDirs)
                    _tempDirs.Add(tempDir);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Warning: Could not extract archive {Path.GetFileName(archivePath)}: {ex.Message}");
                // Clean up on failure
                if (tempDir != null)
                {
                    try { Directory.Delete(tempDir, true); } catch { }
                }
            }
            return result;
        }

        static List<AudioFileInfo> AnalyzeFiles(List<string> files, int threads, bool showProgress, int memoryLimitMb = 0)
        {
            var results = new List<AudioFileInfo>();
            int completed = 0;
            var lockObj = new object();

            Parallel.ForEach(files,
                new ParallelOptions { MaxDegreeOfParallelism = threads },
                filePath =>
                {
                    // Memory limiting: wait if exceeding configured limit
                    if (memoryLimitMb > 0)
                    {
                        long limitBytes = (long)memoryLimitMb * 1024 * 1024;
                        int waited = 0;
                        while (System.Diagnostics.Process.GetCurrentProcess().WorkingSet64 > limitBytes && waited < 10_000)
                        {
                            if (waited == 0)
                            {
                                GC.Collect(2, GCCollectionMode.Forced, false);
                                GC.WaitForPendingFinalizers();
                            }
                            Thread.Sleep(200);
                            waited += 200;
                        }
                    }

                    var result = AudioAnalyzer.AnalyzeFile(filePath);
                    lock (lockObj)
                    {
                        results.Add(result);
                        completed++;
                        if (showProgress)
                        {
                            Console.Write($"\r  [{completed}/{files.Count}] {completed * 100 / files.Count}%");
                        }
                    }
                });

            if (showProgress)
                Console.WriteLine();

            return results;
        }

        static void PrintAnalysisResults(List<AudioFileInfo> results, bool verbose)
        {
            if (verbose || results.Count <= 50)
            {
                Console.WriteLine();

                foreach (var r in results.OrderBy(r => r.Status).ThenBy(r => r.FileName))
                {
                    string statusText = r.Status switch
                    {
                        AudioStatus.Valid => "REAL",
                        AudioStatus.Fake => "FAKE",
                        AudioStatus.Unknown => "UNKNOWN",
                        AudioStatus.Corrupt => "CORRUPT",
                        AudioStatus.Optimized => "OPTIMIZED",
                        AudioStatus.Analyzing => "...",
                        _ => "?"
                    };

                    var statusColor = r.Status switch
                    {
                        AudioStatus.Valid => ConsoleColor.Green,
                        AudioStatus.Fake => ConsoleColor.Red,
                        AudioStatus.Unknown => ConsoleColor.Yellow,
                        AudioStatus.Corrupt => ConsoleColor.DarkRed,
                        AudioStatus.Optimized => ConsoleColor.Cyan,
                        _ => ConsoleColor.Gray
                    };

                    Console.WriteLine("───────────────────────────────────────");
                    SetColor(statusColor);
                    Console.Write($"  [{statusText}]");
                    ResetColor();
                    Console.WriteLine($" {r.FileName}");

                    if (!string.IsNullOrEmpty(r.Title) || !string.IsNullOrEmpty(r.Artist))
                        Console.WriteLine($"  {r.Artist} — {r.Title}");

                    SetColor(ConsoleColor.DarkGray);

                    // Audio properties — two columns that fit in ~70 chars
                    string sr = r.SampleRate > 0 ? $"{r.SampleRate:N0} Hz" : "-";
                    string depth = r.BitsPerSampleDisplay;
                    string ch = r.ChannelsDisplay;
                    Console.WriteLine($"  Format:    {r.Extension}  {sr}  {depth}  {ch}");

                    string duration = r.Duration ?? "-";
                    string size = r.FileSize ?? "-";
                    Console.WriteLine($"  Duration:  {duration,-14} Size: {size}");

                    string reported = r.ReportedBitrate > 0 ? $"{r.ReportedBitrate} kbps" : "-";
                    string actual = r.ActualBitrate > 0 ? $"{r.ActualBitrate} kbps" : "-";
                    Console.WriteLine($"  Bitrate:   {reported,-14} Actual: {actual}");

                    string freq = r.EffectiveFrequency > 0 ? $"{r.EffectiveFrequency:N0} Hz" : "-";
                    string bpm = r.Bpm > 0 ? r.Bpm.ToString() : "-";
                    Console.WriteLine($"  Max Freq:  {freq,-14} BPM: {bpm}");

                    // Flags on one line
                    var flags = new List<string>();
                    if (r.HasClipping) flags.Add($"Clipping {r.ClippingPercentage:F1}%");
                    else if (r.HasScaledClipping) flags.Add($"Scaled Clip {r.ScaledClippingPercentage:F1}%");
                    if (r.IsMqa) flags.Add(r.IsMqaStudio ? "MQA Studio" : "MQA");
                    if (r.IsAiGenerated) flags.Add($"AI: {r.AiSource}");
                    if (r.ExperimentalAiSuspicious) flags.Add($"Spectral AI: {r.ExperimentalAiConfidence:P0}");
                    if (r.HasReplayGain) flags.Add($"RG: {r.ReplayGain:+0.0;-0.0;0.0} dB");
                    if (r.HasAlbumCover) flags.Add("Cover: Yes");
                    if (flags.Count > 0)
                        Console.WriteLine($"  Flags:     {string.Join(" | ", flags)}");

                    if (verbose)
                    {
                        Console.WriteLine($"  Path:      {r.FilePath}");
                        if (r.MaxSampleLevel > 0) Console.WriteLine($"  Peak:      {r.MaxSampleLevelDb:F1} dBFS");
                        if (r.IsMqa) Console.WriteLine($"  MQA Info:  Original SR: {r.MqaOriginalSampleRate} | Encoder: {r.MqaEncoder}");
                        if (r.Frequency > 0) Console.WriteLine($"  Dom Freq:  {r.Frequency:N0} Hz");
                        if (!string.IsNullOrEmpty(r.ErrorMessage)) Console.WriteLine($"  Error:     {r.ErrorMessage}");
                    }

                    ResetColor();
                }
                Console.WriteLine("───────────────────────────────────────");
            }
            else
            {
                // For large result sets, just show fakes/corrupt/flagged files
                var flagged = results.Where(r => r.Status == AudioStatus.Fake || r.Status == AudioStatus.Corrupt || r.IsAiGenerated || r.ExperimentalAiSuspicious || r.HasClipping || r.HasScaledClipping).ToList();
                if (flagged.Count > 0)
                {
                    Console.WriteLine($"\nFlagged files ({flagged.Count}):");
                    foreach (var r in flagged)
                    {
                        var c = r.Status switch
                        {
                            AudioStatus.Fake => ConsoleColor.Red,
                            AudioStatus.Corrupt => ConsoleColor.DarkRed,
                            _ => ConsoleColor.Yellow
                        };
                        SetColor(c);
                        string reason = r.Status == AudioStatus.Fake ? "FAKE" : r.Status == AudioStatus.Corrupt ? "CORRUPT" : "";
                        if (r.IsAiGenerated) reason += (reason.Length > 0 ? " + " : "") + "AI";
                        if (r.ExperimentalAiSuspicious) reason += (reason.Length > 0 ? " + " : "") + "SPECTRAL AI";
                        if (r.HasClipping) reason += (reason.Length > 0 ? " + " : "") + "CLIPPING";
                        if (r.HasScaledClipping) reason += (reason.Length > 0 ? " + " : "") + "SCALED CLIP";
                        Console.Write($"  [{reason}]");
                        ResetColor();
                        Console.WriteLine($" {r.FileName}");
                    }
                }
                Console.WriteLine("\nUse --verbose to see all files.");
            }

            // Summary at the end
            int real = results.Count(r => r.Status == AudioStatus.Valid);
            int fake = results.Count(r => r.Status == AudioStatus.Fake);
            int unknown = results.Count(r => r.Status == AudioStatus.Unknown);
            int corrupt = results.Count(r => r.Status == AudioStatus.Corrupt);
            int optimized = results.Count(r => r.Status == AudioStatus.Optimized);

            Console.WriteLine();
            Console.WriteLine("═══════════════════════════════════════");
            Console.WriteLine("            RESULTS SUMMARY            ");
            Console.WriteLine("═══════════════════════════════════════");
            Console.WriteLine($"  Total:     {results.Count}");
            Console.WriteLine($"  Real:      {real}");
            Console.WriteLine($"  Fake:      {fake}");
            Console.WriteLine($"  Unknown:   {unknown}");
            Console.WriteLine($"  Corrupt:   {corrupt}");
            Console.WriteLine($"  Optimized: {optimized}");

            int mqa = results.Count(r => r.IsMqa);
            int ai = results.Count(r => r.IsAiGenerated);
            int clipping = results.Count(r => r.HasClipping || r.HasScaledClipping);
            int withReplayGain = results.Count(r => r.HasReplayGain);
            int withCover = results.Count(r => r.HasAlbumCover);
            if (mqa > 0) Console.WriteLine($"  MQA:       {mqa}");
            if (ai > 0) Console.WriteLine($"  AI:        {ai}");
            int spectralAi = results.Count(r => r.ExperimentalAiSuspicious);
            if (spectralAi > 0) Console.WriteLine($"  Spectral:  {spectralAi} (experimental)");
            if (clipping > 0) Console.WriteLine($"  Clipping:  {clipping}");
            Console.WriteLine($"  Has Cover: {withCover}");
            if (withReplayGain > 0) Console.WriteLine($"  ReplayGain:{withReplayGain}");
            Console.WriteLine("═══════════════════════════════════════");
        }

        static void PrintDetailedInfo(AudioFileInfo r)
        {
            var statusColor = r.Status switch
            {
                AudioStatus.Valid => ConsoleColor.Green,
                AudioStatus.Fake => ConsoleColor.Red,
                AudioStatus.Unknown => ConsoleColor.Yellow,
                AudioStatus.Corrupt => ConsoleColor.DarkRed,
                AudioStatus.Optimized => ConsoleColor.Cyan,
                _ => ConsoleColor.Gray
            };

            Console.Write("Status:          ");
            SetColor(statusColor);
            Console.WriteLine(r.Status switch
            {
                AudioStatus.Valid => "REAL (Genuine Lossless)",
                AudioStatus.Fake => "FAKE (Upsampled / Fake Lossless)",
                AudioStatus.Unknown => "UNKNOWN",
                AudioStatus.Corrupt => "CORRUPT",
                AudioStatus.Optimized => "OPTIMIZED",
                _ => r.Status.ToString()
            });
            ResetColor();

            Console.WriteLine($"File:            {r.FileName}");
            Console.WriteLine($"Path:            {r.FilePath}");
            Console.WriteLine($"Extension:       {r.Extension}");
            Console.WriteLine($"File Size:       {r.FileSize}");
            Console.WriteLine();
            Console.WriteLine($"Title:           {r.Title}");
            Console.WriteLine($"Artist:          {r.Artist}");
            Console.WriteLine();
            Console.WriteLine($"Sample Rate:     {r.SampleRateDisplay}");
            Console.WriteLine($"Bit Depth:       {r.BitsPerSampleDisplay}");
            Console.WriteLine($"Channels:        {r.ChannelsDisplay}");
            Console.WriteLine($"Duration:        {r.Duration}");
            Console.WriteLine($"Reported Bitrate:{r.ReportedBitrateDisplay}");
            Console.WriteLine($"Actual Bitrate:  {r.ActualBitrateDisplay}");
            Console.WriteLine($"Max Frequency:   {r.EffectiveFrequencyDisplay}");
            Console.WriteLine($"Frequency:       {(r.Frequency > 0 ? $"{r.Frequency:N0} Hz" : "-")}");
            Console.WriteLine();
            Console.WriteLine($"Clipping:        {r.ClippingDisplay}");
            if (r.HasClipping)
                Console.WriteLine($"Clip Samples:    {r.ClippingSamples:N0} ({r.ClippingPercentage:F3}%)");
            if (r.HasScaledClipping)
                Console.WriteLine($"Scaled Clipping: {r.ScaledClippingPercentage:F3}%");
            Console.WriteLine($"Peak Level:      {(r.MaxSampleLevel > 0 ? $"{r.MaxSampleLevelDb:F1} dBFS" : "-")}");
            Console.WriteLine($"BPM:             {(r.Bpm > 0 ? r.Bpm.ToString() : "-")}");
            Console.WriteLine($"Replay Gain:     {(r.HasReplayGain ? $"{r.ReplayGain:F1} dB" : "-")}");
            Console.WriteLine($"Album Cover:     {(r.HasAlbumCover ? "Yes" : "No")}");
            Console.WriteLine();
            Console.WriteLine($"MQA:             {(r.IsMqa ? (r.IsMqaStudio ? "MQA Studio" : "MQA") : "No")}");
            if (r.IsMqa)
            {
                Console.WriteLine($"MQA Original SR: {r.MqaOriginalSampleRate}");
                Console.WriteLine($"MQA Encoder:     {r.MqaEncoder}");
            }
            Console.WriteLine($"AI Generated:    {(r.IsAiGenerated ? $"Yes — {r.AiSource}" : "No")}");
            if (r.ExperimentalAiSuspicious)
            {
                Console.WriteLine($"Spectral AI:     Suspicious ({r.ExperimentalAiConfidence:P0})");
                Console.WriteLine($"  Flags:         {string.Join(", ", r.ExperimentalAiFlags)}");
            }

            if (!string.IsNullOrEmpty(r.ErrorMessage))
                Console.WriteLine($"\nError:           {r.ErrorMessage}");
        }

        static void PrintJson(List<AudioFileInfo> results)
        {
            Console.WriteLine("[");
            for (int i = 0; i < results.Count; i++)
            {
                var r = results[i];
                string comma = i < results.Count - 1 ? "," : "";
                Console.WriteLine($"  {{");
                Console.WriteLine($"    \"fileName\": {JsonEscape(r.FileName)},");
                Console.WriteLine($"    \"filePath\": {JsonEscape(r.FilePath)},");
                Console.WriteLine($"    \"status\": {JsonEscape(r.Status.ToString())},");
                Console.WriteLine($"    \"artist\": {JsonEscape(r.Artist)},");
                Console.WriteLine($"    \"title\": {JsonEscape(r.Title)},");
                Console.WriteLine($"    \"extension\": {JsonEscape(r.Extension)},");
                Console.WriteLine($"    \"sampleRate\": {r.SampleRate},");
                Console.WriteLine($"    \"bitsPerSample\": {r.BitsPerSample},");
                Console.WriteLine($"    \"channels\": {r.Channels},");
                Console.WriteLine($"    \"duration\": {JsonEscape(r.Duration)},");
                Console.WriteLine($"    \"durationSeconds\": {r.DurationSeconds:F2},");
                Console.WriteLine($"    \"fileSize\": {JsonEscape(r.FileSize)},");
                Console.WriteLine($"    \"fileSizeBytes\": {r.FileSizeBytes},");
                Console.WriteLine($"    \"reportedBitrate\": {r.ReportedBitrate},");
                Console.WriteLine($"    \"actualBitrate\": {r.ActualBitrate},");
                Console.WriteLine($"    \"effectiveFrequency\": {r.EffectiveFrequency},");
                Console.WriteLine($"    \"hasClipping\": {(r.HasClipping ? "true" : "false")},");
                Console.WriteLine($"    \"clippingPercentage\": {r.ClippingPercentage:F4},");
                Console.WriteLine($"    \"hasScaledClipping\": {(r.HasScaledClipping ? "true" : "false")},");
                Console.WriteLine($"    \"scaledClippingPercentage\": {r.ScaledClippingPercentage:F4},");
                Console.WriteLine($"    \"maxSampleLevel\": {r.MaxSampleLevel:F6},");
                Console.WriteLine($"    \"maxSampleLevelDb\": {r.MaxSampleLevelDb:F1},");
                Console.WriteLine($"    \"frequency\": {r.Frequency},");
                Console.WriteLine($"    \"bpm\": {r.Bpm},");
                Console.WriteLine($"    \"replayGain\": {r.ReplayGain:F1},");
                Console.WriteLine($"    \"hasReplayGain\": {(r.HasReplayGain ? "true" : "false")},");
                Console.WriteLine($"    \"isMqa\": {(r.IsMqa ? "true" : "false")},");
                Console.WriteLine($"    \"isMqaStudio\": {(r.IsMqaStudio ? "true" : "false")},");
                Console.WriteLine($"    \"mqaOriginalSampleRate\": {JsonEscape(r.MqaOriginalSampleRate)},");
                Console.WriteLine($"    \"mqaEncoder\": {JsonEscape(r.MqaEncoder)},");
                Console.WriteLine($"    \"isAiGenerated\": {(r.IsAiGenerated ? "true" : "false")},");
                Console.WriteLine($"    \"aiSource\": {JsonEscape(r.AiSource)},");
                Console.WriteLine($"    \"experimentalAiSuspicious\": {(r.ExperimentalAiSuspicious ? "true" : "false")},");
                Console.WriteLine($"    \"experimentalAiConfidence\": {r.ExperimentalAiConfidence:F2},");
                Console.WriteLine($"    \"hasAlbumCover\": {(r.HasAlbumCover ? "true" : "false")}");
                Console.WriteLine($"  }}{comma}");
            }
            Console.WriteLine("]");
        }

        static string JsonEscape(string? s)
        {
            if (s == null) return "null";
            return "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r").Replace("\t", "\\t") + "\"";
        }

        static AudioStatus? ParseStatus(string s)
        {
            return s switch
            {
                "real" or "valid" => AudioStatus.Valid,
                "fake" => AudioStatus.Fake,
                "unknown" => AudioStatus.Unknown,
                "corrupt" => AudioStatus.Corrupt,
                "optimized" => AudioStatus.Optimized,
                _ => null
            };
        }

        static int Error(string message)
        {
            SetColor(ConsoleColor.Red);
            Console.Error.WriteLine($"Error: {message}");
            ResetColor();
            return 1;
        }

        static void SetColor(ConsoleColor color)
        {
            if (!_noColor)
                Console.ForegroundColor = color;
        }

        static void ResetColor()
        {
            if (!_noColor)
                Console.ResetColor();
        }

        // ═══════════════════════════════════════════
        //  Spectrogram
        // ═══════════════════════════════════════════

        static int RunSpectrogram(string[] args)
        {
            if (args.Length == 0 || args.Contains("--help"))
            {
                Console.WriteLine(@"
USAGE: audioauditorcli spectrogram <path> [options]

Generate and save spectrograms as PNG images.

OPTIONS:
  -o, --output <dir>    Output directory for PNG files (default: current directory)
  --width <px>          Image width in pixels (default: 1200)
  --height <px>         Image height in pixels (default: 400)
  --linear              Use linear frequency scale (default: logarithmic)
  --difference          Show L-R channel difference instead of mono
  --recursive, -r       Recurse into subdirectories (default for folders)
  --no-recursive        Do not recurse
  --all                 Generate for all files (same as specifying a folder)

EXAMPLES:
  .\AudioAuditorCLI spectrogram ""song.flac""
  .\AudioAuditorCLI spectrogram ""C:\Music"" -o ""C:\Spectrograms""
  .\AudioAuditorCLI spectrogram ""C:\Music"" --width 1600 --height 600 --linear
");
                return 0;
            }

            var paths = new List<string>();
            string outputDir = ".";
            int width = 1200;
            int height = 400;
            bool linearScale = false;
            SpectrogramChannel channel = SpectrogramChannel.Mono;
            bool recursive = true;

            for (int i = 0; i < args.Length; i++)
            {
                switch (args[i].ToLowerInvariant())
                {
                    case "-o" or "--output" when i + 1 < args.Length: outputDir = args[++i]; break;
                    case "--width" when i + 1 < args.Length: width = Math.Clamp(int.Parse(args[++i]), 200, 8000); break;
                    case "--height" when i + 1 < args.Length: height = Math.Clamp(int.Parse(args[++i]), 100, 4000); break;
                    case "--linear": linearScale = true; break;
                    case "--difference": channel = SpectrogramChannel.Difference; break;
                    case "--no-recursive": recursive = false; break;
                    case "--recursive" or "-r" or "--all": recursive = true; break;
                    default:
                        if (!args[i].StartsWith("-"))
                            paths.Add(args[i]);
                        break;
                }
            }

            if (paths.Count == 0)
                return Error("No input path specified.");

            outputDir = Path.GetFullPath(outputDir);
            Directory.CreateDirectory(outputDir);

            var files = CollectFiles(paths, recursive);
            if (files.Count == 0)
                return Error("No supported audio files found.");

            Console.WriteLine($"Generating spectrograms for {files.Count} file(s)...");
            Console.WriteLine($"Output: {outputDir}");
            Console.WriteLine($"Size: {width} x {height}  Scale: {(linearScale ? "Linear" : "Logarithmic")}  Channel: {channel}");
            Console.WriteLine();

            int success = 0;
            int failed = 0;

            for (int i = 0; i < files.Count; i++)
            {
                string filePath = files[i];
                string fileName = Path.GetFileNameWithoutExtension(filePath);
                // Sanitize filename for output
                string safeName = string.Join("_", fileName.Split(Path.GetInvalidFileNameChars()));
                string outPath = Path.Combine(outputDir, safeName + ".png");

                // Avoid overwriting: add suffix if file exists
                int suffix = 1;
                while (File.Exists(outPath))
                {
                    outPath = Path.Combine(outputDir, $"{safeName}_{suffix}.png");
                    suffix++;
                }

                Console.Write($"\r  [{i + 1}/{files.Count}] {Path.GetFileName(filePath)}...");

                try
                {
                    var result = SpectrogramGenerator.GenerateRawPixels(filePath, width, height, linearScale, channel);
                    if (result != null)
                    {
                        SaveRgb24AsPng(result.Value.pixels, result.Value.width, result.Value.height, outPath);
                        success++;
                    }
                    else
                    {
                        Console.WriteLine($" (skipped: too short or silent)");
                        failed++;
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($" (error: {ex.Message})");
                    failed++;
                }
            }

            Console.WriteLine();
            Console.WriteLine($"Done: {success} saved, {failed} failed/skipped.");
            Console.WriteLine($"Output: {outputDir}");

            CleanupTempDirs();
            return 0;
        }

        static void SaveRgb24AsPng(byte[] rgb24, int width, int height, string outputPath)
        {
            using var bitmap = new SKBitmap(width, height, SKColorType.Rgba8888, SKAlphaType.Opaque);
            var pixels = bitmap.GetPixels();
            unsafe
            {
                byte* dst = (byte*)pixels.ToPointer();
                for (int i = 0; i < width * height; i++)
                {
                    dst[i * 4 + 0] = rgb24[i * 3 + 0]; // R
                    dst[i * 4 + 1] = rgb24[i * 3 + 1]; // G
                    dst[i * 4 + 2] = rgb24[i * 3 + 2]; // B
                    dst[i * 4 + 3] = 255;               // A
                }
            }
            using var image = SKImage.FromBitmap(bitmap);
            using var data = image.Encode(SKEncodedImageFormat.Png, 100);
            using var fs = new FileStream(outputPath, FileMode.Create, FileAccess.Write);
            data.SaveTo(fs);
        }

        // ═══════════════════════════════════════════
        //  CPU / Memory Preset Parsing
        // ═══════════════════════════════════════════

        static int ParseCpuPreset(string value)
        {
            return value.ToLowerInvariant() switch
            {
                "auto" or "balanced" => Math.Max(1, Environment.ProcessorCount / 2),
                "low" => 2,
                "medium" or "med" => 4,
                "high" => 8,
                "max" or "maximum" => 16,
                _ when int.TryParse(value, out int n) => Math.Clamp(n, 1, 32),
                _ => Math.Max(1, Environment.ProcessorCount / 2)
            };
        }

        static int ParseMemoryPreset(string value)
        {
            return value.ToLowerInvariant() switch
            {
                "auto" or "balanced" => 0, // no limit
                "low" => 512,
                "medium" or "med" => 1024,
                "high" => 2048,
                "very-high" or "veryhigh" => 4096,
                "max" or "maximum" => 8192,
                _ when int.TryParse(value, out int n) => Math.Clamp(n, 256, 16384),
                _ => 0
            };
        }

        // ═══════════════════════════════════════════
        //  Temp Directory Cleanup
        // ═══════════════════════════════════════════

        static void CleanupTempDirs()
        {
            lock (_tempDirs)
            {
                foreach (var dir in _tempDirs)
                {
                    try { Directory.Delete(dir, true); } catch { }
                }
                _tempDirs.Clear();
            }
        }
    }
}
