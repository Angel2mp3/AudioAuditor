using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using AudioQualityChecker.Models;
using AudioQualityChecker.Services;

namespace AudioQualityChecker;

public partial class NowPlayingWindow : Window
{
    private readonly AudioPlayer _player;
    private readonly Action _onPrev;
    private readonly Action _onNext;
    private readonly Action _onShuffleToggle;
    private readonly Func<bool> _getShuffleState;

    private DispatcherTimer? _updateTimer;
    private LyricsResult _currentLyrics = LyricsResult.Empty;
    private int _currentLyricIndex = -1;
    private readonly List<TextBlock> _lyricTextBlocks = new();
    private LyricService.LyricProvider _lyricProvider = LyricService.LyricProvider.Auto;
    private LyricVersionHint? _versionOverride;
    private string? _versionOverrideFilePath;
    private AudioFileInfo? _currentFile;
    private bool _isSeeking;
    private int _lyricsVersion;
    private NpLyricDisplayMode _lyricMode;
    private bool _lyricsRefreshPending;
    private bool _lyricsNeedCatchUp;
    private System.Windows.Media.Effects.BlurEffect? _focusedLyricsInactiveBlur;
    private DispatcherTimer? _lyricsScrollTimer;
    private System.Threading.CancellationTokenSource? _coverCts;
    private System.Threading.CancellationTokenSource? _lyricsCts;
    private TimeSpan _lastPlaybarRenderPosition = TimeSpan.MinValue;
    private DateTime _lastPlaybarRenderUtc = DateTime.MinValue;

    private static readonly (LyricService.LyricProvider Provider, string Name)[] LyricProviders =
    {
        (LyricService.LyricProvider.Auto, "Auto"),
        (LyricService.LyricProvider.LrcFile, "LRC File"),
        (LyricService.LyricProvider.Embedded, "Embedded"),
        (LyricService.LyricProvider.LrcLib, "LRCLIB"),
        (LyricService.LyricProvider.Netease, "Netease"),
        (LyricService.LyricProvider.Musixmatch, "Musixmatch"),
    };
    private int _providerIndex;

    private System.Windows.Media.Effects.BlurEffect GetFocusedLyricsInactiveBlur()
    {
        double radius = Math.Clamp(ThemeManager.NpFocusedLyricsBlurRadius, 0, 16.0);
        if (_focusedLyricsInactiveBlur != null
            && Math.Abs(_focusedLyricsInactiveBlur.Radius - radius) < 0.001)
        {
            return _focusedLyricsInactiveBlur;
        }

        var blur = new System.Windows.Media.Effects.BlurEffect
        {
            Radius = radius,
            KernelType = System.Windows.Media.Effects.KernelType.Gaussian,
            RenderingBias = System.Windows.Media.Effects.RenderingBias.Performance
        };
        blur.Freeze();
        _focusedLyricsInactiveBlur = blur;
        return blur;
    }

    private static void ObserveUiTask(Task task, string operation)
    {
        if (task.IsCompletedSuccessfully)
            return;

        _ = ObserveUiTaskAsync(task, operation);
    }

    private static async Task ObserveUiTaskAsync(Task task, string operation)
    {
        try
        {
            await task.ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"{operation} failed: {ex}");
        }
    }

    public NowPlayingWindow(
        AudioPlayer player,
        Action onPrev,
        Action onNext,
        Action onShuffleToggle,
        Func<bool> getShuffleState)
    {
        InitializeComponent();
        _player = player;
        _onPrev = onPrev;
        _onNext = onNext;
        _onShuffleToggle = onShuffleToggle;
        _getShuffleState = getShuffleState;
        _lyricMode = ThemeManager.NpLyricMode;

        _updateTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
        _updateTimer.Tick += UpdateTimer_Tick;
        if (IsLyricsUiActive())
            _updateTimer.Start();

        StateChanged += NowPlayingWindow_StateChanged;
        IsVisibleChanged += NowPlayingWindow_IsVisibleChanged;

        Closed += (_, _) =>
        {
            _updateTimer?.Stop();
            if (_updateTimer != null)
                _updateTimer.Tick -= UpdateTimer_Tick;
            _updateTimer = null;
            _lyricsScrollTimer?.Stop();
            _lyricsScrollTimer = null;
            CancelLyricsFetch(invalidateVersion: true);
            StateChanged -= NowPlayingWindow_StateChanged;
            IsVisibleChanged -= NowPlayingWindow_IsVisibleChanged;
        };

        UpdateShuffleIcon();
    }

    public void SetTrack(AudioFileInfo file, double volume)
    {
        if (!string.Equals(_currentFile?.FilePath, file.FilePath, StringComparison.OrdinalIgnoreCase))
            CancelLyricsFetch(invalidateVersion: true);

        _currentFile = file;
        _isSeeking = false;
        NpSongTitle.Text = file.Title ?? file.FileName ?? "Unknown";
        NpSongArtist.Text = file.Artist ?? "";
        NpVolumeSlider.Value = volume;
        NpSeekSlider.Maximum = Math.Max(0, _player.TotalDuration.TotalSeconds);
        if (NpSeekSlider.Maximum > 0)
            NpSeekSlider.Value = Math.Clamp(_player.CurrentPosition.TotalSeconds, 0, NpSeekSlider.Maximum);

        if (file.FilePath != null)
        {
            ObserveUiTask(LoadCoverAsync(file.FilePath), nameof(LoadCoverAsync));
            if (IsLyricsUiActive())
                ObserveUiTask(LoadLyrics(file.FilePath), nameof(LoadLyrics));
            else
                _lyricsRefreshPending = true;
        }

        UpdatePlayState();
        RenderPlaybarStyle();
        if (IsLyricsUiActive())
        {
            _updateTimer?.Start();
            UpdateTimer_Tick(null, EventArgs.Empty);
        }
    }

    private bool IsLyricsUiActive() =>
        IsVisible && WindowState != WindowState.Minimized;

    private void NowPlayingWindow_StateChanged(object? sender, EventArgs e) =>
        UpdateLyricsWorkState();

    private void NowPlayingWindow_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e) =>
        UpdateLyricsWorkState();

    private void UpdateLyricsWorkState()
    {
        if (!IsLyricsUiActive())
        {
            _updateTimer?.Stop();
            _lyricsScrollTimer?.Stop();
            _lyricsScrollTimer = null;
            CancelLyricsFetch(invalidateVersion: true);
            if (_currentFile?.FilePath != null)
                _lyricsRefreshPending = true;
            ClearFocusedLyricsEffects();
            return;
        }

        _isSeeking = false;
        _lyricMode = ThemeManager.NpLyricMode;
        _updateTimer?.Start();
        UpdateTimer_Tick(null, EventArgs.Empty);
        Dispatcher.BeginInvoke(new Action(() =>
        {
            if (!IsLyricsUiActive()) return;

            if (_lyricsRefreshPending && _currentFile?.FilePath != null)
            {
                _lyricsRefreshPending = false;
                ObserveUiTask(LoadLyrics(_currentFile.FilePath), nameof(LoadLyrics));
            }
            else if (_currentLyrics.IsTimed && _player != null)
            {
                _lyricsNeedCatchUp = true;
                _currentLyricIndex = -1;
                UpdateLyricHighlight(_player.CurrentPosition);
            }

            ApplyFocusedLyricsEffects();
            RenderPlaybarStyle();
        }), DispatcherPriority.Loaded);
    }

    public void UpdateVolume(double volume) => NpVolumeSlider.Value = volume;

    // ─── Cover Art ───

    private async Task LoadCoverAsync(string filePath)
    {
        // Cancel any in-flight cover load from a previous track
        _coverCts?.Cancel();
        var cts = new System.Threading.CancellationTokenSource();
        _coverCts = cts;

        // Clear immediately so the old cover doesn't linger during the load
        ClearCover();

        try
        {
            var (bmp, colors) = await Task.Run<(BitmapImage?, AlbumColorExtractor.DominantColors?)>(() =>
            {
                cts.Token.ThrowIfCancellationRequested();
                using var tagFile = TagLib.File.Create(filePath);
                if (tagFile.Tag.Pictures.Length == 0) return (null, null);

                var imageData = tagFile.Tag.Pictures[0].Data.Data;
                using var ms = new MemoryStream(imageData);

                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.StreamSource = ms;
                bmp.EndInit();
                bmp.Freeze();

                // Run color extraction while still on the background thread
                byte[]? pixels = null;
                int pw = 0, ph = 0, stride = 0;
                try
                {
                    var converted = new FormatConvertedBitmap(bmp, PixelFormats.Bgra32, null, 0);
                    converted.Freeze();
                    pw = converted.PixelWidth;
                    ph = converted.PixelHeight;
                    stride = pw * 4;
                    pixels = new byte[stride * ph];
                    converted.CopyPixels(pixels, stride, 0);
                }
                catch { /* color extraction is best-effort */ }

                AlbumColorExtractor.DominantColors? colors = null;
                if (pixels != null)
                {
                    try { colors = AlbumColorExtractor.Extract(pixels, pw, ph, stride); }
                    catch { }
                }

                return (bmp, colors);
            }, cts.Token);

            if (cts.Token.IsCancellationRequested) return;

            if (bmp == null)
            {
                ClearCover();
                return;
            }

            NpCoverImage.Source = bmp;
            const double maxDim = 420;
            double scale = Math.Min(maxDim / bmp.PixelWidth, maxDim / bmp.PixelHeight);
            NpCoverImage.Width = bmp.PixelWidth * scale;
            NpCoverImage.Height = bmp.PixelHeight * scale;

            if (colors != null)
                ApplyGlowColors(colors);
        }
        catch (OperationCanceledException) { }
        catch
        {
            if (!cts.Token.IsCancellationRequested)
                ClearCover();
        }
    }

    private void ClearCover()
    {
        NpCoverImage.Source = null;
        NpCoverGlow1.Background = Brushes.Transparent;
        NpCoverGlow2.Background = Brushes.Transparent;
        BgGradient.Background = new SolidColorBrush(Color.FromRgb(15, 15, 25));
    }

    private void ApplyGlowColors(AlbumColorExtractor.DominantColors colors)
    {
        try
        {
            NpCoverGlow1.Background = new SolidColorBrush(
                Color.FromArgb(180, colors.Primary.R, colors.Primary.G, colors.Primary.B));
            NpCoverGlow2.Background = new SolidColorBrush(
                Color.FromArgb(120, colors.Secondary.R, colors.Secondary.G, colors.Secondary.B));

            var bg1 = Color.FromArgb(200, colors.Background.R, colors.Background.G, colors.Background.B);
            var bg2 = Color.FromRgb(10, 10, 18);
            BgGradient.Background = new LinearGradientBrush(bg1, bg2, 45);
        }
        catch
        {
            BgGradient.Background = new SolidColorBrush(Color.FromRgb(15, 15, 25));
        }
    }

    // ─── Lyrics ───

    private void CancelLyricsFetch(bool invalidateVersion)
    {
        if (invalidateVersion)
            _lyricsVersion++;

        _lyricsCts?.Cancel();
        _lyricsCts = null;
    }

    private async Task LoadLyrics(string filePath)
    {
        if (!IsLyricsUiActive())
        {
            _lyricsRefreshPending = true;
            return;
        }

        // Reset the user's manual version override when the track changes
        if (!string.Equals(_versionOverrideFilePath, filePath, StringComparison.OrdinalIgnoreCase))
        {
            _versionOverride = null;
            _versionOverrideFilePath = filePath;
        }

        CancelLyricsFetch(invalidateVersion: false);
        var lyricsCts = new System.Threading.CancellationTokenSource();
        _lyricsCts = lyricsCts;
        int version = ++_lyricsVersion;
        string? artist = _currentFile?.Artist;
        string? title = _currentFile?.Title;
        double duration = _currentFile?.DurationSeconds ?? 0;

        try
        {
            _currentLyrics = await LyricService.GetLyricsAsync(
                filePath, _lyricProvider, artist, title, durationSeconds: duration,
                forceVersion: _versionOverride,
                cancellationToken: lyricsCts.Token);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        finally
        {
            if (ReferenceEquals(_lyricsCts, lyricsCts))
                _lyricsCts = null;

            lyricsCts.Dispose();
        }

        if (version != _lyricsVersion) return; // track changed during fetch
        if (!IsLyricsUiActive())
        {
            _lyricsRefreshPending = true;
            return;
        }

        _currentLyricIndex = -1;
        BuildLyricLines();
        _lyricsNeedCatchUp = _currentLyrics.IsTimed;

        // Force immediate lyric sync so timed lyrics start highlighting right away
        if (_currentLyrics.IsTimed && _player != null)
        {
            void SyncLoadedLyrics()
            {
                if (version != _lyricsVersion) return;
                if (!IsLyricsUiActive()) return;
                _lyricsNeedCatchUp = true;
                UpdateLyricHighlight(_player.CurrentPosition);
            }

            await Dispatcher.InvokeAsync(SyncLoadedLyrics, System.Windows.Threading.DispatcherPriority.Loaded);
            await Dispatcher.InvokeAsync(SyncLoadedLyrics, System.Windows.Threading.DispatcherPriority.ContextIdle);
        }
    }

    private void BtnWrongVersion_Click(object sender, RoutedEventArgs e)
    {
        if (_currentFile == null || string.IsNullOrEmpty(_currentFile.FilePath)) return;

        // Toggle Explicit↔Clean and re-fetch
        _versionOverride = _versionOverride switch
        {
            LyricVersionHint.Clean => LyricVersionHint.Explicit,
            LyricVersionHint.Explicit => LyricVersionHint.Clean,
            _ => LyricVersionHint.Clean    // first click defaults to Clean
        };
        _versionOverrideFilePath = _currentFile.FilePath;

        NpLyricsSource.Text = _versionOverride == LyricVersionHint.Clean
            ? "Searching Clean version…"
            : "Searching Explicit version…";

        ObserveUiTask(LoadLyrics(_currentFile.FilePath), nameof(LoadLyrics));
    }

    private void BuildLyricLines()
    {
        LyricsPanel.Children.Clear();
        _lyricTextBlocks.Clear();

        if (!_currentLyrics.HasLyrics)
        {
            var noLyrics = new TextBlock
            {
                Text = "No lyrics available for this track",
                Foreground = new SolidColorBrush(Color.FromArgb(68, 255, 255, 255)),
                FontSize = 16,
                FontStyle = FontStyles.Italic,
                Margin = new Thickness(0, 60, 0, 0)
            };
            LyricsPanel.Children.Add(noLyrics);
            NpLyricsSource.Text = "";
            return;
        }

        NpLyricsSource.Text = _currentLyrics.IsTimed
            ? $"Source: {_currentLyrics.Source} (synced)"
            : $"Source: {_currentLyrics.Source} (static)";

        LyricsPanel.Children.Add(new Border { Height = 80 });

        foreach (var line in _currentLyrics.Lines)
        {
            var tb = new TextBlock
            {
                Text = line.Text,
                TextWrapping = TextWrapping.Wrap,
                TextAlignment = TextAlignment.Left,
                Margin = new Thickness(0, 6, 0, 6),
                FontSize = 18,
                Foreground = new SolidColorBrush(Color.FromArgb(85, 255, 255, 255)),
                ContextMenu = CreateLyricLineContextMenu()
            };
            _lyricTextBlocks.Add(tb);
            LyricsPanel.Children.Add(tb);
        }

        LyricsPanel.Children.Add(new Border { Height = 200 });
        ApplyFocusedLyricsEffects();
    }

    private ContextMenu CreateLyricLineContextMenu()
    {
        var menu = new ContextMenu();
        var modeItem = new MenuItem { Header = "Lyrics mode" };
        AddLyricModeItems(modeItem);
        menu.Items.Add(modeItem);
        return menu;
    }

    private void AddLyricModeItems(ItemsControl menu)
    {
        (string Label, NpLyricDisplayMode Mode)[] modes =
        {
            ("Standard", NpLyricDisplayMode.Standard),
            ("Blur", NpLyricDisplayMode.Blur),
            ("Uniform", NpLyricDisplayMode.Uniform),
        };
        foreach (var (label, mode) in modes)
        {
            var captured = mode;
            var item = new MenuItem { Header = label, IsCheckable = true, IsChecked = _lyricMode == mode };
            item.Click += (_, _) => SetLyricMode(captured);
            menu.Items.Add(item);
        }
    }

    private void UpdateLyricHighlight(TimeSpan position)
    {
        if (!_currentLyrics.IsTimed || _lyricTextBlocks.Count == 0) return;

        var lookAhead = position + TimeSpan.FromMilliseconds(80);

        int newIdx = -1;
        for (int i = _currentLyrics.Lines.Count - 1; i >= 0; i--)
        {
            if (lookAhead >= _currentLyrics.Lines[i].Time)
            {
                newIdx = i;
                break;
            }
        }

        if (newIdx == _currentLyricIndex)
            return;
        _currentLyricIndex = newIdx;

        for (int i = 0; i < _lyricTextBlocks.Count; i++)
        {
            var tb = _lyricTextBlocks[i];
            if (i == newIdx)
            {
                AnimateForeground(tb, Colors.White);
                AnimateFontSize(tb, 22);
                tb.FontWeight = FontWeights.SemiBold;
            }
            else
            {
                byte alpha;
                if (_lyricMode == NpLyricDisplayMode.Blur && _currentLyrics.IsTimed)
                {
                    // Distance-based fade: past lines fade faster than upcoming lines
                    int dist = Math.Abs(i - newIdx);
                    double opacity = i < newIdx
                        ? Math.Max(0.13, 1.0 - dist * 0.28)
                        : Math.Max(0.20, 1.0 - dist * 0.20);
                    alpha = (byte)(opacity * 255);
                }
                else if (_lyricMode == NpLyricDisplayMode.Uniform)
                {
                    alpha = 255; // all lines uniformly bright; active distinguished by size only
                }
                else
                {
                    alpha = i < newIdx ? (byte)68 : (byte)85;
                }
                AnimateForeground(tb, Color.FromArgb(alpha, 255, 255, 255));
                AnimateFontSize(tb, 18);
                tb.FontWeight = FontWeights.Normal;
            }
        }

        ApplyFocusedLyricsEffects();

        // Auto-scroll
        if (newIdx >= 0 && newIdx < _lyricTextBlocks.Count)
        {
            try
            {
                var target = _lyricTextBlocks[newIdx];
                var transform = target.TransformToAncestor(LyricsPanel);
                var point = transform.Transform(new Point(0, 0));
                double scrollerHeight = LyricsScroller.ViewportHeight;
                double targetY = point.Y - scrollerHeight * 0.35;
                if (targetY < 0) targetY = 0;

                AnimateScroll(LyricsScroller, targetY, 220);
            }
            catch { /* visual tree may still be settling */ }
        }
    }

    private double GetFocusedLyricOpacity(int lineIndex)
    {
        if (_lyricMode != NpLyricDisplayMode.Blur || _currentLyricIndex < 0 || !_currentLyrics.IsTimed)
            return 1.0;

        int distance = Math.Abs(lineIndex - _currentLyricIndex);
        if (distance == 0)
            return 1.0;

        return lineIndex < _currentLyricIndex
            ? Math.Max(0.12, 1.0 - distance * 0.30)
            : Math.Max(0.22, 1.0 - distance * 0.18);
    }

    private static void AnimateForeground(TextBlock tb, Color target)
    {
        var brush = tb.Foreground as SolidColorBrush;
        if (brush == null || brush.IsFrozen)
        {
            brush = new SolidColorBrush(brush?.Color ?? Colors.Transparent);
            tb.Foreground = brush;
        }

        if (!AnimationPolicy.IsMotionAllowed(AnimationArea.Lyrics))
        {
            brush.BeginAnimation(SolidColorBrush.ColorProperty, null); // stop any running animation
            brush.Color = target;
            return;
        }

        var anim = new ColorAnimation
        {
            To = target,
            Duration = TimeSpan.FromMilliseconds(300),
            EasingFunction = new QuadraticEase()
        };
        brush.BeginAnimation(SolidColorBrush.ColorProperty, anim);
    }

    private static void AnimateFontSize(TextBlock tb, double target)
    {
        if (!AnimationPolicy.IsMotionAllowed(AnimationArea.Lyrics))
        {
            tb.BeginAnimation(TextBlock.FontSizeProperty, null);
            tb.FontSize = target;
            return;
        }

        tb.BeginAnimation(TextBlock.FontSizeProperty, new DoubleAnimation(target, TimeSpan.FromMilliseconds(180))
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
        });
    }

    private void AnimateScroll(ScrollViewer viewer, double targetOffset, double durationMs)
    {
        _lyricsScrollTimer?.Stop();
        _lyricsScrollTimer = null;

        double current = viewer.VerticalOffset;
        double diff = targetOffset - current;
        if (Math.Abs(diff) < 1) return;

        if (!AnimationPolicy.IsMotionAllowed(AnimationArea.Lyrics))
        {
            viewer.ScrollToVerticalOffset(targetOffset);
            return;
        }

        int steps = Math.Max(1, (int)(durationMs / 16));
        int step = 0;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        timer.Tick += (_, _) =>
        {
            if (!IsLyricsUiActive())
            {
                timer.Stop();
                if (ReferenceEquals(_lyricsScrollTimer, timer))
                    _lyricsScrollTimer = null;
                return;
            }

            step++;
            double t = (double)step / steps;
            t = 1 - (1 - t) * (1 - t);
            viewer.ScrollToVerticalOffset(current + diff * t);
            if (step >= steps)
            {
                timer.Stop();
                if (ReferenceEquals(_lyricsScrollTimer, timer))
                    _lyricsScrollTimer = null;
            }
        };
        _lyricsScrollTimer = timer;
        timer.Start();
    }

    private void SetLyricMode(NpLyricDisplayMode mode)
    {
        if (_lyricMode == mode) return;
        _lyricMode = mode;
        ThemeManager.NpLyricMode = mode;
        ThemeManager.SavePlayOptions();
        // Rebuild + re-highlight so the new mode's colouring applies cleanly (even while paused).
        BuildLyricLines();
        _currentLyricIndex = -1;
        if (_player != null)
            UpdateLyricHighlight(_player.CurrentPosition);
        ApplyFocusedLyricsEffects();
    }

    private void ApplyFocusedLyricsEffects()
    {
        if (_lyricTextBlocks.Count == 0)
            return;

        bool focusedLyricsActive = _lyricMode == NpLyricDisplayMode.Blur
                                   && IsLyricsUiActive()
                                   && _currentLyrics.IsTimed;
        bool shouldBlurInactive = focusedLyricsActive
                                  && ThemeManager.NpFocusedLyricsBlurRadius > 0;
        var inactiveBlur = shouldBlurInactive ? GetFocusedLyricsInactiveBlur() : null;

        for (int i = 0; i < _lyricTextBlocks.Count; i++)
        {
            bool inactive = focusedLyricsActive && (_currentLyricIndex < 0 || i != _currentLyricIndex);
            _lyricTextBlocks[i].Effect = inactive ? inactiveBlur : null;
            _lyricTextBlocks[i].Opacity = inactive ? GetFocusedLyricOpacity(i) : 1.0;
        }
    }

    private void ClearFocusedLyricsEffects()
    {
        foreach (var tb in _lyricTextBlocks)
        {
            tb.Effect = null;
            tb.Opacity = 1.0;
            tb.BeginAnimation(TextBlock.FontSizeProperty, null);
            if (tb.Foreground is SolidColorBrush brush)
                brush.BeginAnimation(SolidColorBrush.ColorProperty, null);
        }
    }

    // ─── Timer Update ───

    private void UpdateTimer_Tick(object? sender, EventArgs e)
    {
        if (_player == null) return;
        if (!IsLyricsUiActive())
        {
            _updateTimer?.Stop();
            _lyricsScrollTimer?.Stop();
            _lyricsScrollTimer = null;
            ClearFocusedLyricsEffects();
            return;
        }

        var pos = _player.CurrentPosition;
        var total = _player.TotalDuration;

        NpTimeElapsed.Text = FormatTime(pos);
        NpTimeTotal.Text = FormatTime(total);

        if (!_isSeeking && total.TotalSeconds > 0)
        {
            NpSeekSlider.Maximum = total.TotalSeconds;
            NpSeekSlider.Value = pos.TotalSeconds;
        }
        if (ShouldRenderPlaybar(pos))
            RenderPlaybarStyle();

        UpdatePlayState();
        UpdateLyricHighlight(pos);

        if (_lyricsNeedCatchUp)
        {
            _currentLyricIndex = -1;
            UpdateLyricHighlight(pos);
            if (_currentLyricIndex >= 0 || !_currentLyrics.IsTimed || _lyricTextBlocks.Count == 0)
                _lyricsNeedCatchUp = false;
        }
    }

    private void UpdatePlayState()
    {
        NpPlayIcon.Text = _player?.IsPlaying == true ? "\u23F8" : "\u25B6";
    }

    private bool ShouldRenderPlaybar(TimeSpan position)
    {
        var now = DateTime.UtcNow;
        var style = ThemeManager.NpPlaybarAnimationStyle;
        double minIntervalMs = style == PlaybarAnimationStyle.Regular ? 250 : 120;
        bool positionMoved = _lastPlaybarRenderPosition == TimeSpan.MinValue
                             || Math.Abs((position - _lastPlaybarRenderPosition).TotalMilliseconds) >= 180;
        bool intervalElapsed = (now - _lastPlaybarRenderUtc).TotalMilliseconds >= minIntervalMs;

        if (!positionMoved && !intervalElapsed)
            return false;

        _lastPlaybarRenderPosition = position;
        _lastPlaybarRenderUtc = now;
        return true;
    }

    private static string FormatTime(TimeSpan ts) =>
        ts.TotalHours >= 1
            ? ts.ToString(@"h\:mm\:ss")
            : ts.ToString(@"m\:ss");

    private static (Color primary, Color secondary) InterpolatePlaybarCycleColors(Color primary, Color secondary, Color tertiary, double phaseSeconds)
    {
        const double cycleSeconds = 12.0;
        double phase = (phaseSeconds % cycleSeconds) / cycleSeconds * 3.0;
        if (phase < 1.0) return (LerpColor(primary, secondary, phase), LerpColor(secondary, tertiary, phase));
        if (phase < 2.0) return (LerpColor(secondary, tertiary, phase - 1.0), LerpColor(tertiary, primary, phase - 1.0));
        return (LerpColor(tertiary, primary, phase - 2.0), LerpColor(primary, secondary, phase - 2.0));
    }

    private static Color LerpColor(Color a, Color b, double t)
    {
        t = Math.Clamp(t, 0, 1);
        return Color.FromArgb(
            (byte)(a.A + (b.A - a.A) * t),
            (byte)(a.R + (b.R - a.R) * t),
            (byte)(a.G + (b.G - a.G) * t),
            (byte)(a.B + (b.B - a.B) * t));
    }

    private void RenderPlaybarStyle()
    {
        if (NpPlaybarAnimCanvas == null)
            return;

        Color accent = Colors.CornflowerBlue;
        Color secondary = Colors.CornflowerBlue;
        Color tertiary = Colors.CornflowerBlue;
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

        double phase = AnimationPolicy.IsMotionAllowed(AnimationArea.Playbar) ? DateTime.UtcNow.TimeOfDay.TotalSeconds : 0;
        if (ThemeManager.NpColorMatchEnabled)
            (accent, secondary) = InterpolatePlaybarCycleColors(accent, secondary, tertiary, phase);

        double pct = NpSeekSlider.Maximum > 0 ? NpSeekSlider.Value / NpSeekSlider.Maximum : 0;
        RenderPlaybarCanvas(NpPlaybarAnimCanvas, pct, accent, secondary, ThemeManager.NpPlaybarAnimationStyle, phase);
    }

    private static void RenderPlaybarCanvas(
        Canvas canvas,
        double pct,
        Color accent,
        Color secondary,
        PlaybarAnimationStyle style,
        double phaseSeconds)
    {
        canvas.Children.Clear();

        double w = canvas.ActualWidth;
        double h = canvas.ActualHeight;
        pct = Math.Clamp(pct, 0, 1);
        double fillW = w * pct;
        if (w < 1 || h < 1 || fillW < 1) return;

        // Thickness of the played bar; matches the slider track so the fill reads as a clean
        // continuation into the playhead dot. Kept in sync with PlaybarRenderer.BarThickness.
        const double barThickness = 4.0;

        if (style == PlaybarAnimationStyle.Wave)
        {
            double waveMid = h / 2;
            double waveBarH = Math.Min(barThickness, h);

            // Base filled progress bar FIRST so the played area isn't the dark surface behind the
            // transparent slider track (the "black playbar" bug); the wave draws on top.
            var waveBase = new System.Windows.Shapes.Rectangle
            {
                Width = fillW,
                Height = waveBarH,
                RadiusX = waveBarH / 2,
                RadiusY = waveBarH / 2,
                Fill = new SolidColorBrush(Color.FromArgb(255, accent.R, accent.G, accent.B)),
                IsHitTestVisible = false
            };
            Canvas.SetLeft(waveBase, 0);
            Canvas.SetTop(waveBase, (h - waveBarH) / 2);
            canvas.Children.Add(waveBase);

            // A smooth sine stroke centered vertically, in a brighter tint so it stands out.
            double amplitude = Math.Clamp(h * 0.3, 1.5, waveMid - 1);
            var geometry = new StreamGeometry();
            using (var ctx = geometry.Open())
            {
                int steps = Math.Clamp((int)(fillW / 4), 16, 260);
                ctx.BeginFigure(new Point(0, waveMid), false, false);
                for (int i = 0; i <= steps; i++)
                {
                    double x = fillW * i / steps;
                    double y = waveMid + Math.Sin((x / Math.Max(1, w)) * Math.PI * 6 + phaseSeconds * 4) * amplitude;
                    ctx.LineTo(new Point(x, y), true, true);
                }
            }
            geometry.Freeze();

            byte LR = (byte)(accent.R + (255 - accent.R) * 0.55);
            byte LG = (byte)(accent.G + (255 - accent.G) * 0.55);
            byte LB = (byte)(accent.B + (255 - accent.B) * 0.55);
            canvas.Children.Add(new System.Windows.Shapes.Path
            {
                Data = geometry,
                Stroke = new SolidColorBrush(Color.FromArgb(255, LR, LG, LB)),
                StrokeThickness = Math.Clamp(h * 0.3, 2, 5),
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
                StrokeLineJoin = PenLineJoin.Round,
                IsHitTestVisible = false
            });
            return;
        }

        // Regular (and any fallback): a plain thin accent bar centered on the track, rounded
        // ends, full opacity — no animation.
        double regularBarH = Math.Min(barThickness, h);
        var regularRect = new System.Windows.Shapes.Rectangle
        {
            Width = fillW,
            Height = regularBarH,
            RadiusX = regularBarH / 2,
            RadiusY = regularBarH / 2,
            Fill = new SolidColorBrush(Color.FromArgb(255, accent.R, accent.G, accent.B)),
            IsHitTestVisible = false
        };
        Canvas.SetLeft(regularRect, 0);
        Canvas.SetTop(regularRect, (h - regularBarH) / 2);
        canvas.Children.Add(regularRect);
    }

    // ─── Control Events ───

    private void NpPlayPause_Click(object sender, RoutedEventArgs e)
    {
        if (_player == null) return;
        if (_player.IsPlaying)
            _player.Pause();
        else
            _player.Resume();
        UpdatePlayState();
    }

    private void NpPrev_Click(object sender, RoutedEventArgs e) => _onPrev();
    private void NpNext_Click(object sender, RoutedEventArgs e) => _onNext();

    private void NpShuffle_Click(object sender, RoutedEventArgs e)
    {
        _onShuffleToggle();
        UpdateShuffleIcon();
    }

    private void UpdateShuffleIcon()
    {
        NpShuffleIcon.Foreground = _getShuffleState()
            ? Brushes.White
            : new SolidColorBrush(Color.FromArgb(128, 255, 255, 255));
    }

    private void NpLyricSource_Click(object sender, RoutedEventArgs e)
    {
        _providerIndex = (_providerIndex + 1) % LyricProviders.Length;
        _lyricProvider = LyricProviders[_providerIndex].Provider;
        NpLyricSourceText.Text = $"\U0001F3A4 {LyricProviders[_providerIndex].Name}";

        if (_currentFile?.FilePath != null)
            ObserveUiTask(LoadLyrics(_currentFile.FilePath), nameof(LoadLyrics));
    }

    private void NpVolumeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_player != null)
            _player.Volume = (float)(NpVolumeSlider.Value / 100.0);
    }

    private void NpSeekSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        // Visual-only update during drag — actual seek happens on MouseUp
        RenderPlaybarStyle();
    }

    private void NpSeekSlider_MouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        _isSeeking = true;
    }

    private void NpSeekSlider_MouseUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (_isSeeking && _player != null)
        {
            _player.Seek(NpSeekSlider.Value);
            _currentLyricIndex = -1;
            UpdateLyricHighlight(TimeSpan.FromSeconds(NpSeekSlider.Value));
        }
        _isSeeking = false;
        RenderPlaybarStyle();
    }

    private void NpClose_Click(object sender, RoutedEventArgs e) => Close();
}
