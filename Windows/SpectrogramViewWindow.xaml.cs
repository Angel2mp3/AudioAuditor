using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using AudioQualityChecker.Models;
using AudioQualityChecker.Services;
using Microsoft.Win32;
using IOPath = System.IO.Path;

namespace AudioQualityChecker
{
    public partial class SpectrogramViewWindow : Window
    {
        private readonly AudioFileInfo _file;
        private readonly MainWindow _mainWindow;
        private BitmapSource? _bitmap;

        private bool _linearScale;
        private bool _differenceChannel;
        private bool _endZoom;

        public SpectrogramViewWindow(AudioFileInfo file, MainWindow mainWindow)
        {
            InitializeComponent();
            _file = file;
            _mainWindow = mainWindow;
            Title = $"Spectrogram — {file.FileName}";

            _linearScale = ThemeManager.SpectrogramLinearScale;
            _differenceChannel = ThemeManager.SpectrogramDifferenceChannel;
            _endZoom = false;

            BtnScale.IsChecked = _linearScale;
            BtnScale.Content = _linearScale ? "Linear" : "Log";
            BtnChannel.IsChecked = _differenceChannel;
            BtnChannel.Content = _differenceChannel ? "L-R" : "Mono";
            BtnZoom.IsChecked = _endZoom;
            BtnZoom.Content = _endZoom ? "End" : "Full";

            Loaded += async (_, _) => await RegenerateAsync();
        }

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2) return;
            DragMove();
        }

        private void Close_Click(object sender, RoutedEventArgs e) => Close();

        private async Task RegenerateAsync()
        {
            LoadingPanel.Visibility = Visibility.Visible;
            SpectViewer.Visibility = Visibility.Collapsed;

            int exportWidth = Math.Clamp((int)(_file.DurationSeconds * 60.0), 800, 16000);
            int exportHeight = 800;

            StatusLabel.Text = $"Rendering {exportWidth} × {exportHeight} px...";

            double endZoomSeconds = _endZoom ? 10.0 : 0.0;
            var channel = _differenceChannel ? SpectrogramChannel.Difference : SpectrogramChannel.Mono;

            var raw = await Task.Run(() => SpectrogramGenerator.Generate(
                _file.FilePath, exportWidth, exportHeight,
                _linearScale, channel, endZoomSeconds,
                ThemeManager.SpectrogramHiFiMode,
                ThemeManager.SpectrogramMagmaColormap));

            if (raw == null)
            {
                LoadingText.Text = "Could not generate spectrogram.";
                return;
            }

            _bitmap = _mainWindow.RenderSpectrogramForViewer(_file, exportWidth, exportHeight, raw);

            if (_bitmap == null)
            {
                LoadingText.Text = "Render failed.";
                return;
            }

            SpectImage.Source = _bitmap;
            SpectImage.Width = _bitmap.PixelWidth;
            SpectImage.Height = _bitmap.PixelHeight;

            LoadingPanel.Visibility = Visibility.Collapsed;
            SpectViewer.Visibility = Visibility.Visible;
            StatusLabel.Text = $"{_bitmap.PixelWidth} × {_bitmap.PixelHeight} px  •  {(BtnChannel.Content)}  •  {(BtnScale.Content)}  •  {(BtnZoom.Content)}";
        }

        private void Channel_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not System.Windows.Controls.Primitives.ToggleButton btn) return;
            _differenceChannel = btn.IsChecked == true;
            btn.Content = _differenceChannel ? "L-R" : "Mono";
            _ = RegenerateAsync();
        }

        private void Scale_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not System.Windows.Controls.Primitives.ToggleButton btn) return;
            _linearScale = btn.IsChecked == true;
            btn.Content = _linearScale ? "Linear" : "Log";
            _ = RegenerateAsync();
        }

        private void Zoom_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not System.Windows.Controls.Primitives.ToggleButton btn) return;
            _endZoom = btn.IsChecked == true;
            btn.Content = _endZoom ? "End" : "Full";
            _ = RegenerateAsync();
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            if (_bitmap == null) return;

            var dlg = new SaveFileDialog
            {
                Title = "Save Spectrogram",
                FileName = $"{IOPath.GetFileNameWithoutExtension(_file.FileName)}_spectrogram.png",
                Filter = "PNG Image|*.png",
                DefaultExt = ".png"
            };
            if (dlg.ShowDialog() != true) return;

            try
            {
                using var stream = new FileStream(dlg.FileName, FileMode.Create);
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(_bitmap));
                encoder.Save(stream);
                StatusLabel.Text = $"Saved: {dlg.FileName}";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error saving:\n{ex.Message}", "Save Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}
