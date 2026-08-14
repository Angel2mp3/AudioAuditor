using System;
using System.Collections.Generic;
using AudioQualityChecker.Models;

namespace AudioQualityChecker.Services
{
    /// <summary>
    /// Maps a column/feature name to the <see cref="AudioFileInfo"/> properties that analysis
    /// feature produces.
    ///
    /// Used when a detector is switched on after files are already loaded: re-analyse, then copy
    /// back only these fields so the newly enabled column fills in without disturbing values the
    /// user is already looking at.
    /// </summary>
    public static class AnalysisFeatureFields
    {
        /// <summary>
        /// Rip Log is not produced by <c>AudioAnalyzer.AnalyzeFile</c> — it comes from a
        /// per-folder cambia run — so callers give it its own backfill path.
        /// </summary>
        public const string RipLog = "Rip Log";

        private static readonly Dictionary<string, string[]> Map = new(StringComparer.OrdinalIgnoreCase)
        {
            ["BPM"] = new[] { nameof(AudioFileInfo.Bpm) },
            ["DR"] = new[] { nameof(AudioFileInfo.DynamicRange), nameof(AudioFileInfo.HasDynamicRange) },
            ["True Peak"] = new[] { nameof(AudioFileInfo.TruePeakDbTP), nameof(AudioFileInfo.HasTruePeak) },
            ["LUFS"] = new[] { nameof(AudioFileInfo.IntegratedLufs), nameof(AudioFileInfo.HasLufs) },
            [RipLog] = new[] { nameof(AudioFileInfo.RipLogScore), nameof(AudioFileInfo.RipLogVerdict), nameof(AudioFileInfo.HasRipLog) },
            ["Silence"] = new[] { nameof(AudioFileInfo.LeadingSilenceMs), nameof(AudioFileInfo.TrailingSilenceMs), nameof(AudioFileInfo.MidTrackSilenceGaps), nameof(AudioFileInfo.TotalMidSilenceMs), nameof(AudioFileInfo.HasExcessiveSilence) },
            ["Clipping"] = new[] { nameof(AudioFileInfo.HasClipping), nameof(AudioFileInfo.ClippingPercentage), nameof(AudioFileInfo.ClippingSamples), nameof(AudioFileInfo.MaxSampleLevel), nameof(AudioFileInfo.MaxSampleLevelDb), nameof(AudioFileInfo.HasScaledClipping), nameof(AudioFileInfo.ScaledClippingPercentage) },
            ["MQA"] = new[] { nameof(AudioFileInfo.IsMqa), nameof(AudioFileInfo.IsMqaStudio), nameof(AudioFileInfo.MqaOriginalSampleRate), nameof(AudioFileInfo.MqaEncoder) },
            ["AI"] = new[] { nameof(AudioFileInfo.IsAiGenerated), nameof(AudioFileInfo.AiSource), nameof(AudioFileInfo.AiSources), nameof(AudioFileInfo.AiConfidence), nameof(AudioFileInfo.ExperimentalAiSuspicious), nameof(AudioFileInfo.ExperimentalAiConfidence), nameof(AudioFileInfo.ExperimentalAiFlags) },
            ["Fake Stereo"] = new[] { nameof(AudioFileInfo.IsFakeStereo), nameof(AudioFileInfo.FakeStereoType), nameof(AudioFileInfo.StereoCorrelation) },
        };

        /// <summary>
        /// The union of properties owned by <paramref name="featureHeaders"/>. Unknown headers
        /// contribute nothing, so an empty result means there is nothing to re-analyse for.
        /// </summary>
        public static HashSet<string> For(IEnumerable<string> featureHeaders)
        {
            var fields = new HashSet<string>(StringComparer.Ordinal);

            foreach (var header in featureHeaders)
                if (Map.TryGetValue(header, out var owned))
                    foreach (var name in owned)
                        fields.Add(name);

            return fields;
        }
    }
}
