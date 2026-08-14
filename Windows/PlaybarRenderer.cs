using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using AudioQualityChecker.Services;

namespace AudioQualityChecker
{
    /// <summary>
    /// Single, shared renderer for every playbar animation style, used by both the main-window
    /// playbar and the embedded Now Playing mini-player playbar. Two animated styles are supported:
    /// Regular (a plain thin progress bar, no animation) and Wave (a smooth accent sine stroke).
    ///
    /// Conventions: <paramref name="pct"/> is play progress 0..1; <paramref name="phaseSeconds"/>
    /// is a continuously increasing time value driving motion (0 = no animation). The played fill
    /// is a thin bar centered vertically in the overlay canvas; BarThickness controls that thickness.
    ///
    /// PERF: elements are POOLED per canvas, not rebuilt. This renderer is driven by three loops at
    /// once — Visualizer_Tick (60 Hz), WaveformAnimation_Tick (30 Hz) and PlayerTimer_Tick (20 Hz) —
    /// and it used to Children.Clear() and allocate a fresh brush + Rectangle (+ StreamGeometry,
    /// second brush and Path for Wave) on every one of those calls. Now each canvas keeps its shapes
    /// and only mutates Width / Canvas.Top / brush.Color / geometry, matching how the spectrogram
    /// (Spectrogram.cs) and mini visualizer (MiniVisualizerRenderer.cs) already work. Output is
    /// pixel-identical.
    /// </summary>
    public partial class MainWindow
    {
        /// <summary>Pooled visuals for one playbar canvas.</summary>
        private sealed class PlaybarVisuals
        {
            public PlaybarAnimationStyle Style = (PlaybarAnimationStyle)(-1); // force first build
            public Rectangle? Bar;
            public SolidColorBrush? BarBrush;
            public Path? WavePath;
            public SolidColorBrush? WaveBrush;
        }

        private readonly Dictionary<Canvas, PlaybarVisuals> _playbarVisuals = new();

        private void RenderPlaybar(
            Canvas canvas,
            double pct,
            Color accent,
            Color secondary,
            PlaybarAnimationStyle style,
            double phaseSeconds)
        {
            if (canvas == null) return;

            double w = canvas.ActualWidth;
            double h = canvas.ActualHeight;
            pct = Math.Clamp(pct, 0, 1);
            double fillW = w * pct;

            // Bail BEFORE clearing so we never leave the canvas blank for a frame (a cleared-then-
            // returned canvas reads as a flicker). Only clear when there's truly nothing to draw.
            if (w < 1 || h < 1 || fillW < 1)
            {
                if (canvas.Children.Count > 0)
                {
                    canvas.Children.Clear();
                    _playbarVisuals.Remove(canvas);
                }
                return;
            }

            // Several callers clear these canvases directly (MainWindow.Waveform.cs, NpCore.cs,
            // MiniPlayerWindow.xaml.cs) to blank the playbar on stop/hide. That would leave the pool
            // holding elements no longer in the tree, so every later frame would mutate orphans and
            // draw nothing. Detecting it here keeps the fix in one place instead of in every caller.
            if (canvas.Children.Count == 0)
                _playbarVisuals.Remove(canvas);

            if (!_playbarVisuals.TryGetValue(canvas, out var visuals))
            {
                visuals = new PlaybarVisuals();
                _playbarVisuals[canvas] = visuals;
            }

            // Only tear down when the STYLE changes — a style switch changes which elements exist.
            if (visuals.Style != style)
            {
                canvas.Children.Clear();
                visuals.Bar = null;
                visuals.BarBrush = null;
                visuals.WavePath = null;
                visuals.WaveBrush = null;
                visuals.Style = style;
            }

            canvas.ClipToBounds = false;

            switch (style)
            {
                case PlaybarAnimationStyle.Wave: RenderWave(canvas, visuals, w, fillW, h, accent, phaseSeconds); break;
                case PlaybarAnimationStyle.Regular:
                default: RenderRegular(canvas, visuals, fillW, h, accent); break;
            }
        }

        /// <summary>Thickness (px) of the played bar; matches the 4px slider track so the fill
        /// reads as a clean continuation of the track into the playhead dot.</summary>
        private const double BarThickness = 4.0;

        /// <summary>Ensures the played accent bar exists and matches the current size/colour.</summary>
        private static void UpdateBar(Canvas canvas, PlaybarVisuals visuals, double fillW, double h, Color accent)
        {
            double barH = Math.Min(BarThickness, h);
            var opaque = Color.FromArgb(255, accent.R, accent.G, accent.B);

            if (visuals.Bar == null)
            {
                // Not frozen: the colour is mutated in place each frame instead of reallocated.
                visuals.BarBrush = new SolidColorBrush(opaque);
                visuals.Bar = new Rectangle
                {
                    Fill = visuals.BarBrush,
                    IsHitTestVisible = false
                };
                canvas.Children.Add(visuals.Bar);
            }
            else if (visuals.BarBrush!.Color != opaque)
            {
                visuals.BarBrush.Color = opaque;
            }

            var bar = visuals.Bar;
            if (bar.Width != fillW) bar.Width = fillW;
            if (bar.Height != barH)
            {
                bar.Height = barH;
                bar.RadiusX = barH / 2;
                bar.RadiusY = barH / 2;
            }
            Canvas.SetLeft(bar, 0);
            Canvas.SetTop(bar, (h - barH) / 2);
        }

        private static void RenderRegular(Canvas canvas, PlaybarVisuals visuals, double fillW, double h, Color accent)
        {
            // A plain progress bar: a thin accent fill, centered on the track, with rounded ends.
            // No animation — it just grows as playback advances and connects into the playhead dot.
            UpdateBar(canvas, visuals, fillW, h, accent);
        }

        private static void RenderWave(Canvas canvas, PlaybarVisuals visuals, double w, double fillW, double h,
            Color accent, double phaseSeconds)
        {
            double mid = h / 2;

            // Base filled progress bar FIRST, so the played area reads as a normal accent bar
            // instead of exposing the dark surface behind the transparent slider track (the
            // "black playbar" bug). The wave is then drawn on top as an accent.
            UpdateBar(canvas, visuals, fillW, h, accent);

            // A smooth sine stroke centered vertically, in a brighter tint so it stands out over
            // the filled bar. Stroke thickness and amplitude scale to the bar height; step count
            // is capped so very wide bars don't oversample.
            double amplitude = Math.Clamp(h * 0.3, 1.5, mid - 1);
            var geometry = new StreamGeometry();
            using (var ctx = geometry.Open())
            {
                int steps = Math.Clamp((int)(fillW / 4), 16, 260);
                ctx.BeginFigure(new Point(0, mid), false, false);
                for (int i = 0; i <= steps; i++)
                {
                    double x = fillW * i / steps;
                    // Taper the amplitude back to center over the last stretch so the wave ends exactly
                    // at the vertical center (mid) at the playhead — otherwise the end rides the full
                    // sine and sits above/below the dot, leaving a visible gap at some phases.
                    double t = (double)i / steps;
                    double endPull = t > 0.88 ? Math.Max(0.0, (1.0 - t) / 0.12) : 1.0;
                    double y = mid + Math.Sin((x / Math.Max(1, w)) * Math.PI * 6 + phaseSeconds * 4) * amplitude * endPull;
                    ctx.LineTo(new Point(x, y), true, true);
                }
            }
            geometry.Freeze();

            // The geometry genuinely changes every frame (the sine phase moves), but the Path and
            // its brush do not — only Data and StrokeThickness are reassigned.
            var strokeColor = LightenColor(accent, 0.55);
            if (visuals.WavePath == null)
            {
                visuals.WaveBrush = new SolidColorBrush(strokeColor);
                visuals.WavePath = new Path
                {
                    Stroke = visuals.WaveBrush,
                    StrokeStartLineCap = PenLineCap.Round,
                    StrokeEndLineCap = PenLineCap.Round,
                    StrokeLineJoin = PenLineJoin.Round,
                    IsHitTestVisible = false
                };
                canvas.Children.Add(visuals.WavePath);
            }
            else if (visuals.WaveBrush!.Color != strokeColor)
            {
                visuals.WaveBrush.Color = strokeColor;
            }

            double thickness = Math.Clamp(h * 0.3, 2, 5);
            if (visuals.WavePath.StrokeThickness != thickness)
                visuals.WavePath.StrokeThickness = thickness;
            visuals.WavePath.Data = geometry;
        }

        /// <summary>Blends a color toward white by <paramref name="amount"/> (0..1), preserving alpha.</summary>
        private static Color LightenColor(Color c, double amount)
        {
            amount = Math.Clamp(amount, 0, 1);
            return Color.FromArgb(c.A,
                (byte)(c.R + (255 - c.R) * amount),
                (byte)(c.G + (255 - c.G) * amount),
                (byte)(c.B + (255 - c.B) * amount));
        }
    }
}
