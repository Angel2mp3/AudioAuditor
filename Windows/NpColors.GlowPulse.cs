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
    // NP cover-glow pulse animation (the breathing glow behind the album art).
    // Extracted verbatim from NpColors.cs (2026-06-05 large-file split).
    public partial class MainWindow
    {
        // ─── NP Glow Pulse Animation ───

        private DispatcherTimer? _npGlowPulseTimer;
        private double _npGlowPhase;

        private void NpApplyCoverGlowBrushes(Color primary, Color secondary)
        {
            NpCoverGlow1.Background = NpCreateCoverGlowBrush(primary, secondary, false);
            NpCoverGlow2.Background = NpCreateCoverGlowBrush(secondary, primary, true);
            NpApplyGlowMotionFrame();
        }

        private static LinearGradientBrush NpCreateCoverGlowBrush(Color first, Color second, bool reversed)
        {
            var mid = Color.FromRgb(
                (byte)((first.R + second.R) / 2),
                (byte)((first.G + second.G) / 2),
                (byte)((first.B + second.B) / 2));

            return new LinearGradientBrush(
                new GradientStopCollection
                {
                    new(first, 0.0),
                    new(mid, 0.5),
                    new(second, 1.0)
                },
                reversed ? new Point(1, 0.35) : new Point(0, 0.35),
                reversed ? new Point(0, 0.65) : new Point(1, 0.65));
        }

        private void NpStartGlowPulse()
        {
            if (!AnimationPolicy.IsMotionAllowed(AnimationArea.CoverGlow)) return;
            if (!IsNowPlayingUiActive() || _npCoverGlowSize <= 0)
                return;
            if (_npGlowPulseTimer != null) return;
            // Do NOT reset _npGlowPhase here. The phase is a continuous sine input; resetting
            // it to 0 on every (re)start makes the glow visibly snap when the NP screen resumes
            // after a minimize/restore. Continuing from the current phase keeps it smooth.
            _npGlowPulseTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
            _npGlowPulseTimer.Tick += NpGlowPulse_Tick;
            _npGlowPulseTimer.Start();
        }

        private void NpStopGlowPulse()
        {
            _npGlowPulseTimer?.Stop();
            _npGlowPulseTimer = null;
        }

        private void NpGlowPulse_Tick(object? sender, EventArgs e)
        {
            if (!AnimationPolicy.IsMotionAllowed(AnimationArea.CoverGlow))
            {
                NpStopGlowPulse();
                return;
            }

            _npGlowPhase += 0.03; // Slow breathing
            double pulse = 0.5 + 0.12 * Math.Sin(_npGlowPhase); // oscillates 0.38 – 0.62
            double pulse2 = 0.35 + 0.08 * Math.Sin(_npGlowPhase + 1.0); // offset phase
            // Scale by user-controlled glow size from Customize Layout (0 = off, 1 = default, 2 = max)
            double scale = Math.Clamp(_npCoverGlowSize, 0, 2.0);
            NpCoverGlow1.Opacity = pulse * scale;
            NpCoverGlow2.Opacity = pulse2 * scale;
            NpApplyGlowMotionFrame();
        }

        private void NpApplyGlowMotionFrame()
        {
            if (!AnimationPolicy.IsMotionAllowed(AnimationArea.CoverGlow) || !_npCoverGlowMotionEnabled || !IsNowPlayingUiActive())
                return;

            switch (_npGlowMotionMode)
            {
                case GlowMotionMode.Swirl:
                    NpMoveGlowBrushSwirl(NpCoverGlow1.Background, _npGlowPhase);
                    NpMoveGlowBrushSwirl(NpCoverGlow2.Background, _npGlowPhase + Math.PI * 0.6);
                    break;
                case GlowMotionMode.LinearLR:
                    NpMoveGlowBrushLinear(NpCoverGlow1.Background, _npGlowPhase, leftToRight: true);
                    NpMoveGlowBrushLinear(NpCoverGlow2.Background, _npGlowPhase + Math.PI * 0.6, leftToRight: true);
                    break;
                case GlowMotionMode.LinearRL:
                    NpMoveGlowBrushLinear(NpCoverGlow1.Background, _npGlowPhase, leftToRight: false);
                    NpMoveGlowBrushLinear(NpCoverGlow2.Background, _npGlowPhase + Math.PI * 0.6, leftToRight: false);
                    break;
                case GlowMotionMode.Random:
                    NpAdvanceRandomGlow();
                    break;
                case GlowMotionMode.DiagonalSweep:
                    NpMoveGlowBrushDiagonal(NpCoverGlow1.Background, _npGlowPhase);
                    NpMoveGlowBrushDiagonal(NpCoverGlow2.Background, _npGlowPhase + Math.PI * 0.6);
                    break;
                case GlowMotionMode.Orbit:
                    NpMoveGlowBrushOrbit(NpCoverGlow1.Background, _npGlowPhase);
                    NpMoveGlowBrushOrbit(NpCoverGlow2.Background, _npGlowPhase + Math.PI * 0.6);
                    break;
                case GlowMotionMode.ColorDrift:
                    NpAdvanceRandomGlow();
                    NpMoveGlowBrushLinear(NpCoverGlow1.Background, _npGlowPhase * 0.45, leftToRight: true);
                    NpMoveGlowBrushLinear(NpCoverGlow2.Background, _npGlowPhase * 0.45 + Math.PI * 0.6, leftToRight: false);
                    break;
            }
        }

        private static void NpMoveGlowBrushSwirl(Brush brush, double phase)
        {
            if (brush is not LinearGradientBrush gradient || gradient.IsFrozen)
                return;

            double cos = Math.Cos(phase);
            double sin = Math.Sin(phase);
            gradient.StartPoint = new Point(0.5 - cos * 0.5, 0.5 - sin * 0.5);
            gradient.EndPoint = new Point(0.5 + cos * 0.5, 0.5 + sin * 0.5);
        }

        // Sweep the gradient horizontally — phase wraps in [0, 1) so the brush slides past
        // continuously without snapping. y stays centered for a clean horizontal band.
        private static void NpMoveGlowBrushLinear(Brush brush, double phase, bool leftToRight)
        {
            if (brush is not LinearGradientBrush gradient || gradient.IsFrozen)
                return;
            // Map phase to a [0,1) sweep; the visible "band" spans 0.4 of the brush width.
            double t = (phase / (Math.PI * 2.0)) % 1.0;
            if (t < 0) t += 1.0;
            double start = leftToRight ? t - 0.2 : 1.0 - t - 0.2;
            double end = start + 0.4;
            gradient.StartPoint = new Point(start, 0.5);
            gradient.EndPoint = new Point(end, 0.5);
        }

        private static void NpMoveGlowBrushDiagonal(Brush brush, double phase)
        {
            if (brush is not LinearGradientBrush gradient || gradient.IsFrozen)
                return;

            double t = (phase / (Math.PI * 2.0)) % 1.0;
            if (t < 0) t += 1.0;
            double start = t - 0.25;
            double end = start + 0.5;
            gradient.StartPoint = new Point(start, start);
            gradient.EndPoint = new Point(end, end);
        }

        private static void NpMoveGlowBrushOrbit(Brush brush, double phase)
        {
            if (brush is not LinearGradientBrush gradient || gradient.IsFrozen)
                return;

            double cos = Math.Cos(phase);
            double sin = Math.Sin(phase);
            gradient.StartPoint = new Point(0.35 + cos * 0.28, 0.35 + sin * 0.28);
            gradient.EndPoint = new Point(0.65 - sin * 0.28, 0.65 + cos * 0.28);
        }

        // Random-mode tween: keep the swirl orientation (so endpoints stay sane) but periodically
        // ease the gradient stop colors toward a new random target sampled from the album palette.
        private void NpAdvanceRandomGlow()
        {
            // Endpoints follow a slow Swirl so the gradient still has motion between color swaps.
            NpMoveGlowBrushSwirl(NpCoverGlow1.Background, _npGlowPhase * 0.3);
            NpMoveGlowBrushSwirl(NpCoverGlow2.Background, _npGlowPhase * 0.3 + Math.PI * 0.6);

            long nowMs = Environment.TickCount64;
            if (_npGlowRandomLastSwapMs == 0 || nowMs - _npGlowRandomLastSwapMs > NpGlowRandomSwapMs)
            {
                _npGlowRandomCurrentA = _npGlowRandomTargetA == default ? _npAlbumPrimary : _npGlowRandomTargetA;
                _npGlowRandomCurrentB = _npGlowRandomTargetB == default ? _npAlbumSecondary : _npGlowRandomTargetB;
                _npGlowRandomTargetA = NpPickRandomPaletteColor(_npGlowRandomCurrentA);
                _npGlowRandomTargetB = NpPickRandomPaletteColor(_npGlowRandomCurrentB);
                _npGlowRandomLastSwapMs = nowMs;
                _npGlowRandomTweenT = 0.0;
            }

            // Ease toward the target across NpGlowRandomTweenDurationMs, then hold until next swap.
            double elapsed = nowMs - _npGlowRandomLastSwapMs;
            double t = Math.Clamp(elapsed / NpGlowRandomTweenDurationMs, 0.0, 1.0);
            _npGlowRandomTweenT = t;
            var a = NpLerpColor(_npGlowRandomCurrentA, _npGlowRandomTargetA, t);
            var b = NpLerpColor(_npGlowRandomCurrentB, _npGlowRandomTargetB, t);
            NpReplaceBrushColors(NpCoverGlow1.Background, a, b);
            NpReplaceBrushColors(NpCoverGlow2.Background, b, a);
        }

        private Color NpPickRandomPaletteColor(Color avoid)
        {
            // Seed from extracted palette if present; otherwise nudge the existing color slightly
            // so Random mode still moves on tracks with no palette yet.
            Color[] palette = (_npAlbumPrimary != default || _npAlbumSecondary != default)
                ? new[] { _npAlbumPrimary, _npAlbumSecondary }
                : new[] { avoid };
            for (int attempt = 0; attempt < 4; attempt++)
            {
                var pick = palette[_npGlowRandomRng.Next(palette.Length)];
                if (pick == default) continue;
                if (pick != avoid) return pick;
            }
            // Fallback: jitter the avoid color so we always return something
            return Color.FromRgb(
                (byte)Math.Clamp(avoid.R + _npGlowRandomRng.Next(-40, 40), 0, 255),
                (byte)Math.Clamp(avoid.G + _npGlowRandomRng.Next(-40, 40), 0, 255),
                (byte)Math.Clamp(avoid.B + _npGlowRandomRng.Next(-40, 40), 0, 255));
        }

        private static Color NpLerpColor(Color a, Color b, double t)
        {
            if (a == default) return b;
            if (b == default) return a;
            return Color.FromRgb(
                (byte)(a.R + (b.R - a.R) * t),
                (byte)(a.G + (b.G - a.G) * t),
                (byte)(a.B + (b.B - a.B) * t));
        }

        private static void NpReplaceBrushColors(Brush brush, Color first, Color second)
        {
            if (brush is not LinearGradientBrush gradient || gradient.IsFrozen) return;
            var stops = gradient.GradientStops;
            if (stops.Count < 3) return;
            var mid = Color.FromRgb(
                (byte)((first.R + second.R) / 2),
                (byte)((first.G + second.G) / 2),
                (byte)((first.B + second.B) / 2));
            stops[0].Color = first;
            stops[1].Color = mid;
            stops[2].Color = second;
        }

        /// <summary>
        /// One-shot apply for the cover-glow scale — used when the user drags the slider so the
        /// change is visible immediately even if the breathing tick hasn't fired yet.
        /// </summary>
        private void NpApplyCoverGlowScale()
        {
            if (NpCoverGlow1 == null || NpCoverGlow2 == null) return;
            double scale = Math.Clamp(_npCoverGlowSize, 0, 2.0);
            // Use the breathing-curve midpoints as a stable preview baseline
            NpCoverGlow1.Opacity = 0.5 * scale;
            NpCoverGlow2.Opacity = 0.35 * scale;

            if (scale <= 0)
                NpStopGlowPulse();
            else if (AnimationPolicy.IsMotionAllowed(AnimationArea.CoverGlow) && IsNowPlayingUiActive())
                NpStartGlowPulse();
        }
    }
}
