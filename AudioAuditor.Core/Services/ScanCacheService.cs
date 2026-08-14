using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using AudioQualityChecker.Abstractions;
using AudioQualityChecker.Models;

namespace AudioQualityChecker.Services
{
    public static class ScanCacheService
    {
        private static readonly string CacheDir = AppPaths.AppDataDirectory;
        private static readonly string CacheFile = Path.Combine(CacheDir, "scan_cache.json.gz");

        // Pre-compression filename. Read once so an upgrading user keeps their cache, then deleted
        // after the first successful gzipped write. Losing it would only cost one rescan, but there
        // is no reason to make anyone pay that.
        private static readonly string LegacyCacheFile = Path.Combine(CacheDir, "scan_cache.json");

        private static ConcurrentDictionary<string, CacheEntry> _cache = new(StringComparer.OrdinalIgnoreCase);
        private static bool _loaded;
        private static bool _dirty;
        private static bool _cacheSkipped;

        // Guards against spending minutes parsing a runaway cache at startup. The compressed file
        // is checked against a smaller bound than the JSON it expands to, because gzip takes ~90%
        // off this data — hence two limits rather than one shared number.
        private const long MaxCacheSizeBytes = 100L * 1024 * 1024;         // plain JSON on disk
        private const long MaxCompressedCacheSizeBytes = 20L * 1024 * 1024; // ~200 MB decompressed
        private const long MaxDecompressedCacheBytes = 250L * 1024 * 1024;  // hard ceiling, see below
        private const int MaxCacheEntries = 50_000;                         // cap in-memory entries

        public static int EntryCount => _cache.Count;
        public static bool CacheSkipped => _cacheSkipped;

        public static void EnsureLoaded()
        {
            if (_loaded) return;
            _loaded = true;
            try
            {
                string source;
                if (File.Exists(CacheFile)) source = CacheFile;
                else if (File.Exists(LegacyCacheFile)) source = LegacyCacheFile;
                else return;

                if (IsTooLargeToLoad(source))
                {
                    _cacheSkipped = true;
                    return;
                }

                var entries = CompressedJsonStore.Load<List<CacheEntry>>(source);
                if (entries == null) return;
                // Keep only the last MaxCacheEntries to avoid unbounded memory use
                foreach (var e in entries.TakeLast(MaxCacheEntries))
                    if (!string.IsNullOrEmpty(e.FilePath))
                        _cache[e.FilePath] = e;
            }
            catch { }
        }

        /// <summary>
        /// True when <paramref name="path"/> is big enough that parsing it would stall startup.
        /// A gzipped cache is judged on both its compressed size and the uncompressed size recorded
        /// in its trailer, so a small file that expands to gigabytes (corrupt or hostile) is
        /// rejected before a single byte is inflated.
        /// </summary>
        private static bool IsTooLargeToLoad(string path)
        {
            try
            {
                long onDisk = new FileInfo(path).Length;

                long expanded = CompressedJsonStore.GetUncompressedSize(path);
                if (expanded > 0)
                    return onDisk > MaxCompressedCacheSizeBytes || expanded > MaxDecompressedCacheBytes;

                return onDisk > MaxCacheSizeBytes;
            }
            catch { return false; }
        }

        public static bool TryGet(string filePath, long fileSizeBytes, DateTime lastWriteUtc, out AudioFileInfo? result)
        {
            return TryGet(filePath, fileSizeBytes, lastWriteUtc, settingsFingerprint: null, out result);
        }

        public static bool TryGet(string filePath, long fileSizeBytes, DateTime lastWriteUtc, IAnalysisSettings settings, out AudioFileInfo? result)
        {
            ArgumentNullException.ThrowIfNull(settings);
            var settingsFingerprint = AnalysisSettingsSnapshot.From(settings).CacheFingerprint;
            return TryGet(filePath, fileSizeBytes, lastWriteUtc, settingsFingerprint, out result);
        }

        /// <summary>
        /// Cache lookup with a pre-computed settings fingerprint. Prefer this in batch scans:
        /// <see cref="AnalysisSettingsSnapshot.CacheFingerprint"/> is a computed property that
        /// builds ~20 interpolated strings and a string.Join on every read, and the value is
        /// identical for every file in a batch — so the IAnalysisSettings overload rebuilt it
        /// once per file, twice (lookup and store).
        /// </summary>
        public static bool TryGet(string filePath, long fileSizeBytes, DateTime lastWriteUtc, string? settingsFingerprint, out AudioFileInfo? result)
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
            if (!string.IsNullOrEmpty(settingsFingerprint) &&
                !string.Equals(entry.SettingsFingerprint, settingsFingerprint, StringComparison.Ordinal))
            {
                return false;
            }
            result = entry.ToAudioFileInfo();
            return true;
        }

        public static void Set(AudioFileInfo info)
        {
            Set(info, settingsFingerprint: null);
        }

        public static void Set(AudioFileInfo info, IAnalysisSettings settings)
        {
            ArgumentNullException.ThrowIfNull(settings);
            var settingsFingerprint = AnalysisSettingsSnapshot.From(settings).CacheFingerprint;
            Set(info, settingsFingerprint);
        }

        /// <summary>Cache store with a pre-computed settings fingerprint — see the TryGet overload.</summary>
        public static void Set(AudioFileInfo info, string? settingsFingerprint)
        {
            Set(info, settingsFingerprint, fileSizeBytes: null, lastWriteUtc: null);
        }

        /// <summary>
        /// Cache store that reuses a stat the caller already took. The batch scanner stats every
        /// file for the cache lookup, so re-running FileInfo here doubled the syscalls per file.
        /// </summary>
        public static void Set(AudioFileInfo info, string? settingsFingerprint, long? fileSizeBytes, DateTime? lastWriteUtc)
        {
            if (string.IsNullOrEmpty(info.FilePath)) return;
            try
            {
                long size;
                DateTime written;
                if (fileSizeBytes.HasValue && lastWriteUtc.HasValue)
                {
                    size = fileSizeBytes.Value;
                    written = lastWriteUtc.Value;
                }
                else
                {
                    var fi = new FileInfo(info.FilePath);
                    if (!fi.Exists) return;
                    size = fi.Length;
                    written = fi.LastWriteTimeUtc;
                }
                _cache[info.FilePath] = CacheEntry.FromAudioFileInfo(info, size, written, settingsFingerprint);
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
                if (!CompressedJsonStore.Save(CacheFile, _cache.Values.ToList(), options))
                    return;
                _dirty = false;

                // Only now that the compressed file is safely on disk. This is pure cache — if an
                // older build is ever run again it just rescans — so there is nothing to preserve.
                try { if (File.Exists(LegacyCacheFile)) File.Delete(LegacyCacheFile); } catch { }
            }
            catch { }
        }

        /// <summary>
        /// Writes the cache to <paramref name="destPath"/> as readable, indented plain JSON.
        /// The cache file itself is gzipped and so can't be opened in a text editor — this backs
        /// the Settings "Edit Cache" action, paired with <see cref="ImportPlainJson"/>.
        /// </summary>
        public static bool ExportPlainJson(string destPath)
        {
            EnsureLoaded();
            try
            {
                var options = new JsonSerializerOptions
                {
                    WriteIndented = true,
                    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault
                };
                File.WriteAllText(destPath, JsonSerializer.Serialize(_cache.Values.ToList(), options));
                return true;
            }
            catch { return false; }
        }

        /// <summary>
        /// Replaces the cache with the contents of a plain-JSON file previously produced by
        /// <see cref="ExportPlainJson"/>, then persists it. A file that no longer parses is
        /// rejected outright rather than partially applied, so a bad hand-edit leaves the existing
        /// cache intact instead of shredding it.
        /// </summary>
        public static bool ImportPlainJson(string srcPath)
        {
            try
            {
                var entries = CompressedJsonStore.Load<List<CacheEntry>>(srcPath);
                if (entries == null) return false;

                var replacement = new ConcurrentDictionary<string, CacheEntry>(StringComparer.OrdinalIgnoreCase);
                foreach (var e in entries.TakeLast(MaxCacheEntries))
                    if (!string.IsNullOrEmpty(e.FilePath))
                        replacement[e.FilePath] = e;

                _cache = replacement;
                _loaded = true;
                _dirty = true;
                SaveToDisk();
                return true;
            }
            catch { return false; }
        }

        public static void Clear()
        {
            _cache.Clear();
            _dirty = false;
            _loaded = false;
            _cacheSkipped = false;
            try { if (File.Exists(CacheFile)) File.Delete(CacheFile); } catch { }
            try { if (File.Exists(LegacyCacheFile)) File.Delete(LegacyCacheFile); } catch { }
        }

        /// <summary>
        /// On-disk size of the cache. Counts both files: mid-migration (loaded from the legacy file
        /// but not yet saved) they coexist, and reporting only one would understate what Settings
        /// offers to free.
        /// </summary>
        public static long GetCacheSizeBytes()
        {
            long total = 0;
            foreach (var path in new[] { CacheFile, LegacyCacheFile })
            {
                try { if (File.Exists(path)) total += new FileInfo(path).Length; }
                catch { }
            }
            return total;
        }

        private class CacheEntry
        {
            public string FilePath { get; set; } = "";
            public long FileSizeBytes { get; set; }
            public DateTime LastWriteUtc { get; set; }
            public string? SettingsFingerprint { get; set; }

            // Core analysis results
            public int Status { get; set; }
            public string? Artist { get; set; }
            public string? Title { get; set; }
            public string? FileName { get; set; }
            public string? FolderPath { get; set; }
            public int SampleRate { get; set; }
            public int BitsPerSample { get; set; }
            public string? Duration { get; set; }
            public double DurationSeconds { get; set; }
            public string? FileSize { get; set; }
            public int ReportedBitrate { get; set; }
            public int ActualBitrate { get; set; }
            public int EstimatedSourceBitrate { get; set; }
            public string? Extension { get; set; }
            public int EffectiveFrequency { get; set; }
            public double CutoffDropDb { get; set; }
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
            public string? MqaOriginalSampleRate { get; set; }
            public string? MqaEncoder { get; set; }

            // AI detection
            public bool IsAiGenerated { get; set; }
            public string? AiSource { get; set; }
            public List<string>? AiSources { get; set; }
            public double AiConfidence { get; set; }
            public bool ExperimentalAiSuspicious { get; set; }
            public double ExperimentalAiConfidence { get; set; }
            public List<string>? ExperimentalAiFlags { get; set; }
            public bool SHLabsScanned { get; set; }
            public string? SHLabsPrediction { get; set; }
            public double SHLabsProbability { get; set; }
            public double SHLabsConfidence { get; set; }
            public string? SHLabsAiType { get; set; }

            // Other
            public bool HasAlbumCover { get; set; }
            public bool IsAlac { get; set; }
            public string? ErrorMessage { get; set; }

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
            public string? FakeStereoType { get; set; }
            public double StereoCorrelation { get; set; }

            // True Peak / LUFS
            public double TruePeakDbTP { get; set; }
            public bool HasTruePeak { get; set; }
            public double IntegratedLufs { get; set; }
            public bool HasLufs { get; set; }

            // CD Rip Checker (cambia score for the log next to this file)
            //
            // Always serialized. The serializer's DefaultIgnoreCondition is WhenWritingDefault,
            // which compares against the TYPE default (0), not this initializer — so a genuine
            // worst-case score of 0 was omitted and came back as -1 ("no rip log") on reload,
            // while -1 was the only value actually written. Exactly inverted.
            [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
            public int RipLogScore { get; set; } = -1;
            public string? RipLogVerdict { get; set; }
            public bool HasRipLog { get; set; }

            public AudioFileInfo ToAudioFileInfo()
            {
                return new AudioFileInfo
                {
                    Status = (AudioStatus)Status,
                    Artist = Artist ?? "",
                    Title = Title ?? "",
                    FileName = FileName ?? "",
                    FilePath = FilePath,
                    FolderPath = FolderPath ?? "",
                    SampleRate = SampleRate,
                    BitsPerSample = BitsPerSample,
                    Duration = Duration ?? "",
                    DurationSeconds = DurationSeconds,
                    FileSize = FileSize ?? "",
                    FileSizeBytes = FileSizeBytes,
                    ReportedBitrate = ReportedBitrate,
                    ActualBitrate = ActualBitrate,
                    EstimatedSourceBitrate = EstimatedSourceBitrate,
                    Extension = Extension ?? "",
                    EffectiveFrequency = EffectiveFrequency,
                    CutoffDropDb = CutoffDropDb,
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
                    MqaOriginalSampleRate = MqaOriginalSampleRate ?? "",
                    MqaEncoder = MqaEncoder ?? "",
                    IsAiGenerated = IsAiGenerated,
                    AiSource = AiSource ?? "",
                    AiSources = AiSources ?? new(),
                    AiConfidence = AiConfidence,
                    ExperimentalAiSuspicious = ExperimentalAiSuspicious,
                    ExperimentalAiConfidence = ExperimentalAiConfidence,
                    ExperimentalAiFlags = ExperimentalAiFlags ?? new(),
                    SHLabsScanned = SHLabsScanned,
                    SHLabsPrediction = SHLabsPrediction ?? "",
                    SHLabsProbability = SHLabsProbability,
                    SHLabsConfidence = SHLabsConfidence,
                    SHLabsAiType = SHLabsAiType ?? "",
                    HasAlbumCover = HasAlbumCover,
                    IsAlac = IsAlac,
                    ErrorMessage = ErrorMessage ?? "",
                    LeadingSilenceMs = LeadingSilenceMs,
                    TrailingSilenceMs = TrailingSilenceMs,
                    MidTrackSilenceGaps = MidTrackSilenceGaps,
                    TotalMidSilenceMs = TotalMidSilenceMs,
                    HasExcessiveSilence = HasExcessiveSilence,
                    DynamicRange = DynamicRange,
                    HasDynamicRange = HasDynamicRange,
                    IsFakeStereo = IsFakeStereo,
                    FakeStereoType = FakeStereoType ?? "",
                    StereoCorrelation = StereoCorrelation,
                    TruePeakDbTP = TruePeakDbTP,
                    HasTruePeak = HasTruePeak,
                    IntegratedLufs = IntegratedLufs,
                    HasLufs = HasLufs,
                    RipLogScore = RipLogScore,
                    RipLogVerdict = RipLogVerdict ?? "",
                    HasRipLog = HasRipLog,
                };
            }

            public static CacheEntry FromAudioFileInfo(AudioFileInfo info, long sizeBytes, DateTime lastWriteUtc, string? settingsFingerprint = null)
            {
                static string? S(string? v) => string.IsNullOrEmpty(v) ? null : v;
                return new CacheEntry
                {
                    FilePath = info.FilePath,
                    FileSizeBytes = sizeBytes,
                    LastWriteUtc = lastWriteUtc,
                    SettingsFingerprint = settingsFingerprint,
                    Status = (int)info.Status,
                    Artist = S(info.Artist),
                    Title = S(info.Title),
                    FileName = S(info.FileName),
                    FolderPath = S(info.FolderPath),
                    SampleRate = info.SampleRate,
                    BitsPerSample = info.BitsPerSample,
                    Duration = S(info.Duration),
                    DurationSeconds = info.DurationSeconds,
                    FileSize = S(info.FileSize),
                    ReportedBitrate = info.ReportedBitrate,
                    ActualBitrate = info.ActualBitrate,
                    EstimatedSourceBitrate = info.EstimatedSourceBitrate,
                    Extension = S(info.Extension),
                    EffectiveFrequency = info.EffectiveFrequency,
                    CutoffDropDb = info.CutoffDropDb,
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
                    MqaOriginalSampleRate = S(info.MqaOriginalSampleRate),
                    MqaEncoder = S(info.MqaEncoder),
                    IsAiGenerated = info.IsAiGenerated,
                    AiSource = S(info.AiSource),
                    AiSources = info.AiSources?.Count > 0 ? info.AiSources : null,
                    AiConfidence = info.AiConfidence,
                    ExperimentalAiSuspicious = info.ExperimentalAiSuspicious,
                    ExperimentalAiConfidence = info.ExperimentalAiConfidence,
                    ExperimentalAiFlags = info.ExperimentalAiFlags?.Count > 0 ? info.ExperimentalAiFlags : null,
                    SHLabsScanned = info.SHLabsScanned,
                    SHLabsPrediction = S(info.SHLabsPrediction),
                    SHLabsProbability = info.SHLabsProbability,
                    SHLabsConfidence = info.SHLabsConfidence,
                    SHLabsAiType = S(info.SHLabsAiType),
                    HasAlbumCover = info.HasAlbumCover,
                    IsAlac = info.IsAlac,
                    ErrorMessage = S(info.ErrorMessage),
                    LeadingSilenceMs = info.LeadingSilenceMs,
                    TrailingSilenceMs = info.TrailingSilenceMs,
                    MidTrackSilenceGaps = info.MidTrackSilenceGaps,
                    TotalMidSilenceMs = info.TotalMidSilenceMs,
                    HasExcessiveSilence = info.HasExcessiveSilence,
                    DynamicRange = info.DynamicRange,
                    HasDynamicRange = info.HasDynamicRange,
                    IsFakeStereo = info.IsFakeStereo,
                    FakeStereoType = S(info.FakeStereoType),
                    StereoCorrelation = info.StereoCorrelation,
                    TruePeakDbTP = info.TruePeakDbTP,
                    HasTruePeak = info.HasTruePeak,
                    IntegratedLufs = info.IntegratedLufs,
                    HasLufs = info.HasLufs,
                    RipLogScore = info.RipLogScore,
                    RipLogVerdict = S(info.RipLogVerdict),
                    HasRipLog = info.HasRipLog,
                };
            }
        }
    }
}
