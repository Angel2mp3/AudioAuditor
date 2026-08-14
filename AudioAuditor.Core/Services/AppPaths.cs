using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;

namespace AudioQualityChecker.Services
{
    public static class AppPaths
    {
        public static string AppDataDirectory => Path.Combine(GetBaseDirectory(Environment.SpecialFolder.ApplicationData, config: true), "AudioAuditor");
        public static string LocalAppDataDirectory => Path.Combine(GetBaseDirectory(Environment.SpecialFolder.LocalApplicationData, config: false), "AudioAuditor");
        public static string DocumentsDirectory => Path.Combine(GetDocumentsBaseDirectory(), "AudioAuditor");

        public static string AppDataPath(params string[] parts) => Combine(AppDataDirectory, parts);

        /// <summary>
        /// The folder holding the app's own executable — stable across runs, and the only sane place
        /// to tell a user "drop the binary here". Falls back to <see cref="AppContext.BaseDirectory"/>
        /// when the process is hosted (<c>dotnet run</c>, the test host), because then
        /// <see cref="Environment.ProcessPath"/> points at the host, not at this app.
        /// </summary>
        public static string ExecutableDirectory { get; } = ResolveExecutableDirectory();

        /// <summary>
        /// Where to look for a bundled sidecar tool (ffmpeg, cambia), nearest first.
        ///
        /// A single-file publish with <c>IncludeAllContentForSelfExtract</c> unpacks its bundled
        /// content to a temp folder, and that is what <see cref="AppContext.BaseDirectory"/> becomes —
        /// while the exe stays wherever the user put it, which is where a hand-placed binary lives.
        /// Neither directory alone covers both cases, so probe both.
        /// </summary>
        public static IReadOnlyList<string> SidecarSearchDirectories { get; } = BuildSidecarSearchDirectories();

        private static string ResolveExecutableDirectory()
        {
            string fallback = TrimSeparator(AppContext.BaseDirectory);

            string? exe = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(exe)) return fallback;

            // Only trust ProcessPath when it is this app's own apphost. Under `dotnet run` or the
            // xUnit test host it names dotnet.exe / testhost.exe, whose folder holds nothing of ours.
            string? entry = Assembly.GetEntryAssembly()?.GetName().Name;
            if (entry != null &&
                !Path.GetFileNameWithoutExtension(exe).Equals(entry, StringComparison.OrdinalIgnoreCase))
                return fallback;

            string? dir = Path.GetDirectoryName(exe);
            return string.IsNullOrWhiteSpace(dir) ? fallback : TrimSeparator(dir);
        }

        private static IReadOnlyList<string> BuildSidecarSearchDirectories()
        {
            var dirs = new List<string>(2) { ExecutableDirectory };

            string baseDir = TrimSeparator(AppContext.BaseDirectory);
            if (!string.IsNullOrWhiteSpace(baseDir) &&
                !baseDir.Equals(ExecutableDirectory, StringComparison.OrdinalIgnoreCase))
                dirs.Add(baseDir);

            return dirs;
        }

        private static string TrimSeparator(string path) =>
            string.IsNullOrEmpty(path)
                ? path
                : path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        private static string GetBaseDirectory(Environment.SpecialFolder folder, bool config)
        {
            var special = Environment.GetFolderPath(folder);
            if (!string.IsNullOrWhiteSpace(special))
                return special;

            if (!OperatingSystem.IsWindows())
            {
                var xdg = Environment.GetEnvironmentVariable(config ? "XDG_CONFIG_HOME" : "XDG_CACHE_HOME");
                if (!string.IsNullOrWhiteSpace(xdg))
                    return xdg;

                var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                if (!string.IsNullOrWhiteSpace(home))
                    return Path.Combine(home, config ? ".config" : ".cache");
            }

            return AppContext.BaseDirectory;
        }

        private static string GetDocumentsBaseDirectory()
        {
            var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            if (!string.IsNullOrWhiteSpace(documents))
                return documents;

            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (!string.IsNullOrWhiteSpace(home))
                return OperatingSystem.IsWindows() ? home : Path.Combine(home, "Documents");

            return AppContext.BaseDirectory;
        }

        private static string Combine(string root, string[] parts)
        {
            if (parts.Length == 0)
                return root;

            var combined = root;
            foreach (var part in parts)
                combined = Path.Combine(combined, part);
            return combined;
        }
    }
}
