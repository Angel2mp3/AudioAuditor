using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using AudioQualityChecker.Services;
using WpfPath = System.Windows.Shapes.Path;
using NAudio.Wave;

namespace AudioQualityChecker
{
    public partial class WaveformCompareWindow : Window
    {
        private readonly string _fileA;
        private readonly string _fileB;
        private double[]? _waveA;
        private double[]? _waveB;

        private (double min, double max)[]? _envA;
        private (double min, double max)[]? _envB;

        private readonly DispatcherTimer _renderDebounce;

        public WaveformCompareWindow(string fileA, string fileB)
        {
            InitializeComponent();
            _fileA = fileA;
            _fileB = fileB;
            FileALabel.Text = System.IO.Path.GetFileName(fileA);
            FileBLabel.Text = System.IO.Path.GetFileName(fileB);

            _renderDebounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
            _renderDebounce.Tick += (_, _) => { _renderDebounce.Stop(); RenderWaveforms(); };

            Loaded += async (_, _) => await LoadAndRender();
        }

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2) return;
            DragMove();
        }

        private void Close_Click(object sender, RoutedEventArgs e) => Close();
        private void Canvas_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            _envA = null;
            _envB = null;
            _renderDebounce.Stop();
            _renderDebounce.Start();
        }

        private void MergeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (MergeLabel != null)
                MergeLabel.Text = $"{(int)MergeSlider.Value}%";
            _renderDebounce.Stop();
            _renderDebounce.Start();
        }

        private void OffsetSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (OffsetLabel != null)
                OffsetLabel.Text = $"{(int)OffsetSlider.Value} px";
            _renderDebounce.Stop();
            _renderDebounce.Start();
        }

        private async Task LoadAndRender()
        {
            StatsText.Text = "Loading waveforms...";
            try
            {
                var taskA = Task.Run(() => LoadWaveform(_fileA));
                var taskB = Task.Run(() => LoadWaveform(_fileB));
                _waveA = await taskA;
                _waveB = await taskB;

                if (_waveA == null || _waveB == null)
                {
                    StatsText.Text = "Failed to load one or both files.";
                    return;
                }

                int minLen = Math.Min(_waveA.Length, _waveB.Length);
                if (minLen == 0)
                {
                    StatsText.Text = "One or both files have no audio samples.";
                    return;
                }
                double maxDiff = 0;
                double sumDiffSq = 0;
                for (int i = 0; i < minLen; i++)
                {
                    double d = Math.Abs(_waveA[i] - _waveB[i]);
                    if (d > maxDiff) maxDiff = d;
                    sumDiffSq += d * d;
                }
                double rmsDiff = Math.Sqrt(sumDiffSq / minLen);
                double correlation = ComputeCorrelation(_waveA, _waveB, minLen);

                StatsText.Text = $"Correlation: {correlation:P1}  |  RMS Diff: {rmsDiff:F4}  |  Peak Diff: {maxDiff:F4}  |  Samples: {minLen:N0}";
                RenderWaveforms();
            }
            catch (Exception ex)
            {
                StatsText.Text = $"Error: {ex.Message}";
            }
        }

        private static double ComputeCorrelation(double[] a, double[] b, int len)
        {
            double sumA = 0, sumB = 0, sumAB = 0, sumA2 = 0, sumB2 = 0;
            for (int i = 0; i < len; i++)
            {
                sumA += a[i];
                sumB += b[i];
                sumAB += a[i] * b[i];
                sumA2 += a[i] * a[i];
                sumB2 += b[i] * b[i];
            }
            double n = len;
            double denom = Math.Sqrt((n * sumA2 - sumA * sumA) * (n * sumB2 - sumB * sumB));
            if (denom < 1e-20) return 0;
            return (n * sumAB - sumA * sumB) / denom;
        }

        private static double[]? LoadWaveform(string filePath)
        {
            try
            {
                var (disposable, samples, format) = AudioAnalyzer.OpenAudioFile(filePath);
                if (disposable == null || samples == null || format == null) return null;

                using (disposable)
                {
                    int channels = format.Channels;
                    int blockSize = 8192;
                    float[] buf = new float[blockSize * channels];
                    var result = new System.Collections.Generic.List<double>();

                    int read;
                    while ((read = samples.Read(buf, 0, buf.Length)) > 0)
                    {
                        int frames = read / channels;
                        for (int i = 0; i < frames; i++)
                        {
                            double sum = 0;
                            for (int ch = 0; ch < channels; ch++)
                                sum += buf[i * channels + ch];
                            result.Add(sum / channels);
                        }
                    }
                    return result.ToArray();
                }
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Renders both waveforms on a single canvas.
        /// Merge slider (0-100%) controls vertical separation:
        ///   0% = File A in top quarter, File B in bottom quarter (fully apart)
        ///   100% = both drawn at center (fully overlapping)
        /// </summary>
        private void RenderWaveforms()
        {
            WaveCanvas.Children.Clear();
            if (_waveA == null || _waveB == null) return;

            double w = WaveCanvas.ActualWidth;
            double h = WaveCanvas.ActualHeight;
            if (w < 10 || h < 10) return;

            int points = (int)w;

            if (_envA == null || _envA.Length != points)
                _envA = GetEnvelope(_waveA, points);
            if (_envB == null || _envB.Length != points)
                _envB = GetEnvelope(_waveB, points);

            int offset = (int)OffsetSlider.Value;
            double merge = MergeSlider.Value / 100.0;

            // Vertical centers for each waveform
            // At merge=0: A center is at 25% height, B center is at 75%
            // At merge=1: both centers converge to 50%
            double centerA = h * (0.25 + 0.25 * merge);
            double centerB = h * (0.75 - 0.25 * merge);
            double waveHeight = h * 0.45; // available amplitude space for each waveform

            // Draw center reference line
            var centerLine = new System.Windows.Shapes.Line
            {
                X1 = 0, X2 = w,
                Y1 = h / 2, Y2 = h / 2,
                Stroke = new SolidColorBrush(Color.FromArgb(40, 128, 128, 128)),
                StrokeThickness = 1,
                StrokeDashArray = new DoubleCollection { 4, 4 },
                IsHitTestVisible = false
            };
            WaveCanvas.Children.Add(centerLine);

            // Draw File A waveform at centerA
            DrawWaveform(WaveCanvas, _envA, points, centerA, waveHeight, 0,
                Color.FromArgb(180, 91, 155, 213));

            // Draw File B waveform at centerB with horizontal offset
            if (_envB != null)
                DrawWaveform(WaveCanvas, _envB, points, centerB, waveHeight, offset,
                    Color.FromArgb(160, 229, 115, 115));

            // Draw difference waveform when waveforms are close enough (merge > 30%)
            if (merge > 0.3 && _envA != null && _envB != null)
            {
                double diffCenter = (centerA + centerB) / 2;
                byte diffAlpha = (byte)(120 * Math.Min(1.0, (merge - 0.3) / 0.7));
                DrawDifference(WaveCanvas, _envA, _envB, Math.Min(points, _envB.Length), diffCenter, waveHeight, offset,
                    Color.FromArgb(diffAlpha, 129, 199, 132));
            }
        }

        private static (double min, double max)[] GetEnvelope(double[] samples, int points)
        {
            var env = new (double min, double max)[points];
            double samplesPerPoint = (double)samples.Length / points;

            for (int i = 0; i < points; i++)
            {
                int start = (int)(i * samplesPerPoint);
                int end = Math.Min((int)((i + 1) * samplesPerPoint), samples.Length);
                double min = 0, max = 0;
                for (int j = start; j < end; j++)
                {
                    if (samples[j] < min) min = samples[j];
                    if (samples[j] > max) max = samples[j];
                }
                env[i] = (min, max);
            }
            return env;
        }

        private static void DrawWaveform(Canvas canvas, (double min, double max)[] env, int points,
            double centerY, double height, int offset, Color color)
        {
            var brush = new SolidColorBrush(color);
            brush.Freeze();

            var geometry = new StreamGeometry();
            using (var ctx = geometry.Open())
            {
                double scale = (height / 2) * 0.9;
                int firstIdx = Math.Max(0, -offset);
                int lastIdx = Math.Min(points, env.Length - offset);
                if (firstIdx >= lastIdx) return;

                ctx.BeginFigure(new Point(firstIdx, centerY - env[firstIdx + offset].max * scale), true, true);
                for (int i = firstIdx + 1; i < lastIdx; i++)
                {
                    int si = i + offset;
                    if (si < 0 || si >= env.Length) continue;
                    ctx.LineTo(new Point(i, centerY - env[si].max * scale), true, false);
                }
                for (int i = lastIdx - 1; i >= firstIdx; i--)
                {
                    int si = i + offset;
                    if (si < 0 || si >= env.Length) continue;
                    ctx.LineTo(new Point(i, centerY - env[si].min * scale), true, false);
                }
            }
            geometry.Freeze();

            canvas.Children.Add(new WpfPath
            {
                Data = geometry,
                Fill = brush,
                IsHitTestVisible = false
            });
        }

        private static void DrawDifference(Canvas canvas, (double min, double max)[] envA,
            (double min, double max)[] envB, int points, double centerY, double height,
            int offset, Color color)
        {
            var brush = new SolidColorBrush(color);
            brush.Freeze();

            var geometry = new StreamGeometry();
            using (var ctx = geometry.Open())
            {
                double scale = (height / 2) * 0.9;
                int firstIdx = Math.Max(0, Math.Max(0, -offset));
                int lastIdx = Math.Min(points, Math.Min(envA.Length, envB.Length - offset));
                if (firstIdx >= lastIdx) return;

                int si0 = firstIdx + offset;
                double diffMax0 = envA[firstIdx].max - envB[si0].max;
                ctx.BeginFigure(new Point(firstIdx, centerY - diffMax0 * scale * 3), true, true);
                for (int i = firstIdx + 1; i < lastIdx; i++)
                {
                    int si = i + offset;
                    if (si < 0 || si >= envB.Length) continue;
                    double d = envA[i].max - envB[si].max;
                    ctx.LineTo(new Point(i, centerY - d * scale * 3), true, false);
                }
                for (int i = lastIdx - 1; i >= firstIdx; i--)
                {
                    int si = i + offset;
                    if (si < 0 || si >= envB.Length) continue;
                    double d = envA[i].min - envB[si].min;
                    ctx.LineTo(new Point(i, centerY - d * scale * 3), true, false);
                }
            }
            geometry.Freeze();

            canvas.Children.Add(new WpfPath
            {
                Data = geometry,
                Fill = brush,
                IsHitTestVisible = false
            });
        }
    }
}
