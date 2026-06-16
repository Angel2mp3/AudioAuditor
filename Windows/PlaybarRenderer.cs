using System;
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
    /// </summary>
    public partial class MainWindow
    {
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
                if (canvas.Children.Count > 0) canvas.Children.Clear();
                return;
            }

            canvas.Children.Clear();
            canvas.ClipToBounds = false;

            switch (style)
            {
                case PlaybarAnimationStyle.Wave: RenderWave(canvas, w, fillW, h, accent, phaseSeconds); break;
                case PlaybarAnimationStyle.Regular:
                default: RenderRegular(canvas, fillW, h, accent); break;
            }
        }

        /// <summary>Thickness (px) of the played bar; matches the 4px slider track so the fill
        /// reads as a clean continuation of the track into the playhead dot.</summary>
        private const double BarThickness = 4.0;

        private static void RenderRegular(Canvas canvas, double fillW, double h, Color accent)
        {
            // A plain progress bar: a thin accent fill, centered on the track, with rounded ends.
            // No animation — it just grows as playback advances and connects into the playhead dot.
            double barH = Math.Min(BarThickness, h);
            var fill = new SolidColorBrush(Color.FromArgb(255, accent.R, accent.G, accent.B));
            fill.Freeze();
            var rect = new Rectangle
            {
                Width = fillW,
                Height = barH,
                RadiusX = barH / 2,
                RadiusY = barH / 2,
                Fill = fill,
                IsHitTestVisible = false
            };
            Canvas.SetLeft(rect, 0);
            Canvas.SetTop(rect, (h - barH) / 2);
            canvas.Children.Add(rect);
        }

        private static void RenderWave(Canvas canvas, double w, double fillW, double h,
            Color accent, double phaseSeconds)
        {
            double mid = h / 2;
            double barH = Math.Min(BarThickness, h);

            // Base filled progress bar FIRST, so the played area reads as a normal accent bar
            // instead of exposing the dark surface behind the transparent slider track (the
            // "black playbar" bug). The wave is then drawn on top as an accent.
            var fill = new SolidColorBrush(Color.FromArgb(255, accent.R, accent.G, accent.B));
            fill.Freeze();
            var baseBar = new Rectangle
            {
                Width = fillW,
                Height = barH,
                RadiusX = barH / 2,
                RadiusY = barH / 2,
                Fill = fill,
                IsHitTestVisible = false
            };
            Canvas.SetLeft(baseBar, 0);
            Canvas.SetTop(baseBar, (h - barH) / 2);
            canvas.Children.Add(baseBar);

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

            var stroke = new SolidColorBrush(LightenColor(accent, 0.55));
            stroke.Freeze();
            canvas.Children.Add(new Path
            {
                Data = geometry,
                Stroke = stroke,
                StrokeThickness = Math.Clamp(h * 0.3, 2, 5),
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
                StrokeLineJoin = PenLineJoin.Round,
                IsHitTestVisible = false
            });
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
