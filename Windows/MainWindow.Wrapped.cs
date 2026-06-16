using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using AudioQualityChecker.Services;

namespace AudioQualityChecker
{
    // "Wrapped" stats dashboard, hosted as an in-app overlay inside the main window (WrappedOverlay
    // in MainWindow.xaml) — not a separate window, so it always fits the current app window size and
    // the normal min/max/resize controls keep working.
    public partial class MainWindow
    {
        private WrappedSummary? _wrappedSummary;

        private enum WrappedRange { Day, Week, Month, Year, All, Custom }
        private WrappedRange _wrappedRange = WrappedRange.All;
        private DateTime? _wrappedCustomFrom;
        private DateTime? _wrappedCustomTo;

        // Lossless formats for the lossless/lossy ratio.
        private static readonly HashSet<string> LosslessFormats = new(StringComparer.OrdinalIgnoreCase)
        {
            "FLAC", "ALAC", "WAV", "AIFF", "AIF", "APE", "WV", "TAK", "DSF", "DFF"
        };

        // ─── Show / hide ───

        public void ShowWrappedOverlay()
        {
            _wrappedRange = WrappedRange.All;
            WrappedCustomPopup.IsOpen = false;
            RefreshWrappedForRange();
            WrappedResetConfirmOverlay.Visibility = Visibility.Collapsed;
            WrappedOverlay.Visibility = Visibility.Visible;
            WrappedOverlay.Focus();

            if (ThemeManager.AnimationsEnabled)
            {
                WrappedOverlay.Opacity = 0;
                WrappedOverlay.BeginAnimation(OpacityProperty,
                    new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(280)));
                AnimateWrappedColumnsIn();
            }
            else
            {
                WrappedOverlay.Opacity = 1;
            }
        }

        private void HideWrappedOverlay()
        {
            WrappedResetConfirmOverlay.Visibility = Visibility.Collapsed;
            WrappedOverlay.Visibility = Visibility.Collapsed;
        }

        private void WrappedClose_Click(object sender, RoutedEventArgs e) => HideWrappedOverlay();

        private void WrappedOverlay_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                if (WrappedCustomPopup.IsOpen)
                {
                    WrappedCustomPopup.IsOpen = false;
                    WrappedSyncRangeToggles();
                }
                else if (WrappedResetConfirmOverlay.Visibility == Visibility.Visible)
                    WrappedResetConfirmOverlay.Visibility = Visibility.Collapsed;
                else
                    HideWrappedOverlay();
                e.Handled = true;
            }
        }

        private void AnimateWrappedColumnsIn()
        {
            var cols = new[] { WrappedCol0, WrappedCol1, WrappedCol2 };
            for (int i = 0; i < cols.Length; i++)
            {
                var col = cols[i];
                if (col == null) continue;
                col.Opacity = 0;
                var tt = new TranslateTransform(0, 18);
                col.RenderTransform = tt;
                var begin = TimeSpan.FromMilliseconds(90 * i);
                col.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(420))
                {
                    BeginTime = begin,
                    EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
                });
                tt.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(18, 0, TimeSpan.FromMilliseconds(420))
                {
                    BeginTime = begin,
                    EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
                });
            }
        }

        // ─── Reset stats (themed in-overlay confirmation) ───

        private void WrappedResetStats_Click(object sender, RoutedEventArgs e)
        {
            WrappedResetConfirmOverlay.Visibility = Visibility.Visible;
            if (ThemeManager.AnimationsEnabled)
            {
                WrappedResetConfirmOverlay.Opacity = 0;
                WrappedResetConfirmOverlay.BeginAnimation(OpacityProperty,
                    new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(160)));
            }
        }

        private void WrappedResetConfirmCancel_Click(object sender, RoutedEventArgs e)
            => WrappedResetConfirmOverlay.Visibility = Visibility.Collapsed;

        // Dismiss the confirmation when the dimmed backdrop (but not the dialog card) is clicked.
        private void WrappedResetConfirmDim_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (ReferenceEquals(e.OriginalSource, sender))
                WrappedResetConfirmOverlay.Visibility = Visibility.Collapsed;
        }

        private void WrappedResetConfirmYes_Click(object sender, RoutedEventArgs e)
        {
            WrappedResetConfirmOverlay.Visibility = Visibility.Collapsed;
            LocalStatsCollector.Reset();
            RefreshWrappedForRange();
        }

        // ─── Time-range selection ───

        private void WrappedRange_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not ToggleButton tb) return;

            if ((tb.Tag as string) == "Custom")
            {
                // Don't switch the active range yet — open the calendar and apply on confirm.
                WrappedSyncRangeToggles();      // keep the current range visually selected
                WrappedRangeCustom.IsChecked = true;
                OpenWrappedCustomPopup();
                return;
            }

            _wrappedRange = (tb.Tag as string) switch
            {
                "Day" => WrappedRange.Day,
                "Week" => WrappedRange.Week,
                "Month" => WrappedRange.Month,
                "Year" => WrappedRange.Year,
                _ => WrappedRange.All
            };
            RefreshWrappedForRange();
        }

        private void OpenWrappedCustomPopup()
        {
            var today = DateTime.Today;
            WrappedCustomCalendar.DisplayDate = today;
            if (_wrappedCustomFrom is { } f && _wrappedCustomTo is { } t)
            {
                WrappedCustomCalendar.SelectedDates.Clear();
                WrappedCustomCalendar.SelectedDates.AddRange(f.Date, t.Date);
                WrappedCustomCalendar.DisplayDate = f.Date;
            }
            else
            {
                WrappedCustomCalendar.SelectedDates.Clear();
            }
            WrappedUpdateCustomRangeText();
            WrappedCustomPopup.IsOpen = true;
        }

        private void WrappedCustomCalendar_SelectedDatesChanged(object? sender, SelectionChangedEventArgs e)
            => WrappedUpdateCustomRangeText();

        private void WrappedUpdateCustomRangeText()
        {
            var dates = WrappedCustomCalendar.SelectedDates;
            if (dates.Count == 0)
            {
                WrappedCustomRangeText.Text = "No range selected";
                if (WrappedCustomApplyBtn != null) WrappedCustomApplyBtn.IsEnabled = false;
                return;
            }
            var from = dates.Min().Date;
            var to = dates.Max().Date;
            WrappedCustomRangeText.Text = from == to
                ? from.ToString("MMM d, yyyy")
                : $"{from:MMM d, yyyy} – {to:MMM d, yyyy}";
            if (WrappedCustomApplyBtn != null) WrappedCustomApplyBtn.IsEnabled = true;
        }

        private void WrappedCustomApply_Click(object sender, RoutedEventArgs e)
        {
            var dates = WrappedCustomCalendar.SelectedDates;
            if (dates.Count == 0) return;
            _wrappedCustomFrom = dates.Min().Date;
            _wrappedCustomTo = dates.Max().Date;
            _wrappedRange = WrappedRange.Custom;
            WrappedCustomPopup.IsOpen = false;
            RefreshWrappedForRange();
        }

        private void WrappedCustomCancel_Click(object sender, RoutedEventArgs e)
        {
            WrappedCustomPopup.IsOpen = false;
            WrappedSyncRangeToggles(); // revert the Custom toggle if it isn't the active range
        }

        /// <summary>Resolves the current range to a half-open [from, to) window.</summary>
        private (DateTime from, DateTime to) WrappedResolveRange()
        {
            var today = DateTime.Today;
            switch (_wrappedRange)
            {
                case WrappedRange.Day:
                    return (today, today.AddDays(1));
                case WrappedRange.Week:
                    var firstDow = CultureInfo.CurrentCulture.DateTimeFormat.FirstDayOfWeek;
                    int diff = ((int)today.DayOfWeek - (int)firstDow + 7) % 7;
                    var weekStart = today.AddDays(-diff);
                    return (weekStart, weekStart.AddDays(7));
                case WrappedRange.Month:
                    var monthStart = new DateTime(today.Year, today.Month, 1);
                    return (monthStart, monthStart.AddMonths(1));
                case WrappedRange.Year:
                    var yearStart = new DateTime(today.Year, 1, 1);
                    return (yearStart, yearStart.AddYears(1));
                case WrappedRange.Custom:
                    var cf = (_wrappedCustomFrom ?? today).Date;
                    var ct = (_wrappedCustomTo ?? today).Date;
                    return (cf, ct.AddDays(1));
                default:
                    return (DateTime.MinValue, DateTime.MaxValue);
            }
        }

        private void RefreshWrappedForRange()
        {
            DateTime chartFrom, chartTo;
            if (_wrappedRange == WrappedRange.All)
            {
                _wrappedSummary = WrappedSummary.From(LocalStatsCollector.GetSnapshot());
                FillDashboard(_wrappedSummary); // sets its own "Since …" subtitle
                // The charts only have the timestamped event era to draw from; frame All Time from
                // the earliest recorded event to now.
                chartTo = DateTime.Now;
                chartFrom = LocalStatsCollector.EarliestEventTime() ?? chartTo.AddDays(-1);
            }
            else
            {
                (chartFrom, chartTo) = WrappedResolveRange();
                _wrappedSummary = WrappedSummary.From(LocalStatsCollector.GetSnapshotForRange(chartFrom, chartTo));
                FillDashboard(_wrappedSummary);
                WrappedSubtitleText.Text = WrappedBuildRangeSubtitle(chartFrom, chartTo, _wrappedSummary.HasAnyData);
            }
            RenderWrappedCharts(chartFrom, chartTo);
            WrappedSyncRangeToggles();
        }

        private string WrappedBuildRangeSubtitle(DateTime from, DateTime to, bool hasData)
        {
            var lastDay = to.AddDays(-1);
            string label = _wrappedRange switch
            {
                WrappedRange.Day => $"Today · {from:MMM d, yyyy}",
                WrappedRange.Week => $"This week · {from:MMM d} – {lastDay:MMM d, yyyy}",
                WrappedRange.Month => from.ToString("MMMM yyyy"),
                WrappedRange.Year => from.ToString("yyyy"),
                WrappedRange.Custom => from.Date == lastDay.Date
                    ? from.ToString("MMM d, yyyy")
                    : $"{from:MMM d, yyyy} – {lastDay:MMM d, yyyy}",
                _ => ""
            };
            return hasData ? label : $"{label} · no activity recorded in this range yet";
        }

        private void WrappedSyncRangeToggles()
        {
            WrappedRangeDay.IsChecked = _wrappedRange == WrappedRange.Day;
            WrappedRangeWeek.IsChecked = _wrappedRange == WrappedRange.Week;
            WrappedRangeMonth.IsChecked = _wrappedRange == WrappedRange.Month;
            WrappedRangeYear.IsChecked = _wrappedRange == WrappedRange.Year;
            WrappedRangeAll.IsChecked = _wrappedRange == WrappedRange.All;
            WrappedRangeCustom.IsChecked = _wrappedRange == WrappedRange.Custom;
        }

        // ─── Charts (hand-drawn, range-aware) ───

        private List<(DateTime bucketStart, int plays, double minutes)>? _wrappedActivity;
        private List<(DateTime bucketStart, double avgBitrate, double avgDr, int n)>? _wrappedQuality;
        private DateTime _wrappedChartFrom, _wrappedChartTo;
        private bool _wrappedChartHandlersHooked;

        private void RenderWrappedCharts(DateTime from, DateTime to)
        {
            _wrappedChartFrom = from;
            _wrappedChartTo = to;
            _wrappedActivity = LocalStatsCollector.GetActivitySeries(from, to);
            _wrappedQuality = LocalStatsCollector.GetQualityTrend(from, to);

            // Charts can't draw until their host has a real size; redraw whenever it changes (first
            // layout, window resize). Hook once so refreshes don't stack handlers.
            if (!_wrappedChartHandlersHooked)
            {
                _wrappedChartHandlersHooked = true;
                WActivityChartHost.SizeChanged += (_, _) => DrawActivityChart();
                WFormatDonutHost.SizeChanged += (_, _) => DrawFormatDonut();
                WQualityTrendHost.SizeChanged += (_, _) => DrawQualityTrend();
            }
            DrawActivityChart();
            DrawFormatDonut();
            DrawQualityTrend();
        }

        private Color ChartColor(string key, Color fallback) =>
            FindResource(key) is SolidColorBrush b ? b.Color : fallback;

        private void ChartPlaceholder(Grid host, string text)
        {
            host.Children.Clear();
            host.Children.Add(new TextBlock
            {
                Text = text,
                Foreground = new SolidColorBrush(ChartColor("TextMuted", Color.FromRgb(150, 150, 150))),
                FontSize = 11,
                TextWrapping = TextWrapping.Wrap,
                TextAlignment = TextAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            });
        }

        private static void AddCanvasText(Canvas c, string text, double left, double top, double size,
            Color color, double? width = null, TextAlignment align = TextAlignment.Left, FontWeight? weight = null)
        {
            var tb = new TextBlock { Text = text, FontSize = size, Foreground = new SolidColorBrush(color) };
            if (weight.HasValue) tb.FontWeight = weight.Value;
            if (width.HasValue) { tb.Width = width.Value; tb.TextAlignment = align; }
            Canvas.SetLeft(tb, left);
            Canvas.SetTop(tb, top);
            c.Children.Add(tb);
        }

        private string WrappedBucketLabelFormat()
        {
            var span = _wrappedChartTo - _wrappedChartFrom;
            if (span <= TimeSpan.FromDays(2)) return "h tt";
            if (span <= TimeSpan.FromDays(92)) return "MMM d";
            return "MMM yyyy";
        }

        private void DrawActivityChart()
        {
            var host = WActivityChartHost;
            double w = host.ActualWidth, h = host.ActualHeight;
            if (w <= 4 || h <= 4) return;
            var data = _wrappedActivity;
            host.Children.Clear();
            if (data == null || data.Count == 0 || data.All(d => d.plays == 0))
            {
                ChartPlaceholder(host, "No activity in this range yet.");
                return;
            }

            var canvas = new Canvas { Width = w, Height = h, ClipToBounds = true };
            Color accent = ChartColor("AccentColor", Color.FromRgb(100, 149, 237));
            Color muted = ChartColor("TextMuted", Color.FromRgb(150, 150, 150));

            double padTop = 16, padBottom = 18, padX = 2;
            double plotW = w - padX * 2, plotH = h - padTop - padBottom;
            int n = data.Count;
            int maxPlays = Math.Max(1, data.Max(d => d.plays));

            double X(int i) => padX + (n == 1 ? plotW / 2 : plotW * i / (n - 1));
            double Y(int plays) => padTop + plotH * (1 - (double)plays / maxPlays);

            canvas.Children.Add(new Line
            {
                X1 = padX, X2 = w - padX, Y1 = padTop + plotH, Y2 = padTop + plotH,
                Stroke = new SolidColorBrush(Color.FromArgb(40, 255, 255, 255)), StrokeThickness = 1
            });

            // Filled area under the curve.
            var fig = new PathFigure { StartPoint = new Point(X(0), padTop + plotH) };
            for (int i = 0; i < n; i++) fig.Segments.Add(new LineSegment(new Point(X(i), Y(data[i].plays)), true));
            fig.Segments.Add(new LineSegment(new Point(X(n - 1), padTop + plotH), true));
            fig.IsClosed = true;
            var areaGeo = new PathGeometry();
            areaGeo.Figures.Add(fig);
            canvas.Children.Add(new Path
            {
                Data = areaGeo,
                Fill = new SolidColorBrush(Color.FromArgb(46, accent.R, accent.G, accent.B))
            });

            var poly = new Polyline
            {
                Stroke = new SolidColorBrush(accent), StrokeThickness = 2, StrokeLineJoin = PenLineJoin.Round
            };
            for (int i = 0; i < n; i++) poly.Points.Add(new Point(X(i), Y(data[i].plays)));
            canvas.Children.Add(poly);

            string fmt = WrappedBucketLabelFormat();
            AddCanvasText(canvas, $"peak {maxPlays} plays", padX, 0, 10, muted);
            AddCanvasText(canvas, data[0].bucketStart.ToString(fmt), padX, h - 14, 10, muted);
            AddCanvasText(canvas, data[n - 1].bucketStart.ToString(fmt), padX, h - 14, 10, muted,
                width: plotW, align: TextAlignment.Right);

            host.Children.Add(canvas);
        }

        private static byte ClampByte(double v) => (byte)Math.Clamp(v, 0, 255);

        private static Color AccentShade(Color a, int i)
        {
            double[] f = { 1.0, 0.78, 1.22, 0.62, 1.4 };
            double m = f[i % f.Length];
            return Color.FromRgb(ClampByte(a.R * m), ClampByte(a.G * m), ClampByte(a.B * m));
        }

        private static Color GrayShade(int i)
        {
            byte[] g = { 150, 120, 98, 176, 86 };
            byte v = g[i % g.Length];
            return Color.FromRgb(v, v, v);
        }

        private static Path MakeDonutSlice(double cx, double cy, double rO, double rI,
            double startDeg, double sweepDeg, Brush fill)
        {
            if (sweepDeg >= 359.999) sweepDeg = 359.999; // a full ring can't be a single arc
            double a0 = startDeg * Math.PI / 180.0, a1 = (startDeg + sweepDeg) * Math.PI / 180.0;
            Point pO0 = new(cx + rO * Math.Cos(a0), cy + rO * Math.Sin(a0));
            Point pO1 = new(cx + rO * Math.Cos(a1), cy + rO * Math.Sin(a1));
            Point pI1 = new(cx + rI * Math.Cos(a1), cy + rI * Math.Sin(a1));
            Point pI0 = new(cx + rI * Math.Cos(a0), cy + rI * Math.Sin(a0));
            bool large = sweepDeg > 180;
            var fig = new PathFigure { StartPoint = pO0 };
            fig.Segments.Add(new ArcSegment(pO1, new Size(rO, rO), 0, large, SweepDirection.Clockwise, true));
            fig.Segments.Add(new LineSegment(pI1, true));
            fig.Segments.Add(new ArcSegment(pI0, new Size(rI, rI), 0, large, SweepDirection.Counterclockwise, true));
            fig.IsClosed = true;
            var geo = new PathGeometry();
            geo.Figures.Add(fig);
            return new Path { Data = geo, Fill = fill };
        }

        private void DrawFormatDonut()
        {
            var host = WFormatDonutHost;
            double w = host.ActualWidth, h = host.ActualHeight;
            if (w <= 4 || h <= 4) return;
            host.Children.Clear();
            var summary = _wrappedSummary;
            var formats = summary?.TopFormats;
            int total = formats?.Sum(f => f.Count) ?? 0;
            if (summary == null || formats == null || formats.Count == 0 || total == 0)
            {
                ChartPlaceholder(host, "No format data in this range yet.");
                return;
            }

            var canvas = new Canvas { Width = w, Height = h };
            Color accent = ChartColor("AccentColor", Color.FromRgb(100, 149, 237));
            Color textPrimary = ChartColor("TextPrimary", Colors.White);
            Color muted = ChartColor("TextMuted", Color.FromRgb(150, 150, 150));

            double donut = Math.Min(h, w * 0.52);
            double cx = donut / 2, cy = h / 2;
            double rO = donut / 2 - 4, rI = rO * 0.58;

            double angle = -90; // 12 o'clock
            int losslessIdx = 0, lossyIdx = 0;
            var swatches = new List<(Color c, string label, int count, double frac)>();
            foreach (var (label, count) in formats)
            {
                double frac = (double)count / total;
                bool lossless = LosslessFormats.Contains(label);
                Color c = lossless ? AccentShade(accent, losslessIdx++) : GrayShade(lossyIdx++);
                canvas.Children.Add(MakeDonutSlice(cx, cy, rO, rI, angle, frac * 360, new SolidColorBrush(c)));
                angle += frac * 360;
                swatches.Add((c, label, count, frac));
            }

            // Center: lossless share.
            AddCanvasText(canvas, summary.HasLosslessData ? $"{summary.LosslessPct:F0}%" : "—",
                cx - rI, cy - 14, 17, textPrimary, width: rI * 2, align: TextAlignment.Center, weight: FontWeights.Bold);
            AddCanvasText(canvas, "lossless", cx - rI, cy + 7, 10, muted, width: rI * 2, align: TextAlignment.Center);

            // Legend on the right.
            var legend = new StackPanel();
            Canvas.SetLeft(legend, donut + 14);
            Canvas.SetTop(legend, Math.Max(0, (h - swatches.Count * 19) / 2));
            foreach (var (c, label, count, frac) in swatches)
            {
                var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 1.5, 0, 1.5) };
                row.Children.Add(new Border
                {
                    Width = 10, Height = 10, CornerRadius = new CornerRadius(2),
                    Background = new SolidColorBrush(c), VerticalAlignment = VerticalAlignment.Center
                });
                row.Children.Add(new TextBlock
                {
                    Text = label, FontSize = 11, Margin = new Thickness(6, 0, 0, 0),
                    Foreground = new SolidColorBrush(textPrimary), VerticalAlignment = VerticalAlignment.Center
                });
                row.Children.Add(new TextBlock
                {
                    Text = $"{frac * 100:F0}%", FontSize = 11, Margin = new Thickness(6, 0, 0, 0),
                    Foreground = new SolidColorBrush(muted), VerticalAlignment = VerticalAlignment.Center
                });
                legend.Children.Add(row);
            }
            canvas.Children.Add(legend);

            host.Children.Add(canvas);
        }

        private void DrawQualityTrend()
        {
            var host = WQualityTrendHost;
            double w = host.ActualWidth, h = host.ActualHeight;
            if (w <= 4 || h <= 4) return;
            host.Children.Clear();
            var data = _wrappedQuality;
            int pts = data?.Count(d => d.n > 0) ?? 0;
            if (data == null || pts < 2)
            {
                ChartPlaceholder(host, "Not enough analyzed files in this range yet.");
                return;
            }

            var canvas = new Canvas { Width = w, Height = h, ClipToBounds = true };
            Color accent = ChartColor("AccentColor", Color.FromRgb(100, 149, 237));
            Color second = ChartColor("TextSecondary", Color.FromRgb(170, 180, 200));
            double half = h / 2;
            DrawSparkline(canvas, data.Select(d => (d.bucketStart, d.avgBitrate, d.n)).ToList(),
                0, half, w, "Avg bitrate", "kbps", accent);
            DrawSparkline(canvas, data.Select(d => (d.bucketStart, d.avgDr, d.n)).ToList(),
                half, half, w, "Avg DR", "", second);
            host.Children.Add(canvas);
        }

        private void DrawSparkline(Canvas canvas, List<(DateTime t, double value, int n)> series,
            double top, double height, double w, string title, string unit, Color color)
        {
            Color muted = ChartColor("TextMuted", Color.FromRgb(150, 150, 150));
            double padTop = 16, padBottom = 6, padX = 2;
            double plotH = height - padTop - padBottom;
            int n = series.Count;

            var valid = new List<(int i, double v)>();
            for (int i = 0; i < n; i++) if (series[i].n > 0 && series[i].value > 0) valid.Add((i, series[i].value));
            if (valid.Count == 0) return;

            double minV = valid.Min(p => p.v), maxV = valid.Max(p => p.v);
            if (maxV - minV < 1e-6) { maxV += 1; minV -= 1; }

            double X(int i) => padX + (n == 1 ? (w - padX * 2) / 2 : (w - padX * 2) * i / (n - 1));
            double Y(double v) => top + padTop + plotH * (1 - (v - minV) / (maxV - minV));

            var poly = new Polyline { Stroke = new SolidColorBrush(color), StrokeThickness = 2, StrokeLineJoin = PenLineJoin.Round };
            foreach (var (i, v) in valid) poly.Points.Add(new Point(X(i), Y(v)));
            canvas.Children.Add(poly);

            double latest = valid[^1].v;
            string latestText = unit.Length > 0 ? $"{latest:F0} {unit}" : $"{latest:F1}";
            AddCanvasText(canvas, title, padX, top, 10, muted);
            AddCanvasText(canvas, latestText, padX, top, 10, color, width: w - padX * 2, align: TextAlignment.Right, weight: FontWeights.SemiBold);
        }

        // ─── Dashboard population ───

        private void FillDashboard(WrappedSummary w)
        {
            WrappedSubtitleText.Text = $"Since {w.FirstActivity:MMM yyyy} · {w.DaysActive:N0} days";

            WStatFilesText.Text = $"{w.FilesScanned:N0}";
            WStatHoursText.Text = $"{w.Hours:F1}";
            WStatSessionsText.Text = $"{w.Sessions:N0}";

            WStatLibrarySize.Text = w.LibraryBytes > 0 ? $"{w.LibraryGb:F1} GB" : "-";
            WStatTotalPlays.Text = $"{w.TotalPlays:N0}";
            WStatUniqueArtists.Text = $"{w.UniqueArtists:N0}";
            WStatUniqueTracks.Text = $"{w.UniqueTracks:N0}";

            WStatAvgBitrate.Text = w.AvgBitrate;
            WStatAvgLufs.Text = w.AvgLufs;
            WStatAvgDr.Text = w.AvgDr;
            WStatClipping.Text = w.ClippingCount > 0 ? $"{w.ClippingCount:N0}" : "0";
            WStatAudiophile.Text = w.AudiophileRating;

            WStatLosslessRatio.Text = w.HasLosslessData ? $"{w.LosslessPct:F0}% lossless" : "";
            WStatMostByTime.Text = w.MostByTimeLabel ?? "";

            WStatDrRating.Text = w.DrRating;
            WStatClippingPct.Text = w.HasClippingData ? $"{w.ClippingPct:F0}%" : "-";
            WStatLosslessLossy.Text = w.HasLosslessData
                ? $"{w.LosslessPlays:N0} lossless · {w.LossyPlays:N0} lossy"
                : "";
            WStatMqaCount.Text = w.MqaCount > 0 ? $"{w.MqaCount:N0} MQA" : "";

            WStatAvgFileSize.Text = w.AvgFileSize;
            WStatAvgPlaysPerTrack.Text = w.AvgPlaysPerTrack;

            FillBarPanel(WFormatsPanel, w.TopFormats, byMax: false);
            FillBarPanel(WArtistsPanel, w.TopArtists, byMax: true);
            FillBarPanel(WAlbumsPanel, w.TopAlbums, byMax: true);
            FillBarPanel(WTracksPanel, w.TopTracks, byMax: true);
            FillBarPanel(WSampleRatePanel, w.TopSampleRates, byMax: true);
            FillBarPanel(WBitDepthPanel, w.TopBitDepths, byMax: true);
            FillBarPanel(WChannelPanel, w.TopChannels, byMax: false);
        }

        /// <summary>Populates a dashboard list panel with animated bar rows.</summary>
        private void FillBarPanel(StackPanel panel, List<(string Label, int Count)> items, bool byMax)
        {
            panel.Children.Clear();
            if (items.Count == 0)
            {
                panel.Children.Add(new TextBlock
                {
                    Text = "No data yet",
                    Foreground = (Brush)FindResource("TextMuted"),
                    FontSize = 11
                });
                return;
            }

            // Formats scale by total (share of plays); ranked lists scale by the leader.
            int denom = byMax
                ? Math.Max(1, items[0].Count)
                : Math.Max(1, items.Sum(i => i.Count));

            foreach (var (label, count) in items)
            {
                double pct = (double)count / denom * 100.0;
                panel.Children.Add(CreateBarRow(label, count, pct));
            }
        }

        private Grid CreateBarRow(string label, int count, double percent)
        {
            var grid = new Grid { Margin = new Thickness(0, 3, 0, 3) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var barGrid = new Grid();
            var bgBar = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(30, 128, 128, 128)),
                CornerRadius = new CornerRadius(3),
                Height = 22,
                HorizontalAlignment = HorizontalAlignment.Stretch
            };

            Color accent = (FindResource("AccentColor") as SolidColorBrush)?.Color ?? Color.FromRgb(100, 149, 237);
            var fillBar = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(150, accent.R, accent.G, accent.B)),
                CornerRadius = new CornerRadius(3),
                Height = 22,
                HorizontalAlignment = HorizontalAlignment.Left,
                Width = 0
            };

            barGrid.Loaded += (_, _) =>
            {
                double target = Math.Max(2, barGrid.ActualWidth * percent / 100.0);
                if (ThemeManager.AnimationsEnabled)
                {
                    fillBar.BeginAnimation(WidthProperty,
                        new DoubleAnimation(0, target, TimeSpan.FromMilliseconds(550))
                        {
                            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
                        });
                }
                else
                {
                    fillBar.Width = target;
                }
            };
            barGrid.Children.Add(bgBar);
            barGrid.Children.Add(fillBar);

            var labelTb = new TextBlock
            {
                Text = label,
                FontSize = 11,
                Foreground = (Brush)FindResource("TextPrimary"),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(8, 0, 0, 0),
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            barGrid.Children.Add(labelTb);

            Grid.SetColumn(barGrid, 0);
            grid.Children.Add(barGrid);

            var countTb = new TextBlock
            {
                Text = count.ToString("N0"),
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromArgb(180, 255, 255, 255)),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(8, 0, 0, 0),
                MinWidth = 34,
                TextAlignment = TextAlignment.Right
            };
            Grid.SetColumn(countTb, 1);
            grid.Children.Add(countTb);

            return grid;
        }

        /// <summary>
        /// All derived presentation values, computed once from a StatsData snapshot.
        /// </summary>
        private sealed class WrappedSummary
        {
            public DateTime FirstActivity;
            public DateTime LastActivity;
            public int DaysActive;

            public int FilesScanned;
            public long LibraryBytes;
            public double LibraryGb;
            public double Hours;
            public int Sessions;

            public int TotalPlays;
            public int UniqueTracks;
            public int UniqueArtists;
            public int UniqueAlbums;

            public string AvgBitrate = "-";
            public string AvgLufs = "-";
            public string AvgDr = "-";
            public int ClippingCount;
            public string AudiophileRating = "-";

            public List<(string Label, int Count)> TopFormats = new();
            public List<(string Label, int Count)> TopArtists = new();
            public List<(string Label, int Count)> TopAlbums = new();
            public List<(string Label, int Count)> TopTracks = new();

            public double LosslessPct;
            public bool HasLosslessData;
            public int LosslessPlays;
            public int LossyPlays;
            public string? MostByTimeLabel;

            public List<(string Label, int Count)> TopSampleRates = new();
            public List<(string Label, int Count)> TopBitDepths = new();
            public int MqaCount;
            public int AnalyzedCount;
            public double ClippingPct;
            public bool HasClippingData;
            public string DrRating = "-";
            public List<(string Label, int Count)> TopChannels = new();
            public string AvgFileSize = "-";
            public string AvgPlaysPerTrack = "-";

            public bool HasAnyData;

            public static WrappedSummary From(StatsData s)
            {
                var w = new WrappedSummary
                {
                    FirstActivity = s.FirstActivityDate,
                    LastActivity = s.LastActivityDate,
                    FilesScanned = s.TotalFilesScanned,
                    LibraryBytes = s.TotalLibraryBytes,
                    LibraryGb = s.TotalLibraryBytes / 1024.0 / 1024.0 / 1024.0,
                    Hours = s.TotalListeningSeconds / 3600.0,
                    Sessions = s.ScanSessionCount,
                    ClippingCount = s.ClippingDetectedCount,
                    UniqueArtists = s.ArtistPlayCounts.Count,
                    UniqueAlbums = s.AlbumPlayCounts.Count,
                    UniqueTracks = s.TrackPlayCounts.Count,
                };

                w.DaysActive = Math.Max(1, (int)Math.Round((w.LastActivity - w.FirstActivity).TotalDays) + 1);
                w.TotalPlays = s.TrackPlayCounts.Values.Sum(t => t.PlayCount);

                if (s.BitrateCount > 0) w.AvgBitrate = $"{s.BitrateSum / s.BitrateCount:N0} kbps";
                if (s.LufsCount > 0) w.AvgLufs = $"{s.LufsSum / s.LufsCount:F1}";
                if (s.DrCount > 0) w.AvgDr = $"{s.DrSum / s.DrCount:F1}";

                w.TopFormats = s.FormatCounts
                    .OrderByDescending(kv => kv.Value).Take(6)
                    .Select(kv => (kv.Key, kv.Value)).ToList();

                w.TopArtists = s.ArtistPlayCounts
                    .OrderByDescending(kv => kv.Value).Take(5)
                    .Select(kv => (kv.Key, kv.Value)).ToList();

                w.TopAlbums = s.AlbumPlayCounts
                    .OrderByDescending(kv => kv.Value).Take(5)
                    .Select(kv => (kv.Key, kv.Value)).ToList();

                w.TopTracks = s.TrackPlayCounts
                    .OrderByDescending(kv => kv.Value.PlayCount).Take(5)
                    .Select(kv => ($"{kv.Value.Title} — {kv.Value.Artist}", kv.Value.PlayCount)).ToList();

                int losslessPlays = 0, totalFmtPlays = 0;
                foreach (var kv in s.FormatCounts)
                {
                    totalFmtPlays += kv.Value;
                    if (LosslessFormats.Contains(kv.Key)) losslessPlays += kv.Value;
                }
                if (totalFmtPlays > 0)
                {
                    w.HasLosslessData = true;
                    w.LosslessPct = (double)losslessPlays / totalFmtPlays * 100.0;
                    w.LosslessPlays = losslessPlays;
                    w.LossyPlays = totalFmtPlays - losslessPlays;
                }

                w.TopSampleRates = s.SampleRateCounts
                    .OrderByDescending(kv => kv.Value).Take(5)
                    .Select(kv => (kv.Key, kv.Value)).ToList();
                w.TopBitDepths = s.BitDepthCounts
                    .OrderByDescending(kv => kv.Value).Take(5)
                    .Select(kv => (kv.Key, kv.Value)).ToList();
                w.MqaCount = s.MqaCount;
                w.AnalyzedCount = s.AnalyzedFileCount;
                if (s.AnalyzedFileCount > 0)
                {
                    w.HasClippingData = true;
                    w.ClippingPct = (double)s.ClippingDetectedCount / s.AnalyzedFileCount * 100.0;
                }
                if (s.DrCount > 0)
                {
                    double dr = s.DrSum / s.DrCount;
                    w.DrRating = dr >= 14 ? "Excellent" : dr >= 11 ? "Very good" : dr >= 8 ? "Moderate" : "Compressed";
                }

                w.TopChannels = s.ChannelCounts
                    .OrderByDescending(kv => kv.Value).Take(4)
                    .Select(kv => (kv.Key, kv.Value)).ToList();

                if (s.TotalFilesScanned > 0 && s.TotalLibraryBytes > 0)
                {
                    double avgMb = (double)s.TotalLibraryBytes / s.TotalFilesScanned / 1024.0 / 1024.0;
                    w.AvgFileSize = avgMb >= 1024 ? $"{avgMb / 1024.0:F1} GB" : $"{avgMb:F0} MB";
                }
                if (w.UniqueTracks > 0)
                    w.AvgPlaysPerTrack = $"{(double)w.TotalPlays / w.UniqueTracks:F1}";

                var byTime = s.TrackPlayCounts.Values
                    .OrderByDescending(t => t.SecondsListened).FirstOrDefault();
                if (byTime != null && byTime.SecondsListened > 0)
                {
                    double mins = byTime.SecondsListened / 60.0;
                    w.MostByTimeLabel = $"Most time: {byTime.Title} — {byTime.Artist} ({mins:F0} min)";
                }

                w.AudiophileRating = ComputeAudiophileRating(s, w);
                w.HasAnyData = w.FilesScanned > 0 || w.TotalPlays > 0 || w.TopFormats.Count > 0;
                return w;
            }

            private static string ComputeAudiophileRating(StatsData s, WrappedSummary w)
            {
                int score = 0;
                if (s.BitrateCount > 0)
                {
                    double br = (double)s.BitrateSum / s.BitrateCount;
                    if (br >= 900) score += 2; else if (br >= 256) score += 1;
                }
                if (s.DrCount > 0)
                {
                    double dr = s.DrSum / s.DrCount;
                    if (dr >= 12) score += 2; else if (dr >= 8) score += 1;
                }
                if (w.HasLosslessData)
                {
                    if (w.LosslessPct >= 75) score += 2; else if (w.LosslessPct >= 40) score += 1;
                }

                return score switch
                {
                    >= 5 => "Golden Ears 🏆",
                    >= 3 => "Discerning Listener ✨",
                    >= 1 => "Casual Audiophile 🎧",
                    _ => "Just Getting Started 🌱"
                };
            }
        }
    }
}
