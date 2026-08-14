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
                // Field mapping lives in Core (MetadataEditService) so the Avalonia editor
                // reads and writes exactly the same tags.
                var tags = Services.MetadataEditService.Read(_filePath);

                TitleBox.Text = tags.Title;
                ArtistBox.Text = tags.Artist;
                AlbumBox.Text = tags.Album;
                AlbumArtistBox.Text = tags.AlbumArtist;
                YearBox.Text = tags.Year;
                TrackNumberBox.Text = tags.TrackNumber;
                DiscNumberBox.Text = tags.DiscNumber;
                GenreBox.Text = tags.Genre;
                ComposerBox.Text = tags.Composer;
                ConductorBox.Text = tags.Conductor;
                CopyrightBox.Text = tags.Copyright;
                CommentBox.Text = tags.Comment;

                LoadCoverPreview();
            }
            catch (Exception ex)
            {
                StatusText.Text = $"Error loading: {ex.Message}";
            }

            _savedSnapshot = CurrentSnapshot();
        }

        private void LoadCoverPreview()
        {
            try
            {
                var cover = Services.MetadataEditService.ReadCover(_filePath);
                if (cover == null)
                {
                    CoverPreview.Source = null;
                    CoverInfoText.Text = "No cover";
                    return;
                }

                using var ms = new MemoryStream(cover.Data);
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.StreamSource = ms;
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.DecodePixelWidth = 160;
                bmp.EndInit();
                bmp.Freeze();
                CoverPreview.Source = bmp;
                CoverInfoText.Text = $"{cover.Data.Length:N0} bytes";
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
                this.SafeDragMove();
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        // ═══ Unsaved-edit guard ═══

        /// <summary>
        /// Field values as they last stood on disk. Closing used to discard edits without a word,
        /// which is easy to do here because the window has no implicit save.
        /// </summary>
        private string _savedSnapshot = "";

        private string CurrentSnapshot() => string.Join("",
            TitleBox.Text, ArtistBox.Text, AlbumBox.Text, AlbumArtistBox.Text, YearBox.Text,
            TrackNumberBox.Text, DiscNumberBox.Text, GenreBox.Text, ComposerBox.Text,
            ConductorBox.Text, CopyrightBox.Text, CommentBox.Text);

        private bool HasUnsavedEdits() =>
            _newCoverData != null || _coverRemoved || CurrentSnapshot() != _savedSnapshot;

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            base.OnClosing(e);
            if (e.Cancel || !HasUnsavedEdits()) return;

            bool discard = ErrorDialog.Confirm("Discard Changes",
                "This file has unsaved metadata edits.\n\nClose anyway and lose them?",
                this, severity: AlertSeverity.Warning);
            if (!discard) e.Cancel = true;
        }

        private void SaveCover_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // If user loaded a new cover that hasn't been saved yet, save that
                byte[]? data = _newCoverData;
                string? mime = _newCoverMime;

                // Otherwise extract from the original file
                if (data == null && !_coverRemoved)
                {
                    var embedded = Services.MetadataEditService.ReadCover(_filePath);
                    if (embedded == null)
                    {
                        StatusText.Text = "No album cover to save.";
                        return;
                    }
                    data = embedded.Data;
                    mime = embedded.MimeType;
                }

                if (data == null)
                {
                    StatusText.Text = "No album cover to save.";
                    return;
                }

                string ext = new Services.CoverArt(data, mime ?? "image/jpeg").Extension;

                string defaultName = Path.GetFileNameWithoutExtension(_filePath) + "_cover" + ext;

                var dialog = new SaveFileDialog
                {
                    Title = "Save Album Cover",
                    FileName = defaultName,
                    Filter = $"Image Files|*{ext}|All Files|*.*"
                };

                if (dialog.ShowDialog() == true)
                {
                    File.WriteAllBytes(dialog.FileName, data);
                    StatusText.Text = "Cover saved successfully.";
                }
            }
            catch (Exception ex)
            {
                StatusText.Text = $"Error saving cover: {ex.Message}";
            }
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
                _newCoverMime = Services.CoverArt.MimeTypeForExtension(Path.GetExtension(dialog.FileName));
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

        // URLs and the query fallback chain live in Core (MusicServiceUrls) so both editors
        // send the same search to the same places.

        private void SearchMusicBrainz_Click(object sender, RoutedEventArgs e) =>
            OpenLookup(Services.MusicServiceUrls.LookupSite.MusicBrainz);

        private void SearchDiscogs_Click(object sender, RoutedEventArgs e) =>
            OpenLookup(Services.MusicServiceUrls.LookupSite.Discogs);

        private void SearchAllMusic_Click(object sender, RoutedEventArgs e) =>
            OpenLookup(Services.MusicServiceUrls.LookupSite.AllMusic);

        private void SearchRateYourMusic_Click(object sender, RoutedEventArgs e) =>
            OpenLookup(Services.MusicServiceUrls.LookupSite.RateYourMusic);

        private void OpenLookup(Services.MusicServiceUrls.LookupSite site) =>
            OpenUrl(Services.MusicServiceUrls.Lookup(site, BuildSearchQuery()));

        private string BuildSearchQuery() =>
            Services.MusicServiceUrls.LookupQuery(ArtistBox.Text, TitleBox.Text, AlbumBox.Text)
            ?? Path.GetFileNameWithoutExtension(_filePath);

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
            bool result = ErrorDialog.Confirm("Strip All Metadata",
                "This will remove ALL metadata tags from the file including title, artist, album, cover art, and any other embedded tags.\n\nA backup copy is saved next to the file first — use Metadata → Restore from backup to undo this. Continue?",
                this, severity: AlertSeverity.Warning);

            if (!result) return;

            try
            {
                Services.MetadataEditService.StripAll(_filePath);

                MetadataChanged = true;

                // Update the in-memory model
                _fileInfo.Title = "";
                _fileInfo.Artist = "";
                _fileInfo.Album = "";
                _fileInfo.HasAlbumCover = false;

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
                _newCoverMime = null;
                _savedSnapshot = CurrentSnapshot();
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
                var tags = new Services.EditableTags
                {
                    Title = TitleBox.Text,
                    Artist = ArtistBox.Text,
                    Album = AlbumBox.Text,
                    AlbumArtist = AlbumArtistBox.Text,
                    Year = YearBox.Text,
                    TrackNumber = TrackNumberBox.Text,
                    DiscNumber = DiscNumberBox.Text,
                    Genre = GenreBox.Text,
                    Composer = ComposerBox.Text,
                    Conductor = ConductorBox.Text,
                    Copyright = CopyrightBox.Text,
                    Comment = CommentBox.Text,
                };

                var change = _coverRemoved ? Services.CoverChange.Remove
                    : _newCoverData != null ? Services.CoverChange.Replace
                    : Services.CoverChange.Keep;

                var newCover = _newCoverData != null
                    ? new Services.CoverArt(_newCoverData, _newCoverMime ?? "image/jpeg")
                    : null;

                Services.MetadataEditService.Write(_filePath, tags, change, newCover,
                    createBackup: ChkBackups.IsChecked == true);
                MetadataChanged = true;

                // Mirror onto the in-memory row so the grid matches without a re-scan. Album was
                // missing here, so the Album column kept its pre-edit value until the next scan.
                _fileInfo.Title = string.IsNullOrWhiteSpace(tags.Title)
                    ? Path.GetFileNameWithoutExtension(_filePath)
                    : tags.Title.Trim();
                _fileInfo.Artist = tags.Artist.Trim();
                _fileInfo.Album = tags.Album.Trim();
                if (change == Services.CoverChange.Replace) _fileInfo.HasAlbumCover = true;
                else if (change == Services.CoverChange.Remove) _fileInfo.HasAlbumCover = false;

                // What is on screen is now what is on disk.
                _savedSnapshot = CurrentSnapshot();
                _coverRemoved = false;
                _newCoverData = null;
                _newCoverMime = null;

                StatusText.Text = "Saved successfully.";
            }
            catch (Exception ex)
            {
                StatusText.Text = $"Error saving: {ex.Message}";
            }
        }
    }
}
