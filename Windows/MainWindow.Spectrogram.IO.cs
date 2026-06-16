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
    // Spectrogram save/export handlers: single, all, view window, and multi-file
    // folder export. Extracted verbatim from MainWindow.xaml.cs (2026-06-05 split).
    public partial class MainWindow
    {
        // ═══════════════════════════════════════════
        //  Save Spectrogram (single)
        // ═══════════════════════════════════════════

        private void SaveSpectrogram_Click(object sender, RoutedEventArgs e)
        {
            var selected = FileGrid.SelectedItems.OfType<AudioFileInfo>()
                                .Where(f => f.Status != AudioStatus.Corrupt).ToList();
            if (selected.Count == 0) return;
            if (selected.Count == 1) { SaveSpectrogramForFile(selected[0]); return; }

            // Multiple files — pick a folder
            var dlg = new OpenFolderDialog { Title = "Select folder for spectrograms" };
            if (dlg.ShowDialog() == true)
                _ = SaveSpectrogramsToFolderAsync(selected, dlg.FolderName);
        }

        private void SpectrogramImage_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2 && _currentSpectrogramFile != null)
            {
                SaveSpectrogramForFile(_currentSpectrogramFile);
            }
        }

        private void SaveSpectrogramForFile(AudioFileInfo file)
        {
            if (file.Status == AudioStatus.Corrupt) return;

            var dialog = new SaveFileDialog
            {
                Title = "Save Spectrogram",
                FileName = $"{IOPath.GetFileNameWithoutExtension(file.FileName)}_spectrogram.png",
                Filter = "PNG Image|*.png",
                DefaultExt = ".png"
            };

            if (dialog.ShowDialog() == true)
            {
                try
                {
                    int exportWidth  = Math.Clamp((int)(file.DurationSeconds * 60.0), 800, 16000);
                    int exportHeight = 800;
                    var bitmap = RenderSpectrogramWithLabels(file, exportWidth, exportHeight,
                        highFidelity: ThemeManager.SpectrogramHiFiMode,
                        magmaColormap: ThemeManager.SpectrogramMagmaColormap);
                    if (bitmap != null)
                    {
                        using var stream = new FileStream(dialog.FileName, FileMode.Create);
                        var encoder = new PngBitmapEncoder();
                        encoder.Frames.Add(BitmapFrame.Create(bitmap));
                        encoder.Save(stream);
                        StatusText.Text = $"Spectrogram saved: {dialog.FileName}";
                    }
                    else
                    {
                        ErrorDialog.Show("Save Failed", "Could not generate spectrogram for this file.", this);
                    }
                }
                catch (Exception ex)
                {
                    ErrorDialog.Show("Save Error", $"Error saving spectrogram:\n{ex.Message}", this);
                }
            }
        }

        /// <summary>
        /// Renders a spectrogram with Hz labels and title baked into the image.
        /// If preGenerated is provided, uses it instead of generating a new spectrogram.
        /// </summary>
        private BitmapSource? RenderSpectrogramWithLabels(AudioFileInfo file, int spectWidth, int spectHeight,
            BitmapSource? preGenerated = null, bool highFidelity = false, bool magmaColormap = false)
        {
            var rawBitmap = preGenerated ?? SpectrogramGenerator.Generate(file.FilePath, spectWidth, spectHeight,
                _spectrogramLinearScale, _spectrogramChannel, _spectrogramEndZoom ? 10 : 0,
                highFidelity, magmaColormap, default);
            if (rawBitmap == null) return null;

            int leftMargin  = 70;   // Hz labels
            int rightMargin = 30;   // dB scale strip
            int topMargin   = 28;   // Title bar
            int bottomMargin = 4;

            int rawW = rawBitmap.PixelWidth;
            int rawH = rawBitmap.PixelHeight;

            int totalWidth  = leftMargin + rawW + rightMargin;
            int totalHeight = topMargin + rawH + bottomMargin;

            var dv = new DrawingVisual();
            using (var dc = dv.RenderOpen())
            {
                // Background
                dc.DrawRectangle(Brushes.Black, null, new System.Windows.Rect(0, 0, totalWidth, totalHeight));

                // Draw spectrogram
                dc.DrawImage(rawBitmap, new System.Windows.Rect(leftMargin, topMargin, rawW, rawH));

                // Title — include bitrate and encoder info
                string bitrateInfo = file.ReportedBitrate > 0 ? $"  —  {file.ReportedBitrate} kbps" : "";
                string realBrInfo  = file.ActualBitrate > 0 ? $" (Real: {file.ActualBitrate} kbps)" : "";
                var titleText = new FormattedText(
                    $"{file.FileName}  —  {file.SampleRate:N0} Hz / {file.BitsPerSampleDisplay}  —  {file.Duration}{bitrateInfo}{realBrInfo}  —  {file.Status}",
                    System.Globalization.CultureInfo.InvariantCulture,
                    FlowDirection.LeftToRight,
                    new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal),
                    11, Brushes.White, 96);
                dc.DrawText(titleText, new System.Windows.Point(leftMargin + 4, 6));

                // Hz labels (5 labels)
                int nyquist = file.SampleRate / 2;

                string topHz, upperMidHz, midHz, lowerMidHz, botHz;

                if (_spectrogramLinearScale)
                {
                    topHz = $"{nyquist:N0} Hz";
                    upperMidHz = $"{(int)(nyquist * 0.75):N0} Hz";
                    midHz = $"{(int)(nyquist * 0.50):N0} Hz";
                    lowerMidHz = $"{(int)(nyquist * 0.25):N0} Hz";
                    botHz = "0 Hz";
                }
                else
                {
                    double logMinF = Math.Log10(20.0);
                    double logMaxF = Math.Log10(nyquist);
                    double logRangeF = logMaxF - logMinF;

                    topHz = $"{nyquist:N0} Hz";
                    upperMidHz = $"{(int)Math.Pow(10, logMinF + 0.75 * logRangeF):N0} Hz";
                    midHz = $"{(int)Math.Pow(10, logMinF + 0.5 * logRangeF):N0} Hz";
                    lowerMidHz = $"{(int)Math.Pow(10, logMinF + 0.25 * logRangeF):N0} Hz";
                    botHz = "20 Hz";
                }

                var labelBrush = new SolidColorBrush(Color.FromRgb(180, 180, 180));
                labelBrush.Freeze();
                var labelTypeFace = new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);

                var ftTop = new FormattedText(topHz, System.Globalization.CultureInfo.InvariantCulture,
                    FlowDirection.LeftToRight, labelTypeFace, 11, labelBrush, 96);
                dc.DrawText(ftTop, new System.Windows.Point(leftMargin - ftTop.Width - 6, topMargin + 2));

                var ftUpperMid = new FormattedText(upperMidHz, System.Globalization.CultureInfo.InvariantCulture,
                    FlowDirection.LeftToRight, labelTypeFace, 11, labelBrush, 96);
                dc.DrawText(ftUpperMid, new System.Windows.Point(leftMargin - ftUpperMid.Width - 6, topMargin + rawH * 0.25 - ftUpperMid.Height / 2));

                var ftMid = new FormattedText(midHz, System.Globalization.CultureInfo.InvariantCulture,
                    FlowDirection.LeftToRight, labelTypeFace, 11, labelBrush, 96);
                dc.DrawText(ftMid, new System.Windows.Point(leftMargin - ftMid.Width - 6, topMargin + rawH / 2 - ftMid.Height / 2));

                var ftLowerMid = new FormattedText(lowerMidHz, System.Globalization.CultureInfo.InvariantCulture,
                    FlowDirection.LeftToRight, labelTypeFace, 11, labelBrush, 96);
                dc.DrawText(ftLowerMid, new System.Windows.Point(leftMargin - ftLowerMid.Width - 6, topMargin + rawH * 0.75 - ftLowerMid.Height / 2));

                var ftBot = new FormattedText(botHz, System.Globalization.CultureInfo.InvariantCulture,
                    FlowDirection.LeftToRight, labelTypeFace, 11, labelBrush, 96);
                dc.DrawText(ftBot, new System.Windows.Point(leftMargin - ftBot.Width - 6, topMargin + rawH - ftBot.Height - 2));

                // dB scale on the right margin (gradient strip + 4 labels)
                int dbStripX = leftMargin + rawW + 2;
                int dbStripW = 8;
                for (int row = 0; row < rawH; row++)
                {
                    double t = 1.0 - (double)row / (rawH - 1);
                    byte v = (byte)(t * 255);
                    var dbBrush = new SolidColorBrush(Color.FromRgb(v, v, v));
                    dc.DrawRectangle(dbBrush, null,
                        new System.Windows.Rect(dbStripX, topMargin + row, dbStripW, 1));
                }
                string[] dbLabels = { "0", "-40", "-80", "-120" };
                double[] dbPositions = { 0.0, 0.33, 0.67, 1.0 };
                var dbTypeFace = labelTypeFace;
                for (int di = 0; di < dbLabels.Length; di++)
                {
                    double py = topMargin + dbPositions[di] * rawH;
                    var ft = new FormattedText(dbLabels[di], System.Globalization.CultureInfo.InvariantCulture,
                        FlowDirection.LeftToRight, dbTypeFace, 9, labelBrush, 96);
                    dc.DrawText(ft, new System.Windows.Point(dbStripX + dbStripW + 1, py - ft.Height / 2));
                }

                // HiFi mode: draw reference lines at 16k, 19k, 20k Hz (lossy artifact markers)
                if (highFidelity && !_spectrogramLinearScale)
                {
                    var refPen = new Pen(new SolidColorBrush(Color.FromArgb(120, 255, 255, 255)), 1);
                    var refHz = new[] { 16000, 19000, 20000 };
                    double logMinRef = Math.Log10(20.0);
                    double logMaxRef = Math.Log10(nyquist);
                    double logRangeRef = logMaxRef - logMinRef;
                    foreach (int hz in refHz)
                    {
                        if (hz > nyquist) continue;
                        double t = (Math.Log10(hz) - logMinRef) / logRangeRef;
                        double py = topMargin + (1.0 - t) * rawH;
                        dc.DrawLine(refPen,
                            new System.Windows.Point(leftMargin, py),
                            new System.Windows.Point(leftMargin + rawW, py));
                        var ftRef = new FormattedText($"{hz / 1000}k",
                            System.Globalization.CultureInfo.InvariantCulture,
                            FlowDirection.LeftToRight, labelTypeFace, 9,
                            new SolidColorBrush(Color.FromArgb(180, 255, 255, 255)), 96);
                        dc.DrawText(ftRef, new System.Windows.Point(leftMargin + spectWidth - ftRef.Width - 2, py - ftRef.Height - 1));
                    }
                }
            }

            var rtb = new RenderTargetBitmap(totalWidth, totalHeight, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(dv);
            rtb.Freeze();
            return rtb;
        }

        // ═══════════════════════════════════════════
        //  Save All Spectrograms
        // ═══════════════════════════════════════════

        private async void SaveAllSpectrograms_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_files.Count == 0)
                {
                    ErrorDialog.Show("Nothing to Save", "No files loaded.", this);
                    return;
                }

                var dialog = new OpenFolderDialog
                {
                    Title = "Select folder to save spectrograms"
                };

                if (dialog.ShowDialog() != true) return;

                string folder = dialog.FolderName;
                var filesToProcess = _files.Where(f => f.Status != AudioStatus.Corrupt).ToList();
                int total = filesToProcess.Count;
                int completed = 0;
                int failed = 0;

                // Throttle to half the configured concurrency (spectrograms are memory-heavy)
                int maxParallel = Math.Max(1, ThemeManager.MaxConcurrency / 2);
                var spectSemaphore = new SemaphoreSlim(maxParallel);

                AnalysisProgressPanel.Visibility = Visibility.Visible;
                AnalysisProgress.Maximum = total;
                AnalysisProgress.Value = 0;
                AnalysisEtaText.Text = "";
                _analysisStartTime = DateTime.UtcNow;
                StatusText.Text = $"Saving spectrograms 0 / {total}...";

                foreach (var file in filesToProcess)
                {
                    await spectSemaphore.WaitAsync();
                    try
                    {
                        // Wait if memory usage exceeds configured limit
                        await ThemeManager.WaitForMemoryAsync();
                        string outPath = IOPath.Combine(folder,
                            $"{IOPath.GetFileNameWithoutExtension(file.FileName)}_spectrogram.png");

                        // Handle duplicate names
                        int i = 1;
                        while (File.Exists(outPath))
                        {
                            outPath = IOPath.Combine(folder,
                                $"{IOPath.GetFileNameWithoutExtension(file.FileName)}_spectrogram_{i++}.png");
                        }

                        string savePath = outPath;
                        var fileRef = file;

                        // Generate spectrogram on background thread (CPU-heavy)
                        var rawBitmap = await Task.Run(() =>
                        {
                            Thread.CurrentThread.Priority = ThreadPriority.BelowNormal;
                            try
                            {
                                return SpectrogramGenerator.Generate(fileRef.FilePath, 1800, 600,
                                    _spectrogramLinearScale, _spectrogramChannel,
                                    _spectrogramEndZoom ? 10 : 0,
                                    ThemeManager.SpectrogramHiFiMode,
                                    ThemeManager.SpectrogramMagmaColormap,
                                    default);
                            }
                            finally { Thread.CurrentThread.Priority = ThreadPriority.Normal; }
                        });

                        if (rawBitmap != null)
                        {
                            // Render with labels on UI thread (DrawingVisual requires STA)
                            var bitmap = RenderSpectrogramWithLabels(fileRef, 1800, 600, rawBitmap);
                            if (bitmap != null)
                            {
                                // Save to disk on background thread
                                await Task.Run(() =>
                                {
                                    using var stream = new FileStream(savePath, FileMode.Create);
                                    var encoder = new PngBitmapEncoder();
                                    encoder.Frames.Add(BitmapFrame.Create(bitmap));
                                    encoder.Save(stream);
                                });
                            }
                            else failed++;
                        }
                        else failed++;
                    }
                    catch
                    {
                        failed++;
                    }
                    finally
                    {
                        spectSemaphore.Release();
                    }

                    var c = Interlocked.Increment(ref completed);
                    AnalysisProgress.Value = c;
                    StatusText.Text = $"Saving spectrograms {c} / {total}...";
                    UpdateAnalysisEta(c, total);
                }

                AnalysisProgressPanel.Visibility = Visibility.Collapsed;
                AnalysisEtaText.Text = "";
                string msg = failed > 0
                    ? $"Saved {completed - failed} / {total} spectrograms to {folder} ({failed} failed)"
                    : $"Saved {completed} spectrograms to {folder}";
                StatusText.Text = msg;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[SaveAllSpectrograms_Click] {ex}");
                StatusText.Text = "Save failed.";
                AnalysisProgressPanel.Visibility = Visibility.Collapsed;
            }
        }

        /// <summary>Public bridge used by SpectrogramViewWindow to render with labels.</summary>
        public BitmapSource? RenderSpectrogramForViewer(AudioFileInfo file, int w, int h,
            BitmapSource? preGenerated = null)
            => RenderSpectrogramWithLabels(file, w, h, preGenerated,
                ThemeManager.SpectrogramHiFiMode, ThemeManager.SpectrogramMagmaColormap);

        public void ClearSpectrogramCache()
        {
            _spectrogramCache.Clear();
            _spectrogramCacheLru.Clear();
        }

        public int SpectrogramCacheCount => _spectrogramCache.Count;

        // ═══════════════════════════════════════════
        //  Spectrogram View Window
        // ═══════════════════════════════════════════

        private void ViewSpectrogram_Click(object sender, RoutedEventArgs e)
        {
            if (FileGrid.SelectedItem is AudioFileInfo file && file.Status != AudioStatus.Corrupt)
                new SpectrogramViewWindow(file, this) { Owner = this }.Show();
        }

        // ═══════════════════════════════════════════
        //  Multi-file spectrogram export (folder)
        // ═══════════════════════════════════════════

        private async Task SaveSpectrogramsToFolderAsync(List<AudioFileInfo> files, string folder)
        {
            int total = files.Count;
            int completed = 0;
            int failed = 0;

            int maxParallel = Math.Max(1, ThemeManager.MaxConcurrency / 2);
            var sem = new SemaphoreSlim(maxParallel);

            AnalysisProgressPanel.Visibility = Visibility.Visible;
            AnalysisProgress.Maximum = total;
            AnalysisProgress.Value = 0;
            _analysisStartTime = DateTime.UtcNow;
            StatusText.Text = $"Saving spectrograms 0 / {total}...";

            var tasks = files.Select(async file =>
            {
                await sem.WaitAsync();
                try
                {
                    await ThemeManager.WaitForMemoryAsync();
                    int exportWidth  = Math.Clamp((int)(file.DurationSeconds * 60.0), 800, 16000);
                    int exportHeight = 800;
                    string outPath = IOPath.Combine(folder,
                        $"{IOPath.GetFileNameWithoutExtension(file.FileName)}_spectrogram.png");
                    int n = 1;
                    while (File.Exists(outPath))
                        outPath = IOPath.Combine(folder,
                            $"{IOPath.GetFileNameWithoutExtension(file.FileName)}_spectrogram_{n++}.png");

                    var raw = await Task.Run(() => SpectrogramGenerator.Generate(
                        file.FilePath, exportWidth, exportHeight,
                        _spectrogramLinearScale, _spectrogramChannel,
                        _spectrogramEndZoom ? 10 : 0,
                        ThemeManager.SpectrogramHiFiMode, ThemeManager.SpectrogramMagmaColormap, default));

                    if (raw != null)
                    {
                        var bitmap = RenderSpectrogramWithLabels(file, exportWidth, exportHeight, raw,
                            ThemeManager.SpectrogramHiFiMode, ThemeManager.SpectrogramMagmaColormap);
                        if (bitmap != null)
                        {
                            string save = outPath;
                            await Task.Run(() =>
                            {
                                using var stream = new FileStream(save, FileMode.Create);
                                var enc = new PngBitmapEncoder();
                                enc.Frames.Add(BitmapFrame.Create(bitmap));
                                enc.Save(stream);
                            });
                        }
                        else Interlocked.Increment(ref failed);
                    }
                    else Interlocked.Increment(ref failed);
                }
                catch { Interlocked.Increment(ref failed); }
                finally { sem.Release(); }

                int c = Interlocked.Increment(ref completed);
                await Dispatcher.InvokeAsync(() =>
                {
                    AnalysisProgress.Value = c;
                    StatusText.Text = $"Saving spectrograms {c} / {total}...";
                    UpdateAnalysisEta(c, total);
                });
            });

            await Task.WhenAll(tasks);
            AnalysisProgressPanel.Visibility = Visibility.Collapsed;
            AnalysisEtaText.Text = "";
            StatusText.Text = failed > 0
                ? $"Saved {total - failed} / {total} spectrograms to {folder} ({failed} failed)"
                : $"Saved {total} spectrograms to {folder}";
        }
    }
}
