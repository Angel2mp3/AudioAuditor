using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AudioQualityChecker.Services
{
    /// <summary>
    /// Persists the user's working set (added roots + currently loaded file paths)
    /// so the app can repopulate the grid after a clean exit or a crash.
    ///
    /// This is intentionally separate from <see cref="ScanCacheService"/>:
    ///   - ScanCacheService remembers per-file analysis RESULTS keyed by path.
    ///   - SessionRestoreService remembers WHICH files/folders were loaded.
    ///
    /// They pair: when both are enabled, restore reloads the working set and the
    /// scan cache makes the re-scan instant for unchanged files.
    /// </summary>
    public static class SessionRestoreService
    {
        private static readonly string SessionFile =
            AppPaths.AppDataPath("last_session.json");

        // Crash-relaunch breadcrumb. Presence on next start = "we exited because of a crash".
        private static readonly string RecoveryMarker =
            AppPaths.AppDataPath("recovery_pending.json");

        private const int CurrentSchema = 1;

        private static readonly JsonSerializerOptions JsonOpts = new()
        {
            WriteIndented = false,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        };

        public sealed class SessionState
        {
            public int Schema { get; set; } = CurrentSchema;
            public string AppVersion { get; set; } = "";
            public DateTime SavedUtc { get; set; }

            /// <summary>
            /// Top-level paths the user actually added (folders or individual files).
            /// Re-using these on restore picks up new files added since the last session.
            /// </summary>
            public List<string> Roots { get; set; } = new();

            /// <summary>
            /// Resolved file paths currently in the grid. Used as a fallback when a
            /// root is unavailable (offline drive, deleted folder, etc.) or when the
            /// user-chosen file list isn't easily expressible by roots.
            /// </summary>
            public List<string> Files { get; set; } = new();

            public bool WasCleanExit { get; set; }
            public bool WasCrashSnapshot { get; set; }
        }

        public static string SessionFilePath => SessionFile;
        public static string RecoveryMarkerPath => RecoveryMarker;

        public static bool HasSavedSession()
        {
            try { return File.Exists(SessionFile) && new FileInfo(SessionFile).Length > 0; }
            catch { return false; }
        }

        public static SessionState? Load()
        {
            try
            {
                if (!File.Exists(SessionFile)) return null;
                string json = File.ReadAllText(SessionFile);
                if (string.IsNullOrWhiteSpace(json)) return null;
                var state = JsonSerializer.Deserialize<SessionState>(json, JsonOpts);
                return state;
            }
            catch (Exception ex)
            {
                if (ThemeManager.CrashLoggingEnabled) LocalCrashLogger.Write(ex);
                return null;
            }
        }

        /// <summary>
        /// Atomically saves the working set. Call after each import batch finishes,
        /// when the user removes/clears files, on close, and on crash snapshots.
        /// </summary>
        public static void Save(IEnumerable<string> roots, IEnumerable<string> files,
                                bool cleanExit = false, bool crashSnapshot = false)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(SessionFile)!);

                string ver = System.Reflection.Assembly.GetExecutingAssembly()
                                 .GetName().Version?.ToString() ?? "0.0.0";

                var state = new SessionState
                {
                    Schema = CurrentSchema,
                    AppVersion = ver,
                    SavedUtc = DateTime.UtcNow,
                    Roots = roots?.Where(p => !string.IsNullOrWhiteSpace(p))
                                  .Distinct(StringComparer.OrdinalIgnoreCase)
                                  .ToList() ?? new(),
                    Files = files?.Where(p => !string.IsNullOrWhiteSpace(p))
                                  .Distinct(StringComparer.OrdinalIgnoreCase)
                                  .ToList() ?? new(),
                    WasCleanExit = cleanExit,
                    WasCrashSnapshot = crashSnapshot,
                };

                string json = JsonSerializer.Serialize(state, JsonOpts);
                string tmp = SessionFile + ".tmp";
                File.WriteAllText(tmp, json);
                File.Move(tmp, SessionFile, overwrite: true);
            }
            catch (Exception ex)
            {
                if (ThemeManager.CrashLoggingEnabled) LocalCrashLogger.Write(ex);
            }
        }

        public static void Clear()
        {
            try { if (File.Exists(SessionFile)) File.Delete(SessionFile); }
            catch (Exception ex)
            {
                if (ThemeManager.CrashLoggingEnabled) LocalCrashLogger.Write(ex);
            }
        }

        // ── Crash-relaunch breadcrumb ────────────────────────────────────

        public static void WriteRecoveryMarker(string reason)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(RecoveryMarker)!);
                var payload = new
                {
                    Schema = CurrentSchema,
                    UtcStamp = DateTime.UtcNow,
                    Reason = reason ?? "",
                };
                File.WriteAllText(RecoveryMarker, JsonSerializer.Serialize(payload, JsonOpts));
            }
            catch { /* best-effort, never escalate from a crash handler */ }
        }

        public static bool ConsumeRecoveryMarker(out DateTime stampUtc, out string reason)
        {
            stampUtc = default;
            reason = "";
            try
            {
                if (!File.Exists(RecoveryMarker)) return false;
                string json = File.ReadAllText(RecoveryMarker);
                try
                {
                    using var doc = JsonDocument.Parse(json);
                    if (doc.RootElement.TryGetProperty("UtcStamp", out var s) &&
                        s.TryGetDateTime(out var dt)) stampUtc = dt;
                    if (doc.RootElement.TryGetProperty("Reason", out var r))
                        reason = r.GetString() ?? "";
                }
                catch { /* malformed marker still counts as "we crashed" */ }
                File.Delete(RecoveryMarker);
                return true;
            }
            catch { return false; }
        }
    }
}
