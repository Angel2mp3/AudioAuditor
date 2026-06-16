using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace AudioQualityChecker.Services
{
    /// <summary>
    /// Lightweight visualizer renderer for the Mini Player.
    /// Supports simplified Bars, Mirror, Scope, Circles, and Off styles.
    /// </summary>
    public class MiniVisualizerRenderer
    {
        private readonly Canvas _canvas;
        private readonly AudioPlayer _player;

        private Rectangle[]? _bars;
        private SolidColorBrush[]? _barBrushes;
        private Rectangle[]? _mirrorBars;
        private SolidColorBrush[]? _mirrorBrushes;
        private Polyline? _scopeLine;
        private Line[]? _circleLines;
        private SolidColorBrush[]? _circleBrushes;
        private Color[]? _paletteOverride;
        private bool _clearedInactive;

        private readonly double[] _smoothed;
        private readonly double[] _fftReal;
        private readonly double[] _fftImag;
        private readonly double[] _barValues;
        private float _lastRms;

        private const int NumBars = 32;
        private const int FftSize = 256;

        public bool IsActive { get; set; }
        public int Style { get; set; } // 0=Bars, 1=Mirror, 2=Scope, 3=Off, 4=Circles

        public MiniVisualizerRenderer(Canvas canvas, AudioPlayer player)
        {
            _canvas = canvas;
            _player = player;
            _smoothed = new double[NumBars];
            _fftReal = new double[FftSize];
            _fftImag = new double[FftSize];
            _barValues = new double[NumBars];
        }

        public void SetPalette(Color primary, Color secondary)
        {
            _paletteOverride = new[]
            {
                primary,
                secondary == default ? primary : secondary
            };
            ClearVisuals();
        }

        public void ClearPalette()
        {
            _paletteOverride = null;
            ClearVisuals();
        }

        public void Render()
        {
            if (Style == 3 || !IsActive) // Off
            {
                ClearVisualsOnce();
                return;
            }

            if (!_player.IsPlaying && !_player.IsPaused)
            {
                if (_canvas.Children.Count > 0)
                {
                    ClearVisuals();
                }
                return;
            }

            _clearedInactive = false;

            double w = _canvas.ActualWidth;
            double h = _canvas.ActualHeight;
            if (w < 10 || h < 10) return;

            var snapshot = _player.GetVisualizerSnapshot(2048);
            float[] samples = snapshot.Samples;

            // Skip FFT and canvas update when audio is silent — saves UI-thread work
            // during quiet passages. Bars continue to decay on ticks that cross the threshold.
            float rms = 0f;
            int rmsWindow = Math.Min(256, samples.Length);
            for (int i = samples.Length - rmsWindow; i < samples.Length; i++)
                rms += samples[i] * samples[i];
            rms = rmsWindow > 0 ? (float)Math.Sqrt(rms / rmsWindow) : 0f;
            bool wasSilent = _lastRms < 0.0001f;
            _lastRms = rms;
            if (rms < 0.0001f && wasSilent)
                return;

            // Fill FFT buffers with Hann window
            Array.Clear(_fftReal);
            Array.Clear(_fftImag);
            int offset = Math.Max(0, samples.Length - FftSize);
            for (int i = 0; i < FftSize && (offset + i) < samples.Length; i++)
            {
                double win = 0.5 * (1.0 - Math.Cos(2.0 * Math.PI * i / (FftSize - 1)));
                _fftReal[i] = samples[offset + i] * win;
            }

            // Compensate for volume when VisualizerFullVolume is enabled, so the bars stay lively
            // at any playback volume (mirrors Spectrogram.cs and the main visualizer).
            float capturedVolume = snapshot.UserVolume;
            if (ThemeManager.VisualizerFullVolume && capturedVolume > 0.01f && capturedVolume < 1f)
            {
                double gain = 1.0 / capturedVolume;
                for (int i = 0; i < FftSize; i++)
                    _fftReal[i] *= gain;
            }

            FFT(_fftReal, _fftImag);

            int specLen = FftSize / 2;
            double logMin = Math.Log10(20);
            int sr = _player.VisualizerSampleRate > 0 ? _player.VisualizerSampleRate : 44100;
            double logMax = Math.Log10(sr / 2.0);

            for (int b = 0; b < NumBars; b++)
            {
                double freqLow = Math.Pow(10, logMin + (logMax - logMin) * b / NumBars);
                double freqHigh = Math.Pow(10, logMin + (logMax - logMin) * (b + 1) / NumBars);
                int binLow = Math.Clamp((int)(freqLow / (sr / 2.0) * specLen), 0, specLen - 1);
                int binHigh = Math.Clamp((int)(freqHigh / (sr / 2.0) * specLen), 0, specLen - 1);

                double sum = 0;
                int cnt = 0;
                for (int i = binLow; i <= binHigh && i < specLen; i++)
                {
                    double mag = Math.Sqrt(_fftReal[i] * _fftReal[i] + _fftImag[i] * _fftImag[i]) / (FftSize / 2.0);
                    double db = mag > 1e-10 ? 20.0 * Math.Log10(mag) : -100;
                    sum += db;
                    cnt++;
                }
                double avgDb = cnt > 0 ? sum / cnt : -100;
                // Leave headroom so loud bars land ~0.85-0.9 rather than pinning at 1.0 — a wall of
                // bars stuck at the exact top is what read as a flat "ceiling".
                double norm = Math.Clamp((avgDb + 60.0) / 68.0, 0, 1);
                _barValues[b] = norm;
            }

            // Smoothing — aggressive attack, moderate decay for reactive feel
            for (int b = 0; b < NumBars; b++)
            {
                if (_barValues[b] > _smoothed[b])
                    _smoothed[b] = _barValues[b] * 0.7 + _smoothed[b] * 0.3;  // fast attack
                else
                    _smoothed[b] = _barValues[b] * 0.4 + _smoothed[b] * 0.6;  // moderate decay
            }

            var gradient = _paletteOverride ?? ThemeManager.GetVisualizerColors().ProgressGradient;

            switch (Style)
            {
                case 0: RenderBars(w, h, NumBars, gradient); break;
                case 1: RenderMirror(w, h, NumBars, gradient); break;
                case 2: RenderScope(w, h, samples); break;
                case 4: RenderCircles(w, h, NumBars, gradient); break;
            }
        }

        private void RenderBars(double w, double h, int numBars, Color[] gradient)
        {
            double barWidth = w / numBars * 0.75;
            double gap = w / numBars * 0.25;

            if (_bars == null || _bars.Length != numBars)
            {
                _canvas.Children.Clear();
                _bars = new Rectangle[numBars];
                _barBrushes = new SolidColorBrush[numBars];
                for (int b = 0; b < numBars; b++)
                {
                    _barBrushes[b] = new SolidColorBrush(gradient[0]);
                    _bars[b] = new Rectangle
                    {
                        Width = Math.Max(2, barWidth),
                        Height = 2,
                        Fill = _barBrushes[b],
                        RadiusX = 1.5,
                        RadiusY = 1.5,
                        IsHitTestVisible = false
                    };
                    Canvas.SetLeft(_bars[b], b * (barWidth + gap) + gap / 2);
                    Canvas.SetTop(_bars[b], h - 2);
                    _canvas.Children.Add(_bars[b]);
                }
                _mirrorBars = null;
                _scopeLine = null;
                _circleLines = null;
            }

            double time = Environment.TickCount64 / 1000.0;
            bool rainbow = ThemeManager.VisualizerRainbowEnabled;

            for (int b = 0; b < numBars; b++)
            {
                double bh = _smoothed[b] * h * 0.98;
                if (bh < 2) bh = 2;
                if (bh > h) bh = h;
                _bars[b].Height = bh;
                Canvas.SetTop(_bars[b], h - bh);
                _barBrushes![b].Color = GetBarColor(b, numBars, _smoothed[b], gradient, rainbow, time);
            }
        }

        private void RenderMirror(double w, double h, int numBars, Color[] gradient)
        {
            double barWidth = w / numBars * 0.75;
            double gap = w / numBars * 0.25;
            double centerY = h / 2.0;

            if (_bars == null || _bars.Length != numBars || _mirrorBars == null)
            {
                _canvas.Children.Clear();
                _bars = new Rectangle[numBars];
                _mirrorBars = new Rectangle[numBars];
                _barBrushes = new SolidColorBrush[numBars];
                _mirrorBrushes = new SolidColorBrush[numBars];
                for (int b = 0; b < numBars; b++)
                {
                    _barBrushes[b] = new SolidColorBrush(gradient[0]);
                    _mirrorBrushes[b] = new SolidColorBrush(gradient[0]);

                    _bars[b] = new Rectangle
                    {
                        Width = Math.Max(2, barWidth),
                        Height = 2,
                        Fill = _barBrushes[b],
                        RadiusX = 1.5,
                        RadiusY = 1.5,
                        IsHitTestVisible = false
                    };
                    Canvas.SetLeft(_bars[b], b * (barWidth + gap) + gap / 2);
                    Canvas.SetTop(_bars[b], centerY - 1);
                    _canvas.Children.Add(_bars[b]);

                    _mirrorBars[b] = new Rectangle
                    {
                        Width = Math.Max(2, barWidth),
                        Height = 2,
                        Fill = _mirrorBrushes[b],
                        RadiusX = 1.5,
                        RadiusY = 1.5,
                        Opacity = 0.5,
                        IsHitTestVisible = false
                    };
                    Canvas.SetLeft(_mirrorBars[b], b * (barWidth + gap) + gap / 2);
                    Canvas.SetTop(_mirrorBars[b], centerY);
                    _canvas.Children.Add(_mirrorBars[b]);
                }
                _scopeLine = null;
                _circleLines = null;
            }

            double time = Environment.TickCount64 / 1000.0;
            bool rainbow = ThemeManager.VisualizerRainbowEnabled;

            for (int b = 0; b < numBars; b++)
            {
                double bh = _smoothed[b] * centerY * 0.96;
                if (bh < 2) bh = 2;
                if (bh > centerY) bh = centerY;

                _bars[b].Height = bh;
                Canvas.SetTop(_bars[b], centerY - bh);

                _mirrorBars[b].Height = bh;
                Canvas.SetTop(_mirrorBars[b], centerY);

                var color = GetBarColor(b, numBars, _smoothed[b], gradient, rainbow, time);
                _barBrushes![b].Color = color;
                _mirrorBrushes![b].Color = color;
            }
        }

        private void RenderScope(double w, double h, float[] samples)
        {
            if (_scopeLine == null)
            {
                _canvas.Children.Clear();
                _scopeLine = new Polyline
                {
                    Stroke = new SolidColorBrush((_paletteOverride ?? ThemeManager.GetVisualizerColors().ProgressGradient)[0]),
                    StrokeThickness = 1.5,
                    IsHitTestVisible = false
                };
                _canvas.Children.Add(_scopeLine);
                _bars = null;
                _mirrorBars = null;
                _circleLines = null;
            }

            int steps = Math.Min(80, samples.Length);
            var points = new PointCollection(steps);
            double midY = h / 2;
            for (int i = 0; i < steps; i++)
            {
                int idx = samples.Length - steps + i;
                if (idx < 0) idx = 0;
                double x = w * i / Math.Max(1, steps - 1);
                double y = midY + samples[idx] * midY * 0.85;
                points.Add(new Point(x, Math.Clamp(y, 0, h)));
            }
            _scopeLine.Points = points;
        }

        // Number of concentric circles laid out in a row (a compact version of the
        // Now Playing "Circles" visualizer, which uses 5 rings).
        private const int MiniCircleCount = 3;

        private void RenderCircles(double w, double h, int numBars, Color[] gradient)
        {
            double margin = w * 0.06;
            double spacing = (w - 2 * margin) / MiniCircleCount;
            double baseRadius = Math.Min(spacing * 0.30, h * 0.34);
            if (baseRadius < 3) baseRadius = 3;

            // ~1 bar per 5px of circumference, kept small for the mini canvas.
            int barsPerCircle = Math.Clamp((int)(2 * Math.PI * baseRadius / 5.0), 12, 36);
            int totalLines = MiniCircleCount * barsPerCircle;

            if (_circleLines == null || _circleLines.Length != totalLines)
            {
                _canvas.Children.Clear();
                _circleLines = new Line[totalLines];
                _circleBrushes = new SolidColorBrush[totalLines];
                for (int i = 0; i < totalLines; i++)
                {
                    _circleBrushes[i] = new SolidColorBrush(gradient[0]);
                    _circleLines[i] = new Line
                    {
                        Stroke = _circleBrushes[i],
                        StrokeThickness = 1.5,
                        StrokeStartLineCap = PenLineCap.Round,
                        StrokeEndLineCap = PenLineCap.Round,
                        IsHitTestVisible = false
                    };
                    _canvas.Children.Add(_circleLines[i]);
                }
                _bars = null;
                _mirrorBars = null;
                _scopeLine = null;
            }

            double time = Environment.TickCount64 / 1000.0;
            bool rainbow = ThemeManager.VisualizerRainbowEnabled;
            double cy = h / 2.0;
            double strokeThickness = Math.Max(1.0, 2 * Math.PI * baseRadius / barsPerCircle * 0.5);
            int bandSize = Math.Max(1, numBars / MiniCircleCount);

            for (int c = 0; c < MiniCircleCount; c++)
            {
                double cx = margin + spacing * (c + 0.5);
                int bandStart = c * bandSize;
                int bandLen = (c == MiniCircleCount - 1) ? numBars - bandStart : bandSize;
                if (bandLen < 1) bandLen = 1;

                for (int s = 0; s < barsPerCircle; s++)
                {
                    int lineIdx = c * barsPerCircle + s;
                    double angle = 2 * Math.PI * s / barsPerCircle - Math.PI / 2;
                    double cos = Math.Cos(angle);
                    double sin = Math.Sin(angle);

                    int sampleIdx = Math.Clamp(bandStart + s * bandLen / barsPerCircle, 0, numBars - 1);
                    double energy = _smoothed[sampleIdx];
                    double barHeight = energy * baseRadius * 0.85;

                    var line = _circleLines![lineIdx];
                    line.X1 = cx + cos * baseRadius;
                    line.Y1 = cy + sin * baseRadius;
                    line.X2 = cx + cos * (baseRadius + barHeight);
                    line.Y2 = cy + sin * (baseRadius + barHeight);
                    line.StrokeThickness = strokeThickness;

                    _circleBrushes![lineIdx].Color = GetBarColor(lineIdx, totalLines, energy, gradient, rainbow, time);
                }
            }
        }

        private static Color GetBarColor(int barIdx, int totalBars, double energy, Color[] gradient, bool rainbow, double time)
        {
            if (rainbow)
            {
                double hue = (time * 30.0 + barIdx * 360.0 / totalBars) % 360.0;
                return HsvToColor(hue, 0.8, 0.75 + energy * 0.25);
            }

            double t = (double)barIdx / Math.Max(1, totalBars - 1);
            if (gradient.Length >= 3)
            {
                double seg = t * (gradient.Length - 1);
                int i = (int)seg;
                double frac = seg - i;
                if (i >= gradient.Length - 1) return gradient[^1];
                return LerpColor(gradient[i], gradient[i + 1], frac);
            }
            if (gradient.Length == 2)
                return LerpColor(gradient[0], gradient[1], t);
            return gradient[0];
        }

        private void ClearVisualsOnce()
        {
            if (_clearedInactive)
                return;

            ClearVisuals();
            _clearedInactive = true;
        }

        private void ClearVisuals()
        {
            _canvas.Children.Clear();
            _bars = null;
            _barBrushes = null;
            _mirrorBars = null;
            _mirrorBrushes = null;
            _scopeLine = null;
            _circleLines = null;
            _circleBrushes = null;
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

        private static Color HsvToColor(double h, double s, double v)
        {
            h = h % 360;
            if (h < 0) h += 360;
            double c = v * s;
            double x = c * (1 - Math.Abs((h / 60.0) % 2 - 1));
            double m = v - c;
            double r, g, b;
            switch ((int)(h / 60))
            {
                case 0: r = c; g = x; b = 0; break;
                case 1: r = x; g = c; b = 0; break;
                case 2: r = 0; g = c; b = x; break;
                case 3: r = 0; g = x; b = c; break;
                case 4: r = x; g = 0; b = c; break;
                default: r = c; g = 0; b = x; break;
            }
            return Color.FromRgb(
                (byte)Math.Clamp((r + m) * 255, 0, 255),
                (byte)Math.Clamp((g + m) * 255, 0, 255),
                (byte)Math.Clamp((b + m) * 255, 0, 255));
        }

        // ─── Simple iterative Cooley-Tukey FFT ───
        private static void FFT(double[] real, double[] imag)
        {
            int n = real.Length;
            if ((n & (n - 1)) != 0) return; // must be power of 2

            // Bit-reversal permutation
            int j = 0;
            for (int i = 0; i < n; i++)
            {
                if (i < j)
                {
                    (real[i], real[j]) = (real[j], real[i]);
                    (imag[i], imag[j]) = (imag[j], imag[i]);
                }
                int bit = n >> 1;
                while (j >= bit && bit > 0)
                {
                    j -= bit;
                    bit >>= 1;
                }
                j += bit;
            }

            // Butterfly loops
            for (int len = 2; len <= n; len <<= 1)
            {
                double ang = -2.0 * Math.PI / len;
                double wlenCos = Math.Cos(ang);
                double wlenSin = Math.Sin(ang);
                for (int i = 0; i < n; i += len)
                {
                    double wReal = 1.0;
                    double wImag = 0.0;
                    for (int k = 0; k < len / 2; k++)
                    {
                        int u = i + k;
                        int v = i + k + len / 2;
                        double uReal = real[u];
                        double uImag = imag[u];
                        double vReal = real[v] * wReal - imag[v] * wImag;
                        double vImag = real[v] * wImag + imag[v] * wReal;
                        real[u] = uReal + vReal;
                        imag[u] = uImag + vImag;
                        real[v] = uReal - vReal;
                        imag[v] = uImag - vImag;
                        double nextWReal = wReal * wlenCos - wImag * wlenSin;
                        double nextWImag = wReal * wlenSin + wImag * wlenCos;
                        wReal = nextWReal;
                        wImag = nextWImag;
                    }
                }
            }
        }
    }
}
