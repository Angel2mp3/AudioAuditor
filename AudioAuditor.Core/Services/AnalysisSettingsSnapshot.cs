using AudioQualityChecker.Abstractions;
using System.Globalization;

namespace AudioQualityChecker.Services
{
    public sealed record AnalysisSettingsSnapshot : IAnalysisSettings
    {
        public bool EnableBpmDetection { get; init; }
        public bool EnableExperimentalAi { get; init; }
        public bool EnableSilenceDetection { get; init; }
        public bool EnableFakeStereoDetection { get; init; }
        public bool EnableDynamicRange { get; init; }
        public bool EnableTruePeak { get; init; }
        public bool EnableLufs { get; init; }
        public bool EnableClippingDetection { get; init; }
        public bool EnableMqaDetection { get; init; }
        public bool EnableDefaultAiDetection { get; init; }
        public bool AlwaysFullAnalysis { get; init; }
        public bool FrequencyCutoffAllowEnabled { get; init; }
        public int FrequencyCutoffAllowHz { get; init; }
        public SilenceSettings Silence { get; init; } = new(false, 0.5, false, 5.0);
        public string CacheFingerprint => CreateCacheFingerprint();

        public static AnalysisSettingsSnapshot From(IAnalysisSettings settings)
        {
            return new AnalysisSettingsSnapshot
            {
                EnableBpmDetection = settings.EnableBpmDetection,
                EnableExperimentalAi = settings.EnableExperimentalAi,
                EnableSilenceDetection = settings.EnableSilenceDetection,
                EnableFakeStereoDetection = settings.EnableFakeStereoDetection,
                EnableDynamicRange = settings.EnableDynamicRange,
                EnableTruePeak = settings.EnableTruePeak,
                EnableLufs = settings.EnableLufs,
                EnableClippingDetection = settings.EnableClippingDetection,
                EnableMqaDetection = settings.EnableMqaDetection,
                EnableDefaultAiDetection = settings.EnableDefaultAiDetection,
                AlwaysFullAnalysis = settings.AlwaysFullAnalysis,
                FrequencyCutoffAllowEnabled = settings.FrequencyCutoffAllowEnabled,
                FrequencyCutoffAllowHz = settings.FrequencyCutoffAllowHz,
                Silence = settings.Silence
            };
        }

        private string CreateCacheFingerprint()
        {
            static string Bool(bool value) => value ? "1" : "0";
            static string Number(double value) => value.ToString("R", CultureInfo.InvariantCulture);

            return string.Join("|",
                // v6: the analysis pass changed in four ways that all move verdicts, so nothing
                // cached under v5 can be trusted. Lossless verdicts now rest on the measured
                // steepness of the spectral edge rather than cutoff frequency alone (a 320 kbps
                // transcode in a lossless container used to read as Valid); the cutoff scan gained
                // a wall-detection fallback; the spectrum is taken per channel instead of from an
                // L+R sum; and the cutoff curves are chosen by codec family rather than by file
                // extension. Dynamic Range block selection changed in the same pass.
                // v5: BS.1770 channel weighting corrected for >2ch (LFE was weighted 1.41 and
                // excluded the right surround), so every cached 5.1 Integrated LUFS is wrong.
                // v4: AI scoring reworked (real marker confidence is now stored and combined with
                // noisy-OR instead of being re-derived from the marker count). Entries cached under
                // v3 have no AiConfidence, so they must be recomputed rather than trusted.
                "analysis-settings-v6",
                $"clip={Bool(EnableClippingDetection)}",
                $"mqa={Bool(EnableMqaDetection)}",
                $"defaultAi={Bool(EnableDefaultAiDetection)}",
                $"experimentalAi={Bool(EnableExperimentalAi)}",
                $"fakeStereo={Bool(EnableFakeStereoDetection)}",
                $"bpm={Bool(EnableBpmDetection)}",
                $"silence={Bool(EnableSilenceDetection)}",
                $"silenceMinGap={Bool(Silence.MinGapEnabled)}",
                $"silenceMinGapSeconds={Number(Silence.MinGapSeconds)}",
                $"silenceSkipEdges={Bool(Silence.SkipEdgesEnabled)}",
                $"silenceSkipEdgeSeconds={Number(Silence.SkipEdgeSeconds)}",
                $"dynamicRange={Bool(EnableDynamicRange)}",
                $"truePeak={Bool(EnableTruePeak)}",
                $"lufs={Bool(EnableLufs)}",
                $"alwaysFull={Bool(AlwaysFullAnalysis)}",
                $"cutoffAllow={Bool(FrequencyCutoffAllowEnabled)}",
                $"cutoffAllowHz={FrequencyCutoffAllowHz}",
                // Not a setting, but it changes what can be analyzed at all: ffmpeg is the decoder
                // for AAC/M4A, ALAC, WMA, APE, WavPack and friends. Without this, someone who
                // scanned before installing ffmpeg would keep being served the cached
                // "no decoder available" verdicts afterwards and conclude the install did nothing.
                $"ffmpeg={Bool(FfmpegDecoder.IsAvailable)}");
        }
    }
}
