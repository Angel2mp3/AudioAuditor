using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using AudioQualityChecker.Models;

namespace AudioQualityChecker.Services
{
    /// <summary>
    /// Collects listening and analysis statistics locally.
    /// OFF by default — only collects when user explicitly opts in.
    /// All data stays on the user's PC in a single JSON file.
    /// </summary>
    public static class LocalStatsCollector
    {
        private static readonly string StatsFile = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "AudioAuditor", "stats.json");

        private static StatsData? _data;
        private static readonly object _lock = new();

        private static readonly JsonSerializerOptions SaveOptions = new()
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault
        };

        public static bool IsEnabled => ThemeManager.StatsCollectionEnabled;

        private static StatsData Data
        {
            get
            {
                if (_data == null)
                {
                    _data = Load();
                }
                return _data;
            }
        }

        // ─── Public API ───

        public static void RecordScan(int fileCount, long totalBytes)
        {
            if (!IsEnabled) return;
            lock (_lock)
            {
                Data.TotalFilesScanned += fileCount;
                Data.TotalLibraryBytes += totalBytes;
                Data.ScanSessionCount++;
                Data.LastActivityDate = DateTime.Now;
                AppendEvent(Data.ScanEvents, new ScanEvent
                {
                    Time = DateTime.Now,
                    FileCount = fileCount,
                    Bytes = totalBytes
                });
                Save();
            }
        }

        public static void RecordTrackPlayed(AudioFileInfo? track, double secondsListened)
        {
            if (!IsEnabled || track == null) return;
            lock (_lock)
            {
                Data.TotalListeningSeconds += secondsListened;
                Data.LastActivityDate = DateTime.Now;

                string key = $"{track.Artist ?? "Unknown"}|{track.Title ?? track.FileName ?? "Unknown"}";
                if (!Data.TrackPlayCounts.ContainsKey(key))
                    Data.TrackPlayCounts[key] = new TrackStats
                    {
                        Artist = track.Artist ?? "Unknown",
                        Title = track.Title ?? track.FileName ?? "Unknown",
                        Album = track.Album ?? "",
                        Format = Path.GetExtension(track.FilePath)?.TrimStart('.').ToUpperInvariant() ?? "UNKNOWN"
                    };
                Data.TrackPlayCounts[key].PlayCount++;
                Data.TrackPlayCounts[key].SecondsListened += secondsListened;

                if (!string.IsNullOrEmpty(track.Artist))
                {
                    if (!Data.ArtistPlayCounts.ContainsKey(track.Artist))
                        Data.ArtistPlayCounts[track.Artist] = 0;
                    Data.ArtistPlayCounts[track.Artist]++;
                }

                if (!string.IsNullOrEmpty(track.Album))
                {
                    if (!Data.AlbumPlayCounts.ContainsKey(track.Album))
                        Data.AlbumPlayCounts[track.Album] = 0;
                    Data.AlbumPlayCounts[track.Album]++;
                }

                string ext = Path.GetExtension(track.FilePath)?.TrimStart('.').ToUpperInvariant() ?? "UNKNOWN";
                if (!Data.FormatCounts.ContainsKey(ext))
                    Data.FormatCounts[ext] = 0;
                Data.FormatCounts[ext]++;

                AppendEvent(Data.PlayEvents, new PlayEvent
                {
                    Time = DateTime.Now,
                    Artist = track.Artist ?? "Unknown",
                    Title = track.Title ?? track.FileName ?? "Unknown",
                    Album = track.Album ?? "",
                    Format = ext,
                    Seconds = secondsListened
                });

                Save();
            }
        }

        public static void RecordAnalysisResult(AudioFileInfo track)
        {
            if (!IsEnabled) return;
            lock (_lock)
            {
                Data.LastActivityDate = DateTime.Now;

                string ext = Path.GetExtension(track.FilePath)?.TrimStart('.').ToUpperInvariant() ?? "UNKNOWN";
                if (!Data.FormatCounts.ContainsKey(ext))
                    Data.FormatCounts[ext] = 0;
                Data.FormatCounts[ext]++;

                if (track.ActualBitrate > 0)
                {
                    Data.BitrateSum += track.ActualBitrate;
                    Data.BitrateCount++;
                }
                if (track.HasLufs)
                {
                    Data.LufsSum += track.IntegratedLufs;
                    Data.LufsCount++;
                }
                if (track.HasDynamicRange)
                {
                    Data.DrSum += track.DynamicRange;
                    Data.DrCount++;
                }
                if (track.HasClipping)
                    Data.ClippingDetectedCount++;

                Data.AnalyzedFileCount++;

                if (track.SampleRate > 0)
                {
                    string srKey = FormatSampleRate(track.SampleRate);
                    Data.SampleRateCounts[srKey] = Data.SampleRateCounts.GetValueOrDefault(srKey) + 1;
                }
                if (track.BitsPerSample > 0)
                {
                    string bdKey = $"{track.BitsPerSample}-bit";
                    Data.BitDepthCounts[bdKey] = Data.BitDepthCounts.GetValueOrDefault(bdKey) + 1;
                }
                if (track.IsMqa)
                    Data.MqaCount++;

                if (track.Channels > 0)
                {
                    string chKey = FormatChannels(track.Channels);
                    Data.ChannelCounts[chKey] = Data.ChannelCounts.GetValueOrDefault(chKey) + 1;
                }

                AppendEvent(Data.AnalysisEvents, new AnalysisEvent
                {
                    Time = DateTime.Now,
                    Format = ext,
                    Bitrate = track.ActualBitrate > 0 ? track.ActualBitrate : 0,
                    Lufs = track.HasLufs ? track.IntegratedLufs : null,
                    Dr = track.HasDynamicRange ? track.DynamicRange : null,
                    Clipping = track.HasClipping,
                    SampleRate = track.SampleRate,
                    BitsPerSample = track.BitsPerSample,
                    Mqa = track.IsMqa,
                    Channels = track.Channels
                });

                Save();
            }
        }

        // Append an event, trimming the oldest if the list exceeds its cap. Keeps stats.json bounded
        // for heavy users (events are only used for date-range Wrapped views; the running aggregates
        // above remain the source of truth for All Time).
        private static void AppendEvent<T>(List<T> list, T ev)
        {
            list.Add(ev);
            if (list.Count > MaxEventsPerKind)
                list.RemoveRange(0, list.Count - MaxEventsPerKind);
        }

        private const int MaxEventsPerKind = 100_000;

        /// <summary>Labels a channel count for display (1 → "Mono", 2 → "Stereo", else "Nch").</summary>
        private static string FormatChannels(int channels) => channels switch
        {
            1 => "Mono",
            2 => "Stereo",
            _ => $"{channels}ch"
        };

        /// <summary>Formats a sample rate in Hz as a compact kHz label (e.g. 44100 → "44.1 kHz", 48000 → "48 kHz").</summary>
        private static string FormatSampleRate(int hz)
        {
            double khz = hz / 1000.0;
            string num = Math.Abs(khz - Math.Round(khz)) < 0.05
                ? ((int)Math.Round(khz)).ToString(System.Globalization.CultureInfo.InvariantCulture)
                : khz.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture);
            return $"{num} kHz";
        }

        public static void Reset()
        {
            lock (_lock)
            {
                _data = new StatsData();
                Save();
            }
        }

        public static StatsData GetSnapshot()
        {
            lock (_lock)
            {
                // Return a copy so callers can't mutate our internal state
                string json = JsonSerializer.Serialize(Data);
                return JsonSerializer.Deserialize<StatsData>(json)!;
            }
        }

        /// <summary>
        /// Builds a <see cref="StatsData"/> aggregated from only the timestamped events that fall in
        /// [<paramref name="fromInclusive"/>, <paramref name="toExclusive"/>). Used by the Wrapped
        /// date-range views. Mirrors the live recording logic so a range reads the same as All Time
        /// would for that window. Events predate the timestamped-log feature only for All Time, so a
        /// range over older history returns an empty (no-data) snapshot — that's expected.
        /// </summary>
        public static StatsData GetSnapshotForRange(DateTime fromInclusive, DateTime toExclusive)
        {
            lock (_lock)
            {
                var d = new StatsData();
                bool any = false;
                DateTime first = DateTime.MaxValue, last = DateTime.MinValue;

                bool InRange(DateTime t) => t >= fromInclusive && t < toExclusive;
                void Mark(DateTime t)
                {
                    any = true;
                    if (t < first) first = t;
                    if (t > last) last = t;
                }

                foreach (var s in Data.ScanEvents)
                {
                    if (!InRange(s.Time)) continue;
                    Mark(s.Time);
                    d.TotalFilesScanned += s.FileCount;
                    d.TotalLibraryBytes += s.Bytes;
                    d.ScanSessionCount++;
                }

                foreach (var p in Data.PlayEvents)
                {
                    if (!InRange(p.Time)) continue;
                    Mark(p.Time);
                    d.TotalListeningSeconds += p.Seconds;

                    string key = $"{(string.IsNullOrEmpty(p.Artist) ? "Unknown" : p.Artist)}|{(string.IsNullOrEmpty(p.Title) ? "Unknown" : p.Title)}";
                    if (!d.TrackPlayCounts.TryGetValue(key, out var ts))
                        d.TrackPlayCounts[key] = ts = new TrackStats
                        {
                            Artist = string.IsNullOrEmpty(p.Artist) ? "Unknown" : p.Artist,
                            Title = string.IsNullOrEmpty(p.Title) ? "Unknown" : p.Title,
                            Album = p.Album,
                            Format = p.Format
                        };
                    ts.PlayCount++;
                    ts.SecondsListened += p.Seconds;

                    if (!string.IsNullOrEmpty(p.Artist))
                        d.ArtistPlayCounts[p.Artist] = d.ArtistPlayCounts.GetValueOrDefault(p.Artist) + 1;
                    if (!string.IsNullOrEmpty(p.Album))
                        d.AlbumPlayCounts[p.Album] = d.AlbumPlayCounts.GetValueOrDefault(p.Album) + 1;
                    if (!string.IsNullOrEmpty(p.Format))
                        d.FormatCounts[p.Format] = d.FormatCounts.GetValueOrDefault(p.Format) + 1;
                }

                foreach (var a in Data.AnalysisEvents)
                {
                    if (!InRange(a.Time)) continue;
                    Mark(a.Time);
                    if (!string.IsNullOrEmpty(a.Format))
                        d.FormatCounts[a.Format] = d.FormatCounts.GetValueOrDefault(a.Format) + 1;
                    if (a.Bitrate > 0) { d.BitrateSum += a.Bitrate; d.BitrateCount++; }
                    if (a.Lufs.HasValue) { d.LufsSum += a.Lufs.Value; d.LufsCount++; }
                    if (a.Dr.HasValue) { d.DrSum += a.Dr.Value; d.DrCount++; }
                    if (a.Clipping) d.ClippingDetectedCount++;
                    d.AnalyzedFileCount++;
                    if (a.SampleRate > 0)
                    {
                        string srKey = FormatSampleRate(a.SampleRate);
                        d.SampleRateCounts[srKey] = d.SampleRateCounts.GetValueOrDefault(srKey) + 1;
                    }
                    if (a.BitsPerSample > 0)
                    {
                        string bdKey = $"{a.BitsPerSample}-bit";
                        d.BitDepthCounts[bdKey] = d.BitDepthCounts.GetValueOrDefault(bdKey) + 1;
                    }
                    if (a.Mqa) d.MqaCount++;
                    if (a.Channels > 0)
                    {
                        string chKey = FormatChannels(a.Channels);
                        d.ChannelCounts[chKey] = d.ChannelCounts.GetValueOrDefault(chKey) + 1;
                    }
                }

                // Activity dates frame the subtitle/“days active”: use the real span of events in
                // range when present, otherwise the requested window so the header still reads right.
                d.FirstActivityDate = any ? first : fromInclusive;
                d.LastActivityDate = any ? last : (toExclusive > fromInclusive ? toExclusive.AddTicks(-1) : fromInclusive);
                return d;
            }
        }

        // ─── Time-series (for Wrapped charts) ───

        private enum StatsBucket { Hour, Day, Month }

        private static StatsBucket ChooseBucket(DateTime from, DateTime to)
        {
            var span = to - from;
            if (span <= TimeSpan.FromDays(2)) return StatsBucket.Hour;
            if (span <= TimeSpan.FromDays(92)) return StatsBucket.Day;
            return StatsBucket.Month;
        }

        private static DateTime BucketKey(DateTime t, StatsBucket b) => b switch
        {
            StatsBucket.Hour => new DateTime(t.Year, t.Month, t.Day, t.Hour, 0, 0),
            StatsBucket.Day => t.Date,
            _ => new DateTime(t.Year, t.Month, 1)
        };

        private static DateTime BucketNext(DateTime cur, StatsBucket b) => b switch
        {
            StatsBucket.Hour => cur.AddHours(1),
            StatsBucket.Day => cur.AddDays(1),
            _ => cur.AddMonths(1)
        };

        // Contiguous bucket starts spanning [from, to) so charts get a continuous x-axis (gaps = 0).
        private static List<DateTime> EnumerateBuckets(DateTime from, DateTime to, StatsBucket b)
        {
            var list = new List<DateTime>();
            var cur = BucketKey(from, b);
            int guard = 0;
            while (cur < to && guard++ < 100_000)
            {
                list.Add(cur);
                cur = BucketNext(cur, b);
            }
            if (list.Count == 0) list.Add(BucketKey(from, b));
            return list;
        }

        private static Dictionary<DateTime, int> IndexBuckets(List<DateTime> buckets)
        {
            var map = new Dictionary<DateTime, int>(buckets.Count);
            for (int i = 0; i < buckets.Count; i++) map[buckets[i]] = i;
            return map;
        }

        /// <summary>Plays and listening-minutes per time bucket across [from, to). Bucket size is
        /// chosen from the span (hourly ≤2d, daily ≤92d, else monthly). Powers the activity chart.</summary>
        public static List<(DateTime bucketStart, int plays, double minutes)> GetActivitySeries(DateTime from, DateTime to)
        {
            lock (_lock)
            {
                var bucket = ChooseBucket(from, to);
                var buckets = EnumerateBuckets(from, to, bucket);
                var index = IndexBuckets(buckets);
                var plays = new int[buckets.Count];
                var minutes = new double[buckets.Count];

                foreach (var p in Data.PlayEvents)
                {
                    if (p.Time < from || p.Time >= to) continue;
                    if (index.TryGetValue(BucketKey(p.Time, bucket), out int i))
                    {
                        plays[i]++;
                        minutes[i] += p.Seconds / 60.0;
                    }
                }

                var result = new List<(DateTime, int, double)>(buckets.Count);
                for (int i = 0; i < buckets.Count; i++) result.Add((buckets[i], plays[i], minutes[i]));
                return result;
            }
        }

        /// <summary>Average bitrate and dynamic range per time bucket across [from, to) (same bucketing
        /// as <see cref="GetActivitySeries"/>). <c>n</c> is the analyzed-file count in the bucket (0 = gap).</summary>
        public static List<(DateTime bucketStart, double avgBitrate, double avgDr, int n)> GetQualityTrend(DateTime from, DateTime to)
        {
            lock (_lock)
            {
                var bucket = ChooseBucket(from, to);
                var buckets = EnumerateBuckets(from, to, bucket);
                var index = IndexBuckets(buckets);
                var brSum = new double[buckets.Count];
                var brN = new int[buckets.Count];
                var drSum = new double[buckets.Count];
                var drN = new int[buckets.Count];

                foreach (var a in Data.AnalysisEvents)
                {
                    if (a.Time < from || a.Time >= to) continue;
                    if (!index.TryGetValue(BucketKey(a.Time, bucket), out int i)) continue;
                    if (a.Bitrate > 0) { brSum[i] += a.Bitrate; brN[i]++; }
                    if (a.Dr.HasValue) { drSum[i] += a.Dr.Value; drN[i]++; }
                }

                var result = new List<(DateTime, double, double, int)>(buckets.Count);
                for (int i = 0; i < buckets.Count; i++)
                    result.Add((buckets[i],
                        brN[i] > 0 ? brSum[i] / brN[i] : 0,
                        drN[i] > 0 ? drSum[i] / drN[i] : 0,
                        Math.Max(brN[i], drN[i])));
                return result;
            }
        }

        /// <summary>Earliest recorded event time (used to frame the All Time chart window), or null
        /// when no timestamped events exist yet.</summary>
        public static DateTime? EarliestEventTime()
        {
            lock (_lock)
            {
                DateTime? min = null;
                void Consider(DateTime t) { if (min == null || t < min) min = t; }
                foreach (var p in Data.PlayEvents) Consider(p.Time);
                foreach (var a in Data.AnalysisEvents) Consider(a.Time);
                foreach (var s in Data.ScanEvents) Consider(s.Time);
                return min;
            }
        }

        // ─── Persistence ───

        private static StatsData Load()
        {
            try
            {
                if (File.Exists(StatsFile))
                {
                    string json = File.ReadAllText(StatsFile);
                    var data = JsonSerializer.Deserialize<StatsData>(json);
                    if (data != null) return data;
                }
            }
            catch { }
            return new StatsData();
        }

        private static void Save()
        {
            try
            {
                string dir = Path.GetDirectoryName(StatsFile)!;
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                // Compact (not indented): the event log can grow to tens of thousands of entries, and
                // indentation would multiply the file size for no benefit (it isn't hand-edited).
                string json = JsonSerializer.Serialize(_data, SaveOptions);
                File.WriteAllText(StatsFile, json);
            }
            catch { }
        }
    }

    public class StatsData
    {
        [JsonPropertyName("version")] public int Version { get; set; } = 1;
        [JsonPropertyName("firstActivity")] public DateTime FirstActivityDate { get; set; } = DateTime.Now;
        [JsonPropertyName("lastActivity")] public DateTime LastActivityDate { get; set; } = DateTime.Now;
        [JsonPropertyName("filesScanned")] public int TotalFilesScanned { get; set; }
        [JsonPropertyName("libraryBytes")] public long TotalLibraryBytes { get; set; }
        [JsonPropertyName("listeningSeconds")] public double TotalListeningSeconds { get; set; }
        [JsonPropertyName("scanSessions")] public int ScanSessionCount { get; set; }
        [JsonPropertyName("bitrateSum")] public long BitrateSum { get; set; }
        [JsonPropertyName("bitrateCount")] public int BitrateCount { get; set; }
        [JsonPropertyName("lufsSum")] public double LufsSum { get; set; }
        [JsonPropertyName("lufsCount")] public int LufsCount { get; set; }
        [JsonPropertyName("drSum")] public double DrSum { get; set; }
        [JsonPropertyName("drCount")] public int DrCount { get; set; }
        [JsonPropertyName("clippingCount")] public int ClippingDetectedCount { get; set; }
        [JsonPropertyName("analyzedCount")] public int AnalyzedFileCount { get; set; }
        [JsonPropertyName("mqaCount")] public int MqaCount { get; set; }
        [JsonPropertyName("sampleRates")] public Dictionary<string, int> SampleRateCounts { get; set; } = new();
        [JsonPropertyName("bitDepths")] public Dictionary<string, int> BitDepthCounts { get; set; } = new();
        [JsonPropertyName("channels")] public Dictionary<string, int> ChannelCounts { get; set; } = new();
        [JsonPropertyName("tracks")] public Dictionary<string, TrackStats> TrackPlayCounts { get; set; } = new();
        [JsonPropertyName("artists")] public Dictionary<string, int> ArtistPlayCounts { get; set; } = new();
        [JsonPropertyName("albums")] public Dictionary<string, int> AlbumPlayCounts { get; set; } = new();
        [JsonPropertyName("formats")] public Dictionary<string, int> FormatCounts { get; set; } = new();

        // Timestamped event log (added in v2). Powers date-range Wrapped views (Day/Week/Month/Year/
        // Custom). Only data recorded after this feature shipped has timestamps; the aggregate fields
        // above stay authoritative for All Time and include the full untimestamped history.
        [JsonPropertyName("playEvents")] public List<PlayEvent> PlayEvents { get; set; } = new();
        [JsonPropertyName("analysisEvents")] public List<AnalysisEvent> AnalysisEvents { get; set; } = new();
        [JsonPropertyName("scanEvents")] public List<ScanEvent> ScanEvents { get; set; } = new();
    }

    public class PlayEvent
    {
        [JsonPropertyName("t")] public DateTime Time { get; set; }
        [JsonPropertyName("ar")] public string Artist { get; set; } = "";
        [JsonPropertyName("ti")] public string Title { get; set; } = "";
        [JsonPropertyName("al")] public string Album { get; set; } = "";
        [JsonPropertyName("fmt")] public string Format { get; set; } = "";
        [JsonPropertyName("sec")] public double Seconds { get; set; }
    }

    public class AnalysisEvent
    {
        [JsonPropertyName("t")] public DateTime Time { get; set; }
        [JsonPropertyName("fmt")] public string Format { get; set; } = "";
        [JsonPropertyName("br")] public int Bitrate { get; set; }
        [JsonPropertyName("lu")] public double? Lufs { get; set; }
        [JsonPropertyName("dr")] public double? Dr { get; set; }
        [JsonPropertyName("cl")] public bool Clipping { get; set; }
        [JsonPropertyName("sr")] public int SampleRate { get; set; }
        [JsonPropertyName("bd")] public int BitsPerSample { get; set; }
        [JsonPropertyName("mqa")] public bool Mqa { get; set; }
        [JsonPropertyName("ch")] public int Channels { get; set; }
    }

    public class ScanEvent
    {
        [JsonPropertyName("t")] public DateTime Time { get; set; }
        [JsonPropertyName("fc")] public int FileCount { get; set; }
        [JsonPropertyName("by")] public long Bytes { get; set; }
    }

    public class TrackStats
    {
        [JsonPropertyName("artist")] public string Artist { get; set; } = "";
        [JsonPropertyName("title")] public string Title { get; set; } = "";
        [JsonPropertyName("album")] public string Album { get; set; } = "";
        [JsonPropertyName("format")] public string Format { get; set; } = "";
        [JsonPropertyName("plays")] public int PlayCount { get; set; }
        [JsonPropertyName("seconds")] public double SecondsListened { get; set; }
    }
}
