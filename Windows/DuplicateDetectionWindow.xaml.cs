using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using AudioQualityChecker.Models;

namespace AudioQualityChecker
{
    public partial class DuplicateDetectionWindow : Window
    {
        private readonly List<AudioFileInfo> _files;

        public DuplicateDetectionWindow(List<AudioFileInfo> files)
        {
            InitializeComponent();
            _files = files;
        }

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2) return;
            DragMove();
        }

        private void Close_Click(object sender, RoutedEventArgs e) => Close();

        private async void Scan_Click(object sender, RoutedEventArgs e)
        {
            BtnScan.IsEnabled = false;
            ResultTree.Items.Clear();
            ProgressText.Text = "Scanning...";

            var groups = await Task.Run(() => FindDuplicates());

            ResultTree.Items.Clear();
            int totalDupes = 0;

            foreach (var group in groups)
            {
                var groupItem = new TreeViewItem
                {
                    Header = $"Duplicate group ({group.Count()} files) — {group.Key}",
                    IsExpanded = true,
                    Foreground = System.Windows.Media.Brushes.White
                };

                foreach (var file in group)
                {
                    var fileItem = new TreeViewItem
                    {
                        Header = $"{file.FileName}  ({file.FileSize}, {file.Duration})",
                        Tag = file,
                        Foreground = (System.Windows.Media.Brush)FindResource("TextSecondary"),
                        FontFamily = new System.Windows.Media.FontFamily("Segoe UI")
                    };
                    groupItem.Items.Add(fileItem);
                    totalDupes++;
                }

                ResultTree.Items.Add(groupItem);
            }

            int groupCount = groups.Count;
            SummaryText.Text = groupCount > 0
                ? $"Found {groupCount} duplicate group{(groupCount != 1 ? "s" : "")} ({totalDupes} files total)"
                : "No duplicates found.";
            ProgressText.Text = "";
            BtnScan.IsEnabled = true;
        }

        private List<IGrouping<string, AudioFileInfo>> FindDuplicates()
        {
            var groups = new List<IGrouping<string, AudioFileInfo>>();

            // Strategy 1: Match by metadata (artist + title, case-insensitive)
            var byMetadata = _files
                .Where(f => !string.IsNullOrWhiteSpace(f.Artist) && !string.IsNullOrWhiteSpace(f.Title))
                .GroupBy(f => $"{f.Artist.Trim()} – {f.Title.Trim()}", StringComparer.OrdinalIgnoreCase)
                .Where(g => g.Count() > 1)
                .ToList();

            // Strategy 2: Match by exact file size + duration (for files without metadata)
            var byFingerprint = _files
                .Where(f => f.DurationSeconds > 0 && f.FileSizeBytes > 0)
                .GroupBy(f => $"{Math.Round(f.DurationSeconds, 1)}s / {f.FileSizeBytes}b")
                .Where(g => g.Count() > 1)
                .ToList();

            // Merge: use metadata matches first, add size/duration matches that aren't already found
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var g in byMetadata)
            {
                groups.Add(g);
                foreach (var f in g)
                    seen.Add(f.FilePath);
            }

            foreach (var g in byFingerprint)
            {
                // Only add if none of the files in this group were already identified
                if (g.All(f => !seen.Contains(f.FilePath)))
                    groups.Add(g);
            }

            return groups;
        }
    }
}
