using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using AudioQualityChecker.Models;
using AudioQualityChecker.Services;

namespace AudioQualityChecker
{
    /// <summary>
    /// "Write Analysis to Files" dialog: pick which analyzed metrics to persist into the selected
    /// audio files. Field checkboxes are built from <see cref="AnalysisTagWriteService.Fields"/> and
    /// writing is delegated to that service (custom tags + an optional Comment summary line).
    /// </summary>
    public partial class WriteAnalysisWindow : Window
    {
        private readonly List<AudioFileInfo> _files;
        private readonly AnalysisTagWriteService _service = new();
        private readonly Dictionary<string, CheckBox> _fieldBoxes = new(StringComparer.OrdinalIgnoreCase);
        private CancellationTokenSource? _cts;

        public bool FilesChanged { get; private set; }

        public WriteAnalysisWindow(IEnumerable<AudioFileInfo> files, Window owner)
        {
            InitializeComponent();
            Owner = owner;
            _files = files.ToList();
            FileCountText.Text = $"{_files.Count} file{(_files.Count == 1 ? "" : "s")} selected";
            BuildFieldPicker();
        }

        private static readonly (AnalysisFieldCategory Category, string Title)[] CategoryOrder =
        {
            (AnalysisFieldCategory.CoreQuality, "Core quality"),
            (AnalysisFieldCategory.Detections, "Detections"),
            (AnalysisFieldCategory.Musical, "Musical"),
            (AnalysisFieldCategory.Other, "Other"),
        };

        private void BuildFieldPicker()
        {
            foreach (var (category, title) in CategoryOrder)
            {
                var fields = AnalysisTagWriteService.Fields.Where(f => f.Category == category).ToList();
                if (fields.Count == 0) continue;

                // Section header with a select-all toggle for the category.
                var selectAll = new CheckBox
                {
                    Content = title,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = (Brush)FindResource("TextPrimary"),
                    Margin = new Thickness(0, 8, 0, 6)
                };
                CategoryHost.Children.Add(selectAll);

                var wrap = new WrapPanel { Margin = new Thickness(22, 0, 0, 4) };
                var boxes = new List<CheckBox>();
                foreach (var def in fields)
                {
                    // Default-check a field only if at least one selected file actually has a value.
                    bool anyValue = _files.Any(f => !string.IsNullOrWhiteSpace(def.Extract(f)));
                    var box = new CheckBox
                    {
                        Content = def.Label,
                        IsChecked = anyValue,
                        IsEnabled = anyValue,
                        Margin = new Thickness(0, 0, 18, 6),
                        ToolTip = anyValue ? null : "No measured value on the selected files."
                    };
                    _fieldBoxes[def.Key] = box;
                    boxes.Add(box);
                    box.Checked += (_, _) => SyncSelectAll(selectAll, boxes);
                    box.Unchecked += (_, _) => SyncSelectAll(selectAll, boxes);
                    wrap.Children.Add(box);
                }

                selectAll.Click += (_, _) =>
                {
                    bool on = selectAll.IsChecked == true;
                    foreach (var b in boxes.Where(b => b.IsEnabled))
                        b.IsChecked = on;
                };
                SyncSelectAll(selectAll, boxes);
                CategoryHost.Children.Add(wrap);
            }
        }

        // Reflect the children's collective state on the section header (checked / unchecked /
        // indeterminate) without recursing back into the children.
        private static void SyncSelectAll(CheckBox selectAll, List<CheckBox> boxes)
        {
            var enabled = boxes.Where(b => b.IsEnabled).ToList();
            int on = enabled.Count(b => b.IsChecked == true);
            selectAll.IsChecked = on == 0 ? false : on == enabled.Count ? true : (bool?)null;
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

        private async void Apply_Click(object sender, RoutedEventArgs e)
        {
            var options = new AnalysisTagWriteOptions
            {
                WriteCustomTags = true,
                WriteCommentSummary = ChkComment.IsChecked == true,
                CreateBackups = ChkBackups.IsChecked == true
            };
            foreach (var (key, box) in _fieldBoxes)
                if (box.IsChecked == true) options.Fields.Add(key);

            if (options.Fields.Count == 0)
            {
                StatusText.Text = "Pick at least one field to write.";
                return;
            }

            if (!ErrorDialog.Confirm("Write Analysis to Files",
                $"Write {options.Fields.Count} field(s) into {_files.Count} file(s)?",
                this)) return;

            _cts = new CancellationTokenSource();
            SetBusy(true, "Writing…");
            try
            {
                var progress = new Progress<(int done, int total, string fileName)>(p =>
                    StatusText.Text = $"Writing {p.done}/{p.total}…");
                var summary = await _service.ApplyAsync(_files, options, progress, _cts.Token);

                FilesChanged = FilesChanged || summary.FilesChanged > 0;
                StatusText.Text =
                    $"Wrote {summary.FilesChanged} file{(summary.FilesChanged == 1 ? "" : "s")}."
                    + (summary.FilesSkipped > 0 ? $" {summary.FilesSkipped} had nothing to write." : "")
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

        private void SetBusy(bool busy, string? status = null)
        {
            if (status != null) StatusText.Text = status;
            Cursor = busy ? Cursors.Wait : Cursors.Arrow;
            BtnApply.IsEnabled = !busy;
        }
    }
}
