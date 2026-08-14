using System;
using System.Collections.Generic;

namespace AudioQualityChecker.Services
{
    /// <summary>Min/max pair for one horizontal pixel column of a waveform.</summary>
    public readonly record struct WaveformPoint(double Min, double Max);

    /// <summary>How closely two waveforms match.</summary>
    public sealed record WaveformComparison(
        double Correlation,
        double RmsDifference,
        double PeakDifference,
        int ComparedSamples);

    /// <summary>
    /// Loading, enveloping and comparing waveforms for the two-file compare window.
    ///
    /// Pure math, lifted out of the WPF window (Windows/WaveformCompareWindow.xaml.cs) so the
    /// Avalonia window draws the same numbers rather than recomputing them slightly differently.
    /// </summary>
    public static class WaveformCompare
    {
        /// <summary>
        /// Reads a file as a mono sample array (channels averaged). Returns null when the file
        /// cannot be opened or decoded.
        /// </summary>
        public static double[]? Load(string filePath)
        {
            try
            {
                var (disposable, samples, format) = AudioAnalyzer.OpenAudioFile(filePath);
                if (disposable == null || samples == null || format == null) return null;

                using (disposable)
                {
                    int channels = format.Channels;
                    const int blockSize = 8192;
                    var buffer = new float[blockSize * channels];
                    var result = new List<double>();

                    int read;
                    while ((read = samples.Read(buffer, 0, buffer.Length)) > 0)
                    {
                        int frames = read / channels;
                        for (int i = 0; i < frames; i++)
                        {
                            double sum = 0;
                            for (int ch = 0; ch < channels; ch++)
                                sum += buffer[i * channels + ch];
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
        /// Reduces <paramref name="samples"/> to <paramref name="points"/> min/max pairs, one per
        /// pixel column. Returns an empty array when there is nothing to draw.
        /// </summary>
        public static WaveformPoint[] Envelope(double[] samples, int points)
        {
            if (points <= 0 || samples.Length == 0) return Array.Empty<WaveformPoint>();

            var envelope = new WaveformPoint[points];
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

                envelope[i] = new WaveformPoint(min, max);
            }

            return envelope;
        }

        /// <summary>
        /// Compares the overlapping portion of two waveforms. Returns null when either is empty.
        /// </summary>
        public static WaveformComparison? Compare(double[] a, double[] b)
        {
            int length = Math.Min(a.Length, b.Length);
            if (length == 0) return null;

            double peakDiff = 0;
            double sumDiffSquared = 0;

            for (int i = 0; i < length; i++)
            {
                double diff = Math.Abs(a[i] - b[i]);
                if (diff > peakDiff) peakDiff = diff;
                sumDiffSquared += diff * diff;
            }

            return new WaveformComparison(
                Correlation(a, b, length),
                Math.Sqrt(sumDiffSquared / length),
                peakDiff,
                length);
        }

        /// <summary>
        /// Pearson correlation over the first <paramref name="length"/> samples. Returns 0 when
        /// either signal is flat, since a constant has no correlation to measure against.
        /// </summary>
        public static double Correlation(double[] a, double[] b, int length)
        {
            double sumA = 0, sumB = 0, sumAB = 0, sumA2 = 0, sumB2 = 0;

            for (int i = 0; i < length; i++)
            {
                sumA += a[i];
                sumB += b[i];
                sumAB += a[i] * b[i];
                sumA2 += a[i] * a[i];
                sumB2 += b[i] * b[i];
            }

            double n = length;
            // Clamp each variance term at 0 BEFORE the sqrt. For a DC-offset or near-constant
            // signal these are mathematically zero but accumulate slightly negative in floating
            // point; Math.Sqrt then returns NaN, `NaN < 1e-20` is false, and NaN escaped the guard
            // all the way to the UI as "Correlation: NaN%".
            double varA = Math.Max(0, n * sumA2 - sumA * sumA);
            double varB = Math.Max(0, n * sumB2 - sumB * sumB);
            double denominator = Math.Sqrt(varA * varB);

            return denominator < 1e-20 ? 0 : (n * sumAB - sumA * sumB) / denominator;
        }
    }
}
