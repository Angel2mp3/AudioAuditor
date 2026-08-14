using System;
using System.Collections.Generic;
using System.IO;
using AudioQualityChecker.Models;

namespace AudioQualityChecker.Services
{
    /// <summary>
    /// Turns a parsed cue sheet plus its analyzed source file into one
    /// <see cref="AudioFileInfo"/> per track.
    ///
    /// The tracks are "virtual": they all point at the same audio file and carry a start/end
    /// offset into it, with a synthetic <c>FilePath</c> of <c>{audioPath}#CUE{n}</c> so the grid
    /// and the player can tell them apart.
    /// </summary>
    public static class CueVirtualTracks
    {
        /// <summary>Synthetic path identifying track <paramref name="trackNumber"/> of a cue source.</summary>
        public static string TrackId(string audioPath, int trackNumber) => $"{audioPath}#CUE{trackNumber}";

        /// <summary>
        /// Builds the virtual tracks for <paramref name="sheet"/> against its analyzed
        /// <paramref name="parent"/>. Tracks whose id is already in <paramref name="existingIds"/>,
        /// and any that work out to a non-positive duration, are skipped.
        /// </summary>
        public static List<AudioFileInfo> Build(CueSheet sheet, AudioFileInfo parent,
            ICollection<string>? existingIds = null)
        {
            var tracks = new List<AudioFileInfo>();
            string audioPath = sheet.AudioFilePath;

            foreach (var track in sheet.Tracks)
            {
                // The last track has no following INDEX to bound it, so it runs to the end
                // of the source file.
                var endTime = track.EndTime > TimeSpan.Zero
                    ? track.EndTime
                    : TimeSpan.FromSeconds(parent.DurationSeconds);

                var duration = endTime - track.StartTime;
                if (duration.TotalSeconds <= 0) continue;

                string trackId = TrackId(audioPath, track.TrackNumber);
                if (existingIds != null && existingIds.Contains(trackId)) continue;

                tracks.Add(new AudioFileInfo
                {
                    FilePath = trackId,
                    FileName = $"[{track.TrackNumber:D2}] {(string.IsNullOrEmpty(track.Title) ? Path.GetFileNameWithoutExtension(audioPath) : track.Title)}",
                    FolderPath = parent.FolderPath,
                    Title = track.Title,
                    Artist = !string.IsNullOrEmpty(track.Performer) ? track.Performer : parent.Artist,
                    Extension = parent.Extension,
                    SampleRate = parent.SampleRate,
                    BitsPerSample = parent.BitsPerSample,
                    Channels = parent.Channels,
                    ReportedBitrate = parent.ReportedBitrate,
                    ActualBitrate = parent.ActualBitrate,
                    EffectiveFrequency = parent.EffectiveFrequency,
                    Duration = FormatDuration(duration),
                    DurationSeconds = duration.TotalSeconds,
                    FileSize = parent.FileSize,
                    FileSizeBytes = parent.FileSizeBytes,
                    DateModified = parent.DateModified,
                    DateCreated = parent.DateCreated,
                    Status = parent.Status,
                    IsCueVirtualTrack = true,
                    CueSheetPath = audioPath,
                    CueTrackNumber = track.TrackNumber,
                    CueStartTime = track.StartTime,
                    CueEndTime = endTime,
                });
            }

            return tracks;
        }

        private static string FormatDuration(TimeSpan duration) => duration.TotalHours >= 1
            ? $"{(int)duration.TotalHours}:{duration.Minutes:D2}:{duration.Seconds:D2}"
            : $"{duration.Minutes}:{duration.Seconds:D2}";
    }
}
