using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using AudioQualityChecker.Models;
using AudioQualityChecker.Services;

namespace AudioQualityChecker
{
    public partial class MainWindow
    {        // ═══════════════════════════════════════════
        //  Audio Visualizer
        // ═══════════════════════════════════════════

        private void ToggleVisualizer_Click(object sender, RoutedEventArgs e)
        {
            _visualizerMode = !_visualizerMode;
            ThemeManager.VisualizerMode = _visualizerMode;
            ThemeManager.SavePlayOptions();
            UpdateVisualizerToggleText();

            if (_visualizerMode)
            {
                SpectrogramImage.Visibility = Visibility.Collapsed;
                VisualizerCanvas.Visibility = Visibility.Visible;
                FreqLabelGrid.Visibility = Visibility.Collapsed;
                BtnVisualizerStyle.Visibility = Visibility.Visible;
                StartVisualizer();
            }
            else
            {
                VisualizerCanvas.Visibility = Visibility.Collapsed;
                SpectrogramImage.Visibility = Visibility.Visible;
                FreqLabelGrid.Visibility = Visibility.Visible;
                BtnVisualizerStyle.Visibility = Visibility.Collapsed;
                StopVisualizer();
            }

            // Update title prefix
            if (_currentSpectrogramFile is AudioFileInfo sf)
            {
                SpectrogramTitle.Text = BuildSpectrogramTitle(sf);
            }
        }

        private void UpdateVisualizerToggleText()
        {
            if (VisualizerToggleText != null)
                VisualizerToggleText.Text = _visualizerMode ? "Spectrogram" : "Visualizer";
        }

        // ── Visualizer Style Names ──
        private static readonly string[] _vizStyleNames =
        {
            "Bars", "Mirror", "Particles", "Circles", "Scope", "VU Meter"
        };
        private const int VizStyleCount = 6; // number of real styles (0-5)

        private void VisualizerStyle_Click(object sender, RoutedEventArgs e)
        {
            // Build the dropdown menu items dynamically, themed to current settings
            VisualizerStyleMenu.Children.Clear();

            var panelBg = (System.Windows.Media.Brush)FindResource("PanelBg");
            var hoverBg = (System.Windows.Media.Brush)FindResource("ButtonBg");
            var textBrush = (System.Windows.Media.Brush)FindResource("TextPrimary");
            var accentBrush = (System.Windows.Media.Brush)FindResource("AccentColor");
            var borderBrush = (System.Windows.Media.Brush)FindResource("ButtonBorder");

            for (int i = 0; i < VizStyleCount; i++)
            {
                int styleIdx = i; // capture for lambda
                bool isActive = (i == _visualizerStyle && !_vizCycleActive);

                var tb = new TextBlock
                {
                    Text = _vizStyleNames[i],
                    Foreground = isActive ? accentBrush : textBrush,
                    FontWeight = isActive ? FontWeights.SemiBold : FontWeights.Normal,
                    FontSize = 11,
                    FontFamily = new System.Windows.Media.FontFamily("Segoe UI"),
                    Padding = new Thickness(6, 3, 6, 3),
                    Cursor = System.Windows.Input.Cursors.Hand,
                    Background = System.Windows.Media.Brushes.Transparent
                };

                tb.MouseEnter += (s, _) => ((TextBlock)s!).Background = hoverBg;
                tb.MouseLeave += (s, _) => ((TextBlock)s!).Background = System.Windows.Media.Brushes.Transparent;
                tb.MouseLeftButtonUp += (s, _) =>
                {
                    StopVisualizerCycle();
                    ApplyVisualizerStyle(styleIdx);
                    VisualizerStylePopup.IsOpen = false;
                };

                VisualizerStyleMenu.Children.Add(tb);
            }

            // Separator
            VisualizerStyleMenu.Children.Add(new System.Windows.Controls.Separator
            {
                Margin = new Thickness(2, 1, 2, 1),
                Background = borderBrush
            });

            // Cycle option
            bool cycleActive = _vizCycleActive;
            var cycleTb = new TextBlock
            {
                Text = "⟳ Cycle All",
                Foreground = cycleActive ? accentBrush : textBrush,
                FontWeight = cycleActive ? FontWeights.SemiBold : FontWeights.Normal,
                FontSize = 11,
                FontFamily = new System.Windows.Media.FontFamily("Segoe UI"),
                Padding = new Thickness(6, 3, 6, 3),
                Cursor = System.Windows.Input.Cursors.Hand,
                Background = System.Windows.Media.Brushes.Transparent
            };
            cycleTb.MouseEnter += (s, _) => ((TextBlock)s!).Background = hoverBg;
            cycleTb.MouseLeave += (s, _) => ((TextBlock)s!).Background = System.Windows.Media.Brushes.Transparent;
            cycleTb.MouseLeftButtonUp += (s, _) =>
            {
                StartVisualizerCycle();
                VisualizerStylePopup.IsOpen = false;
            };
            VisualizerStyleMenu.Children.Add(cycleTb);

            VisualizerStylePopup.IsOpen = true;
        }

        private void ApplyVisualizerStyle(int style)
        {
            _visualizerStyle = style;
            ThemeManager.VisualizerStyle = _visualizerStyle;
            ThemeManager.SavePlayOptions();
            UpdateVisualizerStyleText();

            // Force recreation of visual elements on style change
            _vizBars = null;
            _vizMirrorBars = null;
            _particles = null;
            _particleElements = null;
            _circleElements = null;
            _scopeLine = null;
            _vuBlocks = null;
            VisualizerCanvas.Children.Clear();
        }

        private bool _vizCycleActive;
        private int _vizCycleIndex;

        private void StartVisualizerCycle()
        {
            _vizCycleActive = true;
            _vizCycleIndex = 0;

            // Parse the custom cycle list from settings
            _vizCycleList = new List<int>();
            if (!string.IsNullOrWhiteSpace(ThemeManager.VisualizerCycleList))
            {
                foreach (var part in ThemeManager.VisualizerCycleList.Split(',', StringSplitOptions.RemoveEmptyEntries))
                {
                    if (int.TryParse(part.Trim(), out int idx) && idx >= 0 && idx < VizStyleCount)
                        _vizCycleList.Add(idx);
                }
            }
            // Fallback: if empty or nothing valid parsed, use all styles
            if (_vizCycleList.Count == 0)
            {
                for (int i = 0; i < VizStyleCount; i++) _vizCycleList.Add(i);
            }

            if (_vizCycleTimer == null)
            {
                _vizCycleTimer = new System.Windows.Threading.DispatcherTimer();
                _vizCycleTimer.Tick += VizCycleTimer_Tick;
            }
            _vizCycleTimer.Interval = TimeSpan.FromSeconds(ThemeManager.VisualizerCycleSpeed);
            _vizCycleTimer.Start();

            // Apply first style immediately
            ApplyVisualizerStyle(_vizCycleList[0]);
            UpdateVisualizerStyleText();
        }

        private void StopVisualizerCycle()
        {
            _vizCycleActive = false;
            _vizCycleTimer?.Stop();
        }

        private void VizCycleTimer_Tick(object? sender, EventArgs e)
        {
            if (_vizCycleList == null || _vizCycleList.Count == 0) return;
            _vizCycleIndex = (_vizCycleIndex + 1) % _vizCycleList.Count;
            ApplyVisualizerStyle(_vizCycleList[_vizCycleIndex]);
        }

        private void UpdateVisualizerStyleText()
        {
            if (VisualizerStyleText != null)
            {
                if (_vizCycleActive)
                {
                    VisualizerStyleText.Text = "Cycle";
                }
                else
                {
                    VisualizerStyleText.Text = _visualizerStyle < _vizStyleNames.Length
                        ? _vizStyleNames[_visualizerStyle]
                        : "Bars";
                }
            }
        }

        // ═══════════════════════════════════════════
        //  Spectrogram Scale / Channel / End-Zoom
        // ═══════════════════════════════════════════

        private void SpectrogramScale_Click(object sender, RoutedEventArgs e)
        {
            _spectrogramLinearScale = !_spectrogramLinearScale;
            ThemeManager.SpectrogramLinearScale = _spectrogramLinearScale;
            ThemeManager.SavePlayOptions();
            UpdateSpectrogramScaleText();
            RefreshSpectrogram();
        }

        private void SpectrogramChannel_Click(object sender, RoutedEventArgs e)
        {
            _spectrogramChannel = _spectrogramChannel == SpectrogramChannel.Mono
                ? SpectrogramChannel.Difference
                : SpectrogramChannel.Mono;
            ThemeManager.SpectrogramDifferenceChannel = _spectrogramChannel == SpectrogramChannel.Difference;
            ThemeManager.SavePlayOptions();
            UpdateSpectrogramChannelText();
            RefreshSpectrogram();
        }

        private void JumpToEnd_Click(object sender, RoutedEventArgs e)
        {
            _spectrogramEndZoom = !_spectrogramEndZoom;
            _spectrogramZoomLevel = 1.0;
            UpdateZoomButton();
            UpdateJumpToEndText();
            RefreshSpectrogram();
        }

        private void SpectrogramScrollViewer_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (SpectrogramImage.Source == null) return;

            e.Handled = true;

            double oldZoom = _spectrogramZoomLevel;

            if (e.Delta > 0)
                _spectrogramZoomLevel = Math.Min(_spectrogramZoomLevel * 1.25, 20.0);
            else
                _spectrogramZoomLevel = Math.Max(_spectrogramZoomLevel / 1.25, 1.0);

            if (Math.Abs(oldZoom - _spectrogramZoomLevel) < 0.01) return;

            double viewportWidth = SpectrogramScrollViewer.ViewportWidth;
            if (viewportWidth <= 0) return;

            double oldWidth = SpectrogramImage.ActualWidth > 0 ? SpectrogramImage.ActualWidth : viewportWidth;
            double newWidth = viewportWidth * _spectrogramZoomLevel;

            // Keep content under mouse cursor stable
            var mousePos = e.GetPosition(SpectrogramScrollViewer);
            double oldOffset = SpectrogramScrollViewer.HorizontalOffset;
            double mouseRelative = (oldOffset + mousePos.X) / oldWidth;

            SpectrogramImage.Width = newWidth;
            SpectrogramImage.Height = SpectrogramScrollViewer.ViewportHeight;

            SpectrogramScrollViewer.UpdateLayout();
            double newOffset = mouseRelative * newWidth - mousePos.X;
            SpectrogramScrollViewer.ScrollToHorizontalOffset(Math.Max(0, newOffset));
            UpdateZoomButton();
        }

        private void SpectrogramScrollViewer_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (SpectrogramScrollViewer == null || SpectrogramImage.Source == null) return;
            double viewportWidth = SpectrogramScrollViewer.ActualWidth;
            double viewportHeight = SpectrogramScrollViewer.ActualHeight;
            if (viewportWidth <= 0 || viewportHeight <= 0) return;

            SpectrogramImage.Width = viewportWidth * _spectrogramZoomLevel;
            SpectrogramImage.Height = viewportHeight;
        }

        private void ResetZoom_Click(object sender, RoutedEventArgs e)
        {
            _spectrogramZoomLevel = 1.0;
            UpdateZoomButton();
            if (SpectrogramScrollViewer != null && SpectrogramImage.Source != null)
            {
                SpectrogramImage.Width = SpectrogramScrollViewer.ActualWidth;
                SpectrogramImage.Height = SpectrogramScrollViewer.ActualHeight;
                SpectrogramScrollViewer.ScrollToHorizontalOffset(0);
            }
        }

        private void UpdateZoomButton()
        {
            if (BtnResetZoom == null) return;
            if (_spectrogramZoomLevel > 1.01)
            {
                BtnResetZoom.Visibility = Visibility.Visible;
                ZoomLevelText.Text = $"{_spectrogramZoomLevel:F1}x";
            }
            else
            {
                BtnResetZoom.Visibility = Visibility.Collapsed;
            }
        }

        private void UpdateSpectrogramScaleText()
        {
            if (SpectrogramScaleText != null)
                SpectrogramScaleText.Text = _spectrogramLinearScale ? "Log" : "Linear";
        }

        private void UpdateSpectrogramChannelText()
        {
            if (SpectrogramChannelText != null)
                SpectrogramChannelText.Text = _spectrogramChannel == SpectrogramChannel.Mono ? "L-R" : "Mono";
        }

        private void UpdateJumpToEndText()
        {
            if (JumpToEndText != null)
                JumpToEndText.Text = _spectrogramEndZoom ? "Full" : "End";
        }

        /// <summary>
        /// Re-generates and displays the spectrogram for the currently selected file
        /// using the current display options (scale, channel, zoom).
        /// </summary>
        private async void RefreshSpectrogram()
        {
            try
            {
            if (_currentSpectrogramFile is not AudioFileInfo file) return;
            if (file.Status == AudioStatus.Corrupt) return;

            _spectrogramCts?.Cancel();
            _spectrogramCts = new CancellationTokenSource();
            var token = _spectrogramCts.Token;

            try
            {
                string spectRefreshKey = $"{file.FilePath}|{_spectrogramLinearScale}|{_spectrogramChannel}|{ThemeManager.SpectrogramMagmaColormap}|{ThemeManager.SpectrogramHiFiMode}|{_spectrogramEndZoom}";
                BitmapSource? bitmap;
                if (_spectrogramCache.TryGetValue(spectRefreshKey, out var cachedRefresh))
                {
                    bitmap = cachedRefresh;
                    _spectrogramCacheLru.Remove(spectRefreshKey);
                    _spectrogramCacheLru.AddLast(spectRefreshKey);
                }
                else
                {
                    await _spectrogramSemaphore.WaitAsync(token);
                    try
                    {
                        bitmap = await Task.Run(() =>
                            SpectrogramGenerator.Generate(file.FilePath, 1200, 400,
                                _spectrogramLinearScale, _spectrogramChannel,
                                _spectrogramEndZoom ? 10 : 0,
                                ThemeManager.SpectrogramHiFiMode,
                                ThemeManager.SpectrogramMagmaColormap,
                                token), token);
                    }
                    finally
                    {
                        _spectrogramSemaphore.Release();
                    }
                    if (bitmap != null)
                    {
                        if (_spectrogramCacheLru.Count >= SpectrogramCacheMaxEntries)
                        {
                            string? oldest = _spectrogramCacheLru.First?.Value;
                            if (oldest != null) { _spectrogramCache.Remove(oldest); _spectrogramCacheLru.RemoveFirst(); }
                        }
                        _spectrogramCache[spectRefreshKey] = bitmap;
                        _spectrogramCacheLru.AddLast(spectRefreshKey);
                    }
                }

                if (token.IsCancellationRequested) return;

                if (bitmap != null)
                {
                    SpectrogramImage.Source = bitmap;

                    // Apply current zoom to refreshed image
                    if (SpectrogramScrollViewer != null)
                    {
                        double vw = SpectrogramScrollViewer.ActualWidth;
                        double vh = SpectrogramScrollViewer.ActualHeight;
                        if (vw > 0 && vh > 0)
                        {
                            SpectrogramImage.Width = vw * _spectrogramZoomLevel;
                            SpectrogramImage.Height = vh;
                            SpectrogramScrollViewer.ScrollToHorizontalOffset(0);
                        }
                    }

                    int nyquist = file.SampleRate / 2;
                    if (_spectrogramLinearScale)
                    {
                        FreqLabelTop.Text = $"{nyquist:N0} Hz";
                        FreqLabelUpperMid.Text = $"{(int)(nyquist * 0.75):N0} Hz";
                        FreqLabelMid.Text = $"{(int)(nyquist * 0.50):N0} Hz";
                        FreqLabelLowerMid.Text = $"{(int)(nyquist * 0.25):N0} Hz";
                        FreqLabelBot.Text = "0 Hz";
                    }
                    else
                    {
                        double logMin = Math.Log10(20.0);
                        double logMax = Math.Log10(nyquist);
                        double logRange = logMax - logMin;

                        FreqLabelTop.Text = $"{nyquist:N0} Hz";
                        FreqLabelUpperMid.Text = $"{(int)Math.Pow(10, logMin + 0.75 * logRange):N0} Hz";
                        FreqLabelMid.Text = $"{(int)Math.Pow(10, logMin + 0.5 * logRange):N0} Hz";
                        FreqLabelLowerMid.Text = $"{(int)Math.Pow(10, logMin + 0.25 * logRange):N0} Hz";
                        FreqLabelBot.Text = "20 Hz";
                    }

                    SpectrogramTitle.Text = BuildSpectrogramTitle(file);
                }
            }
            catch (OperationCanceledException) { }
            catch { }
            }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[RefreshSpectrogram] {ex}"); }
        }

        private void StartVisualizer()
        {
            // Reduce Motion / Battery Saver can suppress the visualizer entirely.
            if (!AnimationPolicy.IsMotionAllowed(AnimationArea.Visualizer))
                return;

            if (_npVisible && !_npVizRedirected)
                return;

            if (!_npVizRedirected &&
                _miniPlayerWindow?.IsVisible == true &&
                _miniPlayerWindow.IsMiniVisualizerActive)
            {
                return;
            }

            if (!_visualizerActive)
            {
                _visualizerActive = true;
                CompositionTarget.Rendering += Visualizer_Tick;
            }
        }

        private void StopVisualizer()
        {
            if (_visualizerActive)
            {
                _visualizerActive = false;
                CompositionTarget.Rendering -= Visualizer_Tick;
                VisualizerCanvas.Children.Clear();
                _vizBars = null;
                _particles = null;
                _particleElements = null;
                _particleBrushes = null;
                _circleElements = null;
                _circleBrushes = null;
                _scopeLine = null;
                _vuBlocks = null;
                _vuBrushes = null;
                StopVisualizerCycle();
            }
        }

        /// <summary>
        /// Re-evaluate whether the main visualizer should be running after a
        /// Reduce Motion / Battery Saver change. Only acts when visualizer mode is
        /// the active spectrogram view; the tick itself self-stops if disallowed.
        /// </summary>
        private void ApplyVisualizerPerformancePolicy()
        {
            if (!_visualizerMode)
                return;
            if (AnimationPolicy.IsMotionAllowed(AnimationArea.Visualizer))
                StartVisualizer();
            else
                StopVisualizer();

            // The mini player owns its own visualizer loop — refresh it too.
            _miniPlayerWindow?.ApplyVisualizerPerformancePolicy();
        }

        private double[] _vizSmoothed = new double[64];
        private System.Windows.Shapes.Rectangle[]? _vizBars;
        private SolidColorBrush[]? _vizBrushes;
        private TimeSpan _lastVizRenderTime = TimeSpan.Zero;

        // Pre-allocated buffers to avoid per-frame GC pressure
        private const int VizFftSize = 2048;
        private const int VizNumBars = 64;
        private readonly double[] _vizReal = new double[VizFftSize];
        private readonly double[] _vizImag = new double[VizFftSize];
        private readonly double[] _vizMags = new double[VizFftSize / 2];
        private readonly double[] _vizBarValues = new double[VizNumBars];

        // Particle Fountain system
        private struct Particle
        {
            public double X, Y, VelocityX, VelocityY;
            public double Life, MaxLife;
            public int Band;
        }
        private List<Particle>? _particles;
        private System.Windows.Shapes.Ellipse[]? _particleElements;
        private SolidColorBrush[]? _particleBrushes;
        private const int MaxParticles = 300;

        // Circle Rings system
        private System.Windows.Shapes.Line[]? _circleElements;
        private SolidColorBrush[]? _circleBrushes;

        // Oscilloscope system
        private System.Windows.Shapes.Polyline? _scopeLine;
        private System.Windows.Media.PointCollection? _scopePoints; // reused across frames (see RenderOscilloscope)

        // VU Meter system
        private System.Windows.Shapes.Rectangle[]? _vuBlocks;
        private SolidColorBrush[]? _vuBrushes;
        private const int VuColumns = 32;   // number of frequency columns
        private const int VuRows = 20;      // blocks per column

        // Visualizer cycle mode
        private System.Windows.Threading.DispatcherTimer? _vizCycleTimer;
        private List<int>? _vizCycleList;  // which styles to cycle through

        private void Visualizer_Tick(object? sender, EventArgs e)
        {
            // Reduce Motion / Battery Saver turned off mid-run — stop the loop.
            if (!AnimationPolicy.IsMotionAllowed(AnimationArea.Visualizer))
            {
                StopVisualizer();
                return;
            }

            // Skip rendering when window is minimized or hidden in tray
            if (WindowState == WindowState.Minimized || !IsVisible)
                return;

            if (!_player.IsPlaying && !_player.IsPaused)
            {
                if (VizTarget.Children.Count > 0)
                    VizTarget.Children.Clear();
                _vizBars = null;
                _particles = null;
                _particleElements = null;
                _circleElements = null;
                _scopeLine = null;
               
                _vuBlocks = null;
                return;
            }

            // Use precise rendering time for frame limiting (~60fps normally, ~30fps while scanning)
            if (e is RenderingEventArgs re)
            {
                int minMs = _isAnalyzing ? 33 : 16;
                if ((re.RenderingTime - _lastVizRenderTime).TotalMilliseconds < minMs) return;
                _lastVizRenderTime = re.RenderingTime;
            }

            double width = VizTarget.ActualWidth;
            double height = VizTarget.ActualHeight;
            if (width < 10 || height < 10) return;

            int numBars = VizNumBars;

            // Get recent samples and run FFT
            var vizSnapshot = _player.GetVisualizerSnapshot(4096);
            float[] samples = vizSnapshot.Samples;
            int fftSize = VizFftSize;

            // Clear and fill pre-allocated FFT buffers
            Array.Clear(_vizReal);
            Array.Clear(_vizImag);

            // Use the most recent fftSize samples from the captured buffer
            int offset = Math.Max(0, samples.Length - fftSize);
            for (int i = 0; i < fftSize && (offset + i) < samples.Length; i++)
            {
                double w = 0.5 * (1.0 - Math.Cos(2.0 * Math.PI * i / (fftSize - 1)));
                _vizReal[i] = samples[offset + i] * w;
            }

            // Compensate for volume when VisualizerFullVolume is enabled
            float capturedVolume = vizSnapshot.UserVolume;
            if (ThemeManager.VisualizerFullVolume && capturedVolume > 0.01f && capturedVolume < 1f)
            {
                double gain = 1.0 / capturedVolume;
                for (int i = 0; i < fftSize; i++)
                    _vizReal[i] *= gain;
            }

            VisualizerFFT(_vizReal, _vizImag);

            int specLen = fftSize / 2;
            double halfN = fftSize / 2.0;
            for (int i = 0; i < specLen; i++)
            {
                double mag = Math.Sqrt(_vizReal[i] * _vizReal[i] + _vizImag[i] * _vizImag[i]) / halfN;
                _vizMags[i] = mag > 1e-10 ? 20.0 * Math.Log10(mag) : -100;
            }

            // Group into logarithmic frequency bands
            int sr = _player.VisualizerSampleRate > 0 ? _player.VisualizerSampleRate : 44100;
            double logMin = Math.Log10(20);
            double logMax = Math.Log10(sr / 2.0);

            for (int b = 0; b < numBars; b++)
            {
                double freqLow = Math.Pow(10, logMin + (logMax - logMin) * b / numBars);
                double freqHigh = Math.Pow(10, logMin + (logMax - logMin) * (b + 1) / numBars);
                int binLow = Math.Clamp((int)(freqLow / (sr / 2.0) * specLen), 0, specLen - 1);
                int binHigh = Math.Clamp((int)(freqHigh / (sr / 2.0) * specLen), binLow, specLen - 1);

                double sum = 0;
                int count = 0;
                for (int i = binLow; i <= binHigh; i++) { sum += _vizMags[i]; count++; }
                _vizBarValues[b] = count > 0 ? sum / count : -100;
            }

            // Normalize using fixed absolute dB scale (0 dB = full scale after FFT normalization)
            double range = 60;
            double minDb = -60; // -60 dBFS = silence floor
            for (int b = 0; b < numBars; b++)
                _vizBarValues[b] = Math.Clamp((_vizBarValues[b] - minDb) / range, 0, 1);

            // Smooth for visual appeal: attack fast, decay slow
            if (_vizSmoothed.Length != numBars) _vizSmoothed = new double[numBars];
            for (int b = 0; b < numBars; b++)
            {
                if (_vizBarValues[b] > _vizSmoothed[b])
                    _vizSmoothed[b] = _vizBarValues[b] * 0.7 + _vizSmoothed[b] * 0.3;  // fast attack
                else
                    _vizSmoothed[b] = _vizBarValues[b] * 0.15 + _vizSmoothed[b] * 0.85; // slow decay
            }

            // Dispatch to the active style renderer
            switch (_visualizerStyle)
            {
                case 1:
                    RenderMirroredBars(width, height, numBars);
                    break;
                case 2:
                    RenderParticleFountain(width, height, numBars);
                    break;
                case 3:
                    RenderCircleRings(width, height, numBars);
                    break;
                case 4:
                    RenderOscilloscope(width, height);
                    break;
                case 5:
                    RenderVuMeter(width, height, numBars);
                    break;
                default:
                    RenderClassicBars(width, height, numBars);
                    break;
            }

            RenderPlaybarAnim();
        }

        // ── Classic Bars renderer ──
        private void RenderClassicBars(double width, double height, int numBars)
        {
            var vizColors = ThemeManager.GetVisualizerColors();
            var gradient = vizColors.ProgressGradient;
            double barWidth = width / numBars * 0.8;
            double gap = width / numBars * 0.2;

            // Ensure we're in bars mode (clean up other styles)
            if (_particleElements != null || _circleElements != null || _scopeLine != null
                || _vuBlocks != null)
            {
                VizTarget.Children.Clear();
                _particleElements = null;
                _particles = null;
                _circleElements = null;
                _scopeLine = null;
               
                _vuBlocks = null;
                _vizBars = null;
            }

            if (_vizBars == null || _vizBars.Length != numBars)
            {
                VizTarget.Children.Clear();
                _vizBars = new System.Windows.Shapes.Rectangle[numBars];
                _vizBrushes = new SolidColorBrush[numBars];
                for (int b = 0; b < numBars; b++)
                {
                    _vizBrushes[b] = new SolidColorBrush(gradient[0]);
                    _vizBars[b] = new System.Windows.Shapes.Rectangle
                    {
                        Width = barWidth,
                        Height = 2,
                        Fill = _vizBrushes[b],
                        RadiusX = 2,
                        RadiusY = 2,
                        IsHitTestVisible = false
                    };
                    Canvas.SetLeft(_vizBars[b], b * (barWidth + gap) + gap / 2);
                    Canvas.SetTop(_vizBars[b], height - 2);
                    VizTarget.Children.Add(_vizBars[b]);
                }
            }

            bool rainbow = ThemeManager.VisualizerRainbowEnabled;
            double time = Environment.TickCount64 / 1000.0;

            for (int b = 0; b < numBars; b++)
            {
                double barHeight = _vizSmoothed[b] * height * 0.92;
                if (barHeight < 2) barHeight = 2;

                _vizBars[b].Width = barWidth;
                _vizBars[b].Height = barHeight;
                Canvas.SetLeft(_vizBars[b], b * (barWidth + gap) + gap / 2);
                Canvas.SetTop(_vizBars[b], height - barHeight);

                _vizBrushes![b].Color = GetBarColor(b, numBars, _vizSmoothed[b], gradient, rainbow, time);
            }
        }

        // ── Mirrored Bars renderer ──
        private System.Windows.Shapes.Rectangle[]? _vizMirrorBars;
        private SolidColorBrush[]? _vizMirrorBrushes;

        private void RenderMirroredBars(double width, double height, int numBars)
        {
            var vizColors = ThemeManager.GetVisualizerColors();
            var gradient = vizColors.ProgressGradient;
            double barWidth = width / numBars * 0.8;
            double gap = width / numBars * 0.2;
            double centerY = height / 2.0;

            // Ensure we're in mirrored mode (clean up other styles)
            if (_particleElements != null || _circleElements != null || _scopeLine != null
                || _vuBlocks != null)
            {
                VizTarget.Children.Clear();
                _particleElements = null;
                _particles = null;
                _circleElements = null;
                _scopeLine = null;
                _vuBlocks = null;
                _vizBars = null;
                _vizMirrorBars = null;
            }

            // Need 2x bars (top half + bottom half)
            if (_vizBars == null || _vizBars.Length != numBars || _vizMirrorBars == null)
            {
                VizTarget.Children.Clear();
                _vizBars = new System.Windows.Shapes.Rectangle[numBars];
                _vizMirrorBars = new System.Windows.Shapes.Rectangle[numBars];
                _vizBrushes = new SolidColorBrush[numBars];
                _vizMirrorBrushes = new SolidColorBrush[numBars];
                for (int b = 0; b < numBars; b++)
                {
                    _vizBrushes[b] = new SolidColorBrush(gradient[0]);
                    _vizMirrorBrushes[b] = new SolidColorBrush(gradient[0]);

                    // Top bar (grows upward from center)
                    _vizBars[b] = new System.Windows.Shapes.Rectangle
                    {
                        Width = barWidth,
                        Height = 2,
                        Fill = _vizBrushes[b],
                        RadiusX = 2,
                        RadiusY = 2,
                        IsHitTestVisible = false
                    };
                    Canvas.SetLeft(_vizBars[b], b * (barWidth + gap) + gap / 2);
                    Canvas.SetTop(_vizBars[b], centerY - 1);
                    VizTarget.Children.Add(_vizBars[b]);

                    // Bottom bar (grows downward from center, slightly dimmer)
                    _vizMirrorBars[b] = new System.Windows.Shapes.Rectangle
                    {
                        Width = barWidth,
                        Height = 2,
                        Fill = _vizMirrorBrushes[b],
                        RadiusX = 2,
                        RadiusY = 2,
                        Opacity = 0.6,
                        IsHitTestVisible = false
                    };
                    Canvas.SetLeft(_vizMirrorBars[b], b * (barWidth + gap) + gap / 2);
                    Canvas.SetTop(_vizMirrorBars[b], centerY);
                    VizTarget.Children.Add(_vizMirrorBars[b]);
                }
            }

            bool rainbow = ThemeManager.VisualizerRainbowEnabled;
            double time = Environment.TickCount64 / 1000.0;

            for (int b = 0; b < numBars; b++)
            {
                double barHeight = _vizSmoothed[b] * centerY * 0.90;
                if (barHeight < 2) barHeight = 2;

                // Top half — grows upward from center
                _vizBars[b].Width = barWidth;
                _vizBars[b].Height = barHeight;
                Canvas.SetLeft(_vizBars[b], b * (barWidth + gap) + gap / 2);
                Canvas.SetTop(_vizBars[b], centerY - barHeight);

                // Bottom half — mirrors downward from center
                _vizMirrorBars![b].Width = barWidth;
                _vizMirrorBars[b].Height = barHeight;
                Canvas.SetLeft(_vizMirrorBars[b], b * (barWidth + gap) + gap / 2);
                Canvas.SetTop(_vizMirrorBars[b], centerY);

                var color = GetBarColor(b, numBars, _vizSmoothed[b], gradient, rainbow, time);
                _vizBrushes![b].Color = color;
                _vizMirrorBrushes![b].Color = color;
            }
        }

        // ── Particle Fountain renderer ──
        private readonly Random _particleRng = new();

        private void RenderParticleFountain(double width, double height, int numBars)
        {
            var vizColors = ThemeManager.GetVisualizerColors();
            var gradient = vizColors.ProgressGradient;
            bool rainbow = ThemeManager.VisualizerRainbowEnabled;
            double time = Environment.TickCount64 / 1000.0;

            // Ensure we're in particle mode (clean up other styles)
            if (_vizBars != null || _circleElements != null || _scopeLine != null
                || _vuBlocks != null)
            {
                VizTarget.Children.Clear();
                _vizBars = null;
                _vizMirrorBars = null;
                _circleElements = null;
                _scopeLine = null;
                _vuBlocks = null;
                _particleElements = null;
                _particles = null;
            }

            // Initialize particle pool
            if (_particles == null)
            {
                _particles = new List<Particle>(MaxParticles);
                _particleElements = new System.Windows.Shapes.Ellipse[MaxParticles];
                _particleBrushes = new SolidColorBrush[MaxParticles];
                for (int i = 0; i < MaxParticles; i++)
                {
                    _particleBrushes[i] = new SolidColorBrush(Colors.White);
                    _particleElements[i] = new System.Windows.Shapes.Ellipse
                    {
                        Width = 4,
                        Height = 4,
                        Fill = _particleBrushes[i],
                        IsHitTestVisible = false,
                        Visibility = Visibility.Collapsed
                    };
                    VizTarget.Children.Add(_particleElements[i]);
                }
            }

            double dt = 1.0 / 60.0; // ~16ms frame time

            // Spawn new particles based on frequency energy
            // Use fewer "spawn bands" (8) to group the 64 bars
            int spawnBands = 8;
            for (int sb = 0; sb < spawnBands; sb++)
            {
                int barStart = sb * numBars / spawnBands;
                int barEnd = (sb + 1) * numBars / spawnBands;
                double bandEnergy = 0;
                for (int b = barStart; b < barEnd; b++)
                    bandEnergy = Math.Max(bandEnergy, _vizSmoothed[b]);

                // Spawn probability proportional to energy
                if (bandEnergy > 0.15 && _particleRng.NextDouble() < bandEnergy * 0.8)
                {
                    if (_particles.Count < MaxParticles)
                    {
                        double spawnX = width * ((sb + 0.5) / spawnBands) + (_particleRng.NextDouble() - 0.5) * (width / spawnBands * 0.6);
                        _particles.Add(new Particle
                        {
                            X = spawnX,
                            Y = height,
                            VelocityX = (_particleRng.NextDouble() - 0.5) * 25,
                            VelocityY = -(40 + bandEnergy * 160 + _particleRng.NextDouble() * 30),
                            Life = 0,
                            MaxLife = 1.8 + bandEnergy * 1.5 + _particleRng.NextDouble() * 0.8,
                            Band = (barStart + barEnd) / 2
                        });
                    }
                }
            }

            // Update and render particles
            for (int i = _particles.Count - 1; i >= 0; i--)
            {
                var p = _particles[i];
                p.Life += dt;

                if (p.Life >= p.MaxLife || p.Y > height + 20)
                {
                    _particles.RemoveAt(i);
                    continue;
                }

                // Physics: gravity pulls down, air resistance slows horizontal drift
                p.VelocityY += 80 * dt; // gentler gravity — particles arc rather than plummet
                p.VelocityX *= 0.992;   // slight air drag on horizontal
                p.VelocityY *= 0.998;   // slight air drag on vertical too
                p.X += p.VelocityX * dt;
                p.Y += p.VelocityY * dt;

                _particles[i] = p;
            }

            // Hide all particle elements first, then assign visible ones
            for (int i = 0; i < MaxParticles; i++)
                _particleElements![i].Visibility = Visibility.Collapsed;

            for (int i = 0; i < _particles.Count && i < MaxParticles; i++)
            {
                var p = _particles[i];
                double lifeFrac = p.Life / p.MaxLife;
                double alpha = lifeFrac < 0.15 ? lifeFrac / 0.15 : Math.Max(0, 1.0 - (lifeFrac - 0.15) / 0.85); // gentle fade in, slow fade out
                alpha = Math.Clamp(alpha, 0, 1);

                double bandNorm = (double)p.Band / numBars;
                double size = 4 + (1 - lifeFrac) * 4; // starts at 8px, shrinks to 4px

                Color color;
                if (_npVisible && _npColorMatchEnabled && _npVizColorPrimary != default)
                {
                    color = GetBarColor(p.Band, numBars, bandNorm, gradient, false, time);
                }
                else if (rainbow)
                {
                    double hue = (bandNorm + time * 0.15) % 1.0;
                    color = HsvToColor(hue * 360, 0.9, 0.6 + alpha * 0.4);
                }
                else
                {
                    double t = bandNorm;
                    if (t < 0.5)
                    {
                        double seg = t / 0.5;
                        color = Color.FromArgb(255,
                            (byte)(gradient[0].R + (gradient[1].R - gradient[0].R) * seg),
                            (byte)(gradient[0].G + (gradient[1].G - gradient[0].G) * seg),
                            (byte)(gradient[0].B + (gradient[1].B - gradient[0].B) * seg));
                    }
                    else
                    {
                        double seg = (t - 0.5) / 0.5;
                        color = Color.FromArgb(255,
                            (byte)(gradient[1].R + (gradient[2].R - gradient[1].R) * seg),
                            (byte)(gradient[1].G + (gradient[2].G - gradient[1].G) * seg),
                            (byte)(gradient[1].B + (gradient[2].B - gradient[1].B) * seg));
                    }
                }

                color.A = (byte)(alpha * 255);
                _particleBrushes![i].Color = color;
                _particleElements![i].Width = size;
                _particleElements[i].Height = size;
                _particleElements[i].Visibility = Visibility.Visible;
                Canvas.SetLeft(_particleElements[i], p.X - size / 2);
                Canvas.SetTop(_particleElements[i], p.Y - size / 2);
            }
        }

        // ── Circle Rings renderer ──
        // 5 circles, each assigned to a different frequency band with bars radiating
        // outward around the full 360° perimeter of each circle
        private const int CircleRingCount = 5;
        private int _lastCircleTotalLines; // track element count for reallocation

        private void RenderCircleRings(double width, double height, int numBars)
        {
            var vizColors = ThemeManager.GetVisualizerColors();
            var gradient = vizColors.ProgressGradient;
            bool rainbow = ThemeManager.VisualizerRainbowEnabled;
            double time = Environment.TickCount64 / 1000.0;

            // Dynamic layout: compute circle spacing and radius from canvas
            double margin = width * 0.06;
            double availableWidth = width - 2 * margin;
            double spacing = availableWidth / CircleRingCount;
            double baseRadius = Math.Min(spacing * 0.28, height * 0.22);

            // Scale bars per circle: ~1 bar per 5° of arc, clamped 24–72
            int barsPerCircle = Math.Clamp((int)(2 * Math.PI * baseRadius / 5.0), 24, 72);
            int totalLines = CircleRingCount * barsPerCircle;

            // Clean up other mode elements
            if (_particleElements != null || _vizBars != null || _scopeLine != null)
            {
                VizTarget.Children.Clear();
                _particleElements = null;
                _particles = null;
                _vizBars = null;
                _vizMirrorBars = null;
                _scopeLine = null;
                _circleElements = null;
            }

            // Initialize / reallocate circle bar elements when count changes
            if (_circleElements == null || _circleElements.Length != totalLines)
            {
                VizTarget.Children.Clear();
                _circleElements = new System.Windows.Shapes.Line[totalLines];
                _circleBrushes = new SolidColorBrush[totalLines];
                for (int i = 0; i < totalLines; i++)
                {
                    _circleBrushes[i] = new SolidColorBrush(Colors.White);
                    _circleElements[i] = new System.Windows.Shapes.Line
                    {
                        Stroke = _circleBrushes[i],
                        StrokeThickness = 2,
                        IsHitTestVisible = false
                    };
                    VizTarget.Children.Add(_circleElements[i]);
                }
                _lastCircleTotalLines = totalLines;
            }

            double centerY = height / 2.0;

            // Frequency band ranges per circle (sub-bass, bass, low-mid, high-mid, treble)
            int barsPerBand = numBars / CircleRingCount;

            // Bar width: thinner to avoid overlap around the circle
            double barWidth = Math.Max(1.5, 2 * Math.PI * baseRadius / barsPerCircle * 0.55);

            for (int c = 0; c < CircleRingCount; c++)
            {
                double cx = margin + spacing * (c + 0.5);
                double cy = centerY;

                // Get energy for this circle's frequency band
                int bandStart = c * barsPerBand;
                int bandEnd = Math.Min(bandStart + barsPerBand, numBars);

                for (int s = 0; s < barsPerCircle; s++)
                {
                    int lineIdx = c * barsPerCircle + s;

                    // Distribute bars evenly around the full 360° circle
                    double angle = (2.0 * Math.PI * s) / barsPerCircle - Math.PI / 2; // start from top

                    // Map this bar to a frequency within this circle's band
                    int barIdx = bandStart + (s * (bandEnd - bandStart)) / barsPerCircle;
                    barIdx = Math.Clamp(barIdx, 0, numBars - 1);
                    double energy = _vizSmoothed[barIdx];

                    // Bar radiates outward from the circle perimeter
                    double barHeight = energy * baseRadius * 1.0;
                    if (barHeight < 1) barHeight = 1;

                    double cosA = Math.Cos(angle);
                    double sinA = Math.Sin(angle);

                    // Inner point: on the circle perimeter
                    double x1 = cx + baseRadius * cosA;
                    double y1 = cy + baseRadius * sinA;

                    // Outer point: extends outward by barHeight
                    double x2 = cx + (baseRadius + barHeight) * cosA;
                    double y2 = cy + (baseRadius + barHeight) * sinA;

                    _circleElements[lineIdx].X1 = x1;
                    _circleElements[lineIdx].Y1 = y1;
                    _circleElements[lineIdx].X2 = x2;
                    _circleElements[lineIdx].Y2 = y2;
                    _circleElements[lineIdx].StrokeThickness = barWidth;

                    // Color: each circle gets a band-based color
                    double bandNorm = (double)c / CircleRingCount;
                    Color color;
                    if (rainbow)
                    {
                        double hue = (bandNorm + time * 0.15 + (double)s / barsPerCircle * 0.3) % 1.0;
                        color = HsvToColor(hue * 360, 0.85, 0.5 + energy * 0.5);
                    }
                    else
                    {
                        color = GetBarColor(c, CircleRingCount, energy, gradient, false, time);
                    }
                    _circleBrushes![lineIdx].Color = color;
                }
            }
        }

        // ── Oscilloscope renderer ──
        // Draws the raw audio waveform as a continuous polyline
        private void RenderOscilloscope(double width, double height)
        {
            var vizColors = ThemeManager.GetVisualizerColors();
            var gradient = vizColors.ProgressGradient;
            bool rainbow = ThemeManager.VisualizerRainbowEnabled;
            double time = Environment.TickCount64 / 1000.0;

            // Clean up other mode elements
            if (_particleElements != null || _vizBars != null || _circleElements != null
                || _vuBlocks != null)
            {
                VizTarget.Children.Clear();
                _particleElements = null;
                _particles = null;
                _vizBars = null;
                _vizMirrorBars = null;
                _circleElements = null;
                _vuBlocks = null;
                _scopeLine = null;
            }

            // Get raw time-domain samples (plus the captured player volume for Full Volume scaling).
            var scopeSnapshot = _player.GetVisualizerSnapshot(VizFftSize);
            float[] vizData = scopeSnapshot.Samples;
            if (vizData.Length == 0) return;

            // Full Volume: scale the trace by the inverse of the player volume so the scope stays
            // lively when the volume is turned down — same compensation the bars/circles use.
            double scopeGain = 1.0;
            float scopeVolume = scopeSnapshot.UserVolume;
            if (ThemeManager.VisualizerFullVolume && scopeVolume > 0.01f && scopeVolume < 1f)
                scopeGain = 1.0 / scopeVolume;

            // Exactly ONE scope polyline. If we don't have a live line on the canvas, strip any
            // stray polylines first (defends against the duplicate / multi-coloured traces) and
            // create a single fresh one.
            if (_scopeLine == null || !VizTarget.Children.Contains(_scopeLine))
            {
                for (int i = VizTarget.Children.Count - 1; i >= 0; i--)
                    if (VizTarget.Children[i] is System.Windows.Shapes.Polyline)
                        VizTarget.Children.RemoveAt(i);

                _scopeLine = new System.Windows.Shapes.Polyline
                {
                    Stroke = new SolidColorBrush(gradient.Length > 1 ? gradient[1] : Colors.Lime),
                    StrokeThickness = 1.5,
                    IsHitTestVisible = false
                };
                VizTarget.Children.Add(_scopeLine);
                _scopePoints = null; // (re)build the reusable point buffer below
            }

            // One steady colour for the whole trace — no per-frame multi-colour churn. Only the
            // explicitly-selected Rainbow theme animates the hue; otherwise it's the colormatch
            // accent (when ColorMatch is on) or the theme accent, held constant.
            Color lineColor;
            if (_npVisible && _npColorMatchEnabled && _npVizColorPrimary != default)
            {
                lineColor = BoostVizColor(_npVizColorPrimary, 80);
            }
            else if (rainbow)
            {
                double hue = (time * 0.2) % 1.0;
                lineColor = HsvToColor(hue * 360, 0.8, 0.9);
            }
            else
            {
                lineColor = gradient.Length > 1 ? gradient[1] : Colors.Lime;
            }
            ((SolidColorBrush)_scopeLine.Stroke).Color = lineColor;

            // Number of points to draw across the width
            int pointCount = Math.Min((int)width, vizData.Length);
            if (pointCount < 2) return;

            double centerY = height / 2.0;
            double amplitude = height * 0.42; // leave some margin

            // Reuse one PointCollection across frames; only rebuild when the point count changes
            // (i.e. the canvas was resized). Per-index updates within this synchronous render pass
            // coalesce into a single redraw, so we avoid allocating ~width Points every frame at 60fps.
            if (_scopePoints == null || _scopePoints.Count != pointCount)
            {
                _scopePoints = new System.Windows.Media.PointCollection(pointCount);
                for (int i = 0; i < pointCount; i++)
                    _scopePoints.Add(default);
                _scopeLine.Points = _scopePoints;
            }

            double step = (double)vizData.Length / pointCount;
            for (int i = 0; i < pointCount; i++)
            {
                int sampleIdx = Math.Min((int)(i * step), vizData.Length - 1);
                double sample = Math.Clamp(vizData[sampleIdx] * scopeGain, -1.0, 1.0);
                double x = (double)i / pointCount * width;
                double y = centerY - sample * amplitude;
                _scopePoints[i] = new System.Windows.Point(x, y);
            }
        }

        // ── VU Meter renderer ──
        // DJ-style blocky stacked blocks — classic retro stereo VU look
        private void RenderVuMeter(double width, double height, int numBars)
        {
            var vizColors = ThemeManager.GetVisualizerColors();
            var gradient = vizColors.ProgressGradient;
            bool rainbow = ThemeManager.VisualizerRainbowEnabled;
            double time = Environment.TickCount64 / 1000.0;

            int totalBlocks = VuColumns * VuRows;

            // Clean up other mode elements
            if (_particleElements != null || _vizBars != null || _circleElements != null
                || _scopeLine != null )
            {
                VizTarget.Children.Clear();
                _particleElements = null;
                _particles = null;
                _vizBars = null;
                _vizMirrorBars = null;
                _circleElements = null;
                _scopeLine = null;
               
                _vuBlocks = null;
            }

            // Initialize VU blocks
            if (_vuBlocks == null || _vuBlocks.Length != totalBlocks)
            {
                VizTarget.Children.Clear();
                _vuBlocks = new System.Windows.Shapes.Rectangle[totalBlocks];
                _vuBrushes = new SolidColorBrush[totalBlocks];
                for (int i = 0; i < totalBlocks; i++)
                {
                    _vuBrushes[i] = new SolidColorBrush(Colors.Black);
                    _vuBlocks[i] = new System.Windows.Shapes.Rectangle
                    {
                        Fill = _vuBrushes[i],
                        IsHitTestVisible = false,
                        RadiusX = 1,
                        RadiusY = 1
                    };
                    VizTarget.Children.Add(_vuBlocks[i]);
                }
            }

            // Layout constants
            double gap = 2;
            double totalGapW = gap * (VuColumns + 1);
            double totalGapH = gap * (VuRows + 1);
            double blockW = (width - totalGapW) / VuColumns;
            double blockH = (height - totalGapH) / VuRows;

            // Map each column to a frequency band via logarithmic spread
            for (int col = 0; col < VuColumns; col++)
            {
                // Map column to frequency bar
                int barIdx = (col * numBars) / VuColumns;
                barIdx = Math.Clamp(barIdx, 0, numBars - 1);
                double energy = _vizSmoothed[barIdx];

                // How many rows should be lit (from bottom)
                int litRows = (int)(energy * VuRows);
                litRows = Math.Clamp(litRows, 0, VuRows);

                double x = gap + col * (blockW + gap);

                for (int row = 0; row < VuRows; row++)
                {
                    int blockIdx = col * VuRows + row;
                    // row 0 = top, row VuRows-1 = bottom; we light from bottom
                    int rowFromBottom = VuRows - 1 - row;
                    double y = gap + row * (blockH + gap);

                    Canvas.SetLeft(_vuBlocks[blockIdx], x);
                    Canvas.SetTop(_vuBlocks[blockIdx], y);
                    _vuBlocks[blockIdx].Width = Math.Max(1, blockW);
                    _vuBlocks[blockIdx].Height = Math.Max(1, blockH);

                    bool isLit = rowFromBottom < litRows;
                    double rowNorm = (double)rowFromBottom / VuRows; // 0=bottom, 1=top

                    Color color;
                    if (_npVisible && _npColorMatchEnabled && _npVizColorPrimary != default)
                    {
                        color = GetBarColor(col, VuColumns, rowNorm, gradient, false, time);
                    }
                    else if (rainbow)
                    {
                        double hue = ((double)col / VuColumns + time * 0.08) % 1.0;
                        color = HsvToColor(hue * 360, 0.85, isLit ? (0.5 + rowNorm * 0.5) : 0.08);
                    }
                    else
                    {
                        // Theme-aware VU: use visualizer gradient colors mapped bottom→top
                        Color vuBase;
                        if (rowNorm < 0.5)
                        {
                            double t = rowNorm / 0.5;
                            vuBase = Color.FromRgb(
                                (byte)(gradient[0].R + (gradient[1].R - gradient[0].R) * t),
                                (byte)(gradient[0].G + (gradient[1].G - gradient[0].G) * t),
                                (byte)(gradient[0].B + (gradient[1].B - gradient[0].B) * t));
                        }
                        else
                        {
                            double t = (rowNorm - 0.5) / 0.5;
                            vuBase = Color.FromRgb(
                                (byte)(gradient[1].R + (gradient[2].R - gradient[1].R) * t),
                                (byte)(gradient[1].G + (gradient[2].G - gradient[1].G) * t),
                                (byte)(gradient[1].B + (gradient[2].B - gradient[1].B) * t));
                        }
                        color = vuBase;
                    }

                    if (isLit)
                    {
                        // Lit block: full brightness with slight glow for top blocks
                        double brightness = 0.8 + rowNorm * 0.2;
                        color.A = (byte)(200 + brightness * 55);
                    }
                    else
                    {
                        // Dim/dark block: very faint ghost of the color
                        color = Color.FromArgb(30,
                            (byte)(color.R * 0.3),
                            (byte)(color.G * 0.3),
                            (byte)(color.B * 0.3));
                    }

                    _vuBrushes![blockIdx].Color = color;
                }
            }
        }

        // ── Shared color utility for bar-based modes ──
        private Color GetBarColor(int barIndex, int numBars, double value, Color[] gradient, bool rainbow, double time)
        {
            // If NP color-match is active, use album colors instead of theme gradient
            if (_npVisible && _npColorMatchEnabled && _npVizColorPrimary != default)
            {
                var prim = _npVizColorPrimary;
                var sec = _npVizColorSecondary;
                // Boost vibrancy: increase saturation before brightening
                var c1 = BoostVizColor(prim, 60);
                var c2 = BoostVizColor(sec, 80);
                double t = value;
                return Color.FromArgb(255,
                    (byte)(c1.R + (c2.R - c1.R) * t),
                    (byte)(c1.G + (c2.G - c1.G) * t),
                    (byte)(c1.B + (c2.B - c1.B) * t));
            }

            if (rainbow)
            {
                double hue = ((double)barIndex / numBars + time * 0.15 + value * 0.3) % 1.0;
                double saturation = 0.85 + value * 0.15;
                double brightness = 0.5 + value * 0.5;
                return HsvToColor(hue * 360, saturation, brightness);
            }
            else
            {
                double t = value;
                if (t < 0.5)
                {
                    double seg = t / 0.5;
                    return Color.FromArgb(
                        (byte)(gradient[0].A + (gradient[1].A - gradient[0].A) * seg),
                        (byte)(gradient[0].R + (gradient[1].R - gradient[0].R) * seg),
                        (byte)(gradient[0].G + (gradient[1].G - gradient[0].G) * seg),
                        (byte)(gradient[0].B + (gradient[1].B - gradient[0].B) * seg));
                }
                else
                {
                    double seg = (t - 0.5) / 0.5;
                    return Color.FromArgb(
                        (byte)(gradient[1].A + (gradient[2].A - gradient[1].A) * seg),
                        (byte)(gradient[1].R + (gradient[2].R - gradient[1].R) * seg),
                        (byte)(gradient[1].G + (gradient[2].G - gradient[1].G) * seg),
                        (byte)(gradient[1].B + (gradient[2].B - gradient[1].B) * seg));
                }
            }
        }

        /// <summary>
        /// Boosts a color for visualizer display — increases saturation and brightness
        /// so album-matched colors are vibrant rather than washed-out/grey.
        /// </summary>
        private static Color BoostVizColor(Color c, int brighten)
        {
            double r = c.R / 255.0, g = c.G / 255.0, b = c.B / 255.0;
            double max = Math.Max(r, Math.Max(g, b));
            double min = Math.Min(r, Math.Min(g, b));
            double delta = max - min;
            double h = 0, s = max == 0 ? 0 : delta / max, v = max;

            if (delta > 0)
            {
                if (max == r) h = 60 * (((g - b) / delta) % 6);
                else if (max == g) h = 60 * ((b - r) / delta + 2);
                else h = 60 * ((r - g) / delta + 4);
                if (h < 0) h += 360;
            }

            if (s >= 0.10)
                s = Math.Max(s, 0.5);

            v = Math.Min(1.0, v + brighten / 255.0);

            return HsvToColor(h, s, v);
        }

        /// <summary>
        /// Converts HSV (hue 0-360, saturation 0-1, value 0-1) to a WPF Color.
        /// </summary>
        private static Color HsvToColor(double h, double s, double v)
        {
            h %= 360;
            if (h < 0) h += 360;
            double c = v * s;
            double x = c * (1 - Math.Abs((h / 60) % 2 - 1));
            double m = v - c;
            double r, g, b;

            if (h < 60)       { r = c; g = x; b = 0; }
            else if (h < 120) { r = x; g = c; b = 0; }
            else if (h < 180) { r = 0; g = c; b = x; }
            else if (h < 240) { r = 0; g = x; b = c; }
            else if (h < 300) { r = x; g = 0; b = c; }
            else              { r = c; g = 0; b = x; }

            return Color.FromArgb(255,
                (byte)((r + m) * 255),
                (byte)((g + m) * 255),
                (byte)((b + m) * 255));
        }

        private static void VisualizerFFT(double[] real, double[] imag)
        {
            int n = real.Length;
            if (n == 0) return;
            int bits = (int)Math.Log2(n);

            for (int i = 0; i < n; i++)
            {
                int j = 0, v = i;
                for (int b = 0; b < bits; b++) { j = (j << 1) | (v & 1); v >>= 1; }
                if (j > i) { (real[i], real[j]) = (real[j], real[i]); (imag[i], imag[j]) = (imag[j], imag[i]); }
            }

            for (int size = 2; size <= n; size *= 2)
            {
                int half = size / 2;
                double step = -2.0 * Math.PI / size;
                for (int i = 0; i < n; i += size)
                    for (int j = 0; j < half; j++)
                    {
                        double a = step * j, cos = Math.Cos(a), sin = Math.Sin(a);
                        int ei = i + j, oi = i + j + half;
                        double tr = real[oi] * cos - imag[oi] * sin;
                        double ti = real[oi] * sin + imag[oi] * cos;
                        real[oi] = real[ei] - tr; imag[oi] = imag[ei] - ti;
                        real[ei] += tr; imag[ei] += ti;
                    }
            }
        }

    }
}
