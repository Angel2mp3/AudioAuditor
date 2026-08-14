using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;

namespace AudioQualityChecker.Services
{
    /// <summary>
    /// Read/modify/write access to the shared <c>options.txt</c> key=value settings file.
    ///
    /// The same file is written by more than one build of AudioAuditor (WPF, Avalonia, CLI),
    /// and they do not all know the same set of keys. A whole-file rewrite from one build
    /// therefore deletes every key the other builds own — which shows up to the user as
    /// "the app forgot my settings". <see cref="Merge"/> only touches the keys the caller
    /// passes in and leaves everything else — including unrecognised keys, blank lines and
    /// comments — byte-identical.
    /// </summary>
    public static class OptionsFileStore
    {
        private static readonly object _writeLock = new();

        /// <summary>
        /// Applies <paramref name="updates"/> to the file at <paramref name="path"/>, preserving
        /// unknown keys and line order. An update with a <c>null</c> value deletes that key.
        /// Keys absent from the file are appended in the order given.
        /// </summary>
        public static void Merge(string path, IEnumerable<KeyValuePair<string, string?>> updates)
        {
            if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("Path required.", nameof(path));
            if (updates is null) throw new ArgumentNullException(nameof(updates));

            // Last write wins if the caller passes a key twice.
            var pending = new Dictionary<string, string?>(StringComparer.Ordinal);
            var order = new List<string>();
            foreach (var kv in updates)
            {
                if (string.IsNullOrEmpty(kv.Key)) continue;
                if (!pending.ContainsKey(kv.Key)) order.Add(kv.Key);
                pending[kv.Key] = kv.Value;
            }

            lock (_writeLock)
            {
                var existing = ReadAllLinesOrEmpty(path);
                var result = new List<string>(existing.Length + order.Count);
                var written = new HashSet<string>(StringComparer.Ordinal);

                foreach (var line in existing)
                {
                    var key = ParseKey(line);
                    if (key is null || !pending.TryGetValue(key, out var newValue))
                    {
                        // Not ours: an unknown key, a comment, or a blank line. Keep verbatim.
                        result.Add(line);
                        continue;
                    }

                    // Ours. Drop stale duplicates of a key we've already emitted, and drop the
                    // line entirely when the new value is null (an explicit delete).
                    if (written.Add(key) && newValue is not null)
                        result.Add(key + "=" + Flatten(newValue));
                }

                foreach (var key in order)
                {
                    var value = pending[key];
                    if (value is not null && written.Add(key))
                        result.Add(key + "=" + Flatten(value));
                }

                WriteAtomic(path, result);
            }
        }

        /// <summary>
        /// Collapses newlines in a value to spaces. The file is line-based <c>key=value</c>, so a
        /// value carrying a newline writes a second line that the next <see cref="Merge"/> then
        /// preserves forever as an unrecognised key. Free-text settings reach this — the scrobble
        /// blacklist is built from ID3 artist/title, which can contain embedded newlines.
        /// </summary>
        private static string Flatten(string value)
        {
            if (value.IndexOf('\n') < 0 && value.IndexOf('\r') < 0) return value;
            return value.Replace('\r', ' ').Replace('\n', ' ');
        }

        /// <summary>Parses the key of a <c>key=value</c> line, or null if the line is not one.</summary>
        private static string? ParseKey(string line)
        {
            if (string.IsNullOrEmpty(line)) return null;
            int eq = line.IndexOf('=');
            return eq <= 0 ? null : line.Substring(0, eq);
        }

        private static string[] ReadAllLinesOrEmpty(string path)
        {
            try { return File.Exists(path) ? File.ReadAllLines(path) : Array.Empty<string>(); }
            catch { return Array.Empty<string>(); }
        }

        /// <summary>
        /// Writes via a temp file in the same directory, so an interrupted write cannot leave a
        /// truncated settings file behind — the failure mode this whole type exists to prevent.
        ///
        /// The temp name is unique per write because <see cref="_writeLock"/> only covers this
        /// process, and the WPF, Avalonia and CLI builds all target the same options.txt. On a
        /// shared temp path a second process could truncate the file between this process's
        /// write and its move, so the "atomic" move published a half-written file.
        /// </summary>
        private static void WriteAtomic(string path, List<string> lines)
        {
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            var temp = path + "." + Environment.ProcessId + "."
                     + Guid.NewGuid().ToString("N").Substring(0, 8) + ".tmp";
            File.WriteAllLines(temp, lines);

            try
            {
                MoveWithRetry(temp, path);
            }
            catch
            {
                try { if (File.Exists(temp)) File.Delete(temp); } catch { }
                throw;
            }
        }

        /// <summary>
        /// Moves the temp file into place, retrying briefly on IO errors: another build writing
        /// the same options.txt can hold the target open for a moment, and a single failed move
        /// would otherwise drop the user's settings change on the floor.
        /// </summary>
        private static void MoveWithRetry(string temp, string path)
        {
            const int attempts = 5;
            for (int i = 1; ; i++)
            {
                try
                {
                    File.Move(temp, path, overwrite: true);
                    return;
                }
                catch (IOException) when (i < attempts)
                {
                    Thread.Sleep(20 * i);
                }
                catch (UnauthorizedAccessException) when (i < attempts)
                {
                    Thread.Sleep(20 * i);
                }
            }
        }
    }
}
