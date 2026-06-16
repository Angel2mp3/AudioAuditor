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
    {
        // Shared with the GUI via AudioAuditor.Core so the two never drift (see SupportedFormats).
        private static readonly IReadOnlySet<string> SupportedExtensions = SupportedFormats.AudioExtensions;
        private static readonly IReadOnlySet<string> ArchiveExtensions = SupportedFormats.ArchiveExtensions;

        private static readonly List<string> _tempDirs = new();

        private static bool _noColor = false;
        private static bool _noTips = false;
        private static bool _noFun = false;
        private static bool _noEta = true;
        private static readonly string _configPath = AppPaths.AppDataPath("cli-config.txt");

        // Pulsing star frames: breathes in (· → ✦ → ✧ → ★) then back out
        private static readonly string[] StarFrames = { " ", "·", "✦", "✧", "★", "✧", "✦", "·" };

        private static readonly string[] ScanningWords =
        {
            "Analyzing", "Scrutinizing", "Inspecting", "Examining", "Evaluating",
            "Dissecting", "Probing", "Investigating", "Auditing", "Audio-ing",
            "Scanning", "Decoding", "Processing", "Measuring", "Checking",
            "Verifying", "Assessing", "Surveying", "Appraising", "Gauging",
            "Profiling", "Sifting", "Parsing", "Combing", "Cataloging",
            "Classifying", "Reviewing", "Crunching", "Deciphering", "Interrogating",
            "Detecting", "Sampling", "Cross-referencing", "Comparing", "Benchmarking",
            "Indexing", "Quantizing", "Fingerprinting", "Deconstructing",
            "Triangulating", "Calibrating"
        };

        private static readonly string[] Tips =
        {
            "Tip: Use --fast to skip silence, DR, true peak, LUFS, BPM, and rip quality checks.",
            "Tip: Use --thorough when you want the full detector set for a smaller batch.",
            "Tip: Use --json to pipe results into other tools.",
            "Tip: Use --status fake to filter only questionable files.",
            "Tip: You can drag & drop folders into the terminal.",
            "Tip: Use --no-bpm to speed up scans if you don't need BPM.",
            "Tip: Use --cpu low to keep system responsive during large scans.",
            "Tip: Export to PDF for shareable reports: export path -o report.pdf",
            "Tip: Use --verbose for full per-file details on large scans.",
            "Tip: Pipe file lists via stdin: find . -name '*.flac' | audioauditorcli analyze",
            "Tip: Use 'config' in interactive mode to set default flags.",
            "Tip: Use --shlabs for cloud-based AI music detection (quota limited).",
            "Tip: Use 'metadata show file.flac' to view all embedded tags.",
            "Tip: Use --experimental-ai for spectral AI detection patterns.",
            "Tip: Use --rip-quality to check CD rip integrity.",
            "Tip: Use --no-config to ignore saved defaults for one run.",
            "Tip: Use spectrogram command to generate visual frequency plots."
        };

        private static readonly string[] CompletionMessages =
        {
            "All done! Your ears deserve the truth.",
            "Scan complete. No audio fooled us today.",
            "Finished! Every bit has been accounted for.",
            "Analysis wrapped up. Time for a listening session?",
            "Done! Your library just got fact-checked.",
            "Complete! We left no waveform unturned.",
            "All files processed. The verdict is in.",
            "Scan finished. Your audio, fully audited.",
            "That's a wrap! Quality confirmed (or denied).",
            "Done! Now you know what's really in those files."
        };

        static async Task<int> Main(string[] args)
        {
            // Enable UTF-8 output so star/Unicode chars render correctly on Windows
            Console.OutputEncoding = System.Text.Encoding.UTF8;

            // Rejoin args to handle unquoted paths with spaces, then re-split properly
            args = RejoinArgs(args);

            // Check for global flags before command parsing
            if (args.Contains("--no-color") || args.Contains("--no-colour"))
            {
                _noColor = true;
                args = args.Where(a => a != "--no-color" && a != "--no-colour").ToArray();
            }

            if (args.Contains("--no-tips"))
            {
                _noTips = true;
                args = args.Where(a => a != "--no-tips").ToArray();
            }

            if (args.Contains("--no-fun"))
            {
                _noFun = true;
                _noTips = true;
                args = args.Where(a => a != "--no-fun").ToArray();
            }

            if (args.Contains("--eta"))
            {
                _noEta = false;
                args = args.Where(a => a != "--eta").ToArray();
            }
            if (args.Contains("--no-eta")) // kept for backwards compat, now a no-op (ETA already off)
                args = args.Where(a => a != "--no-eta").ToArray();

            // Detect NO_COLOR environment variable (https://no-color.org/)
            if (Environment.GetEnvironmentVariable("NO_COLOR") != null)
                _noColor = true;

            // Load config file defaults (unless --no-config)
            if (!args.Contains("--no-config"))
            {
                args = PrependConfigArgs(args);
            }
            else
            {
                args = args.Where(a => a != "--no-config").ToArray();
            }

            bool skipUpdateCheck = args.Contains("--no-update-check");
            if (skipUpdateCheck)
            {
                args = args.Where(a => a != "--no-update-check").ToArray();
            }

            // ── Integrity check (silent — only alerts on tampered builds) ──
            try
            {
                var (isTampered, _) = AudioQualityChecker.Services.IntegrityVerifier.Verify();
                if (isTampered)
                {
                    if (!_noColor) Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine("╔══════════════════════════════════════════════════════════════╗");
                    Console.WriteLine("║  ⚠  WARNING — POTENTIALLY TAMPERED SOFTWARE DETECTED       ║");
                    Console.WriteLine("╠══════════════════════════════════════════════════════════════╣");
                    Console.WriteLine("║  This copy of AudioAuditor may have been modified and       ║");
                    Console.WriteLine("║  could contain malware.                                     ║");
                    Console.WriteLine("║                                                             ║");
                    Console.WriteLine("║  Official sources ONLY:                                     ║");
                    Console.WriteLine("║    • https://audioauditor.org/                              ║");
                    Console.WriteLine("║    • https://github.com/Angel2mp3/AudioAuditor              ║");
                    Console.WriteLine("║                                                             ║");
                    Console.WriteLine("║  Any other source is NOT official and could be dangerous.   ║");
                    Console.WriteLine("║  Delete this copy and download the genuine version.         ║");
                    Console.WriteLine("╚══════════════════════════════════════════════════════════════╝");
                    if (!_noColor) Console.ResetColor();
                    Console.WriteLine();
                }
            }
            catch { /* never block startup */ }

            // Non-blocking update check — starts in background, prints result if available
            var updateCheck = !skipUpdateCheck
                ? Task.Run(async () =>
                {
                    try { return await AudioQualityChecker.Services.UpdateChecker.CheckForUpdateAsync(GetVersion()); }
                    catch { return false; }
                })
                : Task.FromResult(false);

            if (args.Length == 0)
            {
                return await RunInteractive(updateCheck);
            }

            if (args[0] == "--help" || args[0] == "-h")
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
                "analyze" => await RunAnalyze(args.Skip(1).ToArray()),
                "export" => RunExport(args.Skip(1).ToArray()),
                "metadata" => RunMetadata(args.Skip(1).ToArray()),
                "info" => await RunInfo(args.Skip(1).ToArray()),
                "spectrogram" or "spectro" => RunSpectrogram(args.Skip(1).ToArray()),
                "rename" => RunRename(args.Skip(1).ToArray()),
                "duplicates" or "dupes" or "dupe" => RunDuplicates(args.Skip(1).ToArray()),
                "identify" or "id" => await RunIdentify(args.Skip(1).ToArray()),
                _ => Error($"Unknown command: {args[0]}. Use --help for usage.")
            };

            // Print update notification if the background check found one
            if (await TryAwaitUpdateCheckAsync(updateCheck, TimeSpan.FromSeconds(2)))
            {
                var ver = AudioQualityChecker.Services.UpdateChecker.LatestVersion;
                if (ver != null)
                {
                    Console.WriteLine();
                    if (!_noColor) Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine($"  Update available: v{ver} (current: v{GetVersion()})");
                    Console.WriteLine($"  https://github.com/Angel2mp3/AudioAuditor/releases/latest");
                    if (!_noColor) Console.ResetColor();
                }
            }

            return result;
        }

        static async Task<bool> TryAwaitUpdateCheckAsync(Task<bool> updateCheck, TimeSpan timeout)
        {
            try
            {
                return await updateCheck.WaitAsync(timeout);
            }
            catch (TimeoutException)
            {
                return false;
            }
            catch (OperationCanceledException)
            {
                return false;
            }
            catch
            {
                return false;
            }
        }

        static string GetVersion()
        {
            var asm = System.Reflection.Assembly.GetExecutingAssembly();
            var ver = asm.GetName().Version;
            return ver != null ? $"{ver.Major}.{ver.Minor}.{ver.Build}" : "1.8";
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
            int startIndex = 1;

            if (args[0].Equals("metadata", StringComparison.OrdinalIgnoreCase) &&
                args.Length > 1 &&
                !args[1].StartsWith("-"))
            {
                result.Add(args[1]);
                startIndex = 2;
            }

            for (int i = startIndex; i < args.Length; i++)
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
                        or "--cutoff-allow"
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
                    // Accumulate path parts — prefer the longest valid path
                    pathParts.Add(arg);
                    string joined = string.Join(" ", pathParts);

                    // If the extended path doesn't exist, check if all-but-last was a valid path
                    if (!File.Exists(joined) && !Directory.Exists(joined) && pathParts.Count > 1)
                    {
                        string prevJoined = string.Join(" ", pathParts.Take(pathParts.Count - 1));
                        if (File.Exists(prevJoined) || Directory.Exists(prevJoined))
                        {
                            // Previous parts formed a valid path — flush and start new
                            result.Add(prevJoined);
                            pathParts.Clear();
                            pathParts.Add(arg);
                        }
                    }
                    // Otherwise keep accumulating — the full join may become valid with more parts
                }
            }

            // Flush remaining path parts
            if (pathParts.Count > 0)
                result.Add(string.Join(" ", pathParts));

            return result.ToArray();
        }

        static string[] PrependConfigArgs(string[] args)
        {
            try
            {
                if (!File.Exists(_configPath)) return args;
                var configLines = File.ReadAllLines(_configPath)
                    .Select(l => l.Trim())
                    .Where(l => !string.IsNullOrEmpty(l) && !l.StartsWith('#'));
                var configArgs = new List<string>();
                foreach (var line in configLines)
                {
                    // Split "flag value" lines into separate args
                    var parts = line.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
                    configArgs.AddRange(parts);
                }
                if (configArgs.Count == 0) return args;
                // Config args go first so explicit CLI args can override
                return configArgs.Concat(args).ToArray();
            }
            catch { return args; }
        }

        // Interactive Mode - see Program.Interactive.cs
        // ═══════════════════════════════════════════
        //  Config
        // ═══════════════════════════════════════════

        static void RunConfigInteractive()
        {
            Console.WriteLine($"  Config file: {_configPath}");
            if (File.Exists(_configPath))
            {
                Console.WriteLine("  Current config:");
                SetColor(ConsoleColor.DarkGray);
                foreach (var line in File.ReadAllLines(_configPath))
                    Console.WriteLine($"    {line}");
                ResetColor();
            }
            else
            {
                Console.WriteLine("  No config file found.");
            }

            Console.WriteLine();
            Console.WriteLine("  Commands: [show / edit / reset / path]");
            Console.Write("  > ");
            string? action = Console.ReadLine()?.Trim().ToLowerInvariant();

            switch (action)
            {
                case "edit":
                    Console.WriteLine("  Enter flags (one per line, blank line to finish):");
                    Console.WriteLine("  Example: --threads 4");
                    Console.WriteLine("  Example: --cpu low");
                    Console.WriteLine("  Example: --memory auto");
                    Console.WriteLine("  Example: --thorough");
                    Console.WriteLine("  Example: --fast");
                    Console.WriteLine("  Example: --rip-quality");
                    Console.WriteLine("  Example: --experimental-ai");
                    Console.WriteLine("  Example: --shlabs");
                    Console.WriteLine("  Example: --no-bpm");
                    Console.WriteLine("  Example: --no-silence");
                    Console.WriteLine("  Example: --status fake");
                    Console.WriteLine("  Example: # comment lines are ignored");
                    var lines = new List<string>();
                    while (true)
                    {
                        Console.Write("    ");
                        string? line = Console.ReadLine();
                        if (string.IsNullOrEmpty(line)) break;
                        lines.Add(line);
                    }
                    Directory.CreateDirectory(Path.GetDirectoryName(_configPath)!);
                    File.WriteAllLines(_configPath, lines);
                    Console.WriteLine($"  Saved {lines.Count} line(s) to config.");
                    break;

                case "reset":
                    if (File.Exists(_configPath))
                    {
                        File.Delete(_configPath);
                        Console.WriteLine("  Config file deleted.");
                    }
                    else Console.WriteLine("  No config file to delete.");
                    break;

                case "path":
                    Console.WriteLine($"  {_configPath}");
                    break;

                default:
                    // show — already shown above
                    break;
            }
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
  metadata     View, edit, or auto-enrich audio file metadata
  info         Show detailed info for a single file
  spectrogram  Generate and save spectrograms as PNG images
  rename       Batch-rename files from their tags (preview-first)
  duplicates   Find duplicate tracks in a folder
  identify     Identify a track via AcoustID fingerprint

GLOBAL OPTIONS:
  --cpu <mode>     CPU usage mode: auto, low (2), medium (4), high (8), max (16)
  --memory <mb>    Memory limit in MB (512-8192), or: auto, low, medium, high, max
  --no-config      Ignore saved CLI config defaults for this run
  --no-update-check Skip the background update check
  --no-color       Disable colored output
  --no-tips        Disable random scan tips
  --no-fun         Disable scanning annotations, tips, and completion messages
  --eta            Show estimated time remaining during scan (default: off)
  --no-eta         Accepted for compatibility; ETA is off unless --eta is used
  --version, -V    Show version information

COMMON ANALYZE OPTIONS:
  --verbose, -v     Show detailed per-file analysis
  --json            Output scan results as JSON
  --status <s>      Filter: real, fake, unknown, corrupt, optimized
  --recursive, -r   Recurse into folders
  --no-recursive    Do not recurse into folders
  --fast            Force lightweight scan defaults
  --thorough        Enable silence, DR, true peak, LUFS, BPM, and rip quality
  --silence         Enable silence detection
  --dynamic-range   Enable dynamic range measurement
  --true-peak       Enable true peak measurement
  --lufs            Enable integrated LUFS measurement
  --bpm             Enable BPM detection
  --rip-quality     Enable rip/encode quality detection
  --experimental-ai Enable experimental spectral AI detection
  --shlabs          Enable SH Labs AI detection
  --no-ai           Disable the standard AI watermark detector
  --always-full     Always run a full-file pass even when detectors are off
  --cutoff-allow <hz> Don't flag as fake when frequency cutoff >= this Hz (default 19600)

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

        // Analyze + Export + Metadata + Info commands - see Program.Commands.cs
        // Console UI helpers (progress, colors, tables, exporters) - see Program.ConsoleUI.cs
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
                    case "--width" when i + 1 < args.Length:
                        if (int.TryParse(args[++i], out int w)) width = Math.Clamp(w, 200, 8000);
                        else return Error($"Invalid value for --width: {args[i]}");
                        break;
                    case "--height" when i + 1 < args.Length:
                        if (int.TryParse(args[++i], out int h)) height = Math.Clamp(h, 100, 4000);
                        else return Error($"Invalid value for --height: {args[i]}");
                        break;
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

                WriteProgress($"  [{i + 1}/{files.Count}] {Path.GetFileName(filePath)}...");

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
