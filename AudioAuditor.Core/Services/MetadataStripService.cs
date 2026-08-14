using System;
using System.Collections.Generic;

namespace AudioQualityChecker.Services
{
    /// <summary>Tag fields the strip tool can clear, combinable.</summary>
    [Flags]
    public enum StripFields
    {
        None = 0,
        Title = 1 << 0,
        Artist = 1 << 1,
        Album = 1 << 2,
        AlbumArtist = 1 << 3,
        Year = 1 << 4,

        /// <summary>Track number, and the disc number alongside it.</summary>
        TrackNumber = 1 << 5,

        Genre = 1 << 6,
        Composer = 1 << 7,
        Conductor = 1 << 8,
        Comment = 1 << 9,
        Lyrics = 1 << 10,
        Copyright = 1 << 11,
        Cover = 1 << 12,
        ReplayGain = 1 << 13,

        All = (1 << 14) - 1
    }

    /// <summary>
    /// Clears selected tag fields without touching the rest.
    ///
    /// Lifted out of the WPF strip window (Windows/MetadataStripWindow.xaml.cs) so both
    /// front-ends clear exactly the same things — Replay Gain in particular lives in three
    /// different tag formats and is easy to get half-right in a second copy.
    /// </summary>
    public static class MetadataStripService
    {
        /// <summary>Replay Gain keys, as written by every tagger that emits them.</summary>
        private static readonly string[] ReplayGainKeys =
        {
            "REPLAYGAIN_TRACK_GAIN",
            "REPLAYGAIN_TRACK_PEAK",
            "REPLAYGAIN_ALBUM_GAIN",
            "REPLAYGAIN_ALBUM_PEAK",
        };

        /// <summary>Clears <paramref name="fields"/> from the file. Throws if it cannot be written.</summary>
        public static void Strip(string filePath, StripFields fields)
        {
            if (fields == StripFields.None) return;

            using var tagFile = TagLib.File.Create(filePath);
            var tag = tagFile.Tag;

            if (fields.HasFlag(StripFields.Title)) tag.Title = null;
            if (fields.HasFlag(StripFields.Artist)) tag.Performers = Array.Empty<string>();
            if (fields.HasFlag(StripFields.Album)) tag.Album = null;
            if (fields.HasFlag(StripFields.AlbumArtist)) tag.AlbumArtists = Array.Empty<string>();
            if (fields.HasFlag(StripFields.Year)) tag.Year = 0;

            if (fields.HasFlag(StripFields.TrackNumber))
            {
                // Disc goes with track: they are the same "where does this sit in the release"
                // information, and leaving a bare disc number behind is never what was wanted.
                tag.Track = 0;
                tag.Disc = 0;
            }

            if (fields.HasFlag(StripFields.Genre)) tag.Genres = Array.Empty<string>();
            if (fields.HasFlag(StripFields.Composer)) tag.Composers = Array.Empty<string>();
            if (fields.HasFlag(StripFields.Conductor)) tag.Conductor = null;
            if (fields.HasFlag(StripFields.Comment)) tag.Comment = null;
            if (fields.HasFlag(StripFields.Lyrics)) tag.Lyrics = null;
            if (fields.HasFlag(StripFields.Copyright)) tag.Copyright = null;
            if (fields.HasFlag(StripFields.Cover)) tag.Pictures = Array.Empty<TagLib.IPicture>();
            if (fields.HasFlag(StripFields.ReplayGain)) StripReplayGain(tagFile);

            tagFile.Save();
        }

        /// <summary>
        /// Removes REPLAYGAIN_* from every tag format that can carry it. There is no common
        /// TagLib property for these, so each container has to be reached individually.
        /// </summary>
        private static void StripReplayGain(TagLib.File tagFile)
        {
            // ID3v2 stores them as TXXX user-text frames, keyed by description.
            if (tagFile.GetTag(TagLib.TagTypes.Id3v2) is TagLib.Id3v2.Tag id3)
            {
                var toRemove = new List<TagLib.Id3v2.Frame>();
                foreach (var frame in id3.GetFrames<TagLib.Id3v2.UserTextInformationFrame>())
                {
                    if (frame.Description?.StartsWith("REPLAYGAIN_", StringComparison.OrdinalIgnoreCase) == true)
                        toRemove.Add(frame);
                }

                foreach (var frame in toRemove)
                    id3.RemoveFrame(frame);
            }

            // Xiph comments (FLAC, OGG).
            if (tagFile.GetTag(TagLib.TagTypes.Xiph) is TagLib.Ogg.XiphComment xiph)
                foreach (var key in ReplayGainKeys)
                    xiph.RemoveField(key);

            // APE tags (APE, WavPack, and as a secondary tag on MP3).
            if (tagFile.GetTag(TagLib.TagTypes.Ape) is TagLib.Ape.Tag ape)
                foreach (var key in ReplayGainKeys)
                    ape.RemoveItem(key);
        }
    }
}
