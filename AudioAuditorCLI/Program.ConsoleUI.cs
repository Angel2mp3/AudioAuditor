using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text.Json;
using SharpCompress.Archives;
using SharpCompress.Common;
using SharpCompress.Readers;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SkiaSharp;
using AudioQualityChecker.Abstractions;
using AudioQualityChecker.Models;
using AudioQualityChecker.Services;

namespace AudioQualityChecker.CLI
{
    partial class Program
    {        // ═══════════════════════════════════════════
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
                tempDir = Path.Combine(Path.GetTempPath(), "AudioAuditor_" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(tempDir);

                const int MaxArchiveEntries = 50_000;
                const long MaxExtractedBytes = 5L * 1024 * 1024 * 1024; // 5 GB

                string ext = Path.GetExtension(archivePath);
                if (ext.Equals(".zip", StringComparison.OrdinalIgnoreCase))
                {
                    ZipFile.ExtractToDirectory(archivePath, tempDir);
                }
                else
                {
                    // Use SharpCompress for RAR, 7z, tar, tgz, etc.
                    using var archive = ArchiveFactory.OpenArchive(archivePath, new ReaderOptions());
                    string safeBase = Path.GetFullPath(tempDir) + Path.DirectorySeparatorChar;
                    int entryCount = 0;
                    long totalBytes = 0;
                    foreach (var entry in archive.Entries.Where(e => !e.IsDirectory && e.Key != null))
                    {
                        if (++entryCount > MaxArchiveEntries)
                        {
                            Console.Error.WriteLine($"  Warning: archive has too many entries; stopping at {MaxArchiveEntries}.");
                            break;
                        }
                        totalBytes += entry.Size;
                        if (totalBytes > MaxExtractedBytes)
                        {
                            Console.Error.WriteLine("  Warning: archive extraction limit reached (5 GB); stopping.");
                            break;
                        }
                        // ZIP slip guard: verify entry path stays inside tempDir
                        string entryKey = entry.Key!.Replace('/', Path.DirectorySeparatorChar);
                        string fullDest = Path.GetFullPath(Path.Combine(tempDir, entryKey));
                        if (!fullDest.StartsWith(safeBase, StringComparison.OrdinalIgnoreCase))
                        {
                            Console.Error.WriteLine($"  Skipping suspicious archive entry: {entry.Key}");
                            continue;
                        }
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

        static AnalysisSettingsSnapshot CreateAnalysisSettingsSnapshot()
        {
            return AnalysisSettingsSnapshot.From(AudioAnalyzer.ActiveSettings);
        }

        static AudioFileInfo AnalyzeFileWithTimeout(string filePath, IAnalysisSettings settings, CancellationToken ct, out bool completed)
        {
            completed = false;
            try
            {
                var analysisTask = Task.Factory.StartNew(
                    () => AudioAnalyzer.AnalyzeFile(filePath, settings, ct),
                    ct,
                    TaskCreationOptions.LongRunning,
                    TaskScheduler.Default);
                if (!analysisTask.Wait(TimeSpan.FromSeconds(120), ct))
                {
                    return CreateAnalysisError(filePath, "Analysis timed out");
                }

                var result = analysisTask.GetAwaiter().GetResult();
                completed = true;
                return result;
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                return CreateAnalysisError(filePath, $"Failed to open or analyze: {ex.Message}");
            }
        }

        static AudioFileInfo CreateAnalysisError(string filePath, string message)
        {
            return new AudioFileInfo
            {
                FilePath = filePath,
                FileName = Path.GetFileName(filePath),
                FolderPath = Path.GetDirectoryName(filePath) ?? "",
                Extension = Path.GetExtension(filePath).ToLowerInvariant(),
                Status = AudioStatus.Unknown,
                ErrorMessage = message
            };
        }

        static List<AudioFileInfo> AnalyzeFiles(List<string> files, IAnalysisSettings settings, int threads, bool showProgress, int memoryLimitMb, out bool userStopped)
        {
            var analysisSettings = AnalysisSettingsSnapshot.From(settings);
            var results = new ConcurrentBag<AudioFileInfo>();
            int completed = 0;
            var rng = new Random();
            // Tracks timestamps (TickCount64) of each file completion for rolling ETA
            var completionTimes = new System.Collections.Concurrent.ConcurrentQueue<long>();

            // Show a random tip ~30% of the time (suppressed by --no-tips or --no-fun)
            if (showProgress && !_noTips && !_noFun && !Console.IsOutputRedirected && rng.NextDouble() < 0.3)
            {
                SetColor(ConsoleColor.DarkGray);
                Console.WriteLine($"  {Tips[rng.Next(Tips.Length)]}  (--no-tips to disable)");
                ResetColor();
            }

            // Pause/cancel primitives
            using var cts = new CancellationTokenSource();
            using var animCts = new CancellationTokenSource(); // separate lifetime: keeps anim alive during drain
            var pauseEvent = new System.Threading.ManualResetEventSlim(true); // set = running
            bool isPaused = false;
            var startTime = DateTime.Now;

            // ANSI support: used for two-line display and colour — Win10+, WT, or TERM set
            bool useAnsi = showProgress && !Console.IsOutputRedirected && !_noColor && (
                Environment.GetEnvironmentVariable("WT_SESSION") != null ||
                Environment.GetEnvironmentVariable("TERM") != null ||
                (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(
                    System.Runtime.InteropServices.OSPlatform.Windows) &&
                    Environment.OSVersion.Version.Build >= 10586));

            // Static help line — printed once, never redrawn (replaces per-tick "p=pause q=stop" spam)
            if (showProgress && !Console.IsOutputRedirected)
            {
                SetColor(ConsoleColor.DarkGray);
                Console.WriteLine("  Commands:  p = pause/resume    s = stop");
                ResetColor();
                try { Console.CursorVisible = false; } catch { }
            }

            // Input task — single-key commands, no Enter required
            //   p = toggle pause/resume   r = resume   q or s = stop scan
            Task? inputTask = null;
            if (showProgress && !Console.IsInputRedirected && !Console.IsOutputRedirected)
            {
                inputTask = Task.Run(() =>
                {
                    while (!cts.IsCancellationRequested)
                    {
                        if (!Console.KeyAvailable) { Thread.Sleep(30); continue; }
                        var ki = Console.ReadKey(intercept: true);
                        switch (char.ToLowerInvariant(ki.KeyChar))
                        {
                            case 'p':
                                isPaused = !isPaused;
                                if (isPaused) pauseEvent.Reset();
                                else pauseEvent.Set();
                                break;
                            case 'r':
                                isPaused = false;
                                pauseEvent.Set();
                                break;
                            case 'q':
                            case 's':
                                cts.Cancel();
                                pauseEvent.Set(); // unblock any waiting workers
                                break;
                        }
                    }
                });
            }

            // Animation task — redraws progress every 250ms via cursor positioning (stable across worker output)
            Task? animTask = null;
            int progressRow = -1; // captured on first draw; updated in place each tick
            object consoleLock = new();
            int lastRedirectedProgress = 0;
            if (showProgress && !Console.IsOutputRedirected)
            {
                animTask = Task.Run(async () =>
                {
                    var animRng = new Random();
                    int tickCount = 0;
                    int wordIdx = animRng.Next(ScanningWords.Length);
                    long wordChangeTick = Environment.TickCount64;
                    long nextWordDelay = 15000 + animRng.Next(10000); // 15–25 s between words
                    string cachedEta = "";
                    long etaCalcTick = 0;
                    int smoothedRemainSec = -1;
                    bool firstDraw = true;
                    int snapOnPause = -1;

                    // Shimmy state — letter-by-letter morph between consecutive words
                    string shimmyFrom = ScanningWords[wordIdx];
                    string shimmyTo   = ScanningWords[wordIdx];
                    int    shimmyPos  = int.MaxValue;   // MaxValue = shimmy complete
                    long   shimmyTickMs = Environment.TickCount64;
                    const int ShimmyLetterMs = 120;     // ms per letter step (~8 letters/sec)

                    while (!animCts.Token.IsCancellationRequested)
                    {
                        int snap = Volatile.Read(ref completed);
                        int total = files.Count;
                        long now = Environment.TickCount64;
                        bool paused = isPaused;
                        bool stopping = cts.Token.IsCancellationRequested;

                        // Kick off a new shimmy every 9–13 s — freeze when paused or stopping
                        if (!_noFun && !paused && !stopping && now - wordChangeTick > nextWordDelay
                            && shimmyPos >= Math.Max(shimmyFrom.Length, shimmyTo.Length))
                        {
                            int nextIdx = (wordIdx + 1) % ScanningWords.Length;
                            shimmyFrom   = ScanningWords[wordIdx];
                            shimmyTo     = ScanningWords[nextIdx];
                            shimmyPos    = 0;
                            shimmyTickMs = now;
                            wordIdx      = nextIdx;
                            wordChangeTick = now;
                            nextWordDelay  = 15000 + animRng.Next(10000);
                        }

                        // Advance shimmy position based on elapsed time
                        int maxLen = Math.Max(shimmyFrom.Length, shimmyTo.Length);
                        if (shimmyPos <= maxLen
                            && now - shimmyTickMs >= ShimmyLetterMs)
                        {
                            int steps = Math.Min(1, (int)((now - shimmyTickMs) / ShimmyLetterMs));
                            shimmyPos    += steps;
                            shimmyTickMs += steps * ShimmyLetterMs;
                        }

                        // Build current display word: chars 0..shimmyPos from 'to', rest from 'from'
                        string currentWord;
                        if (!_noFun && shimmyPos <= maxLen)
                        {
                            var sb = new System.Text.StringBuilder(maxLen);
                            for (int ci = 0; ci < maxLen; ci++)
                            {
                                if (ci < shimmyPos)
                                    sb.Append(ci < shimmyTo.Length ? shimmyTo[ci] : ' ');
                                else
                                    sb.Append(ci < shimmyFrom.Length ? shimmyFrom[ci] : ' ');
                            }
                            currentWord = sb.ToString().TrimEnd();
                        }
                        else
                        {
                            currentWord = ScanningWords[wordIdx];
                        }

                        // Star frame (250ms × 8 = 2s cycle)
                        string star = StarFrames[tickCount % StarFrames.Length];
                        tickCount++;

                        // ETA — rolling 30s window + exponential smoothing (frozen when paused or stopping)
                        if (!_noEta && !paused && !stopping && snap > 0 && snap < total && (now - etaCalcTick > 1000 || etaCalcTick == 0))
                        {
                            long oldest = 0;
                            while (completionTimes.TryPeek(out oldest) && now - oldest > 30000)
                                completionTimes.TryDequeue(out _);

                            double rate = 0;
                            int windowCount = completionTimes.Count;
                            if (windowCount >= 3 && completionTimes.TryPeek(out oldest))
                            {
                                long windowSpanMs = now - oldest;
                                if (windowSpanMs > 0)
                                    rate = windowCount / (double)windowSpanMs;
                            }
                            if (rate <= 0)
                            {
                                double elapsedMs = (DateTime.Now - startTime).TotalMilliseconds;
                                if (elapsedMs > 0) rate = snap / elapsedMs;
                            }
                            if (rate > 0)
                            {
                                int rawRemain = (int)((total - snap) / rate / 1000.0);
                                smoothedRemainSec = smoothedRemainSec < 0
                                    ? rawRemain
                                    : (int)(smoothedRemainSec * 0.75 + rawRemain * 0.25);
                                cachedEta = smoothedRemainSec < 10 ? "  ETA <10s"
                                          : smoothedRemainSec < 60 ? $"  ETA {smoothedRemainSec}s"
                                          : $"  ETA {smoothedRemainSec / 60}m {smoothedRemainSec % 60:D2}s";
                            }
                            etaCalcTick = now;
                        }
                        if (snap == 0 || snap >= total) { cachedEta = ""; smoothedRemainSec = -1; }

                        try
                        {
                            int width = Math.Max(40, Console.WindowWidth - 2);
                            int pct = total > 0 ? snap * 100 / total : 0;
                            string etaPart = _noEta ? "" : cachedEta;

                            // Track drain state: how many files were in-flight when pause was pressed
                            if (!paused) snapOnPause = -1;
                            else if (snapOnPause < 0) snapOnPause = snap;

                            // Hide scanning word when paused or stopping — fewer distractions
                            string wordPart = (!_noFun && !paused && !stopping) ? $"  {currentWord}..." : "";

                            // Status suffix reflects exact state
                            string pauseSuffix;
                            if (stopping)
                                pauseSuffix = "   [STOPPING...]";
                            else if (paused)
                            {
                                bool draining = snap > snapOnPause;
                                if (draining) snapOnPause = snap; // advance marker as more files complete
                                pauseSuffix = draining ? "   [FINISHING IN-FLIGHT...]" : "   [PAUSED]";
                            }
                            else
                                pauseSuffix = "";

                            string line1Content = $" [{snap}/{total}] {pct}%{etaPart}{wordPart}{pauseSuffix}";
                            if (line1Content.Length > width - 3) line1Content = line1Content[..(width - 3)];
                            string line1Padded = line1Content.PadRight(width - 3);
                            string starColor = stopping ? "\x1B[31m" : paused ? "\x1B[33m" : "\x1B[36m"; // red=stopping, yellow=paused, cyan=running
                            string line1 = useAnsi ? $"  {starColor}{star}\x1B[0m{line1Padded}" : $"  {star}{line1Padded}";

                            if (useAnsi)
                            {
                                lock (consoleLock)
                                {
                                    if (firstDraw)
                                    {
                                        Console.WriteLine(line1);
                                        progressRow = Console.CursorTop - 1;
                                        firstDraw = false;
                                    }
                                    else
                                    {
                                        int targetRow = progressRow;
                                        int maxRow = Console.BufferHeight - 1;
                                        if (targetRow < 0 || targetRow > maxRow) targetRow = Math.Max(0, maxRow - 1);
                                        try
                                        {
                                            int savedTop = Console.CursorTop;
                                            int savedLeft = Console.CursorLeft;
                                            Console.SetCursorPosition(0, targetRow);
                                            Console.Write("\x1B[2K" + line1);
                                            // Restore cursor to wherever it was (key-read thread, etc.)
                                            if (savedTop != targetRow)
                                                Console.SetCursorPosition(savedLeft, savedTop);
                                            else
                                                Console.SetCursorPosition(0, savedTop + 1);
                                        }
                                        catch { /* terminal resized/scrolled — skip this tick */ }
                                    }
                                }
                            }
                            else
                            {
                                // Non-ANSI fallback: single overwrite line
                                Console.Write($"\r  {star}{line1Padded}");
                            }
                        }
                        catch { }

                        try { await Task.Delay(250, animCts.Token); }
                        catch (OperationCanceledException) { break; }
                    }
                });
            }

            // Analysis — Parallel.ForEach with NoBuffering partitioner for dynamic work-stealing.
            // File analysis uses dedicated threads so decoder hangs do not starve the ThreadPool.
            userStopped = false;
            AudioAnalyzer.PauseEvent = pauseEvent;
            try
            {
                var partitioner = System.Collections.Concurrent.Partitioner.Create(
                    files, System.Collections.Concurrent.EnumerablePartitionerOptions.NoBuffering);

                Parallel.ForEach(partitioner,
                    new ParallelOptions { MaxDegreeOfParallelism = threads > 0 ? threads : Environment.ProcessorCount, CancellationToken = cts.Token },
                    filePath =>
                    {
                        // Block here while paused; unblocks instantly on resume or cancel
                        pauseEvent.Wait(cts.Token);

                        // Memory hint: one quick gen-0 GC if over limit, then continue.
                        // Blocking loops with GC.Collect(2) destroy scan throughput.
                        if (memoryLimitMb > 0)
                        {
                            long limitBytes = (long)memoryLimitMb * 1024 * 1024;
                            if (System.Diagnostics.Process.GetCurrentProcess().WorkingSet64 > limitBytes)
                                GC.Collect(0, GCCollectionMode.Optimized, false);
                        }

                        // Scan-cache lookup
                        AudioFileInfo? result = null;
                        try
                        {
                            var fi = new FileInfo(filePath);
                            if (ScanCacheService.TryGet(filePath, fi.Length, fi.LastWriteTimeUtc, analysisSettings, out var cached) && cached != null)
                                result = cached;
                        }
                        catch { }

                        if (result == null)
                        {
                            result = AnalyzeFileWithTimeout(filePath, analysisSettings, cts.Token, out bool completedAnalysis);
                            if (completedAnalysis)
                                try { ScanCacheService.Set(result, analysisSettings); } catch { }
                        }
                        results.Add(result);
                        int completedNow = Interlocked.Increment(ref completed);
                        completionTimes.Enqueue(Environment.TickCount64);

                        // Fallback plain-text progress for redirected output
                        if (showProgress && Console.IsOutputRedirected)
                        {
                            lock (consoleLock)
                            {
                                if (completedNow > lastRedirectedProgress)
                                {
                                    lastRedirectedProgress = completedNow;
                                    Console.WriteLine($"  [{completedNow}/{files.Count}] {completedNow * 100 / files.Count}%");
                                }
                            }
                        }
                    });
            }
            catch (OperationCanceledException)
            {
                userStopped = true;
            }
            finally
            {
                AudioAnalyzer.PauseEvent = null;
            }

            cts.Cancel();
            pauseEvent.Set(); // unblock any workers that are still waiting
            animCts.Cancel(); // stop animation now that drain is complete
            if (animTask != null) try { animTask.Wait(1000); } catch { }
            if (inputTask != null) try { inputTask.Wait(500); } catch { }

            if (showProgress && !Console.IsOutputRedirected)
            {
                try { Console.CursorVisible = true; } catch { }
                if (useAnsi)
                {
                    lock (consoleLock)
                    {
                        try
                        {
                            // Move cursor to the progress row and clear it plus any rows below.
                            int targetRow = progressRow >= 0 ? progressRow : Math.Max(0, Console.CursorTop - 1);
                            Console.SetCursorPosition(0, targetRow);
                            Console.Write("\x1B[J"); // clear from cursor to end of screen
                        }
                        catch
                        {
                            // Fallback: relative ANSI
                            Console.Write("\x1B[1A\r\x1B[J");
                        }
                    }
                }
                else
                    Console.WriteLine();
            }

            if (userStopped)
            {
                int c = Volatile.Read(ref completed);
                SetColor(ConsoleColor.Yellow);
                Console.WriteLine($"\n  Scan stopped. {c} of {files.Count} files processed.");
                ResetColor();
            }

            return results.ToList();
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
                    if (r.IsAnyAiDetected) flags.Add($"AI: {r.AiVerdict} ({r.AiCombinedConfidence:F0}%)");
                    if (r.ExperimentalAiSuspicious) flags.Add($"Spectral AI: {r.ExperimentalAiConfidence:P0}");
                    if (r.HasReplayGain) flags.Add($"RG: {r.ReplayGain:+0.0;-0.0;0.0} dB");
                    if (r.IsFakeStereo) flags.Add($"Fake Stereo: {r.FakeStereoType}");
                    if (r.HasExcessiveSilence) flags.Add("Excessive Silence");
                    if (r.HasAlbumCover) flags.Add("Cover: Yes");
                    if (r.HasTruePeak) flags.Add($"TP: {r.TruePeakDisplay}");
                    if (r.HasLufs) flags.Add($"LUFS: {r.LufsDisplay}");
                    if (r.HasRipQuality) flags.Add($"Rip: {r.RipQuality}");
                    if (r.SHLabsScanned) flags.Add($"SHLabs: {r.SHLabsPrediction}");
                    if (r.IsCueVirtualTrack) flags.Add("Cue Track");
                    if (flags.Count > 0)
                        Console.WriteLine($"  Flags:     {string.Join(" | ", flags)}");

                    if (verbose)
                    {
                        Console.WriteLine($"  Path:      {r.FilePath}");
                        if (r.MaxSampleLevel > 0) Console.WriteLine($"  Peak:      {r.MaxSampleLevelDb:F1} dBFS");
                        if (r.HasTruePeak) Console.WriteLine($"  True Peak: {r.TruePeakDisplay}");
                        if (r.HasLufs) Console.WriteLine($"  LUFS:      {r.LufsDisplay}");
                        if (r.HasRipQuality) Console.WriteLine($"  Rip Qual:  {r.RipQuality} — {r.RipQualityDetail}");
                        if (r.SHLabsScanned) Console.WriteLine($"  SH Labs:   {r.SHLabsPrediction} ({r.SHLabsProbability:F0}%) [{r.SHLabsAiType}]");
                        if (r.IsMqa) Console.WriteLine($"  MQA Info:  Original SR: {r.MqaOriginalSampleRate} | Encoder: {r.MqaEncoder}");
                        if (r.Frequency > 0) Console.WriteLine($"  Dom Freq:  {r.Frequency:N0} Hz");
                        if (r.IsCueVirtualTrack) Console.WriteLine($"  Cue Sheet: {r.CueSheetPath} (Track {r.CueTrackNumber})");
                        if (!string.IsNullOrEmpty(r.ErrorMessage)) Console.WriteLine($"  Error:     {r.ErrorMessage}");
                    }

                    ResetColor();
                }
                Console.WriteLine("───────────────────────────────────────");
            }
            else
            {
                // For large result sets, just show fakes/corrupt/flagged files
                var flagged = results.Where(r => r.Status == AudioStatus.Fake || r.Status == AudioStatus.Corrupt || r.IsAiPossibleOrYes || r.HasClipping || r.HasScaledClipping || r.IsFakeStereo || r.HasExcessiveSilence || (r.HasRipQuality && r.RipQuality == "Bad")).ToList();
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
                        if (r.IsAiPossibleOrYes) reason += (reason.Length > 0 ? " + " : "") + "AI";
                        if (r.HasClipping) reason += (reason.Length > 0 ? " + " : "") + "CLIP";
                        if (r.HasScaledClipping) reason += (reason.Length > 0 ? " + " : "") + "SCALE";
                        if (r.IsFakeStereo) reason += (reason.Length > 0 ? " + " : "") + "STEREO";
                        if (r.HasExcessiveSilence) reason += (reason.Length > 0 ? " + " : "") + "SILENCE";
                        if (r.HasRipQuality && r.RipQuality == "Bad") reason += (reason.Length > 0 ? " + " : "") + "RIP";
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
            int aiYes = results.Count(r => r.AiVerdict == "Yes");
            int aiPossible = results.Count(r => r.AiVerdict == "Possible");
            int clipping = results.Count(r => r.HasClipping || r.HasScaledClipping);
            int withReplayGain = results.Count(r => r.HasReplayGain);
            int withCover = results.Count(r => r.HasAlbumCover);
            if (mqa > 0) Console.WriteLine($"  MQA:       {mqa}");
            if (aiYes > 0)      Console.WriteLine($"  AI (Yes):       {aiYes}");
            if (aiPossible > 0) Console.WriteLine($"  AI (Possible):  {aiPossible}");
            int spectralAi = results.Count(r => r.ExperimentalAiSuspicious);
            if (spectralAi > 0) Console.WriteLine($"  Spectral:  {spectralAi} (experimental)");
            int shLabsAi = results.Count(r => r.SHLabsScanned && r.SHLabsPrediction != "Human Made");
            if (shLabsAi > 0) Console.WriteLine($"  SH Labs AI:{shLabsAi}");
            if (clipping > 0) Console.WriteLine($"  Clipping:  {clipping}");
            int fakeStereo = results.Count(r => r.IsFakeStereo);
            if (fakeStereo > 0) Console.WriteLine($"  FakeStereo:{fakeStereo}");
            int badRip = results.Count(r => r.HasRipQuality && r.RipQuality == "Bad");
            if (badRip > 0) Console.WriteLine($"  Bad Rip:   {badRip}");
            int cueVirtual = results.Count(r => r.IsCueVirtualTrack);
            if (cueVirtual > 0) Console.WriteLine($"  Cue Tracks:{cueVirtual}");
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
            if (r.HasTruePeak) Console.WriteLine($"True Peak:       {r.TruePeakDisplay}");
            if (r.HasLufs) Console.WriteLine($"Integrated LUFS: {r.LufsDisplay}");
            Console.WriteLine($"BPM:             {(r.Bpm > 0 ? r.Bpm.ToString() : "-")}");
            Console.WriteLine($"Replay Gain:     {(r.HasReplayGain ? $"{r.ReplayGain:F1} dB" : "-")}");
            Console.WriteLine($"Dynamic Range:   {r.DynamicRangeDisplay}");
            if (r.HasRipQuality) Console.WriteLine($"Rip Quality:     {r.RipQualityDisplay}");
            Console.WriteLine($"Album Cover:     {(r.HasAlbumCover ? "Yes" : "No")}");
            if (r.IsAlac) Console.WriteLine($"ALAC:            Yes");
            Console.WriteLine();
            Console.WriteLine($"Fake Stereo:     {r.FakeStereoDisplay}");
            if (r.IsFakeStereo)
                Console.WriteLine($"Stereo Corr:     {r.StereoCorrelation:F4}");
            Console.WriteLine($"Silence:         {r.SilenceDisplay}");
            if (r.TotalMidSilenceMs > 0) Console.WriteLine($"Mid Silence:     {r.TotalMidSilenceMs:N0} ms");
            Console.WriteLine($"Date Modified:   {r.DateModifiedDisplay}");
            Console.WriteLine($"Date Created:    {r.DateCreatedDisplay}");
            Console.WriteLine();
            if (r.IsCueVirtualTrack)
            {
                Console.WriteLine($"Cue Sheet:       {r.CueSheetPath}");
                Console.WriteLine($"Cue Track:       {r.CueTrackNumber}");
                Console.WriteLine($"Cue Start:       {r.CueStartTime}");
                Console.WriteLine($"Cue End:         {r.CueEndTime}");
                Console.WriteLine();
            }
            Console.WriteLine($"MQA:             {(r.IsMqa ? (r.IsMqaStudio ? "MQA Studio" : "MQA") : "No")}");
            if (r.IsMqa)
            {
                Console.WriteLine($"MQA Original SR: {r.MqaOriginalSampleRate}");
                Console.WriteLine($"MQA Encoder:     {r.MqaEncoder}");
            }
            // Overall verdict combining whichever detectors ran
            var verdictColor = r.AiVerdict switch
            {
                "Yes" => ConsoleColor.Red,
                "Possible" => ConsoleColor.Yellow,
                _ => ConsoleColor.Gray
            };
            Console.Write("AI Detection:    ");
            SetColor(verdictColor);
            Console.WriteLine(r.IsAnyAiDetected
                ? $"{r.AiVerdict} ({r.AiCombinedConfidence:F0}% confidence)"
                : $"{r.AiVerdict}");
            ResetColor();
            if (r.IsAiGenerated)
                Console.WriteLine($"  Watermark:     {r.AiSource}");
            if (r.AiSources?.Count > 0)
                Console.WriteLine($"  Sources:       {string.Join(", ", r.AiSources)}");
            if (r.ExperimentalAiSuspicious)
            {
                Console.WriteLine($"  Spectral:      Suspicious ({r.ExperimentalAiConfidence:P0})");
                Console.WriteLine($"    Flags:       {string.Join(", ", r.ExperimentalAiFlags)}");
            }
            if (r.SHLabsScanned)
            {
                Console.WriteLine($"  SH Labs:       {r.SHLabsPrediction} ({r.SHLabsProbability:F0}%)");
                Console.WriteLine($"    Confidence:  {r.SHLabsConfidence:F1}%");
                if (!string.IsNullOrEmpty(r.SHLabsAiType))
                    Console.WriteLine($"    AI Type:     {r.SHLabsAiType}");
            }

            if (!string.IsNullOrEmpty(r.ErrorMessage))
                Console.WriteLine($"\nError:           {r.ErrorMessage}");
        }

        static void PrintCompletionMessage()
        {
            if (Console.IsOutputRedirected) return;
            if (_noFun) return;
            var rng = new Random();
            if (rng.NextDouble() < 0.25)
            {
                SetColor(ConsoleColor.DarkGray);
                Console.WriteLine($"\n  {CompletionMessages[rng.Next(CompletionMessages.Length)]}");
                ResetColor();
            }
        }

        static void PrintJson(List<AudioFileInfo> results)
        {
            var output = results.Select(r => new
            {
                fileName = r.FileName,
                filePath = r.FilePath,
                status = r.Status.ToString(),
                artist = r.Artist,
                title = r.Title,
                extension = r.Extension,
                sampleRate = r.SampleRate,
                bitsPerSample = r.BitsPerSample,
                channels = r.Channels,
                duration = r.Duration,
                durationSeconds = Math.Round(r.DurationSeconds, 2),
                fileSize = r.FileSize,
                fileSizeBytes = r.FileSizeBytes,
                reportedBitrate = r.ReportedBitrate,
                actualBitrate = r.ActualBitrate,
                effectiveFrequency = r.EffectiveFrequency,
                hasClipping = r.HasClipping,
                clippingPercentage = Math.Round(r.ClippingPercentage, 4),
                hasScaledClipping = r.HasScaledClipping,
                scaledClippingPercentage = Math.Round(r.ScaledClippingPercentage, 4),
                maxSampleLevel = Math.Round(r.MaxSampleLevel, 6),
                maxSampleLevelDb = Math.Round(r.MaxSampleLevelDb, 1),
                frequency = r.Frequency,
                bpm = r.Bpm,
                replayGain = Math.Round(r.ReplayGain, 1),
                hasReplayGain = r.HasReplayGain,
                dynamicRange = Math.Round(r.DynamicRange, 1),
                hasDynamicRange = r.HasDynamicRange,
                isMqa = r.IsMqa,
                isMqaStudio = r.IsMqaStudio,
                mqaOriginalSampleRate = r.MqaOriginalSampleRate,
                mqaEncoder = r.MqaEncoder,
                aiVerdict = r.AiVerdict,
                aiConfidence = Math.Round(r.AiCombinedConfidence, 1),
                isAiGenerated = r.IsAiGenerated,
                aiSource = r.AiSource,
                experimentalAiSuspicious = r.ExperimentalAiSuspicious,
                experimentalAiConfidence = Math.Round(r.ExperimentalAiConfidence, 2),
                experimentalAiFlags = r.ExperimentalAiFlags ?? new List<string>(),
                aiSources = r.AiSources ?? new List<string>(),
                hasAlbumCover = r.HasAlbumCover,
                isFakeStereo = r.IsFakeStereo,
                fakeStereoType = r.FakeStereoType,
                stereoCorrelation = Math.Round(r.StereoCorrelation, 4),
                hasExcessiveSilence = r.HasExcessiveSilence,
                leadingSilenceMs = Math.Round(r.LeadingSilenceMs),
                trailingSilenceMs = Math.Round(r.TrailingSilenceMs),
                midTrackSilenceGaps = r.MidTrackSilenceGaps,
                totalMidSilenceMs = Math.Round(r.TotalMidSilenceMs),
                truePeakDbTP = Math.Round(r.TruePeakDbTP, 2),
                hasTruePeak = r.HasTruePeak,
                integratedLufs = Math.Round(r.IntegratedLufs, 2),
                hasLufs = r.HasLufs,
                ripQuality = r.RipQuality,
                ripQualityDetail = r.RipQualityDetail,
                hasRipQuality = r.HasRipQuality,
                isAlac = r.IsAlac,
                clippingSamples = r.ClippingSamples,
                shLabsScanned = r.SHLabsScanned,
                shLabsPrediction = r.SHLabsPrediction,
                shLabsProbability = Math.Round(r.SHLabsProbability, 4),
                shLabsConfidence = Math.Round(r.SHLabsConfidence, 2),
                shLabsAiType = r.SHLabsAiType,
                isCueVirtualTrack = r.IsCueVirtualTrack,
                cueSheetPath = r.CueSheetPath,
                cueTrackNumber = r.CueTrackNumber,
                dateModified = r.DateModifiedDisplay,
                dateCreated = r.DateCreatedDisplay
            }).ToList();

            var options = new JsonSerializerOptions { WriteIndented = true };
            Console.WriteLine(JsonSerializer.Serialize(output, options));
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

        struct CommonFlags
        {
            public int Threads;
            public int MemoryLimitMb;
            public bool Recursive;
            public string? StatusFilter;
            public List<string> Paths;

            public static CommonFlags Default() => new()
            {
                Threads = Math.Max(1, Environment.ProcessorCount / 2),
                Recursive = true,
                Paths = new List<string>()
            };
        }

        /// <summary>
        /// Tries to parse a common flag (--threads, --cpu, --memory, --recursive, --no-recursive, --status).
        /// Returns true if the flag was consumed. Advances index i if the flag has a value.
        /// Sets errorMsg if parsing fails (caller should return Error(errorMsg)).
        /// </summary>
        static bool TryParseCommonFlag(string[] args, ref int i, ref CommonFlags flags, out string? errorMsg)
        {
            errorMsg = null;
            switch (args[i].ToLowerInvariant())
            {
                case "--threads" when i + 1 < args.Length:
                    if (int.TryParse(args[++i], out int t)) flags.Threads = Math.Clamp(t, 1, 32);
                    else { errorMsg = $"Invalid value for --threads: {args[i]}"; return true; }
                    return true;
                case "--cpu" when i + 1 < args.Length:
                    flags.Threads = ParseCpuPreset(args[++i]);
                    return true;
                case "--memory" when i + 1 < args.Length:
                    flags.MemoryLimitMb = ParseMemoryPreset(args[++i]);
                    return true;
                case "--no-recursive":
                    flags.Recursive = false;
                    return true;
                case "--recursive" or "-r":
                    flags.Recursive = true;
                    return true;
                case "--status" when i + 1 < args.Length:
                    flags.StatusFilter = args[++i].ToLowerInvariant();
                    return true;
                default:
                    if (!args[i].StartsWith("-"))
                    {
                        flags.Paths.Add(args[i]);
                        return true;
                    }
                    return false;
            }
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

        static void ColorWrite(ConsoleColor color, string text)
        {
            SetColor(color);
            Console.Write(text);
            ResetColor();
        }

        static void ColorWriteLine(ConsoleColor color, string text)
        {
            SetColor(color);
            Console.WriteLine(text);
            ResetColor();
        }

        static void WriteProgress(string text)
        {
            try
            {
                int width = Console.WindowWidth - 1;
                Console.Write($"\r{text.PadRight(width)}");
            }
            catch
            {
                Console.Write($"\r{text}");
            }
        }

    }
}
