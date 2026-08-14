using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using AudioQualityChecker.Models;
using AudioQualityChecker.Services;

namespace AudioQualityChecker
{
    /// <summary>
    /// "Paste metadata" dialog: paste a tracklist / CSV / single block (auto-detected) or copy shared
    /// metadata from a master file, review the proposed per-file changes, and write the selected ones.
    /// Reuses <see cref="MetadataEnrichmentService.ApplyAsync"/> so writing behaves like the Auto-Tag tab.
    /// </summary>
    public partial class PasteMetadataWindow : Window
    {
        private readonly List<AudioFileInfo> _files;
        private readonly MetadataEnrichmentService _enrichment = new();
        private readonly ObservableCollection<MetadataEnrichmentChange> _changes = new();
        private List<(string SourcePath, string TargetPath)> _folderCoverPairs = new();
        private CancellationTokenSource? _cts;

        public bool MetadataChanged { get; private set; }

        public PasteMetadataWindow(IEnumerable<AudioFileInfo> files, Window owner)
        {
            InitializeComponent();
            Owner = owner;
            _files = files.ToList();

            FileCountText.Text = $"{_files.Count} file{(_files.Count == 1 ? "" : "s")} selected";
            ChangesGrid.ItemsSource = _changes;
            CmbMaster.ItemsSource = _files;
            if (_files.Count > 0) CmbMaster.SelectedIndex = 0;
        }

        /// <summary>Opens the dialog straight into "Copy from a folder" mode (the set→set transfer).
        /// Sets panel visibility directly because Mode_Changed no-ops before the window has loaded.</summary>
        public void SelectFolderMode()
        {
            RbFolder.IsChecked = true;
            PastePanel.Visibility = Visibility.Collapsed;
            MasterPanel.Visibility = Visibility.Collapsed;
            FolderPanel.Visibility = Visibility.Visible;
        }

        private void Header_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed) this.SafeDragMove();
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            _cts?.Cancel();
            Close();
        }

        private void Mode_Changed(object sender, RoutedEventArgs e)
        {
            if (!IsLoaded) return;
            PastePanel.Visibility = RbPaste.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
            MasterPanel.Visibility = RbMaster.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
            FolderPanel.Visibility = RbFolder.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        }

        private void FolderBrowse_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new Microsoft.Win32.OpenFolderDialog { Title = "Choose the source folder" };
            if (dialog.ShowDialog() == true)
                FolderPathBox.Text = dialog.FolderName;
        }

        private void PasteBox_TextChanged(object sender, TextChangedEventArgs e) => UpdateDetected();

        private void Kind_Changed(object sender, SelectionChangedEventArgs e) => UpdateDetected();

        private void UpdateDetected()
        {
            if (!IsLoaded) return;
            if (CmbKind.SelectedIndex != 0)
            {
                DetectedText.Text = "";
                return;
            }

            var kind = PastedMetadataService.DetectKind(PasteBox.Text);
            DetectedText.Text = kind == PastedMetadataKind.Empty ? "" : $"Detected: {DescribeKind(kind)}";
        }

        private static string DescribeKind(PastedMetadataKind kind) => kind switch
        {
            PastedMetadataKind.SingleBlock => "single block",
            PastedMetadataKind.Tracklist => "tracklist",
            PastedMetadataKind.Csv => "CSV / table",
            _ => "—"
        };

        private void Parse_Click(object sender, RoutedEventArgs e)
        {
            _changes.Clear();
            _folderCoverPairs = new();
            try
            {
                PastedMetadataResult result =
                    RbMaster.IsChecked == true ? ParseMaster()
                    : RbFolder.IsChecked == true ? ParseFolder()
                    : ParsePastedText();

                foreach (var change in result.Changes)
                    _changes.Add(change);
                _folderCoverPairs = result.CoverPairs;

                StatusText.Text = _changes.Count == 0
                    ? "No changes — " + (string.IsNullOrWhiteSpace(result.Summary) ? "nothing matched." : result.Summary)
                    : result.Summary + $"  ({_changes.Count} field change{(_changes.Count == 1 ? "" : "s")})";
                BtnApply.IsEnabled = _changes.Count > 0 || _folderCoverPairs.Count > 0;
            }
            catch (Exception ex)
            {
                StatusText.Text = $"Parse error: {ex.Message}";
            }
        }

        private PastedMetadataResult ParsePastedText()
        {
            PastedMetadataKind? forced = CmbKind.SelectedIndex switch
            {
                1 => PastedMetadataKind.SingleBlock,
                2 => PastedMetadataKind.Tracklist,
                3 => PastedMetadataKind.Csv,
                _ => null
            };
            return PastedMetadataService.Parse(_files, PasteBox.Text, forced);
        }

        private PastedMetadataResult ParseMaster()
        {
            if (CmbMaster.SelectedItem is not AudioFileInfo master)
                return new PastedMetadataResult { Summary = "Pick a master file first." };
            return PastedMetadataService.BuildCopyFromMasterChanges(master, _files);
        }

        private PastedMetadataResult ParseFolder()
        {
            string folder = FolderPathBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(folder) || !System.IO.Directory.Exists(folder))
                return new PastedMetadataResult { Summary = "Pick a source folder first." };

            var fields = SelectedFolderFields();
            if (fields.Count == 0)
                return new PastedMetadataResult { Summary = "Check at least one field to copy." };

            var paths = System.IO.Directory
                .EnumerateFiles(folder, "*", System.IO.SearchOption.AllDirectories)
                .Where(p => SupportedFormats.AudioExtensions.Contains(System.IO.Path.GetExtension(p)));
            var sources = PastedMetadataService.ReadSourceTagSets(paths);
            return PastedMetadataService.BuildCopyFromFolderChanges(sources, _files, fields);
        }

        private HashSet<MetadataEnrichmentField> SelectedFolderFields()
        {
            var fields = new HashSet<MetadataEnrichmentField>();
            void Add(CheckBox box, MetadataEnrichmentField field)
            {
                if (box.IsChecked == true) fields.Add(field);
            }
            Add(FldfTitle, MetadataEnrichmentField.Title);
            Add(FldfArtist, MetadataEnrichmentField.Artist);
            Add(FldfAlbum, MetadataEnrichmentField.Album);
            Add(FldfAlbumArtist, MetadataEnrichmentField.AlbumArtist);
            Add(FldfYear, MetadataEnrichmentField.Year);
            Add(FldfTrack, MetadataEnrichmentField.TrackNumber);
            Add(FldfDisc, MetadataEnrichmentField.DiscNumber);
            Add(FldfGenre, MetadataEnrichmentField.Genre);
            Add(FldfComposer, MetadataEnrichmentField.Composer);
            Add(FldfComment, MetadataEnrichmentField.Comment);
            Add(FldfLyrics, MetadataEnrichmentField.Lyrics);
            Add(FldfCopyright, MetadataEnrichmentField.Copyright);
            Add(FldfCover, MetadataEnrichmentField.CoverArt);
            Add(FldfFileName, MetadataEnrichmentField.FileName);
            return fields;
        }

        // Open larger than before, but never bigger than the program window.
        private void PasteWindow_Loaded(object sender, RoutedEventArgs e)
        {
            if (Owner is { ActualWidth: > 0, ActualHeight: > 0 } owner)
            {
                Width = Math.Min(Width, owner.ActualWidth * 0.95);
                Height = Math.Min(Height, owner.ActualHeight * 0.95);
            }
        }

        private async void Apply_Click(object sender, RoutedEventArgs e)
        {
            var selected = _changes.Where(c => c.IsSelected).ToList();
            // File-name rows rename on disk; everything else is a tag write. Split them so the tag
            // writer never sees a rename, and run renames last (covers still reference old paths).
            var renameChanges = selected.Where(c => c.Field == MetadataEnrichmentField.FileName).ToList();
            var tagChanges = selected.Where(c => c.Field != MetadataEnrichmentField.FileName).ToList();

            bool copyCover = RbMaster.IsChecked == true && ChkCopyCover.IsChecked == true
                             && CmbMaster.SelectedItem is AudioFileInfo;
            bool copyFolderCovers = RbFolder.IsChecked == true && _folderCoverPairs.Count > 0;
            if (selected.Count == 0 && !copyCover && !copyFolderCovers)
            {
                StatusText.Text = "Nothing selected to apply.";
                return;
            }

            string coverNote = copyCover ? " and copy the cover art"
                : copyFolderCovers ? $" and copy cover art to {_folderCoverPairs.Count} file(s)" : "";
            string renameNote = renameChanges.Count > 0 ? $" and rename {renameChanges.Count} file(s)" : "";
            if (!ErrorDialog.Confirm("Apply Pasted Metadata",
                $"Apply {tagChanges.Count} field change{(tagChanges.Count == 1 ? "" : "s")}{coverNote}{renameNote}?",
                this)) return;

            _cts = new CancellationTokenSource();
            SetBusy(true, "Writing changes…");
            try
            {
                bool backups = ChkBackups.IsChecked == true;
                var summary = await _enrichment.ApplyAsync(tagChanges, backups, _cts.Token);
                ApplyToInMemory(tagChanges);

                var cover = default(PastedMetadataService.CoverCopyResult);
                if (copyCover && CmbMaster.SelectedItem is AudioFileInfo master)
                    cover = await PastedMetadataService.CopyCoverFromMasterAsync(master, _files, backups, _cts.Token);
                else if (copyFolderCovers)
                    cover = await PastedMetadataService.CopyCoverPairsAsync(_folderCoverPairs, backups, _cts.Token);
                int coverCount = cover.Copied;

                int renamedCount = 0;
                if (renameChanges.Count > 0)
                {
                    var byPath = _files.ToDictionary(f => f.FilePath, StringComparer.OrdinalIgnoreCase);
                    var pairs = new List<(AudioFileInfo file, string newName)>();
                    foreach (var rc in renameChanges)
                        if (byPath.TryGetValue(rc.FilePath, out var f)) pairs.Add((f, rc.NewValue));
                    renamedCount = await PastedMetadataService.ApplyRenamesAsync(pairs, _cts.Token);
                }

                MetadataChanged = MetadataChanged || summary.FilesChanged > 0 || coverCount > 0 || renamedCount > 0;
                StatusText.Text = $"Applied {summary.ChangesApplied} change{(summary.ChangesApplied == 1 ? "" : "s")} to {summary.FilesChanged} file{(summary.FilesChanged == 1 ? "" : "s")}."
                    + (coverCount > 0 ? $" Cover copied to {coverCount}." : "")
                    + (cover.Failed > 0 ? $" Cover failed on {cover.Failed}." : "")
                    + (renamedCount > 0 ? $" Renamed {renamedCount}." : "")
                    + (summary.FailedFiles > 0 ? $" {summary.FailedFiles} failed." : "");
            }
            catch (OperationCanceledException)
            {
                StatusText.Text = "Cancelled.";
            }
            catch (Exception ex)
            {
                StatusText.Text = $"Write failed: {ex.Message}";
            }
            finally
            {
                SetBusy(false);
                // Safe here and only here: every await above has completed, so nothing can still
                // be registering callbacks on the token.
                _cts?.Dispose();
                _cts = null;
            }
        }

        private void ApplyToInMemory(IEnumerable<MetadataEnrichmentChange> applied)
        {
            var byPath = _files.ToDictionary(f => f.FilePath, StringComparer.OrdinalIgnoreCase);
            foreach (var change in applied)
            {
                if (!byPath.TryGetValue(change.FilePath, out var file)) continue;
                switch (change.Field)
                {
                    case MetadataEnrichmentField.Title: file.Title = change.NewValue; break;
                    case MetadataEnrichmentField.Artist: file.Artist = change.NewValue; break;
                    case MetadataEnrichmentField.Album: file.Album = change.NewValue; break;
                }
            }
        }

        private void SetBusy(bool busy, string? status = null)
        {
            if (status != null) StatusText.Text = status;
            Cursor = busy ? Cursors.Wait : Cursors.Arrow;
            BtnApply.IsEnabled = !busy;
            BtnParse.IsEnabled = !busy;
        }
    }
}
