using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using AudioQualityChecker.Models;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using NAudio.Vorbis;
using TagLib;

namespace AudioQualityChecker.Services
{
    public static partial class AudioAnalyzer
    {        // ═══════════════════════════════════════════════════════
        //  Optimizer Detection (Platinum Notes, etc.)
        // ═══════════════════════════════════════════════════════

        private static bool DetectOptimizer(AudioFileInfo info)
        {
            if (info.EffectiveFrequency == 0) return false;
            if (IsLosslessFile(info)) return false;

            try
            {
                var (disposable, samples, waveFormat) = OpenAudioFile(info.FilePath);
                using var _d = disposable;
                int sampleRate = waveFormat.SampleRate;
                int channels = waveFormat.Channels;

                long totalFrames;
                if (disposable is AudioFileReader afr2)
                    totalFrames = afr2.Length / afr2.WaveFormat.BlockAlign;
#if !CROSS_PLATFORM
                else if (disposable is MediaFoundationReader mfr3)
                    totalFrames = mfr3.Length / mfr3.WaveFormat.BlockAlign;
#endif
                else
                    totalFrames = (long)(info.DurationSeconds * sampleRate);

                int samplesNeeded = FftSize * 4;
                if (totalFrames < samplesNeeded) return false;

                // Seek to middle — use Position if WaveStream, else sequential skip
                long midStart = (totalFrames - samplesNeeded) / 2;
                if (disposable is WaveStream wsOpt)
                {
                    wsOpt.Position = midStart * wsOpt.WaveFormat.BlockAlign;
                }
                else
                {
                    long toSkip = midStart * channels;
                    float[] skipBuf2 = new float[4096 * channels];
                    while (toSkip > 0)
                    {
                        int chunk = (int)Math.Min(toSkip, skipBuf2.Length);
                        int got = samples.Read(skipBuf2, 0, chunk);
                        if (got <= 0) break;
                        toSkip -= got;
                    }
                }

                float[] buffer = new float[samplesNeeded * channels];
                int read = samples.Read(buffer, 0, buffer.Length);
                if (read < buffer.Length) return false;

                int specLen = FftSize / 2;
                double[] avgSpec = new double[specLen];

                double[] window = new double[FftSize];
                for (int i = 0; i < FftSize; i++)
                    window[i] = 0.5 * (1.0 - Math.Cos(2.0 * Math.PI * i / (FftSize - 1)));

                for (int seg = 0; seg < 4; seg++)
                {
                    double[] real = new double[FftSize];
                    double[] imag = new double[FftSize];
                    int offset = seg * FftSize * channels;
                    for (int i = 0; i < FftSize; i++)
                    {
                        float sum = 0;
                        for (int ch = 0; ch < channels; ch++)
                            sum += buffer[offset + i * channels + ch];
                        real[i] = (sum / channels) * window[i];
                    }

                    FFT(real, imag);

                    for (int i = 0; i < specLen; i++)
                        avgSpec[i] += Math.Sqrt(real[i] * real[i] + imag[i] * imag[i]);
                }
                for (int i = 0; i < specLen; i++) avgSpec[i] /= 4;

                return CheckOptimizerArtifacts(avgSpec, sampleRate, info.EffectiveFrequency);
            }
            catch { return false; }
        }

        private static bool CheckOptimizerArtifacts(double[] spectrum, int sampleRate, int cutoffFreq)
        {
            int specLen = spectrum.Length;
            double binHz = (double)sampleRate / (2 * specLen);
            int cutoffBin = (int)(cutoffFreq / binHz);

            if (cutoffBin < 20 || cutoffBin >= specLen - 20) return false;

            int region = Math.Max(10, cutoffBin / 20);
            int belowStart = Math.Max(5, cutoffBin - region * 3);
            int belowEnd = cutoffBin - region;
            int nearStart = cutoffBin - region;
            int nearEnd = Math.Min(specLen - 1, cutoffBin + region);

            double belowAvg = 0;
            for (int i = belowStart; i < belowEnd; i++) belowAvg += spectrum[i];
            belowAvg /= Math.Max(1, belowEnd - belowStart);

            double nearAvg = 0;
            for (int i = nearStart; i <= nearEnd; i++) nearAvg += spectrum[i];
            nearAvg /= Math.Max(1, nearEnd - nearStart + 1);

            // 1) Unnatural boost near cutoff
            bool boost = belowAvg > 0 && (nearAvg / belowAvg) > 1.8;

            // 2) Suspiciously flat high region
            int hStart = Math.Max(cutoffBin - region * 2, 5);
            double mean = 0;
            for (int i = hStart; i < cutoffBin; i++) mean += spectrum[i];
            mean /= Math.Max(1, cutoffBin - hStart);
            double variance = 0;
            for (int i = hStart; i < cutoffBin; i++)
                variance += (spectrum[i] - mean) * (spectrum[i] - mean);
            variance /= Math.Max(1, cutoffBin - hStart);
            bool flat = mean > 0 && Math.Sqrt(variance) / mean < 0.15;

            // 3) Sharp wall
            int aboveEnd = Math.Min(specLen - 1, cutoffBin + region * 2);
            double aboveAvg = 0;
            for (int i = cutoffBin; i <= aboveEnd; i++) aboveAvg += spectrum[i];
            aboveAvg /= Math.Max(1, aboveEnd - cutoffBin + 1);
            double belowCutAvg = 0;
            int bc = Math.Max(5, cutoffBin - region * 2);
            for (int i = bc; i < cutoffBin; i++) belowCutAvg += spectrum[i];
            belowCutAvg /= Math.Max(1, cutoffBin - bc);

            bool wall = belowCutAvg > 0 &&
                        (aboveAvg / belowCutAvg) < 0.05 &&
                        (nearAvg / belowCutAvg) > 0.8;

            return ((boost ? 1 : 0) + (flat ? 1 : 0) + (wall ? 1 : 0)) >= 2;
        }

    }
}