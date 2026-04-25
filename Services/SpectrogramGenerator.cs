using System;
using System.Buffers;
using System.Threading;
#if !CROSS_PLATFORM
using System.Windows.Media;
using System.Windows.Media.Imaging;
#endif
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using NAudio.Vorbis;

namespace AudioQualityChecker.Services
{
    /// <summary>
    /// Channel mode for spectrogram rendering.
    /// </summary>
    public enum SpectrogramChannel
    {
        /// <summary>Mono downmix (L+R)/2</summary>
        Mono,
        /// <summary>Left minus Right difference</summary>
        Difference
    }

    public static class SpectrogramGenerator
    {
        private const int FftSize = 4096;
        private const int FftSizeHQ = 16384; // High-Fidelity mode

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

        // Magma perceptual gradient (Scientific / dark-to-light)
        private static readonly (byte R, byte G, byte B)[] MagmaColors =
        {
            (0,   0,   4  ),   // 0.00 — near black
            (28,  16,  68 ),   // 0.15 — deep purple
            (79,  18,  123),   // 0.30
            (129, 37,  129),   // 0.45
            (181, 54,  85 ),   // 0.55
            (229, 107, 44 ),   // 0.70
            (252, 180, 98 ),   // 0.85
            (252, 253, 191),   // 1.00 — light yellow
        };
        private static readonly double[] MagmaPos =
            { 0.00, 0.15, 0.30, 0.45, 0.55, 0.70, 0.85, 1.00 };

        /// <summary>
        /// Generates raw RGB24 pixel data for a spectrogram. Cross-platform.
        /// Returns (pixels, width, height) or null if the file is too short/silent.
        /// </summary>
        public static (byte[] pixels, int width, int height)? GenerateRawPixels(string filePath,
            int width = 1200, int height = 400,
            bool linearScale = false, SpectrogramChannel channel = SpectrogramChannel.Mono,
            double endZoomSeconds = 0,
            bool highFidelity = false,
            bool magmaColormap = false,
            CancellationToken ct = default)
        {
            try
            {
                var (disposable, samples, waveFormat) = AudioAnalyzer.OpenAudioFile(filePath);
                using var _ = disposable;
                int sampleRate = waveFormat.SampleRate;
                int channels = waveFormat.Channels;

                long totalFrames;
                if (disposable is AudioFileReader afr)
                    totalFrames = afr.Length / afr.WaveFormat.BlockAlign;
#if !CROSS_PLATFORM
                else if (disposable is MediaFoundationReader mfr)
                    totalFrames = mfr.Length / mfr.WaveFormat.BlockAlign;
#endif
                else if (disposable is WaveStream ws && ws.Length > 0)
                    totalFrames = ws.Length / ws.WaveFormat.BlockAlign;
                else if (disposable is VorbisWaveReader vbr)
                    totalFrames = vbr.Length / vbr.WaveFormat.BlockAlign;
                else
                    totalFrames = 0;

                int fftSize = highFidelity ? FftSizeHQ : FftSize;

                if (totalFrames < fftSize * 2) return null;

                long startFrame = 0;
                long rangeFrames = totalFrames;
                if (endZoomSeconds > 0)
                {
                    long zoomFrames = (long)(endZoomSeconds * sampleRate);
                    if (zoomFrames < totalFrames)
                    {
                        startFrame = totalFrames - zoomFrames;
                        rangeFrames = zoomFrames;
                    }
                }

                if (rangeFrames < fftSize) return null;

                int rows = height;
                int spectrumSize = fftSize / 2;

                // Compute how many columns the audio can actually support.
                // With a sequential ISampleProvider we can't seek backward, so when the
                // natural hop (stepFrames) is smaller than fftSize we would need overlap
                // that the reader can't provide. In that case fall back to non-overlapping
                // windows (stepFrames = fftSize) and cap columns accordingly.
                long stepFrames = Math.Max(1, (rangeFrames - fftSize) / width);
                int columns;
                if (stepFrames < fftSize)
                {
                    // Non-overlapping sequential windows
                    columns = (int)(rangeFrames / fftSize);
                    if (columns < 1) columns = 1;
                    stepFrames = fftSize;
                }
                else
                {
                    // Sequential reader can skip forward fine; cap to actual audio length
                    columns = width;
                    long maxColumns = (rangeFrames - fftSize) / stepFrames + 1;
                    if (maxColumns < columns)
                        columns = (int)maxColumns;
                }

                double[] window = new double[fftSize];
                if (highFidelity)
                {
                    // Blackman-Harris window — better sidelobe suppression for HiFi mode
                    for (int i = 0; i < fftSize; i++)
                    {
                        double x = 2 * Math.PI * i / (fftSize - 1);
                        window[i] = 0.35875 - 0.48829 * Math.Cos(x)
                                    + 0.14128 * Math.Cos(2 * x) - 0.01168 * Math.Cos(3 * x);
                    }
                }
                else
                {
                    // Standard Hann window
                    for (int i = 0; i < fftSize; i++)
                        window[i] = 0.5 * (1.0 - Math.Cos(2.0 * Math.PI * i / (fftSize - 1)));
                }

                double[][] specData = new double[columns][];
                double globalMax = -200;
                float[] frameBuf = new float[fftSize * channels];
                long currentFrame = 0;

                if (startFrame > 0)
                    SkipFrames(samples, channels, startFrame, ref currentFrame);

                double[] real = ArrayPool<double>.Shared.Rent(fftSize);
                double[] imag = ArrayPool<double>.Shared.Rent(fftSize);
                double[] mags = ArrayPool<double>.Shared.Rent(spectrumSize);
                try
                {
                    for (int col = 0; col < columns; col++)
                    {
                        // Cooperative cancellation every 50 columns (~12ms at 250μs/col)
                        if ((col & 0x3F) == 0) ct.ThrowIfCancellationRequested();

                        long targetFrame = startFrame + col * stepFrames;
                        long framesToSkip = targetFrame - currentFrame;
                        if (framesToSkip > 0)
                            SkipFrames(samples, channels, framesToSkip, ref currentFrame);

                        int read = samples.Read(frameBuf, 0, frameBuf.Length);
                        currentFrame += fftSize;

                        if (read < frameBuf.Length)
                        {
                            specData[col] = new double[spectrumSize];
                            for (int i = 0; i < spectrumSize; i++) specData[col][i] = -200;
                            continue;
                        }

                        Array.Clear(real, 0, fftSize);
                        Array.Clear(imag, 0, fftSize);

                        if (channel == SpectrogramChannel.Difference && channels >= 2)
                        {
                            for (int i = 0; i < fftSize; i++)
                            {
                                float left = frameBuf[i * channels];
                                float right = frameBuf[i * channels + 1];
                                real[i] = (left - right) * window[i];
                            }
                        }
                        else
                        {
                            for (int i = 0; i < fftSize; i++)
                            {
                                float sum = 0;
                                for (int ch = 0; ch < channels; ch++)
                                    sum += frameBuf[i * channels + ch];
                                real[i] = (sum / channels) * window[i];
                            }
                        }

                        FFT(real, imag);

                        for (int i = 0; i < spectrumSize; i++)
                        {
                            double mag = Math.Sqrt(real[i] * real[i] + imag[i] * imag[i]);
                            mags[i] = mag > 1e-12 ? 20.0 * Math.Log10(mag) : -200;
                            if (mags[i] > globalMax) globalMax = mags[i];
                        }

                        specData[col] = new double[spectrumSize];
                        Array.Copy(mags, 0, specData[col], 0, spectrumSize);
                    }
                }
                finally
                {
                    ArrayPool<double>.Shared.Return(real);
                    ArrayPool<double>.Shared.Return(imag);
                    ArrayPool<double>.Shared.Return(mags);
                }

                if (globalMax < -150) return null;

                double dynamicRange = 130;
                double minDb = globalMax - dynamicRange;
                byte[] pixels = new byte[columns * rows * 3];

                if (linearScale)
                {
                    double nyquist = sampleRate / 2.0;
                    for (int col = 0; col < columns; col++)
                    {
                        var colData = specData[col];
                        for (int row = 0; row < rows; row++)
                        {
                            double t = 1.0 - (double)row / (rows - 1);
                            double freq = t * nyquist;
                            double bin = freq / sampleRate * fftSize;
                            int b0 = Math.Clamp((int)bin, 0, spectrumSize - 1);
                            int b1 = Math.Clamp(b0 + 1, 0, spectrumSize - 1);
                            double frac = bin - (int)bin;
                            double val = colData[b0] * (1.0 - frac) + colData[b1] * frac;
                            double norm = Math.Clamp((val - minDb) / dynamicRange, 0, 1);
                            var (r, g, b) = MapColor(norm, magmaColormap);
                            int idx = (row * columns + col) * 3;
                            pixels[idx] = r;
                            pixels[idx + 1] = g;
                            pixels[idx + 2] = b;
                        }
                    }
                }
                else
                {
                    double logMin = Math.Log10(20.0);
                    double logMax = Math.Log10(sampleRate / 2.0);
                    double logRange = logMax - logMin;
                    for (int col = 0; col < columns; col++)
                    {
                        var colData = specData[col];
                        for (int row = 0; row < rows; row++)
                        {
                            double t = 1.0 - (double)row / (rows - 1);
                            double freq = Math.Pow(10, logMin + t * logRange);
                            double bin = freq / sampleRate * fftSize;
                            int b0 = Math.Clamp((int)bin, 0, spectrumSize - 1);
                            int b1 = Math.Clamp(b0 + 1, 0, spectrumSize - 1);
                            double frac = bin - (int)bin;
                            double val = colData[b0] * (1.0 - frac) + colData[b1] * frac;
                            double norm = Math.Clamp((val - minDb) / dynamicRange, 0, 1);
                            var (r, g, b) = MapColor(norm, magmaColormap);
                            int idx = (row * columns + col) * 3;
                            pixels[idx] = r;
                            pixels[idx + 1] = g;
                            pixels[idx + 2] = b;
                        }
                    }
                }

                return (pixels, columns, rows);
            }
            catch
            {
                return null;
            }
        }

#if !CROSS_PLATFORM
        /// <summary>
        /// Generates a spectrogram bitmap using sequential reading (no seeking).
        /// Returns a frozen BitmapSource safe for cross-thread access.
        /// </summary>
        /// <param name="linearScale">If true, use linear frequency axis instead of logarithmic.</param>
        /// <param name="channel">Channel mode (Mono or L-R Difference).</param>
        /// <param name="endZoomSeconds">If > 0, only render the last N seconds of the file.</param>
        public static BitmapSource? Generate(string filePath, int width = 1200, int height = 400,
            bool linearScale = false, SpectrogramChannel channel = SpectrogramChannel.Mono,
            double endZoomSeconds = 0,
            bool highFidelity = false, bool magmaColormap = false,
            CancellationToken ct = default)
        {
            var result = GenerateRawPixels(filePath, width, height, linearScale, channel, endZoomSeconds,
                highFidelity, magmaColormap, ct);
            if (result == null) return null;

            var (pixels, columns, rows) = result.Value;
            var bitmap = BitmapSource.Create(
                columns, rows, 96, 96,
                PixelFormats.Rgb24, null,
                pixels, columns * 3);
            bitmap.Freeze();
            return bitmap;
        }
#endif

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

        private static (byte R, byte G, byte B) MapColor(double t, bool useMagma = false)
        {
            t = Math.Clamp(t, 0, 1);

            var colors = useMagma ? MagmaColors : GradientColors;
            var pos    = useMagma ? MagmaPos    : GradientPos;

            for (int i = 0; i < pos.Length - 1; i++)
            {
                if (t <= pos[i + 1])
                {
                    double seg = (t - pos[i]) / (pos[i + 1] - pos[i]);
                    seg = Math.Clamp(seg, 0, 1);

                    var c0 = colors[i];
                    var c1 = colors[i + 1];
                    return (
                        (byte)(c0.R + (c1.R - c0.R) * seg),
                        (byte)(c0.G + (c1.G - c0.G) * seg),
                        (byte)(c0.B + (c1.B - c0.B) * seg)
                    );
                }
            }

            var last = colors[^1];
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
