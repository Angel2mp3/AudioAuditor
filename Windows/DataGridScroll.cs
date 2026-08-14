using System;
using System.Collections.Generic;
using System.Globalization;
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

        // DependencyPropertyDescriptor.AddValueChanged registers in a process-wide static table and
        // is never released automatically — it roots every column and, through the handler closure,
        // this window. Kept so UnhookColumnLayoutPersistence can undo it on close.
        private EventHandler? _columnLayoutChangedHandler;

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
            _columnLayoutChangedHandler = (_, _) => QueueColumnLayoutSave();
            foreach (var col in FileGrid.Columns)
            {
                ColumnWidthDescriptor?.AddValueChanged(col, _columnLayoutChangedHandler);
                ColumnIndexDescriptor?.AddValueChanged(col, _columnLayoutChangedHandler);
            }
        }

        private static System.ComponentModel.DependencyPropertyDescriptor? ColumnWidthDescriptor =>
            System.ComponentModel.DependencyPropertyDescriptor
                .FromProperty(DataGridColumn.WidthProperty, typeof(DataGridColumn));

        private static System.ComponentModel.DependencyPropertyDescriptor? ColumnIndexDescriptor =>
            System.ComponentModel.DependencyPropertyDescriptor
                .FromProperty(DataGridColumn.DisplayIndexProperty, typeof(DataGridColumn));

        /// <summary>
        /// Releases the descriptor subscriptions taken in <see cref="HookColumnLayoutPersistence"/>.
        /// Without this the static descriptor table keeps the columns — and this window — alive for
        /// the life of the process.
        /// </summary>
        private void UnhookColumnLayoutPersistence()
        {
            if (_columnLayoutChangedHandler == null) return;
            foreach (var col in FileGrid.Columns)
            {
                ColumnWidthDescriptor?.RemoveValueChanged(col, _columnLayoutChangedHandler);
                ColumnIndexDescriptor?.RemoveValueChanged(col, _columnLayoutChangedHandler);
            }
            _columnLayoutChangedHandler = null;
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
                ThemeManager.ColumnLayout = FormatColumnLayout(
                    FileGrid.Columns.Select(col =>
                        (col.Header?.ToString() ?? "", col.DisplayIndex, col.ActualWidth)));
                ThemeManager.SavePlayOptions();
            }
            catch (Exception ex)
            {
                // Surface the failure instead of silently losing the layout.
                if (ThemeManager.CrashLoggingEnabled)
                    LocalCrashLogger.Write(ex);
            }
        }

        /// <summary>
        /// Parses the persisted ColumnLayout string into header → (display index, width).
        /// Split out from <see cref="RestoreColumnLayout"/> so the format — which round-trips
        /// through options.txt and has already been broken once by a field-shift bug — is
        /// covered by unit tests without needing a live DataGrid.
        /// </summary>
        internal static Dictionary<string, (int DisplayIndex, double Width)> ParseColumnLayout(string? layout)
        {
            var layoutMap = new Dictionary<string, (int DisplayIndex, double Width)>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrEmpty(layout)) return layoutMap;

            foreach (var entry in layout.Split('|', StringSplitOptions.RemoveEmptyEntries))
            {
                // Split from the RIGHT: the last two fields are displayIndex and width, so a
                // header containing ':' stays intact. Splitting left-to-right on fixed indices
                // shifted the fields and silently dropped that column's saved layout — and the
                // user's chosen widths/order must never be lost to a parse quirk.
                int widthSep = entry.LastIndexOf(':');
                if (widthSep <= 0) continue;
                int indexSep = entry.LastIndexOf(':', widthSep - 1);
                if (indexSep <= 0) continue;

                string header = entry[..indexSep];
                if (int.TryParse(entry[(indexSep + 1)..widthSep], NumberStyles.Integer, CultureInfo.InvariantCulture, out int di) &&
                    double.TryParse(entry[(widthSep + 1)..], NumberStyles.Float, CultureInfo.InvariantCulture, out double w))
                {
                    layoutMap[header] = (di, w);
                }
            }
            return layoutMap;
        }

        /// <summary>Builds the persisted ColumnLayout string. Inverse of <see cref="ParseColumnLayout"/>.</summary>
        internal static string FormatColumnLayout(IEnumerable<(string Header, int DisplayIndex, double Width)> columns)
        {
            var parts = new List<string>();
            foreach (var (header, displayIndex, width) in columns)
            {
                // Invariant on BOTH sides — this string lands in options.txt, where every other
                // numeric value is invariant. F0 emits no separator today, so the asymmetry was
                // latent rather than broken; it stops being latent the moment the format changes.
                parts.Add(string.Format(CultureInfo.InvariantCulture, "{0}:{1}:{2:F0}", header, displayIndex, width));
            }
            return string.Join("|", parts);
        }

        private void RestoreColumnLayout()
        {
            try
            {
                var layoutMap = ParseColumnLayout(ThemeManager.ColumnLayout);
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

            // Too few columns left to use the grid: drop the session-only hides and fall back to
            // the persisted set. GetHiddenColumnSet() already applies the same "unusable → back to
            // defaults" rule on its own copy, so the fallback happens without touching
            // ThemeManager.HiddenColumns — a transient bad state must never be written to disk.
            if (visibleCount < 4)
            {
                _sessionHiddenColumns.Clear();
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
