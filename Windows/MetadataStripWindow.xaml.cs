using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using AudioQualityChecker.Models;

namespace AudioQualityChecker
{
    public partial class MetadataStripWindow : Window
    {
        [DllImport("dwmapi.dll", PreserveSig = true)]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);
        private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
        private const int DWMWA_CAPTION_COLOR = 35;

        private readonly List<AudioFileInfo> _files;

        /// <summary>Guards against a second strip starting while one is in flight (tag writes are not re-entrant).</summary>
        private bool _stripping;

        /// <summary>Set to true when metadata was modified so the caller can refresh.</summary>
        public bool MetadataChanged { get; private set; }

        public MetadataStripWindow(List<AudioFileInfo> files, Window owner)
        {
            InitializeComponent();
            Owner = owner;
            _files = files;
            FileCountLabel.Text = $"{files.Count} file{(files.Count != 1 ? "s" : "")} selected";
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
                    int color = c.R | (c.G << 8) | (c.B << 16);
                    DwmSetWindowAttribute(hwnd, DWMWA_CAPTION_COLOR, ref color, sizeof(int));
                }
            }
            catch { }
        }

        private void Header_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed)
                this.SafeDragMove();
        }

        private void Close_Click(object sender, RoutedEventArgs e) => Close();

        private void SelectAll_Click(object sender, RoutedEventArgs e)
        {
            ChkTitle.IsChecked = true;
            ChkArtist.IsChecked = true;
            ChkAlbum.IsChecked = true;
            ChkAlbumArtist.IsChecked = true;
            ChkYear.IsChecked = true;
            ChkTrackNumber.IsChecked = true;
            ChkGenre.IsChecked = true;
            ChkComposer.IsChecked = true;
            ChkConductor.IsChecked = true;
            ChkComment.IsChecked = true;
            ChkLyrics.IsChecked = true;
            ChkCopyright.IsChecked = true;
            ChkCover.IsChecked = true;
            ChkReplayGain.IsChecked = true;
        }

        private void SelectNone_Click(object sender, RoutedEventArgs e)
        {
            ChkTitle.IsChecked = false;
            ChkArtist.IsChecked = false;
            ChkAlbum.IsChecked = false;
            ChkAlbumArtist.IsChecked = false;
            ChkYear.IsChecked = false;
            ChkTrackNumber.IsChecked = false;
            ChkGenre.IsChecked = false;
            ChkComposer.IsChecked = false;
            ChkConductor.IsChecked = false;
            ChkComment.IsChecked = false;
            ChkLyrics.IsChecked = false;
            ChkCopyright.IsChecked = false;
            ChkCover.IsChecked = false;
            ChkReplayGain.IsChecked = false;
        }

        private async void StripFields_Click(object sender, RoutedEventArgs e)
        {
            if (_stripping) return;
            bool any = ChkTitle.IsChecked == true || ChkArtist.IsChecked == true ||
                       ChkAlbum.IsChecked == true || ChkAlbumArtist.IsChecked == true ||
                       ChkYear.IsChecked == true || ChkTrackNumber.IsChecked == true ||
                       ChkGenre.IsChecked == true || ChkComposer.IsChecked == true ||
                       ChkConductor.IsChecked == true || ChkComment.IsChecked == true ||
                       ChkLyrics.IsChecked == true || ChkCopyright.IsChecked == true ||
                       ChkCover.IsChecked == true || ChkReplayGain.IsChecked == true;

            if (!any)
            {
                StatusText.Text = "No fields selected.";
                return;
            }

            bool confirmResult = ErrorDialog.Confirm("Strip Metadata",
                $"This will strip the selected metadata fields from {_files.Count} file{(_files.Count != 1 ? "s" : "")}.\n\nThis cannot be undone. Continue?",
                this, severity: AlertSeverity.Warning);

            if (!confirmResult) return;

            // Clearing lives in Core (MetadataStripService) so the Avalonia window strips
            // exactly the same things — Replay Gain spans three tag formats.
            var fields = SelectedFields();
            bool backups = ChkStripBackups.IsChecked == true;
            var files = _files.ToList();
            var written = new List<AudioFileInfo>();
            var errors = new List<string>();

            _stripping = true;
            BtnStrip.IsEnabled = false;
            try
            {
                // Off the UI thread: this is one TagLib open+save per file, and on a large selection
                // the window used to lock up with no progress and no way to tell it was still going.
                var progress = new Progress<(int done, int total, string name)>(p =>
                    StatusText.Text = $"Stripping {p.done + 1}/{p.total}: {p.name}");

                await Task.Run(() =>
                {
                    for (int i = 0; i < files.Count; i++)
                    {
                        var fileInfo = files[i];
                        ((IProgress<(int, int, string)>)progress).Report((i, files.Count, fileInfo.FileName));
                        try
                        {
                            if (backups) Services.FileRenamer.CreateBackup(fileInfo.FilePath);
                            Services.MetadataStripService.Strip(fileInfo.FilePath, fields);
                            written.Add(fileInfo);
                        }
                        catch (Exception ex)
                        {
                            errors.Add($"{fileInfo.FileName}: {ex.Message}");
                        }
                    }
                });

                // Mirror onto the in-memory rows so the grid matches without a re-scan — but only
                // for files that were actually written.
                foreach (var fileInfo in written)
                {
                    if (fields.HasFlag(Services.StripFields.Title)) fileInfo.Title = "";
                    if (fields.HasFlag(Services.StripFields.Artist)) fileInfo.Artist = "";
                    if (fields.HasFlag(Services.StripFields.Album)) fileInfo.Album = "";
                    if (fields.HasFlag(Services.StripFields.Cover)) fileInfo.HasAlbumCover = false;
                    if (fields.HasFlag(Services.StripFields.ReplayGain))
                    {
                        fileInfo.ReplayGain = 0;
                        fileInfo.HasReplayGain = false;
                    }
                }

                MetadataChanged = written.Count > 0;
                string msg = $"Stripped fields from {written.Count} file{(written.Count != 1 ? "s" : "")}";
                if (errors.Count > 0) msg += $" ({errors.Count} failed — {errors[0]})";
                StatusText.Text = msg;
            }
            catch (Exception ex)
            {
                StatusText.Text = $"Strip failed: {ex.Message}";
            }
            finally
            {
                _stripping = false;
                BtnStrip.IsEnabled = true;
            }
        }

        private Services.StripFields SelectedFields()
        {
            var fields = Services.StripFields.None;

            if (ChkTitle.IsChecked == true) fields |= Services.StripFields.Title;
            if (ChkArtist.IsChecked == true) fields |= Services.StripFields.Artist;
            if (ChkAlbum.IsChecked == true) fields |= Services.StripFields.Album;
            if (ChkAlbumArtist.IsChecked == true) fields |= Services.StripFields.AlbumArtist;
            if (ChkYear.IsChecked == true) fields |= Services.StripFields.Year;
            if (ChkTrackNumber.IsChecked == true) fields |= Services.StripFields.TrackNumber;
            if (ChkGenre.IsChecked == true) fields |= Services.StripFields.Genre;
            if (ChkComposer.IsChecked == true) fields |= Services.StripFields.Composer;
            if (ChkConductor.IsChecked == true) fields |= Services.StripFields.Conductor;
            if (ChkComment.IsChecked == true) fields |= Services.StripFields.Comment;
            if (ChkLyrics.IsChecked == true) fields |= Services.StripFields.Lyrics;
            if (ChkCopyright.IsChecked == true) fields |= Services.StripFields.Copyright;
            if (ChkCover.IsChecked == true) fields |= Services.StripFields.Cover;
            if (ChkReplayGain.IsChecked == true) fields |= Services.StripFields.ReplayGain;

            return fields;
        }
    }
}
