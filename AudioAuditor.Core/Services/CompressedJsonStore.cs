using System;
using System.IO;
using System.IO.Compression;
using System.Text.Json;

namespace AudioQualityChecker.Services
{
    /// <summary>
    /// Reads and writes gzipped JSON for the app's own data files (scan cache, stats, colour cache).
    ///
    /// These files are plain JSON on disk and grow with the user's library — a 50,000-entry scan
    /// cache is tens of megabytes of extremely repetitive text. Gzip takes ~90% off it, and the
    /// cost is noise next to the <see cref="JsonSerializer"/> pass both paths pay anyway.
    ///
    /// Loading sniffs the gzip magic bytes rather than trusting the file extension, so a file that
    /// was hand-copied, half-migrated, or restored from a backup under the wrong name still reads.
    /// Nothing here throws: every caller treats a failure as "no data yet" and rebuilds, and that
    /// contract must not change.
    /// </summary>
    public static class CompressedJsonStore
    {
        // Deflate at Fastest gets 88% where Optimal gets 92%, for ~40% less CPU. On a file this
        // repetitive the last few percent are not worth the wait on a save that blocks the caller.
        private const CompressionLevel Level = CompressionLevel.Fastest;

        private const byte GzipMagic0 = 0x1F;
        private const byte GzipMagic1 = 0x8B;

        /// <summary>
        /// Serialises <paramref name="value"/> as gzipped JSON to <paramref name="gzPath"/>.
        /// Writes to a sibling temp file and moves it into place, so an interrupted or failed write
        /// leaves the previous good file untouched instead of a truncated one.
        /// </summary>
        /// <returns>True if the file was written.</returns>
        public static bool Save<T>(string gzPath, T value, JsonSerializerOptions? options = null)
        {
            if (string.IsNullOrEmpty(gzPath)) return false;

            string temp = gzPath + ".tmp";
            try
            {
                var dir = Path.GetDirectoryName(gzPath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                using (var file = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
                using (var gzip = new GZipStream(file, Level))
                {
                    JsonSerializer.Serialize(gzip, value, options);
                }

                File.Move(temp, gzPath, overwrite: true);
                return true;
            }
            catch
            {
                try { if (File.Exists(temp)) File.Delete(temp); } catch { }
                return false;
            }
        }

        /// <summary>
        /// Loads <paramref name="gzPath"/>, falling back to <paramref name="legacyPlainPath"/> (the
        /// pre-compression file) when the gzipped one is absent. Either path may hold gzipped or
        /// plain JSON — the content decides, not the name.
        /// </summary>
        /// <returns>The deserialised value, or <c>default</c> if neither file exists or is readable.</returns>
        public static T? Load<T>(string gzPath, string? legacyPlainPath = null, JsonSerializerOptions? options = null)
        {
            var result = LoadFile<T>(gzPath, options);
            if (result is not null) return result;

            if (!string.IsNullOrEmpty(legacyPlainPath) &&
                !string.Equals(legacyPlainPath, gzPath, StringComparison.OrdinalIgnoreCase))
            {
                return LoadFile<T>(legacyPlainPath, options);
            }

            return default;
        }

        /// <summary>
        /// Uncompressed byte count of a gzipped file, without decompressing the whole thing — gzip
        /// stores it in the last four bytes (mod 2^32). Returns 0 when unknown. Used to reject a
        /// cache that would balloon in memory before any of it is parsed.
        /// </summary>
        public static long GetUncompressedSize(string path)
        {
            try
            {
                var info = new FileInfo(path);
                if (!info.Exists || info.Length < 18) return 0;

                using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
                Span<byte> head = stackalloc byte[2];
                if (fs.Read(head) != 2 || head[0] != GzipMagic0 || head[1] != GzipMagic1) return 0;

                fs.Seek(-4, SeekOrigin.End);
                Span<byte> tail = stackalloc byte[4];
                if (fs.Read(tail) != 4) return 0;

                return (uint)(tail[0] | (tail[1] << 8) | (tail[2] << 16) | (tail[3] << 24));
            }
            catch { return 0; }
        }

        /// <summary>True when the file starts with the gzip magic bytes.</summary>
        public static bool IsGzip(string path)
        {
            try
            {
                using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
                Span<byte> head = stackalloc byte[2];
                return fs.Read(head) == 2 && head[0] == GzipMagic0 && head[1] == GzipMagic1;
            }
            catch { return false; }
        }

        private static T? LoadFile<T>(string path, JsonSerializerOptions? options)
        {
            try
            {
                if (string.IsNullOrEmpty(path) || !File.Exists(path)) return default;

                using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
                if (file.Length == 0) return default;

                // Peek at the first two bytes and rewind, so one code path handles both the
                // migrated .gz file and any plain-JSON file left over from an older build.
                Span<byte> head = stackalloc byte[2];
                bool gzipped = file.Read(head) == 2 && head[0] == GzipMagic0 && head[1] == GzipMagic1;
                file.Position = 0;

                if (!gzipped)
                    return JsonSerializer.Deserialize<T>(file, options);

                using var gzip = new GZipStream(file, CompressionMode.Decompress);
                return JsonSerializer.Deserialize<T>(gzip, options);
            }
            catch
            {
                // Truncated gzip, malformed JSON, locked file — all mean "no usable data", which
                // every caller already handles by starting empty.
                return default;
            }
        }
    }
}
