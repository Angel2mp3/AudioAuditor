using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using AudioQualityChecker.Models;
using AudioQualityChecker.Services;
using AudioQualityChecker.Services.Scrobbling;
using Microsoft.Win32;
using Forms = System.Windows.Forms;

namespace AudioQualityChecker
{
    public partial class SettingsWindow : Window
    {
        private bool _initializing = true;
        private string? _lastFmToken; // stored between auth steps
        private bool _apiKeysVisible = false; // hidden by default
        private string _realApiKey = "";
        private string _realApiSecret = "";
        private string _realLibreApiKey = "";
        private string _realLibreApiSecret = "";
        private bool _libreFmKeysVisible = false;
        private string? _libreFmToken;
        private bool _listenBrainzTokenVisible = false;
        private string _realListenBrainzUsername = "";
        private string _realListenBrainzToken = "";
        private bool _malojaKeyVisible = false;
        private string _realMalojaKey = "";
        private bool _suppressScrobbleTextEvents;
        private bool _discordIdVisible = false;
        private string _realDiscordAppId = "";
        private bool _acoustIdKeyVisible = false;
        private string _realAcoustIdKey = "";
        private DispatcherTimer? _crossfadePreviewTimer;
        private CustomThemeDefinition _customThemeEditorBase = CustomThemeDefinition.CreateDefault();

        /// <summary>When true, MainWindow should show the SH Labs privacy overlay after Settings closes.</summary>
        public bool RequestPrivacyOnClose { get; private set; }

        /// <summary>When true, MainWindow should show the AI config overlay after Settings closes.</summary>
        public bool RequestAiConfigOnClose { get; private set; }

        public SettingsWindow()
        {
            InitializeComponent();
            WireCustomThemeEditorEvents();
            Closed += (_, _) =>
            {
                _crossfadePreviewTimer?.Stop();
                _crossfadePreviewTimer = null;
            };

            // Populate theme combo
            foreach (var theme in ThemeManager.GetAvailableThemeNames())
                ThemeCombo.Items.Add(theme);
            ThemeCombo.SelectedItem = ThemeManager.CurrentTheme;

            // Populate playbar theme combo
            foreach (var pt in ThemeManager.AvailablePlaybarThemes)
                PlaybarCombo.Items.Add(pt);
            PlaybarCombo.SelectedItem = ThemeManager.IsPlaybarFollowingTheme
                ? "Follow Theme"
                : ThemeManager.CurrentPlaybarTheme;

            foreach (PlaybarAnimationStyle style in Enum.GetValues(typeof(PlaybarAnimationStyle)))
            {
                MainPlaybarAnimationCombo.Items.Add(style);
                NpPlaybarAnimationCombo.Items.Add(style);
            }
            MainPlaybarAnimationCombo.SelectedItem = ThemeManager.MainPlaybarAnimationStyle;
            NpPlaybarAnimationCombo.SelectedItem = ThemeManager.NpPlaybarAnimationStyle;
            // "Color Drift" is intentionally NOT a mode — it's controlled by the ChkNpColorDriftGlow
            // toggle below, which can layer the drift glow under any effect.
            foreach (var mode in new[] { "Off", "Stars", "Rain", "Snow", "Leaves", "Underwater" })
                NpBackgroundAnimationCombo.Items.Add(mode);
            NpBackgroundAnimationCombo.SelectedItem =
                ThemeManager.NormalizeNpBackgroundAnimationMode(ThemeManager.NpBackgroundAnimationMode);
            ChkNpColorDriftGlow.IsChecked = ThemeManager.NpColorDriftBackgroundEnabled;
            ChkNpBackgroundUseAlbumColors.IsChecked = ThemeManager.NpBackgroundUseAlbumColors;
            ChkNpBackgroundCycle.IsChecked = ThemeManager.NpBackgroundCycleEnabled;
            NpBackgroundCycleSpeedSlider.Value = Math.Clamp(ThemeManager.NpBackgroundCycleSpeed, 0.25, 3.0);
            NpBackgroundCycleSpeedLabel.Text = $"{NpBackgroundCycleSpeedSlider.Value:0.0}x";
            ChkNpBackgroundCycleOnSongChange.IsChecked = ThemeManager.NpBackgroundCycleOnSongChange;
            NpStarDensitySlider.Value = ThemeManager.ClampNpStarDensity(ThemeManager.NpStarDensity);
            NpStarDensityLabel.Text = $"{NpStarDensitySlider.Value:0.0}x";
            ChkNpShootingStars.IsChecked = ThemeManager.NpShootingStarsEnabled;
            NpShootingStarDensitySlider.Value = ThemeManager.ClampNpShootingStarDensity(ThemeManager.NpShootingStarDensity);
            NpShootingStarDensityLabel.Text = $"{NpShootingStarDensitySlider.Value:0.0}x";
            NpRainIntensitySlider.Value = ThemeManager.ClampNpRainIntensity(ThemeManager.NpRainIntensity);
            NpRainIntensityLabel.Text = $"{NpRainIntensitySlider.Value:0.0}x";
            ChkNpRainLightning.IsChecked = ThemeManager.NpRainLightningEnabled;
            NpRainLightningSlider.Value = ThemeManager.ClampNpRainLightningAmount(ThemeManager.NpRainLightningAmount);
            NpRainLightningLabel.Text = $"{NpRainLightningSlider.Value:0.0}x";
            NpSnowDensitySlider.Value = ThemeManager.ClampNpSnowDensity(ThemeManager.NpSnowDensity);
            NpSnowDensityLabel.Text = $"{NpSnowDensitySlider.Value:0.0}x";
            NpSnowflakeSlider.Value = ThemeManager.ClampNpSnowflakeAmount(ThemeManager.NpSnowflakeAmount);
            NpSnowflakeLabel.Text = $"{NpSnowflakeSlider.Value:0.0}x";
            NpUnderwaterBubbleSlider.Value = ThemeManager.ClampNpUnderwaterBubbleDensity(ThemeManager.NpUnderwaterBubbleDensity);
            NpUnderwaterBubbleLabel.Text = $"{NpUnderwaterBubbleSlider.Value:0.0}x";
            NpUnderwaterCausticSlider.Value = ThemeManager.ClampNpUnderwaterCausticIntensity(ThemeManager.NpUnderwaterCausticIntensity);
            NpUnderwaterCausticLabel.Text = $"{NpUnderwaterCausticSlider.Value:0.0}x";
            ChkNpUnderwaterFish.IsChecked = ThemeManager.NpUnderwaterFishEnabled;
            ChkNpUnderwaterSeaweed.IsChecked = ThemeManager.NpUnderwaterSeaweedEnabled;
            NpAnimationSpeedSlider.Value =
                ThemeManager.ClampNpBackgroundAnimationSpeed(ThemeManager.NpBackgroundAnimationSpeed);
            NpAnimationSpeedLabel.Text = $"{NpAnimationSpeedSlider.Value:0.0}x";
            NpUpdateBgEffectRowsVisibility();

            // Populate visualizer theme combo
            foreach (var vt in ThemeManager.GetAvailableVisualizerThemeNames())
                VisualizerThemeCombo.Items.Add(vt);
            VisualizerThemeCombo.SelectedItem = ThemeManager.IsVisualizerFollowingPlaybar
                ? "Follow Playbar"
                : ThemeManager.CurrentVisualizerTheme;
            LoadCustomThemeEditor(ThemeManager.GetThemeDefinition(ThemeManager.CurrentTheme)
                ?? (CustomThemeDefinition.CreateDefault() with
                {
                    Name = $"{ThemeManager.CurrentTheme} Custom",
                    BaseTheme = ThemeManager.CurrentTheme
                }));

            // Set play option checkboxes from saved state
            ChkAutoPlay.IsChecked = ThemeManager.AutoPlayNext;
            ChkNormalization.IsChecked = ThemeManager.AudioNormalization;
            ChkCrossfade.IsChecked = ThemeManager.Crossfade;
            CrossfadeSlider.Value = ThemeManager.CrossfadeDuration;
            CrossfadeDurationLabel.Text = $"{ThemeManager.CrossfadeDuration}s";
            CrossfadeDurationBox.Text = ThemeManager.CrossfadeDuration.ToString();
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

            // Initialize service visibility checkboxes
            var visibleChecks = new[] { ChkServiceVisible0, ChkServiceVisible1, ChkServiceVisible2, ChkServiceVisible3, ChkServiceVisible4, ChkServiceVisible5 };
            for (int i = 0; i < 6; i++)
                visibleChecks[i].IsChecked = ThemeManager.MusicServiceSlotVisible[i];

            // Initialize main toolbar visibility checkboxes
            ChkShowWrappedButton.IsChecked = ThemeManager.ShowWrappedButton;
            ChkShowMiniPlayerButton.IsChecked = ThemeManager.ShowMiniPlayerButton;
            ChkShowMusicServiceButtons.IsChecked = ThemeManager.ShowMusicServiceButtons;

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

            // Libre.fm
            _realLibreApiKey = ThemeManager.LibreFmApiKey;
            _realLibreApiSecret = ThemeManager.LibreFmApiSecret;
            LibreFmApiKeyBox.Text = ThemeManager.LibreFmApiKey;
            LibreFmApiSecretBox.Text = ThemeManager.LibreFmApiSecret;
            ChkLibreFmEnabled.IsChecked = ThemeManager.LibreFmEnabled;
            UpdateLibreFmStatus();

            // ListenBrainz
            _realListenBrainzUsername = ThemeManager.ListenBrainzUsername;
            _realListenBrainzToken = ThemeManager.ListenBrainzUserToken;
            ListenBrainzUsernameBox.Text = ThemeManager.ListenBrainzUsername;
            ListenBrainzTokenBox.Text = ThemeManager.ListenBrainzUserToken;
            ChkListenBrainzEnabled.IsChecked = ThemeManager.ListenBrainzEnabled;
            UpdateListenBrainzStatus();

            // Maloja
            _realMalojaKey = ThemeManager.MalojaApiKey;
            MalojaServerBox.Text = ThemeManager.MalojaServerUrl;
            MalojaUsernameBox.Text = ThemeManager.MalojaUsername;
            MalojaKeyBox.Text = ThemeManager.MalojaApiKey;
            ChkMalojaEnabled.IsChecked = ThemeManager.MalojaEnabled;
            UpdateMalojaStatus();

            // Scrobble thresholds
            _suppressScrobbleTextEvents = true;
            ScrobbleAtPercentBox.Text = ThemeManager.ScrobbleAtPercent.ToString();
            ScrobbleAtSecondsBox.Text = ThemeManager.ScrobbleAtSeconds.ToString();
            MinScrobbleTrackSecondsBox.Text = ThemeManager.MinScrobbleTrackSeconds.ToString();
            ChkPauseScrobbling.IsChecked = ThemeManager.PauseScrobbling;
            _suppressScrobbleTextEvents = false;

            // Hide API keys by default (use dot masking)
            ApplyApiKeyVisibility();
            ApplyLibreFmVisibility();
            ApplyListenBrainzVisibility();
            ApplyMalojaVisibility();

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
            // Populate the combo list first; preset selection is resolved in a second pass
            // so that Auto (raw field == 0) wins over a numerically-matching preset.
            for (int ci = 0; ci < ThemeManager.ConcurrencyPresets.Length; ci++)
            {
                var (label, value) = ThemeManager.ConcurrencyPresets[ci];
                string display = value == 0
                    ? $"{label} — {ThemeManager.DefaultConcurrency} threads"
                    : label;
                ConcurrencyCombo.Items.Add(display);
            }
            int selectedConcurrencyIdx;
            if (ThemeManager.IsConcurrencyAuto)
            {
                selectedConcurrencyIdx = 0; // Auto preset is always index 0
            }
            else
            {
                int savedValue = ThemeManager.MaxConcurrency;
                int matchIdx = -1;
                for (int ci = 0; ci < ThemeManager.ConcurrencyPresets.Length; ci++)
                {
                    var (_, value) = ThemeManager.ConcurrencyPresets[ci];
                    if (value > 0 && value == savedValue) { matchIdx = ci; break; }
                }
                if (matchIdx >= 0)
                {
                    selectedConcurrencyIdx = matchIdx;
                }
                else
                {
                    selectedConcurrencyIdx = ThemeManager.ConcurrencyPresets.Length - 1; // Custom
                    CustomCpuPanel.Visibility = Visibility.Visible;
                    CustomCpuBox.Text = savedValue.ToString();
                }
            }
            ConcurrencyCombo.SelectedIndex = selectedConcurrencyIdx;
            ConcurrencyInfoText.Text = $"Your system has {Environment.ProcessorCount} logical processors. " +
                $"Presets scale dynamically to your hardware. Lower values reduce CPU spikes.";

            // Memory limit — same Auto-wins-over-numeric-match policy as concurrency above
            for (int mi = 0; mi < ThemeManager.MemoryPresets.Length; mi++)
            {
                var (label, valueMB) = ThemeManager.MemoryPresets[mi];
                string display = valueMB == 0
                    ? $"{label} — {ThemeManager.DefaultMemoryMB:N0} MB"
                    : label;
                MemoryLimitCombo.Items.Add(display);
            }
            int selectedMemoryIdx;
            if (ThemeManager.IsMemoryAuto)
            {
                selectedMemoryIdx = 0;
            }
            else
            {
                int savedValueMB = ThemeManager.MaxMemoryMB;
                int matchIdx = -1;
                for (int mi = 0; mi < ThemeManager.MemoryPresets.Length; mi++)
                {
                    var (_, valueMB) = ThemeManager.MemoryPresets[mi];
                    if (valueMB > 0 && valueMB == savedValueMB) { matchIdx = mi; break; }
                }
                if (matchIdx >= 0)
                {
                    selectedMemoryIdx = matchIdx;
                }
                else
                {
                    selectedMemoryIdx = ThemeManager.MemoryPresets.Length - 1; // Custom
                    CustomMemPanel.Visibility = Visibility.Visible;
                    CustomMemBox.Text = savedValueMB.ToString();
                }
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

            // Preload next track
            ChkPreloadNextTrack.IsChecked = ThemeManager.PreloadNextTrackEnabled;

            // NP color cache
            ChkNpColorCache.IsChecked = ThemeManager.NpColorCacheEnabled;
            ChkNpColorCachePersist.IsChecked = ThemeManager.NpColorCachePersist;
            ChkNpRememberManualColors.IsChecked = ThemeManager.NpRememberManualColorPicks;
            ChkNpAlbumBackdrop.IsChecked = ThemeManager.NpAlbumBackdropEnabled;
            UpdateNpColorCacheStatus();

            // NP "look up this song" services (independent of main-window slots)
            InitNpSearchServiceControls();

            // Reduce Motion (inverse of the legacy AnimationsEnabled flag)
            ChkReduceMotion.IsChecked = ThemeManager.ReduceMotion;

            // Battery Saver + GPU acceleration
            InitPerformanceControls();

            // Scan cache
            ChkScanCache.IsChecked = ThemeManager.ScanCacheEnabled;
            ChkFocusNewFiles.IsChecked = ThemeManager.FocusNewlyAddedFilesEnabled;
            ChkRestoreLastSession.IsChecked = ThemeManager.RestoreLastSessionEnabled;
            UpdateCacheStatus();

            // Local stats collection
            ChkStatsCollection.IsChecked = ThemeManager.StatsCollectionEnabled;

            // Crash logging
            ChkCrashLogging.IsChecked = ThemeManager.CrashLoggingEnabled;

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

            ColFavoriteCb.IsChecked = ThemeManager.ShowFavoritesColumn;
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

        /// <summary>
        /// Tints this window with the Now Playing ColorMatch palette instead of the app theme.
        /// Called by MainWindow right after construction when Settings is opened while the NP
        /// screen is showing with ColorMatch on, so Settings visually matches that screen.
        /// The brushes are written into this window's own resource scope, overriding the
        /// app-level theme tokens the controls bind to via DynamicResource.
        /// </summary>
        public void ApplyColorMatchTint(IReadOnlyDictionary<string, Brush> brushes)
        {
            if (brushes == null) return;
            foreach (var kvp in brushes)
            {
                // The NP scoped palette makes floating-surface *fills* slightly translucent
                // (nice for popups layered over the Now Playing screen). But this Settings
                // window is a top-level AllowsTransparency window whose root Border IS that
                // fill — a translucent fill there shows the desktop through the window. Force
                // background fills fully opaque here while leaving borders, and the source
                // palette the NP popups share, untouched.
                Resources[kvp.Key] = kvp.Key.EndsWith("Bg", StringComparison.Ordinal)
                    ? MakeOpaque(kvp.Value)
                    : kvp.Value;
            }
        }

        /// <summary>Returns a fully-opaque copy of a translucent solid brush; passes anything else through.</summary>
        private static Brush MakeOpaque(Brush brush)
        {
            if (brush is SolidColorBrush scb && scb.Color.A < 255)
            {
                var c = scb.Color;
                var opaque = new SolidColorBrush(Color.FromRgb(c.R, c.G, c.B));
                opaque.Freeze();
                return opaque;
            }
            return brush;
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

        // Drag the window from anywhere on its surface, except when the click lands on an
        // interactive control (so buttons, tabs, sliders, combo boxes, text fields, list items
        // still work normally). Large containers (TabControl body, ScrollViewers, panels) are NOT
        // treated as interactive, so their empty areas remain draggable.
        private void Window_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton != MouseButton.Left) return;
            if (IsInteractiveOriginalSource(e.OriginalSource as DependencyObject)) return;
            try { DragMove(); } catch { /* DragMove throws if the button was already released */ }
        }

        private static bool IsInteractiveOriginalSource(DependencyObject? src)
        {
            while (src != null)
            {
                switch (src)
                {
                    case System.Windows.Controls.Primitives.ButtonBase:       // Button, CheckBox, RadioButton, RepeatButton, ToggleButton
                    case System.Windows.Controls.Primitives.RangeBase:        // Slider, ScrollBar, ProgressBar
                    case System.Windows.Controls.Primitives.TextBoxBase:      // TextBox, RichTextBox
                    case System.Windows.Controls.PasswordBox:
                    case System.Windows.Controls.ComboBox:
                    case System.Windows.Controls.ListBox:
                    case System.Windows.Controls.ListBoxItem:
                    case System.Windows.Controls.TabItem:
                    case System.Windows.Controls.Primitives.Thumb:
                        return true;
                }
                src = System.Windows.Media.VisualTreeHelper.GetParent(src)
                      ?? (src as FrameworkElement)?.Parent;
            }
            return false;
        }

        private void ThemeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_initializing) return;
            if (ThemeCombo.SelectedItem is string theme)
            {
                ThemeManager.ApplyTheme(theme);
                LoadCustomThemeEditor(ThemeManager.GetThemeDefinition(theme)
                    ?? (CreateThemeFromEditor() with { Name = $"{theme} Custom", BaseTheme = theme }));
            }
        }

        private void RefreshThemeCombos(string selectedTheme)
        {
            var wasInitializing = _initializing;
            _initializing = true;

            ThemeCombo.Items.Clear();
            foreach (var theme in ThemeManager.GetAvailableThemeNames())
                ThemeCombo.Items.Add(theme);
            ThemeCombo.SelectedItem = ThemeCombo.Items.Contains(selectedTheme) ? selectedTheme : "Blurple";

            VisualizerThemeCombo.Items.Clear();
            foreach (var theme in ThemeManager.GetAvailableVisualizerThemeNames())
                VisualizerThemeCombo.Items.Add(theme);
            VisualizerThemeCombo.SelectedItem = ThemeManager.IsVisualizerFollowingPlaybar
                ? "Follow Playbar"
                : ThemeManager.CurrentVisualizerTheme;

            _initializing = wasInitializing;
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

        private void MainPlaybarAnimationCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_initializing) return;
            if (MainPlaybarAnimationCombo.SelectedItem is PlaybarAnimationStyle style)
            {
                ThemeManager.MainPlaybarAnimationStyle = style;
                ThemeManager.SavePlayOptions();
            }
        }

        private void NpPlaybarAnimationCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_initializing) return;
            if (NpPlaybarAnimationCombo.SelectedItem is PlaybarAnimationStyle style)
            {
                ThemeManager.NpPlaybarAnimationStyle = style;
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
            int val = (int)Math.Round(CrossfadeSlider.Value);
            CrossfadeDurationLabel.Text = $"{val}s";
            CrossfadeDurationBox.Text = val.ToString();
            ThemeManager.CrossfadeDuration = val;
            ThemeManager.SavePlayOptions();
        }

        private void CrossfadeDurationBox_LostFocus(object sender, RoutedEventArgs e) =>
            ApplyCrossfadeDurationBox();

        private void CrossfadeDurationBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Enter) return;
            ApplyCrossfadeDurationBox();
            Keyboard.ClearFocus();
            e.Handled = true;
        }

        private void ApplyCrossfadeDurationBox()
        {
            if (_initializing) return;

            if (!int.TryParse(CrossfadeDurationBox.Text, out var val))
                val = ThemeManager.CrossfadeDuration;

            val = Math.Clamp(val, 1, 30);
            CrossfadeSlider.Value = val;
            CrossfadeDurationBox.Text = val.ToString();
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
                DrawCrossfadePreview();
            }
        }

        private void CrossfadeManualSkip_Changed(object sender, RoutedEventArgs e)
        {
            if (_initializing) return;
            ThemeManager.CrossfadeOnManualSkip = ChkCrossfadeOnManualSkip.IsChecked == true;
            ThemeManager.SavePlayOptions();
        }

        // ═══════════════════════════════════════════
        //  Crossfade Curve Preview
        // ═══════════════════════════════════════════

        private void CrossfadeCurveCanvas_Loaded(object sender, RoutedEventArgs e) => DrawCrossfadePreview();

        private void DrawCrossfadePreview()
        {
            if (_initializing) return;

            double w = CrossfadeCurveCanvas.ActualWidth;
            double h = CrossfadeCurveCanvas.ActualHeight;
            if (w <= 0 || h <= 0) return;

            const int steps = 60;
            var fadeOutPoints = new PointCollection(steps);
            var fadeInPoints = new PointCollection(steps);
            var curve = ThemeManager.CrossfadeCurve;

            for (int i = 0; i < steps; i++)
            {
                float t = i / (float)(steps - 1);
                float outAmp = AudioPlayer.CrossfadeCurveFadeOut(t, curve);
                float inAmp = AudioPlayer.CrossfadeCurveFadeIn(t, curve);

                double x = w * t;
                fadeOutPoints.Add(new Point(x, h * (1.0 - outAmp)));
                fadeInPoints.Add(new Point(x, h * (1.0 - inAmp)));
            }

            CrossfadeFadeOutLine.Points = fadeOutPoints;
            CrossfadeFadeInLine.Points = fadeInPoints;
        }

        private void AnimateCrossfade_Click(object sender, RoutedEventArgs e)
        {
            if (_crossfadePreviewTimer != null)
            {
                _crossfadePreviewTimer.Stop();
                _crossfadePreviewTimer = null;
                CrossfadePlayhead.Visibility = Visibility.Collapsed;
                BtnAnimateCrossfade.Content = "Animate Preview";
                return;
            }

            DrawCrossfadePreview();
            BtnAnimateCrossfade.Content = "Stop Animation";
            CrossfadePlayhead.Visibility = Visibility.Visible;
            int previewStep = 0;
            const int totalSteps = 60;
            var curve = ThemeManager.CrossfadeCurve;

            _crossfadePreviewTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(30) };
            _crossfadePreviewTimer.Tick += (_, _) =>
            {
                if (previewStep >= totalSteps)
                {
                    _crossfadePreviewTimer?.Stop();
                    _crossfadePreviewTimer = null;
                    CrossfadePlayhead.Visibility = Visibility.Collapsed;
                    BtnAnimateCrossfade.Content = "Animate Preview";
                    return;
                }

                float t = previewStep / (float)(totalSteps - 1);
                float outAmp = AudioPlayer.CrossfadeCurveFadeOut(t, curve);
                float inAmp = AudioPlayer.CrossfadeCurveFadeIn(t, curve);

                double w = CrossfadeCurveCanvas.ActualWidth;
                double h = CrossfadeCurveCanvas.ActualHeight;
                if (w > 0 && h > 0)
                {
                    double x = w * t;
                    double yOut = h * (1.0 - outAmp);
                    double yIn = h * (1.0 - inAmp);
                    Canvas.SetLeft(CrossfadePlayhead, x - 4);
                    Canvas.SetTop(CrossfadePlayhead, (yOut + yIn) / 2 - 4);
                }
                previewStep++;
            };
            _crossfadePreviewTimer.Start();
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

        private void ServiceVisible_Changed(object sender, RoutedEventArgs e)
        {
            if (_initializing) return;
            var checks = new[] { ChkServiceVisible0, ChkServiceVisible1, ChkServiceVisible2, ChkServiceVisible3, ChkServiceVisible4, ChkServiceVisible5 };
            for (int i = 0; i < 6; i++)
                ThemeManager.MusicServiceSlotVisible[i] = checks[i].IsChecked == true;
            ThemeManager.SavePlayOptions();
        }

        private void ShowWrappedButton_Changed(object sender, RoutedEventArgs e)
        {
            if (_initializing) return;
            ThemeManager.ShowWrappedButton = ChkShowWrappedButton.IsChecked == true;
            ThemeManager.SavePlayOptions();
            (Owner as MainWindow)?.ApplyToolbarButtonVisibility();
        }

        private void ShowMiniPlayerButton_Changed(object sender, RoutedEventArgs e)
        {
            if (_initializing) return;
            ThemeManager.ShowMiniPlayerButton = ChkShowMiniPlayerButton.IsChecked == true;
            ThemeManager.SavePlayOptions();
            (Owner as MainWindow)?.ApplyToolbarButtonVisibility();
        }

        private void ShowMusicServiceButtons_Changed(object sender, RoutedEventArgs e)
        {
            if (_initializing) return;
            ThemeManager.ShowMusicServiceButtons = ChkShowMusicServiceButtons.IsChecked == true;
            ThemeManager.SavePlayOptions();
            (Owner as MainWindow)?.ApplyToolbarButtonVisibility();
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

            // Track analysis features that flip OFF→ON so we can backfill just that column for
            // already-loaded rows (instead of forcing the user to clear + re-add everything).
            var newlyEnabled = new List<string>();
            void SetFeature(string header, bool nowEnabled)
            {
                bool wasEnabled = ThemeManager.IsAnalysisColumnEnabled(header);
                ThemeManager.SetAnalysisColumnEnabled(header, nowEnabled);
                if (nowEnabled && !wasEnabled) newlyEnabled.Add(header);
            }

            SetFeature("Clipping", ColClippingCb.IsChecked == true);
            SetFeature("BPM", ColBpmCb.IsChecked == true);
            SetFeature("DR", ColDRCb.IsChecked == true);
            SetFeature("MQA", ColMqaCb.IsChecked == true);
            SetFeature("AI", ColAiCb.IsChecked == true);
            SetFeature("Fake Stereo", ColStereoCb.IsChecked == true);
            SetFeature("Silence", ColSilenceCb.IsChecked == true);
            SetFeature("True Peak", ColTruePeakCb.IsChecked == true);
            SetFeature("LUFS", ColLufsCb.IsChecked == true);
            SetFeature("Rip Quality", ColRipQualityCb.IsChecked == true);
            ChkDefaultAi.IsChecked = ThemeManager.DefaultAiDetectionEnabled;

            // Build comma-separated list of hidden column headers
            var hidden = new List<string>();
            void Check(CheckBox cb, string header)
            {
                if (cb.IsChecked != true)
                    hidden.Add(ThemeManager.NormalizeColumnHeader(header));
            }

            // Flagless default-hidden columns (★, Date Created) are driven by explicit, persisted
            // preferences so they survive every default re-application (theme/playbar changes,
            // the usable-set fallback, version migrations). Check() keeps the raw string
            // consistent; SetColumnUserShown() is what actually makes the choice stick.
            ThemeManager.SetColumnUserShown("★", ColFavoriteCb.IsChecked == true);
            ThemeManager.SetColumnUserShown("Date Created", ColDateCreatedCb.IsChecked == true);
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
            {
                mw.ApplyColumnVisibility();
                // Backfill just the newly-enabled analysis column(s) for already-loaded rows.
                if (newlyEnabled.Count > 0)
                    mw.RefreshColumnsForFeatures(newlyEnabled);
            }
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            // Auto-hide API keys when closing
            _apiKeysVisible = false;
            _libreFmKeysVisible = false;
            _listenBrainzTokenVisible = false;
            _malojaKeyVisible = false;
            _discordIdVisible = false;
            ApplyApiKeyVisibility();
            ApplyLibreFmVisibility();
            ApplyListenBrainzVisibility();
            ApplyMalojaVisibility();
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

        private void PreloadNextTrack_Changed(object sender, RoutedEventArgs e)
        {
            if (_initializing) return;
            ThemeManager.PreloadNextTrackEnabled = ChkPreloadNextTrack.IsChecked == true;
            ThemeManager.SavePlayOptions();
        }

        private void SelectNpBackground_Click(object sender, RoutedEventArgs e)
        {
            string? path = PickAndCopyBackgroundImage("np");
            if (path == null) return;
            ThemeManager.NpCustomBackgroundImagePath = path;
            ThemeManager.NpBackgroundMode = "CustomImage";
            ThemeManager.SavePlayOptions();
            if (Owner is MainWindow mw)
                mw.NpRefreshBackdropFromSettings();
        }

        private void ResetNpBackground_Click(object sender, RoutedEventArgs e)
        {
            ThemeManager.NpCustomBackgroundImagePath = "";
            ThemeManager.NpBackgroundMode = "AlbumArt";
            ThemeManager.SavePlayOptions();
            if (Owner is MainWindow mw)
                mw.NpRefreshBackdropFromSettings();
        }

        private void SelectMainBackground_Click(object sender, RoutedEventArgs e)
        {
            string? path = PickAndCopyBackgroundImage("main");
            if (path == null) return;
            ThemeManager.MainBackgroundImagePath = path;
            ThemeManager.SavePlayOptions();
            if (Owner is MainWindow mw)
                mw.ApplyMainCustomBackground();
        }

        private void ResetMainBackground_Click(object sender, RoutedEventArgs e)
        {
            ThemeManager.MainBackgroundImagePath = "";
            ThemeManager.SavePlayOptions();
            if (Owner is MainWindow mw)
                mw.ApplyMainCustomBackground();
        }

        private string? PickAndCopyBackgroundImage(string prefix)
        {
            var dlg = new OpenFileDialog
            {
                Title = "Choose background image",
                Filter = "Image files|*.png;*.jpg;*.jpeg;*.webp;*.bmp|All files|*.*"
            };
            if (dlg.ShowDialog(this) != true)
                return null;

            string ext = Path.GetExtension(dlg.FileName);
            if (string.IsNullOrWhiteSpace(ext))
                ext = ".png";
            string dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "AudioAuditor",
                "backgrounds");
            Directory.CreateDirectory(dir);
            string dest = Path.Combine(dir, $"{prefix}-{Guid.NewGuid():N}{ext.ToLowerInvariant()}");
            File.Copy(dlg.FileName, dest, overwrite: false);
            return dest;
        }


        // ═══════════════════════════════════════════
        //  Scan Cache
        // ═══════════════════════════════════════════

        private void ReduceMotion_Changed(object sender, RoutedEventArgs e)
        {
            if (_initializing) return;
            ThemeManager.ReduceMotion = ChkReduceMotion.IsChecked == true;
            ThemeManager.SavePlayOptions();
            if (Owner is MainWindow mw)
                mw.ApplyPerformancePolicy();
        }

        private void ScanCache_Changed(object sender, RoutedEventArgs e)
        {
            if (_initializing) return;
            ThemeManager.ScanCacheEnabled = ChkScanCache.IsChecked == true;
            ThemeManager.SavePlayOptions();
            UpdateCacheStatus();
        }

        private void FocusNewFiles_Changed(object sender, RoutedEventArgs e)
        {
            if (_initializing) return;
            ThemeManager.FocusNewlyAddedFilesEnabled = ChkFocusNewFiles.IsChecked == true;
            ThemeManager.SavePlayOptions();
        }

        private void RestoreLastSession_Changed(object sender, RoutedEventArgs e)
        {
            if (_initializing) return;
            bool nowOn = ChkRestoreLastSession.IsChecked == true;
            ThemeManager.RestoreLastSessionEnabled = nowOn;

            // When the user turns this on, scan cache is what makes the restored list
            // reopen instantly (cached results, no re-analysis). Auto-enable it and tell
            // the user once — this matches the design discussion: the two settings can
            // coexist, but restore implicitly requires the cache.
            bool autoEnabledCache = false;
            if (nowOn && !ThemeManager.ScanCacheEnabled)
            {
                ThemeManager.ScanCacheEnabled = true;
                ChkScanCache.IsChecked = true;
                autoEnabledCache = true;
            }

            ThemeManager.SavePlayOptions();
            UpdateCacheStatus();

            if (autoEnabledCache && !ThemeManager.RestoreSessionCacheNoticeShown)
            {
                if (Owner is MainWindow mw)
                {
                    mw.ShowThemedNotice(
                        "Scan cache turned on too",
                        "Restore last session works alongside the scan cache so your restored files load instantly without re-analysis. You can turn either off any time.");
                }
                ThemeManager.RestoreSessionCacheNoticeShown = true;
                ThemeManager.SavePlayOptions();
            }
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

        private void StatsCollection_Changed(object sender, RoutedEventArgs e)
        {
            if (_initializing) return;
            ThemeManager.StatsCollectionEnabled = ChkStatsCollection.IsChecked == true;
            ThemeManager.SavePlayOptions();
        }

        private void ViewWrapped_Click(object sender, RoutedEventArgs e)
        {
            // Wrapped is now an in-app overlay on the main window. Close Settings and show it there.
            var main = Owner as MainWindow;
            Close();
            main?.ShowWrappedOverlay();
        }

        private void CrashLogging_Changed(object sender, RoutedEventArgs e)
        {
            if (_initializing) return;
            ThemeManager.CrashLoggingEnabled = ChkCrashLogging.IsChecked == true;
            ThemeManager.SavePlayOptions();
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

        // Scrobbling (Last.fm + Libre.fm + ListenBrainz + Thresholds + API key visibility) - see SettingsWindow.Scrobbling.cs
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

        private void OpenCredits_Click(object sender, RoutedEventArgs e)
        {
            // Hide Settings while Credits is open, then bring it back when Credits closes, so the two
            // themed windows don't stack on top of each other.
            var credits = new CreditsWindow { Owner = this, WindowStartupLocation = WindowStartupLocation.CenterScreen };
            Hide();
            try
            {
                credits.ShowDialog();
            }
            finally
            {
                Show();
                Activate();
            }
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
