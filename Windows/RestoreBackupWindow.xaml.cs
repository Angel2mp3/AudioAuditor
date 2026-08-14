using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using AudioQualityChecker.Models;
using AudioQualityChecker.Services;

namespace AudioQualityChecker
{
    /// <summary>
    /// One row per <c>.audioauditor-backup-*</c> file found beside the selected tracks.
    ///
    /// The backup is a whole-file copy, so restoring it is a copy back — never a tag read. That
    /// matters here: a backup's extension is not an audio extension, so TagLib cannot open one and
    /// there is no way to show its tags without restoring it first.
    /// </summary>
    public sealed class BackupEntry : INotifyPropertyChanged
    {
        private bool _isSelected;

        public required string BackupPath { get; init; }
        public required string OriginalPath { get; init; }
        public required string FileName { get; init; }
        public required DateTime TakenUtc { get; init; }
        public required long SizeBytes { get; init; }

        /// <summary>True for the earliest backup of its file — the state before the first edit.</summary>
        public required bool IsOldest { get; init; }

        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (_isSelected == value) return;
                _isSelected = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
            }
        }

        public string Detail =>
            $"Taken {TakenUtc.ToLocalTime():yyyy-MM-dd HH:mm:ss}  •  {FormatSize(SizeBytes)}";

        public Visibility OldestBadgeVisibility => IsOldest ? Visibility.Visible : Visibility.Collapsed;

        public event PropertyChangedEventHandler? PropertyChanged;

        private static string FormatSize(long bytes) => bytes switch
        {
            >= 1024L * 1024 * 1024 => $"{bytes / (1024.0 * 1024 * 1024):0.##} GB",
            >= 1024 * 1024 => $"{bytes / (1024.0 * 1024):0.#} MB",
            >= 1024 => $"{bytes / 1024.0:0} KB",
            _ => $"{bytes} B"
        };
    }

    /// <summary>
    /// Restores an audio file from the backup copy taken before a tag write, and prunes the copies
    /// that are no longer wanted. Backups had no reader at all before this window: they were
    /// created by every batch tool and could only be recovered by renaming files by hand.
    /// </summary>
    public partial class RestoreBackupWindow : Window
    {
        [DllImport("dwmapi.dll", PreserveSig = true)]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);
        private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
        private const int DWMWA_CAPTION_COLOR = 35;

        private readonly ObservableCollection<BackupEntry> _entries = new();

        /// <summary>Guards a second run while one is in flight — these are whole-file copies.</summary>
        private bool _busy;

        /// <summary>Set when a file on disk changed, so the caller can refresh the grid.</summary>
        public bool MetadataChanged { get; private set; }

        public RestoreBackupWindow(List<AudioFileInfo> files, Window owner)
        {
            InitializeComponent();
            Owner = owner;
            BackupList.ItemsSource = _entries;

            Load(files);
        }

        private void Load(List<AudioFileInfo> files)
        {
            int filesWithBackups = 0;

            foreach (var file in files)
            {
                if (string.IsNullOrWhiteSpace(file.FilePath)) continue;

                var backups = FileRenamer.FindBackups(file.FilePath);
                if (backups.Count == 0) continue;
                filesWithBackups++;

                // FindBackups returns oldest first, so index 0 is the pre-first-edit copy.
                for (int i = 0; i < backups.Count; i++)
                {
                    _entries.Add(new BackupEntry
                    {
                        BackupPath = backups[i],
                        OriginalPath = file.FilePath,
                        FileName = file.FileName,
                        TakenUtc = ParseTakenUtc(backups[i]),
                        SizeBytes = SafeLength(backups[i]),
                        IsOldest = i == 0,
                        IsSelected = i == 0
                    });
                }
            }

            SummaryLabel.Text = _entries.Count == 0
                ? $"No backups found for the {files.Count} selected file{(files.Count == 1 ? "" : "s")}. " +
                  "Backups are only written when \"Back up first\" is ticked before an edit."
                : $"{_entries.Count} backup{(_entries.Count == 1 ? "" : "s")} for {filesWithBackups} of " +
                  $"{files.Count} selected file{(files.Count == 1 ? "" : "s")}. " +
                  "Restoring overwrites the file on disk and keeps the backup.";

            BtnRestore.IsEnabled = _entries.Count > 0;
            BtnDelete.IsEnabled = _entries.Count > 0;
        }

        /// <summary>
        /// Reads the UTC stamp out of the backup's name. Falls back to the file's own write time
        /// for a copy whose name was mangled — the row is still restorable either way.
        /// </summary>
        private static DateTime ParseTakenUtc(string backupPath)
        {
            string name = Path.GetFileName(backupPath);
            int at = name.LastIndexOf(FileRenamer.BackupSuffix, StringComparison.OrdinalIgnoreCase);
            if (at >= 0)
            {
                string stamp = name[(at + FileRenamer.BackupSuffix.Length)..];
                if (DateTime.TryParseExact(stamp, "yyyyMMddHHmmss", CultureInfo.InvariantCulture,
                        DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsed))
                    return parsed;
            }

            try { return File.GetLastWriteTimeUtc(backupPath); }
            catch { return DateTime.UtcNow; }
        }

        private static long SafeLength(string path)
        {
            try { return new FileInfo(path).Length; }
            catch { return 0; }
        }

        // ── Selection helpers ──

        private void SelectOldest_Click(object sender, RoutedEventArgs e)
        {
            foreach (var entry in _entries) entry.IsSelected = entry.IsOldest;
        }

        private void SelectAll_Click(object sender, RoutedEventArgs e)
        {
            foreach (var entry in _entries) entry.IsSelected = true;
        }

        private void SelectNone_Click(object sender, RoutedEventArgs e)
        {
            foreach (var entry in _entries) entry.IsSelected = false;
        }

        // ── Actions ──

        private async void RestoreSelected_Click(object sender, RoutedEventArgs e)
        {
            if (_busy) return;

            var chosen = _entries.Where(x => x.IsSelected).ToList();
            if (chosen.Count == 0)
            {
                StatusText.Text = "Nothing selected.";
                return;
            }

            // Two backups of the same file would overwrite each other in an order the list does not
            // make obvious. Make the user pick one rather than silently applying the last.
            var duplicate = chosen.GroupBy(x => x.OriginalPath, StringComparer.OrdinalIgnoreCase)
                                  .FirstOrDefault(g => g.Count() > 1);
            if (duplicate != null)
            {
                StatusText.Text = $"Pick one backup per file — {Path.GetFileName(duplicate.Key)} has " +
                                  $"{duplicate.Count()} selected.";
                return;
            }

            if (!ErrorDialog.Confirm("Restore from Backup",
                    $"This will overwrite {chosen.Count} file{(chosen.Count == 1 ? "" : "s")} on disk with " +
                    $"the backup cop{(chosen.Count == 1 ? "y" : "ies")} you selected.\n\n" +
                    "The backups themselves are kept. Continue?",
                    this, severity: AlertSeverity.Warning))
                return;

            SetBusy(true, "Restoring…");
            int restored = 0;
            var errors = new List<string>();

            try
            {
                await Task.Run(() =>
                {
                    foreach (var entry in chosen)
                    {
                        try
                        {
                            var outcome = FileRenamer.Restore(entry.BackupPath, entry.OriginalPath);
                            if (outcome == RestoreOutcome.Restored) restored++;
                            else errors.Add($"{entry.FileName}: {Describe(outcome)}");
                        }
                        catch (Exception ex)
                        {
                            errors.Add($"{entry.FileName}: {ex.Message}");
                        }
                    }
                });

                MetadataChanged = MetadataChanged || restored > 0;
                StatusText.Text = $"Restored {restored} file{(restored == 1 ? "" : "s")}" +
                                  (errors.Count > 0 ? $" ({errors.Count} failed — {errors[0]})" : ".");
            }
            finally
            {
                SetBusy(false);
            }
        }

        private async void DeleteSelected_Click(object sender, RoutedEventArgs e)
        {
            if (_busy) return;

            var chosen = _entries.Where(x => x.IsSelected).ToList();
            if (chosen.Count == 0)
            {
                StatusText.Text = "Nothing selected.";
                return;
            }

            if (!ErrorDialog.Confirm("Delete Backups",
                    $"Permanently delete {chosen.Count} backup cop{(chosen.Count == 1 ? "y" : "ies")}?\n\n" +
                    "The audio files themselves are not touched, but the edits they protect can no " +
                    "longer be undone. This cannot be reversed.",
                    this, severity: AlertSeverity.Warning))
                return;

            SetBusy(true, "Deleting…");
            int deleted = 0;
            var errors = new List<string>();

            try
            {
                await Task.Run(() =>
                {
                    foreach (var entry in chosen)
                    {
                        try
                        {
                            File.Delete(entry.BackupPath);
                            deleted++;
                        }
                        catch (Exception ex)
                        {
                            errors.Add($"{entry.FileName}: {ex.Message}");
                        }
                    }
                });

                foreach (var entry in chosen)
                    if (!File.Exists(entry.BackupPath)) _entries.Remove(entry);

                BtnRestore.IsEnabled = _entries.Count > 0;
                BtnDelete.IsEnabled = _entries.Count > 0;

                StatusText.Text = $"Deleted {deleted} backup{(deleted == 1 ? "" : "s")}" +
                                  (errors.Count > 0 ? $" ({errors.Count} failed — {errors[0]})" : ".");
            }
            finally
            {
                SetBusy(false);
            }
        }

        private static string Describe(RestoreOutcome outcome) => outcome switch
        {
            RestoreOutcome.BackupMissing => "the backup is no longer on disk",
            RestoreOutcome.BackupEmpty => "the backup is empty — it was never written completely",
            _ => "not restored"
        };

        private void SetBusy(bool busy, string? status = null)
        {
            _busy = busy;
            BtnRestore.IsEnabled = !busy && _entries.Count > 0;
            BtnDelete.IsEnabled = !busy && _entries.Count > 0;
            if (status != null) StatusText.Text = status;
            Cursor = busy ? Cursors.Wait : null;
        }

        // ── Window chrome ──

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

                bool isLight = ThemeManager.CurrentTheme == "Light";
                int darkMode = isLight ? 0 : 1;
                DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref darkMode, sizeof(int));

                if (FindResource("TitleBarBg") is System.Windows.Media.SolidColorBrush captionBrush)
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

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.Key == Key.Escape && !_busy)
            {
                e.Handled = true;
                Close();
                return;
            }
            base.OnKeyDown(e);
        }
    }
}
