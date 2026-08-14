using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using AudioQualityChecker.Abstractions;
using AudioQualityChecker.Models;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using NAudio.Vorbis;
using TagLib;

namespace AudioQualityChecker.Services
{
    public static partial class AudioAnalyzer
    {        // ═══════════════════════════════════════════════════════
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

        /// <param name="wallDropDb">
        /// How steeply the spectrum falls across the returned cutoff, in dB. Left at 0 on every
        /// path that does not locate a discrete cutoff, so callers can treat a non-zero value as
        /// "a real transition was measured here" without re-checking which branch produced it.
        /// </param>
        internal static int FindCutoffFrequency(double[] spectrum, int sampleRate, out double wallDropDb)
        {
            wallDropDb = 0.0;

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

            // Step 3: Find where the spectrum drops to the noise floor and STAYS there.
            // A real codec cutoff means energy plummets to -60 dB or below and never recovers.
            // Natural audio rolloff is gradual and still has energy at -30 to -50 dB.
            //
            // Strategy: scan from high frequency downward looking for the highest bin
            // where the smoothed energy is still meaningfully above the noise floor.
            // This is more robust than looking for "drops" which can trigger on
            // natural spectral features.

            // Determine the noise floor: average of the top 5% of the spectrum
            // (above Nyquist * 0.95), which for most audio is pure noise.
            int noiseStart = (int)(specLen * 0.95);
            double noiseFloor = 0.0;
            int noiseCnt = 0;
            for (int i = noiseStart; i < specLen; i++)
            {
                noiseFloor += smooth[i];
                noiseCnt++;
            }
            if (noiseCnt > 0) noiseFloor /= noiseCnt;
            // Clamp: noise floor shouldn't be reported higher than -40 dB
            if (noiseFloor > -40.0) noiseFloor = -40.0;

            // ── Adaptive content threshold ──
            // A fixed threshold fails because spectral energy varies wildly by
            // genre: bright pop might have -25 dB at 14 kHz while dark ambient
            // has -45 dB there. Instead, derive the threshold from the file's own
            // core content band (2-8 kHz) where virtually all music has energy.
            int refStart = Math.Max(5, (int)(2000.0 / binHz));
            int refEnd   = Math.Min(specLen, (int)(8000.0 / binHz));
            // Collect smoothed values and take the median for robustness
            var refValues = new System.Collections.Generic.List<double>(refEnd - refStart);
            for (int i = refStart; i < refEnd; i++)
                refValues.Add(smooth[i]);
            refValues.Sort();
            double refMedian = refValues.Count > 0 ? refValues[refValues.Count / 2] : -40.0;

            // Content threshold = reference median minus 25 dB.
            // This means we look for energy within 25 dB of the core content,
            // adapting to each file's spectral shape automatically.
            double adaptiveThreshold = refMedian - 25.0;
            // But never above the noise floor + 10 dB (must stay above noise)
            double noiseThresholdLimit = noiseFloor + 10.0;
            double contentThreshold = Math.Max(adaptiveThreshold, noiseThresholdLimit);
            // Safety bounds
            if (contentThreshold > -25.0) contentThreshold = -25.0;
            if (contentThreshold < -70.0) contentThreshold = -70.0;

            // Scan from high frequencies downward to find where real content ends.
            // Use a sliding window to avoid single-bin noise spikes fooling us.
            int windowBins = Math.Max(4, (int)(800.0 / binHz)); // ~800 Hz window
            int startBin = Math.Max(10, (int)(4000.0 / binHz));

            int cutoffBin = specLen - 1; // default: full spectrum

            for (int i = specLen - windowBins - 1; i >= startBin; i--)
            {
                // Average energy in a window starting at bin i
                double windowEnergy = 0;
                for (int j = i; j < i + windowBins; j++)
                    windowEnergy += smooth[j];
                windowEnergy /= windowBins;

                if (windowEnergy > contentThreshold)
                {
                    // Found the highest frequency with real content
                    cutoffBin = i + windowBins; // top of the window
                    break;
                }
            }

            // If we never found content above the threshold starting from the top,
            // check if the whole spectrum is below threshold (very quiet/corrupt file)
            if (cutoffBin >= specLen - 1)
            {
                // Check if there's content in the lower bands
                double lowEnergy = 0;
                int lowCnt = 0;
                int lowEnd = Math.Min(specLen, (int)(8000.0 / binHz));
                for (int i = startBin; i < lowEnd; i++)
                {
                    lowEnergy += smooth[i];
                    lowCnt++;
                }
                if (lowCnt > 0) lowEnergy /= lowCnt;

                if (lowEnergy <= contentThreshold)
                {
                    // Even lower frequencies have no content — file is basically silent
                    return 0;
                }
                // Otherwise, content extends to Nyquist — unless a wall hides above the
                // level scan's reach (see FindSpectralWall).
                return FallBackToWall(dB, binHz, specLen, sampleRate, out wallDropDb);
            }

            // Additional validation: a codec cutoff should show a significant difference
            // between the energy below and above the detected cutoff point.
            // If the difference is small, it's just natural rolloff, not a hard cutoff.
            double energyBelow = 0;
            int cntBelow = 0;
            int checkRange = Math.Max(8, (int)(2000.0 / binHz));
            for (int i = Math.Max(startBin, cutoffBin - checkRange); i < cutoffBin; i++)
            {
                energyBelow += smooth[i];
                cntBelow++;
            }
            if (cntBelow > 0) energyBelow /= cntBelow;

            double energyAbove = 0;
            int cntAbove = 0;
            for (int i = cutoffBin; i < Math.Min(specLen, cutoffBin + checkRange); i++)
            {
                energyAbove += smooth[i];
                cntAbove++;
            }
            if (cntAbove > 0) energyAbove /= cntAbove;

            double dropAcrossCutoff = energyBelow - energyAbove;

            // Additional sharpness check: a real codec lowpass is a brick-wall filter
            // that drops steeply within a narrow band (~500 Hz). Natural rolloff is
            // gradual across octaves and won't show a sharp transition.
            int sharpRange = Math.Max(4, (int)(500.0 / binHz)); // ~500 Hz
            double sharpBelow = 0, sharpAbove = 0;
            int scBelow = 0, scAbove = 0;
            for (int i = Math.Max(startBin, cutoffBin - sharpRange); i < cutoffBin; i++)
            {
                sharpBelow += smooth[i]; scBelow++;
            }
            for (int i = cutoffBin; i < Math.Min(specLen, cutoffBin + sharpRange); i++)
            {
                sharpAbove += smooth[i]; scAbove++;
            }
            if (scBelow > 0) sharpBelow /= scBelow;
            if (scAbove > 0) sharpAbove /= scAbove;
            double sharpDrop = sharpBelow - sharpAbove;

            int freq = (int)(cutoffBin * binHz);

            // Validate that this is a real codec lowpass, not natural rolloff.
            // Codec lowpass has two hallmarks:
            //   1. Broad drop: >= 20 dB energy difference across 2 kHz boundary
            //   2. Sharp drop: >= 10 dB within just 500 Hz (brick-wall filter)
            //
            // For cutoffs in the normal lossy range (10+ kHz), EITHER hallmark is
            // sufficient — some encoders produce a steep but narrow transition,
            // others a softer but clearly visible shelf. We use relaxed thresholds
            // (15 dB broad, 8 dB sharp) and require at least one to pass.
            //
            // For very low detected cutoffs (< 10 kHz), we require BOTH strict
            // thresholds to prevent false detections that would report absurdly
            // low bitrates (the old "24 kbps" problem).
            if (freq < 10000)
            {
                // Very low cutoff — require strong evidence on BOTH checks
                if (dropAcrossCutoff < 20.0 || sharpDrop < 10.0)
                    return FallBackToWall(dB, binHz, specLen, sampleRate, out wallDropDb);
            }
            else
            {
                // Normal range — either check showing clear evidence is sufficient
                bool broadDropOk = dropAcrossCutoff >= 15.0;
                bool sharpDropOk = sharpDrop >= 8.0;
                if (!broadDropOk && !sharpDropOk)
                    return FallBackToWall(dB, binHz, specLen, sampleRate, out wallDropDb);
            }

            // Hand back the steeper of the two measurements. A codec lowpass scores high on at
            // least one of them; natural rolloff scores low on both. This is the only evidence
            // that separates a 20.5 kHz LAME 320 wall from a genuinely dull master, since the
            // two are indistinguishable by cutoff frequency alone.
            wallDropDb = Math.Max(dropAcrossCutoff, sharpDrop);

            return Math.Min(freq, sampleRate / 2);
        }

        // ═══════════════════════════════════════════════════════
        //  Spectral Wall Scan (fallback)
        //
        //  The level scan above picks its threshold from the file's own 2-8 kHz
        //  median, 25 dB down. On music whose top end legitimately sits 60-90 dB
        //  below the body, that threshold is crossed thousands of Hz below the
        //  real edge; the sharpness check then correctly rejects the bogus
        //  cutoff, and the whole function reports "full band".
        //
        //  That is how a 320 kbps MP3 decoded into a lossless container passed
        //  as genuine: its wall at ~20.5 kHz was never located at all, so there
        //  was nothing for the verdict to judge.
        //
        //  So when the level scan gives up, look for the wall directly — the
        //  steepest sustained drop, measured on the RAW dB curve. The ±500 Hz
        //  smoothing the level scan uses would blur the very edge being sought,
        //  which is why this reads dB[] rather than smooth[].
        // ═══════════════════════════════════════════════════════

        /// <summary>Guard band skipped either side of a candidate edge, so the transition
        /// itself is excluded from both comparison bands.</summary>
        private const double WallGuardHz = 200.0;

        /// <summary>Width of the comparison band taken above and below a candidate edge.</summary>
        private const double WallBandHz = 1300.0;

        /// <summary>
        /// Wraps <see cref="FindSpectralWall"/> for the give-up paths of
        /// <see cref="FindCutoffFrequency"/>: reports the wall if one is found, otherwise
        /// preserves the historical "content reaches Nyquist" answer.
        /// </summary>
        private static int FallBackToWall(double[] dB, double binHz, int specLen, int sampleRate, out double wallDropDb)
        {
            int wall = FindSpectralWall(dB, binHz, specLen, out wallDropDb);
            if (wall > 0) return Math.Min(wall, sampleRate / 2);

            wallDropDb = 0.0;
            return sampleRate / 2;
        }

        /// <summary>
        /// Finds the steepest sustained drop in the spectrum above 10 kHz. Returns the edge
        /// frequency in Hz, or 0 when nothing reaches <see cref="LossyWallDropDb"/> — below that
        /// the caller keeps its existing answer, so only files with a genuinely deep wall are
        /// affected by this scan at all.
        /// </summary>
        internal static int FindSpectralWall(double[] dB, double binHz, int specLen, out double dropDb)
        {
            dropDb = 0.0;

            int guard = Math.Max(2, (int)(WallGuardHz / binHz));
            int band = Math.Max(8, (int)(WallBandHz / binHz));
            int first = Math.Max(band + guard, (int)(10000.0 / binHz));
            int last = specLen - guard - band - 1;
            if (last <= first) return 0;

            int bestBin = 0;
            double bestDrop = 0.0;

            for (int c = first; c <= last; c++)
            {
                double below = 0;
                for (int i = c - guard - band; i < c - guard; i++) below += dB[i];
                below /= band;

                double above = 0;
                for (int i = c + guard; i < c + guard + band; i++) above += dB[i];
                above /= band;

                double drop = below - above;
                if (drop > bestDrop)
                {
                    bestDrop = drop;
                    bestBin = c;
                }
            }

            if (bestDrop < LossyWallDropDb) return 0;

            dropDb = bestDrop;
            return (int)(bestBin * binHz);
        }

        // ═══════════════════════════════════════════════════════
        //  Full-Band Gate
        //
        //  A lossless container proves nothing on its own — anyone can decode
        //  an MP3 into a WAV/AIFF/FLAC. The question is whether the audio
        //  inside ever had full bandwidth.
        //
        //  Cutoff frequency alone cannot answer that. LAME at 320 kbps CBR
        //  lowpasses at ~20.5 kHz, which is close enough to 22.05 kHz that any
        //  tolerance loose enough to spare genuinely dull masters also spares
        //  the transcode. What separates them is the SHAPE of the ending:
        //  a mastering rolloff decays gradually, an encoder lowpass is a
        //  brick wall dropping tens of dB inside a few hundred Hz.
        // ═══════════════════════════════════════════════════════

        /// <summary>
        /// Minimum drop across the cutoff (dB) before a sub-Nyquist cutoff is read as an encoder
        /// lowpass rather than natural rolloff. Calibrated against measured transcodes — see the
        /// wall-depth tests. Raising it makes the scanner more forgiving, never less.
        /// </summary>
        private const double LossyWallDropDb = 25.0;

        /// <summary>How close to a sample rate's Nyquist a wall may sit before it counts as "at" it.</summary>
        private const int WallMarginHz = 700;

        /// <summary>
        /// True when a lossless-container file's spectrum is consistent with a genuinely
        /// full-bandwidth source.
        ///
        /// Below <see cref="LossyWallDropDb"/> this is byte-for-byte the frequency-only test the
        /// scanner has always used, so no file that passes today can start failing unless a hard
        /// wall is actually measured.
        /// </summary>
        internal static bool IsFullBandLossless(int cutoffHz, double wallDropDb, int sampleRate)
        {
            int nyquist = sampleRate / 2;

            // Content at 20 kHz+ can't be from a sub-320 kbps transcode. Using both a percentage
            // and an absolute floor prevents hi-res files (96/192 kHz) from false-flagging for
            // merely having no ultrasonic content.
            bool lenientPass = cutoffHz >= (int)(nyquist * 0.90) || cutoffHz >= 20000;

            // Gradual ending — no wall was measured, so frequency is all we have to go on.
            if (wallDropDb < LossyWallDropDb) return lenientPass;

            // A wall sitting at the file's own Nyquist is where the spectrum has to end anyway.
            if (cutoffHz >= nyquist - WallMarginHz) return true;

            // A wall at 22.05/24 kHz inside a hi-res file is the fingerprint of an upsample from
            // CD or 48 kHz, not of a lossy source. That is a different (and milder) problem, so
            // it is not called fake lossless here.
            if (IsStandardSourceNyquist(cutoffHz)) return true;

            // Brick wall well below Nyquist: an encoder lowpass.
            return false;
        }

        private static bool IsStandardSourceNyquist(int cutoffHz)
            => Math.Abs(cutoffHz - 22050) <= WallMarginHz
            || Math.Abs(cutoffHz - 24000) <= WallMarginHz;

        // ═══════════════════════════════════════════════════════
        //  Codec Family
        //
        //  The cutoff-to-bitrate curves describe ENCODERS, but the file extension
        //  names a CONTAINER. Keying the curves off the extension meant a .m4b or
        //  .m4r — plain AAC, same as .m4a — missed the AAC curve and was judged
        //  against LAME's, so identical audio got different verdicts depending only
        //  on what the file was called.
        // ═══════════════════════════════════════════════════════

        /// <summary>Maps a file to the encoder family whose lowpass behaviour it actually follows.</summary>
        private static string CodecFamily(AudioFileInfo info)
        {
            if (info.IsAlac) return "alac";

            return info.Extension switch
            {
                ".m4a" or ".m4b" or ".m4r" or ".mp4" or ".aac" or ".3gp" or ".3g2" => "aac",
                ".opus" or ".webm" => "opus",   // WebM audio is Opus in practice
                ".ogg" or ".oga"                                                   => "ogg",
                ".mp2"                                                             => "mp2",
                _ => info.Extension.TrimStart('.')
            };
        }

        /// <summary>
        /// Whether we have a cutoff curve actually calibrated for this encoder.
        ///
        /// Everything else falls through to the LAME/MP3 curve, which is a reasonable default but
        /// not evidence. MPEG Layer II, AC-3, AMR and Speex all roll off far lower than MP3 at the
        /// same bitrate, so measuring them against LAME's numbers accuses correctly-encoded files —
        /// a legitimate 192 kbps MP2 was being reported as Fake. Uncalibrated codecs are therefore
        /// capped at Unknown: we can say we are unsure, but not that the file is wrong.
        /// </summary>
        private static bool HasCalibratedCutoffCurve(string codec)
            => codec is "mp3" or "wma" or "aac" or "opus" or "ogg" or "alac"
                     or "flac" or "wav" or "aiff" or "aif" or "bwf";

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

        /// <param name="fullBandLossless">
        /// Result of <see cref="IsFullBandLossless"/> for this file. The caller owns that decision —
        /// this method must not second-guess it, or a measured brick wall gets overridden here and
        /// the file reports 1411 kbps again.
        /// </param>
        private static int EstimateBitrateFromCutoff(int cutoffHz, int sampleRate, bool fullBandLossless, string codec)
        {
            if (fullBandLossless)
                return 1411;

            // Codec-aware cutoff-to-bitrate mapping.
            // Different encoders apply different lowpass filters at each bitrate.

            // AAC encoders (FDK-AAC, Apple AAC) generally preserve higher frequencies
            // at lower bitrates compared to MP3/LAME.
            // ALAC is lossless — reaching here means the full-band gate already rejected it,
            // so every rung below describes the lossy source it was upconverted from.
            if (codec is "alac")
            {
                if (cutoffHz >= 20000) return 320;
                if (cutoffHz >= 19500) return 256;
                if (cutoffHz >= 18500) return 192;
                if (cutoffHz >= 17500) return 160;
                if (cutoffHz >= 16000) return 128;
                if (cutoffHz >= 15000) return 96;
                if (cutoffHz >= 11000) return 64;
                if (cutoffHz >= 8000)  return 48;
                return 32;
            }

            if (codec is "m4a" or "aac" or "mp4")
            {
                if (cutoffHz >= 20000) return 320;
                if (cutoffHz >= 19500) return 256;
                if (cutoffHz >= 18500) return 192;
                if (cutoffHz >= 17500) return 160;
                if (cutoffHz >= 16500) return 128;
                if (cutoffHz >= 15000) return 96;
                if (cutoffHz >= 13000) return 80;
                if (cutoffHz >= 11000) return 64;
                if (cutoffHz >= 8000)  return 48;
                if (cutoffHz >= 5500)  return 32;
                return 24;
            }

            // Opus uses a psychoacoustic bandwidth model — tends to have content
            // at higher frequencies even at low bitrates due to bandwidth extension.
            if (codec is "opus")
            {
                if (cutoffHz >= 20000) return 256;
                if (cutoffHz >= 19000) return 192;
                if (cutoffHz >= 18000) return 160;
                if (cutoffHz >= 16500) return 128;
                if (cutoffHz >= 15000) return 96;
                if (cutoffHz >= 12000) return 64;
                if (cutoffHz >= 8000)  return 48;
                if (cutoffHz >= 5000)  return 32;
                return 24;
            }

            // Vorbis has smooth rolloff rather than a hard lowpass.
            if (codec is "ogg")
            {
                if (cutoffHz >= 20000) return 320;
                if (cutoffHz >= 19000) return 256;
                if (cutoffHz >= 18000) return 192;
                if (cutoffHz >= 17000) return 160;
                if (cutoffHz >= 15500) return 128;
                if (cutoffHz >= 14000) return 96;
                if (cutoffHz >= 11000) return 64;
                if (cutoffHz >= 8000)  return 48;
                if (cutoffHz >= 5000)  return 32;
                return 24;
            }

            // MP3 (LAME defaults) — the most common case.
            // WMA uses similar lowpass behavior to MP3.
            // This is also the fallback for unknown codecs.
            if (cutoffHz >= 20000) return 320;
            if (cutoffHz >= 19000) return 256;
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

        private static void DetermineQuality(AudioFileInfo info, IAnalysisSettings settings)
        {
            int reported = info.ReportedBitrate;
            int actual = info.ActualBitrate;
            int cutoff = info.EffectiveFrequency;
            bool isLossless = IsLosslessFile(info);

            // ── Frequency cutoff allow-listing ──
            // If enabled and the measured cutoff meets the threshold, treat the file as
            // full-bandwidth — skip the upconvert/transcode check entirely.
            if (settings.FrequencyCutoffAllowEnabled && cutoff > 0 && cutoff >= settings.FrequencyCutoffAllowHz)
            {
                info.Status = AudioStatus.Valid;
                return;
            }

            // ── Lossless ──
            if (isLossless)
            {
                // AnalysisSampleRate, not SampleRate: the cutoff was measured against the decoder's
                // output rate. For DSD the container declares 2.8 MHz while the spectrum came from
                // a ~176 kHz PCM decode, so judging it against the declared Nyquist is nonsense.
                int measuredRate = info.AnalysisSampleRate > 0 ? info.AnalysisSampleRate : info.SampleRate;
                if (IsFullBandLossless(cutoff, info.CutoffDropDb, measuredRate))
                {
                    info.Status = AudioStatus.Valid;
                    return;
                }

                // A brick wall was measured below Nyquist: an encoder lowpass survived into a
                // lossless container. That is the evidence, so the estimated source bitrate is
                // descriptive here rather than a reason to acquit — a 20.5 kHz wall estimates
                // 320 kbps, and a 320 kbps MP3 decoded to AIFF is still not lossless.
                if (info.CutoffDropDb >= LossyWallDropDb)
                {
                    info.Status = AudioStatus.Fake;
                    if (info.EstimatedSourceBitrate <= 0) info.EstimatedSourceBitrate = actual;
                    return;
                }

                // No wall — content merely stops short of the expected range. Judge severity by
                // the estimated original bitrate: flag clearly low-quality sources (≤192 kbps),
                // and leave moderate-high ones Unknown, since natural rolloff varies by genre
                // and can mimic a 224-256 kbps lowpass.
                if (actual <= 192)
                    info.Status = AudioStatus.Fake;
                else if (actual <= 256)
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
            string codec = CodecFamily(info);
            int expectedCutoff = ExpectedCutoffForBitrate(reported, codec);

            // Compare actual cutoff against expected cutoff for the claimed bitrate
            if (expectedCutoff > 0 && cutoff > 0)
            {
                double freqRatio = (double)cutoff / expectedCutoff;

                if (freqRatio >= 0.85)
                {
                    // Cutoff is near or above what we'd expect — genuine
                    info.Status = AudioStatus.Valid;
                }
                else if (freqRatio >= 0.70)
                {
                    // Moderately low — could be VBR, different encoder, or mild transcode
                    info.Status = AudioStatus.Unknown;
                }
                else if (!HasCalibratedCutoffCurve(codec))
                {
                    // Below the LAME curve, but that curve was never meant to describe this
                    // encoder. Not enough to accuse the file — say unsure instead.
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
        /// Codec-aware: different encoders have different lowpass behaviors.
        /// </summary>
        private static int ExpectedCutoffForBitrate(int bitrateKbps, string codec = "mp3")
        {
            // AAC encoders preserve more high frequencies at lower bitrates
            if (codec is "m4a" or "aac" or "mp4")
            {
                if (bitrateKbps >= 320) return 20500;
                if (bitrateKbps >= 256) return 20000;
                if (bitrateKbps >= 192) return 19500;
                if (bitrateKbps >= 160) return 18500;
                if (bitrateKbps >= 128) return 17000;
                if (bitrateKbps >= 96)  return 15500;
                if (bitrateKbps >= 64)  return 13000;
                if (bitrateKbps >= 48)  return 10000;
                if (bitrateKbps >= 32)  return 8000;
                return 5500;
            }

            // Opus uses bandwidth extension and psychoacoustic models
            if (codec is "opus")
            {
                if (bitrateKbps >= 192) return 20500;
                if (bitrateKbps >= 128) return 20000;
                if (bitrateKbps >= 96)  return 19000;
                if (bitrateKbps >= 64)  return 16500;
                if (bitrateKbps >= 48)  return 14000;
                if (bitrateKbps >= 32)  return 10000;
                return 6000;
            }

            // Vorbis
            if (codec is "ogg")
            {
                if (bitrateKbps >= 320) return 20500;
                if (bitrateKbps >= 256) return 20000;
                if (bitrateKbps >= 192) return 19000;
                if (bitrateKbps >= 160) return 18000;
                if (bitrateKbps >= 128) return 16500;
                if (bitrateKbps >= 96)  return 15000;
                if (bitrateKbps >= 64)  return 12000;
                if (bitrateKbps >= 48)  return 9000;
                if (bitrateKbps >= 32)  return 7000;
                return 5000;
            }

            // MP3 (LAME defaults) — most common encoder, also used as fallback
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

    }
}
