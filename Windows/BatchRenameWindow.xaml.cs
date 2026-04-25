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

namespace AudioQualityChecker
{
    public partial class BatchRenameWindow : Window
    {
        private readonly List<AudioFileInfo> _files;
        private readonly Action<AudioFileInfo, string> _onFileRenamed;
        private CancellationTokenSource? _previewCts;

        public BatchRenameWindow(List<AudioFileInfo> files, Action<AudioFileInfo, string> onFileRenamed)
        {
            InitializeComponent();
            _files = files;
            _onFileRenamed = onFileRenamed;
            Loaded += (_, _) => UpdatePreviewAsync();
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
            => UpdatePreviewAsync();

        private async void UpdatePreviewAsync()
        {
            if (!IsLoaded) return;

            // Cancel any in-flight preview generation
            _previewCts?.Cancel();
            var cts = new CancellationTokenSource();
            _previewCts = cts;

            string pattern = PatternBox.Text;
            bool organize = ChkOrganizeFolders.IsChecked == true;
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
                            CurrentName = currentName,
                            Arrow = "→",
                            NewName = newName
                        });
                    }
                    return list;
                }, cts.Token);

                if (cts.Token.IsCancellationRequested) return;

                PreviewList.ItemsSource = items;
                StatusText.Text = $"{items.Count} file{(items.Count != 1 ? "s" : "")}";
                BtnRename.IsEnabled = true;
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                StatusText.Text = $"Preview error: {ex.Message}";
                BtnRename.IsEnabled = true;
            }
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
    }

    public class RenamePreviewItem
    {
        public string CurrentName { get; set; } = "";
        public string Arrow { get; set; } = "→";
        public string NewName { get; set; } = "";
    }
}
