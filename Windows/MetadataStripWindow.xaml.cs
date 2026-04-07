using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
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
                DragMove();
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

        private void StripFields_Click(object sender, RoutedEventArgs e)
        {
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

            var confirmResult = MessageBox.Show(
                $"This will strip the selected metadata fields from {_files.Count} file{(_files.Count != 1 ? "s" : "")}.\n\nThis cannot be undone. Continue?",
                "Strip Metadata",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (confirmResult != MessageBoxResult.Yes) return;

            int success = 0;
            int failed = 0;

            foreach (var fileInfo in _files)
            {
                try
                {
                    using var tagFile = TagLib.File.Create(fileInfo.FilePath);
                    var tag = tagFile.Tag;

                    if (ChkTitle.IsChecked == true)
                    {
                        tag.Title = null;
                        fileInfo.Title = "";
                    }
                    if (ChkArtist.IsChecked == true)
                    {
                        tag.Performers = Array.Empty<string>();
                        fileInfo.Artist = "";
                    }
                    if (ChkAlbum.IsChecked == true)
                        tag.Album = null;
                    if (ChkAlbumArtist.IsChecked == true)
                        tag.AlbumArtists = Array.Empty<string>();
                    if (ChkYear.IsChecked == true)
                        tag.Year = 0;
                    if (ChkTrackNumber.IsChecked == true)
                    {
                        tag.Track = 0;
                        tag.Disc = 0;
                    }
                    if (ChkGenre.IsChecked == true)
                        tag.Genres = Array.Empty<string>();
                    if (ChkComposer.IsChecked == true)
                        tag.Composers = Array.Empty<string>();
                    if (ChkConductor.IsChecked == true)
                        tag.Conductor = null;
                    if (ChkComment.IsChecked == true)
                        tag.Comment = null;
                    if (ChkLyrics.IsChecked == true)
                        tag.Lyrics = null;
                    if (ChkCopyright.IsChecked == true)
                        tag.Copyright = null;
                    if (ChkCover.IsChecked == true)
                    {
                        tag.Pictures = Array.Empty<TagLib.IPicture>();
                        fileInfo.HasAlbumCover = false;
                    }
                    if (ChkReplayGain.IsChecked == true)
                    {
                        StripReplayGainTags(tagFile);
                        fileInfo.ReplayGain = 0;
                        fileInfo.HasReplayGain = false;
                    }

                    tagFile.Save();
                    success++;
                }
                catch
                {
                    failed++;
                }
            }

            MetadataChanged = success > 0;
            string msg = $"Stripped fields from {success} file{(success != 1 ? "s" : "")}";
            if (failed > 0) msg += $" ({failed} failed)";
            StatusText.Text = msg;
        }

        /// <summary>Remove REPLAYGAIN_* tags from all tag formats.</summary>
        private static void StripReplayGainTags(TagLib.File tagFile)
        {
            // ID3v2 TXXX frames
            if (tagFile.GetTag(TagLib.TagTypes.Id3v2) is TagLib.Id3v2.Tag id3)
            {
                var toRemove = new List<TagLib.Id3v2.Frame>();
                foreach (var frame in id3.GetFrames<TagLib.Id3v2.UserTextInformationFrame>())
                {
                    if (frame.Description != null &&
                        frame.Description.StartsWith("REPLAYGAIN_", StringComparison.OrdinalIgnoreCase))
                        toRemove.Add(frame);
                }
                foreach (var f in toRemove)
                    id3.RemoveFrame(f);
            }

            // Xiph Comment (FLAC/OGG)
            if (tagFile.GetTag(TagLib.TagTypes.Xiph) is TagLib.Ogg.XiphComment xiph)
            {
                xiph.RemoveField("REPLAYGAIN_TRACK_GAIN");
                xiph.RemoveField("REPLAYGAIN_TRACK_PEAK");
                xiph.RemoveField("REPLAYGAIN_ALBUM_GAIN");
                xiph.RemoveField("REPLAYGAIN_ALBUM_PEAK");
            }

            // APE tags
            if (tagFile.GetTag(TagLib.TagTypes.Ape) is TagLib.Ape.Tag ape)
            {
                ape.RemoveItem("REPLAYGAIN_TRACK_GAIN");
                ape.RemoveItem("REPLAYGAIN_TRACK_PEAK");
                ape.RemoveItem("REPLAYGAIN_ALBUM_GAIN");
                ape.RemoveItem("REPLAYGAIN_ALBUM_PEAK");
            }
        }
    }
}
