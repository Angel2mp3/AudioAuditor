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

        // AudioFileInfo properties populated by each analysis feature. Copying only these for a
        // just-enabled feature fills that one column without disturbing the others.
        private static readonly Dictionary<string, string[]> FeatureFields = new(StringComparer.OrdinalIgnoreCase)
        {
            ["BPM"]         = new[] { nameof(AudioFileInfo.Bpm) },
            ["DR"]          = new[] { nameof(AudioFileInfo.DynamicRange), nameof(AudioFileInfo.HasDynamicRange) },
            ["True Peak"]   = new[] { nameof(AudioFileInfo.TruePeakDbTP), nameof(AudioFileInfo.HasTruePeak) },
            ["LUFS"]        = new[] { nameof(AudioFileInfo.IntegratedLufs), nameof(AudioFileInfo.HasLufs) },
            ["Rip Quality"] = new[] { nameof(AudioFileInfo.RipQuality), nameof(AudioFileInfo.RipQualityDetail), nameof(AudioFileInfo.HasRipQuality) },
            ["Silence"]     = new[] { nameof(AudioFileInfo.LeadingSilenceMs), nameof(AudioFileInfo.TrailingSilenceMs), nameof(AudioFileInfo.MidTrackSilenceGaps), nameof(AudioFileInfo.TotalMidSilenceMs), nameof(AudioFileInfo.HasExcessiveSilence) },
            ["Clipping"]    = new[] { nameof(AudioFileInfo.HasClipping), nameof(AudioFileInfo.ClippingPercentage), nameof(AudioFileInfo.ClippingSamples), nameof(AudioFileInfo.MaxSampleLevel), nameof(AudioFileInfo.MaxSampleLevelDb), nameof(AudioFileInfo.HasScaledClipping), nameof(AudioFileInfo.ScaledClippingPercentage) },
            ["MQA"]         = new[] { nameof(AudioFileInfo.IsMqa), nameof(AudioFileInfo.IsMqaStudio), nameof(AudioFileInfo.MqaOriginalSampleRate), nameof(AudioFileInfo.MqaEncoder) },
            ["AI"]          = new[] { nameof(AudioFileInfo.IsAiGenerated), nameof(AudioFileInfo.AiSource), nameof(AudioFileInfo.AiSources), nameof(AudioFileInfo.ExperimentalAiSuspicious), nameof(AudioFileInfo.ExperimentalAiConfidence), nameof(AudioFileInfo.ExperimentalAiFlags) },
            ["Fake Stereo"] = new[] { nameof(AudioFileInfo.IsFakeStereo), nameof(AudioFileInfo.FakeStereoType), nameof(AudioFileInfo.StereoCorrelation) },
        };

        /// <summary>
        /// Backfills only the column(s) for the given just-enabled analysis features across all
        /// loaded rows — re-analyzes each file but copies just those features' fields, leaving the
        /// other columns' values untouched. Called from Settings when a feature flips OFF→ON.
        /// </summary>
        public void RefreshColumnsForFeatures(IReadOnlyCollection<string> featureHeaders)
        {
            if (_files.Count == 0 || _isAnalyzing || _isRefreshing) return;

            var fields = new HashSet<string>(StringComparer.Ordinal);
            foreach (var header in featureHeaders)
                if (FeatureFields.TryGetValue(header, out var f))
                    foreach (var name in f) fields.Add(name);

            if (fields.Count == 0) return;
            _ = RefreshFilesAsync(_files.ToList(), fields);
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
                        // A dedicated long-running thread + timeout keeps a hung decoder from
                        // pinning the slot forever (mirrors AnalyzeAndAddFiles).
                        var analysisTask = Task.Factory.StartNew(
                            () => AudioAnalyzer.AnalyzeFile(file.FilePath, settings, ct),
                            ct, TaskCreationOptions.LongRunning, TaskScheduler.Default);
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
                            try { ScanCacheService.Set(fresh, settings); } catch { }
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
                    try { await Task.Run(() => ScanCacheService.SaveToDisk(), ct); } catch { }
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
