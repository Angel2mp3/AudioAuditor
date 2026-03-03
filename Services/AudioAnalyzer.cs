using System;
using System.IO;
using AudioQualityChecker.Models;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using NAudio.Vorbis;
using TagLib;

namespace AudioQualityChecker.Services
{
    public static class AudioAnalyzer
    {
        private const int FftSize = 4096;
        private const int AnalysisSegments = 200;
        private const float ClippingThreshold = 0.9999f;

        public static AudioFileInfo AnalyzeFile(string filePath)
        {
            var info = new AudioFileInfo
            {
                FilePath = filePath,
                FileName = Path.GetFileName(filePath),
                Extension = Path.GetExtension(filePath).ToLowerInvariant()
            };

            try
            {
                var fi = new FileInfo(filePath);
                info.FileSizeBytes = fi.Length;
                info.FileSize = FormatFileSize(fi.Length);

                // ── Metadata via TagLib ──
                try
                {
                    using var tagFile = TagLib.File.Create(filePath);
                    info.Artist = tagFile.Tag.FirstPerformer ?? tagFile.Tag.FirstAlbumArtist ?? "";
                    info.Title = tagFile.Tag.Title ?? "";
                    info.ReportedBitrate = tagFile.Properties.AudioBitrate;
                    info.SampleRate = tagFile.Properties.AudioSampleRate;
                    info.BitsPerSample = tagFile.Properties.BitsPerSample;
                    info.Channels = tagFile.Properties.AudioChannels;
                    info.Duration = FormatDuration(tagFile.Properties.Duration);
                    info.DurationSeconds = tagFile.Properties.Duration.TotalSeconds;

                    // Frequency (sample rate is the audio frequency)
                    info.Frequency = tagFile.Properties.AudioSampleRate;

                    // Extract BPM from tag first
                    if (tagFile.Tag.BeatsPerMinute > 0)
                        info.Bpm = (int)tagFile.Tag.BeatsPerMinute;

                    // Extract Replay Gain from tags
                    ExtractReplayGain(tagFile, info);

                    // If no BPM tag, detect algorithmically
                    if (info.Bpm <= 0)
                    {
                        try { info.Bpm = DetectBpm(filePath); } catch { }
                    }
                }
                catch
                {
                    info.Status = AudioStatus.Corrupt;
                    info.ErrorMessage = "Cannot read metadata (file may be corrupted)";
                    return info;
                }

                // ── Spectral analysis via NAudio ──
                try
                {
                    AnalyzeSpectralContent(filePath, info);
                }
                catch
                {
                    if (info.SampleRate > 0)
                    {
                        info.Status = AudioStatus.Unknown;
                        info.ErrorMessage = "Spectral analysis failed";
                    }
                    else
                    {
                        info.Status = AudioStatus.Corrupt;
                        info.ErrorMessage = "Cannot decode audio data";
                    }
                    return info;
                }

                // ── Optimizer detection ──
                if (DetectOptimizer(info))
                {
                    info.Status = AudioStatus.Optimized;
                    return info;
                }

                // ── Quality verdict ──
                DetermineQuality(info);

                // ── MQA detection (runs after main analysis) ──
                try
                {
                    var mqaResult = MqaDetector.Detect(filePath);
                    if (mqaResult != null)
                    {
                        info.IsMqa = mqaResult.IsMqa;
                        info.IsMqaStudio = mqaResult.IsStudio;
                        info.MqaOriginalSampleRate = mqaResult.OriginalSampleRate;
                        info.MqaEncoder = mqaResult.Encoder;
                    }
                }
                catch { /* MQA detection is optional, don't fail the whole analysis */ }

                // ── AI watermark detection ──
                try
                {
                    var aiResult = AiWatermarkDetector.Detect(filePath);
                    if (aiResult != null && aiResult.IsAiDetected)
                    {
                        info.IsAiGenerated = true;
                        info.AiSource = aiResult.Summary;
                        info.AiSources = aiResult.Sources;
                    }
                }
                catch { /* AI detection is optional */ }

                // ── Album cover detection ──
                try
                {
                    using var tagFile2 = TagLib.File.Create(filePath);
                    info.HasAlbumCover = tagFile2.Tag.Pictures?.Length > 0;
                }
                catch { /* album cover check is optional */ }
            }
            catch (Exception ex)
            {
                info.Status = AudioStatus.Corrupt;
                info.ErrorMessage = $"Error: {ex.Message}";
            }

            return info;
        }

        // ═══════════════════════════════════════════════════════
        //  Spectral Analysis
        // ═══════════════════════════════════════════════════════

        private static void AnalyzeSpectralContent(string filePath, AudioFileInfo info)
        {
            var (disposable, samples, waveFormat) = OpenAudioFile(filePath);
            using var _ = disposable;

            int sampleRate = waveFormat.SampleRate;
            int channels = waveFormat.Channels;

            if (info.SampleRate == 0) info.SampleRate = sampleRate;
            if (info.Channels == 0) info.Channels = channels;

            // For ISampleProvider we can't get Length easily, so estimate from the underlying reader
            long totalFrames;
            if (disposable is AudioFileReader afr)
                totalFrames = afr.Length / afr.WaveFormat.BlockAlign;
            else if (disposable is MediaFoundationReader mfr2)
                totalFrames = mfr2.Length / mfr2.WaveFormat.BlockAlign;
            else if (disposable is WaveStream ws && ws.Length > 0)
                totalFrames = ws.Length / ws.WaveFormat.BlockAlign;
            else
                totalFrames = (long)(info.DurationSeconds * sampleRate);

            int segmentCount = (int)Math.Min(AnalysisSegments, totalFrames / FftSize);

            if (segmentCount < 3)
            {
                info.EffectiveFrequency = 0;
                info.ActualBitrate = 0;
                info.Status = AudioStatus.Unknown;
                info.ErrorMessage = "File too short for analysis";
                return;
            }

            // Skip first/last 5% to avoid intro silence and fade-outs
            long safeStart = (long)(totalFrames * 0.05);
            long safeEnd = (long)(totalFrames * 0.95);
            long safeRange = safeEnd - safeStart - FftSize;
            if (safeRange < FftSize * 3) { safeStart = 0; safeRange = totalFrames - FftSize; }

            long stepFrames = safeRange / segmentCount;

            int spectrumSize = FftSize / 2;
            double[] avgSpectrum = new double[spectrumSize];
            float[] readBuf = new float[FftSize * channels];
            float[] skipBuf = new float[4096 * channels];

            // Hanning window (pre-compute)
            double[] window = new double[FftSize];
            for (int i = 0; i < FftSize; i++)
                window[i] = 0.5 * (1.0 - Math.Cos(2.0 * Math.PI * i / (FftSize - 1)));

            long clippingSamples = 0;
            long totalSamplesRead = 0;
            int validSegments = 0;

            long currentFrame = 0;

            // Skip to safeStart by reading and discarding
            {
                long toSkip = safeStart * channels;
                while (toSkip > 0)
                {
                    int chunk = (int)Math.Min(toSkip, skipBuf.Length);
                    int got = samples.Read(skipBuf, 0, chunk);
                    if (got <= 0) break;
                    toSkip -= got;
                }
                currentFrame = safeStart;
            }

            for (int seg = 0; seg < segmentCount; seg++)
            {
                long framePos = safeStart + seg * stepFrames;

                // Skip forward to the target position
                long framesToSkip = framePos - currentFrame;
                if (framesToSkip > 0)
                {
                    long samplesToSkip = framesToSkip * channels;
                    while (samplesToSkip > 0)
                    {
                        int chunk = (int)Math.Min(samplesToSkip, skipBuf.Length);
                        int got = samples.Read(skipBuf, 0, chunk);
                        if (got <= 0) break;
                        samplesToSkip -= got;
                    }
                    currentFrame = framePos;
                }

                int read = samples.Read(readBuf, 0, readBuf.Length);
                currentFrame += FftSize;
                if (read < readBuf.Length) continue; // skip incomplete

                // Down-mix to mono + clipping detection
                double[] real = new double[FftSize];
                double[] imag = new double[FftSize];

                for (int i = 0; i < FftSize; i++)
                {
                    float sum = 0;
                    for (int ch = 0; ch < channels; ch++)
                    {
                        float s = readBuf[i * channels + ch];
                        sum += s;
                        if (Math.Abs(s) >= ClippingThreshold) clippingSamples++;
                        totalSamplesRead++;
                    }
                    real[i] = (sum / channels) * window[i];
                }

                FFT(real, imag);

                for (int i = 0; i < spectrumSize; i++)
                {
                    double mag = Math.Sqrt(real[i] * real[i] + imag[i] * imag[i]);
                    avgSpectrum[i] += mag;
                }
                validSegments++;
            }

            if (validSegments == 0)
            {
                info.Status = AudioStatus.Unknown;
                info.ErrorMessage = "No valid audio segments";
                return;
            }

            for (int i = 0; i < spectrumSize; i++)
                avgSpectrum[i] /= validSegments;

            // Clipping
            if (totalSamplesRead > 0)
            {
                info.ClippingSamples = clippingSamples;
                info.ClippingPercentage = (double)clippingSamples / totalSamplesRead * 100.0;
                info.HasClipping = info.ClippingPercentage > 0.01;
            }

            // ── Find the cutoff frequency ──
            info.EffectiveFrequency = FindCutoffFrequency(avgSpectrum, sampleRate);

            // ── Map cutoff → estimated bitrate ──
            bool isLossless = IsLosslessExtension(info.Extension);
            int estimated = EstimateBitrateFromCutoff(info.EffectiveFrequency, sampleRate, isLossless);

            // Cap actual at reported so it never exceeds what the container says
            if (info.ReportedBitrate > 0)
                info.ActualBitrate = Math.Min(estimated, info.ReportedBitrate);
            else
                info.ActualBitrate = estimated;
        }

        // ═══════════════════════════════════════════════════════
        //  Cutoff Detection — Band-Energy-Drop method
        //
        //  Instead of guessing a noise floor, we:
        //  1. Convert spectrum to dB
        //  2. Smooth it heavily to remove per-bin noise
        //  3. Compute the running average energy in overlapping bands
        //  4. Find the frequency where energy drops steeply compared
        //     to the band below it (the "shelf" left by lossy encoders)
        //
        //  This matches what you'd visually see in a spectrogram:
        //  a clear line where content abruptly stops.
        // ═══════════════════════════════════════════════════════

        private static int FindCutoffFrequency(double[] spectrum, int sampleRate)
        {
            int specLen = spectrum.Length;
            double binHz = (double)sampleRate / (2 * specLen);

            // Step 1: Convert to dB (relative to peak)
            double peak = 0;
            for (int i = 5; i < specLen; i++)
                if (spectrum[i] > peak) peak = spectrum[i];
            if (peak < 1e-12) return 0;

            double[] dB = new double[specLen];
            for (int i = 0; i < specLen; i++)
                dB[i] = spectrum[i] > 1e-12 ? 20.0 * Math.Log10(spectrum[i] / peak) : -120.0;

            // Step 2: Heavy smoothing — 1 kHz wide moving average
            //         This removes fine detail but preserves the macro shape
            int smoothRadius = Math.Max(4, (int)(500.0 / binHz)); // ±500 Hz
            double[] smooth = new double[specLen];
            double runSum = 0;
            int runCount = 0;

            // Seed the running window
            for (int i = 0; i < Math.Min(smoothRadius, specLen); i++)
            {
                runSum += dB[i];
                runCount++;
            }

            for (int i = 0; i < specLen; i++)
            {
                // Expand right edge
                int addIdx = i + smoothRadius;
                if (addIdx < specLen) { runSum += dB[addIdx]; runCount++; }
                // Shrink left edge
                int remIdx = i - smoothRadius - 1;
                if (remIdx >= 0) { runSum -= dB[remIdx]; runCount--; }

                smooth[i] = runCount > 0 ? runSum / runCount : -120.0;
            }

            // Step 3: Compute energy in wider bands (~2 kHz each) and look for the drop
            int bandBins = Math.Max(8, (int)(2000.0 / binHz));
            int halfBand = bandBins / 2;

            // We look for the biggest drop between adjacent bands.
            // Start from ~5 kHz (below this, lossy codecs don't cut off)
            int startBin = Math.Max(10, (int)(5000.0 / binHz));
            // Stop before we get too close to Nyquist
            int stopBin = specLen - bandBins - 1;

            double bestDropDb = 0;
            int bestDropBin = 0;

            for (int i = startBin; i < stopBin; i += halfBand)
            {
                // Average energy of band centered at i
                double bandBelow = 0;
                int cntBelow = 0;
                for (int j = Math.Max(0, i - halfBand); j < i; j++)
                {
                    bandBelow += smooth[j];
                    cntBelow++;
                }
                if (cntBelow > 0) bandBelow /= cntBelow;

                double bandAbove = 0;
                int cntAbove = 0;
                for (int j = i; j < Math.Min(specLen, i + halfBand); j++)
                {
                    bandAbove += smooth[j];
                    cntAbove++;
                }
                if (cntAbove > 0) bandAbove /= cntAbove;

                double drop = bandBelow - bandAbove; // positive = energy dropped

                // Only count if the band below has meaningful content (not already in the noise)
                if (bandBelow > -60 && drop > bestDropDb)
                {
                    bestDropDb = drop;
                    bestDropBin = i;
                }
            }

            // If the biggest drop is < 8 dB, there's no clear cutoff —
            // content extends across the full spectrum (truly high quality)
            if (bestDropDb < 8.0)
            {
                // No sharp cutoff found → content goes to Nyquist
                return sampleRate / 2;
            }

            // Refine: walk backwards from the drop point to find exactly
            // where the smooth curve starts descending significantly
            // (the actual "knee" of the shelf)
            double refLevel = smooth[Math.Max(0, bestDropBin - halfBand)];
            int cutoffBin = bestDropBin;
            for (int i = bestDropBin; i >= startBin; i--)
            {
                if (smooth[i] >= refLevel - 3.0) // within 3 dB of reference
                {
                    cutoffBin = i;
                    break;
                }
            }

            int freq = (int)(cutoffBin * binHz);
            return Math.Min(freq, sampleRate / 2);
        }

        // ═══════════════════════════════════════════════════════
        //  Bitrate Estimation from Cutoff Frequency
        //
        //  Based on actual LAME MP3 / AAC / Vorbis encoder lowpass:
        //    320 kbps  →  20+ kHz (no audible cutoff)
        //    256 kbps  →  ~19.5 kHz
        //    192 kbps  →  ~18.5 kHz  (LAME: 19.5 kHz lowpass)
        //    160 kbps  →  ~17.5 kHz  (LAME: 17.5 kHz lowpass)
        //    128 kbps  →  ~16 kHz    (LAME: 16 kHz lowpass)
        //     96 kbps  →  ~15 kHz    (LAME: 15 kHz lowpass)
        //     64 kbps  →  ~11 kHz
        //     32 kbps  →  ~8 kHz
        // ═══════════════════════════════════════════════════════

        private static int EstimateBitrateFromCutoff(int cutoffHz, int sampleRate, bool isLossless)
        {
            int nyquist = sampleRate / 2;

            if (isLossless)
            {
                // If content extends to near Nyquist, it's real lossless
                if (cutoffHz >= (int)(nyquist * 0.90))
                    return 1411;
                // Otherwise fall through — it's an upconvert from lossy
            }

            // Map cutoff frequency to the bitrate whose encoder would produce that cutoff
            if (cutoffHz >= 19500) return 320;
            if (cutoffHz >= 18500) return 256;
            if (cutoffHz >= 17500) return 192;
            if (cutoffHz >= 16500) return 160;
            if (cutoffHz >= 15500) return 128;
            if (cutoffHz >= 14500) return 96;
            if (cutoffHz >= 12000) return 80;
            if (cutoffHz >= 10000) return 64;
            if (cutoffHz >= 7500)  return 48;
            if (cutoffHz >= 5000)  return 32;

            return 24;
        }

        // ═══════════════════════════════════════════════════════
        //  Quality Determination
        // ═══════════════════════════════════════════════════════

        private static void DetermineQuality(AudioFileInfo info)
        {
            int reported = info.ReportedBitrate;
            int actual = info.ActualBitrate;
            int cutoff = info.EffectiveFrequency;
            int nyquist = info.SampleRate / 2;
            bool isLossless = IsLosslessExtension(info.Extension);

            // ── Lossless ──
            if (isLossless)
            {
                if (cutoff >= (int)(nyquist * 0.90))
                {
                    // Spectrum extends to Nyquist — but check bitrate ratio as secondary factor
                    if (reported > 0 && actual > 0)
                    {
                        double brRatio = (double)actual / reported;
                        if (brRatio < 0.25)
                        {
                            // Huge gap: e.g. reported 1200kbps but actual ~256kbps → fake
                            info.Status = AudioStatus.Fake;
                            return;
                        }
                        if (brRatio < 0.45)
                        {
                            info.Status = AudioStatus.Unknown;
                            return;
                        }
                    }
                    info.Status = AudioStatus.Valid;
                    return;
                }
                // Spectral content stops well short of Nyquist → upconvert
                // Bitrate thresholds slightly raised to give bitrate more influence
                if (actual <= 180)
                    info.Status = AudioStatus.Fake;
                else if (actual <= 288)
                    info.Status = AudioStatus.Unknown;
                else
                    info.Status = AudioStatus.Valid;
                return;
            }

            // ── Lossy ──
            if (reported <= 0 || actual <= 0)
            {
                info.Status = AudioStatus.Unknown;
                return;
            }

            // What cutoff frequency would we EXPECT for the reported bitrate?
            int expectedCutoff = ExpectedCutoffForBitrate(reported);

            // Compare actual cutoff against expected cutoff for the claimed bitrate
            if (expectedCutoff > 0 && cutoff > 0)
            {
                double freqRatio = (double)cutoff / expectedCutoff;

                if (freqRatio >= 0.90)
                {
                    // Cutoff is near or above what we'd expect — genuine
                    info.Status = AudioStatus.Valid;
                }
                else if (freqRatio >= 0.75)
                {
                    // Slightly low but within tolerance (VBR, unusual encoder settings)
                    info.Status = AudioStatus.Unknown;
                }
                else
                {
                    // Way below expected — this was transcoded from a lower quality source
                    info.Status = AudioStatus.Fake;
                }
            }
            else
            {
                // Fallback ratio-based check (bitrate influence)
                double ratio = (double)actual / reported;
                if (ratio >= 0.78) info.Status = AudioStatus.Valid;
                else if (ratio >= 0.50) info.Status = AudioStatus.Unknown;
                else info.Status = AudioStatus.Fake;
            }
        }

        /// <summary>
        /// Returns the typical lowpass cutoff frequency a legitimate encoder would use
        /// for the given bitrate. This is what we compare the detected cutoff against.
        /// </summary>
        private static int ExpectedCutoffForBitrate(int bitrateKbps)
        {
            // Based on LAME defaults (most common MP3 encoder) and typical AAC behavior
            if (bitrateKbps >= 320) return 20500;
            if (bitrateKbps >= 256) return 19500;
            if (bitrateKbps >= 224) return 19000;
            if (bitrateKbps >= 192) return 18500;
            if (bitrateKbps >= 160) return 17500;
            if (bitrateKbps >= 128) return 16000;
            if (bitrateKbps >= 112) return 15500;
            if (bitrateKbps >= 96)  return 15000;
            if (bitrateKbps >= 80)  return 13000;
            if (bitrateKbps >= 64)  return 11000;
            if (bitrateKbps >= 48)  return 9000;
            if (bitrateKbps >= 32)  return 7000;
            return 5000;
        }

        // ═══════════════════════════════════════════════════════
        //  Optimizer Detection (Platinum Notes, etc.)
        // ═══════════════════════════════════════════════════════

        private static bool DetectOptimizer(AudioFileInfo info)
        {
            if (info.EffectiveFrequency == 0) return false;
            if (IsLosslessExtension(info.Extension)) return false;

            try
            {
                var (disposable, samples, waveFormat) = OpenAudioFile(info.FilePath);
                using var _d = disposable;
                int sampleRate = waveFormat.SampleRate;
                int channels = waveFormat.Channels;

                long totalFrames;
                if (disposable is AudioFileReader afr2)
                    totalFrames = afr2.Length / afr2.WaveFormat.BlockAlign;
                else if (disposable is MediaFoundationReader mfr3)
                    totalFrames = mfr3.Length / mfr3.WaveFormat.BlockAlign;
                else
                    totalFrames = (long)(info.DurationSeconds * sampleRate);

                int samplesNeeded = FftSize * 4;
                if (totalFrames < samplesNeeded) return false;

                // Skip to middle by sequential reading
                long midStart = (totalFrames - samplesNeeded) / 2;
                long toSkip = midStart * channels;
                float[] skipBuf2 = new float[4096 * channels];
                while (toSkip > 0)
                {
                    int chunk = (int)Math.Min(toSkip, skipBuf2.Length);
                    int got = samples.Read(skipBuf2, 0, chunk);
                    if (got <= 0) break;
                    toSkip -= got;
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

        // ═══════════════════════════════════════════════════════
        //  FFT (Cooley-Tukey radix-2)
        // ═══════════════════════════════════════════════════════

        private static void FFT(double[] real, double[] imag)
        {
            int n = real.Length;
            if (n == 0) return;
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

        // ═══════════════════════════════════════════════════════
        //  Helpers
        // ═══════════════════════════════════════════════════════

        private static bool IsLosslessExtension(string ext)
            => ext is ".flac" or ".wav" or ".aiff" or ".aif"
                   or ".ape" or ".wv" or ".alac" or ".dsf" or ".dff";

        /// <summary>
        /// Detects BPM using onset detection + autocorrelation.
        /// Analyzes up to 30 seconds of audio.
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
                    var mfr = new MediaFoundationReader(filePath);
                    var sc = new SampleChannel(mfr, false);
                    sampleReader = sc;
                    readerDisposable = mfr;
                    sampleRate = sc.WaveFormat.SampleRate;
                    channels = sc.WaveFormat.Channels;
                }

                // Read up to 30 seconds of interleaved samples
                int monoSamples = sampleRate * 30;
                int rawToRead = monoSamples * channels;
                float[] rawBuf = new float[rawToRead];
                int rawRead = sampleReader.Read(rawBuf, 0, rawToRead);
                readerDisposable.Dispose();
                readerDisposable = null;

                if (rawRead < sampleRate * channels * 2) return 0; // need >= 2s

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

                // Compute energy in ~23ms frames (~43 Hz frame rate)
                int frameSize = sampleRate * 23 / 1000;
                int hopSize = frameSize / 2;
                int numFrames = (monoCount - frameSize) / hopSize;
                if (numFrames < 100) return 0;

                double[] energy = new double[numFrames];
                for (int i = 0; i < numFrames; i++)
                {
                    int start = i * hopSize;
                    double e = 0;
                    for (int j = 0; j < frameSize && start + j < monoCount; j++)
                    {
                        float s = mono[start + j];
                        e += s * s;
                    }
                    energy[i] = e / frameSize;
                }

                // Onset strength: positive first difference
                double[] onset = new double[numFrames - 1];
                for (int i = 0; i < onset.Length; i++)
                    onset[i] = Math.Max(0, energy[i + 1] - energy[i]);

                // Normalize onset
                double maxOnset = 0;
                for (int i = 0; i < onset.Length; i++)
                    if (onset[i] > maxOnset) maxOnset = onset[i];
                if (maxOnset > 0)
                    for (int i = 0; i < onset.Length; i++)
                        onset[i] /= maxOnset;

                // Autocorrelation for BPM range 60-200
                double hopDuration = (double)hopSize / sampleRate;
                int minLag = Math.Max(1, (int)(60.0 / 200 / hopDuration));
                int maxLag = (int)(60.0 / 60 / hopDuration);
                maxLag = Math.Min(maxLag, onset.Length / 2);

                if (minLag >= maxLag) return 0;

                double maxCorr = 0;
                int bestLag = minLag;

                for (int lag = minLag; lag <= maxLag; lag++)
                {
                    double sum = 0;
                    int count = onset.Length - lag;
                    for (int i = 0; i < count; i++)
                        sum += onset[i] * onset[i + lag];
                    double corr = count > 0 ? sum / count : 0;

                    if (corr > maxCorr)
                    {
                        maxCorr = corr;
                        bestLag = lag;
                    }
                }

                if (maxCorr < 1e-10) return 0;

                double bpm = 60.0 / (bestLag * hopDuration);
                int bpmInt = (int)Math.Round(bpm);

                // If detected BPM seems like half-time, double it
                if (bpmInt >= 60 && bpmInt <= 85)
                {
                    // Check if double is also a peak
                    int doubleLag = bestLag / 2;
                    if (doubleLag >= minLag)
                    {
                        double doubleSum = 0;
                        int cnt = onset.Length - doubleLag;
                        for (int i = 0; i < cnt; i++)
                            doubleSum += onset[i] * onset[i + doubleLag];
                        double doubleCorr = cnt > 0 ? doubleSum / cnt : 0;
                        if (doubleCorr > maxCorr * 0.8)
                            bpmInt = (int)Math.Round(60.0 / (doubleLag * hopDuration));
                    }
                }

                return (bpmInt >= 60 && bpmInt <= 200) ? bpmInt : 0;
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
        private static void ExtractReplayGain(TagLib.File tagFile, AudioFileInfo info)
        {
            try
            {
                // Try ID3v2 TXXX frames
                if (tagFile.GetTag(TagLib.TagTypes.Id3v2) is TagLib.Id3v2.Tag id3)
                {
                    foreach (var frame in id3.GetFrames<TagLib.Id3v2.UserTextInformationFrame>())
                    {
                        if (frame.Description != null &&
                            frame.Description.Contains("REPLAYGAIN_TRACK_GAIN", StringComparison.OrdinalIgnoreCase))
                        {
                            if (TryParseReplayGain(frame.Text?.Length > 0 ? frame.Text[0] : null, out double gain))
                            {
                                info.ReplayGain = gain;
                                info.HasReplayGain = true;
                                return;
                            }
                        }
                    }
                }

                // Try Xiph Comment (FLAC, OGG, OPUS)
                if (tagFile.GetTag(TagLib.TagTypes.Xiph) is TagLib.Ogg.XiphComment xiph)
                {
                    var fields = xiph.GetField("REPLAYGAIN_TRACK_GAIN");
                    if (fields != null && fields.Length > 0)
                    {
                        if (TryParseReplayGain(fields[0], out double gain))
                        {
                            info.ReplayGain = gain;
                            info.HasReplayGain = true;
                            return;
                        }
                    }
                }

                // Try APE tag
                if (tagFile.GetTag(TagLib.TagTypes.Ape) is TagLib.Ape.Tag ape)
                {
                    var item = ape.GetItem("REPLAYGAIN_TRACK_GAIN");
                    if (item != null)
                    {
                        if (TryParseReplayGain(item.ToString(), out double gain))
                        {
                            info.ReplayGain = gain;
                            info.HasReplayGain = true;
                            return;
                        }
                    }
                }
            }
            catch { }
        }

        private static bool TryParseReplayGain(string? value, out double gain)
        {
            gain = 0;
            if (string.IsNullOrWhiteSpace(value)) return false;
            // Strip " dB" suffix
            value = value.Trim().Replace(" dB", "", StringComparison.OrdinalIgnoreCase)
                                .Replace(" db", "", StringComparison.OrdinalIgnoreCase);
            return double.TryParse(value, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out gain);
        }

        /// <summary>
        /// Opens an audio file as a sample provider (float samples).
        /// Tries AudioFileReader first (best quality), falls back to MediaFoundationReader
        /// for formats NAudio can't natively decode (OGG, OPUS, AAC/M4A, APE, etc.).
        /// Returns the reader (to be disposed by caller) and the ISampleProvider.
        /// </summary>
        internal static (IDisposable reader, ISampleProvider samples, WaveFormat format) OpenAudioFile(string filePath)
        {
            string ext = Path.GetExtension(filePath).ToLowerInvariant();

            // ── Opus files: use OpusFileReader (Concentus) ──
            if (ext is ".opus")
            {
                try
                {
                    var opus = new OpusFileReader(filePath);
                    ISampleProvider opusSample = opus.WaveFormat.Encoding == WaveFormatEncoding.IeeeFloat
                        ? (ISampleProvider)new WaveToSampleProvider(opus)
                        : new Pcm16BitToSampleProvider(opus);
                    return (opus, opusSample, opus.WaveFormat);
                }
                catch { /* fall through */ }
            }

            // ── OGG Vorbis files ──
            if (ext is ".ogg")
            {
                try
                {
                    var vorbis = new VorbisWaveReader(filePath);
                    return (vorbis, vorbis, vorbis.WaveFormat);
                }
                catch { /* fall through */ }
            }

            // ── DSD files ──
            if (ext is ".dsf" or ".dff" or ".dsd")
            {
                try
                {
                    var dsd = new DsdToPcmReader(filePath);
                    ISampleProvider dsdSample = dsd.WaveFormat.Encoding == WaveFormatEncoding.IeeeFloat
                        ? (ISampleProvider)new WaveToSampleProvider(dsd)
                        : new Pcm16BitToSampleProvider(dsd);
                    return (dsd, dsdSample, dsd.WaveFormat);
                }
                catch { /* fall through */ }
            }

            // AudioFileReader handles: MP3, WAV, AIFF, WMA, FLAC (via MediaFoundation on Win10+)
            try
            {
                var afr = new AudioFileReader(filePath);
                return (afr, afr, afr.WaveFormat);
            }
            catch { /* fall through to MediaFoundation */ }

            // MediaFoundationReader with float output
            try
            {
                var mfr = new MediaFoundationReader(filePath);
                if (mfr.WaveFormat.Encoding == WaveFormatEncoding.IeeeFloat)
                {
                    var sp = (ISampleProvider)new WaveToSampleProvider(mfr);
                    return (mfr, sp, mfr.WaveFormat);
                }

                // Try SampleChannel which handles many PCM bit-depths internally
                try
                {
                    var sc = new SampleChannel(mfr, false);
                    return (mfr, sc, sc.WaveFormat);
                }
                catch { /* try explicit conversion */ }

                // Explicit conversion to 16-bit PCM
                try
                {
                    var conv = new WaveFormatConversionStream(
                        new WaveFormat(mfr.WaveFormat.SampleRate, 16, mfr.WaveFormat.Channels), mfr);
                    var sp16 = new Pcm16BitToSampleProvider(conv);
                    return (mfr, sp16, mfr.WaveFormat);
                }
                catch
                {
                    mfr.Dispose();
                    throw;
                }
            }
            catch { /* fall through */ }

            // MediaFoundationReader with forced PCM output (helps some FLAC/AAC files)
            try
            {
                var settings = new MediaFoundationReader.MediaFoundationReaderSettings
                {
                    RequestFloatOutput = false
                };
                var mfr2 = new MediaFoundationReader(filePath, settings);
                try
                {
                    var sc2 = new SampleChannel(mfr2, false);
                    return (mfr2, sc2, sc2.WaveFormat);
                }
                catch
                {
                    mfr2.Dispose();
                    throw;
                }
            }
            catch { /* all strategies exhausted */ }

            throw new InvalidOperationException($"Cannot open audio file: {Path.GetFileName(filePath)}");
        }

        private static string FormatFileSize(long bytes)
        {
            if (bytes < 1024) return $"{bytes} B";
            if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
            if (bytes < 1024 * 1024 * 1024) return $"{bytes / (1024.0 * 1024):F1} MB";
            return $"{bytes / (1024.0 * 1024 * 1024):F2} GB";
        }

        private static string FormatDuration(TimeSpan ts)
        {
            if (ts.TotalHours >= 1)
                return $"{(int)ts.TotalHours}:{ts.Minutes:D2}:{ts.Seconds:D2}";
            return $"{ts.Minutes}:{ts.Seconds:D2}";
        }
    }
}
