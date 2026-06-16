using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using AudioQualityChecker.Services;

namespace AudioQualityChecker
{
    // Underwater background animation for the Now Playing surface.
    //
    // Style: calm deep-sea base (slow rising bubbles + drifting blue/teal light
    // shafts) PLUS aquarium accents (swaying seaweed fronds anchored to the
    // bottom edge and occasional tiny fish silhouettes drifting across).
    //
    // It plugs into the same particle-field pipeline as Stars/Rain/Snow/Leaves:
    //   - NpStartParticleField routes "Underwater" here via NpBuildUnderwater.
    //   - Each animated shape carries an NpParticle on its Tag; the shared start
    //     pass calls Begin(shape, speed, rng) so per-shape clocks only start on a
    //     fresh field (no mid-flight re-Begin "jump").
    //   - Fish are sporadic single-pass swimmers driven by NpStartFishScheduler,
    //     modeled on the shooting-star scheduler.
    //
    // Perf discipline matches the rest of the file: frozen cached brushes, no
    // per-element BlurEffect, bounded element counts, and capped Forever clocks.
    public partial class MainWindow
    {
        // Fixed deep-sea palette used when "use album colors" is off or no album
        // palette is available. Teal/blue-white, tuned to read as calm water.
        private static readonly Color NpUwBubbleColor = Color.FromRgb(198, 240, 255);
        private static readonly Color NpUwCausticColor = Color.FromRgb(120, 205, 230);
        private static readonly Color NpUwSeaweedColor = Color.FromRgb(38, 120, 104);
        private static readonly Color NpUwFishColor = Color.FromRgb(14, 38, 52);

        private void NpBuildUnderwater(double width, double height)
        {
            // Album-tinted palette when enabled, else the fixed deep-sea colors.
            bool album = NpTryResolveBackgroundAnimationPalette(out var p, out var s, out var t);
            Color bubbleColor = album ? EnsureMinLuminance(p, 150) : NpUwBubbleColor;
            Color causticColor = album ? EnsureMinLuminance(s, 110) : NpUwCausticColor;
            Color seaweedColor = album ? EnsureMinLuminance(t == default ? s : t, 70) : NpUwSeaweedColor;

            NpBuildCaustics(width, height, causticColor);
            NpBuildBubbles(width, height, bubbleColor);
            if (ThemeManager.NpUnderwaterSeaweedEnabled)
                NpBuildSeaweed(width, height, seaweedColor);
        }

        // Big, very soft diagonal light shafts that slowly drift sideways and
        // cross-fade on long offset cycles to fake moving wave-light. Kept to a
        // handful of low-opacity rectangles so this stays cheap.
        private void NpBuildCaustics(double width, double height, Color color)
        {
            double intensity = ThemeManager.ClampNpUnderwaterCausticIntensity(ThemeManager.NpUnderwaterCausticIntensity);
            if (intensity <= 0.001) return;

            int shafts = 4;
            double shaftWidth = Math.Max(120, width / 3.2);

            for (int i = 0; i < shafts; i++)
            {
                // Peak opacity scales with the intensity knob; clamped tasteful.
                byte peak = (byte)Math.Clamp(26 * intensity, 0, 70);
                var brush = new LinearGradientBrush
                {
                    StartPoint = new Point(0, 0),
                    EndPoint = new Point(0.35, 1),
                    GradientStops =
                    {
                        new GradientStop(Color.FromArgb(0, color.R, color.G, color.B), 0.0),
                        new GradientStop(Color.FromArgb(peak, color.R, color.G, color.B), 0.5),
                        new GradientStop(Color.FromArgb(0, color.R, color.G, color.B), 1.0)
                    }
                };
                brush.Freeze();

                var shaft = new System.Windows.Shapes.Rectangle
                {
                    Width = shaftWidth,
                    Height = height * 1.6,
                    Fill = brush,
                    Opacity = 0,
                    IsHitTestVisible = false,
                    RenderTransformOrigin = new Point(0.5, 0.5)
                };
                // Spread the shafts across the width, slightly off the top edge.
                double x = (width / shafts) * i - shaftWidth * 0.25;
                Canvas.SetLeft(shaft, x);
                Canvas.SetTop(shaft, -height * 0.3);
                var skew = new SkewTransform(-18, 0);
                var drift = new TranslateTransform();
                shaft.RenderTransform = new TransformGroup { Children = { skew, drift } };

                double driftAmp = 40 + _npGlowRandomRng.NextDouble() * 60;
                double driftMs = 14000 + _npGlowRandomRng.Next(10000);
                double fadeMs = 7000 + _npGlowRandomRng.Next(6000);
                double fadeDelay = _npGlowRandomRng.Next(6000);
                double holdOpacity = (0.45 + _npGlowRandomRng.NextDouble() * 0.4);

                shaft.Tag = new NpParticle
                {
                    Begin = (el, spd, rng) =>
                    {
                        var grp = ((FrameworkElement)el).RenderTransform as TransformGroup;
                        var tt = grp?.Children.OfType<TranslateTransform>().FirstOrDefault();
                        if (tt != null)
                        {
                            tt.BeginAnimation(TranslateTransform.XProperty,
                                new DoubleAnimation(-driftAmp, driftAmp, TimeSpan.FromMilliseconds(driftMs / Math.Max(0.35, spd)))
                                {
                                    AutoReverse = true,
                                    RepeatBehavior = RepeatBehavior.Forever,
                                    EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
                                });
                        }
                        el.BeginAnimation(UIElement.OpacityProperty,
                            new DoubleAnimation(holdOpacity * 0.25, holdOpacity, TimeSpan.FromMilliseconds(fadeMs / Math.Max(0.35, spd)))
                            {
                                BeginTime = TimeSpan.FromMilliseconds(fadeDelay),
                                AutoReverse = true,
                                RepeatBehavior = RepeatBehavior.Forever,
                                EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
                            });
                    }
                };
                NpStarsCanvas.Children.Add(shaft);
                _npStarShapes.Add(shaft);
            }
        }

        // Slowly rising bubbles with a gentle sine wobble (the Snow sway trick).
        // Soft edges come from the shared cached radial "glow" brush; depth is
        // conveyed by size + opacity, not a per-bubble BlurEffect.
        private void NpBuildBubbles(double width, double height, Color color)
        {
            double density = ThemeManager.ClampNpUnderwaterBubbleDensity(ThemeManager.NpUnderwaterBubbleDensity);
            int bubbleCount = (int)Math.Clamp(width * height / 13000.0 * density, 24, 300);

            for (int i = 0; i < bubbleCount; i++)
            {
                double depth = _npGlowRandomRng.NextDouble();           // 0 far .. 1 near
                bool hero = _npGlowRandomRng.NextDouble() < 0.08;       // a few big ones
                double size = hero
                    ? 7 + _npGlowRandomRng.NextDouble() * 7
                    : 1.8 + depth * 4.5;
                byte a = (byte)(70 + depth * 110);

                var bubble = new System.Windows.Shapes.Ellipse
                {
                    Width = size,
                    Height = size,
                    Fill = NpGetStarGlowBrush(color),
                    Opacity = a / 255.0,
                    IsHitTestVisible = false
                };
                double x = _npGlowRandomRng.NextDouble() * width;
                // Start scattered through (and below) the surface so the field is
                // populated immediately rather than rising from an empty bottom.
                double startY = _npGlowRandomRng.NextDouble() * height + size;
                Canvas.SetLeft(bubble, x);
                Canvas.SetTop(bubble, startY);

                var rise = new TranslateTransform();
                var sway = new TranslateTransform();
                bubble.RenderTransform = new TransformGroup { Children = { rise, sway } };

                double risePx = startY + size + 40; // travel up past the top edge
                double baseMs = 11000 - depth * 5000; // near = faster
                double swayAmp = 6 + _npGlowRandomRng.NextDouble() * 16;
                double swayMs = 1800 + _npGlowRandomRng.Next(2400);
                double swayDelay = _npGlowRandomRng.Next(2600);

                bubble.Tag = new NpParticle
                {
                    Begin = (el, spd, rng) =>
                    {
                        double durMs = baseMs / Math.Max(0.35, spd);
                        rise.BeginAnimation(TranslateTransform.YProperty,
                            new DoubleAnimation(0, -risePx, TimeSpan.FromMilliseconds(durMs))
                            {
                                BeginTime = TimeSpan.FromMilliseconds(rng.Next((int)Math.Max(1, durMs))),
                                RepeatBehavior = RepeatBehavior.Forever
                            });
                        sway.BeginAnimation(TranslateTransform.XProperty,
                            new DoubleAnimation(-swayAmp, swayAmp, TimeSpan.FromMilliseconds(swayMs / Math.Max(0.35, spd)))
                            {
                                BeginTime = TimeSpan.FromMilliseconds(swayDelay),
                                AutoReverse = true,
                                RepeatBehavior = RepeatBehavior.Forever,
                                EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
                            });
                    }
                };
                NpStarsCanvas.Children.Add(bubble);
                _npStarShapes.Add(bubble);
            }
        }

        // A few bottom-anchored kelp fronds that sway via a bottom-pinned
        // RotateTransform on a slow eased sine, each phase-offset so they don't
        // move in unison. RotateTransform clocks are cleared in NpStopStarfield.
        private void NpBuildSeaweed(double width, double height, Color color)
        {
            int fronds = (int)Math.Clamp(width / 220.0, 3, 6);

            for (int i = 0; i < fronds; i++)
            {
                double frondHeight = height * (0.28 + _npGlowRandomRng.NextDouble() * 0.22);
                double frondWidth = 14 + _npGlowRandomRng.NextDouble() * 14;
                // Spread fronds across the width with a little jitter.
                double baseX = (width / fronds) * (i + 0.5) + (_npGlowRandomRng.NextDouble() - 0.5) * 60;

                var brush = new LinearGradientBrush
                {
                    StartPoint = new Point(0.5, 1),
                    EndPoint = new Point(0.5, 0),
                    GradientStops =
                    {
                        new GradientStop(Color.FromArgb(190, color.R, color.G, color.B), 0.0),
                        new GradientStop(Color.FromArgb(120, color.R, color.G, color.B), 0.6),
                        new GradientStop(Color.FromArgb(40, color.R, color.G, color.B), 1.0)
                    }
                };
                brush.Freeze();

                // A simple tapered ribbon path: narrow at the top, wider at the base.
                var geo = new PathGeometry();
                var fig = new PathFigure { StartPoint = new Point(0, frondHeight) };
                fig.Segments.Add(new BezierSegment(
                    new Point(-frondWidth * 0.4, frondHeight * 0.6),
                    new Point(frondWidth * 0.5, frondHeight * 0.3),
                    new Point(frondWidth * 0.2, 0), true));
                fig.Segments.Add(new BezierSegment(
                    new Point(frondWidth * 0.9, frondHeight * 0.3),
                    new Point(frondWidth * 1.3, frondHeight * 0.6),
                    new Point(frondWidth, frondHeight), true));
                fig.IsClosed = true;
                geo.Figures.Add(fig);
                geo.Freeze();

                var frond = new System.Windows.Shapes.Path
                {
                    Data = geo,
                    Fill = brush,
                    Opacity = 0.55 + _npGlowRandomRng.NextDouble() * 0.25,
                    IsHitTestVisible = false
                };
                Canvas.SetLeft(frond, baseX);
                Canvas.SetTop(frond, height - frondHeight);
                // Pin rotation to the base of the frond so it sways like seaweed.
                var rot = new RotateTransform(0, frondWidth * 0.5, frondHeight);
                frond.RenderTransform = new TransformGroup { Children = { rot } };

                double swayDeg = 4 + _npGlowRandomRng.NextDouble() * 5;
                double swayMs = 3600 + _npGlowRandomRng.Next(2800);
                double swayDelay = _npGlowRandomRng.Next(3000);

                frond.Tag = new NpParticle
                {
                    Begin = (el, spd, rng) =>
                    {
                        var grp = ((FrameworkElement)el).RenderTransform as TransformGroup;
                        var r = grp?.Children.OfType<RotateTransform>().FirstOrDefault();
                        if (r == null) return;
                        r.BeginAnimation(RotateTransform.AngleProperty,
                            new DoubleAnimation(-swayDeg, swayDeg, TimeSpan.FromMilliseconds(swayMs / Math.Max(0.35, spd)))
                            {
                                BeginTime = TimeSpan.FromMilliseconds(swayDelay),
                                AutoReverse = true,
                                RepeatBehavior = RepeatBehavior.Forever,
                                EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
                            });
                    }
                };
                NpStarsCanvas.Children.Add(frond);
                _npStarShapes.Add(frond);
            }
        }

        // Sporadic single-pass fish silhouette drifting across the canvas, with a
        // gentle vertical bob and alternating direction. Modeled on the
        // shooting-star scheduler: one swimmer at a time, long random gaps.
        private void NpStartFishScheduler(double width, double height, double speed)
        {
            _npFishTimer?.Stop();
            _npFishTimer = new DispatcherTimer();

            void ScheduleNext()
            {
                double meanMs = Math.Clamp(18000.0 / Math.Max(0.4, speed), 8000, 30000);
                double jitter = meanMs * (0.6 + _npGlowRandomRng.NextDouble() * 1.0);
                _npFishTimer!.Interval = TimeSpan.FromMilliseconds(jitter);
            }

            _npFishTimer.Tick += (_, _) =>
            {
                if (!IsNowPlayingUiActive() ||
                    ThemeManager.NormalizeNpBackgroundAnimationMode(ThemeManager.NpBackgroundAnimationMode) != "Underwater" ||
                    !ThemeManager.NpUnderwaterFishEnabled)
                {
                    NpStopParticleTimers();
                    return;
                }
                NpSpawnFish(width, height,
                    ThemeManager.ClampNpBackgroundAnimationSpeed(ThemeManager.NpBackgroundAnimationSpeed));
                ScheduleNext();
            };
            ScheduleNext();
            // First fish fairly soon so the effect is visible without a long wait.
            _npFishTimer.Interval = TimeSpan.FromMilliseconds(2500 + _npGlowRandomRng.Next(5000));
            _npFishTimer.Start();
        }

        private void NpSpawnFish(double width, double height, double speed)
        {
            if (NpStarsCanvas == null) return;

            bool leftToRight = _npGlowRandomRng.NextDouble() < 0.5;
            double scale = 0.7 + _npGlowRandomRng.NextDouble() * 0.8;
            double fishW = 26 * scale;
            double fishH = 13 * scale;

            // Simple fish silhouette: a teardrop body + triangular tail.
            var geo = new PathGeometry();
            var body = new PathFigure { StartPoint = new Point(0, fishH * 0.5) };
            body.Segments.Add(new BezierSegment(
                new Point(fishW * 0.35, 0), new Point(fishW * 0.75, 0),
                new Point(fishW, fishH * 0.5), true));
            body.Segments.Add(new BezierSegment(
                new Point(fishW * 0.75, fishH), new Point(fishW * 0.35, fishH),
                new Point(0, fishH * 0.5), true));
            body.IsClosed = true;
            geo.Figures.Add(body);
            var tail = new PathFigure { StartPoint = new Point(0, fishH * 0.5) };
            tail.Segments.Add(new LineSegment(new Point(-fishW * 0.28, fishH * 0.12), true));
            tail.Segments.Add(new LineSegment(new Point(-fishW * 0.28, fishH * 0.88), true));
            tail.IsClosed = true;
            geo.Figures.Add(tail);
            geo.Freeze();

            var fish = new System.Windows.Shapes.Path
            {
                Data = geo,
                Fill = new SolidColorBrush(Color.FromArgb(150, NpUwFishColor.R, NpUwFishColor.G, NpUwFishColor.B)),
                Opacity = 0,
                IsHitTestVisible = false,
                RenderTransformOrigin = new Point(0.5, 0.5)
            };

            double y = height * (0.2 + _npGlowRandomRng.NextDouble() * 0.6);
            var move = new TranslateTransform();
            var bob = new TranslateTransform();
            // Flip horizontally for right-to-left swimmers.
            var flip = new ScaleTransform(leftToRight ? 1 : -1, 1, fishW * 0.5, fishH * 0.5);
            fish.RenderTransform = new TransformGroup { Children = { flip, move, bob } };

            double startX = leftToRight ? -fishW - 20 : width + 20;
            double endX = leftToRight ? width + fishW + 20 : -fishW - 40;
            Canvas.SetLeft(fish, startX);
            Canvas.SetTop(fish, y);
            NpStarsCanvas.Children.Add(fish);

            double durMs = (14000 + _npGlowRandomRng.Next(8000)) / Math.Max(0.4, speed);

            move.BeginAnimation(TranslateTransform.XProperty,
                new DoubleAnimation(0, endX - startX, TimeSpan.FromMilliseconds(durMs)));
            bob.BeginAnimation(TranslateTransform.YProperty,
                new DoubleAnimation(-6, 6, TimeSpan.FromMilliseconds(2200 / Math.Max(0.4, speed)))
                {
                    AutoReverse = true,
                    RepeatBehavior = RepeatBehavior.Forever,
                    EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
                });

            var fade = new DoubleAnimationUsingKeyFrames
            {
                Duration = TimeSpan.FromMilliseconds(durMs),
                KeyFrames =
                {
                    new LinearDoubleKeyFrame(0.0, KeyTime.FromPercent(0)),
                    new LinearDoubleKeyFrame(0.5, KeyTime.FromPercent(0.12)),
                    new LinearDoubleKeyFrame(0.5, KeyTime.FromPercent(0.88)),
                    new LinearDoubleKeyFrame(0.0, KeyTime.FromPercent(1))
                }
            };
            fade.Completed += (_, _) =>
            {
                move.BeginAnimation(TranslateTransform.XProperty, null);
                bob.BeginAnimation(TranslateTransform.YProperty, null);
                NpStarsCanvas.Children.Remove(fish);
            };
            fish.BeginAnimation(UIElement.OpacityProperty, fade);
        }
    }
}
