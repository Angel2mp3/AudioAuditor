using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using IOPath = System.IO.Path;
using AudioQualityChecker.Models;
using AudioQualityChecker.Services;
using Microsoft.Win32;

namespace AudioQualityChecker
{
    public partial class MainWindow : Window
    {
        // ── Dark title bar interop ──
        [DllImport("dwmapi.dll", PreserveSig = true)]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);
        private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
        private const int DWMWA_CAPTION_COLOR = 35;

        private readonly ObservableCollection<AudioFileInfo> _files = new();
        private ICollectionView? _filteredView;
        private CancellationTokenSource? _spectrogramCts;
        private bool _isAnalyzing;

        // Audio player
        private readonly AudioPlayer _player = new();
        private readonly DispatcherTimer _playerTimer;
        private bool _isSeeking;

        // Search
        private string _searchText = "";
        private AudioStatus? _statusFilter = null;
        private bool _mismatchedBitrateFilter;

        // Drag-from-grid: track whether we initiated an outbound drag
        private bool _isOutboundDrag;

        // Seek cooldown to prevent snap-back
        private DateTime _lastSeekTime = DateTime.MinValue;

        // Track the currently displayed spectrogram file
        private AudioFileInfo? _currentSpectrogramFile;

        // Queue system
        private readonly ObservableCollection<AudioFileInfo> _queue = new();

        // Shuffle mode
        private bool _shuffleMode;
        private readonly Random _shuffleRng = new();
        private readonly List<string> _shuffleHistory = new(); // tracks recently played file paths
        private const int ShuffleHistorySize = 50; // remember last N tracks to avoid repeats

        // Animated waveform
        private double[] _waveformData = Array.Empty<double>();
        private DateTime _waveformAnimStart;
        private bool _waveformAnimActive;

        // Cached position for smooth interpolation between timer ticks
        private double _cachedPositionSec;
        private double _cachedDurationSec;
        private DateTime _cachedPositionTime = DateTime.UtcNow;
        private bool _isPlayingCached;

        // Visualizer
        private bool _visualizerMode;
        private bool _visualizerActive;

        // Integrations
        private readonly DiscordRichPresenceService _discord = new();
        private readonly LastFmService _lastFm = new();

        // EQ sliders
        private Slider[] _eqSliders = Array.Empty<Slider>();
        private TextBlock[] _eqValueLabels = Array.Empty<TextBlock>();

        // Mute state
        private bool _isMuted;
        private double _preMuteVolume = 100;

        // Previous track: restart vs go-back
        private DateTime _lastPrevClickTime = DateTime.MinValue;

        private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".mp3", ".flac", ".wav", ".ogg", ".aac", ".m4a", ".wma",
            ".aiff", ".aif", ".ape", ".wv", ".opus", ".alac", ".dsf", ".dff"
        };

        public MainWindow()
        {
            InitializeComponent();

            // Restore saved column layout (order + widths)
            RestoreColumnLayout();

            // Set up filtered view
            _filteredView = CollectionViewSource.GetDefaultView(_files);
            _filteredView.Filter = SearchFilter;
            FileGrid.ItemsSource = _filteredView;

            _player.PlaybackStopped += Player_PlaybackStopped;
            _player.TrackFinished += Player_TrackFinished;

            _playerTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
            _playerTimer.Tick += PlayerTimer_Tick;

            // Initialize music service button labels
            UpdateServiceButtonLabels();

            // Restore visualizer mode
            _visualizerMode = ThemeManager.VisualizerMode;
            UpdateVisualizerToggleText();

            // Initialize equalizer UI
            InitializeEqualizerSliders();
            ChkEqEnabled.IsChecked = ThemeManager.EqualizerEnabled;
            EqPanel.Visibility = Visibility.Collapsed;

            // Initialize Discord Rich Presence
            if (ThemeManager.DiscordRpcEnabled)
            {
                _discord.Enable();
                // Idle presence set automatically on Ready event
            }

            // Initialize Last.fm
            if (ThemeManager.LastFmEnabled && !string.IsNullOrEmpty(ThemeManager.LastFmSessionKey))
                _lastFm.Configure(ThemeManager.LastFmApiKey, ThemeManager.LastFmApiSecret, ThemeManager.LastFmSessionKey);

            // Update Last.fm status indicator
            UpdateLastFmStatusIndicator();
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            ApplyThemeTitleBar();

            // Hook WndProc for horizontal scroll (touchpad) support
            var hwnd = new WindowInteropHelper(this).Handle;
            var source = HwndSource.FromHwnd(hwnd);
            source?.AddHook(WndProc);
        }

        private const int WM_MOUSEHWHEEL = 0x020E;

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr hParam, ref bool handled)
        {
            if (msg == WM_MOUSEHWHEEL)
            {
                // wParam high word is the delta (positive = scroll right, negative = scroll left)
                int delta = (short)(wParam.ToInt64() >> 16);
                var scrollViewer = FindVisualChild<ScrollViewer>(FileGrid);
                if (scrollViewer != null)
                {
                    scrollViewer.ScrollToHorizontalOffset(scrollViewer.HorizontalOffset + delta);
                    handled = true;
                }
            }
            return IntPtr.Zero;
        }

        private void ApplyThemeTitleBar()
        {
            try
            {
                var hwnd = new WindowInteropHelper(this).Handle;
                if (hwnd == IntPtr.Zero) return;

                // Set dark mode for light-on-dark text
                bool isLight = ThemeManager.CurrentTheme == "Light";
                int darkMode = isLight ? 0 : 1;
                DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref darkMode, sizeof(int));

                // Set caption color to match theme toolbar
                int colorRef = ThemeManager.GetTitleBarColorRef();
                DwmSetWindowAttribute(hwnd, DWMWA_CAPTION_COLOR, ref colorRef, sizeof(int));
            }
            catch { }
        }

        protected override void OnClosed(EventArgs e)
        {
            SaveColumnLayout();
            StopWaveformAnimation();
            StopVisualizer();
            _playerTimer.Stop();
            _player.Dispose();
            _discord.Dispose();
            _lastFm.Dispose();
            base.OnClosed(e);
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
            catch { }
        }

        private void RestoreColumnLayout()
        {
            try
            {
                string layout = ThemeManager.ColumnLayout;
                if (string.IsNullOrEmpty(layout)) return;

                var entries = layout.Split('|', StringSplitOptions.RemoveEmptyEntries);
                // Build a map: header → (displayIndex, width)
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

                // Apply display indices and widths
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
            catch { }
        }

        // ═══════════════════════════════════════════
        //  Search / Filter
        // ═══════════════════════════════════════════

        private bool SearchFilter(object obj)
        {
            if (obj is not AudioFileInfo f) return false;

            // Status filter
            if (_statusFilter.HasValue && f.Status != _statusFilter.Value)
                return false;

            // Mismatched bitrate filter
            if (_mismatchedBitrateFilter)
            {
                if (f.ReportedBitrate <= 0 || f.ActualBitrate <= 0)
                    return false;
                double ratio = (double)f.ActualBitrate / f.ReportedBitrate;
                if (ratio >= 0.80) // matching is >= 80%
                    return false;
            }

            // Text search
            if (string.IsNullOrWhiteSpace(_searchText)) return true;

            var q = _searchText;
            return f.FileName.Contains(q, StringComparison.OrdinalIgnoreCase)
                || f.Artist.Contains(q, StringComparison.OrdinalIgnoreCase)
                || f.Title.Contains(q, StringComparison.OrdinalIgnoreCase)
                || f.FilePath.Contains(q, StringComparison.OrdinalIgnoreCase)
                || f.Extension.Contains(q, StringComparison.OrdinalIgnoreCase)
                || f.Status.ToString().Contains(q, StringComparison.OrdinalIgnoreCase);
        }

        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            _searchText = SearchBox.Text;
            SearchPlaceholder.Visibility = string.IsNullOrEmpty(_searchText)
                ? Visibility.Visible : Visibility.Collapsed;
            _filteredView?.Refresh();
        }

        private void StatusFilterCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (StatusFilterCombo?.SelectedIndex is not int idx) return;

            _statusFilter = idx switch
            {
                1 => AudioStatus.Valid,
                2 => AudioStatus.Fake,
                3 => AudioStatus.Unknown,
                4 => AudioStatus.Corrupt,
                5 => AudioStatus.Optimized,
                _ => null // "All Statuses" or special filters
            };

            _mismatchedBitrateFilter = idx == 6;

            _filteredView?.Refresh();
        }

        // ═══════════════════════════════════════════
        //  Toolbar
        // ═══════════════════════════════════════════

        private void AddFiles_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Title = "Select Audio Files",
                Filter = "Audio Files|*.mp3;*.flac;*.wav;*.ogg;*.aac;*.m4a;*.wma;*.aiff;*.aif;*.ape;*.wv;*.opus;*.dsf;*.dff|All Files|*.*",
                Multiselect = true
            };

            if (dialog.ShowDialog() == true)
                _ = AnalyzeAndAddFiles(dialog.FileNames);
        }

        private void AddFolder_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFolderDialog
            {
                Title = "Select folder containing audio files"
            };

            if (dialog.ShowDialog() == true)
            {
                var files = Directory.EnumerateFiles(dialog.FolderName, "*.*", SearchOption.AllDirectories)
                    .Where(f => SupportedExtensions.Contains(IOPath.GetExtension(f)))
                    .ToArray();

                if (files.Length == 0)
                {
                    ErrorDialog.Show("No Files Found", "No supported audio files found in the selected folder.", this);
                    return;
                }

                _ = AnalyzeAndAddFiles(files);
            }
        }

        private void ClearAll_Click(object sender, RoutedEventArgs e)
        {
            _player.Stop();
            _playerTimer.Stop();
            StopWaveformAnimation();
            StopVisualizer();
            _files.Clear();
            _queue.Clear();
            SearchBox.Text = "";
            SpectrogramPanel.Visibility = Visibility.Collapsed;
            SpectrogramLoading.Visibility = Visibility.Collapsed;
            SpectrogramPlaceholder.Text = "Select a file to view its spectrogram — Double-click or press Enter to play";
            SpectrogramPlaceholder.Visibility = Visibility.Visible;
            StatusText.Text = "Ready — Drag and drop audio files or folders to begin";
            _currentSpectrogramFile = null;
            SpectrogramImage.Source = null;
            VisualizerCanvas.Children.Clear();
            WaveformCanvas.Children.Clear();
            _waveformData = Array.Empty<double>();
            _waveformBaseData = Array.Empty<double>();
            UpdatePlayerUI();

            // Force a GC to release spectrogram bitmaps and audio data from memory
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }

        // ═══════════════════════════════════════════
        //  File Analysis (multi-threaded)
        // ═══════════════════════════════════════════

        private async Task AnalyzeAndAddFiles(string[] filePaths)
        {
            if (_isAnalyzing)
            {
                ErrorDialog.Show("Busy", "Analysis already in progress. Please wait.", this);
                return;
            }

            // Deduplicate against already-loaded files
            var existing = new HashSet<string>(_files.Select(f => f.FilePath), StringComparer.OrdinalIgnoreCase);
            var newPaths = filePaths.Where(p => !existing.Contains(p)).ToArray();

            if (newPaths.Length == 0) return;

            _isAnalyzing = true;
            int total = newPaths.Length;
            int completed = 0;

            AnalysisProgress.Visibility = Visibility.Visible;
            AnalysisProgress.Maximum = total;
            AnalysisProgress.Value = 0;
            StatusText.Text = $"Analyzing 0 / {total} files...";

            int maxParallel = ThemeManager.MaxConcurrency;
            var semaphore = new SemaphoreSlim(maxParallel);

            var tasks = newPaths.Select(async path =>
            {
                await semaphore.WaitAsync();
                try
                {
                    // Wait if memory usage exceeds configured limit
                    await ThemeManager.WaitForMemoryAsync();
                    var info = await Task.Run(() => AudioAnalyzer.AnalyzeFile(path));
                    await Dispatcher.InvokeAsync(() =>
                    {
                        _files.Add(info);
                        var count = Interlocked.Increment(ref completed);
                        StatusText.Text = $"Analyzed {count} / {total} files...";
                        AnalysisProgress.Value = count;
                    });
                }
                catch
                {
                    Interlocked.Increment(ref completed);
                    await Dispatcher.InvokeAsync(() =>
                    {
                        _files.Add(new AudioFileInfo
                        {
                            FilePath = path,
                            FileName = IOPath.GetFileName(path),
                            Extension = IOPath.GetExtension(path).ToLowerInvariant(),
                            Status = AudioStatus.Corrupt,
                            ErrorMessage = "Failed to open or analyze"
                        });
                        AnalysisProgress.Value = completed;
                        StatusText.Text = $"Analyzed {completed} / {total} files...";
                    });
                }
                finally
                {
                    semaphore.Release();
                }
            });

            await Task.WhenAll(tasks);
            _isAnalyzing = false;
            AnalysisProgress.Visibility = Visibility.Collapsed;

            UpdateStatusSummary();
        }

        private void UpdateStatusSummary()
        {
            int valid = _files.Count(f => f.Status == AudioStatus.Valid);
            int fake = _files.Count(f => f.Status == AudioStatus.Fake);
            int unknown = _files.Count(f => f.Status == AudioStatus.Unknown);
            int corrupt = _files.Count(f => f.Status == AudioStatus.Corrupt);
            int mqa = _files.Count(f => f.IsMqa);
            int ai = _files.Count(f => f.IsAiGenerated);
            string mqaPart = mqa > 0 ? $", {mqa} MQA" : "";
            string aiPart = ai > 0 ? $", {ai} AI" : "";
            StatusText.Text = $"{_files.Count} files — {valid} real, {fake} fake, {unknown} unknown, {corrupt} corrupted{mqaPart}{aiPart}";
        }

        // ═══════════════════════════════════════════
        //  Spectrogram
        // ═══════════════════════════════════════════

        // Spectrogram serialization to prevent concurrent file access issues
        private readonly SemaphoreSlim _spectrogramSemaphore = new(1, 1);

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

            SpectrogramPlaceholder.Visibility = Visibility.Collapsed;

            // In visualizer mode, show visualizer immediately instead of "Generating spectrogram..."
            if (_visualizerMode)
            {
                SpectrogramLoading.Visibility = Visibility.Collapsed;
                SpectrogramPanel.Visibility = Visibility.Visible;
                SpectrogramImage.Visibility = Visibility.Collapsed;
                VisualizerCanvas.Visibility = Visibility.Visible;
                FreqLabelGrid.Visibility = Visibility.Collapsed;
                if ((_player.IsPlaying || _player.IsPaused) && _player.CurrentFile != null &&
                    string.Equals(selectedFile.FilePath, _player.CurrentFile, StringComparison.OrdinalIgnoreCase))
                {
                    if (_player.IsPlaying) StartVisualizer();
                }
                SpectrogramTitle.Text = BuildSpectrogramTitle(selectedFile);
                _currentSpectrogramFile = selectedFile;
            }
            else
            {
                SpectrogramLoading.Visibility = Visibility.Visible;
                SpectrogramPanel.Visibility = Visibility.Collapsed;
            }

            try
            {
                // Serialize spectrogram generation to prevent concurrent file access
                await _spectrogramSemaphore.WaitAsync(token);
                BitmapSource? bitmap;
                try
                {
                    bitmap = await Task.Run(() =>
                        SpectrogramGenerator.Generate(selectedFile.FilePath, 1200, 400), token);
                }
                finally
                {
                    _spectrogramSemaphore.Release();
                }

                if (token.IsCancellationRequested) return;

                if (bitmap != null)
                {
                    SpectrogramImage.Source = bitmap;
                    _currentSpectrogramFile = selectedFile;

                    // In visualizer mode while playing, keep showing the currently playing song's info
                    // instead of the one that was just clicked/selected
                    bool showSelectedInTitle = true;
                    if (_visualizerMode && (_player.IsPlaying || _player.IsPaused) && _player.CurrentFile != null)
                    {
                        // Only update title if the selected file IS the playing file
                        if (!string.Equals(selectedFile.FilePath, _player.CurrentFile, StringComparison.OrdinalIgnoreCase))
                            showSelectedInTitle = false;
                    }

                    if (showSelectedInTitle)
                    {
                        SpectrogramTitle.Text = BuildSpectrogramTitle(selectedFile);
                    }

                    int nyquist = selectedFile.SampleRate / 2;
                    double logMin = Math.Log10(20.0);
                    double logMax = Math.Log10(nyquist);
                    double logRange = logMax - logMin;

                    FreqLabelTop.Text = $"{nyquist:N0} Hz";
                    FreqLabelUpperMid.Text = $"{(int)Math.Pow(10, logMin + 0.75 * logRange):N0} Hz";
                    FreqLabelMid.Text = $"{(int)Math.Pow(10, logMin + 0.5 * logRange):N0} Hz";
                    FreqLabelLowerMid.Text = $"{(int)Math.Pow(10, logMin + 0.25 * logRange):N0} Hz";
                    FreqLabelBot.Text = "20 Hz";

                    SpectrogramLoading.Visibility = Visibility.Collapsed;
                    SpectrogramPanel.Visibility = Visibility.Visible;

                    // Apply visualizer mode
                    if (_visualizerMode)
                    {
                        SpectrogramImage.Visibility = Visibility.Collapsed;
                        VisualizerCanvas.Visibility = Visibility.Visible;
                        FreqLabelGrid.Visibility = Visibility.Collapsed;
                        if (_player.IsPlaying) StartVisualizer();
                    }
                    else
                    {
                        SpectrogramImage.Visibility = Visibility.Visible;
                        VisualizerCanvas.Visibility = Visibility.Collapsed;
                        FreqLabelGrid.Visibility = Visibility.Visible;
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
                    }
                    else
                    {
                        SpectrogramPlaceholder.Text = "Could not generate spectrogram for this file";
                        SpectrogramPlaceholder.Visibility = Visibility.Visible;
                    }
                }
            }
            catch (OperationCanceledException) { }
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
                    }
                    else
                    {
                        SpectrogramPlaceholder.Text = "Error generating spectrogram";
                        SpectrogramPlaceholder.Visibility = Visibility.Visible;
                    }
                }
            }
        }

        // ═══════════════════════════════════════════
        //  Audio Player
        // ═══════════════════════════════════════════

        private void PlayPause_Click(object sender, RoutedEventArgs e)
        {
            if (_player.IsPlaying)
            {
                _player.Pause();
                _playerTimer.Stop();

                // Fix: update cached playing state immediately to stop waveform progress advancing
                _isPlayingCached = false;

                UpdatePlayerUI();

                // Discord: show paused
                var file = FileGrid.SelectedItem as AudioFileInfo;
                _discord.UpdatePresence(file?.Artist, file?.Title, file?.FileName,
                    _player.TotalDuration, _player.CurrentPosition, true);
            }
            else if (_player.IsPaused)
            {
                _player.Resume();

                // Fix: restore cached playing state for smooth waveform interpolation
                _cachedPositionSec = _player.CurrentPosition.TotalSeconds;
                _cachedDurationSec = _player.TotalDuration.TotalSeconds;
                _cachedPositionTime = DateTime.UtcNow;
                _isPlayingCached = true;

                _playerTimer.Start();
                UpdatePlayerUI();

                // Discord: show playing again
                var file = FileGrid.SelectedItem as AudioFileInfo;
                _discord.UpdatePresence(file?.Artist, file?.Title, file?.FileName,
                    _player.TotalDuration, _player.CurrentPosition, false);
            }
            else if (FileGrid.SelectedItem is AudioFileInfo file2)
            {
                PlayFile(file2);
            }
        }

        private void Stop_Click(object sender, RoutedEventArgs e)
        {
            _player.Stop();
            _playerTimer.Stop();
            StopWaveformAnimation();
            WaveformCanvas.Children.Clear();
            UpdatePlayerUI();
            _discord.ClearPresence();
            _lastFm.TrackStopped();
        }

        private void Rewind_Click(object sender, RoutedEventArgs e)
        {
            if (_player.IsPlaying || _player.IsPaused)
            {
                _player.SeekRelative(-5);
                _lastSeekTime = DateTime.UtcNow;
                // Immediately update the UI slider to reflect new position
                if (_player.TotalDuration.TotalSeconds > 0)
                    SeekSlider.Value = _player.CurrentPosition.TotalSeconds;
                UpdatePlayerTimeText();
            }
        }

        private void Forward_Click(object sender, RoutedEventArgs e)
        {
            if (_player.IsPlaying || _player.IsPaused)
            {
                _player.SeekRelative(5);
                _lastSeekTime = DateTime.UtcNow;
                // Immediately update the UI slider to reflect new position
                if (_player.TotalDuration.TotalSeconds > 0)
                    SeekSlider.Value = _player.CurrentPosition.TotalSeconds;
                UpdatePlayerTimeText();
            }
        }

        private void Shuffle_Click(object sender, RoutedEventArgs e)
        {
            _shuffleMode = !_shuffleMode;
            UpdateShuffleUI();
        }

        private void UpdateShuffleUI()
        {
            if (ShuffleIcon != null)
            {
                ShuffleIcon.Foreground = _shuffleMode
                    ? (System.Windows.Media.Brush)FindResource("AccentColor")
                    : (System.Windows.Media.Brush)FindResource("TextMuted");
            }
        }

        /// <summary>
        /// Picks a random non-corrupt track from the list, avoiding recently played tracks via history.
        /// </summary>
        private AudioFileInfo? PickRandomTrack(List<AudioFileInfo> items)
        {
            // Build set of recently played paths to avoid
            var recentPaths = new HashSet<string>(_shuffleHistory, StringComparer.OrdinalIgnoreCase);

            // Also exclude the currently playing track
            if (_player.CurrentFile != null)
                recentPaths.Add(_player.CurrentFile);

            // First try: pick from tracks NOT in recent history
            var candidates = items.Where(f => f.Status != AudioStatus.Corrupt
                && !recentPaths.Contains(f.FilePath)).ToList();

            // If all tracks are in history (small playlist), allow any non-current track
            if (candidates.Count == 0)
            {
                candidates = items.Where(f => f.Status != AudioStatus.Corrupt
                    && !string.Equals(f.FilePath, _player.CurrentFile, StringComparison.OrdinalIgnoreCase)).ToList();
            }

            // If still nothing (only one track), allow replaying
            if (candidates.Count == 0)
            {
                candidates = items.Where(f => f.Status != AudioStatus.Corrupt).ToList();
            }

            if (candidates.Count == 0) return null;

            var picked = candidates[_shuffleRng.Next(candidates.Count)];

            // Add to history
            _shuffleHistory.Add(picked.FilePath);
            // Trim history to prevent it from growing too large
            while (_shuffleHistory.Count > ShuffleHistorySize)
                _shuffleHistory.RemoveAt(0);

            return picked;
        }

        private void PrevTrack_Click(object sender, RoutedEventArgs e)
        {
            var now = DateTime.UtcNow;
            bool isPlaying = _player.IsPlaying || _player.IsPaused;

            // If currently playing and more than 1.5s since last prev-click,
            // restart the current song instead of going back
            if (isPlaying && _player.CurrentPosition.TotalSeconds > 3
                && (now - _lastPrevClickTime).TotalSeconds > 1.5)
            {
                _lastPrevClickTime = now;
                _player.Seek(0);
                SeekSlider.Value = 0;
                UpdatePlayerTimeText();
                return;
            }

            _lastPrevClickTime = now;

            var items = _filteredView?.Cast<AudioFileInfo>().ToList();
            if (items == null || items.Count == 0) return;

            if (_shuffleMode)
            {
                var candidate = PickRandomTrack(items);
                if (candidate != null)
                {
                    FileGrid.SelectedItem = candidate;
                    FileGrid.ScrollIntoView(candidate);
                    if (candidate.Status != AudioStatus.Corrupt)
                        PlayFile(candidate);
                }
                return;
            }

            int currentIdx = -1;
            if (_player.CurrentFile != null)
                currentIdx = items.FindIndex(f => string.Equals(f.FilePath, _player.CurrentFile, StringComparison.OrdinalIgnoreCase));
            else if (FileGrid.SelectedItem is AudioFileInfo sel)
                currentIdx = items.IndexOf(sel);

            int prevIdx = currentIdx - 1;
            if (prevIdx < 0) prevIdx = items.Count - 1;

            var prevFile = items[prevIdx];
            FileGrid.SelectedItem = prevFile;
            FileGrid.ScrollIntoView(prevFile);
            if (prevFile.Status != AudioStatus.Corrupt)
                PlayFile(prevFile);
        }

        private void NextTrack_Click(object sender, RoutedEventArgs e)
        {
            // Check queue first
            if (_queue.Count > 0)
            {
                var nextFile = _queue[0];
                _queue.RemoveAt(0);
                if (nextFile.Status != AudioStatus.Corrupt)
                {
                    FileGrid.SelectedItem = nextFile;
                    FileGrid.ScrollIntoView(nextFile);
                    PlayFile(nextFile);
                    return;
                }
            }

            var items = _filteredView?.Cast<AudioFileInfo>().ToList();
            if (items == null || items.Count == 0) return;

            if (_shuffleMode)
            {
                var candidate = PickRandomTrack(items);
                if (candidate != null)
                {
                    FileGrid.SelectedItem = candidate;
                    FileGrid.ScrollIntoView(candidate);
                    if (candidate.Status != AudioStatus.Corrupt)
                        PlayFile(candidate);
                }
                return;
            }

            int currentIdx = -1;
            if (_player.CurrentFile != null)
                currentIdx = items.FindIndex(f => string.Equals(f.FilePath, _player.CurrentFile, StringComparison.OrdinalIgnoreCase));
            else if (FileGrid.SelectedItem is AudioFileInfo sel)
                currentIdx = items.IndexOf(sel);

            int nextIdx = currentIdx + 1;
            if (nextIdx >= items.Count) nextIdx = 0;

            var nextInList = items[nextIdx];
            FileGrid.SelectedItem = nextInList;
            FileGrid.ScrollIntoView(nextInList);
            if (nextInList.Status != AudioStatus.Corrupt)
                PlayFile(nextInList);
        }

        private void VolumeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            _player.Volume = (float)(VolumeSlider.Value / 100.0);
            if (VolumeLabel != null)
                VolumeLabel.Text = $"{(int)VolumeSlider.Value}%";
            if (VolumeIcon != null)
            {
                if (VolumeSlider.Value <= 0)
                    VolumeIcon.Text = "\uE74F"; // Muted icon
                else if (VolumeSlider.Value < 50)
                    VolumeIcon.Text = "\uE993"; // Low volume icon
                else
                    VolumeIcon.Text = "\uE767"; // Normal volume icon
            }
        }

        private void VolumeIcon_Click(object sender, MouseButtonEventArgs e)
        {
            if (_isMuted)
            {
                // Unmute: restore previous volume
                _isMuted = false;
                VolumeSlider.Value = _preMuteVolume;
            }
            else
            {
                // Mute: save current volume and set to 0
                _isMuted = true;
                _preMuteVolume = VolumeSlider.Value;
                VolumeSlider.Value = 0;
            }
        }

        private void SeekSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            // When user clicks or drags, immediately seek
            if (_isSeeking && _player.TotalDuration.TotalSeconds > 0)
            {
                double pos = SeekSlider.Value / SeekSlider.Maximum * _player.TotalDuration.TotalSeconds;
                _player.Seek(pos);
                _lastSeekTime = DateTime.UtcNow;
            }
        }

        private void SeekSlider_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            _isSeeking = true;
        }

        private void SeekSlider_MouseUp(object sender, MouseButtonEventArgs e)
        {
            if (_player.TotalDuration.TotalSeconds > 0)
            {
                double pos = SeekSlider.Value / SeekSlider.Maximum * _player.TotalDuration.TotalSeconds;
                _player.Seek(pos);
                _lastSeekTime = DateTime.UtcNow;
            }
            _isSeeking = false;
        }

        private void SeekSlider_DragCompleted(object sender, DragCompletedEventArgs e)
        {
            if (_player.TotalDuration.TotalSeconds > 0)
            {
                double pos = SeekSlider.Value / SeekSlider.Maximum * _player.TotalDuration.TotalSeconds;
                _player.Seek(pos);
                _lastSeekTime = DateTime.UtcNow;
            }
            _isSeeking = false;
        }

        private void PlayFile(AudioFileInfo file)
        {
            try
            {
                bool normalize = ThemeManager.AudioNormalization;
                bool crossfade = ThemeManager.Crossfade;

                // Apply crossfade duration setting
                _player.CrossfadeDurationSeconds = ThemeManager.CrossfadeDuration;

                // ALWAYS stop current playback cleanly first to prevent audio bleed
                // The crossfade path handles its own stop internally
                if (crossfade && _player.IsPlaying)
                {
                    _player.PlayWithCrossfade(file.FilePath, normalize);
                }
                else
                {
                    _player.Stop();
                    _playerTimer.Stop();
                    // Small delay to let NAudio release resources
                    System.Threading.Thread.Sleep(30);
                    _player.Play(file.FilePath, normalize);
                }

                _player.Volume = (float)(VolumeSlider.Value / 100.0);
                SeekSlider.Maximum = _player.TotalDuration.TotalSeconds;
                _playerTimer.Start();
                UpdatePlayerUI();
                DrawWaveformBackground();
                if (_visualizerMode) StartVisualizer();

                // Update spectrogram/visualizer title to reflect the now-playing file
                _currentSpectrogramFile = file;
                SpectrogramTitle.Text = BuildSpectrogramTitle(file);

                // Update album cover for the playing track
                UpdateAlbumCover();

                // Discord Rich Presence
                _discord.UpdatePresence(file.Artist, file.Title, file.FileName,
                    _player.TotalDuration, TimeSpan.Zero, false);

                // Last.fm now playing
                _lastFm.TrackStarted(file.Artist, file.Title, _player.TotalDuration.TotalSeconds);
            }
            catch (Exception ex)
            {
                ErrorDialog.Show("Playback Error", $"Cannot play this file:\n{ex.Message}", this);
            }
        }

        private void PlayFile_Click(object sender, RoutedEventArgs e)
        {
            if (FileGrid.SelectedItem is AudioFileInfo file)
                PlayFile(file);
        }

        private void FileGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (FileGrid.SelectedItem is AudioFileInfo file)
                PlayFile(file);
        }

        /// <summary>
        /// Handles horizontal scrolling in the DataGrid via touchpad/Shift+scroll.
        /// WPF DataGrid doesn't natively handle horizontal scroll gestures from precision touchpads.
        /// </summary>
        private void FileGrid_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            // Find the ScrollViewer inside the DataGrid
            var scrollViewer = FindVisualChild<ScrollViewer>(FileGrid);
            if (scrollViewer == null) return;

            // Shift+scroll → horizontal scroll
            if (Keyboard.Modifiers == ModifierKeys.Shift)
            {
                scrollViewer.ScrollToHorizontalOffset(scrollViewer.HorizontalOffset - e.Delta);
                e.Handled = true;
            }
        }

        /// <summary>
        /// Finds a child of the specified type in the visual tree.
        /// </summary>
        private static T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is T typedChild) return typedChild;
                var found = FindVisualChild<T>(child);
                if (found != null) return found;
            }
            return null;
        }

        private void PlayerTimer_Tick(object? sender, EventArgs e)
        {
            // Cache position for smooth waveform interpolation
            _cachedPositionSec = _player.CurrentPosition.TotalSeconds;
            _cachedDurationSec = _player.TotalDuration.TotalSeconds;
            _cachedPositionTime = DateTime.UtcNow;
            _isPlayingCached = _player.IsPlaying;

            // Add cooldown after seek to let NAudio catch up, prevents snap-back
            bool seekCooldown = (DateTime.UtcNow - _lastSeekTime).TotalMilliseconds < 500;
            if (!_isSeeking && !seekCooldown && _cachedDurationSec > 0)
            {
                SeekSlider.Value = _cachedPositionSec;
                UpdateWaveformProgress();
            }

            UpdatePlayerTimeText();

            // Last.fm scrobble check
            if (_lastFm.IsEnabled)
                _lastFm.UpdatePlayback(_player.CurrentPosition.TotalSeconds);

            // Discord Rich Presence — service handles its own throttling
            if (_discord.IsEnabled)
            {
                var file = FileGrid.SelectedItem as AudioFileInfo;
                _discord.UpdatePresence(file?.Artist, file?.Title, file?.FileName,
                    _player.TotalDuration, _player.CurrentPosition, false);
            }
        }

        private void Player_PlaybackStopped(object? sender, EventArgs e)
        {
            Dispatcher.Invoke(() =>
            {
                // Guard against spurious stop events while audio is still playing
                if (_player.IsPlaying)
                {
                    if (!_playerTimer.IsEnabled)
                        _playerTimer.Start();
                    // Ensure waveform animation stays alive
                    StartWaveformAnimation();
                    return;
                }
                // If paused, keep animation but stop timer
                if (_player.IsPaused)
                {
                    return;
                }
                _playerTimer.Stop();
                UpdatePlayerUI();
            });
        }

        private void Player_TrackFinished(object? sender, EventArgs e)
        {
            Dispatcher.Invoke(() =>
            {
                if (!ThemeManager.AutoPlayNext) return;

                // If queue has items, play from queue first
                if (_queue.Count > 0)
                {
                    var nextFile = _queue[0];
                    _queue.RemoveAt(0);
                    if (nextFile.Status != AudioStatus.Corrupt)
                    {
                        FileGrid.SelectedItem = nextFile;
                        FileGrid.ScrollIntoView(nextFile);
                        PlayFile(nextFile);
                        // Update spectrogram/visualizer title for the new track
                        _currentSpectrogramFile = nextFile;
                        SpectrogramTitle.Text = BuildSpectrogramTitle(nextFile);
                        return;
                    }
                }

                // Otherwise find current file in the filtered view and play next
                var items = _filteredView?.Cast<AudioFileInfo>().ToList();
                if (items == null || items.Count == 0) return;

                // Shuffle mode: pick a random track
                if (_shuffleMode)
                {
                    var randomTrack = PickRandomTrack(items);
                    if (randomTrack != null)
                    {
                        FileGrid.SelectedItem = randomTrack;
                        FileGrid.ScrollIntoView(randomTrack);
                        PlayFile(randomTrack);
                        _currentSpectrogramFile = randomTrack;
                        SpectrogramTitle.Text = BuildSpectrogramTitle(randomTrack);
                    }
                    return;
                }

                int currentIdx = -1;
                string? currentPath = _player.CurrentFile;
                if (currentPath != null)
                {
                    currentIdx = items.FindIndex(f => string.Equals(f.FilePath, currentPath, StringComparison.OrdinalIgnoreCase));
                }

                int nextIdx = currentIdx + 1;
                if (nextIdx >= items.Count) return; // end of list

                var nextInList = items[nextIdx];
                if (nextInList.Status == AudioStatus.Corrupt) return;

                FileGrid.SelectedItem = nextInList;
                FileGrid.ScrollIntoView(nextInList);
                PlayFile(nextInList);
                // Update spectrogram/visualizer title for the new track
                _currentSpectrogramFile = nextInList;
                SpectrogramTitle.Text = BuildSpectrogramTitle(nextInList);
            });
        }

        private void UpdatePlayerUI()
        {
            if (_player.IsPlaying)
                PlayPauseIcon.Text = "\uE769"; // Pause icon
            else
                PlayPauseIcon.Text = "\uE768"; // Play icon

            PlayerFileText.Text = _player.CurrentFile != null
                ? IOPath.GetFileName(_player.CurrentFile)
                : "";

            UpdatePlayerTimeText();
        }

        private void UpdatePlayerTimeText()
        {
            var cur = _player.CurrentPosition;
            var tot = _player.TotalDuration;
            PlayerTimeText.Text = $"{FormatTime(cur)} / {FormatTime(tot)}";
        }

        private static string FormatTime(TimeSpan ts)
        {
            if (ts.TotalHours >= 1)
                return $"{(int)ts.TotalHours}:{ts.Minutes:D2}:{ts.Seconds:D2}";
            return $"{ts.Minutes}:{ts.Seconds:D2}";
        }

        // ═══════════════════════════════════════════
        //  Drag & Drop (into the app)
        // ═══════════════════════════════════════════

        private void Window_DragOver(object sender, DragEventArgs e)
        {
            // Ignore drags that originated from our own grid
            if (_isOutboundDrag)
            {
                e.Effects = DragDropEffects.None;
                e.Handled = true;
                return;
            }

            if (e.Data.GetDataPresent(DataFormats.FileDrop))
                e.Effects = DragDropEffects.Copy;
            else
                e.Effects = DragDropEffects.None;
            e.Handled = true;
        }

        private void Window_Drop(object sender, DragEventArgs e)
        {
            // Ignore drops from our own outbound drag
            if (_isOutboundDrag) return;

            if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;

            var droppedPaths = (string[])e.Data.GetData(DataFormats.FileDrop)!;
            var audioFiles = new List<string>();

            foreach (var path in droppedPaths)
            {
                if (Directory.Exists(path))
                {
                    audioFiles.AddRange(
                        Directory.EnumerateFiles(path, "*.*", SearchOption.AllDirectories)
                                 .Where(f => SupportedExtensions.Contains(IOPath.GetExtension(f))));
                }
                else if (File.Exists(path) && SupportedExtensions.Contains(IOPath.GetExtension(path)))
                {
                    audioFiles.Add(path);
                }
            }

            if (audioFiles.Count > 0)
                _ = AnalyzeAndAddFiles(audioFiles.ToArray());
        }

        // ═══════════════════════════════════════════
        //  Drag FROM grid to Explorer / Mp3tag
        //  Uses DataGrid.PreviewMouseMove only on actual rows
        // ═══════════════════════════════════════════

        protected override void OnPreviewMouseLeftButtonDown(MouseButtonEventArgs e)
        {
            base.OnPreviewMouseLeftButtonDown(e);
            _isOutboundDrag = false;
        }

        protected override void OnPreviewMouseMove(MouseEventArgs e)
        {
            base.OnPreviewMouseMove(e);

            if (e.LeftButton != MouseButtonState.Pressed) return;
            if (_isOutboundDrag) return;

            // Only start drag if the mouse is over a DataGridRow (not scrollbar, header, splitter)
            if (e.OriginalSource is not DependencyObject dep) return;
            var row = FindParent<DataGridRow>(dep);
            if (row == null) return;

            if (FileGrid.SelectedItem is AudioFileInfo file && File.Exists(file.FilePath))
            {
                _isOutboundDrag = true;
                var data = new DataObject(DataFormats.FileDrop, new[] { file.FilePath });
                DragDrop.DoDragDrop(FileGrid, data, DragDropEffects.Copy);
                _isOutboundDrag = false;
            }
        }

        private static T? FindParent<T>(DependencyObject child) where T : DependencyObject
        {
            var current = VisualTreeHelper.GetParent(child);
            while (current != null)
            {
                if (current is T found) return found;
                current = VisualTreeHelper.GetParent(current);
            }
            return null;
        }

        // ═══════════════════════════════════════════
        //  Context Menu
        // ═══════════════════════════════════════════

        private void OpenFileLocation_Click(object sender, RoutedEventArgs e)
        {
            if (FileGrid.SelectedItem is AudioFileInfo file)
                Process.Start("explorer.exe", $"/select,\"{file.FilePath}\"");
        }

        private void CopyPath_Click(object sender, RoutedEventArgs e)
        {
            if (FileGrid.SelectedItem is AudioFileInfo file)
                Clipboard.SetText(file.FilePath);
        }

        private void CopyFileName_Click(object sender, RoutedEventArgs e)
        {
            if (FileGrid.SelectedItem is AudioFileInfo file)
                Clipboard.SetText(file.FileName);
        }

        private void RemoveFromList_Click(object sender, RoutedEventArgs e)
        {
            if (FileGrid.SelectedItem is AudioFileInfo file)
                _files.Remove(file);
        }

        // ═══════════════════════════════════════════
        //  Album Cover
        // ═══════════════════════════════════════════

        private bool _showAlbumCover;

        private void ToggleAlbumCover_Click(object sender, RoutedEventArgs e)
        {
            _showAlbumCover = !_showAlbumCover;
            AlbumCoverToggleText.Text = _showAlbumCover ? "♪ Hide" : "♪ Cover";

            if (_showAlbumCover)
            {
                AlbumCoverColumn.Width = new GridLength(210);
                AlbumCoverPanel.Visibility = Visibility.Visible;
                UpdateAlbumCover();
            }
            else
            {
                AlbumCoverColumn.Width = new GridLength(0);
                AlbumCoverPanel.Visibility = Visibility.Collapsed;
                AlbumCoverImage.Source = null;
            }
        }

        private void UpdateAlbumCover()
        {
            if (!_showAlbumCover) return;

            // Prefer the currently playing file, fallback to selected
            AudioFileInfo? file = null;
            if (_player.CurrentFile != null)
                file = _files.FirstOrDefault(f => string.Equals(f.FilePath, _player.CurrentFile, StringComparison.OrdinalIgnoreCase));
            if (file == null)
                file = FileGrid.SelectedItem as AudioFileInfo;

            if (file == null || string.IsNullOrEmpty(file.FilePath))
            {
                AlbumCoverImage.Source = null;
                return;
            }

            try
            {
                var cover = ExtractAlbumCover(file.FilePath);
                AlbumCoverImage.Source = cover;
            }
            catch
            {
                AlbumCoverImage.Source = null;
            }
        }

        private static BitmapSource? ExtractAlbumCover(string filePath)
        {
            try
            {
                using var tagFile = TagLib.File.Create(filePath);
                var pictures = tagFile.Tag.Pictures;
                if (pictures == null || pictures.Length == 0) return null;

                var pic = pictures[0];
                using var ms = new MemoryStream(pic.Data.Data);
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.StreamSource = ms;
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.DecodePixelWidth = 400; // limit memory usage
                bitmap.EndInit();
                bitmap.Freeze();
                return bitmap;
            }
            catch
            {
                return null;
            }
        }

        private void ViewAlbumCover_Click(object sender, RoutedEventArgs e)
        {
            if (FileGrid.SelectedItem is not AudioFileInfo file) return;

            try
            {
                var cover = ExtractAlbumCover(file.FilePath);
                if (cover == null)
                {
                    ErrorDialog.Show("No Album Cover", "This file does not contain an album cover image.", this);
                    return;
                }

                // Show in a popup window
                var window = new Window
                {
                    Title = $"Album Cover — {file.Artist} - {file.Title}".Trim(' ', '-', '—'),
                    Width = Math.Min(cover.PixelWidth + 40, 800),
                    Height = Math.Min(cover.PixelHeight + 60, 800),
                    WindowStartupLocation = WindowStartupLocation.CenterOwner,
                    Owner = this,
                    Background = new SolidColorBrush(Color.FromRgb(30, 30, 30)),
                    ResizeMode = ResizeMode.CanResize
                };

                // Apply dark title bar
                window.SourceInitialized += (s, _) =>
                {
                    try
                    {
                        var hwnd = new WindowInteropHelper(window).Handle;
                        int darkMode = 1;
                        DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref darkMode, sizeof(int));
                        int color = 0x001E1E1E; // RGB(30,30,30) as COLORREF
                        DwmSetWindowAttribute(hwnd, DWMWA_CAPTION_COLOR, ref color, sizeof(int));
                    }
                    catch { }
                };

                var image = new System.Windows.Controls.Image
                {
                    Source = cover,
                    Stretch = System.Windows.Media.Stretch.Uniform,
                    Margin = new Thickness(10)
                };
                window.Content = image;
                window.ShowDialog();
            }
            catch (Exception ex)
            {
                ErrorDialog.Show("Error", $"Could not load album cover:\n{ex.Message}", this);
            }
        }

        // ═══════════════════════════════════════════
        //  Settings
        // ═══════════════════════════════════════════

        private void Settings_Click(object sender, RoutedEventArgs e)
        {
            var settingsWindow = new SettingsWindow { Owner = this };
            settingsWindow.ShowDialog();
            // Refresh service button labels after settings change
            UpdateServiceButtonLabels();
            // Refresh title bar color after theme change
            ApplyThemeTitleBar();
            // Re-theme equalizer sliders
            _eqSliderTemplateCache = null;
            InitializeEqualizerSliders();
            ChkEqEnabled.IsChecked = ThemeManager.EqualizerEnabled;

            // Sync Discord RPC state
            if (ThemeManager.DiscordRpcEnabled && !_discord.IsEnabled)
                _discord.Enable();
            else if (!ThemeManager.DiscordRpcEnabled && _discord.IsEnabled)
                _discord.Disable();

            // Sync spatial audio state
            var spatial = _player.CurrentSpatialAudio;
            if (spatial != null) spatial.Enabled = ThemeManager.SpatialAudioEnabled;

            // Sync Last.fm state
            if (!string.IsNullOrEmpty(ThemeManager.LastFmSessionKey))
                _lastFm.Configure(ThemeManager.LastFmApiKey, ThemeManager.LastFmApiSecret, ThemeManager.LastFmSessionKey);

            // Update Last.fm status indicator
            UpdateLastFmStatusIndicator();
        }

        // ═══════════════════════════════════════════
        //  Queue
        // ═══════════════════════════════════════════

        private void Queue_Click(object sender, RoutedEventArgs e)
        {
            var queueWindow = new QueueWindow(_queue) { Owner = this };
            queueWindow.ShowDialog();
        }

        private void AddToQueue_Click(object sender, RoutedEventArgs e)
        {
            if (FileGrid.SelectedItem is AudioFileInfo file && file.Status != AudioStatus.Corrupt)
            {
                _queue.Add(file);
                StatusText.Text = $"Added to queue: {file.FileName} ({_queue.Count} in queue)";
            }
        }

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
            string statusDisplay = StatusDisplayText(file.Status);
            string statusExtra = file.HasClipping ? " | CLIPPING DETECTED" : "";

            var extras = new List<string>();
            if (file.Bpm > 0) extras.Add($"BPM: {file.Bpm}");
            if (file.IsMqa) extras.Add($"MQA: {file.MqaDisplay}");
            if (file.IsAiGenerated) extras.Add($"AI: {file.AiSource}");
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
            var forceVector = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Spotify", "Tidal" };

            for (int i = 0; i < 6; i++)
            {
                string svc = ThemeManager.MusicServiceSlots[i];
                images[i].Source = CreateServiceLogo(svc, i, forceVector.Contains(svc));
                buttons[i].ToolTip = svc == "Custom..." ? "Search on custom service" : $"Search on {svc}";
            }
        }

        private static ImageSource CreateServiceLogo(string service, int slotIndex = -1, bool forceVector = false)
        {
            // Use embedded PNGs for services that have them (unless forceVector is set)
            if (!forceVector && (service == "Qobuz" || service == "Spotify" || service == "Amazon Music" || service == "Tidal" || service == "YouTube Music" || service == "Apple Music"))
            {
                try
                {
                    string pngName = service switch
                    {
                        "Spotify" => "Spotify.png",
                        "YouTube Music" => "YTM.png",
                        "Tidal" => "Tidal.png",
                        "Qobuz" => "Qobuz.png",
                        "Amazon Music" => "Amazon-music.png",
                        "Apple Music" => "Apple_music.png",
                        _ => ""
                    };
                    if (!string.IsNullOrEmpty(pngName))
                    {
                        var bmp = new BitmapImage();
                        bmp.BeginInit();
                        bmp.UriSource = new Uri($"pack://application:,,,/{pngName}");
                        bmp.CacheOption = BitmapCacheOption.OnLoad;
                        bmp.DecodePixelWidth = 56;
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
                string iconPath = (slotIndex >= 0 && slotIndex < 6) ? ThemeManager.CustomServiceIcons[slotIndex] : "";
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

        // ═══════════════════════════════════════════
        //  Save Spectrogram (single)
        // ═══════════════════════════════════════════

        private void SaveSpectrogram_Click(object sender, RoutedEventArgs e)
        {
            if (FileGrid.SelectedItem is AudioFileInfo file)
                SaveSpectrogramForFile(file);
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
                    var bitmap = RenderSpectrogramWithLabels(file, 1800, 600);
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
        private BitmapSource? RenderSpectrogramWithLabels(AudioFileInfo file, int spectWidth, int spectHeight, BitmapSource? preGenerated = null)
        {
            var rawBitmap = preGenerated ?? SpectrogramGenerator.Generate(file.FilePath, spectWidth, spectHeight);
            if (rawBitmap == null) return null;

            int leftMargin = 70;   // Hz labels
            int topMargin = 28;    // Title bar
            int bottomMargin = 4;
            int rightMargin = 4;

            int totalWidth = leftMargin + spectWidth + rightMargin;
            int totalHeight = topMargin + spectHeight + bottomMargin;

            var dv = new DrawingVisual();
            using (var dc = dv.RenderOpen())
            {
                // Background
                dc.DrawRectangle(Brushes.Black, null, new System.Windows.Rect(0, 0, totalWidth, totalHeight));

                // Draw spectrogram
                dc.DrawImage(rawBitmap, new System.Windows.Rect(leftMargin, topMargin, spectWidth, spectHeight));

                // Title
                var titleText = new FormattedText(
                    $"{file.FileName}  —  {file.SampleRate:N0} Hz / {file.BitsPerSampleDisplay}  —  {file.Duration}  —  Status: {file.Status}",
                    System.Globalization.CultureInfo.InvariantCulture,
                    FlowDirection.LeftToRight,
                    new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal),
                    13, Brushes.White, 96);
                dc.DrawText(titleText, new System.Windows.Point(leftMargin + 4, 6));

                // Hz labels (5 labels on log-frequency scale)
                int nyquist = file.SampleRate / 2;
                double logMinF = Math.Log10(20.0);
                double logMaxF = Math.Log10(nyquist);
                double logRangeF = logMaxF - logMinF;

                string topHz = $"{nyquist:N0} Hz";
                string upperMidHz = $"{(int)Math.Pow(10, logMinF + 0.75 * logRangeF):N0} Hz";
                string midHz = $"{(int)Math.Pow(10, logMinF + 0.5 * logRangeF):N0} Hz";
                string lowerMidHz = $"{(int)Math.Pow(10, logMinF + 0.25 * logRangeF):N0} Hz";
                string botHz = "20 Hz";

                var labelBrush = new SolidColorBrush(Color.FromRgb(180, 180, 180));
                labelBrush.Freeze();
                var labelTypeFace = new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);

                var ftTop = new FormattedText(topHz, System.Globalization.CultureInfo.InvariantCulture,
                    FlowDirection.LeftToRight, labelTypeFace, 11, labelBrush, 96);
                dc.DrawText(ftTop, new System.Windows.Point(leftMargin - ftTop.Width - 6, topMargin + 2));

                var ftUpperMid = new FormattedText(upperMidHz, System.Globalization.CultureInfo.InvariantCulture,
                    FlowDirection.LeftToRight, labelTypeFace, 11, labelBrush, 96);
                dc.DrawText(ftUpperMid, new System.Windows.Point(leftMargin - ftUpperMid.Width - 6, topMargin + spectHeight * 0.25 - ftUpperMid.Height / 2));

                var ftMid = new FormattedText(midHz, System.Globalization.CultureInfo.InvariantCulture,
                    FlowDirection.LeftToRight, labelTypeFace, 11, labelBrush, 96);
                dc.DrawText(ftMid, new System.Windows.Point(leftMargin - ftMid.Width - 6, topMargin + spectHeight / 2 - ftMid.Height / 2));

                var ftLowerMid = new FormattedText(lowerMidHz, System.Globalization.CultureInfo.InvariantCulture,
                    FlowDirection.LeftToRight, labelTypeFace, 11, labelBrush, 96);
                dc.DrawText(ftLowerMid, new System.Windows.Point(leftMargin - ftLowerMid.Width - 6, topMargin + spectHeight * 0.75 - ftLowerMid.Height / 2));

                var ftBot = new FormattedText(botHz, System.Globalization.CultureInfo.InvariantCulture,
                    FlowDirection.LeftToRight, labelTypeFace, 11, labelBrush, 96);
                dc.DrawText(ftBot, new System.Windows.Point(leftMargin - ftBot.Width - 6, topMargin + spectHeight - ftBot.Height - 2));
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

            AnalysisProgress.Visibility = Visibility.Visible;
            AnalysisProgress.Maximum = total;
            AnalysisProgress.Value = 0;
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
                        SpectrogramGenerator.Generate(fileRef.FilePath, 1800, 600));

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
            }

            AnalysisProgress.Visibility = Visibility.Collapsed;
            string msg = failed > 0
                ? $"Saved {completed - failed} / {total} spectrograms to {folder} ({failed} failed)"
                : $"Saved {completed} spectrograms to {folder}";
            StatusText.Text = msg;
        }

        // ═══════════════════════════════════════════
        //  Animated Waveform Visualization
        // ═══════════════════════════════════════════

        private double[] _waveformBaseData = Array.Empty<double>();

        /// <summary>
        /// Generates a set of pre-computed waveform amplitudes for the background visualization.
        /// Uses a seeded pseudo-random wave pattern so each song gets a unique but consistent look.
        /// </summary>
        private void DrawWaveformBackground()
        {
            WaveformCanvas.Children.Clear();

            double canvasWidth = WaveformCanvas.ActualWidth;
            double canvasHeight = WaveformCanvas.ActualHeight;
            if (canvasWidth < 10 || canvasHeight < 5) return;

            int points = (int)canvasWidth;
            _waveformData = new double[points];
            _waveformBaseData = new double[points];

            // Generate a nice wavy pattern using layered sine waves
            int seed = (_player.CurrentFile ?? "").GetHashCode();
            var rng = new Random(seed);

            double freq1 = 2 + rng.NextDouble() * 3;
            double freq2 = 8 + rng.NextDouble() * 10;
            double freq3 = 20 + rng.NextDouble() * 30;
            double phase1 = rng.NextDouble() * Math.PI * 2;
            double phase2 = rng.NextDouble() * Math.PI * 2;
            double phase3 = rng.NextDouble() * Math.PI * 2;

            for (int i = 0; i < points; i++)
            {
                double t = (double)i / points;
                double wave = 0.5 * Math.Sin(freq1 * Math.PI * t + phase1)
                            + 0.3 * Math.Sin(freq2 * Math.PI * t + phase2)
                            + 0.2 * Math.Sin(freq3 * Math.PI * t + phase3)
                            + 0.15 * Math.Sin(1.5 * Math.PI * t + phase1 * 0.7)
                            + 0.1 * Math.Sin(0.8 * Math.PI * t + phase2 * 1.3);
                _waveformBaseData[i] = wave; // raw -1..1 value for animation
                _waveformData[i] = Math.Clamp((wave + 1.25) / 2.5, 0.25, 0.95); // normalized with guaranteed minimum
            }

            // Start animation
            _waveformAnimStart = DateTime.UtcNow;
            StartWaveformAnimation();
        }

        private void StartWaveformAnimation()
        {
            if (!_waveformAnimActive)
            {
                _waveformAnimActive = true;
                CompositionTarget.Rendering += WaveformAnimation_Tick;
            }
        }

        private void StopWaveformAnimation()
        {
            if (_waveformAnimActive)
            {
                _waveformAnimActive = false;
                CompositionTarget.Rendering -= WaveformAnimation_Tick;
            }
        }

        private void WaveformAnimation_Tick(object? sender, EventArgs e)
        {
            if (_waveformBaseData.Length == 0) return;
            // Keep animation alive while a track is loaded, even if momentarily paused
            if (!_player.IsPlaying && !_player.IsPaused && _player.CurrentFile == null) return;

            // Auto-restart the player timer if it was lost during a spurious stop event
            if (_player.IsPlaying && !_playerTimer.IsEnabled)
                _playerTimer.Start();

            double canvasWidth = WaveformCanvas.ActualWidth;
            double canvasHeight = WaveformCanvas.ActualHeight;
            if (canvasWidth < 10 || canvasHeight < 5) return;

            double elapsed = (DateTime.UtcNow - _waveformAnimStart).TotalSeconds;
            var playbarColors = ThemeManager.GetPlaybarColors();
            double animSpeed = playbarColors.AnimationSpeed;

            // Animate base data with time-varying phase
            int points = _waveformBaseData.Length;
            double mid = canvasHeight / 2;

            // Fade envelope: gentle taper at edges (3% on each side)
            double fadeRegion = 0.03;

            // Update animated waveform data
            for (int i = 0; i < points; i++)
            {
                double t = (double)i / points;
                // Add time-varying oscillation to the base wave
                double anim = _waveformBaseData[i]
                    + 0.15 * Math.Sin(4 * Math.PI * t + elapsed * animSpeed * 2.5)
                    + 0.1 * Math.Sin(7 * Math.PI * t - elapsed * animSpeed * 1.8)
                    + 0.08 * Math.Sin(12 * Math.PI * t + elapsed * animSpeed * 3.2);
                _waveformData[i] = Math.Clamp((anim + 1.33) / 2.66, 0.25, 0.95);
            }

            WaveformCanvas.Children.Clear();

            // Draw full background wave (dim)
            var bgBrush = new SolidColorBrush(playbarColors.BackgroundColor);
            bgBrush.Freeze();

            var bgGeometry = new StreamGeometry();
            using (var ctx = bgGeometry.Open())
            {
                ctx.BeginFigure(new Point(0, mid), true, true);
                for (int i = 0; i < points && i < (int)canvasWidth; i++)
                {
                    double t = (double)i / points;
                    double envelope = WaveformEnvelope(t, fadeRegion);
                    double amp = _waveformData[i] * mid * 0.85 * envelope;
                    ctx.LineTo(new Point(i, mid - amp), true, false);
                }
                for (int i = Math.Min(points, (int)canvasWidth) - 1; i >= 0; i--)
                {
                    double t = (double)i / points;
                    double envelope = WaveformEnvelope(t, fadeRegion);
                    double amp = _waveformData[i] * mid * 0.85 * envelope;
                    ctx.LineTo(new Point(i, mid + amp), true, false);
                }
            }
            bgGeometry.Freeze();

            var bgPath = new System.Windows.Shapes.Path
            {
                Data = bgGeometry,
                Fill = bgBrush,
                IsHitTestVisible = false
            };
            WaveformCanvas.Children.Add(bgPath);

            // Draw progress overlay — derive from SeekSlider value for perfect sync with thumb
            double progress;
            if (_isSeeking)
            {
                // During seeking, use the slider's value directly
                progress = SeekSlider.Maximum > 0 ? SeekSlider.Value / SeekSlider.Maximum : 0;
            }
            else if (_cachedDurationSec > 0)
            {
                // Use interpolated time but clamp to slider's range for consistency
                double interpSec = _cachedPositionSec;
                if (_isPlayingCached)
                {
                    double dt = (DateTime.UtcNow - _cachedPositionTime).TotalSeconds;
                    // Clamp interpolation to max 150ms ahead to prevent overshoot
                    dt = Math.Min(dt, 0.15);
                    interpSec = Math.Min(_cachedPositionSec + dt, _cachedDurationSec);
                }
                progress = interpSec / _cachedDurationSec;
            }
            else
            {
                progress = 0;
            }
            progress = Math.Clamp(progress, 0, 1);
            int progressPixel = (int)(progress * canvasWidth);

            if (progressPixel > 0)
            {
                var gradientColors = playbarColors.ProgressGradient;
                var gradient = new LinearGradientBrush(
                    new GradientStopCollection
                    {
                        new GradientStop(gradientColors[0], 0),
                        new GradientStop(gradientColors[1], 0.5),
                        new GradientStop(gradientColors[2], 1.0)
                    }, new Point(0, 0), new Point(1, 0));
                gradient.Freeze();

                var progGeometry = new StreamGeometry();
                using (var ctx = progGeometry.Open())
                {
                    ctx.BeginFigure(new Point(0, mid), true, true);
                    for (int i = 0; i < progressPixel && i < points; i++)
                    {
                        double t = (double)i / points;
                        double envelope = WaveformEnvelope(t, fadeRegion);
                        double amp = _waveformData[i] * mid * 0.85 * envelope;
                        ctx.LineTo(new Point(i, mid - amp), true, false);
                    }
                    for (int i = Math.Min(progressPixel, points) - 1; i >= 0; i--)
                    {
                        double t = (double)i / points;
                        double envelope = WaveformEnvelope(t, fadeRegion);
                        double amp = _waveformData[i] * mid * 0.85 * envelope;
                        ctx.LineTo(new Point(i, mid + amp), true, false);
                    }
                }
                progGeometry.Freeze();

                var progPath = new System.Windows.Shapes.Path
                {
                    Data = progGeometry,
                    Fill = gradient,
                    IsHitTestVisible = false
                };
                WaveformCanvas.Children.Add(progPath);

                // Add a bright leading edge line at progress position
                if (progressPixel > 1 && progressPixel < points)
                {
                    double edgeT = (double)progressPixel / points;
                    double edgeEnvelope = WaveformEnvelope(edgeT, fadeRegion);
                    double amp = _waveformData[progressPixel] * mid * 0.85 * edgeEnvelope;
                    var edgeLine = new System.Windows.Shapes.Line
                    {
                        X1 = progressPixel, Y1 = mid - amp,
                        X2 = progressPixel, Y2 = mid + amp,
                        Stroke = new SolidColorBrush(gradientColors[2]),
                        StrokeThickness = 2,
                        IsHitTestVisible = false
                    };
                    WaveformCanvas.Children.Add(edgeLine);
                }
            }
        }

        /// <summary>
        /// Smooth fade envelope: returns 0.4..1. Fades in over [0..fadeRegion] and out over [1-fadeRegion..1]
        /// using a smooth cubic (smoothstep) curve. High minimum keeps edges visible.
        /// </summary>
        private static double WaveformEnvelope(double t, double fadeRegion)
        {
            double fadeIn = t < fadeRegion ? SmoothStep(t / fadeRegion) : 1.0;
            double fadeOut = t > (1.0 - fadeRegion) ? SmoothStep((1.0 - t) / fadeRegion) : 1.0;
            double env = fadeIn * fadeOut;
            return 0.4 + 0.6 * env; // always at least 40% visible at edges
        }

        /// <summary>
        /// Hermite smoothstep: 3t^2 - 2t^3 for smooth [0..1] transition.
        /// </summary>
        private static double SmoothStep(double x)
        {
            x = Math.Clamp(x, 0.0, 1.0);
            return x * x * (3.0 - 2.0 * x);
        }

        private void UpdateWaveformProgress()
        {
            // Animation tick handles everything now via CompositionTarget.Rendering
        }

        // ═══════════════════════════════════════════
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
                StartVisualizer();
            }
            else
            {
                VisualizerCanvas.Visibility = Visibility.Collapsed;
                SpectrogramImage.Visibility = Visibility.Visible;
                FreqLabelGrid.Visibility = Visibility.Visible;
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

        private void StartVisualizer()
        {
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
            }
        }

        private double[] _vizSmoothed = new double[64];
        private System.Windows.Shapes.Rectangle[]? _vizBars;
        private SolidColorBrush[]? _vizBrushes;
        private TimeSpan _lastVizRenderTime = TimeSpan.Zero;

        private void Visualizer_Tick(object? sender, EventArgs e)
        {
            if (!_player.IsPlaying && !_player.IsPaused)
            {
                if (VisualizerCanvas.Children.Count > 0)
                    VisualizerCanvas.Children.Clear();
                _vizBars = null;
                return;
            }

            // Use precise rendering time for frame limiting (~60fps)
            if (e is RenderingEventArgs re)
            {
                if ((re.RenderingTime - _lastVizRenderTime).TotalMilliseconds < 16) return;
                _lastVizRenderTime = re.RenderingTime;
            }

            double width = VisualizerCanvas.ActualWidth;
            double height = VisualizerCanvas.ActualHeight;
            if (width < 10 || height < 10) return;

            int numBars = 64;

            // Get recent samples and run FFT
            float[] samples = _player.GetVisualizerSamples(4096);
            int fftSize = 2048;

            double[] real = new double[fftSize];
            double[] imag = new double[fftSize];

            // Use the most recent fftSize samples from the captured buffer
            int offset = Math.Max(0, samples.Length - fftSize);
            for (int i = 0; i < fftSize && (offset + i) < samples.Length; i++)
            {
                double w = 0.5 * (1.0 - Math.Cos(2.0 * Math.PI * i / (fftSize - 1)));
                real[i] = samples[offset + i] * w;
            }

            VisualizerFFT(real, imag);

            int specLen = fftSize / 2;
            double halfN = fftSize / 2.0;
            double[] mags = new double[specLen];
            for (int i = 0; i < specLen; i++)
            {
                double mag = Math.Sqrt(real[i] * real[i] + imag[i] * imag[i]) / halfN;
                mags[i] = mag > 1e-10 ? 20.0 * Math.Log10(mag) : -100;
            }

            // Group into logarithmic frequency bands
            double[] bars = new double[numBars];
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
                for (int i = binLow; i <= binHigh; i++) { sum += mags[i]; count++; }
                bars[b] = count > 0 ? sum / count : -100;
            }

            // Normalize using fixed absolute dB scale (0 dB = full scale after FFT normalization)
            double range = 60;
            double minDb = -60; // -60 dBFS = silence floor
            for (int b = 0; b < numBars; b++)
                bars[b] = Math.Clamp((bars[b] - minDb) / range, 0, 1);

            // Smooth for visual appeal: attack fast, decay slow
            if (_vizSmoothed.Length != numBars) _vizSmoothed = new double[numBars];
            for (int b = 0; b < numBars; b++)
            {
                if (bars[b] > _vizSmoothed[b])
                    _vizSmoothed[b] = bars[b] * 0.7 + _vizSmoothed[b] * 0.3;  // fast attack
                else
                    _vizSmoothed[b] = bars[b] * 0.15 + _vizSmoothed[b] * 0.85; // slow decay
            }

            var playbarColors = ThemeManager.GetPlaybarColors();
            var gradient = playbarColors.ProgressGradient;
            double barWidth = width / numBars * 0.8;
            double gap = width / numBars * 0.2;

            // Pre-create bar rectangles on first frame or size change
            if (_vizBars == null || _vizBars.Length != numBars)
            {
                VisualizerCanvas.Children.Clear();
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
                    VisualizerCanvas.Children.Add(_vizBars[b]);
                }
            }

            // Update existing bar properties (much faster than recreating)
            bool rainbow = ThemeManager.RainbowVisualizerEnabled;
            double time = Environment.TickCount64 / 1000.0; // seconds for smooth cycling

            for (int b = 0; b < numBars; b++)
            {
                double barHeight = _vizSmoothed[b] * height * 0.92;
                if (barHeight < 2) barHeight = 2;

                _vizBars[b].Width = barWidth;
                _vizBars[b].Height = barHeight;
                Canvas.SetLeft(_vizBars[b], b * (barWidth + gap) + gap / 2);
                Canvas.SetTop(_vizBars[b], height - barHeight);

                Color color;
                if (rainbow)
                {
                    // Each bar gets its own hue, shifting over time at different rates
                    double hue = ((double)b / numBars + time * 0.15 + _vizSmoothed[b] * 0.3) % 1.0;
                    double saturation = 0.85 + _vizSmoothed[b] * 0.15; // more vivid when louder
                    double brightness = 0.5 + _vizSmoothed[b] * 0.5; // brighter when louder
                    color = HsvToColor(hue * 360, saturation, brightness);
                }
                else
                {
                    double t = _vizSmoothed[b];
                    if (t < 0.5)
                    {
                        double seg = t / 0.5;
                        color = Color.FromArgb(
                            (byte)(gradient[0].A + (gradient[1].A - gradient[0].A) * seg),
                            (byte)(gradient[0].R + (gradient[1].R - gradient[0].R) * seg),
                            (byte)(gradient[0].G + (gradient[1].G - gradient[0].G) * seg),
                            (byte)(gradient[0].B + (gradient[1].B - gradient[0].B) * seg));
                    }
                    else
                    {
                        double seg = (t - 0.5) / 0.5;
                        color = Color.FromArgb(
                            (byte)(gradient[1].A + (gradient[2].A - gradient[1].A) * seg),
                            (byte)(gradient[1].R + (gradient[2].R - gradient[1].R) * seg),
                            (byte)(gradient[1].G + (gradient[2].G - gradient[1].G) * seg),
                            (byte)(gradient[1].B + (gradient[2].B - gradient[1].B) * seg));
                    }
                }

                _vizBrushes![b].Color = color;
            }
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

        // ═══════════════════════════════════════════
        //  Export Results
        // ═══════════════════════════════════════════

        private void ExportDropdown_Click(object sender, RoutedEventArgs e)
        {
            if (sender is System.Windows.Controls.Button btn && btn.ContextMenu != null)
            {
                btn.ContextMenu.PlacementTarget = btn;
                btn.ContextMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
                btn.ContextMenu.IsOpen = true;
            }
        }

        private void ExportResults_Click(object sender, RoutedEventArgs e)
        {
            if (_files.Count == 0)
            {
                ErrorDialog.Show("Nothing to Export", "No files loaded to export.", this);
                return;
            }

            // Build filter string with the user's preferred format first
            string preferredFormat = ThemeManager.ExportFormat;
            var filterParts = new List<string>
            {
                "CSV File (*.csv)|*.csv",
                "Text Report (*.txt)|*.txt",
                "PDF File (*.pdf)|*.pdf",
                "Excel Workbook (*.xlsx)|*.xlsx",
                "Word Document (*.docx)|*.docx"
            };

            int defaultIndex = preferredFormat switch
            {
                "csv" => 1,
                "txt" => 2,
                "pdf" => 3,
                "xlsx" => 4,
                "docx" => 5,
                _ => 1
            };

            string defaultExt = preferredFormat switch
            {
                "csv" => ".csv",
                "txt" => ".txt",
                "pdf" => ".pdf",
                "xlsx" => ".xlsx",
                "docx" => ".docx",
                _ => ".csv"
            };

            var dialog = new SaveFileDialog
            {
                Title = "Export Analysis Results",
                Filter = string.Join("|", filterParts),
                FilterIndex = defaultIndex,
                DefaultExt = defaultExt,
                FileName = $"AudioAuditor_Report_{DateTime.Now:yyyyMMdd_HHmmss}"
            };

            if (dialog.ShowDialog() == true)
            {
                try
                {
                    // Extract current DataGrid column layout (order, visibility, headers)
                    var columnInfos = GetCurrentColumnLayout();
                    ExportService.Export(_files, dialog.FileName, columnInfos);
                    StatusText.Text = $"Exported {_files.Count} files to {dialog.FileName}";
                }
                catch (Exception ex)
                {
                    ErrorDialog.Show("Export Error", $"Failed to export:\n{ex.Message}", this);
                }
            }
        }

        /// <summary>
        /// Extracts the current DataGrid column layout (order, visibility, header text, binding path).
        /// </summary>
        private List<ExportColumnInfo> GetCurrentColumnLayout()
        {
            var result = new List<ExportColumnInfo>();
            foreach (var col in FileGrid.Columns)
            {
                string header = "";
                string bindingPath = "";

                if (col.Header is string headerStr)
                    header = headerStr;

                // Extract binding path from the column
                if (col is DataGridBoundColumn boundCol && boundCol.Binding is Binding binding)
                {
                    bindingPath = binding.Path?.Path ?? "";
                }
                else if (col is DataGridTemplateColumn templateCol)
                {
                    // Use SortMemberPath for template columns
                    bindingPath = templateCol.SortMemberPath ?? "";
                    if (string.IsNullOrEmpty(bindingPath))
                        bindingPath = header; // fallback to header
                }

                if (string.IsNullOrEmpty(bindingPath))
                    bindingPath = header;

                result.Add(new ExportColumnInfo
                {
                    Header = header,
                    BindingPath = bindingPath,
                    DisplayIndex = col.DisplayIndex,
                    IsVisible = col.Visibility == Visibility.Visible
                });
            }
            return result;
        }

        // ═══════════════════════════════════════════
        //  Equalizer
        // ═══════════════════════════════════════════

        private static readonly string[] EqBandLabels =
            { "32", "64", "125", "250", "500", "1K", "2K", "4K", "8K", "16K" };

        private void InitializeEqualizerSliders()
        {
            EqSlidersPanel.Children.Clear();
            _eqSliders = new Slider[10];
            _eqValueLabels = new TextBlock[10];

            for (int i = 0; i < 10; i++)
            {
                var bandPanel = new StackPanel
                {
                    Orientation = Orientation.Vertical,
                    Width = 40,
                    Margin = new Thickness(2, 0, 2, 0),
                    HorizontalAlignment = HorizontalAlignment.Center
                };

                var valueLabel = new TextBlock
                {
                    Text = "0",
                    FontSize = 9,
                    FontFamily = new FontFamily("Segoe UI"),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Foreground = (Brush)FindResource("TextMuted")
                };
                _eqValueLabels[i] = valueLabel;

                var slider = new Slider
                {
                    Minimum = -12,
                    Maximum = 12,
                    Value = ThemeManager.EqualizerGains[i],
                    Orientation = Orientation.Vertical,
                    Height = 80,
                    IsSnapToTickEnabled = true,
                    TickFrequency = 1,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Tag = i,
                    Width = 20
                };

                // Apply full themed vertical slider template via XAML
                slider.Template = GetEqSliderTemplate();

                slider.ValueChanged += EqSlider_ValueChanged;
                _eqSliders[i] = slider;

                var freqLabel = new TextBlock
                {
                    Text = EqBandLabels[i],
                    FontSize = 9,
                    FontFamily = new FontFamily("Segoe UI"),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Foreground = (Brush)FindResource("TextSecondary")
                };

                bandPanel.Children.Add(valueLabel);
                bandPanel.Children.Add(slider);
                bandPanel.Children.Add(freqLabel);
                EqSlidersPanel.Children.Add(bandPanel);
            }
        }

        private ControlTemplate? _eqSliderTemplateCache;

        private ControlTemplate GetEqSliderTemplate()
        {
            if (_eqSliderTemplateCache != null) return _eqSliderTemplateCache;

            // Get theme colors for the template
            var accentBrush = FindResource("AccentColor") as Brush ?? Brushes.DodgerBlue;
            var trackBrush = FindResource("ScrollBg") as Brush ?? Brushes.Gray;
            var thumbStroke = FindResource("TextPrimary") as Brush ?? Brushes.White;

            string accentColor = "#3399FF";
            string trackColor = "#333333";
            string strokeColor = "#FFFFFF";

            if (accentBrush is SolidColorBrush ab) accentColor = ab.Color.ToString();
            if (trackBrush is SolidColorBrush tb) trackColor = tb.Color.ToString();
            if (thumbStroke is SolidColorBrush sb) strokeColor = sb.Color.ToString();

            string xaml = $@"
<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'
                 xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'
                 TargetType='Slider'>
    <Grid>
        <!-- Track background -->
        <Border Width='4' CornerRadius='2' Background='{trackColor}'
                HorizontalAlignment='Center'/>
        <Track x:Name='PART_Track' IsDirectionReversed='true' Orientation='Vertical'>
            <Track.DecreaseRepeatButton>
                <RepeatButton IsTabStop='False' Focusable='False'>
                    <RepeatButton.Template>
                        <ControlTemplate TargetType='RepeatButton'>
                            <Border Width='4' CornerRadius='2' Background='{accentColor}'
                                    HorizontalAlignment='Center'/>
                        </ControlTemplate>
                    </RepeatButton.Template>
                </RepeatButton>
            </Track.DecreaseRepeatButton>
            <Track.IncreaseRepeatButton>
                <RepeatButton IsTabStop='False' Focusable='False'>
                    <RepeatButton.Template>
                        <ControlTemplate TargetType='RepeatButton'>
                            <Border Background='Transparent'/>
                        </ControlTemplate>
                    </RepeatButton.Template>
                </RepeatButton>
            </Track.IncreaseRepeatButton>
            <Track.Thumb>
                <Thumb OverridesDefaultStyle='True'>
                    <Thumb.Template>
                        <ControlTemplate TargetType='Thumb'>
                            <Ellipse Width='14' Height='14'
                                     Fill='{accentColor}' Stroke='{strokeColor}'
                                     StrokeThickness='1.2'/>
                        </ControlTemplate>
                    </Thumb.Template>
                </Thumb>
            </Track.Thumb>
        </Track>
    </Grid>
</ControlTemplate>";

            _eqSliderTemplateCache = (ControlTemplate)System.Windows.Markup.XamlReader.Parse(xaml);
            return _eqSliderTemplateCache;
        }

        private void EqSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (sender is not Slider slider || slider.Tag is not int idx) return;

            float gain = (float)slider.Value;
            _eqValueLabels[idx].Text = gain >= 0 ? $"+{(int)gain}" : $"{(int)gain}";

            ThemeManager.EqualizerGains[idx] = gain;

            var eq = _player.CurrentEqualizer;
            if (eq != null)
                eq.UpdateBand(idx, gain);

            ThemeManager.SavePlayOptions();
        }

        private void EqToggle_Click(object sender, RoutedEventArgs e)
        {
            EqPanel.Visibility = EqPanel.Visibility == Visibility.Visible
                ? Visibility.Collapsed : Visibility.Visible;
        }

        private void EqEnabled_Changed(object sender, RoutedEventArgs e)
        {
            bool enabled = ChkEqEnabled.IsChecked == true;
            ThemeManager.EqualizerEnabled = enabled;

            var eq = _player.CurrentEqualizer;
            if (eq != null)
                eq.Enabled = enabled;

            ThemeManager.SavePlayOptions();
        }

        private void EqReset_Click(object sender, RoutedEventArgs e)
        {
            for (int i = 0; i < _eqSliders.Length; i++)
            {
                _eqSliders[i].Value = 0;
                ThemeManager.EqualizerGains[i] = 0;
            }

            _player.CurrentEqualizer?.Reset();
            ThemeManager.SavePlayOptions();
        }

        // ═══════════════════════════════════════════
        //  Keyboard Shortcuts
        // ═══════════════════════════════════════════

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);

            // Don't intercept keys when typing in search box
            if (SearchBox.IsFocused) return;

            if (e.Key == Key.Delete && FileGrid.SelectedItem is AudioFileInfo file)
            {
                _files.Remove(file);
                e.Handled = true;
            }
            else if (e.Key == Key.Enter && FileGrid.SelectedItem is AudioFileInfo playFile)
            {
                PlayFile(playFile);
                e.Handled = true;
            }
            else if (e.Key == Key.Space && !SearchBox.IsFocused)
            {
                PlayPause_Click(this, new RoutedEventArgs());
                e.Handled = true;
            }
            else if (e.Key == Key.F && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
            {
                SearchBox.Focus();
                SearchBox.SelectAll();
                e.Handled = true;
            }
            else if (e.Key == Key.Escape && SearchBox.IsFocused)
            {
                SearchBox.Text = "";
                FileGrid.Focus();
                e.Handled = true;
            }
        }

        // ═══════════════════════════════════════════
        //  Last.fm Status Indicator
        // ═══════════════════════════════════════════

        private void UpdateLastFmStatusIndicator()
        {
            if (_lastFm.IsEnabled)
            {
                LastFmStatusIndicator.Text = "Last.fm: Scrobbling";
                LastFmStatusIndicator.Foreground = (Brush)FindResource("AccentColor");
                LastFmStatusIndicator.Cursor = System.Windows.Input.Cursors.Hand;
            }
            else
            {
                LastFmStatusIndicator.Text = "Last.fm: Not Connected";
                LastFmStatusIndicator.Foreground = (Brush)FindResource("TextMuted");
                LastFmStatusIndicator.Cursor = System.Windows.Input.Cursors.Arrow;
            }
        }

        private void LastFmStatus_Click(object sender, MouseButtonEventArgs e)
        {
            if (_lastFm.IsEnabled && !string.IsNullOrEmpty(ThemeManager.LastFmUsername))
            {
                try
                {
                    Process.Start(new ProcessStartInfo($"https://www.last.fm/user/{Uri.EscapeDataString(ThemeManager.LastFmUsername)}") { UseShellExecute = true });
                }
                catch { }
            }
            else if (_lastFm.IsEnabled)
            {
                try
                {
                    Process.Start(new ProcessStartInfo("https://www.last.fm") { UseShellExecute = true });
                }
                catch { }
            }
        }
    }
}
