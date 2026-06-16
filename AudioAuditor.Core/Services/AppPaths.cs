using System;
using System.IO;

namespace AudioQualityChecker.Services
{
    public static class AppPaths
    {
        public static string AppDataDirectory => Path.Combine(GetBaseDirectory(Environment.SpecialFolder.ApplicationData, config: true), "AudioAuditor");
        public static string LocalAppDataDirectory => Path.Combine(GetBaseDirectory(Environment.SpecialFolder.LocalApplicationData, config: false), "AudioAuditor");
        public static string DocumentsDirectory => Path.Combine(GetDocumentsBaseDirectory(), "AudioAuditor");

        public static string AppDataPath(params string[] parts) => Combine(AppDataDirectory, parts);
        public static string LocalAppDataPath(params string[] parts) => Combine(LocalAppDataDirectory, parts);
        public static string DocumentsPath(params string[] parts) => Combine(DocumentsDirectory, parts);

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
