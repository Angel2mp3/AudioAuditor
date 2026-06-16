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
    {        /// <summary>
        /// Detects BPM using multi-band onset detection + autocorrelation with
        /// harmonic/subharmonic disambiguation. Analyzes up to 60 seconds of audio,
        /// skipping the first 10 seconds to avoid sparse intros.
        /// </summary>
        private static int DetectBpm(string filePath)
        {
            ISampleProvider? sampleReader = null;
            IDisposable? readerDisposable = null;

            try
            {
                int sampleRate;
                int channels;

                try
                {
                    var afr = new AudioFileReader(filePath);
                    sampleReader = afr;
                    readerDisposable = afr;
                    sampleRate = afr.WaveFormat.SampleRate;
                    channels = afr.WaveFormat.Channels;
                }
                catch
                {
#if !CROSS_PLATFORM
                    var mfr = new MediaFoundationReader(filePath);
                    var sc = new SampleChannel(mfr, false);
                    sampleReader = sc;
                    readerDisposable = mfr;
                    sampleRate = sc.WaveFormat.SampleRate;
                    channels = sc.WaveFormat.Channels;
#else
                    // On cross-platform, re-try via the full OpenAudioFile chain
                    var (reader, samples, fmt) = OpenAudioFile(filePath);
                    sampleReader = samples;
                    readerDisposable = reader;
                    sampleRate = fmt.SampleRate;
                    channels = fmt.Channels;
#endif
                }

                // Skip first 10 seconds (intros are often sparse), then read up to 60 seconds
                int skipSamples = sampleRate * 10 * channels;
                if (readerDisposable is WaveStream wsBpm)
                {
                    // Seek by time: 10 seconds * blockAlign * sampleRate
                    long seekBytes = (long)sampleRate * 10 * wsBpm.WaveFormat.BlockAlign;
                    if (seekBytes < wsBpm.Length)
                        wsBpm.Position = seekBytes;
                }
                else
                {
                    float[] skipBuf = new float[Math.Min(skipSamples, 4096 * channels)];
                    int toSkip = skipSamples;
                    while (toSkip > 0)
                    {
                        int chunk = Math.Min(toSkip, skipBuf.Length);
                        int got = sampleReader.Read(skipBuf, 0, chunk);
                        if (got <= 0) break;
                        toSkip -= got;
                    }
                }

                int monoSamples = sampleRate * 60;
                int rawToRead = monoSamples * channels;
                float[] rawBuf = new float[rawToRead];
                int rawRead = sampleReader.Read(rawBuf, 0, rawToRead);
                readerDisposable.Dispose();
                readerDisposable = null;

                if (rawRead < sampleRate * channels * 4) return 0; // need >= 4s

                // Convert to mono
                int monoCount = rawRead / channels;
                float[] mono = new float[monoCount];
                for (int i = 0; i < monoCount; i++)
                {
                    float sum = 0;
                    for (int ch = 0; ch < channels; ch++)
                        sum += rawBuf[i * channels + ch];
                    mono[i] = sum / channels;
                }

                // ── Multi-band spectral flux onset detection ──
                // Use short FFT frames for time resolution
                int fftLen = 1024;
                int hopSize = fftLen / 2; // 50% overlap
                int numFrames = (monoCount - fftLen) / hopSize;
                if (numFrames < 100) return 0;

                int specLen = fftLen / 2;
                double hopDuration = (double)hopSize / sampleRate;

                // Define frequency bands (bin indices): sub-bass/kick, bass, low-mid, mid
                int BinForFreq(double freq) => (int)Math.Round(freq * fftLen / sampleRate);
                int bandKickLo = BinForFreq(30), bandKickHi = BinForFreq(200);
                int bandBassLo = BinForFreq(200), bandBassHi = BinForFreq(500);
                int bandMidLo = BinForFreq(500), bandMidHi = BinForFreq(4000);
                int bandHiLo = BinForFreq(4000), bandHiHi = Math.Min(BinForFreq(12000), specLen - 1);

                // Hanning window
                double[] window = new double[fftLen];
                for (int i = 0; i < fftLen; i++)
                    window[i] = 0.5 * (1.0 - Math.Cos(2.0 * Math.PI * i / (fftLen - 1)));

                // Compute spectral magnitude for each frame
                double[][] specMag = new double[numFrames][];
                for (int f = 0; f < numFrames; f++)
                {
                    int start = f * hopSize;
                    double[] real = new double[fftLen];
                    double[] imag = new double[fftLen];
                    for (int i = 0; i < fftLen; i++)
                        real[i] = mono[start + i] * window[i];
                    FFT(real, imag);
                    double[] mag = new double[specLen];
                    for (int i = 0; i < specLen; i++)
                        mag[i] = Math.Sqrt(real[i] * real[i] + imag[i] * imag[i]);
                    specMag[f] = mag;
                }

                // Compute spectral flux (onset strength) per band, then combine
                double[] onset = new double[numFrames - 1];
                double[] prevMag = specMag[0];
                for (int f = 1; f < numFrames; f++)
                {
                    double[] curMag = specMag[f];

                    // Spectral flux: sum of positive differences per band (weighted)
                    double fluxKick = 0, fluxBass = 0, fluxMid = 0, fluxHi = 0;
                    for (int b = bandKickLo; b <= bandKickHi && b < specLen; b++)
                        fluxKick += Math.Max(0, curMag[b] - prevMag[b]);
                    for (int b = bandBassLo; b <= bandBassHi && b < specLen; b++)
                        fluxBass += Math.Max(0, curMag[b] - prevMag[b]);
                    for (int b = bandMidLo; b <= bandMidHi && b < specLen; b++)
                        fluxMid += Math.Max(0, curMag[b] - prevMag[b]);
                    for (int b = bandHiLo; b <= bandHiHi && b < specLen; b++)
                        fluxHi += Math.Max(0, curMag[b] - prevMag[b]);

                    // Weight kick/bass bands higher — they carry the beat in most music
                    onset[f - 1] = fluxKick * 3.0 + fluxBass * 2.0 + fluxMid * 1.0 + fluxHi * 0.5;
                    prevMag = curMag;
                }

                // Normalize onset signal
                double maxOnset = 0;
                for (int i = 0; i < onset.Length; i++)
                    if (onset[i] > maxOnset) maxOnset = onset[i];
                if (maxOnset > 0)
                    for (int i = 0; i < onset.Length; i++)
                        onset[i] /= maxOnset;

                // Adaptive threshold: suppress onset values below local median * 1.5
                int medianWindow = (int)(2.0 / hopDuration); // ~2 second window
                for (int i = 0; i < onset.Length; i++)
                {
                    int lo = Math.Max(0, i - medianWindow / 2);
                    int hi = Math.Min(onset.Length - 1, i + medianWindow / 2);
                    double localMean = 0;
                    int cnt = 0;
                    for (int j = lo; j <= hi; j++) { localMean += onset[j]; cnt++; }
                    localMean /= cnt;
                    onset[i] = Math.Max(0, onset[i] - localMean * 0.5);
                }

                // Re-normalize after thresholding
                maxOnset = 0;
                for (int i = 0; i < onset.Length; i++)
                    if (onset[i] > maxOnset) maxOnset = onset[i];
                if (maxOnset > 0)
                    for (int i = 0; i < onset.Length; i++)
                        onset[i] /= maxOnset;

                // ── Autocorrelation for BPM range 50–220 ──
                int minLag = Math.Max(1, (int)(60.0 / 220 / hopDuration));
                int maxLag = (int)(60.0 / 50 / hopDuration);
                maxLag = Math.Min(maxLag, onset.Length / 2);

                if (minLag >= maxLag) return 0;

                double[] corr = new double[maxLag + 1];
                double maxCorr = 0;

                for (int lag = minLag; lag <= maxLag; lag++)
                {
                    double sum = 0;
                    int count = onset.Length - lag;
                    for (int i = 0; i < count; i++)
                        sum += onset[i] * onset[i + lag];
                    corr[lag] = count > 0 ? sum / count : 0;
                    if (corr[lag] > maxCorr) maxCorr = corr[lag];
                }

                if (maxCorr < 1e-10) return 0;

                // ── Find top peaks in autocorrelation ──
                var peaks = new List<(int lag, double value)>();
                for (int lag = minLag + 1; lag < maxLag; lag++)
                {
                    if (corr[lag] > corr[lag - 1] && corr[lag] > corr[lag + 1] && corr[lag] > maxCorr * 0.3)
                        peaks.Add((lag, corr[lag]));
                }

                if (peaks.Count == 0)
                {
                    // Fallback: use the global max
                    int bestLag = minLag;
                    for (int lag = minLag; lag <= maxLag; lag++)
                        if (corr[lag] > corr[bestLag]) bestLag = lag;
                    double bpmFallback = 60.0 / (bestLag * hopDuration);
                    int bpmFallbackInt = (int)Math.Round(bpmFallback);
                    return (bpmFallbackInt >= 50 && bpmFallbackInt <= 220) ? bpmFallbackInt : 0;
                }

                // Sort peaks by correlation strength
                peaks.Sort((a, b) => b.value.CompareTo(a.value));

                // ── Harmonic/subharmonic disambiguation ──
                // For each candidate, check if a peak at half the lag (double BPM) exists
                // and is reasonably strong. Prefer the musically common range 80-160 BPM.
                int bestBpm = 0;
                double bestScore = 0;

                foreach (var (lag, val) in peaks)
                {
                    double candidateBpm = 60.0 / (lag * hopDuration);
                    if (candidateBpm < 50 || candidateBpm > 220) continue;

                    double score = val;

                    // Perceptual tempo preference: gently prefer 80-160 BPM range
                    if (candidateBpm >= 80 && candidateBpm <= 160)
                        score *= 1.15;

                    // Check for harmonic relationship with other peaks
                    int halfLag = lag / 2;
                    int doubleLag = lag * 2;

                    // If there's a strong peak at double BPM (half lag), this might be the subharmonic
                    if (halfLag >= minLag)
                    {
                        double halfCorr = corr[halfLag];
                        double doubleBpm = 60.0 / (halfLag * hopDuration);
                        // If the double-BPM peak is at least 50% as strong AND in a more common range
                        if (halfCorr > val * 0.5 && doubleBpm >= 80 && doubleBpm <= 180)
                            score *= 0.7; // penalize this candidate — the double-BPM is likely the real tempo
                    }

                    // If there's a strong peak at half BPM (double lag), this candidate is fine
                    if (doubleLag <= maxLag && corr[doubleLag] > val * 0.4)
                        score *= 1.1; // slight bonus: the half-BPM subharmonic confirms this tempo

                    if (score > bestScore)
                    {
                        bestScore = score;
                        bestBpm = (int)Math.Round(candidateBpm);
                    }
                }

                // Final half-time correction: if BPM is suspiciously low, check double
                if (bestBpm > 0 && bestBpm < 80)
                {
                    int doubled = bestBpm * 2;
                    if (doubled >= 80 && doubled <= 200)
                        bestBpm = doubled;
                }

                return (bestBpm >= 50 && bestBpm <= 220) ? bestBpm : 0;
            }
            catch
            {
                return 0;
            }
            finally
            {
                readerDisposable?.Dispose();
            }
        }

        /// <summary>
        /// Tries to extract Replay Gain (track gain) from ID3v2, APEv2, Xiph/Vorbis comments.
        /// </summary>
    }
}