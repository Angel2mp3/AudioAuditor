using AudioQualityChecker.Abstractions;
using System.Globalization;

namespace AudioQualityChecker.Services
{
    public sealed record AnalysisSettingsSnapshot : IAnalysisSettings
    {
        public bool EnableBpmDetection { get; init; }
        public bool EnableExperimentalAi { get; init; }
        public bool EnableRipQuality { get; init; }
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
                EnableRipQuality = settings.EnableRipQuality,
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
                "analysis-settings-v2",
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
                $"ripQuality={Bool(EnableRipQuality)}",
                $"alwaysFull={Bool(AlwaysFullAnalysis)}",
                $"cutoffAllow={Bool(FrequencyCutoffAllowEnabled)}",
                $"cutoffAllowHz={FrequencyCutoffAllowHz}");
        }
    }
}
