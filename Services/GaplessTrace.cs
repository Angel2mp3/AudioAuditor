using System;
using System.IO;
using System.Text;

namespace AudioQualityChecker.Services
{
    /// <summary>
    /// TEMPORARY diagnostic tracer for the gapless track-switch → UI-update path.
    /// Writes filename-only breadcrumbs to %AppData%\AudioAuditor\gapless-trace.log so we can
    /// see exactly which step fails when the UI doesn't follow a seamless gapless transition.
    /// No full paths / metadata are logged. Remove once the gapless UI bug is fixed.
    /// </summary>
    internal static class GaplessTrace
    {
        private static readonly string LogPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "AudioAuditor", "gapless-trace.log");

        private static readonly object Lock = new();

        /// <summary>Rotate past this size so the trace can't grow without bound.</summary>
        private const long MaxLogBytes = 1024 * 1024;

        /// <summary>
        /// Logs a single breadcrumb line. Never throws.
        ///
        /// Gated on the same opt-in as <see cref="LocalCrashLogger"/>: this used to write on every
        /// gapless transition for every user, forever, with no way to turn it off and no size cap.
        /// A diagnostic nobody asked for shouldn't be accumulating on their disk.
        /// </summary>
        public static void Log(string message)
        {
            if (!AudioAuditorSettings.CrashLoggingEnabled) return;

            try
            {
                lock (Lock)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
                    RotateIfLarge();
                    File.AppendAllText(
                        LogPath,
                        $"{DateTime.Now:HH:mm:ss.fff} [t{Environment.CurrentManagedThreadId}] {message}{Environment.NewLine}",
                        Encoding.UTF8);
                }
            }
            catch { /* tracing must never break playback */ }
        }

        /// <summary>
        /// Keeps one previous generation as <c>.1</c> and starts fresh, so a trace covering the bug
        /// being chased survives while the total stays bounded at ~2 MB. Caller holds the lock.
        /// </summary>
        private static void RotateIfLarge()
        {
            try
            {
                var info = new FileInfo(LogPath);
                if (!info.Exists || info.Length < MaxLogBytes) return;

                string previous = LogPath + ".1";
                if (File.Exists(previous)) File.Delete(previous);
                File.Move(LogPath, previous);
            }
            catch { }
        }

        /// <summary>Safe filename extraction for logging — never emits a full path.</summary>
        public static string Name(string? path)
        {
            if (string.IsNullOrEmpty(path)) return "(null)";
            try { return Path.GetFileName(path); } catch { return "(badpath)"; }
        }

        public static string LogFilePath => LogPath;
    }
}
