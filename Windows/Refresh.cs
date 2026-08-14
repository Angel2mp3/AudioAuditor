using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using AudioQualityChecker.Models;
using AudioQualityChecker.Services;

namespace AudioQualityChecker
{
    public partial class MainWindow
    {
        // ═══════════════════════════════════════════
        //  Refresh — re-analyze loaded rows in place
        // ═══════════════════════════════════════════
        //
        // Re-runs analysis for the highlighted rows (or every loaded row when none are
        // selected) and copies the results back into the existing AudioFileInfo objects, so
        // favorites, selection, sort and grouping all survive. Used to recover rows that
        // didn't read the first time, or to backfill data after the user adds columns.

        private CancellationTokenSource? _refreshCts;
        private volatile bool _isRefreshing;

        /// <summary>
        /// Backfills only the column(s) for the given just-enabled analysis features across all
        /// loaded rows — re-analyzes each file but copies just those features' fields, leaving the
        /// other columns' values untouched. Called from Settings when a feature flips OFF→ON.
        /// </summary>
        public void RefreshColumnsForFeatures(IReadOnlyCollection<string> featureHeaders)
        {
            if (_files.Count == 0 || _isAnalyzing || _isRefreshing) return;

            // "Rip Log" isn't produced by AnalyzeFile — it comes from cambia per folder, so it gets
            // its own backfill path rather than a re-analysis.
            // One snapshot shared by both consumers — _files was copied twice in this method.
            var snapshot = _files.ToList();

            if (featureHeaders.Contains(AnalysisFeatureFields.RipLog, StringComparer.OrdinalIgnoreCase))
                BackfillRipLogsAsync(snapshot).Observe(nameof(BackfillRipLogsAsync));

            // Feature → property map lives in Core so Avalonia's refresh fills the same fields.
            var fields = AnalysisFeatureFields.For(featureHeaders);
            if (fields.Count == 0) return;

            RefreshFilesAsync(snapshot, fields).Observe(nameof(RefreshFilesAsync));
        }

        /// <summary>
        /// CD Rip Checker scan auto-detect: runs cambia once per distinct folder across the given rows
        /// and stamps the resulting score/verdict onto every file in that folder. No-op unless the
        /// feature is enabled and the cambia binary is available. Safe to call repeatedly (idempotent).
        /// </summary>
        private async Task BackfillRipLogsAsync(IReadOnlyList<AudioFileInfo> files)
        {
            if (!ThemeManager.RipLogCheckEnabled || files.Count == 0) return;
            if (!RipLogCheckService.IsAvailable) return;

            var folders = files.Select(f => f.FolderPath)
                .Where(s => !string.IsNullOrEmpty(s))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (folders.Count == 0) return;

            Dictionary<string, RipLogResult> map;
            try { map = await Task.Run(() => RipLogCheckService.CheckFoldersAsync(folders)); }
            catch { return; }
            if (map.Count == 0) return;

            // Back on the UI thread (no ConfigureAwait above) — safe to stamp and notify.
            foreach (var f in files)
                if (!string.IsNullOrEmpty(f.FolderPath) && map.TryGetValue(f.FolderPath, out var r))
                    f.SetRipLog(r.Score, r.Verdict);
        }

        private async void Refresh_Click(object sender, RoutedEventArgs e)
        {
            var selected = FileGrid.SelectedItems.Cast<AudioFileInfo>().ToList();
            // Selection scopes the refresh; with nothing selected we refresh everything loaded.
            var scope = selected.Count > 0 ? selected : _files.ToList();
            await RefreshFilesAsync(scope, onlyFields: null);
        }

        /// <summary>
        /// Re-analyzes <paramref name="targets"/> and copies the fresh results into the existing
        /// rows. When <paramref name="onlyFields"/> is non-null only those properties are copied
        /// (used to fill a single newly-enabled column without disturbing the other columns).
        /// </summary>
        private async Task RefreshFilesAsync(IReadOnlyList<AudioFileInfo> targets, IReadOnlyCollection<string>? onlyFields)
        {
            if (_isAnalyzing || _isRefreshing)
            {
                StatusText.Text = "Can't refresh while analysis is running — try again once it finishes.";
                return;
            }

            // Cue virtual tracks derive their values from their parent file, so re-analyzing them
            // directly is meaningless; skip them. Also drop rows with no real file path.
            var files = targets
                .Where(f => f is { IsCueVirtualTrack: false } && !string.IsNullOrEmpty(f.FilePath))
                .Distinct()
                .ToList();
            if (files.Count == 0) return;

            _isRefreshing = true;
            _refreshCts = new CancellationTokenSource();
            var ct = _refreshCts.Token;
            var settings = AnalysisSettingsSnapshot.From(new ThemeManagerSettings());
            using var semaphore = new SemaphoreSlim(Math.Max(1, ThemeManager.MaxConcurrency));

            int completed = 0;
            int total = files.Count;
            AnalysisProgress.Maximum = total;
            AnalysisProgress.Value = 0;
            AnalysisProgressPanel.Visibility = Visibility.Visible;
            StatusText.Text = onlyFields == null
                ? $"Refreshing {total} {(total == 1 ? "file" : "files")}..."
                : $"Updating column for {total} {(total == 1 ? "file" : "files")}...";

            try
            {
                var tasks = files.Select(async file =>
                {
                    await semaphore.WaitAsync(ct);
                    try
                    {
                        ct.ThrowIfCancellationRequested();

                        // Bypass the scan cache deliberately: a refresh exists to recompute rows
                        // that read wrong or are missing data, so a cached hit would defeat it.
                        // A timeout keeps a hung decoder from pinning the slot forever (mirrors
                        // AnalyzeAndAddFiles). Concurrency is capped by the caller's semaphore, so
                        // this runs on the pool rather than creating an OS thread per file.
                        var analysisTask = Task.Run(
                            () => AudioAnalyzer.AnalyzeFile(file.FilePath, settings, ct), ct);
                        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                        var timeoutTask = Task.Delay(TimeSpan.FromSeconds(120), timeoutCts.Token);
                        if (await Task.WhenAny(analysisTask, timeoutTask) == timeoutTask)
                        {
                            ct.ThrowIfCancellationRequested();
                            return; // timed out — leave the existing row untouched
                        }
                        timeoutCts.Cancel();
                        var fresh = await analysisTask;

                        if (ThemeManager.ScanCacheEnabled)
                        {
                            try { ScanCacheService.Set(fresh, settings); }
                            catch (Exception ex) { if (ThemeManager.CrashLoggingEnabled) LocalCrashLogger.Write(ex); }
                        }

                        await Dispatcher.InvokeAsync(() =>
                        {
                            file.CopyAnalysisFrom(fresh, onlyFields);
                            AnalysisProgress.Value = Math.Min(Interlocked.Increment(ref completed), total);
                        }, DispatcherPriority.Background);
                    }
                    catch (OperationCanceledException) { }
                    catch
                    {
                        // A single bad file shouldn't abort the batch; just count it as done.
                        await Dispatcher.InvokeAsync(() =>
                            AnalysisProgress.Value = Math.Min(Interlocked.Increment(ref completed), total),
                            DispatcherPriority.Background);
                    }
                    finally { semaphore.Release(); }
                });

                await Task.WhenAll(tasks);

                if (ThemeManager.ScanCacheEnabled)
                {
                    // Cancellation is the normal path when the user starts another refresh, so it
                    // stays silent; a genuine write failure is what needs a trace.
                    try { await Task.Run(() => ScanCacheService.SaveToDisk(), ct); }
                    catch (OperationCanceledException) { }
                    catch (Exception ex) { if (ThemeManager.CrashLoggingEnabled) LocalCrashLogger.Write(ex); }
                }
            }
            catch (OperationCanceledException) { }
            finally
            {
                AnalysisProgressPanel.Visibility = Visibility.Collapsed;
                _isRefreshing = false;
                _refreshCts?.Dispose();
                _refreshCts = null;

                if (!ct.IsCancellationRequested)
                {
                    UpdateStatusSummary();
                    SaveSessionState();
                }
            }
        }
    }
}
