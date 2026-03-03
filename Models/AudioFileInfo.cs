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

        // Clipping detection
        public bool HasClipping { get; set; }
        public double ClippingPercentage { get; set; }
        public long ClippingSamples { get; set; }

        // BPM, Replay Gain, Frequency
        public int Bpm { get; set; }
        public double ReplayGain { get; set; }
        public bool HasReplayGain { get; set; }
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

        // Album cover
        public bool HasAlbumCover { get; set; }

        // Display properties
        public string SampleRateDisplay => SampleRate > 0 ? $"{SampleRate:N0} Hz" : "-";
        public string BitsPerSampleDisplay => BitsPerSample > 0 ? $"{BitsPerSample}-bit" : "-";
        public string ReportedBitrateDisplay => ReportedBitrate > 0 ? $"{ReportedBitrate} kbps" : "-";
        public string ActualBitrateDisplay => ActualBitrate > 0 ? $"{ActualBitrate} kbps" : "-";
        public string EffectiveFrequencyDisplay => EffectiveFrequency > 0 ? $"{EffectiveFrequency:N0} Hz" : "-";
        public string ChannelsDisplay => Channels > 0 ? (Channels == 1 ? "Mono" : Channels == 2 ? "Stereo" : $"{Channels}ch") : "-";
        public string ClippingDisplay => HasClipping ? $"YES ({ClippingPercentage:F2}%)" : "No";
        public string BpmDisplay => Bpm > 0 ? $"{Bpm}" : "-";
        public string ReplayGainDisplay => HasReplayGain ? $"{ReplayGain:+0.00;-0.00;0.00} dB" : "-";
        public string FrequencyDisplay => Frequency > 0 ? $"{Frequency:N0} Hz" : "-";
        public string MqaDisplay => IsMqa ? (IsMqaStudio ? $"MQA Studio ({MqaOriginalSampleRate})" : $"MQA ({MqaOriginalSampleRate})") : "No";
        public string AiDisplay => IsAiGenerated ? AiSource : "No";

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
