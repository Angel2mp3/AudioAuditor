using System;
using System.IO;

namespace AudioQualityChecker.Services
{
    /// <summary>The editable tag fields the metadata editor exposes.</summary>
    public sealed class EditableTags
    {
        public string Title { get; set; } = "";
        public string Artist { get; set; } = "";
        public string Album { get; set; } = "";
        public string AlbumArtist { get; set; } = "";
        public string Year { get; set; } = "";
        public string TrackNumber { get; set; } = "";
        public string DiscNumber { get; set; } = "";
        public string Genre { get; set; } = "";
        public string Composer { get; set; } = "";
        public string Conductor { get; set; } = "";
        public string Copyright { get; set; } = "";
        public string Comment { get; set; } = "";
    }

    /// <summary>Embedded cover art, as stored in the file.</summary>
    public sealed record CoverArt(byte[] Data, string MimeType)
    {
        /// <summary>File extension matching <see cref="MimeType"/>, for "save cover as".</summary>
        public string Extension => MimeType switch
        {
            "image/png" => ".png",
            "image/bmp" => ".bmp",
            "image/gif" => ".gif",
            "image/webp" => ".webp",
            _ => ".jpg"
        };

        /// <summary>Cover art read from an image file on disk, typed from its extension.</summary>
        public static CoverArt FromFile(string imagePath) => new(
            File.ReadAllBytes(imagePath),
            MimeTypeForExtension(Path.GetExtension(imagePath)));

        /// <summary>
        /// Maps an image extension to its MIME type. Shared so every cover-writing path agrees —
        /// the editor used to label a .webp as image/jpeg inside the tag while the online-cover
        /// path wrote image/webp for the same picture.
        /// </summary>
        public static string MimeTypeForExtension(string? extension) =>
            (extension ?? "").ToLowerInvariant() switch
            {
                ".png" => "image/png",
                ".bmp" => "image/bmp",
                ".gif" => "image/gif",
                ".webp" => "image/webp",
                _ => "image/jpeg"
            };
    }

    /// <summary>What to do with the file's cover art when saving.</summary>
    public enum CoverChange
    {
        /// <summary>Leave whatever is already embedded.</summary>
        Keep,

        /// <summary>Drop the embedded art.</summary>
        Remove,

        /// <summary>Replace with the supplied image.</summary>
        Replace
    }

    /// <summary>
    /// Reads and writes the tag fields the metadata editor exposes.
    ///
    /// Lifted out of the WPF editor (Windows/MetadataEditorWindow.xaml.cs) so both front-ends
    /// write tags through one implementation — the field-by-field mapping is exactly the kind
    /// of code where two copies quietly diverge on a null-vs-empty detail.
    /// </summary>
    public static class MetadataEditService
    {
        /// <summary>Reads the editable fields. Throws if the file cannot be opened by TagLib.</summary>
        public static EditableTags Read(string filePath)
        {
            using var tagFile = TagLib.File.Create(filePath);
            var tag = tagFile.Tag;

            return new EditableTags
            {
                Title = tag.Title ?? "",
                Artist = tag.FirstPerformer ?? "",
                Album = tag.Album ?? "",
                AlbumArtist = tag.FirstAlbumArtist ?? "",
                Year = tag.Year > 0 ? tag.Year.ToString() : "",
                TrackNumber = tag.Track > 0 ? tag.Track.ToString() : "",
                DiscNumber = tag.Disc > 0 ? tag.Disc.ToString() : "",
                Genre = tag.FirstGenre ?? "",
                Composer = tag.FirstComposer ?? "",
                Conductor = tag.Conductor ?? "",
                Copyright = tag.Copyright ?? "",
                Comment = tag.Comment ?? "",
            };
        }

        /// <summary>Reads the embedded front cover, or null when the file has none.</summary>
        public static CoverArt? ReadCover(string filePath)
        {
            using var tagFile = TagLib.File.Create(filePath);
            return ReadCover(tagFile);
        }

        private static CoverArt? ReadCover(TagLib.File tagFile)
        {
            var pictures = tagFile.Tag.Pictures;
            if (pictures == null || pictures.Length == 0) return null;

            var picture = pictures[0];
            return new CoverArt(picture.Data.Data, picture.MimeType ?? "image/jpeg");
        }

        /// <summary>
        /// Writes <paramref name="tags"/> back to the file. A blank field clears that tag.
        /// Cover art is only touched when <paramref name="coverChange"/> says so, so saving
        /// text edits never disturbs existing artwork.
        ///
        /// <paramref name="createBackup"/> takes the same <c>.audioauditor-backup-*</c> copy the
        /// batch tools take. It is off by default so the tag-writing tests stay pure; every
        /// interactive caller passes it.
        /// </summary>
        public static void Write(string filePath, EditableTags tags,
            CoverChange coverChange = CoverChange.Keep, CoverArt? newCover = null,
            bool createBackup = false)
        {
            // Before the open, not after: a backup that cannot be taken must stop the write.
            if (createBackup) FileRenamer.CreateBackup(filePath);

            using var tagFile = TagLib.File.Create(filePath);
            var tag = tagFile.Tag;

            tag.Title = Blank(tags.Title) ? null : tags.Title.Trim();
            tag.Performers = Blank(tags.Artist) ? Array.Empty<string>() : new[] { tags.Artist.Trim() };
            tag.Album = Blank(tags.Album) ? null : tags.Album.Trim();
            tag.AlbumArtists = Blank(tags.AlbumArtist) ? Array.Empty<string>() : new[] { tags.AlbumArtist.Trim() };

            // Unparseable means "clear it", matching the WPF editor: a user emptying the box and
            // one typing nonsense both mean the same thing, and 0 is TagLib's "unset".
            tag.Year = uint.TryParse(tags.Year.Trim(), out var year) ? year : 0;
            tag.Track = uint.TryParse(tags.TrackNumber.Trim(), out var track) ? track : 0;
            tag.Disc = uint.TryParse(tags.DiscNumber.Trim(), out var disc) ? disc : 0;

            tag.Genres = Blank(tags.Genre) ? Array.Empty<string>() : new[] { tags.Genre.Trim() };
            tag.Composers = Blank(tags.Composer) ? Array.Empty<string>() : new[] { tags.Composer.Trim() };
            tag.Conductor = Blank(tags.Conductor) ? null : tags.Conductor.Trim();
            tag.Copyright = Blank(tags.Copyright) ? null : tags.Copyright.Trim();
            tag.Comment = Blank(tags.Comment) ? null : tags.Comment.Trim();

            switch (coverChange)
            {
                case CoverChange.Remove:
                    tag.Pictures = Array.Empty<TagLib.IPicture>();
                    break;

                case CoverChange.Replace when newCover != null:
                    tag.Pictures = new TagLib.IPicture[]
                    {
                        new TagLib.Picture(new TagLib.ByteVector(newCover.Data))
                        {
                            Type = TagLib.PictureType.FrontCover,
                            MimeType = newCover.MimeType
                        }
                    };
                    break;
            }

            tagFile.Save();
        }

        /// <summary>
        /// Removes every tag in the file, cover art included.
        ///
        /// Backs up by default, unlike <see cref="Write"/>: this clears the whole tag block at once
        /// and there is nothing left afterwards to reconstruct it from. Restore the sibling copy
        /// through <see cref="FileRenamer.FindBackups"/> / <see cref="FileRenamer.Restore"/>.
        /// </summary>
        public static void StripAll(string filePath, bool createBackup = true)
        {
            if (createBackup) FileRenamer.CreateBackup(filePath);

            using var tagFile = TagLib.File.Create(filePath);
            tagFile.RemoveTags(TagLib.TagTypes.AllTags);
            tagFile.Save();
        }

        private static bool Blank(string? value) => string.IsNullOrWhiteSpace(value);
    }
}
