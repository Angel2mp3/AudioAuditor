using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using SharpCompress.Archives;
using SharpCompress.Common;
using SharpCompress.Readers;

namespace AudioQualityChecker.Services
{
    /// <summary>
    /// Turns whatever the user dropped on the app into a flat list of analysable audio paths:
    /// playlists become the tracks they reference, archives are extracted, cue sheets are passed
    /// through for the analyzer to expand, and everything else is filtered to supported audio.
    ///
    /// Lifted out of the WPF MainWindow (Windows/Analysis.cs) so the Avalonia build gets the same
    /// behaviour instead of a second implementation that would drift.
    /// </summary>
    public static class FileSourceExpander
    {
        internal const long MaxPlaylistBytes = 8L * 1024 * 1024;
        internal const int MaxPlaylistEntries = 10_000;
        internal const int MaxArchiveEntries = 10_000;
        private const long MaxArchiveEntryBytes = 2L * 1024 * 1024 * 1024;
        private const long MaxArchiveTotalBytes = 4L * 1024 * 1024 * 1024;

        /// <summary>
        /// Expands playlist files (.m3u, .m3u8, .pls) into their entries.
        /// Non-playlist paths pass through unchanged; unreadable playlists are skipped.
        /// </summary>
        public static List<string> ExpandPlaylists(IEnumerable<string> paths)
        {
            var result = new List<string>();

            foreach (var path in paths)
            {
                string ext = Path.GetExtension(path);
                if (!SupportedFormats.PlaylistExtensions.Contains(ext) || !File.Exists(path))
                {
                    result.Add(path);
                    continue;
                }

                try
                {
                    if (new FileInfo(path).Length > MaxPlaylistBytes) continue;
                    int resultStart = result.Count;
                    var playlistDir = Path.GetDirectoryName(path) ?? "";
                    var lines = File.ReadLines(path);
                    int entryCount = 0;
                    bool rejected = false;

                    if (ext.Equals(".pls", StringComparison.OrdinalIgnoreCase))
                    {
                        // PLS: File1=path, File2=path, ...
                        foreach (var line in lines)
                        {
                            if (!line.StartsWith("File", StringComparison.OrdinalIgnoreCase) || !line.Contains('='))
                                continue;
                            if (++entryCount > MaxPlaylistEntries) { rejected = true; break; }

                            var entry = line[(line.IndexOf('=') + 1)..].Trim();
                            var resolved = ResolveEntry(entry, playlistDir);
                            if (resolved != null) result.Add(resolved);
                        }
                    }
                    else
                    {
                        // M3U/M3U8: every non-comment, non-empty line is a path
                        foreach (var line in lines)
                        {
                            var trimmed = line.Trim();
                            if (trimmed.Length == 0 || trimmed.StartsWith('#')) continue;
                            if (++entryCount > MaxPlaylistEntries) { rejected = true; break; }

                            var resolved = ResolveEntry(trimmed, playlistDir);
                            if (resolved != null) result.Add(resolved);
                        }
                    }
                    if (rejected) result.RemoveRange(resultStart, result.Count - resultStart);
                }
                catch
                {
                    // Unreadable playlist — nothing to contribute, and failing the whole
                    // drop because one playlist is broken would be worse.
                }
            }

            return result;
        }

        /// <summary>
        /// Resolves one playlist entry to a local file, or null when it is a stream URL or
        /// points at something that is not there.
        /// </summary>
        internal static string? ResolveEntry(string entry, string baseDir)
        {
            if (entry.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                entry.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                return null;

            if (Path.IsPathRooted(entry) && File.Exists(entry))
                return entry;

            var combined = Path.Combine(baseDir, entry);
            return File.Exists(combined) ? Path.GetFullPath(combined) : null;
        }

        /// <summary>
        /// Extracts audio out of archives into a temp directory and returns those paths, along
        /// with any supported audio and cue sheets passed straight through. Anything else is
        /// dropped. Corrupt archives are skipped.
        /// </summary>
        public static List<string> ExtractAudioFromArchives(IEnumerable<string> paths)
        {
            var result = new List<string>();

            foreach (var path in paths)
            {
                string ext = Path.GetExtension(path);

                if (SupportedFormats.ArchiveExtensions.Contains(ext) && File.Exists(path))
                {
                    try
                    {
                        result.AddRange(ExtractOne(path, ext));
                    }
                    catch
                    {
                        // Corrupt or unsupported archive — skip it, keep the rest of the drop.
                    }
                }
                else if (SupportedFormats.AudioExtensions.Contains(ext))
                {
                    result.Add(path);
                }
                else if (ext.Equals(".cue", StringComparison.OrdinalIgnoreCase) && File.Exists(path))
                {
                    // Passed through; the analysis pass expands it into per-track entries.
                    result.Add(path);
                }
            }

            return result;
        }

        private static IEnumerable<string> ExtractOne(string archivePath, string ext)
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "AudioAuditor_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            var extracted = new List<string>();
            var claimed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            long totalBytes = 0;

            try
            {
                if (ext.Equals(".zip", StringComparison.OrdinalIgnoreCase))
                {
                    using var archive = ZipFile.OpenRead(archivePath);
                    if (archive.Entries.Count > MaxArchiveEntries)
                        throw new InvalidDataException("Archive contains too many entries.");

                    foreach (var entry in archive.Entries)
                    {
                        if (string.IsNullOrEmpty(entry.Name) || IsZipSymbolicLink(entry)) continue;
                        if (!SupportedFormats.AudioExtensions.Contains(Path.GetExtension(entry.Name))) continue;
                        if (entry.Length > MaxArchiveEntryBytes)
                            throw new InvalidDataException("Archive entry is too large.");

                        string destination = ClaimDestination(claimed, GetSafeDestination(tempDir, entry.FullName));
                        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                        using var source = entry.Open();
                        ExtractBounded(source, destination, ref totalBytes);
                        extracted.Add(destination);
                    }
                }
                else
                {
                    using var archive = ArchiveFactory.OpenArchive(archivePath, new ReaderOptions());
                    int entryCount = 0;

                    foreach (var entry in archive.Entries)
                    {
                        if (++entryCount > MaxArchiveEntries)
                            throw new InvalidDataException("Archive contains too many entries.");
                        if (entry.IsDirectory || entry.Key == null || entry.LinkTarget != null) continue;
                        if (!SupportedFormats.AudioExtensions.Contains(Path.GetExtension(entry.Key))) continue;
                        if (entry.Size > MaxArchiveEntryBytes)
                            throw new InvalidDataException("Archive entry is too large.");

                        string destination = ClaimDestination(claimed, GetSafeDestination(tempDir, entry.Key));
                        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                        using var source = entry.OpenEntryStream();
                        ExtractBounded(source, destination, ref totalBytes);
                        extracted.Add(destination);
                    }
                }

                return extracted;
            }
            catch
            {
                try { Directory.Delete(tempDir, recursive: true); } catch { }
                throw;
            }
        }

        private static string GetSafeDestination(string tempDir, string entryKey)
        {
            string safeBase = Path.GetFullPath(tempDir) + Path.DirectorySeparatorChar;
            string relative = entryKey.Replace('/', Path.DirectorySeparatorChar);
            string destination = Path.GetFullPath(Path.Combine(tempDir, relative));
            if (!destination.StartsWith(safeBase, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Archive entry escapes the extraction directory.");
            return destination;
        }

        /// <summary>
        /// Makes a validated destination unique within this archive. Zip permits duplicate entry
        /// names, and two entries differing only in case collide on a Windows filesystem. Extraction
        /// opens with <see cref="FileMode.CreateNew"/> — kept deliberately, since it is what
        /// guarantees an entry can never overwrite something already written — so a collision threw
        /// and the outer handler rejected the *entire* archive. Suffixing the stem keeps both files
        /// instead of losing the whole import to an ordinary duplicate name.
        ///
        /// Runs after <see cref="GetSafeDestination"/>, so the escape check still applies to the
        /// path the archive actually asked for.
        /// </summary>
        private static string ClaimDestination(HashSet<string> claimed, string destination)
        {
            if (claimed.Add(destination)) return destination;

            string dir = Path.GetDirectoryName(destination) ?? "";
            string stem = Path.GetFileNameWithoutExtension(destination);
            string ext = Path.GetExtension(destination);

            for (int n = 2; ; n++)
            {
                string candidate = Path.Combine(dir, $"{stem} ({n}){ext}");
                if (claimed.Add(candidate)) return candidate;
            }
        }

        private static bool IsZipSymbolicLink(ZipArchiveEntry entry) =>
            ((entry.ExternalAttributes >> 16) & 0xF000) == 0xA000;

        private static void ExtractBounded(Stream source, string destination, ref long totalBytes)
        {
            try
            {
                using var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                var buffer = new byte[81920];
                long entryBytes = 0;
                int read;
                while ((read = source.Read(buffer, 0, buffer.Length)) > 0)
                {
                    entryBytes += read;
                    totalBytes += read;
                    if (entryBytes > MaxArchiveEntryBytes || totalBytes > MaxArchiveTotalBytes)
                        throw new InvalidDataException("Archive expands beyond the allowed size.");
                    output.Write(buffer, 0, read);
                }
            }
            catch
            {
                try { File.Delete(destination); } catch { }
                throw;
            }
        }
    }
}
