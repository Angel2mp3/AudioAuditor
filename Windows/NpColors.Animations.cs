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
    // NP animated-background modes (Color Drift, Stars, Rain, Snow, Leaves) and
    // their particle/scheduler infrastructure. Extracted verbatim from NpColors.cs
    // (2026-06-05 large-file split). Underwater lives in NpColors.Underwater.cs.
    public partial class MainWindow
    {
        // ─── NP Animated Background ───

        private double _npBgAngle;

        // The gradient brush Color Drift owns and animates. Tracking it explicitly
        // means a backdrop/colormatch repaint that swaps NpBgGradient.Background can
        // be detected and reclaimed instead of silently freezing the animation.
        private LinearGradientBrush? _npColorDriftBrush;

        // True once the current particle field's per-shape animations have been
        // Begun. Re-Begin-ing a running DoubleAnimation snaps its transform back
        // to From (0) — that is the visible "particles fly sideways" jump on a
        // slider change or track change. We only Begin on a fresh (dirty) field.
        private bool _npParticleFieldStarted;

        /// <summary>
        /// Color Drift (the animated gradient "glow" background) is on when either the
        /// mode picker is set to it, or the standalone "Color Drift background" toggle is
        /// enabled (which lets it run UNDER a particle effect).
        /// </summary>
        private bool NpIsColorDriftActive()
        {
            string mode = ThemeManager.NormalizeNpBackgroundAnimationMode(ThemeManager.NpBackgroundAnimationMode);
            return mode == "Color Drift" || ThemeManager.NpColorDriftBackgroundEnabled;
        }

        private void NpStartBgAnimation()
        {
            if (!AnimationPolicy.IsMotionAllowed(AnimationArea.NpBackground)) return;
            if (!IsNowPlayingUiActive()) return;
            NpUpdateBackgroundCycleTimer();

            string mode = ThemeManager.NormalizeNpBackgroundAnimationMode(ThemeManager.NpBackgroundAnimationMode);
            bool colorDrift = NpIsColorDriftActive();

            // Particle layer (NpStarsCanvas) — independent of the gradient layer below.
            if (mode is "Stars" or "Rain" or "Snow" or "Leaves" or "Underwater")
                NpStartParticleField(mode);
            else
                NpStopStarfield();

            // Gradient "glow" layer (NpBgGradient). Runs on its own timer and can sit
            // under a particle field. When it's off, restore the static background so a
            // previous drift gradient doesn't linger as an imprint.
            if (colorDrift)
            {
                NpStartColorDrift();
            }
            else
            {
                NpStopColorDrift();
                NpRestoreStaticBackground();
            }
        }

        private void NpStartColorDrift()
        {
            NpEnsureColorDriftBrush();
            if (_npBgAnimTimer != null) return;
            _npBgAngle = 45;
            // Start at the throttled cadence if the frame-synced lyric loop is already live.
            _npBgAnimTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(_npLyricRendering ? 100 : 50) };
            _npBgAnimTimer.Tick += NpBgAnim_Tick;
            _npBgAnimTimer.Start();
        }

        private void NpStopColorDrift()
        {
            _npBgAnimTimer?.Stop();
            _npBgAnimTimer = null;
            _npColorDriftBrush = null;
        }

        /// <summary>
        /// Repaints NpBgGradient to its correct non-drift state so a stale Color Drift
        /// gradient doesn't stay imprinted after switching effects. ColorMatch on →
        /// re-derive the album gradient; off → clear to transparent (particles/backdrop
        /// provide the visuals).
        /// </summary>
        private void NpRestoreStaticBackground()
        {
            if (_npColorMatchEnabled)
            {
                NpApplyColorMatchMode(); // re-derives the album gradient + its overlay (alpha 20)
            }
            else
            {
                NpBgGradient.Background = System.Windows.Media.Brushes.Transparent;
                NpBgGradient.Opacity = 0.6;
                // The drift tick mutates NpDarkOverlay's alpha; restore the static (no-ColorMatch)
                // overlay so turning Color Drift off doesn't leave a darker/altered shade behind.
                NpDarkOverlay.Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(102, 0, 0, 0));
            }
        }

        private void NpStopBgAnimation()
        {
            _npBackgroundCycleTimer?.Stop();
            NpStopColorDrift();
            NpStopStarfield();
        }

        private void NpBgAnim_Tick(object? sender, EventArgs e)
        {
            if (!AnimationPolicy.IsMotionAllowed(AnimationArea.NpBackground))
            {
                NpStopBgAnimation();
                return;
            }
            if (!IsNowPlayingUiActive())
            {
                NpStopBgAnimation();
                return;
            }

            // If Color Drift was turned off (mode changed or toggle cleared) but a path failed to
            // stop the timer, stop it here and restore the static background — otherwise the tick
            // keeps reclaiming the drift brush and the effect "won't turn off".
            if (!NpIsColorDriftActive())
            {
                NpStopColorDrift();
                NpRestoreStaticBackground();
                return;
            }

            // If a backdrop/colormatch repaint swapped our brush out, reclaim it so
            // Color Drift keeps animating instead of silently dying.
            if (!ReferenceEquals(NpBgGradient.Background, _npColorDriftBrush) || _npColorDriftBrush == null)
                NpEnsureColorDriftBrush();

            if (NpBgGradient.Background is LinearGradientBrush lgb && !lgb.IsFrozen)
            {
                double speed = ThemeManager.ClampNpBackgroundAnimationSpeed(ThemeManager.NpBackgroundAnimationSpeed);
                _npBgAngle = (_npBgAngle + 0.15 * speed) % 360;
                double rad = _npBgAngle * Math.PI / 180.0;
                double cos = Math.Cos(rad), sin = Math.Sin(rad);
                lgb.StartPoint = new System.Windows.Point(0.5 - cos * 0.5, 0.5 - sin * 0.5);
                lgb.EndPoint = new System.Windows.Point(0.5 + cos * 0.5, 0.5 + sin * 0.5);
                // Only wobble the middle stop of the default 3-stop gradient. Multi-stop
                // eyedropper-pick gradients have evenly-spaced stops whose positions carry the
                // colors, so nudging stop[1] there would visibly distort them — the angle
                // rotation alone animates those.
                if (lgb.GradientStops.Count == 3)
                    lgb.GradientStops[1].Offset = 0.50 + Math.Sin(rad * 0.75) * 0.08;
                // NOTE: do NOT darken NpDarkOverlay here. Color Drift must look identical to the
                // static colormatch background (same brightness) — it only animates the gradient's
                // motion. The overlay strength is owned by NpApplyColorMatchMode (brightness setting).
            }
        }

        private void NpEnsureColorDriftBrush()
        {
            // Already installed and live — nothing to do.
            if (_npColorDriftBrush != null
                && ReferenceEquals(NpBgGradient.Background, _npColorDriftBrush)
                && !_npColorDriftBrush.IsFrozen)
                return;

            // When the user has eyedropper picks for this track, drive the drift gradient from the
            // full pick set (same stops as the static manual-pick background) so configured picks
            // beyond the first three still animate, rather than collapsing to the derived bg color.
            if (NpHasColorPickerOverridesForCurrentTrack())
            {
                var pickStops = NpBuildPickGradientStops();
                if (pickStops != null)
                {
                    var pickBrush = new LinearGradientBrush(pickStops, new Point(0, 0), new Point(1, 1));
                    _npColorDriftBrush = pickBrush;
                    NpBgGradient.Background = pickBrush;
                    return;
                }
            }

            Color first;
            Color second;
            Color mid;
            if (NpGetEffectiveColorMatchPalette(out _, out _, out _, out var bg, out _))
            {
                // ColorMatch ON: match the STATIC colormatch background gradient exactly (same
                // album-background colors + alpha as ApplyNpExtractedColors), so Color Drift looks
                // identical to the regular colormatch background — it only animates, never changes
                // the colors/brightness.
                first = Color.FromArgb(220, bg.R, bg.G, bg.B);
                second = Color.FromArgb(200, (byte)(bg.R / 4), (byte)(bg.G / 4), (byte)(bg.B / 4));
                mid = Color.FromArgb(210,
                    (byte)((first.R + second.R) / 2),
                    (byte)((first.G + second.G) / 2),
                    (byte)((first.B + second.B) / 2));
            }
            else
            {
                // ColorMatch OFF: the drift gradient follows the theme's visualizer colors.
                var colors = ThemeManager.GetVisualizerColors().ProgressGradient;
                first = colors.Length > 0 ? Color.FromArgb(105, colors[0].R, colors[0].G, colors[0].B) : Color.FromArgb(105, 90, 90, 90);
                second = colors.Length > 1 ? Color.FromArgb(105, colors[1].R, colors[1].G, colors[1].B) : Color.FromArgb(105, 130, 130, 130);
                mid = Color.FromArgb(90,
                    (byte)((first.R + second.R) / 2),
                    (byte)((first.G + second.G) / 2),
                    (byte)((first.B + second.B) / 2));
            }

            var brush = new LinearGradientBrush(
                new GradientStopCollection
                {
                    new(first, 0.0),
                    new(mid, 0.5),
                    new(second, 1.0)
                },
                new Point(0, 0),
                new Point(1, 1));
            _npColorDriftBrush = brush;
            NpBgGradient.Background = brush;
        }

        private static readonly string[] NpBackgroundCycleModes = ["Color Drift", "Stars", "Rain", "Snow", "Leaves", "Underwater"];

        private void NpUpdateBackgroundCycleTimer()
        {
            if (!AnimationPolicy.IsMotionAllowed(AnimationArea.NpBackground) || !IsNowPlayingUiActive() || !ThemeManager.NpBackgroundCycleEnabled)
            {
                _npBackgroundCycleTimer?.Stop();
                return;
            }

            _npBackgroundCycleTimer ??= new DispatcherTimer();
            _npBackgroundCycleTimer.Tick -= NpBackgroundCycleTimer_Tick;
            _npBackgroundCycleTimer.Tick += NpBackgroundCycleTimer_Tick;
            _npBackgroundCycleTimer.Interval = TimeSpan.FromSeconds(45.0 / Math.Clamp(ThemeManager.NpBackgroundCycleSpeed, 0.25, 3.0));
            if (!_npBackgroundCycleTimer.IsEnabled)
                _npBackgroundCycleTimer.Start();
        }

        private void NpBackgroundCycleTimer_Tick(object? sender, EventArgs e) =>
            NpAdvanceBackgroundAnimation();

        private void NpAdvanceBackgroundAnimation()
        {
            string current = ThemeManager.NormalizeNpBackgroundAnimationMode(ThemeManager.NpBackgroundAnimationMode);
            int idx = Array.IndexOf(NpBackgroundCycleModes, current);
            ThemeManager.NpBackgroundAnimationMode = NpBackgroundCycleModes[(idx + 1) % NpBackgroundCycleModes.Length];
            ThemeManager.SavePlayOptions();
            NpRefreshBackdropFromSettings();
        }

        private void NpAdvanceBackgroundAnimationOnSongChange()
        {
            if (ThemeManager.NpBackgroundCycleOnSongChange)
                NpAdvanceBackgroundAnimation();
        }

        // Unified entry point for the per-particle background modes (Stars / Rain / Snow).
        private void NpStartParticleField(string mode)
        {
            _npBgAnimTimer?.Stop();
            _npBgAnimTimer = null;
            if (NpStarsCanvas == null || !IsNowPlayingUiActive())
                return;

            double width = Math.Max(NpStarsCanvas.ActualWidth, ActualWidth > 0 ? ActualWidth : 1200);
            double height = Math.Max(NpStarsCanvas.ActualHeight, ActualHeight > 0 ? ActualHeight : 800);
            double density = ThemeManager.ClampNpStarDensity(ThemeManager.NpStarDensity);
            double shootingStarDensity = ThemeManager.ClampNpShootingStarDensity(ThemeManager.NpShootingStarDensity);
            bool shootingStars = ThemeManager.NpShootingStarsEnabled;
            string paletteKey = NpGetBackgroundAnimationPaletteKey();

            bool dirty = _npBgFxMode != mode ||
                _npBgFxPaletteKey != paletteKey ||
                _npStarShapes.Count == 0 ||
                Math.Abs(_npStarfieldWidth - width) > 1 ||
                Math.Abs(_npStarfieldHeight - height) > 1 ||
                Math.Abs(_npStarfieldDensity - density) > 0.001 ||
                Math.Abs(_npStarfieldShootingStarDensity - shootingStarDensity) > 0.001 ||
                _npStarfieldShootingStars != shootingStars;

            if (dirty)
            {
                NpStopParticleTimers();
                _npStarShapes.Clear();
                _npShootingStarPool.Clear();
                _npLightningOverlay = null;
                NpStarsCanvas.Children.Clear();
                _npBgFxMode = mode;
                _npStarfieldWidth = width;
                _npStarfieldHeight = height;
                _npStarfieldDensity = density;
                _npStarfieldShootingStarDensity = shootingStarDensity;
                _npStarfieldShootingStars = shootingStars;
                _npBgFxPaletteKey = paletteKey;

                switch (mode)
                {
                    case "Rain": NpBuildRain(width, height); break;
                    case "Snow": NpBuildSnow(width, height); break;
                    case "Leaves": NpBuildLeaves(width, height); break;
                    case "Underwater": NpBuildUnderwater(width, height); break;
                    default: NpBuildStarfield(width, height); break;
                }
                _npParticleFieldStarted = false;
            }

            NpStarsCanvas.Opacity = mode switch
            {
                "Rain" => 0.62,
                "Leaves" => 0.64,
                "Underwater" => 0.72,
                _ => 0.58
            };
            double speed = ThemeManager.ClampNpBackgroundAnimationSpeed(ThemeManager.NpBackgroundAnimationSpeed);

            // Only Begin the per-shape clocks on a fresh field. On a non-dirty
            // restart (slider tweak, track change, same-mode refresh) the
            // particles are already animating — re-Begin-ing would reset every
            // transform to 0 and the field would visibly jump sideways.
            if (!_npParticleFieldStarted)
            {
                foreach (var shape in _npStarShapes)
                {
                    if (shape is FrameworkElement { Tag: NpParticle p })
                        p.Begin(shape, speed, _npGlowRandomRng);
                }
                _npParticleFieldStarted = true;

                if (mode == "Stars" && shootingStars)
                    NpStartShootingStarScheduler(width, height, shootingStarDensity, speed);
                else if (mode == "Rain" && ThemeManager.NpRainLightningEnabled && ThemeManager.NpRainLightningAmount > 0.001)
                    NpStartLightningScheduler(width, height, ThemeManager.ClampNpRainLightningAmount(ThemeManager.NpRainLightningAmount), speed);
                else if (mode == "Underwater" && ThemeManager.NpUnderwaterFishEnabled)
                    NpStartFishScheduler(width, height, speed);
            }
        }

        // Lightweight per-particle animation descriptor. Stored on each shape's Tag so the
        // builder and the start pass stay decoupled and we can rebuild cleanly.
        private sealed class NpParticle
        {
            public required Action<UIElement, double, Random> Begin;
        }

        // Cache of frozen radial "glow" brushes keyed by base star color so the
        // bright subset gets a soft-glow look WITHOUT a per-element BlurEffect
        // (the BlurEffect ×N was the dominant cause of starfield lag).
        private readonly Dictionary<Color, RadialGradientBrush> _npStarGlowBrushes = new();

        private RadialGradientBrush NpGetStarGlowBrush(Color color)
        {
            if (_npStarGlowBrushes.TryGetValue(color, out var cached))
                return cached;
            var brush = new RadialGradientBrush
            {
                GradientStops =
                {
                    new GradientStop(Color.FromArgb(255, color.R, color.G, color.B), 0.0),
                    new GradientStop(Color.FromArgb(150, color.R, color.G, color.B), 0.45),
                    new GradientStop(Color.FromArgb(0, color.R, color.G, color.B), 1.0)
                }
            };
            brush.Freeze();
            _npStarGlowBrushes[color] = brush;
            return brush;
        }

        private string NpGetBackgroundAnimationPaletteKey()
        {
            if (!NpTryResolveBackgroundAnimationPalette(out var primary, out var secondary, out var tertiary))
                return "fixed";
            return $"{primary}-{secondary}-{tertiary}";
        }

        private bool NpTryResolveBackgroundAnimationPalette(out Color primary, out Color secondary, out Color tertiary)
        {
            primary = default;
            secondary = default;
            tertiary = default;
            if (!ThemeManager.NpBackgroundUseAlbumColors)
                return false;
            if (!NpTryResolveActiveColorMatchPalette(out var p, out var s, out var t, out _))
                return false;
            primary = EnsureMinLuminance(p, 120);
            secondary = EnsureMinLuminance(s, 110);
            tertiary = EnsureMinLuminance(t == default ? s : t, 100);
            return true;
        }

        private void NpBuildStarfield(double width, double height)
        {
            double density = _npStarfieldDensity;
            int starCount = (int)Math.Clamp(width * height / 9000.0 * density, 32, 320);
            Color[] starColors = NpTryResolveBackgroundAnimationPalette(out var starPrimary, out var starSecondary, out var starTertiary)
                ? [starPrimary, starSecondary, starTertiary, Color.FromArgb(255, Math.Max(starPrimary.R, starSecondary.R), Math.Max(starPrimary.G, starSecondary.G), Math.Max(starPrimary.B, starSecondary.B))]
                :
                [
                    Color.FromRgb(255, 255, 255),
                    Color.FromRgb(205, 224, 255),
                    Color.FromRgb(255, 228, 180),
                    Color.FromRgb(235, 205, 255),
                    Color.FromRgb(200, 245, 255)
                ];

            // Cap perpetual clocks: only a bounded subset twinkles, and a smaller
            // subset of those drifts. The rest are painted once at a fixed opacity
            // so the field stays dense without hundreds of Forever animations.
            int maxAnimated = Math.Min(starCount, Math.Max(96, (int)(starCount * 0.72)));
            int maxDrift = Math.Min(maxAnimated, Math.Max(40, (int)(starCount * 0.28)));

            for (int i = 0; i < starCount; i++)
            {
                bool bright = _npGlowRandomRng.NextDouble() < 0.14;
                double size = bright
                    ? 2.6 + _npGlowRandomRng.NextDouble() * 3.6
                    : 0.7 + _npGlowRandomRng.NextDouble() * 2.6;
                var color = starColors[_npGlowRandomRng.Next(starColors.Length)];
                bool animate = i < maxAnimated || _npGlowRandomRng.NextDouble() < 0.35;
                bool drift = animate && (i < maxDrift || _npGlowRandomRng.NextDouble() < 0.18);
                double driftSpeed = drift
                    ? (8 + _npGlowRandomRng.NextDouble() * 22)
                    : 0;
                double dimA = 0.10 + _npGlowRandomRng.NextDouble() * 0.18;
                double brightA = (bright ? 0.78 : 0.45) + _npGlowRandomRng.NextDouble() * 0.24;
                double pulseMs = 1500 + _npGlowRandomRng.Next(3200);
                double phaseDelay = _npGlowRandomRng.Next(3200);

                var star = new System.Windows.Shapes.Ellipse
                {
                    Width = size,
                    Height = size,
                    // Bright stars: cached frozen radial brush gives the soft glow
                    // look the BlurEffect used to. Rest: flat solid fill.
                    Fill = bright
                        ? NpGetStarGlowBrush(color)
                        : new SolidColorBrush(Color.FromArgb(
                            (byte)(150 + _npGlowRandomRng.Next(95)),
                            color.R, color.G, color.B)),
                    // Static stars sit at a mid opacity so they read well without a clock.
                    Opacity = animate ? dimA : Math.Min(0.52, (dimA + brightA) * 0.65)
                };
                double startX = _npGlowRandomRng.NextDouble() * width;
                double startY = _npGlowRandomRng.NextDouble() * height;
                Canvas.SetLeft(star, startX);
                Canvas.SetTop(star, startY);

                if (animate)
                {
                    star.Tag = new NpParticle
                    {
                        Begin = (el, spd, rng) =>
                        {
                            el.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation
                            {
                                From = dimA,
                                To = brightA,
                                Duration = TimeSpan.FromMilliseconds(pulseMs / spd),
                                BeginTime = TimeSpan.FromMilliseconds(phaseDelay / spd),
                                AutoReverse = true,
                                RepeatBehavior = RepeatBehavior.Forever,
                                EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
                            });
                            if (driftSpeed <= 0) return;
                            var t = new TranslateTransform();
                            el.RenderTransform = t;
                            double dx = (rng.NextDouble() - 0.5) * driftSpeed;
                            double dy = (rng.NextDouble() - 0.5) * driftSpeed;
                            double durMs = (16000 + rng.Next(14000)) / spd;
                            t.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(0, dx, TimeSpan.FromMilliseconds(durMs))
                            { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut } });
                            t.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(0, dy, TimeSpan.FromMilliseconds(durMs * 1.3))
                            { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut } });
                        }
                    };
                }

                NpStarsCanvas.Children.Add(star);
                _npStarShapes.Add(star);
            }
        }

        // One shared, frozen vertical fade reused by every rain streak. Depth is
        // expressed via per-Line Opacity so we no longer allocate a brush per drop.
        private LinearGradientBrush? _npRainStrokeBrush;

        private LinearGradientBrush NpGetRainStrokeBrush()
        {
            if (_npRainStrokeBrush != null) return _npRainStrokeBrush;
            var b = new LinearGradientBrush(
                Color.FromArgb(0, 200, 222, 255),
                Color.FromArgb(255, 215, 232, 255),
                new Point(0.5, 0), new Point(0.5, 1));
            b.Freeze();
            _npRainStrokeBrush = b;
            return b;
        }

        private void NpBuildRain(double width, double height)
        {
            double intensity = ThemeManager.ClampNpRainIntensity(ThemeManager.NpRainIntensity);
            // Was width/5 (up to 520 streaks × 2 forever-animations each) — the single biggest
            // source of rain-mode lag. Fewer streaks + one animation per streak (see below).
            int dropCount = (int)Math.Clamp(width / 8.0 * intensity, 30, 260);
            double wind = -7 - _npGlowRandomRng.NextDouble() * 5; // slight slant
            Brush rainBrush = NpGetRainStrokeBrush();
            if (NpTryResolveBackgroundAnimationPalette(out var rainPrimary, out var rainSecondary, out _))
            {
                rainBrush = new LinearGradientBrush(
                    Color.FromArgb(0, rainPrimary.R, rainPrimary.G, rainPrimary.B),
                    Color.FromArgb(255, rainSecondary.R, rainSecondary.G, rainSecondary.B),
                    new Point(0.5, 0), new Point(0.5, 1));
                rainBrush.Freeze();
            }

            for (int i = 0; i < dropCount; i++)
            {
                double len = 12 + _npGlowRandomRng.NextDouble() * 26;
                double thick = 0.8 + _npGlowRandomRng.NextDouble() * 1.1;
                double depth = _npGlowRandomRng.NextDouble(); // 0 far .. 1 near
                byte a = (byte)(60 + depth * 130);
                var drop = new System.Windows.Shapes.Line
                {
                    X1 = 0,
                    Y1 = 0,
                    X2 = wind * 0.4, // static slant — carries the wind look now that the X drift animation is gone
                    Y2 = len,
                    StrokeThickness = thick,
                    StrokeStartLineCap = PenLineCap.Round,
                    StrokeEndLineCap = PenLineCap.Round,
                    Stroke = rainBrush,
                    Opacity = a / 255.0
                };
                double x = _npGlowRandomRng.NextDouble() * (width + 80) - 40;
                double startY = -_npGlowRandomRng.NextDouble() * height - len;
                Canvas.SetLeft(drop, x);
                Canvas.SetTop(drop, startY);
                var t = new TranslateTransform();
                drop.RenderTransform = t;

                double fallPx = height + len + 60 - startY;
                double baseMs = 1500 - depth * 800; // near = faster
                drop.Tag = new NpParticle
                {
                    Begin = (el, spd, rng) =>
                    {
                        double durMs = baseMs / Math.Max(0.35, spd);
                        var tt = ((FrameworkElement)el).RenderTransform as TranslateTransform;
                        if (tt == null) return;
                        // Single fall animation per streak (half the animation clocks of the old
                        // two-axis version). The wind slant is baked into the streak geometry (X2).
                        tt.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(0, fallPx, TimeSpan.FromMilliseconds(durMs))
                        { BeginTime = TimeSpan.FromMilliseconds(rng.Next((int)Math.Max(1, durMs))), RepeatBehavior = RepeatBehavior.Forever });
                    }
                };
                NpStarsCanvas.Children.Add(drop);
                _npStarShapes.Add(drop);
            }

            // Lightning flash overlay (kept hidden; the scheduler flickers it).
            var flash = new System.Windows.Shapes.Rectangle
            {
                Width = width,
                Height = height,
                Fill = new SolidColorBrush(Color.FromRgb(226, 234, 255)),
                Opacity = 0,
                IsHitTestVisible = false
            };
            Canvas.SetLeft(flash, 0);
            Canvas.SetTop(flash, 0);
            NpStarsCanvas.Children.Add(flash);
            _npLightningOverlay = flash;
        }

        private void NpBuildSnow(double width, double height)
        {
            double density = ThemeManager.ClampNpSnowDensity(ThemeManager.NpSnowDensity);
            double flake = ThemeManager.ClampNpSnowflakeAmount(ThemeManager.NpSnowflakeAmount);
            int flakeCount = (int)Math.Clamp(width * height / 11000.0 * density, 28, 360);
            Color[] snowColors = NpTryResolveBackgroundAnimationPalette(out var snowPrimary, out var snowSecondary, out var snowTertiary)
                ? [snowPrimary, snowSecondary, snowTertiary]
                : [Color.FromRgb(255, 255, 255)];

            for (int i = 0; i < flakeCount; i++)
            {
                double depth = _npGlowRandomRng.NextDouble();
                double size = (1.6 + depth * 4.0) * (0.7 + flake * 0.6);
                byte a = (byte)(120 + depth * 120);
                var snow = new System.Windows.Shapes.Ellipse
                {
                    Width = size,
                    Height = size,
                    // Soft edge via a single cached radial brush; depth is conveyed
                    // through element opacity instead of a per-flake BlurEffect.
                    Fill = NpGetStarGlowBrush(snowColors[_npGlowRandomRng.Next(snowColors.Length)]),
                    Opacity = a / 255.0
                };
                double x = _npGlowRandomRng.NextDouble() * width;
                double startY = -_npGlowRandomRng.NextDouble() * height - size;
                Canvas.SetLeft(snow, x);
                Canvas.SetTop(snow, startY);
                // Fall (Y) and sway (X) act on independent axes, so one TranslateTransform
                // carries both — half the animation clocks and no TransformGroup per flake,
                // for identical motion.
                var move = new TranslateTransform();
                snow.RenderTransform = move;

                double fallPx = height + size + 50 - startY;
                double baseMs = 9000 - depth * 4200;
                double swayAmp = 14 + _npGlowRandomRng.NextDouble() * 30;
                double swayMs = 2600 + _npGlowRandomRng.Next(2600);
                double swayDelay = _npGlowRandomRng.Next(3000);
                snow.Tag = new NpParticle
                {
                    Begin = (el, spd, rng) =>
                    {
                        var t = ((FrameworkElement)el).RenderTransform as TranslateTransform;
                        if (t == null) return;
                        double durMs = baseMs / Math.Max(0.35, spd);
                        t.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(0, fallPx, TimeSpan.FromMilliseconds(durMs))
                        { BeginTime = TimeSpan.FromMilliseconds(rng.Next((int)Math.Max(1, durMs))), RepeatBehavior = RepeatBehavior.Forever });
                        t.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(-swayAmp, swayAmp, TimeSpan.FromMilliseconds(swayMs / Math.Max(0.35, spd)))
                        { BeginTime = TimeSpan.FromMilliseconds(swayDelay), AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut } });
                    }
                };
                NpStarsCanvas.Children.Add(snow);
                _npStarShapes.Add(snow);
            }
        }

        private void NpBuildLeaves(double width, double height)
        {
            double density = ThemeManager.ClampNpSnowDensity(ThemeManager.NpSnowDensity);
            int leafCount = (int)Math.Clamp(width * height / 14500.0 * density, 24, 260);
            Color[] leafColors = NpTryResolveBackgroundAnimationPalette(out var primary, out var secondary, out var tertiary)
                ? [primary, secondary, tertiary]
                : [Color.FromRgb(190, 118, 36), Color.FromRgb(212, 151, 55), Color.FromRgb(126, 88, 35)];

            for (int i = 0; i < leafCount; i++)
            {
                double depth = _npGlowRandomRng.NextDouble();
                double widthPx = 7 + depth * 9;
                double heightPx = 4 + depth * 6;
                var color = leafColors[_npGlowRandomRng.Next(leafColors.Length)];
                byte alpha = (byte)(120 + depth * 105);

                var leaf = new System.Windows.Shapes.Path
                {
                    Width = widthPx,
                    Height = heightPx,
                    Stretch = Stretch.Fill,
                    Fill = new SolidColorBrush(Color.FromArgb(alpha, color.R, color.G, color.B)),
                    Data = Geometry.Parse("M 0,0.5 C 0.25,0 0.75,0 1,0.5 C 0.75,1 0.25,1 0,0.5 Z"),
                    Opacity = alpha / 255.0,
                    RenderTransformOrigin = new Point(0.5, 0.5)
                };

                double x = _npGlowRandomRng.NextDouble() * width;
                double startY = -_npGlowRandomRng.NextDouble() * height - heightPx;
                Canvas.SetLeft(leaf, x);
                Canvas.SetTop(leaf, startY);

                var group = new TransformGroup();
                var fall = new TranslateTransform();
                var sway = new TranslateTransform();
                var rotate = new RotateTransform(_npGlowRandomRng.NextDouble() * 360);
                group.Children.Add(fall);
                group.Children.Add(sway);
                group.Children.Add(rotate);
                leaf.RenderTransform = group;

                double fallPx = height + heightPx + 80 - startY;
                double baseMs = 10500 - depth * 4700;
                double swayAmp = 24 + _npGlowRandomRng.NextDouble() * 52;
                double swayMs = 2200 + _npGlowRandomRng.Next(3200);
                double spinMs = 1800 + _npGlowRandomRng.Next(2600);
                double delayMs = _npGlowRandomRng.Next(5000);

                leaf.Tag = new NpParticle
                {
                    Begin = (el, spd, rng) =>
                    {
                        double speed = Math.Max(0.35, spd);
                        double fallMs = baseMs / speed;
                        fall.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(0, fallPx, TimeSpan.FromMilliseconds(fallMs))
                        {
                            BeginTime = TimeSpan.FromMilliseconds(rng.Next((int)Math.Max(1, fallMs))),
                            RepeatBehavior = RepeatBehavior.Forever
                        });
                        sway.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(-swayAmp, swayAmp, TimeSpan.FromMilliseconds(swayMs / speed))
                        {
                            BeginTime = TimeSpan.FromMilliseconds(delayMs),
                            AutoReverse = true,
                            RepeatBehavior = RepeatBehavior.Forever,
                            EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
                        });
                        rotate.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(0, 360, TimeSpan.FromMilliseconds(spinMs / speed))
                        {
                            BeginTime = TimeSpan.FromMilliseconds(delayMs * 0.5),
                            RepeatBehavior = RepeatBehavior.Forever
                        });
                    }
                };

                NpStarsCanvas.Children.Add(leaf);
                _npStarShapes.Add(leaf);
            }
        }

        private Canvas NpCreateShootingStar(double length, double thickness, double headSize, double angle)
        {
            var meteor = new Canvas
            {
                Width = length + headSize * 2,
                Height = headSize * 5,
                Opacity = 0,
                Tag = "ShootingStar",
                RenderTransform = new TransformGroup
                {
                    Children =
                    {
                        new RotateTransform(angle, length, headSize * 2.5),
                        new TranslateTransform()
                    }
                }
            };

            double centerY = meteor.Height / 2;
            var tailBrush = new LinearGradientBrush
            {
                StartPoint = new Point(0, 0.5),
                EndPoint = new Point(1, 0.5),
                GradientStops =
                {
                    new GradientStop(Color.FromArgb(0, 210, 230, 255), 0.0),
                    new GradientStop(Color.FromArgb(42, 190, 220, 255), 0.32),
                    new GradientStop(Color.FromArgb(150, 230, 244, 255), 0.78),
                    new GradientStop(Color.FromArgb(235, 255, 255, 255), 1.0)
                }
            };
            var tail = new System.Windows.Shapes.Line
            {
                X1 = 0,
                Y1 = centerY,
                X2 = length,
                Y2 = centerY,
                StrokeThickness = thickness,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
                Stroke = tailBrush
            };
            var headGlow = new System.Windows.Shapes.Ellipse
            {
                Width = headSize * 2.3,
                Height = headSize * 2.3,
                Fill = new RadialGradientBrush
                {
                    GradientStops =
                    {
                        new GradientStop(Color.FromArgb(210, 255, 255, 255), 0.0),
                        new GradientStop(Color.FromArgb(115, 180, 220, 255), 0.38),
                        new GradientStop(Color.FromArgb(0, 180, 220, 255), 1.0)
                    }
                }
            };
            var head = new System.Windows.Shapes.Ellipse
            {
                Width = headSize,
                Height = headSize,
                Fill = new SolidColorBrush(Color.FromArgb(245, 255, 255, 255))
            };

            Canvas.SetLeft(tail, headSize);
            Canvas.SetTop(tail, 0);
            Canvas.SetLeft(headGlow, headSize + length - headGlow.Width / 2);
            Canvas.SetTop(headGlow, centerY - headGlow.Height / 2);
            Canvas.SetLeft(head, headSize + length - head.Width / 2);
            Canvas.SetTop(head, centerY - head.Height / 2);
            meteor.Children.Add(tail);
            meteor.Children.Add(headGlow);
            meteor.Children.Add(head);
            return meteor;
        }

        // Spawns sporadic single-pass meteors instead of an always-on looping conveyor.
        private void NpStartShootingStarScheduler(double width, double height, double density, double speed)
        {
            _npShootingStarTimer?.Stop();
            _npShootingStarTimer = new DispatcherTimer();

            void ScheduleNext()
            {
                // Higher density / speed => shorter gaps between streaks.
                double meanMs = Math.Clamp(11000.0 / Math.Max(0.25, density) / Math.Max(0.4, speed), 900, 22000);
                double jitter = meanMs * (0.45 + _npGlowRandomRng.NextDouble() * 1.1);
                _npShootingStarTimer!.Interval = TimeSpan.FromMilliseconds(jitter);
            }

            _npShootingStarTimer.Tick += (_, _) =>
            {
                if (!IsNowPlayingUiActive() ||
                    ThemeManager.NormalizeNpBackgroundAnimationMode(ThemeManager.NpBackgroundAnimationMode) != "Stars")
                {
                    NpStopParticleTimers();
                    return;
                }
                NpSpawnShootingStar(width, height,
                    ThemeManager.ClampNpBackgroundAnimationSpeed(ThemeManager.NpBackgroundAnimationSpeed));
                if (density > 2.4 && _npGlowRandomRng.NextDouble() < 0.4)
                    NpSpawnShootingStar(width, height,
                        ThemeManager.ClampNpBackgroundAnimationSpeed(ThemeManager.NpBackgroundAnimationSpeed));
                ScheduleNext();
            };
            ScheduleNext();
            // A first streak fairly soon so the effect is visible without a long wait.
            _npShootingStarTimer.Interval = TimeSpan.FromMilliseconds(500 + _npGlowRandomRng.Next(2200));
            _npShootingStarTimer.Start();
        }

        private void NpSpawnShootingStar(double width, double height, double speed)
        {
            if (NpStarsCanvas == null) return;

            Canvas? meteor = _npShootingStarPool.FirstOrDefault(m => m.Tag as string == "ShootingStarIdle");
            if (meteor == null)
            {
                double length = 120 + _npGlowRandomRng.NextDouble() * 160;
                double thickness = 1.3 + _npGlowRandomRng.NextDouble() * 1.6;
                double headSize = thickness * (3.0 + _npGlowRandomRng.NextDouble() * 1.4);
                double angle = 16 + _npGlowRandomRng.NextDouble() * 20; // downward-right
                meteor = NpCreateShootingStar(length, thickness, headSize, angle);
                NpStarsCanvas.Children.Insert(0, meteor);
                _npShootingStarPool.Add(meteor);
            }
            meteor.Tag = "ShootingStarActive";

            double startX = _npGlowRandomRng.NextDouble() * width * 0.7 - meteor.Width * 0.3;
            double startY = _npGlowRandomRng.NextDouble() * height * 0.45 - meteor.Height;
            Canvas.SetLeft(meteor, startX);
            Canvas.SetTop(meteor, startY);

            var transform = (meteor.RenderTransform as TransformGroup)?
                .Children.OfType<TranslateTransform>().FirstOrDefault();
            if (transform == null) return;

            double durMs = (1500 + _npGlowRandomRng.Next(1400)) / Math.Max(0.4, speed);
            double travelX = width * (0.55 + _npGlowRandomRng.NextDouble() * 0.5);
            double travelY = travelX * (0.42 + _npGlowRandomRng.NextDouble() * 0.18);

            var ease = new QuadraticEase { EasingMode = EasingMode.EaseIn };
            transform.BeginAnimation(TranslateTransform.XProperty,
                new DoubleAnimation(0, travelX, TimeSpan.FromMilliseconds(durMs)) { EasingFunction = ease });
            transform.BeginAnimation(TranslateTransform.YProperty,
                new DoubleAnimation(0, travelY, TimeSpan.FromMilliseconds(durMs)) { EasingFunction = ease });

            var fade = new DoubleAnimationUsingKeyFrames
            {
                Duration = TimeSpan.FromMilliseconds(durMs),
                KeyFrames =
                {
                    new DiscreteDoubleKeyFrame(0, KeyTime.FromPercent(0)),
                    new LinearDoubleKeyFrame(0.95, KeyTime.FromPercent(0.14)),
                    new LinearDoubleKeyFrame(0.75, KeyTime.FromPercent(0.5)),
                    new LinearDoubleKeyFrame(0.0, KeyTime.FromPercent(1))
                }
            };
            fade.Completed += (_, _) =>
            {
                meteor.Opacity = 0;
                meteor.Tag = "ShootingStarIdle";
            };
            meteor.BeginAnimation(UIElement.OpacityProperty, fade);
        }

        private void NpStartLightningScheduler(double width, double height, double amount, double speed)
        {
            _npLightningTimer?.Stop();
            _npLightningTimer = new DispatcherTimer();

            void ScheduleNext()
            {
                double meanMs = Math.Clamp(16000.0 / Math.Max(0.05, amount), 4500, 90000);
                double jitter = meanMs * (0.5 + _npGlowRandomRng.NextDouble() * 1.2);
                _npLightningTimer!.Interval = TimeSpan.FromMilliseconds(jitter);
            }

            _npLightningTimer.Tick += (_, _) =>
            {
                if (!IsNowPlayingUiActive() ||
                    ThemeManager.NormalizeNpBackgroundAnimationMode(ThemeManager.NpBackgroundAnimationMode) != "Rain" ||
                    !ThemeManager.NpRainLightningEnabled)
                {
                    NpStopParticleTimers();
                    return;
                }
                NpFlashLightning();
                ScheduleNext();
            };
            ScheduleNext();
            _npLightningTimer.Interval = TimeSpan.FromMilliseconds(2500 + _npGlowRandomRng.Next(6000));
            _npLightningTimer.Start();
        }

        private void NpFlashLightning()
        {
            if (_npLightningOverlay == null) return;
            double peak = 0.34 + _npGlowRandomRng.NextDouble() * 0.22;
            var flash = new DoubleAnimationUsingKeyFrames
            {
                Duration = TimeSpan.FromMilliseconds(820),
                KeyFrames =
                {
                    new DiscreteDoubleKeyFrame(0, KeyTime.FromPercent(0)),
                    new LinearDoubleKeyFrame(peak, KeyTime.FromPercent(0.05)),
                    new LinearDoubleKeyFrame(peak * 0.18, KeyTime.FromPercent(0.16)),
                    new LinearDoubleKeyFrame(peak * 0.9, KeyTime.FromPercent(0.24)),
                    new LinearDoubleKeyFrame(0.0, KeyTime.FromPercent(1))
                }
            };
            _npLightningOverlay.BeginAnimation(UIElement.OpacityProperty, flash);
        }

        private void NpStopParticleTimers()
        {
            _npShootingStarTimer?.Stop();
            _npShootingStarTimer = null;
            _npLightningTimer?.Stop();
            _npLightningTimer = null;
            _npFishTimer?.Stop();
            _npFishTimer = null;
            _npSizeDebounceTimer?.Stop();
        }

        // Debounce so a burst of layout passes (track change, lyrics panel
        // show/hide, window drag) coalesces into one rebuild, and ignore tiny
        // sub-threshold deltas entirely — both used to reseed the field mid-flight
        // and make every particle snap sideways.
        private DispatcherTimer? _npSizeDebounceTimer;
        private const double NpSizeRebuildThreshold = 24.0;

        private void NpStarsCanvas_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            string mode = ThemeManager.NormalizeNpBackgroundAnimationMode(ThemeManager.NpBackgroundAnimationMode);
            if (mode is not ("Stars" or "Rain" or "Snow" or "Leaves" or "Underwater"))
                return;
            if (!IsNowPlayingUiActive())
                return;

            // Below-threshold change: leave the running field untouched.
            if (Math.Abs(e.NewSize.Width - _npStarfieldWidth) < NpSizeRebuildThreshold &&
                Math.Abs(e.NewSize.Height - _npStarfieldHeight) < NpSizeRebuildThreshold)
                return;

            _npSizeDebounceTimer?.Stop();
            _npSizeDebounceTimer ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(170) };
            _npSizeDebounceTimer.Tick -= NpSizeDebounce_Tick;
            _npSizeDebounceTimer.Tick += NpSizeDebounce_Tick;
            _npSizeDebounceTimer.Start();
        }

        private void NpSizeDebounce_Tick(object? sender, EventArgs e)
        {
            _npSizeDebounceTimer?.Stop();
            string mode = ThemeManager.NormalizeNpBackgroundAnimationMode(ThemeManager.NpBackgroundAnimationMode);
            if (mode is not ("Stars" or "Rain" or "Snow" or "Leaves" or "Underwater") || !IsNowPlayingUiActive())
                return;
            // NpStartParticleField's own dirty check (size delta) will do the
            // single rebuild now that the size has settled.
            NpStartParticleField(mode);
        }

        private void NpStopStarfield()
        {
            NpStopParticleTimers();
            if (NpStarsCanvas == null) return;
            foreach (var star in _npStarShapes)
            {
                star.BeginAnimation(UIElement.OpacityProperty, null);
                if (star.RenderTransform is TranslateTransform transform)
                {
                    transform.BeginAnimation(TranslateTransform.XProperty, null);
                    transform.BeginAnimation(TranslateTransform.YProperty, null);
                    transform.X = 0;
                    transform.Y = 0;
                }
                else if (star.RenderTransform is TransformGroup group)
                {
                    foreach (var child in group.Children.OfType<TranslateTransform>())
                    {
                        child.BeginAnimation(TranslateTransform.XProperty, null);
                        child.BeginAnimation(TranslateTransform.YProperty, null);
                        child.X = 0;
                        child.Y = 0;
                    }
                    // Underwater seaweed sways via a RotateTransform; clear those
                    // clocks too so switching away from Underwater doesn't leak a
                    // Forever animation (the TranslateTransform-only sweep above
                    // would otherwise miss them).
                    foreach (var rot in group.Children.OfType<RotateTransform>())
                        rot.BeginAnimation(RotateTransform.AngleProperty, null);
                }
            }
            foreach (var meteor in _npShootingStarPool)
            {
                meteor.BeginAnimation(UIElement.OpacityProperty, null);
                meteor.Opacity = 0;
                if (meteor.RenderTransform is TransformGroup g)
                    foreach (var child in g.Children.OfType<TranslateTransform>())
                    {
                        child.BeginAnimation(TranslateTransform.XProperty, null);
                        child.BeginAnimation(TranslateTransform.YProperty, null);
                        child.X = 0;
                        child.Y = 0;
                    }
            }
            _npLightningOverlay?.BeginAnimation(UIElement.OpacityProperty, null);
            if (_npLightningOverlay != null) _npLightningOverlay.Opacity = 0;
            _npBgFxMode = "Off";
            _npParticleFieldStarted = false;
            NpStarsCanvas.Opacity = 0;
        }

    }
}
