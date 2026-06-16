using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using IOPath = System.IO.Path;
using AudioQualityChecker.Models;
using AudioQualityChecker.Services;
using AudioQualityChecker.Services.Scrobbling;

namespace AudioQualityChecker
{
    public partial class MainWindow
    {        // ─── WPF Now Playing: Lyrics ───

        private void NpCancelLyricsWork(bool invalidateVersion)
        {
            if (invalidateVersion)
                _npLyricsVersion++;

            _npLyricsCts?.Cancel();
            _npLyricsCts = null;
            NpStopLyricStatusRestoreTimer();
        }

        private void NpStopLyricStatusRestoreTimer()
        {
            _npLyricStatusRestoreTimer?.Stop();
            _npLyricStatusRestoreTimer = null;
        }

        /// <summary>
        /// Called the instant a track transition is committed — before the new track's decoder
        /// even loads — so the OUTGOING track's lyrics are dropped immediately. Without this they
        /// linger on screen and get scroll-snapped to the top during the load window (position
        /// resets to 0 on the old, still-displayed lines), which looks broken. NpSetTrack rebuilds
        /// lyrics for the new track once it has loaded.
        /// </summary>
        private void NpBeginTrackTransition(string newFilePath)
        {
            if (!_npVisible || !IsNowPlayingUiActive() || _npLyricsHidden)
                return;
            // Same track (Loop One / replay) — keep its lyrics, nothing is changing.
            if (string.Equals(_npLastTrackPath, newFilePath, StringComparison.OrdinalIgnoreCase))
                return;

            NpCancelLyricsWork(invalidateVersion: false);
            _npLyricsVersion++; // invalidate any in-flight fetch so its result can't paint stale lines
            _npLyricsNeedCatchUp = true;
            _npCurrentLyricIndex = -1;
            _npCurrentLyrics = LyricsResult.Empty;
            _npLyricTextBlocks.Clear();
            _npTranslatedLines = null;
            NpClearLyricsManualScrollGrace();
            NpLyricsScroller.ScrollToVerticalOffset(0);
            NpShowLyricStatus("Loading lyrics…", restoreLyrics: false);
        }

        private async Task NpLoadLyricsAsync(string filePath, string? artist = null, string? title = null, string? album = null, double durationSeconds = 0)
        {
            if (!IsNowPlayingUiActive())
            {
                _npPendingVisibleRefresh = true;
                return;
            }

            NpCancelLyricsWork(invalidateVersion: false);
            var lyricsCts = new CancellationTokenSource();
            _npLyricsCts = lyricsCts;
            _npCurrentLyricIndex = -1;
            int version = _npLyricsVersion; // snapshot before await

            // Show searching status immediately
            var providerName = NpLyricProviders[_npProviderIndex].Name;
            NpShowLyricStatus($"Searching for lyrics ({providerName})...", restoreLyrics: false);

            LyricsResult result;
            try
            {
                result = await LyricService.GetLyricsAsync(
                    filePath,
                    _npLyricProvider,
                    artist,
                    title,
                    album,
                    durationSeconds,
                    cancellationToken: lyricsCts.Token);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch
            {
                if (lyricsCts.IsCancellationRequested)
                    return;

                result = LyricService.GetLyrics(filePath, _npLyricProvider);
            }
            finally
            {
                if (ReferenceEquals(_npLyricsCts, lyricsCts))
                    _npLyricsCts = null;

                lyricsCts.Dispose();
            }

            // If the track changed while we were fetching, discard stale results
            if (version != _npLyricsVersion) return;
            if (!IsNowPlayingUiActive())
            {
                _npPendingVisibleRefresh = true;
                return;
            }

            // Normalize whitespace in fetched lyrics to prevent garbled display
            if (result.HasLyrics)
            {
                var normalizedLines = result.Lines
                    .Select(l => new LyricLine(l.Time,
                        System.Text.RegularExpressions.Regex.Replace(l.Text, @"[\t\r\n]+", " ").Trim())
                    {
                        Words = l.Words
                    })
                    .ToList();
                result = result with { Lines = normalizedLines };
            }

            _npCurrentLyrics = result;
            _npCurrentLyricIndex = -1;
            NpBuildLyricLines();
            NpResyncLyricsAfterRebuild();

            // Auto-save timed lyrics if enabled and no .lrc exists
            if (_npCurrentLyrics.IsTimed && ThemeManager.NpAutoSaveLyricsEnabled && _player?.CurrentFile != null)
            {
                string lrcPath = System.IO.Path.Combine(
                    System.IO.Path.GetDirectoryName(_player.CurrentFile)!,
                    System.IO.Path.GetFileNameWithoutExtension(_player.CurrentFile) + ".lrc");
                if (!File.Exists(lrcPath))
                {
                    try { NpSaveLyricsToLrc(_player.CurrentFile); }
                    catch { }
                }
            }

            await Dispatcher.InvokeAsync(() => NpUpdateSaveLyricsButton());

            // Force immediate lyric sync — the song may already be well into playback
            // by the time the async lyrics load completes, so kick the highlight now
            // rather than waiting for the next timer tick + layout pass
            if (_npCurrentLyrics.IsTimed && _player != null)
            {
                _npCurrentLyricIndex = -1;
                // Dispatch at Render priority so the layout pass completes first
                void SyncLoadedLyrics()
                {
                    if (version != _npLyricsVersion) return;
                    if (!IsNowPlayingUiActive()) return;
                    _npLyricsNeedCatchUp = true;
                    NpUpdateLyricHighlight(_player.CurrentPosition);
                }

                await Dispatcher.InvokeAsync(SyncLoadedLyrics, DispatcherPriority.Render);
                await Dispatcher.InvokeAsync(SyncLoadedLyrics, DispatcherPriority.ContextIdle);
            }
        }

        private void NpShowLyricStatus(string message, bool restoreLyrics = true)
        {
            bool canRestoreLyrics = restoreLyrics && _npCurrentLyrics.HasLyrics;
            int version = _npLyricsVersion;
            NpStopLyricStatusRestoreTimer();
            NpLyricsPanel.Children.Clear();
            _npLyricTextBlocks.Clear();
            var status = new TextBlock
            {
                Text = message,
                Foreground = (Brush)FindResource("TextDim"),
                FontSize = 15,
                FontStyle = FontStyles.Italic,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 20, 0, 0)
            };
            NpLyricsPanel.Children.Add(status);

            if (!canRestoreLyrics)
                return;

            var restoreTimer = new System.Windows.Threading.DispatcherTimer
                { Interval = TimeSpan.FromSeconds(2) };
            restoreTimer.Tick += (_, _) =>
            {
                restoreTimer.Stop();
                if (ReferenceEquals(_npLyricStatusRestoreTimer, restoreTimer))
                    _npLyricStatusRestoreTimer = null;
                if (version != _npLyricsVersion || !IsNowPlayingUiActive())
                    return;
                NpBuildLyricLines();
                NpResyncLyricsAfterRebuild();
            };
            _npLyricStatusRestoreTimer = restoreTimer;
            restoreTimer.Start();

        }

        private void NpBuildLyricLines()
        {
            NpStopLyricStatusRestoreTimer();
            NpLyricsPanel.Children.Clear();
            _npLyricTextBlocks.Clear();
            _npLyricEffectsCleared = false; // fresh lines need their focus effects (re)applied

            if (!_npCurrentLyrics.HasLyrics)
            {
                var providerName = NpLyricProviders[_npProviderIndex].Name;
                var noLyrics = new TextBlock
                {
                    Text = $"No lyrics found via {providerName}\nTry switching providers or drop a .lrc file",
                    Foreground = (Brush)FindResource("TextDim"),
                    FontSize = 15,
                    FontStyle = FontStyles.Italic,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 20, 0, 0)
                };
                NpLyricsPanel.Children.Add(noLyrics);
                return;
            }

            // Small top spacer only - lyrics start near the top aligned with album cover
            NpLyricsPanel.Children.Add(new Border { Height = 8 });

            for (int i = 0; i < _npCurrentLyrics.Lines.Count; i++)
            {
                var line = _npCurrentLyrics.Lines[i];

                // Main lyric line
                var tb = new TextBlock
                {
                    TextWrapping = TextWrapping.Wrap,
                    TextAlignment = TextAlignment.Left,
                    Margin = new Thickness(0, 6, 0, _npTranslateEnabled && _npTranslatedLines != null ? 0 : 6),
                    FontSize = _npLyricsSize > 0 ? _npLyricsSize : (WindowState == WindowState.Maximized ? 22 : 18),
                    FontFamily = new FontFamily("Segoe UI"),
                    Cursor = _npCurrentLyrics.IsTimed ? System.Windows.Input.Cursors.Hand : System.Windows.Input.Cursors.Arrow,
                    Foreground = new SolidColorBrush(
                        System.Windows.Media.Color.FromArgb(85, 255, 255, 255))
                };

                // Karaoke mode: build word-by-word Runs; otherwise plain text.
                // Enhanced LRC supplies exact word timings in line.Words. Build one Run per
                // KaraokeWord so NpAnimateKaraokeWords can use real timestamps instead of
                // falling back to line-duration guessing.
                if (_npKaraokeEnabled && _npCurrentLyrics.IsTimed)
                {
                    IEnumerable<string> displayWords = line.Words is { Count: > 0 }
                        ? line.Words.Select(w => w.Text).Where(w => !string.IsNullOrWhiteSpace(w))
                        : System.Text.RegularExpressions.Regex.Split(line.Text, @"\s+|[,.\-—!?;:\(\)\[\]""]+")
                            .Where(w => !string.IsNullOrEmpty(w));

                    foreach (var word in displayWords)
                    {
                        var run = new System.Windows.Documents.Run(word.Trim() + " ")
                        {
                            Foreground = new SolidColorBrush(
                                System.Windows.Media.Color.FromArgb(85, 255, 255, 255))
                        };
                        tb.Inlines.Add(run);
                    }
                }
                else
                {
                    tb.Text = line.Text;
                }

                // Click to seek to this lyric's timestamp
                if (_npCurrentLyrics.IsTimed)
                {
                    int lineIndex = i;
                    var capturedLyrics = _npCurrentLyrics; // capture for closure safety
                    tb.MouseLeftButtonDown += (s, e) =>
                    {
                        try
                        {
                            if (capturedLyrics != _npCurrentLyrics) return; // stale lyrics
                            if (lineIndex >= capturedLyrics.Lines.Count || _player == null) return;
                            var seekTime = capturedLyrics.Lines[lineIndex].Time;
                            if (_player.TotalDuration.TotalSeconds <= 0) return;
                            NpClearLyricsManualScrollGrace();
                            _player.Seek(seekTime.TotalSeconds);
                            _lastSeekTime = DateTime.UtcNow;
                            _lastSeekTargetPosition = seekTime;

                            // Update NP seek slider without triggering seek feedback
                            if (NpSeekSlider != null)
                            {
                                NpSeekSlider.ValueChanged -= NpSeekSlider_ValueChanged;
                                NpSeekSlider.Value = seekTime.TotalSeconds;
                                NpSeekSlider.ValueChanged += NpSeekSlider_ValueChanged;
                            }

                            // Also update main seek slider
                            if (SeekSlider?.Maximum > 0)
                                SeekSlider.Value = seekTime.TotalSeconds / _player.TotalDuration.TotalSeconds * SeekSlider.Maximum;

                            // Resume playback if paused
                            if (!_player.IsPlaying && _player.IsPaused)
                                _player.Resume();

                            // Force immediate lyric highlight update
                            _npCurrentLyricIndex = -1;
                            try
                            {
                                // Defensive: only update if lyric block counts match
                                if (_npLyricTextBlocks.Count == _npCurrentLyrics.Lines.Count)
                                    NpUpdateLyricHighlight(seekTime, skipLookahead: true);
                            }
                            catch (Exception ex)
                            {
                                System.Diagnostics.Debug.WriteLine($"Lyric highlight after seek error: {ex.Message}");
                            }

                            // Sync the playback state UI immediately so the play/pause icon
                            // doesn't lag a tick behind after a lyric jump (the 50 ms timer
                            // would eventually catch up, but the gap is visible to the user).
                            NpUpdatePlayState();
                            UpdatePlayerUI();
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"Lyric seek error: {ex.Message}");
                        }
                    };

                }

                tb.ContextMenu = NpCreateLyricLineContextMenu(includeSaveLyrics: _npCurrentLyrics.IsTimed);

                _npLyricTextBlocks.Add(tb);
                NpLyricsPanel.Children.Add(tb);

                // Show translation below the original line
                if (_npTranslateEnabled && _npTranslatedLines != null && i < _npTranslatedLines.Count)
                {
                    var translated = _npTranslatedLines[i];
                    if (!string.IsNullOrWhiteSpace(translated) &&
                        !string.Equals(translated, line.Text, StringComparison.OrdinalIgnoreCase))
                    {
                        var transTb = new TextBlock
                        {
                            Text = translated,
                            TextWrapping = TextWrapping.Wrap,
                            TextAlignment = TextAlignment.Left,
                            Margin = new Thickness(0, 0, 0, 6),
                            FontSize = 14,
                            FontStyle = FontStyles.Italic,
                            FontFamily = new FontFamily("Segoe UI"),
                            Foreground = new SolidColorBrush(
                                System.Windows.Media.Color.FromArgb(140, 255, 255, 255)),
                            Tag = "translation" // mark for styling
                        };
                        NpLyricsPanel.Children.Add(transTb);
                    }
                }
            }

            NpLyricsPanel.Children.Add(new Border { Height = 200 });

            // If translate was just enabled, kick off translation
            if (_npTranslateEnabled && _npTranslatedLines == null && !_npTranslationInProgress)
                _ = NpTranslateLyricsAsync();

            NpApplyFocusedLyricsEffects();
        }

        private ContextMenu NpCreateLyricLineContextMenu(bool includeSaveLyrics)
        {
            var menu = new ContextMenu();
            var modeItem = new MenuItem { Header = "Lyrics mode" };
            NpAddLyricModeItems(modeItem);
            menu.Items.Add(modeItem);
            var alternateItem = new MenuItem { Header = "Find alternate lyrics..." };
            alternateItem.Click += NpFindAlternateLyrics_Click;
            menu.Items.Add(alternateItem);

            if (includeSaveLyrics)
            {
                menu.Items.Add(new Separator());
                var saveItem = new MenuItem { Header = "Save Lyrics as .lrc" };
                saveItem.Click += (_, _) =>
                {
                    if (_player?.CurrentFile != null)
                    {
                        try
                        {
                            NpSaveLyricsToLrc(_player.CurrentFile);
                            NpShowLyricStatus("Lyrics saved to .lrc");
                        }
                        catch (Exception ex)
                        {
                            NpShowLyricStatus($"Save failed: {ex.Message}");
                        }
                    }
                };
                menu.Items.Add(saveItem);
            }

            return menu;
        }

        // ─── Lyric display mode (Standard / Blur / Uniform) ───

        private bool NpBlurMode => _npLyricMode == NpLyricDisplayMode.Blur;
        private bool NpUniformMode => _npLyricMode == NpLyricDisplayMode.Uniform;

        /// <summary>Adds the three lyric-mode options (checkable, current one ticked) to a menu.</summary>
        private void NpAddLyricModeItems(ItemsControl menu)
        {
            // "Uniform" intentionally removed — only Standard and Blur are offered.
            (string Label, NpLyricDisplayMode Mode)[] modes =
            {
                ("Standard", NpLyricDisplayMode.Standard),
                ("Blur", NpLyricDisplayMode.Blur),
            };
            foreach (var (label, mode) in modes)
            {
                var captured = mode;
                var item = new MenuItem { Header = label, IsCheckable = true, IsChecked = _npLyricMode == mode };
                // Ensure the themed MenuItem style applies even though this menu is built in code.
                item.SetResourceReference(System.Windows.FrameworkElement.StyleProperty, typeof(MenuItem));
                item.Click += (_, _) => NpSetLyricMode(captured);
                menu.Items.Add(item);
            }
        }

        private void NpUpdateLyricHighlight(TimeSpan position, bool skipLookahead = false)
        {
            var lyrics = _npCurrentLyrics; // capture reference to prevent mid-method replacement
            if (!lyrics.IsTimed || _npLyricTextBlocks.Count == 0) return;
            if (_npLyricTextBlocks.Count != lyrics.Lines.Count) return; // out of sync, skip

            // Use custom lyrics size if set, otherwise window-state default
            bool fs = WindowState == WindowState.Maximized;
            double baseLyricSize = _npLyricsSize > 0 ? _npLyricsSize : (fs ? 22 : 18);

            // Add 200 ms lookahead to compensate for NAudio audio-buffer lag —
            // the reported position trails actual playback by one buffer length (~50–200 ms).
            // Skip lookahead for click-seeks: the seek is instantaneous so the position is exact.
            var lookAhead = skipLookahead ? position : position + TimeSpan.FromMilliseconds(200);

            int newIdx = -1;
            for (int i = lyrics.Lines.Count - 1; i >= 0; i--)
            {
                if (lookAhead >= lyrics.Lines[i].Time)
                {
                    newIdx = i;
                    break;
                }
            }

            bool lineChanged = newIdx != _npCurrentLyricIndex;

            // In karaoke mode, update word progress on active line even without line change
            if (!lineChanged && _npKaraokeEnabled && newIdx >= 0 && newIdx < _npLyricTextBlocks.Count
                && _npLyricTextBlocks[newIdx] != null)
            {
                NpAnimateKaraokeWords(_npLyricTextBlocks[newIdx], newIdx, position);
                NpApplyFocusedLyricsEffects();
                return;
            }

            if (!lineChanged)
                return;

            int oldIdx = _npCurrentLyricIndex;
            _npCurrentLyricIndex = newIdx;

            var duration = TimeSpan.FromMilliseconds(150);
            var ease = new QuadraticEase { EasingMode = EasingMode.EaseOut };

            // Only restyle lines whose appearance actually changes: the previously-active line,
            // the newly-active line, and the lines between them (whose before/after inactive
            // colour flips). On the first highlight after a (re)build or seek (oldIdx < 0) we
            // restyle everything. This replaces ~N animation restarts per line change — the
            // source of the lyric-transition jank — with just a handful, while producing the
            // identical final styling (untouched lines already hold their correct state).
            bool fullRestyle = oldIdx < 0 || oldIdx >= _npLyricTextBlocks.Count;
            int restyleLo = fullRestyle ? 0 : Math.Min(oldIdx, newIdx);
            int restyleHi = fullRestyle ? _npLyricTextBlocks.Count - 1 : Math.Max(oldIdx, newIdx);

            for (int i = 0; i < _npLyricTextBlocks.Count; i++)
            {
                if (!fullRestyle && (i < restyleLo || i > restyleHi))
                    continue;

                var tb = _npLyricTextBlocks[i];

                if (i == newIdx)
                {
                    // Active line: bright highlight
                    tb.FontWeight = FontWeights.SemiBold;
                    if (NpBlurMode)
                    {
                        tb.BeginAnimation(TextBlock.FontSizeProperty, null);
                        tb.FontSize = baseLyricSize;
                        NpApplyLyricScale(tb, 1.045, duration, ease);
                    }
                    else
                    {
                        NpApplyFontSize(tb, baseLyricSize, duration, ease);
                        NpApplyLyricScale(tb, 1.0, duration, ease);
                    }

                    if (_npKaraokeEnabled && tb.Inlines.Count > 0)
                    {
                        // Karaoke word-by-word: illuminate words progressively
                        NpAnimateKaraokeWords(tb, i, position);
                    }
                    else
                    {
                        var activeBrush = NpEnsureMutableForeground(tb, Colors.White);
                        NpApplyBrushColor(activeBrush, Colors.White, duration, ease);
                    }
                }
                else
                {
                    // Non-active lines
                    System.Windows.Media.Color targetColor = NpGetInactiveLyricColor(i, newIdx);
                    double targetSize = baseLyricSize;
                    tb.FontWeight = FontWeights.Normal;

                    if (_npKaraokeEnabled && tb.Inlines.Count > 0)
                    {
                        // Reset all word Runs to dim
                        foreach (var inline in tb.Inlines)
                        {
                            if (inline is System.Windows.Documents.Run run)
                            {
                                var brush = NpEnsureMutableRunForeground(run, targetColor);
                                NpApplyBrushColor(brush, targetColor, duration, ease);
                            }
                        }
                    }
                    else
                    {
                        var brush = NpEnsureMutableForeground(tb, Colors.White);
                        NpApplyBrushColor(brush, targetColor, duration, ease);
                    }

                    if (NpBlurMode)
                    {
                        tb.BeginAnimation(TextBlock.FontSizeProperty, null);
                        tb.FontSize = targetSize;
                        NpApplyLyricScale(tb, 1.0, duration, ease);
                    }
                    else
                    {
                        NpApplyFontSize(tb, targetSize, duration, ease);
                        NpApplyLyricScale(tb, 1.0, duration, ease);
                    }
                }
            }

            NpApplyFocusedLyricsEffects();

            // Smooth auto-scroll — position active line at 25% from top
            if (newIdx >= 0 && newIdx < _npLyricTextBlocks.Count && NpCanAutoScrollLyrics())
            {
                var target = _npLyricTextBlocks[newIdx];
                try
                {
                    var transform = target.TransformToAncestor(NpLyricsPanel);
                    var point = transform.Transform(new System.Windows.Point(0, 0));
                    double scrollerHeight = NpLyricsScroller.ViewportHeight;
                    double targetY = point.Y - scrollerHeight * 0.25;
                    if (targetY < 0) targetY = 0;
                    NpAnimateScroll(NpLyricsScroller, targetY, 200);
                }
                catch { /* element not yet in visual tree */ }
            }
        }

        private DateTime _npLastLyricHealthCheckUtc = DateTime.MinValue;

        /// <summary>
        /// Safety net against a lyric highlight that lags/starts late and then stays desynced.
        /// Called from the 50ms NP tick. Two recoveries, both no-ops when the highlight is healthy:
        ///   1. If the rendered line count and the lyric line count diverge, NpUpdateLyricHighlight
        ///      early-returns forever and the highlight freezes — rebuild the lines to restore it.
        ///   2. Roughly once a second, recompute the expected active line from the live position and,
        ///      only if it differs from the highlighted one, force it back in step. Because it acts
        ///      solely on a real divergence, an in-sync highlight is never touched (no periodic flash).
        /// </summary>
        private void NpEnsureLyricSyncHealthy(TimeSpan position)
        {
            var lyrics = _npCurrentLyrics;
            if (!lyrics.IsTimed || _npLyricsHidden) return;
            if (!IsNowPlayingUiActive() || _player?.IsPlaying != true) return;
            if (lyrics.Lines.Count == 0) return;

            // (1) Self-heal a textblock/line count mismatch that would otherwise wedge the highlight.
            if (_npLyricTextBlocks.Count != lyrics.Lines.Count)
            {
                NpBuildLyricLines();
                NpResyncLyricsAfterRebuild();
                return;
            }

            // (2) Throttled divergence correction.
            if ((DateTime.UtcNow - _npLastLyricHealthCheckUtc).TotalMilliseconds < 1000) return;
            _npLastLyricHealthCheckUtc = DateTime.UtcNow;

            // Skip during the post-seek cooldown — the position is intentionally in flux there.
            if ((DateTime.UtcNow - _lastSeekTime).TotalMilliseconds < 500) return;

            // Mirror NpUpdateLyricHighlight's 200ms audio-buffer lookahead so "expected" matches
            // what a healthy highlight would compute.
            var lookAhead = position + TimeSpan.FromMilliseconds(200);
            int expected = -1;
            for (int i = lyrics.Lines.Count - 1; i >= 0; i--)
            {
                if (lookAhead >= lyrics.Lines[i].Time) { expected = i; break; }
            }

            if (expected != _npCurrentLyricIndex)
            {
                _npCurrentLyricIndex = -1;
                NpUpdateLyricHighlight(position);
            }
        }

        private void NpLyricsScroller_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            NpPauseLyricAutoScrollForUser();
        }

        private void NpLyricsScroller_ScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            if (_npLyricsAutoScrolling || Math.Abs(e.VerticalChange) < 0.01)
                return;

            if (NpLyricsScroller.IsMouseOver || Mouse.LeftButton == MouseButtonState.Pressed)
                NpPauseLyricAutoScrollForUser();
        }

        private void NpPauseLyricAutoScrollForUser()
        {
            _npLyricsManualScrollPauseUntilUtc = DateTime.UtcNow.AddSeconds(5);
            _npLyricsScrollTimer?.Stop();
            _npLyricsScrollTimer = null;
            _npLyricsAutoScrolling = false;
        }

        private void NpClearLyricsManualScrollGrace()
        {
            _npLyricsManualScrollPauseUntilUtc = DateTime.MinValue;
        }

        private bool NpCanAutoScrollLyrics() =>
            DateTime.UtcNow >= _npLyricsManualScrollPauseUntilUtc;

        private System.Windows.Media.Color NpGetInactiveLyricColor(int lineIndex, int activeIndex)
        {
            // Blur: inactive lines are white (distinguished by the blur/opacity effect instead).
            // Uniform: all lines share the same near-white colour (active distinguished by size only).
            if ((NpBlurMode || NpUniformMode) && activeIndex >= 0)
                return Colors.White;

            // Standard: past lines dimmer, upcoming lines slightly brighter.
            return lineIndex < activeIndex
                ? System.Windows.Media.Color.FromArgb(68, 255, 255, 255)
                : System.Windows.Media.Color.FromArgb(85, 255, 255, 255);
        }

        private double NpGetFocusedLyricOpacity(int lineIndex)
        {
            if (!NpBlurMode || _npCurrentLyricIndex < 0)
                return 1.0;

            int distance = Math.Abs(lineIndex - _npCurrentLyricIndex);
            if (distance == 0)
                return 1.0;

            return lineIndex < _npCurrentLyricIndex
                ? Math.Max(0.13, 1.0 - distance * 0.28)
                : Math.Max(0.20, 1.0 - distance * 0.20);
        }

        private static SolidColorBrush NpEnsureMutableForeground(TextBlock textBlock, Color fallback)
        {
            var brush = textBlock.Foreground as SolidColorBrush;
            if (brush == null || brush.IsFrozen)
            {
                brush = new SolidColorBrush(brush?.Color ?? fallback);
                textBlock.Foreground = brush;
            }
            return brush;
        }

        private static SolidColorBrush NpEnsureMutableRunForeground(System.Windows.Documents.Run run, Color fallback)
        {
            var brush = run.Foreground as SolidColorBrush;
            if (brush == null || brush.IsFrozen)
            {
                brush = new SolidColorBrush(brush?.Color ?? fallback);
                run.Foreground = brush;
            }
            return brush;
        }

        private static void NpApplyBrushColor(SolidColorBrush brush, Color targetColor, TimeSpan duration, IEasingFunction? ease)
        {
            if (!AnimationPolicy.IsMotionAllowed(AnimationArea.Lyrics))
            {
                brush.BeginAnimation(SolidColorBrush.ColorProperty, null);
                brush.Color = targetColor;
                return;
            }

            brush.BeginAnimation(SolidColorBrush.ColorProperty,
                new ColorAnimation(targetColor, duration) { EasingFunction = ease });
        }

        private static void NpApplyFontSize(TextBlock textBlock, double targetSize, TimeSpan duration, IEasingFunction? ease)
        {
            if (!AnimationPolicy.IsMotionAllowed(AnimationArea.Lyrics))
            {
                textBlock.BeginAnimation(TextBlock.FontSizeProperty, null);
                textBlock.FontSize = targetSize;
                return;
            }

            textBlock.BeginAnimation(TextBlock.FontSizeProperty,
                new DoubleAnimation(targetSize, duration) { EasingFunction = ease });
        }

        private static void NpApplyLyricScale(TextBlock textBlock, double targetScale, TimeSpan duration, IEasingFunction? ease)
        {
            if (textBlock.RenderTransform is not ScaleTransform scale)
            {
                scale = new ScaleTransform(1.0, 1.0);
                textBlock.RenderTransform = scale;
            }
            // Lyrics are left-aligned inside a ScrollViewer that clips horizontal
            // overflow. Scaling the active line from the center pushed its left edge
            // past x=0 and clipped the first characters ("beginning cut off"). Anchor
            // the scale to the left-middle so the line grows rightward and the start
            // stays put. Set every call so lines built before this fix are corrected too.
            textBlock.RenderTransformOrigin = new Point(0, 0.5);

            if (!AnimationPolicy.IsMotionAllowed(AnimationArea.Lyrics))
            {
                scale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
                scale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
                scale.ScaleX = targetScale;
                scale.ScaleY = targetScale;
                return;
            }

            var animation = new DoubleAnimation(targetScale, duration) { EasingFunction = ease };
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, animation);
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, animation.Clone());
        }

        /// <summary>Animate karaoke word-by-word illumination on the active line.</summary>
        private void NpAnimateKaraokeWords(TextBlock tb, int lineIdx, TimeSpan position)
        {
            var runs = tb.Inlines.OfType<System.Windows.Documents.Run>().ToList();
            if (runs.Count == 0) return;

            // Capture reference to protect against concurrent lyrics replacement
            var lyrics = _npCurrentLyrics;
            if (lineIdx >= lyrics.Lines.Count) return;

            var line = lyrics.Lines[lineIdx];
            var words = line.Words;

            var ease = new QuadraticEase { EasingMode = EasingMode.EaseOut };
            var dur = TimeSpan.FromMilliseconds(200);

            // Apply same audio-buffer lookahead as line-level timing
            var lookAhead = position + TimeSpan.FromMilliseconds(200);

            // If we have provider word-level timing and counts match, use real timings
            if (words != null && words.Count == runs.Count)
            {
                for (int w = 0; w < runs.Count; w++)
                {
                    var kw = words[w];
                    double wordDuration = (kw.End - kw.Start).TotalMilliseconds;
                    if (wordDuration <= 0) wordDuration = 500;

                    double wordElapsed = (lookAhead - kw.Start).TotalMilliseconds;
                    double factor = Math.Clamp(wordElapsed / wordDuration, 0, 1);

                    // Smooth transition with slight anticipation
                    factor = Math.Clamp(factor * 1.2, 0, 1);

                    byte a = (byte)(90 + (255 - 90) * factor);
                    byte rgb = (byte)(180 + (255 - 180) * factor);
                    var targetColor = System.Windows.Media.Color.FromArgb(a, 255, rgb, rgb);
                    if (factor >= 0.85) targetColor = Colors.White;

                    var brush = NpEnsureMutableRunForeground(runs[w], targetColor);
                    NpApplyBrushColor(brush, targetColor, dur, ease);
                }
                return;
            }

            // ── Fallback: character-count-proportional estimation ──
            var lineStart = line.Time;
            var lineEnd = lineIdx + 1 < lyrics.Lines.Count
                ? lyrics.Lines[lineIdx + 1].Time
                : lineStart + TimeSpan.FromSeconds(4);

            double lineDuration = (lineEnd - lineStart).TotalMilliseconds;
            if (lineDuration <= 0) lineDuration = 4000;
            double elapsed = (lookAhead - lineStart).TotalMilliseconds;
            double progress = Math.Clamp(elapsed / lineDuration, 0, 1);

            var wordLengths = runs.Select(r => Math.Max(1, r.Text.Length)).ToArray();
            int totalChars = wordLengths.Sum();
            double[] wordStartFrac = new double[runs.Count];
            double accumulated = 0;
            for (int i = 0; i < runs.Count; i++)
            {
                wordStartFrac[i] = accumulated / totalChars;
                accumulated += wordLengths[i];
            }

            double transitionWidth = Math.Max(0.05, 1.0 / runs.Count);

            for (int w = 0; w < runs.Count; w++)
            {
                double wordEndFrac = w + 1 < runs.Count ? wordStartFrac[w + 1] : 1.0;
                double wordCenter = (wordStartFrac[w] + wordEndFrac) / 2.0;
                double factor = Math.Clamp((progress - wordCenter + transitionWidth / 2) / transitionWidth, 0, 1);

                byte a = (byte)(90 + (255 - 90) * factor);
                byte rgb = (byte)(180 + (255 - 180) * factor);
                var targetColor = System.Windows.Media.Color.FromArgb(a, 255, rgb, rgb);
                if (factor >= 0.85) targetColor = Colors.White;

                var brush = NpEnsureMutableRunForeground(runs[w], targetColor);
                NpApplyBrushColor(brush, targetColor, dur, ease);
            }
        }

        /// <summary>Smoothly animates a ScrollViewer to a target vertical offset.</summary>
        private void NpAnimateScroll(ScrollViewer viewer, double targetOffset, double durationMs)
        {
            _npLyricsScrollTimer?.Stop();
            _npLyricsScrollTimer = null;
            _npLyricsAutoScrolling = false;

            double current = viewer.VerticalOffset;
            double diff = targetOffset - current;
            if (Math.Abs(diff) < 1) return;

            if (!AnimationPolicy.IsMotionAllowed(AnimationArea.Lyrics))
            {
                _npLyricsAutoScrolling = true;
                viewer.ScrollToVerticalOffset(targetOffset);
                Dispatcher.BeginInvoke(new Action(() => _npLyricsAutoScrolling = false), DispatcherPriority.Background);
                return;
            }

            int steps = (int)(durationMs / 16); // ~60fps
            if (steps < 1) steps = 1;
            int step = 0;
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
            _npLyricsAutoScrolling = true;
            timer.Tick += (_, _) =>
            {
                if (!IsNowPlayingUiActive())
                {
                    timer.Stop();
                    if (ReferenceEquals(_npLyricsScrollTimer, timer))
                        _npLyricsScrollTimer = null;
                    _npLyricsAutoScrolling = false;
                    return;
                }

                step++;
                double t = (double)step / steps;
                // ease-out quad
                t = 1 - (1 - t) * (1 - t);
                viewer.ScrollToVerticalOffset(current + diff * t);
                if (step >= steps)
                {
                    timer.Stop();
                    if (ReferenceEquals(_npLyricsScrollTimer, timer))
                        _npLyricsScrollTimer = null;
                    _npLyricsAutoScrolling = false;
                }
            };
            _npLyricsScrollTimer = timer;
            timer.Start();
        }


        // ─── NP Translation ───

        private void NpTranslate_Click(object sender, RoutedEventArgs e)
        {
            _npTranslateEnabled = !_npTranslateEnabled;
            NpUpdateTranslateIcon();

            if (_npTranslateEnabled)
            {
                if (ThemeManager.OfflineModeEnabled)
                {
                    ShowOfflineNotice("Lyric translation");
                    _npTranslateEnabled = false;
                    NpUpdateTranslateIcon();
                    return;
                }
                NpTranslateSettingsBtn.Visibility = Visibility.Visible;
                _ = NpTranslateLyricsAsync();
            }
            else
            {
                NpTranslateSettingsBtn.Visibility = Visibility.Collapsed;
                _npTranslatedLines = null;
                // Rebuild lines without translations
                NpBuildLyricLines();
                NpResyncLyricsAfterRebuild();
            }
            NpSavePreferences();
        }

        private void NpUpdateTranslateIcon()
        {
            NpTranslateIcon.Stroke = NpGetIconBrush(_npTranslateEnabled);
            NpTranslateBtn.ToolTip = _npTranslateEnabled ? "Translation: ON" : "Translate lyrics";
            NpSetToggleBg(NpTranslateBtn, _npTranslateEnabled);
        }

        private void NpFocusedLyrics_Click(object sender, RoutedEventArgs e)
        {
            // The button opens a small picker of the lyric display modes (Standard / Blur).
            var menu = new ContextMenu();
            menu.SetResourceReference(System.Windows.FrameworkElement.StyleProperty, typeof(ContextMenu));
            NpAddLyricModeItems(menu);
            menu.PlacementTarget = NpFocusedLyricsBtn;
            menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Top;
            menu.IsOpen = true;
        }

        private void NpSetLyricMode(NpLyricDisplayMode mode)
        {
            if (_npLyricMode == mode) return;
            _npLyricMode = mode;
            ThemeManager.NpLyricMode = mode;
            NpUpdateFocusedLyricsIcon();
            // Rebuild the lyric view so the new mode's colouring/effects apply cleanly mid-song.
            NpBuildLyricLines();
            NpResyncLyricsAfterRebuild();
            NpSavePreferences();
        }

        private void NpUpdateFocusedLyricsIcon()
        {
            bool active = _npLyricMode != NpLyricDisplayMode.Standard;
            NpFocusedLyricsIcon.Stroke = NpGetIconBrush(active);
            NpFocusedLyricsBtn.ToolTip = $"Lyrics mode: {_npLyricMode}";
            NpSetToggleBg(NpFocusedLyricsBtn, active);
        }

        private bool _npLyricEffectsCleared;

        private void NpApplyFocusedLyricsEffects()
        {
            if (_npLyricTextBlocks.Count == 0)
                return;

            bool focusedLyricsActive = NpBlurMode
                                       && IsNowPlayingUiActive()
                                       && _npCurrentLyrics.IsTimed;

            if (!focusedLyricsActive)
            {
                // Nothing to blur/dim. Clear effects, opacity and scale once, then skip on every
                // subsequent line change — re-applying identical 1.0 opacity/scale to every line
                // on each change was a needless animation storm.
                if (_npLyricEffectsCleared)
                    return;
                foreach (var tb in _npLyricTextBlocks)
                {
                    tb.Effect = null;
                    NpApplyLyricOpacity(tb, 1.0);
                    NpApplyLyricScale(tb, 1.0, TimeSpan.Zero, null);
                }
                _npLyricEffectsCleared = true;
                return;
            }

            _npLyricEffectsCleared = false;
            bool shouldBlurInactive = _npFocusedLyricsBlurRadius > 0;
            var inactiveBlur = shouldBlurInactive ? NpGetFocusedLyricsInactiveBlur() : null;

            for (int i = 0; i < _npLyricTextBlocks.Count; i++)
            {
                var tb = _npLyricTextBlocks[i];
                bool inactiveFocusedLine = _npCurrentLyricIndex >= 0 && i != _npCurrentLyricIndex;
                tb.Effect = shouldBlurInactive && inactiveFocusedLine ? inactiveBlur : null;
                NpApplyLyricOpacity(tb, inactiveFocusedLine ? NpGetFocusedLyricOpacity(i) : 1.0);
            }
        }

        private static void NpApplyLyricOpacity(TextBlock textBlock, double targetOpacity)
        {
            if (!AnimationPolicy.IsMotionAllowed(AnimationArea.Lyrics))
            {
                textBlock.BeginAnimation(UIElement.OpacityProperty, null);
                textBlock.Opacity = targetOpacity;
                return;
            }

            textBlock.BeginAnimation(UIElement.OpacityProperty,
                new DoubleAnimation(targetOpacity, TimeSpan.FromMilliseconds(180))
                {
                    EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
                });
        }

        private void NpClearFocusedLyricsEffects()
        {
            foreach (var tb in _npLyricTextBlocks)
            {
                tb.Effect = null;
                tb.BeginAnimation(UIElement.OpacityProperty, null);
                tb.Opacity = 1.0;
                NpApplyLyricScale(tb, 1.0, TimeSpan.Zero, null);
            }
        }

        private void NpClearLyricAnimations()
        {
            foreach (var tb in _npLyricTextBlocks)
            {
                tb.BeginAnimation(TextBlock.FontSizeProperty, null);
                tb.BeginAnimation(UIElement.OpacityProperty, null);
                if (tb.RenderTransform is ScaleTransform scale)
                {
                    scale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
                    scale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
                    scale.ScaleX = 1.0;
                    scale.ScaleY = 1.0;
                }
                if (tb.Foreground is SolidColorBrush brush)
                    brush.BeginAnimation(SolidColorBrush.ColorProperty, null);

                foreach (var run in tb.Inlines.OfType<System.Windows.Documents.Run>())
                {
                    if (run.Foreground is SolidColorBrush runBrush)
                        runBrush.BeginAnimation(SolidColorBrush.ColorProperty, null);
                }
            }
        }

        public void ApplyAnimationsEnabledState()
        {
            // Each NP area is gated independently so Battery Saver's per-area flags
            // (and Reduce Motion's all-off) take effect without a restart. Start
            // methods self-guard via AnimationPolicy, so we only need to stop the
            // areas that are no longer allowed and (re)start the rest.
            bool bg = AnimationPolicy.IsMotionAllowed(AnimationArea.NpBackground);
            bool glow = AnimationPolicy.IsMotionAllowed(AnimationArea.CoverGlow);
            bool lyrics = AnimationPolicy.IsMotionAllowed(AnimationArea.Lyrics);

            if (!bg) NpStopBgAnimation();
            if (!glow) NpStopGlowPulse();
            if (!lyrics)
            {
                _npLyricsScrollTimer?.Stop();
                _npLyricsScrollTimer = null;
                NpClearLyricAnimations();
            }

            NpApplyFocusedLyricsEffects();
            if (IsNowPlayingUiActive())
            {
                if (bg) NpStartBgAnimation();
                if (glow) NpStartGlowPulse();
            }
        }

        /// <summary>
        /// Umbrella re-apply for all performance/motion settings (Reduce Motion +
        /// Battery Saver). Covers the NP areas AND the audio visualizers, which the
        /// NP-only path above doesn't own. Called from the Settings handlers.
        /// </summary>
        public void ApplyPerformancePolicy()
        {
            ApplyAnimationsEnabledState();
            ApplyVisualizerPerformancePolicy();
        }

        private void NpResyncLyricsAfterRebuild()
        {
            NpApplyFocusedLyricsEffects();

            if (!_npCurrentLyrics.IsTimed)
            {
                _npLyricsNeedCatchUp = false;
                return;
            }

            _npCurrentLyricIndex = -1;
            _npLyricsNeedCatchUp = true;

            if (_npLyricTextBlocks.Count == 0)
                return;

            if (!IsNowPlayingUiActive() || _player == null)
                return;

            bool lyricSeekCooldown = (DateTime.UtcNow - _lastSeekTime).TotalMilliseconds < 500;
            var lyricPos = lyricSeekCooldown ? _lastSeekTargetPosition : _player.CurrentPosition;
            NpUpdateLyricHighlight(lyricPos);

            _ = Dispatcher.InvokeAsync(() =>
            {
                if (IsNowPlayingUiActive() && _npCurrentLyrics.IsTimed && _player != null)
                {
                    bool retrySeekCooldown = (DateTime.UtcNow - _lastSeekTime).TotalMilliseconds < 500;
                    var retryPos = retrySeekCooldown ? _lastSeekTargetPosition : _player.CurrentPosition;
                    NpUpdateLyricHighlight(retryPos);
                }
            }, DispatcherPriority.ContextIdle);
        }

        private void NpTranslateSettings_Click(object sender, RoutedEventArgs e)
        {
            // Toggle popup
            if (NpTranslatePopup.IsOpen)
            {
                NpTranslatePopup.IsOpen = false;
                return;
            }

            // Populate combos if empty
            if (NpTranslateFromCombo.Items.Count == 0)
            {
                NpTranslateFromCombo.Items.Add(new ComboBoxItem { Content = "Auto-detect", Tag = "auto" });
                foreach (var kv in TranslateService.LanguageNames)
                    NpTranslateFromCombo.Items.Add(new ComboBoxItem { Content = kv.Value, Tag = kv.Key });
                NpTranslateFromCombo.SelectedIndex = 0;
            }
            if (NpTranslateToCombo.Items.Count == 0)
            {
                foreach (var kv in TranslateService.LanguageNames)
                    NpTranslateToCombo.Items.Add(new ComboBoxItem { Content = kv.Value, Tag = kv.Key });
                // Default: English
                for (int i = 0; i < NpTranslateToCombo.Items.Count; i++)
                {
                    if (NpTranslateToCombo.Items[i] is ComboBoxItem ci && ci.Tag is string t && t == "en")
                    { NpTranslateToCombo.SelectedIndex = i; break; }
                }
                if (NpTranslateToCombo.SelectedIndex < 0)
                    NpTranslateToCombo.SelectedIndex = 0;
            }
            NpTranslatePopup.IsOpen = true;
        }

        private void NpTranslateFrom_Changed(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (NpTranslateFromCombo.SelectedItem is ComboBoxItem item && item.Tag is string lang)
            {
                _npTranslateFrom = lang;
                if (_npTranslateEnabled)
                {
                    if (ThemeManager.OfflineModeEnabled)
                    {
                        ShowOfflineNotice("Lyric translation");
                        return;
                    }
                    _npTranslatedLines = null;
                    _ = NpTranslateLyricsAsync();
                }
            }
        }

        private void NpTranslateTo_Changed(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (NpTranslateToCombo.SelectedItem is ComboBoxItem item && item.Tag is string lang)
            {
                _npTranslateTo = lang;
                if (_npTranslateEnabled)
                {
                    if (ThemeManager.OfflineModeEnabled)
                    {
                        ShowOfflineNotice("Lyric translation");
                        return;
                    }
                    _npTranslatedLines = null;
                    _ = NpTranslateLyricsAsync();
                }
            }
        }

        private async Task NpTranslateLyricsAsync()
        {
            if (_npTranslationInProgress)
                return;

            var lyrics = _npCurrentLyrics; // capture reference before async work
            if (!lyrics.HasLyrics) return;
            int version = _npLyricsVersion; // snapshot to detect track change

            var lines = lyrics.Lines.Select(l => l.Text).ToList();
            if (lines.Count == 0) return;

            _npTranslationInProgress = true;
            try
            {
                // Detect source language if set to auto
                string fromLang = _npTranslateFrom;
                if (fromLang == "auto")
                {
                    // Use first non-empty, non-instrumental line to detect
                    var sample = lines.FirstOrDefault(l =>
                        !string.IsNullOrWhiteSpace(l) && l.Length > 3
                        && !l.StartsWith('[') && !l.StartsWith('(')) ?? "";
                    if (!string.IsNullOrWhiteSpace(sample))
                        fromLang = await TranslateService.DetectLanguageAsync(sample);
                    else
                        fromLang = "en"; // fallback
                }

                if (version != _npLyricsVersion) return; // track changed during await

                // Don't translate if source == target
                if (string.Equals(fromLang, _npTranslateTo, StringComparison.OrdinalIgnoreCase))
                {
                    _npTranslatedLines = null;
                    NpBuildLyricLines();
                    NpResyncLyricsAfterRebuild();
                    return;
                }

                try
                {
                    _npTranslatedLines = await TranslateService.TranslateLinesAsync(lines, fromLang, _npTranslateTo);
                }
                catch
                {
                    _npTranslatedLines = null;
                }

                if (version != _npLyricsVersion) return; // track changed during await
                NpBuildLyricLines();
                NpResyncLyricsAfterRebuild();
            }
            finally
            {
                _npTranslationInProgress = false;
            }
        }

        // ─── NP Karaoke Mode ───

        private void NpKaraoke_Click(object sender, RoutedEventArgs e)
        {
            _npKaraokeEnabled = !_npKaraokeEnabled;
            NpUpdateKaraokeIcon();
            _npCurrentLyricIndex = -1;
            NpBuildLyricLines();
            NpResyncLyricsAfterRebuild();
            NpSavePreferences();
        }

        private void NpUpdateKaraokeIcon()
        {
            NpKaraokeIcon.Stroke = NpGetIconBrush(_npKaraokeEnabled);

            if (!_npKaraokeEnabled)
            {
                NpKaraokeBtn.ToolTip = "Karaoke word-by-word";
            }
            else
            {
                bool hasWordSync = _npCurrentLyricIndex >= 0
                    && _npCurrentLyricIndex < _npCurrentLyrics.Lines.Count
                    && _npCurrentLyrics.Lines[_npCurrentLyricIndex].Words != null;
                NpKaraokeBtn.ToolTip = hasWordSync
                    ? "Karaoke: ON (synced)"
                    : "Karaoke: ON (estimated)";
            }

            NpSetToggleBg(NpKaraokeBtn, _npKaraokeEnabled);
        }


        // ─── NP Drag Leave (hide overlay) ───

        private void NpLyrics_DragLeave(object sender, System.Windows.DragEventArgs e)
        {
            NpDropOverlay.Visibility = Visibility.Collapsed;
        }

        // ─── NP Drag and Drop LRC ───

        private void NpLyrics_DragOver(object sender, System.Windows.DragEventArgs e)
        {
            e.Effects = System.Windows.DragDropEffects.None;
            if (e.Data.GetDataPresent(System.Windows.DataFormats.FileDrop))
            {
                var files = e.Data.GetData(System.Windows.DataFormats.FileDrop) as string[];
                if (files != null && files.Any(f =>
                    f.EndsWith(".lrc", StringComparison.OrdinalIgnoreCase)))
                {
                    e.Effects = System.Windows.DragDropEffects.Copy;
                    NpDropOverlay.Visibility = Visibility.Visible;
                }
            }
            e.Handled = true;
        }

        private void NpLyrics_Drop(object sender, System.Windows.DragEventArgs e)
        {
            NpDropOverlay.Visibility = Visibility.Collapsed;

            if (!e.Data.GetDataPresent(System.Windows.DataFormats.FileDrop)) return;
            var files = e.Data.GetData(System.Windows.DataFormats.FileDrop) as string[];
            if (files == null) return;

            var lrcFile = files.FirstOrDefault(f =>
                f.EndsWith(".lrc", StringComparison.OrdinalIgnoreCase));
            if (lrcFile == null) return;

            var result = LyricService.LoadFromLrcFile(lrcFile);
            if (result.HasLyrics)
            {
                _npCurrentLyrics = result;
                _npCurrentLyricIndex = -1;
                NpBuildLyricLines();
                NpResyncLyricsAfterRebuild();
                NpUpdateSaveLyricsButton();
            }
        }

        // ─── NP Load LRC File (file picker) ───

        private void NpLoadLrcFile_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "LRC files (*.lrc)|*.lrc|All files (*.*)|*.*",
                Title = "Select LRC lyrics file"
            };
            if (dlg.ShowDialog() == true)
            {
                var result = LyricService.LoadFromLrcFile(dlg.FileName);
                if (result.HasLyrics)
                {
                    _npCurrentLyrics = result;
                    _npCurrentLyricIndex = -1;
                    NpBuildLyricLines();
                    NpResyncLyricsAfterRebuild();
                    NpUpdateSaveLyricsButton();
                }
            }
        }

        // ─── NP Search LRCLIB ───

        private async void NpSearchLyrics_Click(object sender, RoutedEventArgs e)
        {
            if (ThemeManager.OfflineModeEnabled)
            {
                ShowOfflineNotice("Online lyrics search");
                return;
            }
            if (_player.CurrentFile == null) return;

            var currentFile = _files.FirstOrDefault(f =>
                string.Equals(f.FilePath, _player.CurrentFile, StringComparison.OrdinalIgnoreCase));
            if (currentFile == null) return;

            var artist = currentFile.Artist ?? "";
            var title = currentFile.Title ?? currentFile.FileName ?? "";

            if (string.IsNullOrWhiteSpace(artist) && string.IsNullOrWhiteSpace(title)) return;

            NpSongSpecs.Text = "Searching lyrics...";

            try
            {
                var results = await LyricService.SearchLrcLibAsync(artist, title);
                if (results.Count > 0)
                {
                    var best = results.FirstOrDefault(r => r.HasSyncedLyrics)
                               ?? results.FirstOrDefault(r => r.HasPlainLyrics);
                    if (best != null)
                    {
                        _npCurrentLyrics = LyricService.ApplySearchResult(best);
                        _npCurrentLyricIndex = -1;
                        NpBuildLyricLines();
                        NpResyncLyricsAfterRebuild();
                        NpUpdateSaveLyricsButton();
                        // Restore specs text
                        NpSongSpecs.Text = "";
                        if (_player.CurrentFile != null)
                        {
                            var cf = _files.FirstOrDefault(f =>
                                string.Equals(f.FilePath, _player.CurrentFile, StringComparison.OrdinalIgnoreCase));
                            if (cf != null) NpSetTrack(cf);
                        }
                        return;
                    }
                }
                NpSongSpecs.Text = "No lyrics found";
            }
            catch
            {
                NpSongSpecs.Text = "Search failed";
            }
        }

        private async void NpFindAlternateLyrics_Click(object sender, RoutedEventArgs e)
        {
            if (ThemeManager.OfflineModeEnabled)
            {
                ShowOfflineNotice("Alternate lyrics search");
                return;
            }
            if (_player.CurrentFile == null) return;

            var currentFile = _files.FirstOrDefault(f =>
                string.Equals(f.FilePath, _player.CurrentFile, StringComparison.OrdinalIgnoreCase));
            if (currentFile == null) return;

            string artist = currentFile.Artist ?? "";
            string title = currentFile.Title ?? currentFile.FileName ?? "";
            if (string.IsNullOrWhiteSpace(artist) && string.IsNullOrWhiteSpace(title)) return;

            NpShowLyricStatus("Finding alternate lyrics...", restoreLyrics: true);

            try
            {
                var results = await LyricService.SearchLrcLibAsync(artist, title);
                if (results.Count == 0)
                {
                    NpShowLyricStatus("No alternate lyrics found.", restoreLyrics: true);
                    return;
                }

                NpShowAlternateLyricsWindow(results);
            }
            catch
            {
                NpShowLyricStatus("Alternate lyric search failed.", restoreLyrics: true);
            }
        }

        private void NpShowAlternateLyricsWindow(IReadOnlyList<LrcLibSearchResult> results)
        {
            var window = new Window
            {
                Owner = this,
                Title = "Alternate lyrics",
                Width = 660,
                Height = 430,
                MinWidth = 520,
                MinHeight = 320,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Background = (Brush)FindResource("WindowBg"),
                Foreground = (Brush)FindResource("TextPrimary")
            };

            var root = new DockPanel { Margin = new Thickness(16) };
            var header = new TextBlock
            {
                Text = "Choose a lyric result",
                FontSize = 17,
                FontWeight = FontWeights.SemiBold,
                Foreground = (Brush)FindResource("TextPrimary"),
                Margin = new Thickness(0, 0, 0, 12)
            };
            DockPanel.SetDock(header, Dock.Top);
            root.Children.Add(header);

            var list = new StackPanel();
            foreach (var result in results
                         .Where(r => r.HasSyncedLyrics || r.HasPlainLyrics)
                         .Take(20))
            {
                var row = new Border
                {
                    Background = (Brush)FindResource("GlassFloatingBg"),
                    BorderBrush = (Brush)FindResource("GlassBorderBrush"),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(7),
                    Padding = new Thickness(10),
                    Margin = new Thickness(0, 0, 0, 8)
                };

                var grid = new Grid();
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                string kind = result.HasSyncedLyrics ? "Synced" : "Plain";
                string duration = result.Duration > 0
                    ? TimeSpan.FromSeconds(result.Duration).ToString(@"m\:ss")
                    : "unknown length";
                var details = new TextBlock
                {
                    Text = $"LRCLIB • {kind} • {duration}\n{result.TrackName} — {result.ArtistName}\n{result.AlbumName}",
                    Foreground = (Brush)FindResource("TextSecondary"),
                    FontFamily = new FontFamily("Segoe UI"),
                    FontSize = 12.5,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 0, 12, 0)
                };
                Grid.SetColumn(details, 0);
                grid.Children.Add(details);

                var useButton = new Button
                {
                    Content = "Use",
                    Padding = new Thickness(14, 5, 14, 5),
                    VerticalAlignment = VerticalAlignment.Center,
                    Cursor = Cursors.Hand
                };
                useButton.Click += (_, _) =>
                {
                    var applied = LyricService.ApplySearchResult(result);
                    if (!applied.HasLyrics) return;
                    _npCurrentLyrics = applied;
                    _npCurrentLyricIndex = -1;
                    _npTranslatedLines = null;
                    NpBuildLyricLines();
                    NpResyncLyricsAfterRebuild();
                    NpUpdateSaveLyricsButton();
                    window.Close();
                };
                Grid.SetColumn(useButton, 1);
                grid.Children.Add(useButton);

                row.Child = grid;
                list.Children.Add(row);
            }

            if (list.Children.Count == 0)
            {
                list.Children.Add(new TextBlock
                {
                    Text = "No usable alternate lyrics were returned.",
                    Foreground = (Brush)FindResource("TextSecondary"),
                    FontSize = 13,
                    Margin = new Thickness(0, 12, 0, 0)
                });
            }

            root.Children.Add(new ScrollViewer
            {
                Content = list,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto
            });
            window.Content = root;
            window.ShowDialog();
        }

        /// <summary>Update the Now Playing panel when a new track starts.</summary>
        private void UpdateNowPlayingView()
        {
            if (!_npVisible || _player.CurrentFile == null)
                return;

            if (!IsNowPlayingUiActive())
            {
                _npPendingVisibleRefresh = true;
                return;
            }

            NpResumeVisibleWork(forceReloadLyrics: false, forceLyricResync: true);
        }

        private void NpLoadPreferences()
        {
            if (_npPrefsLoaded) return;
            _npPrefsLoaded = true;
            _npVisualizerEnabled = ThemeManager.NpVisualizerEnabled;
            _npColorMatchEnabled = ThemeManager.NpColorMatchEnabled;
            _npLyricsHidden = ThemeManager.NpLyricsHidden;
            _npTranslateEnabled = ThemeManager.NpTranslateEnabled;
            _npKaraokeEnabled = ThemeManager.NpKaraokeEnabled;
            _npLyricMode = ThemeManager.NpLyricMode;
            _npFocusedLyricsBlurRadius = ThemeManager.NpFocusedLyricsBlurRadius;
            _npCoverGlowMotionEnabled = ThemeManager.NpCoverGlowMotionEnabled;
            _npGlowMotionMode = ThemeManager.NpGlowMotionMode;
            _npVisualizerStyle = ThemeManager.NpVisualizerStyle;
            _npSubCoverShowArtist = ThemeManager.NpSubCoverShowArtist;
            _npLayoutProfileIsFullscreen = WindowState == WindowState.Maximized;
            _npLayoutProfileVisualizerEnabled = _npVisualizerEnabled;
            _npCoverGlowSize = ThemeManager.NpCoverGlowSize;
            NpLoadActiveLayoutProfile(_npLayoutProfileIsFullscreen, _npLayoutProfileVisualizerEnabled);
            NpApplyCoverGlowScale();
            NpApplyCoverShape();
        }

        private void NpSavePreferences()
        {
            if (!_npPrefsLoaded)
                NpLoadPreferences();

            ThemeManager.NpVisualizerEnabled = _npVisualizerEnabled;
            ThemeManager.NpColorMatchEnabled = _npColorMatchEnabled;
            ThemeManager.NpLyricsHidden = _npLyricsHidden;
            ThemeManager.NpTranslateEnabled = _npTranslateEnabled;
            ThemeManager.NpKaraokeEnabled = _npKaraokeEnabled;
            ThemeManager.NpLyricMode = _npLyricMode;
            ThemeManager.NpFocusedLyricsBlurRadius = _npFocusedLyricsBlurRadius;
            ThemeManager.NpCoverGlowMotionEnabled = _npCoverGlowMotionEnabled;
            ThemeManager.NpGlowMotionMode = _npGlowMotionMode;
            ThemeManager.NpVisualizerStyle = _npVisualizerStyle;
            ThemeManager.NpSubCoverShowArtist = _npSubCoverShowArtist;
            ThemeManager.NpCoverGlowSize = _npCoverGlowSize;
            NpEnsureLayoutProfileForCurrentWindowState();
            NpSaveActiveLayoutProfile(_npLayoutProfileIsFullscreen, _npLayoutProfileVisualizerEnabled);
            ThemeManager.SavePlayOptions();
        }

        private void EqEnabled_Changed(object sender, RoutedEventArgs e)
        {
            bool enabled = ChkEqEnabled.IsChecked == true;
            ThemeManager.EqualizerEnabled = enabled;

            var eq = _player.CurrentEqualizer;
            if (eq != null)
                eq.Enabled = enabled;

            ThemeManager.SavePlayOptions();
        }

        private void EqReset_Click(object sender, RoutedEventArgs e)
        {
            for (int i = 0; i < _eqSliders.Length; i++)
            {
                _eqSliders[i].Value = 0;
                ThemeManager.EqualizerGains[i] = 0;
            }

            _player.CurrentEqualizer?.Reset();
            ThemeManager.SavePlayOptions();

            // After resetting we're effectively on Flat — reflect it in the dropdown
            if (EqProfileCombo != null)
            {
                _suppressEqProfileSelection = true;
                EqProfileCombo.SelectedItem = "Flat";
                _suppressEqProfileSelection = false;
            }
            UpdateEqDeleteButtonVisibility();
        }

    }
}
