using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace AudioQualityChecker.Services
{
    /// <summary>
    /// Experimental AI detection using spectral/waveform analysis.
    /// Seven checks:
    ///   1. Ultrasonic energy excess (spectral rolloff extrapolation)
    ///   2. High-frequency stereo correlation
    ///   3. Spectral regularity (roughness + frame similarity)
    ///   4. Spectral centroid stability
    ///   5. Dynamic uniformity (RMS coefficient of variation)
    ///   6. Peak saturation / hard limiting artifact
    ///   7. Crest factor homogeneity (uniform dynamic compression)
    ///
    /// Checks 6-7 target obfuscation: deliberate hard-clipping or
    /// aggressive limiting applied to AI audio to destroy embedded
    /// watermarks. They are supporting-only flags (weight ≤0.20) and
    /// will never trigger Suspicious on their own.
    ///
    /// WARNING: This is heuristic-based and WILL produce false positives.
    /// It should only be enabled as an opt-in experimental feature.
    /// </summary>
    public static class ExperimentalAiDetector
    {
        private const int FftSize = 4096;
        private const int MaxSamplesToRead = 44100 * 30; // Analyze up to 30 seconds

        public class ExperimentalResult
        {
            public bool Suspicious { get; set; }
            public double Confidence { get; set; }
            public List<string> Flags { get; set; } = new();
            public string Summary { get; set; } = "";
        }

        /// <summary>
        /// Runs experimental spectral AI detection on an audio file.
        /// Returns a result with confidence score and flags describing what was found.
        /// </summary>
        public static ExperimentalResult Analyze(string filePath)
        {
            var result = new ExperimentalResult();
            var flags = new List<(string flag, double weight)>();

            try
            {
                var (disposable, samples, waveFormat) = AudioAnalyzer.OpenAudioFile(filePath);
                using var _ = disposable;

                int sampleRate = waveFormat.SampleRate;
                int channels = waveFormat.Channels;

                // Read interleaved samples
                float[] rawSamples = ReadSamples(samples, MaxSamplesToRead * channels);
                if (rawSamples.Length < FftSize * channels)
                    return result; // Too short to analyze

                // Extract mono and stereo channels
                float[] monoSamples;
                float[]? leftChannel = null;
                float[]? rightChannel = null;

                if (channels >= 2)
                {
                    leftChannel = new float[rawSamples.Length / channels];
                    rightChannel = new float[rawSamples.Length / channels];
                    for (int i = 0; i < leftChannel.Length; i++)
                    {
                        leftChannel[i] = rawSamples[i * channels];
                        rightChannel[i] = rawSamples[i * channels + 1];
                    }
                    monoSamples = new float[leftChannel.Length];
                    for (int i = 0; i < monoSamples.Length; i++)
                        monoSamples[i] = (leftChannel[i] + rightChannel[i]) * 0.5f;
                }
                else
                {
                    monoSamples = rawSamples;
                }

                // ── Check 1: Ultrasonic energy excess ──
                var ultraResult = CheckUltrasonicEnergy(monoSamples, sampleRate);
                if (ultraResult.HasValue)
                    flags.Add(ultraResult.Value);

                // ── Check 2: HF stereo correlation (stereo files only) ──
                if (leftChannel != null && rightChannel != null)
                {
                    var stereoResult = CheckStereoCorrelation(leftChannel, rightChannel, sampleRate);
                    if (stereoResult.HasValue)
                        flags.Add(stereoResult.Value);
                }

                // ── Check 3: Spectral regularity ──
                var regularityResult = CheckSpectralRegularity(monoSamples, sampleRate);
                if (regularityResult.HasValue)
                    flags.Add(regularityResult.Value);

                // ── Check 4: Spectral centroid stability ──
                var centroidResult = CheckSpectralCentroidStability(monoSamples, sampleRate);
                if (centroidResult.HasValue)
                    flags.Add(centroidResult.Value);

                // ── Check 5: Dynamic uniformity ──
                var dynamicsResult = CheckDynamicUniformity(monoSamples, sampleRate);
                if (dynamicsResult.HasValue)
                    flags.Add(dynamicsResult.Value);

                // ── Check 6: Hard limiting / peak saturation artifact ──
                var limitResult = CheckHardLimitingArtifact(monoSamples);
                if (limitResult.HasValue)
                    flags.Add(limitResult.Value);

                // ── Check 7: Crest factor homogeneity ──
                var crestResult = CheckCrestFactorHomogeneity(monoSamples, sampleRate);
                if (crestResult.HasValue)
                    flags.Add(crestResult.Value);
            }
            catch
            {
                return result; // Analysis failed, return empty result
            }

            // Aggregate results
            if (flags.Count > 0)
            {
                double totalWeight = flags.Sum(f => f.weight);
                result.Confidence = Math.Min(totalWeight, 1.0);
                result.Flags = flags.Select(f => f.flag).ToList();

                // Require at least 2 flags or one very strong flag to mark as suspicious
                if (flags.Count >= 2 || totalWeight >= 0.6)
                {
                    result.Suspicious = true;
                    result.Summary = string.Join(", ", result.Flags);
                }
            }

            return result;
        }

        // ══════════════════════════════════════════════════════════════
        //  Check 1: Ultrasonic Energy Excess
        //  Extrapolates natural rolloff from 8-16kHz bands and checks
        //  if energy above 16kHz exceeds what's expected. AI generators
        //  often leave excess ultrasonic energy from their synthesis.
        // ══════════════════════════════════════════════════════════════

        private static (string flag, double weight)? CheckUltrasonicEnergy(float[] mono, int sampleRate)
        {
            if (sampleRate < 40000) return null; // Need at least ~40kHz SR to analyze ultrasonic

            int halfFft = FftSize / 2;
            double binHz = (double)sampleRate / FftSize;

            // Frequency band boundaries in bins
            int bin8k = (int)(8000 / binHz);
            int bin12k = (int)(12000 / binHz);
            int bin16k = (int)(16000 / binHz);
            int binNyquist = halfFft - 1;

            if (bin16k >= binNyquist - 2) return null; // Not enough resolution above 16kHz

            // Compute average spectrum across multiple frames
            var avgSpectrum = ComputeAverageSpectrum(mono, FftSize);
            if (avgSpectrum == null) return null;

            // Measure energy in bands
            double energy8to12 = BandEnergy(avgSpectrum, bin8k, bin12k);
            double energy12to16 = BandEnergy(avgSpectrum, bin12k, bin16k);
            double energyAbove16 = BandEnergy(avgSpectrum, bin16k, binNyquist);

            if (energy8to12 < 1e-10 || energy12to16 < 1e-10) return null; // Too quiet

            // Extrapolate expected rolloff: if energy drops from 8-12k to 12-16k,
            // continue that rate above 16kHz
            double rolloffRate = energy12to16 / energy8to12;
            double expectedAbove16 = energy12to16 * rolloffRate;

            if (expectedAbove16 < 1e-12) expectedAbove16 = 1e-12;
            double excess = energyAbove16 / expectedAbove16;

            // Threshold: excess > 3.0 suggests unnatural ultrasonic content
            if (excess > 3.0)
            {
                double weight = Math.Min((excess - 3.0) / 7.0, 0.4) + 0.15;
                return ($"Ultrasonic excess ({excess:F1}x)", weight);
            }

            return null;
        }

        // ══════════════════════════════════════════════════════════════
        //  Check 2: High-Frequency Stereo Correlation
        //  AI generators often produce nearly identical L/R content
        //  in the high-frequency band (>4kHz). Natural recordings have
        //  lower correlation due to room acoustics and mic placement.
        //  Threshold: correlation > 0.96 in HF band is suspicious.
        // ══════════════════════════════════════════════════════════════

        private static (string flag, double weight)? CheckStereoCorrelation(float[] left, float[] right, int sampleRate)
        {
            if (left.Length < FftSize || right.Length < FftSize) return null;

            // Simple windowed cross-correlation in HF band
            // We'll use FFT-based approach: compute magnitude spectra for both channels
            // and correlate them in the HF region
            int halfFft = FftSize / 2;
            double binHz = (double)sampleRate / FftSize;
            int bin4k = (int)(4000 / binHz);

            int numFrames = Math.Min(left.Length / FftSize, 20); // Up to 20 frames
            if (numFrames < 3) return null;

            var correlations = new List<double>();

            for (int frame = 0; frame < numFrames; frame++)
            {
                int offset = frame * FftSize;
                if (offset + FftSize > left.Length) break;

                var leftMag = ComputeMagnitudeSpectrum(left, offset, FftSize);
                var rightMag = ComputeMagnitudeSpectrum(right, offset, FftSize);

                // Correlate HF region only (above 4kHz)
                double corr = PearsonCorrelation(leftMag, rightMag, bin4k, halfFft);
                if (!double.IsNaN(corr))
                    correlations.Add(corr);
            }

            if (correlations.Count < 3) return null;

            double avgCorrelation = correlations.Average();

            // Very high HF stereo correlation is suspicious
            if (avgCorrelation > 0.96)
            {
                double weight = Math.Min((avgCorrelation - 0.96) / 0.04, 1.0) * 0.35 + 0.1;
                return ($"HF stereo correlation ({avgCorrelation:F3})", weight);
            }

            return null;
        }

        // ══════════════════════════════════════════════════════════════
        //  Check 3: Spectral Regularity
        //  AI-generated audio tends to have very smooth, regular spectra
        //  with high frame-to-frame similarity. Natural audio has more
        //  variation (spectral roughness) and more diverse frames.
        //  Requires BOTH low roughness AND high similarity to flag.
        // ══════════════════════════════════════════════════════════════

        private static (string flag, double weight)? CheckSpectralRegularity(float[] mono, int sampleRate)
        {
            if (mono.Length < FftSize * 5) return null; // Need at least 5 frames

            int halfFft = FftSize / 2;
            int numFrames = Math.Min(mono.Length / FftSize, 30);
            if (numFrames < 5) return null;

            var allSpectra = new double[numFrames][];
            var roughnessValues = new List<double>();

            for (int frame = 0; frame < numFrames; frame++)
            {
                int offset = frame * FftSize;
                if (offset + FftSize > mono.Length) break;

                var mag = ComputeMagnitudeSpectrum(mono, offset, FftSize);
                allSpectra[frame] = mag;

                // Spectral roughness: std deviation of differences between adjacent bins
                double roughness = ComputeSpectralRoughness(mag, halfFft);
                roughnessValues.Add(roughness);
            }

            if (roughnessValues.Count < 5) return null;

            double avgRoughness = roughnessValues.Average();

            // Frame-to-frame cosine similarity
            var similarities = new List<double>();
            for (int i = 1; i < numFrames; i++)
            {
                if (allSpectra[i] == null || allSpectra[i - 1] == null) continue;
                double sim = CosineSimilarity(allSpectra[i - 1], allSpectra[i], 0, halfFft);
                if (!double.IsNaN(sim))
                    similarities.Add(sim);
            }

            if (similarities.Count < 3) return null;

            double avgSimilarity = similarities.Average();

            // Both must be suspicious: very smooth spectrum AND very similar frames
            bool lowRoughness = avgRoughness < 0.02;
            bool highSimilarity = avgSimilarity > 0.985;

            if (lowRoughness && highSimilarity)
            {
                double weight = 0.3;
                if (avgSimilarity > 0.995) weight += 0.1;
                if (avgRoughness < 0.01) weight += 0.1;
                return ($"Spectral regularity (rough={avgRoughness:F4}, sim={avgSimilarity:F4})", weight);
            }

            return null;
        }

        // ══════════════════════════════════════════════════════════════
        //  Check 4: Spectral Centroid Stability
        //  The spectral centroid (weighted mean frequency) naturally
        //  shifts across sections in real music (verse/chorus, dynamics,
        //  instrument changes). AI generators tend to produce a
        //  suspiciously stable centroid throughout the track.
        //  Threshold: coefficient of variation < 0.02 over 10+ frames.
        // ══════════════════════════════════════════════════════════════

        private static (string flag, double weight)? CheckSpectralCentroidStability(float[] mono, int sampleRate)
        {
            if (mono.Length < FftSize * 10) return null;

            int halfFft = FftSize / 2;
            int numFrames = Math.Min(mono.Length / FftSize, 30);
            if (numFrames < 10) return null;

            double binHz = (double)sampleRate / FftSize;
            var centroids = new List<double>();

            for (int frame = 0; frame < numFrames; frame++)
            {
                int offset = frame * FftSize;
                if (offset + FftSize > mono.Length) break;

                var mag = ComputeMagnitudeSpectrum(mono, offset, FftSize);

                // Weighted mean frequency (spectral centroid)
                double weightedSum = 0, totalWeight = 0;
                for (int i = 1; i < halfFft; i++)
                {
                    double power = mag[i] * mag[i];
                    weightedSum += i * binHz * power;
                    totalWeight += power;
                }

                if (totalWeight > 1e-10)
                    centroids.Add(weightedSum / totalWeight);
            }

            if (centroids.Count < 10) return null;

            double mean = centroids.Average();
            if (mean < 200) return null; // Near-silent or sub-bass only

            double variance = centroids.Sum(c => (c - mean) * (c - mean)) / centroids.Count;
            double cv = Math.Sqrt(variance) / mean;

            // Very stable centroid (CV < 0.02) is suspicious for real music
            if (cv < 0.02)
            {
                double weight = Math.Min((0.02 - cv) / 0.02, 1.0) * 0.15 + 0.1;
                return ($"Centroid stability (CV={cv:F4})", weight);
            }

            return null;
        }

        // ══════════════════════════════════════════════════════════════
        //  Check 5: Dynamic Uniformity
        //  Natural music has loudness variation across sections (intro,
        //  verse, chorus, breakdown, etc.). AI generators often produce
        //  audio with unnaturally uniform RMS across the whole track.
        //  Uses ~500ms frames; coefficient of variation < 0.04 is
        //  suspicious. Low weight — mainly a supporting indicator.
        // ══════════════════════════════════════════════════════════════

        private static (string flag, double weight)? CheckDynamicUniformity(float[] mono, int sampleRate)
        {
            int frameSize = sampleRate / 2; // ~500ms per frame
            if (mono.Length < frameSize * 8) return null; // Require at least 4 seconds

            int numFrames = Math.Min(mono.Length / frameSize, 20);
            if (numFrames < 8) return null;

            var rmsValues = new List<double>();
            for (int frame = 0; frame < numFrames; frame++)
            {
                int offset = frame * frameSize;
                if (offset + frameSize > mono.Length) break;

                double sumSq = 0;
                for (int i = 0; i < frameSize; i++)
                    sumSq += mono[offset + i] * (double)mono[offset + i];

                double rms = Math.Sqrt(sumSq / frameSize);
                if (rms > 1e-5)
                    rmsValues.Add(rms);
            }

            if (rmsValues.Count < 8) return null;

            double mean = rmsValues.Average();
            if (mean < 5e-4) return null; // Too quiet overall

            double variance = rmsValues.Sum(r => (r - mean) * (r - mean)) / rmsValues.Count;
            double cv = Math.Sqrt(variance) / mean;

            // Natural music typically has CV > 0.08 from section-to-section dynamics
            // Very low CV (< 0.04) suggests unnaturally flat dynamics
            if (cv < 0.04)
            {
                double weight = Math.Min((0.04 - cv) / 0.04, 1.0) * 0.15 + 0.1;
                return ($"Dynamic uniformity (CV={cv:F4})", weight);
            }

            return null;
        }

        // ══════════════════════════════════════════════════════════════
        //  Check 6: Hard Limiting / Peak Saturation Artifact
        //  When AI audio is deliberately hard-clipped or limited to
        //  destroy embedded watermarks that live in loud transients,
        //  it leaves a characteristic "ceiling saturation" — a disproportionate
        //  fraction of samples at or very near ±1.0.
        //  Professional mastering limiters target -0.1 to -0.3 dBFS
        //  (keeping true ceiling samples <0.1%); >0.5% suggests hard
        //  clipping intervention.
        //  NOTE: Supporting flag only — never triggers Suspicious alone.
        //  Natural false positives: heavily clipped rock records (Death
        //  Magnetic-style mastering), distortion effects, overdriven sources.
        // ══════════════════════════════════════════════════════════════

        private static (string flag, double weight)? CheckHardLimitingArtifact(float[] mono)
        {
            if (mono.Length < 2205) return null;

            int ceilingSamples = 0;
            for (int i = 0; i < mono.Length; i++)
            {
                if (Math.Abs(mono[i]) >= 0.9990f)
                    ceilingSamples++;
            }

            double ceilingRatio = (double)ceilingSamples / mono.Length;

            // Professional limiters prevent true ceiling saturation.
            // >0.5% of samples at ±1.0 suggests hard clipping.
            if (ceilingRatio > 0.005)
            {
                double weight = Math.Min((ceilingRatio - 0.005) / 0.045, 1.0) * 0.12 + 0.08;
                return ($"Peak saturation ({ceilingRatio:P1} at ceiling)", weight);
            }

            return null;
        }

        // ══════════════════════════════════════════════════════════════
        //  Check 7: Crest Factor Homogeneity
        //  When AI audio is uniformly compressed or limited to suppress
        //  peaks (and any embedded dynamic watermarks), the short-term
        //  crest factor (peak/RMS per 20ms window) becomes suspiciously
        //  uniform. Real music has high crest factor variation: loud
        //  transients, quiet passages, and section changes all produce
        //  widely varying ratios. Aggressively limited audio has every
        //  window at nearly the same peak-to-RMS ratio.
        //  Threshold: CV < 0.08 AND mean CF < 8 dB (both required).
        //  NOTE: Supporting flag only. Distorted/EDM genres can trigger
        //  this legitimately — always check in combination with other flags.
        // ══════════════════════════════════════════════════════════════

        private static (string flag, double weight)? CheckCrestFactorHomogeneity(float[] mono, int sampleRate)
        {
            int windowSize = sampleRate * 20 / 1000; // ~20ms windows
            if (windowSize < 2) windowSize = 2;
            if (mono.Length < windowSize * 20) return null;

            int numWindows = Math.Min(mono.Length / windowSize, 100);
            var crestFactors = new List<double>();

            for (int w = 0; w < numWindows; w++)
            {
                int offset = w * windowSize;
                float peak = 0;
                double sumSq = 0;

                for (int i = 0; i < windowSize; i++)
                {
                    float s = Math.Abs(mono[offset + i]);
                    if (s > peak) peak = s;
                    sumSq += s * s;
                }

                double rms = Math.Sqrt(sumSq / windowSize);
                if (rms < 1e-6 || peak < 0.01f) continue; // Skip silent windows

                crestFactors.Add(peak / rms);
            }

            if (crestFactors.Count < 20) return null;

            double mean = crestFactors.Average();
            double variance = crestFactors.Sum(c => (c - mean) * (c - mean)) / crestFactors.Count;
            double cv = Math.Sqrt(variance) / mean;
            double meanCfDb = 20.0 * Math.Log10(Math.Max(mean, 1e-10));

            // Linear equivalent of 8 dB: 10^(8/20) ≈ 2.512
            // Combined requirement: low variation AND low absolute crest factor.
            // Either alone is common in normal audio; together they suggest
            // uniform aggressive limiting rather than natural dynamics.
            const double limit8dB = 2.511886;
            if (cv < 0.08 && mean < limit8dB)
            {
                double weight = Math.Min((0.08 - cv) / 0.08, 1.0) * 0.12 + 0.08;
                return ($"Crest factor homogeneity (CF={meanCfDb:F1}dB, CV={cv:F3})", weight);
            }

            return null;
        }

        // ══════════════════════════════════════════════════════════════
        //  DSP Helpers
        // ══════════════════════════════════════════════════════════════

        private static float[] ReadSamples(ISampleProvider provider, int maxSamples)
        {
            var buffer = new float[8192];
            var all = new List<float>(maxSamples);
            int remaining = maxSamples;

            while (remaining > 0)
            {
                int toRead = Math.Min(buffer.Length, remaining);
                int read = provider.Read(buffer, 0, toRead);
                if (read <= 0) break;

                for (int i = 0; i < read; i++)
                    all.Add(buffer[i]);

                remaining -= read;
            }

            return all.ToArray();
        }

        private static double[]? ComputeAverageSpectrum(float[] mono, int fftSize)
        {
            int halfFft = fftSize / 2;
            int numFrames = mono.Length / fftSize;
            if (numFrames < 3) return null;

            numFrames = Math.Min(numFrames, 30);
            var avgSpectrum = new double[halfFft];

            for (int frame = 0; frame < numFrames; frame++)
            {
                var mag = ComputeMagnitudeSpectrum(mono, frame * fftSize, fftSize);
                for (int i = 0; i < halfFft; i++)
                    avgSpectrum[i] += mag[i];
            }

            for (int i = 0; i < halfFft; i++)
                avgSpectrum[i] /= numFrames;

            return avgSpectrum;
        }

        private static double[] ComputeMagnitudeSpectrum(float[] samples, int offset, int fftSize)
        {
            int halfFft = fftSize / 2;
            var fftBuffer = new NAudio.Dsp.Complex[fftSize];

            // Apply Hann window and fill FFT buffer
            for (int i = 0; i < fftSize; i++)
            {
                double window = 0.5 * (1.0 - Math.Cos(2.0 * Math.PI * i / (fftSize - 1)));
                fftBuffer[i].X = (float)(samples[offset + i] * window);
                fftBuffer[i].Y = 0;
            }

            // NAudio FFT (in-place, requires power-of-two)
            int m = (int)Math.Log2(fftSize);
            NAudio.Dsp.FastFourierTransform.FFT(true, m, fftBuffer);

            // Compute magnitude spectrum
            var mag = new double[halfFft];
            for (int i = 0; i < halfFft; i++)
            {
                double re = fftBuffer[i].X;
                double im = fftBuffer[i].Y;
                mag[i] = Math.Sqrt(re * re + im * im);
            }

            return mag;
        }

        private static double BandEnergy(double[] spectrum, int startBin, int endBin)
        {
            double sum = 0;
            endBin = Math.Min(endBin, spectrum.Length);
            for (int i = startBin; i < endBin; i++)
                sum += spectrum[i] * spectrum[i];
            return sum / Math.Max(1, endBin - startBin);
        }

        private static double PearsonCorrelation(double[] a, double[] b, int start, int end)
        {
            end = Math.Min(end, Math.Min(a.Length, b.Length));
            int n = end - start;
            if (n < 2) return double.NaN;

            double sumA = 0, sumB = 0, sumAB = 0, sumA2 = 0, sumB2 = 0;
            for (int i = start; i < end; i++)
            {
                sumA += a[i]; sumB += b[i];
                sumAB += a[i] * b[i];
                sumA2 += a[i] * a[i];
                sumB2 += b[i] * b[i];
            }

            double denomA = n * sumA2 - sumA * sumA;
            double denomB = n * sumB2 - sumB * sumB;
            if (denomA <= 0 || denomB <= 0) return double.NaN;

            return (n * sumAB - sumA * sumB) / Math.Sqrt(denomA * denomB);
        }

        private static double CosineSimilarity(double[] a, double[] b, int start, int end)
        {
            end = Math.Min(end, Math.Min(a.Length, b.Length));
            double dot = 0, magA = 0, magB = 0;
            for (int i = start; i < end; i++)
            {
                dot += a[i] * b[i];
                magA += a[i] * a[i];
                magB += b[i] * b[i];
            }
            if (magA < 1e-20 || magB < 1e-20) return double.NaN;
            return dot / (Math.Sqrt(magA) * Math.Sqrt(magB));
        }

        private static double ComputeSpectralRoughness(double[] spectrum, int length)
        {
            length = Math.Min(length, spectrum.Length);
            if (length < 3) return 0;

            // Roughness = std deviation of bin-to-bin differences
            var diffs = new double[length - 1];
            for (int i = 0; i < length - 1; i++)
                diffs[i] = spectrum[i + 1] - spectrum[i];

            double mean = diffs.Average();
            double variance = diffs.Sum(d => (d - mean) * (d - mean)) / diffs.Length;
            return Math.Sqrt(variance);
        }
    }
}
