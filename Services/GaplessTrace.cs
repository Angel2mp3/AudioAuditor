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

        /// <summary>Logs a single breadcrumb line. Never throws.</summary>
        public static void Log(string message)
        {
            try
            {
                lock (Lock)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
                    File.AppendAllText(
                        LogPath,
                        $"{DateTime.Now:HH:mm:ss.fff} [t{Environment.CurrentManagedThreadId}] {message}{Environment.NewLine}",
                        Encoding.UTF8);
                }
            }
            catch { /* tracing must never break playback */ }
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
