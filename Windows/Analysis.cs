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

        // Both expansions moved to AudioAuditor.Core (FileSourceExpander) so the Avalonia
        // build shares them rather than growing a second copy.

        private List<string> ExpandPlaylists(IEnumerable<string> paths) =>
            FileSourceExpander.ExpandPlaylists(paths);

        private List<string> ExtractAudioFromArchives(IEnumerable<string> paths) =>
            FileSourceExpander.ExtractAudioFromArchives(paths);

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
                // Quota split lives in Core (SHLabsBatchPlanner) so Avalonia budgets identically.
                var (dailyRem, monthlyRem) = SHLabsDetectionService.GetQuota();
                var plan = SHLabsBatchPlanner.Create(newPaths, Math.Min(dailyRem, monthlyRem),
                    p => SHLabsDetectionService.GetCachedResult(p) != null);

                if (plan.IsExhausted)
                {
                    // No quota left — inform and continue without SH Labs
                    await ShowSHLabsLimitOverlayAsync(SHLabsBatchPlanner.ExhaustedMessage, showCancel: false);
                }
                else if (plan.IsPartial)
                {
                    // More files than remaining quota — let the user know
                    if (!await ShowSHLabsLimitOverlayAsync(SHLabsBatchPlanner.PartialMessage(plan), showCancel: true))
                    {
                        // Say so: this batch is dropped here, and silently returning made the
                        // files the user just added look like they had vanished.
                        StatusText.Text = $"Scan cancelled — {newPaths.Length} file(s) not added.";
                        return;
                    }

                    shLabsTargets = plan.Targets;
                }
                else
                {
                    shLabsTargets = plan.Targets;
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
                // Dispose the previous scan's gates before replacing them — every completed scan
                // used to leave one of each behind for the GC.
                _analysisSemaphore?.Dispose();
                _shLabsSemaphore?.Dispose();
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
                // Do NOT wrap these adds in _filteredView.DeferRefresh(). The grid's view is
                // filtered, sorted and grouped by folder, so batching the re-settle looks like an
                // easy win — but ListCollectionView throws "Cannot change or check the contents or
                // Current position of CollectionView while Refresh is being deferred" on the first
                // Add, because it reads CurrentPosition while processing the change. DeferRefresh
                // is for batching changes to the *view's* filter/sort/group settings, not for bulk
                // mutation of the source. Deferring here dequeued an item, threw on Add, lost that
                // row, and left uiFlushScheduled stuck at 1 so the grid stopped updating for the
                // rest of the scan. See GroupedViewDeferRefreshTests.
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
                // Built once for the whole batch. CacheFingerprint is a computed property (~20
                // interpolated strings + a Join); the per-file overloads rebuilt it twice per file
                // for a value that never changes within a scan.
                string batchFingerprint = analysisSettings.CacheFingerprint;

                // One bounded pass over the whole batch rather than chunks of 500 joined by a
                // Task.WhenAll. The chunk barrier meant a single slow file at the tail of a chunk
                // left every other worker idle until it finished, and a paused scan could hold one
                // Task.Delay poller per queued file. ForEachAsync keeps only MaxDegreeOfParallelism
                // workers alive, which caps both the task allocation the chunking was there to
                // avoid and the number of pollers.
                var parallelOptions = new ParallelOptions
                {
                    MaxDegreeOfParallelism = Math.Max(1, ThemeManager.MaxConcurrency),
                    CancellationToken = ct
                };

                try
                {
                    await Parallel.ForEachAsync(newPaths, parallelOptions, async (path, _) =>
                    {
                        AudioFileInfo? info = null;
                        bool acquired = false;
                        bool cacheAnalysisResult = false;
                        long statSize = 0;
                        DateTime statWritten = default;
                        bool statTaken = false;

                        // ── Check scan cache first ──
                        if (ThemeManager.ScanCacheEnabled)
                        {
                            try
                            {
                                var fi = new System.IO.FileInfo(path);
                                if (fi.Exists)
                                {
                                    statSize = fi.Length;
                                    statWritten = fi.LastWriteTimeUtc;
                                    statTaken = true;
                                    if (ScanCacheService.TryGet(path, statSize, statWritten, batchFingerprint, out var cached) && cached != null)
                                    {
                                        Interlocked.Increment(ref _analysisCompleted);
                                        QueueUiResult(cached);
                                        return;
                                    }
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

                            // Runs on the ThreadPool. Concurrency is already capped by the semaphore
                            // above, so LongRunning only meant spinning up and tearing down a
                            // dedicated OS thread (1 MB of stack reserve) once PER FILE — 100k
                            // files meant 100k thread creations.
                            var analysisTask = Task.Run(
                                () => AudioAnalyzer.AnalyzeFile(path, analysisSettings, ct), ct);
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
                                // Reuse the stat taken for the cache lookup above instead of
                                // re-running FileInfo on the same path.
                                try
                                {
                                    if (statTaken)
                                        ScanCacheService.Set(info, batchFingerprint, statSize, statWritten);
                                    else
                                        ScanCacheService.Set(info, batchFingerprint);
                                }
                                catch (Exception ex)
                                {
                                    // A silent miss here just makes the next scan slow, but it is
                                    // invisible — so it looks like the cache setting does nothing.
                                    if (ThemeManager.CrashLoggingEnabled) LocalCrashLogger.Write(ex);
                                }
                            }

                            Interlocked.Increment(ref _analysisCompleted);
                            QueueUiResult(info);
                        }
                    });
                }
                catch (OperationCanceledException) { /* cancelled — still flush what finished */ }

                await FlushPendingResultsAsync();

                // Save scan cache to disk after batch completes
                if (ThemeManager.ScanCacheEnabled)
                {
                    // Whole-scan cache write: failing silently means every rescan stays slow.
                    try { await Task.Run(() => ScanCacheService.SaveToDisk()); }
                    catch (Exception ex) { if (ThemeManager.CrashLoggingEnabled) LocalCrashLogger.Write(ex); }
                }

                // ── Create virtual tracks from cue sheets ──
                // One index up front instead of a linear scan of _files per cue entry.
                Dictionary<string, AudioFileInfo>? filesByPath = null;
                if (cueEntries.Count > 0)
                {
                    filesByPath = new Dictionary<string, AudioFileInfo>(StringComparer.OrdinalIgnoreCase);
                    foreach (var f in _files)
                        filesByPath[f.FilePath] = f;
                }

                foreach (var (audioPath, sheet) in cueEntries)
                {
                    // Find the analyzed parent file
                    if (!filesByPath!.TryGetValue(audioPath, out var parent)) continue;

                    foreach (var virtualTrack in CueVirtualTracks.Build(sheet, parent, existing))
                    {
                        firstAddedItem ??= virtualTrack;
                        addedItemCount++;
                        _files.Add(virtualTrack);
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

                    // End of scan is the natural point to persist everything the per-file
                    // RecordAnalysisResult calls accumulated in memory. Off the UI thread: the
                    // stats file can be several MB before compression.
                    _ = Task.Run(() => LocalStatsCollector.Flush());

                    // Update lifetime stats for 30-day popup
                    if (ThemeManager.FirstScanDate == default)
                        ThemeManager.FirstScanDate = DateTime.Now;
                    ThemeManager.TotalFilesScannedLifetime += addedItemCount;
                    ThemeManager.SavePlayOptions();

                    // Persist the working set so "restore last session" can reload it.
                    SaveSessionState();

                    // CD Rip Checker: stamp per-folder rip-log verdicts (opt-in; one cambia run per folder).
                    BackfillRipLogsAsync(_files.ToList()).Observe(nameof(BackfillRipLogsAsync));

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

        private void UpdateAnalysisEta(int completed, int total) =>
            AnalysisEtaText.Text = AnalysisEta.Format(completed, total, DateTime.UtcNow - _analysisStartTime);
    }
}
