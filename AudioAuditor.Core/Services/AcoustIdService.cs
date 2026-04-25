using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace AudioQualityChecker.Services
{
    public class AcoustIdResult
    {
        public string Title { get; set; } = "";
        public string Artist { get; set; } = "";
        public string Album { get; set; } = "";
        public string MusicBrainzRecordingId { get; set; } = "";
        public double Score { get; set; }     // 0.0 – 1.0
        public int? TrackNumber { get; set; }
        public int? Year { get; set; }
    }

    public static class AcoustIdService
    {
        private static readonly HttpClient _http = new()
        {
            Timeout = TimeSpan.FromSeconds(20),
            DefaultRequestHeaders = { { "User-Agent", "AudioAuditor/1.5.0 (https://github.com)" } }
        };

        /// <summary>
        /// Path to fpcalc executable. Searched next to the app, then in PATH.
        /// </summary>
        private static readonly SemaphoreSlim _downloadLock = new(1, 1);

        public static string? FindFpcalc()
        {
            // Next to the running exe
            var appDir = AppDomain.CurrentDomain.BaseDirectory;
            var local = Path.Combine(appDir, "fpcalc.exe");
            if (File.Exists(local)) return local;

            // In AppData alongside settings
            var appData = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "AudioAuditor");
            var appDataPath = Path.Combine(appData, "fpcalc.exe");
            if (File.Exists(appDataPath)) return appDataPath;

            // In PATH
            var pathDirs = Environment.GetEnvironmentVariable("PATH")?.Split(';') ?? Array.Empty<string>();
            foreach (var dir in pathDirs)
            {
                try
                {
                    var candidate = Path.Combine(dir.Trim(), "fpcalc.exe");
                    if (File.Exists(candidate)) return candidate;
                }
                catch { }
            }

            return null;
        }

        /// <summary>
        /// Downloads fpcalc from the official Chromaprint releases if not already present.
        /// Returns the path to fpcalc.exe on success, or null on failure.
        /// </summary>
        public static async Task<string?> EnsureFpcalcAsync(CancellationToken ct = default)
        {
            var existing = FindFpcalc();
            if (existing != null) return existing;

            // Offline mode: don't attempt to download fpcalc
            if (AudioAuditorSettings.OfflineMode) return null;

            await _downloadLock.WaitAsync(ct);
            try
            {
                // Double-check after acquiring lock
                existing = FindFpcalc();
                if (existing != null) return existing;

                var appData = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "AudioAuditor");
                Directory.CreateDirectory(appData);
                var targetPath = Path.Combine(appData, "fpcalc.exe");
                var tempZip = Path.Combine(appData, "chromaprint.zip");

                // Download the official Chromaprint 1.5.1 Windows release
                const string downloadUrl = "https://github.com/acoustid/chromaprint/releases/download/v1.5.1/chromaprint-fpcalc-1.5.1-windows-x86_64.zip";

                using var response = await _http.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead, ct);
                if (!response.IsSuccessStatusCode) return null;

                // Save zip to temp file
                using (var fs = new FileStream(tempZip, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    await response.Content.CopyToAsync(fs, ct);
                }

                // Extract fpcalc.exe from the zip
                using (var archive = System.IO.Compression.ZipFile.OpenRead(tempZip))
                {
                    var entry = archive.Entries.FirstOrDefault(e =>
                        e.Name.Equals("fpcalc.exe", StringComparison.OrdinalIgnoreCase));
                    if (entry == null)
                    {
                        TryDelete(tempZip);
                        return null;
                    }
                    entry.ExtractToFile(targetPath, overwrite: true);
                }

                TryDelete(tempZip);
                return File.Exists(targetPath) ? targetPath : null;
            }
            catch
            {
                return null;
            }
            finally
            {
                _downloadLock.Release();
            }
        }

        private static void TryDelete(string path)
        {
            try { File.Delete(path); } catch { }
        }

        /// <summary>
        /// Generate a Chromaprint fingerprint using fpcalc.
        /// Returns (fingerprint, duration_seconds) or null on failure.
        /// </summary>
        public static async Task<(string fingerprint, int duration)?> GetFingerprint(string audioPath, CancellationToken ct = default)
        {
            var fpcalc = FindFpcalc();
            if (fpcalc == null)
            {
                // Auto-download fpcalc
                fpcalc = await EnsureFpcalcAsync(ct);
                if (fpcalc == null) return null;
            }

            try
            {
                // Use ArgumentList (not Arguments string) to avoid quoting/injection issues
                // with audio paths that contain quote characters
                var psi = new ProcessStartInfo
                {
                    FileName = fpcalc,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };
                psi.ArgumentList.Add("-json");
                psi.ArgumentList.Add(audioPath);

                using var proc = Process.Start(psi);
                if (proc == null) return null;

                var output = await proc.StandardOutput.ReadToEndAsync(ct);
                await proc.WaitForExitAsync(ct);

                if (proc.ExitCode != 0) return null;

                using var doc = JsonDocument.Parse(output);
                var root = doc.RootElement;
                var fp = root.GetProperty("fingerprint").GetString();
                var dur = (int)root.GetProperty("duration").GetDouble();

                if (string.IsNullOrEmpty(fp)) return null;
                return (fp, dur);
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Look up a fingerprint on the AcoustID service.
        /// Requires an API key (register at https://acoustid.org/new-application).
        /// </summary>
        public static async Task<List<AcoustIdResult>> Lookup(string fingerprint, int duration, string apiKey, CancellationToken ct = default)
        {
            var results = new List<AcoustIdResult>();
            if (string.IsNullOrWhiteSpace(apiKey)) return results;

            try
            {
                // AcoustID API v2
                var url = $"https://api.acoustid.org/v2/lookup?" +
                          $"client={Uri.EscapeDataString(apiKey)}" +
                          $"&duration={duration}" +
                          $"&fingerprint={Uri.EscapeDataString(fingerprint)}" +
                          $"&meta=recordings+releasegroups+compress";

                var response = await _http.GetAsync(url, ct);
                if (!response.IsSuccessStatusCode) return results;

                var json = await response.Content.ReadAsStringAsync(ct);
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                if (!root.TryGetProperty("results", out var resultsArr)) return results;

                foreach (var result in resultsArr.EnumerateArray())
                {
                    double score = result.TryGetProperty("score", out var s) ? s.GetDouble() : 0;
                    if (!result.TryGetProperty("recordings", out var recordings)) continue;

                    foreach (var rec in recordings.EnumerateArray())
                    {
                        var entry = new AcoustIdResult { Score = score };

                        if (rec.TryGetProperty("title", out var t))
                            entry.Title = t.GetString() ?? "";

                        if (rec.TryGetProperty("id", out var id))
                            entry.MusicBrainzRecordingId = id.GetString() ?? "";

                        if (rec.TryGetProperty("artists", out var artists))
                        {
                            var names = new List<string>();
                            foreach (var a in artists.EnumerateArray())
                                if (a.TryGetProperty("name", out var n))
                                    names.Add(n.GetString() ?? "");
                            entry.Artist = string.Join(", ", names);
                        }

                        // Try to get album + year from release groups
                        if (rec.TryGetProperty("releasegroups", out var rgs))
                        {
                            foreach (var rg in rgs.EnumerateArray())
                            {
                                if (rg.TryGetProperty("title", out var at))
                                    entry.Album = at.GetString() ?? "";

                                // Secondarytypes first release date
                                if (rg.TryGetProperty("releases", out var rels))
                                {
                                    foreach (var rel in rels.EnumerateArray())
                                    {
                                        if (rel.TryGetProperty("date", out var dateEl))
                                        {
                                            var dateObj = dateEl.TryGetProperty("year", out var yr)
                                                ? yr.GetInt32()
                                                : (int?)null;
                                            if (dateObj.HasValue)
                                                entry.Year = dateObj;
                                        }
                                        if (rel.TryGetProperty("mediums", out var meds))
                                        {
                                            foreach (var med in meds.EnumerateArray())
                                            {
                                                if (med.TryGetProperty("tracks", out var tracks))
                                                {
                                                    foreach (var trk in tracks.EnumerateArray())
                                                    {
                                                        if (trk.TryGetProperty("position", out var pos))
                                                            entry.TrackNumber = pos.GetInt32();
                                                    }
                                                }
                                            }
                                        }
                                        break; // first release is enough
                                    }
                                }
                                break; // first release group
                            }
                        }

                        results.Add(entry);
                        break; // first recording per result
                    }
                }
            }
            catch { }

            return results.OrderByDescending(r => r.Score).ToList();
        }

        /// <summary>
        /// Full identify: fingerprint + lookup in one call.
        /// </summary>
        public static async Task<List<AcoustIdResult>> Identify(string audioPath, string apiKey, CancellationToken ct = default)
        {
            var fp = await GetFingerprint(audioPath, ct);
            if (fp == null) return new List<AcoustIdResult>();

            return await Lookup(fp.Value.fingerprint, fp.Value.duration, apiKey, ct);
        }
    }
}
