using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using AudioQualityChecker.Services;
using Microsoft.Win32;

namespace AudioQualityChecker
{
    public partial class SettingsWindow : Window
    {
        private bool _initializing = true;
        private string? _lastFmToken; // stored between auth steps
        private bool _apiKeysVisible = false; // hidden by default
        private string _realApiKey = "";
        private string _realApiSecret = "";
        private bool _discordIdVisible = false;
        private string _realDiscordAppId = "";
        private bool _acoustIdKeyVisible = false;
        private string _realAcoustIdKey = "";

        /// <summary>When true, MainWindow should show the SH Labs privacy overlay after Settings closes.</summary>
        public bool RequestPrivacyOnClose { get; private set; }

        /// <summary>When true, MainWindow should show the AI config overlay after Settings closes.</summary>
        public bool RequestAiConfigOnClose { get; private set; }

        public SettingsWindow()
        {
            InitializeComponent();

            // Populate theme combo
            foreach (var theme in ThemeManager.AvailableThemes)
                ThemeCombo.Items.Add(theme);
            ThemeCombo.SelectedItem = ThemeManager.CurrentTheme;

            // Populate playbar theme combo
            foreach (var pt in ThemeManager.AvailablePlaybarThemes)
                PlaybarCombo.Items.Add(pt);
            PlaybarCombo.SelectedItem = ThemeManager.IsPlaybarFollowingTheme
                ? "Follow Theme"
                : ThemeManager.CurrentPlaybarTheme;

            // Populate visualizer theme combo
            foreach (var vt in ThemeManager.AvailableVisualizerThemes)
                VisualizerThemeCombo.Items.Add(vt);
            VisualizerThemeCombo.SelectedItem = ThemeManager.IsVisualizerFollowingPlaybar
                ? "Follow Playbar"
                : ThemeManager.CurrentVisualizerTheme;

            // Set play option checkboxes from saved state
            ChkAutoPlay.IsChecked = ThemeManager.AutoPlayNext;
            ChkNormalization.IsChecked = ThemeManager.AudioNormalization;
            ChkCrossfade.IsChecked = ThemeManager.Crossfade;
            CrossfadeSlider.Value = ThemeManager.CrossfadeDuration;
            CrossfadeDurationLabel.Text = $"{ThemeManager.CrossfadeDuration}s";
            ChkCrossfadeOnManualSkip.IsChecked = ThemeManager.CrossfadeOnManualSkip;
            foreach (ComboBoxItem item in CrossfadeCurveCombo.Items)
                if (item.Tag?.ToString() == ThemeManager.CrossfadeCurve.ToString())
                    { CrossfadeCurveCombo.SelectedItem = item; break; }
            if (CrossfadeCurveCombo.SelectedItem == null) CrossfadeCurveCombo.SelectedIndex = 0;
            ChkGapless.IsChecked = ThemeManager.GaplessEnabled;
            ChkSpatialAudio.IsChecked = ThemeManager.SpatialAudioEnabled;
            // Rainbow mode is now driven by the "Rainbow Bars" playbar style

            // Visualizer cycle settings
            CycleSpeedSlider.Value = ThemeManager.VisualizerCycleSpeed;
            CycleSpeedLabel.Text = $"{ThemeManager.VisualizerCycleSpeed}s";
            LoadCycleStyleChecks();

            // Populate all 6 music service combos
            var serviceCombos = new[] { ServiceCombo0, ServiceCombo1, ServiceCombo2, ServiceCombo3, ServiceCombo4, ServiceCombo5 };
            for (int i = 0; i < 6; i++)
            {
                foreach (var svc in ThemeManager.AvailableMusicServices)
                    serviceCombos[i].Items.Add(svc);
                serviceCombos[i].SelectedItem = ThemeManager.MusicServiceSlots[i];
            }

            // Populate custom URL/icon fields
            var urlBoxes = new[] { CustomUrlBox0, CustomUrlBox1, CustomUrlBox2, CustomUrlBox3, CustomUrlBox4, CustomUrlBox5 };
            var iconBoxes = new[] { CustomIconBox0, CustomIconBox1, CustomIconBox2, CustomIconBox3, CustomIconBox4, CustomIconBox5 };
            for (int i = 0; i < 6; i++)
            {
                urlBoxes[i].Text = ThemeManager.CustomServiceUrls[i];
                iconBoxes[i].Text = ThemeManager.CustomServiceIcons[i];
            }

            // Show/hide custom panels
            UpdateCustomPanelVisibility();

            // Connection mode
            ChkOfflineMode.IsChecked = ThemeManager.OfflineModeEnabled;

            // Streaming region settings
            ChkRegionAwareSearch.IsChecked = ThemeManager.RegionAwareSearchEnabled;
            ComboStreamingRegion.SelectedItem = ThemeManager.StreamingRegion;

            // Discord RPC
            ChkDiscordRpc.IsChecked = ThemeManager.DiscordRpcEnabled;
            _realDiscordAppId = ThemeManager.DiscordRpcClientId;
            DiscordAppIdBox.Text = ThemeManager.DiscordRpcClientId;
            ChkDiscordShowElapsed.IsChecked = ThemeManager.DiscordRpcShowElapsed;

            // Discord display mode combo
            DiscordDisplayModeCombo.Items.Add("Track Details (Title + Artist)");
            DiscordDisplayModeCombo.Items.Add("File Name Only");
            DiscordDisplayModeCombo.SelectedIndex = ThemeManager.DiscordRpcDisplayMode switch
            {
                "FileName" => 1,
                _ => 0 // TrackDetails
            };

            // Hide Discord App ID by default
            ApplyDiscordIdVisibility();
            UpdateDiscordStatus();

            // Last.fm
            _realApiKey = ThemeManager.LastFmApiKey;
            _realApiSecret = ThemeManager.LastFmApiSecret;
            LastFmApiKeyBox.Text = ThemeManager.LastFmApiKey;
            LastFmApiSecretBox.Text = ThemeManager.LastFmApiSecret;
            UpdateLastFmStatus();

            // Hide API keys by default (use dot masking)
            ApplyApiKeyVisibility();

            // AcoustID
            _realAcoustIdKey = ThemeManager.AcoustIdApiKey;
            AcoustIdKeyBox.Text = ThemeManager.AcoustIdApiKey;
            ApplyAcoustIdKeyVisibility();

            // Export format
            var formats = new[] { "CSV (.csv)", "Text (.txt)", "PDF (.pdf)", "Excel (.xlsx)", "Word (.docx)" };
            foreach (var fmt in formats)
                ExportFormatCombo.Items.Add(fmt);
            string saved = ThemeManager.ExportFormat;
            ExportFormatCombo.SelectedIndex = saved switch
            {
                "csv" => 0,
                "txt" => 1,
                "pdf" => 2,
                "xlsx" => 3,
                "docx" => 4,
                _ => 0
            };

            // Performance / concurrency
            int selectedConcurrencyIdx = 0;
            bool cpuMatchedPreset = false;
            for (int ci = 0; ci < ThemeManager.ConcurrencyPresets.Length; ci++)
            {
                var (label, value) = ThemeManager.ConcurrencyPresets[ci];
                string display = value == 0
                    ? $"{label} — {ThemeManager.DefaultConcurrency} threads"
                    : label;
                ConcurrencyCombo.Items.Add(display);
                if (value >= 0 && (value == ThemeManager.MaxConcurrency ||
                    (value == 0 && ThemeManager.MaxConcurrency == ThemeManager.DefaultConcurrency)))
                {
                    selectedConcurrencyIdx = ci;
                    cpuMatchedPreset = true;
                }
            }
            // If current value doesn't match any fixed preset, select Custom
            if (!cpuMatchedPreset && ThemeManager.MaxConcurrency != ThemeManager.DefaultConcurrency)
            {
                selectedConcurrencyIdx = ThemeManager.ConcurrencyPresets.Length - 1; // Custom
                CustomCpuPanel.Visibility = Visibility.Visible;
                CustomCpuBox.Text = ThemeManager.MaxConcurrency.ToString();
            }
            ConcurrencyCombo.SelectedIndex = selectedConcurrencyIdx;
            ConcurrencyInfoText.Text = $"Your system has {Environment.ProcessorCount} logical processors. " +
                $"Presets scale dynamically to your hardware. Lower values reduce CPU spikes.";

            // Memory limit
            int selectedMemoryIdx = 0;
            bool memMatchedPreset = false;
            for (int mi = 0; mi < ThemeManager.MemoryPresets.Length; mi++)
            {
                var (label, valueMB) = ThemeManager.MemoryPresets[mi];
                string display = valueMB == 0
                    ? $"{label} — {ThemeManager.DefaultMemoryMB:N0} MB"
                    : label;
                MemoryLimitCombo.Items.Add(display);
                if (valueMB >= 0 && (valueMB == ThemeManager.MaxMemoryMB ||
                    (valueMB == 0 && ThemeManager.MaxMemoryMB == ThemeManager.DefaultMemoryMB)))
                {
                    selectedMemoryIdx = mi;
                    memMatchedPreset = true;
                }
            }
            // If current value doesn't match any fixed preset, select Custom
            if (!memMatchedPreset && ThemeManager.MaxMemoryMB != ThemeManager.DefaultMemoryMB)
            {
                selectedMemoryIdx = ThemeManager.MemoryPresets.Length - 1; // Custom
                CustomMemPanel.Visibility = Visibility.Visible;
                CustomMemBox.Text = ThemeManager.MaxMemoryMB.ToString();
            }
            MemoryLimitCombo.SelectedIndex = selectedMemoryIdx;
            MemoryInfoText.Text = $"Your system has {ThemeManager.TotalSystemMemoryMB:N0} MB total RAM. " +
                $"Presets scale dynamically to your hardware. Limits memory used during analysis.";

            // AI detection
            ChkDefaultAi.IsChecked = ThemeManager.DefaultAiDetectionEnabled;
            ChkExperimentalAi.IsChecked = ThemeManager.ExperimentalAiDetection;

            // SH Labs AI detection
            ChkSHLabsAi.IsChecked = ThemeManager.SHLabsAiDetection;
            SHLabsApiKeyBox.Text = ThemeManager.SHLabsCustomApiKey;
            UpdateSHLabsQuota();

            // Visualizer full-volume
            ChkVisualizerFullVolume.IsChecked = ThemeManager.VisualizerFullVolume;

            // NP color cache
            ChkNpColorCache.IsChecked = ThemeManager.NpColorCacheEnabled;
            ChkNpColorCachePersist.IsChecked = ThemeManager.NpColorCachePersist;
            UpdateNpColorCacheStatus();

            // Scan cache
            ChkScanCache.IsChecked = ThemeManager.ScanCacheEnabled;
            UpdateCacheStatus();

            // Close to tray
            ChkCloseToTray.IsChecked = ThemeManager.CloseToTray;

            // Auto-update check
            ChkCheckForUpdates.IsChecked = ThemeManager.CheckForUpdates;

            // Analysis options
            ChkSilenceMinGap.IsChecked = ThemeManager.SilenceMinGapEnabled;
            TxtSilenceMinGapSec.Text = ThemeManager.SilenceMinGapSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture);
            ChkSilenceSkipEdges.IsChecked = ThemeManager.SilenceSkipEdgesEnabled;
            TxtSilenceSkipEdgeSec.Text = ThemeManager.SilenceSkipEdgeSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture);
            ChkAlwaysFullAnalysis.IsChecked = ThemeManager.AlwaysFullAnalysis;

            // Rename pattern
            switch (ThemeManager.RenamePatternIndex)
            {
                case 0: RbRenameFake.IsChecked = true; break;
                case 1: RbRenameStatusActual.IsChecked = true; break;
                case 2: RbRenameStatusBoth.IsChecked = true; break;
            }

            // Default folders
            TxtDefaultCopy.Text = ThemeManager.DefaultCopyFolder;
            TxtDefaultMove.Text = ThemeManager.DefaultMoveFolder;
            TxtDefaultPlaylist.Text = ThemeManager.DefaultPlaylistFolder;

            // Spectrogram export quality
            ChkSpectHiFi.IsChecked = ThemeManager.SpectrogramHiFiMode;
            ChkSpectMagma.IsChecked = ThemeManager.SpectrogramMagmaColormap;

            // Version info
            string currentVersion = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "?";
            CurrentVersionText.Text = currentVersion;
            if (!string.IsNullOrEmpty(UpdateChecker.LatestVersion))
                LatestVersionText.Text = UpdateChecker.LatestVersion;
            else
                LatestVersionText.Text = "checking...";
            _ = LoadLatestVersionAsync(currentVersion);

            // Column visibility checkboxes — checked = visible/enabled
            ThemeManager.SyncHiddenColumnsWithAnalysisOptions();
            var hidden = ThemeManager.GetHiddenColumnSet();
            bool Visible(string header) => !hidden.Contains(ThemeManager.NormalizeColumnHeader(header));

            ColFavoriteCb.IsChecked = Visible("★");
            ColStatusCb.IsChecked = Visible("Status");
            ColTitleCb.IsChecked = Visible("Title");
            ColArtistCb.IsChecked = Visible("Artist");
            ColFilenameCb.IsChecked = Visible("Filename");
            ColPathCb.IsChecked = Visible("Path");
            ColSampleRateCb.IsChecked = Visible("Sample Rate");
            ColBitsCb.IsChecked = Visible("Bits");
            ColChCb.IsChecked = Visible("Ch");
            ColDurationCb.IsChecked = Visible("Duration");
            ColSizeCb.IsChecked = Visible("Size");
            ColBitrateCb.IsChecked = Visible("Bitrate");
            ColActualBRCb.IsChecked = Visible("Actual BR");
            ColFormatCb.IsChecked = Visible("Format");
            ColMaxFreqCb.IsChecked = Visible("Max Freq");
            ColClippingCb.IsChecked = Visible("Clipping");
            ColBpmCb.IsChecked = Visible("BPM");
            ColReplayGainCb.IsChecked = Visible("Replay Gain");
            ColDRCb.IsChecked = Visible("DR");
            ColMqaCb.IsChecked = Visible("MQA");
            ColAiCb.IsChecked = Visible("AI");
            ColStereoCb.IsChecked = Visible("Fake Stereo");
            ColSilenceCb.IsChecked = Visible("Silence");
            ColDateModifiedCb.IsChecked = Visible("Date Modified");
            ColDateCreatedCb.IsChecked = Visible("Date Created");
            ColTruePeakCb.IsChecked = Visible("True Peak");
            ColLufsCb.IsChecked = Visible("LUFS");
            ColRipQualityCb.IsChecked = Visible("Rip Quality");

            // Hz cutoff allow (F13)
            ChkFreqCutoffAllow.IsChecked = ThemeManager.FrequencyCutoffAllowEnabled;
            TxtFreqCutoffHz.Text = ThemeManager.FrequencyCutoffAllowHz.ToString();

            // Restore last active tab
            if (SettingsTabControl != null && ThemeManager.LastSettingsTab >= 0 && ThemeManager.LastSettingsTab < SettingsTabControl.Items.Count)
                SettingsTabControl.SelectedIndex = ThemeManager.LastSettingsTab;

            _initializing = false;

            UpdateSpectrogramCacheStatus();
            UpdateFavoritesStatus();
        }

        private void UpdateCustomPanelVisibility()
        {
            var combos = new[] { ServiceCombo0, ServiceCombo1, ServiceCombo2, ServiceCombo3, ServiceCombo4, ServiceCombo5 };
            var panels = new[] { CustomPanel0, CustomPanel1, CustomPanel2, CustomPanel3, CustomPanel4, CustomPanel5 };
            for (int i = 0; i < 6; i++)
            {
                panels[i].Visibility = (combos[i].SelectedItem as string) == "Custom..."
                    ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        private void Header_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
                DragMove();
        }

        private void ThemeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_initializing) return;
            if (ThemeCombo.SelectedItem is string theme)
            {
                ThemeManager.ApplyTheme(theme);
            }
        }

        private void PlaybarCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_initializing) return;
            if (PlaybarCombo.SelectedItem is string playbarTheme)
            {
                ThemeManager.SetPlaybarTheme(playbarTheme);
                ThemeManager.SavePlayOptions();
            }
        }

        private void VisualizerThemeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_initializing) return;
            if (VisualizerThemeCombo.SelectedItem is string vizTheme)
            {
                ThemeManager.SetVisualizerTheme(vizTheme);
            }
        }

        private void PlayOption_Changed(object sender, RoutedEventArgs e)
        {
            if (_initializing) return;

            ThemeManager.AutoPlayNext = ChkAutoPlay.IsChecked == true;
            ThemeManager.AudioNormalization = ChkNormalization.IsChecked == true;
            ThemeManager.Crossfade = ChkCrossfade.IsChecked == true;
            ThemeManager.GaplessEnabled = ChkGapless.IsChecked == true;

            // Mutual exclusivity: enabling one disables the other
            if (sender == ChkGapless && ThemeManager.GaplessEnabled && ThemeManager.Crossfade)
            {
                ThemeManager.Crossfade = false;
                ChkCrossfade.IsChecked = false;
            }
            else if (sender == ChkCrossfade && ThemeManager.Crossfade && ThemeManager.GaplessEnabled)
            {
                ThemeManager.GaplessEnabled = false;
                ChkGapless.IsChecked = false;
            }

            ThemeManager.SpatialAudioEnabled = ChkSpatialAudio.IsChecked == true;
            ThemeManager.CheckForUpdates = ChkCheckForUpdates.IsChecked == true;
            ThemeManager.SavePlayOptions();
        }

        private void CrossfadeSlider_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_initializing) return;
            int val = (int)CrossfadeSlider.Value;
            CrossfadeDurationLabel.Text = $"{val}s";
            ThemeManager.CrossfadeDuration = val;
            ThemeManager.SavePlayOptions();
        }

        private void CrossfadeCurve_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (_initializing) return;
            if (CrossfadeCurveCombo.SelectedItem is ComboBoxItem item
                && Enum.TryParse<CrossfadeType>(item.Tag?.ToString(), out var curve))
            {
                ThemeManager.CrossfadeCurve = curve;
                ThemeManager.SavePlayOptions();
            }
        }

        private void CrossfadeManualSkip_Changed(object sender, RoutedEventArgs e)
        {
            if (_initializing) return;
            ThemeManager.CrossfadeOnManualSkip = ChkCrossfadeOnManualSkip.IsChecked == true;
            ThemeManager.SavePlayOptions();
        }

        private void CycleSpeedSlider_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_initializing) return;
            int val = (int)CycleSpeedSlider.Value;
            CycleSpeedLabel.Text = $"{val}s";
            ThemeManager.VisualizerCycleSpeed = val;
            ThemeManager.SavePlayOptions();
        }

        private void CycleStyleCheck_Changed(object sender, RoutedEventArgs e)
        {
            if (_initializing) return;
            SaveCycleStyleChecks();
        }

        private void LoadCycleStyleChecks()
        {
            var list = ThemeManager.VisualizerCycleList;
            if (string.IsNullOrWhiteSpace(list))
            {
                // All checked by default
                ChkCycleBars.IsChecked = true;
                ChkCycleMirror.IsChecked = true;
                ChkCycleParticles.IsChecked = true;
                ChkCycleCircles.IsChecked = true;
                ChkCycleScope.IsChecked = true;
                ChkCycleVU.IsChecked = true;
                return;
            }

            var indices = new HashSet<int>();
            foreach (var part in list.Split(',', StringSplitOptions.RemoveEmptyEntries))
            {
                if (int.TryParse(part.Trim(), out int idx))
                    indices.Add(idx);
            }

            ChkCycleBars.IsChecked = indices.Contains(0);
            ChkCycleMirror.IsChecked = indices.Contains(1);
            ChkCycleParticles.IsChecked = indices.Contains(2);
            ChkCycleCircles.IsChecked = indices.Contains(3);
            ChkCycleScope.IsChecked = indices.Contains(4);
            ChkCycleVU.IsChecked = indices.Contains(5);
        }

        private void SaveCycleStyleChecks()
        {
            var selected = new List<int>();
            if (ChkCycleBars.IsChecked == true) selected.Add(0);
            if (ChkCycleMirror.IsChecked == true) selected.Add(1);
            if (ChkCycleParticles.IsChecked == true) selected.Add(2);
            if (ChkCycleCircles.IsChecked == true) selected.Add(3);
            if (ChkCycleScope.IsChecked == true) selected.Add(4);
            if (ChkCycleVU.IsChecked == true) selected.Add(5);

            ThemeManager.VisualizerCycleList = string.Join(",", selected);
            ThemeManager.SavePlayOptions();
        }

        private void ServiceCombo_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (_initializing) return;

            var combos = new[] { ServiceCombo0, ServiceCombo1, ServiceCombo2, ServiceCombo3, ServiceCombo4, ServiceCombo5 };
            for (int i = 0; i < 6; i++)
            {
                if (combos[i].SelectedItem is string svc)
                    ThemeManager.MusicServiceSlots[i] = svc;
            }

            UpdateCustomPanelVisibility();
            ThemeManager.SavePlayOptions();
        }

        private void CustomUrl_Changed(object sender, TextChangedEventArgs e)
        {
            if (_initializing) return;

            if (sender is TextBox tb && tb.Tag is string tagStr && int.TryParse(tagStr, out int idx) && idx >= 0 && idx < 6)
            {
                ThemeManager.CustomServiceUrls[idx] = tb.Text;
                ThemeManager.SavePlayOptions();
            }
        }

        private void OfflineMode_Changed(object sender, RoutedEventArgs e)
        {
            if (_initializing) return;
            bool wantsOffline = ChkOfflineMode.IsChecked == true;
            if (wantsOffline == ThemeManager.OfflineModeEnabled) return;

            string msg = wantsOffline
                ? "Offline mode disables internet-dependent features such as lyrics fetching, update checks, SH Labs AI detection, Last.fm scrobbling, lyric translation, and Discord Rich Presence album art.\n\nNearly all other features — including local audio analysis, BPM detection, spectrogram generation, playback, metadata editing, and file management — work completely offline without any connection.\n\nEnable offline mode?"
                : "Online mode allows AudioAuditor to use internet-connected features including lyrics lookup, update checks, AI detection, Last.fm scrobbling, lyric translation, and Discord Rich Presence album art.\n\nEnable online mode?";
            string title = wantsOffline ? "Enable Offline Mode" : "Enable Online Mode";

            if (MessageBox.Show(msg, title, MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
            {
                ThemeManager.OfflineModeEnabled = wantsOffline;
                ThemeManager.SavePlayOptions();
                // Notify main window to update the badge
                if (Owner is MainWindow mw)
                    mw.Dispatcher.InvokeAsync(() => mw.UpdateOfflineBadge());
            }
            else
            {
                ChkOfflineMode.IsChecked = ThemeManager.OfflineModeEnabled; // revert
            }
        }

        private void RegionAwareSearch_Changed(object sender, RoutedEventArgs e)
        {
            if (_initializing) return;
            ThemeManager.RegionAwareSearchEnabled = ChkRegionAwareSearch.IsChecked == true;
            ThemeManager.SavePlayOptions();
        }

        private void StreamingRegion_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (_initializing) return;
            if (ComboStreamingRegion.SelectedItem is string region)
                ThemeManager.StreamingRegion = region;
            ThemeManager.SavePlayOptions();
        }

        private void BrowseIcon_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn || btn.Tag is not string tagStr) return;
            if (!int.TryParse(tagStr, out int idx) || idx < 0 || idx >= 6) return;

            var dialog = new OpenFileDialog
            {
                Title = "Select Icon Image",
                Filter = "Image Files|*.png;*.jpg;*.jpeg;*.ico;*.bmp|All Files|*.*"
            };

            if (dialog.ShowDialog() == true)
            {
                var iconBoxes = new[] { CustomIconBox0, CustomIconBox1, CustomIconBox2, CustomIconBox3, CustomIconBox4, CustomIconBox5 };
                iconBoxes[idx].Text = dialog.FileName;
                ThemeManager.CustomServiceIcons[idx] = dialog.FileName;
                ThemeManager.SavePlayOptions();
            }
        }

        private void ColumnVisibility_Changed(object sender, RoutedEventArgs e)
        {
            if (_initializing) return;

            ThemeManager.SetAnalysisColumnEnabled("Clipping", ColClippingCb.IsChecked == true);
            ThemeManager.SetAnalysisColumnEnabled("BPM", ColBpmCb.IsChecked == true);
            ThemeManager.SetAnalysisColumnEnabled("DR", ColDRCb.IsChecked == true);
            ThemeManager.SetAnalysisColumnEnabled("MQA", ColMqaCb.IsChecked == true);
            ThemeManager.SetAnalysisColumnEnabled("AI", ColAiCb.IsChecked == true);
            ThemeManager.SetAnalysisColumnEnabled("Fake Stereo", ColStereoCb.IsChecked == true);
            ThemeManager.SetAnalysisColumnEnabled("Silence", ColSilenceCb.IsChecked == true);
            ThemeManager.SetAnalysisColumnEnabled("True Peak", ColTruePeakCb.IsChecked == true);
            ThemeManager.SetAnalysisColumnEnabled("LUFS", ColLufsCb.IsChecked == true);
            ThemeManager.SetAnalysisColumnEnabled("Rip Quality", ColRipQualityCb.IsChecked == true);
            ChkDefaultAi.IsChecked = ThemeManager.DefaultAiDetectionEnabled;

            // Build comma-separated list of hidden column headers
            var hidden = new List<string>();
            void Check(CheckBox cb, string header)
            {
                if (cb.IsChecked != true)
                    hidden.Add(ThemeManager.NormalizeColumnHeader(header));
            }

            Check(ColFavoriteCb, "★");
            Check(ColStatusCb, "Status");
            Check(ColTitleCb, "Title");
            Check(ColArtistCb, "Artist");
            Check(ColFilenameCb, "Filename");
            Check(ColPathCb, "Path");
            Check(ColSampleRateCb, "Sample Rate");
            Check(ColBitsCb, "Bits");
            Check(ColChCb, "Ch");
            Check(ColDurationCb, "Duration");
            Check(ColSizeCb, "Size");
            Check(ColBitrateCb, "Bitrate");
            Check(ColActualBRCb, "Actual BR");
            Check(ColFormatCb, "Format");
            Check(ColMaxFreqCb, "Max Freq");
            Check(ColClippingCb, "Clipping");
            Check(ColBpmCb, "BPM");
            Check(ColReplayGainCb, "Replay Gain");
            Check(ColDRCb, "DR");
            Check(ColMqaCb, "MQA");
            Check(ColAiCb, "AI");
            Check(ColStereoCb, "Fake Stereo");
            Check(ColSilenceCb, "Silence");
            Check(ColDateModifiedCb, "Date Modified");
            Check(ColDateCreatedCb, "Date Created");
            Check(ColTruePeakCb, "True Peak");
            Check(ColLufsCb, "LUFS");
            Check(ColRipQualityCb, "Rip Quality");

            ThemeManager.HiddenColumns = string.Join(",", hidden);
            ThemeManager.SyncHiddenColumnsWithAnalysisOptions();
            ThemeManager.SavePlayOptions();

            // Apply to MainWindow immediately if it's open
            if (Owner is MainWindow mw)
                mw.ApplyColumnVisibility();
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            // Auto-hide API keys when closing
            _apiKeysVisible = false;
            _discordIdVisible = false;
            ApplyApiKeyVisibility();
            ApplyDiscordIdVisibility();
            Close();
        }

        private void SettingsTabControl_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_initializing) return;
            if (e.Source is not System.Windows.Controls.TabControl tc) return;
            ThemeManager.LastSettingsTab = tc.SelectedIndex;
            ThemeManager.SavePlayOptions();
        }

        private async Task LoadLatestVersionAsync(string currentVersion)
        {
            try
            {
                if (string.IsNullOrEmpty(UpdateChecker.LatestVersion))
                    await UpdateChecker.CheckForUpdateAsync(currentVersion);

                Dispatcher.Invoke(() =>
                {
                    if (!string.IsNullOrEmpty(UpdateChecker.LatestVersion))
                        LatestVersionText.Text = UpdateChecker.LatestVersion;
                    else
                        LatestVersionText.Text = "unable to check";
                });
            }
            catch
            {
                Dispatcher.Invoke(() => LatestVersionText.Text = "unable to check");
            }
        }

        private void DefaultAi_Changed(object sender, RoutedEventArgs e)
        {
            if (_initializing) return;

            bool enabled = ChkDefaultAi.IsChecked == true;
            ThemeManager.SetAnalysisColumnEnabled("AI", enabled);

            if (ColAiCb.IsChecked != enabled)
            {
                ColAiCb.IsChecked = enabled;
                return;
            }

            ThemeManager.SyncHiddenColumnsWithAnalysisOptions();
            ThemeManager.SavePlayOptions();
            if (Owner is MainWindow mw)
                mw.ApplyColumnVisibility();
        }

        private void ExperimentalAi_Changed(object sender, RoutedEventArgs e)
        {
            if (_initializing) return;
            ThemeManager.ExperimentalAiDetection = ChkExperimentalAi.IsChecked == true;
            AudioAnalyzer.EnableExperimentalAi = ThemeManager.ExperimentalAiDetection;
            ThemeManager.SavePlayOptions();
        }

        private void SHLabsAi_Changed(object sender, RoutedEventArgs e)
        {
            if (_initializing) return;

            bool wantsSHLabs = ChkSHLabsAi.IsChecked == true;

            // If enabling and privacy not yet accepted, close Settings and show privacy on MainWindow
            if (wantsSHLabs && !ThemeManager.SHLabsPrivacyAccepted)
            {
                ChkSHLabsAi.IsChecked = false;
                RequestPrivacyOnClose = true;
                Close();
                return;
            }

            ThemeManager.SHLabsAiDetection = wantsSHLabs;
            ThemeManager.SavePlayOptions();
            UpdateSHLabsQuota();
        }

        private void SHLabsPrivacyIcon_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            // Close Settings and show the privacy notice on MainWindow
            RequestPrivacyOnClose = true;
            Close();
        }

        private void SHLabsApiKey_Changed(object sender, TextChangedEventArgs e)
        {
            if (_initializing) return;
            ThemeManager.SHLabsCustomApiKey = SHLabsApiKeyBox.Text.Trim();
            SHLabsDetectionService.CustomApiKey = ThemeManager.SHLabsCustomApiKey;
            ThemeManager.SavePlayOptions();
            UpdateSHLabsQuota();
        }

        private void SHLabsApiKeyClear_Click(object sender, RoutedEventArgs e)
        {
            SHLabsApiKeyBox.Text = "";
            ThemeManager.SHLabsCustomApiKey = "";
            SHLabsDetectionService.CustomApiKey = "";
            ThemeManager.SavePlayOptions();
            UpdateSHLabsQuota();
        }

        private void UpdateSHLabsQuota()
        {
            if (ThemeManager.SHLabsAiDetection)
            {
                if (SHLabsDetectionService.HasCustomApiKey)
                {
                    SHLabsQuotaText.Text = "Using your own API key — no rate limits apply";
                }
                else
                {
                    var (daily, monthly) = SHLabsDetectionService.GetQuota();
                    SHLabsQuotaText.Text = $"Remaining today: {daily}  •  This month: {monthly}";
                }
            }
            else
            {
                SHLabsQuotaText.Text = "";
            }
        }

        private void VisualizerFullVolume_Changed(object sender, RoutedEventArgs e)
        {
            if (_initializing) return;
            ThemeManager.VisualizerFullVolume = ChkVisualizerFullVolume.IsChecked == true;
            ThemeManager.SavePlayOptions();
        }

        private void NpColorCache_Changed(object sender, RoutedEventArgs e)
        {
            if (_initializing) return;
            ThemeManager.NpColorCacheEnabled = ChkNpColorCache.IsChecked == true;
            ThemeManager.SavePlayOptions();
        }

        private void NpColorCachePersist_Changed(object sender, RoutedEventArgs e)
        {
            if (_initializing) return;
            ThemeManager.NpColorCachePersist = ChkNpColorCachePersist.IsChecked == true;
            ThemeManager.SavePlayOptions();
        }


        // ═══════════════════════════════════════════
        //  Scan Cache
        // ═══════════════════════════════════════════

        private void ScanCache_Changed(object sender, RoutedEventArgs e)
        {
            if (_initializing) return;
            ThemeManager.ScanCacheEnabled = ChkScanCache.IsChecked == true;
            ThemeManager.SavePlayOptions();
            UpdateCacheStatus();
        }

        private void UpdateNpColorCacheStatus()
        {
            try
            {
                var path = System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "AudioAuditor", "np_color_cache.json");
                if (File.Exists(path))
                {
                    var fi = new FileInfo(path);
                    NpColorCacheStatusText.Text = $"Persisted cache: {fi.Length / 1024.0:F1} KB on disk";
                }
                else
                    NpColorCacheStatusText.Text = "No persisted cache file";
            }
            catch { }
        }

        private void CloseToTray_Changed(object sender, RoutedEventArgs e)
        {
            if (_initializing) return;
            ThemeManager.CloseToTray = ChkCloseToTray.IsChecked == true;
            ThemeManager.SavePlayOptions();
        }

        private void ClearCache_Click(object sender, RoutedEventArgs e)
        {
            ScanCacheService.Clear();
            UpdateCacheStatus();
        }

        // ═══════════════════════════════════════════
        //  Analysis options (silence thresholds, always full scan)
        // ═══════════════════════════════════════════

        private void SilenceOption_Changed(object sender, RoutedEventArgs e) => SyncAnalysisSettings();
        private void SilenceOption_Changed(object sender, System.Windows.Controls.TextChangedEventArgs e) => SyncAnalysisSettings();

        private void SyncAnalysisSettings()
        {
            if (_initializing) return;

            ThemeManager.SilenceMinGapEnabled = ChkSilenceMinGap.IsChecked == true;
            if (double.TryParse(TxtSilenceMinGapSec.Text,
                    System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture, out var gapSec) && gapSec > 0)
            {
                ThemeManager.SilenceMinGapSeconds = gapSec;
                AudioAnalyzer.SilenceMinGapSeconds = gapSec;
            }

            ThemeManager.SilenceSkipEdgesEnabled = ChkSilenceSkipEdges.IsChecked == true;
            if (double.TryParse(TxtSilenceSkipEdgeSec.Text,
                    System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture, out var edgeSec) && edgeSec > 0)
            {
                ThemeManager.SilenceSkipEdgeSeconds = edgeSec;
                AudioAnalyzer.SilenceSkipEdgeSeconds = edgeSec;
            }

            AudioAnalyzer.SilenceMinGapEnabled = ThemeManager.SilenceMinGapEnabled;
            AudioAnalyzer.SilenceSkipEdgesEnabled = ThemeManager.SilenceSkipEdgesEnabled;

            ThemeManager.AlwaysFullAnalysis = ChkAlwaysFullAnalysis.IsChecked == true;
            AudioAnalyzer.AlwaysFullAnalysis = ThemeManager.AlwaysFullAnalysis;

            ThemeManager.SavePlayOptions();
        }

        // ═══════════════════════════════════════════
        //  Spectrogram export quality
        // ═══════════════════════════════════════════

        private void SpectOption_Changed(object sender, RoutedEventArgs e)
        {
            if (_initializing) return;
            ThemeManager.SpectrogramHiFiMode = ChkSpectHiFi.IsChecked == true;
            ThemeManager.SpectrogramMagmaColormap = ChkSpectMagma.IsChecked == true;
            ThemeManager.SavePlayOptions();
        }

        private void UpdateCacheStatus()
        {
            if (ThemeManager.ScanCacheEnabled)
            {
                ScanCacheService.EnsureLoaded();
                int count = ScanCacheService.EntryCount;
                long sizeBytes = ScanCacheService.GetCacheSizeBytes();
                string sizeStr = sizeBytes < 1024 ? $"{sizeBytes} B"
                    : sizeBytes < 1024 * 1024 ? $"{sizeBytes / 1024.0:F1} KB"
                    : $"{sizeBytes / (1024.0 * 1024.0):F1} MB";
                CacheStatusText.Text = count > 0
                    ? $"{count:N0} files cached ({sizeStr})"
                    : "Cache is empty";
            }
            else
            {
                CacheStatusText.Text = "Disabled";
            }
        }

        private void UpdateFavoritesStatus()
        {
            int count = FavoritesService.Count;
            long sizeBytes = FavoritesService.GetFileSizeBytes();
            string sizeStr = sizeBytes < 1024 ? $"{sizeBytes} B"
                : sizeBytes < 1024 * 1024 ? $"{sizeBytes / 1024.0:F1} KB"
                : $"{sizeBytes / (1024.0 * 1024.0):F1} MB";
            FavoritesStatusText.Text = count > 0
                ? $"{count:N0} {(count == 1 ? "file" : "files")} ({sizeStr})"
                : "No favorites saved";
        }

        // ═══════════════════════════════════════════
        //  Discord Rich Presence
        // ═══════════════════════════════════════════

        private void DiscordCreateApp_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Process.Start(new ProcessStartInfo("https://discord.com/developers/applications") { UseShellExecute = true });
            }
            catch { }
        }

        private void DiscordDownloadLogo_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // icon.png is embedded as a WPF Resource, extract it from the assembly
                string downloadsFolder = System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
                string destPath = System.IO.Path.Combine(downloadsFolder, "audioauditor.png");

                var resourceUri = new Uri("pack://application:,,,/Resources/icon.png", UriKind.Absolute);
                var streamInfo = System.Windows.Application.GetResourceStream(resourceUri);
                if (streamInfo == null)
                {
                    MessageBox.Show("Could not find the AudioAuditor logo in Resources.", "Logo Not Found",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                using (var source = streamInfo.Stream)
                using (var dest = new System.IO.FileStream(destPath, System.IO.FileMode.Create))
                {
                    source.CopyTo(dest);
                }

                MessageBox.Show($"Logo saved to:\n{destPath}\n\nUpload this as a Rich Presence asset named \"audioauditor\" on the Discord Developer Portal.",
                    "Logo Downloaded", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to save logo: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void DiscordAppId_Changed(object sender, TextChangedEventArgs e)
        {
            if (_initializing) return;
            if (_discordIdVisible)
            {
                _realDiscordAppId = DiscordAppIdBox.Text.Trim();
                ThemeManager.DiscordRpcClientId = _realDiscordAppId;
                ThemeManager.SavePlayOptions();
                UpdateDiscordStatus();
            }
        }

        private void DiscordRpc_Changed(object sender, RoutedEventArgs e)
        {
            if (_initializing) return;
            ThemeManager.DiscordRpcEnabled = ChkDiscordRpc.IsChecked == true;
            ThemeManager.SavePlayOptions();
            UpdateDiscordStatus();
        }

        private void DiscordDisplayMode_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (_initializing) return;
            ThemeManager.DiscordRpcDisplayMode = DiscordDisplayModeCombo.SelectedIndex switch
            {
                1 => "FileName",
                _ => "TrackDetails"
            };
            ThemeManager.SavePlayOptions();
        }

        private void DiscordShowElapsed_Changed(object sender, RoutedEventArgs e)
        {
            if (_initializing) return;
            ThemeManager.DiscordRpcShowElapsed = ChkDiscordShowElapsed.IsChecked == true;
            ThemeManager.SavePlayOptions();
        }

        private void ToggleDiscordIdVisibility_Click(object sender, RoutedEventArgs e)
        {
            if (_discordIdVisible)
            {
                _realDiscordAppId = DiscordAppIdBox.Text.Trim();
                ThemeManager.DiscordRpcClientId = _realDiscordAppId;
                ThemeManager.SavePlayOptions();
            }
            _discordIdVisible = !_discordIdVisible;
            ApplyDiscordIdVisibility();
        }

        private void ApplyDiscordIdVisibility()
        {
            if (_discordIdVisible)
            {
                _initializing = true;
                DiscordAppIdBox.Text = _realDiscordAppId;
                DiscordAppIdBox.IsReadOnly = false;
                DiscordEyeSlash.Visibility = Visibility.Collapsed;
                _initializing = false;
            }
            else
            {
                _initializing = true;
                string dots = _realDiscordAppId.Length > 0 ? new string('●', Math.Max(_realDiscordAppId.Length, 20)) : "";
                DiscordAppIdBox.Text = dots;
                DiscordAppIdBox.IsReadOnly = true;
                DiscordEyeSlash.Visibility = Visibility.Visible;
                _initializing = false;
            }
        }

        private void UpdateDiscordStatus()
        {
            if (string.IsNullOrWhiteSpace(ThemeManager.DiscordRpcClientId))
                DiscordStatusText.Text = "Enter your Discord Application ID to enable Rich Presence.";
            else if (ThemeManager.DiscordRpcEnabled)
                DiscordStatusText.Text = "✓ Configured";
            else
                DiscordStatusText.Text = "Ready — enable the checkbox above to connect.";
        }

        // ═══════════════════════════════════════════
        //  Last.fm
        // ═══════════════════════════════════════════

        private void LastFmKey_Changed(object sender, TextChangedEventArgs e)
        {
            if (_initializing) return;
            // Only save if keys are visible (not showing dots)
            if (_apiKeysVisible)
            {
                _realApiKey = LastFmApiKeyBox.Text.Trim();
                _realApiSecret = LastFmApiSecretBox.Text.Trim();
                ThemeManager.LastFmApiKey = _realApiKey;
                ThemeManager.LastFmApiSecret = _realApiSecret;
                ThemeManager.SavePlayOptions();
            }
        }

        private void AcoustIdKey_Changed(object sender, TextChangedEventArgs e)
        {
            if (_initializing) return;
            if (_acoustIdKeyVisible)
            {
                _realAcoustIdKey = AcoustIdKeyBox.Text.Trim();
                ThemeManager.AcoustIdApiKey = _realAcoustIdKey;
            }
            else
            {
                // Don't save dots
                return;
            }
            ThemeManager.SavePlayOptions();
        }

        private void LastFmCreateApiKey_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Process.Start(new ProcessStartInfo("https://www.last.fm/api/account/create") { UseShellExecute = true });
            }
            catch { }
        }

        private void AcoustIdCreateApiKey_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Process.Start(new ProcessStartInfo("https://acoustid.org/new-application") { UseShellExecute = true });
            }
            catch { }
        }

        private async void LastFmAuth_Click(object sender, RoutedEventArgs e)
        {
            string apiKey = _realApiKey.Trim();
            string apiSecret = _realApiSecret.Trim();

            if (string.IsNullOrEmpty(apiKey) || string.IsNullOrEmpty(apiSecret))
            {
                LastFmStatusText.Text = "Enter API Key and API Secret first.";
                return;
            }

            LastFmStatusText.Text = "Getting auth token...";

            var svc = new LastFmService();
            svc.Configure(apiKey, apiSecret, "");

            var result = await svc.GetAuthTokenAsync();
            svc.Dispose();

            if (result == null)
            {
                LastFmStatusText.Text = "Failed to get auth token.";
                return;
            }

            _lastFmToken = result.Value.token;

            try
            {
                Process.Start(new ProcessStartInfo(result.Value.authUrl) { UseShellExecute = true });
            }
            catch
            {
                LastFmStatusText.Text = "Could not open browser.";
                return;
            }

            LastFmStatusText.Text = "Authorize in browser, then click Confirm Auth.";
            BtnLastFmConfirm.Visibility = Visibility.Visible;
        }

        private async void LastFmConfirm_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_lastFmToken))
            {
                LastFmStatusText.Text = "Click Authenticate first.";
                return;
            }

            LastFmStatusText.Text = "Confirming...";

            var svc = new LastFmService();
            svc.Configure(_realApiKey.Trim(), _realApiSecret.Trim(), "");

            string? sessionKey = await svc.GetSessionKeyAsync(_lastFmToken);
            svc.Dispose();

            if (string.IsNullOrEmpty(sessionKey))
            {
                LastFmStatusText.Text = "Auth failed. Did you authorize in the browser?";
                return;
            }

            ThemeManager.LastFmSessionKey = sessionKey;
            ThemeManager.LastFmUsername = svc.Username;
            ThemeManager.LastFmEnabled = true;
            ThemeManager.SavePlayOptions();

            BtnLastFmConfirm.Visibility = Visibility.Collapsed;
            _lastFmToken = null;
            UpdateLastFmStatus();
        }

        private void UpdateLastFmStatus()
        {
            if (!string.IsNullOrEmpty(ThemeManager.LastFmSessionKey))
                LastFmStatusText.Text = "✓ Authenticated";
            else
                LastFmStatusText.Text = "";
        }

        // ═══════════════════════════════════════════
        //  API Key Visibility Toggle
        // ═══════════════════════════════════════════

        private void ToggleApiVisibility_Click(object sender, RoutedEventArgs e)
        {
            // If currently visible, save the real values before hiding
            if (_apiKeysVisible)
            {
                _realApiKey = LastFmApiKeyBox.Text.Trim();
                _realApiSecret = LastFmApiSecretBox.Text.Trim();
                ThemeManager.LastFmApiKey = _realApiKey;
                ThemeManager.LastFmApiSecret = _realApiSecret;
                ThemeManager.SavePlayOptions();
            }
            _apiKeysVisible = !_apiKeysVisible;
            ApplyApiKeyVisibility();
        }

        private void ApplyApiKeyVisibility()
        {
            if (_apiKeysVisible)
            {
                // Show keys: restore real text
                _initializing = true; // prevent saving dots as keys
                LastFmApiKeyBox.FontFamily = new System.Windows.Media.FontFamily("Segoe UI");
                LastFmApiSecretBox.FontFamily = new System.Windows.Media.FontFamily("Segoe UI");
                LastFmApiKeyBox.Text = _realApiKey;
                LastFmApiSecretBox.Text = _realApiSecret;
                LastFmApiKeyBox.IsReadOnly = false;
                LastFmApiSecretBox.IsReadOnly = false;
                EyeSlash.Visibility = Visibility.Collapsed;
                _initializing = false;
            }
            else
            {
                // Hide keys: replace text with dots
                _initializing = true;
                // Store current real values first
                if (LastFmApiKeyBox.FontFamily.Source != "Segoe UI" || string.IsNullOrEmpty(_realApiKey))
                {
                    // Already masked or empty, use stored values
                }
                else
                {
                    _realApiKey = LastFmApiKeyBox.Text;
                    _realApiSecret = LastFmApiSecretBox.Text;
                }
                LastFmApiKeyBox.FontFamily = new System.Windows.Media.FontFamily("Segoe UI");
                LastFmApiSecretBox.FontFamily = new System.Windows.Media.FontFamily("Segoe UI");
                // Fill with bullet dots matching full width
                string keyDots = _realApiKey.Length > 0 ? new string('●', Math.Max(_realApiKey.Length, 40)) : "";
                string secretDots = _realApiSecret.Length > 0 ? new string('●', Math.Max(_realApiSecret.Length, 40)) : "";
                LastFmApiKeyBox.Text = keyDots;
                LastFmApiSecretBox.Text = secretDots;
                LastFmApiKeyBox.IsReadOnly = true;
                LastFmApiSecretBox.IsReadOnly = true;
                EyeSlash.Visibility = Visibility.Visible;
                _initializing = false;
            }
        }

        // ═══════════════════════════════════════════
        //  AcoustID API Key Visibility Toggle
        // ═══════════════════════════════════════════

        private void ToggleAcoustIdVisibility_Click(object sender, RoutedEventArgs e)
        {
            if (_acoustIdKeyVisible)
            {
                _realAcoustIdKey = AcoustIdKeyBox.Text.Trim();
                ThemeManager.AcoustIdApiKey = _realAcoustIdKey;
                ThemeManager.SavePlayOptions();
            }
            _acoustIdKeyVisible = !_acoustIdKeyVisible;
            ApplyAcoustIdKeyVisibility();
        }

        private void ApplyAcoustIdKeyVisibility()
        {
            if (_acoustIdKeyVisible)
            {
                _initializing = true;
                AcoustIdKeyBox.Text = _realAcoustIdKey;
                AcoustIdKeyBox.IsReadOnly = false;
                AcoustIdEyeSlash.Visibility = Visibility.Collapsed;
                _initializing = false;
            }
            else
            {
                _initializing = true;
                string dots = _realAcoustIdKey.Length > 0 ? new string('●', Math.Max(_realAcoustIdKey.Length, 32)) : "";
                AcoustIdKeyBox.Text = dots;
                AcoustIdKeyBox.IsReadOnly = true;
                AcoustIdEyeSlash.Visibility = Visibility.Visible;
                _initializing = false;
            }
        }

        // ═══════════════════════════════════════════
        //  Export Format
        // ═══════════════════════════════════════════

        private void ExportFormatCombo_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (_initializing) return;
            ThemeManager.ExportFormat = ExportFormatCombo.SelectedIndex switch
            {
                0 => "csv",
                1 => "txt",
                2 => "pdf",
                3 => "xlsx",
                4 => "docx",
                _ => "csv"
            };
            ThemeManager.SavePlayOptions();
        }

        // ═══════════════════════════════════════════
        //  Performance
        // ═══════════════════════════════════════════

        private void ConcurrencyCombo_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (_initializing) return;
            int idx = ConcurrencyCombo.SelectedIndex;
            if (idx >= 0 && idx < ThemeManager.ConcurrencyPresets.Length)
            {
                var (_, value) = ThemeManager.ConcurrencyPresets[idx];
                if (value == -1)
                {
                    // Custom: show input, default to current value
                    CustomCpuPanel.Visibility = Visibility.Visible;
                    if (string.IsNullOrEmpty(CustomCpuBox.Text))
                        CustomCpuBox.Text = ThemeManager.MaxConcurrency.ToString();
                }
                else
                {
                    CustomCpuPanel.Visibility = Visibility.Collapsed;
                    ThemeManager.MaxConcurrency = value;
                    ThemeManager.SavePlayOptions();
                }
            }
        }

        private void CustomCpu_Changed(object sender, TextChangedEventArgs e)
        {
            if (_initializing) return;
            if (int.TryParse(CustomCpuBox.Text, out int val) && val >= 1 && val <= Environment.ProcessorCount)
            {
                ThemeManager.MaxConcurrency = val;
                ThemeManager.SavePlayOptions();
            }
        }

        private void MemoryLimitCombo_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (_initializing) return;
            int idx = MemoryLimitCombo.SelectedIndex;
            if (idx >= 0 && idx < ThemeManager.MemoryPresets.Length)
            {
                var (_, valueMB) = ThemeManager.MemoryPresets[idx];
                if (valueMB == -1)
                {
                    // Custom: show input, default to current value
                    CustomMemPanel.Visibility = Visibility.Visible;
                    if (string.IsNullOrEmpty(CustomMemBox.Text))
                        CustomMemBox.Text = ThemeManager.MaxMemoryMB.ToString();
                }
                else
                {
                    CustomMemPanel.Visibility = Visibility.Collapsed;
                    ThemeManager.MaxMemoryMB = valueMB;
                    ThemeManager.SavePlayOptions();
                }
            }
        }

        private void CustomMem_Changed(object sender, TextChangedEventArgs e)
        {
            if (_initializing) return;
            if (int.TryParse(CustomMemBox.Text, out int val) && val >= 128 && val <= (int)ThemeManager.TotalSystemMemoryMB)
            {
                ThemeManager.MaxMemoryMB = val;
                ThemeManager.SavePlayOptions();
            }
        }

        private void SupportDonate_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Process.Start(new ProcessStartInfo("https://ko-fi.com/angelsoftware") { UseShellExecute = true });
            }
            catch { }
        }

        private void Hyperlink_RequestNavigate(object sender, System.Windows.Navigation.RequestNavigateEventArgs e)
        {
            try
            {
                Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
            }
            catch { }
            e.Handled = true;
        }

        // ═══════════════════════════════════════════
        //  F4 — Edit Cache
        // ═══════════════════════════════════════════

        private void EditCache_Click(object sender, RoutedEventArgs e)
        {
            string path = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "AudioAuditor", "scan_cache.json");
            if (!File.Exists(path))
            {
                MessageBox.Show("Cache file not found. Scan some files first to create it.", "Edit Cache",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            try { Process.Start(new ProcessStartInfo("notepad.exe") { ArgumentList = { path }, UseShellExecute = true }); }
            catch (Exception ex) { MessageBox.Show($"Could not open Notepad: {ex.Message}", "Edit Cache"); }
        }

        // ═══════════════════════════════════════════
        //  F9 — Favorites management
        // ═══════════════════════════════════════════

        private void ClearFavorites_Click(object sender, RoutedEventArgs e)
        {
            var result = MessageBox.Show(
                "This will permanently remove all starred files from your favorites list.\n\nAre you sure?",
                "Clear All Favorites",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);
            if (result != MessageBoxResult.Yes) return;
            FavoritesService.ClearAll();
            (Owner as MainWindow)?.RefreshFavoriteSort();
        }

        private void OpenFavoritesFile_Click(object sender, RoutedEventArgs e)
        {
            string path = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "AudioAuditor", "favorites.json");
            if (!File.Exists(path))
            {
                MessageBox.Show("No favorites saved yet.", "Favorites",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            try { Process.Start(new ProcessStartInfo("notepad.exe") { ArgumentList = { path }, UseShellExecute = true }); }
            catch (Exception ex) { MessageBox.Show($"Could not open Notepad: {ex.Message}", "Favorites"); }
        }

        // ═══════════════════════════════════════════
        //  F13 — Hz cutoff allow
        // ═══════════════════════════════════════════

        private void FreqCutoff_Changed(object sender, RoutedEventArgs e)
        {
            if (_initializing) return;
            ThemeManager.FrequencyCutoffAllowEnabled = ChkFreqCutoffAllow.IsChecked == true;
            AudioAnalyzer.FrequencyCutoffAllowEnabled = ThemeManager.FrequencyCutoffAllowEnabled;
            ThemeManager.SavePlayOptions();
        }

        private void FreqCutoffHz_Changed(object sender, TextChangedEventArgs e)
        {
            if (_initializing) return;
            if (int.TryParse(TxtFreqCutoffHz.Text, out int hz) && hz > 0 && hz <= 96000)
            {
                ThemeManager.FrequencyCutoffAllowHz = hz;
                AudioAnalyzer.FrequencyCutoffAllowHz = hz;
                ThemeManager.SavePlayOptions();
            }
        }

        // ═══════════════════════════════════════════
        //  F14 — Clear spectrogram cache
        // ═══════════════════════════════════════════

        private void ClearSpectrogramCache_Click(object sender, RoutedEventArgs e)
        {
            (Owner as MainWindow)?.ClearSpectrogramCache();
            UpdateSpectrogramCacheStatus();
        }

        private void UpdateSpectrogramCacheStatus()
        {
            int n = (Owner as MainWindow)?.SpectrogramCacheCount ?? 0;
            SpectrogramCacheStatusText.Text = n > 0 ? $"{n} cached" : "Empty";
        }



        // ═══════════════════════════════════════════
        //  Rename & Default Folders
        // ═══════════════════════════════════════════

        private void RenamePattern_Changed(object sender, RoutedEventArgs e)
        {
            if (_initializing) return;
            if (RbRenameFake.IsChecked == true) ThemeManager.RenamePatternIndex = 0;
            else if (RbRenameStatusActual.IsChecked == true) ThemeManager.RenamePatternIndex = 1;
            else if (RbRenameStatusBoth.IsChecked == true) ThemeManager.RenamePatternIndex = 2;
            ThemeManager.SavePlayOptions();
        }

        private void DefaultFolder_Changed(object sender, TextChangedEventArgs e)
        {
            if (_initializing) return;
            ThemeManager.DefaultCopyFolder = TxtDefaultCopy.Text ?? "";
            ThemeManager.DefaultMoveFolder = TxtDefaultMove.Text ?? "";
            ThemeManager.DefaultPlaylistFolder = TxtDefaultPlaylist.Text ?? "";
            ThemeManager.SavePlayOptions();
        }

        private void BrowseCopyFolder_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFolderDialog { Title = "Select default copy folder" };
            if (dlg.ShowDialog() == true)
            {
                TxtDefaultCopy.Text = dlg.FolderName;
                ThemeManager.DefaultCopyFolder = dlg.FolderName;
                ThemeManager.SavePlayOptions();
            }
        }

        private void BrowseMoveFolder_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFolderDialog { Title = "Select default move folder" };
            if (dlg.ShowDialog() == true)
            {
                TxtDefaultMove.Text = dlg.FolderName;
                ThemeManager.DefaultMoveFolder = dlg.FolderName;
                ThemeManager.SavePlayOptions();
            }
        }

        private void BrowsePlaylistFolder_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFolderDialog { Title = "Select default playlist folder" };
            if (dlg.ShowDialog() == true)
            {
                TxtDefaultPlaylist.Text = dlg.FolderName;
                ThemeManager.DefaultPlaylistFolder = dlg.FolderName;
                ThemeManager.SavePlayOptions();
            }
        }
    }
}
