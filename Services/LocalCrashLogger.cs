using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace AudioQualityChecker.Services
{
    /// <summary>
    /// Writes unhandled exception details to local text files.
    /// Only active when the user explicitly opts in via Settings.
    /// All data stays on the user's PC — nothing is transmitted.
    /// </summary>
    public static class LocalCrashLogger
    {
        private static readonly string CrashLogDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "AudioAuditor", "crash-logs");

        private const int MaxLogFiles = 20;

        /// <summary>
        /// Optional hook a UI component can set to contribute live, non-private app state
        /// to crash logs (e.g. "Analyzing=true; Files=128"). Kept as a delegate so this
        /// logger has no dependency on the WPF window and stays safe to call from anywhere.
        /// Must never throw and must never return file paths or audio metadata.
        /// </summary>
        public static Func<string?>? AppStateProvider;

        public static void EnsureDirectory()
        {
            if (!Directory.Exists(CrashLogDir))
                Directory.CreateDirectory(CrashLogDir);
        }

        public static void Write(Exception ex) => Write(ex, null);

        /// <param name="channel">
        /// Which crash channel caught this — e.g. "UI thread (Dispatcher)", "Background task",
        /// or "AppDomain". Helps tell a recoverable UI-thread fault from a fatal one.
        /// </param>
        public static void Write(Exception ex, string? channel)
        {
            if (ex == null) return;
            try
            {
                EnsureDirectory();
                PurgeOldLogs();

                string timestamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
                string fileName = $"crash-{timestamp}.txt";
                string filePath = Path.Combine(CrashLogDir, fileName);

                var sb = new StringBuilder();
                sb.AppendLine("══════════════════════════════════════════");
                sb.AppendLine("  AudioAuditor Local Crash Log");
                sb.AppendLine("══════════════════════════════════════════");
                sb.AppendLine("// No file paths, user data, or audio metadata are collected.");
                sb.AppendLine($"Timestamp:   {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                sb.AppendLine($"Version:     {Assembly.GetExecutingAssembly().GetName().Version}");
                sb.AppendLine($"OS:          {Environment.OSVersion}");
                sb.AppendLine($"64-bit OS:   {Environment.Is64BitOperatingSystem}");
                sb.AppendLine($"64-bit Proc: {Environment.Is64BitProcess}");
                sb.AppendLine($"CLR:         {Environment.Version}");
                sb.AppendLine("──────────────────────────────────────────");
                sb.AppendLine($"Channel:     {channel ?? "(unspecified)"}");
                sb.AppendLine($"Uptime:      {DescribeUptime()}");
                sb.AppendLine($"Theme:       {SafeTheme()}");
                sb.AppendLine($"Memory:      {DescribeMemory()}");
                sb.AppendLine($"Thread:      {DescribeThread()}");
                string? appState = SafeAppState();
                if (!string.IsNullOrWhiteSpace(appState))
                    sb.AppendLine($"App state:   {Sanitize(appState)}");
                sb.AppendLine("──────────────────────────────────────────");
                sb.AppendLine($"Exception:   {ex.GetType().FullName}");
                sb.AppendLine($"Message:     {Sanitize(ex.Message)}");
                sb.AppendLine("──────────────────────────────────────────");
                sb.AppendLine("Stack Trace:");
                sb.AppendLine(Sanitize(ex.StackTrace) ?? "(none)");

                if (ex.InnerException != null)
                {
                    sb.AppendLine("──────────────────────────────────────────");
                    sb.AppendLine("Inner Exception:");
                    sb.AppendLine($"  Type:    {ex.InnerException.GetType().FullName}");
                    sb.AppendLine($"  Message: {Sanitize(ex.InnerException.Message)}");
                    sb.AppendLine($"  Stack:   {Sanitize(ex.InnerException.StackTrace) ?? "(none)"}");
                }

                sb.AppendLine("══════════════════════════════════════════");

                File.WriteAllText(filePath, sb.ToString(), Encoding.UTF8);
            }
            catch
            {
                // If crash logging itself fails, silently fail — don't crash the crash handler
            }
        }

        // ── Non-private diagnostic context helpers ───────────────────────
        // Each is wrapped so a failure here can never break crash logging.

        private static string DescribeUptime()
        {
            try
            {
                var span = DateTime.Now - Process.GetCurrentProcess().StartTime;
                if (span.TotalSeconds < 0) span = TimeSpan.Zero;
                return span.TotalHours >= 1
                    ? $"{(int)span.TotalHours}h {span.Minutes}m {span.Seconds}s"
                    : span.TotalMinutes >= 1
                        ? $"{span.Minutes}m {span.Seconds}s"
                        : $"{span.Seconds}s";
            }
            catch { return "(unknown)"; }
        }

        private static string SafeTheme()
        {
            try { return ThemeManager.CurrentTheme ?? "(unknown)"; }
            catch { return "(unknown)"; }
        }

        private static string DescribeMemory()
        {
            try
            {
                long managed = GC.GetTotalMemory(forceFullCollection: false);
                long working = Process.GetCurrentProcess().WorkingSet64;
                return $"managed {managed / (1024 * 1024)} MB, working set {working / (1024 * 1024)} MB";
            }
            catch { return "(unknown)"; }
        }

        private static string DescribeThread()
        {
            try
            {
                var t = Thread.CurrentThread;
                string name = string.IsNullOrEmpty(t.Name) ? "(unnamed)" : t.Name!;
                return $"#{Environment.CurrentManagedThreadId} {name}{(t.IsThreadPoolThread ? " [pool]" : "")}";
            }
            catch { return "(unknown)"; }
        }

        private static string? SafeAppState()
        {
            try { return AppStateProvider?.Invoke(); }
            catch { return null; }
        }

        /// <summary>
        /// Strips potential PII from exception strings: local file paths,
        /// user profile paths, and UNC paths are replaced with [REDACTED].
        /// </summary>
        private static string Sanitize(string? input)
        {
            if (string.IsNullOrEmpty(input)) return input ?? "";

            // Replace Windows absolute paths (C:\..., D:\..., etc.)
            string result = Regex.Replace(input, "[A-Za-z]:\\\\[^\\s\\n\\r\"<>|]*", "[REDACTED]");

            // Replace UNC paths (\\server\share\...)
            result = Regex.Replace(result, "\\\\\\\\[^\\s\\n\\r\"<>|]*", "[REDACTED]");

            // Replace %USERPROFILE% and other env-var style paths
            result = Regex.Replace(result, "%[A-Za-z_]+%[^\\s\\n\\r\"<>|]*", "[REDACTED]");

            // Replace Unix-style home paths (/home/..., ~/...)
            result = Regex.Replace(result, "(/home/[^\\s\\n\\r\"<>|]*|~/[^\\s\\n\\r\"<>|]*)", "[REDACTED]");

            return result;
        }

        public static void PurgeOldLogs()
        {
            try
            {
                if (!Directory.Exists(CrashLogDir)) return;
                var files = Directory.GetFiles(CrashLogDir, "crash-*.txt")
                    .Select(f => new FileInfo(f))
                    .OrderByDescending(f => f.CreationTime)
                    .Skip(MaxLogFiles)
                    .ToList();
                foreach (var f in files)
                {
                    try { f.Delete(); } catch { }
                }
            }
            catch { }
        }

        public static string GetLogDirectory() => CrashLogDir;
    }
}
