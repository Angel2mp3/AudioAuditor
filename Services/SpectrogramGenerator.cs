using System;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using NAudio.Vorbis;

namespace AudioQualityChecker.Services
{
    public static class SpectrogramGenerator
    {
        private const int FftSize = 4096;

        // Color gradient: black → blue → purple → red → orange → yellow → white
        private static readonly (byte R, byte G, byte B)[] GradientColors =
        {
            (0,   0,   0  ),   // 0.00 — silence / black
            (5,   5,   50 ),   // 0.08
            (15,  15,  110),   // 0.18 — dark blue
            (55,  15,  150),   // 0.32 — purple
            (170, 25,  25 ),   // 0.48 — red
            (215, 115, 5  ),   // 0.62 — orange
            (250, 215, 45 ),   // 0.78 — yellow
            (255, 255, 255),   // 1.00 — white
        };
        private static readonly double[] GradientPos =
            { 0.00, 0.08, 0.18, 0.32, 0.48, 0.62, 0.78, 1.00 };

        /// <summary>
        /// Generates a spectrogram bitmap using sequential reading (no seeking).
        /// Returns a frozen BitmapSource safe for cross-thread access.
        /// </summary>
        public static BitmapSource? Generate(string filePath, int width = 1200, int height = 400)
        {
            try
            {
                var (disposable, samples, waveFormat) = AudioAnalyzer.OpenAudioFile(filePath);
                using var _ = disposable;
                int sampleRate = waveFormat.SampleRate;
                int channels = waveFormat.Channels;

                // Determine total frames from underlying reader
                long totalFrames;
                if (disposable is AudioFileReader afr)
                    totalFrames = afr.Length / afr.WaveFormat.BlockAlign;
                else if (disposable is MediaFoundationReader mfr)
                    totalFrames = mfr.Length / mfr.WaveFormat.BlockAlign;
                else if (disposable is WaveStream ws && ws.Length > 0)
                    totalFrames = ws.Length / ws.WaveFormat.BlockAlign;
                else if (disposable is VorbisWaveReader vbr)
                    totalFrames = vbr.Length / vbr.WaveFormat.BlockAlign;
                else
                    totalFrames = 0;

                if (totalFrames < FftSize * 2) return null;

                int columns = width;
                int rows = height;
                int spectrumSize = FftSize / 2;

                // How to divide the file into 'columns' FFT frames
                // We'll read sequentially and advance by stepFrames between FFTs
                long stepFrames = Math.Max(1, (totalFrames - FftSize) / columns);

                // Pre-compute Hanning window
                double[] window = new double[FftSize];
                for (int i = 0; i < FftSize; i++)
                    window[i] = 0.5 * (1.0 - Math.Cos(2.0 * Math.PI * i / (FftSize - 1)));

                // First pass: read all columns, store dB spectra, track global max
                double[][] specData = new double[columns][];
                double globalMax = -200;

                // Sequential read buffer
                float[] frameBuf = new float[FftSize * channels];

                long currentFrame = 0;

                for (int col = 0; col < columns; col++)
                {
                    long targetFrame = col * stepFrames;

                    // Skip forward if needed (read and discard)
                    long framesToSkip = targetFrame - currentFrame;
                    if (framesToSkip > 0)
                    {
                        SkipFrames(samples, channels, framesToSkip, ref currentFrame);
                    }

                    // Read the FFT window
                    int read = samples.Read(frameBuf, 0, frameBuf.Length);
                    currentFrame += FftSize;

                    if (read < frameBuf.Length)
                    {
                        // Incomplete read — fill with empty
                        specData[col] = new double[spectrumSize];
                        for (int i = 0; i < spectrumSize; i++) specData[col][i] = -200;
                        continue;
                    }

                    // Down-mix to mono, apply window, FFT
                    double[] real = new double[FftSize];
                    double[] imag = new double[FftSize];

                    for (int i = 0; i < FftSize; i++)
                    {
                        float sum = 0;
                        for (int ch = 0; ch < channels; ch++)
                            sum += frameBuf[i * channels + ch];
                        real[i] = (sum / channels) * window[i];
                    }

                    FFT(real, imag);

                    double[] mags = new double[spectrumSize];
                    for (int i = 0; i < spectrumSize; i++)
                    {
                        double mag = Math.Sqrt(real[i] * real[i] + imag[i] * imag[i]);
                        mags[i] = mag > 1e-12 ? 20.0 * Math.Log10(mag) : -200;
                        if (mags[i] > globalMax) globalMax = mags[i];
                    }

                    specData[col] = mags;
                }

                // If entire file was silent, bail
                if (globalMax < -150) return null;

                // Render to pixels
                double dynamicRange = 80; // dB
                double minDb = globalMax - dynamicRange;

                byte[] pixels = new byte[columns * rows * 3];
                double logMin = Math.Log10(20.0);
                double logMax = Math.Log10(sampleRate / 2.0);
                double logRange = logMax - logMin;

                for (int col = 0; col < columns; col++)
                {
                    var colData = specData[col];

                    for (int row = 0; row < rows; row++)
                    {
                        // Log-frequency: top = high freq, bottom = low freq
                        double t = 1.0 - (double)row / (rows - 1);
                        double freq = Math.Pow(10, logMin + t * logRange);

                        // Map to FFT bin with linear interpolation
                        double bin = freq / sampleRate * FftSize;
                        int b0 = Math.Clamp((int)bin, 0, spectrumSize - 1);
                        int b1 = Math.Clamp(b0 + 1, 0, spectrumSize - 1);
                        double frac = bin - (int)bin;

                        double val = colData[b0] * (1.0 - frac) + colData[b1] * frac;

                        double norm = Math.Clamp((val - minDb) / dynamicRange, 0, 1);
                        var (r, g, b) = MapColor(norm);

                        int idx = (row * columns + col) * 3;
                        pixels[idx] = r;
                        pixels[idx + 1] = g;
                        pixels[idx + 2] = b;
                    }
                }

                var bitmap = BitmapSource.Create(
                    columns, rows, 96, 96,
                    PixelFormats.Rgb24, null,
                    pixels, columns * 3);

                bitmap.Freeze();
                return bitmap;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Skip forward by reading and discarding samples.
        /// Much more reliable than Position-based seeking for compressed formats.
        /// </summary>
        private static void SkipFrames(ISampleProvider samples, int channels, long framesToSkip, ref long currentFrame)
        {
            int chunkSize = 4096 * channels;
            float[] discard = new float[chunkSize];
            long samplesLeft = framesToSkip * channels;

            while (samplesLeft > 0)
            {
                int toRead = (int)Math.Min(samplesLeft, chunkSize);
                int read = samples.Read(discard, 0, toRead);
                if (read <= 0) break;
                samplesLeft -= read;
            }
            currentFrame += framesToSkip;
        }

        private static (byte R, byte G, byte B) MapColor(double t)
        {
            t = Math.Clamp(t, 0, 1);

            for (int i = 0; i < GradientPos.Length - 1; i++)
            {
                if (t <= GradientPos[i + 1])
                {
                    double seg = (t - GradientPos[i]) / (GradientPos[i + 1] - GradientPos[i]);
                    seg = Math.Clamp(seg, 0, 1);

                    var c0 = GradientColors[i];
                    var c1 = GradientColors[i + 1];
                    return (
                        (byte)(c0.R + (c1.R - c0.R) * seg),
                        (byte)(c0.G + (c1.G - c0.G) * seg),
                        (byte)(c0.B + (c1.B - c0.B) * seg)
                    );
                }
            }

            var last = GradientColors[^1];
            return (last.R, last.G, last.B);
        }

        // ═══════════════════════════════════════════════════════
        //  FFT (Cooley-Tukey radix-2, forward only)
        // ═══════════════════════════════════════════════════════

        private static void FFT(double[] real, double[] imag)
        {
            int n = real.Length;
            int bits = (int)Math.Log2(n);

            for (int i = 0; i < n; i++)
            {
                int j = ReverseBits(i, bits);
                if (j > i)
                {
                    (real[i], real[j]) = (real[j], real[i]);
                    (imag[i], imag[j]) = (imag[j], imag[i]);
                }
            }

            for (int size = 2; size <= n; size *= 2)
            {
                int half = size / 2;
                double step = -2.0 * Math.PI / size;

                for (int i = 0; i < n; i += size)
                {
                    for (int j = 0; j < half; j++)
                    {
                        double a = step * j;
                        double cos = Math.Cos(a);
                        double sin = Math.Sin(a);
                        int e = i + j, o = i + j + half;
                        double tr = real[o] * cos - imag[o] * sin;
                        double ti = real[o] * sin + imag[o] * cos;
                        real[o] = real[e] - tr;
                        imag[o] = imag[e] - ti;
                        real[e] += tr;
                        imag[e] += ti;
                    }
                }
            }
        }

        private static int ReverseBits(int v, int bits)
        {
            int r = 0;
            for (int i = 0; i < bits; i++) { r = (r << 1) | (v & 1); v >>= 1; }
            return r;
        }
    }
}
