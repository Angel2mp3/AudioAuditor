using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using SharpCompress.Archives;
using SharpCompress.Common;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using IOPath = System.IO.Path;
using AudioQualityChecker.Models;
using AudioQualityChecker.Services;
using AudioQualityChecker.Services.Scrobbling;
using Microsoft.Win32;

namespace AudioQualityChecker
{
    // Music service search: opening track/artist queries on the configured
    // streaming services. Extracted verbatim from MainWindow.xaml.cs (2026-06-05 split).
    public partial class MainWindow
    {
        // ═══════════════════════════════════════════
        //  Music Service Search
        // ═══════════════════════════════════════════

        private static string StatusDisplayText(AudioStatus status) => status switch
        {
            AudioStatus.Valid => "REAL",
            AudioStatus.Fake => "FAKE",
            AudioStatus.Unknown => "UNKNOWN",
            AudioStatus.Corrupt => "CORRUPT",
            AudioStatus.Optimized => "OPTIMIZED",
            AudioStatus.Analyzing => "ANALYZING",
            _ => status.ToString().ToUpper()
        };

        private string BuildSpectrogramTitle(AudioFileInfo file)
        {
            string titlePrefix = _visualizerMode ? "Visualizer" : "Spectrogram";

            // In visualizer mode, show a compact title to avoid overlapping toolbar buttons
            if (_visualizerMode)
            {
                return $"{titlePrefix}: {file.FileName}";
            }

            string statusDisplay = StatusDisplayText(file.Status);
            string statusExtra = file.HasClipping ? " | CLIPPING DETECTED"
                               : file.HasScaledClipping ? $" | SCALED CLIPPING ({file.MaxSampleLevelDb:F1} dB)"
                               : "";

            var extras = new List<string>();
            if (file.Bpm > 0) extras.Add($"BPM: {file.Bpm}");
            if (file.IsMqa) extras.Add($"MQA: {file.MqaDisplay}");
            if (file.IsAiGenerated) extras.Add($"AI: {file.AiSource}");

            // Spectrogram mode indicators
            var modeIndicators = new List<string>();
            if (_spectrogramChannel == SpectrogramChannel.Difference) modeIndicators.Add("L-R");
            if (_spectrogramLinearScale) modeIndicators.Add("Linear");
            if (_spectrogramEndZoom) modeIndicators.Add("End 10s");
            if (modeIndicators.Count > 0) extras.Add(string.Join(", ", modeIndicators));

            string extraInfo = extras.Count > 0 ? "   |   " + string.Join("   |   ", extras) : "";

            return $"{titlePrefix}: {file.FileName}   |   " +
                   $"{file.SampleRate:N0} Hz / {file.BitsPerSampleDisplay}   |   " +
                   $"{file.Duration}{extraInfo}   |   Status: {statusDisplay}{statusExtra}";
        }

        private void UpdateServiceButtonLabels()
        {
            var images = new[] { ServiceImage1, ServiceImage2, ServiceImage3, ServiceImage4, ServiceImage5, ServiceImage6 };
            var buttons = new[] { ServiceBtn1, ServiceBtn2, ServiceBtn3, ServiceBtn4, ServiceBtn5, ServiceBtn6 };

            // Services whose PNGs render too small — force vector icon instead
            var forceVector = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < 6; i++)
            {
                string svc = ThemeManager.MusicServiceSlots[i];
                images[i].Source = CreateServiceLogo(svc, i, forceVector.Contains(svc));
                buttons[i].ToolTip = svc == "Custom..." ? "Search on custom service" : $"Search on {svc}";
                buttons[i].Visibility = ThemeManager.MusicServiceSlotVisible[i] ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        internal void ApplyToolbarButtonVisibility()
        {
            WrappedButton.Visibility     = ThemeManager.ShowWrappedButton      ? Visibility.Visible : Visibility.Collapsed;
            MiniPlayerButton.Visibility  = ThemeManager.ShowMiniPlayerButton   ? Visibility.Visible : Visibility.Collapsed;
            MusicServicesPanel.Visibility = ThemeManager.ShowMusicServiceButtons ? Visibility.Visible : Visibility.Collapsed;
        }

        // customIconPathOverride lets callers (e.g. the NP search popup) supply a custom
        // icon path from a different config array than ThemeManager.CustomServiceIcons.
        private static ImageSource CreateServiceLogo(string service, int slotIndex = -1, bool forceVector = false, string? customIconPathOverride = null)
        {
            // Use embedded PNGs for services that have them (unless forceVector is set)
            if (!forceVector && (service == "Qobuz" || service == "Spotify" || service == "Amazon Music" || service == "Tidal" || service == "YouTube Music" || service == "Apple Music" || service == "SoundCloud" || service == "Deezer" || service == "Last.fm"))
            {
                try
                {
                    string pngName = service switch
                    {
                        "Spotify" => "Resources/Spotify.png",
                        "YouTube Music" => "Resources/YTM.png",
                        "Tidal" => "Resources/Tidal.png",
                        "Qobuz" => "Resources/Qobuz.png",
                        "Amazon Music" => "Resources/Amazon-music.png",
                        "Apple Music" => "Resources/Apple_music.png",
                        "SoundCloud" => "Resources/Soundcloud.png",
                        "Deezer" => "Resources/Deezer.png",
                        "Last.fm" => "Resources/last.fm.png",
                        _ => ""
                    };
                    if (!string.IsNullOrEmpty(pngName))
                    {
                        var bmp = new BitmapImage();
                        bmp.BeginInit();
                        bmp.UriSource = new Uri($"pack://application:,,,/{pngName}");
                        bmp.CacheOption = BitmapCacheOption.OnLoad;
                        bmp.DecodePixelWidth = 64;
                        bmp.EndInit();
                        bmp.Freeze();
                        return bmp;
                    }
                }
                catch { /* fall through to generated icon */ }
            }

            // Load custom icon from file path
            if (service == "Custom...")
            {
                string iconPath = customIconPathOverride
                    ?? ((slotIndex >= 0 && slotIndex < 6) ? ThemeManager.CustomServiceIcons[slotIndex] : "");
                if (!string.IsNullOrEmpty(iconPath) && File.Exists(iconPath))
                {
                    try
                    {
                        var bmp = new BitmapImage();
                        bmp.BeginInit();
                        bmp.UriSource = new Uri(iconPath);
                        bmp.CacheOption = BitmapCacheOption.OnLoad;
                        bmp.DecodePixelWidth = 44;
                        bmp.EndInit();
                        bmp.Freeze();
                        return bmp;
                    }
                    catch { /* fall through to default */ }
                }
            }

            var group = new DrawingGroup();
            const double S = 24; // coordinate space
            var c = new Point(S / 2, S / 2);

            switch (service)
            {
                case "Spotify":
                {
                    // Green circle
                    group.Children.Add(new GeometryDrawing(
                        new SolidColorBrush(Color.FromRgb(30, 215, 96)), null,
                        new EllipseGeometry(c, 12, 12)));
                    // 3 curved sound-wave arcs
                    var pen = new Pen(new SolidColorBrush(Color.FromRgb(0, 0, 0)), 2.0)
                        { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
                    double[][] arcs = { new[]{6.0, 8.5, 12.0, 5.0, 18.0, 8.5},
                                        new[]{7.0, 12.0, 12.0, 9.0, 17.0, 12.0},
                                        new[]{8.0, 15.5, 12.0, 13.5, 16.0, 15.5} };
                    foreach (var a in arcs)
                    {
                        var pg = new PathGeometry();
                        var fig = new PathFigure { StartPoint = new Point(a[0], a[1]), IsClosed = false, IsFilled = false };
                        fig.Segments.Add(new QuadraticBezierSegment(new Point(a[2], a[3]), new Point(a[4], a[5]), true));
                        pg.Figures.Add(fig);
                        group.Children.Add(new GeometryDrawing(null, pen, pg));
                    }
                    break;
                }
                case "YouTube Music":
                {
                    // Red circle
                    group.Children.Add(new GeometryDrawing(
                        new SolidColorBrush(Color.FromRgb(255, 0, 0)), null,
                        new EllipseGeometry(c, 12, 12)));
                    // White circle ring
                    group.Children.Add(new GeometryDrawing(
                        null, new Pen(Brushes.White, 1.4),
                        new EllipseGeometry(c, 5.5, 5.5)));
                    // White play triangle
                    var tri = new StreamGeometry();
                    using (var ctx = tri.Open())
                    {
                        ctx.BeginFigure(new Point(10, 8), true, true);
                        ctx.LineTo(new Point(16.5, 12), true, false);
                        ctx.LineTo(new Point(10, 16), true, false);
                    }
                    tri.Freeze();
                    group.Children.Add(new GeometryDrawing(Brushes.White, null, tri));
                    break;
                }
                case "Tidal":
                {
                    // Black circle
                    group.Children.Add(new GeometryDrawing(
                        new SolidColorBrush(Color.FromRgb(0, 0, 0)), null,
                        new EllipseGeometry(c, 12, 12)));
                    // 3 white diamonds in triangle arrangement
                    AddDiamond(group, 12, 7.5, 3.0, 2.8, Brushes.White);
                    AddDiamond(group, 8, 13, 3.0, 2.8, Brushes.White);
                    AddDiamond(group, 16, 13, 3.0, 2.8, Brushes.White);
                    break;
                }
                case "Amazon Music":
                {
                    // Handled above via PNG resource
                    break;
                }
                case "Qobuz":
                {
                    // Handled above via PNG resource
                    break;
                }
                case "Apple Music":
                {
                    // Red/pink circle
                    group.Children.Add(new GeometryDrawing(
                        new SolidColorBrush(Color.FromRgb(252, 60, 68)), null,
                        new EllipseGeometry(c, 12, 12)));
                    // Music note ♪
                    var notePen = new Pen(Brushes.White, 1.8) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
                    var stem = new PathGeometry();
                    var stFig = new PathFigure { StartPoint = new Point(14, 6.5), IsClosed = false, IsFilled = false };
                    stFig.Segments.Add(new LineSegment(new Point(14, 16), true));
                    stem.Figures.Add(stFig);
                    group.Children.Add(new GeometryDrawing(null, notePen, stem));
                    // Note head
                    group.Children.Add(new GeometryDrawing(Brushes.White, null,
                        new EllipseGeometry(new Point(12, 16), 2.5, 1.8)));
                    // Flag
                    var flag = new PathGeometry();
                    var fFig = new PathFigure { StartPoint = new Point(14, 6.5), IsClosed = false, IsFilled = false };
                    fFig.Segments.Add(new QuadraticBezierSegment(new Point(18, 7), new Point(17, 10.5), true));
                    flag.Figures.Add(fFig);
                    group.Children.Add(new GeometryDrawing(null, notePen, flag));
                    break;
                }
                case "Deezer":
                {
                    // Purple circle
                    group.Children.Add(new GeometryDrawing(
                        new SolidColorBrush(Color.FromRgb(162, 56, 255)), null,
                        new EllipseGeometry(c, 12, 12)));
                    // Equalizer bars (5 bars)
                    double[] heights = { 6, 10, 14, 8, 11 };
                    for (int b = 0; b < 5; b++)
                    {
                        double x = 6 + b * 3;
                        double h = heights[b];
                        double top = 19 - h;
                        group.Children.Add(new GeometryDrawing(Brushes.White, null,
                            new RectangleGeometry(new Rect(x, top, 2, h), 0.5, 0.5)));
                    }
                    break;
                }
                case "SoundCloud":
                {
                    // Orange circle
                    group.Children.Add(new GeometryDrawing(
                        new SolidColorBrush(Color.FromRgb(255, 85, 0)), null,
                        new EllipseGeometry(c, 12, 12)));
                    // Simplified cloud
                    var cloud = new CombinedGeometry(GeometryCombineMode.Union,
                        new EllipseGeometry(new Point(13, 12), 5, 4),
                        new CombinedGeometry(GeometryCombineMode.Union,
                            new EllipseGeometry(new Point(9, 13), 3.5, 3),
                            new EllipseGeometry(new Point(10, 10), 3, 2.5)));
                    group.Children.Add(new GeometryDrawing(Brushes.White, null, cloud));
                    break;
                }
                case "Bandcamp":
                {
                    // Blue circle
                    group.Children.Add(new GeometryDrawing(
                        new SolidColorBrush(Color.FromRgb(29, 160, 195)), null,
                        new EllipseGeometry(c, 12, 12)));
                    // Angled bar (Bandcamp's slanted rectangle)
                    var bar = new StreamGeometry();
                    using (var ctx = bar.Open())
                    {
                        ctx.BeginFigure(new Point(8, 7), true, true);
                        ctx.LineTo(new Point(18, 7), true, false);
                        ctx.LineTo(new Point(16, 17), true, false);
                        ctx.LineTo(new Point(6, 17), true, false);
                    }
                    bar.Freeze();
                    group.Children.Add(new GeometryDrawing(Brushes.White, null, bar));
                    break;
                }
                case "Last.fm":
                {
                    // Red circle (Last.fm brand red)
                    group.Children.Add(new GeometryDrawing(
                        new SolidColorBrush(Color.FromRgb(186, 0, 0)), null,
                        new EllipseGeometry(c, 12, 12)));
                    // "fm" text
                    var ft = new FormattedText("fm", System.Globalization.CultureInfo.InvariantCulture,
                        FlowDirection.LeftToRight, new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal), 11, Brushes.White,
                        VisualTreeHelper.GetDpi(Application.Current.MainWindow).PixelsPerDip);
                    group.Children.Add(new GeometryDrawing(Brushes.White, null,
                        ft.BuildGeometry(new Point(12 - ft.Width / 2, 12 - ft.Height / 2))));
                    break;
                }
                default:
                {
                    // Generic grey circle with "?"
                    group.Children.Add(new GeometryDrawing(
                        new SolidColorBrush(Color.FromRgb(100, 100, 100)), null,
                        new EllipseGeometry(c, 12, 12)));
                    var ft = new FormattedText("?", System.Globalization.CultureInfo.InvariantCulture,
                        FlowDirection.LeftToRight, new Typeface("Segoe UI"), 14, Brushes.White,
                        VisualTreeHelper.GetDpi(Application.Current.MainWindow).PixelsPerDip);
                    group.Children.Add(new GeometryDrawing(Brushes.White, null,
                        ft.BuildGeometry(new Point(12 - ft.Width / 2, 12 - ft.Height / 2))));
                    break;
                }
            }

            var img = new DrawingImage(group);
            img.Freeze();
            return img;
        }

        private static void AddDiamond(DrawingGroup group, double cx, double cy, double rx, double ry, Brush fill)
        {
            var diamond = new StreamGeometry();
            using (var ctx = diamond.Open())
            {
                ctx.BeginFigure(new Point(cx, cy - ry), true, true);
                ctx.LineTo(new Point(cx + rx, cy), true, false);
                ctx.LineTo(new Point(cx, cy + ry), true, false);
                ctx.LineTo(new Point(cx - rx, cy), true, false);
            }
            diamond.Freeze();
            group.Children.Add(new GeometryDrawing(fill, null, diamond));
        }

        private void ServiceSearch_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn || btn.Tag is not string tagStr) return;
            if (!int.TryParse(tagStr, out int idx) || idx < 0 || idx > 5) return;

            if (FileGrid.SelectedItem is not AudioFileInfo file)
            {
                ErrorDialog.Show("No Selection", "Select a song first to search.", this);
                return;
            }

            string serviceName = ThemeManager.MusicServiceSlots[idx];
            string query = !string.IsNullOrEmpty(file.Artist) && !string.IsNullOrEmpty(file.Title)
                ? $"{file.Artist} {file.Title}"
                : IOPath.GetFileNameWithoutExtension(file.FileName);

            string url;
            if (serviceName == "Custom...")
            {
                string customUrl = ThemeManager.CustomServiceUrls[idx];
                if (string.IsNullOrWhiteSpace(customUrl))
                {
                    ErrorDialog.Show("No Custom URL", "Configure a custom search URL in Settings first.\nPaste the search URL and the song name will be appended automatically.", this);
                    return;
                }
                string encoded = Uri.EscapeDataString(query);
                if (customUrl.Contains("{query}"))
                    url = customUrl.Replace("{query}", encoded);
                else
                    url = customUrl.TrimEnd('/') + "/" + encoded;
            }
            else
            {
                url = ThemeManager.GetMusicServiceUrl(serviceName, query);
            }

            try
            {
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                ErrorDialog.Show("Browser Error", $"Could not open browser:\n{ex.Message}", this);
            }
        }
    }
}
