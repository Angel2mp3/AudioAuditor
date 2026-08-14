using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using AudioQualityChecker.Models;
using AudioQualityChecker.Services;
using Microsoft.Win32;

namespace AudioQualityChecker
{
    /// <summary>
    /// Full-screen Metadata Workspace: load two tracks side by side, transfer tags field-by-field
    /// (or all at once) while watching it happen, edit any field in place, and save each side back to
    /// disk. Mirrors the Now Playing screen's overlay pattern (a sibling panel that hides MainContent)
    /// and reuses <see cref="BatchFieldEditService"/> for the actual TagLib writes — no second writer.
    ///
    /// Transfers and edits live only in the on-screen boxes until the user clicks Save, so the
    /// "watch the transfer happen" is live but fully reversible (just don't save).
    /// </summary>
    public partial class MainWindow
    {
        private bool _mwVisible;
        private AudioFileInfo? _mwLeft;
        private AudioFileInfo? _mwRight;
        private readonly List<MwFieldRow> _mwFields = new();
        private readonly BatchFieldEditService _mwEditService = new();

        // Pending cover art per side. Captured raw on load; replaced (and marked dirty) when the
        // user transfers a cover across. Dirty covers are written on Save via BatchCoverAction.
        private byte[]? _mwLeftCoverBytes, _mwRightCoverBytes;
        private string? _mwLeftCoverMime, _mwRightCoverMime;
        private bool _mwLeftCoverDirty, _mwRightCoverDirty;

        // The Save buttons are plain async void handlers with no busy state, so a double-click used
        // to start two concurrent tag writes against the same file. Tag writing is not re-entrant.
        private bool _mwSaving;

        private sealed class MwFieldRow
        {
            public Func<TagLib.Tag, string> Read = _ => "";
            public Action<BatchFieldEditOptions, string> Write = (_, _) => { };
            public TextBox Left = null!;
            public TextBox Right = null!;
        }

        // Field set is exactly what BatchFieldEditService can write. (Conductor / Copyright / cover
        // transfer are intentionally out of v1 — they have no batch-write support yet.)
        private static readonly (string Label, Func<TagLib.Tag, string> Read, Action<BatchFieldEditOptions, string> Write)[] MwFieldDefs =
        {
            ("Title",        t => t.Title ?? "",                       (o, v) => { o.SetTitle = true; o.Title = v; }),
            ("Artist",       t => t.FirstPerformer ?? "",              (o, v) => { o.SetArtist = true; o.Artist = v; }),
            ("Album",        t => t.Album ?? "",                       (o, v) => { o.SetAlbum = true; o.Album = v; }),
            ("Album Artist", t => t.FirstAlbumArtist ?? "",            (o, v) => { o.SetAlbumArtist = true; o.AlbumArtist = v; }),
            ("Year",         t => t.Year > 0 ? t.Year.ToString() : "", (o, v) => { o.SetYear = true; o.Year = v; }),
            ("Track",        t => t.Track > 0 ? t.Track.ToString() : "", (o, v) => { o.TrackMode = BatchTrackMode.Fixed; o.TrackFixed = v; }),
            ("Disc",         t => t.Disc > 0 ? t.Disc.ToString() : "", (o, v) => { o.SetDisc = true; o.Disc = v; }),
            ("Genre",        t => t.FirstGenre ?? "",                  (o, v) => { o.SetGenre = true; o.Genre = v; }),
            ("Composer",     t => t.FirstComposer ?? "",               (o, v) => { o.SetComposer = true; o.Composer = v; }),
            ("Comment",      t => t.Comment ?? "",                     (o, v) => { o.SetComment = true; o.Comment = v; }),
        };

        // ─── Launch / toggle ───

        private void OpenMetadataWorkspace_Click(object sender, RoutedEventArgs e)
        {
            var selected = FileGrid.SelectedItems.Cast<AudioFileInfo>().ToList();
            var left = selected.ElementAtOrDefault(0) ?? FileGrid.SelectedItem as AudioFileInfo;
            if (left == null) return;
            var right = selected.ElementAtOrDefault(1);

            MwBuildFieldRows();
            MwLoadSide(isRight: false, file: left);
            if (right != null) MwLoadSide(isRight: true, file: right);
            else MwClearSide(isRight: true);

            MwStatusText.Text = right != null
                ? ""
                : "Right side is empty — select a second track first, or use “Pick…”.";
            ToggleMetadataWorkspace(true);
        }

        private void ToggleMetadataWorkspace(bool show)
        {
            _mwVisible = show;
            MetadataWorkspacePanel.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
            MainContent.Visibility = show ? Visibility.Collapsed : Visibility.Visible;
        }

        private void MwBack_Click(object sender, RoutedEventArgs e) => ToggleMetadataWorkspace(false);

        // ─── Field-row generation (label | left box | →← | right box) ───

        private void MwBuildFieldRows()
        {
            if (_mwFields.Count > 0) return; // build once

            foreach (var def in MwFieldDefs)
            {
                int row = MwFieldsGrid.RowDefinitions.Count;
                MwFieldsGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

                var label = new TextBlock
                {
                    Text = def.Label,
                    Margin = new Thickness(0, 0, 10, 6),
                    VerticalAlignment = VerticalAlignment.Center,
                    Foreground = (Brush)FindResource("TextSecondary"),
                    FontSize = 12,
                    FontFamily = new FontFamily("Segoe UI")
                };
                Grid.SetRow(label, row); Grid.SetColumn(label, 0);

                var left = MwMakeBox();
                Grid.SetRow(left, row); Grid.SetColumn(left, 1);

                var right = MwMakeBox();
                Grid.SetRow(right, row); Grid.SetColumn(right, 3);

                var toRight = MwMakeArrow("→", "Copy this field: left → right");
                var toLeft = MwMakeArrow("←", "Copy this field: right → left");
                toRight.Click += (_, _) => right.Text = left.Text;
                toLeft.Click += (_, _) => left.Text = right.Text;

                var gutter = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Margin = new Thickness(6, 0, 6, 6),
                    VerticalAlignment = VerticalAlignment.Center
                };
                gutter.Children.Add(toRight);
                gutter.Children.Add(toLeft);
                Grid.SetRow(gutter, row); Grid.SetColumn(gutter, 2);

                MwFieldsGrid.Children.Add(label);
                MwFieldsGrid.Children.Add(left);
                MwFieldsGrid.Children.Add(gutter);
                MwFieldsGrid.Children.Add(right);

                _mwFields.Add(new MwFieldRow { Read = def.Read, Write = def.Write, Left = left, Right = right });
            }
        }

        private TextBox MwMakeBox() => new()
        {
            Margin = new Thickness(0, 0, 0, 6),
            Padding = new Thickness(6, 4, 6, 4),
            FontSize = 12,
            FontFamily = new FontFamily("Segoe UI"),
            Background = (Brush)FindResource("InputBg"),
            Foreground = (Brush)FindResource("TextPrimary"),
            BorderBrush = (Brush)FindResource("GlassBorderBrush"),
            BorderThickness = new Thickness(1)
        };

        private Button MwMakeArrow(string glyph, string tip) => new()
        {
            Content = glyph,
            ToolTip = tip,
            Width = 30,
            Margin = new Thickness(2, 0, 2, 0),
            Padding = new Thickness(0, 2, 0, 2),
            Style = (Style)FindResource("DarkButton")
        };

        // ─── Loading a side ───

        private void MwLoadSide(bool isRight, AudioFileInfo file)
        {
            if (isRight) _mwRight = file; else _mwLeft = file;
            (isRight ? MwRightFileName : MwLeftFileName).Text = file.FileName;

            try
            {
                using var tagFile = TagLib.File.Create(file.FilePath);
                var tag = tagFile.Tag;
                foreach (var f in _mwFields)
                    (isRight ? f.Right : f.Left).Text = f.Read(tag);

                var (cbytes, cmime) = MwReadCover(tagFile);
                if (isRight) { _mwRightCoverBytes = cbytes; _mwRightCoverMime = cmime; _mwRightCoverDirty = false; }
                else { _mwLeftCoverBytes = cbytes; _mwLeftCoverMime = cmime; _mwLeftCoverDirty = false; }
                MwSetCoverImage(isRight ? MwRightCover : MwLeftCover, cbytes);
            }
            catch (Exception ex)
            {
                MwStatusText.Text = $"Couldn't read {file.FileName}: {ex.Message}";
            }

            (isRight ? MwRightInfo : MwLeftInfo).Text = MwBuildInfo(file);
        }

        private void MwClearSide(bool isRight)
        {
            if (isRight) _mwRight = null; else _mwLeft = null;
            (isRight ? MwRightFileName : MwLeftFileName).Text = "(no file)";
            (isRight ? MwRightInfo : MwLeftInfo).Text = "";
            (isRight ? MwRightCover : MwLeftCover).Source = null;
            if (isRight) { _mwRightCoverBytes = null; _mwRightCoverMime = null; _mwRightCoverDirty = false; }
            else { _mwLeftCoverBytes = null; _mwLeftCoverMime = null; _mwLeftCoverDirty = false; }
            foreach (var f in _mwFields)
                (isRight ? f.Right : f.Left).Text = "";
        }

        /// <summary>Raw embedded cover bytes + mime for the front-most picture, or (null, null).</summary>
        private static (byte[]? bytes, string? mime) MwReadCover(TagLib.File tagFile)
        {
            try
            {
                var pics = tagFile.Tag.Pictures;
                if (pics is { Length: > 0 } && pics[0].Data.Data is { Length: > 0 } data)
                    return (data, string.IsNullOrWhiteSpace(pics[0].MimeType) ? "image/jpeg" : pics[0].MimeType);
            }
            catch { /* no readable cover */ }
            return (null, null);
        }

        private static void MwSetCoverImage(Image target, byte[]? bytes)
        {
            try
            {
                if (bytes is { Length: > 0 })
                {
                    using var ms = new MemoryStream(bytes);
                    var bmp = new BitmapImage();
                    bmp.BeginInit();
                    bmp.StreamSource = ms;
                    bmp.CacheOption = BitmapCacheOption.OnLoad;
                    bmp.DecodePixelWidth = 176;
                    bmp.EndInit();
                    bmp.Freeze();
                    target.Source = bmp;
                    return;
                }
            }
            catch { /* fall through to clearing */ }
            target.Source = null;
        }

        // ─── Cover transfer (mirrors the field →/← arrows) ───

        private void MwCoverRight_Click(object sender, RoutedEventArgs e) => MwTransferCover(toRight: true);
        private void MwCoverLeft_Click(object sender, RoutedEventArgs e) => MwTransferCover(toRight: false);

        private void MwTransferCover(bool toRight)
        {
            var bytes = toRight ? _mwLeftCoverBytes : _mwRightCoverBytes;
            var mime = toRight ? _mwLeftCoverMime : _mwRightCoverMime;
            if (toRight)
            {
                _mwRightCoverBytes = bytes; _mwRightCoverMime = mime; _mwRightCoverDirty = true;
                MwSetCoverImage(MwRightCover, bytes);
            }
            else
            {
                _mwLeftCoverBytes = bytes; _mwLeftCoverMime = mime; _mwLeftCoverDirty = true;
                MwSetCoverImage(MwLeftCover, bytes);
            }
        }

        /// <summary>
        /// Read-only specs + the rip-log honesty line. Rip accuracy is reported only from a parsed
        /// log (cambia) — "no rip log" means "not verified", never "bad rip" inferred from the audio.
        /// </summary>
        private static string MwBuildInfo(AudioFileInfo f)
        {
            var specs = new List<string>();
            if (!string.IsNullOrWhiteSpace(f.FormatDisplay)) specs.Add(f.FormatDisplay);
            if (f.SampleRate > 0) specs.Add(f.SampleRateDisplay);
            if (f.BitsPerSample > 0) specs.Add(f.BitsPerSampleDisplay);
            if (f.ActualBitrate > 0) specs.Add(f.ActualBitrateDisplay);
            if (f.Channels > 0) specs.Add(f.ChannelsDisplay);
            if (!string.IsNullOrWhiteSpace(f.Duration)) specs.Add(f.Duration);

            string rip = f.HasRipLog
                ? $"Rip log: {f.RipLogDisplay}"
                : "No rip log — rip accuracy can't be verified (it's read from the ripper's log, never the audio).";

            return (specs.Count > 0 ? string.Join("  •  ", specs) + "\n" : "") + rip;
        }

        // ─── Copy-all + spectrogram compare ───

        // "Copy all" moves the cover too, but only when there is one to move: copying from a side
        // with no artwork used to mark the target dirty with null bytes, which saved as "remove the
        // cover" and quietly deleted artwork the user never touched.
        private void MwCopyAllRight_Click(object sender, RoutedEventArgs e)
        {
            foreach (var f in _mwFields) f.Right.Text = f.Left.Text;
            if (_mwLeftCoverBytes is { Length: > 0 }) MwTransferCover(toRight: true);
        }

        private void MwCopyAllLeft_Click(object sender, RoutedEventArgs e)
        {
            foreach (var f in _mwFields) f.Left.Text = f.Right.Text;
            if (_mwRightCoverBytes is { Length: > 0 }) MwTransferCover(toRight: false);
        }

        private void MwCompareSpectrograms_Click(object sender, RoutedEventArgs e)
        {
            if (_mwLeft == null || _mwRight == null)
            {
                MwStatusText.Text = "Load a track on both sides to compare spectrograms.";
                return;
            }
            new SpectrogramCompareWindow(_mwLeft.FilePath, _mwRight.FilePath) { Owner = this }.Show();
        }

        // ─── Pick a file for a side ───

        private void MwPickLeft_Click(object sender, RoutedEventArgs e) => MwPickFile(isRight: false);
        private void MwPickRight_Click(object sender, RoutedEventArgs e) => MwPickFile(isRight: true);

        private void MwPickFile(bool isRight)
        {
            var dlg = new OpenFileDialog
            {
                Title = isRight ? "Pick the right-side track" : "Pick the left-side track",
                Filter = "Audio files (*.flac;*.mp3;*.m4a;*.wav;*.ogg;*.opus;*.aiff;*.wma)" +
                         "|*.flac;*.mp3;*.m4a;*.wav;*.ogg;*.opus;*.aiff;*.wma|All files (*.*)|*.*"
            };
            if (dlg.ShowDialog(this) != true) return;

            // Prefer the already-scanned model (keeps specs + rip status); otherwise a light stand-in.
            var existing = _files.FirstOrDefault(f =>
                string.Equals(f.FilePath, dlg.FileName, StringComparison.OrdinalIgnoreCase));
            var file = existing ?? new AudioFileInfo
            {
                FilePath = dlg.FileName,
                FileName = Path.GetFileName(dlg.FileName),
                FolderPath = Path.GetDirectoryName(dlg.FileName) ?? ""
            };
            MwLoadSide(isRight, file);
        }

        // ─── Save a side ───

        private async void MwSaveLeft_Click(object sender, RoutedEventArgs e) => await MwSaveSide(isRight: false);
        private async void MwSaveRight_Click(object sender, RoutedEventArgs e) => await MwSaveSide(isRight: true);

        private async Task MwSaveSide(bool isRight)
        {
            if (_mwSaving) return;

            var file = isRight ? _mwRight : _mwLeft;
            if (file == null)
            {
                MwStatusText.Text = "Nothing loaded on that side.";
                return;
            }

            var options = new BatchFieldEditOptions();
            foreach (var f in _mwFields)
                f.Write(options, (isRight ? f.Right : f.Left).Text.Trim());

            bool coverDirty = isRight ? _mwRightCoverDirty : _mwLeftCoverDirty;
            if (coverDirty)
            {
                var cbytes = isRight ? _mwRightCoverBytes : _mwLeftCoverBytes;
                if (cbytes is { Length: > 0 })
                {
                    options.CoverAction = BatchCoverAction.SetFromBytes;
                    options.CoverBytes = cbytes;
                    options.CoverMime = isRight ? _mwRightCoverMime : _mwLeftCoverMime;
                }
                else
                {
                    options.CoverAction = BatchCoverAction.Remove;
                }
            }

            _mwSaving = true;
            MwStatusText.Text = $"Saving {file.FileName}…";
            try
            {
                var summary = await _mwEditService.ApplyAsync(
                    new[] { file }, options, createBackups: MwBackups.IsChecked == true);
                if (summary.FilesChanged > 0)
                {
                    if (isRight) _mwRightCoverDirty = false; else _mwLeftCoverDirty = false;
                    MwUpdateInMemory(file, options);
                    _filteredView?.Refresh();
                    MwStatusText.Text = $"Saved {file.FileName}.";
                }
                else
                {
                    MwStatusText.Text = summary.Errors.FirstOrDefault() ?? "Nothing was written.";
                }
            }
            catch (Exception ex)
            {
                MwStatusText.Text = $"Save failed: {ex.Message}";
            }
            finally
            {
                _mwSaving = false;
            }
        }

        // Keep the grid's visible metadata columns (Title/Artist/Album) in sync without a re-scan.
        // The full tag set is already on disk; these are the only metadata columns the grid shows.
        private static void MwUpdateInMemory(AudioFileInfo f, BatchFieldEditOptions o)
        {
            if (o.SetTitle) f.Title = o.Title;
            if (o.SetArtist) f.Artist = o.Artist;
            if (o.SetAlbum) f.Album = o.Album;
        }
    }
}
