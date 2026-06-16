using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;
using AudioQualityChecker.Services;

namespace AudioQualityChecker
{
    public partial class MainWindow
    {
        private readonly HashSet<string> _sessionHiddenColumns = new(StringComparer.OrdinalIgnoreCase);

        private DateTime _lastHorizontalScrollTime = DateTime.MinValue;
        private double _fileGridHorizontalScrollAnchorOffset;
        private bool _fileGridRestoringHorizontalScrollAnchor;

        private DispatcherTimer? _columnLayoutSaveTimer;

        /// <summary>
        /// Persists column order/width whenever the user reorders or resizes a column.
        /// Saving only in OnClosed loses the layout if the app crashes, which is exactly
        /// what the "doesn't save settings" report describes. Debounced so a drag-resize
        /// doesn't write the options file on every pixel.
        /// </summary>
        private void HookColumnLayoutPersistence()
        {
            _columnLayoutSaveTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(600)
            };
            _columnLayoutSaveTimer.Tick += (_, _) =>
            {
                _columnLayoutSaveTimer!.Stop();
                SaveColumnLayout();
            };

            // Reorder fires once per drag-drop.
            FileGrid.ColumnReordered += (_, _) => QueueColumnLayoutSave();

            // DataGridColumn does NOT implement INotifyPropertyChanged, so observe the
            // Width and DisplayIndex dependency properties via descriptors instead.
            var widthDesc = System.ComponentModel.DependencyPropertyDescriptor
                .FromProperty(DataGridColumn.WidthProperty, typeof(DataGridColumn));
            var indexDesc = System.ComponentModel.DependencyPropertyDescriptor
                .FromProperty(DataGridColumn.DisplayIndexProperty, typeof(DataGridColumn));
            EventHandler onChanged = (_, _) => QueueColumnLayoutSave();
            foreach (var col in FileGrid.Columns)
            {
                widthDesc?.AddValueChanged(col, onChanged);
                indexDesc?.AddValueChanged(col, onChanged);
            }
        }

        private void QueueColumnLayoutSave()
        {
            if (_columnLayoutSaveTimer == null) return;
            _columnLayoutSaveTimer.Stop();
            _columnLayoutSaveTimer.Start();
        }

        private void SaveColumnLayout()
        {
            try
            {
                var parts = new List<string>();
                foreach (var col in FileGrid.Columns)
                {
                    string header = col.Header?.ToString() ?? "";
                    int displayIndex = col.DisplayIndex;
                    double width = col.ActualWidth;
                    parts.Add($"{header}:{displayIndex}:{width:F0}");
                }
                ThemeManager.ColumnLayout = string.Join("|", parts);
                ThemeManager.SavePlayOptions();
            }
            catch (Exception ex)
            {
                // Surface the failure instead of silently losing the layout.
                if (ThemeManager.CrashLoggingEnabled)
                    LocalCrashLogger.Write(ex);
            }
        }

        private void RestoreColumnLayout()
        {
            try
            {
                string layout = ThemeManager.ColumnLayout;
                if (string.IsNullOrEmpty(layout)) return;

                var entries = layout.Split('|', StringSplitOptions.RemoveEmptyEntries);
                var layoutMap = new Dictionary<string, (int DisplayIndex, double Width)>(StringComparer.OrdinalIgnoreCase);
                foreach (var entry in entries)
                {
                    var parts = entry.Split(':');
                    if (parts.Length >= 3 &&
                        int.TryParse(parts[1], out int di) &&
                        double.TryParse(parts[2], out double w))
                    {
                        layoutMap[parts[0]] = (di, w);
                    }
                }

                if (layoutMap.Count == 0) return;

                foreach (var col in FileGrid.Columns)
                {
                    string header = col.Header?.ToString() ?? "";
                    if (layoutMap.TryGetValue(header, out var info))
                    {
                        if (info.DisplayIndex >= 0 && info.DisplayIndex < FileGrid.Columns.Count)
                            col.DisplayIndex = info.DisplayIndex;
                        if (info.Width > 10)
                            col.Width = new DataGridLength(info.Width);
                    }
                }
            }
            catch (Exception ex)
            {
                if (ThemeManager.CrashLoggingEnabled)
                    LocalCrashLogger.Write(ex);
            }
        }

        public void ApplyColumnVisibility()
        {
            if (ThemeManager.SyncHiddenColumnsWithAnalysisOptions())
                ThemeManager.SavePlayOptions();

            var hidden = ThemeManager.GetHiddenColumnSet();

            foreach (var h in _sessionHiddenColumns)
                hidden.Add(ThemeManager.NormalizeColumnHeader(h));

            int visibleCount = FileGrid.Columns.Count(col =>
            {
                string header = ThemeManager.NormalizeColumnHeader(col.Header?.ToString() ?? "");
                return !string.IsNullOrWhiteSpace(header) && !hidden.Contains(header);
            });

            if (visibleCount < 4)
            {
                _sessionHiddenColumns.Clear();
                ThemeManager.HiddenColumns = "";
                ThemeManager.SyncHiddenColumnsWithAnalysisOptions();
                ThemeManager.SavePlayOptions();
                hidden = ThemeManager.GetHiddenColumnSet();
            }

            foreach (var col in FileGrid.Columns)
            {
                string header = ThemeManager.NormalizeColumnHeader(col.Header?.ToString() ?? "");
                bool isHidden = hidden.Contains(header);
                col.Visibility = isHidden ? Visibility.Collapsed : Visibility.Visible;
            }
        }

        private void HideColumnForSession(string header)
        {
            var normalized = ThemeManager.NormalizeColumnHeader(header);
            // Clear any user-shown preference, else the preference applier would re-show an
            // opt-in column (★ / Date Created) the user just asked to hide.
            ThemeManager.SetColumnUserShown(normalized, false);
            var hidden = ThemeManager.GetHiddenColumnSet();
            hidden.Add(normalized);
            ThemeManager.HiddenColumns = string.Join(",", hidden.OrderBy(h => h, StringComparer.OrdinalIgnoreCase));
            ThemeManager.SavePlayOptions();
            ApplyColumnVisibility();
        }

        private void ShowAllColumns()
        {
            _sessionHiddenColumns.Clear();
            ThemeManager.HiddenColumns = "";
            ThemeManager.ShowAllFlaglessDefaultColumns(); // reveal opt-in ★ / Date Created too
            ThemeManager.SyncHiddenColumnsWithAnalysisOptions();
            ThemeManager.SavePlayOptions();
            ApplyColumnVisibility();
        }

        private void HideColumn_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem mi && mi.Parent is ContextMenu cm &&
                cm.PlacementTarget is DataGridColumnHeader header)
            {
                string headerText = header.Content?.ToString() ?? "";
                if (!string.IsNullOrEmpty(headerText))
                    HideColumnForSession(headerText);
            }
        }

        private void ShowAllColumns_Click(object sender, RoutedEventArgs e)
        {
            ShowAllColumns();
        }

        private void ScrollFileGridHorizontally(ScrollViewer scrollViewer, double delta)
        {
            double verticalOffset = scrollViewer.VerticalOffset;
            _fileGridHorizontalScrollAnchorOffset = verticalOffset;
            scrollViewer.ScrollToHorizontalOffset(scrollViewer.HorizontalOffset + delta);
            _lastHorizontalScrollTime = DateTime.UtcNow;

            Dispatcher.BeginInvoke(new Action(() =>
            {
                if ((DateTime.UtcNow - _lastHorizontalScrollTime).TotalMilliseconds <= 350 &&
                    Math.Abs(scrollViewer.VerticalOffset - verticalOffset) > 0.01)
                {
                    scrollViewer.ScrollToVerticalOffset(verticalOffset);
                }
            }), DispatcherPriority.Background);
        }

        private void FileGrid_ScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            if (_fileGridRestoringHorizontalScrollAnchor)
                return;

            // Drive the anchor logic off the scroll deltas directly rather than off the
            // Shift+wheel timestamp, so dragging the *horizontal scrollbar* is covered too.
            // (The old time-gate only fired within 450ms of a Shift+scroll wheel gesture, so
            // scrollbar-drag horizontal scrolling re-introduced the vertical-jitter bug.)
            if (Math.Abs(e.HorizontalChange) > 0.01)
            {
                // A horizontal scroll happened from *any* source. Virtualization sometimes
                // nudges the vertical offset along with it — snap that drift back to the anchor.
                var scrollViewer = e.OriginalSource as ScrollViewer ?? FindVisualChild<ScrollViewer>(FileGrid);
                if (scrollViewer == null)
                    return;

                if (Math.Abs(scrollViewer.VerticalOffset - _fileGridHorizontalScrollAnchorOffset) > 0.01)
                    RestoreFileGridVerticalOffsetDuringHorizontalGesture(scrollViewer);
            }
            else if (Math.Abs(e.VerticalChange) > 0.01)
            {
                // Genuine pure-vertical scroll — the user actually wants to move vertically,
                // so accept the new position as the anchor to hold during future h-scrolls.
                var scrollViewer = e.OriginalSource as ScrollViewer ?? FindVisualChild<ScrollViewer>(FileGrid);
                if (scrollViewer != null)
                    _fileGridHorizontalScrollAnchorOffset = scrollViewer.VerticalOffset;
            }
        }

        private void RestoreFileGridVerticalOffsetDuringHorizontalGesture(ScrollViewer scrollViewer)
        {
            if (Math.Abs(scrollViewer.VerticalOffset - _fileGridHorizontalScrollAnchorOffset) <= 0.01)
                return;

            _fileGridRestoringHorizontalScrollAnchor = true;
            scrollViewer.ScrollToVerticalOffset(_fileGridHorizontalScrollAnchorOffset);
            Dispatcher.BeginInvoke(new Action(() =>
            {
                _fileGridRestoringHorizontalScrollAnchor = false;
            }), DispatcherPriority.Background);
        }
    }
}
