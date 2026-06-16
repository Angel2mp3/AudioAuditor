using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using SharpCompress.Archives;
using SharpCompress.Common;
using SharpCompress.Readers;
using IOPath = System.IO.Path;
using AudioQualityChecker.Models;
using AudioQualityChecker.Services;

namespace AudioQualityChecker
{
    public partial class MainWindow
    {
        // ═══════════════════════════════════════════
        //  File Analysis (multi-threaded)
        // ═══════════════════════════════════════════

        /// <summary>
        /// Expands playlist files (.m3u, .m3u8, .pls) into their audio file entries.
        /// Non-playlist files are passed through unchanged.
        /// </summary>
        private List<string> ExpandPlaylists(IEnumerable<string> paths)
        {
            var result = new List<string>();
            foreach (var path in paths)
            {
                string ext = IOPath.GetExtension(path);
                if (PlaylistExtensions.Contains(ext) && File.Exists(path))
                {
                    try
                    {
                        var playlistDir = IOPath.GetDirectoryName(path) ?? "";
                        var lines = File.ReadAllLines(path);

                        if (ext.Equals(".pls", StringComparison.OrdinalIgnoreCase))
                        {
                            // PLS format: File1=path, File2=path, ...
                            foreach (var line in lines)
                            {
                                if (line.StartsWith("File", StringComparison.OrdinalIgnoreCase) && line.Contains('='))
                                {
                                    var filePath = line[(line.IndexOf('=') + 1)..].Trim();
                                    var resolved = ResolvePath(filePath, playlistDir);
                                    if (resolved != null) result.Add(resolved);
                                }
                            }
                        }
                        else
                        {
                            // M3U/M3U8: each non-comment, non-empty line is a path
                            foreach (var line in lines)
                            {
                                var trimmed = line.Trim();
                                if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith('#'))
                                    continue;
                                var resolved = ResolvePath(trimmed, playlistDir);
                                if (resolved != null) result.Add(resolved);
                            }
                        }
                    }
                    catch { /* skip unreadable playlist */ }
                }
                else
                {
                    result.Add(path);
                }
            }
            return result;

            static string? ResolvePath(string entry, string baseDir)
            {
                // Skip URLs (http://, https://)
                if (entry.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                    entry.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                    return null;

                // Try as absolute path first
                if (IOPath.IsPathRooted(entry) && File.Exists(entry))
                    return entry;

                // Try relative to playlist directory
                var combined = IOPath.Combine(baseDir, entry);
                if (File.Exists(combined))
                    return IOPath.GetFullPath(combined);

                return null;
            }
        }

        /// <summary>
        /// Extracts audio files from archives (.zip, .rar, .7z, etc.).
        /// Returns the list of extracted audio file paths plus any non-archive audio paths unchanged.
        /// </summary>
        private List<string> ExtractAudioFromArchives(IEnumerable<string> paths)
        {
            var result = new List<string>();
            foreach (var path in paths)
            {
                string ext = IOPath.GetExtension(path);
                if (ArchiveExtensions.Contains(ext) && File.Exists(path))
                {
                    try
                    {
                        string tempDir = IOPath.Combine(IOPath.GetTempPath(), "AudioAuditor_" + Guid.NewGuid().ToString("N"));
                        Directory.CreateDirectory(tempDir);

                        if (ext.Equals(".zip", StringComparison.OrdinalIgnoreCase))
                        {
                            ZipFile.ExtractToDirectory(path, tempDir);
                        }
                        else
                        {
                            // Use SharpCompress for RAR, 7z, tar, gz, etc.
                            using var archive = ArchiveFactory.OpenArchive(path, new ReaderOptions());
                            string safeBase = Path.GetFullPath(tempDir) + Path.DirectorySeparatorChar;
                            foreach (var entry in archive.Entries.Where(e => !e.IsDirectory && e.Key != null))
                            {
                                // ZIP slip guard: skip entries that would escape tempDir
                                string entryKey = entry.Key!.Replace('/', Path.DirectorySeparatorChar);
                                string fullDest = Path.GetFullPath(Path.Combine(tempDir, entryKey));
                                if (!fullDest.StartsWith(safeBase, StringComparison.OrdinalIgnoreCase))
                                    continue;
                                entry.WriteToDirectory(tempDir, new ExtractionOptions
                                {
                                    ExtractFullPath = true,
                                    Overwrite = true
                                });
                            }
                        }

                        var extracted = Directory.EnumerateFiles(tempDir, "*.*", SearchOption.AllDirectories)
                            .Where(f => SupportedExtensions.Contains(IOPath.GetExtension(f)));
                        result.AddRange(extracted);
                    }
                    catch { /* skip corrupt archives */ }
                }
                else if (SupportedExtensions.Contains(ext))
                {
                    result.Add(path);
                }
                else if (ext.Equals(".cue", StringComparison.OrdinalIgnoreCase) && File.Exists(path))
                {
                    // Cue files are passed through — expanded later in AnalyzeAndAddFiles
                    result.Add(path);
                }
            }
            return result;
        }

        private async Task AnalyzeAndAddFiles(string[] filePaths)
        {

            // Deduplicate against already-loaded files
            var existing = new HashSet<string>(_files.Select(f => f.FilePath), StringComparer.OrdinalIgnoreCase);
            var newPaths = filePaths
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Where(p => !existing.Contains(p))
                .ToArray();

            if (newPaths.Length == 0) return;

            // ── Expand .cue files into virtual tracks ──
            var cueFiles = newPaths.Where(p => IOPath.GetExtension(p).Equals(".cue", StringComparison.OrdinalIgnoreCase)).ToArray();
            var regularFiles = newPaths.Where(p => !IOPath.GetExtension(p).Equals(".cue", StringComparison.OrdinalIgnoreCase)).ToList();
            var cueEntries = new List<(string audioPath, Services.CueSheet sheet)>();

            foreach (var cuePath in cueFiles)
            {
                var sheet = Services.CueSheetParser.Parse(cuePath);
                if (sheet != null && !string.IsNullOrEmpty(sheet.AudioFilePath))
                {
                    if (!existing.Contains(sheet.AudioFilePath) &&
                        !regularFiles.Contains(sheet.AudioFilePath, StringComparer.OrdinalIgnoreCase))
                        regularFiles.Add(sheet.AudioFilePath);
                    cueEntries.Add((sheet.AudioFilePath, sheet));
                }
            }

            newPaths = regularFiles.ToArray();
            if (newPaths.Length == 0 && cueEntries.Count == 0) return;

            // ── SH Labs pre-flight: decide which files will use the API ──
            HashSet<string>? shLabsTargets = null;
            if (ThemeManager.SHLabsAiDetection)
            {
                // Count how many actually need an API call (no cache hit)
                var uncached = newPaths.Where(p => SHLabsDetectionService.GetCachedResult(p) == null).ToList();
                var (dailyRem, monthlyRem) = SHLabsDetectionService.GetQuota();
                int available = Math.Min(dailyRem, monthlyRem);

                if (uncached.Count > available && available > 0)
                {
                    // More files than remaining quota — let the user know
                    var msg = $"You have {available} SH Labs scan{(available == 1 ? "" : "s")} remaining today. " +
                              $"{uncached.Count} file{(uncached.Count == 1 ? "" : "s")} need scanning.\n\n" +
                              $"The first {available} file{(available == 1 ? "" : "s")} will be scanned with SH Labs. " +
                              $"The rest will use your other selected detection methods.\n\nContinue?";
                    var confirmed = await ShowSHLabsLimitOverlayAsync(msg, showCancel: true);
                    if (!confirmed) return;

                    // Take first N uncached files
                    shLabsTargets = new HashSet<string>(uncached.Take(available), StringComparer.OrdinalIgnoreCase);
                    // Also include all cached files (free lookups)
                    foreach (var p in newPaths.Where(p => SHLabsDetectionService.GetCachedResult(p) != null))
                        shLabsTargets.Add(p);
                }
                else if (available == 0)
                {
                    // No quota left — inform and continue without SH Labs
                    await ShowSHLabsLimitOverlayAsync(
                        "You've reached your SH Labs scan limit. Files will be analyzed using your other selected detection methods.",
                        showCancel: false);
                    shLabsTargets = null; // disable SH Labs for this batch
                }
                else
                {
                    // Enough quota for all files
                    shLabsTargets = new HashSet<string>(newPaths, StringComparer.OrdinalIgnoreCase);
                }
            }

            bool isFirstBatch = !_isAnalyzing;
            if (isFirstBatch)
            {
                _analysisCts?.Cancel();
                _analysisCts = new CancellationTokenSource();
                _analysisCompleted = 0;
                _analysisTotal = 0;
                _analysisStartTime = DateTime.UtcNow;
                _analysisSemaphore = new SemaphoreSlim(ThemeManager.MaxConcurrency);
                _shLabsSemaphore = new SemaphoreSlim(3);
                _analysisSettingsSnapshot = AnalysisSettingsSnapshot.From(new ThemeManagerSettings());
                _isAnalyzing = true;
                AnalysisProgressPanel.Visibility = Visibility.Visible;
                AnalysisPauseButton.Visibility = Visibility.Visible;
                AnalysisPauseButton.Content = "⏸";
                _analysisPauseEvent.Set(); // ensure not paused from previous run
                AudioAnalyzer.PauseEvent = _analysisPauseEvent;
                AnalysisProgress.Value = 0;
                AnalysisEtaText.Text = "";
            }
            var ct = _analysisCts!.Token;
            var analysisSettings = _analysisSettingsSnapshot ?? AnalysisSettingsSnapshot.From(new ThemeManagerSettings());

            Interlocked.Add(ref _analysisTotal, newPaths.Length);
            int currentTotal = _analysisTotal;
            AnalysisProgress.Maximum = currentTotal;
            StatusText.Text = $"Analyzing {_analysisCompleted} / {currentTotal} files...";

            Interlocked.Increment(ref _activeBatches);
            var semaphore = _analysisSemaphore!;
            var shLabsSemaphore = _shLabsSemaphore!;
            var pendingUiResults = new ConcurrentQueue<AudioFileInfo>();
            int uiFlushScheduled = 0;
            AudioFileInfo? firstAddedItem = null;
            int addedItemCount = 0;

            void FlushPendingResultsOnUi()
            {
                while (pendingUiResults.TryDequeue(out var pending))
                {
                    firstAddedItem ??= pending;
                    addedItemCount++;
                    _files.Add(pending);
                    LocalStatsCollector.RecordAnalysisResult(pending);
                }

                int count = Volatile.Read(ref _analysisCompleted);
                int total = Volatile.Read(ref _analysisTotal);
                AnalysisProgress.Maximum = total;
                AnalysisProgress.Value = Math.Min(count, total);
                StatusText.Text = $"Analyzed {count} / {total} files...";
                UpdateAnalysisEta(count, total);
            }

            void ScheduleUiFlush()
            {
                if (Interlocked.CompareExchange(ref uiFlushScheduled, 1, 0) != 0)
                    return;

                _ = Dispatcher.InvokeAsync(() =>
                {
                    FlushPendingResultsOnUi();
                    Interlocked.Exchange(ref uiFlushScheduled, 0);
                    if (!pendingUiResults.IsEmpty)
                        ScheduleUiFlush();
                }, DispatcherPriority.Background);
            }

            void QueueUiResult(AudioFileInfo result)
            {
                pendingUiResults.Enqueue(result);
                ScheduleUiFlush();
            }

            async Task FlushPendingResultsAsync()
            {
                await Dispatcher.InvokeAsync(FlushPendingResultsOnUi, DispatcherPriority.Background);
            }

            try
            {
                // Process in chunks to avoid creating 100k+ Task objects simultaneously.
                // The semaphore still caps concurrent execution; chunking caps task allocation.
                const int ChunkSize = 500;
                for (int _chunkStart = 0; _chunkStart < newPaths.Length; _chunkStart += ChunkSize)
                {
                    if (ct.IsCancellationRequested) break;
                    var chunk = newPaths.Skip(_chunkStart).Take(ChunkSize).ToArray();
                    var chunkTasks = chunk.Select(async path =>
                    {
                        AudioFileInfo? info = null;
                        bool acquired = false;
                        bool cacheAnalysisResult = false;

                        // ── Check scan cache first ──
                        if (ThemeManager.ScanCacheEnabled)
                        {
                            try
                            {
                                var fi = new System.IO.FileInfo(path);
                                if (fi.Exists && ScanCacheService.TryGet(path, fi.Length, fi.LastWriteTimeUtc, analysisSettings, out var cached) && cached != null)
                                {
                                    Interlocked.Increment(ref _analysisCompleted);
                                    QueueUiResult(cached);
                                    return;
                                }
                            }
                            catch { /* cache miss — fall through to normal analysis */ }
                        }

                        try
                        {
                            // Wait if analysis is paused (poll so we don't pin a ThreadPool thread)
                            while (!_analysisPauseEvent.Wait(0))
                            {
                                await Task.Delay(10, ct);
                            }
                            await semaphore.WaitAsync(ct);
                            acquired = true;
                            ct.ThrowIfCancellationRequested();
                            await ThemeManager.WaitForMemoryAsync(ct);
                            ct.ThrowIfCancellationRequested();

                            // Use a dedicated analysis thread so a hung decoder cannot starve the ThreadPool.
                            var analysisTask = Task.Factory.StartNew(
                                () => AudioAnalyzer.AnalyzeFile(path, analysisSettings, ct),
                                ct,
                                TaskCreationOptions.LongRunning,
                                TaskScheduler.Default);
                            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                            var timeoutTask = Task.Delay(TimeSpan.FromSeconds(120), timeoutCts.Token);
                            var winner = await Task.WhenAny(analysisTask, timeoutTask);
                            if (winner == timeoutTask)
                            {
                                ct.ThrowIfCancellationRequested();
                                info = new AudioFileInfo
                                {
                                    FilePath = path,
                                    FileName = IOPath.GetFileName(path),
                                    FolderPath = IOPath.GetDirectoryName(path) ?? "",
                                    Extension = IOPath.GetExtension(path).ToLowerInvariant(),
                                    Status = AudioStatus.Unknown,
                                    ErrorMessage = "Analysis timed out"
                                };
                            }
                            else
                            {
                                timeoutCts.Cancel();
                                info = await analysisTask;
                                cacheAnalysisResult = true;
                            }
                            ct.ThrowIfCancellationRequested();
                        }
                        catch (OperationCanceledException) { return; }
                        catch
                        {
                            if (!ct.IsCancellationRequested)
                            {
                                Interlocked.Increment(ref _analysisCompleted);
                                QueueUiResult(new AudioFileInfo
                                {
                                    FilePath = path,
                                    FileName = IOPath.GetFileName(path),
                                    FolderPath = IOPath.GetDirectoryName(path) ?? "",
                                    Extension = IOPath.GetExtension(path).ToLowerInvariant(),
                                    Status = AudioStatus.Unknown,
                                    ErrorMessage = "Failed to open or analyze"
                                });
                            }
                            return;
                        }
                        finally
                        {
                            if (acquired) semaphore.Release();
                        }

                        // ── SH Labs detection (runs outside analysis semaphore to avoid blocking local analysis) ──
                        if (info != null && shLabsTargets != null && shLabsTargets.Contains(path))
                        {
                            try
                            {
                                await shLabsSemaphore.WaitAsync(ct);
                                try
                                {
                                    var shResult = await SHLabsDetectionService.AnalyzeAsync(path, ct);
                                    if (shResult != null)
                                    {
                                        info.SHLabsScanned = true;
                                        info.SHLabsPrediction = shResult.Prediction;
                                        info.SHLabsProbability = shResult.Probability;
                                        info.SHLabsConfidence = shResult.Confidence;
                                        info.SHLabsAiType = shResult.MostLikelyAiType;
                                    }
                                }
                                finally { shLabsSemaphore.Release(); }
                            }
                            catch (OperationCanceledException) { /* SH Labs cancelled — file still added below */ }
                            catch { /* SH Labs failure is non-fatal — other detectors still ran */ }
                        }

                        if (info != null && !ct.IsCancellationRequested)
                        {
                            // Cache the result for future use
                            if (ThemeManager.ScanCacheEnabled && cacheAnalysisResult)
                            {
                                try { ScanCacheService.Set(info, analysisSettings); } catch { }
                            }

                            Interlocked.Increment(ref _analysisCompleted);
                            QueueUiResult(info);
                        }
                    });

                    try { await Task.WhenAll(chunkTasks); } catch (OperationCanceledException) { break; }
                } // end chunk loop

                await FlushPendingResultsAsync();

                // Save scan cache to disk after batch completes
                if (ThemeManager.ScanCacheEnabled)
                {
                    try { await Task.Run(() => ScanCacheService.SaveToDisk()); } catch { }
                }

                // ── Create virtual tracks from cue sheets ──
                foreach (var (audioPath, sheet) in cueEntries)
                {
                    // Find the analyzed parent file
                    var parent = _files.FirstOrDefault(f => f.FilePath.Equals(audioPath, StringComparison.OrdinalIgnoreCase));
                    if (parent == null) continue;

                    foreach (var track in sheet.Tracks)
                    {
                        var endTime = track.EndTime > TimeSpan.Zero ? track.EndTime : TimeSpan.FromSeconds(parent.DurationSeconds);
                        var duration = endTime - track.StartTime;
                        if (duration.TotalSeconds <= 0) continue;

                        string trackId = $"{audioPath}#CUE{track.TrackNumber}";
                        if (existing.Contains(trackId)) continue;

                        var virtual_ = new AudioFileInfo
                        {
                            FilePath = trackId,
                            FileName = $"[{track.TrackNumber:D2}] {(string.IsNullOrEmpty(track.Title) ? IOPath.GetFileNameWithoutExtension(audioPath) : track.Title)}",
                            FolderPath = parent.FolderPath,
                            Title = track.Title,
                            Artist = !string.IsNullOrEmpty(track.Performer) ? track.Performer : parent.Artist,
                            Extension = parent.Extension,
                            SampleRate = parent.SampleRate,
                            BitsPerSample = parent.BitsPerSample,
                            Channels = parent.Channels,
                            ReportedBitrate = parent.ReportedBitrate,
                            ActualBitrate = parent.ActualBitrate,
                            EffectiveFrequency = parent.EffectiveFrequency,
                            Duration = duration.TotalHours >= 1
                                ? $"{(int)duration.TotalHours}:{duration.Minutes:D2}:{duration.Seconds:D2}"
                                : $"{duration.Minutes}:{duration.Seconds:D2}",
                            DurationSeconds = duration.TotalSeconds,
                            FileSize = parent.FileSize,
                            FileSizeBytes = parent.FileSizeBytes,
                            DateModified = parent.DateModified,
                            DateCreated = parent.DateCreated,
                            Status = parent.Status,
                            IsCueVirtualTrack = true,
                            CueSheetPath = sheet.AudioFilePath,
                            CueTrackNumber = track.TrackNumber,
                            CueStartTime = track.StartTime,
                            CueEndTime = endTime,
                        };
                        firstAddedItem ??= virtual_;
                        addedItemCount++;
                        _files.Add(virtual_);
                    }
                }
            }
            finally
            {
                if (Interlocked.Decrement(ref _activeBatches) == 0)
                {
                    _isAnalyzing = false;
                    _analysisSettingsSnapshot = null;
                    AudioAnalyzer.PauseEvent = null;
                    AnalysisProgressPanel.Visibility = Visibility.Collapsed;
                    AnalysisPauseButton.Visibility = Visibility.Collapsed;
                    AnalysisEtaText.Text = "";

                    // Apply saved favorites to all loaded files, then sort favorites to top
                    FavoritesService.Apply(_files);
                    RefreshFavoriteSort();

                    UpdateStatusSummary();
                    FocusNewlyAddedFile(firstAddedItem, addedItemCount);

                    long totalBytes = newPaths.Sum(p => { try { return new System.IO.FileInfo(p).Length; } catch { return 0; } });
                    LocalStatsCollector.RecordScan(addedItemCount, totalBytes);

                    // Update lifetime stats for 30-day popup
                    if (ThemeManager.FirstScanDate == default)
                        ThemeManager.FirstScanDate = DateTime.Now;
                    ThemeManager.TotalFilesScannedLifetime += addedItemCount;
                    ThemeManager.SavePlayOptions();

                    // Persist the working set so "restore last session" can reload it.
                    SaveSessionState();

                    ScheduleDonationPopup();
                }
            }
        }

        private void FocusNewlyAddedFile(AudioFileInfo? item, int addedCount)
        {
            if (!ThemeManager.FocusNewlyAddedFilesEnabled || item == null || addedCount <= 0)
                return;

            Dispatcher.InvokeAsync(() =>
            {
                if (_filteredView == null) return;

                bool isVisible = false;
                foreach (var visible in _filteredView)
                {
                    if (ReferenceEquals(visible, item))
                    {
                        isVisible = true;
                        break;
                    }
                }

                if (!isVisible)
                {
                    StatusText.Text = addedCount == 1
                        ? "Added 1 file. The new item is hidden by the current filter."
                        : $"Added {addedCount:N0} files. The first new item is hidden by the current filter.";
                    return;
                }

                FileGrid.SelectedItems.Clear();
                FileGrid.SelectedItem = item;
                FileGrid.ScrollIntoView(item);
                FileGrid.Focus();
            }, DispatcherPriority.ContextIdle);
        }

        private void UpdateAnalysisEta(int completed, int total)
        {
            if (completed < 1 || completed >= total)
            {
                AnalysisEtaText.Text = "";
                return;
            }

            var elapsed = DateTime.UtcNow - _analysisStartTime;
            double avgPerFile = elapsed.TotalSeconds / completed;
            int remaining = total - completed;
            double etaSeconds = avgPerFile * remaining;

            if (etaSeconds < 1)
                AnalysisEtaText.Text = "< 1s";
            else if (etaSeconds < 60)
                AnalysisEtaText.Text = $"~{(int)etaSeconds}s left";
            else
            {
                int mins = (int)(etaSeconds / 60);
                int secs = (int)(etaSeconds % 60);
                AnalysisEtaText.Text = secs > 0 ? $"~{mins}m {secs}s left" : $"~{mins}m left";
            }
        }
    }
}
