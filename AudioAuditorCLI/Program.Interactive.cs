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
        //  Interactive Mode
        // ═══════════════════════════════════════════

        static async Task<int> RunInteractive(Task<bool> updateCheck)
        {
            bool isWindows = System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(
                System.Runtime.InteropServices.OSPlatform.Windows);

            // Set terminal tab title
            Console.Title = "AudioAuditorCLI";

            SetColor(ConsoleColor.Cyan);
            Console.WriteLine(@"
    _   _   _ ____ ___ ___       _   _   _ ____ ___ _____ ___  ____
   / \ | | | |  _ \_ _/ _ \     / \ | | | |  _ \_ _|_   _/ _ \|  _ \
  / _ \| | | | | | | | | | |   / _ \| | | | | | | |  | || | | | |_) |
 / ___ \ |_| | |_| | | |_| |  / ___ \ |_| | |_| | |  | || |_| |  _ <
/_/   \_\___/|____/___\___/  /_/   \_\___/|____/___| |_| \___/|_| \_\
");
            ResetColor();
            Console.WriteLine($"  AudioAuditor CLI v{GetVersion()} — Interactive Mode");
            Console.WriteLine($"  Type 'help' for commands, 'exit' to quit.\n");

            // Show update notification if available
            if (await TryAwaitUpdateCheckAsync(updateCheck, TimeSpan.FromSeconds(2)))
            {
                var ver = AudioQualityChecker.Services.UpdateChecker.LatestVersion;
                if (ver != null)
                {
                    SetColor(ConsoleColor.Yellow);
                    Console.WriteLine($"  Update available: v{ver} — https://github.com/Angel2mp3/AudioAuditor/releases/latest");
                    ResetColor();
                    Console.WriteLine();
                }
            }

            string lastPath = Directory.GetCurrentDirectory();

            while (true)
            {
                SetColor(ConsoleColor.Green);
                Console.Write("audioauditor");
                ResetColor();
                Console.Write("> ");

                string? input = Console.ReadLine();
                if (input == null) break; // Ctrl+C / EOF
                input = input.Trim();
                if (string.IsNullOrEmpty(input)) continue;

                // Parse the interactive input into command + args
                var parts = SplitInteractiveInput(input);
                if (parts.Length == 0) continue;

                string cmd = parts[0].ToLowerInvariant();

                switch (cmd)
                {
                    case "exit" or "quit" or "q":
                        Console.WriteLine("Goodbye!");
                        return 0;

                    case "help" or "?":
                        PrintInteractiveHelp();
                        break;

                    case "scan" or "analyze":
                        if (parts.Length < 2)
                        {
                            Console.Write("Path to scan: ");
                            string? scanPath = Console.ReadLine()?.Trim().Trim('"');
                            if (string.IsNullOrEmpty(scanPath)) { Console.WriteLine("No path provided."); break; }
                            parts = new[] { cmd, scanPath };
                        }
                        // Remember last scanned path for convenience
                        lastPath = parts[1].Trim('"');
                        await RunAnalyze(parts.Skip(1).ToArray());
                        break;

                    case "info":
                        if (parts.Length < 2)
                        {
                            Console.Write("File path: ");
                            string? infoPath = Console.ReadLine()?.Trim().Trim('"');
                            if (string.IsNullOrEmpty(infoPath)) { Console.WriteLine("No path provided."); break; }
                            parts = new[] { cmd, infoPath };
                        }
                        await RunInfo(parts.Skip(1).ToArray());
                        break;

                    case "export":
                        if (parts.Length < 2)
                        {
                            Console.Write("Path to scan: ");
                            string? exportSrc = Console.ReadLine()?.Trim().Trim('"');
                            if (string.IsNullOrEmpty(exportSrc)) { Console.WriteLine("No path provided."); break; }
                            Console.Write("Output file (e.g. results.csv): ");
                            string? exportDst = Console.ReadLine()?.Trim().Trim('"');
                            if (string.IsNullOrEmpty(exportDst)) { Console.WriteLine("No output specified."); break; }
                            parts = new[] { cmd, exportSrc, "-o", exportDst };
                        }
                        RunExport(parts.Skip(1).ToArray());
                        break;

                    case "metadata" or "meta" or "tags":
                        if (parts.Length < 2)
                        {
                            Console.Write("Action (show/set/strip): ");
                            string? action = Console.ReadLine()?.Trim();
                            if (string.IsNullOrEmpty(action)) { Console.WriteLine("No action specified."); break; }
                            Console.Write("File path: ");
                            string? metaFile = Console.ReadLine()?.Trim().Trim('"');
                            if (string.IsNullOrEmpty(metaFile)) { Console.WriteLine("No path provided."); break; }
                            parts = new[] { cmd, action, metaFile };
                        }
                        RunMetadata(parts.Skip(1).ToArray());
                        break;

                    case "spectrogram" or "spectro":
                        if (parts.Length < 2)
                        {
                            Console.Write("File/folder path: ");
                            string? spectroPath = Console.ReadLine()?.Trim().Trim('"');
                            if (string.IsNullOrEmpty(spectroPath)) { Console.WriteLine("No path provided."); break; }
                            parts = new[] { cmd, spectroPath };
                        }
                        RunSpectrogram(parts.Skip(1).ToArray());
                        break;

                    case "rename":
                        if (parts.Length < 2)
                        {
                            Console.Write("Path to rename: ");
                            string? rnPath = Console.ReadLine()?.Trim().Trim('"');
                            if (string.IsNullOrEmpty(rnPath)) { Console.WriteLine("No path provided."); break; }
                            parts = new[] { cmd, rnPath };
                        }
                        RunRename(parts.Skip(1).ToArray());
                        break;

                    case "duplicates" or "dupes" or "dupe":
                        if (parts.Length < 2)
                        {
                            Console.Write("Folder to scan: ");
                            string? dpPath = Console.ReadLine()?.Trim().Trim('"');
                            if (string.IsNullOrEmpty(dpPath)) { Console.WriteLine("No path provided."); break; }
                            parts = new[] { cmd, dpPath };
                        }
                        RunDuplicates(parts.Skip(1).ToArray());
                        break;

                    case "identify" or "id":
                        if (parts.Length < 2)
                        {
                            Console.Write("File to identify: ");
                            string? idPath = Console.ReadLine()?.Trim().Trim('"');
                            if (string.IsNullOrEmpty(idPath)) { Console.WriteLine("No path provided."); break; }
                            parts = new[] { cmd, idPath };
                        }
                        await RunIdentify(parts.Skip(1).ToArray());
                        break;

                    case "cd":
                        if (parts.Length >= 2)
                        {
                            string dir = parts[1].Trim('"');
                            if (Directory.Exists(dir))
                            {
                                Directory.SetCurrentDirectory(dir);
                                Console.WriteLine($"  Changed to: {Directory.GetCurrentDirectory()}");
                            }
                            else Console.WriteLine($"  Directory not found: {dir}");
                        }
                        else Console.WriteLine($"  Current: {Directory.GetCurrentDirectory()}");
                        break;

                    case "ls" or "dir":
                    {
                        string targetDir = parts.Length >= 2 ? parts[1].Trim('"') : Directory.GetCurrentDirectory();
                        if (!Directory.Exists(targetDir)) { Console.WriteLine($"  Not found: {targetDir}"); break; }
                        var dirs = Directory.GetDirectories(targetDir);
                        var dirFiles = Directory.GetFiles(targetDir);
                        SetColor(ConsoleColor.Cyan);
                        foreach (var d in dirs) Console.WriteLine($"  [DIR] {Path.GetFileName(d)}");
                        ResetColor();
                        int audioCount = 0;
                        foreach (var f in dirFiles)
                        {
                            bool isAudio = SupportedExtensions.Contains(Path.GetExtension(f));
                            bool isArchive = ArchiveExtensions.Contains(Path.GetExtension(f));
                            if (isAudio) { SetColor(ConsoleColor.Green); audioCount++; }
                            else if (isArchive) SetColor(ConsoleColor.Yellow);
                            Console.WriteLine($"  {Path.GetFileName(f)}");
                            ResetColor();
                        }
                        Console.WriteLine($"\n  {dirs.Length} folder(s), {dirFiles.Length} file(s) ({audioCount} audio)");
                        break;
                    }

                    case "version":
                        Console.WriteLine($"AudioAuditor CLI v{GetVersion()}");
                        Console.WriteLine($"Runtime: {System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription}");
                        Console.WriteLine($"OS: {System.Runtime.InteropServices.RuntimeInformation.OSDescription}");
                        break;

                    case "clear" or "cls":
                        Console.Clear();
                        break;

                    case "config":
                        RunConfigInteractive();
                        break;

                    default:
                        // If the input looks like a path, auto-scan it
                        string possiblePath = parts[0].Trim('"');
                        if (File.Exists(possiblePath) || Directory.Exists(possiblePath))
                        {
                            Console.WriteLine($"  Scanning: {possiblePath}");
                            lastPath = possiblePath;
                            await RunAnalyze(parts);
                        }
                        else
                        {
                            ColorWriteLine(ConsoleColor.Yellow, $"  Unknown command: {cmd}. Type 'help' for available commands.");
                        }
                        break;
                }

                Console.WriteLine();
            }

            return 0;
        }

        static void PrintInteractiveHelp()
        {
            Console.WriteLine(@"
  COMMANDS:
    scan <path>          Scan files or folders for quality (alias: analyze)
    scan <path> -v       Scan with verbose output
    scan <path> --json   Scan and output as JSON
    scan <path> --fast   Fast scan using default lightweight detectors
    scan <path> --thorough Enable silence, DR, true peak, LUFS, BPM, rip quality
    info <file>          Detailed analysis of a single file
    export <path> -o f   Analyze and export to file (csv, txt, pdf, xlsx, docx)
    metadata show <file> View file metadata/tags
    metadata set <file>  Edit metadata (add --title, --artist, etc.)
    metadata enrich <p>  Auto-fill missing tags from online sources (--dry-run)
    spectro <path>       Generate spectrogram PNG(s)
    rename <path>        Batch-rename files from tags (preview-first, --dry-run)
    duplicates <path>    Find duplicate tracks in a folder
    identify <file>      Identify a track via AcoustID fingerprint

  NAVIGATION:
    cd <dir>             Change working directory
    ls / dir             List files in current directory
    clear / cls          Clear screen

  OTHER:
    version              Show version info
    help / ?             Show this help
    exit / quit / q      Exit AudioAuditor

  TIPS:
    • Drag a file or folder into this window to paste its path
    • Just type/paste a path to auto-scan it
    • Common flags work here too: --verbose, --status fake, --cpu low, --memory auto
    • Use --no-config, --no-update-check, --no-tips, or --no-fun for quieter runs
");
        }

        /// <summary>
        /// Splits interactive input respecting quoted strings.
        /// e.g. scan "C:\My Music\album" -v → ["scan", "C:\My Music\album", "-v"]
        /// </summary>
        static string[] SplitInteractiveInput(string input)
        {
            var parts = new List<string>();
            bool inQuotes = false;
            var current = new System.Text.StringBuilder();

            for (int i = 0; i < input.Length; i++)
            {
                char c = input[i];
                if (c == '"')
                {
                    inQuotes = !inQuotes;
                }
                else if (c == ' ' && !inQuotes)
                {
                    if (current.Length > 0)
                    {
                        parts.Add(current.ToString());
                        current.Clear();
                    }
                }
                else
                {
                    current.Append(c);
                }
            }

            if (current.Length > 0)
                parts.Add(current.ToString());

            return parts.ToArray();
        }

    }
}
