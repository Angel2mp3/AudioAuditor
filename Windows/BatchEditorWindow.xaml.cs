using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using AudioQualityChecker.Models;
using AudioQualityChecker.Services;
using Microsoft.Win32;

namespace AudioQualityChecker
{
    public enum BatchEditorTab
    {
        ManualEdit,
        AutoTag,
        Rename,
        Convert,
        CleanUp
    }

    public partial class BatchEditorWindow : Window
    {
        private readonly List<AudioFileInfo> _files;
        private readonly Action<AudioFileInfo, string> _onFileRenamed;

        private readonly MetadataEnrichmentService _enrichment = new();
        private readonly BatchFieldEditService _batchEdit = new();

        private readonly ObservableCollection<MetadataEnrichmentChange> _changes = new();
        private readonly ObservableCollection<string> _failures = new();
        private IReadOnlyList<MetadataEnrichmentPreview> _previews = Array.Empty<MetadataEnrichmentPreview>();
        private bool _autoTagSearched;
        // Auto-Tag search and Convert run independently — one shared source meant starting a
        // conversion cancelled an in-flight search (and vice versa).
        private CancellationTokenSource? _searchCts;
        private CancellationTokenSource? _convertCts;

        private List<RenamePreviewItem> _renamePreview = new();

        // Tags are read from disk once per file and reused across preview rebuilds. The preview is
        // rebuilt on every keystroke in the pattern box, and rebuilding used to reopen every
        // selected file with TagLib, synchronously, on the UI thread.
        private readonly SmartRenameService.TagCache _renameTagCache = new();

        private byte[]? _coverBytes;
        private string? _coverMime;

        public bool MetadataChanged { get; private set; }

        public BatchEditorWindow(IEnumerable<AudioFileInfo> files, Window owner,
            Action<AudioFileInfo, string> onFileRenamed, BatchEditorTab initialTab)
        {
            InitializeComponent();
            Owner = owner;
            _files = files.ToList();
            _onFileRenamed = onFileRenamed;

            FileCountText.Text = $"{_files.Count} file{(_files.Count == 1 ? "" : "s")} selected";
            ChangesGrid.ItemsSource = _changes;
            PreviewGrid.ItemsSource = _renamePreview;

            LoadSmartSettings();

            // Restore optional source keys; enable those sources when a key is present.
            DiscogsTokenBox.Text = ThemeManager.DiscogsToken;
            FanartKeyBox.Text = ThemeManager.FanartTvApiKey;
            ChkDiscogs.IsChecked = !string.IsNullOrWhiteSpace(ThemeManager.DiscogsToken);
            ChkFanart.IsChecked = !string.IsNullOrWhiteSpace(ThemeManager.FanartTvApiKey);

            // Streaming-link credentials + platform preference.
            SpotifyIdBox.Text = ThemeManager.SpotifyClientId;
            SpotifySecretBox.Text = ThemeManager.SpotifyClientSecret;
            YouTubeKeyBox.Text = ThemeManager.YouTubeApiKey;
            CmbStreamingPlatform.SelectedIndex = Math.Clamp(ThemeManager.StreamingLinkPlatformIndex, 0, 3);

            // Covers every close path (X button, Esc, Alt+F4), not just Close_Click.
            Closed += (_, _) =>
            {
                _searchCts?.Cancel();
                _searchCts?.Dispose();
                _searchCts = null;
                _convertCts?.Cancel();
                _convertCts?.Dispose();
                _convertCts = null;
            };

            Loaded += (_, _) =>
            {
                Tabs.SelectedIndex = (int)initialTab;
                ConfigureFooter();
                UpdateRenameModeVisibility();
                if (Tabs.SelectedIndex == (int)BatchEditorTab.Rename)
                    UpdateRenamePreview();
            };
        }

        private void Header_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed)
                this.SafeDragMove();
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            _searchCts?.Cancel();
            _convertCts?.Cancel();
            Close();
        }

        private void Tabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (e.OriginalSource != Tabs) return;
            ConfigureFooter();

            if (Tabs.SelectedIndex == (int)BatchEditorTab.AutoTag && !_autoTagSearched)
                RunSearchAsync().Observe(nameof(RunSearchAsync));
            else if (Tabs.SelectedIndex == (int)BatchEditorTab.Rename)
                UpdateRenamePreview();
            else if (Tabs.SelectedIndex == (int)BatchEditorTab.Convert)
                InitConvertTab();
            else if (Tabs.SelectedIndex == (int)BatchEditorTab.CleanUp)
                PreviewCleanup();
        }

        // ─────────────────────────── Footer wiring ───────────────────────────

        private void ConfigureFooter()
        {
            switch ((BatchEditorTab)Tabs.SelectedIndex)
            {
                case BatchEditorTab.ManualEdit:
                    BtnSecondary.Visibility = Visibility.Collapsed;
                    BtnTertiary.Visibility = Visibility.Collapsed;
                    BtnPrimary.Content = "Apply to Files";
                    StatusText.Text = "Check the fields you want to set, then apply.";
                    break;
                case BatchEditorTab.AutoTag:
                    BtnSecondary.Visibility = Visibility.Visible;
                    BtnSecondary.Content = "Search Again";
                    BtnTertiary.Visibility = Visibility.Visible;
                    BtnTertiary.Content = "Apply High-Confidence";
                    BtnPrimary.Content = "Apply Selected";
                    break;
                case BatchEditorTab.Rename:
                    BtnSecondary.Visibility = Visibility.Collapsed;
                    BtnTertiary.Visibility = Visibility.Collapsed;
                    BtnPrimary.Content = "Rename Files";
                    break;
                case BatchEditorTab.Convert:
                    BtnSecondary.Visibility = Visibility.Collapsed;
                    BtnTertiary.Visibility = Visibility.Collapsed;
                    BtnPrimary.Content = "Convert Files";
                    break;
                case BatchEditorTab.CleanUp:
                    BtnSecondary.Visibility = Visibility.Visible;
                    BtnSecondary.Content = "Preview";
                    BtnTertiary.Visibility = Visibility.Collapsed;
                    BtnPrimary.Content = "Clean Files";
                    StatusText.Text = "Choose what to strip, Preview, then clean.";
                    break;
            }
        }

        private async void BtnPrimary_Click(object sender, RoutedEventArgs e)
        {
            switch ((BatchEditorTab)Tabs.SelectedIndex)
            {
                case BatchEditorTab.ManualEdit:
                    await ApplyManualAsync();
                    break;
                case BatchEditorTab.AutoTag:
                    await ApplySelectedAsync();
                    break;
                case BatchEditorTab.Rename:
                    await RenameAsync();
                    break;
                case BatchEditorTab.Convert:
                    await ConvertAsync();
                    break;
                case BatchEditorTab.CleanUp:
                    await ApplyCleanupAsync();
                    break;
            }
        }

        private async void BtnSecondary_Click(object sender, RoutedEventArgs e)
        {
            if (Tabs.SelectedIndex == (int)BatchEditorTab.AutoTag)
                await RunSearchAsync();
            else if (Tabs.SelectedIndex == (int)BatchEditorTab.CleanUp)
                PreviewCleanup();
        }

        private async void BtnTertiary_Click(object sender, RoutedEventArgs e)
        {
            if (Tabs.SelectedIndex == (int)BatchEditorTab.AutoTag)
                await ApplyHighConfidenceAsync();
        }

        // ─────────────────────────── Manual Edit ───────────────────────────

        private void MeTrackMode_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (!IsLoaded) return;
            int mode = MeTrackMode.SelectedIndex;
            bool show = mode is 1 or 2;
            MeTrackLabel.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
            MeTrackValue.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
            MeTrackLabel.Text = mode == 2 ? "Start at:" : "Number:";
            if (show && string.IsNullOrWhiteSpace(MeTrackValue.Text))
                MeTrackValue.Text = "1";
        }

        private void MeCover_Changed(object sender, RoutedEventArgs e)
        {
            if (!IsLoaded) return;
            bool fromFile = MeCoverFile.IsChecked == true;
            MeChooseImage.IsEnabled = fromFile;
            if (!fromFile)
            {
                _coverBytes = null;
                _coverMime = null;
                MeCoverPreview.Source = null;
                MeCoverInfo.Text = MeCoverOnline.IsChecked == true
                    ? "Covers will be fetched online per album"
                    : MeCoverRemove.IsChecked == true
                        ? "Covers will be removed from all files"
                        : "No image selected";
            }
        }

        private void ChooseImage_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Title = "Select album cover image",
                Filter = "Image Files|*.jpg;*.jpeg;*.png;*.bmp;*.gif|All Files|*.*"
            };
            if (dialog.ShowDialog() != true) return;

            try
            {
                _coverBytes = File.ReadAllBytes(dialog.FileName);
                _coverMime = CoverArt.MimeTypeForExtension(Path.GetExtension(dialog.FileName));

                using var ms = new MemoryStream(_coverBytes);
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.StreamSource = ms;
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.DecodePixelWidth = 220;
                bmp.EndInit();
                bmp.Freeze();
                MeCoverPreview.Source = bmp;
                MeCoverInfo.Text = $"{Path.GetFileName(dialog.FileName)} ({_coverBytes.Length:N0} bytes)";
            }
            catch (Exception ex)
            {
                MeCoverInfo.Text = $"Error: {ex.Message}";
            }
        }

        private BatchFieldEditOptions BuildManualOptions()
        {
            var o = new BatchFieldEditOptions
            {
                SetTitle = MeSetTitle.IsChecked == true, Title = MeTitle.Text,
                SetArtist = MeSetArtist.IsChecked == true, Artist = MeArtist.Text,
                SetAlbum = MeSetAlbum.IsChecked == true, Album = MeAlbum.Text,
                SetAlbumArtist = MeSetAlbumArtist.IsChecked == true, AlbumArtist = MeAlbumArtist.Text,
                SetYear = MeSetYear.IsChecked == true, Year = MeYear.Text,
                SetGenre = MeSetGenre.IsChecked == true, Genre = MeGenre.Text,
                SetComposer = MeSetComposer.IsChecked == true, Composer = MeComposer.Text,
                SetComment = MeSetComment.IsChecked == true, Comment = MeComment.Text,
                SetDisc = MeSetDisc.IsChecked == true, Disc = MeDisc.Text,
            };

            o.TrackMode = MeTrackMode.SelectedIndex switch
            {
                1 => BatchTrackMode.Fixed,
                2 => BatchTrackMode.AutoIncrement,
                _ => BatchTrackMode.None
            };
            if (o.TrackMode == BatchTrackMode.Fixed)
                o.TrackFixed = MeTrackValue.Text;
            else if (o.TrackMode == BatchTrackMode.AutoIncrement)
            {
                // A typo here used to fall back to 1 and renumber the whole selection anyway.
                // Leave the bad value in place so Validate rejects it before anything is written.
                o.TrackStart = BatchFieldEditService.TryParseTrackStart(MeTrackValue.Text, out var s, out _) ? s : 0;
            }

            if (MeCoverRemove.IsChecked == true)
                o.CoverAction = BatchCoverAction.Remove;
            else if (MeCoverOnline.IsChecked == true)
                o.CoverAction = BatchCoverAction.FetchOnlinePerAlbum;
            else if (MeCoverFile.IsChecked == true && _coverBytes is { Length: > 0 })
            {
                o.CoverAction = BatchCoverAction.SetFromBytes;
                o.CoverBytes = _coverBytes;
                o.CoverMime = _coverMime;
            }

            return o;
        }

        private async Task ApplyManualAsync()
        {
            var options = BuildManualOptions();
            if (!options.HasAnyChange)
            {
                StatusText.Text = "Nothing selected to change. Check a field, track mode, or cover option.";
                return;
            }

            // Catch bad numbers before the confirm prompt — a typo would otherwise be written as 0,
            // silently clearing the year/disc/track on every selected file.
            if (options.TrackMode == BatchTrackMode.AutoIncrement
                && !BatchFieldEditService.TryParseTrackStart(MeTrackValue.Text, out _, out var startError))
            {
                StatusText.Text = startError;
                return;
            }

            var invalid = BatchFieldEditService.Validate(options);
            if (invalid.Count > 0)
            {
                StatusText.Text = string.Join("  ", invalid);
                return;
            }

            if (!ErrorDialog.Confirm("Apply Batch Edit",
                $"Apply these changes to {_files.Count} file{(_files.Count == 1 ? "" : "s")}?\n\nBackups are recommended before writing tags.",
                this)) return;

            SetBusy(true, "Writing tags…");
            try
            {
                var progress = new Progress<(int done, int total, string fileName)>(p =>
                    StatusText.Text = p.done >= p.total ? "Writing…" : $"Writing {p.done + 1}/{p.total}: {p.fileName}");

                var summary = await _batchEdit.ApplyAsync(_files, options, MeBackupsChecked(), progress);
                // Only mirror files that were actually written. Updating the whole selection put
                // values in the grid for files whose write had failed.
                ApplyManualToInMemory(options, summary.WrittenPaths);
                MetadataChanged = summary.FilesChanged > 0;
                _renameTagCache.Clear();    // tags just changed; the rename preview must re-read them

                StatusText.Text = $"Updated {summary.FilesChanged} file{(summary.FilesChanged == 1 ? "" : "s")}."
                    + (summary.FailedFiles > 0 ? $" {summary.FailedFiles} failed." : "");
            }
            catch (Exception ex)
            {
                StatusText.Text = $"Write failed: {ex.Message}";
            }
            finally
            {
                SetBusy(false);
            }
        }

        private bool MeBackupsChecked() => MeBackups.IsChecked == true;

        private async void FillMissing_Click(object sender, RoutedEventArgs e) => await FillMissingAsync();

        private void PasteMetadata_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new PasteMetadataWindow(_files, this);
            dialog.ShowDialog();
            if (dialog.MetadataChanged)
                MetadataChanged = true;
        }

        /// <summary>
        /// One-click "auto-fill anything still missing": searches online for every selected file,
        /// proposes changes for empty fields only (MissingOnly), and writes just the high-confidence
        /// matches. Reuses the same enrichment engine as the Auto-Tag tab.
        /// </summary>
        private async Task FillMissingAsync()
        {
            if (!ErrorDialog.Confirm("Fill Missing Fields",
                $"Search online and fill missing fields for {_files.Count} file{(_files.Count == 1 ? "" : "s")}?\n\n"
                + "Only empty fields are filled, and only high-confidence matches are written.",
                this)) return;

            // Same family as the Auto-Tag search (enrichment over the same selection), so it shares
            // that token source — starting one legitimately supersedes the other.
            _searchCts?.Cancel();
            _searchCts?.Dispose();
            _searchCts = new CancellationTokenSource();
            var fillCts = _searchCts;
            SetBusy(true, "Searching online for missing metadata…");
            try
            {
                var options = MetadataEnrichmentOptions.CreateDefault();
                options.MissingOnly = true;
                // Streaming-link needs explicit opt-in (and keys); leave it out of the missing-fill set.
                options.EnabledFields.Remove(MetadataEnrichmentField.StreamingLink);
                options.UseDiscogs = !string.IsNullOrWhiteSpace(ThemeManager.DiscogsToken);
                options.DiscogsToken = ThemeManager.DiscogsToken;
                options.UseFanartTv = !string.IsNullOrWhiteSpace(ThemeManager.FanartTvApiKey);
                options.FanartTvApiKey = ThemeManager.FanartTvApiKey;
                options.UseAcoustId = !string.IsNullOrWhiteSpace(ThemeManager.AcoustIdApiKey);
                options.AcoustIdApiKey = ThemeManager.AcoustIdApiKey;

                var progress = new Progress<EnrichmentProgress>(p =>
                    StatusText.Text = $"Searching {p.Done}/{p.Total} — {p.Message}");

                var previews = await _enrichment.PreviewAsync(_files, options, progress, null, fillCts.Token);
                var summary = await _enrichment.AutoApplyHighConfidenceAsync(previews, MeBackupsChecked(), fillCts.Token);

                var applied = previews.SelectMany(p => p.Changes)
                    .Where(c => c.Confidence >= MetadataEnrichmentService.HighConfidenceThreshold)
                    .ToList();
                ApplyEnrichmentToInMemory(applied, summary.WrittenPaths);
                MetadataChanged = MetadataChanged || summary.FilesChanged > 0;

                StatusText.Text = summary.ChangesApplied == 0
                    ? "Nothing missing could be confidently filled."
                    : $"Filled {summary.ChangesApplied} missing field{(summary.ChangesApplied == 1 ? "" : "s")} across {summary.FilesChanged} file{(summary.FilesChanged == 1 ? "" : "s")}."
                      + (summary.FailedFiles > 0 ? $" {summary.FailedFiles} failed." : "");
            }
            catch (OperationCanceledException)
            {
                StatusText.Text = "Fill-missing cancelled.";
            }
            catch (Exception ex)
            {
                StatusText.Text = $"Fill-missing failed: {ex.Message}";
            }
            finally
            {
                SetBusy(false);
            }
        }

        private void ApplyManualToInMemory(BatchFieldEditOptions o, ICollection<string> writtenPaths)
        {
            foreach (var file in _files)
            {
                if (!writtenPaths.Contains(file.FilePath)) continue;

                if (o.SetTitle) file.Title = o.Title.Trim();
                if (o.SetArtist) file.Artist = o.Artist.Trim();
                if (o.SetAlbum) file.Album = o.Album.Trim();
                if (o.CoverAction == BatchCoverAction.SetFromBytes || o.CoverAction == BatchCoverAction.FetchOnlinePerAlbum)
                    file.HasAlbumCover = true;
                else if (o.CoverAction == BatchCoverAction.Remove)
                    file.HasAlbumCover = false;
            }
        }

        // ─────────────────────────── Clean Up ───────────────────────────

        private readonly ObservableCollection<JunkCleanChange> _cleanupPreview = new();

        private JunkCleanOptions BuildCleanupOptions() => new()
        {
            CleanComment = ChkCleanComment.IsChecked == true,
            CleanTitle = ChkCleanTitle.IsChecked == true,
            RemoveAllComments = ChkRemoveAllComments.IsChecked == true
        };

        private void CleanupOption_Changed(object sender, RoutedEventArgs e)
        {
            if (!IsLoaded) return;
            PreviewCleanup();
        }

        private void PreviewCleanup()
        {
            CleanupGrid.ItemsSource = _cleanupPreview;
            _cleanupPreview.Clear();

            var options = BuildCleanupOptions();
            if (!options.HasAnyAction)
            {
                StatusText.Text = "Pick at least one thing to strip.";
                return;
            }

            foreach (var change in SourceJunkCleaner.BuildPreview(_files, options))
                _cleanupPreview.Add(change);

            StatusText.Text = _cleanupPreview.Count == 0
                ? "No junk found in the selected files."
                : $"{_cleanupPreview.Count} field change{(_cleanupPreview.Count == 1 ? "" : "s")} found — review, then clean.";
        }

        private async Task ApplyCleanupAsync()
        {
            var options = BuildCleanupOptions();
            if (!options.HasAnyAction)
            {
                StatusText.Text = "Pick at least one thing to strip.";
                return;
            }

            PreviewCleanup();
            if (_cleanupPreview.Count == 0)
            {
                StatusText.Text = "Nothing to clean — no junk found.";
                return;
            }

            if (!ErrorDialog.Confirm("Clean Up Tags",
                $"Apply {_cleanupPreview.Count} change{(_cleanupPreview.Count == 1 ? "" : "s")} to your files?\n\nBackups are recommended before writing tags.",
                this)) return;

            SetBusy(true, "Cleaning tags…");
            try
            {
                var progress = new Progress<(int done, int total, string fileName)>(p =>
                    StatusText.Text = p.done >= p.total ? "Cleaning…" : $"Cleaning {p.done + 1}/{p.total}: {p.fileName}");

                var summary = await SourceJunkCleaner.ApplyAsync(
                    _files, options, ChkCleanupBackups.IsChecked == true, progress);
                MetadataChanged = MetadataChanged || summary.FilesChanged > 0;
                _renameTagCache.Clear();    // tags just changed; the rename preview must re-read them

                StatusText.Text = $"Cleaned {summary.FieldsChanged} field{(summary.FieldsChanged == 1 ? "" : "s")} across {summary.FilesChanged} file{(summary.FilesChanged == 1 ? "" : "s")}."
                    + (summary.FailedFiles > 0 ? $" {summary.FailedFiles} failed." : "");
                PreviewCleanup();
            }
            catch (Exception ex)
            {
                StatusText.Text = $"Clean-up failed: {ex.Message}";
            }
            finally
            {
                SetBusy(false);
            }
        }

        // ─────────────────────────── Auto-Tag ───────────────────────────

        private MetadataEnrichmentOptions BuildEnrichmentOptions()
        {
            var options = MetadataEnrichmentOptions.CreateDefault();
            options.MissingOnly = ChkMissingOnly.IsChecked == true;
            options.ReplaceExistingCover = ChkReplaceCovers.IsChecked == true;
            options.UseMusicBrainz = ChkMusicBrainz.IsChecked == true;
            options.UseCoverArtArchive = ChkCoverArchive.IsChecked == true;
            options.UseITunes = ChkITunes.IsChecked == true;
            options.UseAcoustId = ChkAcoustId.IsChecked == true;
            options.AcoustIdApiKey = ThemeManager.AcoustIdApiKey;
            options.UseDeezer = ChkDeezer.IsChecked == true;
            options.UseTheAudioDb = ChkTheAudioDb.IsChecked == true;
            options.UseDiscogs = ChkDiscogs.IsChecked == true;
            options.DiscogsToken = DiscogsTokenBox.Text.Trim();
            options.UseFanartTv = ChkFanart.IsChecked == true;
            options.FanartTvApiKey = FanartKeyBox.Text.Trim();
            options.SpotifyClientId = SpotifyIdBox.Text.Trim();
            options.SpotifyClientSecret = SpotifySecretBox.Text.Trim();
            options.YouTubeApiKey = YouTubeKeyBox.Text.Trim();
            options.StreamingLinkPlatform = CmbStreamingPlatform.SelectedIndex switch
            {
                1 => StreamingLinkPlatform.Apple,
                2 => StreamingLinkPlatform.Spotify,
                3 => StreamingLinkPlatform.YouTube,
                _ => StreamingLinkPlatform.Deezer
            };
            options.EnabledFields = new HashSet<MetadataEnrichmentField>();

            // Persist the optional keys so they're remembered next time.
            ThemeManager.DiscogsToken = options.DiscogsToken;
            ThemeManager.FanartTvApiKey = options.FanartTvApiKey;
            ThemeManager.SpotifyClientId = options.SpotifyClientId;
            ThemeManager.SpotifyClientSecret = options.SpotifyClientSecret;
            ThemeManager.YouTubeApiKey = options.YouTubeApiKey;
            ThemeManager.StreamingLinkPlatformIndex = Math.Clamp(CmbStreamingPlatform.SelectedIndex, 0, 3);
            ThemeManager.SavePlayOptions();

            AddField(options, FldTitle, MetadataEnrichmentField.Title);
            AddField(options, FldArtist, MetadataEnrichmentField.Artist);
            AddField(options, FldAlbum, MetadataEnrichmentField.Album);
            AddField(options, FldAlbumArtist, MetadataEnrichmentField.AlbumArtist);
            AddField(options, FldYear, MetadataEnrichmentField.Year);
            AddField(options, FldTrack, MetadataEnrichmentField.TrackNumber);
            AddField(options, FldDisc, MetadataEnrichmentField.DiscNumber);
            AddField(options, FldGenre, MetadataEnrichmentField.Genre);
            AddField(options, FldComposer, MetadataEnrichmentField.Composer);
            AddField(options, FldComment, MetadataEnrichmentField.Comment);
            AddField(options, FldLyrics, MetadataEnrichmentField.Lyrics);
            AddField(options, FldCopyright, MetadataEnrichmentField.Copyright);
            AddField(options, FldCover, MetadataEnrichmentField.CoverArt);
            AddField(options, FldStreamingLink, MetadataEnrichmentField.StreamingLink);
            return options;
        }

        private static void AddField(MetadataEnrichmentOptions options, CheckBox box, MetadataEnrichmentField field)
        {
            if (box.IsChecked == true) options.EnabledFields.Add(field);
        }

        private async Task RunSearchAsync()
        {
            _searchCts?.Cancel();
            _searchCts?.Dispose();
            _searchCts = new CancellationTokenSource();
            var searchCts = _searchCts;
            _autoTagSearched = true;
            _changes.Clear();
            _failures.Clear();
            FailureToggle.Visibility = Visibility.Collapsed;
            FailurePopup.IsOpen = false;
            SetBusy(true, "Searching metadata providers…");

            int matched = 0, failed = 0;
            try
            {
                var options = BuildEnrichmentOptions();

                // Live per-file outcome: keep a running matched/failed tally in the status bar.
                var progress = new Progress<EnrichmentProgress>(p =>
                {
                    if (p.Outcome is EnrichmentOutcome.Matched or EnrichmentOutcome.LowConfidence) matched++;
                    else failed++;
                    StatusText.Text = $"Searching {p.Done}/{p.Total} — {p.Message}"
                        + (failed > 0 ? $"  ·  {failed} unmatched" : "");
                });

                // Stream each file's result into the grid as soon as it completes.
                var onResult = new Progress<MetadataEnrichmentPreview>(preview =>
                {
                    foreach (var change in preview.Changes)
                        _changes.Add(change);
                    if (OutcomeIsFailure(preview))
                        _failures.Add(DescribeFailure(preview));
                });

                _previews = await _enrichment.PreviewAsync(_files, options, progress, onResult, searchCts.Token);

                int selected = _changes.Count(c => c.IsSelected);
                StatusText.Text = _changes.Count == 0
                    ? "No safe changes found. Try disabling Missing only or another provider."
                    : $"Found {_changes.Count} proposed change{(_changes.Count == 1 ? "" : "s")}; {selected} high-confidence pre-selected."
                      + (failed > 0 ? $"  ·  {failed} file{(failed == 1 ? "" : "s")} unmatched." : "");

                UpdateFailureToggle();
            }
            catch (OperationCanceledException)
            {
                StatusText.Text = "Search cancelled.";
            }
            catch (Exception ex)
            {
                StatusText.Text = $"Search failed: {ex.Message}";
            }
            finally
            {
                SetBusy(false);
            }
        }

        private static bool OutcomeIsFailure(MetadataEnrichmentPreview preview)
            => preview.Candidate == null || preview.Confidence < MetadataEnrichmentService.ReviewConfidenceThreshold;

        private static string DescribeFailure(MetadataEnrichmentPreview preview)
        {
            if (preview.Candidate == null)
                return $"{preview.File.FileName} — {(string.IsNullOrWhiteSpace(preview.Status) ? "no match found" : preview.Status)}";
            return $"{preview.File.FileName} — low confidence ({preview.Confidence:P0}, {preview.Candidate.Provider})";
        }

        private void UpdateFailureToggle()
        {
            FailureList.ItemsSource = null;
            FailureList.ItemsSource = _failures;
            FailureToggle.Content = $"⚠ {_failures.Count} unmatched";
            FailureToggle.Visibility = _failures.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        private void FailureToggle_Click(object sender, RoutedEventArgs e)
            => FailurePopup.IsOpen = !FailurePopup.IsOpen;

        private async Task ApplySelectedAsync()
        {
            var selected = _changes.Where(c => c.IsSelected).ToList();
            if (selected.Count == 0)
            {
                StatusText.Text = "No changes selected.";
                return;
            }

            if (!ErrorDialog.Confirm("Apply Metadata Changes",
                $"Apply {selected.Count} selected metadata change{(selected.Count == 1 ? "" : "s")}?",
                this)) return;

            await WriteEnrichmentAsync(() => _enrichment.ApplyAsync(selected, ChkBackups.IsChecked == true), selected);
        }

        private async Task ApplyHighConfidenceAsync()
        {
            if (!_autoTagSearched || _previews.Count == 0)
            {
                await RunSearchAsync();
                if (_previews.Count == 0) return;
            }

            int highCount = _previews.SelectMany(p => p.Changes)
                .Count(c => c.Confidence >= MetadataEnrichmentService.HighConfidenceThreshold);
            if (highCount == 0)
            {
                StatusText.Text = "No high-confidence matches to apply.";
                return;
            }

            if (!ErrorDialog.Confirm("Auto-Tag",
                $"Auto-apply {highCount} high-confidence change{(highCount == 1 ? "" : "s")} across your selection?",
                this)) return;

            var applied = _previews.SelectMany(p => p.Changes)
                .Where(c => c.Confidence >= MetadataEnrichmentService.HighConfidenceThreshold)
                .ToList();
            await WriteEnrichmentAsync(
                () => _enrichment.AutoApplyHighConfidenceAsync(_previews, ChkBackups.IsChecked == true), applied);
        }

        private async Task WriteEnrichmentAsync(
            Func<Task<MetadataEnrichmentApplySummary>> apply, List<MetadataEnrichmentChange> applied)
        {
            SetBusy(true, "Writing selected metadata changes…");
            try
            {
                var summary = await apply();
                ApplyEnrichmentToInMemory(applied, summary.WrittenPaths);
                MetadataChanged = MetadataChanged || summary.FilesChanged > 0;
                _renameTagCache.Clear();    // tags just changed; the rename preview must re-read them
                StatusText.Text = $"Applied {summary.ChangesApplied} change{(summary.ChangesApplied == 1 ? "" : "s")} to {summary.FilesChanged} file{(summary.FilesChanged == 1 ? "" : "s")}."
                    + (summary.FailedFiles > 0 ? $" {summary.FailedFiles} failed." : "");
            }
            catch (Exception ex)
            {
                StatusText.Text = $"Write failed: {ex.Message}";
            }
            finally
            {
                SetBusy(false);
            }
        }

        private void ApplyEnrichmentToInMemory(
            IEnumerable<MetadataEnrichmentChange> applied, ICollection<string> writtenPaths)
        {
            var byPath = _files.ToDictionary(f => f.FilePath, StringComparer.OrdinalIgnoreCase);
            foreach (var change in applied)
            {
                // A change is only real once its file's tags reached disk.
                if (!writtenPaths.Contains(change.FilePath)) continue;
                if (!byPath.TryGetValue(change.FilePath, out var file)) continue;
                switch (change.Field)
                {
                    case MetadataEnrichmentField.Title: file.Title = change.NewValue; break;
                    case MetadataEnrichmentField.Artist: file.Artist = change.NewValue; break;
                    case MetadataEnrichmentField.Album: file.Album = change.NewValue; break;
                    case MetadataEnrichmentField.CoverArt: file.HasAlbumCover = true; break;
                }
            }
        }

        // ─────────────────────────── Rename ───────────────────────────

        private void RenameOption_Changed(object sender, RoutedEventArgs e)
        {
            UpdateRenameModeVisibility();
            if (IsLoaded) SaveSmartSettings();
            UpdateRenamePreview();
        }

        private void PatternBox_TextChanged(object sender, TextChangedEventArgs e) => UpdateRenamePreview();

        private void UpdateRenameModeVisibility()
        {
            if (!IsLoaded) return;
            bool smart = RbSmartRename.IsChecked == true;
            ManualPanel.Visibility = smart ? Visibility.Collapsed : Visibility.Visible;
            SmartPanel.Visibility = smart ? Visibility.Visible : Visibility.Collapsed;
            ChkOrganizeFolders.Visibility = smart ? Visibility.Collapsed : Visibility.Visible;
            ChkSmartIncludeTracks.Visibility = smart ? Visibility.Visible : Visibility.Collapsed;
            ChkSmartAppendNumbers.Visibility = smart ? Visibility.Visible : Visibility.Collapsed;
            ChkSmartRenameClean.Visibility = smart ? Visibility.Visible : Visibility.Collapsed;
            SmartCustomPattern.Visibility = smart && CmbSmartStyle.SelectedIndex == 5
                ? Visibility.Visible : Visibility.Collapsed;
        }

        private void UpdateRenamePreview()
        {
            if (!IsLoaded || Tabs.SelectedIndex != (int)BatchEditorTab.Rename) return;

            try
            {
                var options = BuildRenameOptions();
                _renamePreview = SmartRenameService.BuildPreview(_files, options, null, _renameTagCache)
                    .Select(p => new RenamePreviewItem
                    {
                        File = p.File,
                        CurrentName = p.CurrentName,
                        Arrow = "→",
                        NewName = p.NewName,
                        TargetPath = p.TargetPath,
                        Confidence = p.Confidence.ToString(),
                        Reason = string.Join("; ", p.Reasons.Distinct())
                    })
                    .ToList();
                PreviewGrid.ItemsSource = _renamePreview;
                UpdateRenameStatus();
            }
            catch (Exception ex)
            {
                StatusText.Text = $"Preview error: {ex.Message}";
            }
        }

        /// <summary>
        /// Recomputes the "N of M will be renamed" line and the Rename button's enabled state from
        /// the current preview rows. Called after a rebuild and after a manual name edit, since an
        /// edit can promote a Skip row into the applicable set.
        /// </summary>
        private void UpdateRenameStatus()
        {
            int applicable = RenameableCount();
            StatusText.Text = $"{applicable} of {_renamePreview.Count} file{(_renamePreview.Count == 1 ? "" : "s")} will be renamed";
            BtnPrimary.IsEnabled = applicable > 0;
        }

        private SmartRenameOptions BuildRenameOptions()
        {
            bool smart = RbSmartRename.IsChecked == true;
            var options = new SmartRenameOptions
            {
                TrackPadWidth = CmbTrackPad.SelectedIndex + 1,
                FindText = FindBox.Text,
                ReplaceText = ReplaceBox.Text,
                NameCase = CmbNameCase.SelectedIndex switch
                {
                    1 => SmartRenameNameCase.Lower,
                    2 => SmartRenameNameCase.Upper,
                    3 => SmartRenameNameCase.Title,
                    _ => SmartRenameNameCase.None
                },
                SpaceMode = CmbSpaceMode.SelectedIndex switch
                {
                    1 => SmartRenameSpaceMode.Underscores,
                    2 => SmartRenameSpaceMode.Spaces,
                    _ => SmartRenameSpaceMode.Keep
                },
                StripFeaturing = ChkStripFeaturing.IsChecked == true,
            };

            if (!smart)
            {
                // Manual pattern == Smart "Custom" style with explicit intent (rename everything).
                options.Style = SmartRenameStyle.Custom;
                options.CustomPattern = PatternBox.Text;
                options.FolderMode = ChkOrganizeFolders.IsChecked == true
                    ? SmartRenameFolderMode.ArtistAlbum : SmartRenameFolderMode.KeepCurrent;
                options.RenameCleanFiles = true;
                options.ConflictBehavior = SmartRenameConflictBehavior.AppendNumber;
                return options;
            }

            options.Style = CmbSmartStyle.SelectedIndex switch
            {
                1 => SmartRenameStyle.ArtistTitle,
                2 => SmartRenameStyle.TitleArtist,
                3 => SmartRenameStyle.TrackArtistTitle,
                4 => SmartRenameStyle.AlbumArtistTitle,
                5 => SmartRenameStyle.Custom,
                _ => SmartRenameStyle.AlbumSafe
            };
            if (options.Style == SmartRenameStyle.Custom)
                options.CustomPattern = SmartCustomPattern.Text;
            options.FolderMode = CmbSmartFolder.SelectedIndex switch
            {
                1 => SmartRenameFolderMode.ArtistAlbum,
                2 => SmartRenameFolderMode.Album,
                _ => SmartRenameFolderMode.KeepCurrent
            };
            options.IncludeTrackNumbers = ChkSmartIncludeTracks.IsChecked == true;
            options.RenameCleanFiles = ChkSmartRenameClean.IsChecked == true;
            options.ConflictBehavior = ChkSmartAppendNumbers.IsChecked == true
                ? SmartRenameConflictBehavior.AppendNumber
                : SmartRenameConflictBehavior.Skip;
            return options;
        }

        private void PreviewGrid_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
        {
            if (e.EditAction != DataGridEditAction.Commit) return;
            if (e.Row.Item is not RenamePreviewItem item || item.File == null) return;
            if (e.EditingElement is not TextBox tb) return;

            string edited = tb.Text.Trim();
            if (string.IsNullOrWhiteSpace(edited))
            {
                StatusText.Text = "Name can't be empty.";
                return;
            }

            item.NewName = edited;
            item.TargetPath = ResolveTargetPath(item.File, edited);
            if (item.Confidence == SmartRenameConfidence.Skip.ToString())
                item.Confidence = SmartRenameConfidence.High.ToString();
            item.Reason = "Manually edited";
            UpdateRenameStatus();
        }

        private static string ResolveTargetPath(AudioFileInfo file, string relativeName)
        {
            var sourceDir = Path.GetDirectoryName(file.FilePath) ?? "";
            var target = Path.GetFullPath(Path.Combine(sourceDir, relativeName));
            var baseDir = Path.GetFullPath(sourceDir);
            if (!baseDir.EndsWith(Path.DirectorySeparatorChar))
                baseDir += Path.DirectorySeparatorChar;
            // Guard against an edited name escaping the source directory.
            if (!target.StartsWith(baseDir, StringComparison.OrdinalIgnoreCase))
                return Path.Combine(sourceDir, Path.GetFileName(relativeName));
            return target;
        }

        private async Task RenameAsync()
        {
            PreviewGrid.CommitEdit(DataGridEditingUnit.Row, true);

            var items = _renamePreview
                .Where(i => i.File != null
                            && i.Confidence != SmartRenameConfidence.Skip.ToString()
                            && !string.IsNullOrWhiteSpace(i.TargetPath))
                .ToList();
            if (items.Count == 0)
            {
                StatusText.Text = "No files to rename.";
                return;
            }

            SetBusy(true, "Renaming files…");
            int renamed = 0, failed = 0, unchanged = 0, conflicts = 0;

            await Task.Run(() =>
            {
                foreach (var item in items)
                {
                    try
                    {
                        var file = item.File!;
                        var outcome = FileRenamer.Rename(file.FilePath, item.TargetPath);
                        if (outcome == RenameOutcome.TargetExists) { conflicts++; continue; }
                        if (outcome == RenameOutcome.Unchanged) { unchanged++; continue; }

                        Dispatcher.Invoke(() => _onFileRenamed(file, item.TargetPath));
                        renamed++;
                    }
                    catch
                    {
                        failed++;
                    }
                }
            });

            SetBusy(false);
            MetadataChanged = MetadataChanged || renamed > 0;
            if (renamed > 0)
            {
                _renameTagCache.Clear();    // cache is keyed by path; the paths just moved
                UpdateRenamePreview();      // rebuild first: it overwrites StatusText with its own line
            }

            StatusText.Text = $"Renamed {renamed} file{(renamed == 1 ? "" : "s")}"
                + (unchanged > 0 ? $", {unchanged} already named correctly" : "")
                + (conflicts > 0 ? $", {conflicts} skipped (target already exists)" : "")
                + (failed > 0 ? $", {failed} failed" : "");
        }

        private void LoadSmartSettings()
        {
            CmbSmartStyle.SelectedIndex = Math.Clamp(ThemeManager.SmartRenameStyleIndex, 0, 5);
            CmbSmartFolder.SelectedIndex = Math.Clamp(ThemeManager.SmartRenameFolderIndex, 0, 2);
            ChkSmartIncludeTracks.IsChecked = ThemeManager.SmartRenameIncludeTrackNumbers;
            ChkSmartAppendNumbers.IsChecked = ThemeManager.SmartRenameAppendDuplicateNumbers;
            ChkSmartRenameClean.IsChecked = ThemeManager.SmartRenameRenameCleanFiles;
            CmbNameCase.SelectedIndex = Math.Clamp(ThemeManager.SmartRenameNameCaseIndex, 0, 3);
            CmbSpaceMode.SelectedIndex = Math.Clamp(ThemeManager.SmartRenameSpaceModeIndex, 0, 2);
            ChkStripFeaturing.IsChecked = ThemeManager.SmartRenameStripFeaturing;
        }

        private void SaveSmartSettings()
        {
            ThemeManager.SmartRenameStyleIndex = Math.Clamp(CmbSmartStyle.SelectedIndex, 0, 5);
            ThemeManager.SmartRenameFolderIndex = Math.Clamp(CmbSmartFolder.SelectedIndex, 0, 2);
            ThemeManager.SmartRenameIncludeTrackNumbers = ChkSmartIncludeTracks.IsChecked == true;
            ThemeManager.SmartRenameAppendDuplicateNumbers = ChkSmartAppendNumbers.IsChecked == true;
            ThemeManager.SmartRenameRenameCleanFiles = ChkSmartRenameClean.IsChecked == true;
            ThemeManager.SmartRenameNameCaseIndex = Math.Clamp(CmbNameCase.SelectedIndex, 0, 3);
            ThemeManager.SmartRenameSpaceModeIndex = Math.Clamp(CmbSpaceMode.SelectedIndex, 0, 2);
            ThemeManager.SmartRenameStripFeaturing = ChkStripFeaturing.IsChecked == true;
            ThemeManager.SavePlayOptions();
        }

        // ─────────────────────────── Convert ───────────────────────────

        private bool _convertInit;

        private void InitConvertTab()
        {
            if (!_convertInit)
            {
                _convertInit = true;
                ConvertFfmpegPath.Text = AudioConversionService.BundledFfmpegFolder;
            }

            RefreshFfmpegAvailability();
            PopulateConvertQuality();
            UpdateConvertPreview();
        }

        /// <summary>Shows or hides the "get ffmpeg" block based on a fresh lookup.</summary>
        private bool RefreshFfmpegAvailability()
        {
            bool available = AudioConversionService.IsAvailable;
            ConvertFfmpegWarning.Visibility = available ? Visibility.Collapsed : Visibility.Visible;
            return available;
        }

        private void FfmpegDownload_Click(object sender, RoutedEventArgs e)
            => OpenShell(AudioConversionService.DownloadPageUrl, "the download page");

        private void FfmpegOpenFolder_Click(object sender, RoutedEventArgs e)
        {
            string folder = AudioConversionService.BundledFfmpegFolder;
            try { Directory.CreateDirectory(folder); } catch { /* fall through; OpenShell reports it */ }
            OpenShell(folder, "the ffmpeg folder");
        }

        private void FfmpegRecheck_Click(object sender, RoutedEventArgs e)
        {
            // Drop the cached lookup so a binary added since the app started is found.
            AudioConversionService.ResetCache();
            StatusText.Text = RefreshFfmpegAvailability()
                ? "ffmpeg found — conversion is enabled."
                : "Still no ffmpeg in that folder or on PATH.";
        }

        private void OpenShell(string target, string what)
        {
            try
            {
                Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                ErrorDialog.ShowWarning("AudioAuditor", $"Couldn't open {what}.\n{ex.Message}", this);
            }
        }

        private void ConvertFormat_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (!IsLoaded) return;
            PopulateConvertQuality();
            UpdateConvertPreview();
        }

        private AudioConversionFormat SelectedConvertFormat() => CmbConvertFormat.SelectedIndex switch
        {
            1 => AudioConversionFormat.Flac,
            2 => AudioConversionFormat.Wav,
            3 => AudioConversionFormat.Aac,
            4 => AudioConversionFormat.Ogg,
            5 => AudioConversionFormat.Opus,
            6 => AudioConversionFormat.Wma,
            7 => AudioConversionFormat.Aiff,
            _ => AudioConversionFormat.Mp3
        };

        /// <summary>Quality choices depend on the codec: VBR levels for MP3/OGG, bitrate for the
        /// lossy CBR codecs, and nothing for the lossless/PCM formats.</summary>
        private void PopulateConvertQuality()
        {
            var format = SelectedConvertFormat();
            CmbConvertQuality.Items.Clear();
            switch (format)
            {
                case AudioConversionFormat.Mp3:
                    ConvertQualityLabel.Text = "Quality (VBR)";
                    foreach (var (label, _) in Mp3Qualities) CmbConvertQuality.Items.Add(new ComboBoxItem { Content = label });
                    CmbConvertQuality.SelectedIndex = 1;
                    SetConvertQualityEnabled(true);
                    break;
                case AudioConversionFormat.Ogg:
                    ConvertQualityLabel.Text = "Quality (q)";
                    for (int q = 10; q >= 0; q--) CmbConvertQuality.Items.Add(new ComboBoxItem { Content = $"q{q}" });
                    CmbConvertQuality.SelectedIndex = 4; // q6
                    SetConvertQualityEnabled(true);
                    break;
                case AudioConversionFormat.Aac:
                case AudioConversionFormat.Opus:
                case AudioConversionFormat.Wma:
                    ConvertQualityLabel.Text = "Bitrate";
                    foreach (var b in Bitrates) CmbConvertQuality.Items.Add(new ComboBoxItem { Content = $"{b} kbps" });
                    CmbConvertQuality.SelectedIndex = 3; // 256
                    SetConvertQualityEnabled(true);
                    break;
                default: // Flac, Wav, Aiff
                    ConvertQualityLabel.Text = "Lossless";
                    CmbConvertQuality.Items.Add(new ComboBoxItem { Content = "—" });
                    CmbConvertQuality.SelectedIndex = 0;
                    SetConvertQualityEnabled(false);
                    break;
            }
        }

        private void SetConvertQualityEnabled(bool enabled)
        {
            CmbConvertQuality.IsEnabled = enabled;
            ConvertQualityLabel.Opacity = enabled ? 1.0 : 0.6;
        }

        private static readonly (string label, int q)[] Mp3Qualities =
        {
            ("V0 (~245 kbps)", 0), ("V2 (~190 kbps)", 2), ("V4 (~165 kbps)", 4), ("V6 (~130 kbps)", 6)
        };

        private static readonly int[] Bitrates = { 128, 160, 192, 256, 320 };

        private void UpdateConvertPreview()
        {
            if (!IsLoaded) return;
            string ext = BuildConvertOptions().Extension;
            var preview = _files.Select(f => new RenamePreviewItem
            {
                File = f,
                CurrentName = f.FileName,
                Arrow = "→",
                NewName = Path.GetFileNameWithoutExtension(f.FileName) + "." + ext
            }).ToList();
            ConvertGrid.ItemsSource = preview;
            StatusText.Text = $"{_files.Count} file{(_files.Count == 1 ? "" : "s")} → {ext.ToUpperInvariant()}";
        }

        private AudioConversionOptions BuildConvertOptions()
        {
            var format = SelectedConvertFormat();
            var options = new AudioConversionOptions
            {
                TargetFormat = format,
                OutputFolder = ConvertOutputBox.Text.Trim(),
                KeepMetadata = ChkConvertKeepMetadata.IsChecked == true,
                Overwrite = ChkConvertOverwrite.IsChecked == true,
                DeleteOriginal = ChkConvertDeleteOriginal.IsChecked == true
            };

            int idx = Math.Max(0, CmbConvertQuality.SelectedIndex);
            switch (format)
            {
                case AudioConversionFormat.Mp3:
                    options.Mp3Quality = idx < Mp3Qualities.Length ? Mp3Qualities[idx].q : 2;
                    break;
                case AudioConversionFormat.Ogg:
                    options.OggQuality = Math.Clamp(10 - idx, 0, 10);
                    break;
                case AudioConversionFormat.Aac:
                case AudioConversionFormat.Opus:
                case AudioConversionFormat.Wma:
                    options.BitrateKbps = idx < Bitrates.Length ? Bitrates[idx] : 256;
                    break;
            }

            return options;
        }

        private void ConvertBrowse_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFolderDialog { Title = "Choose output folder" };
            if (dialog.ShowDialog() == true)
            {
                ConvertOutputBox.Text = dialog.FolderName;
                UpdateConvertPreview();
            }
        }

        private async Task ConvertAsync()
        {
            if (!AudioConversionService.IsAvailable)
            {
                ConvertFfmpegWarning.Visibility = Visibility.Visible;
                StatusText.Text = "ffmpeg not found — conversion unavailable.";
                return;
            }

            var options = BuildConvertOptions();
            string warning = options.DeleteOriginal
                ? "\n\nOriginals will be DELETED after a successful conversion."
                : "";
            if (!ErrorDialog.Confirm("Convert Files",
                $"Convert {_files.Count} file{(_files.Count == 1 ? "" : "s")} to {options.Extension.ToUpperInvariant()}?{warning}",
                this,
                severity: options.DeleteOriginal ? AlertSeverity.Warning : AlertSeverity.Question)) return;

            _convertCts?.Cancel();
            _convertCts?.Dispose();
            _convertCts = new CancellationTokenSource();
            var convertCts = _convertCts;
            SetBusy(true, "Converting…");
            try
            {
                var service = new AudioConversionService();
                var progress = new Progress<(int done, int total, string fileName)>(p =>
                    StatusText.Text = $"Converting {p.done}/{p.total}: {p.fileName}");

                var result = await service.ConvertAsync(_files, options, progress, convertCts.Token);
                MetadataChanged = MetadataChanged || result.Converted > 0;

                StatusText.Text = $"Converted {result.Converted} file{(result.Converted == 1 ? "" : "s")}"
                    + (result.Skipped > 0 ? $", {result.Skipped} skipped" : "")
                    + (result.Failed > 0 ? $", {result.Failed} failed" : "") + ".";
            }
            catch (OperationCanceledException)
            {
                StatusText.Text = "Conversion cancelled.";
            }
            catch (Exception ex)
            {
                StatusText.Text = $"Conversion failed: {ex.Message}";
            }
            finally
            {
                SetBusy(false);
            }
        }

        private void SetBusy(bool busy, string? status = null)
        {
            if (status != null) StatusText.Text = status;
            Cursor = busy ? Cursors.Wait : Cursors.Arrow;
            BtnPrimary.IsEnabled = !busy;
            BtnSecondary.IsEnabled = !busy;
            BtnTertiary.IsEnabled = !busy;
            MeFillMissing.IsEnabled = !busy;

            // The Rename tab disables Rename when nothing is renameable; a blanket re-enable here
            // would hand it back at the end of every operation. Button state only — the caller's
            // result message must survive.
            if (!busy && Tabs.SelectedIndex == (int)BatchEditorTab.Rename)
                BtnPrimary.IsEnabled = RenameableCount() > 0;
        }

        private int RenameableCount() =>
            _renamePreview.Count(i => i.Confidence != SmartRenameConfidence.Skip.ToString());
    }

    /// <summary>Turns the small cover-art preview bytes into an Image source for the changes grid.</summary>
    public sealed class CoverThumbConverter : System.Windows.Data.IValueConverter
    {
        public object? Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            if (value is not byte[] bytes || bytes.Length == 0) return null;
            try
            {
                using var ms = new MemoryStream(bytes);
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.StreamSource = ms;
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.DecodePixelWidth = 48;
                bmp.EndInit();
                bmp.Freeze();
                return bmp;
            }
            catch
            {
                return null;
            }
        }

        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
            => throw new NotSupportedException();
    }
}
