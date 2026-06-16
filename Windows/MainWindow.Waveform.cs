using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using AudioQualityChecker.Services;

namespace AudioQualityChecker
{
    /// <summary>
    /// Animated waveform + playbar-animation rendering for the main window: the per-song pseudo-random
    /// waveform background, its CompositionTarget render loop, and the playbar-anim rendering tick. This
    /// is the canvas/animation orchestration layer; the pure per-style playbar drawing lives in
    /// PlaybarRenderer.cs (RenderPlaybar) and is called from here. PauseAnimations/ResumeAnimations
    /// (which start/stop this loop) stay in MainWindow.xaml.cs.
    /// </summary>
    public partial class MainWindow
    {
        // Waveform animation state (used only by the methods in this partial).
        private double[] _waveformData = Array.Empty<double>();
        private DateTime _waveformAnimStart;
        private DateTime _lastWaveformRenderTime = DateTime.MinValue;
        private bool _waveformAnimActive;
        private int _waveformDataWidth;
        private double[] _waveformBaseData = Array.Empty<double>();
        private bool _playbarAnimRendering;

        /// <summary>
        /// Generates a set of pre-computed waveform amplitudes for the background visualization.
        /// Uses a seeded pseudo-random wave pattern so each song gets a unique but consistent look.
        /// </summary>
        private void DrawWaveformBackground()
        {
            StopWaveformAnimation();
            WaveformCanvas.Children.Clear();
            PlaybarAnimCanvas.Children.Clear();

            if (ThemeManager.MainPlaybarAnimationStyle != PlaybarAnimationStyle.Wave)
            {
                _waveformData = Array.Empty<double>();
                _waveformBaseData = Array.Empty<double>();
                RenderPlaybarAnim();
                return;
            }

            GenerateWaveformData();
            StartWaveformAnimation();
        }

        private void WaveformCanvas_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            bool waveEnabled = ThemeManager.MainPlaybarAnimationStyle == PlaybarAnimationStyle.Wave
                || (ThemeManager.NpPlaybarAnimationStyle == PlaybarAnimationStyle.Wave && _npVisible);
            if (_player.CurrentFile == null || !waveEnabled)
                return;

            int width = (int)Math.Max(WaveformCanvas.ActualWidth, NpWaveformCanvas.ActualWidth);
            if (width < 10 || Math.Abs(width - _waveformDataWidth) < 2)
                return;

            GenerateWaveformData();
            UpdateWaveformProgress();
        }

        private void StartWaveformAnimation()
        {
            bool mainWave = ThemeManager.MainPlaybarAnimationStyle == PlaybarAnimationStyle.Wave;
            bool npWave = ThemeManager.NpPlaybarAnimationStyle == PlaybarAnimationStyle.Wave && _npVisible;

            if (!mainWave && !npWave)
            {
                StopWaveformAnimation();
                WaveformCanvas.Children.Clear();
                NpWaveformCanvas.Children.Clear();
                RenderPlaybarAnim();
                return;
            }

            if (!_waveformAnimActive)
            {
                _waveformAnimActive = true;
                CompositionTarget.Rendering += WaveformAnimation_Tick;
            }
        }

        private void StopWaveformAnimation()
        {
            if (_waveformAnimActive)
            {
                _waveformAnimActive = false;
                CompositionTarget.Rendering -= WaveformAnimation_Tick;
            }
        }

        private void WaveformAnimation_Tick(object? sender, EventArgs e)
        {
            bool mainWave = ThemeManager.MainPlaybarAnimationStyle == PlaybarAnimationStyle.Wave;
            bool npWave = ThemeManager.NpPlaybarAnimationStyle == PlaybarAnimationStyle.Wave && _npVisible;

            if (!mainWave && !npWave)
            {
                StopWaveformAnimation();
                WaveformCanvas.Children.Clear();
                NpWaveformCanvas.Children.Clear();
                RenderPlaybarAnim();
                return;
            }

            if (_waveformBaseData.Length == 0)
            {
                // Generate data if missing (e.g., NP switched to Wave while main is not)
                GenerateWaveformData();
                if (_waveformBaseData.Length == 0) return;
            }

            // Keep animation alive while a track is loaded, even if momentarily paused
            if (!_player.IsPlaying && !_player.IsPaused && _player.CurrentFile == null) return;

            // Auto-restart the player timer if it was lost during a spurious stop event
            if (_player.IsPlaying && !_playerTimer.IsEnabled)
                _playerTimer.Start();

            var now = DateTime.UtcNow;
            if ((now - _lastWaveformRenderTime).TotalMilliseconds < 33)
                return;
            _lastWaveformRenderTime = now;

            // Freeze gradient cycling when animations are disabled / battery-saved
            double elapsed = AnimationPolicy.IsMotionAllowed(AnimationArea.Playbar)
                ? (now - _waveformAnimStart).TotalSeconds
                : 0;
            var playbarColors = ThemeManager.GetPlaybarColors();
            double animSpeed = playbarColors.AnimationSpeed;

            // Rainbow Bars cycles the live accent/secondary resources each frame so the whole UI
            // (and, below, the accent-driven wave) cycles together.
            bool isRainbow = ThemeManager.CurrentPlaybarTheme == "Rainbow Bars";
            if (isRainbow)
            {
                double hueBase = (elapsed * 30.0) % 360.0; // 30 degrees/sec cycle
                var accentColor = HsvToColor(hueBase, 0.85, 0.95);
                accentColor.A = 255;
                var accentBrush = new SolidColorBrush(accentColor);
                accentBrush.Freeze();
                Application.Current.Resources["PlaybarAccentColor"] = accentBrush;
                var secondaryColor = HsvToColor((hueBase + 120) % 360, 0.85, 0.95);
                secondaryColor.A = 255;
                var secondaryBrush = new SolidColorBrush(secondaryColor);
                secondaryBrush.Freeze();
                Application.Current.Resources["PlaybarSecondaryColor"] = secondaryBrush;
            }

            // Wave is accent-driven: the progress fill uses the live accent→secondary at full
            // opacity (so it matches the playhead dot and the Regular bar and never looks
            // washed out); the background lobe is a faint accent tint. Color-match still overrides
            // the NP canvas below since that's a deliberate album-art feature.
            (Color waveAccent, Color waveSecondary) = ResolvePlaybarAccentSecondary();
            Color[] gradientColors = BuildWaveGradient(waveAccent, waveSecondary);
            Color mainBackgroundColor = Color.FromArgb(40, waveAccent.R, waveAccent.G, waveAccent.B);
            Color npBackgroundColor = mainBackgroundColor;
            Color[] npGradientColors = gradientColors;
            if (npWave && NpTryResolveActivePlaybarPalette(out var npColorMatchBg, out var npColorMatchGradient, out var npAnimSpeed))
            {
                npBackgroundColor = npColorMatchBg;
                npGradientColors = npColorMatchGradient;
                animSpeed = npAnimSpeed;
            }

            // Animate base data with time-varying phase
            int points = _waveformBaseData.Length;
            for (int i = 0; i < points; i++)
            {
                double t = (double)i / points;
                double baseVal = Math.Clamp((_waveformBaseData[i] + 1.33) / 2.66, 0.25, 0.95);
                double wave = 0.08 * Math.Sin(4 * Math.PI * t + elapsed * animSpeed * 2.0)
                            + 0.06 * Math.Sin(7 * Math.PI * t - elapsed * animSpeed * 1.5)
                            + 0.04 * Math.Sin(13 * Math.PI * t + elapsed * animSpeed * 3.0);
                _waveformData[i] = Math.Clamp(baseVal + wave, 0.15, 0.98);
            }

            // Render to main canvas
            if (mainWave)
            {
                double mw = WaveformCanvas.ActualWidth;
                double mh = WaveformCanvas.ActualHeight;
                if (mw >= 10 && mh >= 5)
                {
                    double mainProgress = SeekSlider.Maximum > 0 ? SeekSlider.Value / SeekSlider.Maximum : 0;
                    RenderWaveformCanvas(WaveformCanvas, mw, mh, points, mainBackgroundColor, gradientColors, mainProgress);
                }
            }

            // Render to NP canvas
            if (npWave)
            {
                double nw = NpWaveformCanvas.ActualWidth;
                double nh = NpWaveformCanvas.ActualHeight;
                if (nw >= 10 && nh >= 5)
                {
                    double npProgress = NpSeekSlider.Maximum > 0 ? NpSeekSlider.Value / NpSeekSlider.Maximum : 0;
                    RenderWaveformCanvas(NpWaveformCanvas, nw, nh, points, npBackgroundColor, npGradientColors, npProgress);
                }
            }

            RenderPlaybarAnim();
        }

        private void RenderWaveformCanvas(Canvas canvas, double canvasWidth, double canvasHeight, int points,
            Color bgColor, Color[] gradientColors, double progress)
        {
            canvas.Children.Clear();
            double mid = canvasHeight / 2;
            double fadeRegion = 0.03;
            double amplitudeScale = mid * 0.85;

            // Background wave
            var bgGeometry = new StreamGeometry();
            using (var ctx = bgGeometry.Open())
            {
                ctx.BeginFigure(new Point(0, mid), true, true);
                for (int i = 0; i < points && i < (int)canvasWidth; i++)
                {
                    double t = (double)i / points;
                    double envelope = WaveformEnvelope(t, fadeRegion);
                    double amp = _waveformData[i] * amplitudeScale * envelope;
                    ctx.LineTo(new Point(i, mid - amp), true, false);
                }
                for (int i = Math.Min(points, (int)canvasWidth) - 1; i >= 0; i--)
                {
                    double t = (double)i / points;
                    double envelope = WaveformEnvelope(t, fadeRegion);
                    double amp = _waveformData[i] * amplitudeScale * envelope;
                    ctx.LineTo(new Point(i, mid + amp), true, false);
                }
            }
            bgGeometry.Freeze();

            var bgBrush = new SolidColorBrush(bgColor);
            bgBrush.Freeze();
            canvas.Children.Add(new System.Windows.Shapes.Path
            {
                Data = bgGeometry,
                Fill = bgBrush,
                IsHitTestVisible = false
            });

            // Progress overlay
            progress = Math.Clamp(progress, 0, 1);
            int progressPixel = (int)Math.Round(progress * canvasWidth);
            progressPixel = Math.Clamp(progressPixel, 0, Math.Min(points, (int)canvasWidth));
            if (progressPixel > 0)
            {
                var gradient = new LinearGradientBrush(
                    new GradientStopCollection
                    {
                        new GradientStop(gradientColors[0], 0),
                        new GradientStop(gradientColors[1], 0.5),
                        new GradientStop(gradientColors[2], 1.0)
                    }, new Point(0, 0), new Point(1, 0));
                gradient.Freeze();

                var progGeometry = new StreamGeometry();
                using (var ctx = progGeometry.Open())
                {
                    ctx.BeginFigure(new Point(0, mid), true, true);
                    for (int i = 0; i < progressPixel && i < points; i++)
                    {
                        double t = (double)i / points;
                        double envelope = WaveformEnvelope(t, fadeRegion);
                        double amp = _waveformData[i] * amplitudeScale * envelope;
                        ctx.LineTo(new Point(i, mid - amp), true, false);
                    }
                    if (progressPixel > 0)
                        ctx.LineTo(new Point(progressPixel, mid), true, false);
                    for (int i = Math.Min(progressPixel, points) - 1; i >= 0; i--)
                    {
                        double t = (double)i / points;
                        double envelope = WaveformEnvelope(t, fadeRegion);
                        double amp = _waveformData[i] * amplitudeScale * envelope;
                        ctx.LineTo(new Point(i, mid + amp), true, false);
                    }
                }
                progGeometry.Freeze();

                canvas.Children.Add(new System.Windows.Shapes.Path
                {
                    Data = progGeometry,
                    Fill = gradient,
                    IsHitTestVisible = false
                });

                if (progressPixel > 1)
                {
                    var capBrush = new SolidColorBrush(gradientColors[2]);
                    capBrush.Freeze();
                    var cap = new System.Windows.Shapes.Ellipse
                    {
                        Width = 5,
                        Height = 5,
                        Fill = capBrush,
                        IsHitTestVisible = false
                    };
                    Canvas.SetLeft(cap, progressPixel - 2.5);
                    Canvas.SetTop(cap, mid - 2.5);
                    canvas.Children.Add(cap);
                }
            }
        }

        private void GenerateWaveformData()
        {
            if (_player.CurrentFile == null) return;

            double canvasWidth = Math.Max(WaveformCanvas.ActualWidth, NpWaveformCanvas.ActualWidth);
            if (canvasWidth < 10) canvasWidth = 400; // fallback

            int points = (int)canvasWidth;
            _waveformDataWidth = points;
            _waveformData = new double[points];
            _waveformBaseData = new double[points];

            int seed = _player.CurrentFile.GetHashCode();
            var rng = new Random(seed);

            double freq1 = 2 + rng.NextDouble() * 3;
            double freq2 = 8 + rng.NextDouble() * 10;
            double freq3 = 20 + rng.NextDouble() * 30;
            double phase1 = rng.NextDouble() * Math.PI * 2;
            double phase2 = rng.NextDouble() * Math.PI * 2;
            double phase3 = rng.NextDouble() * Math.PI * 2;

            for (int i = 0; i < points; i++)
            {
                double t = (double)i / points;
                double wave = 0.5 * Math.Sin(freq1 * Math.PI * t + phase1)
                            + 0.3 * Math.Sin(freq2 * Math.PI * t + phase2)
                            + 0.2 * Math.Sin(freq3 * Math.PI * t + phase3)
                            + 0.15 * Math.Sin(1.5 * Math.PI * t + phase1 * 0.7)
                            + 0.1 * Math.Sin(0.8 * Math.PI * t + phase2 * 1.3);
                _waveformBaseData[i] = wave;
                _waveformData[i] = Math.Clamp((wave + 1.25) / 2.5, 0.25, 0.95);
            }

            _waveformAnimStart = DateTime.UtcNow;
        }

        private double GetCurrentBassEnergy()
        {
            if (_vizSmoothed == null || _vizSmoothed.Length == 0) return 0;
            double sum = 0;
            int cnt = Math.Min(5, _vizSmoothed.Length);
            for (int i = 0; i < cnt; i++) sum += _vizSmoothed[i];
            return sum / cnt;
        }

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

        /// <summary>Resolves the live playbar accent/secondary colors from app resources, with
        /// sensible fallbacks. Drives the accent-coloured Wave fill so it matches the rest of the UI.</summary>
        private (Color accent, Color secondary) ResolvePlaybarAccentSecondary()
        {
            var accentBrush = TryFindResource("PlaybarAccentColor") as SolidColorBrush
                ?? TryFindResource("AccentColor") as SolidColorBrush
                ?? Brushes.CornflowerBlue;
            var secondaryBrush = TryFindResource("PlaybarSecondaryColor") as SolidColorBrush
                ?? accentBrush;
            return (accentBrush.Color, secondaryBrush.Color);
        }

        /// <summary>Builds the 3-stop accent→secondary gradient used for the Wave progress fill,
        /// at full opacity so it never looks washed out.</summary>
        private static Color[] BuildWaveGradient(Color accent, Color secondary)
        {
            Color a = Color.FromArgb(255, accent.R, accent.G, accent.B);
            Color b = Color.FromArgb(255, secondary.R, secondary.G, secondary.B);
            return new[] { a, LerpColor(a, b, 0.5), b };
        }

        private void RenderPlaybarAnim()
        {
            var style = ThemeManager.MainPlaybarAnimationStyle;
            if (style == PlaybarAnimationStyle.Wave)
            {
                PlaybarAnimCanvas.Children.Clear();
                return;
            }

            var accentBrush = TryFindResource("PlaybarAccentColor") as SolidColorBrush
                ?? TryFindResource("AccentColor") as SolidColorBrush
                ?? Brushes.CornflowerBlue;
            var secondaryBrush = TryFindResource("PlaybarSecondaryColor") as SolidColorBrush
                ?? accentBrush;
            var tertiaryBrush = TryFindResource("PlaybarTertiaryColor") as SolidColorBrush
                ?? secondaryBrush;
            Color accent = accentBrush.Color;
            Color secondary = secondaryBrush.Color;
            bool colormatchCycle = ThemeManager.MainColorMatchEnabled && _mainAlbumPrimary != default;
            if (colormatchCycle)
                (accent, secondary) = InterpolatePlaybarCycleColors(accent, secondary, tertiaryBrush.Color, DateTime.UtcNow.TimeOfDay.TotalSeconds);
            double pct = SeekSlider.Maximum > 0 ? SeekSlider.Value / SeekSlider.Maximum : 0;

            EnsurePlaybarAnimRendering(colormatchCycle || IsAnimatedPlaybarStyle(style));
            double phase = IsAnimatedPlaybarStyle(style) ? DateTime.UtcNow.TimeOfDay.TotalSeconds : 0;
            RenderPlaybar(PlaybarAnimCanvas, pct, accent, secondary, style, phase);
        }

        /// <summary>Styles that need a continuous CompositionTarget.Rendering tick to animate.</summary>
        private static bool IsAnimatedPlaybarStyle(PlaybarAnimationStyle style) =>
            style is PlaybarAnimationStyle.Wave;

        private void EnsurePlaybarAnimRendering(bool enable)
        {
            if (enable && !_playbarAnimRendering)
            {
                _playbarAnimRendering = true;
                CompositionTarget.Rendering += PlaybarAnimRendering_Tick;
            }
            else if (!enable && _playbarAnimRendering)
            {
                _playbarAnimRendering = false;
                CompositionTarget.Rendering -= PlaybarAnimRendering_Tick;
            }
        }

        private void PlaybarAnimRendering_Tick(object? sender, EventArgs e)
        {
            var style = ThemeManager.MainPlaybarAnimationStyle;
            bool colormatchCycle = ThemeManager.MainColorMatchEnabled && _mainAlbumPrimary != default;
            if (!colormatchCycle && !IsAnimatedPlaybarStyle(style))
                return;

            var accentBrush = TryFindResource("PlaybarAccentColor") as SolidColorBrush
                ?? TryFindResource("AccentColor") as SolidColorBrush
                ?? Brushes.CornflowerBlue;
            var secondaryBrush = TryFindResource("PlaybarSecondaryColor") as SolidColorBrush
                ?? accentBrush;
            var tertiaryBrush = TryFindResource("PlaybarTertiaryColor") as SolidColorBrush
                ?? secondaryBrush;
            Color accent = accentBrush.Color;
            Color secondary = secondaryBrush.Color;
            if (colormatchCycle)
                (accent, secondary) = InterpolatePlaybarCycleColors(accent, secondary, tertiaryBrush.Color, DateTime.UtcNow.TimeOfDay.TotalSeconds);
            double pct = SeekSlider.Maximum > 0 ? SeekSlider.Value / SeekSlider.Maximum : 0;
            RenderPlaybar(PlaybarAnimCanvas, pct, accent, secondary, style, DateTime.UtcNow.TimeOfDay.TotalSeconds);
        }

        /// <summary>
        /// Smooth fade envelope: returns 0.4..1. Fades in over [0..fadeRegion] and out over [1-fadeRegion..1]
        /// using a smooth cubic (smoothstep) curve. High minimum keeps edges visible.
        /// </summary>
        private static double WaveformEnvelope(double t, double fadeRegion)
        {
            double fadeIn = t < fadeRegion ? SmoothStep(t / fadeRegion) : 1.0;
            double fadeOut = t > (1.0 - fadeRegion) ? SmoothStep((1.0 - t) / fadeRegion) : 1.0;
            double env = fadeIn * fadeOut;
            return 0.4 + 0.6 * env; // always at least 40% visible at edges
        }

        /// <summary>
        /// Hermite smoothstep: 3t^2 - 2t^3 for smooth [0..1] transition.
        /// </summary>
        private static double SmoothStep(double x)
        {
            x = Math.Clamp(x, 0.0, 1.0);
            return x * x * (3.0 - 2.0 * x);
        }

        private void UpdateWaveformProgress()
        {
            if (ThemeManager.MainPlaybarAnimationStyle == PlaybarAnimationStyle.Wave)
            {
                if (!_waveformAnimActive && _player.CurrentFile != null)
                    DrawWaveformBackground();
                return;
            }

            RenderPlaybarAnim();
        }
    }
}
