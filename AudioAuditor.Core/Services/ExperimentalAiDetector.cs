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
    /// Eight checks:
    ///   1. Ultrasonic energy excess (spectral rolloff extrapolation)
    ///   2. High-frequency stereo correlation
    ///   3. Spectral regularity (roughness + frame similarity)
    ///   4. Spectral centroid stability
    ///   5. Dynamic uniformity (RMS coefficient of variation)
    ///   6. Peak saturation / hard limiting artifact
    ///   7. Crest factor homogeneity (uniform dynamic compression)
    ///   8. Spectral grid peaks (deconvolution / upsampling artifact)
    ///
    /// Audio is sampled from three regions of the track rather than from the
    /// head, and every check spreads its frame budget across that buffer —
    /// see <see cref="FrameOffset"/> for why that matters.
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
        private const int MaxFramesPerCheck = 30;

        // Three 10-second regions instead of 30 seconds from the head. Same decode budget, but an
        // intro is the least representative part of a track: measuring "does this vary" over one
        // sparse opening section is how a slow build-up reads as machine-uniform.
        private const int RegionSeconds = 10;
        private static readonly double[] RegionStarts = { 0.10, 0.50, 0.85 };

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
        public static ExperimentalResult Analyze(string filePath) => Analyze(filePath, null);

        /// <param name="shared">
        /// Decoder already opened by <see cref="AudioAnalyzer.AnalyzeFile(string, IAnalysisSettings, CancellationToken)"/>,
        /// rewound and reused instead of opening a fourth one for this file. Null (the public
        /// overload, and every caller outside the analyzer) opens its own exactly as before.
        /// </param>
        internal static ExperimentalResult Analyze(string filePath, AudioAnalyzer.SharedAudioSource? shared)
        {
            var result = new ExperimentalResult();
            var flags = new List<(string flag, double weight)>();
            int primaryFlagCount = 0;   // checks 1-5; checks 6-7 are supporting-only

            try
            {
                using var lease = AudioAnalyzer.AudioLease.Open(filePath, shared);
                IDisposable disposable = lease.Reader;
                ISampleProvider samples = lease.Samples;
                WaveFormat waveFormat = lease.Format;

                int sampleRate = waveFormat.SampleRate;
                int channels = waveFormat.Channels;

                // Read interleaved samples from three regions of the track
                float[] rawSamples = ReadRegions(disposable, samples, sampleRate, channels);
                if (rawSamples.Length < FftSize * channels)
                    return result; // Too short to analyze

                // Extract mono and stereo channels
                float[] monoSamples = ToMono(rawSamples, channels);
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

                // ── Check 8: Spectral grid peaks (deconvolution artifact) ──
                var gridResult = CheckSpectralGridPeaks(monoSamples, sampleRate);
                if (gridResult.HasValue)
                    flags.Add(gridResult.Value);

                primaryFlagCount = flags.Count;

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

                // Require 2 flags or one very strong flag — and at least one PRIMARY flag
                // (checks 1-5). Checks 6-7 only describe aggressive limiting, which is the norm on
                // loudness-war human masters; letting the pair of them alone reach `flags.Count >= 2`
                // made those files suspicious on their own, which this class's contract rules out.
                if (primaryFlagCount > 0 && (flags.Count >= 2 || totalWeight >= 0.6))
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
            var avgSpectrum = ComputeAverageSpectrum(mono, FftSize, MaxFramesPerCheck);
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
                int offset = FrameOffset(frame, numFrames, Math.Min(left.Length, right.Length), FftSize);
                if (offset + FftSize > left.Length || offset + FftSize > right.Length) break;

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
        //  with high similarity between frames. Natural audio has more
        //  variation (spectral roughness) and more diverse frames.
        //  Requires BOTH low roughness AND high similarity to flag.
        //  Frames are spread across the sampled regions, so the similarity
        //  term compares parts of the track that are seconds apart rather
        //  than adjacent windows — the question is whether verse and chorus
        //  look alike, not whether 93 ms of audio resembles the next 93 ms.
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
                int offset = FrameOffset(frame, numFrames, mono.Length, FftSize);
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
                int offset = FrameOffset(frame, numFrames, mono.Length, FftSize);
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
                int offset = FrameOffset(frame, numFrames, mono.Length, frameSize);
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
                int offset = FrameOffset(w, numWindows, mono.Length, windowSize);
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
        //  Check 8: Spectral Grid Peaks (deconvolution artifact)
        //
        //  Transposed-convolution / upsampling stacks leave narrow spectral
        //  ridges at an architecture-fixed spacing — the audio equivalent of
        //  the checkerboard artifact in generated images (Afchar et al.,
        //  ISMIR 2025, arXiv:2506.19108, validated on Suno and Udio).
        //
        //  This asks a different question from check 1. Check 1 asks "is
        //  there too much energy above 16 kHz", which is unanswerable on
        //  lossy files because the encoder's lowpass removed the evidence.
        //  This asks "are there evenly spaced ridges", which lives in the
        //  mid band that survives encoding.
        //
        //  KNOWN CONFOUND: musical harmonics are also evenly spaced. The
        //  discriminator is that harmonics move with the melody and smear
        //  out of a spectrum averaged across the whole track, while a
        //  generator artifact sits at a fixed frequency and survives
        //  averaging. Sustained single-key drones are therefore the
        //  realistic false positive, which is why this stays inside the
        //  0.5-capped spectral tier and cannot accuse a file on its own.
        // ══════════════════════════════════════════════════════════════

        private const int GridFftSize = 16384;      // ~2.7 Hz/bin at 44.1 kHz
        private const int GridMaxFrames = 120;      // spread across the whole buffer
        private const double GridBandBottomHz = 500.0;
        private const double GridBandTopHz = 16000.0;
        private const double GridContinuumRadiusHz = 60.0;  // median filter half-width
        private const double GridPeakProminenceDb = 4.0;
        private const int GridMinPeaks = 6;
        private const double GridMinSpacingHz = 20.0;
        private const double GridMaxSpacingHz = 2000.0;

        // Calibrated against a 6-file labeled set (3 Suno-family, 3 human), each lossless input also
        // measured as a 320 kbps transcode so the codec could not be doing the separating:
        //   AI   0.227 – 0.301
        //   human 0.094 – 0.146  (FLAC and MP3 within 0.02 of each other)
        // Threshold sits in that gap, 30 % above the highest human value. Six files is a smoke
        // test, not a validation set — re-run AiDetectionCalibration before moving this.
        private const double GridPeriodicityThreshold = 0.19;

        internal readonly record struct GridPeakMeasurement(
            double Periodicity,   // 0–1, strongest normalised autocorrelation of the residual
            double SpacingHz,     // median gap between ridges
            int PeakCount);       // residual peaks clearing the prominence floor

        /// <summary>
        /// Measures evenly spaced narrow ridges in an averaged magnitude spectrum. Pure function of
        /// its inputs — bin width is derived from <paramref name="spectrum"/>.Length, so any FFT
        /// size works and the behaviour can be pinned with synthetic spectra.
        /// </summary>
        internal static GridPeakMeasurement MeasureSpectralGridPeaks(double[] spectrum, int sampleRate)
        {
            var none = new GridPeakMeasurement(0, 0, 0);
            int specLen = spectrum.Length;
            if (specLen < 512 || sampleRate <= 0) return none;

            double binHz = (double)sampleRate / (2 * specLen);

            // Stop below the codec's lowpass. A brick wall is an enormous step in the residual and
            // would manufacture periodicity out of an encoder artifact — the single most likely
            // false positive in this check, so the band ends well short of it.
            int cutoffHz = AudioAnalyzer.FindCutoffFrequency(spectrum, sampleRate, out _);
            double topHz = cutoffHz > 0 ? Math.Min(GridBandTopHz, cutoffHz * 0.95) : GridBandTopHz;

            int lo = (int)(GridBandBottomHz / binHz);
            int hi = (int)Math.Min(topHz / binHz, specLen - 1);
            int n = hi - lo;
            if (n < 256) return none;

            var db = new double[n];
            for (int i = 0; i < n; i++)
                db[i] = 20.0 * Math.Log10(Math.Max(spectrum[lo + i], 1e-12));

            // Residual against a median continuum. Median, not mean: it follows the spectral
            // envelope without being dragged up by the very peaks we are trying to isolate.
            int radius = Math.Max(3, (int)(GridContinuumRadiusHz / binHz));
            var residual = new double[n];
            var window = new double[radius * 2 + 1];
            for (int i = 0; i < n; i++)
            {
                int start = Math.Max(0, i - radius);
                int end = Math.Min(n - 1, i + radius);
                int count = end - start + 1;
                Array.Copy(db, start, window, 0, count);
                Array.Sort(window, 0, count);
                residual[i] = db[i] - window[count / 2];
            }

            // Peak pick: local maxima clearing the prominence floor.
            var peakBins = new List<int>();
            for (int i = 1; i < n - 1; i++)
            {
                if (residual[i] >= GridPeakProminenceDb
                    && residual[i] >= residual[i - 1]
                    && residual[i] > residual[i + 1])
                {
                    peakBins.Add(i);
                }
            }

            // A ridge landing on fs/4, fs/8 or fs/16 is not measured here. The paper calls that grid
            // out, but on the calibration set it fired on half the human files too: with dozens of
            // ridges in band, one landing near a given frequency is chance, not architecture.
            if (peakBins.Count < 2)
                return new GridPeakMeasurement(0, 0, peakBins.Count);

            // Periodicity via autocorrelation of the mean-removed residual. Cheaper and far more
            // testable than fitting a grid to the peak list, and it answers the same question:
            // does the residual repeat at a fixed spacing.
            double mean = 0;
            for (int i = 0; i < n; i++) mean += residual[i];
            mean /= n;
            for (int i = 0; i < n; i++) residual[i] -= mean;

            int lagMin = Math.Max(2, (int)(GridMinSpacingHz / binHz));
            int lagMax = Math.Min(n / 3, (int)(GridMaxSpacingHz / binHz));
            if (lagMax <= lagMin) return none;

            double bestR = 0;
            for (int lag = lagMin; lag <= lagMax; lag++)
            {
                double dot = 0, energyA = 0, energyB = 0;
                for (int i = 0; i + lag < n; i++)
                {
                    double a = residual[i];
                    double b = residual[i + lag];
                    dot += a * b;
                    energyA += a * a;
                    energyB += b * b;
                }

                double denom = Math.Sqrt(energyA * energyB);
                if (denom < 1e-12) continue;

                // Pearson-style per lag rather than dividing by total energy: the biased form
                // decays with lag and would systematically hide wide ridge spacings.
                double r = dot / denom;
                if (r > bestR) bestR = r;
            }

            // Spacing comes from the peak positions, not from the winning lag. A comb correlates
            // just as well at every multiple of its spacing, and when the true spacing is not a
            // whole number of bins the integer lag that wins is usually a harmonic of it — a
            // 200 Hz grid reported as 2000 Hz. The median gap between ridges is the direct
            // measurement and shrugs off a few spurious peaks.
            var gaps = new double[peakBins.Count - 1];
            for (int i = 1; i < peakBins.Count; i++)
                gaps[i - 1] = (peakBins[i] - peakBins[i - 1]) * binHz;
            Array.Sort(gaps);

            return new GridPeakMeasurement(bestR, gaps[gaps.Length / 2], peakBins.Count);
        }

        /// <summary>
        /// Runs the grid-peak measurement over a file and returns the raw numbers whether or not
        /// they clear the threshold. Threshold calibration needs to see the values that did *not*
        /// fire, which the flag list by definition never shows.
        /// </summary>
        internal static GridPeakMeasurement MeasureFileGridPeaks(string filePath)
        {
            var (disposable, samples, waveFormat) = AudioAnalyzer.OpenAudioFile(filePath);
            using var _ = disposable;

            int channels = waveFormat.Channels;
            float[] raw = ReadRegions(disposable, samples, waveFormat.SampleRate, channels);
            float[] mono = ToMono(raw, channels);
            if (mono.Length < GridFftSize * 3) return default;

            var avgSpectrum = ComputeAverageSpectrum(mono, GridFftSize, GridMaxFrames);
            return avgSpectrum == null
                ? default
                : MeasureSpectralGridPeaks(avgSpectrum, waveFormat.SampleRate);
        }

        private static (string flag, double weight)? CheckSpectralGridPeaks(float[] mono, int sampleRate)
        {
            if (mono.Length < GridFftSize * 3) return null;

            var avgSpectrum = ComputeAverageSpectrum(mono, GridFftSize, GridMaxFrames);
            if (avgSpectrum == null) return null;

            var m = MeasureSpectralGridPeaks(avgSpectrum, sampleRate);
            if (m.Periodicity <= GridPeriodicityThreshold || m.PeakCount < GridMinPeaks)
                return null;

            // Scaled so the measured AI range (r ≈ 0.23–0.30) lands around 0.25–0.39. This is the
            // strongest single signal in the tier, but the tier's 0.5 cap still means one flag on
            // its own cannot push the combined verdict past "No" — check 8 corroborates, it does
            // not accuse.
            double weight = 0.20 + Math.Min((m.Periodicity - GridPeriodicityThreshold) / 0.15, 1.0) * 0.25;
            string flag = $"Spectral grid peaks (Δf={m.SpacingHz:F0} Hz, {m.PeakCount} peaks, r={m.Periodicity:F2})";

            return (flag, Math.Min(weight, 0.45));
        }

        // ══════════════════════════════════════════════════════════════
        //  DSP Helpers
        // ══════════════════════════════════════════════════════════════

        /// <summary>
        /// Reads <see cref="RegionSeconds"/> from each of <see cref="RegionStarts"/> and returns the
        /// regions concatenated as one interleaved buffer. Falls back to a plain sequential read
        /// when the track is too short to hold three distinct regions.
        /// </summary>
        private static float[] ReadRegions(IDisposable? disposable, ISampleProvider provider, int sampleRate, int channels)
        {
            long regionFrames = (long)sampleRate * RegionSeconds;
            var seekable = disposable as WaveStream;
            long totalFrames = seekable != null && seekable.WaveFormat.BlockAlign > 0
                ? seekable.Length / seekable.WaveFormat.BlockAlign
                : 0;

            // Short track, or a decoder that cannot seek and reports no length: one straight read
            // covering the same budget is the honest fallback.
            if (totalFrames < regionFrames * RegionStarts.Length)
                return ReadSequential(provider, (int)(regionFrames * RegionStarts.Length * channels));

            var all = new List<float>((int)(regionFrames * RegionStarts.Length * channels));
            var buffer = new float[8192];
            long currentFrame = 0;

            foreach (double position in RegionStarts)
            {
                long startFrame = (long)(totalFrames * position);
                if (startFrame + regionFrames > totalFrames)
                    startFrame = totalFrames - regionFrames;
                if (startFrame < 0) startFrame = 0;

                // Same seek-or-skip shape as AudioAnalyzer.AnalyzeSpectralContent: set Position when
                // the stream is seekable, otherwise read and discard forward.
                if (seekable != null)
                {
                    seekable.Position = startFrame * seekable.WaveFormat.BlockAlign;
                    currentFrame = startFrame;
                }
                else
                {
                    long toSkip = (startFrame - currentFrame) * channels;
                    while (toSkip > 0)
                    {
                        int chunk = (int)Math.Min(toSkip, buffer.Length);
                        int got = provider.Read(buffer, 0, chunk);
                        if (got <= 0) break;
                        toSkip -= got;
                    }
                    currentFrame = startFrame;
                }

                long remaining = regionFrames * channels;
                while (remaining > 0)
                {
                    int toRead = (int)Math.Min(buffer.Length, remaining);
                    int read = provider.Read(buffer, 0, toRead);
                    if (read <= 0) break;

                    for (int i = 0; i < read; i++)
                        all.Add(buffer[i]);

                    remaining -= read;
                    currentFrame += read / channels;
                }
            }

            return all.ToArray();
        }

        /// <summary>
        /// Downmixes an interleaved buffer to mono from the front L/R pair. Returns the input
        /// unchanged when already mono.
        ///
        /// Front pair only, not an average of every channel: folding a band-limited LFE and the
        /// surrounds into the sum would drag the spectral centroid down and add low-frequency
        /// energy that no check here is calibrated for.
        /// </summary>
        private static float[] ToMono(float[] interleaved, int channels)
        {
            if (channels <= 1) return interleaved;

            var mono = new float[interleaved.Length / channels];
            for (int i = 0; i < mono.Length; i++)
                mono[i] = (interleaved[i * channels] + interleaved[i * channels + 1]) * 0.5f;
            return mono;
        }

        private static float[] ReadSequential(ISampleProvider provider, int maxSamples)
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

        private static double[]? ComputeAverageSpectrum(float[] mono, int fftSize, int maxFrames)
        {
            int halfFft = fftSize / 2;
            int available = mono.Length / fftSize;
            if (available < 3) return null;

            int numFrames = Math.Min(available, maxFrames);
            var avgSpectrum = new double[halfFft];

            for (int frame = 0; frame < numFrames; frame++)
            {
                var mag = ComputeMagnitudeSpectrum(mono, FrameOffset(frame, numFrames, mono.Length, fftSize), fftSize);
                for (int i = 0; i < halfFft; i++)
                    avgSpectrum[i] += mag[i];
            }

            for (int i = 0; i < halfFft; i++)
                avgSpectrum[i] /= numFrames;

            return avgSpectrum;
        }

        /// <summary>
        /// Sample offset of frame <paramref name="frame"/> when <paramref name="numFrames"/> frames
        /// are spread evenly across the whole buffer.
        ///
        /// Every check here asks "how much does this track vary". Taking frames consecutively from
        /// offset 0 — which is what these checks used to do — answered that over the first ~3
        /// seconds and then generalised to the track, which is exactly how a sustained intro reads
        /// as machine-uniform. Spreading the same frame budget over the buffer measures the span
        /// the verdict actually claims to be about.
        /// </summary>
        private static int FrameOffset(int frame, int numFrames, int totalSamples, int frameSize)
        {
            long span = totalSamples - frameSize;
            if (span <= 0 || numFrames <= 1) return 0;
            return (int)(span * frame / (numFrames - 1));
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
