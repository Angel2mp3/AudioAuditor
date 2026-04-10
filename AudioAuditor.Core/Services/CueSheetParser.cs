using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

namespace AudioQualityChecker.Services
{
    public class CueTrack
    {
        public int TrackNumber { get; set; }
        public string Title { get; set; } = "";
        public string Performer { get; set; } = "";
        public TimeSpan StartTime { get; set; }
        public TimeSpan EndTime { get; set; } // Zero if last track (fills to end)
        public TimeSpan Duration => EndTime > StartTime ? EndTime - StartTime : TimeSpan.Zero;
    }

    public class CueSheet
    {
        public string Title { get; set; } = "";
        public string Performer { get; set; } = "";
        public string AudioFilePath { get; set; } = ""; // resolved absolute path
        public string AudioFileName { get; set; } = ""; // from FILE directive
        public List<CueTrack> Tracks { get; } = new();
    }

    public static class CueSheetParser
    {
        private static readonly Regex QuotedString = new(@"""([^""]*)""|(\S+)", RegexOptions.Compiled);

        /// <summary>
        /// Parse a .cue file and resolve the referenced audio file path.
        /// Returns null if the cue file is invalid or the audio file can't be found.
        /// </summary>
        public static CueSheet? Parse(string cuePath)
        {
            if (!File.Exists(cuePath)) return null;

            string[] lines;
            try { lines = File.ReadAllLines(cuePath); }
            catch { return null; }

            var sheet = new CueSheet();
            string cueDir = Path.GetDirectoryName(cuePath) ?? "";
            CueTrack? currentTrack = null;

            foreach (var rawLine in lines)
            {
                string line = rawLine.Trim();
                if (line.Length == 0) continue;

                if (line.StartsWith("REM ", StringComparison.OrdinalIgnoreCase))
                    continue;

                if (line.StartsWith("TITLE ", StringComparison.OrdinalIgnoreCase))
                {
                    string val = ExtractQuoted(line, 6);
                    if (currentTrack != null)
                        currentTrack.Title = val;
                    else
                        sheet.Title = val;
                }
                else if (line.StartsWith("PERFORMER ", StringComparison.OrdinalIgnoreCase))
                {
                    string val = ExtractQuoted(line, 10);
                    if (currentTrack != null)
                        currentTrack.Performer = val;
                    else
                        sheet.Performer = val;
                }
                else if (line.StartsWith("FILE ", StringComparison.OrdinalIgnoreCase))
                {
                    // FILE "filename.flac" WAVE
                    string val = ExtractQuoted(line, 5);
                    sheet.AudioFileName = val;

                    // Resolve relative to cue file location
                    string resolved = Path.Combine(cueDir, val);
                    if (File.Exists(resolved))
                        sheet.AudioFilePath = resolved;
                    else
                    {
                        // Try same name with common extensions
                        string baseName = Path.GetFileNameWithoutExtension(val);
                        foreach (var ext in new[] { ".flac", ".wav", ".ape", ".wv", ".mp3", ".ogg", ".m4a" })
                        {
                            string candidate = Path.Combine(cueDir, baseName + ext);
                            if (File.Exists(candidate))
                            {
                                sheet.AudioFilePath = candidate;
                                break;
                            }
                        }
                    }
                }
                else if (line.StartsWith("TRACK ", StringComparison.OrdinalIgnoreCase))
                {
                    // TRACK 01 AUDIO
                    var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length >= 2 && int.TryParse(parts[1], out int trackNum))
                    {
                        currentTrack = new CueTrack
                        {
                            TrackNumber = trackNum,
                            Performer = sheet.Performer // inherit album performer
                        };
                        sheet.Tracks.Add(currentTrack);
                    }
                }
                else if (line.StartsWith("INDEX 01 ", StringComparison.OrdinalIgnoreCase) ||
                         line.StartsWith("INDEX 1 ", StringComparison.OrdinalIgnoreCase))
                {
                    // INDEX 01 MM:SS:FF (frames are 1/75 sec)
                    if (currentTrack != null)
                    {
                        string timeStr = line.Substring(line.LastIndexOf(' ') + 1);
                        currentTrack.StartTime = ParseCueTime(timeStr);
                    }
                }
            }

            // Calculate end times: each track ends where the next begins
            for (int i = 0; i < sheet.Tracks.Count - 1; i++)
                sheet.Tracks[i].EndTime = sheet.Tracks[i + 1].StartTime;
            // Last track: EndTime stays Zero (means "to end of file")

            if (string.IsNullOrEmpty(sheet.AudioFilePath) || sheet.Tracks.Count == 0)
                return null;

            return sheet;
        }

        /// <summary>
        /// Parse CUE time format MM:SS:FF where FF = frames (1/75 second).
        /// </summary>
        private static TimeSpan ParseCueTime(string time)
        {
            var parts = time.Split(':');
            if (parts.Length != 3) return TimeSpan.Zero;

            if (int.TryParse(parts[0], out int min) &&
                int.TryParse(parts[1], out int sec) &&
                int.TryParse(parts[2], out int frames))
            {
                double totalMs = (min * 60 + sec) * 1000.0 + (frames / 75.0 * 1000.0);
                return TimeSpan.FromMilliseconds(totalMs);
            }

            return TimeSpan.Zero;
        }

        private static string ExtractQuoted(string line, int offset)
        {
            string rest = line.Substring(offset).Trim();
            if (rest.StartsWith('"'))
            {
                int end = rest.IndexOf('"', 1);
                if (end > 0)
                    return rest.Substring(1, end - 1);
            }
            // No quotes — take everything up to end (or next keyword)
            return rest.TrimEnd();
        }
    }
}
