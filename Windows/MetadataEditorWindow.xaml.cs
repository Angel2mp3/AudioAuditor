using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using AudioQualityChecker.Models;
using Microsoft.Win32;

namespace AudioQualityChecker
{
    public partial class MetadataEditorWindow : Window
    {
        [DllImport("dwmapi.dll", PreserveSig = true)]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);
        private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
        private const int DWMWA_CAPTION_COLOR = 35;

        private readonly string _filePath;
        private readonly AudioFileInfo _fileInfo;
        private bool _coverRemoved;
        private byte[]? _newCoverData;
        private string? _newCoverMime;

        /// <summary>Set to true when metadata was saved so the caller can refresh.</summary>
        public bool MetadataChanged { get; private set; }

        public MetadataEditorWindow(AudioFileInfo fileInfo, Window owner)
        {
            InitializeComponent();
            Owner = owner;
            _filePath = fileInfo.FilePath;
            _fileInfo = fileInfo;

            LoadMetadata();
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            ApplyThemeTitleBar();
        }

        private void ApplyThemeTitleBar()
        {
            try
            {
                var hwnd = new WindowInteropHelper(this).Handle;
                if (hwnd == IntPtr.Zero) return;

                bool isLight = Services.ThemeManager.CurrentTheme == "Light";
                int darkMode = isLight ? 0 : 1;
                DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref darkMode, sizeof(int));

                var captionBrush = FindResource("TitleBarBg") as System.Windows.Media.SolidColorBrush;
                if (captionBrush != null)
                {
                    var c = captionBrush.Color;
                    int colorRef = c.R | (c.G << 8) | (c.B << 16);
                    DwmSetWindowAttribute(hwnd, DWMWA_CAPTION_COLOR, ref colorRef, sizeof(int));
                }
            }
            catch { }
        }

        private void LoadMetadata()
        {
            FileNameLabel.Text = Path.GetFileName(_filePath);

            try
            {
                using var tagFile = TagLib.File.Create(_filePath);
                var tag = tagFile.Tag;

                TitleBox.Text = tag.Title ?? "";
                ArtistBox.Text = tag.FirstPerformer ?? "";
                AlbumBox.Text = tag.Album ?? "";
                AlbumArtistBox.Text = tag.FirstAlbumArtist ?? "";
                YearBox.Text = tag.Year > 0 ? tag.Year.ToString() : "";
                TrackNumberBox.Text = tag.Track > 0 ? tag.Track.ToString() : "";
                DiscNumberBox.Text = tag.Disc > 0 ? tag.Disc.ToString() : "";
                GenreBox.Text = tag.FirstGenre ?? "";
                ComposerBox.Text = tag.FirstComposer ?? "";
                ConductorBox.Text = tag.Conductor ?? "";
                CopyrightBox.Text = tag.Copyright ?? "";
                CommentBox.Text = tag.Comment ?? "";

                LoadCoverPreview(tagFile);
            }
            catch (Exception ex)
            {
                StatusText.Text = $"Error loading: {ex.Message}";
            }
        }

        private void LoadCoverPreview(TagLib.File? tagFile = null)
        {
            try
            {
                bool ownFile = tagFile == null;
                tagFile ??= TagLib.File.Create(_filePath);

                var pics = tagFile.Tag.Pictures;
                if (pics != null && pics.Length > 0)
                {
                    var pic = pics[0];
                    using var ms = new MemoryStream(pic.Data.Data);
                    var bmp = new BitmapImage();
                    bmp.BeginInit();
                    bmp.StreamSource = ms;
                    bmp.CacheOption = BitmapCacheOption.OnLoad;
                    bmp.DecodePixelWidth = 160;
                    bmp.EndInit();
                    bmp.Freeze();
                    CoverPreview.Source = bmp;
                    CoverInfoText.Text = $"{pic.Data.Count:N0} bytes";
                }
                else
                {
                    CoverPreview.Source = null;
                    CoverInfoText.Text = "No cover";
                }

                if (ownFile)
                    tagFile.Dispose();
            }
            catch
            {
                CoverPreview.Source = null;
                CoverInfoText.Text = "Error loading cover";
            }
        }

        private void Header_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
                DragMove();
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void ReplaceCover_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Title = "Select album cover image",
                Filter = "Image Files|*.jpg;*.jpeg;*.png;*.bmp;*.gif|All Files|*.*"
            };

            if (dialog.ShowDialog() != true) return;

            try
            {
                _newCoverData = File.ReadAllBytes(dialog.FileName);
                string ext = Path.GetExtension(dialog.FileName).ToLowerInvariant();
                _newCoverMime = ext switch
                {
                    ".png" => "image/png",
                    ".bmp" => "image/bmp",
                    ".gif" => "image/gif",
                    _ => "image/jpeg"
                };
                _coverRemoved = false;

                // Preview the new cover
                using var ms = new MemoryStream(_newCoverData);
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.StreamSource = ms;
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.DecodePixelWidth = 160;
                bmp.EndInit();
                bmp.Freeze();
                CoverPreview.Source = bmp;
                CoverInfoText.Text = $"{_newCoverData.Length:N0} bytes (new)";
            }
            catch (Exception ex)
            {
                StatusText.Text = $"Error loading image: {ex.Message}";
            }
        }

        private void RemoveCover_Click(object sender, RoutedEventArgs e)
        {
            _coverRemoved = true;
            _newCoverData = null;
            _newCoverMime = null;
            CoverPreview.Source = null;
            CoverInfoText.Text = "Cover will be removed on save";
        }

        // ═══ Search Metadata Online ═══

        private void SearchMusicBrainz_Click(object sender, RoutedEventArgs e)
        {
            var query = BuildSearchQuery();
            OpenUrl($"https://musicbrainz.org/search?query={Uri.EscapeDataString(query)}&type=recording");
        }

        private void SearchDiscogs_Click(object sender, RoutedEventArgs e)
        {
            var query = BuildSearchQuery();
            OpenUrl($"https://www.discogs.com/search/?q={Uri.EscapeDataString(query)}&type=all");
        }

        private void SearchAllMusic_Click(object sender, RoutedEventArgs e)
        {
            var query = BuildSearchQuery();
            OpenUrl($"https://www.allmusic.com/search/all/{Uri.EscapeDataString(query)}");
        }

        private void SearchRateYourMusic_Click(object sender, RoutedEventArgs e)
        {
            var query = BuildSearchQuery();
            OpenUrl($"https://rateyourmusic.com/search?searchterm={Uri.EscapeDataString(query)}&searchtype=");
        }

        private string BuildSearchQuery()
        {
            string artist = ArtistBox.Text.Trim();
            string title = TitleBox.Text.Trim();
            string album = AlbumBox.Text.Trim();

            if (!string.IsNullOrEmpty(artist) && !string.IsNullOrEmpty(title))
                return $"{artist} {title}";
            if (!string.IsNullOrEmpty(artist) && !string.IsNullOrEmpty(album))
                return $"{artist} {album}";
            if (!string.IsNullOrEmpty(title))
                return title;
            if (!string.IsNullOrEmpty(artist))
                return artist;

            return Path.GetFileNameWithoutExtension(_filePath);
        }

        private static void OpenUrl(string url)
        {
            try
            {
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            }
            catch { }
        }

        private void StripAllMetadata_Click(object sender, RoutedEventArgs e)
        {
            var result = MessageBox.Show(
                "This will remove ALL metadata tags from the file including title, artist, album, cover art, and any other embedded tags.\n\nThis cannot be undone. Continue?",
                "Strip All Metadata",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result != MessageBoxResult.Yes) return;

            try
            {
                using var tagFile = TagLib.File.Create(_filePath);
                tagFile.RemoveTags(TagLib.TagTypes.AllTags);
                tagFile.Save();

                MetadataChanged = true;

                // Update the in-memory model
                _fileInfo.Title = "";
                _fileInfo.Artist = "";

                StatusText.Text = "All metadata stripped successfully.";

                // Reload UI
                TitleBox.Text = "";
                ArtistBox.Text = "";
                AlbumBox.Text = "";
                AlbumArtistBox.Text = "";
                YearBox.Text = "";
                TrackNumberBox.Text = "";
                DiscNumberBox.Text = "";
                GenreBox.Text = "";
                ComposerBox.Text = "";
                ConductorBox.Text = "";
                CopyrightBox.Text = "";
                CommentBox.Text = "";
                CoverPreview.Source = null;
                CoverInfoText.Text = "No cover";
                _coverRemoved = false;
                _newCoverData = null;
            }
            catch (Exception ex)
            {
                StatusText.Text = $"Error: {ex.Message}";
            }
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                using var tagFile = TagLib.File.Create(_filePath);
                var tag = tagFile.Tag;

                tag.Title = string.IsNullOrWhiteSpace(TitleBox.Text) ? null : TitleBox.Text.Trim();
                tag.Performers = string.IsNullOrWhiteSpace(ArtistBox.Text)
                    ? Array.Empty<string>() : new[] { ArtistBox.Text.Trim() };
                tag.Album = string.IsNullOrWhiteSpace(AlbumBox.Text) ? null : AlbumBox.Text.Trim();
                tag.AlbumArtists = string.IsNullOrWhiteSpace(AlbumArtistBox.Text)
                    ? Array.Empty<string>() : new[] { AlbumArtistBox.Text.Trim() };

                if (uint.TryParse(YearBox.Text.Trim(), out uint year))
                    tag.Year = year;
                else
                    tag.Year = 0;

                if (uint.TryParse(TrackNumberBox.Text.Trim(), out uint track))
                    tag.Track = track;
                else
                    tag.Track = 0;

                if (uint.TryParse(DiscNumberBox.Text.Trim(), out uint disc))
                    tag.Disc = disc;
                else
                    tag.Disc = 0;

                tag.Genres = string.IsNullOrWhiteSpace(GenreBox.Text)
                    ? Array.Empty<string>() : new[] { GenreBox.Text.Trim() };

                tag.Composers = string.IsNullOrWhiteSpace(ComposerBox.Text)
                    ? Array.Empty<string>() : new[] { ComposerBox.Text.Trim() };
                tag.Conductor = string.IsNullOrWhiteSpace(ConductorBox.Text) ? null : ConductorBox.Text.Trim();
                tag.Copyright = string.IsNullOrWhiteSpace(CopyrightBox.Text) ? null : CopyrightBox.Text.Trim();
                tag.Comment = string.IsNullOrWhiteSpace(CommentBox.Text) ? null : CommentBox.Text.Trim();

                // Handle cover art changes
                if (_coverRemoved)
                {
                    tag.Pictures = Array.Empty<TagLib.IPicture>();
                }
                else if (_newCoverData != null)
                {
                    var pic = new TagLib.Picture(new TagLib.ByteVector(_newCoverData))
                    {
                        Type = TagLib.PictureType.FrontCover,
                        MimeType = _newCoverMime ?? "image/jpeg"
                    };
                    tag.Pictures = new TagLib.IPicture[] { pic };
                }

                tagFile.Save();
                MetadataChanged = true;

                // Update the in-memory model
                _fileInfo.Title = tag.Title ?? Path.GetFileNameWithoutExtension(_filePath);
                _fileInfo.Artist = tag.FirstPerformer ?? "";

                StatusText.Text = "Saved successfully.";
            }
            catch (Exception ex)
            {
                StatusText.Text = $"Error saving: {ex.Message}";
            }
        }
    }
}
