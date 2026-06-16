using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using AudioQualityChecker.Models;
using AudioQualityChecker.Services;

namespace AudioQualityChecker
{
    public partial class BatchRenameWindow : Window
    {
        private readonly List<AudioFileInfo> _files;
        private readonly Action<AudioFileInfo, string> _onFileRenamed;
        private CancellationTokenSource? _previewCts;
        private List<RenamePreviewItem> _currentPreview = new();

        public BatchRenameWindow(List<AudioFileInfo> files, Action<AudioFileInfo, string> onFileRenamed)
        {
            InitializeComponent();
            _files = files;
            _onFileRenamed = onFileRenamed;
            LoadSmartSettings();
            Loaded += (_, _) =>
            {
                UpdateModeVisibility();
                UpdatePreviewAsync();
            };
        }

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2) return;
            DragMove();
        }

        private void Close_Click(object sender, RoutedEventArgs e) => Close();

        private void PatternBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
            => UpdatePreviewAsync();

        private void Option_Changed(object sender, RoutedEventArgs e)
        {
            UpdateModeVisibility();
            if (IsLoaded)
                SaveSmartSettings();
            UpdatePreviewAsync();
        }

        private async void UpdatePreviewAsync()
        {
            if (!IsLoaded) return;

            // Cancel any in-flight preview generation
            _previewCts?.Cancel();
            var cts = new CancellationTokenSource();
            _previewCts = cts;

            string pattern = PatternBox.Text;
            bool organize = ChkOrganizeFolders.IsChecked == true;
            bool smart = RbSmartRename.IsChecked == true;
            var files = _files.ToList(); // snapshot

            StatusText.Text = "Generating preview...";
            BtnRename.IsEnabled = false;

            // Debounce: wait 150ms before starting heavy work
            try { await Task.Delay(150, cts.Token); }
            catch (OperationCanceledException) { return; }

            try
            {
                var items = await Task.Run(() =>
                {
                    if (smart)
                        return BuildSmartPreview(files);

                    var list = new List<RenamePreviewItem>();
                    foreach (var file in files)
                    {
                        cts.Token.ThrowIfCancellationRequested();

                        string currentName = Path.GetFileName(file.FilePath);
                        string ext = Path.GetExtension(file.FilePath);
                        string newBase = ApplyPattern(pattern, file);

                        newBase = SanitizeFileName(newBase);
                        if (string.IsNullOrWhiteSpace(newBase))
                            newBase = Path.GetFileNameWithoutExtension(file.FilePath);

                        string newName;
                        if (organize)
                        {
                            string artist = SanitizePath(file.Artist ?? "Unknown Artist");
                            string album = "Unknown Album";
                            try
                            {
                                using var tagFile = TagLib.File.Create(file.FilePath);
                                if (!string.IsNullOrWhiteSpace(tagFile.Tag.Album))
                                    album = tagFile.Tag.Album;
                            }
                            catch { }
                            album = SanitizePath(album);
                            newName = Path.Combine(artist, album, newBase + ext);
                        }
                        else
                        {
                            newName = newBase + ext;
                        }

                        list.Add(new RenamePreviewItem
                        {
                            File = file,
                            CurrentName = currentName,
                            Arrow = "→",
                            NewName = newName,
                            Confidence = "",
                            Reason = ""
                        });
                    }
                    return list;
                }, cts.Token);

                if (cts.Token.IsCancellationRequested) return;

                _currentPreview = items;
                PreviewList.ItemsSource = _currentPreview;
                if (smart)
                {
                    int high = _currentPreview.Count(i => i.Confidence == SmartRenameConfidence.High.ToString());
                    int review = _currentPreview.Count(i => i.Confidence == SmartRenameConfidence.Review.ToString());
                    int skipped = _currentPreview.Count(i => i.Confidence == SmartRenameConfidence.Skip.ToString());
                    StatusText.Text = $"{high} high-confidence, {review} review, {skipped} skipped";
                    BtnRename.IsEnabled = high > 0;
                }
                else
                {
                    StatusText.Text = $"{items.Count} file{(items.Count != 1 ? "s" : "")}";
                    BtnRename.IsEnabled = true;
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                StatusText.Text = $"Preview error: {ex.Message}";
                BtnRename.IsEnabled = true;
            }
        }

        private void UpdateModeVisibility()
        {
            if (!IsLoaded) return;
            bool smart = RbSmartRename.IsChecked == true;
            ManualPanel.Visibility = smart ? Visibility.Collapsed : Visibility.Visible;
            SmartPanel.Visibility = smart ? Visibility.Visible : Visibility.Collapsed;
            ChkOrganizeFolders.Visibility = smart ? Visibility.Collapsed : Visibility.Visible;
            ChkSmartIncludeTracks.Visibility = smart ? Visibility.Visible : Visibility.Collapsed;
            ChkSmartAppendNumbers.Visibility = smart ? Visibility.Visible : Visibility.Collapsed;
            ChkSmartRenameClean.Visibility = smart ? Visibility.Visible : Visibility.Collapsed;
        }

        private List<RenamePreviewItem> BuildSmartPreview(IReadOnlyList<AudioFileInfo> files)
        {
            var options = BuildSmartOptions();
            return SmartRenameService.BuildPreview(files, options)
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
        }

        private SmartRenameOptions BuildSmartOptions()
        {
            return new SmartRenameOptions
            {
                Style = CmbSmartStyle.SelectedIndex switch
                {
                    1 => SmartRenameStyle.ArtistTitle,
                    2 => SmartRenameStyle.TitleArtist,
                    3 => SmartRenameStyle.TrackArtistTitle,
                    4 => SmartRenameStyle.AlbumArtistTitle,
                    _ => SmartRenameStyle.AlbumSafe
                },
                FolderMode = CmbSmartFolder.SelectedIndex switch
                {
                    1 => SmartRenameFolderMode.ArtistAlbum,
                    2 => SmartRenameFolderMode.Album,
                    _ => SmartRenameFolderMode.KeepCurrent
                },
                IncludeTrackNumbers = ChkSmartIncludeTracks.IsChecked == true,
                RenameCleanFiles = ChkSmartRenameClean.IsChecked == true,
                ConflictBehavior = ChkSmartAppendNumbers.IsChecked == true
                    ? SmartRenameConflictBehavior.AppendNumber
                    : SmartRenameConflictBehavior.Skip
            };
        }

        private void LoadSmartSettings()
        {
            CmbSmartStyle.SelectedIndex = Math.Clamp(ThemeManager.SmartRenameStyleIndex, 0, 4);
            CmbSmartFolder.SelectedIndex = Math.Clamp(ThemeManager.SmartRenameFolderIndex, 0, 2);
            ChkSmartIncludeTracks.IsChecked = ThemeManager.SmartRenameIncludeTrackNumbers;
            ChkSmartAppendNumbers.IsChecked = ThemeManager.SmartRenameAppendDuplicateNumbers;
            ChkSmartRenameClean.IsChecked = ThemeManager.SmartRenameRenameCleanFiles;
        }

        private void SaveSmartSettings()
        {
            ThemeManager.SmartRenameStyleIndex = Math.Clamp(CmbSmartStyle.SelectedIndex, 0, 4);
            ThemeManager.SmartRenameFolderIndex = Math.Clamp(CmbSmartFolder.SelectedIndex, 0, 2);
            ThemeManager.SmartRenameIncludeTrackNumbers = ChkSmartIncludeTracks.IsChecked == true;
            ThemeManager.SmartRenameAppendDuplicateNumbers = ChkSmartAppendNumbers.IsChecked == true;
            ThemeManager.SmartRenameRenameCleanFiles = ChkSmartRenameClean.IsChecked == true;
            ThemeManager.SavePlayOptions();
        }

        private static string ApplyPattern(string pattern, AudioFileInfo file)
        {
            var result = pattern;
            result = Replace(result, "{artist}", file.Artist);
            result = Replace(result, "{title}", file.Title);
            result = Replace(result, "{bitrate}", file.ReportedBitrate > 0 ? file.ReportedBitrate.ToString() : "");
            result = Replace(result, "{samplerate}", file.SampleRate > 0 ? file.SampleRate.ToString() : "");
            result = Replace(result, "{ext}", file.Extension);

            // Read tag info on demand for fields not in AudioFileInfo
            if (result.Contains("{album}", StringComparison.OrdinalIgnoreCase) ||
                result.Contains("{year}", StringComparison.OrdinalIgnoreCase) ||
                result.Contains("{track}", StringComparison.OrdinalIgnoreCase) ||
                result.Contains("{genre}", StringComparison.OrdinalIgnoreCase) ||
                result.Contains("{codec}", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    using var tagFile = TagLib.File.Create(file.FilePath);
                    result = Replace(result, "{album}", tagFile.Tag.Album);
                    result = Replace(result, "{year}", tagFile.Tag.Year > 0 ? tagFile.Tag.Year.ToString() : "");
                    result = Replace(result, "{track}", tagFile.Tag.Track > 0 ? tagFile.Tag.Track.ToString("D2") : "");
                    result = Replace(result, "{genre}", tagFile.Tag.FirstGenre);
                    result = Replace(result, "{codec}", tagFile.Properties?.Description);
                }
                catch
                {
                    result = Replace(result, "{album}", "");
                    result = Replace(result, "{year}", "");
                    result = Replace(result, "{track}", "");
                    result = Replace(result, "{genre}", "");
                    result = Replace(result, "{codec}", "");
                }
            }

            return result;
        }

        private static string Replace(string input, string tag, string? value)
        {
            if (!input.Contains(tag, StringComparison.OrdinalIgnoreCase))
                return input;
            return input.Replace(tag, value ?? "", StringComparison.OrdinalIgnoreCase);
        }

        private static string SanitizeFileName(string name)
        {
            var invalid = Path.GetInvalidFileNameChars();
            foreach (char c in invalid)
                name = name.Replace(c, '_');
            // Collapse multiple underscores/spaces
            name = Regex.Replace(name, @"[_\s]{2,}", " ").Trim();
            // Trim trailing dots/spaces (Windows limitation)
            return name.TrimEnd('.', ' ');
        }

        private static string SanitizePath(string segment)
        {
            var invalid = Path.GetInvalidFileNameChars();
            foreach (char c in invalid)
                segment = segment.Replace(c, '_');
            segment = segment.Trim().TrimEnd('.', ' ');

            // Prevent path traversal: neutralize .. sequences and strip separator chars
            segment = segment.Replace("..", "__");
            segment = segment.Replace(Path.DirectorySeparatorChar, '_')
                             .Replace(Path.AltDirectorySeparatorChar, '_');

            return string.IsNullOrWhiteSpace(segment) ? "_" : segment;
        }

        private static string VerifyTargetDir(string baseDir, string relativePath)
        {
            // Ensure the resulting path doesn't escape the source directory
            string fullBase = Path.GetFullPath(baseDir) + Path.DirectorySeparatorChar;
            string fullTarget = Path.GetFullPath(Path.Combine(baseDir, relativePath));
            if (!fullTarget.StartsWith(fullBase, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"Rename target would escape source directory.");
            return fullTarget;
        }

        private async void Rename_Click(object sender, RoutedEventArgs e)
        {
            if (RbSmartRename.IsChecked == true)
            {
                await RenameSmartAsync();
                return;
            }

            string pattern = PatternBox.Text;
            bool organize = ChkOrganizeFolders.IsChecked == true;
            BtnRename.IsEnabled = false;
            StatusText.Text = "Renaming files...";

            var filesToRename = _files.ToList(); // snapshot
            int renamed = 0;
            int failed = 0;

            try
            {
                await Task.Run(() =>
                {
                    foreach (var file in filesToRename)
                    {
                        try
                        {
                            string ext = Path.GetExtension(file.FilePath);
                            string dir = Path.GetDirectoryName(file.FilePath) ?? "";
                            string newBase = SanitizeFileName(ApplyPattern(pattern, file));
                            if (string.IsNullOrWhiteSpace(newBase))
                                newBase = Path.GetFileNameWithoutExtension(file.FilePath);

                            string targetDir = dir;
                            if (organize)
                            {
                                string artist = SanitizePath(file.Artist ?? "Unknown Artist");
                                string album = "Unknown Album";
                                try
                                {
                                    using var tagFile = TagLib.File.Create(file.FilePath);
                                    if (!string.IsNullOrWhiteSpace(tagFile.Tag.Album))
                                        album = tagFile.Tag.Album;
                                }
                                catch { }
                                album = SanitizePath(album);
                                // VerifyTargetDir ensures the path can't escape the original directory
                                targetDir = VerifyTargetDir(dir, Path.Combine(artist, album));
                            }

                            Directory.CreateDirectory(targetDir);
                            string newPath = Path.Combine(targetDir, newBase + ext);

                            // Avoid overwriting an existing different file
                            if (File.Exists(newPath) && !string.Equals(newPath, file.FilePath, StringComparison.OrdinalIgnoreCase))
                            {
                                int n = 1;
                                while (File.Exists(newPath))
                                {
                                    newPath = Path.Combine(targetDir, $"{newBase} ({n}){ext}");
                                    n++;
                                }
                            }

                            if (!string.Equals(newPath, file.FilePath, StringComparison.OrdinalIgnoreCase))
                            {
                                File.Move(file.FilePath, newPath);
                                Dispatcher.Invoke(() => _onFileRenamed(file, newPath));
                                renamed++;
                            }
                        }
                        catch
                        {
                            failed++;
                        }
                    }
                });
            }
            catch (Exception ex)
            {
                StatusText.Text = $"Error: {ex.Message}";
                BtnRename.IsEnabled = true;
                return;
            }

            StatusText.Text = $"Renamed {renamed} file{(renamed != 1 ? "s" : "")}" +
                              (failed > 0 ? $", {failed} failed" : "");

            if (failed == 0 && renamed > 0)
                Close();
            else
                BtnRename.IsEnabled = true;
        }

        private async Task RenameSmartAsync()
        {
            var items = _currentPreview
                .Where(i => i.File != null
                            && i.Confidence == SmartRenameConfidence.High.ToString()
                            && !string.IsNullOrWhiteSpace(i.TargetPath))
                .ToList();

            if (items.Count == 0)
            {
                StatusText.Text = "No high-confidence smart renames to apply.";
                return;
            }

            BtnRename.IsEnabled = false;
            StatusText.Text = "Applying high-confidence smart renames...";
            int renamed = 0;
            int failed = 0;

            await Task.Run(() =>
            {
                foreach (var item in items)
                {
                    try
                    {
                        var file = item.File!;
                        if (File.Exists(item.TargetPath) &&
                            !string.Equals(item.TargetPath, file.FilePath, StringComparison.OrdinalIgnoreCase))
                        {
                            failed++;
                            continue;
                        }

                        string? targetDir = Path.GetDirectoryName(item.TargetPath);
                        if (!string.IsNullOrWhiteSpace(targetDir))
                            Directory.CreateDirectory(targetDir);

                        if (!string.Equals(item.TargetPath, file.FilePath, StringComparison.OrdinalIgnoreCase))
                        {
                            File.Move(file.FilePath, item.TargetPath);
                            Dispatcher.Invoke(() => _onFileRenamed(file, item.TargetPath));
                            renamed++;
                        }
                    }
                    catch
                    {
                        failed++;
                    }
                }
            });

            StatusText.Text = $"Smart rename: {renamed} renamed" +
                              (failed > 0 ? $", {failed} failed" : "");
            if (failed == 0 && renamed > 0)
                Close();
            else
                BtnRename.IsEnabled = true;
        }
    }

    public class RenamePreviewItem
    {
        public AudioFileInfo? File { get; set; }
        public string CurrentName { get; set; } = "";
        public string Arrow { get; set; } = "→";
        public string NewName { get; set; } = "";
        public string TargetPath { get; set; } = "";
        public string Confidence { get; set; } = "";
        public string Reason { get; set; } = "";
    }
}
