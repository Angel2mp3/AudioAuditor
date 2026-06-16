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
            try
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
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Scan_Click] {ex}");
                ProgressText.Text = "Scan failed.";
                BtnScan.IsEnabled = true;
            }
        }

        // Matching logic lives in AudioAuditor.Core (DuplicateFinder) so the GUI and CLI share it.
        private List<IGrouping<string, AudioFileInfo>> FindDuplicates()
            => Services.DuplicateFinder.FindDuplicates(_files);
    }
}
