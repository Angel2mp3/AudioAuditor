using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AudioQualityChecker.Abstractions;
using AudioQualityChecker.Models;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using NAudio.Vorbis;
using TagLib;

namespace AudioQualityChecker.Services
{
    public static partial class AudioAnalyzer
    {
        private const int FftSize = 4096;
        private const int AnalysisSegments = 100;
        private const float ClippingThreshold = 0.9999f;

        /// <summary>Set to true to enable BPM detection. Defaults to false for performance.</summary>
        public static bool EnableBpmDetection { get; set; } = false;
        public static bool EnableExperimentalAi { get; set; }
        public static bool EnableRipQuality { get; set; }
        public static bool EnableSilenceDetection { get; set; }
        public static bool EnableFakeStereoDetection { get; set; } = true;
        public static bool EnableDynamicRange { get; set; }
        public static bool EnableTruePeak { get; set; }
        public static bool EnableLufs { get; set; }
        public static bool EnableClippingDetection { get; set; } = true;
        public static bool EnableMqaDetection { get; set; } = true;
        public static bool EnableDefaultAiDetection { get; set; } = true;

        // Silence detection fine-tuning (defaults mirror ThemeManager initial values)
        public static bool SilenceMinGapEnabled { get; set; }
        public static double SilenceMinGapSeconds { get; set; } = 0.5;
        public static bool SilenceSkipEdgesEnabled { get; set; }
        public static double SilenceSkipEdgeSeconds { get; set; } = 5.0;

        // Forces the full-file audio pass even when all per-feature detectors are disabled
        public static bool AlwaysFullAnalysis { get; set; }

        /// <summary>When set, RunFullFilePass cooperatively pauses at safe checkpoints.</summary>
        public static System.Threading.ManualResetEventSlim? PauseEvent { get; set; }

        private static void WaitIfPaused(CancellationToken ct)
        {
            var pauseEvent = PauseEvent;
            while (pauseEvent != null && !pauseEvent.Wait(0))
            {
                Thread.Sleep(10);
                ct.ThrowIfCancellationRequested();
                pauseEvent = PauseEvent;
            }
            ct.ThrowIfCancellationRequested();
        }

        // Frequency cutoff allow-listing: skip quality downgrade for files above threshold
        public static bool FrequencyCutoffAllowEnabled { get; set; }
        public static int FrequencyCutoffAllowHz { get; set; } = 19600;

        public static IAnalysisSettings ActiveSettings { get; set; } = new StaticAnalysisSettings();

        public static void ApplySettings(IAnalysisSettings settings)
        {
            ArgumentNullException.ThrowIfNull(settings);
            var snapshot = AnalysisSettingsSnapshot.From(settings);

            EnableBpmDetection = snapshot.EnableBpmDetection;
            EnableExperimentalAi = snapshot.EnableExperimentalAi;
            EnableRipQuality = snapshot.EnableRipQuality;
            EnableSilenceDetection = snapshot.EnableSilenceDetection;
            EnableFakeStereoDetection = snapshot.EnableFakeStereoDetection;
            EnableDynamicRange = snapshot.EnableDynamicRange;
            EnableTruePeak = snapshot.EnableTruePeak;
            EnableLufs = snapshot.EnableLufs;
            EnableClippingDetection = snapshot.EnableClippingDetection;
            EnableMqaDetection = snapshot.EnableMqaDetection;
            EnableDefaultAiDetection = snapshot.EnableDefaultAiDetection;
            AlwaysFullAnalysis = snapshot.AlwaysFullAnalysis;
            FrequencyCutoffAllowEnabled = snapshot.FrequencyCutoffAllowEnabled;
            FrequencyCutoffAllowHz = snapshot.FrequencyCutoffAllowHz;
            SilenceMinGapEnabled = snapshot.Silence.MinGapEnabled;
            SilenceMinGapSeconds = snapshot.Silence.MinGapSeconds;
            SilenceSkipEdgesEnabled = snapshot.Silence.SkipEdgesEnabled;
            SilenceSkipEdgeSeconds = snapshot.Silence.SkipEdgeSeconds;
        }

        private sealed class StaticAnalysisSettings : IAnalysisSettings
        {
            public bool EnableBpmDetection => AudioAnalyzer.EnableBpmDetection;
            public bool EnableExperimentalAi => AudioAnalyzer.EnableExperimentalAi;
            public bool EnableRipQuality => AudioAnalyzer.EnableRipQuality;
            public bool EnableSilenceDetection => AudioAnalyzer.EnableSilenceDetection;
            public bool EnableFakeStereoDetection => AudioAnalyzer.EnableFakeStereoDetection;
            public bool EnableDynamicRange => AudioAnalyzer.EnableDynamicRange;
            public bool EnableTruePeak => AudioAnalyzer.EnableTruePeak;
            public bool EnableLufs => AudioAnalyzer.EnableLufs;
            public bool EnableClippingDetection => AudioAnalyzer.EnableClippingDetection;
            public bool EnableMqaDetection => AudioAnalyzer.EnableMqaDetection;
            public bool EnableDefaultAiDetection => AudioAnalyzer.EnableDefaultAiDetection;
            public bool AlwaysFullAnalysis => AudioAnalyzer.AlwaysFullAnalysis;
            public bool FrequencyCutoffAllowEnabled => AudioAnalyzer.FrequencyCutoffAllowEnabled;
            public int FrequencyCutoffAllowHz => AudioAnalyzer.FrequencyCutoffAllowHz;
            public SilenceSettings Silence => new(
                AudioAnalyzer.SilenceMinGapEnabled,
                AudioAnalyzer.SilenceMinGapSeconds,
                AudioAnalyzer.SilenceSkipEdgesEnabled,
                AudioAnalyzer.SilenceSkipEdgeSeconds);
        }

        // Runtime environment validation — checked once on first analysis
        private static volatile bool _envChecked;

        // FFT calibration state — validated once per session to ensure accurate spectral results
        private static volatile bool _fftCalibrated;
        private static double _calibrationOffset;

        public static AudioFileInfo AnalyzeFile(string filePath, CancellationToken ct = default)
        {
            return AnalyzeFile(filePath, ActiveSettings, ct);
        }

        public static AudioFileInfo AnalyzeFile(string filePath, IAnalysisSettings settings, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            ArgumentNullException.ThrowIfNull(settings);
            var analysisSettings = AnalysisSettingsSnapshot.From(settings);

            // Validate runtime environment on first analysis pass (silent, non-blocking)
            if (!_envChecked)
            {
                _envChecked = true;
                try { DiagnosticContext.ValidateEnvironment(); } catch { }
            }

            // FFT calibration: no offset by default. Previously used reflection-based
            // integrity checks; removed for maintainability. If a specific build needs
            // a correction, set _calibrationOffset here explicitly.
            if (!_fftCalibrated)
            {
                _fftCalibrated = true;
                _calibrationOffset = 0.0;
            }

            var info = new AudioFileInfo
            {
                FilePath = filePath,
                FileName = Path.GetFileName(filePath),
                Extension = Path.GetExtension(filePath).ToLowerInvariant(),
                FolderPath = Path.GetDirectoryName(filePath) ?? ""
            };

            TagLib.File? sharedTagFile = null;
            try
            {
                var fi = new FileInfo(filePath);
                info.FileSizeBytes = fi.Length;
                info.FileSize = FormatFileSize(fi.Length);
                info.DateModified = fi.LastWriteTime;
                info.DateCreated = fi.CreationTime;

                // ── Metadata via TagLib (kept open for reuse by MQA + AI watermark detectors) ──
                try
                {
                    WaitIfPaused(ct);
                    sharedTagFile = TagLib.File.Create(filePath);
                    info.Artist = sharedTagFile.Tag.FirstPerformer ?? sharedTagFile.Tag.FirstAlbumArtist ?? "";
                    info.Title = sharedTagFile.Tag.Title ?? "";
                    info.Album = sharedTagFile.Tag.Album ?? "";
                    info.ReportedBitrate = sharedTagFile.Properties.AudioBitrate;
                    info.SampleRate = sharedTagFile.Properties.AudioSampleRate;
                    info.BitsPerSample = sharedTagFile.Properties.BitsPerSample;
                    info.Channels = sharedTagFile.Properties.AudioChannels;
                    info.Duration = FormatDuration(sharedTagFile.Properties.Duration);
                    info.DurationSeconds = sharedTagFile.Properties.Duration.TotalSeconds;

                    // Frequency (sample rate is the audio frequency)
                    info.Frequency = sharedTagFile.Properties.AudioSampleRate;

                    // Extract BPM from tag first
                    if (sharedTagFile.Tag.BeatsPerMinute > 0)
                        info.Bpm = (int)sharedTagFile.Tag.BeatsPerMinute;

                    // Extract Replay Gain from tags
                    ExtractReplayGain(sharedTagFile, info);

                    // Detect ALAC codec inside M4A/MP4 containers
                    if (info.Extension is ".m4a" or ".mp4")
                    {
                        try
                        {
                            if (DetectAlacCodec(filePath, sharedTagFile))
                            {
                                info.IsAlac = true;
                                // TagLib reports 0 for ALAC-in-MP4 bit depth/bitrate. Read the
                                // real values from the ALAC magic-cookie atom when that happens.
                                if (info.BitsPerSample <= 0 || info.ReportedBitrate <= 0)
                                    PopulateAlacMetadata(filePath, info);
                            }
                        }
                        catch { /* ALAC detection is best-effort */ }
                    }
                    else if (info.Extension == ".alac")
                    {
                        info.IsAlac = true;
                        if (info.BitsPerSample <= 0 || info.ReportedBitrate <= 0)
                        {
                            try { PopulateAlacMetadata(filePath, info); } catch { }
                        }
                    }

                    // Album cover detection (reuse the already-open TagLib instance)
                    info.HasAlbumCover = sharedTagFile.Tag.Pictures?.Length > 0;
                }
                catch (OperationCanceledException) { throw; }
                catch
                {
                    // Metadata read failure is not fatal — audio may still decode fine via NAudio
                    sharedTagFile = null;
                }

                // If no BPM tag, detect algorithmically
                if (analysisSettings.EnableBpmDetection && info.Bpm <= 0)
                {
                    try { info.Bpm = DetectBpm(filePath); } catch { }
                }

                // ── Spectral analysis via NAudio ──
                try
                {
                    AnalyzeSpectralContent(filePath, info, analysisSettings, ct);
                }
                catch (OperationCanceledException) { throw; }
                catch
                {
                    if (info.SampleRate > 0)
                    {
                        info.Status = AudioStatus.Unknown;
                        info.ErrorMessage = "Spectral analysis failed";
                    }
                    else
                    {
                        info.Status = AudioStatus.Unknown;
                        info.ErrorMessage = "Cannot decode audio data";
                    }
                    return info;
                }


                // Combined full-file pass (Silence + DR + True Peak + LUFS + Rip Quality)
                if (analysisSettings.AlwaysFullAnalysis
                    || analysisSettings.EnableSilenceDetection
                    || analysisSettings.EnableDynamicRange
                    || analysisSettings.EnableTruePeak
                    || analysisSettings.EnableLufs
                    || analysisSettings.EnableRipQuality)
                {
                    try
                    {
                        RunFullFilePass(filePath, info, analysisSettings, ct);
                    }
                    catch (OperationCanceledException) { throw; }
                    catch { /* Full file pass is optional; individual features degrade gracefully */ }
                }

                // Optimizer detection
                try
                {
                    if (DetectOptimizer(info))
                    {
                        info.Status = AudioStatus.Optimized;
                        return info;
                    }
                }
                catch { /* Optimizer detection is optional */ }

                // Quality verdict
                DetermineQuality(info, analysisSettings);

                // MQA detection runs after main analysis.
                if (analysisSettings.EnableMqaDetection)
                {
                    try
                    {
                        bool mqaEligible = info.Channels == 2
                                         && (info.SampleRate == 44100 || info.SampleRate == 48000)
                                         && (info.Extension is ".flac" or ".wav" or ".alac" or ".m4a" or ".mp4" or ".aiff" or ".aif");
                        var mqaResult = MqaDetector.Detect(filePath, sharedTagFile, mqaEligible);
                        if (mqaResult != null)
                        {
                            info.IsMqa = mqaResult.IsMqa;
                            info.IsMqaStudio = mqaResult.IsStudio;
                            info.MqaOriginalSampleRate = mqaResult.OriginalSampleRate;
                            info.MqaEncoder = mqaResult.Encoder;
                        }
                    }
                    catch { /* MQA detection is optional */ }
                }

                // AI watermark detection
                if (analysisSettings.EnableDefaultAiDetection)
                {
                    try
                    {
                        var aiResult = AiWatermarkDetector.Detect(filePath, sharedTagFile);
                        if (aiResult != null && aiResult.IsAiDetected)
                        {
                            info.IsAiGenerated = true;
                            info.AiSource = aiResult.Summary;
                            info.AiSources = aiResult.Sources;
                        }
                    }
                    catch { /* AI detection is optional */ }
                }

                // Experimental AI detection
                try
                {
                    if (analysisSettings.EnableExperimentalAi)
                    {
                        var expResult = ExperimentalAiDetector.Analyze(filePath);
                        if (expResult != null && expResult.Suspicious)
                        {
                            info.ExperimentalAiSuspicious = true;
                            info.ExperimentalAiConfidence = expResult.Confidence;
                            info.ExperimentalAiFlags = expResult.Flags;
                        }
                    }
                }
                catch { /* Experimental AI detection is optional */ }
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                info.Status = AudioStatus.Corrupt;
                info.ErrorMessage = $"Error: {ex.Message}";
            }
            finally
            {
                sharedTagFile?.Dispose();
            }

            return info;
        }

        // ═══════════════════════════════════════════════════════
        //  Spectral Analysis
        // ═══════════════════════════════════════════════════════

        private static void AnalyzeSpectralContent(
            string filePath,
            AudioFileInfo info,
            IAnalysisSettings settings,
            CancellationToken ct)
        {
            WaitIfPaused(ct);
            var (disposable, samples, waveFormat) = OpenAudioFile(filePath);
            using var _ = disposable;

            int sampleRate = waveFormat.SampleRate;
            int channels = waveFormat.Channels;

            if (info.SampleRate == 0) info.SampleRate = sampleRate;
            if (info.Channels == 0) info.Channels = channels;

            long totalFrames;
            if (disposable is AudioFileReader afr)
                totalFrames = afr.Length / afr.WaveFormat.BlockAlign;
#if !CROSS_PLATFORM
            else if (disposable is MediaFoundationReader mfr2)
                totalFrames = mfr2.Length / mfr2.WaveFormat.BlockAlign;
#endif
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

            long safeStart = (long)(totalFrames * 0.05);
            long safeEnd = (long)(totalFrames * 0.95);
            long safeRange = safeEnd - safeStart - FftSize;
            if (safeRange < FftSize * 3) { safeStart = 0; safeRange = totalFrames - FftSize; }

            long stepFrames = safeRange / segmentCount;

            int spectrumSize = FftSize / 2;
            double[] avgSpectrum = new double[spectrumSize];
            float[] readBuf = new float[FftSize * channels];
            float[] skipBuf = new float[4096 * channels];

            double[] window = new double[FftSize];
            for (int i = 0; i < FftSize; i++)
                window[i] = 0.5 * (1.0 - Math.Cos(2.0 * Math.PI * i / (FftSize - 1)));

            long clippingSamples = 0;
            long totalSamplesRead = 0;
            int validSegments = 0;
            float maxAbsSample = 0f;

            const int PeakHistBins = 1000;
            const float PeakHistMin = 0.5f;
            const float PeakHistScale = PeakHistBins / (1.0f - PeakHistMin);
            int[]? peakHistogram = settings.EnableClippingDetection ? new int[PeakHistBins] : null;

            bool trackStereo = channels == 2 && settings.EnableFakeStereoDetection;
            double corrLR = 0, corrLL = 0, corrRR = 0;
            long stereoSamples = 0;

            WaveStream? seekableStream = disposable as WaveStream;
            long currentFrame = 0;

            if (seekableStream != null)
            {
                seekableStream.Position = safeStart * seekableStream.WaveFormat.BlockAlign;
                currentFrame = safeStart;
            }
            else
            {
                long toSkip = safeStart * channels;
                while (toSkip > 0)
                {
                    WaitIfPaused(ct);
                    int chunk = (int)Math.Min(toSkip, skipBuf.Length);
                    int got = samples.Read(skipBuf, 0, chunk);
                    if (got <= 0) break;
                    toSkip -= got;
                }
                currentFrame = safeStart;
            }

            for (int seg = 0; seg < segmentCount; seg++)
            {
                WaitIfPaused(ct);
                long framePos = safeStart + seg * stepFrames;
                long framesToSkip = framePos - currentFrame;
                if (framesToSkip > 0)
                {
                    if (seekableStream != null)
                    {
                        seekableStream.Position = framePos * seekableStream.WaveFormat.BlockAlign;
                    }
                    else
                    {
                        long samplesToSkip = framesToSkip * channels;
                        while (samplesToSkip > 0)
                        {
                            WaitIfPaused(ct);
                            int chunk = (int)Math.Min(samplesToSkip, skipBuf.Length);
                            int got = samples.Read(skipBuf, 0, chunk);
                            if (got <= 0) break;
                            samplesToSkip -= got;
                        }
                    }
                    currentFrame = framePos;
                }

                int read = samples.Read(readBuf, 0, readBuf.Length);
                currentFrame += FftSize;
                if (read < readBuf.Length) continue;

                double[] real = new double[FftSize];
                double[] imag = new double[FftSize];

                for (int i = 0; i < FftSize; i++)
                {
                    float sum = 0;
                    for (int ch = 0; ch < channels; ch++)
                    {
                        float sample = readBuf[i * channels + ch];
                        float absSample = Math.Abs(sample);
                        sum += sample;
                        if (settings.EnableClippingDetection)
                        {
                            if (absSample >= ClippingThreshold) clippingSamples++;
                            if (absSample >= PeakHistMin && absSample < 1.0f)
                            {
                                int bin = (int)((absSample - PeakHistMin) * PeakHistScale);
                                if (bin >= PeakHistBins) bin = PeakHistBins - 1;
                                peakHistogram![bin]++;
                            }
                        }
                        if (absSample > maxAbsSample) maxAbsSample = absSample;
                        totalSamplesRead++;
                    }

                    if (trackStereo)
                    {
                        float left = readBuf[i * 2];
                        float right = readBuf[i * 2 + 1];
                        corrLR += (double)left * right;
                        corrLL += (double)left * left;
                        corrRR += (double)right * right;
                        stereoSamples++;
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

            if (totalSamplesRead > 0 && settings.EnableClippingDetection)
            {
                info.ClippingSamples = clippingSamples;
                info.ClippingPercentage = (double)clippingSamples / totalSamplesRead * 100.0;
                info.HasClipping = info.ClippingPercentage > 0.01;
                info.MaxSampleLevel = maxAbsSample;
                info.MaxSampleLevelDb = maxAbsSample > 1e-10 ? 20.0 * Math.Log10(maxAbsSample) : -200;

                if (!info.HasClipping && maxAbsSample > 0.5f && maxAbsSample < ClippingThreshold)
                {
                    float scaledThreshold = maxAbsSample * 0.9999f;
                    int scaledBin = Math.Max(0, (int)((scaledThreshold - PeakHistMin) * PeakHistScale));
                    if (scaledBin < PeakHistBins)
                    {
                        long scaledClipSamples = 0;
                        for (int b = scaledBin; b < PeakHistBins; b++)
                            scaledClipSamples += peakHistogram![b];
                        if (totalSamplesRead > 0)
                        {
                            double pct = (double)scaledClipSamples / totalSamplesRead * 100.0;
                            if (pct > 0.01)
                            {
                                info.HasScaledClipping = true;
                                info.ScaledClippingPercentage = pct;
                            }
                        }
                    }
                }
            }

            int rawCutoff = FindCutoffFrequency(avgSpectrum, sampleRate);
            info.EffectiveFrequency = _calibrationOffset == 0.0
                ? rawCutoff
                : Math.Max(0, rawCutoff + (int)(_calibrationOffset * 100));

            if (settings.EnableFakeStereoDetection && trackStereo && stereoSamples > 0 && corrLL > 0 && corrRR > 0)
            {
                double denom = Math.Sqrt(corrLL * corrRR);
                double correlation = denom > 1e-10 ? corrLR / denom : 0;
                info.StereoCorrelation = Math.Round(correlation, 4);

                if (correlation >= 0.9999)
                {
                    info.IsFakeStereo = true;
                    info.FakeStereoType = "Mono Duplicate";
                }
                else if (correlation >= 0.995)
                {
                    info.IsFakeStereo = true;
                    info.FakeStereoType = "Near-Mono";
                }
            }

            bool isLossless = IsLosslessFile(info);
            string codec = info.IsAlac ? "alac" : info.Extension.TrimStart('.');
            int estimated = EstimateBitrateFromCutoff(info.EffectiveFrequency, sampleRate, isLossless, codec);

            if (isLossless)
            {
                int cutoff = info.EffectiveFrequency;
                int nyquist = sampleRate / 2;

                if (cutoff >= (int)(nyquist * 0.90) || cutoff >= 20000)
                {
                    if (info.DurationSeconds > 0 && info.FileSizeBytes > 0)
                        info.ActualBitrate = (int)(info.FileSizeBytes * 8.0 / info.DurationSeconds / 1000.0);
                    else
                    {
                        int bitsPerSample = info.BitsPerSample > 0 ? info.BitsPerSample : 16;
                        int ch = info.Channels > 0 ? info.Channels : 2;
                        info.ActualBitrate = sampleRate * bitsPerSample * ch / 1000;
                    }
                }
                else if (cutoff <= 0 || estimated <= 32)
                {
                    if (info.DurationSeconds > 0 && info.FileSizeBytes > 0)
                        info.ActualBitrate = (int)(info.FileSizeBytes * 8.0 / info.DurationSeconds / 1000.0);
                    else
                    {
                        int bitsPerSample = info.BitsPerSample > 0 ? info.BitsPerSample : 16;
                        int ch = info.Channels > 0 ? info.Channels : 2;
                        info.ActualBitrate = sampleRate * bitsPerSample * ch / 1000;
                    }
                }
                else
                {
                    info.ActualBitrate = estimated;
                }
            }
            else
            {
                if (info.ReportedBitrate > 0)
                {
                    double ratio = (double)estimated / info.ReportedBitrate;
                    if (ratio >= 0.70 && ratio <= 1.30)
                    {
                        info.ActualBitrate = info.ReportedBitrate;
                    }
                    else if (estimated < info.ReportedBitrate)
                    {
                        int trustCutoffLimit;
                        if (estimated <= 64)
                            trustCutoffLimit = 11000;
                        else if (estimated <= 96)
                            trustCutoffLimit = 12500;
                        else
                            trustCutoffLimit = 14000;

                        if (info.EffectiveFrequency < trustCutoffLimit)
                            info.ActualBitrate = estimated;
                        else
                            info.ActualBitrate = info.ReportedBitrate;
                    }
                    else
                    {
                        info.ActualBitrate = info.ReportedBitrate;
                    }
                }
                else
                {
                    info.ActualBitrate = estimated;
                }
            }
        }

        // ═══════════════════════════════════════════════════════

        private const float SilenceThresholdLinear = 0.001f; // ~-60 dBFS

        // Quality / Cutoff / Bitrate Estimation - see AudioAnalyzer.Quality.cs
        // Optimizer Detection - see AudioAnalyzer.Optimizer.cs
        // ═══════════════════════════════════════════════════════
        //  FFT (Cooley-Tukey radix-2) — pre-computed twiddle factors
        // ═══════════════════════════════════════════════════════

        private static readonly System.Collections.Concurrent.ConcurrentDictionary<int, (double[] cos, double[] sin)> _twiddleCache = new();

        private static (double[] cos, double[] sin) GetTwiddle(int halfSize)
        {
            return _twiddleCache.GetOrAdd(halfSize, h =>
            {
                var cos = new double[h];
                var sin = new double[h];
                double step = -2.0 * Math.PI / (h * 2);
                for (int j = 0; j < h; j++)
                {
                    double a = step * j;
                    cos[j] = Math.Cos(a);
                    sin[j] = Math.Sin(a);
                }
                return (cos, sin);
            });
        }

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
                var (twCos, twSin) = GetTwiddle(half);
                for (int i = 0; i < n; i += size)
                {
                    for (int j = 0; j < half; j++)
                    {
                        double cos = twCos[j];
                        double sin = twSin[j];
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
                   or ".ape" or ".wv" or ".alac" or ".dsf" or ".dff"
                   or ".tak" or ".bwf";

        /// <summary>
        /// Checks if a file is lossless, accounting for ALAC codec inside M4A containers.
        /// </summary>
        private static bool IsLosslessFile(AudioFileInfo info)
            => IsLosslessExtension(info.Extension) || info.IsAlac;

        /// <summary>
        /// Detects whether an M4A/MP4 file contains the ALAC (Apple Lossless) codec
        /// by checking TagLib codec info, MP4 box types, and raw binary scanning.
        /// </summary>
        private static bool DetectAlacCodec(string filePath, TagLib.File tagFile)
        {
            // Method 1: Check TagLib codec description (covers many TagLib versions)
            try
            {
                foreach (var codec in tagFile.Properties.Codecs)
                {
                    if (codec?.Description != null)
                    {
                        string desc = codec.Description.ToUpperInvariant();
                        if (desc.Contains("ALAC") || desc.Contains("APPLE LOSSLESS"))
                            return true;
                    }
                }
            }
            catch { /* fall through */ }

            // Method 2: If TagLib opened it as an MPEG-4 file, check the audio codec box type
            try
            {
                if (tagFile is TagLib.Mpeg4.File mp4File)
                {
                    // TagLib exposes codec info through Properties.Codecs — check for AppleTag AudioSampleEntry
                    foreach (var codec in mp4File.Properties.Codecs)
                    {
                        // TagLib.Mpeg4.IsoAudioSampleEntry or similar — check type name as fallback
                        string typeName = codec?.GetType().Name ?? "";
                        if (typeName.Contains("Apple", StringComparison.OrdinalIgnoreCase))
                            return true;
                    }
                }
            }
            catch { /* fall through */ }

            // Method 3: Check BitsPerSample — AAC reports 0 or 16 typically, ALAC reports 16/24/32
            // ALAC files have very high bitrates (usually 700-1400+ kbps) compared to AAC
            if (tagFile.Properties.BitsPerSample >= 16 && tagFile.Properties.AudioBitrate > 500)
                return true;

            // Method 4: Scan MP4 atoms for 'alac' codec identifier
            // The 'alac' four-character code appears in the stsd (sample description) box
            // as the codec type, typically within the first 64-128KB of the file
            try
            {
                using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
                int scanLen = (int)Math.Min(131072, fs.Length); // scan first 128KB
                byte[] buf = new byte[scanLen];
                int bytesRead = fs.Read(buf, 0, scanLen);

                byte a = (byte)'a', l = (byte)'l', c = (byte)'c';

                for (int i = 0; i < bytesRead - 3; i++)
                {
                    // Match 'alac' (0x61 0x6C 0x61 0x63)
                    if (buf[i] == a && buf[i + 1] == l && buf[i + 2] == a && buf[i + 3] == c)
                        return true;
                }
            }
            catch { /* binary scan failed */ }

            return false;
        }

        /// <summary>
        /// Reads real bit depth, sample rate, channel count, and average bitrate from the
        /// ALAC magic cookie (ALACSpecificConfig) inside an MP4/M4A container. TagLib reports
        /// 0 for these on ALAC, so we parse the box tree directly.
        ///
        /// Layout per Apple's ALAC spec: the 'alac' sample entry lives under
        /// moov/trak/mdia/minf/stbl/stsd. The entry contains a nested 'alac' box whose
        /// payload is the 24-byte ALACSpecificConfig:
        ///   frameLength(4) compatibleVersion(1) bitDepth(1) pb(1) mb(1) kb(1)
        ///   numChannels(1) maxRun(2) maxFrameBytes(4) avgBitRate(4) sampleRate(4)
        /// </summary>
        private static void PopulateAlacMetadata(string filePath, AudioFileInfo info)
        {
            using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            // Walk down the container hierarchy to the sample description box.
            long stsdEnd = FindBox(fs, 0, fs.Length, new[] { "moov", "trak", "mdia", "minf", "stbl", "stsd" });
            if (stsdEnd < 0) return;

            // stsd: 4 bytes version/flags + 4 bytes entry count, then sample entries.
            long pos = fs.Position; // positioned at first byte of stsd payload by FindBox
            fs.Seek(pos + 8, SeekOrigin.Begin);

            // Scan sample entries for the 'alac' four-char code, then read the nested
            // 'alac' config box that follows the 36-byte AudioSampleEntry header.
            byte[] buf = new byte[4];
            long scanEnd = stsdEnd;
            while (fs.Position + 8 <= scanEnd)
            {
                long entryStart = fs.Position;
                long entrySize = ReadUInt32BE(fs);
                if (entrySize < 8) break;
                if (fs.Read(buf, 0, 4) != 4) break;
                string type = System.Text.Encoding.ASCII.GetString(buf);
                if (type == "alac")
                {
                    // AudioSampleEntry: 6 reserved + 2 data-ref-index + 8 reserved
                    // + 2 channelcount + 2 samplesize + 2 predefined + 2 reserved
                    // + 4 samplerate(16.16) = 28 bytes, then the nested 'alac' config box.
                    fs.Seek(entryStart + 8 + 28, SeekOrigin.Begin);
                    long cfgSize = ReadUInt32BE(fs);
                    if (fs.Read(buf, 0, 4) != 4) return;
                    string cfgType = System.Text.Encoding.ASCII.GetString(buf);
                    if (cfgType != "alac" || cfgSize < 12 + 24) return;

                    // Skip the 4-byte version/flags of the full box, then read the 24-byte config.
                    fs.Seek(4, SeekOrigin.Current);
                    byte[] cfg = new byte[24];
                    if (fs.Read(cfg, 0, 24) != 24) return;

                    int bitDepth = cfg[5];
                    int numChannels = cfg[9];
                    long avgBitRate = ((long)cfg[16] << 24) | ((long)cfg[17] << 16) | ((long)cfg[18] << 8) | cfg[19];
                    long sampleRate = ((long)cfg[20] << 24) | ((long)cfg[21] << 16) | ((long)cfg[22] << 8) | cfg[23];

                    if (bitDepth is > 0 and <= 32) info.BitsPerSample = bitDepth;
                    if (numChannels is > 0 and <= 8 && info.Channels <= 0) info.Channels = numChannels;
                    if (sampleRate is > 0 and <= 768000 && info.SampleRate <= 0)
                    {
                        info.SampleRate = (int)sampleRate;
                        info.Frequency = (int)sampleRate;
                    }
                    if (info.ReportedBitrate <= 0)
                    {
                        if (avgBitRate > 0)
                            info.ReportedBitrate = (int)(avgBitRate / 1000);
                        else if (info.DurationSeconds > 0)
                            info.ReportedBitrate = (int)(info.FileSizeBytes * 8 / info.DurationSeconds / 1000);
                    }
                    return;
                }
                fs.Seek(entryStart + entrySize, SeekOrigin.Begin);
            }
        }

        /// <summary>
        /// Descends an MP4 box path (e.g. moov/trak/.../stsd). On success the stream is left
        /// positioned at the first byte of the final box's payload and the payload end offset
        /// is returned; returns -1 if any box in the path is missing.
        /// </summary>
        private static long FindBox(FileStream fs, long start, long end, string[] path)
        {
            long rangeStart = start, rangeEnd = end;
            long payloadEnd = -1;
            byte[] typeBuf = new byte[4];
            foreach (var want in path)
            {
                fs.Seek(rangeStart, SeekOrigin.Begin);
                bool found = false;
                while (fs.Position + 8 <= rangeEnd)
                {
                    long boxStart = fs.Position;
                    long size = ReadUInt32BE(fs);
                    if (fs.Read(typeBuf, 0, 4) != 4) return -1;
                    string type = System.Text.Encoding.ASCII.GetString(typeBuf);
                    long boxEnd = size == 1
                        ? boxStart + ReadUInt64BE(fs) // 64-bit largesize
                        : boxStart + size;
                    long contentStart = fs.Position;
                    if (size < 8 || boxEnd > rangeEnd) return -1;
                    if (type == want)
                    {
                        rangeStart = contentStart;
                        rangeEnd = boxEnd;
                        payloadEnd = boxEnd;
                        fs.Seek(contentStart, SeekOrigin.Begin);
                        found = true;
                        break;
                    }
                    fs.Seek(boxEnd, SeekOrigin.Begin);
                }
                if (!found) return -1;
            }
            return payloadEnd;
        }

        private static long ReadUInt32BE(FileStream fs)
        {
            byte[] b = new byte[4];
            if (fs.Read(b, 0, 4) != 4) return 0;
            return ((long)b[0] << 24) | ((long)b[1] << 16) | ((long)b[2] << 8) | b[3];
        }

        private static long ReadUInt64BE(FileStream fs)
        {
            byte[] b = new byte[8];
            if (fs.Read(b, 0, 8) != 8) return 0;
            long v = 0;
            for (int i = 0; i < 8; i++) v = (v << 8) | b[i];
            return v;
        }
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
        /// Calculates R128-style loudness and writes REPLAYGAIN_TRACK_GAIN and REPLAYGAIN_TRACK_PEAK
        /// tags to the file via TagLib. Reference level: -18 LUFS.
        /// Returns (gain, peak) on success, or null on failure.
        /// </summary>
        public static (double Gain, double Peak)? CalculateAndWriteReplayGain(string filePath)
        {
            // Step 1: Calculate integrated loudness (simplified R128 — RMS-based)
            double rmsSum = 0;
            long totalFrames = 0;
            double peak = 0;

            var (disposable, samples, format) = OpenAudioFile(filePath);
            if (disposable == null || samples == null || format == null) return null;

            using (disposable)
            {
                int channels = format.Channels;
                int blockSize = 4096 * channels;
                float[] buf = new float[blockSize];

                while (true)
                {
                    int read = samples.Read(buf, 0, blockSize);
                    if (read <= 0) break;
                    int frames = read / channels;

                    for (int i = 0; i < frames; i++)
                    {
                        double sum = 0;
                        for (int ch = 0; ch < channels; ch++)
                        {
                            double s = buf[i * channels + ch];
                            sum += s * s;
                            double abs = Math.Abs(s);
                            if (abs > peak) peak = abs;
                        }
                        rmsSum += sum / channels; // average power across channels
                    }
                    totalFrames += frames;
                }
            }

            if (totalFrames == 0 || peak < 1e-10) return null;

            double meanPower = rmsSum / totalFrames;
            double rmsDb = 10.0 * Math.Log10(meanPower); // in dBFS
            // R128 reference: -18 LUFS ≈ -18 dBFS for a simple RMS-based approximation
            double gain = -18.0 - rmsDb;
            gain = Math.Round(gain, 2);
            peak = Math.Round(peak, 6);

            // Step 2: Write tags via TagLib
            try
            {
                using var tagFile = TagLib.File.Create(filePath);
                string gainStr = $"{gain:+0.00;-0.00;0.00} dB";
                string peakStr = $"{peak:F6}";

                // Write to ID3v2 TXXX frames (MP3)
                if (tagFile.GetTag(TagLib.TagTypes.Id3v2, true) is TagLib.Id3v2.Tag id3)
                {
                    SetOrAddTxxx(id3, "REPLAYGAIN_TRACK_GAIN", gainStr);
                    SetOrAddTxxx(id3, "REPLAYGAIN_TRACK_PEAK", peakStr);
                }

                // Write to Xiph Comment (FLAC, OGG, OPUS)
                if (tagFile.GetTag(TagLib.TagTypes.Xiph, true) is TagLib.Ogg.XiphComment xiph)
                {
                    xiph.SetField("REPLAYGAIN_TRACK_GAIN", gainStr);
                    xiph.SetField("REPLAYGAIN_TRACK_PEAK", peakStr);
                }

                // Write to APE tag (APE, MPC, WavPack)
                if (tagFile.GetTag(TagLib.TagTypes.Ape, true) is TagLib.Ape.Tag ape)
                {
                    ape.SetValue("REPLAYGAIN_TRACK_GAIN", gainStr);
                    ape.SetValue("REPLAYGAIN_TRACK_PEAK", peakStr);
                }

                tagFile.Save();
                return (gain, peak);
            }
            catch { return null; }
        }

        private static void SetOrAddTxxx(TagLib.Id3v2.Tag id3, string description, string value)
        {
            // Remove existing frame with same description
            foreach (var frame in id3.GetFrames<TagLib.Id3v2.UserTextInformationFrame>().ToArray())
            {
                if (frame.Description != null &&
                    frame.Description.Equals(description, StringComparison.OrdinalIgnoreCase))
                {
                    id3.RemoveFrame(frame);
                }
            }
            // Add new frame
            var newFrame = new TagLib.Id3v2.UserTextInformationFrame(description)
            {
                Text = new[] { value },
                TextEncoding = TagLib.StringType.UTF8
            };
            id3.AddFrame(newFrame);
        }

        /// <summary>
        /// ISampleProvider backed by NLayer's fully-managed MPEG decoder, so MP3/MP2 decode on
        /// every OS (Windows uses AudioFileReader/ACM; this is the Linux/macOS path).
        /// </summary>
        private sealed class NLayerMp3SampleProvider : ISampleProvider, IDisposable
        {
            private readonly NLayer.MpegFile _mpeg;
            public NLayerMp3SampleProvider(string filePath)
            {
                _mpeg = new NLayer.MpegFile(filePath);
                WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(_mpeg.SampleRate, _mpeg.Channels);
            }
            public WaveFormat WaveFormat { get; }
            public int Read(float[] buffer, int offset, int count) => _mpeg.ReadSamples(buffer, offset, count);
            public void Dispose() => _mpeg.Dispose();
        }

        /// <summary>
        /// Opens an audio file as a sample provider (float samples).
        /// Tries AudioFileReader first (best quality), then a managed MP3 decoder, then (on Windows)
        /// MediaFoundationReader for formats NAudio can't natively decode (AAC/M4A, WMA, etc.).
        /// Returns the reader (to be disposed by caller) and the ISampleProvider.
        /// </summary>
        public static (IDisposable reader, ISampleProvider samples, WaveFormat format) OpenAudioFile(string filePath)
        {
            return OpenAudioFileInner(filePath);
        }

        private static (IDisposable reader, ISampleProvider samples, WaveFormat format) OpenAudioFileInner(string filePath)
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

#if !CROSS_PLATFORM
            // TTA (True Audio) — MediaFoundation has a TTA codec on Win10+
            if (ext is ".tta")
            {
                try
                {
                    var tta = new MediaFoundationReader(filePath);
                    var ttaSample = new SampleChannel(tta, false);
                    return (tta, ttaSample, ttaSample.WaveFormat);
                }
                catch { /* fall through */ }
            }

            // MPC (Musepack) — MediaFoundationReader may work if codec is installed
            if (ext is ".mpc" or ".mp+")
            {
                try
                {
                    var mpc = new MediaFoundationReader(filePath);
                    var mpcSample = new SampleChannel(mpc, false);
                    return (mpc, mpcSample, mpcSample.WaveFormat);
                }
                catch { /* MPC codec likely not installed — TagLib# metadata still available */ }
            }
#endif

            // AudioFileReader handles: MP3, WAV, AIFF, WMA, FLAC (via MediaFoundation on Win10+)
            try
            {
                var afr = new AudioFileReader(filePath);
                return (afr, afr, afr.WaveFormat);
            }
            catch { /* fall through to MediaFoundation */ }

            // Managed MPEG decoder (NLayer) — pure managed, works on every OS. This is what makes
            // MP3/MP2 analysis work on Linux/macOS, where AudioFileReader has no frame decompressor.
            // On Windows AudioFileReader already succeeds above, so this never runs there (no change).
            if (ext is ".mp3" or ".mp2")
            {
                try
                {
                    var mp3 = new NLayerMp3SampleProvider(filePath);
                    return (mp3, mp3, mp3.WaveFormat);
                }
                catch { /* fall through */ }
            }

#if !CROSS_PLATFORM
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
            catch { /* fall through to managed FLAC */ }
#endif

            // Managed FLAC decoder (handles hi-res and files MediaFoundation can't decode)
            if (ext is ".flac" or ".fla")
            {
                try
                {
                    var flac = new FlacFileReader(filePath);
                    var flacSample = new SampleChannel(flac, false);
                    return (flac, flacSample, flacSample.WaveFormat);
                }
                catch { /* fall through */ }
            }

            // WaveFileReader for raw WAV/RF64/BWF (including 24-bit → 16-bit conversion)
            if (ext is ".wav" or ".wave" or ".bwf" or ".rf64")
            {
                try
                {
                    var wav = new WaveFileReader(filePath);
                    if (wav.WaveFormat.BitsPerSample == 24 || wav.WaveFormat.BitsPerSample == 32)
                    {
                        var floatProvider = new Wave32To16Stream(wav);
                        var sample = new SampleChannel(floatProvider, false);
                        return (floatProvider, sample, sample.WaveFormat);
                    }
                    else
                    {
                        var pcm = WaveFormatConversionStream.CreatePcmStream(wav);
                        var sample = new SampleChannel(pcm, false);
                        return (pcm, sample, sample.WaveFormat);
                    }
                }
                catch { /* fall through */ }
            }

            throw new InvalidOperationException($"Cannot open audio file: {Path.GetFileName(filePath)}");
        }

        private static string FormatFileSize(long bytes)
        {
            if (bytes < 1024) return $"{bytes} B";
            if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
            if (bytes < 1024 * 1024 * 1024) return $"{bytes / (1024.0 * 1024):F1} MB";
            return $"{bytes / (1024.0 * 1024 * 1024):F2} GB";
        }

        // True Peak + LUFS - see AudioAnalyzer.Loudness.cs
        // ═══════════════════════════════════════════════════════
        //  Rip/Encode Quality Detection
        // ═══════════════════════════════════════════════════════

        /// <summary>
        /// Improved rip quality analysis with better-tuned thresholds and additional
        /// signals (DC offset, noise floor) to reduce false positives.
        /// </summary>
        private static void FinalizeRipQuality(AudioFileInfo info, int sr, int channels,
            long totalFrames, long zeroRuns, long stickyRuns, long popClicks, long truncatedSamples,
            float dcOffset, float noiseFloorRms)
        {
            var issues = new List<string>();
            string quality = "Good";
            double durationSec = totalFrames > 0 ? (double)totalFrames / sr : 0;

            // Zero gaps: only flag if > 2 occurrences or very long gaps (> 1s)
            if (zeroRuns > 2)
            {
                issues.Add($"{zeroRuns} zero gaps");
                quality = "Bad";
            }
            else if (zeroRuns > 0)
            {
                issues.Add($"{zeroRuns} zero gap");
                if (quality == "Good") quality = "Suspect";
            }

            // Glitches/stuck samples: require more occurrences, threshold tuned
            if (stickyRuns > 10)
            {
                issues.Add($"{stickyRuns} glitches");
                quality = "Bad";
            }
            else if (stickyRuns > 3)
            {
                issues.Add($"{stickyRuns} glitches");
                if (quality == "Good") quality = "Suspect";
            }

            // Clicks/pops: adaptive threshold, normalized per channel
            double popsPerSecond = durationSec > 0 ? popClicks / (double)channels / durationSec : 0;
            if (popsPerSecond > 10)
            {
                issues.Add($"clicks {popsPerSecond:F0}/s");
                quality = "Bad";
            }
            else if (popsPerSecond > 3)
            {
                issues.Add($"clicks {popsPerSecond:F1}/s");
                if (quality == "Good") quality = "Suspect";
            }

            // Bit truncation: only for 16-bit lossless, flag if > 60%
            if (totalFrames > 0 && info.BitsPerSample == 16 && IsLosslessFile(info))
            {
                double truncPct = (double)truncatedSamples / totalFrames * 100;
                if (truncPct > 60)
                {
                    issues.Add("bit truncation");
                    if (quality == "Good") quality = "Suspect";
                }
            }

            // DC offset: indicates bad ADC or ripping hardware
            float dcPct = Math.Abs(dcOffset) * 100;
            if (dcPct > 1.0f)
            {
                issues.Add($"DC offset {dcPct:F1}%");
                if (quality == "Good") quality = "Suspect";
            }

            // High noise floor in quiet sections
            float noiseDb = noiseFloorRms > 0 ? (float)(20.0 * Math.Log10(noiseFloorRms)) : -120f;
            if (noiseDb > -50f)
            {
                issues.Add($"high noise floor ({noiseDb:F0} dBFS)");
                if (quality == "Good") quality = "Suspect";
            }

            info.RipQuality = quality;
            info.RipQualityDetail = issues.Count > 0 ? string.Join(", ", issues) : "Clean";
            info.HasRipQuality = true;
        }

        private static string FormatDuration(TimeSpan ts)
        {
            if (ts.TotalHours >= 1)
                return $"{(int)ts.TotalHours}:{ts.Minutes:D2}:{ts.Seconds:D2}";
            return $"{ts.Minutes}:{ts.Seconds:D2}";
        }
    }
}
