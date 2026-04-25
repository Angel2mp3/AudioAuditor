using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using AudioQualityChecker.Models;

namespace AudioQualityChecker.Services
{
    public static class ScanCacheService
    {
        private static readonly string CacheDir =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AudioAuditor");
        private static readonly string CacheFile = Path.Combine(CacheDir, "scan_cache.json");

        private static ConcurrentDictionary<string, CacheEntry> _cache = new(StringComparer.OrdinalIgnoreCase);
        private static bool _loaded;
        private static bool _dirty;
        private static bool _cacheSkipped;

        private const long MaxCacheSizeBytes = 100L * 1024 * 1024; // 100 MB — skip loading above this
        private const int MaxCacheEntries = 50_000;                 // cap in-memory entries

        public static int EntryCount => _cache.Count;
        public static bool CacheSkipped => _cacheSkipped;

        public static void EnsureLoaded()
        {
            if (_loaded) return;
            _loaded = true;
            try
            {
                if (!File.Exists(CacheFile)) return;
                long fileSize = new FileInfo(CacheFile).Length;
                if (fileSize > MaxCacheSizeBytes)
                {
                    _cacheSkipped = true;
                    return;
                }
                var json = File.ReadAllText(CacheFile);
                var entries = JsonSerializer.Deserialize<List<CacheEntry>>(json);
                if (entries == null) return;
                // Keep only the last MaxCacheEntries to avoid unbounded memory use
                foreach (var e in entries.TakeLast(MaxCacheEntries))
                    if (!string.IsNullOrEmpty(e.FilePath))
                        _cache[e.FilePath] = e;
            }
            catch { }
        }

        public static bool TryGet(string filePath, long fileSizeBytes, DateTime lastWriteUtc, out AudioFileInfo? result)
        {
            result = null;
            if (!_cache.TryGetValue(filePath, out var entry)) return false;
            if (entry.FileSizeBytes != fileSizeBytes ||
                Math.Abs((entry.LastWriteUtc - lastWriteUtc).TotalSeconds) > 2)
            {
                _cache.TryRemove(filePath, out _);
                _dirty = true;
                return false;
            }
            result = entry.ToAudioFileInfo();
            return true;
        }

        public static void Set(AudioFileInfo info)
        {
            if (string.IsNullOrEmpty(info.FilePath)) return;
            try
            {
                var fi = new FileInfo(info.FilePath);
                if (!fi.Exists) return;
                _cache[info.FilePath] = CacheEntry.FromAudioFileInfo(info, fi.Length, fi.LastWriteTimeUtc);
                _dirty = true;
            }
            catch { }
        }

        public static void SaveToDisk()
        {
            if (!_dirty) return;
            try
            {
                if (!Directory.Exists(CacheDir))
                    Directory.CreateDirectory(CacheDir);
                var options = new JsonSerializerOptions { WriteIndented = false, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault };
                var json = JsonSerializer.Serialize(_cache.Values.ToList(), options);
                File.WriteAllText(CacheFile, json);
                _dirty = false;
            }
            catch { }
        }

        public static void Clear()
        {
            _cache.Clear();
            _dirty = false;
            _loaded = false;
            _cacheSkipped = false;
            try { if (File.Exists(CacheFile)) File.Delete(CacheFile); } catch { }
        }

        public static long GetCacheSizeBytes()
        {
            try { return File.Exists(CacheFile) ? new FileInfo(CacheFile).Length : 0; }
            catch { return 0; }
        }

        private class CacheEntry
        {
            public string FilePath { get; set; } = "";
            public long FileSizeBytes { get; set; }
            public DateTime LastWriteUtc { get; set; }

            // Core analysis results
            public int Status { get; set; }
            public string Artist { get; set; } = "";
            public string Title { get; set; } = "";
            public string FileName { get; set; } = "";
            public string FolderPath { get; set; } = "";
            public int SampleRate { get; set; }
            public int BitsPerSample { get; set; }
            public string Duration { get; set; } = "";
            public double DurationSeconds { get; set; }
            public string FileSize { get; set; } = "";
            public int ReportedBitrate { get; set; }
            public int ActualBitrate { get; set; }
            public string Extension { get; set; } = "";
            public int EffectiveFrequency { get; set; }
            public int Channels { get; set; }
            public DateTime DateModified { get; set; }
            public DateTime DateCreated { get; set; }

            // Clipping
            public bool HasClipping { get; set; }
            public double ClippingPercentage { get; set; }
            public long ClippingSamples { get; set; }
            public double MaxSampleLevel { get; set; }
            public double MaxSampleLevelDb { get; set; }
            public bool HasScaledClipping { get; set; }
            public double ScaledClippingPercentage { get; set; }

            // BPM / Replay Gain
            public int Bpm { get; set; }
            public double ReplayGain { get; set; }
            public bool HasReplayGain { get; set; }
            public int Frequency { get; set; }

            // MQA
            public bool IsMqa { get; set; }
            public bool IsMqaStudio { get; set; }
            public string MqaOriginalSampleRate { get; set; } = "";
            public string MqaEncoder { get; set; } = "";

            // AI detection
            public bool IsAiGenerated { get; set; }
            public string AiSource { get; set; } = "";
            public List<string> AiSources { get; set; } = new();
            public bool ExperimentalAiSuspicious { get; set; }
            public double ExperimentalAiConfidence { get; set; }
            public List<string> ExperimentalAiFlags { get; set; } = new();
            public bool SHLabsScanned { get; set; }
            public string SHLabsPrediction { get; set; } = "";
            public double SHLabsProbability { get; set; }
            public double SHLabsConfidence { get; set; }
            public string SHLabsAiType { get; set; } = "";

            // Other
            public bool HasAlbumCover { get; set; }
            public bool IsAlac { get; set; }
            public string ErrorMessage { get; set; } = "";

            // Silence
            public double LeadingSilenceMs { get; set; }
            public double TrailingSilenceMs { get; set; }
            public int MidTrackSilenceGaps { get; set; }
            public double TotalMidSilenceMs { get; set; }
            public bool HasExcessiveSilence { get; set; }

            // Dynamic Range
            public double DynamicRange { get; set; }
            public bool HasDynamicRange { get; set; }

            // Fake Stereo
            public bool IsFakeStereo { get; set; }
            public string FakeStereoType { get; set; } = "";
            public double StereoCorrelation { get; set; }

            // True Peak / LUFS
            public double TruePeakDbTP { get; set; }
            public bool HasTruePeak { get; set; }
            public double IntegratedLufs { get; set; }
            public bool HasLufs { get; set; }

            // Rip Quality
            public string RipQuality { get; set; } = "";
            public string RipQualityDetail { get; set; } = "";
            public bool HasRipQuality { get; set; }

            public AudioFileInfo ToAudioFileInfo()
            {
                return new AudioFileInfo
                {
                    Status = (AudioStatus)Status,
                    Artist = Artist,
                    Title = Title,
                    FileName = FileName,
                    FilePath = FilePath,
                    FolderPath = FolderPath,
                    SampleRate = SampleRate,
                    BitsPerSample = BitsPerSample,
                    Duration = Duration,
                    DurationSeconds = DurationSeconds,
                    FileSize = FileSize,
                    FileSizeBytes = FileSizeBytes,
                    ReportedBitrate = ReportedBitrate,
                    ActualBitrate = ActualBitrate,
                    Extension = Extension,
                    EffectiveFrequency = EffectiveFrequency,
                    Channels = Channels,
                    DateModified = DateModified,
                    DateCreated = DateCreated,
                    HasClipping = HasClipping,
                    ClippingPercentage = ClippingPercentage,
                    ClippingSamples = ClippingSamples,
                    MaxSampleLevel = MaxSampleLevel,
                    MaxSampleLevelDb = MaxSampleLevelDb,
                    HasScaledClipping = HasScaledClipping,
                    ScaledClippingPercentage = ScaledClippingPercentage,
                    Bpm = Bpm,
                    ReplayGain = ReplayGain,
                    HasReplayGain = HasReplayGain,
                    Frequency = Frequency,
                    IsMqa = IsMqa,
                    IsMqaStudio = IsMqaStudio,
                    MqaOriginalSampleRate = MqaOriginalSampleRate,
                    MqaEncoder = MqaEncoder,
                    IsAiGenerated = IsAiGenerated,
                    AiSource = AiSource,
                    AiSources = AiSources ?? new(),
                    ExperimentalAiSuspicious = ExperimentalAiSuspicious,
                    ExperimentalAiConfidence = ExperimentalAiConfidence,
                    ExperimentalAiFlags = ExperimentalAiFlags ?? new(),
                    SHLabsScanned = SHLabsScanned,
                    SHLabsPrediction = SHLabsPrediction,
                    SHLabsProbability = SHLabsProbability,
                    SHLabsConfidence = SHLabsConfidence,
                    SHLabsAiType = SHLabsAiType,
                    HasAlbumCover = HasAlbumCover,
                    IsAlac = IsAlac,
                    ErrorMessage = ErrorMessage,
                    LeadingSilenceMs = LeadingSilenceMs,
                    TrailingSilenceMs = TrailingSilenceMs,
                    MidTrackSilenceGaps = MidTrackSilenceGaps,
                    TotalMidSilenceMs = TotalMidSilenceMs,
                    HasExcessiveSilence = HasExcessiveSilence,
                    DynamicRange = DynamicRange,
                    HasDynamicRange = HasDynamicRange,
                    IsFakeStereo = IsFakeStereo,
                    FakeStereoType = FakeStereoType,
                    StereoCorrelation = StereoCorrelation,
                    TruePeakDbTP = TruePeakDbTP,
                    HasTruePeak = HasTruePeak,
                    IntegratedLufs = IntegratedLufs,
                    HasLufs = HasLufs,
                    RipQuality = RipQuality,
                    RipQualityDetail = RipQualityDetail,
                    HasRipQuality = HasRipQuality,
                };
            }

            public static CacheEntry FromAudioFileInfo(AudioFileInfo info, long sizeBytes, DateTime lastWriteUtc)
            {
                return new CacheEntry
                {
                    FilePath = info.FilePath,
                    FileSizeBytes = sizeBytes,
                    LastWriteUtc = lastWriteUtc,
                    Status = (int)info.Status,
                    Artist = info.Artist,
                    Title = info.Title,
                    FileName = info.FileName,
                    FolderPath = info.FolderPath,
                    SampleRate = info.SampleRate,
                    BitsPerSample = info.BitsPerSample,
                    Duration = info.Duration,
                    DurationSeconds = info.DurationSeconds,
                    FileSize = info.FileSize,
                    ReportedBitrate = info.ReportedBitrate,
                    ActualBitrate = info.ActualBitrate,
                    Extension = info.Extension,
                    EffectiveFrequency = info.EffectiveFrequency,
                    Channels = info.Channels,
                    DateModified = info.DateModified,
                    DateCreated = info.DateCreated,
                    HasClipping = info.HasClipping,
                    ClippingPercentage = info.ClippingPercentage,
                    ClippingSamples = info.ClippingSamples,
                    MaxSampleLevel = info.MaxSampleLevel,
                    MaxSampleLevelDb = info.MaxSampleLevelDb,
                    HasScaledClipping = info.HasScaledClipping,
                    ScaledClippingPercentage = info.ScaledClippingPercentage,
                    Bpm = info.Bpm,
                    ReplayGain = info.ReplayGain,
                    HasReplayGain = info.HasReplayGain,
                    Frequency = info.Frequency,
                    IsMqa = info.IsMqa,
                    IsMqaStudio = info.IsMqaStudio,
                    MqaOriginalSampleRate = info.MqaOriginalSampleRate,
                    MqaEncoder = info.MqaEncoder,
                    IsAiGenerated = info.IsAiGenerated,
                    AiSource = info.AiSource,
                    AiSources = info.AiSources,
                    ExperimentalAiSuspicious = info.ExperimentalAiSuspicious,
                    ExperimentalAiConfidence = info.ExperimentalAiConfidence,
                    ExperimentalAiFlags = info.ExperimentalAiFlags,
                    SHLabsScanned = info.SHLabsScanned,
                    SHLabsPrediction = info.SHLabsPrediction,
                    SHLabsProbability = info.SHLabsProbability,
                    SHLabsConfidence = info.SHLabsConfidence,
                    SHLabsAiType = info.SHLabsAiType,
                    HasAlbumCover = info.HasAlbumCover,
                    IsAlac = info.IsAlac,
                    ErrorMessage = info.ErrorMessage,
                    LeadingSilenceMs = info.LeadingSilenceMs,
                    TrailingSilenceMs = info.TrailingSilenceMs,
                    MidTrackSilenceGaps = info.MidTrackSilenceGaps,
                    TotalMidSilenceMs = info.TotalMidSilenceMs,
                    HasExcessiveSilence = info.HasExcessiveSilence,
                    DynamicRange = info.DynamicRange,
                    HasDynamicRange = info.HasDynamicRange,
                    IsFakeStereo = info.IsFakeStereo,
                    FakeStereoType = info.FakeStereoType,
                    StereoCorrelation = info.StereoCorrelation,
                    TruePeakDbTP = info.TruePeakDbTP,
                    HasTruePeak = info.HasTruePeak,
                    IntegratedLufs = info.IntegratedLufs,
                    HasLufs = info.HasLufs,
                    RipQuality = info.RipQuality,
                    RipQualityDetail = info.RipQualityDetail,
                    HasRipQuality = info.HasRipQuality,
                };
            }
        }
    }
}
