using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using AudioQualityChecker.Services;

namespace AudioQualityChecker
{
    public partial class SpectrogramCompareWindow : Window
    {
        private readonly string _fileA;
        private readonly string _fileB;
        private BitmapSource? _imgA;
        private BitmapSource? _imgB;
        private int _imgAPixelWidth;
        private int _imgAPixelHeight;
        private int _imgBPixelWidth;
        private int _imgBPixelHeight;

        private WriteableBitmap? _diffBitmap;
        private int _diffPixelWidth;
        private int _diffPixelHeight;

        private bool _isFullZoom = false;

        // We support Stacked, Overlay and Wipe modes. Default is Stacked.
        private string _viewMode = "Stacked";

        public SpectrogramCompareWindow(string fileA, string fileB)
        {
            _fileA = fileA;
            _fileB = fileB;
            InitializeComponent();
            Loaded += OnLoaded;
        }

        private async void OnLoaded(object sender, RoutedEventArgs e)
        {
            StackedLabelA.Text = System.IO.Path.GetFileName(_fileA);
            StackedLabelB.Text = System.IO.Path.GetFileName(_fileB);

            // Generate spectrograms on background thread, using theme settings
            try
            {
                var channel = ThemeManager.SpectrogramDifferenceChannel ? Services.SpectrogramChannel.Difference : Services.SpectrogramChannel.Mono;
                var bmpA = await Task.Run(() => SpectrogramGenerator.Generate(_fileA, 1200, 400,
                    ThemeManager.SpectrogramLinearScale,
                    channel,
                    0,
                    ThemeManager.SpectrogramHiFiMode,
                    ThemeManager.SpectrogramMagmaColormap));
                var bmpB = await Task.Run(() => SpectrogramGenerator.Generate(_fileB, 1200, 400,
                    ThemeManager.SpectrogramLinearScale,
                    channel,
                    0,
                    ThemeManager.SpectrogramHiFiMode,
                    ThemeManager.SpectrogramMagmaColormap));

                if (bmpA != null)
                {
                    _imgA = bmpA;
                    _imgAPixelWidth = bmpA.PixelWidth;
                    _imgAPixelHeight = bmpA.PixelHeight;
                    ImageA.Source = bmpA;
                    StackedImageA.Source = bmpA;
                }
                if (bmpB != null)
                {
                    _imgB = bmpB;
                    _imgBPixelWidth = bmpB.PixelWidth;
                    _imgBPixelHeight = bmpB.PixelHeight;
                    ImageB.Source = bmpB;
                    StackedImageB.Source = bmpB;
                }

                ComputeDiffBitmap();
                UpdateViewMode();
                UpdateStats();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to generate spectrograms:\n{ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void UpdateViewMode()
        {
            var accent = Application.Current.TryFindResource("AccentColor") as Brush;
            var btnBg = Application.Current.TryFindResource("ButtonBg") as Brush;
            var textPrimary = Application.Current.TryFindResource("TextPrimary") as Brush;
            var white = new SolidColorBrush(Colors.White);

            BtnStacked.Background = _viewMode == "Stacked" ? (accent ?? Brushes.CornflowerBlue) : (btnBg ?? Brushes.Gray);
            BtnStacked.Foreground = _viewMode == "Stacked" ? white : (textPrimary ?? Brushes.White);
            BtnOverlay.Background = _viewMode == "Overlay" ? (accent ?? Brushes.CornflowerBlue) : (btnBg ?? Brushes.Gray);
            BtnOverlay.Foreground = _viewMode == "Overlay" ? white : (textPrimary ?? Brushes.White);
            BtnWipe.Background = _viewMode == "Wipe" ? (accent ?? Brushes.CornflowerBlue) : (btnBg ?? Brushes.Gray);
            BtnWipe.Foreground = _viewMode == "Wipe" ? white : (textPrimary ?? Brushes.White);

            if (_viewMode == "Stacked")
            {
                StackedPanel.Visibility = Visibility.Visible;
                OverlayPanel.Visibility = Visibility.Collapsed;
                WipeSliderPanel.Visibility = Visibility.Collapsed;
                OffsetSliderPanel.Visibility = Visibility.Collapsed;
                MergeSliderPanel.Visibility = Visibility.Collapsed;
                UpdateStackedLayout();
            }
            else if (_viewMode == "Overlay")
            {
                StackedPanel.Visibility = Visibility.Collapsed;
                OverlayPanel.Visibility = Visibility.Visible;
                WipeSliderPanel.Visibility = Visibility.Collapsed;
                OffsetSliderPanel.Visibility = Visibility.Visible;
                MergeSliderPanel.Visibility = Visibility.Visible;

                if (_imgB != null)
                {
                    ImageB.Opacity = 1;
                    ImageB.RenderTransform = new TranslateTransform(0, 0);
                    ImageB.Clip = new RectangleGeometry(new Rect(0, 0, _imgBPixelWidth, _imgBPixelHeight));
                }
                if (_diffBitmap != null)
                {
                    DiffImage.Visibility = Visibility.Visible;
                    DiffImage.Opacity = 1;
                    DiffImage.Source = _diffBitmap;
                }
                WipeLine.Visibility = Visibility.Collapsed;
                UpdateLayout();
            }
            else // Wipe
            {
                StackedPanel.Visibility = Visibility.Collapsed;
                OverlayPanel.Visibility = Visibility.Visible;
                WipeSliderPanel.Visibility = Visibility.Visible;
                OffsetSliderPanel.Visibility = Visibility.Collapsed;
                MergeSliderPanel.Visibility = Visibility.Collapsed;

                if (_imgB != null)
                {
                    ImageB.Opacity = 1;
                    ImageB.RenderTransform = new TranslateTransform(0, 0);
                }
                DiffImage.Visibility = Visibility.Collapsed;
                WipeLine.Visibility = Visibility.Visible;
                UpdateWipe();
                UpdateLayout();
            }
        }

        private void ComputeDiffBitmap()
        {
            if (_imgA == null || _imgB == null) return;

            int w = Math.Min(_imgAPixelWidth, _imgBPixelWidth);
            int h = Math.Min(_imgAPixelHeight, _imgBPixelHeight);
            _diffPixelWidth = w;
            _diffPixelHeight = h;

            int strideA = _imgAPixelWidth * 3;
            int strideB = _imgBPixelWidth * 3;
            int strideD = w * 3;
            var pixelsA = new byte[strideA * _imgAPixelHeight];
            var pixelsB = new byte[strideB * _imgBPixelHeight];
            var pixelsD = new byte[strideD * h];

            _imgA.CopyPixels(pixelsA, strideA, 0);
            _imgB.CopyPixels(pixelsB, strideB, 0);

            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    int iA = y * strideA + x * 3;
                    int iB = y * strideB + x * 3;
                    int iD = y * strideD + x * 3;

                    int dr = Math.Abs(pixelsA[iA] - pixelsB[iB]);
                    int dg = Math.Abs(pixelsA[iA + 1] - pixelsB[iB + 1]);
                    int db = Math.Abs(pixelsA[iA + 2] - pixelsB[iB + 2]);

                    // Map average diff to red intensity; boost contrast
                    int avgDiff = Math.Min(255, (dr + dg + db) / 3 * 3);
                    pixelsD[iD] = (byte)avgDiff;      // R
                    pixelsD[iD + 1] = 0;              // G
                    pixelsD[iD + 2] = 0;              // B
                }
            }

            _diffBitmap = new WriteableBitmap(w, h, 96, 96, PixelFormats.Rgb24, null);
            _diffBitmap.WritePixels(new Int32Rect(0, 0, w, h), pixelsD, strideD, 0);
        }

        private void UpdateStackedLayout()
        {
            if (_imgA == null) return;

            double scale = 1;
            if (!_isFullZoom)
            {
                double containerW = Math.Max(StackedPanel.ActualWidth - 16, 100);
                scale = Math.Min(containerW / _imgAPixelWidth, 1.0);
                if (double.IsNaN(scale) || scale <= 0) scale = 1;
            }

            StackedImageA.Width = _imgAPixelWidth * scale;
            StackedImageA.Height = _imgAPixelHeight * scale;
            if (_imgB != null)
            {
                StackedImageB.Width = _imgBPixelWidth * scale;
                StackedImageB.Height = _imgBPixelHeight * scale;
            }
        }

        private new void UpdateLayout()
        {
            if (_imgA == null) return;
            if (_viewMode == "Stacked")
            {
                UpdateStackedLayout();
                return;
            }

            double containerW = SpectCanvas.ActualWidth;
            double containerH = SpectCanvas.ActualHeight;
            double imgW = _imgAPixelWidth;
            double imgH = _imgAPixelHeight;

            if (_isFullZoom)
            {
                ImageA.Width = imgW;
                ImageA.Height = imgH;
                if (_imgB != null)
                {
                    ImageB.Width = _imgBPixelWidth;
                    ImageB.Height = _imgBPixelHeight;
                }
                SpectCanvas.Width = imgW;
                SpectCanvas.Height = imgH;
            }
            else
            {
                if (containerW <= 0 || containerH <= 0)
                {
                    return;
                }
                double scale = Math.Min(containerW / imgW, containerH / imgH);
                if (double.IsNaN(scale) || scale <= 0) scale = 1;
                ImageA.Width = imgW * scale;
                ImageA.Height = imgH * scale;
                if (_imgB != null)
                {
                    ImageB.Width = _imgBPixelWidth * scale;
                    ImageB.Height = _imgBPixelHeight * scale;
                }
                if (_diffBitmap != null)
                {
                    DiffImage.Width = _diffPixelWidth * scale;
                    DiffImage.Height = _diffPixelHeight * scale;
                }
                SpectCanvas.Width = Math.Max(ImageA.Width, ImageB?.Width ?? 0);
                SpectCanvas.Height = Math.Max(ImageA.Height, ImageB?.Height ?? 0);
            }

            if (_viewMode == "Overlay")
            {
                ApplyMerge();
                ApplyOffset();
            }
        }

        private void ApplyMerge()
        {
            if (_viewMode != "Overlay" || _imgB == null || _imgA == null) return;

            double merge = MergeSlider.Value / 100.0;
            double scale = ImageA.Height / _imgAPixelHeight;
            double totalH = _imgAPixelHeight * scale;

            // 0% = fully below A, 100% = fully overlapping A
            double bY = totalH * (1.0 - merge);

            var ttB = (TranslateTransform)ImageB.RenderTransform;
            ttB.Y = bY;

            // Update diff image: same position as B, clipped to overlap region
            if (_diffBitmap != null)
            {
                var ttD = (TranslateTransform)DiffImage.RenderTransform;
                ttD.Y = bY;

                double overlapH = Math.Max(0, totalH - bY);
                double clipH = overlapH / scale;
                DiffImage.Clip = new RectangleGeometry(new Rect(0, 0, _diffPixelWidth, Math.Min(_diffPixelHeight, clipH)));
            }
        }

        private void ApplyOffset()
        {
            if (_viewMode != "Overlay" || _imgB == null) return;

            int offset = (int)OffsetSlider.Value;
            double scale = ImageA.Width / _imgAPixelWidth;
            double visualOffset = offset * scale;

            var ttB = (TranslateTransform)ImageB.RenderTransform;
            ttB.X = visualOffset;

            if (_diffBitmap != null)
            {
                var ttD = (TranslateTransform)DiffImage.RenderTransform;
                ttD.X = visualOffset;
            }
        }

        private void UpdateWipe()
        {
            if (_viewMode != "Wipe" || _imgB == null) return;

            double pct = WipeSlider.Value / 100.0;
            double scale = ImageB.Width / _imgBPixelWidth;
            double wipePx = _imgBPixelWidth * pct;
            double visualWipe = wipePx * scale;

            var clipRect = new Rect(0, 0, Math.Min(wipePx, _imgBPixelWidth), _imgBPixelHeight);
            ImageB.Clip = new RectangleGeometry(clipRect);

            WipeLine.Height = ImageB.Height;
            Canvas.SetLeft(WipeLine, visualWipe);
            Canvas.SetTop(WipeLine, 0);
        }

        private void UpdateStats()
        {
            string? GetRate(string f)
            {
                string json = f + ".info.json";
                if (!File.Exists(json)) return null;
                try
                {
                    var lines = File.ReadAllLines(json);
                    var line = lines.FirstOrDefault(l => l.Contains("\"sample_rate\":"));
                    if (line != null)
                    {
                        int idx = line.IndexOf(':');
                        if (idx >= 0)
                        {
                            var val = line.Substring(idx + 1).Trim().TrimEnd(',', '"');
                            if (double.TryParse(val, out double sr))
                                return $"{sr / 1000:F1} kHz";
                        }
                    }
                }
                catch { }
                return null;
            }

            var infoA = GetRate(_fileA);
            var infoB = GetRate(_fileB);
            var parts = new System.Collections.Generic.List<string>();
            parts.Add($"File A: {_imgAPixelWidth}×{_imgAPixelHeight}px");
            if (infoA != null) parts.Add($"Sample Rate: {infoA}");
            parts.Add($"| File B: {_imgBPixelWidth}×{_imgBPixelHeight}px");
            if (infoB != null) parts.Add($"Sample Rate: {infoB}");
            StatsText.Text = string.Join(" ", parts);
        }

        // ─── Event Handlers ───

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
                DragMove();
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void ViewMode_Click(object sender, RoutedEventArgs e)
        {
            if (sender == BtnStacked)
                _viewMode = "Stacked";
            else if (sender == BtnOverlay)
                _viewMode = "Overlay";
            else if (sender == BtnWipe)
                _viewMode = "Wipe";
            UpdateViewMode();
        }

        private void Channel_Click(object sender, RoutedEventArgs e)
        {
            // Mono toggle — future feature
        }

        private void Scale_Click(object sender, RoutedEventArgs e)
        {
            // Log/linear scale toggle — future feature
        }

        private void Zoom_Click(object sender, RoutedEventArgs e)
        {
            _isFullZoom = BtnZoom.IsChecked ?? false;
            BtnZoom.Content = _isFullZoom ? "Full" : "Fit";
            UpdateLayout();
        }

        private void MergeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_imgB == null) return;
            MergeLabel.Text = $"{(int)e.NewValue}%";
            ApplyMerge();
        }

        private void OffsetSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_imgB == null) return;
            OffsetLabel.Text = $"{(int)e.NewValue} px";
            ApplyOffset();
        }

        private void WipeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_imgB == null) return;
            WipeLabel.Text = $"{(int)e.NewValue}%";
            UpdateWipe();
        }

        protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
        {
            base.OnRenderSizeChanged(sizeInfo);
            if (sizeInfo.WidthChanged || sizeInfo.HeightChanged)
            {
                UpdateLayout();
            }
        }
    }
}
