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
    {
        private void NowPlaying_Click(object sender, RoutedEventArgs e)
        {
            ToggleNowPlaying(!_npVisible);
        }

        private void ToggleNowPlaying(bool show)
        {
            _npVisible = show;
            NowPlayingPanel.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
            MainContent.Visibility = show ? Visibility.Collapsed : Visibility.Visible;
            Debug.WriteLine($"Now Playing visible={show}; discordEnabled={_discord.IsEnabled}; scrobbleTrackActive={_scrobbler.HasCurrentTrack}");

            if (show)
            {
                _miniPlayerWindow?.SetExternalVisualizerSuspended(true);
                NpLoadPreferences();
                NpEnsureUpdateTimer();

                NpVolumeSlider.Value = VolumeSlider.Value;
                NpUpdatePlayState();
                NpUpdateShuffleIcon();
                NpUpdateLoopIcon();
                if (_npVisible) NpUpdateQueuePopup();
                NpUpdateAutoPlayIcon();
                NpUpdateCrossfadeIcon();
                NpUpdateVisualizerIcon();
                NpUpdateColorMatchIcon();
                NpApplyVizPlacement();
                NpApplyButtonBar();
                NpUpdateLyricsOffIcon();
                NpUpdateTranslateIcon();
                NpUpdateFocusedLyricsIcon();
                NpUpdateKaraokeIcon();
                NpApplyLyricsOffMode();
                NpUpdateNextTrackPreview();
                NpProviderBtn.ToolTip = $"Provider: {NpLyricProviders[_npProviderIndex].Name}";

                NpResumeVisibleWork(forceReloadLyrics: true, forceLyricResync: true);

                if (_visualizerActive && !_npVisualizerEnabled)
                {
                    // Main visualizer is running but NP viz is off — pause main since it's hidden
                    _mainVizWasActive = true;
                    StopVisualizer();
                    VisualizerCanvas.Children.Clear();
                }


            }
            else
            {
                _miniPlayerWindow?.SetExternalVisualizerSuspended(false);
                if (NpEqPopup?.IsOpen == true)
                    NpEqPopup.IsOpen = false;
                ReturnEqualizerPanelHome(collapse: true);
                NpSuspendVisibleWork(markPendingRefresh: false);

                // Resume main visualizer if it was running before NP opened
                if (_mainVizWasActive && _visualizerMode && _player.IsPlaying)
                {
                    _mainVizWasActive = false;
                    ClearVisualizerCaches();
                    VisualizerCanvas.Children.Clear();
                    StartVisualizer();
                }

                // Re-assert main-window theme ownership when returning from Now Playing.
                if (ThemeManager.MainColorMatchEnabled && _mainAlbumPrimary != default)
                {
                    ApplyMainColorMatch();
                }
                else
                {
                    // Restore the standard theme's playbar colors. ApplyTheme → UpdatePlaybarAccentResource
                    // sets PlaybarAccentColor/Secondary to the theme's bright playbar accent. Do NOT hand-set
                    // them to ProgressGradient[0] first — that's the DARK gradient stop and is what left the
                    // playbar looking black on standard themes (e.g. Ocean) after returning from Now Playing.
                    ThemeManager.ApplyTheme(ThemeManager.CurrentTheme);
                    ThemeManager.UpdatePlaybarAccentResource();
                    ApplyThemeTitleBar();
                }
                RefreshVisualizerOwnershipAfterMiniChange();
            }
        }

        private NowPlayingWorkState CaptureNowPlayingWorkState() => new(
            MainWindowVisible: IsVisible,
            MainWindowMinimized: WindowState == WindowState.Minimized,
            NowPlayingVisible: _npVisible,
            NowPlayingVisualizerEnabled: _npVisualizerEnabled,
            MiniPlayerVisible: _miniPlayerWindow?.IsVisible == true,
            MiniPlayerMinimized: _miniPlayerWindow?.WindowState == WindowState.Minimized,
            MiniPlayerVisualizerEnabled: _miniPlayerWindow?.IsMiniVisualizerActive == true,
            MiniPlayerExternallySuspended: _miniPlayerWindow?.IsExternalVisualizerSuspended == true,
            MainVisualizerEnabled: _visualizerMode,
            PlaybackActive: _player.IsPlaying);

        private bool IsNowPlayingUiActive() =>
            NowPlayingWorkCoordinator.CanRunNowPlayingWork(CaptureNowPlayingWorkState());

        private void NpEnsureUpdateTimer()
        {
            if (_npUpdateTimer != null) return;
            _npUpdateTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
            _npUpdateTimer.Tick += NpUpdateTimer_Tick;
        }

        private void NpSuspendVisibleWork(bool markPendingRefresh)
        {
            _npLifecycleState = _npVisible ? NpLifecycleState.Suspended : NpLifecycleState.Hidden;
            _npUpdateTimer?.Stop();
            _npLyricsScrollTimer?.Stop();
            _npLyricsScrollTimer = null;
            NpCancelLyricsWork(invalidateVersion: true);
            NpClearFocusedLyricsEffects();
            NpStopBgAnimation();
            NpStopGlowPulse();
            NpStopVisualizer();
            NpEnsurePlaybarCycleRendering(false); // stop the 60fps CompositionTarget.Rendering hook while hidden
            NpWaveformCanvas?.Children.Clear();
            if (!IsVisible || WindowState == WindowState.Minimized)
                StopWaveformAnimation();

            if (markPendingRefresh && _npVisible)
                _npPendingVisibleRefresh = true;
        }

        private void NpResumeVisibleWork(bool forceReloadLyrics, bool forceLyricResync)
        {
            if (!IsNowPlayingUiActive())
            {
                if (_npVisible)
                    _npPendingVisibleRefresh = true;
                NpSuspendVisibleWork(markPendingRefresh: false);
                return;
            }

            if (_npResumeVisibleWorkRunning)
            {
                if (forceReloadLyrics)
                    _npPendingVisibleRefresh = true;
                if (forceLyricResync)
                    _npLyricsNeedCatchUp = true;
                return;
            }

            var now = DateTime.UtcNow;
            if (_npLifecycleState == NpLifecycleState.Active &&
                !_npPendingVisibleRefresh &&
                !forceReloadLyrics &&
                !forceLyricResync &&
                (now - _npLastResumeVisibleWorkUtc).TotalMilliseconds < 150)
            {
                return;
            }

            _npResumeVisibleWorkRunning = true;
            _npLifecycleState = NpLifecycleState.Resuming;
            try
            {
                _isSeeking = false;
                _npIsSeeking = false;
                _lastSeekTime = DateTime.MinValue;
                _lastSeekTargetPosition = _player.CurrentPosition;
                _npLyricMode = ThemeManager.NpLyricMode;
                NpInvalidatePlaybarVisuals();
                if (forceLyricResync)
                    _npLyricsNeedCatchUp = true;

                NpEnsureUpdateTimer();
                NpRefreshVisibleNowPlaying(forceReloadLyrics: forceReloadLyrics || _npPendingVisibleRefresh);
                _npPendingVisibleRefresh = false;
                NpUpdatePlayState();
                NpUpdateNextTrackPreview();
                _npUpdateTimer?.Start();
                NpStartBgAnimation();
                NpStartGlowPulse();

                if (NowPlayingWorkCoordinator.ResolveVisualizerOwner(CaptureNowPlayingWorkState()) == VisualizerSurfaceOwner.NowPlaying)
                    NpStartVisualizer();
                if (ThemeManager.NpPlaybarAnimationStyle == PlaybarAnimationStyle.Wave && _player.CurrentFile != null)
                {
                    if (_waveformBaseData.Length == 0)
                        GenerateWaveformData();
                    StartWaveformAnimation();
                }

                if (NpTryResolveActiveColorMatchPalette(out _, out _, out _, out _))
                    NpApplyColorMatchMode();
                else if (ThemeManager.MainColorMatchEnabled && _mainAlbumPrimary != default)
                    ApplyMainColorMatch();
                else
                    ApplyThemeTitleBar();

                if (forceLyricResync)
                    NpResyncLyricsAfterRebuild();
                else
                    NpApplyFocusedLyricsEffects();

                // Ensure all toggle states and icons are resynced after minimize/restore
                NpUpdateKaraokeIcon();
                NpUpdateTranslateIcon();
                NpUpdateFocusedLyricsIcon();
                NpUpdateCrossfadeIcon();

                NpUpdateTimer_Tick(null, EventArgs.Empty);
                if (ThemeManager.NpPlaybarAnimationStyle == PlaybarAnimationStyle.Wave)
                    WaveformAnimation_Tick(null, EventArgs.Empty);
            }
            finally
            {
                _npLifecycleState = IsNowPlayingUiActive() ? NpLifecycleState.Active : NpLifecycleState.Suspended;
                _npResumeVisibleWorkRunning = false;
                _npLastResumeVisibleWorkUtc = DateTime.UtcNow;
            }
        }

        private void NpRefreshVisibleNowPlaying(bool forceReloadLyrics)
        {
            if (!IsNowPlayingUiActive())
            {
                if (_npVisible)
                    _npPendingVisibleRefresh = true;
                return;
            }

            _npPendingVisibleRefresh = false;

            if (_player.CurrentFile == null)
                return;

            var currentFile = _files.FirstOrDefault(f =>
                string.Equals(f.FilePath, _player.CurrentFile, StringComparison.OrdinalIgnoreCase));
            if (currentFile == null)
                return;

            if (forceReloadLyrics)
                _npLastTrackPath = null;

            NpSetTrack(currentFile);
        }

        private void NpSetTrack(AudioFileInfo file)
        {
            // Reset Now Playing seek slider to prevent stale seeks on track change
            double duration = string.Equals(_player.CurrentFile, file.FilePath, StringComparison.OrdinalIgnoreCase)
                ? _player.TotalDuration.TotalSeconds
                : 0;
            double position = duration > 0 ? Math.Clamp(_player.CurrentPosition.TotalSeconds, 0, duration) : 0;
            NpSeekSlider.ValueChanged -= NpSeekSlider_ValueChanged;
            NpSeekSlider.Maximum = duration;
            NpSeekSlider.Value = position;
            NpSeekSlider.ValueChanged += NpSeekSlider_ValueChanged;
            NpRenderPlaybarStyle();

            var displayTitle = file.Title
                ?? (file.FileName != null ? System.IO.Path.GetFileNameWithoutExtension(file.FileName) : null)
                ?? "Unknown";
            NpSongTitle.Text = displayTitle;
            NpBigTitle.Text = displayTitle;
            NpSongArtist.Text = file.Artist ?? "";

            // Reset lyrics when switching tracks — clear immediately to prevent stale state
            bool isSameTrack = string.Equals(_npLastTrackPath, file.FilePath, StringComparison.OrdinalIgnoreCase);
            _npLastTrackPath = file.FilePath;

            if (!isSameTrack)
            {
                NpClearLyricsManualScrollGrace();
                NpLyricsScroller.ScrollToVerticalOffset(0);
                _npCurrentLyricIndex = -1;
                _npCurrentLyrics = LyricsResult.Empty;
                _npLyricTextBlocks.Clear();
                _npTranslatedLines = null;
                NpLyricsPanel.Children.Clear();
                NpCancelLyricsWork(invalidateVersion: false);
                _npLyricsVersion++; // invalidate any in-flight lyrics fetch
                _npLyricsNeedCatchUp = true;
                NpLoadColorPickerOverridesForTrack(file.FilePath);
            }

            if (!string.IsNullOrEmpty(file.FilePath))
            {
                ObserveUiTask(NpLoadCoverAsync(file.FilePath), nameof(NpLoadCoverAsync));
                if (!isSameTrack && !_npLyricsHidden)
                    ObserveUiTask(NpLoadLyricsAsync(file.FilePath, file.Artist, file.Title, file.Album, file.DurationSeconds), nameof(NpLoadLyricsAsync));
            }

            // Defer heavy UI rebuilds so track transitions feel instant
            string filePath = file.FilePath;
            Dispatcher.BeginInvoke(new Action(() =>
            {
                // Guard against rapid track changes — only update if this is still the current track
                if (_player?.CurrentFile == null ||
                    !string.Equals(_player.CurrentFile, filePath, StringComparison.OrdinalIgnoreCase))
                    return;

                var defaultBrush = (Brush)FindResource("TextSecondary");

                // Build audio specs with color-coded bitrate
                NpSongSpecs.Inlines.Clear();
                var specParts = new List<string>();
                if (!string.IsNullOrEmpty(file.FormatDisplay)) specParts.Add(file.FormatDisplay);
                if (file.SampleRate > 0) specParts.Add($"{file.SampleRate / 1000.0:0.#} kHz");
                if (file.BitsPerSample > 0) specParts.Add($"{file.BitsPerSample}-bit");
                if (file.Channels > 0) specParts.Add(file.Channels == 1 ? "Mono" : file.Channels == 2 ? "Stereo" : $"{file.Channels}ch");

                for (int s = 0; s < specParts.Count; s++)
                {
                    if (s > 0) NpSongSpecs.Inlines.Add(new System.Windows.Documents.Run("  •  ") { Foreground = defaultBrush });
                    NpSongSpecs.Inlines.Add(new System.Windows.Documents.Run(specParts[s]) { Foreground = defaultBrush });
                }

                int displayBitrate = file.ActualBitrate > 0 ? file.ActualBitrate : file.ReportedBitrate;
                if (displayBitrate > 0)
                {
                    if (specParts.Count > 0)
                        NpSongSpecs.Inlines.Add(new System.Windows.Documents.Run("  •  ") { Foreground = defaultBrush });

                    var statusColor = file.Status switch
                    {
                        AudioStatus.Valid => System.Windows.Media.Color.FromRgb(0x4C, 0xC9, 0x4C),
                        AudioStatus.Fake => System.Windows.Media.Color.FromRgb(0xFF, 0x5C, 0x5C),
                        AudioStatus.Corrupt => System.Windows.Media.Color.FromRgb(0xFF, 0x5C, 0x5C),
                        _ => System.Windows.Media.Color.FromRgb(0xFF, 0xA5, 0x00),
                    };
                    NpSongSpecs.Inlines.Add(new System.Windows.Documents.Run($"{displayBitrate} kbps")
                    {
                        Foreground = new SolidColorBrush(statusColor),
                        FontWeight = FontWeights.SemiBold
                    });
                }

                if (ThemeManager.DynamicRangeEnabled && file.HasDynamicRange && file.DynamicRange > 0)
                {
                    NpSongSpecs.Inlines.Add(new System.Windows.Documents.Run("  •  ") { Foreground = defaultBrush });
                    NpSongSpecs.Inlines.Add(new System.Windows.Documents.Run($"DR-{file.DynamicRange:0}") { Foreground = defaultBrush });
                }

                if (ThemeManager.BpmDetectionEnabled && file.Bpm > 0)
                {
                    NpSongSpecs.Inlines.Add(new System.Windows.Documents.Run("  •  ") { Foreground = defaultBrush });
                    NpSongSpecs.Inlines.Add(new System.Windows.Documents.Run($"{file.Bpm} BPM") { Foreground = defaultBrush });
                }

                if (ThemeManager.RipQualityEnabled && file.HasRipQuality)
                {
                    NpSongSpecs.Inlines.Add(new System.Windows.Documents.Run("  •  ") { Foreground = defaultBrush });
                    var ripColor = file.RipQuality switch
                    {
                        "Good" => System.Windows.Media.Color.FromRgb(0x4C, 0xC9, 0x4C),
                        "Suspect" => System.Windows.Media.Color.FromRgb(0xFF, 0xA5, 0x00),
                        "Bad" => System.Windows.Media.Color.FromRgb(0xFF, 0x5C, 0x5C),
                        _ => System.Windows.Media.Color.FromRgb(0xFF, 0xA5, 0x00),
                    };
                    NpSongSpecs.Inlines.Add(new System.Windows.Documents.Run(file.RipQualityDisplay)
                    {
                        Foreground = new SolidColorBrush(ripColor),
                        FontWeight = FontWeights.SemiBold
                    });
                }

                // Build MQA / AI / quality tags
                NpTagsPanel.Children.Clear();
                if (file.IsMqa)
                    NpTagsPanel.Children.Add(NpCreateTag(file.IsMqaStudio ? "MQA Studio" : "MQA", "#00C2FF"));
                if (file.IsAlac)
                    NpTagsPanel.Children.Add(NpCreateTag("ALAC", "#7ACC52"));
                if (file.AiVerdict == "Yes")
                    NpTagsPanel.Children.Add(NpCreateTag("AI", "#FF6B6B"));
                else if (file.AiVerdict == "Possible")
                    NpTagsPanel.Children.Add(NpCreateTag("AI?", "#FFC107"));
                if (file.IsFakeStereo)
                    NpTagsPanel.Children.Add(NpCreateTag("Fake Stereo", "#FFA500"));

                NpUpdateNextTrackPreview();
            }), DispatcherPriority.Background);
        }

        private Border NpCreateTag(string text, string color)
        {
            var c = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(color);
            return new Border
            {
                Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(40, c.R, c.G, c.B)),
                BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromArgb(100, c.R, c.G, c.B)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(3),
                Padding = new Thickness(5, 1, 5, 1),
                Margin = new Thickness(2, 0, 0, 0),
                Child = new TextBlock
                {
                    Text = text,
                    FontSize = 9,
                    FontWeight = FontWeights.SemiBold,
                    FontFamily = new FontFamily("Segoe UI"),
                    Foreground = new SolidColorBrush(c),
                    VerticalAlignment = VerticalAlignment.Center
                }
            };
        }

        private async Task NpLoadCoverAsync(string filePath)
        {
            int gen = _npColorGeneration;
            string cacheKey = HashPath(filePath);
            try
            {
                // Check cover bytes cache first for instant loading
                byte[]? imageData = null;
                lock (_coverBytesCacheLock)
                {
                    if (_coverBytesCache.TryGetValue(cacheKey, out var cachedBytes))
                        imageData = cachedBytes;
                }

                // Check color cache first for instant application
                if (TryGetNpColorFromCache(cacheKey, out var cachedColors))
                {
                    BitmapImage? cachedBitmap = null;
                    try
                    {
                        if (imageData == null)
                            imageData = await Task.Run(() => NpReadCoverImageData(filePath));
                        if (imageData != null)
                            cachedBitmap = await Task.Run(() => NpDecodeCoverBitmap(imageData));
                    }
                    catch { /* cached colors can still be applied without artwork */ }

                    Dispatcher.Invoke(() =>
                    {
                        if (gen != _npColorGeneration) return;
                        if (cachedBitmap != null)
                            NpApplyCoverBitmap(cachedBitmap, cacheKey);
                        ApplyNpExtractedColors(cachedColors);
                    });
                    return;
                }

                if (imageData == null)
                    imageData = await Task.Run(() => NpReadCoverImageData(filePath));

                if (imageData == null)
                {
                    Dispatcher.Invoke(() => { if (gen == _npColorGeneration) NpClearCover(); });
                    return;
                }

                var bmp = await Task.Run(() => NpDecodeCoverBitmap(imageData));

                Dispatcher.Invoke(() =>
                {
                    if (gen != _npColorGeneration) return;
                    NpApplyCoverBitmap(bmp, cacheKey);
                });

                await NpApplyGlowAsync(imageData, filePath, gen);
            }
            catch
            {
                Dispatcher.Invoke(() => { if (gen == _npColorGeneration) NpClearCover(); });
            }
        }

        private static byte[]? NpReadCoverImageData(string filePath)
        {
            using var tagFile = TagLib.File.Create(filePath);
            if (tagFile.Tag.Pictures.Length == 0)
                return null;

            var data = tagFile.Tag.Pictures[0].Data.Data;
            return data.Length == 0 ? null : data.ToArray();
        }

        private static BitmapImage NpDecodeCoverBitmap(byte[] imageData)
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            using (var ms = new MemoryStream(imageData))
            {
                bitmap.StreamSource = ms;
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.EndInit();
            }
            bitmap.Freeze();
            return bitmap;
        }

        private void NpApplyCoverBitmap(BitmapSource bitmap, string? cacheKey = null)
        {
            NpCoverImage.Source = bitmap;
            NpApplyNowPlayingBackdrop(bitmap, cacheKey);
            NpCoverImage.ClearValue(FrameworkElement.WidthProperty);
            NpCoverImage.ClearValue(FrameworkElement.HeightProperty);
            NpApplyFullscreenScaling(WindowState == WindowState.Maximized);
            NpApplyCoverShape();
        }

        private void NpClearCover()
        {
            NpCoverImage.Source = null;
            NpCoverImage.ClearValue(FrameworkElement.WidthProperty);
            NpCoverImage.ClearValue(FrameworkElement.HeightProperty);
            NpCoverGlow1.Background = Brushes.Transparent;
            NpCoverGlow2.Background = Brushes.Transparent;
            NpBgGradient.Background = Brushes.Transparent;
            NpApplyNowPlayingBackdrop(null);
            NpCoverShadow.Color = Colors.Black;
            NpCoverShadow.Opacity = 0.4;
            _npAlbumPrimary = default;
            _npAlbumSecondary = default;
            _npAlbumTertiary = default;
            _npAlbumBackground = default;
            NpResetColorMatchCaches();
        }

        private void NpApplyNowPlayingBackdrop(BitmapSource? albumBitmap, string? cacheKey = null)
        {
            if (NpAlbumBackdropImage == null || NpAlbumBackdropBlur == null)
                return;

            var customSource = NpResolveCustomNowPlayingBackdrop();
            var source = customSource ?? (ThemeManager.NpAlbumBackdropEnabled ? albumBitmap : null);
            if (source == null)
            {
                NpClearNowPlayingBackdrop();
                return;
            }

            if (!IsNowPlayingUiActive())
            {
                _npPendingVisibleRefresh = true;
                return;
            }

            NpAlbumBackdropBlur.Radius = Math.Clamp(ThemeManager.NpBackgroundBlur, 0, 48);
            NpAlbumBackdropImage.Source = customSource != null
                ? source
                : NpGetCachedBackdropSource((BitmapSource)source, cacheKey);
            NpAlbumBackdropImage.Opacity = Math.Clamp(ThemeManager.NpBackgroundOpacity, 0, 0.8);
            NpApplyBackdropBrightnessOverlay();
            double zoom = Math.Clamp(ThemeManager.NpBackgroundZoom, 1, 2.5);
            NpAlbumBackdropImage.RenderTransformOrigin = new Point(
                Math.Clamp(ThemeManager.NpBackgroundHorizontalPosition, 0, 1),
                Math.Clamp(ThemeManager.NpBackgroundVerticalPosition, 0, 1));
            NpAlbumBackdropImage.RenderTransform = zoom <= 1.001
                ? Transform.Identity
                : new ScaleTransform(zoom, zoom);
        }

        private void NpApplyBackdropBrightnessOverlay()
        {
            if (NpAlbumBackdropBrightnessOverlay == null)
                return;

            double brightness = Math.Clamp(ThemeManager.NpBackgroundBrightness, 0.35, 1.6);
            if (brightness < 0.995)
            {
                NpAlbumBackdropBrightnessOverlay.Background = Brushes.Black;
                NpAlbumBackdropBrightnessOverlay.Opacity = Math.Clamp((1.0 - brightness) * 0.7, 0, 0.45);
            }
            else if (brightness > 1.005)
            {
                NpAlbumBackdropBrightnessOverlay.Background = Brushes.White;
                NpAlbumBackdropBrightnessOverlay.Opacity = Math.Clamp((brightness - 1.0) * 0.18, 0, 0.12);
            }
            else
            {
                NpAlbumBackdropBrightnessOverlay.Opacity = 0;
            }
        }

        private BitmapSource NpGetCachedBackdropSource(BitmapSource source, string? cacheKey)
        {
            string key = cacheKey ?? "";
            if (string.IsNullOrWhiteSpace(key))
                key = $"{source.PixelWidth}x{source.PixelHeight}:{source.GetHashCode()}";

            if (_npBackdropCachedSource != null &&
                string.Equals(_npBackdropCacheKey, key, StringComparison.Ordinal))
            {
                return _npBackdropCachedSource;
            }

            _npBackdropCacheKey = key;
            _npBackdropCachedSource = NpCreatePerformanceBackdrop(source);
            return _npBackdropCachedSource;
        }

        private static BitmapSource NpCreatePerformanceBackdrop(BitmapSource source)
        {
            int maxSide = Math.Max(source.PixelWidth, source.PixelHeight);
            if (maxSide <= 900)
                return source;

            double scale = 900.0 / maxSide;
            var scaled = new TransformedBitmap(source, new ScaleTransform(scale, scale));
            scaled.Freeze();
            return scaled;
        }

        internal void NpRefreshBackdropFromSettings()
        {
            NpApplyNowPlayingBackdrop(NpCoverImage?.Source as BitmapSource);
            if (IsNowPlayingUiActive())
            {
                NpStopBgAnimation();
                NpStartBgAnimation();
            }
        }

        private ImageSource? NpResolveCustomNowPlayingBackdrop()
        {
            if (!string.Equals(ThemeManager.NpBackgroundMode, "CustomImage", StringComparison.OrdinalIgnoreCase))
                return null;

            string path = ThemeManager.NpCustomBackgroundImagePath;
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                return null;

            if (_npBackdropCustomSource != null &&
                string.Equals(_npBackdropCustomPath, path, StringComparison.OrdinalIgnoreCase))
            {
                return _npBackdropCustomSource;
            }

            try
            {
                var image = new BitmapImage();
                image.BeginInit();
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.UriSource = new Uri(path, UriKind.Absolute);
                image.EndInit();
                image.Freeze();
                _npBackdropCustomPath = path;
                _npBackdropCustomSource = NpCreatePerformanceBackdrop(image);
                return _npBackdropCustomSource;
            }
            catch
            {
                _npBackdropCustomPath = null;
                _npBackdropCustomSource = null;
                return null;
            }
        }

        private void NpClearNowPlayingBackdrop()
        {
            if (NpAlbumBackdropImage == null) return;
            NpAlbumBackdropImage.Source = null;
            NpAlbumBackdropImage.Opacity = 0;
            NpAlbumBackdropImage.RenderTransform = Transform.Identity;
            if (NpAlbumBackdropBrightnessOverlay != null)
                NpAlbumBackdropBrightnessOverlay.Opacity = 0;
            _npBackdropCacheKey = null;
            _npBackdropCachedSource = null;
        }

        private void NpCoverBorder_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            NpCoverClip.Rect = new Rect(0, 0, e.NewSize.Width, e.NewSize.Height);
            NpApplyCoverShape();
        }

        /// <summary>
        /// Pre-computes the next track's album-art colors and caches cover image bytes
        /// in the background so track switching feels instant. Cancelled automatically
        /// when the user skips before the preload completes.
        /// </summary>
        private void PreloadNextTrackData()
        {
            if (!ThemeManager.PreloadNextTrackEnabled) return;

            _npPreloadCts?.Cancel();
            _npPreloadCts = new CancellationTokenSource();
            var ct = _npPreloadCts.Token;

            var nextTrack = GetNextTrackForGapless();
            if (nextTrack == null) return;
            string path = nextTrack.FilePath;
            string hash = HashPath(path);

            // Warm the next track's audio decoder so normal auto-advance / next-skip starts almost
            // instantly instead of opening it cold after the current track ends. Gapless/crossfade
            // have their own next-track machinery and don't adopt through Play(), so skip there.
            // This only pre-opens a decoder by path — it never touches the shuffle deck or advance
            // logic, so it can't affect which track plays next.
            if (!ThemeManager.GaplessEnabled && !ThemeManager.Crossfade)
            {
                _ = Task.Run(() =>
                {
                    if (ct.IsCancellationRequested) return;
                    try { _player.PrepareNextDecoder(path); } catch { /* cold open at transition is the fallback */ }
                });
            }

            bool needsColors = !NpColorCacheContains(hash);
            bool needsCoverBytes = false;
            lock (_coverBytesCacheLock)
                needsCoverBytes = !_coverBytesCache.ContainsKey(hash);

            if (!needsColors && !needsCoverBytes) return;

            _ = Task.Run(async () =>
            {
                try
                {
                    byte[]? imageData = await Task.Run(() => NpReadCoverImageData(path), ct);
                    if (imageData == null || ct.IsCancellationRequested) return;

                    // Cache cover bytes for instant loading
                    if (needsCoverBytes)
                    {
                        lock (_coverBytesCacheLock)
                        {
                            if (_coverBytesCache.Count >= CoverBytesCacheMaxEntries)
                            {
                                var oldest = _coverBytesCache.Keys.FirstOrDefault();
                                if (oldest != null) _coverBytesCache.Remove(oldest);
                            }
                            _coverBytesCache[hash] = imageData;
                        }
                    }

                    // Extract and cache colors
                    if (needsColors)
                    {
                        var colors = await Task.Run(() =>
                        {
                            var bmp = new BitmapImage();
                            bmp.BeginInit();
                            using (var ms = new MemoryStream(imageData))
                            {
                                bmp.StreamSource = ms;
                                bmp.CacheOption = BitmapCacheOption.OnLoad;
                                bmp.EndInit();
                            }
                            bmp.Freeze();

                            var converted = new FormatConvertedBitmap(bmp, PixelFormats.Bgra32, null, 0);
                            int stride = converted.PixelWidth * 4;
                            var pixels = new byte[stride * converted.PixelHeight];
                            converted.CopyPixels(pixels, stride, 0);

                            return AlbumColorExtractor.Extract(pixels, converted.PixelWidth, converted.PixelHeight, stride);
                        }, ct);

                        if (!ct.IsCancellationRequested)
                            StoreNpColorInCache(path, colors);
                    }
                }
                catch { /* preload is best-effort */ }
            });
        }

        private void StoreNpColorInCache(string filePath, AlbumColorExtractor.DominantColors colors)
        {
            if (!ThemeManager.NpColorCacheEnabled) return;
            _npColorThemeService.StoreForFilePath(filePath, colors);
        }

        private bool TryGetNpColorFromCache(string key, out AlbumColorExtractor.DominantColors colors)
        {
            colors = null!;
            if (!ThemeManager.NpColorCacheEnabled)
                return false;

            return _npColorThemeService.TryGetByKey(key, out colors);
        }

        private bool NpColorCacheContains(string key)
        {
            return _npColorThemeService.ContainsByKey(key);
        }

        private static string HashPath(string path)
        {
            return ColorThemeService.HashPath(path);
        }

        private void LoadNpColorCacheFromDisk()
        {
            if (!ThemeManager.NpColorCachePersist && !ThemeManager.NpRememberManualColorPicks) return;
            _npColorThemeService.LoadFromDisk(includeAutoColors: ThemeManager.NpColorCachePersist);
        }

        private void SaveNpColorCacheToDisk()
        {
            if (!ThemeManager.NpColorCachePersist && !ThemeManager.NpRememberManualColorPicks) return;
            _npColorThemeService.SaveToDisk(includeAutoColors: ThemeManager.NpColorCachePersist);
        }

        private void ApplyNpExtractedColors(AlbumColorExtractor.DominantColors colors)
        {
            colors = AlbumColorExtractor.SanitizeDominantColors(colors);
            var primaryColor = System.Windows.Media.Color.FromRgb(
                colors.Primary.R, colors.Primary.G, colors.Primary.B);
            var secondaryColor = System.Windows.Media.Color.FromRgb(
                colors.Secondary.R, colors.Secondary.G, colors.Secondary.B);
            var tertiaryColor = System.Windows.Media.Color.FromRgb(
                colors.Tertiary.R, colors.Tertiary.G, colors.Tertiary.B);
            var backgroundColor = System.Windows.Media.Color.FromRgb(
                colors.Background.R, colors.Background.G, colors.Background.B);

            _npAlbumPrimary = primaryColor;
            _npAlbumSecondary = secondaryColor;
            _npAlbumTertiary = tertiaryColor;
            _npAlbumBackground = backgroundColor;
            NpResetColorMatchCaches();

            NpApplyCoverGlowBrushes(primaryColor, secondaryColor);

            NpCoverShadow.Color = primaryColor;
            NpCoverShadow.Opacity = 0.6;

            var bg1 = System.Windows.Media.Color.FromArgb(220,
                colors.Background.R, colors.Background.G, colors.Background.B);
            var bg2 = System.Windows.Media.Color.FromArgb(200,
                (byte)(colors.Background.R / 4),
                (byte)(colors.Background.G / 4),
                (byte)(colors.Background.B / 4));
            NpBgGradient.Background = new LinearGradientBrush(bg1, bg2, 45);

            NpApplyColorMatchMode();

            // If Color Drift is running, reinstall its gradient brush NOW (same frame) from the new
            // album colors. Otherwise the static gradient above renders for one frame and the drift
            // tick rebuilds a frame later — the visible flash + "wrong shade" on track change.
            if (_npBgAnimTimer != null)
                NpEnsureColorDriftBrush();

            // Belt-and-suspenders: schedule a second re-apply at ContextIdle so any
            // resource writes that race with this one (e.g. LoadMainCoverColors finishing
            // in the same dispatch frame) get overridden by the album colors. The visible
            // screen wins shared resources — when on main, main runs last; when on NP, NP runs last.
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (_npVisible)
                {
                    if (ThemeManager.MainColorMatchEnabled && _mainAlbumPrimary != default)
                        ApplyMainColorMatch();
                    if (_npColorMatchEnabled && (_npAlbumPrimary != default || NpHasColorPickerOverridesForCurrentTrack()))
                        NpApplyColorMatchMode();
                }
                else
                {
                    if (_npColorMatchEnabled && (_npAlbumPrimary != default || NpHasColorPickerOverridesForCurrentTrack()))
                        NpApplyColorMatchMode();
                    if (ThemeManager.MainColorMatchEnabled && _mainAlbumPrimary != default)
                        ApplyMainColorMatch();
                }

                // Keep Color Drift's brush as the installed background after the idle re-apply too.
                if (_npBgAnimTimer != null)
                    NpEnsureColorDriftBrush();
            }), System.Windows.Threading.DispatcherPriority.ContextIdle);
        }

        private async Task NpApplyGlowAsync(byte[] imageData, string? cacheKey = null, int generation = -1)
        {
            try
            {
                var colors = await Task.Run(() =>
                {
                    var bmp = new BitmapImage();
                    bmp.BeginInit();
                    using (var ms = new MemoryStream(imageData))
                    {
                        bmp.StreamSource = ms;
                        bmp.CacheOption = BitmapCacheOption.OnLoad;
                        bmp.EndInit();
                    }
                    bmp.Freeze();

                    var converted = new FormatConvertedBitmap(bmp, PixelFormats.Bgra32, null, 0);
                    int stride = converted.PixelWidth * 4;
                    var pixels = new byte[stride * converted.PixelHeight];
                    converted.CopyPixels(pixels, stride, 0);

                    return AlbumColorExtractor.Extract(pixels, converted.PixelWidth, converted.PixelHeight, stride);
                });

                if (cacheKey != null)
                    StoreNpColorInCache(cacheKey, colors);

                Dispatcher.Invoke(() =>
                {
                    if (generation >= 0 && generation != _npColorGeneration) return;
                    ApplyNpExtractedColors(colors);
                });
            }
            catch
            {
                Dispatcher.Invoke(() =>
                {
                    if (generation >= 0 && generation != _npColorGeneration) return;
                    NpBgGradient.Background = Brushes.Transparent;
                });
            }
        }

        // Now Playing: Lyrics, Translation, Karaoke, LRC handling - see NpLyrics.cs
        // ─── NP Timer ───

        private bool NpShouldRenderPlaybar(TimeSpan position)
        {
            var style = ThemeManager.NpPlaybarAnimationStyle;

            // When the 60fps cycle-rendering hook is active it already drives the playbar for
            // animated/ColorMatch styles. Don't ALSO rebuild from the 50ms timer — two
            // independent drivers clearing+rebuilding the same canvas is what makes it flicker.
            // Wave is the exception: its hook tick is idle (the waveform animation draws it), so
            // the timer still maintains it.
            if (_npPlaybarCycleRendering && style != PlaybarAnimationStyle.Wave)
                return false;

            var now = DateTime.UtcNow;
            double minIntervalMs = style == PlaybarAnimationStyle.Regular ? 250 : 120;
            bool positionMoved = _npLastPlaybarRenderPosition == TimeSpan.MinValue
                                 || Math.Abs((position - _npLastPlaybarRenderPosition).TotalMilliseconds) >= 180;
            bool intervalElapsed = (now - _npLastPlaybarRenderUtc).TotalMilliseconds >= minIntervalMs;

            if (!positionMoved && !intervalElapsed)
                return false;

            _npLastPlaybarRenderPosition = position;
            _npLastPlaybarRenderUtc = now;
            return true;
        }

        private void NpUpdateTimer_Tick(object? sender, EventArgs e)
        {
            if (_player == null) return;
            if (!IsNowPlayingUiActive())
            {
                NpSuspendVisibleWork(markPendingRefresh: true);
                return;
            }

            var pos = _player.CurrentPosition;
            var total = _player.TotalDuration;

            NpTimeElapsed.Text = NpFormatTime(pos);
            NpTimeTotal.Text = NpFormatTime(total);

            // Don't update slider while user is dragging, and respect seek cooldown
            if (total.TotalSeconds > 0 && !_npIsSeeking &&
                (DateTime.UtcNow - _lastSeekTime).TotalMilliseconds > 500)
            {
                NpSeekSlider.Maximum = total.TotalSeconds;
                NpSeekSlider.ValueChanged -= NpSeekSlider_ValueChanged;
                NpSeekSlider.Value = pos.TotalSeconds;
                NpSeekSlider.ValueChanged += NpSeekSlider_ValueChanged;
            }
            else if (total.TotalSeconds > 0)
            {
                NpSeekSlider.Maximum = total.TotalSeconds;
            }

            NpUpdatePlayState();
            if (NpShouldRenderPlaybar(pos))
                NpRenderPlaybarStyle();

            // During seek cooldown, use the last seek target for lyrics to avoid NAudio position lag
            bool lyricSeekCooldown = (DateTime.UtcNow - _lastSeekTime).TotalMilliseconds < 500;
            var lyricPos = lyricSeekCooldown ? _lastSeekTargetPosition : pos;
            NpUpdateLyricHighlight(lyricPos);

            // Drive the highlight from the render loop while timed lyrics are playing (tighter sync
            // than this 50ms tick); the loop self-disables when these conditions stop holding.
            NpEnsureLyricRendering(
                !_npLyricsHidden
                && _npCurrentLyrics.IsTimed
                && _npLyricTextBlocks.Count > 0
                && _player.IsPlaying);

            if (_npLyricsNeedCatchUp)
            {
                // Sticky catch-up keeps retrying each tick until a line successfully highlights.
                // CRITICAL: only reset _npCurrentLyricIndex and re-call the highlighter when the
                // first call (above) didn't already advance the index. Otherwise the second call
                // restarts the in-flight DoubleAnimation/ColorAnimation for the line that just
                // changed — which produces a visibly jittery transition.
                bool firstCallAdvanced = _npCurrentLyricIndex >= 0;
                if (!firstCallAdvanced)
                {
                    _npCurrentLyricIndex = -1;
                    NpUpdateLyricHighlight(lyricPos);
                }

                double firstLineTime = _npCurrentLyrics.IsTimed && _npCurrentLyrics.Lines.Count > 0
                    ? _npCurrentLyrics.Lines[0].Time.TotalSeconds
                    : 0;
                bool beforeFirstLine = lyricPos.TotalSeconds + 0.12 < firstLineTime;

                if (!_npCurrentLyrics.IsTimed
                    || _npCurrentLyricIndex >= 0
                    || (_npCurrentLyrics.Lines.Count > 0 && _npLyricTextBlocks.Count > 0 && beforeFirstLine))
                {
                    _npLyricsNeedCatchUp = false;
                }
            }

            // Safety net: recover a highlight that has lagged/frozen out of sync (count mismatch
            // or a stuck index). No-op while the highlight is healthy.
            NpEnsureLyricSyncHealthy(lyricPos);
            // NOTE: no periodic "defensive" re-highlight here. NpUpdateLyricHighlight above
            // already re-evaluates the active line every tick and only animates on a real
            // line change. The old defensive block compared the position to the *current*
            // line's start time + 500ms — true for almost every line's whole duration — so it
            // forced a full re-highlight (+ auto-scroll) ~20x/sec, which is what made the
            // lyrics visibly flash and the NP screen lag.
        }

        private SolidColorBrush? _npControlTintBrush;
        private System.Windows.Media.Color _npControlTintColor;

        private void NpUpdatePlayState()
        {
            bool playing = _player?.IsPlaying == true;
            // Hold the playing-state icon through an in-flight skip load so it doesn't flicker to
            // "paused" before the next track starts. The visualizer block below still keys off the
            // real playing state.
            bool iconPlaying = playing || _pendingPlaybackVisual;
            NpPlayPath.Visibility = iconPlaying ? Visibility.Collapsed : Visibility.Visible;
            NpPausePath.Visibility = iconPlaying ? Visibility.Visible : Visibility.Collapsed;

            // Ensure play/pause buttons have correct color (respects color match). Cache the
            // brush so the 50ms tick doesn't allocate a new one every time — only rebuild when
            // the album color actually changes.
            if (_npColorMatchEnabled && _npAlbumSecondary != default)
            {
                if (_npControlTintBrush == null || _npControlTintColor != _npAlbumSecondary)
                {
                    _npControlTintColor = _npAlbumSecondary;
                    _npControlTintBrush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(
                        (byte)Math.Min(255, _npAlbumSecondary.R + 100),
                        (byte)Math.Min(255, _npAlbumSecondary.G + 100),
                        (byte)Math.Min(255, _npAlbumSecondary.B + 100)));
                    NpPlayPath.Fill = _npControlTintBrush;
                    NpPausePath.Fill = _npControlTintBrush;
                }
            }

            // Start NP visualizer when playing (leave frozen on pause, don't tear down). Only
            // evaluate while actually playing — avoids a per-tick work-state allocation otherwise.
            if (playing &&
                NowPlayingWorkCoordinator.ResolveVisualizerOwner(CaptureNowPlayingWorkState()) == VisualizerSurfaceOwner.NowPlaying)
            {
                NpStartVisualizer();
            }
        }

        private static string NpFormatTime(TimeSpan ts) =>
            ts.TotalHours >= 1 ? ts.ToString(@"h\:mm\:ss") : ts.ToString(@"m\:ss");

        // ─── Frame-synced lyric highlight ───
        //
        // The 50ms update timer (NpUpdateTimer_Tick) still calls NpUpdateLyricHighlight as a
        // fallback, but a DispatcherTimer runs at a lower scheduling priority than rendering and
        // quantizes the highlight to 50ms steps. While timed lyrics are playing we ALSO drive the
        // highlight from CompositionTarget.Rendering, which fires per frame at render priority and
        // uses the live audio position — so the active line flips in lock-step with the audio and
        // keeps tracking even when heavy background FX load the UI thread. NpUpdateLyricHighlight
        // early-returns when the active line hasn't changed, so the per-frame cost is negligible.

        private void NpEnsureLyricRendering(bool enable)
        {
            if (enable && !_npLyricRendering)
            {
                _npLyricRendering = true;
                CompositionTarget.Rendering += NpLyricRendering_Tick;
                NpApplyLyricFxThrottle(true);
            }
            else if (!enable && _npLyricRendering)
            {
                _npLyricRendering = false;
                CompositionTarget.Rendering -= NpLyricRendering_Tick;
                NpApplyLyricFxThrottle(false);
            }
        }

        private void NpLyricRendering_Tick(object? sender, EventArgs e)
        {
            if (_player == null || !IsNowPlayingUiActive() || _npLyricsHidden
                || !_npCurrentLyrics.IsTimed || _npLyricTextBlocks.Count == 0)
            {
                NpEnsureLyricRendering(false);
                return;
            }
            // Paused: position is stable, so the 50ms timer's single update suffices — no need to
            // re-evaluate every frame.
            if (_player.IsPlaying != true) return;

            bool lyricSeekCooldown = (DateTime.UtcNow - _lastSeekTime).TotalMilliseconds < 500;
            var lyricPos = lyricSeekCooldown ? _lastSeekTargetPosition : _player.CurrentPosition;
            NpUpdateLyricHighlight(lyricPos);
        }

        // While the per-frame lyric loop is live, halve the decorative Color Drift gradient timer's
        // UI-thread wakeups (50→100ms). The drift is a very slow rotation, so the change is
        // imperceptible, but it frees UI-thread time for the lyric highlight under load.
        private void NpApplyLyricFxThrottle(bool throttle)
        {
            if (_npBgAnimTimer == null) return;
            _npBgAnimTimer.Interval = TimeSpan.FromMilliseconds(throttle ? 100 : 50);
        }

        private void NpEnsurePlaybarCycleRendering(bool enable)
        {
            if (enable && !_npPlaybarCycleRendering)
            {
                _npPlaybarCycleRendering = true;
                CompositionTarget.Rendering += NpPlaybarCycleRendering_Tick;
            }
            else if (!enable && _npPlaybarCycleRendering)
            {
                _npPlaybarCycleRendering = false;
                CompositionTarget.Rendering -= NpPlaybarCycleRendering_Tick;
            }
        }

        private void NpPlaybarCycleRendering_Tick(object? sender, EventArgs e)
        {
            if (!_npVisible || !IsNowPlayingUiActive() || !AnimationPolicy.IsMotionAllowed(AnimationArea.Playbar)) return;
            // Render if ColorMatch is cycling colors or the style animates. Wave is driven by the
            // waveform animation instead, so skip it here.
            var style = ThemeManager.NpPlaybarAnimationStyle;
            if (style == PlaybarAnimationStyle.Wave) return;
            if (_npColorMatchEnabled || IsAnimatedPlaybarStyle(style))
                NpRenderPlaybarStyle();
        }

        private void NpRenderPlaybarStyle()
        {
            if (NpPlaybarAnimCanvas == null)
                return;

            var style = ThemeManager.NpPlaybarAnimationStyle;
            // Drive a continuous 60fps render when the style itself animates (Wave/Gradient/
            // Spectrum/Glow) OR when ColorMatch is cycling colors. Otherwise the only driver is the
            // throttled 50ms update timer, which makes animated styles look choppy.
            bool playbarMotion = AnimationPolicy.IsMotionAllowed(AnimationArea.Playbar);
            bool animated = IsAnimatedPlaybarStyle(style) || (_npColorMatchEnabled && playbarMotion);
            NpEnsurePlaybarCycleRendering(_npVisible && animated && playbarMotion);

            // Wave style uses the full waveform background animation, not the small playbar canvas
            if (style == PlaybarAnimationStyle.Wave)
            {
                NpPlaybarAnimCanvas.Children.Clear();
                if (_waveformBaseData.Length == 0)
                    GenerateWaveformData();
                if (!_waveformAnimActive)
                    StartWaveformAnimation();
                return;
            }

            NpWaveformCanvas.Children.Clear();

            Color accent = Colors.CornflowerBlue;
            Color secondary = Colors.CornflowerBlue;
            Color tertiary = Colors.CornflowerBlue;
            bool npColormatch = false;
            if (NpGetEffectiveColorMatchPalette(out var primary, out var paletteSecondary, out var paletteTertiary, out _, out _))
            {
                // ColorMatch ON: album-or-neutral colors only — never the theme accent.
                accent = EnsureMinLuminance(primary, 150);
                secondary = paletteSecondary != default ? EnsureMinLuminance(paletteSecondary, 115) : accent;
                tertiary = paletteTertiary != default ? EnsureMinLuminance(paletteTertiary, 100) : secondary;
                npColormatch = true;
            }
            else
            {
                // ColorMatch OFF: follow the playbar theme.
                if (TryFindResource("PlaybarAccentColor") is SolidColorBrush playbarAccent)
                    accent = playbarAccent.Color;
                else if (TryFindResource("AccentColor") is SolidColorBrush accentBrush)
                    accent = accentBrush.Color;
                if (TryFindResource("PlaybarSecondaryColor") is SolidColorBrush secBrush)
                    secondary = secBrush.Color;
                else
                    secondary = accent;
                if (TryFindResource("PlaybarTertiaryColor") is SolidColorBrush terBrush)
                    tertiary = terBrush.Color;
                else
                    tertiary = secondary;
            }

            // When colormatch is active, cycle through palette colors over time
            if (npColormatch)
                (accent, secondary) = InterpolatePlaybarCycleColors(accent, secondary, tertiary, DateTime.UtcNow.TimeOfDay.TotalSeconds);

            // The slider now renders ON TOP of the bar canvas (so the playhead dot is above the bar).
            // That means the slider's own DecreaseRepeatButton fill (bound to PlaybarAccentColor) is the
            // visible played bar. Scope the (cycled) album colors onto the NP playbar host under
            // ColorMatch so that fill + thumb show album colors; clear the scope otherwise so it follows
            // the theme. This keeps the colormatch look while putting the dot above the bar.
            if (NpPlaybarHost != null)
            {
                if (npColormatch)
                {
                    var accentBrush = new SolidColorBrush(accent); accentBrush.Freeze();
                    var secondaryBrush = new SolidColorBrush(secondary); secondaryBrush.Freeze();
                    NpPlaybarHost.Resources["PlaybarAccentColor"] = accentBrush;
                    NpPlaybarHost.Resources["PlaybarSecondaryColor"] = secondaryBrush;
                }
                else
                {
                    NpPlaybarHost.Resources.Remove("PlaybarAccentColor");
                    NpPlaybarHost.Resources.Remove("PlaybarSecondaryColor");
                }
            }

            double pct = NpSeekSlider.Maximum > 0 ? NpSeekSlider.Value / NpSeekSlider.Maximum : 0;
            double phase = AnimationPolicy.IsMotionAllowed(AnimationArea.Playbar) ? DateTime.UtcNow.TimeOfDay.TotalSeconds : 0;
            RenderPlaybar(NpPlaybarAnimCanvas, pct, accent, secondary, style, phase);
        }

        // ─── NP Control Events ───

        private void NpSaveLyricsToLrc(string audioFilePath)
        {
            if (!_npCurrentLyrics.HasLyrics) return;
            string lrcPath = System.IO.Path.Combine(
                System.IO.Path.GetDirectoryName(audioFilePath)!,
                System.IO.Path.GetFileNameWithoutExtension(audioFilePath) + ".lrc");

            var lines = new System.Collections.Generic.List<string>();
            if (!string.IsNullOrEmpty(_npCurrentLyrics.Title))
                lines.Add($"[ti:{_npCurrentLyrics.Title}]");
            if (!string.IsNullOrEmpty(_npCurrentLyrics.Artist))
                lines.Add($"[ar:{_npCurrentLyrics.Artist}]");
            if (!string.IsNullOrEmpty(_npCurrentLyrics.Album))
                lines.Add($"[al:{_npCurrentLyrics.Album}]");

            if (_npCurrentLyrics.IsTimed)
            {
                foreach (var line in _npCurrentLyrics.Lines)
                {
                    int min = (int)line.Time.TotalMinutes;
                    int sec = line.Time.Seconds;
                    int cs = (int)(line.Time.Milliseconds / 10.0);
                    string normalized = System.Text.RegularExpressions.Regex.Replace(line.Text, @"\s+", " ").Trim();
                    lines.Add($"[{min:D2}:{sec:D2}.{cs:D2}]{normalized}");
                }
            }
            else
            {
                foreach (var line in _npCurrentLyrics.Lines)
                    lines.Add(System.Text.RegularExpressions.Regex.Replace(line.Text, @"\s+", " ").Trim());
            }

            File.WriteAllLines(lrcPath, lines);
        }

        private void NpSaveLyrics_Click(object sender, RoutedEventArgs e)
        {
            if (NpSaveLyricsBtn?.ContextMenu != null)
                NpSaveLyricsBtn.ContextMenu.IsOpen = true;
        }

        private void NpSaveLyricsMenu_Opened(object sender, RoutedEventArgs e)
        {
            bool hasLyrics = _player.CurrentFile != null && _npCurrentLyrics.HasLyrics;
            if (NpSaveLyricsNowItem != null)
                NpSaveLyricsNowItem.IsEnabled = hasLyrics;
            if (NpAutoSaveLyricsToggleItem != null)
                NpAutoSaveLyricsToggleItem.IsChecked = ThemeManager.NpAutoSaveLyricsEnabled;
            if (NpAvoidCensoredLyricsItem != null)
                NpAvoidCensoredLyricsItem.IsChecked = ThemeManager.LyricsAvoidCensored;
            if (NpSaveAllLyricsItem != null)
                NpSaveAllLyricsItem.IsEnabled = _player.CurrentFile != null && _files.Count > 0;
        }

        private void NpAvoidCensoredLyricsToggle_Click(object sender, RoutedEventArgs e)
        {
            ThemeManager.LyricsAvoidCensored = !ThemeManager.LyricsAvoidCensored;
            ThemeManager.SavePlayOptions();
        }

        private void NpSaveLyricsNow_Click(object sender, RoutedEventArgs e)
        {
            if (_player.CurrentFile == null || !_npCurrentLyrics.HasLyrics) return;
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

        private void NpAutoSaveLyricsToggle_Click(object sender, RoutedEventArgs e)
        {
            ThemeManager.NpAutoSaveLyricsEnabled = !ThemeManager.NpAutoSaveLyricsEnabled;
            ThemeManager.SavePlayOptions();
            NpUpdateSaveLyricsButton();
        }

        private async void NpSaveAllLyricsNow_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_player.CurrentFile == null || _files.Count == 0) return;
                string currentFolder = System.IO.Path.GetDirectoryName(_player.CurrentFile)!;
                var folderFiles = _files.Where(f =>
                    string.Equals(System.IO.Path.GetDirectoryName(f.FilePath), currentFolder, StringComparison.OrdinalIgnoreCase))
                    .ToList();
                if (folderFiles.Count == 0) return;

                NpShowLyricStatus($"Saving lyrics for {folderFiles.Count} songs...");
                int savedCount = 0;

                await Task.Run(() =>
                {
                    foreach (var file in folderFiles)
                    {
                        string lrcPath = System.IO.Path.Combine(
                            currentFolder,
                            System.IO.Path.GetFileNameWithoutExtension(file.FilePath) + ".lrc");
                        if (File.Exists(lrcPath)) continue;

                        try
                        {
                            var lyrics = LyricService.GetLyrics(file.FilePath, _npLyricProvider);
                            if (lyrics.HasLyrics)
                            {
                                var lines = new System.Collections.Generic.List<string>();
                                if (!string.IsNullOrEmpty(lyrics.Title))
                                    lines.Add($"[ti:{lyrics.Title}]");
                                if (!string.IsNullOrEmpty(lyrics.Artist))
                                    lines.Add($"[ar:{lyrics.Artist}]");
                                if (!string.IsNullOrEmpty(lyrics.Album))
                                    lines.Add($"[al:{lyrics.Album}]");

                                if (lyrics.IsTimed)
                                {
                                    foreach (var line in lyrics.Lines)
                                    {
                                        int min = (int)line.Time.TotalMinutes;
                                        int sec = line.Time.Seconds;
                                        int cs = (int)(line.Time.Milliseconds / 10.0);
                                        string normalized = System.Text.RegularExpressions.Regex.Replace(line.Text, @"\s+", " ").Trim();
                                        lines.Add($"[{min:D2}:{sec:D2}.{cs:D2}]{normalized}");
                                    }
                                }
                                else
                                {
                                    foreach (var line in lyrics.Lines)
                                        lines.Add(System.Text.RegularExpressions.Regex.Replace(line.Text, @"\s+", " ").Trim());
                                }

                                File.WriteAllLines(lrcPath, lines);
                                savedCount++;
                            }
                        }
                        catch { /* best-effort per file */ }
                    }
                });

                NpShowLyricStatus($"Saved {savedCount} lyric files");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[NpSaveAllLyricsNow_Click] {ex}");
                NpShowLyricStatus("Save failed");
            }
        }

        private void NpUpdateSaveLyricsButton()
        {
            if (NpSaveLyricsBtn == null || NpSaveLyricsIcon == null) return;
            bool hasLyrics = _player?.CurrentFile != null && _npCurrentLyrics.HasLyrics;
            NpSaveLyricsBtn.IsEnabled = hasLyrics;
            if (!hasLyrics)
            {
                NpSaveLyricsIcon.Stroke = (Brush)FindResource("TextMuted");
                return;
            }
            if (ThemeManager.NpAutoSaveLyricsEnabled)
            {
                NpSaveLyricsIcon.Stroke = (Brush)FindResource("AccentColor");
            }
            else
            {
                NpSaveLyricsIcon.Stroke = (Brush)FindResource("TextMuted");
            }
        }

        private void NpPlayPause_Click(object sender, RoutedEventArgs e)
        {
            if (_player == null) return;
            if (_player.IsPlaying) _player.Pause(); else _player.Resume();
            NpUpdatePlayState();
        }

        private void NpPrev_Click(object sender, RoutedEventArgs e) =>
            PrevTrack_Click(sender, e);

        private void NpNext_Click(object sender, RoutedEventArgs e) =>
            NextTrack_Click(sender, e);

        private void NpShuffle_Click(object sender, RoutedEventArgs e)
        {
            _shuffleMode = !_shuffleMode;
            UpdateShuffleUI();
            NpUpdateShuffleIcon();
        }

        private void NpLoop_Click(object sender, RoutedEventArgs e)
        {
            CycleLoopMode();
        }

        private void NpUpdateShuffleIcon()
        {
            if (NpShuffleIcon == null) return;
            var activeColor = NpGetIconBrush(true);
            var inactiveColor = NpGetIconBrush(false);
            NpShuffleIcon.Stroke = _shuffleMode ? activeColor : inactiveColor;
            NpShuffleBtn.ToolTip = _shuffleMode ? "Shuffle: ON" : "Shuffle: OFF";
            NpSetToggleBg(NpShuffleBtn, _shuffleMode);
        }

        private void NpLyricSource_Click(object sender, RoutedEventArgs e)
        {
            NpCancelLyricsWork(invalidateVersion: true);
            _npProviderIndex = (_npProviderIndex + 1) % NpLyricProviders.Length;
            _npLyricProvider = NpLyricProviders[_npProviderIndex].Provider;
            NpProviderBtn.ToolTip = $"Provider: {NpLyricProviders[_npProviderIndex].Name}";

            if (_player.CurrentFile != null)
            {
                var currentFile = _files.FirstOrDefault(f =>
                    string.Equals(f.FilePath, _player.CurrentFile, StringComparison.OrdinalIgnoreCase));
                if (currentFile != null)
                    ObserveUiTask(NpLoadLyricsAsync(currentFile.FilePath, currentFile.Artist, currentFile.Title, currentFile.Album, currentFile.DurationSeconds), nameof(NpLoadLyricsAsync));
            }
        }

        private void NpVolumeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_player != null)
            {
                _player.Volume = (float)(NpVolumeSlider.Value / 100.0);
                VolumeSlider.Value = NpVolumeSlider.Value;
            }
            NpUpdateVolumeIcon();
        }

        private void NpVolumeIcon_Click(object sender, MouseButtonEventArgs e)
        {
            if (!_isMuted)
            {
                _preMuteVolume = NpVolumeSlider.Value;
                NpVolumeSlider.Value = 0;
                VolumeSlider.Value = 0;
                _isMuted = true;
            }
            else
            {
                NpVolumeSlider.Value = _preMuteVolume;
                VolumeSlider.Value = _preMuteVolume;
                _isMuted = false;
            }
            NpUpdateVolumeIcon();
        }

        private void NpUpdateVolumeIcon()
        {
            double vol = NpVolumeSlider.Value;
            string pathData;
            if (vol <= 0 || _isMuted)
                pathData = "M 2,5 L 5,5 L 9,2 L 9,14 L 5,11 L 2,11 Z M 12,5 L 16,11 M 16,5 L 12,11"; // mute X
            else if (vol <= 33)
                pathData = "M 2,5 L 5,5 L 9,2 L 9,14 L 5,11 L 2,11 Z"; // speaker only
            else if (vol <= 66)
                pathData = "M 2,5 L 5,5 L 9,2 L 9,14 L 5,11 L 2,11 Z M 11,5 Q 13,8 11,11"; // one wave
            else
                pathData = "M 2,5 L 5,5 L 9,2 L 9,14 L 5,11 L 2,11 Z M 11,5 Q 13,8 11,11 M 13,3 Q 16,8 13,13"; // two waves
            try { NpVolumeIconPath.Data = Geometry.Parse(pathData); } catch { }
        }

        // ─── NP Visualizer Resize ───

        private void NpVizResize_MouseDown(object sender, MouseButtonEventArgs e)
        {
            _npVizResizing = true;
            _npVizResizeStartY = e.GetPosition(this).Y;
            _npVizResizeStartH = _npVizBarHeight;
            ((UIElement)sender).CaptureMouse();
            e.Handled = true;
        }

        private void NpVizResize_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
        {
            if (!_npVizResizing) return;
            double deltaY = _npVizResizeStartY - e.GetPosition(this).Y;
            double newHeight = Math.Clamp(_npVizResizeStartH + deltaY, 40, 400);
            _npVizBarHeight = newHeight;
            if (_npVizPlacement == 0)
                NpVizBar.Height = newHeight;
            else
                NpUnderCoverVizRow.Height = new GridLength(newHeight);
            e.Handled = true;
        }

        private void NpVizResize_MouseUp(object sender, MouseButtonEventArgs e)
        {
            _npVizResizing = false;
            ((UIElement)sender).ReleaseMouseCapture();
            _npVizSize = (int)Math.Round(_npVizBarHeight);
            NpSavePreferences();
            e.Handled = true;
        }

        private void NpSeekSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            // During drag, only update visual position — actual seek happens on release
            NpRenderPlaybarStyle();
        }

        private void NpSeekSlider_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            // Detect thumb click vs track click by checking if the mouse is over the thumb
            var thumb = FindVisualChild<System.Windows.Controls.Primitives.Thumb>(NpSeekSlider);
            if (thumb != null)
            {
                var pos = e.GetPosition(thumb);
                if (pos.X >= -4 && pos.X <= thumb.ActualWidth + 4 &&
                    pos.Y >= -4 && pos.Y <= thumb.ActualHeight + 4)
                {
                    _npIsSeeking = true;
                    return; // Let the Slider handle thumb drag normally
                }
            }

            // Track click — compute position and seek immediately to avoid snap-back races
            if (NpSeekSlider.ActualWidth > 0 && _player != null && _player.TotalDuration.TotalSeconds > 0)
            {
                double ratio = Math.Clamp(e.GetPosition(NpSeekSlider).X / NpSeekSlider.ActualWidth, 0, 1);
                double posSec = ratio * _player.TotalDuration.TotalSeconds;

                NpSeekSlider.Value = posSec;
                _player.Seek(posSec);
                _lastSeekTime = DateTime.UtcNow;
                _lastSeekTargetPosition = TimeSpan.FromSeconds(posSec);
                _npIsSeeking = true;

                // Sync main slider
                if (SeekSlider.Maximum > 0)
                    SeekSlider.Value = posSec / _player.TotalDuration.TotalSeconds * SeekSlider.Maximum;

                // Force immediate lyric re-sync to seek target
                _npCurrentLyricIndex = -1;
                NpUpdateLyricHighlight(_lastSeekTargetPosition);
                NpRenderPlaybarStyle();
            }
            e.Handled = true;
        }

        private void NpSeekSlider_MouseUp(object sender, MouseButtonEventArgs e)
        {
            // For track clicks, the seek already happened on mouse down — just clear the flag.
            // For thumb drags, DragCompleted handles the seek and clears the flag.
            var thumb = FindVisualChild<System.Windows.Controls.Primitives.Thumb>(NpSeekSlider);
            if (thumb == null || !thumb.IsDragging)
            {
                _npIsSeeking = false;
                NpRenderPlaybarStyle();
            }
        }

        private void NpSeekSlider_DragCompleted(object sender, DragCompletedEventArgs e)
        {
            if (_player != null && _player.TotalDuration.TotalSeconds > 0 && NpSeekSlider.Maximum > 0)
            {
                _player.Seek(NpSeekSlider.Value);
                _lastSeekTime = DateTime.UtcNow;
                _lastSeekTargetPosition = TimeSpan.FromSeconds(NpSeekSlider.Value);

                // Sync main slider
                SeekSlider.Value = NpSeekSlider.Value / NpSeekSlider.Maximum * SeekSlider.Maximum;

                // Force immediate lyric re-sync to seek target
                _npCurrentLyricIndex = -1;
                NpUpdateLyricHighlight(_lastSeekTargetPosition);
            }
            _npIsSeeking = false;
            NpRenderPlaybarStyle();
        }

        private void NpSeekSlider_MouseMove(object sender, MouseEventArgs e)
        {
            UpdateSeekTooltip(NpSeekSlider, e);
        }

        private void NpBack_Click(object sender, RoutedEventArgs e) => ToggleNowPlaying(false);

        // ─── NP Queue ───

        private void NpQueue_Click(object sender, RoutedEventArgs e)
        {
            var queueWindow = new QueueWindow(_queue) { Owner = this, UpNext = GetUpNextTracks() };
            if (NpGetEffectiveColorMatchPalette(out var primary, out var secondary, out var tertiary, out var background, out _))
                queueWindow.ApplyColorMatch(primary, secondary, tertiary, background);
            queueWindow.ShowDialog();
            NpUpdateQueuePopup();
        }

        private void NpUpdateQueuePopup()
        {
            NpQueuePopupContent.Children.Clear();

            var upcoming = new List<(int idx, string text)>();
            var tracks = GetUpcomingTracks(5);

            for (int i = 0; i < tracks.Count; i++)
            {
                var track = tracks[i];
                string title = !string.IsNullOrWhiteSpace(track.Title) ? track.Title : track.FileName ?? "Unknown";
                string artist = !string.IsNullOrWhiteSpace(track.Artist) ? track.Artist : "";
                upcoming.Add((i + 1, string.IsNullOrEmpty(artist) ? title : $"{title} — {artist}"));
            }

            if (upcoming.Count == 0)
            {
                NpQueuePopupContent.Children.Add(new TextBlock
                {
                    Text = "No upcoming tracks.",
                    FontSize = 12,
                    FontFamily = new FontFamily("Segoe UI"),
                    Foreground = (Brush)FindResource("TextMuted"),
                    Margin = new Thickness(0, 4, 0, 0)
                });
                return;
            }

            foreach (var (idx, text) in upcoming)
            {
                var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 3, 0, 3) };
                var idxBlock = new TextBlock
                {
                    Text = $"{idx}.",
                    FontSize = 11,
                    FontWeight = FontWeights.SemiBold,
                    FontFamily = new FontFamily("Segoe UI"),
                    Margin = new Thickness(0, 0, 8, 0),
                    Width = 18,
                    TextAlignment = TextAlignment.Right
                };
                idxBlock.SetResourceReference(TextBlock.ForegroundProperty, "AccentColor");
                row.Children.Add(idxBlock);

                var textBlock = new TextBlock
                {
                    Text = text,
                    FontSize = 12,
                    FontFamily = new FontFamily("Segoe UI"),
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    MaxWidth = 260
                };
                textBlock.SetResourceReference(TextBlock.ForegroundProperty, "TextPrimary");
                row.Children.Add(textBlock);
                NpQueuePopupContent.Children.Add(row);
            }
        }

        // ─── NP Auto-Play Toggle ───

        private void NpAutoPlay_Click(object sender, RoutedEventArgs e)
        {
            ThemeManager.AutoPlayNext = !ThemeManager.AutoPlayNext;
            ThemeManager.SavePlayOptions();
            NpUpdateAutoPlayIcon();
        }

        // ─── Crossfade popup ───

        private void NpCrossfade_Click(object sender, RoutedEventArgs e)
        {
            if (NpCrossfadePopup == null) return;
            if (NpCrossfadePopup.IsOpen)
            {
                NpCrossfadePopup.IsOpen = false;
                return;
            }

            // Populate curve combo once
            if (NpCrossfadeCurveCombo.Items.Count == 0)
            {
                var curves = new[]
                {
                    CrossfadeType.EqualPower,
                    CrossfadeType.Linear,
                    CrossfadeType.Natural,
                    CrossfadeType.Sequential,
                    CrossfadeType.SmoothStep,
                    CrossfadeType.FastFade,
                    CrossfadeType.SlowBlend
                };
                foreach (var c in curves)
                    NpCrossfadeCurveCombo.Items.Add(c.ToString());
            }

            NpCrossfadeDurationSlider.Value = ThemeManager.CrossfadeDuration;
            NpCrossfadeDurationLabel.Text = $"{ThemeManager.CrossfadeDuration}s";
            NpCrossfadeCurveCombo.SelectedItem = ThemeManager.CrossfadeCurve.ToString();
            NpUpdateCrossfadeToggleText();
            NpUpdateCrossfadeSkipText();
            NpUpdateCrossfadeIcon();
            NpCrossfadePopup.IsOpen = true;
        }

        private void NpCrossfadeToggle_Click(object sender, RoutedEventArgs e)
        {
            ThemeManager.Crossfade = !ThemeManager.Crossfade;
            // Crossfade and gapless are mutually exclusive
            if (ThemeManager.Crossfade && ThemeManager.GaplessEnabled)
                ThemeManager.GaplessEnabled = false;
            ThemeManager.SavePlayOptions();
            NpUpdateCrossfadeToggleText();
            NpUpdateCrossfadeIcon();
        }

        private void NpCrossfadeDuration_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            int dur = (int)Math.Round(NpCrossfadeDurationSlider.Value);
            ThemeManager.CrossfadeDuration = dur;
            ThemeManager.SavePlayOptions();
            if (NpCrossfadeDurationLabel != null)
                NpCrossfadeDurationLabel.Text = $"{dur}s";
        }

        private void NpCrossfadeCurve_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (NpCrossfadeCurveCombo?.SelectedItem is string s
                && Enum.TryParse<CrossfadeType>(s, out var curve))
            {
                ThemeManager.CrossfadeCurve = curve;
                ThemeManager.SavePlayOptions();
            }
        }

        private void NpCrossfadeSkipToggle_Click(object sender, RoutedEventArgs e)
        {
            ThemeManager.CrossfadeOnManualSkip = !ThemeManager.CrossfadeOnManualSkip;
            ThemeManager.SavePlayOptions();
            NpUpdateCrossfadeSkipText();
        }

        private void NpUpdateCrossfadeToggleText()
        {
            if (NpCrossfadeToggleText == null) return;
            NpCrossfadeToggleText.Text = ThemeManager.Crossfade ? "Crossfade: ON" : "Crossfade: OFF";
            NpCrossfadeToggleText.Foreground = ThemeManager.Crossfade
                ? System.Windows.Media.Brushes.White
                : (System.Windows.Media.Brush)FindResource("TextMuted");
        }

        private void NpUpdateCrossfadeSkipText()
        {
            if (NpCrossfadeSkipText == null) return;
            NpCrossfadeSkipText.Text = ThemeManager.CrossfadeOnManualSkip
                ? "Crossfade on manual skip: ON"
                : "Crossfade on manual skip: OFF";
        }

        private void NpUpdateAutoPlayIcon()
        {
            var active = NpGetIconBrush(true);
            var inactive = NpGetIconBrush(false);
            var brush = ThemeManager.AutoPlayNext ? active : inactive;
            NpAutoPlayIcon.Fill = brush;
            NpAutoPlayIcon.Stroke = brush;
            NpAutoPlayBtn.ToolTip = ThemeManager.AutoPlayNext ? "Auto-play: ON" : "Auto-play: OFF";
            NpSetToggleBg(NpAutoPlayBtn, ThemeManager.AutoPlayNext);
        }

        private void NpUpdateCrossfadeIcon()
        {
            if (NpCrossfadeIcon == null) return;
            var active = NpGetIconBrush(true);
            var inactive = NpGetIconBrush(false);
            var brush = ThemeManager.Crossfade ? active : inactive;
            NpCrossfadeIcon.Stroke = brush;
            NpCrossfadeBtn.ToolTip = ThemeManager.Crossfade ? "Crossfade: ON" : "Crossfade: OFF";
            NpSetToggleBg(NpCrossfadeBtn, ThemeManager.Crossfade);
        }

        // ─── NP Visualizer Toggle ───

        private void NpVisualizerToggle_Click(object sender, RoutedEventArgs e)
        {
            if (!_npPrefsLoaded)
                NpLoadPreferences();
            NpSaveActiveLayoutProfile(_npLayoutProfileIsFullscreen, _npLayoutProfileVisualizerEnabled);

            _npVisualizerEnabled = !_npVisualizerEnabled;
            NpEnsureLayoutProfileForCurrentWindowState();
            NpApplyVizPlacement();
            NpUpdateVisualizerIcon();

            if (_npVisualizerEnabled && _player.IsPlaying)
                NpStartVisualizer();
            else
                NpStopVisualizer();

            NpApplyFullscreenScaling(WindowState == WindowState.Maximized);

            NpSavePreferences();
        }

        private void NpUpdateVisualizerIcon()
        {
            var active = NpGetIconBrush(true);
            var inactive = NpGetIconBrush(false);
            NpVisualizerIcon.Stroke = _npVisualizerEnabled ? active : inactive;
            NpVisualizerBtn.ToolTip = _npVisualizerEnabled ? "Visualizer: ON" : "Visualizer: OFF";
            NpSetToggleBg(NpVisualizerBtn, _npVisualizerEnabled);
        }

        private void NpUpdateVizPlacementIcon()
        {
            NpVizPlacementIcon.Stroke = NpGetIconBrush(false);
        }

        private bool _npVizRedirected; // true when NP owns the visualizer pipeline

        private void NpStartVisualizer()
        {
            if (NowPlayingWorkCoordinator.ResolveVisualizerOwner(CaptureNowPlayingWorkState()) != VisualizerSurfaceOwner.NowPlaying) return;
            if (_npVizRedirected) return; // already redirected
            _npVizRedirected = true;

            // Clear cached elements so they rebuild on the active canvas via VizTarget
            ClearVisualizerCaches();
            NpVisualizerCanvas.Children.Clear();
            NpUnderCoverVizCanvas.Children.Clear();
            if (!_visualizerActive)
            {
                _mainVizWasActive = false;
                StartVisualizer();
            }
            else
            {
                _mainVizWasActive = true;
                // Clear main canvas elements since VizTarget now points to NP canvas
                VisualizerCanvas.Children.Clear();
            }
        }

        private void NpStopVisualizer()
        {
            if (!_npVizRedirected) return;
            _npVizRedirected = false;

            ClearVisualizerCaches();
            NpVisualizerCanvas.Children.Clear();
            NpUnderCoverVizCanvas.Children.Clear();
            if (!_mainVizWasActive)
            {
                // Main visualizer was not running before NP took over, stop it
                StopVisualizer();
            }
            else if (!_npVisible)
            {
                // NP is closing — restore main visualizer
                VisualizerCanvas.Children.Clear();
            }
            else
            {
                // NP still visible but NP viz toggled off — keep main viz paused
                StopVisualizer();
                // Keep _mainVizWasActive true so it restores when NP closes
            }
        }

        private void ClearVisualizerCaches()
        {
            _vizBars = null;
            _vizBrushes = null;
            _vizMirrorBars = null;
            _vizMirrorBrushes = null;
            _particles = null;
            _particleElements = null;
            _particleBrushes = null;
            _circleElements = null;
            _circleBrushes = null;
            _scopeLine = null;
            _vuBlocks = null;
            _vuBrushes = null;
        }

        // ─── NP Visualizer Style Picker ───

        private void NpVisualizerStyle_Click(object sender, RoutedEventArgs e)
        {
            // Toggle: if already open, close it
            if (NpVizStylePopup.IsOpen)
            {
                NpVizStylePopup.IsOpen = false;
                return;
            }

            NpVizStyleMenu.Children.Clear();
            for (int i = 0; i < _vizStyleNames.Length; i++)
            {
                int idx = i;
                var tb = new TextBlock
                {
                    Text = (_npVisualizerStyle == i ? "● " : "   ") + _vizStyleNames[i],
                    FontSize = 12,
                    FontFamily = new System.Windows.Media.FontFamily("Segoe UI"),
                    Foreground = (Brush)FindResource(_npVisualizerStyle == i ? "TextPrimary" : "TextSecondary"),
                    Padding = new Thickness(10, 5, 10, 5),
                    Cursor = System.Windows.Input.Cursors.Hand
                };
                tb.MouseLeftButtonUp += (s, ev) =>
                {
                    NpApplyVisualizerStyle(idx);
                    NpVizStylePopup.IsOpen = false;
                };
                NpVizStyleMenu.Children.Add(tb);
            }
            NpVizStylePopup.IsOpen = true;
        }

        private void NpApplyVisualizerStyle(int style)
        {
            _npVisualizerStyle = style;
            // Apply the style to the shared visualizer renderer
            _visualizerStyle = style;
            ThemeManager.VisualizerStyle = style;
            UpdateVisualizerStyleText();
            ClearVisualizerCaches();
            NpVisualizerCanvas.Children.Clear();
            NpUnderCoverVizCanvas.Children.Clear();
            VisualizerCanvas.Children.Clear();
            NpSavePreferences();
        }

        // Now Playing: Color Match, Eyedropper, Main Color Match, Animated Background, Glow Pulse - see NpColors.cs
        // Now Playing: Layout Customization, Settings Button, Viz Placement, Lyrics-Off - see NpLayout.cs
        // ─── NP Double-click Album Art → Tag Editor ───

        private DateTime _npLastCoverClick = DateTime.MinValue;

        private void NpCoverBorder_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            // Detect double-click manually (MouseDoubleClick not available on Border)
            var now = DateTime.UtcNow;
            if ((now - _npLastCoverClick).TotalMilliseconds < 400)
            {
                // Double-click detected — open tag editor
                if (_player.CurrentFile != null)
                {
                    var file = _files.FirstOrDefault(f =>
                        string.Equals(f.FilePath, _player.CurrentFile, StringComparison.OrdinalIgnoreCase));
                    if (file != null)
                    {
                        var editor = new MetadataEditorWindow(file, this);
                        editor.ShowDialog();
                        if (editor.MetadataChanged)
                        {
                            _filteredView?.Refresh();
                            // Reload cover in case it changed
                            NpSetTrack(file);
                        }
                    }
                }
                _npLastCoverClick = DateTime.MinValue;
            }
            else
            {
                _npLastCoverClick = now;
            }
        }

        // ─── NP Next Track Preview ───

        private void NpNextTrackBorder_Click(object sender, MouseButtonEventArgs e)
        {
            _npSubCoverShowArtist = !_npSubCoverShowArtist;
            NpUpdateNextTrackPreview();
            NpSavePreferences();
        }

        private void NpUpdateNextTrackPreview()
        {
            if (_npSubCoverShowArtist)
            {
                // Show current artist
                string artist = NpSongArtist.Text;
                if (!string.IsNullOrWhiteSpace(artist))
                {
                    NpNextTrackLabel.Text = "Artist:  ";
                    NpNextTrackText.Text = artist;
                    NpNextTrackBorder.Visibility = Visibility.Visible;
                    NpNextTrackBorder.ToolTip = "Click to show Queue";
                }
                else
                {
                    NpNextTrackBorder.Visibility = Visibility.Collapsed;
                }
                return;
            }

            // Queue preview mode
            AudioFileInfo? nextTrack = null;
            try { nextTrack = GetUpcomingTracks(1).FirstOrDefault(); }
            catch { }

            if (nextTrack != null && ThemeManager.AutoPlayNext)
            {
                string name = nextTrack.Title ?? nextTrack.FileName ?? "Unknown";
                if (!string.IsNullOrWhiteSpace(nextTrack.Artist))
                    name = $"{nextTrack.Artist} — {name}";
                NpNextTrackLabel.Text = "Up next:  ";
                NpNextTrackText.Text = name;
                NpNextTrackBorder.Visibility = Visibility.Visible;
                NpNextTrackBorder.ToolTip = "Click to show Artist";
            }
            else
            {
                NpNextTrackBorder.Visibility = Visibility.Collapsed;
            }
        }

        // ═══════════════════════════════════════════
        //  EQ Profiles
        // ═══════════════════════════════════════════

        private bool _suppressEqProfileSelection;

        private void InitializeEqProfileCombo()
        {
            if (EqProfileCombo == null) return;
            EqProfileManager.Load();
            RefreshEqProfileCombo();
        }

        private void RefreshEqProfileCombo()
        {
            if (EqProfileCombo == null) return;
            _suppressEqProfileSelection = true;
            EqProfileCombo.Items.Clear();
            foreach (var p in EqProfileManager.BuiltIn)
                EqProfileCombo.Items.Add(p.Name);
            if (EqProfileManager.Custom.Count > 0)
            {
                EqProfileCombo.Items.Add(new Separator());
                foreach (var p in EqProfileManager.Custom)
                    EqProfileCombo.Items.Add(p.Name);
            }
            // Pick the profile that matches current gains, else "Flat"
            string match = "Flat";
            foreach (var p in EqProfileManager.All())
            {
                if (GainsEqual(p.Gains, ThemeManager.EqualizerGains))
                {
                    match = p.Name;
                    break;
                }
            }
            EqProfileCombo.SelectedItem = match;
            _suppressEqProfileSelection = false;
            UpdateEqDeleteButtonVisibility();
        }

        private static bool GainsEqual(float[] a, float[] b)
        {
            if (a == null || b == null || a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++)
                if (Math.Abs(a[i] - b[i]) > 0.05f) return false;
            return true;
        }

        private void EqProfile_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (_suppressEqProfileSelection) return;
            if (EqProfileCombo?.SelectedItem is not string name) return;

            var profile = EqProfileManager.FindByName(name);
            if (profile == null) return;

            // Apply gains: ThemeManager → EQ sliders → audio pipeline
            for (int i = 0; i < profile.Gains.Length && i < ThemeManager.EqualizerGains.Length; i++)
            {
                ThemeManager.EqualizerGains[i] = profile.Gains[i];
                if (i < _eqSliders.Length && _eqSliders[i] != null)
                    _eqSliders[i].Value = profile.Gains[i];   // triggers EqSlider_ValueChanged → updates band
            }
            ThemeManager.SavePlayOptions();
            UpdateEqDeleteButtonVisibility();
        }

        private void EqSaveProfile_Click(object sender, RoutedEventArgs e)
        {
            string? name = PromptForText("Save EQ Profile",
                "Enter a name for this profile:", "My Profile");
            if (string.IsNullOrWhiteSpace(name)) return;

            var saved = EqProfileManager.SaveCustom(name.Trim(), ThemeManager.EqualizerGains);
            if (saved == null)
            {
                StatusText.Text = $"Couldn't save EQ profile '{name}' — name may collide with a built-in.";
                return;
            }

            RefreshEqProfileCombo();
            _suppressEqProfileSelection = true;
            EqProfileCombo.SelectedItem = saved.Name;
            _suppressEqProfileSelection = false;
            UpdateEqDeleteButtonVisibility();
            StatusText.Text = $"EQ profile '{saved.Name}' saved.";
        }

        private void EqDeleteProfile_Click(object sender, RoutedEventArgs e)
        {
            if (EqProfileCombo?.SelectedItem is not string name) return;
            var profile = EqProfileManager.FindByName(name);
            if (profile == null || profile.IsBuiltIn) return;

            if (EqProfileManager.DeleteCustom(name))
            {
                StatusText.Text = $"EQ profile '{name}' deleted.";
                RefreshEqProfileCombo();
            }
        }

        private void UpdateEqDeleteButtonVisibility()
        {
            if (BtnEqDeleteProfile == null) return;
            string? name = EqProfileCombo?.SelectedItem as string;
            var profile = name != null ? EqProfileManager.FindByName(name) : null;
            BtnEqDeleteProfile.Visibility = (profile != null && !profile.IsBuiltIn)
                ? Visibility.Visible
                : Visibility.Collapsed;
        }

        /// <summary>
        /// Modal text-input prompt. Used for "Save EQ profile" — small enough to
        /// not warrant a separate Window file.
        /// </summary>
        private string? PromptForText(string title, string message, string defaultValue)
        {
            var window = new Window
            {
                Title = title,
                Width = 360,
                Height = 160,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                ResizeMode = ResizeMode.NoResize,
                Owner = this,
                Background = (Brush)FindResource("PanelBg"),
                Foreground = (Brush)FindResource("TextPrimary"),
            };

            var grid = new Grid { Margin = new Thickness(16) };
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var msgBlock = new TextBlock
            {
                Text = message,
                Foreground = (Brush)FindResource("TextSecondary"),
                Margin = new Thickness(0, 0, 0, 8)
            };
            Grid.SetRow(msgBlock, 0);

            var input = new TextBox
            {
                Text = defaultValue,
                Background = (Brush)FindResource("InputBg"),
                Foreground = (Brush)FindResource("TextPrimary"),
                BorderBrush = (Brush)FindResource("ButtonBorder"),
                Padding = new Thickness(6, 4, 6, 4)
            };
            Grid.SetRow(input, 1);

            var btnBar = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 12, 0, 0)
            };
            var btnOk = new System.Windows.Controls.Button
            {
                Content = "Save",
                MinWidth = 70,
                Padding = new Thickness(10, 4, 10, 4),
                Margin = new Thickness(0, 0, 8, 0),
                IsDefault = true
            };
            var btnCancel = new System.Windows.Controls.Button
            {
                Content = "Cancel",
                MinWidth = 70,
                Padding = new Thickness(10, 4, 10, 4),
                IsCancel = true
            };
            btnBar.Children.Add(btnOk);
            btnBar.Children.Add(btnCancel);
            Grid.SetRow(btnBar, 2);

            string? result = null;
            btnOk.Click += (_, __) => { result = input.Text.Trim(); window.DialogResult = true; };
            btnCancel.Click += (_, __) => { window.DialogResult = false; };

            grid.Children.Add(msgBlock);
            grid.Children.Add(input);
            grid.Children.Add(btnBar);
            window.Content = grid;
            input.SelectAll();
            input.Focus();

            return window.ShowDialog() == true ? result : null;
        }

        // ═══════════════════════════════════════════
        //  Keyboard Shortcuts
        // ═══════════════════════════════════════════

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);

            // Don't intercept keys when typing in search box
            if (SearchBox.IsFocused) return;

            if (e.Key == Key.Delete && FileGrid.SelectedItem is AudioFileInfo file)
            {
                _files.Remove(file);
                e.Handled = true;
            }
            else if (e.Key == Key.Enter && FileGrid.SelectedItem is AudioFileInfo playFile)
            {
                PlayFile(playFile, isManualSkip: true);
                e.Handled = true;
            }
            else if (e.Key == Key.Space && !SearchBox.IsFocused)
            {
                PlayPause_Click(this, new RoutedEventArgs());
                e.Handled = true;
            }
            else if (e.Key == Key.F && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
            {
                SearchBox.Focus();
                SearchBox.SelectAll();
                e.Handled = true;
            }
            else if (e.Key == Key.Escape && SearchBox.IsFocused)
            {
                SearchBox.Text = "";
                FileGrid.Focus();
                e.Handled = true;
            }
            // Media playback controls
            else if (e.Key == Key.MediaPlayPause)
            {
                PlayPause_Click(this, new RoutedEventArgs());
                e.Handled = true;
            }
            else if (e.Key == Key.MediaNextTrack)
            {
                NextTrack_Click(this, new RoutedEventArgs());
                e.Handled = true;
            }
            else if (e.Key == Key.MediaPreviousTrack)
            {
                PrevTrack_Click(this, new RoutedEventArgs());
                e.Handled = true;
            }
            else if (e.Key == Key.MediaStop)
            {
                if (_player.IsPlaying)
                    PlayPause_Click(this, new RoutedEventArgs());
                e.Handled = true;
            }
            // Arrow key controls: Left/Right = seek, Up/Down = volume
            else if (e.Key == Key.Left)
            {
                _player.SeekRelative(-5);
                e.Handled = true;
            }
            else if (e.Key == Key.Right)
            {
                _player.SeekRelative(5);
                e.Handled = true;
            }
            else if (e.Key == Key.Up)
            {
                double newVol = Math.Min(100, VolumeSlider.Value + 5);
                VolumeSlider.Value = newVol;
                if (_npVisible) NpVolumeSlider.Value = newVol;
                e.Handled = true;
            }
            else if (e.Key == Key.Down)
            {
                double newVol = Math.Max(0, VolumeSlider.Value - 5);
                VolumeSlider.Value = newVol;
                if (_npVisible) NpVolumeSlider.Value = newVol;
                e.Handled = true;
            }
            else if (e.Key == Key.M)
            {
                // Toggle mute
                if (_npVisible)
                    NpVolumeIcon_Click(this, new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left) { RoutedEvent = MouseLeftButtonUpEvent });
                else
                    VolumeIcon_Click(this, new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left) { RoutedEvent = MouseLeftButtonUpEvent });
                e.Handled = true;
            }
        }
    }
}
