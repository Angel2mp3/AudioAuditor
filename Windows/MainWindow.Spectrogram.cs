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
    // Spectrogram generation/preview within the main window (analysis view).
    // Extracted verbatim from MainWindow.xaml.cs (2026-06-05 large-file split).
    public partial class MainWindow
    {
        // ═══════════════════════════════════════════
        //  Spectrogram
        // ═══════════════════════════════════════════

        // Spectrogram serialization to prevent concurrent file access issues
        private readonly SemaphoreSlim _spectrogramSemaphore = new(1, 1);

        // Monotonic id of the latest spectrogram request. A superseded/cancelled request
        // must never strand the "Generating…" spinner that a newer request now owns.
        private int _spectrogramRequestId;

        private async void FileGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (FileGrid.SelectedItem is not AudioFileInfo selectedFile)
            {
                SpectrogramPlaceholder.Text = "Select a file to view its spectrogram";
                SpectrogramPlaceholder.Visibility = Visibility.Visible;
                SpectrogramLoading.Visibility = Visibility.Collapsed;
                SpectrogramPanel.Visibility = Visibility.Collapsed;
                _currentSpectrogramFile = null;
                return;
            }

            if (selectedFile.Status == AudioStatus.Corrupt)
            {
                SpectrogramPlaceholder.Text = $"Cannot generate spectrogram — {selectedFile.ErrorMessage}";
                SpectrogramPlaceholder.Visibility = Visibility.Visible;
                SpectrogramLoading.Visibility = Visibility.Collapsed;
                SpectrogramPanel.Visibility = Visibility.Collapsed;
                _currentSpectrogramFile = null;
                return;
            }

            _spectrogramCts?.Cancel();
            _spectrogramCts = new CancellationTokenSource();
            var token = _spectrogramCts.Token;
            int reqId = ++_spectrogramRequestId;

            SpectrogramPlaceholder.Visibility = Visibility.Collapsed;

            // In visualizer mode, show visualizer immediately instead of "Generating spectrogram..."
            if (_visualizerMode)
            {
                SpectrogramLoading.Visibility = Visibility.Collapsed;
                SpectrogramPanel.Visibility = Visibility.Visible;
                SpectrogramImage.Visibility = Visibility.Collapsed;
                VisualizerCanvas.Visibility = Visibility.Visible;
                FreqLabelGrid.Visibility = Visibility.Collapsed;
                BtnVisualizerStyle.Visibility = Visibility.Visible;
                if ((_player.IsPlaying || _player.IsPaused) && _player.CurrentFile != null &&
                    string.Equals(selectedFile.FilePath, _player.CurrentFile, StringComparison.OrdinalIgnoreCase))
                {
                    if (_player.IsPlaying) StartVisualizer();
                }
                // Visualizer mode: only update title if selected file IS the playing file
                // (showing a different song's name while the visualizer plays another would be wrong)
                if (!_player.IsPlaying && !_player.IsPaused ||
                    _player.CurrentFile == null ||
                    string.Equals(selectedFile.FilePath, _player.CurrentFile, StringComparison.OrdinalIgnoreCase))
                {
                    SpectrogramTitle.Text = BuildSpectrogramTitle(selectedFile);
                    _currentSpectrogramFile = selectedFile;
                }
            }
            else
            {
                SpectrogramLoading.Visibility = Visibility.Visible;
                SpectrogramPanel.Visibility = Visibility.Collapsed;
            }

            // Debounce rapid selection changes. If the selection moved on during the delay, a
            // newer request is already running and owns the spectrogram UI, so just bail out.
            // (We intentionally no longer skip on double-click: playback starts on its own path,
            // and skipping here left the "Generating…" spinner stranded for every played file.)
            var fileAtSelection = selectedFile;
            await Task.Delay(200);
            if (FileGrid.SelectedItem != fileAtSelection) return;

            try
            {
                // Check in-memory spectrogram cache first
                string spectCacheKey = $"{fileAtSelection.FilePath}|{_spectrogramLinearScale}|{_spectrogramChannel}|{ThemeManager.SpectrogramMagmaColormap}|{ThemeManager.SpectrogramHiFiMode}|{_spectrogramEndZoom}";
                BitmapSource? bitmap;
                if (_spectrogramCache.TryGetValue(spectCacheKey, out var cachedBitmap))
                {
                    bitmap = cachedBitmap;
                    // Bump to most-recently-used
                    _spectrogramCacheLru.Remove(spectCacheKey);
                    _spectrogramCacheLru.AddLast(spectCacheKey);
                }
                else
                {
                    // Serialize spectrogram generation to prevent concurrent file access
                    await _spectrogramSemaphore.WaitAsync(token);
                    try
                    {
                        bitmap = await Task.Run(() =>
                            SpectrogramGenerator.Generate(selectedFile.FilePath, 1200, 400,
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
                        // Store in cache with LRU eviction
                        if (_spectrogramCacheLru.Count >= SpectrogramCacheMaxEntries)
                        {
                            string? oldest = _spectrogramCacheLru.First?.Value;
                            if (oldest != null) { _spectrogramCache.Remove(oldest); _spectrogramCacheLru.RemoveFirst(); }
                        }
                        _spectrogramCache[spectCacheKey] = bitmap;
                        _spectrogramCacheLru.AddLast(spectCacheKey);
                    }
                }

                if (token.IsCancellationRequested) return;
                if (reqId != _spectrogramRequestId) return; // a newer selection now owns the UI

                if (bitmap != null)
                {
                    SpectrogramImage.Source = bitmap;
                    _currentSpectrogramFile = selectedFile;
                    _spectrogramZoomLevel = 1.0;
                    UpdateZoomButton();
                    if (SpectrogramScrollViewer != null)
                    {
                        SpectrogramImage.Width = SpectrogramScrollViewer.ActualWidth;
                        SpectrogramImage.Height = SpectrogramScrollViewer.ActualHeight;
                        SpectrogramScrollViewer.ScrollToHorizontalOffset(0);
                    }

                    // Spectrogram mode: always show the selected file's info in the title.
                    // The spectrogram image already shows the selected file, so the title should match.
                    // Visualizer mode: only update title if selected file IS the playing file,
                    // since the visualizer always shows what's currently playing.
                    bool showSelectedInTitle = !_visualizerMode;
                    if (!showSelectedInTitle)
                    {
                        // In visualizer mode, allow update if nothing is playing or if selected == playing
                        if (!_player.IsPlaying && !_player.IsPaused ||
                            _player.CurrentFile == null ||
                            string.Equals(selectedFile.FilePath, _player.CurrentFile, StringComparison.OrdinalIgnoreCase))
                            showSelectedInTitle = true;
                    }

                    if (showSelectedInTitle)
                    {
                        SpectrogramTitle.Text = BuildSpectrogramTitle(selectedFile);
                    }

                    int nyquist = selectedFile.SampleRate / 2;

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

                    SpectrogramLoading.Visibility = Visibility.Collapsed;
                    SpectrogramPanel.Visibility = Visibility.Visible;

                    // Apply visualizer mode
                    if (_visualizerMode)
                    {
                        SpectrogramImage.Visibility = Visibility.Collapsed;
                        VisualizerCanvas.Visibility = Visibility.Visible;
                        FreqLabelGrid.Visibility = Visibility.Collapsed;
                        BtnVisualizerStyle.Visibility = Visibility.Visible;
                        if (_player.IsPlaying) StartVisualizer();
                    }
                    else
                    {
                        SpectrogramImage.Visibility = Visibility.Visible;
                        VisualizerCanvas.Visibility = Visibility.Collapsed;
                        FreqLabelGrid.Visibility = Visibility.Visible;
                        BtnVisualizerStyle.Visibility = Visibility.Collapsed;
                    }
                }
                else
                {
                    SpectrogramLoading.Visibility = Visibility.Collapsed;

                    // In visualizer mode, don't show error text — keep visualizer visible
                    if (_visualizerMode)
                    {
                        SpectrogramPlaceholder.Visibility = Visibility.Collapsed;
                        SpectrogramPanel.Visibility = Visibility.Visible;
                        SpectrogramImage.Visibility = Visibility.Collapsed;
                        VisualizerCanvas.Visibility = Visibility.Visible;
                        FreqLabelGrid.Visibility = Visibility.Collapsed;
                        BtnVisualizerStyle.Visibility = Visibility.Visible;
                    }
                    else
                    {
                        SpectrogramPlaceholder.Text = "Could not generate spectrogram for this file";
                        SpectrogramPlaceholder.Visibility = Visibility.Visible;
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // If this is still the latest request (nothing superseded it), don't leave the
                // "Generating…" spinner up forever.
                if (reqId == _spectrogramRequestId && !_visualizerMode)
                {
                    SpectrogramLoading.Visibility = Visibility.Collapsed;
                    SpectrogramPlaceholder.Text = "Select a file to view its spectrogram";
                    SpectrogramPlaceholder.Visibility = Visibility.Visible;
                }
            }
            catch
            {
                if (!token.IsCancellationRequested)
                {
                    SpectrogramLoading.Visibility = Visibility.Collapsed;

                    if (_visualizerMode)
                    {
                        SpectrogramPlaceholder.Visibility = Visibility.Collapsed;
                        SpectrogramPanel.Visibility = Visibility.Visible;
                        SpectrogramImage.Visibility = Visibility.Collapsed;
                        VisualizerCanvas.Visibility = Visibility.Visible;
                        FreqLabelGrid.Visibility = Visibility.Collapsed;
                        BtnVisualizerStyle.Visibility = Visibility.Visible;
                    }
                    else
                    {
                        SpectrogramPlaceholder.Text = "Error generating spectrogram";
                        SpectrogramPlaceholder.Visibility = Visibility.Visible;
                    }
                }
            }
        }
    }
}
