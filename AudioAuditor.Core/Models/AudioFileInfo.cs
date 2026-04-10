using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace AudioQualityChecker.Models
{
    public enum AudioStatus
    {
        Analyzing,
        Valid,
        Fake,
        Unknown,
        Corrupt,
        Optimized
    }

    public class AudioFileInfo : INotifyPropertyChanged
    {
        private AudioStatus _status = AudioStatus.Analyzing;

        public AudioStatus Status
        {
            get => _status;
            set { _status = value; OnPropertyChanged(); }
        }

        public string Artist { get; set; } = "";
        public string Title { get; set; } = "";
        public string FileName { get; set; } = "";
        public string FilePath { get; set; } = "";
        public string FolderPath { get; set; } = "";
        public int SampleRate { get; set; }
        public int BitsPerSample { get; set; }
        public string Duration { get; set; } = "";
        public double DurationSeconds { get; set; }
        public string FileSize { get; set; } = "";
        public long FileSizeBytes { get; set; }
        public int ReportedBitrate { get; set; }
        public int ActualBitrate { get; set; }
        public string Extension { get; set; } = "";
        public int EffectiveFrequency { get; set; }
        public int Channels { get; set; }

        // File dates
        public DateTime DateModified { get; set; }
        public DateTime DateCreated { get; set; }

        // Clipping detection
        public bool HasClipping { get; set; }
        public double ClippingPercentage { get; set; }
        public long ClippingSamples { get; set; }
        public double MaxSampleLevel { get; set; } // 0.0 to 1.0 peak
        public double MaxSampleLevelDb { get; set; } // peak in dB
        public bool HasScaledClipping { get; set; } // clipping at reduced level
        public double ScaledClippingPercentage { get; set; }

        // BPM, Replay Gain, Frequency
        public int Bpm { get; set; }

        private double _replayGain;
        public double ReplayGain
        {
            get => _replayGain;
            set { _replayGain = value; OnPropertyChanged(); OnPropertyChanged(nameof(ReplayGainDisplay)); }
        }

        private bool _hasReplayGain;
        public bool HasReplayGain
        {
            get => _hasReplayGain;
            set { _hasReplayGain = value; OnPropertyChanged(); OnPropertyChanged(nameof(ReplayGainDisplay)); }
        }

        public int Frequency { get; set; } // dominant/fundamental frequency

        // Error info for corrupt files
        public string ErrorMessage { get; set; } = "";

        // MQA detection
        public bool IsMqa { get; set; }
        public bool IsMqaStudio { get; set; }
        public string MqaOriginalSampleRate { get; set; } = "";
        public string MqaEncoder { get; set; } = "";

        // AI detection
        public bool IsAiGenerated { get; set; }
        public string AiSource { get; set; } = "";
        public List<string> AiSources { get; set; } = new();

        // Experimental AI detection (spectral analysis)
        public bool ExperimentalAiSuspicious { get; set; }
        public double ExperimentalAiConfidence { get; set; }
        public List<string> ExperimentalAiFlags { get; set; } = new();

        // SH Labs AI detection (API-based)
        public bool SHLabsScanned { get; set; }
        public string SHLabsPrediction { get; set; } = ""; // "Human Made", "Pure AI", "Processed AI"
        public double SHLabsProbability { get; set; }       // 0–100
        public double SHLabsConfidence { get; set; }        // 0–100
        public string SHLabsAiType { get; set; } = "";

        // Album cover
        public bool HasAlbumCover { get; set; }

        // ALAC codec detected inside M4A container
        public bool IsAlac { get; set; }

        // Silence detection
        public double LeadingSilenceMs { get; set; }
        public double TrailingSilenceMs { get; set; }
        public int MidTrackSilenceGaps { get; set; } // number of gaps ≥ 500ms
        public double TotalMidSilenceMs { get; set; }
        public bool HasExcessiveSilence { get; set; }

        // Dynamic Range
        public double DynamicRange { get; set; } // DR score (dB), 0 = not calculated
        public bool HasDynamicRange { get; set; }

        // Fake stereo detection
        public bool IsFakeStereo { get; set; }
        public string FakeStereoType { get; set; } = ""; // "Mono Duplicate", "Artificially Widened", ""
        public double StereoCorrelation { get; set; } // 0.0–1.0 (1.0 = identical channels)

        // Cue sheet virtual track
        public bool IsCueVirtualTrack { get; set; }
        public string CueSheetPath { get; set; } = "";
        public int CueTrackNumber { get; set; }
        public TimeSpan CueStartTime { get; set; }
        public TimeSpan CueEndTime { get; set; } // Zero = end of file

        // True Peak (inter-sample, dBTP)
        public double TruePeakDbTP { get; set; } // e.g. -0.3 dBTP
        public bool HasTruePeak { get; set; }

        // Integrated LUFS (EBU R128)
        public double IntegratedLufs { get; set; } // e.g. -14.0 LUFS
        public bool HasLufs { get; set; }

        // Rip/Encode Quality
        public string RipQuality { get; set; } = ""; // "Good", "Suspect", "Bad"
        public string RipQualityDetail { get; set; } = ""; // description of issues found
        public bool HasRipQuality { get; set; }

        // Display properties
        public string DateModifiedDisplay => DateModified != default ? DateModified.ToString("yyyy-MM-dd HH:mm") : "-";
        public string DateCreatedDisplay => DateCreated != default ? DateCreated.ToString("yyyy-MM-dd HH:mm") : "-";
        public string FormatDisplay => IsAlac ? $"{Extension} (ALAC)" : Extension;
        public string SampleRateDisplay => SampleRate > 0 ? $"{SampleRate:N0} Hz" : "-";
        public string BitsPerSampleDisplay => BitsPerSample > 0 ? $"{BitsPerSample}-bit" : "-";
        public string ReportedBitrateDisplay => ReportedBitrate > 0 ? $"{ReportedBitrate} kbps" : "-";
        public string ActualBitrateDisplay => ActualBitrate > 0 ? $"{ActualBitrate} kbps" : "-";
        public string EffectiveFrequencyDisplay => EffectiveFrequency > 0 ? $"{EffectiveFrequency:N0} Hz" : "-";
        public string ChannelsDisplay => Channels > 0 ? (Channels == 1 ? "Mono" : Channels == 2 ? "Stereo" : $"{Channels}ch") : "-";
        public string ClippingDisplay
        {
            get
            {
                if (HasClipping) return $"YES ({ClippingPercentage:F2}%)";
                if (HasScaledClipping) return $"SCALED ({MaxSampleLevelDb:F1} dB, {ScaledClippingPercentage:F2}%)";
                return "No";
            }
        }
        public string BpmDisplay => Bpm > 0 ? $"{Bpm}" : "-";
        public string ReplayGainDisplay => HasReplayGain ? $"{ReplayGain:+0.00;-0.00;0.00} dB" : "-";
        public string FrequencyDisplay => Frequency > 0 ? $"{Frequency:N0} Hz" : "-";
        public string MqaDisplay => IsMqa ? (IsMqaStudio ? $"MQA Studio ({MqaOriginalSampleRate})" : $"MQA ({MqaOriginalSampleRate})") : "No";
        public string SilenceDisplay
        {
            get
            {
                if (!HasExcessiveSilence && LeadingSilenceMs < 1000 && TrailingSilenceMs < 1000 && MidTrackSilenceGaps == 0)
                    return "OK";
                var parts = new List<string>();
                if (LeadingSilenceMs >= 1000) parts.Add($"Lead: {FormatMs(LeadingSilenceMs)}");
                if (TrailingSilenceMs >= 1000) parts.Add($"Trail: {FormatMs(TrailingSilenceMs)}");
                if (MidTrackSilenceGaps > 0) parts.Add($"{MidTrackSilenceGaps} gap{(MidTrackSilenceGaps > 1 ? "s" : "")} ({FormatMs(TotalMidSilenceMs)})");
                return parts.Count > 0 ? string.Join(" | ", parts) : "OK";
            }
        }
        public string FakeStereoDisplay => IsFakeStereo ? FakeStereoType : "No";
        public string DynamicRangeDisplay => HasDynamicRange ? $"DR-{DynamicRange:F0}" : "-";
        public string TruePeakDisplay => HasTruePeak ? $"{TruePeakDbTP:F1} dBTP" : "-";
        public string LufsDisplay => HasLufs ? $"{IntegratedLufs:F1} LUFS" : "-";
        public string RipQualityDisplay => HasRipQuality ? (string.IsNullOrEmpty(RipQualityDetail) ? RipQuality : $"{RipQuality}: {RipQualityDetail}") : "-";

        private static string FormatMs(double ms)
        {
            if (ms >= 60000) return $"{ms / 60000:F1}m";
            if (ms >= 1000) return $"{ms / 1000:F1}s";
            return $"{(int)ms}ms";
        }
        public string AiDisplay
        {
            get
            {
                var parts = new List<string>();
                if (IsAiGenerated)
                    parts.Add(AiSource);
                if (ExperimentalAiSuspicious)
                    parts.Add($"Spectral ({ExperimentalAiConfidence:P0})");
                if (SHLabsScanned && SHLabsPrediction != "Human Made")
                {
                    string label = !string.IsNullOrEmpty(SHLabsAiType)
                        ? $"SH Labs: {SHLabsPrediction} — {SHLabsAiType} ({SHLabsProbability:F0}%)"
                        : $"SH Labs: {SHLabsPrediction} ({SHLabsProbability:F0}%)";
                    parts.Add(label);
                }
                else if (SHLabsScanned)
                {
                    parts.Add("SH Labs: Human Made");
                }
                return parts.Count > 0 ? string.Join(" + ", parts) : "No";
            }
        }

        /// <summary>True when ANY AI detection model flags this file (standard, experimental, or SH Labs).</summary>
        public bool IsAnyAiDetected =>
            IsAiGenerated
            || ExperimentalAiSuspicious
            || (SHLabsScanned && SHLabsPrediction != "Human Made");

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
