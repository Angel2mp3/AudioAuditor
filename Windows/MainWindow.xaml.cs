using System;
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
        private CancellationTokenSource? _analysisCts;
        private bool _isAnalyzing;

        // ETA tracking for progress bar
        private DateTime _analysisStartTime;

        // Shared analysis state for re-entrant file additions
        private int _analysisTotal;
        private int _analysisCompleted;
        private int _activeBatches;
        private SemaphoreSlim? _analysisSemaphore;
        private SemaphoreSlim? _shLabsSemaphore;
        private ManualResetEventSlim _analysisPauseEvent = new(true); // signaled = not paused

        // Audio player
        private readonly AudioPlayer _player = new();
        private readonly DispatcherTimer _playerTimer;
        private bool _isSeeking;
        private bool _npIsSeeking;  // drag guard for NP seek slider

        // SMTC (media session for FluentFlyout/Windows media overlay)
        private SmtcService? _smtc;

        // System tray icon (minimize to tray)
        private System.Windows.Forms.NotifyIcon? _trayIcon;
        private bool _forceClose; // true when exiting via tray context menu

        // Search
        private string _searchText = "";
        private AudioStatus? _statusFilter = null;
        private bool _mismatchedBitrateFilter;

        // Drag-from-grid: track whether we initiated an outbound drag
        private bool _isOutboundDrag;

        // Seek cooldown to prevent snap-back
        private DateTime _lastSeekTime = DateTime.MinValue;
        private TimeSpan _lastSeekTargetPosition = TimeSpan.Zero;

        // Horizontal scroll tracking — suppress vertical drift during touchpad horizontal swipes
        private DateTime _lastHorizontalScrollTime = DateTime.MinValue;

        // Track the currently displayed spectrogram file
        private AudioFileInfo? _currentSpectrogramFile;

        // In-memory spectrogram cache keyed by "filePath|linearScale|channel|magma|hifi"
        // Cleared when spectrogram settings change or manually via settings
        private readonly Dictionary<string, System.Windows.Media.Imaging.BitmapSource> _spectrogramCache =
            new(StringComparer.OrdinalIgnoreCase);
        private readonly LinkedList<string> _spectrogramCacheLru = new();
        private const int SpectrogramCacheMaxEntries = 30;

        // In-memory NP album-art color cache (keyed by file path)
        private readonly Dictionary<string, AlbumColorExtractor.DominantColors> _npColorCache =
            new(StringComparer.OrdinalIgnoreCase);
        private readonly LinkedList<string> _npColorCacheLru = new();
        private const int NpColorCacheMaxEntries = 50;

        // Background color preload for the next track
        private CancellationTokenSource? _npPreloadCts;

        // Generation counter to prevent stale async color tasks from overwriting current track
        private int _npColorGeneration;

        // Queue system
        private readonly ObservableCollection<AudioFileInfo> _queue = new();

        // Shuffle mode — uses a pre-shuffled deck that is rebuilt when exhausted
        private bool _shuffleMode;
        private readonly Random _shuffleRng = new();
        private readonly List<AudioFileInfo> _shuffleDeck = new(); // pre-shuffled playback order
        private int _shuffleDeckIndex; // current position in the deck

        // Now Playing panel state
        private DispatcherTimer? _npUpdateTimer;
        private LyricsResult _npCurrentLyrics = LyricsResult.Empty;
        private int _npCurrentLyricIndex = -1;
        private readonly List<TextBlock> _npLyricTextBlocks = new();
        private LyricService.LyricProvider _npLyricProvider = LyricService.LyricProvider.Auto;
        private bool _npVisible;
        private bool _npVisualizerEnabled;
        private bool _npColorMatchEnabled;
        private DateTime _lastTrackFinishedTime = DateTime.MinValue;
        private volatile bool _crossfadeEarlyTriggered;
        private DateTime _lastOfflineNoticeTime = DateTime.MinValue;
        private string? _npLastTrackPath; // track which song is loaded in NP to detect changes
        private int _npVisualizerStyle; // NP has its own style selection
        private bool _mainVizWasActive; // remember main visualizer state when NP takes over

        // Album colors for color-match theming (set in NpApplyGlow)
        private System.Windows.Media.Color _npAlbumPrimary;
        private System.Windows.Media.Color _npAlbumSecondary;
        private System.Windows.Media.Color _npAlbumBackground;

        // Album colors for main-window color-match theming
        private System.Windows.Media.Color _mainAlbumPrimary;
        private System.Windows.Media.Color _mainAlbumSecondary;
        private System.Windows.Media.Color _npVizColorPrimary;   // for visualizer tinting
        private System.Windows.Media.Color _npVizColorSecondary; // for visualizer tinting
        private double _npVizBarHeight = 100; // default visualizer bar height
        private bool _npVizResizing;          // drag-resize in progress
        private double _npVizResizeStartY;    // mouse Y at drag start
        private double _npVizResizeStartH;    // height at drag start
        private int _npVizPlacement;          // 0 = full-width, 1 = under-cover
        private bool _npLyricsHidden;             // lyrics-off mode (pure viz + art)
        private bool _npTranslateEnabled;         // show translated lyrics alongside original
        private string _npTranslateFrom = "auto"; // source language (auto = detect)
        private string _npTranslateTo = "en";     // target language
        private List<string>? _npTranslatedLines; // cached translation of current lyrics
        private bool _npKaraokeEnabled;           // word-by-word karaoke mode
        private int _npLyricsVersion;              // incremented each track change to discard stale lyrics
        private bool _npLyricsNeedCatchUp;         // force one immediate highlight update on next tick
        private bool _npSubCoverShowArtist = true; // true = Artist (default), false = Queue preview
        private bool _npPrefsLoaded;              // one-time load from ThemeManager
        private int _npCoverSize;                 // custom album cover size (0 = default)
        private int _npTitleSize;                 // custom title font size (0 = default)
        private int _npSubTextSize;               // custom artist/up-next font size (0 = default)
        private int _npLyricsSize;                // custom lyrics font size (0 = default)
        private int _npVizSize;                   // custom visualizer height (0 = default)
        private int _npLyricsOffsetX;             // horizontal lyrics offset in px (0 = default ~24px margin)
        private int _npCoverOffsetX;              // cover horizontal position offset
        private int _npCoverOffsetY;              // cover vertical position offset
        private int _npTitleOffsetX;              // title horizontal position offset
        private int _npTitleOffsetY;              // title vertical position offset
        private int _npArtistOffsetX;             // artist horizontal position offset
        private int _npArtistOffsetY;             // artist vertical position offset
        private int _npVizOffsetY;                // visualizer vertical position offset
        private DispatcherTimer? _npBgAnimTimer;  // animated background timer

        // Active visualizer canvas (NP or main)
        private Canvas VizTarget => (_npVisible && _npVisualizerEnabled)
            ? (_npVizPlacement == 1 ? NpUnderCoverVizCanvas : NpVisualizerCanvas)
            : VisualizerCanvas;

        private static readonly (LyricService.LyricProvider Provider, string Name)[] NpLyricProviders =
        {
            (LyricService.LyricProvider.Auto, "Auto"),
            (LyricService.LyricProvider.LrcFile, "LRC File"),
            (LyricService.LyricProvider.Embedded, "Embedded"),
            (LyricService.LyricProvider.LrcLib, "LRCLIB"),
            (LyricService.LyricProvider.Netease, "Netease"),
            (LyricService.LyricProvider.Musixmatch, "Musixmatch"),
        };
        private int _npProviderIndex;

        // Playback history for back-button navigation
        private readonly List<AudioFileInfo> _playHistory = new();
        private int _playHistoryIndex = -1;
        private bool _navigatingHistory; // true when playing from history (prevents re-pushing)

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
        private int _visualizerStyle; // 0=Classic Bars, 1=Mirrored Bars, 2=Particle Fountain, 3=Circle Rings, 4=Oscilloscope, 5=VU Meter

        // Animation occlusion pause
        private bool _isPausedForOcclusion;
        private DispatcherTimer? _occlusionCheckTimer;

        // Spectrogram options
        private bool _spectrogramLinearScale;
        private SpectrogramChannel _spectrogramChannel = SpectrogramChannel.Mono;
        private bool _spectrogramEndZoom;
        private double _spectrogramZoomLevel = 1.0;

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
            // Common formats
            ".mp3", ".flac", ".wav", ".ogg", ".aac", ".m4a", ".wma",
            ".aiff", ".aif", ".ape", ".wv", ".opus", ".alac", ".dsf", ".dff",
            // Previously added rare formats
            ".tta", ".mpc", ".spx", ".mp+",
            // Additional rare/uncommon formats
            ".mp2",           // MPEG Layer II (broadcast/radio audio)
            ".m4b",           // M4A audiobook container
            ".m4r",           // iPhone ringtone (M4A)
            ".mp4",           // MPEG-4 audio container
            ".3gp", ".3g2",   // 3GPP/3GPP2 mobile audio
            ".amr",           // Adaptive Multi-Rate (voice/mobile)
            ".ac3",           // Dolby AC-3 / Dolby Digital
            ".mka",           // Matroska audio container
            ".webm",          // WebM audio (Opus/Vorbis)
            ".tak",           // Tom's lossless Audio Kompressor (needs TAK codec)
            ".au",  ".snd",   // Sun/NeXT audio (legacy Unix)
        };

        private static readonly HashSet<string> ArchiveExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".zip", ".rar", ".7z", ".tar", ".gz", ".bz2", ".xz", ".tgz"
        };

        private static readonly HashSet<string> PlaylistExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".m3u", ".m3u8", ".pls"
        };

        public MainWindow()
        {
            InitializeComponent();

            // ── Integrity check (silent — only alerts on tampered builds) ──
            try
            {
                var (isTampered, _) = IntegrityVerifier.Verify();
                if (isTampered)
                {
                    Dispatcher.InvokeAsync(() =>
                    {
                        MessageBox.Show(
                            IntegrityVerifier.GetWarningMessage(),
                            "AudioAuditor — Security Warning",
                            MessageBoxButton.OK,
                            MessageBoxImage.Warning);
                    }, DispatcherPriority.Loaded);
                }
            }
            catch { /* never block startup */ }

            // Welcome dialog — shown on first launch or version update
            string currentVersion = System.Reflection.Assembly.GetExecutingAssembly()
                .GetName().Version is { } cv ? $"{cv.Major}.{cv.Minor}.{cv.Build}" : "0.0.0";
            bool showWelcome = !ThemeManager.FirstLaunchComplete || ThemeManager.WelcomeVersionSeen != currentVersion;
            if (showWelcome)
            {
                Dispatcher.InvokeAsync(() =>
                {
                    try
                    {
                        var dlg = new Windows.WelcomeDialog { Owner = this };
                        // Pre-populate feature checkboxes from current settings
                        dlg.ChkSilence.IsChecked = ThemeManager.SilenceDetectionEnabled;
                        dlg.ChkFakeStereo.IsChecked = ThemeManager.FakeStereoDetectionEnabled;
                        dlg.ChkDR.IsChecked = ThemeManager.DynamicRangeEnabled;
                        dlg.ChkTruePeak.IsChecked = ThemeManager.TruePeakEnabled;
                        dlg.ChkLufs.IsChecked = ThemeManager.LufsEnabled;
                        dlg.ChkClipping.IsChecked = ThemeManager.ClippingDetectionEnabled;
                        dlg.ChkBpm.IsChecked = ThemeManager.BpmDetectionEnabled;
                        dlg.ChkMqa.IsChecked = ThemeManager.MqaDetectionEnabled;
                        dlg.ChkDefaultAi.IsChecked = ThemeManager.DefaultAiDetectionEnabled;
                        dlg.ChkExperimentalAi.IsChecked = ThemeManager.ExperimentalAiDetection;
                        dlg.ChkRipQuality.IsChecked = ThemeManager.RipQualityEnabled;
                        dlg.ChkSHLabs.IsChecked = ThemeManager.SHLabsAiDetection;
                        if (dlg.ShowDialog() == true)
                        {
                            ThemeManager.OfflineModeEnabled = dlg.SelectedOffline;
                            ThemeManager.FirstLaunchComplete = true;
                            ThemeManager.SetRegistryFlag("FirstLaunchComplete", true);
                            ThemeManager.WelcomeVersionSeen = currentVersion;

                            // Apply feature toggles from welcome dialog
                            ThemeManager.SilenceDetectionEnabled = dlg.EnableSilenceDetection;
                            AudioAnalyzer.EnableSilenceDetection = dlg.EnableSilenceDetection;
                            ThemeManager.FakeStereoDetectionEnabled = dlg.EnableFakeStereoDetection;
                            AudioAnalyzer.EnableFakeStereoDetection = dlg.EnableFakeStereoDetection;
                            ThemeManager.DynamicRangeEnabled = dlg.EnableDynamicRange;
                            AudioAnalyzer.EnableDynamicRange = dlg.EnableDynamicRange;
                            ThemeManager.TruePeakEnabled = dlg.EnableTruePeak;
                            AudioAnalyzer.EnableTruePeak = dlg.EnableTruePeak;
                            ThemeManager.LufsEnabled = dlg.EnableLufs;
                            AudioAnalyzer.EnableLufs = dlg.EnableLufs;
                            ThemeManager.ClippingDetectionEnabled = dlg.EnableClippingDetection;
                            AudioAnalyzer.EnableClippingDetection = dlg.EnableClippingDetection;
                            ThemeManager.BpmDetectionEnabled = dlg.EnableBpmDetection;
                            AudioAnalyzer.EnableBpmDetection = dlg.EnableBpmDetection;
                            ThemeManager.MqaDetectionEnabled = dlg.EnableMqaDetection;
                            AudioAnalyzer.EnableMqaDetection = dlg.EnableMqaDetection;
                            ThemeManager.DefaultAiDetectionEnabled = dlg.EnableDefaultAiDetection;
                            AudioAnalyzer.EnableDefaultAiDetection = dlg.EnableDefaultAiDetection;
                            ThemeManager.ExperimentalAiDetection = dlg.EnableExperimentalAi;
                            AudioAnalyzer.EnableExperimentalAi = dlg.EnableExperimentalAi;
                            ThemeManager.RipQualityEnabled = dlg.EnableRipQuality;
                            AudioAnalyzer.EnableRipQuality = dlg.EnableRipQuality;
                            ThemeManager.SHLabsAiDetection = dlg.EnableSHLabs;

                            ThemeManager.SavePlayOptions();
                            ApplyColumnVisibility();

                            // If SH Labs was just enabled and privacy not yet accepted, show privacy notice
                            if (dlg.EnableSHLabs && !ThemeManager.SHLabsPrivacyAccepted)
                            {
                                _shLabsPrivacyFromFeatureConfig = true;
                                ShowSHLabsPrivacyOverlay();
                            }
                        }
                        UpdateOfflineBadge();
                    }
                    catch { /* never block startup */ }
                }, DispatcherPriority.Loaded);
            }

            // Load scan cache if enabled
            if (ThemeManager.ScanCacheEnabled)
                ScanCacheService.EnsureLoaded();

            // Restore saved column layout (order + widths)
            RestoreColumnLayout();
            ApplyColumnVisibility();

            // Column header right-click menu (created in code to avoid WPF style setter event bug)
            var headerMenu = new ContextMenu();
            var hideItem = new MenuItem { Header = "Hide Column" };
            hideItem.Click += HideColumn_Click;
            var showAllItem = new MenuItem { Header = "Show All Columns" };
            showAllItem.Click += ShowAllColumns_Click;
            headerMenu.Items.Add(hideItem);
            headerMenu.Items.Add(showAllItem);
            var headerStyle = FileGrid.ColumnHeaderStyle ?? new Style(typeof(DataGridColumnHeader));
            var newStyle = new Style(typeof(DataGridColumnHeader), headerStyle);
            newStyle.Setters.Add(new Setter(DataGridColumnHeader.ContextMenuProperty, headerMenu));
            FileGrid.ColumnHeaderStyle = newStyle;

            // Set up filtered view
            _filteredView = CollectionViewSource.GetDefaultView(_files);
            _filteredView.Filter = SearchFilter;
            _filteredView.GroupDescriptions.Add(new PropertyGroupDescription("FolderPath"));
            FileGrid.ItemsSource = _filteredView;

            _player.PlaybackStopped += Player_PlaybackStopped;
            _player.TrackFinished += Player_TrackFinished;
            _player.GaplessTrackChanged += Player_GaplessTrackChanged;

            _playerTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
            _playerTimer.Tick += PlayerTimer_Tick;

            // Initialize music service button labels
            UpdateServiceButtonLabels();

            // Restore visualizer mode
            _visualizerMode = ThemeManager.VisualizerMode;
            _visualizerStyle = ThemeManager.VisualizerStyle;
            UpdateVisualizerToggleText();
            UpdateVisualizerStyleText();

            // Restore spectrogram display preferences
            _spectrogramLinearScale = ThemeManager.SpectrogramLinearScale;
            _spectrogramChannel = ThemeManager.SpectrogramDifferenceChannel ? SpectrogramChannel.Difference : SpectrogramChannel.Mono;
            UpdateSpectrogramScaleText();
            UpdateSpectrogramChannelText();

            // Initialize equalizer UI
            InitializeEqualizerSliders();
            ChkEqEnabled.IsChecked = ThemeManager.EqualizerEnabled;
            EqPanel.Visibility = Visibility.Collapsed;

            // Load persisted NP color cache if enabled
            LoadNpColorCacheFromDisk();

            // Restore loop mode UI
            UpdateLoopUI();

            // Restore persisted volume so it survives across sessions
            try
            {
                double savedVol = Math.Clamp(ThemeManager.Volume, 0, 100);
                VolumeSlider.Value = savedVol;
                if (NpVolumeSlider != null) NpVolumeSlider.Value = savedVol;
                _player.Volume = (float)(savedVol / 100.0);
            }
            catch { /* slider not yet initialized — defaults will apply */ }

            // Main color match is restored via ApplyMainColorMatch on track change

            // Initialize Discord Rich Presence
            if (ThemeManager.DiscordRpcEnabled && !string.IsNullOrWhiteSpace(ThemeManager.DiscordRpcClientId))
            {
                _discord.Enable();
                // Idle presence set automatically on Ready event
            }

            // Initialize Last.fm
            if (ThemeManager.LastFmEnabled && !string.IsNullOrEmpty(ThemeManager.LastFmSessionKey))
                _lastFm.Configure(ThemeManager.LastFmApiKey, ThemeManager.LastFmApiSecret, ThemeManager.LastFmSessionKey);

            // Update Last.fm status indicator
            UpdateLastFmStatusIndicator();

            // Animation occlusion pause
            this.Activated += OnWindowActivated;
            this.Deactivated += OnWindowDeactivated;
            this.StateChanged += OnWindowStateChanged;

            // Initialize footer support link visibility
            InitializeFooterSupport();

            // AI config is now always dismissed (popup removed in v1.4.5)
            if (!ThemeManager.AiConfigDismissed)
            {
                ThemeManager.AiConfigDismissed = true;
                ThemeManager.SetRegistryFlag("AiConfigDismissed", true);
                ThemeManager.SavePlayOptions();
            }

            // Sync feature toggle flags from persisted settings → AudioAnalyzer
            AudioAnalyzer.EnableSilenceDetection = ThemeManager.SilenceDetectionEnabled;
            AudioAnalyzer.EnableFakeStereoDetection = ThemeManager.FakeStereoDetectionEnabled;
            AudioAnalyzer.EnableDynamicRange = ThemeManager.DynamicRangeEnabled;
            AudioAnalyzer.EnableTruePeak = ThemeManager.TruePeakEnabled;
            AudioAnalyzer.EnableLufs = ThemeManager.LufsEnabled;
            AudioAnalyzer.EnableClippingDetection = ThemeManager.ClippingDetectionEnabled;
            AudioAnalyzer.EnableMqaDetection = ThemeManager.MqaDetectionEnabled;
            AudioAnalyzer.EnableDefaultAiDetection = ThemeManager.DefaultAiDetectionEnabled;
            AudioAnalyzer.EnableExperimentalAi = ThemeManager.ExperimentalAiDetection;
            AudioAnalyzer.EnableRipQuality = ThemeManager.RipQualityEnabled;

            // Feature config overlay is still available via Settings but no longer shown automatically.
            // WelcomeDialog (above) now handles first-run and version-update feature configuration.

            // Reflect offline mode in UI badge
            Dispatcher.InvokeAsync(UpdateOfflineBadge, DispatcherPriority.Loaded);

            // Silent update check on startup
            if (ThemeManager.CheckForUpdates)
            {
                Task.Run(async () =>
                {
                    try
                    {
                        string currentVersion = System.Reflection.Assembly.GetExecutingAssembly()
                            .GetName().Version is { } v ? $"{v.Major}.{v.Minor}.{v.Build}" : "0.0.0";
                        bool hasUpdate = await UpdateChecker.CheckForUpdateAsync(currentVersion);
                        if (hasUpdate && UpdateChecker.LatestVersion != null)
                        {
                            Dispatcher.InvokeAsync(() =>
                            {
                                UpdateLatestText.Text = $"AudioAuditor v{UpdateChecker.LatestVersion} is available!";
                                UpdateCurrentText.Text = $"You're currently on v{currentVersion}";
                                UpdateOverlay.Visibility = Visibility.Visible;
                            });
                        }
                    }
                    catch { /* silently ignore update check failures */ }
                });
            }
        }

        // ── System tray setup ──
        // ── Dark-themed tray context menu color table ──
        private class DarkColorTable : System.Windows.Forms.ProfessionalColorTable
        {
            private readonly System.Drawing.Color _bg = System.Drawing.Color.FromArgb(30, 30, 46);      // PanelBg-ish
            private readonly System.Drawing.Color _hover = System.Drawing.Color.FromArgb(58, 58, 74);   // ButtonBg-ish
            private readonly System.Drawing.Color _accent = System.Drawing.Color.FromArgb(88, 101, 242); // AccentColor-ish
            private readonly System.Drawing.Color _text = System.Drawing.Color.FromArgb(224, 224, 224);  // TextPrimary-ish
            private readonly System.Drawing.Color _border = System.Drawing.Color.FromArgb(58, 58, 74);   // ButtonBorder-ish

            public override System.Drawing.Color MenuBorder => _border;
            public override System.Drawing.Color MenuItemBorder => System.Drawing.Color.Transparent;
            public override System.Drawing.Color MenuItemSelected => _hover;
            public override System.Drawing.Color MenuItemSelectedGradientBegin => _hover;
            public override System.Drawing.Color MenuItemSelectedGradientEnd => _hover;
            public override System.Drawing.Color MenuItemPressedGradientBegin => _accent;
            public override System.Drawing.Color MenuItemPressedGradientEnd => _accent;
            public override System.Drawing.Color ToolStripDropDownBackground => _bg;
            public override System.Drawing.Color ImageMarginGradientBegin => _bg;
            public override System.Drawing.Color ImageMarginGradientEnd => _bg;
            public override System.Drawing.Color ImageMarginGradientMiddle => _bg;
            public override System.Drawing.Color SeparatorDark => _border;
            public override System.Drawing.Color SeparatorLight => _border;
        }

        private void InitializeTrayIcon()
        {
            if (_trayIcon != null) return;

            _trayIcon = new System.Windows.Forms.NotifyIcon
            {
                Icon = new System.Drawing.Icon(
                    System.Windows.Application.GetResourceStream(
                        new Uri("pack://application:,,,/Resources/app.ico"))!.Stream),
                Text = "AudioAuditor",
                Visible = false
            };

            _trayIcon.DoubleClick += (_, _) => RestoreFromTray();

            var menu = new System.Windows.Forms.ContextMenuStrip
            {
                Renderer = new System.Windows.Forms.ToolStripProfessionalRenderer(new DarkColorTable()),
                BackColor = System.Drawing.Color.FromArgb(30, 30, 46),
                ForeColor = System.Drawing.Color.FromArgb(224, 224, 224),
                ShowImageMargin = false
            };
            menu.Items.Add("Show AudioAuditor", null, (_, _) => RestoreFromTray());
            menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
            menu.Items.Add("Exit", null, (_, _) =>
            {
                _forceClose = true;
                _trayIcon.Visible = false;
                _trayIcon.Dispose();
                _trayIcon = null;
                Close();
            });
            _trayIcon.ContextMenuStrip = menu;
        }

        private void RestoreFromTray()
        {
            Show();
            WindowState = WindowState.Normal;
            Activate();
            if (_trayIcon != null) _trayIcon.Visible = false;

            // Re-apply theme and color-match after tray restore
            ApplyThemeTitleBar();
            ResumeAnimations();
            if (_npColorMatchEnabled && _player.CurrentFile != null)
                NpApplyColorMatchMode();
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            if (ThemeManager.CloseToTray && !_forceClose)
            {
                try
                {
                    e.Cancel = true;
                    InitializeTrayIcon();
                    _trayIcon!.Visible = true;
                    PauseAnimations();
                    Hide();
                    return;
                }
                catch (Exception ex)
                {
                    // If tray icon initialization fails, show error and fall through to normal close.
                    MessageBox.Show(
                        $"Could not minimize to system tray:\n{ex.Message}\n\nThe app will close normally.",
                        "System Tray Error",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                    e.Cancel = false;
                }
            }

            _trayIcon?.Dispose();
            base.OnClosing(e);
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            ApplyThemeTitleBar();

            // Hook WndProc for horizontal scroll (touchpad) support
            var hwnd = new WindowInteropHelper(this).Handle;
            var source = HwndSource.FromHwnd(hwnd);
            source?.AddHook(WndProc);

            // Initialize SMTC for media overlay integration (FluentFlyout, etc.)
            _smtc = new SmtcService();
            _smtc.Initialize(hwnd);
            _smtc.PlayRequested += (_, _) => Dispatcher.InvokeAsync(() => { if (_player.IsPaused) _player.Resume(); });
            _smtc.PauseRequested += (_, _) => Dispatcher.InvokeAsync(() => { if (_player.IsPlaying) _player.Pause(); });
            _smtc.NextRequested += (_, _) => Dispatcher.InvokeAsync(() => NextTrack_Click(this, new RoutedEventArgs()));
            _smtc.PreviousRequested += (_, _) => Dispatcher.InvokeAsync(() => PrevTrack_Click(this, new RoutedEventArgs()));
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
                    _lastHorizontalScrollTime = DateTime.UtcNow;
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
            _donationTimer?.Stop();
            _occlusionCheckTimer?.Stop();

            this.Activated -= OnWindowActivated;
            this.Deactivated -= OnWindowDeactivated;
            this.StateChanged -= OnWindowStateChanged;
            CompositionTarget.Rendering -= WaveformAnimation_Tick;
            CompositionTarget.Rendering -= Visualizer_Tick;
            if (_occlusionCheckTimer != null) _occlusionCheckTimer.Tick -= OcclusionCheckTimer_Tick;
            if (_vizCycleTimer != null) _vizCycleTimer.Tick -= VizCycleTimer_Tick;
            if (_npUpdateTimer != null) _npUpdateTimer.Tick -= NpUpdateTimer_Tick;
            if (_npBgAnimTimer != null) _npBgAnimTimer.Tick -= NpBgAnim_Tick;
            if (_npGlowPulseTimer != null) _npGlowPulseTimer.Tick -= NpGlowPulse_Tick;
            NpLayoutPopup.Closed -= NpLayoutPopup_Closed;

            _player.Dispose();
            _discord.Dispose();
            _lastFm.Dispose();
            _smtc?.Dispose();
            SaveNpColorCacheToDisk();
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
        //  Column Visibility
        // ═══════════════════════════════════════════

        private readonly HashSet<string> _sessionHiddenColumns = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Applies column visibility based on persistent HiddenColumns setting + session overrides.
        /// </summary>
        public void ApplyColumnVisibility()
        {
            var hidden = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // Add persistent hidden columns
            if (!string.IsNullOrEmpty(ThemeManager.HiddenColumns))
            {
                foreach (var h in ThemeManager.HiddenColumns.Split(',', StringSplitOptions.RemoveEmptyEntries))
                    hidden.Add(h.Trim());
            }

            // Add session hidden columns
            foreach (var h in _sessionHiddenColumns)
                hidden.Add(h);

            foreach (var col in FileGrid.Columns)
            {
                string header = col.Header?.ToString() ?? "";

                // Feature-disabled columns should always be hidden
                if (header == "Rip Quality" && !ThemeManager.RipQualityEnabled) { col.Visibility = Visibility.Collapsed; continue; }
                if (header == "DR" && !ThemeManager.DynamicRangeEnabled) { col.Visibility = Visibility.Collapsed; continue; }
                if (header == "True Peak" && !ThemeManager.TruePeakEnabled) { col.Visibility = Visibility.Collapsed; continue; }
                if (header == "LUFS" && !ThemeManager.LufsEnabled) { col.Visibility = Visibility.Collapsed; continue; }
                if (header == "Clipping" && !ThemeManager.ClippingDetectionEnabled) { col.Visibility = Visibility.Collapsed; continue; }
                if (header == "MQA" && !ThemeManager.MqaDetectionEnabled) { col.Visibility = Visibility.Collapsed; continue; }
                if (header == "AI" && !ThemeManager.DefaultAiDetectionEnabled) { col.Visibility = Visibility.Collapsed; continue; }
                if (header == "Silence" && !ThemeManager.SilenceDetectionEnabled) { col.Visibility = Visibility.Collapsed; continue; }
                if (header == "Fake Stereo" && !ThemeManager.FakeStereoDetectionEnabled) { col.Visibility = Visibility.Collapsed; continue; }
                if (header == "BPM" && !ThemeManager.BpmDetectionEnabled) { col.Visibility = Visibility.Collapsed; continue; }

                // Check if user has hidden this column
                bool isHidden = hidden.Contains(header);
                col.Visibility = isHidden ? Visibility.Collapsed : Visibility.Visible;
            }
        }

        private void HideColumnForSession(string header)
        {
            _sessionHiddenColumns.Add(header);
            ApplyColumnVisibility();
        }

        private void ShowAllColumns()
        {
            _sessionHiddenColumns.Clear();
            ThemeManager.HiddenColumns = "";
            ThemeManager.SavePlayOptions();
            ApplyColumnVisibility();
        }

        private void HideColumn_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem mi && mi.Parent is ContextMenu cm &&
                cm.PlacementTarget is DataGridColumnHeader header)
            {
                string headerText = header.Content?.ToString() ?? "";
                if (!string.IsNullOrEmpty(headerText))
                    HideColumnForSession(headerText);
            }
        }

        private void ShowAllColumns_Click(object sender, RoutedEventArgs e)
        {
            ShowAllColumns();
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
                || f.FormatDisplay.Contains(q, StringComparison.OrdinalIgnoreCase)
                || f.Status.ToString().Contains(q, StringComparison.OrdinalIgnoreCase);
        }

        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            _searchText = SearchBox.Text;
            SearchPlaceholder.Visibility = string.IsNullOrEmpty(_searchText)
                ? Visibility.Visible : Visibility.Collapsed;
            _filteredView?.Refresh();
            ScrollToPlayingTrack();
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
            ScrollToPlayingTrack();
        }

        /// <summary>
        /// After a filter or search change, if the currently playing track is visible
        /// in the filtered view, auto-select it and scroll to it.
        /// </summary>
        private void ScrollToPlayingTrack()
        {
            if (_filteredView == null) return;
            if (!_player.IsPlaying && !_player.IsPaused) return;
            if (_player.CurrentFile == null) return;

            var playingItem = _filteredView.Cast<AudioFileInfo>()
                .FirstOrDefault(f => string.Equals(f.FilePath, _player.CurrentFile, StringComparison.OrdinalIgnoreCase));

            if (playingItem != null)
            {
                FileGrid.SelectedItem = playingItem;
                FileGrid.ScrollIntoView(playingItem);
            }
        }

        // ═══════════════════════════════════════════
        //  Toolbar
        // ═══════════════════════════════════════════

        private void AddFiles_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Title = "Select Audio Files",
                Filter = "Audio Files|*.mp3;*.flac;*.wav;*.ogg;*.aac;*.m4a;*.wma;*.aiff;*.aif;*.ape;*.wv;*.opus;*.dsf;*.dff;*.cue|Playlists|*.m3u;*.m3u8;*.pls|Archives|*.zip;*.rar;*.7z;*.tar;*.gz;*.tgz|All Files|*.*",
                Multiselect = true
            };

            if (dialog.ShowDialog() == true)
            {
                var allPaths = new List<string>(dialog.FileNames);
                // Expand playlists into audio file paths
                var expanded = ExpandPlaylists(allPaths);
                var files = ExtractAudioFromArchives(expanded);
                if (files.Count > 0)
                    _ = AnalyzeAndAddFiles(files.ToArray());
            }
        }

        private void AddFolder_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFolderDialog
            {
                Title = "Select folders containing audio files",
                Multiselect = true
            };

            if (dialog.ShowDialog() == true)
            {
                var allFiles = new List<string>();
                foreach (var folder in dialog.FolderNames)
                {
                    allFiles.AddRange(
                        Directory.EnumerateFiles(folder, "*.*", SearchOption.AllDirectories)
                            .Where(f => SupportedExtensions.Contains(IOPath.GetExtension(f))
                                     || ArchiveExtensions.Contains(IOPath.GetExtension(f))
                                     || IOPath.GetExtension(f).Equals(".cue", StringComparison.OrdinalIgnoreCase)));
                }

                if (allFiles.Count == 0)
                {
                    ErrorDialog.Show("No Files Found", "No supported audio files found in the selected folder(s).", this);
                    return;
                }

                var expanded = ExtractAudioFromArchives(allFiles);
                if (expanded.Count > 0)
                    _ = AnalyzeAndAddFiles(expanded.ToArray());
            }
        }

        private void ClearAll_Click(object sender, RoutedEventArgs e)
        {
            // Cancel any in-progress analysis so pending files stop loading
            _analysisPauseEvent.Set(); // unblock paused tasks so they can observe cancellation
            _analysisCts?.Cancel();
            _isAnalyzing = false;
            _activeBatches = 0;
            _analysisTotal = 0;
            _analysisCompleted = 0;
            AnalysisProgressPanel.Visibility = Visibility.Collapsed;
            AnalysisPauseButton.Visibility = Visibility.Collapsed;

            _player.Stop();
            _playerTimer.Stop();
            StopWaveformAnimation();
            StopVisualizer();
            _files.Clear();
            _queue.Clear();
            _playHistory.Clear();
            _playHistoryIndex = -1;
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

        private void AnalysisPause_Click(object sender, RoutedEventArgs e)
        {
            if (_analysisPauseEvent.IsSet)
            {
                // Currently running → pause
                _analysisPauseEvent.Reset();
                AnalysisPauseButton.Content = "▶";
                AnalysisPauseButton.ToolTip = "Resume scanning";
                int c = _analysisCompleted, t = _analysisTotal;
                StatusText.Text = $"Pausing after current file(s)… {c} / {t}";
            }
            else
            {
                // Currently paused → resume
                _analysisPauseEvent.Set();
                AnalysisPauseButton.Content = "⏸";
                AnalysisPauseButton.ToolTip = "Pause scanning";
                int c = _analysisCompleted, t = _analysisTotal;
                StatusText.Text = $"Analyzing {c} / {t} files...";
            }
        }

        // ═══════════════════════════════════════════
        //  File Analysis (multi-threaded)
        // ═══════════════════════════════════════════

        /// <summary>
        /// Extracts audio files from ZIP archives to a temp directory.
        /// <summary>
        /// Expands playlist files (.m3u, .m3u8, .pls) into their audio file entries.
        /// Non-playlist files are passed through unchanged.
        /// </summary>
        private List<string> ExpandPlaylists(IEnumerable<string> paths)
        {
            var result = new List<string>();
            foreach (var path in paths)
            {
                string ext = IOPath.GetExtension(path);
                if (PlaylistExtensions.Contains(ext) && File.Exists(path))
                {
                    try
                    {
                        var playlistDir = IOPath.GetDirectoryName(path) ?? "";
                        var lines = File.ReadAllLines(path);

                        if (ext.Equals(".pls", StringComparison.OrdinalIgnoreCase))
                        {
                            // PLS format: File1=path, File2=path, ...
                            foreach (var line in lines)
                            {
                                if (line.StartsWith("File", StringComparison.OrdinalIgnoreCase) && line.Contains('='))
                                {
                                    var filePath = line[(line.IndexOf('=') + 1)..].Trim();
                                    var resolved = ResolvePath(filePath, playlistDir);
                                    if (resolved != null) result.Add(resolved);
                                }
                            }
                        }
                        else
                        {
                            // M3U/M3U8: each non-comment, non-empty line is a path
                            foreach (var line in lines)
                            {
                                var trimmed = line.Trim();
                                if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith('#'))
                                    continue;
                                var resolved = ResolvePath(trimmed, playlistDir);
                                if (resolved != null) result.Add(resolved);
                            }
                        }
                    }
                    catch { /* skip unreadable playlist */ }
                }
                else
                {
                    result.Add(path);
                }
            }
            return result;

            static string? ResolvePath(string entry, string baseDir)
            {
                // Skip URLs (http://, https://)
                if (entry.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                    entry.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                    return null;

                // Try as absolute path first
                if (IOPath.IsPathRooted(entry) && File.Exists(entry))
                    return entry;

                // Try relative to playlist directory
                var combined = IOPath.Combine(baseDir, entry);
                if (File.Exists(combined))
                    return IOPath.GetFullPath(combined);

                return null;
            }
        }

        /// <summary>
        /// Extracts audio files from archives (.zip, .rar, .7z, etc.).
        /// Returns the list of extracted audio file paths plus any non-archive audio paths unchanged.
        /// </summary>
        private List<string> ExtractAudioFromArchives(IEnumerable<string> paths)
        {
            var result = new List<string>();
            foreach (var path in paths)
            {
                string ext = IOPath.GetExtension(path);
                if (ArchiveExtensions.Contains(ext) && File.Exists(path))
                {
                    try
                    {
                        string tempDir = IOPath.Combine(IOPath.GetTempPath(), "AudioAuditor_" + Guid.NewGuid().ToString("N"));
                        Directory.CreateDirectory(tempDir);

                        if (ext.Equals(".zip", StringComparison.OrdinalIgnoreCase))
                        {
                            ZipFile.ExtractToDirectory(path, tempDir);
                        }
                        else
                        {
                            // Use SharpCompress for RAR, 7z, tar, gz, etc.
                            using var archive = ArchiveFactory.Open(path);
                            string safeBase = Path.GetFullPath(tempDir) + Path.DirectorySeparatorChar;
                            foreach (var entry in archive.Entries.Where(e => !e.IsDirectory && e.Key != null))
                            {
                                // ZIP slip guard: skip entries that would escape tempDir
                                string entryKey = entry.Key!.Replace('/', Path.DirectorySeparatorChar);
                                string fullDest = Path.GetFullPath(Path.Combine(tempDir, entryKey));
                                if (!fullDest.StartsWith(safeBase, StringComparison.OrdinalIgnoreCase))
                                    continue;
                                entry.WriteToDirectory(tempDir, new ExtractionOptions
                                {
                                    ExtractFullPath = true,
                                    Overwrite = true
                                });
                            }
                        }

                        var extracted = Directory.EnumerateFiles(tempDir, "*.*", SearchOption.AllDirectories)
                            .Where(f => SupportedExtensions.Contains(IOPath.GetExtension(f)));
                        result.AddRange(extracted);
                    }
                    catch { /* skip corrupt archives */ }
                }
                else if (SupportedExtensions.Contains(ext))
                {
                    result.Add(path);
                }
                else if (ext.Equals(".cue", StringComparison.OrdinalIgnoreCase) && File.Exists(path))
                {
                    // Cue files are passed through — expanded later in AnalyzeAndAddFiles
                    result.Add(path);
                }
            }
            return result;
        }

        private async Task AnalyzeAndAddFiles(string[] filePaths)
        {

            // Deduplicate against already-loaded files
            var existing = new HashSet<string>(_files.Select(f => f.FilePath), StringComparer.OrdinalIgnoreCase);
            var newPaths = filePaths
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Where(p => !existing.Contains(p))
                .ToArray();

            if (newPaths.Length == 0) return;

            // ── Expand .cue files into virtual tracks ──
            var cueFiles = newPaths.Where(p => IOPath.GetExtension(p).Equals(".cue", StringComparison.OrdinalIgnoreCase)).ToArray();
            var regularFiles = newPaths.Where(p => !IOPath.GetExtension(p).Equals(".cue", StringComparison.OrdinalIgnoreCase)).ToList();
            var cueEntries = new List<(string audioPath, Services.CueSheet sheet)>();

            foreach (var cuePath in cueFiles)
            {
                var sheet = Services.CueSheetParser.Parse(cuePath);
                if (sheet != null && !string.IsNullOrEmpty(sheet.AudioFilePath))
                {
                    if (!existing.Contains(sheet.AudioFilePath) &&
                        !regularFiles.Contains(sheet.AudioFilePath, StringComparer.OrdinalIgnoreCase))
                        regularFiles.Add(sheet.AudioFilePath);
                    cueEntries.Add((sheet.AudioFilePath, sheet));
                }
            }

            newPaths = regularFiles.ToArray();
            if (newPaths.Length == 0 && cueEntries.Count == 0) return;

            // ── SH Labs pre-flight: decide which files will use the API ──
            HashSet<string>? shLabsTargets = null;
            if (ThemeManager.SHLabsAiDetection)
            {
                // Count how many actually need an API call (no cache hit)
                var uncached = newPaths.Where(p => SHLabsDetectionService.GetCachedResult(p) == null).ToList();
                var (dailyRem, monthlyRem) = SHLabsDetectionService.GetQuota();
                int available = Math.Min(dailyRem, monthlyRem);

                if (uncached.Count > available && available > 0)
                {
                    // More files than remaining quota — let the user know
                    var msg = $"You have {available} SH Labs scan{(available == 1 ? "" : "s")} remaining today. " +
                              $"{uncached.Count} file{(uncached.Count == 1 ? "" : "s")} need scanning.\n\n" +
                              $"The first {available} file{(available == 1 ? "" : "s")} will be scanned with SH Labs. " +
                              $"The rest will use your other selected detection methods.\n\nContinue?";
                    var confirmed = await ShowSHLabsLimitOverlayAsync(msg, showCancel: true);
                    if (!confirmed) return;

                    // Take first N uncached files
                    shLabsTargets = new HashSet<string>(uncached.Take(available), StringComparer.OrdinalIgnoreCase);
                    // Also include all cached files (free lookups)
                    foreach (var p in newPaths.Where(p => SHLabsDetectionService.GetCachedResult(p) != null))
                        shLabsTargets.Add(p);
                }
                else if (available == 0)
                {
                    // No quota left — inform and continue without SH Labs
                    await ShowSHLabsLimitOverlayAsync(
                        "You've reached your SH Labs scan limit. Files will be analyzed using your other selected detection methods.",
                        showCancel: false);
                    shLabsTargets = null; // disable SH Labs for this batch
                }
                else
                {
                    // Enough quota for all files
                    shLabsTargets = new HashSet<string>(newPaths, StringComparer.OrdinalIgnoreCase);
                }
            }

            bool isFirstBatch = !_isAnalyzing;
            if (isFirstBatch)
            {
                _analysisCts?.Cancel();
                _analysisCts = new CancellationTokenSource();
                _analysisCompleted = 0;
                _analysisTotal = 0;
                _analysisStartTime = DateTime.UtcNow;
                _analysisSemaphore = new SemaphoreSlim(ThemeManager.MaxConcurrency);
                _shLabsSemaphore = new SemaphoreSlim(3);
                _isAnalyzing = true;
                AnalysisProgressPanel.Visibility = Visibility.Visible;
                AnalysisPauseButton.Visibility = Visibility.Visible;
                AnalysisPauseButton.Content = "⏸";
                _analysisPauseEvent.Set(); // ensure not paused from previous run
                AudioAnalyzer.PauseEvent = _analysisPauseEvent;
                AnalysisProgress.Value = 0;
                AnalysisEtaText.Text = "";
            }
            var ct = _analysisCts!.Token;

            Interlocked.Add(ref _analysisTotal, newPaths.Length);
            int currentTotal = _analysisTotal;
            AnalysisProgress.Maximum = currentTotal;
            StatusText.Text = $"Analyzing {_analysisCompleted} / {currentTotal} files...";

            Interlocked.Increment(ref _activeBatches);
            var semaphore = _analysisSemaphore!;
            var shLabsSemaphore = _shLabsSemaphore!;

            try
            {
                // Process in chunks to avoid creating 100k+ Task objects simultaneously.
                // The semaphore still caps concurrent execution; chunking caps task allocation.
                const int ChunkSize = 500;
                for (int _chunkStart = 0; _chunkStart < newPaths.Length; _chunkStart += ChunkSize)
                {
                    if (ct.IsCancellationRequested) break;
                    var chunk = newPaths.Skip(_chunkStart).Take(ChunkSize).ToArray();
                    var chunkTasks = chunk.Select(async path =>
                    {
                    AudioFileInfo? info = null;
                    bool acquired = false;

                    // ── Check scan cache first ──
                    if (ThemeManager.ScanCacheEnabled)
                    {
                        try
                        {
                            var fi = new System.IO.FileInfo(path);
                            if (fi.Exists && ScanCacheService.TryGet(path, fi.Length, fi.LastWriteTimeUtc, out var cached) && cached != null)
                            {
                                var count = Interlocked.Increment(ref _analysisCompleted);
                                await Dispatcher.InvokeAsync(() =>
                                {
                                    _files.Add(cached);
                                    int t = _analysisTotal;
                                    AnalysisProgress.Maximum = t;
                                    StatusText.Text = $"Analyzed {count} / {t} files...";
                                    AnalysisProgress.Value = count;
                                    UpdateAnalysisEta(count, t);
                                });
                                return;
                            }
                        }
                        catch { /* cache miss — fall through to normal analysis */ }
                    }

                    try
                    {
                        // Wait if analysis is paused
                        _analysisPauseEvent.Wait(ct);
                        await semaphore.WaitAsync(ct);
                        acquired = true;
                        ct.ThrowIfCancellationRequested();
                        // Wait if memory usage exceeds configured limit
                        await ThemeManager.WaitForMemoryAsync();
                        ct.ThrowIfCancellationRequested();

                        // Per-file timeout: a hung TagLib or NAudio call cannot stall the scan forever
                        var analysisTask = Task.Run(() => AudioAnalyzer.AnalyzeFile(path, ct), ct);
                        var timeoutTask = Task.Delay(TimeSpan.FromSeconds(120));
                        var winner = await Task.WhenAny(analysisTask, timeoutTask);
                        if (winner == timeoutTask)
                        {
                            info = new AudioFileInfo
                            {
                                FilePath = path,
                                FileName = IOPath.GetFileName(path),
                                FolderPath = IOPath.GetDirectoryName(path) ?? "",
                                Extension = IOPath.GetExtension(path).ToLowerInvariant(),
                                Status = AudioStatus.Corrupt,
                                ErrorMessage = "Analysis timed out (file may be corrupt)"
                            };
                        }
                        else
                        {
                            info = await analysisTask;
                        }
                        ct.ThrowIfCancellationRequested();
                    }
                    catch (OperationCanceledException) { return; }
                    catch
                    {
                        var errCount = Interlocked.Increment(ref _analysisCompleted);
                        if (!ct.IsCancellationRequested)
                        {
                            await Dispatcher.InvokeAsync(() =>
                            {
                                _files.Add(new AudioFileInfo
                                {
                                    FilePath = path,
                                    FileName = IOPath.GetFileName(path),
                                    FolderPath = IOPath.GetDirectoryName(path) ?? "",
                                    Extension = IOPath.GetExtension(path).ToLowerInvariant(),
                                    Status = AudioStatus.Corrupt,
                                    ErrorMessage = "Failed to open or analyze"
                                });
                                int t = _analysisTotal;
                                AnalysisProgress.Maximum = t;
                                AnalysisProgress.Value = errCount;
                                StatusText.Text = $"Analyzed {errCount} / {t} files...";
                                UpdateAnalysisEta(errCount, t);
                            });
                        }
                        return;
                    }
                    finally
                    {
                        if (acquired) semaphore.Release();
                    }

                    // ── SH Labs detection (runs outside analysis semaphore to avoid blocking local analysis) ──
                    if (info != null && shLabsTargets != null && shLabsTargets.Contains(path))
                    {
                        try
                        {
                            await shLabsSemaphore.WaitAsync(ct);
                            try
                            {
                                var shResult = await SHLabsDetectionService.AnalyzeAsync(path, ct);
                                if (shResult != null)
                                {
                                    info.SHLabsScanned = true;
                                    info.SHLabsPrediction = shResult.Prediction;
                                    info.SHLabsProbability = shResult.Probability;
                                    info.SHLabsConfidence = shResult.Confidence;
                                    info.SHLabsAiType = shResult.MostLikelyAiType;
                                }
                            }
                            finally { shLabsSemaphore.Release(); }
                        }
                        catch (OperationCanceledException) { /* SH Labs cancelled — file still added below */ }
                        catch { /* SH Labs failure is non-fatal — other detectors still ran */ }
                    }

                    if (info != null && !ct.IsCancellationRequested)
                    {
                        // Cache the result for future use
                        if (ThemeManager.ScanCacheEnabled)
                        {
                            try { ScanCacheService.Set(info); } catch { }
                        }

                        await Dispatcher.InvokeAsync(() =>
                        {
                            _files.Add(info);
                            var count = Interlocked.Increment(ref _analysisCompleted);
                            int t = _analysisTotal;
                            AnalysisProgress.Maximum = t;
                            StatusText.Text = $"Analyzed {count} / {t} files...";
                            AnalysisProgress.Value = count;
                            UpdateAnalysisEta(count, t);
                        });
                    }
                    });

                    try { await Task.WhenAll(chunkTasks); } catch (OperationCanceledException) { break; }
                } // end chunk loop

                // Save scan cache to disk after batch completes
                if (ThemeManager.ScanCacheEnabled)
                {
                    try { await Task.Run(() => ScanCacheService.SaveToDisk()); } catch { }
                }

                // ── Create virtual tracks from cue sheets ──
                foreach (var (audioPath, sheet) in cueEntries)
                {
                    // Find the analyzed parent file
                    var parent = _files.FirstOrDefault(f => f.FilePath.Equals(audioPath, StringComparison.OrdinalIgnoreCase));
                    if (parent == null) continue;

                    foreach (var track in sheet.Tracks)
                    {
                        var endTime = track.EndTime > TimeSpan.Zero ? track.EndTime : TimeSpan.FromSeconds(parent.DurationSeconds);
                        var duration = endTime - track.StartTime;
                        if (duration.TotalSeconds <= 0) continue;

                        string trackId = $"{audioPath}#CUE{track.TrackNumber}";
                        if (existing.Contains(trackId)) continue;

                        var virtual_ = new AudioFileInfo
                        {
                            FilePath = trackId,
                            FileName = $"[{track.TrackNumber:D2}] {(string.IsNullOrEmpty(track.Title) ? IOPath.GetFileNameWithoutExtension(audioPath) : track.Title)}",
                            FolderPath = parent.FolderPath,
                            Title = track.Title,
                            Artist = !string.IsNullOrEmpty(track.Performer) ? track.Performer : parent.Artist,
                            Extension = parent.Extension,
                            SampleRate = parent.SampleRate,
                            BitsPerSample = parent.BitsPerSample,
                            Channels = parent.Channels,
                            ReportedBitrate = parent.ReportedBitrate,
                            ActualBitrate = parent.ActualBitrate,
                            EffectiveFrequency = parent.EffectiveFrequency,
                            Duration = duration.TotalHours >= 1
                                ? $"{(int)duration.TotalHours}:{duration.Minutes:D2}:{duration.Seconds:D2}"
                                : $"{duration.Minutes}:{duration.Seconds:D2}",
                            DurationSeconds = duration.TotalSeconds,
                            FileSize = parent.FileSize,
                            FileSizeBytes = parent.FileSizeBytes,
                            DateModified = parent.DateModified,
                            DateCreated = parent.DateCreated,
                            Status = parent.Status,
                            IsCueVirtualTrack = true,
                            CueSheetPath = sheet.AudioFilePath,
                            CueTrackNumber = track.TrackNumber,
                            CueStartTime = track.StartTime,
                            CueEndTime = endTime,
                        };
                        _files.Add(virtual_);
                    }
                }
            }
            finally
            {
                if (Interlocked.Decrement(ref _activeBatches) == 0)
                {
                    _isAnalyzing = false;
                    AudioAnalyzer.PauseEvent = null;
                    AnalysisProgressPanel.Visibility = Visibility.Collapsed;
                    AnalysisPauseButton.Visibility = Visibility.Collapsed;
                    AnalysisEtaText.Text = "";

                    // Apply saved favorites to all loaded files, then sort favorites to top
                    FavoritesService.Apply(_files);
                    RefreshFavoriteSort();

                    UpdateStatusSummary();
                    ScheduleDonationPopup();
                }
            }
        }

        private void UpdateAnalysisEta(int completed, int total)
        {
            if (completed < 1 || completed >= total)
            {
                AnalysisEtaText.Text = "";
                return;
            }

            var elapsed = DateTime.UtcNow - _analysisStartTime;
            double avgPerFile = elapsed.TotalSeconds / completed;
            int remaining = total - completed;
            double etaSeconds = avgPerFile * remaining;

            if (etaSeconds < 1)
                AnalysisEtaText.Text = "< 1s";
            else if (etaSeconds < 60)
                AnalysisEtaText.Text = $"~{(int)etaSeconds}s left";
            else
            {
                int mins = (int)(etaSeconds / 60);
                int secs = (int)(etaSeconds % 60);
                AnalysisEtaText.Text = secs > 0 ? $"~{mins}m {secs}s left" : $"~{mins}m left";
            }
        }

        // ═══════════════════════════════════════════
        //  Donation Overlay
        // ═══════════════════════════════════════════

        private DispatcherTimer? _donationTimer;
        private bool _donationScheduled;

        private void ScheduleDonationPopup()
        {
            if (ThemeManager.DonationDismissed || _donationScheduled) return;
            _donationScheduled = true;

            _donationTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(120) };
            _donationTimer.Tick += (s, e) =>
            {
                _donationTimer!.Stop();
                _donationTimer = null;
                // Don't interrupt an in-progress analysis
                if (_isAnalyzing)
                {
                    _donationScheduled = false; // allow re-schedule after next analysis
                    return;
                }
                if (!ThemeManager.DonationDismissed)
                    ShowDonationOverlay();
            };
            _donationTimer.Start();
        }

        private void ShowDonationOverlay()
        {
            DonationOverlay.Visibility = Visibility.Visible;
            MainContent.Effect = new System.Windows.Media.Effects.BlurEffect { Radius = 6 };
        }

        private void HideDonationOverlay()
        {
            DonationOverlay.Visibility = Visibility.Collapsed;
            MainContent.Effect = null;
            ThemeManager.DonationDismissed = true;
            ThemeManager.SetRegistryFlag("DonationDismissed", true);
            ThemeManager.SavePlayOptions();
        }

        private void DonationDonate_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Process.Start(new ProcessStartInfo("https://ko-fi.com/angelsoftware") { UseShellExecute = true });
            }
            catch { }
            HideDonationOverlay();
        }

        private void DonationClose_Click(object sender, RoutedEventArgs e)
        {
            HideDonationOverlay();
        }

        private void DonationBackdrop_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            // Clicking outside the popup also dismisses it
            HideDonationOverlay();
        }

        // ═══════════════════════════════════════════
        //  Update Available Overlay
        // ═══════════════════════════════════════════

        private void UpdateBackdrop_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            UpdateOverlay.Visibility = Visibility.Collapsed;
        }

        private void UpdateDownload_Click(object sender, RoutedEventArgs e)
        {
            UpdateOverlay.Visibility = Visibility.Collapsed;
            string url = UpdateChecker.ReleaseUrl ?? "https://github.com/Angel2mp3/AudioAuditor/releases/latest";
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true });
        }

        private void UpdateDismiss_Click(object sender, RoutedEventArgs e)
        {
            UpdateOverlay.Visibility = Visibility.Collapsed;
        }

        private async void UpdateDownloadInstall_Click(object sender, RoutedEventArgs e)
        {
            if (UpdateChecker.LatestDownloadUrl == null) return;

            BtnUpdateDownloadInstall.IsEnabled = false;
            BtnUpdateOpenBrowser.IsEnabled = false;
            UpdateProgressPanel.Visibility = Visibility.Visible;
            UpdateProgressText.Visibility = Visibility.Visible;
            UpdateProgressText.Text = "Downloading update…";

            string tempExe = Path.Combine(Path.GetTempPath(), "AudioAuditor_Update.exe");
            var progress = new Progress<double>(p =>
            {
                Dispatcher.Invoke(() =>
                {
                    UpdateProgressBar.Width = UpdateProgressPanel.ActualWidth * p;
                    UpdateProgressText.Text = $"Downloading… {p * 100:0}%";
                });
            });

            bool downloaded = await UpdateChecker.DownloadAssetAsync(tempExe, progress);
            if (!downloaded)
            {
                UpdateProgressText.Text = "Download failed. Try opening in browser.";
                BtnUpdateDownloadInstall.IsEnabled = true;
                BtnUpdateOpenBrowser.IsEnabled = true;
                return;
            }

            UpdateProgressText.Text = "Verifying download…";
            bool verified = await UpdateChecker.VerifySha256Async(tempExe);
            if (!verified)
            {
                // SHA256 verification failed — warn but still allow install
                var result = MessageBox.Show(
                    "The downloaded file could not be verified against the published SHA256 hash.\n\n" +
                    "This may happen if the release was just published. You can still install, but it's safer to download from GitHub directly.\n\n" +
                    "Install anyway?",
                    "AudioAuditor — Verification Warning",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);
                if (result != MessageBoxResult.Yes)
                {
                    UpdateProgressPanel.Visibility = Visibility.Collapsed;
                    UpdateProgressText.Visibility = Visibility.Collapsed;
                    BtnUpdateDownloadInstall.IsEnabled = true;
                    BtnUpdateOpenBrowser.IsEnabled = true;
                    return;
                }
            }

            UpdateProgressText.Text = "Preparing to restart…";

            string currentExe = Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule?.FileName ?? "";
            if (string.IsNullOrEmpty(currentExe))
            {
                MessageBox.Show("Could not determine the current executable path. Please update manually.",
                    "AudioAuditor — Update Error", MessageBoxButton.OK, MessageBoxImage.Error);
                UpdateOverlay.Visibility = Visibility.Collapsed;
                return;
            }

            // Write a tiny batch script that waits for this process to exit,
            // copies the new exe over the old one, then restarts.
            string updaterBat = Path.Combine(Path.GetTempPath(), "AudioAuditor_Updater.bat");
            string quotedCurrent = "\"" + currentExe.Replace("\"", "\\\"") + "\"";
            string quotedTemp = "\"" + tempExe.Replace("\"", "\\\"") + "\"";
            string batch = $@"@echo off
chcp 65001 >nul
title AudioAuditor Updater
echo Waiting for AudioAuditor to close...
:waitloop
tasklist | findstr /I /C:""AudioAuditor"" >nul
if %errorlevel% == 0 (
    timeout /t 1 /nobreak >nul
    goto waitloop
)
echo Installing update...
copy /Y {quotedTemp} {quotedCurrent}
if %errorlevel% neq 0 (
    echo Update failed. Please download manually from GitHub.
    pause
    exit /b 1
)
echo Starting AudioAuditor...
start "" {quotedCurrent}
exit
";
            File.WriteAllText(updaterBat, batch);

            // Launch the updater and exit
            Process.Start(new ProcessStartInfo(updaterBat)
            {
                UseShellExecute = true,
                CreateNoWindow = false,
                WindowStyle = ProcessWindowStyle.Hidden
            });

            Application.Current.Shutdown();
        }

        // ═══════════════════════════════════════════
        //  Feature Configuration Overlay
        // ═══════════════════════════════════════════

        private void ShowFeatureConfigOverlay()
        {
            // Load saved toggle states into checkboxes
            FcChkSilence.IsChecked = ThemeManager.SilenceDetectionEnabled;
            FcChkFakeStereo.IsChecked = ThemeManager.FakeStereoDetectionEnabled;
            FcChkDR.IsChecked = ThemeManager.DynamicRangeEnabled;
            FcChkTruePeak.IsChecked = ThemeManager.TruePeakEnabled;
            FcChkLufs.IsChecked = ThemeManager.LufsEnabled;
            FcChkClipping.IsChecked = ThemeManager.ClippingDetectionEnabled;
            FcChkMqa.IsChecked = ThemeManager.MqaDetectionEnabled;
            FcChkBpm.IsChecked = ThemeManager.BpmDetectionEnabled;
            FcChkRipQuality.IsChecked = ThemeManager.RipQualityEnabled;
            FcChkDefaultAi.IsChecked = ThemeManager.DefaultAiDetectionEnabled;
            FcChkExperimentalAi.IsChecked = ThemeManager.ExperimentalAiDetection;
            FcChkSHLabs.IsChecked = ThemeManager.SHLabsAiDetection;

            FeatureConfigOverlay.Visibility = Visibility.Visible;
            MainContent.Effect = new System.Windows.Media.Effects.BlurEffect { Radius = 6 };
        }

        private void HideFeatureConfigOverlay()
        {
            FeatureConfigOverlay.Visibility = Visibility.Collapsed;
            MainContent.Effect = null;
        }

        private void FeatureConfigSave_Click(object sender, RoutedEventArgs e)
        {
            // Persist core feature toggles
            ThemeManager.SilenceDetectionEnabled = FcChkSilence.IsChecked == true;
            AudioAnalyzer.EnableSilenceDetection = ThemeManager.SilenceDetectionEnabled;

            ThemeManager.FakeStereoDetectionEnabled = FcChkFakeStereo.IsChecked == true;
            AudioAnalyzer.EnableFakeStereoDetection = ThemeManager.FakeStereoDetectionEnabled;

            ThemeManager.DynamicRangeEnabled = FcChkDR.IsChecked == true;
            AudioAnalyzer.EnableDynamicRange = ThemeManager.DynamicRangeEnabled;

            ThemeManager.TruePeakEnabled = FcChkTruePeak.IsChecked == true;
            AudioAnalyzer.EnableTruePeak = ThemeManager.TruePeakEnabled;

            ThemeManager.LufsEnabled = FcChkLufs.IsChecked == true;
            AudioAnalyzer.EnableLufs = ThemeManager.LufsEnabled;

            ThemeManager.ClippingDetectionEnabled = FcChkClipping.IsChecked == true;
            AudioAnalyzer.EnableClippingDetection = ThemeManager.ClippingDetectionEnabled;

            ThemeManager.MqaDetectionEnabled = FcChkMqa.IsChecked == true;
            AudioAnalyzer.EnableMqaDetection = ThemeManager.MqaDetectionEnabled;

            ThemeManager.BpmDetectionEnabled = FcChkBpm.IsChecked == true;
            AudioAnalyzer.EnableBpmDetection = ThemeManager.BpmDetectionEnabled;

            // AI detection choices
            ThemeManager.DefaultAiDetectionEnabled = FcChkDefaultAi.IsChecked == true;
            AudioAnalyzer.EnableDefaultAiDetection = ThemeManager.DefaultAiDetectionEnabled;

            ThemeManager.ExperimentalAiDetection = FcChkExperimentalAi.IsChecked == true;
            AudioAnalyzer.EnableExperimentalAi = ThemeManager.ExperimentalAiDetection;

            // Persist Rip Quality opt-in
            ThemeManager.RipQualityEnabled = FcChkRipQuality.IsChecked == true;
            AudioAnalyzer.EnableRipQuality = ThemeManager.RipQualityEnabled;

            bool wantsSHLabs = FcChkSHLabs.IsChecked == true;

            // Mark this version as configured
            string currentVersion = System.Reflection.Assembly.GetExecutingAssembly()
                .GetName().Version is { } v ? $"{v.Major}.{v.Minor}.{v.Build}" : "0.0.0";
            ThemeManager.FeatureConfigVersion = currentVersion;
            ThemeManager.SavePlayOptions();

            // Show/hide columns based on user choices
            ApplyColumnVisibility();

            HideFeatureConfigOverlay();

            // If SH Labs was just enabled and privacy not yet accepted, show privacy notice
            if (wantsSHLabs && !ThemeManager.SHLabsPrivacyAccepted)
            {
                ShowSHLabsPrivacyOverlay();
                return;
            }

            ThemeManager.SHLabsAiDetection = wantsSHLabs;
            ThemeManager.SavePlayOptions();
        }

        private void FeatureConfigBackdrop_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            // Clicking outside does nothing — user must press Save
        }

        private void FcChkSHLabs_Checked(object sender, RoutedEventArgs e)
        {
            // When user checks SH Labs on the feature config screen, show privacy notice
            // if they haven't already accepted it. If they decline, uncheck the box.
            if (!ThemeManager.SHLabsPrivacyAccepted)
            {
                _shLabsPrivacyFromFeatureConfig = true;
                ShowSHLabsPrivacyOverlay();
            }
        }

        private bool _shLabsPrivacyFromFeatureConfig;

        // ═══════════════════════════════════════════
        //  SH Labs Privacy Notice Overlay
        // ═══════════════════════════════════════════

        private void ShowSHLabsPrivacyOverlay()
        {
            SHLabsPrivacyOverlay.Visibility = Visibility.Visible;
            MainContent.Effect = new System.Windows.Media.Effects.BlurEffect { Radius = 6 };
        }

        private void HideSHLabsPrivacyOverlay()
        {
            SHLabsPrivacyOverlay.Visibility = Visibility.Collapsed;
            // Only clear blur if feature config overlay is NOT still showing
            if (FeatureConfigOverlay.Visibility != Visibility.Visible)
                MainContent.Effect = null;
        }

        private void SHLabsPrivacyAccept_Click(object sender, RoutedEventArgs e)
        {
            ThemeManager.SHLabsPrivacyAccepted = true;
            ThemeManager.SHLabsAiDetection = true;
            ThemeManager.SavePlayOptions();
            HideSHLabsPrivacyOverlay();
            _shLabsPrivacyFromFeatureConfig = false;
        }

        private void SHLabsPrivacyDecline_Click(object sender, RoutedEventArgs e)
        {
            // User declined — SH Labs stays off
            ThemeManager.SHLabsAiDetection = false;
            ThemeManager.SavePlayOptions();
            HideSHLabsPrivacyOverlay();
            // If triggered from the feature config checkbox, uncheck it
            if (_shLabsPrivacyFromFeatureConfig)
            {
                FcChkSHLabs.IsChecked = false;
                _shLabsPrivacyFromFeatureConfig = false;
            }
        }

        private void SHLabsPrivacyBackdrop_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            // Clicking outside = decline
            SHLabsPrivacyDecline_Click(sender, e);
        }

        /// <summary>
        /// Called from SettingsWindow when user enables SH Labs and needs privacy confirmation.
        /// </summary>
        public void RequestSHLabsPrivacyFromSettings()
        {
            ShowSHLabsPrivacyOverlay();
        }

        // ═══════════════════════════════════════════
        //  SH Labs Scan Limit Overlay
        // ═══════════════════════════════════════════

        private TaskCompletionSource<bool>? _shLabsLimitTcs;

        private Task<bool> ShowSHLabsLimitOverlayAsync(string message, bool showCancel)
        {
            _shLabsLimitTcs = new TaskCompletionSource<bool>();
            SHLabsLimitMessage.Text = message;
            SHLabsLimitCancelBtn.Visibility = showCancel ? Visibility.Visible : Visibility.Collapsed;
            SHLabsLimitOverlay.Visibility = Visibility.Visible;
            return _shLabsLimitTcs.Task;
        }

        private void HideSHLabsLimitOverlay(bool result)
        {
            SHLabsLimitOverlay.Visibility = Visibility.Collapsed;
            _shLabsLimitTcs?.TrySetResult(result);
        }

        private void SHLabsLimitOk_Click(object sender, RoutedEventArgs e)
            => HideSHLabsLimitOverlay(true);

        private void SHLabsLimitCancel_Click(object sender, RoutedEventArgs e)
            => HideSHLabsLimitOverlay(false);

        private void SHLabsLimitBackdrop_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
            => HideSHLabsLimitOverlay(false);

        // ═══════════════════════════════════════════
        //  Footer Support Link
        // ═══════════════════════════════════════════

        private void InitializeFooterSupport()
        {
            if (ThemeManager.FooterSupportDismissed)
                FooterSupportText.Visibility = Visibility.Collapsed;
        }

        private void FooterSupport_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            try
            {
                Process.Start(new ProcessStartInfo("https://ko-fi.com/angelsoftware") { UseShellExecute = true });
            }
            catch { }
        }

        private void FooterSupport_Dismiss(object sender, RoutedEventArgs e)
        {
            FooterSupportText.Visibility = Visibility.Collapsed;
            ThemeManager.FooterSupportDismissed = true;
            ThemeManager.SetRegistryFlag("FooterSupportDismissed", true);
            ThemeManager.SavePlayOptions();
        }

        private void UpdateStatusSummary()
        {
            int valid = _files.Count(f => f.Status == AudioStatus.Valid);
            int fake = _files.Count(f => f.Status == AudioStatus.Fake);
            int unknown = _files.Count(f => f.Status == AudioStatus.Unknown);
            int corrupt = _files.Count(f => f.Status == AudioStatus.Corrupt);
            int optimized = _files.Count(f => f.Status == AudioStatus.Optimized);
            int mqa = _files.Count(f => f.IsMqa);
            int ai = _files.Count(f => f.IsAiGenerated);
            string optimizedPart = optimized > 0 ? $", {optimized} optimized" : "";
            string mqaPart = mqa > 0 ? $", {mqa} MQA" : "";
            string aiPart = ai > 0 ? $", {ai} AI" : "";
            StatusText.Text = $"{_files.Count} files — {valid} real, {fake} fake, {unknown} unknown, {corrupt} corrupted{optimizedPart}{mqaPart}{aiPart}";
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

            // Debounce: if a double-click happens within 200ms, skip spectrogram (user wants to play)
            var fileAtSelection = selectedFile;
            await Task.Delay(200);
            if (FileGrid.SelectedItem != fileAtSelection) return;
            if (DateTime.Now - _lastDoubleClickTime < TimeSpan.FromMilliseconds(300)) return;

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

                // Discord: show paused — use the actual playing file, not the grid selection
                var discordFile = _player.CurrentFile != null
                    ? _files.FirstOrDefault(f => string.Equals(f.FilePath, _player.CurrentFile, StringComparison.OrdinalIgnoreCase))
                    : null;
                _discord.UpdatePresence(discordFile?.Artist, discordFile?.Title, discordFile?.FileName,
                    _player.TotalDuration, _player.CurrentPosition, true);

                _smtc?.UpdatePlaybackState(false, true);
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

                // Discord: show playing again — use the actual playing file, not the grid selection
                var discordFile = _player.CurrentFile != null
                    ? _files.FirstOrDefault(f => string.Equals(f.FilePath, _player.CurrentFile, StringComparison.OrdinalIgnoreCase))
                    : null;
                _discord.UpdatePresence(discordFile?.Artist, discordFile?.Title, discordFile?.FileName,
                    _player.TotalDuration, _player.CurrentPosition, false);

                _smtc?.UpdatePlaybackState(true, false);
            }
            else if (FileGrid.SelectedItem is AudioFileInfo file2)
            {
                PlayFile(file2, isManualSkip: true);
            }
        }

        private void Stop_Click(object sender, RoutedEventArgs e)
        {
            _crossfadeEarlyTriggered = false;
            _player.Stop();
            _playerTimer.Stop();
            StopWaveformAnimation();
            WaveformCanvas.Children.Clear();
            UpdatePlayerUI();
            _discord.ClearPresence();
            _smtc?.UpdatePlaybackState(false, false);
            _lastFm.TrackStopped();
        }

        private void Rewind_Click(object sender, RoutedEventArgs e)
        {
            if (_player.IsPlaying || _player.IsPaused)
            {
                _player.SeekRelative(-5);
                _lastSeekTime = DateTime.UtcNow;
                _lastSeekTargetPosition = _player.CurrentPosition;
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
                _lastSeekTargetPosition = _player.CurrentPosition;
                // Immediately update the UI slider to reflect new position
                if (_player.TotalDuration.TotalSeconds > 0)
                    SeekSlider.Value = _player.CurrentPosition.TotalSeconds;
                UpdatePlayerTimeText();
            }
        }

        private void Shuffle_Click(object sender, RoutedEventArgs e)
        {
            _shuffleMode = !_shuffleMode;
            if (_shuffleMode)
            {
                // Reset the deck so a fresh shuffle starts immediately
                _shuffleDeck.Clear();
                _shuffleDeckIndex = 0;
            }
            UpdateShuffleUI();
        }

        private void Loop_Click(object sender, RoutedEventArgs e)
        {
            CycleLoopMode();
        }

        private void CycleLoopMode()
        {
            ThemeManager.LoopMode = ThemeManager.LoopMode switch
            {
                LoopMode.Off => LoopMode.All,
                LoopMode.All => LoopMode.One,
                _ => LoopMode.Off
            };
            ThemeManager.SavePlayOptions();
            UpdateLoopUI();
        }

        private void UpdateLoopUI()
        {
            var mode = ThemeManager.LoopMode;
            var accent = (System.Windows.Media.Brush)FindResource("PlaybarAccentColor");
            var muted = (System.Windows.Media.Brush)FindResource("TextMuted");
            bool active = mode != LoopMode.Off;

            if (LoopIcon != null)
            {
                LoopIcon.Stroke = active ? accent : muted;
                LoopIcon.StrokeThickness = active ? 2.0 : 1.6;
            }
            if (LoopOneIndicator != null)
            {
                LoopOneIndicator.Visibility = mode == LoopMode.One ? Visibility.Visible : Visibility.Collapsed;
                LoopOneIndicator.Foreground = accent;
            }
            if (BtnLoop != null)
            {
                string tip = mode switch { LoopMode.All => "Loop: All", LoopMode.One => "Loop: One", _ => "Loop: Off" };
                BtnLoop.ToolTip = tip;
                if (active && accent is System.Windows.Media.SolidColorBrush scb)
                {
                    var glowColor = scb.Color;
                    glowColor.A = 40;
                    BtnLoop.Background = new System.Windows.Media.SolidColorBrush(glowColor);
                }
                else
                {
                    BtnLoop.Background = System.Windows.Media.Brushes.Transparent;
                }
            }

            // NP panel
            NpUpdateLoopIcon();
        }

        private void NpUpdateLoopIcon()
        {
            if (NpLoopIcon == null) return;
            var mode = ThemeManager.LoopMode;
            var activeColor = NpGetIconBrush(true);
            var inactiveColor = NpGetIconBrush(false);
            bool active = mode != LoopMode.Off;
            NpLoopIcon.Stroke = active ? activeColor : inactiveColor;
            if (NpLoopOneIndicator != null)
            {
                NpLoopOneIndicator.Visibility = mode == LoopMode.One ? Visibility.Visible : Visibility.Collapsed;
                NpLoopOneIndicator.Foreground = activeColor;
            }
            if (NpLoopBtn != null)
            {
                NpLoopBtn.ToolTip = mode switch { LoopMode.All => "Loop: All", LoopMode.One => "Loop: One", _ => "Loop: Off" };
                NpSetToggleBg(NpLoopBtn, active);
            }
        }

        private void UpdateShuffleUI()
        {
            if (ShuffleIcon != null)
            {
                var accent = (System.Windows.Media.Brush)FindResource("PlaybarAccentColor");
                var muted = (System.Windows.Media.Brush)FindResource("TextMuted");
                ShuffleIcon.Stroke = _shuffleMode ? accent : muted;
                ShuffleIcon.StrokeThickness = _shuffleMode ? 2.6 : 2.2;

                // Update the button background to clearly show active state
                if (BtnShuffle != null)
                {
                    if (_shuffleMode && accent is System.Windows.Media.SolidColorBrush scb)
                    {
                        var glowColor = scb.Color;
                        glowColor.A = 40; // ~15% opacity
                        BtnShuffle.Background = new System.Windows.Media.SolidColorBrush(glowColor);
                    }
                    else
                    {
                        BtnShuffle.Background = System.Windows.Media.Brushes.Transparent;
                    }
                }
            }
        }

        /// <summary>
        /// Builds a shuffled "deck" of all playable tracks using Fisher-Yates shuffle.
        /// Every track is guaranteed to play exactly once before the deck resets.
        /// </summary>
        private void RebuildShuffleDeck(List<AudioFileInfo> items, AudioFileInfo? avoid = null)
        {
            _shuffleDeck.Clear();
            _shuffleDeck.AddRange(items.Where(f => f.Status != AudioStatus.Corrupt));

            // Fisher-Yates shuffle
            for (int i = _shuffleDeck.Count - 1; i > 0; i--)
            {
                int j = _shuffleRng.Next(i + 1);
                (_shuffleDeck[i], _shuffleDeck[j]) = (_shuffleDeck[j], _shuffleDeck[i]);
            }

            // Move the track we want to avoid away from position 0
            if (avoid != null && _shuffleDeck.Count > 1)
            {
                int idx = _shuffleDeck.FindIndex(f =>
                    string.Equals(f.FilePath, avoid.FilePath, StringComparison.OrdinalIgnoreCase));
                if (idx == 0)
                {
                    // Swap with a random later position
                    int swapIdx = _shuffleRng.Next(1, _shuffleDeck.Count);
                    (_shuffleDeck[0], _shuffleDeck[swapIdx]) = (_shuffleDeck[swapIdx], _shuffleDeck[0]);
                }
            }

            _shuffleDeckIndex = 0;
        }

        /// <summary>
        /// Picks the next track from the shuffled deck. Rebuilds the deck when exhausted,
        /// ensuring every track plays once before any repeats.
        /// </summary>
        private AudioFileInfo? PickRandomTrack(List<AudioFileInfo> items)
        {
            if (items.Count == 0) return null;

            // Rebuild deck if empty, exhausted, or if the track list changed significantly
            int playableCount = items.Count(f => f.Status != AudioStatus.Corrupt);
            if (_shuffleDeck.Count == 0 || _shuffleDeckIndex >= _shuffleDeck.Count
                || Math.Abs(_shuffleDeck.Count - playableCount) > 0)
            {
                // Find what we're currently playing to avoid it being first in the new deck
                AudioFileInfo? currentTrack = null;
                if (_player.CurrentFile != null)
                    currentTrack = items.FirstOrDefault(f =>
                        string.Equals(f.FilePath, _player.CurrentFile, StringComparison.OrdinalIgnoreCase));

                // Only rebuild if deck is exhausted or track list changed
                if (_shuffleDeck.Count != playableCount || _shuffleDeckIndex >= _shuffleDeck.Count)
                    RebuildShuffleDeck(items, currentTrack);
            }

            if (_shuffleDeck.Count == 0) return null;

            var picked = _shuffleDeck[_shuffleDeckIndex];
            _shuffleDeckIndex++;
            return picked;
        }

        private void PrevTrack_Click(object sender, RoutedEventArgs e)
        {
            _crossfadeEarlyTriggered = false;
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

            // Use playback history to go back to the previously played track
            if (_playHistoryIndex > 0)
            {
                _playHistoryIndex--;
                var prevFile = _playHistory[_playHistoryIndex];
                FileGrid.SelectedItem = prevFile;
                FileGrid.ScrollIntoView(prevFile);
                if (prevFile.Status != AudioStatus.Corrupt)
                {
                    _navigatingHistory = true;
                    PlayFile(prevFile, isManualSkip: true);
                    _navigatingHistory = false;
                }
                return;
            }

            // No history available — fall back to list-based navigation
            var items = _filteredView?.Cast<AudioFileInfo>().ToList();
            if (items == null || items.Count == 0) return;

            int currentIdx = -1;
            if (_player.CurrentFile != null)
                currentIdx = items.FindIndex(f => string.Equals(f.FilePath, _player.CurrentFile, StringComparison.OrdinalIgnoreCase));
            else if (FileGrid.SelectedItem is AudioFileInfo sel)
                currentIdx = items.IndexOf(sel);

            int prevIdx = currentIdx - 1;
            if (prevIdx < 0) prevIdx = items.Count - 1;

            var prevListFile = items[prevIdx];
            FileGrid.SelectedItem = prevListFile;
            FileGrid.ScrollIntoView(prevListFile);
            if (prevListFile.Status != AudioStatus.Corrupt)
                PlayFile(prevListFile, isManualSkip: true);
        }

        private void NextTrack_Click(object sender, RoutedEventArgs e)
        {
            _crossfadeEarlyTriggered = false;
            // Check queue first
            if (_queue.Count > 0)
            {
                var nextFile = _queue[0];
                _queue.RemoveAt(0);
                if (nextFile.Status != AudioStatus.Corrupt)
                {
                    FileGrid.SelectedItem = nextFile;
                    FileGrid.ScrollIntoView(nextFile);
                    PlayFile(nextFile, isManualSkip: true);
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
                        PlayFile(candidate, isManualSkip: true);
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
                PlayFile(nextInList, isManualSkip: true);
        }

        private void VolumeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            _player.Volume = (float)(VolumeSlider.Value / 100.0);
            // Persist volume so it's restored across sessions. Skip the save while muted
            // (mute writes 0 to the slider — we want the user's pre-mute level to survive).
            if (!_isMuted)
            {
                ThemeManager.Volume = VolumeSlider.Value;
                ThemeManager.SavePlayOptions();
            }
            if (VolumeLabel != null)
                VolumeLabel.Text = $"{(int)VolumeSlider.Value}%";
            if (VolumeIconPath != null)
            {
                if (VolumeSlider.Value <= 0)
                    VolumeIconPath.Data = System.Windows.Media.Geometry.Parse("M 2,5 L 5,5 L 9,2 L 9,14 L 5,11 L 2,11 Z M 12,5 L 15,8 M 15,5 L 12,8");
                else if (VolumeSlider.Value < 34)
                    VolumeIconPath.Data = System.Windows.Media.Geometry.Parse("M 2,5 L 5,5 L 9,2 L 9,14 L 5,11 L 2,11 Z M 11,6 Q 12.5,8 11,10");
                else if (VolumeSlider.Value < 67)
                    VolumeIconPath.Data = System.Windows.Media.Geometry.Parse("M 2,5 L 5,5 L 9,2 L 9,14 L 5,11 L 2,11 Z M 11,5 Q 13,8 11,11 M 13,3.5 Q 15.5,8 13,12.5");
                else
                    VolumeIconPath.Data = System.Windows.Media.Geometry.Parse("M 2,5 L 5,5 L 9,2 L 9,14 L 5,11 L 2,11 Z M 11,5 Q 13,8 11,11 M 13,3 Q 16,8 13,13 M 15,1 Q 19,8 15,15");
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
            // During drag, only update visual position — actual seek happens on release
            // This prevents audio stuttering from rapid seek calls
        }

        private void SeekSlider_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            _isSeeking = true;
        }

        private void SeekSlider_MouseUp(object sender, MouseButtonEventArgs e)
        {
            // If thumb was being dragged, DragCompleted handles the seek — skip here
            var thumb = FindVisualChild<System.Windows.Controls.Primitives.Thumb>(SeekSlider);
            if (thumb != null && thumb.IsDragging)
            {
                return;
            }

            if (_player.TotalDuration.TotalSeconds > 0 && SeekSlider.Maximum > 0)
            {
                double pos = SeekSlider.Value / SeekSlider.Maximum * _player.TotalDuration.TotalSeconds;
                _player.Seek(pos);
                _lastSeekTime = DateTime.UtcNow;
                _lastSeekTargetPosition = TimeSpan.FromSeconds(pos);

                // Sync NP slider
                NpSeekSlider.ValueChanged -= NpSeekSlider_ValueChanged;
                if (NpSeekSlider.Maximum > 0)
                    NpSeekSlider.Value = pos;
                NpSeekSlider.ValueChanged += NpSeekSlider_ValueChanged;

                // Force immediate lyric re-sync
                _npCurrentLyricIndex = -1;
                NpUpdateLyricHighlight(_lastSeekTargetPosition);
            }
            _isSeeking = false;
        }

        private void SeekSlider_DragCompleted(object sender, DragCompletedEventArgs e)
        {
            if (_player.TotalDuration.TotalSeconds > 0 && SeekSlider.Maximum > 0)
            {
                double pos = SeekSlider.Value / SeekSlider.Maximum * _player.TotalDuration.TotalSeconds;
                _player.Seek(pos);
                _lastSeekTime = DateTime.UtcNow;
                _lastSeekTargetPosition = TimeSpan.FromSeconds(pos);

                // Sync NP slider
                NpSeekSlider.ValueChanged -= NpSeekSlider_ValueChanged;
                if (NpSeekSlider.Maximum > 0)
                    NpSeekSlider.Value = pos;
                NpSeekSlider.ValueChanged += NpSeekSlider_ValueChanged;

                // Force immediate lyric re-sync
                _npCurrentLyricIndex = -1;
                NpUpdateLyricHighlight(_lastSeekTargetPosition);
            }
            _isSeeking = false;
        }

        private void SeekSlider_MouseMove(object sender, MouseEventArgs e)
        {
            UpdateSeekTooltip(SeekSlider, e);
        }

        private void UpdateSeekTooltip(Slider slider, MouseEventArgs e)
        {
            if (_player.TotalDuration.TotalSeconds <= 0 || slider.ActualWidth <= 0) return;
            double mouseX = e.GetPosition(slider).X;
            double ratio = Math.Clamp(mouseX / slider.ActualWidth, 0, 1);
            var hoverTime = TimeSpan.FromSeconds(ratio * _player.TotalDuration.TotalSeconds);
            slider.ToolTip = FormatTime(hoverTime);
        }

        // Tracks consecutive PlayFile failures so a single bad file doesn't stop the queue
        // and a queue full of bad files doesn't cause unbounded recursion.
        private int _consecutiveFailedPlays;
        private const int MaxConsecutiveFailedPlays = 3;

        private void PlayFile(AudioFileInfo file, bool isManualSkip = false)
        {
            _crossfadeEarlyTriggered = false;
            _npColorGeneration++;
            try
            {
                // Track playback history for back-button navigation
                if (!_navigatingHistory)
                {
                    // If we navigated back and then play a new track, trim forward history
                    if (_playHistoryIndex >= 0 && _playHistoryIndex < _playHistory.Count - 1)
                        _playHistory.RemoveRange(_playHistoryIndex + 1, _playHistory.Count - _playHistoryIndex - 1);

                    _playHistory.Add(file);
                    _playHistoryIndex = _playHistory.Count - 1;
                }

                bool normalize = ThemeManager.AudioNormalization;
                bool crossfade = ThemeManager.Crossfade;

                // Apply crossfade duration setting
                _player.CrossfadeDurationSeconds = ThemeManager.CrossfadeDuration;

                // ALWAYS stop current playback cleanly first to prevent audio bleed
                // The crossfade path handles its own stop internally
                // Respect manual-skip setting: skip crossfade on manual Next/Prev if disabled
                if (crossfade && _player.IsPlaying && (!isManualSkip || ThemeManager.CrossfadeOnManualSkip))
                {
                    _player.CrossfadeCurve = ThemeManager.CrossfadeCurve;
                    _player.SetUserVolume((float)(VolumeSlider.Value / 100.0));
                    _player.PlayWithCrossfade(file.FilePath, normalize);
                }
                else
                {
                    _player.Stop();
                    _playerTimer.Stop();
                    // Small delay to let NAudio release resources
                    System.Threading.Thread.Sleep(30);
                    _player.Play(file.FilePath, normalize);
                    _player.Volume = (float)(VolumeSlider.Value / 100.0);
                }
                SeekSlider.Maximum = _player.TotalDuration.TotalSeconds;
                // Also set NP seek slider maximum for Now Playing panel
                NpSeekSlider.ValueChanged -= NpSeekSlider_ValueChanged;
                NpSeekSlider.Maximum = _player.TotalDuration.TotalSeconds;
                NpSeekSlider.Value = 0;
                NpSeekSlider.ValueChanged += NpSeekSlider_ValueChanged;
                _playerTimer.Start();
                UpdatePlayerUI();
                DrawWaveformBackground();
                if (_visualizerMode) StartVisualizer();

                // Update spectrogram/visualizer title to reflect the now-playing file
                _currentSpectrogramFile = file;
                SpectrogramTitle.Text = BuildSpectrogramTitle(file);

                // Update album cover for the playing track
                UpdateAlbumCoverAsync();

                // Main-window color match
                LoadMainCoverColors(file.FilePath);

                // Discord Rich Presence
                _discord.UpdatePresence(file.Artist, file.Title, file.FileName,
                    _player.TotalDuration, TimeSpan.Zero, false);

                // Last.fm now playing
                _lastFm.TrackStarted(file.Artist, file.Title, _player.TotalDuration.TotalSeconds);

                // SMTC media session (FluentFlyout / Windows media overlay)
                _smtc?.UpdateNowPlayingFromTags(file.FilePath);
                _smtc?.UpdatePlaybackState(true, false);

                // Update Now Playing panel if visible
                UpdateNowPlayingView();

                // Successful playback — reset the failure counter
                _consecutiveFailedPlays = 0;

                // Preload next track's colors in the background for instant switching
                PreloadNextTrackColors();
            }
            catch (Exception ex)
            {
                _consecutiveFailedPlays++;
                string fileName = file?.FileName ?? Path.GetFileName(file?.FilePath ?? "(unknown)");
                string filePath = file?.FilePath ?? "";

                if (_consecutiveFailedPlays < MaxConsecutiveFailedPlays)
                {
                    // Auto-advance past the bad file so a single unsupported track doesn't halt the queue.
                    // Show a non-blocking warning in the status area instead of a modal dialog.
                    System.Diagnostics.Debug.WriteLine($"[PlayFile] '{fileName}' failed ({ex.GetType().Name}: {ex.Message}) — auto-skipping ({_consecutiveFailedPlays}/{MaxConsecutiveFailedPlays})");
                    Dispatcher.BeginInvoke(new Action(() => NextTrack_Click(this, new RoutedEventArgs())));
                }
                else
                {
                    // Too many consecutive failures — surface to the user with full filename and stop trying
                    _consecutiveFailedPlays = 0;
                    ErrorDialog.Show(
                        "Playback Error",
                        $"Cannot play this file:\n{fileName}\n\n{ex.Message}\n\nPath: {filePath}",
                        this);
                }
            }
        }

        private void PlayFile_Click(object sender, RoutedEventArgs e)
        {
            if (FileGrid.SelectedItem is AudioFileInfo file)
                PlayFile(file, isManualSkip: true);
        }

        private DateTime _lastDoubleClickTime = DateTime.MinValue;

        private void FileGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            _lastDoubleClickTime = DateTime.Now;
            if (FileGrid.SelectedItem is AudioFileInfo file)
                PlayFile(file, isManualSkip: true);
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
                return;
            }

            // Suppress small vertical scroll events that arrive during a touchpad horizontal swipe
            // (touchpads often send both horizontal + tiny vertical deltas simultaneously)
            if ((DateTime.UtcNow - _lastHorizontalScrollTime).TotalMilliseconds < 150 &&
                Math.Abs(e.Delta) < 60)
            {
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
                var discordFile = _player.CurrentFile != null
                    ? _files.FirstOrDefault(f => string.Equals(f.FilePath, _player.CurrentFile, StringComparison.OrdinalIgnoreCase))
                    : null;
                _discord.UpdatePresence(discordFile?.Artist, discordFile?.Title, discordFile?.FileName,
                    _player.TotalDuration, _player.CurrentPosition, false);
            }

            // Gapless pre-buffer: prepare next track when <3s remaining
            if (_player.IsGaplessActive && !_player.IsGaplessPrepared
                && _cachedDurationSec > 0 && _cachedDurationSec - _cachedPositionSec < 3.0
                && _cachedDurationSec - _cachedPositionSec > 0.1
                && ThemeManager.AutoPlayNext)
            {
                var nextTrack = GetNextTrackForGapless();
                if (nextTrack != null)
                {
                    _ = System.Threading.Tasks.Task.Run(() =>
                    {
                        try { _player.PrepareGapless(nextTrack.FilePath, ThemeManager.AudioNormalization); }
                        catch { /* gapless prep failed — normal TrackFinished will handle it */ }
                    });
                }
            }

            // Early crossfade: start next track before current one ends so there is real overlap
            // Add 100ms safety buffer to avoid cutting off the end if the timer fires late
            double remaining = _cachedDurationSec - _cachedPositionSec;
            double crossfadeDuration = ThemeManager.CrossfadeDuration;
            if (ThemeManager.Crossfade && ThemeManager.AutoPlayNext
                && _player.IsPlaying && !_player.IsGaplessActive
                && !_crossfadeEarlyTriggered
                && _cachedDurationSec >= crossfadeDuration * 2  // track must be longer than 2× fade
                && remaining > 0.5 && remaining <= crossfadeDuration + 0.1)
            {
                _crossfadeEarlyTriggered = true;
                NextTrack_Click(this, new RoutedEventArgs());
            }
        }

        private void Player_PlaybackStopped(object? sender, EventArgs e)
        {
            Dispatcher.InvokeAsync(() =>
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
                if (!ThemeManager.MainColorMatchEnabled)
                    RestoreMainColorMatch();
            });
        }

        private void Player_TrackFinished(object? sender, EventArgs e)
        {
            Dispatcher.InvokeAsync(() =>
            {
                // Debounce: prevent double-fire from NAudio race conditions
                if ((DateTime.UtcNow - _lastTrackFinishedTime).TotalMilliseconds < 2000) return;
                _lastTrackFinishedTime = DateTime.UtcNow;

                // If gapless handled the transition, don't also do a normal play
                if (_player.IsGaplessActive && _player.IsPlaying) return;

                // If early crossfade already advanced to the next track, suppress double-advance
                if (_crossfadeEarlyTriggered && _player.IsPlaying)
                {
                    _crossfadeEarlyTriggered = false;
                    return;
                }
                _crossfadeEarlyTriggered = false;

                if (!ThemeManager.AutoPlayNext) return;

                // Loop One: replay the current track
                if (ThemeManager.LoopMode == LoopMode.One)
                {
                    string? currentFile = _player.CurrentFile;
                    if (currentFile != null)
                    {
                        var currentTrack = _files.FirstOrDefault(f =>
                            string.Equals(f.FilePath, currentFile, StringComparison.OrdinalIgnoreCase));
                        if (currentTrack != null && currentTrack.Status != AudioStatus.Corrupt)
                        {
                            PlayFile(currentTrack);
                            return;
                        }
                    }
                }

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
                if (nextIdx >= items.Count)
                {
                    // Loop All: wrap to beginning
                    if (ThemeManager.LoopMode == LoopMode.All)
                        nextIdx = 0;
                    else
                        return; // end of list
                }

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
            {
                PlayIcon.Visibility = Visibility.Collapsed;
                PauseIcon.Visibility = Visibility.Visible;
            }
            else
            {
                PlayIcon.Visibility = Visibility.Visible;
                PauseIcon.Visibility = Visibility.Collapsed;
            }

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

        // ─── Gapless Playback ───

        /// <summary>
        /// Determines the next track that gapless should pre-buffer, using the same
        /// logic as Player_TrackFinished (queue → shuffle → sequential).
        /// </summary>
        private AudioFileInfo? GetNextTrackForGapless()
        {
            // Loop One: gapless should re-buffer the same track
            if (ThemeManager.LoopMode == LoopMode.One)
            {
                string? currentFile = _player.CurrentFile;
                if (currentFile != null)
                {
                    var current = _files.FirstOrDefault(f =>
                        string.Equals(f.FilePath, currentFile, StringComparison.OrdinalIgnoreCase));
                    return current?.Status != AudioStatus.Corrupt ? current : null;
                }
            }

            // Queue takes priority
            if (_queue.Count > 0)
            {
                var q = _queue[0];
                return q.Status != AudioStatus.Corrupt ? q : null;
            }

            var items = _filteredView?.Cast<AudioFileInfo>().ToList();
            if (items == null || items.Count == 0) return null;

            if (_shuffleMode)
                return PickRandomTrack(items);

            string? currentPath = _player.CurrentFile;
            if (currentPath == null) return null;

            int currentIdx = items.FindIndex(f => string.Equals(f.FilePath, currentPath, StringComparison.OrdinalIgnoreCase));
            int nextIdx = currentIdx + 1;
            if (nextIdx >= items.Count)
            {
                // Loop All: wrap to first track
                if (ThemeManager.LoopMode == LoopMode.All)
                    nextIdx = 0;
                else
                    return null;
            }

            var next = items[nextIdx];
            return next.Status != AudioStatus.Corrupt ? next : null;
        }

        private void Player_GaplessTrackChanged(object? sender, EventArgs e)
        {
            Dispatcher.InvokeAsync(() =>
            {
                _lastTrackFinishedTime = DateTime.UtcNow; // prevent TrackFinished from double-firing

                // Dequeue if the gapless track came from the queue
                if (_queue.Count > 0)
                {
                    var expected = _queue[0];
                    if (string.Equals(expected.FilePath, _player.CurrentFile, StringComparison.OrdinalIgnoreCase))
                        _queue.RemoveAt(0);
                }

                // Find the new file in the grid and update UI
                var newFile = _files.FirstOrDefault(f =>
                    string.Equals(f.FilePath, _player.CurrentFile, StringComparison.OrdinalIgnoreCase));
                if (newFile != null)
                {
                    FileGrid.SelectedItem = newFile;
                    FileGrid.ScrollIntoView(newFile);
                    _currentSpectrogramFile = newFile;
                    SpectrogramTitle.Text = BuildSpectrogramTitle(newFile);

                    // Reset seek sliders for new track duration
                    SeekSlider.Maximum = _player.TotalDuration.TotalSeconds;
                    NpSeekSlider.ValueChanged -= NpSeekSlider_ValueChanged;
                    NpSeekSlider.Maximum = _player.TotalDuration.TotalSeconds;
                    NpSeekSlider.Value = 0;
                    NpSeekSlider.ValueChanged += NpSeekSlider_ValueChanged;

                    UpdatePlayerUI();
                    DrawWaveformBackground();

                    // Update Now Playing panel if it's open
                    if (_npVisible)
                    {
                        NpSetTrack(newFile);
                        NpUpdateQueuePopup();
                    }

                    // Main-window color match for gapless switch
                    LoadMainCoverColors(newFile.FilePath);

                    // Update integrations
                    if (_lastFm.IsEnabled)
                        _lastFm.TrackStarted(newFile.Artist, newFile.Title, _player.TotalDuration.TotalSeconds);
                    if (_discord.IsEnabled)
                        _discord.UpdatePresence(newFile.Artist, newFile.Title, newFile.FileName,
                            _player.TotalDuration, TimeSpan.Zero, false);
                }
            });
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
                                 .Where(f => SupportedExtensions.Contains(IOPath.GetExtension(f))
                                          || ArchiveExtensions.Contains(IOPath.GetExtension(f))
                                          || IOPath.GetExtension(f).Equals(".cue", StringComparison.OrdinalIgnoreCase)));
                }
                else if (File.Exists(path) && (SupportedExtensions.Contains(IOPath.GetExtension(path))
                                            || ArchiveExtensions.Contains(IOPath.GetExtension(path))
                                            || PlaylistExtensions.Contains(IOPath.GetExtension(path))
                                            || IOPath.GetExtension(path).Equals(".cue", StringComparison.OrdinalIgnoreCase)))
                {
                    audioFiles.Add(path);
                }
            }

            if (audioFiles.Count > 0)
            {
                var playlistExpanded = ExpandPlaylists(audioFiles);
                var expanded = ExtractAudioFromArchives(playlistExpanded);
                if (expanded.Count > 0)
                    _ = AnalyzeAndAddFiles(expanded.ToArray());
            }
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
                Process.Start(new ProcessStartInfo("explorer.exe") { ArgumentList = { "/select,", file.FilePath }, UseShellExecute = false });
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
            var selected = FileGrid.SelectedItems.Cast<AudioFileInfo>().ToList();
            if (selected.Count == 0) return;
            foreach (var file in selected)
                _files.Remove(file);
        }

        private void EditMetadata_Click(object sender, RoutedEventArgs e)
        {
            if (FileGrid.SelectedItem is not AudioFileInfo file) return;

            var editor = new MetadataEditorWindow(file, this);
            editor.ShowDialog();

            if (editor.MetadataChanged)
            {
                // Refresh the DataGrid row
                _filteredView?.Refresh();
            }
        }

        private void StripMetadata_Click(object sender, RoutedEventArgs e)
        {
            var selected = FileGrid.SelectedItems.Cast<AudioFileInfo>().ToList();
            if (selected.Count == 0) return;

            var stripper = new MetadataStripWindow(selected, this);
            stripper.ShowDialog();

            if (stripper.MetadataChanged)
                _filteredView?.Refresh();
        }

        private void BatchRename_Click(object sender, RoutedEventArgs e)
        {
            var selected = FileGrid.SelectedItems.Cast<AudioFileInfo>().ToList();
            if (selected.Count == 0) return;

            var renamer = new BatchRenameWindow(selected, (file, newPath) =>
            {
                file.FilePath = newPath;
                file.FileName = IOPath.GetFileName(newPath);
            });
            renamer.Owner = this;
            renamer.ShowDialog();
            _filteredView?.Refresh();
        }

        private void CompareWaveforms_Click(object sender, RoutedEventArgs e)
        {
            var selected = FileGrid.SelectedItems.Cast<AudioFileInfo>().ToList();
            if (selected.Count != 2)
            {
                ErrorDialog.Show("Select Two Files", "Select exactly two files to compare their waveforms.", this);
                return;
            }

            var win = new WaveformCompareWindow(selected[0].FilePath, selected[1].FilePath);
            win.Owner = this;
            win.Show();
        }

        private void CompareSpectrograms_Click(object sender, RoutedEventArgs e)
        {
            var selected = FileGrid.SelectedItems.Cast<AudioFileInfo>().ToList();
            if (selected.Count != 2)
            {
                ErrorDialog.Show("Select Two Files", "Select exactly two files to compare their spectrograms.", this);
                return;
            }

            var win = new SpectrogramCompareWindow(selected[0].FilePath, selected[1].FilePath) { Owner = this };
            win.Show();
        }

        private void FindDuplicates_Click(object sender, RoutedEventArgs e)
        {
            if (_files.Count < 2) return;
            var win = new DuplicateDetectionWindow(_files.ToList());
            win.Owner = this;
            win.Show();
        }

        private async void AcoustIdIdentify_Click(object sender, RoutedEventArgs e)
        {
            if (ThemeManager.OfflineModeEnabled)
            {
                ShowOfflineNotice("AcoustID fingerprinting");
                return;
            }
            if (FileGrid.SelectedItem is not AudioFileInfo file) return;

            // For cue virtual tracks, use the actual audio file referenced by the cue sheet
            string actualPath = file.FilePath;
            if (file.IsCueVirtualTrack && !string.IsNullOrEmpty(file.CueSheetPath))
            {
                // CueSheetPath is the .cue text file — fpcalc needs the real audio file
                // The audio file is usually next to the cue sheet with the same name or referenced inside it
                actualPath = file.FilePath;
            }
            if (!File.Exists(actualPath))
            {
                ErrorDialog.Show("File Not Found", "The audio file could not be found.", this);
                return;
            }

            if (string.IsNullOrWhiteSpace(ThemeManager.AcoustIdApiKey))
            {
                ErrorDialog.Show("AcoustID Not Configured",
                    "Enter your AcoustID API key in Settings → Integrations.\n\nGet a free key at https://acoustid.org/new-application", this);
                return;
            }

            if (AcoustIdService.FindFpcalc() == null)
            {
                StatusText.Text = "Downloading fpcalc...";
                var fpcalc = await AcoustIdService.EnsureFpcalcAsync();
                if (fpcalc == null)
                {
                    ErrorDialog.Show("fpcalc Not Found",
                        "AcoustID requires fpcalc.exe (Chromaprint).\n\nAutomatic download failed. Download it manually from https://acoustid.org/chromaprint and place it next to AudioAuditor.exe or in your PATH.", this);
                    StatusText.Text = "";
                    return;
                }
            }

            StatusText.Text = "Fingerprinting with AcoustID...";
            try
            {
                // Step 1: Generate fingerprint
                var fp = await AcoustIdService.GetFingerprint(actualPath);
                if (fp == null)
                {
                    StatusText.Text = "AcoustID: Fingerprinting failed — fpcalc could not process this file.";
                    return;
                }

                // Step 2: Look up fingerprint
                StatusText.Text = $"AcoustID: Fingerprint OK ({fp.Value.duration}s), searching database...";
                var results = await AcoustIdService.Lookup(fp.Value.fingerprint, fp.Value.duration, ThemeManager.AcoustIdApiKey);
                if (results.Count == 0)
                {
                    StatusText.Text = $"AcoustID: No matches in database (fingerprint {fp.Value.duration}s). Track may not be cataloged.";
                    return;
                }

                var best = results[0];
                string msg = $"Title: {best.Title}\nArtist: {best.Artist}";
                if (!string.IsNullOrEmpty(best.Album)) msg += $"\nAlbum: {best.Album}";
                if (best.Year.HasValue) msg += $"\nYear: {best.Year}";
                if (best.TrackNumber.HasValue) msg += $"\nTrack: {best.TrackNumber}";
                msg += $"\nConfidence: {best.Score:P0}";
                msg += "\n\nWrite this metadata to the file?";

                var write = MessageBox.Show(msg, "AcoustID Result", MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (write == MessageBoxResult.Yes && !file.IsCueVirtualTrack)
                {
                    try
                    {
                        using var tagFile = TagLib.File.Create(file.FilePath);
                        if (!string.IsNullOrEmpty(best.Title)) tagFile.Tag.Title = best.Title;
                        if (!string.IsNullOrEmpty(best.Artist)) tagFile.Tag.Performers = new[] { best.Artist };
                        if (!string.IsNullOrEmpty(best.Album)) tagFile.Tag.Album = best.Album;
                        if (best.Year.HasValue) tagFile.Tag.Year = (uint)best.Year.Value;
                        if (best.TrackNumber.HasValue) tagFile.Tag.Track = (uint)best.TrackNumber.Value;
                        tagFile.Save();

                        file.Title = best.Title;
                        file.Artist = best.Artist;
                        _filteredView?.Refresh();
                        StatusText.Text = "AcoustID: Metadata written.";
                    }
                    catch (Exception ex)
                    {
                        ErrorDialog.Show("Write Failed", $"Could not write metadata: {ex.Message}", this);
                    }
                }
                else
                {
                    StatusText.Text = $"AcoustID: {best.Title} — {best.Artist} ({best.Score:P0})";
                }
            }
            catch (Exception ex)
            {
                StatusText.Text = $"AcoustID error: {ex.Message}";
            }
        }

        private async void WriteReplayGain_Click(object sender, RoutedEventArgs e)
        {
            var selected = FileGrid.SelectedItems.Cast<AudioFileInfo>().ToList();
            if (selected.Count == 0) return;

            int written = 0;
            int failed = 0;

            await Task.Run(() =>
            {
                foreach (var file in selected)
                {
                    var result = AudioAnalyzer.CalculateAndWriteReplayGain(file.FilePath);
                    if (result.HasValue)
                    {
                        var gain = result.Value.Gain;
                        Dispatcher.InvokeAsync(() =>
                        {
                            file.ReplayGain = gain;
                            file.HasReplayGain = true;
                        });
                        written++;
                    }
                    else
                    {
                        failed++;
                    }
                }
            });

            _filteredView?.Refresh();

            string msg = $"Replay Gain written to {written} file{(written != 1 ? "s" : "")}";
            if (failed > 0) msg += $" ({failed} failed)";
            MessageBox.Show(msg, "Replay Gain", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        // ═══════════════════════════════════════════
        //  Album Cover
        // ═══════════════════════════════════════════

        private bool _showAlbumCover;

        private void ToggleAlbumCover_Click(object sender, RoutedEventArgs e)
        {
            _showAlbumCover = !_showAlbumCover;
            AlbumCoverToggleText.Text = _showAlbumCover ? "Hide" : "Cover";

            if (_showAlbumCover)
            {
                AlbumCoverColumn.Width = new GridLength(210);
                AlbumCoverPanel.Visibility = Visibility.Visible;
                UpdateAlbumCoverAsync();
            }
            else
            {
                AlbumCoverColumn.Width = new GridLength(0);
                AlbumCoverPanel.Visibility = Visibility.Collapsed;
                AlbumCoverImage.Source = null;
            }
        }

        private async void UpdateAlbumCoverAsync()
        {
            try
            {
            if (!_showAlbumCover) return;

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
                var cover = await Task.Run(() => ExtractAlbumCover(file.FilePath));
                AlbumCoverImage.Source = cover;
            }
            catch
            {
                AlbumCoverImage.Source = null;
            }
            }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[UpdateAlbumCoverAsync] {ex}"); }
        }

        private void SaveAlbumCoverFromPanel_Click(object sender, RoutedEventArgs e)
        {
            // Determine which file's cover is shown (same logic as UpdateAlbumCover)
            AudioFileInfo? file = null;
            if (_player.CurrentFile != null)
                file = _files.FirstOrDefault(f => string.Equals(f.FilePath, _player.CurrentFile, StringComparison.OrdinalIgnoreCase));
            if (file == null)
                file = FileGrid.SelectedItem as AudioFileInfo;

            if (file == null || string.IsNullOrEmpty(file.FilePath)) return;
            SaveAlbumCoverToFile(file.FilePath);
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

        /// <summary>
        /// Extracts the original album cover bytes and MIME type from an audio file.
        /// Returns null if no cover is embedded.
        /// </summary>
        private static (byte[] data, string mime)? ExtractAlbumCoverBytes(string filePath)
        {
            try
            {
                using var tagFile = TagLib.File.Create(filePath);
                var pictures = tagFile.Tag.Pictures;
                if (pictures == null || pictures.Length == 0) return null;
                var pic = pictures[0];
                return (pic.Data.Data, pic.MimeType ?? "image/jpeg");
            }
            catch
            {
                return null;
            }
        }

        private void SaveAlbumCoverToFile(string audioFilePath)
        {
            var coverData = ExtractAlbumCoverBytes(audioFilePath);
            if (coverData == null)
            {
                ErrorDialog.Show("No Album Cover", "This file does not contain an album cover image.", this);
                return;
            }

            var (data, mime) = coverData.Value;
            string ext = mime switch
            {
                "image/png" => ".png",
                "image/bmp" => ".bmp",
                "image/gif" => ".gif",
                _ => ".jpg"
            };

            string defaultName = IOPath.GetFileNameWithoutExtension(audioFilePath) + "_cover" + ext;

            var dialog = new SaveFileDialog
            {
                Title = "Save Album Cover",
                FileName = defaultName,
                Filter = $"Image Files|*{ext}|All Files|*.*"
            };

            if (dialog.ShowDialog() == true)
            {
                try
                {
                    File.WriteAllBytes(dialog.FileName, data);
                }
                catch (Exception ex)
                {
                    ErrorDialog.Show("Save Error", $"Could not save album cover:\n{ex.Message}", this);
                }
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

                var saveBtn = new Button
                {
                    Content = "Save Cover...",
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Padding = new Thickness(12, 4, 12, 4),
                    Margin = new Thickness(0, 6, 10, 0),
                    Cursor = Cursors.Hand,
                    Background = new SolidColorBrush(Color.FromRgb(55, 55, 55)),
                    Foreground = Brushes.White,
                    BorderBrush = new SolidColorBrush(Color.FromRgb(80, 80, 80)),
                    FontFamily = new FontFamily("Segoe UI"),
                    FontSize = 12
                };
                string capturedPath = file.FilePath;
                saveBtn.Click += (_, _) => SaveAlbumCoverToFile(capturedPath);

                var stack = new DockPanel();
                DockPanel.SetDock(saveBtn, Dock.Top);
                stack.Children.Add(saveBtn);
                stack.Children.Add(image);

                window.Content = stack;
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

            bool showPrivacy = settingsWindow.RequestPrivacyOnClose;

            // Refresh all UI state after settings change — wrap entirely to prevent crash
            try
            {
                UpdateServiceButtonLabels();
                ApplyThemeTitleBar();
                UpdateShuffleUI();
                UpdateLoopUI();
                _eqSliderTemplateCache = null;
                InitializeEqualizerSliders();
                ChkEqEnabled.IsChecked = ThemeManager.EqualizerEnabled;

                // Sync Discord RPC state
                if (ThemeManager.DiscordRpcEnabled && !string.IsNullOrWhiteSpace(ThemeManager.DiscordRpcClientId))
                {
                    if (!_discord.IsEnabled)
                        _discord.Enable();
                    else
                        _discord.Enable();
                }
                else if (_discord.IsEnabled)
                    _discord.Disable();

                // Sync spatial audio state
                var spatial = _player.CurrentSpatialAudio;
                if (spatial != null) spatial.Enabled = ThemeManager.SpatialAudioEnabled;

                // Sync normalization on currently playing track
                if (_player.IsPlaying || _player.IsPaused)
                    _player.SetNormalization(ThemeManager.AudioNormalization);

                // Sync Last.fm state
                if (!string.IsNullOrEmpty(ThemeManager.LastFmSessionKey))
                    _lastFm.Configure(ThemeManager.LastFmApiKey, ThemeManager.LastFmApiSecret, ThemeManager.LastFmSessionKey);

                UpdateLastFmStatusIndicator();
                ApplyColumnVisibility();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Settings_Click refresh] {ex}");
            }

            // Show SH Labs privacy overlay AFTER all refresh is done — defer to next
            // UI cycle so the SettingsWindow is fully closed and focus is restored.
            if (showPrivacy)
            {
                Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, () =>
                {
                    try { ShowSHLabsPrivacyOverlay(); }
                    catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[SHLabs overlay] {ex}"); }
                });
            }
        }

        // ═══════════════════════════════════════════
        //  Queue
        // ═══════════════════════════════════════════

        private void Queue_Click(object sender, RoutedEventArgs e)
        {
            var queueWindow = new QueueWindow(_queue) { Owner = this, UpNext = GetUpNextTracks() };
            queueWindow.ShowDialog();
            if (_npVisible) NpUpdateQueuePopup();
        }

        private List<AudioFileInfo> GetUpNextTracks()
        {
            var upcoming = new List<AudioFileInfo>();
            for (int i = 0; i < Math.Min(_queue.Count, 3); i++)
                upcoming.Add(_queue[i]);

            if (upcoming.Count == 0 && _player.CurrentFile != null)
            {
                var current = _files.FirstOrDefault(f =>
                    string.Equals(f.FilePath, _player.CurrentFile, StringComparison.OrdinalIgnoreCase));
                if (current != null)
                {
                    int currentIdx = _files.IndexOf(current);
                    AudioFileInfo? next = null;
                    if (_shuffleMode && _shuffleDeck.Count > 0)
                    {
                        int deckIdx = _shuffleDeck.FindIndex(f => f.FilePath == current.FilePath);
                        if (deckIdx >= 0 && deckIdx + 1 < _shuffleDeck.Count)
                            next = _shuffleDeck[deckIdx + 1];
                    }
                    else if (currentIdx >= 0 && currentIdx + 1 < _files.Count)
                    {
                        next = _files[currentIdx + 1];
                    }
                    if (next != null) upcoming.Add(next);
                }
            }
            return upcoming;
        }



        private void AddToQueue_Click(object sender, RoutedEventArgs e)
        {
            var selected = FileGrid.SelectedItems.Cast<AudioFileInfo>().Where(f => f.Status != AudioStatus.Corrupt).ToList();
            if (selected.Count == 0) return;
            foreach (var file in selected)
                _queue.Add(file);
            StatusText.Text = $"Added {selected.Count} file(s) to queue ({_queue.Count} in queue)";
            if (_npVisible) NpUpdateQueuePopup();
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
            }
        }

        private static ImageSource CreateServiceLogo(string service, int slotIndex = -1, bool forceVector = false)
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

        // ═══════════════════════════════════════════
        //  Favorites
        // ═══════════════════════════════════════════

        private void ContextMenu_Opened(object sender, RoutedEventArgs e)
        {
            var selected = FileGrid.SelectedItems.Cast<AudioFileInfo>().ToList();
            if (selected.Count == 0) return;

            bool allFav = selected.All(f => f.IsFavorite);
            bool anyFav = selected.Any(f => f.IsFavorite);

            MenuToggleFavorite.Header = allFav ? "Remove from Favorites" : anyFav ? "Toggle Favorites" : "Add to Favorites";
            MenuFavMoveUp.Visibility   = (selected.Count == 1 && selected[0].IsFavorite) ? Visibility.Visible : Visibility.Collapsed;
            MenuFavMoveDown.Visibility = (selected.Count == 1 && selected[0].IsFavorite) ? Visibility.Visible : Visibility.Collapsed;
        }

        private void ToggleFavorite_Click(object sender, RoutedEventArgs e)
        {
            var selected = FileGrid.SelectedItems.Cast<AudioFileInfo>().ToList();
            if (selected.Count == 0) return;
            foreach (var file in selected)
                FavoritesService.Toggle(file);
            RefreshFavoriteSort();
        }

        private void StarCell_Click(object sender, MouseButtonEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is AudioFileInfo file)
            {
                FavoritesService.Toggle(file);
                RefreshFavoriteSort();
            }
        }

        private void MoveFavoriteUp_Click(object sender, RoutedEventArgs e)
        {
            if (FileGrid.SelectedItem is not AudioFileInfo file) return;
            FavoritesService.MoveUp(file, _files);
            RefreshFavoriteSort();
        }

        private void MoveFavoriteDown_Click(object sender, RoutedEventArgs e)
        {
            if (FileGrid.SelectedItem is not AudioFileInfo file) return;
            FavoritesService.MoveDown(file, _files);
            RefreshFavoriteSort();
        }

        public void RefreshFavoriteSort()
        {
            if (_filteredView == null) return;
            _filteredView.SortDescriptions.Clear();
            _filteredView.SortDescriptions.Add(new SortDescription(nameof(AudioFileInfo.IsFavorite),
                ListSortDirection.Descending));
            _filteredView.SortDescriptions.Add(new SortDescription(nameof(AudioFileInfo.FavoriteOrder),
                ListSortDirection.Ascending));
            _filteredView.Refresh();
        }

        // ═══════════════════════════════════════════
        //  File Operations
        // ═══════════════════════════════════════════

        private void QuickRename_Click(object sender, RoutedEventArgs e)
        {
            var selected = FileGrid.SelectedItems.OfType<AudioFileInfo>().ToList();
            int count = 0;
            foreach (var file in selected)
            {
                if (file.IsCueVirtualTrack) continue;
                string dir = IOPath.GetDirectoryName(file.FilePath) ?? "";
                string name = IOPath.GetFileNameWithoutExtension(file.FilePath);
                string ext = IOPath.GetExtension(file.FilePath);
                string suffix;

                string statusText = file.Status switch
                {
                    AudioStatus.Valid => "REAL",
                    AudioStatus.Fake => "FAKE",
                    AudioStatus.Corrupt => "CORRUPT",
                    AudioStatus.Optimized => "OPTIMIZED",
                    _ => "UNKNOWN"
                };

                switch (ThemeManager.RenamePatternIndex)
                {
                    case 0:
                        if (file.ReportedBitrate <= 0) continue;
                        suffix = $"[FAKE {file.ReportedBitrate}kbps]";
                        break;
                    case 1:
                        if (file.ActualBitrate <= 0) continue;
                        suffix = $"[{statusText} {file.ActualBitrate}kbps]";
                        break;
                    case 2:
                        if (file.ReportedBitrate <= 0 || file.ActualBitrate <= 0) continue;
                        suffix = $"[{statusText} {file.ReportedBitrate}kbps {file.ActualBitrate}kbps]";
                        break;
                    default:
                        continue;
                }

                string newName = $"{name} {suffix}{ext}";
                string newPath = IOPath.Combine(dir, newName);
                if (File.Exists(newPath)) continue;
                try
                {
                    File.Move(file.FilePath, newPath);
                    file.FilePath = newPath;
                    file.FileName = newName;
                    count++;
                }
                catch { }
            }
            if (count > 0) StatusText.Text = $"Renamed {count} file(s).";
        }

        private void CopyToFolder_Click(object sender, RoutedEventArgs e)
        {
            var selected = FileGrid.SelectedItems.OfType<AudioFileInfo>()
                               .Where(f => !f.IsCueVirtualTrack).ToList();
            if (selected.Count == 0) return;
            var dlg = new OpenFolderDialog
            {
                Title = "Select destination folder",
                FolderName = ThemeManager.DefaultCopyFolder
            };
            if (dlg.ShowDialog() != true) return;
            string dest = dlg.FolderName;
            int count = 0, failed = 0;
            foreach (var file in selected)
            {
                try
                {
                    string target = IOPath.Combine(dest, IOPath.GetFileName(file.FilePath));
                    if (!File.Exists(target))
                    {
                        File.Copy(file.FilePath, target);
                        count++;
                    }
                }
                catch { failed++; }
            }
            StatusText.Text = failed > 0
                ? $"Copied {count} file(s) to {dest} ({failed} failed / already exists)"
                : $"Copied {count} file(s) to {dest}";
        }

        private void MoveToFolder_Click(object sender, RoutedEventArgs e)
        {
            var selected = FileGrid.SelectedItems.OfType<AudioFileInfo>()
                               .Where(f => !f.IsCueVirtualTrack).ToList();
            if (selected.Count == 0) return;
            var dlg = new OpenFolderDialog
            {
                Title = "Select destination folder",
                FolderName = ThemeManager.DefaultMoveFolder
            };
            if (dlg.ShowDialog() != true) return;
            string dest = dlg.FolderName;
            int count = 0, failed = 0;
            foreach (var file in selected)
            {
                try
                {
                    string target = IOPath.Combine(dest, IOPath.GetFileName(file.FilePath));
                    if (!File.Exists(target))
                    {
                        File.Move(file.FilePath, target);
                        count++;
                        _files.Remove(file);
                    }
                }
                catch { failed++; }
            }
            StatusText.Text = failed > 0
                ? $"Moved {count} file(s) to {dest} ({failed} failed / conflict)"
                : $"Moved {count} file(s) to {dest}";
            UpdateStatusSummary();
        }

        private void SaveToPlaylist_Click(object sender, RoutedEventArgs e)
        {
            var selected = FileGrid.SelectedItems.OfType<AudioFileInfo>()
                               .Where(f => !f.IsCueVirtualTrack).ToList();
            if (selected.Count == 0)
                selected = _files.Where(f => !f.IsCueVirtualTrack).ToList();
            if (selected.Count == 0) return;

            var dlg = new SaveFileDialog
            {
                Title = "Save Playlist",
                Filter = "M3U Playlist|*.m3u|Extended M3U|*.m3u8",
                DefaultExt = "m3u"
            };
            if (!string.IsNullOrWhiteSpace(ThemeManager.DefaultPlaylistFolder) && Directory.Exists(ThemeManager.DefaultPlaylistFolder))
                dlg.InitialDirectory = ThemeManager.DefaultPlaylistFolder;
            if (dlg.ShowDialog() != true) return;

            var lines = new List<string> { "#EXTM3U" };
            foreach (var file in selected)
            {
                lines.Add($"#EXTINF:{(int)file.DurationSeconds},{file.Artist} - {file.Title}");
                lines.Add(file.FilePath);
            }
            try
            {
                File.WriteAllLines(dlg.FileName, lines, System.Text.Encoding.UTF8);
                StatusText.Text = $"Playlist saved: {dlg.FileName} ({selected.Count} tracks)";
            }
            catch (Exception ex)
            {
                ErrorDialog.Show("Save Error", $"Error saving playlist:\n{ex.Message}", this);
            }
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

        // ═══════════════════════════════════════════
        //  Animation Occlusion Pause
        // ═══════════════════════════════════════════

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        [DllImport("user32.dll")]
        private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

        private const uint MONITOR_DEFAULTTONEAREST = 2;

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int Left, Top, Right, Bottom;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MONITORINFO
        {
            public int cbSize;
            public RECT rcMonitor;
            public RECT rcWork;
            public uint dwFlags;
        }

        private void OnWindowActivated(object? sender, EventArgs e)
        {
            _occlusionCheckTimer?.Stop();
            _occlusionCheckTimer = null;

            if (_isPausedForOcclusion)
            {
                _isPausedForOcclusion = false;
                ResumeAnimations();
            }
        }

        private void OnWindowDeactivated(object? sender, EventArgs e)
        {
            if (_occlusionCheckTimer != null)
            {
                _occlusionCheckTimer.Stop();
                _occlusionCheckTimer.Tick -= OcclusionCheckTimer_Tick;
            }
            _occlusionCheckTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            _occlusionCheckTimer.Tick += OcclusionCheckTimer_Tick;
            _occlusionCheckTimer.Start();
        }

        private void OnWindowStateChanged(object? sender, EventArgs e)
        {
            if (WindowState == WindowState.Minimized && !_isPausedForOcclusion)
            {
                _isPausedForOcclusion = true;
                PauseAnimations();
            }
            else if (WindowState != WindowState.Minimized && _isPausedForOcclusion)
            {
                _isPausedForOcclusion = false;
                ResumeAnimations();
            }
            if (_npVisible)
                NpApplyVizPlacement();
        }

        private void OcclusionCheckTimer_Tick(object? sender, EventArgs e)
        {
            if (IsActive) { _occlusionCheckTimer?.Stop(); return; }

            bool fullscreen = IsAnotherAppFullscreen();
            if (fullscreen && !_isPausedForOcclusion)
            {
                _isPausedForOcclusion = true;
                PauseAnimations();
            }
            else if (!fullscreen && _isPausedForOcclusion)
            {
                _isPausedForOcclusion = false;
                ResumeAnimations();
            }
        }

        private void PauseAnimations()
        {
            StopVisualizer();
            StopWaveformAnimation();

            // Pause NP panel animations when minimized/hidden
            if (_npVisible)
            {
                _npUpdateTimer?.Stop();
                NpStopBgAnimation();
                NpStopGlowPulse();
                NpStopVisualizer(); // stop NP visualizer too — it runs independently of the main one
            }
        }

        private void ResumeAnimations()
        {
            if (_npVisible)
            {
                // Resume NP panel timers and re-sync lyrics
                _npUpdateTimer?.Start();
                NpStartBgAnimation();
                NpStartGlowPulse();

                // Resume the NP visualizer if it was running
                if (_npVisualizerEnabled && _player.IsPlaying)
                    NpStartVisualizer();

                // Re-sync lyrics to current position immediately
                if (_npCurrentLyrics.IsTimed && _player != null)
                {
                    var pos = _player.CurrentPosition;
                    _npCurrentLyricIndex = -1; // force full refresh
                    NpUpdateLyricHighlight(pos);
                }
            }
            else
            {
                if (_visualizerMode && _player.IsPlaying)
                    StartVisualizer();
                if (_waveformData.Length > 0)
                    StartWaveformAnimation();
            }
        }

        private bool IsAnotherAppFullscreen()
        {
            try
            {
                IntPtr fg = GetForegroundWindow();
                IntPtr myHwnd = new WindowInteropHelper(this).Handle;
                if (fg == IntPtr.Zero || fg == myHwnd) return false;

                if (!GetWindowRect(fg, out RECT fgRect)) return false;

                IntPtr monitor = MonitorFromWindow(fg, MONITOR_DEFAULTTONEAREST);
                var mi = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
                if (!GetMonitorInfo(monitor, ref mi)) return false;

                var bounds = mi.rcMonitor;
                return fgRect.Left <= bounds.Left &&
                       fgRect.Top <= bounds.Top &&
                       fgRect.Right >= bounds.Right &&
                       fgRect.Bottom >= bounds.Bottom;
            }
            catch { return false; }
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

            // Rainbow Bars: dynamically cycle gradient + accent color each frame
            bool isRainbow = ThemeManager.CurrentPlaybarTheme == "Rainbow Bars";
            Color[] gradientOverride = playbarColors.ProgressGradient;
            if (isRainbow)
            {
                double hueBase = (elapsed * 30.0) % 360.0; // 30 degrees/sec cycle
                gradientOverride = new[]
                {
                    HsvToColor(hueBase, 0.85, 0.9),
                    HsvToColor((hueBase + 120) % 360, 0.85, 0.9),
                    HsvToColor((hueBase + 240) % 360, 0.85, 0.9)
                };
                // Update accent resource so shuffle/volume buttons also cycle
                var accentColor = HsvToColor(hueBase, 0.85, 0.95);
                accentColor.A = 255;
                var accentBrush = new SolidColorBrush(accentColor);
                accentBrush.Freeze();
                Application.Current.Resources["PlaybarAccentColor"] = accentBrush;
            }

            // Animate base data with time-varying phase
            int points = _waveformBaseData.Length;
            double mid = canvasHeight / 2;

            // Fade envelope: gentle taper at edges (3% on each side)
            double fadeRegion = 0.03;

            // Update animated waveform data — Waves animation (flowing undulation)
            for (int i = 0; i < points; i++)
            {
                double t = (double)i / points;
                // Start with normalized base shape (0..1 range)
                double baseVal = Math.Clamp((_waveformBaseData[i] + 1.33) / 2.66, 0.25, 0.95);

                // Multiple traveling waves create a flowing undulation
                double wave = 0.08 * Math.Sin(4 * Math.PI * t + elapsed * animSpeed * 2.0)
                            + 0.06 * Math.Sin(7 * Math.PI * t - elapsed * animSpeed * 1.5)
                            + 0.04 * Math.Sin(13 * Math.PI * t + elapsed * animSpeed * 3.0);
                _waveformData[i] = Math.Clamp(baseVal + wave, 0.15, 0.98);
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
                var gradientColors = isRainbow ? gradientOverride : playbarColors.ProgressGradient;
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

            RenderPlaybarAnim();
        }

        private double GetCurrentBassEnergy()
        {
            if (_vizSmoothed == null || _vizSmoothed.Length == 0) return 0;
            double sum = 0;
            int cnt = Math.Min(5, _vizSmoothed.Length);
            for (int i = 0; i < cnt; i++) sum += _vizSmoothed[i];
            return sum / cnt;
        }

        private void RenderPlaybarAnim()
        {
            PlaybarAnimCanvas.Children.Clear();
            double w = PlaybarAnimCanvas.ActualWidth;
            double h = PlaybarAnimCanvas.ActualHeight;
            if (w < 1 || h < 1 || SeekSlider.Maximum <= 0) return;

            double pct = SeekSlider.Value / SeekSlider.Maximum;
            double fillW = w * pct;

            var accentBrush = TryFindResource("AccentColor") as SolidColorBrush ?? Brushes.CornflowerBlue;
            Color accent = accentBrush.Color;

            // Default playbar fill (was "Classic" style)
            if (fillW >= 1)
            {
                var rect = new System.Windows.Shapes.Rectangle
                {
                    Width = fillW, Height = h,
                    RadiusX = h / 2, RadiusY = h / 2
                };
                rect.Fill = new LinearGradientBrush(
                    Color.FromArgb(55, accent.R, accent.G, accent.B),
                    Color.FromArgb(15, accent.R, accent.G, accent.B),
                    new Point(0, 0), new Point(1, 0));
                Canvas.SetLeft(rect, 0);
                PlaybarAnimCanvas.Children.Add(rect);
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
                string spectRefreshKey = $"{file.FilePath}|{_spectrogramLinearScale}|{_spectrogramChannel}|{ThemeManager.SpectrogramMagmaColormap}|{ThemeManager.SpectrogramHiFiMode}";
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

            // Use precise rendering time for frame limiting (~60fps)
            if (e is RenderingEventArgs re)
            {
                if ((re.RenderingTime - _lastVizRenderTime).TotalMilliseconds < 16) return;
                _lastVizRenderTime = re.RenderingTime;
            }

            double width = VizTarget.ActualWidth;
            double height = VizTarget.ActualHeight;
            if (width < 10 || height < 10) return;

            int numBars = VizNumBars;

            // Get recent samples and run FFT
            float[] samples = _player.GetVisualizerSamples(4096);
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
            if (ThemeManager.VisualizerFullVolume && _player.Volume > 0.01f && _player.Volume < 1f)
            {
                double gain = 1.0 / _player.Volume;
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

            // Get raw samples for waveform display
            float[] vizData = _player.GetVisualizerSamples(VizFftSize);
            if (vizData.Length == 0) return;

            // Initialize scope polyline
            if (_scopeLine == null)
            {
                var brush = new SolidColorBrush(gradient.Length > 1 ? gradient[1] : Colors.Lime);
                _scopeLine = new System.Windows.Shapes.Polyline
                {
                    Stroke = brush,
                    StrokeThickness = 1.5,
                    IsHitTestVisible = false
                };
                VizTarget.Children.Add(_scopeLine);
            }

            // Color the scope line
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

            var points = new System.Windows.Media.PointCollection(pointCount);
            double step = (double)vizData.Length / pointCount;

            for (int i = 0; i < pointCount; i++)
            {
                int sampleIdx = Math.Min((int)(i * step), vizData.Length - 1);
                double sample = vizData[sampleIdx];
                double x = (double)i / pointCount * width;
                double y = centerY - sample * amplitude;
                points.Add(new System.Windows.Point(x, y));
            }

            _scopeLine.Points = points;
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

            // Boost saturation: ensure minimum 0.5 for grey-ish colors
            s = Math.Max(s, 0.5);
            // Brighten the value
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

        private void NowPlaying_Click(object sender, RoutedEventArgs e)
        {
            ToggleNowPlaying(!_npVisible);
        }

        private void ToggleNowPlaying(bool show)
        {
            _npVisible = show;
            NowPlayingPanel.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
            MainContent.Visibility = show ? Visibility.Collapsed : Visibility.Visible;

            if (show)
            {
                NpLoadPreferences();
                if (_npUpdateTimer == null)
                {
                    _npUpdateTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(33) };
                    _npUpdateTimer.Tick += NpUpdateTimer_Tick;
                }
                _npUpdateTimer.Start();

                if (_player.CurrentFile != null)
                {
                    var currentFile = _files.FirstOrDefault(f =>
                        string.Equals(f.FilePath, _player.CurrentFile, StringComparison.OrdinalIgnoreCase));
                    if (currentFile != null)
                        NpSetTrack(currentFile);
                }
                NpVolumeSlider.Value = VolumeSlider.Value;
                NpUpdatePlayState();
                NpUpdateShuffleIcon();
                NpUpdateLoopIcon();
                if (_npVisible) NpUpdateQueuePopup();
                NpUpdateAutoPlayIcon();
                NpUpdateVisualizerIcon();
                NpUpdateColorMatchIcon();
                NpApplyVizPlacement();
                NpUpdateLyricsOffIcon();
                NpUpdateTranslateIcon();
                NpUpdateKaraokeIcon();
                NpApplyLyricsOffMode();
                NpUpdateNextTrackPreview();
                NpProviderBtn.ToolTip = $"Provider: {NpLyricProviders[_npProviderIndex].Name}";
                NpStartBgAnimation();
                NpStartGlowPulse();

                // Force lyric re-sync when reopening NP panel
                _npLyricsNeedCatchUp = true;

                if (_npVisualizerEnabled && _player.IsPlaying)
                    NpStartVisualizer();
                else if (_visualizerActive && !_npVisualizerEnabled)
                {
                    // Main visualizer is running but NP viz is off — pause main since it's hidden
                    _mainVizWasActive = true;
                    StopVisualizer();
                    VisualizerCanvas.Children.Clear();
                }


            }
            else
            {
                _npUpdateTimer?.Stop();
                NpStopVisualizer();
                NpStopBgAnimation();
                NpStopGlowPulse();

                // Resume main visualizer if it was running before NP opened
                if (_mainVizWasActive && _visualizerMode && _player.IsPlaying)
                {
                    _mainVizWasActive = false;
                    ClearVisualizerCaches();
                    VisualizerCanvas.Children.Clear();
                    StartVisualizer();
                }

                // The NP panel may have written shared theme resources (PlaybarAccentColor,
                // ButtonBg, ButtonHover, ButtonPressed, ButtonBorder) while it was visible.
                // Decide who owns them on the main screen now:
                //   main color match on  → re-apply main colors (overrides any NP residue)
                //   main color match off → restore theme defaults (clears NP residue)
                // The earlier "only when BOTH are off" guard left NP-tinted resources stuck
                // on the main screen when only NP color match was enabled.
                if (ThemeManager.MainColorMatchEnabled && _mainAlbumPrimary != default)
                {
                    ApplyMainColorMatch();
                }
                else
                {
                    var vizColors = ThemeManager.GetVisualizerColors();
                    Application.Current.Resources["PlaybarAccentColor"] = new SolidColorBrush(vizColors.ProgressGradient[0]);
                    ThemeManager.ApplyTheme(ThemeManager.CurrentTheme);
                    ApplyThemeTitleBar();
                }
            }
        }

        private void NpSetTrack(AudioFileInfo file)
        {
            // Reset Now Playing seek slider to prevent stale seeks on track change
            NpSeekSlider.ValueChanged -= NpSeekSlider_ValueChanged;
            NpSeekSlider.Value = 0;
            NpSeekSlider.Maximum = 0;
            NpSeekSlider.ValueChanged += NpSeekSlider_ValueChanged;

            var displayTitle = file.Title
                ?? (file.FileName != null ? System.IO.Path.GetFileNameWithoutExtension(file.FileName) : null)
                ?? "Unknown";
            NpSongTitle.Text = displayTitle;
            NpBigTitle.Text = displayTitle;
            NpSongArtist.Text = file.Artist ?? "";

            // Show audio specs with color-coded bitrate
            NpSongSpecs.Inlines.Clear();
            var specParts = new List<string>();
            if (!string.IsNullOrEmpty(file.FormatDisplay)) specParts.Add(file.FormatDisplay);
            if (file.SampleRate > 0) specParts.Add($"{file.SampleRate / 1000.0:0.#} kHz");
            if (file.BitsPerSample > 0) specParts.Add($"{file.BitsPerSample}-bit");
            if (file.Channels > 0) specParts.Add(file.Channels == 1 ? "Mono" : file.Channels == 2 ? "Stereo" : $"{file.Channels}ch");

            var defaultBrush = (Brush)FindResource("TextSecondary");
            for (int s = 0; s < specParts.Count; s++)
            {
                if (s > 0) NpSongSpecs.Inlines.Add(new System.Windows.Documents.Run("  •  ") { Foreground = defaultBrush });
                NpSongSpecs.Inlines.Add(new System.Windows.Documents.Run(specParts[s]) { Foreground = defaultBrush });
            }

            // Add bitrate with status coloring (green=Real, red=Fake, orange=Unknown)
            int displayBitrate = file.ActualBitrate > 0 ? file.ActualBitrate : file.ReportedBitrate;
            if (displayBitrate > 0)
            {
                if (specParts.Count > 0)
                    NpSongSpecs.Inlines.Add(new System.Windows.Documents.Run("  •  ") { Foreground = defaultBrush });

                var statusColor = file.Status switch
                {
                    AudioStatus.Valid => System.Windows.Media.Color.FromRgb(0x4C, 0xC9, 0x4C),     // green
                    AudioStatus.Fake => System.Windows.Media.Color.FromRgb(0xFF, 0x5C, 0x5C),      // red
                    AudioStatus.Corrupt => System.Windows.Media.Color.FromRgb(0xFF, 0x5C, 0x5C),   // red
                    _ => System.Windows.Media.Color.FromRgb(0xFF, 0xA5, 0x00),                     // orange
                };
                NpSongSpecs.Inlines.Add(new System.Windows.Documents.Run($"{displayBitrate} kbps")
                {
                    Foreground = new SolidColorBrush(statusColor),
                    FontWeight = FontWeights.SemiBold
                });
            }

            // Add DR inline (only if enabled and available)
            if (ThemeManager.DynamicRangeEnabled && file.HasDynamicRange && file.DynamicRange > 0)
            {
                NpSongSpecs.Inlines.Add(new System.Windows.Documents.Run("  •  ") { Foreground = defaultBrush });
                NpSongSpecs.Inlines.Add(new System.Windows.Documents.Run($"DR-{file.DynamicRange:0}") { Foreground = defaultBrush });
            }

            // Add BPM inline (only if enabled and available)
            if (ThemeManager.BpmDetectionEnabled && file.Bpm > 0)
            {
                NpSongSpecs.Inlines.Add(new System.Windows.Documents.Run("  •  ") { Foreground = defaultBrush });
                NpSongSpecs.Inlines.Add(new System.Windows.Documents.Run($"{file.Bpm} BPM") { Foreground = defaultBrush });
            }

            // Add RIP quality inline (only if enabled and available)
            if (ThemeManager.RipQualityEnabled && file.HasRipQuality)
            {
                NpSongSpecs.Inlines.Add(new System.Windows.Documents.Run("  •  ") { Foreground = defaultBrush });
                var ripColor = file.RipQuality switch
                {
                    "Good" => System.Windows.Media.Color.FromRgb(0x4C, 0xC9, 0x4C),
                    "Suspect" => System.Windows.Media.Color.FromRgb(0xFF, 0xA5, 0x00),
                    "Bad" => System.Windows.Media.Color.FromRgb(0xFF, 0x5C, 0x5C),
                    _ => System.Windows.Media.Color.FromRgb(0xFF, 0xA5, 0x00),
                };
                NpSongSpecs.Inlines.Add(new System.Windows.Documents.Run(file.RipQualityDisplay)
                {
                    Foreground = new SolidColorBrush(ripColor),
                    FontWeight = FontWeights.SemiBold
                });
            }

            // Build MQA / AI / quality tags
            NpTagsPanel.Children.Clear();
            if (file.IsMqa)
                NpTagsPanel.Children.Add(NpCreateTag(file.IsMqaStudio ? "MQA Studio" : "MQA", "#00C2FF"));
            if (file.IsAlac)
                NpTagsPanel.Children.Add(NpCreateTag("ALAC", "#7ACC52"));
            if (file.AiVerdict == "Yes")
                NpTagsPanel.Children.Add(NpCreateTag("AI", "#FF6B6B"));
            else if (file.AiVerdict == "Possible")
                NpTagsPanel.Children.Add(NpCreateTag("AI?", "#FFC107"));
            if (file.IsFakeStereo)
                NpTagsPanel.Children.Add(NpCreateTag("Fake Stereo", "#FFA500"));

            // Reset lyrics when switching tracks — clear immediately to prevent stale state
            bool isSameTrack = string.Equals(_npLastTrackPath, file.FilePath, StringComparison.OrdinalIgnoreCase);
            _npLastTrackPath = file.FilePath;

            if (!isSameTrack)
            {
                NpLyricsScroller.ScrollToVerticalOffset(0);
                _npCurrentLyricIndex = -1;
                _npCurrentLyrics = LyricsResult.Empty;
                _npLyricTextBlocks.Clear();
                _npTranslatedLines = null;
                NpLyricsPanel.Children.Clear();
                _npLyricsVersion++; // invalidate any in-flight lyrics fetch
                _npLyricsNeedCatchUp = true;
            }

            if (!string.IsNullOrEmpty(file.FilePath))
            {
                _ = NpLoadCoverAsync(file.FilePath);
                if (!isSameTrack)
                    _ = NpLoadLyricsAsync(file.FilePath, file.Artist, file.Title);
            }

            NpUpdateNextTrackPreview();
        }

        private Border NpCreateTag(string text, string color)
        {
            var c = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(color);
            return new Border
            {
                Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(40, c.R, c.G, c.B)),
                BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromArgb(100, c.R, c.G, c.B)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(3),
                Padding = new Thickness(5, 1, 5, 1),
                Margin = new Thickness(2, 0, 0, 0),
                Child = new TextBlock
                {
                    Text = text,
                    FontSize = 9,
                    FontWeight = FontWeights.SemiBold,
                    FontFamily = new FontFamily("Segoe UI"),
                    Foreground = new SolidColorBrush(c),
                    VerticalAlignment = VerticalAlignment.Center
                }
            };
        }

        private async Task NpLoadCoverAsync(string filePath)
        {
            int gen = _npColorGeneration;
            string cacheKey = HashPath(filePath);
            try
            {
                // Check color cache first for instant application
                if (ThemeManager.NpColorCacheEnabled && _npColorCache.TryGetValue(cacheKey, out var cachedColors))
                {
                    _npColorCacheLru.Remove(cacheKey);
                    _npColorCacheLru.AddLast(cacheKey);
                    Dispatcher.Invoke(() =>
                    {
                        if (gen != _npColorGeneration) return;
                        // Still need to load the cover image
                        try
                        {
                            using var tagFile = TagLib.File.Create(filePath);
                            if (tagFile.Tag.Pictures.Length > 0)
                            {
                                var data = tagFile.Tag.Pictures[0].Data.Data;
                                var b = new BitmapImage();
                                b.BeginInit();
                                using (var ms = new MemoryStream(data))
                                {
                                    b.StreamSource = ms;
                                    b.CacheOption = BitmapCacheOption.OnLoad;
                                    b.EndInit();
                                }
                                b.Freeze();
                                NpCoverImage.Source = b;
                                NpCoverImage.ClearValue(FrameworkElement.WidthProperty);
                                NpCoverImage.ClearValue(FrameworkElement.HeightProperty);
                                NpApplyFullscreenScaling(WindowState == WindowState.Maximized);
                            }
                        }
                        catch { }
                        ApplyNpExtractedColors(cachedColors);
                    });
                    return;
                }

                byte[]? imageData = await Task.Run(() =>
                {
                    using var tagFile = TagLib.File.Create(filePath);
                    if (tagFile.Tag.Pictures.Length > 0)
                        return tagFile.Tag.Pictures[0].Data.Data;
                    return null;
                });

                if (imageData == null)
                {
                    Dispatcher.Invoke(() => { if (gen == _npColorGeneration) NpClearCover(); });
                    return;
                }

                var bmp = await Task.Run(() =>
                {
                    var b = new BitmapImage();
                    b.BeginInit();
                    using (var ms = new MemoryStream(imageData))
                    {
                        b.StreamSource = ms;
                        b.CacheOption = BitmapCacheOption.OnLoad;
                        b.EndInit();
                    }
                    b.Freeze();
                    return b;
                });

                Dispatcher.Invoke(() =>
                {
                    if (gen != _npColorGeneration) return;
                    NpCoverImage.Source = bmp;
                    NpCoverImage.ClearValue(FrameworkElement.WidthProperty);
                    NpCoverImage.ClearValue(FrameworkElement.HeightProperty);
                    NpApplyFullscreenScaling(WindowState == WindowState.Maximized);
                });

                await NpApplyGlowAsync(imageData, filePath, gen);
            }
            catch
            {
                Dispatcher.Invoke(() => { if (gen == _npColorGeneration) NpClearCover(); });
            }
        }

        private void NpClearCover()
        {
            NpCoverImage.Source = null;
            NpCoverImage.ClearValue(FrameworkElement.WidthProperty);
            NpCoverImage.ClearValue(FrameworkElement.HeightProperty);
            NpCoverGlow1.Background = Brushes.Transparent;
            NpCoverGlow2.Background = Brushes.Transparent;
            NpBgGradient.Background = Brushes.Transparent;
            NpCoverShadow.Color = Colors.Black;
            NpCoverShadow.Opacity = 0.4;
            _npAlbumPrimary = default;
            _npAlbumSecondary = default;
            _npAlbumBackground = default;
        }

        private void NpCoverBorder_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            NpCoverClip.Rect = new Rect(0, 0, e.NewSize.Width, e.NewSize.Height);
        }

        /// <summary>
        /// Pre-computes the next track's album-art colors in the background so
        /// track switching feels instant. Cancelled automatically when the user
        /// skips before the preload completes.
        /// </summary>
        private void PreloadNextTrackColors()
        {
            _npPreloadCts?.Cancel();
            _npPreloadCts = new CancellationTokenSource();
            var ct = _npPreloadCts.Token;

            var nextTrack = GetNextTrackForGapless();
            if (nextTrack == null) return;
            string path = nextTrack.FilePath;

            // If already cached, nothing to do
            if (_npColorCache.ContainsKey(HashPath(path))) return;

            _ = Task.Run(async () =>
            {
                try
                {
                    byte[]? imageData = null;
                    await Task.Run(() =>
                    {
                        using var tagFile = TagLib.File.Create(path);
                        if (tagFile.Tag.Pictures.Length > 0)
                            imageData = tagFile.Tag.Pictures[0].Data.Data;
                    }, ct);

                    if (imageData == null || ct.IsCancellationRequested) return;

                    var colors = await Task.Run(() =>
                    {
                        var bmp = new BitmapImage();
                        bmp.BeginInit();
                        using (var ms = new MemoryStream(imageData))
                        {
                            bmp.StreamSource = ms;
                            bmp.CacheOption = BitmapCacheOption.OnLoad;
                            bmp.EndInit();
                        }
                        bmp.Freeze();

                        var converted = new FormatConvertedBitmap(bmp, PixelFormats.Bgra32, null, 0);
                        int stride = converted.PixelWidth * 4;
                        var pixels = new byte[stride * converted.PixelHeight];
                        converted.CopyPixels(pixels, stride, 0);

                        return AlbumColorExtractor.Extract(pixels, converted.PixelWidth, converted.PixelHeight, stride);
                    }, ct);

                    if (!ct.IsCancellationRequested)
                        StoreNpColorInCache(path, colors);
                }
                catch { /* preload is best-effort */ }
            });
        }

        private void StoreNpColorInCache(string filePath, AlbumColorExtractor.DominantColors colors)
        {
            if (!ThemeManager.NpColorCacheEnabled) return;
            string key = HashPath(filePath);
            if (_npColorCacheLru.Count >= NpColorCacheMaxEntries)
            {
                string? oldest = _npColorCacheLru.First?.Value;
                if (oldest != null) { _npColorCache.Remove(oldest); _npColorCacheLru.RemoveFirst(); }
            }
            _npColorCache[key] = colors;
            _npColorCacheLru.Remove(key);
            _npColorCacheLru.AddLast(key);
        }

        private static string HashPath(string path)
        {
            using var sha = SHA256.Create();
            var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(path.ToLowerInvariant()));
            return Convert.ToHexString(bytes);
        }

        private string NpColorCacheFilePath =>
            System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "AudioAuditor", "np_color_cache.json");

        private void LoadNpColorCacheFromDisk()
        {
            if (!ThemeManager.NpColorCachePersist) return;
            try
            {
                var path = NpColorCacheFilePath;
                if (!File.Exists(path)) return;
                var json = File.ReadAllText(path);
                var doc = System.Text.Json.JsonDocument.Parse(json);
                if (!doc.RootElement.TryGetProperty("entries", out var entries)) return;
                foreach (var entry in entries.EnumerateArray())
                {
                    if (!entry.TryGetProperty("key", out var keyEl)) continue;
                    if (!entry.TryGetProperty("primary", out var pri)) continue;
                    if (!entry.TryGetProperty("secondary", out var sec)) continue;
                    if (!entry.TryGetProperty("background", out var bg)) continue;
                    string key = keyEl.GetString() ?? "";
                    if (string.IsNullOrEmpty(key)) continue;
                    var colors = new AlbumColorExtractor.DominantColors(
                        new AlbumColorExtractor.Color((byte)pri.GetProperty("r").GetInt32(), (byte)pri.GetProperty("g").GetInt32(), (byte)pri.GetProperty("b").GetInt32()),
                        new AlbumColorExtractor.Color((byte)sec.GetProperty("r").GetInt32(), (byte)sec.GetProperty("g").GetInt32(), (byte)sec.GetProperty("b").GetInt32()),
                        new AlbumColorExtractor.Color(0, 0, 0),
                        new AlbumColorExtractor.Color((byte)bg.GetProperty("r").GetInt32(), (byte)bg.GetProperty("g").GetInt32(), (byte)bg.GetProperty("b").GetInt32()),
                        new AlbumColorExtractor.Color(0, 0, 0)
                    );
                    _npColorCache[key] = colors;
                    _npColorCacheLru.AddLast(key);
                    if (_npColorCacheLru.Count > NpColorCacheMaxEntries)
                    {
                        string? oldest = _npColorCacheLru.First?.Value;
                        if (oldest != null) { _npColorCache.Remove(oldest); _npColorCacheLru.RemoveFirst(); }
                    }
                }
            }
            catch { /* best effort */ }
        }

        private void SaveNpColorCacheToDisk()
        {
            if (!ThemeManager.NpColorCachePersist) return;
            try
            {
                var sb = new StringBuilder();
                sb.AppendLine("{\"entries\":[");
                bool first = true;
                foreach (var kvp in _npColorCache)
                {
                    if (!first) sb.AppendLine(",");
                    first = false;
                    var c = kvp.Value;
                    sb.Append($"{{\"key\":\"{kvp.Key}\",\"primary\":{{\"r\":{c.Primary.R},\"g\":{c.Primary.G},\"b\":{c.Primary.B}}},\"secondary\":{{\"r\":{c.Secondary.R},\"g\":{c.Secondary.G},\"b\":{c.Secondary.B}}},\"background\":{{\"r\":{c.Background.R},\"g\":{c.Background.G},\"b\":{c.Background.B}}}}}");
                }
                sb.AppendLine();
                sb.AppendLine("]}");
                var path = NpColorCacheFilePath;
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
                File.WriteAllText(path, sb.ToString());
            }
            catch { /* best effort */ }
        }

        private void ApplyNpExtractedColors(AlbumColorExtractor.DominantColors colors)
        {
            var primaryColor = System.Windows.Media.Color.FromRgb(
                colors.Primary.R, colors.Primary.G, colors.Primary.B);
            var secondaryColor = System.Windows.Media.Color.FromRgb(
                colors.Secondary.R, colors.Secondary.G, colors.Secondary.B);
            var backgroundColor = System.Windows.Media.Color.FromRgb(
                colors.Background.R, colors.Background.G, colors.Background.B);

            _npAlbumPrimary = primaryColor;
            _npAlbumSecondary = secondaryColor;
            _npAlbumBackground = backgroundColor;

            NpCoverGlow1.Background = new SolidColorBrush(primaryColor);
            NpCoverGlow2.Background = new SolidColorBrush(secondaryColor);

            NpCoverShadow.Color = primaryColor;
            NpCoverShadow.Opacity = 0.6;

            var bg1 = System.Windows.Media.Color.FromArgb(220,
                colors.Background.R, colors.Background.G, colors.Background.B);
            var bg2 = System.Windows.Media.Color.FromArgb(200,
                (byte)(colors.Background.R / 4),
                (byte)(colors.Background.G / 4),
                (byte)(colors.Background.B / 4));
            NpBgGradient.Background = new LinearGradientBrush(bg1, bg2, 45);

            NpApplyColorMatchMode();

            // Belt-and-suspenders: schedule a second re-apply at ContextIdle so any
            // resource writes that race with this one (e.g. LoadMainCoverColors finishing
            // in the same dispatch frame) get overridden by the album colors. The visible
            // screen wins shared resources — when on main, main runs last; when on NP, NP runs last.
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (_npVisible)
                {
                    if (ThemeManager.MainColorMatchEnabled && _mainAlbumPrimary != default)
                        ApplyMainColorMatch();
                    if (_npColorMatchEnabled && _npAlbumPrimary != default)
                        NpApplyColorMatchMode();
                }
                else
                {
                    if (_npColorMatchEnabled && _npAlbumPrimary != default)
                        NpApplyColorMatchMode();
                    if (ThemeManager.MainColorMatchEnabled && _mainAlbumPrimary != default)
                        ApplyMainColorMatch();
                }
            }), System.Windows.Threading.DispatcherPriority.ContextIdle);
        }

        private async Task NpApplyGlowAsync(byte[] imageData, string? cacheKey = null, int generation = -1)
        {
            try
            {
                var colors = await Task.Run(() =>
                {
                    var bmp = new BitmapImage();
                    bmp.BeginInit();
                    using (var ms = new MemoryStream(imageData))
                    {
                        bmp.StreamSource = ms;
                        bmp.CacheOption = BitmapCacheOption.OnLoad;
                        bmp.EndInit();
                    }
                    bmp.Freeze();

                    var converted = new FormatConvertedBitmap(bmp, PixelFormats.Bgra32, null, 0);
                    int stride = converted.PixelWidth * 4;
                    var pixels = new byte[stride * converted.PixelHeight];
                    converted.CopyPixels(pixels, stride, 0);

                    return AlbumColorExtractor.Extract(pixels, converted.PixelWidth, converted.PixelHeight, stride);
                });

                if (cacheKey != null)
                    StoreNpColorInCache(cacheKey, colors);

                Dispatcher.Invoke(() =>
                {
                    if (generation >= 0 && generation != _npColorGeneration) return;
                    ApplyNpExtractedColors(colors);
                });
            }
            catch
            {
                Dispatcher.Invoke(() =>
                {
                    if (generation >= 0 && generation != _npColorGeneration) return;
                    NpBgGradient.Background = Brushes.Transparent;
                });
            }
        }

        // ─── WPF Now Playing: Lyrics ───

        private async Task NpLoadLyricsAsync(string filePath, string? artist = null, string? title = null)
        {
            _npCurrentLyricIndex = -1;
            int version = _npLyricsVersion; // snapshot before await

            // Show searching status immediately
            var providerName = NpLyricProviders[_npProviderIndex].Name;
            NpShowLyricStatus($"Searching for lyrics ({providerName})...");

            LyricsResult result;
            try
            {
                result = await LyricService.GetLyricsAsync(
                    filePath, _npLyricProvider, artist, title);
            }
            catch
            {
                result = LyricService.GetLyrics(filePath, _npLyricProvider);
            }

            // If the track changed while we were fetching, discard stale results
            if (version != _npLyricsVersion) return;

            // Normalize whitespace in fetched lyrics to prevent garbled display
            if (result.HasLyrics)
            {
                var normalizedLines = result.Lines
                    .Select(l => new LyricLine(l.Time,
                        System.Text.RegularExpressions.Regex.Replace(l.Text, @"\s+", " ").Trim()))
                    .ToList();
                result = result with { Lines = normalizedLines };
            }

            _npCurrentLyrics = result;
            _npCurrentLyricIndex = -1;
            NpBuildLyricLines();
            _npLyricsNeedCatchUp = true;

            // Auto-save timed lyrics if enabled and no .lrc exists
            if (_npCurrentLyrics.IsTimed && ThemeManager.NpAutoSaveLyricsEnabled && _player?.CurrentFile != null)
            {
                string lrcPath = System.IO.Path.Combine(
                    System.IO.Path.GetDirectoryName(_player.CurrentFile)!,
                    System.IO.Path.GetFileNameWithoutExtension(_player.CurrentFile) + ".lrc");
                if (!File.Exists(lrcPath))
                {
                    try { NpSaveLyricsToLrc(_player.CurrentFile); }
                    catch { }
                }
            }

            Dispatcher.InvokeAsync(() => NpUpdateSaveLyricsButton());

            // Force immediate lyric sync — the song may already be well into playback
            // by the time the async lyrics load completes, so kick the highlight now
            // rather than waiting for the next timer tick + layout pass
            if (_npCurrentLyrics.IsTimed && _player != null)
            {
                _npCurrentLyricIndex = -1;
                // Dispatch at Render priority so the layout pass completes first
                Dispatcher.InvokeAsync(() =>
                {
                    if (version != _npLyricsVersion) return;
                    NpUpdateLyricHighlight(_player.CurrentPosition);
                }, System.Windows.Threading.DispatcherPriority.Render);
            }
        }

        private void NpShowLyricStatus(string message)
        {
            NpLyricsPanel.Children.Clear();
            _npLyricTextBlocks.Clear();
            var status = new TextBlock
            {
                Text = message,
                Foreground = (Brush)FindResource("TextDim"),
                FontSize = 15,
                FontStyle = FontStyles.Italic,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 20, 0, 0)
            };
            NpLyricsPanel.Children.Add(status);

            // Restore lyrics panel after 2 seconds
            var restoreTimer = new System.Windows.Threading.DispatcherTimer
                { Interval = TimeSpan.FromSeconds(2) };
            restoreTimer.Tick += (_, _) =>
            {
                restoreTimer.Stop();
                NpBuildLyricLines();
            };
            restoreTimer.Start();
        }

        private void NpBuildLyricLines()
        {
            NpLyricsPanel.Children.Clear();
            _npLyricTextBlocks.Clear();

            if (!_npCurrentLyrics.HasLyrics)
            {
                var providerName = NpLyricProviders[_npProviderIndex].Name;
                var noLyrics = new TextBlock
                {
                    Text = $"No lyrics found via {providerName}\nTry switching providers or drop a .lrc file",
                    Foreground = (Brush)FindResource("TextDim"),
                    FontSize = 15,
                    FontStyle = FontStyles.Italic,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 20, 0, 0)
                };
                NpLyricsPanel.Children.Add(noLyrics);
                return;
            }

            // Small top spacer only - lyrics start near the top aligned with album cover
            NpLyricsPanel.Children.Add(new Border { Height = 8 });

            for (int i = 0; i < _npCurrentLyrics.Lines.Count; i++)
            {
                var line = _npCurrentLyrics.Lines[i];

                // Main lyric line
                var tb = new TextBlock
                {
                    TextWrapping = TextWrapping.Wrap,
                    TextAlignment = TextAlignment.Left,
                    Margin = new Thickness(0, 6, 0, _npTranslateEnabled && _npTranslatedLines != null ? 0 : 6),
                    FontSize = _npLyricsSize > 0 ? _npLyricsSize : (WindowState == WindowState.Maximized ? 22 : 18),
                    FontFamily = new FontFamily("Segoe UI"),
                    Cursor = _npCurrentLyrics.IsTimed ? System.Windows.Input.Cursors.Hand : System.Windows.Input.Cursors.Arrow,
                    Foreground = new SolidColorBrush(
                        System.Windows.Media.Color.FromArgb(85, 255, 255, 255))
                };

                // Karaoke mode: build word-by-word Runs; otherwise plain text
                if (_npKaraokeEnabled && _npCurrentLyrics.IsTimed)
                {
                    // Split on whitespace and punctuation so words illuminate cleanly
                    var words = System.Text.RegularExpressions.Regex.Split(line.Text, @"\s+|[,.\-—!?;:\(\)\[\]""]+")
                        .Where(w => !string.IsNullOrEmpty(w))
                        .ToArray();
                    foreach (var word in words)
                    {
                        var run = new System.Windows.Documents.Run(word + " ")
                        {
                            Foreground = new SolidColorBrush(
                                System.Windows.Media.Color.FromArgb(85, 255, 255, 255))
                        };
                        tb.Inlines.Add(run);
                    }
                }
                else
                {
                    tb.Text = line.Text;
                }

                // Click to seek to this lyric's timestamp
                if (_npCurrentLyrics.IsTimed)
                {
                    int lineIndex = i;
                    var capturedLyrics = _npCurrentLyrics; // capture for closure safety
                    tb.MouseLeftButtonDown += (s, e) =>
                    {
                        try
                        {
                            if (capturedLyrics != _npCurrentLyrics) return; // stale lyrics
                            if (lineIndex >= capturedLyrics.Lines.Count || _player == null) return;
                            var seekTime = capturedLyrics.Lines[lineIndex].Time;
                            if (_player.TotalDuration.TotalSeconds <= 0) return;
                            _player.Seek(seekTime.TotalSeconds);
                            _lastSeekTime = DateTime.UtcNow;
                            _lastSeekTargetPosition = seekTime;

                            // Update NP seek slider without triggering seek feedback
                            if (NpSeekSlider != null)
                            {
                                NpSeekSlider.ValueChanged -= NpSeekSlider_ValueChanged;
                                NpSeekSlider.Value = seekTime.TotalSeconds;
                                NpSeekSlider.ValueChanged += NpSeekSlider_ValueChanged;
                            }

                            // Also update main seek slider
                            if (SeekSlider?.Maximum > 0)
                                SeekSlider.Value = seekTime.TotalSeconds / _player.TotalDuration.TotalSeconds * SeekSlider.Maximum;

                            // Resume playback if paused
                            if (!_player.IsPlaying && _player.IsPaused)
                                _player.Resume();

                            // Force immediate lyric highlight update
                            _npCurrentLyricIndex = -1;
                            NpUpdateLyricHighlight(seekTime);
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"Lyric seek error: {ex.Message}");
                        }
                    };

                    // Right-click context menu
                    var menu = new ContextMenu();
                    var saveItem = new MenuItem { Header = "Save Lyrics as .lrc" };
                    saveItem.Click += (_, _) =>
                    {
                        if (_player?.CurrentFile != null)
                        {
                            try
                            {
                                NpSaveLyricsToLrc(_player.CurrentFile);
                                NpShowLyricStatus("Lyrics saved to .lrc");
                            }
                            catch (Exception ex)
                            {
                                NpShowLyricStatus($"Save failed: {ex.Message}");
                            }
                        }
                    };
                    menu.Items.Add(saveItem);
                    tb.ContextMenu = menu;
                }

                _npLyricTextBlocks.Add(tb);
                NpLyricsPanel.Children.Add(tb);

                // Show translation below the original line
                if (_npTranslateEnabled && _npTranslatedLines != null && i < _npTranslatedLines.Count)
                {
                    var translated = _npTranslatedLines[i];
                    if (!string.IsNullOrWhiteSpace(translated) &&
                        !string.Equals(translated, line.Text, StringComparison.OrdinalIgnoreCase))
                    {
                        var transTb = new TextBlock
                        {
                            Text = translated,
                            TextWrapping = TextWrapping.Wrap,
                            TextAlignment = TextAlignment.Left,
                            Margin = new Thickness(0, 0, 0, 6),
                            FontSize = 14,
                            FontStyle = FontStyles.Italic,
                            FontFamily = new FontFamily("Segoe UI"),
                            Foreground = new SolidColorBrush(
                                System.Windows.Media.Color.FromArgb(140, 255, 255, 255)),
                            Tag = "translation" // mark for styling
                        };
                        NpLyricsPanel.Children.Add(transTb);
                    }
                }
            }

            NpLyricsPanel.Children.Add(new Border { Height = 200 });

            // If translate was just enabled, kick off translation
            if (_npTranslateEnabled && _npTranslatedLines == null)
                _ = NpTranslateLyricsAsync();
        }

        private void NpUpdateLyricHighlight(TimeSpan position)
        {
            var lyrics = _npCurrentLyrics; // capture reference to prevent mid-method replacement
            if (!lyrics.IsTimed || _npLyricTextBlocks.Count == 0) return;

            // Use custom lyrics size if set, otherwise window-state default
            bool fs = WindowState == WindowState.Maximized;
            double baseLyricSize = _npLyricsSize > 0 ? _npLyricsSize : (fs ? 22 : 18);

            // Add 120 ms lookahead to compensate for NAudio audio-buffer lag —
            // the reported position trails actual playback by one buffer length (~50–200 ms)
            var lookAhead = position + TimeSpan.FromMilliseconds(120);

            int newIdx = -1;
            for (int i = lyrics.Lines.Count - 1; i >= 0; i--)
            {
                if (lookAhead >= lyrics.Lines[i].Time)
                {
                    newIdx = i;
                    break;
                }
            }

            bool lineChanged = newIdx != _npCurrentLyricIndex;

            // In karaoke mode, update word progress on active line even without line change
            if (!lineChanged && _npKaraokeEnabled && newIdx >= 0 && newIdx < _npLyricTextBlocks.Count
                && _npLyricTextBlocks[newIdx] != null)
            {
                NpAnimateKaraokeWords(_npLyricTextBlocks[newIdx], newIdx, position);
                return;
            }

            if (!lineChanged) return;

            _npCurrentLyricIndex = newIdx;

            var duration = TimeSpan.FromMilliseconds(150);
            var ease = new QuadraticEase { EasingMode = EasingMode.EaseOut };

            for (int i = 0; i < _npLyricTextBlocks.Count; i++)
            {
                var tb = _npLyricTextBlocks[i];

                if (i == newIdx)
                {
                    // Active line: bright highlight
                    tb.FontWeight = FontWeights.SemiBold;
                    var sizeAnim = new DoubleAnimation(baseLyricSize, duration) { EasingFunction = ease };
                    tb.BeginAnimation(TextBlock.FontSizeProperty, sizeAnim);

                    if (_npKaraokeEnabled && tb.Inlines.Count > 0)
                    {
                        // Karaoke word-by-word: illuminate words progressively
                        NpAnimateKaraokeWords(tb, i, position);
                    }
                    else
                    {
                        var activeBrush = tb.Foreground as SolidColorBrush;
                        if (activeBrush == null || activeBrush.IsFrozen)
                        {
                            activeBrush = new SolidColorBrush(Colors.White);
                            tb.Foreground = activeBrush;
                        }
                        var activeAnim = new ColorAnimation(Colors.White, duration) { EasingFunction = ease };
                        activeBrush.BeginAnimation(SolidColorBrush.ColorProperty, activeAnim);
                    }
                }
                else
                {
                    // Non-active lines
                    System.Windows.Media.Color targetColor;
                    double targetSize;

                    if (i < newIdx)
                    {
                        targetColor = System.Windows.Media.Color.FromArgb(68, 255, 255, 255);
                        targetSize = baseLyricSize;
                    }
                    else
                    {
                        targetColor = System.Windows.Media.Color.FromArgb(85, 255, 255, 255);
                        targetSize = baseLyricSize;
                    }
                    tb.FontWeight = FontWeights.Normal;

                    if (_npKaraokeEnabled && tb.Inlines.Count > 0)
                    {
                        // Reset all word Runs to dim
                        foreach (var inline in tb.Inlines)
                        {
                            if (inline is System.Windows.Documents.Run run)
                            {
                                var brush = run.Foreground as SolidColorBrush;
                                if (brush == null || brush.IsFrozen)
                                {
                                    brush = new SolidColorBrush(targetColor);
                                    run.Foreground = brush;
                                }
                                brush.BeginAnimation(SolidColorBrush.ColorProperty,
                                    new ColorAnimation(targetColor, duration) { EasingFunction = ease });
                            }
                        }
                    }
                    else
                    {
                        var colorAnim = new ColorAnimation(targetColor, duration) { EasingFunction = ease };
                        var brush = tb.Foreground as SolidColorBrush;
                        if (brush == null || brush.IsFrozen)
                        {
                            brush = new SolidColorBrush(brush?.Color ?? Colors.White);
                            tb.Foreground = brush;
                        }
                        brush.BeginAnimation(SolidColorBrush.ColorProperty, colorAnim);
                    }

                    var sizeAnim = new DoubleAnimation(targetSize, duration) { EasingFunction = ease };
                    tb.BeginAnimation(TextBlock.FontSizeProperty, sizeAnim);
                }
            }

            // Smooth auto-scroll — position active line at 25% from top
            if (newIdx >= 0 && newIdx < _npLyricTextBlocks.Count)
            {
                var target = _npLyricTextBlocks[newIdx];
                try
                {
                    var transform = target.TransformToAncestor(NpLyricsPanel);
                    var point = transform.Transform(new System.Windows.Point(0, 0));
                    double scrollerHeight = NpLyricsScroller.ViewportHeight;
                    double targetY = point.Y - scrollerHeight * 0.25;
                    if (targetY < 0) targetY = 0;
                    NpAnimateScroll(NpLyricsScroller, targetY, 200);
                }
                catch { /* element not yet in visual tree */ }
            }
        }

        /// <summary>Animate karaoke word-by-word illumination on the active line.</summary>
        private void NpAnimateKaraokeWords(TextBlock tb, int lineIdx, TimeSpan position)
        {
            var runs = tb.Inlines.OfType<System.Windows.Documents.Run>().ToList();
            if (runs.Count == 0) return;

            // Capture reference to protect against concurrent lyrics replacement
            var lyrics = _npCurrentLyrics;
            if (lineIdx >= lyrics.Lines.Count) return;

            // Calculate how far through this line we are (0.0 to 1.0)
            var lineStart = lyrics.Lines[lineIdx].Time;
            var lineEnd = lineIdx + 1 < lyrics.Lines.Count
                ? lyrics.Lines[lineIdx + 1].Time
                : lineStart + TimeSpan.FromSeconds(4); // default 4s for last line

            double lineDuration = (lineEnd - lineStart).TotalMilliseconds;
            if (lineDuration <= 0) lineDuration = 4000;
            double elapsed = (position - lineStart).TotalMilliseconds;
            double progress = Math.Clamp(elapsed / lineDuration, 0, 1);

            // Build character-count-proportional word start positions so longer words get more dwell time
            var wordLengths = runs.Select(r => Math.Max(1, r.Text.Length)).ToArray();
            int totalChars = wordLengths.Sum();
            double[] wordStartFrac = new double[runs.Count];
            double accumulated = 0;
            for (int i = 0; i < runs.Count; i++)
            {
                wordStartFrac[i] = accumulated / totalChars;
                accumulated += wordLengths[i];
            }

            var ease = new QuadraticEase { EasingMode = EasingMode.EaseOut };
            var dur = TimeSpan.FromMilliseconds(200);
            double transitionWidth = Math.Max(0.05, 1.0 / runs.Count); // sharp zone spans ~1 word

            for (int w = 0; w < runs.Count; w++)
            {
                // Word center is halfway between its start and the next word's start
                double wordEnd = w + 1 < runs.Count ? wordStartFrac[w + 1] : 1.0;
                double wordCenter = (wordStartFrac[w] + wordEnd) / 2.0;
                // Compute smooth illumination factor (0 = dim, 1 = fully lit)
                double factor = Math.Clamp((progress - wordCenter + transitionWidth / 2) / transitionWidth, 0, 1);

                // Interpolate between dim and fully lit
                byte a = (byte)(90 + (255 - 90) * factor);
                byte rgb = (byte)(180 + (255 - 180) * factor); // slight warmth when dim
                var targetColor = System.Windows.Media.Color.FromArgb(a, 255, rgb, rgb);
                if (factor >= 0.85) targetColor = Colors.White; // snap to pure white when mostly lit

                var brush = runs[w].Foreground as SolidColorBrush;
                if (brush == null || brush.IsFrozen)
                {
                    brush = new SolidColorBrush(targetColor);
                    runs[w].Foreground = brush;
                }
                else
                {
                    brush.BeginAnimation(SolidColorBrush.ColorProperty,
                        new ColorAnimation(targetColor, dur) { EasingFunction = ease });
                }
            }
        }

        /// <summary>Smoothly animates a ScrollViewer to a target vertical offset.</summary>
        private void NpAnimateScroll(ScrollViewer viewer, double targetOffset, double durationMs)
        {
            double current = viewer.VerticalOffset;
            double diff = targetOffset - current;
            if (Math.Abs(diff) < 1) return;

            int steps = (int)(durationMs / 16); // ~60fps
            if (steps < 1) steps = 1;
            int step = 0;
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
            timer.Tick += (_, _) =>
            {
                step++;
                double t = (double)step / steps;
                // ease-out quad
                t = 1 - (1 - t) * (1 - t);
                viewer.ScrollToVerticalOffset(current + diff * t);
                if (step >= steps)
                    timer.Stop();
            };
            timer.Start();
        }

        // ─── NP Timer ───

        private void NpUpdateTimer_Tick(object? sender, EventArgs e)
        {
            if (_player == null) return;

            var pos = _player.CurrentPosition;
            var total = _player.TotalDuration;

            NpTimeElapsed.Text = NpFormatTime(pos);
            NpTimeTotal.Text = NpFormatTime(total);

            // Don't update slider while user is dragging, and respect seek cooldown
            if (total.TotalSeconds > 0 && !_npIsSeeking &&
                (DateTime.UtcNow - _lastSeekTime).TotalMilliseconds > 500)
            {
                NpSeekSlider.Maximum = total.TotalSeconds;
                NpSeekSlider.ValueChanged -= NpSeekSlider_ValueChanged;
                NpSeekSlider.Value = pos.TotalSeconds;
                NpSeekSlider.ValueChanged += NpSeekSlider_ValueChanged;
            }
            else if (total.TotalSeconds > 0)
            {
                NpSeekSlider.Maximum = total.TotalSeconds;
            }

            NpUpdatePlayState();

            // During seek cooldown, use the last seek target for lyrics to avoid NAudio position lag
            bool lyricSeekCooldown = (DateTime.UtcNow - _lastSeekTime).TotalMilliseconds < 500;
            var lyricPos = lyricSeekCooldown ? _lastSeekTargetPosition : pos;
            NpUpdateLyricHighlight(lyricPos);

            if (_npLyricsNeedCatchUp)
            {
                _npLyricsNeedCatchUp = false;
                _npCurrentLyricIndex = -1;
                NpUpdateLyricHighlight(lyricPos);
            }
        }

        private void NpUpdatePlayState()
        {
            bool playing = _player?.IsPlaying == true;
            NpPlayPath.Visibility = playing ? Visibility.Collapsed : Visibility.Visible;
            NpPausePath.Visibility = playing ? Visibility.Visible : Visibility.Collapsed;

            // Ensure play/pause buttons have correct color (respects color match)
            if (_npColorMatchEnabled && _npAlbumSecondary != default)
            {
                var controlTint = new SolidColorBrush(System.Windows.Media.Color.FromRgb(
                    (byte)Math.Min(255, _npAlbumSecondary.R + 100),
                    (byte)Math.Min(255, _npAlbumSecondary.G + 100),
                    (byte)Math.Min(255, _npAlbumSecondary.B + 100)));
                NpPlayPath.Fill = controlTint;
                NpPausePath.Fill = controlTint;
            }

            // Start NP visualizer when playing (leave frozen on pause, don't tear down)
            if (_npVisualizerEnabled && _npVisible && playing)
            {
                NpStartVisualizer();
            }
        }

        private static string NpFormatTime(TimeSpan ts) =>
            ts.TotalHours >= 1 ? ts.ToString(@"h\:mm\:ss") : ts.ToString(@"m\:ss");

        // ─── NP Control Events ───

        private void NpSaveLyricsToLrc(string audioFilePath)
        {
            if (!_npCurrentLyrics.HasLyrics) return;
            string lrcPath = System.IO.Path.Combine(
                System.IO.Path.GetDirectoryName(audioFilePath)!,
                System.IO.Path.GetFileNameWithoutExtension(audioFilePath) + ".lrc");

            var lines = new System.Collections.Generic.List<string>();
            if (!string.IsNullOrEmpty(_npCurrentLyrics.Title))
                lines.Add($"[ti:{_npCurrentLyrics.Title}]");
            if (!string.IsNullOrEmpty(_npCurrentLyrics.Artist))
                lines.Add($"[ar:{_npCurrentLyrics.Artist}]");
            if (!string.IsNullOrEmpty(_npCurrentLyrics.Album))
                lines.Add($"[al:{_npCurrentLyrics.Album}]");

            if (_npCurrentLyrics.IsTimed)
            {
                foreach (var line in _npCurrentLyrics.Lines)
                {
                    int min = (int)line.Time.TotalMinutes;
                    int sec = line.Time.Seconds;
                    int cs = (int)(line.Time.Milliseconds / 10.0);
                    string normalized = System.Text.RegularExpressions.Regex.Replace(line.Text, @"\s+", " ").Trim();
                    lines.Add($"[{min:D2}:{sec:D2}.{cs:D2}]{normalized}");
                }
            }
            else
            {
                foreach (var line in _npCurrentLyrics.Lines)
                    lines.Add(System.Text.RegularExpressions.Regex.Replace(line.Text, @"\s+", " ").Trim());
            }

            File.WriteAllLines(lrcPath, lines);
        }

        private void NpSaveLyrics_Click(object sender, RoutedEventArgs e)
        {
            if (NpSaveLyricsBtn?.ContextMenu != null)
                NpSaveLyricsBtn.ContextMenu.IsOpen = true;
        }

        private void NpSaveLyricsMenu_Opened(object sender, RoutedEventArgs e)
        {
            bool hasLyrics = _player.CurrentFile != null && _npCurrentLyrics.HasLyrics;
            if (NpSaveLyricsNowItem != null)
                NpSaveLyricsNowItem.IsEnabled = hasLyrics;
            if (NpAutoSaveLyricsToggleItem != null)
                NpAutoSaveLyricsToggleItem.IsChecked = ThemeManager.NpAutoSaveLyricsEnabled;
            if (NpSaveAllLyricsItem != null)
                NpSaveAllLyricsItem.IsEnabled = _player.CurrentFile != null && _files.Count > 0;
        }

        private void NpSaveLyricsNow_Click(object sender, RoutedEventArgs e)
        {
            if (_player.CurrentFile == null || !_npCurrentLyrics.HasLyrics) return;
            try
            {
                NpSaveLyricsToLrc(_player.CurrentFile);
                NpShowLyricStatus("Lyrics saved to .lrc");
            }
            catch (Exception ex)
            {
                NpShowLyricStatus($"Save failed: {ex.Message}");
            }
        }

        private void NpAutoSaveLyricsToggle_Click(object sender, RoutedEventArgs e)
        {
            ThemeManager.NpAutoSaveLyricsEnabled = !ThemeManager.NpAutoSaveLyricsEnabled;
            ThemeManager.SavePlayOptions();
            NpUpdateSaveLyricsButton();
        }

        private async void NpSaveAllLyricsNow_Click(object sender, RoutedEventArgs e)
        {
            if (_player.CurrentFile == null || _files.Count == 0) return;
            string currentFolder = System.IO.Path.GetDirectoryName(_player.CurrentFile)!;
            var folderFiles = _files.Where(f =>
                string.Equals(System.IO.Path.GetDirectoryName(f.FilePath), currentFolder, StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (folderFiles.Count == 0) return;

            NpShowLyricStatus($"Saving lyrics for {folderFiles.Count} songs...");
            int savedCount = 0;

            await Task.Run(() =>
            {
                foreach (var file in folderFiles)
                {
                    string lrcPath = System.IO.Path.Combine(
                        currentFolder,
                        System.IO.Path.GetFileNameWithoutExtension(file.FilePath) + ".lrc");
                    if (File.Exists(lrcPath)) continue;

                    try
                    {
                        var lyrics = LyricService.GetLyrics(file.FilePath, _npLyricProvider);
                        if (lyrics.HasLyrics)
                        {
                            var lines = new System.Collections.Generic.List<string>();
                            if (!string.IsNullOrEmpty(lyrics.Title))
                                lines.Add($"[ti:{lyrics.Title}]");
                            if (!string.IsNullOrEmpty(lyrics.Artist))
                                lines.Add($"[ar:{lyrics.Artist}]");
                            if (!string.IsNullOrEmpty(lyrics.Album))
                                lines.Add($"[al:{lyrics.Album}]");

                            if (lyrics.IsTimed)
                            {
                                foreach (var line in lyrics.Lines)
                                {
                                    int min = (int)line.Time.TotalMinutes;
                                    int sec = line.Time.Seconds;
                                    int cs = (int)(line.Time.Milliseconds / 10.0);
                                    string normalized = System.Text.RegularExpressions.Regex.Replace(line.Text, @"\s+", " ").Trim();
                                    lines.Add($"[{min:D2}:{sec:D2}.{cs:D2}]{normalized}");
                                }
                            }
                            else
                            {
                                foreach (var line in lyrics.Lines)
                                    lines.Add(System.Text.RegularExpressions.Regex.Replace(line.Text, @"\s+", " ").Trim());
                            }

                            File.WriteAllLines(lrcPath, lines);
                            savedCount++;
                        }
                    }
                    catch { /* best-effort per file */ }
                }
            });

            NpShowLyricStatus($"Saved {savedCount} lyric files");
        }

        private void NpUpdateSaveLyricsButton()
        {
            if (NpSaveLyricsBtn == null || NpSaveLyricsIcon == null) return;
            bool hasLyrics = _player?.CurrentFile != null && _npCurrentLyrics.HasLyrics;
            NpSaveLyricsBtn.IsEnabled = hasLyrics;
            if (!hasLyrics)
            {
                NpSaveLyricsIcon.Stroke = (Brush)FindResource("TextMuted");
                return;
            }
            if (ThemeManager.NpAutoSaveLyricsEnabled)
            {
                NpSaveLyricsIcon.Stroke = (Brush)FindResource("AccentColor");
            }
            else
            {
                NpSaveLyricsIcon.Stroke = (Brush)FindResource("TextMuted");
            }
        }

        private void NpPlayPause_Click(object sender, RoutedEventArgs e)
        {
            if (_player == null) return;
            if (_player.IsPlaying) _player.Pause(); else _player.Resume();
            NpUpdatePlayState();
        }

        private void NpPrev_Click(object sender, RoutedEventArgs e) =>
            PrevTrack_Click(sender, e);

        private void NpNext_Click(object sender, RoutedEventArgs e) =>
            NextTrack_Click(sender, e);

        private void NpShuffle_Click(object sender, RoutedEventArgs e)
        {
            _shuffleMode = !_shuffleMode;
            UpdateShuffleUI();
            NpUpdateShuffleIcon();
        }

        private void NpLoop_Click(object sender, RoutedEventArgs e)
        {
            CycleLoopMode();
        }

        private void NpUpdateShuffleIcon()
        {
            if (NpShuffleIcon == null) return;
            var activeColor = NpGetIconBrush(true);
            var inactiveColor = NpGetIconBrush(false);
            NpShuffleIcon.Stroke = _shuffleMode ? activeColor : inactiveColor;
            NpShuffleBtn.ToolTip = _shuffleMode ? "Shuffle: ON" : "Shuffle: OFF";
            NpSetToggleBg(NpShuffleBtn, _shuffleMode);
        }

        private void NpLyricSource_Click(object sender, RoutedEventArgs e)
        {
            _npLyricsVersion++; // invalidate any in-flight lyrics fetch from previous provider
            _npProviderIndex = (_npProviderIndex + 1) % NpLyricProviders.Length;
            _npLyricProvider = NpLyricProviders[_npProviderIndex].Provider;
            NpProviderBtn.ToolTip = $"Provider: {NpLyricProviders[_npProviderIndex].Name}";

            if (_player.CurrentFile != null)
            {
                var currentFile = _files.FirstOrDefault(f =>
                    string.Equals(f.FilePath, _player.CurrentFile, StringComparison.OrdinalIgnoreCase));
                if (currentFile != null)
                    _ = NpLoadLyricsAsync(currentFile.FilePath, currentFile.Artist, currentFile.Title);
            }
        }

        private void NpVolumeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_player != null)
            {
                _player.Volume = (float)(NpVolumeSlider.Value / 100.0);
                VolumeSlider.Value = NpVolumeSlider.Value;
            }
            NpUpdateVolumeIcon();
        }

        private void NpVolumeIcon_Click(object sender, MouseButtonEventArgs e)
        {
            if (!_isMuted)
            {
                _preMuteVolume = NpVolumeSlider.Value;
                NpVolumeSlider.Value = 0;
                VolumeSlider.Value = 0;
                _isMuted = true;
            }
            else
            {
                NpVolumeSlider.Value = _preMuteVolume;
                VolumeSlider.Value = _preMuteVolume;
                _isMuted = false;
            }
            NpUpdateVolumeIcon();
        }

        private void NpUpdateVolumeIcon()
        {
            double vol = NpVolumeSlider.Value;
            string pathData;
            if (vol <= 0 || _isMuted)
                pathData = "M 2,5 L 5,5 L 9,2 L 9,14 L 5,11 L 2,11 Z M 12,5 L 16,11 M 16,5 L 12,11"; // mute X
            else if (vol <= 33)
                pathData = "M 2,5 L 5,5 L 9,2 L 9,14 L 5,11 L 2,11 Z"; // speaker only
            else if (vol <= 66)
                pathData = "M 2,5 L 5,5 L 9,2 L 9,14 L 5,11 L 2,11 Z M 11,5 Q 13,8 11,11"; // one wave
            else
                pathData = "M 2,5 L 5,5 L 9,2 L 9,14 L 5,11 L 2,11 Z M 11,5 Q 13,8 11,11 M 13,3 Q 16,8 13,13"; // two waves
            try { NpVolumeIconPath.Data = Geometry.Parse(pathData); } catch { }
        }

        // ─── NP Visualizer Resize ───

        private void NpVizResize_MouseDown(object sender, MouseButtonEventArgs e)
        {
            _npVizResizing = true;
            _npVizResizeStartY = e.GetPosition(this).Y;
            _npVizResizeStartH = _npVizBarHeight;
            ((UIElement)sender).CaptureMouse();
            e.Handled = true;
        }

        private void NpVizResize_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
        {
            if (!_npVizResizing) return;
            double deltaY = _npVizResizeStartY - e.GetPosition(this).Y;
            double newHeight = Math.Clamp(_npVizResizeStartH + deltaY, 40, 400);
            _npVizBarHeight = newHeight;
            if (_npVizPlacement == 0)
                NpVizBar.Height = newHeight;
            else
                NpUnderCoverVizRow.Height = new GridLength(newHeight);
            e.Handled = true;
        }

        private void NpVizResize_MouseUp(object sender, MouseButtonEventArgs e)
        {
            _npVizResizing = false;
            ((UIElement)sender).ReleaseMouseCapture();
            e.Handled = true;
        }

        private void NpSeekSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            // During drag, only update visual position — actual seek happens on release
        }

        private void NpSeekSlider_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            _npIsSeeking = true;
        }

        private void NpSeekSlider_MouseUp(object sender, MouseButtonEventArgs e)
        {
            // If thumb was being dragged, DragCompleted handles the seek — skip here
            var thumb = FindVisualChild<System.Windows.Controls.Primitives.Thumb>(NpSeekSlider);
            if (thumb != null && thumb.IsDragging)
            {
                return;
            }

            if (_player != null && _player.TotalDuration.TotalSeconds > 0 && NpSeekSlider.Maximum > 0)
            {
                _player.Seek(NpSeekSlider.Value);
                _lastSeekTime = DateTime.UtcNow;
                _lastSeekTargetPosition = TimeSpan.FromSeconds(NpSeekSlider.Value);

                // Sync main slider
                SeekSlider.Value = NpSeekSlider.Value / NpSeekSlider.Maximum * SeekSlider.Maximum;

                // Force immediate lyric re-sync to seek target
                _npCurrentLyricIndex = -1;
                NpUpdateLyricHighlight(_lastSeekTargetPosition);
            }
            _npIsSeeking = false;
        }

        private void NpSeekSlider_DragCompleted(object sender, DragCompletedEventArgs e)
        {
            if (_player != null && _player.TotalDuration.TotalSeconds > 0 && NpSeekSlider.Maximum > 0)
            {
                _player.Seek(NpSeekSlider.Value);
                _lastSeekTime = DateTime.UtcNow;
                _lastSeekTargetPosition = TimeSpan.FromSeconds(NpSeekSlider.Value);

                // Sync main slider
                SeekSlider.Value = NpSeekSlider.Value / NpSeekSlider.Maximum * SeekSlider.Maximum;

                // Force immediate lyric re-sync to seek target
                _npCurrentLyricIndex = -1;
                NpUpdateLyricHighlight(_lastSeekTargetPosition);
            }
            _npIsSeeking = false;
        }

        private void NpSeekSlider_MouseMove(object sender, MouseEventArgs e)
        {
            UpdateSeekTooltip(NpSeekSlider, e);
        }

        private void NpBack_Click(object sender, RoutedEventArgs e) => ToggleNowPlaying(false);

        // ─── NP Queue ───

        private void NpQueue_Click(object sender, RoutedEventArgs e)
        {
            var queueWindow = new QueueWindow(_queue) { Owner = this, UpNext = GetUpNextTracks() };
            queueWindow.ShowDialog();
            NpUpdateQueuePopup();
        }

        private void NpUpdateQueuePopup()
        {
            NpQueuePopupContent.Children.Clear();

            var upcoming = new List<(int idx, string text)>();

            // 1. Queue items
            for (int i = 0; i < Math.Min(_queue.Count, 5); i++)
            {
                var q = _queue[i];
                string title = !string.IsNullOrWhiteSpace(q.Title) ? q.Title : q.FileName ?? "Unknown";
                string artist = !string.IsNullOrWhiteSpace(q.Artist) ? q.Artist : "";
                upcoming.Add((i + 1, string.IsNullOrEmpty(artist) ? title : $"{title} — {artist}"));
            }

            // 2. If queue is empty, show next track from shuffle/sequential
            if (upcoming.Count == 0 && _player.CurrentFile != null)
            {
                var current = _files.FirstOrDefault(f =>
                    string.Equals(f.FilePath, _player.CurrentFile, StringComparison.OrdinalIgnoreCase));
                if (current != null)
                {
                    int currentIdx = _files.IndexOf(current);
                    AudioFileInfo? next = null;
                    if (_shuffleMode && _shuffleDeck.Count > 0)
                    {
                        // Find current in shuffle deck and show next
                        int deckIdx = _shuffleDeck.FindIndex(f => f.FilePath == current.FilePath);
                        if (deckIdx >= 0 && deckIdx + 1 < _shuffleDeck.Count)
                            next = _shuffleDeck[deckIdx + 1];
                    }
                    else if (currentIdx >= 0 && currentIdx + 1 < _files.Count)
                    {
                        next = _files[currentIdx + 1];
                    }

                    if (next != null)
                    {
                        string title = !string.IsNullOrWhiteSpace(next.Title) ? next.Title : next.FileName ?? "Unknown";
                        string artist = !string.IsNullOrWhiteSpace(next.Artist) ? next.Artist : "";
                        upcoming.Add((1, string.IsNullOrEmpty(artist) ? title : $"{title} — {artist}"));
                    }
                }
            }

            if (upcoming.Count == 0)
            {
                NpQueuePopupContent.Children.Add(new TextBlock
                {
                    Text = "No upcoming tracks.",
                    FontSize = 12,
                    FontFamily = new FontFamily("Segoe UI"),
                    Foreground = (Brush)FindResource("TextMuted"),
                    Margin = new Thickness(0, 4, 0, 0)
                });
                return;
            }

            foreach (var (idx, text) in upcoming)
            {
                var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 3, 0, 3) };
                row.Children.Add(new TextBlock
                {
                    Text = $"{idx}.",
                    FontSize = 11,
                    FontWeight = FontWeights.SemiBold,
                    FontFamily = new FontFamily("Segoe UI"),
                    Foreground = (Brush)FindResource("AccentColor"),
                    Margin = new Thickness(0, 0, 8, 0),
                    Width = 18,
                    TextAlignment = TextAlignment.Right
                });
                row.Children.Add(new TextBlock
                {
                    Text = text,
                    FontSize = 12,
                    FontFamily = new FontFamily("Segoe UI"),
                    Foreground = (Brush)FindResource("TextPrimary"),
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    MaxWidth = 260
                });
                NpQueuePopupContent.Children.Add(row);
            }
        }

        // ─── NP Auto-Play Toggle ───

        private void NpAutoPlay_Click(object sender, RoutedEventArgs e)
        {
            ThemeManager.AutoPlayNext = !ThemeManager.AutoPlayNext;
            ThemeManager.SavePlayOptions();
            NpUpdateAutoPlayIcon();
        }

        private void NpUpdateAutoPlayIcon()
        {
            var active = NpGetIconBrush(true);
            var inactive = NpGetIconBrush(false);
            var brush = ThemeManager.AutoPlayNext ? active : inactive;
            NpAutoPlayIcon.Fill = brush;
            NpAutoPlayIcon.Stroke = brush;
            NpAutoPlayBtn.ToolTip = ThemeManager.AutoPlayNext ? "Auto-play: ON" : "Auto-play: OFF";
            NpSetToggleBg(NpAutoPlayBtn, ThemeManager.AutoPlayNext);
        }

        // ─── NP Visualizer Toggle ───

        private void NpVisualizerToggle_Click(object sender, RoutedEventArgs e)
        {
            _npVisualizerEnabled = !_npVisualizerEnabled;
            NpApplyVizPlacement();
            NpUpdateVisualizerIcon();

            if (_npVisualizerEnabled && _player.IsPlaying)
                NpStartVisualizer();
            else
                NpStopVisualizer();

            // Re-scale cover/text proportions so a single slider setting looks consistent in both modes
            NpApplyFullscreenScaling(WindowState == WindowState.Maximized);

            NpSavePreferences();
        }

        private void NpUpdateVisualizerIcon()
        {
            var active = NpGetIconBrush(true);
            var inactive = NpGetIconBrush(false);
            NpVisualizerIcon.Stroke = _npVisualizerEnabled ? active : inactive;
            NpVisualizerBtn.ToolTip = _npVisualizerEnabled ? "Visualizer: ON" : "Visualizer: OFF";
            NpSetToggleBg(NpVisualizerBtn, _npVisualizerEnabled);
        }

        private void NpUpdateVizPlacementIcon()
        {
            NpVizPlacementIcon.Stroke = NpGetIconBrush(false);
        }

        private bool _npVizRedirected; // true when NP owns the visualizer pipeline

        private void NpStartVisualizer()
        {
            if (_npVizRedirected) return; // already redirected
            _npVizRedirected = true;

            // Clear cached elements so they rebuild on the active canvas via VizTarget
            ClearVisualizerCaches();
            NpVisualizerCanvas.Children.Clear();
            NpUnderCoverVizCanvas.Children.Clear();
            if (!_visualizerActive)
            {
                _mainVizWasActive = false;
                StartVisualizer();
            }
            else
            {
                _mainVizWasActive = true;
                // Clear main canvas elements since VizTarget now points to NP canvas
                VisualizerCanvas.Children.Clear();
            }
        }

        private void NpStopVisualizer()
        {
            if (!_npVizRedirected) return;
            _npVizRedirected = false;

            ClearVisualizerCaches();
            NpVisualizerCanvas.Children.Clear();
            NpUnderCoverVizCanvas.Children.Clear();
            if (!_mainVizWasActive)
            {
                // Main visualizer was not running before NP took over, stop it
                StopVisualizer();
            }
            else if (!_npVisible)
            {
                // NP is closing — restore main visualizer
                VisualizerCanvas.Children.Clear();
            }
            else
            {
                // NP still visible but NP viz toggled off — keep main viz paused
                StopVisualizer();
                // Keep _mainVizWasActive true so it restores when NP closes
            }
        }

        private void ClearVisualizerCaches()
        {
            _vizBars = null;
            _vizMirrorBars = null;
            _particles = null;
            _particleElements = null;
            _circleElements = null;
            _scopeLine = null;
           
            _vuBlocks = null;
        }

        // ─── NP Visualizer Style Picker ───

        private void NpVisualizerStyle_Click(object sender, RoutedEventArgs e)
        {
            // Toggle: if already open, close it
            if (NpVizStylePopup.IsOpen)
            {
                NpVizStylePopup.IsOpen = false;
                return;
            }

            NpVizStyleMenu.Children.Clear();
            for (int i = 0; i < _vizStyleNames.Length; i++)
            {
                int idx = i;
                var tb = new TextBlock
                {
                    Text = (_npVisualizerStyle == i ? "● " : "   ") + _vizStyleNames[i],
                    FontSize = 12,
                    FontFamily = new System.Windows.Media.FontFamily("Segoe UI"),
                    Foreground = (Brush)FindResource(_npVisualizerStyle == i ? "TextPrimary" : "TextSecondary"),
                    Padding = new Thickness(10, 5, 10, 5),
                    Cursor = System.Windows.Input.Cursors.Hand
                };
                tb.MouseLeftButtonUp += (s, ev) =>
                {
                    NpApplyVisualizerStyle(idx);
                    NpVizStylePopup.IsOpen = false;
                };
                NpVizStyleMenu.Children.Add(tb);
            }
            NpVizStylePopup.IsOpen = true;
        }

        private void NpApplyVisualizerStyle(int style)
        {
            _npVisualizerStyle = style;
            // Apply the style to the shared visualizer renderer
            _visualizerStyle = style;
            ThemeManager.VisualizerStyle = style;
            UpdateVisualizerStyleText();
            ClearVisualizerCaches();
            NpVisualizerCanvas.Children.Clear();
            NpUnderCoverVizCanvas.Children.Clear();
            VisualizerCanvas.Children.Clear();
            NpSavePreferences();
        }

        // ─── NP Color Match Toggle ───

        private void NpColorMatch_Click(object sender, RoutedEventArgs e)
        {
            _npColorMatchEnabled = !_npColorMatchEnabled;
            NpUpdateColorMatchIcon();
            NpApplyColorMatchMode();
            NpSavePreferences();
        }

        private void NpUpdateColorMatchIcon()
        {
            var active = NpGetIconBrush(true);
            var inactive = NpGetIconBrush(false);
            NpColorMatchIcon.Stroke = _npColorMatchEnabled ? active : inactive;
            NpColorMatchFill.Fill = _npColorMatchEnabled ? active : inactive;
            NpColorMatchBtn.ToolTip = _npColorMatchEnabled ? "Color match: ON" : "Color match: OFF";
            NpSetToggleBg(NpColorMatchBtn, _npColorMatchEnabled);
        }

        /// <summary>
        /// Returns a brush for NP button icons that respects color-match mode.
        /// When color match is ON, returns album-tinted colors instead of theme defaults.
        /// </summary>
        private Brush NpGetIconBrush(bool active)
        {
            if (_npColorMatchEnabled && _npAlbumPrimary != default)
            {
                // Active: white so it pops regardless of album color.
                // Inactive: album color + small boost, clearly dimmer than active.
                if (active)
                    return new SolidColorBrush(Colors.White);
                else
                    return new SolidColorBrush(System.Windows.Media.Color.FromRgb(
                        (byte)Math.Min(255, _npAlbumPrimary.R + 60),
                        (byte)Math.Min(255, _npAlbumPrimary.G + 60),
                        (byte)Math.Min(255, _npAlbumPrimary.B + 60)));
            }
            return (Brush)FindResource(active ? "TextPrimary" : "TextMuted");
        }

        // Sets the background of a toggle button to a dim white pill when active, transparent when inactive.
        private static void NpSetToggleBg(System.Windows.Controls.Button btn, bool active)
        {
            btn.Background = active
                ? new SolidColorBrush(System.Windows.Media.Color.FromArgb(0x28, 255, 255, 255))
                : System.Windows.Media.Brushes.Transparent;
        }

        private System.Windows.Media.Color EnsureMinLuminance(System.Windows.Media.Color c, byte minLum)
        {
            double lum = 0.299 * c.R + 0.587 * c.G + 0.114 * c.B;
            if (lum >= minLum) return c;
            double factor = minLum / Math.Max(lum, 1);
            return System.Windows.Media.Color.FromRgb(
                (byte)Math.Min(255, (int)(c.R * factor)),
                (byte)Math.Min(255, (int)(c.G * factor)),
                (byte)Math.Min(255, (int)(c.B * factor)));
        }

        private void NpApplyColorMatchMode()
        {
            if (_npColorMatchEnabled && _npAlbumPrimary != default)
            {
                // Background: stronger gradient, minimal dark overlay
                NpBgGradient.Opacity = 1.0;
                NpDarkOverlay.Background = new SolidColorBrush(
                    System.Windows.Media.Color.FromArgb(20, 0, 0, 0));

                // Bottom bar: deeply tinted
                var barBg = System.Windows.Media.Color.FromArgb(200,
                    (byte)(_npAlbumBackground.R / 3),
                    (byte)(_npAlbumBackground.G / 3),
                    (byte)(_npAlbumBackground.B / 3));
                NpBottomBar.Background = new SolidColorBrush(barBg);
                NpBottomBar.BorderBrush = new SolidColorBrush(
                    System.Windows.Media.Color.FromArgb(80,
                        _npAlbumPrimary.R, _npAlbumPrimary.G, _npAlbumPrimary.B));

                // Title highlight with brightened primary
                var bright = System.Windows.Media.Color.FromRgb(
                    (byte)Math.Min(255, _npAlbumPrimary.R + 100),
                    (byte)Math.Min(255, _npAlbumPrimary.G + 100),
                    (byte)Math.Min(255, _npAlbumPrimary.B + 100));
                NpSongTitle.Foreground = new SolidColorBrush(bright);
                NpBigTitle.Foreground = new SolidColorBrush(bright);

                // Artist text with lighter secondary
                NpSongArtist.Foreground = new SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(
                        (byte)Math.Min(255, _npAlbumSecondary.R + 80),
                        (byte)Math.Min(255, _npAlbumSecondary.G + 80),
                        (byte)Math.Min(255, _npAlbumSecondary.B + 80)));

                // Specs text brighter
                NpSongSpecs.Foreground = new SolidColorBrush(
                    System.Windows.Media.Color.FromArgb(200,
                        (byte)Math.Min(255, _npAlbumPrimary.R + 60),
                        (byte)Math.Min(255, _npAlbumPrimary.G + 60),
                        (byte)Math.Min(255, _npAlbumPrimary.B + 60)));

                // Tint Queue badge to match album colors
                NpNextTrackBorder.Background = new SolidColorBrush(
                    System.Windows.Media.Color.FromArgb(50,
                        _npAlbumPrimary.R, _npAlbumPrimary.G, _npAlbumPrimary.B));
                NpNextTrackLabel.Foreground = new SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(
                        (byte)Math.Min(255, _npAlbumSecondary.R + 80),
                        (byte)Math.Min(255, _npAlbumSecondary.G + 80),
                        (byte)Math.Min(255, _npAlbumSecondary.B + 80)));
                NpNextTrackText.Foreground = new SolidColorBrush(bright);

                // Tint all icon paths in the bottom bar with album colors
                var iconTint = new SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(
                        (byte)Math.Min(255, _npAlbumPrimary.R + 80),
                        (byte)Math.Min(255, _npAlbumPrimary.G + 80),
                        (byte)Math.Min(255, _npAlbumPrimary.B + 80)));
                var controlTint = new SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(
                        (byte)Math.Min(255, _npAlbumSecondary.R + 100),
                        (byte)Math.Min(255, _npAlbumSecondary.G + 100),
                        (byte)Math.Min(255, _npAlbumSecondary.B + 100)));

                // Playback control fills
                NpPlayPath.Fill = controlTint;
                NpPausePath.Fill = controlTint;

                // Prev/Next icons
                NpPrevLine.Stroke = controlTint;
                NpPrevFill.Fill = controlTint;
                NpNextFill.Fill = controlTint;
                NpNextLine.Stroke = controlTint;

                // Volume icon
                NpVolumeIconPath.Fill = iconTint;
                NpVolumeIconPath.Stroke = iconTint;

                // Time labels
                NpTimeElapsed.Foreground = iconTint;
                NpTimeTotal.Foreground = iconTint;

                // Tint all option button icons in the bottom bar
                // (use individual update methods so active/inactive state is respected)
                NpUpdateShuffleIcon();
                NpUpdateLoopIcon();
                NpUpdateAutoPlayIcon();
                NpUpdateVisualizerIcon();
                NpUpdateVizPlacementIcon();
                NpUpdateLyricsOffIcon();
                NpUpdateTranslateIcon();
                NpUpdateKaraokeIcon();
                NpUpdateColorMatchIcon();

                // Tint remaining static icons that don't have toggle states
                NpVizStyleIcon.Stroke = iconTint;
                NpProviderCircle.Stroke = iconTint;
                NpProviderArc.Stroke = new SolidColorBrush(bright);
                NpSaveLyricsIcon.Stroke = iconTint;
                NpSettingsGear.Stroke = iconTint;
                NpSettingsCenter.Stroke = iconTint;
                NpTranslateSettingsIcon.Stroke = iconTint;
                NpLayoutIcon.Stroke = iconTint;
                NpQueueIcon.Foreground = iconTint;

                // Seek and Volume sliders: tint the accent color
                var sliderAccent = new SolidColorBrush(bright);
                Application.Current.Resources["PlaybarAccentColor"] = sliderAccent;

                // Tint button backgrounds/hover/pressed/border with album colors
                var btnBg = System.Windows.Media.Color.FromArgb(160,
                    (byte)(_npAlbumBackground.R / 2),
                    (byte)(_npAlbumBackground.G / 2),
                    (byte)(_npAlbumBackground.B / 2));
                var btnHover = System.Windows.Media.Color.FromArgb(200,
                    (byte)Math.Min(255, _npAlbumPrimary.R / 3 + 30),
                    (byte)Math.Min(255, _npAlbumPrimary.G / 3 + 30),
                    (byte)Math.Min(255, _npAlbumPrimary.B / 3 + 30));
                var btnPressed = System.Windows.Media.Color.FromArgb(220,
                    (byte)Math.Min(255, _npAlbumPrimary.R / 2 + 40),
                    (byte)Math.Min(255, _npAlbumPrimary.G / 2 + 40),
                    (byte)Math.Min(255, _npAlbumPrimary.B / 2 + 40));
                var btnBorder = System.Windows.Media.Color.FromArgb(60,
                    _npAlbumPrimary.R, _npAlbumPrimary.G, _npAlbumPrimary.B);
                Application.Current.Resources["ButtonBg"] = new SolidColorBrush(btnBg);
                Application.Current.Resources["ButtonHover"] = new SolidColorBrush(btnHover);
                Application.Current.Resources["ButtonPressed"] = new SolidColorBrush(btnPressed);
                Application.Current.Resources["ButtonBorder"] = new SolidColorBrush(btnBorder);

                // Tint the titlebar to match the bottom bar color
                try
                {
                    var hwnd = new WindowInteropHelper(this).Handle;
                    if (hwnd != IntPtr.Zero)
                    {
                        byte r = (byte)(_npAlbumBackground.R / 3);
                        byte g = (byte)(_npAlbumBackground.G / 3);
                        byte b = (byte)(_npAlbumBackground.B / 3);
                        int colorRef = r | (g << 8) | (b << 16);
                        DwmSetWindowAttribute(hwnd, DWMWA_CAPTION_COLOR, ref colorRef, sizeof(int));
                    }
                }
                catch { }

                // Store colors for visualizer tinting (used by render methods)
                // Boost to minimum luminance so bars are clearly visible
                _npVizColorPrimary = EnsureMinLuminance(_npAlbumPrimary, 120);
                _npVizColorSecondary = EnsureMinLuminance(_npAlbumSecondary, 120);
            }
            else
            {
                // Restore defaults
                NpBgGradient.Opacity = 0.6;
                NpDarkOverlay.Background = new SolidColorBrush(
                    System.Windows.Media.Color.FromArgb(102, 0, 0, 0));
                NpBottomBar.Background = (Brush)FindResource("ToolbarBg");
                NpBottomBar.BorderBrush = (Brush)FindResource("BorderColor");
                NpSongTitle.Foreground = (Brush)FindResource("TextPrimary");
                NpBigTitle.Foreground = (Brush)FindResource("TextPrimary");
                NpSongArtist.Foreground = (Brush)FindResource("TextSecondary");
                NpSongSpecs.Foreground = (Brush)FindResource("TextSecondary");

                // Restore Queue badge to defaults
                NpNextTrackBorder.Background = (Brush)FindResource("ButtonBg");
                NpNextTrackLabel.Foreground = (Brush)FindResource("TextPrimary");
                NpNextTrackText.Foreground = (Brush)FindResource("TextPrimary");

                NpPlayPath.Fill = (Brush)FindResource("TextPrimary");
                NpPausePath.Fill = (Brush)FindResource("TextPrimary");
                NpPrevLine.Stroke = (Brush)FindResource("TextPrimary");
                NpPrevFill.Fill = (Brush)FindResource("TextPrimary");
                NpNextFill.Fill = (Brush)FindResource("TextPrimary");
                NpNextLine.Stroke = (Brush)FindResource("TextPrimary");
                NpVolumeIconPath.Fill = (Brush)FindResource("TextMuted");
                NpVolumeIconPath.Stroke = (Brush)FindResource("TextMuted");
                NpTimeElapsed.Foreground = (Brush)FindResource("TextMuted");
                NpTimeTotal.Foreground = (Brush)FindResource("TextMuted");

                // Restore option button icon colors (use individual methods for active/inactive state)
                NpUpdateShuffleIcon();
                NpUpdateLoopIcon();
                NpUpdateAutoPlayIcon();
                NpUpdateVisualizerIcon();
                NpUpdateVizPlacementIcon();
                NpUpdateLyricsOffIcon();
                NpUpdateTranslateIcon();
                NpUpdateKaraokeIcon();
                NpUpdateColorMatchIcon();

                // Restore static icon colors to theme defaults
                var muted = (Brush)FindResource("TextMuted");
                NpVizStyleIcon.Stroke = muted;
                NpProviderCircle.Stroke = muted;
                NpProviderArc.Stroke = (Brush)FindResource("AccentColor");
                NpSaveLyricsIcon.Stroke = muted;
                NpSettingsGear.Stroke = muted;
                NpSettingsCenter.Stroke = muted;
                NpTranslateSettingsIcon.Stroke = muted;
                NpLayoutIcon.Stroke = muted;
                NpQueueIcon.Foreground = muted;

                // Only nuke shared theme resources when BOTH color matches are off.
                // If main-window color match is on, ApplyTheme would wipe its AccentColor /
                // BorderColor / ButtonHover / SelectionBg / PlaybarAccentColor — which is
                // exactly the "skipping a track reverts to default theme" bug, since the
                // NP path runs synchronously on a cache hit, before LoadMainCoverColors
                // finishes (or at all, if the new track has no album art).
                if (!_npColorMatchEnabled && !ThemeManager.MainColorMatchEnabled)
                {
                    // Restore slider accent to theme default
                    var vizColors = ThemeManager.GetVisualizerColors();
                    Application.Current.Resources["PlaybarAccentColor"] = new SolidColorBrush(vizColors.ProgressGradient[0]);

                    // Restore button backgrounds/hover/pressed/border to theme defaults
                    ThemeManager.ApplyTheme(ThemeManager.CurrentTheme);
                    // Restore titlebar to theme default
                    ApplyThemeTitleBar();
                }

                _npVizColorPrimary = default;
                _npVizColorSecondary = default;
            }
        }

        // ═══════════════════════════════════════════
        //  Main Window Color Match
        // ═══════════════════════════════════════════

        private async void LoadMainCoverColors(string filePath)
        {
            int gen = _npColorGeneration;

            // Fast-path: if colors are already cached, apply instantly so the main
            // window doesn't flash the default theme while TagLib + image decode runs.
            string cacheKey = HashPath(filePath);
            if (ThemeManager.NpColorCacheEnabled && _npColorCache.TryGetValue(cacheKey, out var cachedColors))
            {
                Dispatcher.Invoke(() =>
                {
                    if (gen != _npColorGeneration) return;
                    _mainAlbumPrimary = System.Windows.Media.Color.FromRgb(cachedColors.Primary.R, cachedColors.Primary.G, cachedColors.Primary.B);
                    _mainAlbumSecondary = System.Windows.Media.Color.FromRgb(cachedColors.Secondary.R, cachedColors.Secondary.G, cachedColors.Secondary.B);
                    ApplyMainColorMatch();
                });
                // Fall through to re-extract and update cache in the background.
            }

            try
            {
            try
            {
                using var tagFile = await Task.Run(() => TagLib.File.Create(filePath));
                if (tagFile.Tag.Pictures.Length == 0)
                {
                    Dispatcher.Invoke(() =>
                    {
                        if (gen != _npColorGeneration) return;
                        if (!ThemeManager.MainColorMatchEnabled)
                        {
                            _mainAlbumPrimary = default;
                            _mainAlbumSecondary = default;
                            RestoreMainColorMatch();
                        }
                        else if (_mainAlbumPrimary != default)
                        {
                            // Color match is on but new track has no art — re-apply the
                            // previous track's colors so they survive any intervening
                            // theme reset (e.g. NP cache-hit calling ApplyTheme).
                            ApplyMainColorMatch();
                        }
                    });
                    return;
                }

                var imageData = tagFile.Tag.Pictures[0].Data.Data;
                var colors = await Task.Run(() =>
                {
                    var bmp = new BitmapImage();
                    bmp.BeginInit();
                    using (var ms = new MemoryStream(imageData))
                    {
                        bmp.StreamSource = ms;
                        bmp.CacheOption = BitmapCacheOption.OnLoad;
                        bmp.EndInit();
                    }
                    bmp.Freeze();
                    var converted = new FormatConvertedBitmap(bmp, PixelFormats.Bgra32, null, 0);
                    int stride = converted.PixelWidth * 4;
                    var pixels = new byte[stride * converted.PixelHeight];
                    converted.CopyPixels(pixels, stride, 0);
                    return AlbumColorExtractor.Extract(pixels, converted.PixelWidth, converted.PixelHeight, stride);
                });

                Dispatcher.Invoke(() =>
                {
                    if (gen != _npColorGeneration) return;
                    _mainAlbumPrimary = System.Windows.Media.Color.FromRgb(colors.Primary.R, colors.Primary.G, colors.Primary.B);
                    _mainAlbumSecondary = System.Windows.Media.Color.FromRgb(colors.Secondary.R, colors.Secondary.G, colors.Secondary.B);
                    ApplyMainColorMatch();

                    // Belt-and-suspenders: schedule a second re-apply at ContextIdle so that
                    // anything that wrote theme resources during this same dispatch frame
                    // (e.g. NP cache-hit path firing concurrently) gets overridden by the
                    // album colors. Idempotent — safe to fire even if no reset happened.
                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        if (gen != _npColorGeneration) return;
                        if (ThemeManager.MainColorMatchEnabled && _mainAlbumPrimary != default)
                            ApplyMainColorMatch();
                        if (_npColorMatchEnabled && _npAlbumPrimary != default)
                            NpApplyColorMatchMode();
                    }), System.Windows.Threading.DispatcherPriority.ContextIdle);
                });
            }
            catch
            {
                Dispatcher.Invoke(() =>
                {
                    if (gen != _npColorGeneration) return;
                    if (!ThemeManager.MainColorMatchEnabled)
                    {
                        _mainAlbumPrimary = default;
                        _mainAlbumSecondary = default;
                        RestoreMainColorMatch();
                    }
                    else if (_mainAlbumPrimary != default)
                    {
                        // Same fallback as the no-pictures branch above.
                        ApplyMainColorMatch();
                    }
                });
            }
            }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[LoadMainCoverColors] {ex}"); }
        }

        private void ApplyMainColorMatch()
        {
            if (!ThemeManager.MainColorMatchEnabled)
            {
                RestoreMainColorMatch();
                return;
            }
            if (_mainAlbumPrimary == default)
            {
                // Keep previous track's colors until new ones are ready
                return;
            }

            // Boost primary/secondary to minimum luminance 140 so they're always readable as accents
            var brightPrimary = EnsureAccentReadable(_mainAlbumPrimary, 140);
            var brightSecondary = EnsureAccentReadable(_mainAlbumSecondary, 110);

            // Derive darker tints for theme resources
            var accentBrush = new SolidColorBrush(brightPrimary);
            var borderBrush = new SolidColorBrush(Darken(brightPrimary, 0.55));
            var hoverBrush = new SolidColorBrush(Darken(brightPrimary, 0.40));
            var selectionBrush = new SolidColorBrush(Darken(brightPrimary, 0.30));

            Application.Current.Resources["AccentColor"] = accentBrush;
            Application.Current.Resources["BorderColor"] = borderBrush;
            Application.Current.Resources["ButtonHover"] = hoverBrush;
            Application.Current.Resources["SelectionBg"] = selectionBrush;
            Application.Current.Resources["PlaybarAccentColor"] = accentBrush;

            // Tint main playback icons
            PlayIcon.Fill = accentBrush;
            PauseIcon.Fill = accentBrush;

            // Tint shuffle / loop / volume icons
            var secondaryBrush = new SolidColorBrush(brightSecondary);
            ShuffleIcon.Stroke = secondaryBrush;
            LoopIcon.Stroke = secondaryBrush;
            VolumeIconPath.Fill = secondaryBrush;
            VolumeIconPath.Stroke = secondaryBrush;

            // Tint spectrogram title
            SpectrogramTitle.Foreground = accentBrush;

            NpUpdateSaveLyricsButton();
        }

        private void RestoreMainColorMatch()
        {
            if (ThemeManager.MainColorMatchEnabled) return;
            ThemeManager.ApplyTheme(ThemeManager.CurrentTheme);

            PlayIcon.Fill = (Brush)FindResource("TextPrimary");
            PauseIcon.Fill = (Brush)FindResource("TextPrimary");
            ShuffleIcon.Stroke = (Brush)FindResource("TextMuted");
            LoopIcon.Stroke = (Brush)FindResource("TextMuted");
            VolumeIconPath.Fill = (Brush)FindResource("TextMuted");
            VolumeIconPath.Stroke = (Brush)FindResource("TextMuted");
            SpectrogramTitle.Foreground = (Brush)FindResource("TextPrimary");

            NpUpdateSaveLyricsButton();
        }

        internal void UpdateOfflineBadge()
        {
            if (TxtOfflineBadge == null) return;
            TxtOfflineBadge.Visibility = ThemeManager.OfflineModeEnabled
                ? Visibility.Visible
                : Visibility.Collapsed;
        }

        /// <summary>
        /// Shows a non-intrusive status message when the user tries to use an online feature while offline.
        /// Uses a cooldown to avoid spamming the user.
        /// </summary>
        private void ShowOfflineNotice(string featureName)
        {
            if (!ThemeManager.OfflineModeEnabled) return;
            var now = DateTime.UtcNow;
            if ((now - _lastOfflineNoticeTime).TotalSeconds < 30) return;
            _lastOfflineNoticeTime = now;
            StatusText.Text = $"{featureName} is unavailable in offline mode. Enable online mode in Settings to use it.";
        }

        private static System.Windows.Media.Color EnsureAccentReadable(System.Windows.Media.Color c, int minLum)
        {
            double lum = 0.299 * c.R + 0.587 * c.G + 0.114 * c.B;
            if (lum >= minLum) return c;
            double factor = minLum / Math.Max(lum, 1);
            return System.Windows.Media.Color.FromRgb(
                (byte)Math.Min(255, (int)(c.R * factor)),
                (byte)Math.Min(255, (int)(c.G * factor)),
                (byte)Math.Min(255, (int)(c.B * factor)));
        }

        private static System.Windows.Media.Color Darken(System.Windows.Media.Color c, double factor)
        {
            // factor 0 = black, 1 = original color
            return System.Windows.Media.Color.FromRgb(
                (byte)(c.R * factor),
                (byte)(c.G * factor),
                (byte)(c.B * factor));
        }

        // ─── NP Layout Customization ───

        private bool _npLayoutPopupInit;

        private void NpLayoutCustomize_Click(object sender, RoutedEventArgs e)
        {
            // Seed sliders with current values (suppress change events during init)
            _npLayoutPopupInit = true;
            bool fs = WindowState == WindowState.Maximized;
            NpLayoutCoverSlider.Value = _npCoverSize > 0 ? _npCoverSize : (fs ? 420 : 300);
            NpLayoutTitleSlider.Value = _npTitleSize > 0 ? _npTitleSize : (fs ? 32 : 24);
            NpLayoutSubSlider.Value = _npSubTextSize > 0 ? _npSubTextSize : (fs ? 12 : 10);
            NpLayoutLyricsSlider.Value = _npLyricsSize > 0 ? _npLyricsSize : (fs ? 22 : 18);
            NpLayoutLyricsPosSlider.Value = _npLyricsOffsetX;
            NpLayoutVizSlider.Value = _npVizSize > 0 ? _npVizSize : (int)_npVizBarHeight;
            NpLayoutCoverXSlider.Value = _npCoverOffsetX;
            NpLayoutCoverYSlider.Value = _npCoverOffsetY;
            NpLayoutTitleXSlider.Value = _npTitleOffsetX;
            NpLayoutTitleYSlider.Value = _npTitleOffsetY;
            NpLayoutArtistXSlider.Value = _npArtistOffsetX;
            NpLayoutArtistYSlider.Value = _npArtistOffsetY;
            NpLayoutVizYSlider.Value = _npVizOffsetY;
            _npLayoutPopupInit = false;
            NpLayoutPopup.IsOpen = true;
            NpLayoutPopup.Closed -= NpLayoutPopup_Closed;
            NpLayoutPopup.Closed += NpLayoutPopup_Closed;
        }

        private void NpLayoutPopup_Closed(object? sender, EventArgs e)
        {
            NpSavePreferences();
        }

        private void NpLayoutSlider_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_npLayoutPopupInit || !IsLoaded) return;

            _npCoverSize = (int)NpLayoutCoverSlider.Value;
            _npTitleSize = (int)NpLayoutTitleSlider.Value;
            _npSubTextSize = (int)NpLayoutSubSlider.Value;
            _npLyricsSize = (int)NpLayoutLyricsSlider.Value;
            _npLyricsOffsetX = (int)NpLayoutLyricsPosSlider.Value;
            _npVizSize = (int)NpLayoutVizSlider.Value;
            _npCoverOffsetX = (int)NpLayoutCoverXSlider.Value;
            _npCoverOffsetY = (int)NpLayoutCoverYSlider.Value;
            _npTitleOffsetX = (int)NpLayoutTitleXSlider.Value;
            _npTitleOffsetY = (int)NpLayoutTitleYSlider.Value;
            _npArtistOffsetX = (int)NpLayoutArtistXSlider.Value;
            _npArtistOffsetY = (int)NpLayoutArtistYSlider.Value;
            _npVizOffsetY = (int)NpLayoutVizYSlider.Value;

            // Live preview
            NpApplyFullscreenScaling(WindowState == WindowState.Maximized);
        }

        private void NpLayoutReset_Click(object sender, RoutedEventArgs e)
        {
            _npCoverSize = 0;
            _npTitleSize = 0;
            _npSubTextSize = 0;
            _npLyricsSize = 0;
            _npLyricsOffsetX = 0;
            _npVizSize = 0;
            _npCoverOffsetX = 0;
            _npCoverOffsetY = 0;
            _npTitleOffsetX = 0;
            _npTitleOffsetY = 0;
            _npArtistOffsetX = 0;
            _npArtistOffsetY = 0;
            _npVizOffsetY = 0;

            bool fs = WindowState == WindowState.Maximized;
            _npLayoutPopupInit = true;
            NpLayoutCoverSlider.Value = fs ? 420 : 300;
            NpLayoutTitleSlider.Value = fs ? 32 : 24;
            NpLayoutSubSlider.Value = fs ? 12 : 10;
            NpLayoutLyricsSlider.Value = fs ? 22 : 18;
            NpLayoutLyricsPosSlider.Value = 0;
            NpLayoutVizSlider.Value = (int)_npVizBarHeight;
            NpLayoutCoverXSlider.Value = 0;
            NpLayoutCoverYSlider.Value = 0;
            NpLayoutTitleXSlider.Value = 0;
            NpLayoutTitleYSlider.Value = 0;
            NpLayoutArtistXSlider.Value = 0;
            NpLayoutArtistYSlider.Value = 0;
            NpLayoutVizYSlider.Value = 0;
            _npLayoutPopupInit = false;

            NpApplyFullscreenScaling(fs);
            NpSavePreferences();
        }

        // ─── NP Settings Button ───

        private void NpSettings_Click(object sender, RoutedEventArgs e)
        {
            var settingsWindow = new SettingsWindow { Owner = this };
            settingsWindow.ShowDialog();

            // Full theme + NP refresh after settings change
            try
            {
                UpdateServiceButtonLabels();
                ApplyThemeTitleBar();
                UpdateShuffleUI();
                UpdateLoopUI();
                _eqSliderTemplateCache = null;
                InitializeEqualizerSliders();
                ChkEqEnabled.IsChecked = ThemeManager.EqualizerEnabled;

                // Sync Discord RPC state
                if (ThemeManager.DiscordRpcEnabled && !string.IsNullOrWhiteSpace(ThemeManager.DiscordRpcClientId))
                {
                    if (!_discord.IsEnabled) _discord.Enable(); else _discord.Disable();
                }
                else if (_discord.IsEnabled) _discord.Disable();

                // Sync spatial / normalization / Last.fm
                var spatial = _player.CurrentSpatialAudio;
                if (spatial != null) spatial.Enabled = ThemeManager.SpatialAudioEnabled;
                if (_player.IsPlaying || _player.IsPaused)
                    _player.SetNormalization(ThemeManager.AudioNormalization);
                if (!string.IsNullOrEmpty(ThemeManager.LastFmSessionKey))
                    _lastFm.Configure(ThemeManager.LastFmApiKey, ThemeManager.LastFmApiSecret, ThemeManager.LastFmSessionKey);
                UpdateLastFmStatusIndicator();
                ApplyColumnVisibility();

                // NP-specific refreshes
                NpUpdateAutoPlayIcon();
                if (_npColorMatchEnabled)
                    NpApplyColorMatchMode();
                // Re-apply NP background from DynamicResource
                NpBottomBar.Background = (System.Windows.Media.Brush)FindResource("PanelBg");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[NpSettings_Click refresh] {ex}");
            }
        }

        // ─── NP Visualizer Placement Toggle ───

        private void NpVizPlacement_Click(object sender, RoutedEventArgs e)
        {
            _npVizPlacement = (_npVizPlacement + 1) % 2;
            NpApplyVizPlacement();

            // Restart visualizer to use the new target canvas
            if (_npVisualizerEnabled)
            {
                NpStopVisualizer();
                NpStartVisualizer();
            }

            // Different placements use different scale factors — re-apply
            NpApplyFullscreenScaling(WindowState == WindowState.Maximized);

            NpSavePreferences();
        }

        private void NpApplyVizPlacement()
        {
            NpUpdateVizPlacementIcon();
            if (_npVizPlacement == 0)
            {
                // Full-width bar above playbar
                NpVizBar.Height = _npVisualizerEnabled ? _npVizBarHeight : 0;
                NpUnderCoverVizRow.Height = new GridLength(0);
                NpUnderCoverVizBorder.Visibility = Visibility.Collapsed;
                NpVizPlacementBtn.ToolTip = "Visualizer: Full-width";
                NpVizPlacementIcon.Data = System.Windows.Media.Geometry.Parse("M 1,13 L 15,13 M 1,10 L 15,10");
            }
            else
            {
                // Under album cover
                NpVizBar.Height = 0;
                NpUnderCoverVizRow.Height = _npVisualizerEnabled ? new GridLength(_npVizBarHeight) : new GridLength(0);
                NpUnderCoverVizBorder.Visibility = _npVisualizerEnabled ? Visibility.Visible : Visibility.Collapsed;
                NpVizPlacementBtn.ToolTip = "Visualizer: Under cover";
                NpVizPlacementIcon.Data = System.Windows.Media.Geometry.Parse("M 1,6 L 8,6 M 1,10 L 8,10");
            }

            // Adjust title/Queue spacing relative to album cover glow.
            // Glow extends ~24px outside cover. When viz ON, push title UP
            // (big bottom margin = gap above cover) and Queue DOWN (big top margin)
            // so they sit outside the blur. When viz OFF, bring them back closer.
            bool fullscreen = WindowState == WindowState.Maximized;
            if (fullscreen)
            {
                // Fullscreen: push title DOWN (big top margin) and Queue UP (small top margin)
                NpBigTitleBorder.Margin = _npVisualizerEnabled
                    ? new Thickness(8, 80, 8, 36)
                    : new Thickness(8, 80, 8, 4);
                NpNextTrackBorder.Margin = new Thickness(0, 4, 0, 2);
            }
            else
            {
                NpBigTitleBorder.Margin = _npVisualizerEnabled
                    ? new Thickness(8, 0, 8, 36)   // UP: flush top, 36px gap before cover (clears glow)
                    : new Thickness(8, 12, 8, 4);  // DOWN: 12px from top, small gap to cover
                NpNextTrackBorder.Margin = _npVisualizerEnabled
                    ? new Thickness(0, 36, 0, 2)   // DOWN: 36px gap after cover (clears glow)
                    : new Thickness(0, 4, 0, 2);   // UP: small gap under cover
            }
            NpApplyFullscreenScaling(fullscreen);
        }

        private void NpApplyFullscreenScaling(bool fullscreen)
        {
            // Defaults per window state (used when custom size is 0)
            int defCover = fullscreen ? 420 : 300;
            int defTitle = fullscreen ? 32 : 24;
            int defSub = fullscreen ? 12 : 10;
            int defLyrics = fullscreen ? 22 : 18;
            int defViz = (int)_npVizBarHeight; // user-draggable bar height

            int coverSz = _npCoverSize > 0 ? _npCoverSize : defCover;
            int titleSz = _npTitleSize > 0 ? _npTitleSize : defTitle;
            int subSz = _npSubTextSize > 0 ? _npSubTextSize : defSub;
            int lyricsSz = _npLyricsSize > 0 ? _npLyricsSize : defLyrics;

            // Auto-scale cover when visualizer is on so a single slider value looks proportional in both modes
            // (without this, viz-OFF lets the cover stretch to fill freed grid space, viz-ON caps it smaller)
            double vizScale = !_npVisualizerEnabled ? 1.0
                            : (_npVizPlacement == 0 ? 0.78  // full-width viz bar consumes vertical room
                                                    : 0.85); // under-cover viz row
            int renderedCover = (int)Math.Round(coverSz * vizScale);

            // Don't force a square box — let the image take its natural aspect ratio
            // within renderedCover × renderedCover bounds. Stretch="Uniform" + MaxWidth/MaxHeight
            // makes wide thumbnails (e.g. 16:9) render at their real ratio instead of being
            // letterboxed inside a square. The wrapping Border sizes to the Image, so glows
            // and the DropShadowEffect adopt the same aspect automatically.
            NpCoverImage.ClearValue(FrameworkElement.WidthProperty);
            NpCoverImage.ClearValue(FrameworkElement.HeightProperty);
            NpCoverImage.MaxWidth = renderedCover;
            NpCoverImage.MaxHeight = renderedCover;

            // Title/artist widths based on available column width (decoupled from cover size)
            double colWidth = Math.Max(300, (ActualWidth - 96) / 2); // 96 = margins 32+32+16+16
            int titleMaxW = (int)(colWidth * 0.92);
            int titleBorderH = Math.Max(36, titleSz + 16);
            int titleViewboxH = Math.Max(30, titleSz + 10);
            NpBigTitleBorder.MaxHeight = titleBorderH;
            NpBigTitle.FontSize = titleSz;
            NpBigTitle.MaxWidth = titleMaxW;
            if (NpBigTitleBorder.Child is Viewbox vb) vb.MaxHeight = titleViewboxH;

            // Artist / Up-next label
            NpNextTrackLabel.FontSize = subSz;
            NpNextTrackText.FontSize = subSz;
            NpNextTrackText.MaxWidth = (int)(colWidth * 0.65);

            // Lyrics — clear any running animation first so direct set takes effect
            foreach (var tb in _npLyricTextBlocks)
            {
                tb.BeginAnimation(TextBlock.FontSizeProperty, null);
                tb.FontSize = lyricsSz;
            }

            // Lyrics horizontal offset
            int lyricsLeftMargin = 24 + _npLyricsOffsetX;
            NpLyricsDropTarget.Margin = new Thickness(lyricsLeftMargin, 0, 0, 0);

            // Position offsets via RenderTransform (translate)
            NpCoverAssembly.RenderTransform = new TranslateTransform(_npCoverOffsetX, _npCoverOffsetY);
            NpBigTitleBorder.RenderTransform = new TranslateTransform(_npTitleOffsetX, _npTitleOffsetY);
            NpNextTrackBorder.RenderTransform = new TranslateTransform(_npArtistOffsetX, _npArtistOffsetY);
            NpVizBar.RenderTransform = new TranslateTransform(0, _npVizOffsetY);
            NpUnderCoverVizBorder.RenderTransform = new TranslateTransform(0, _npVizOffsetY);

            // Visualizer height (only if custom)
            if (_npVizSize > 0)
            {
                _npVizBarHeight = _npVizSize;
                NpApplyVizBarHeight();
            }
        }

        private void NpApplyVizBarHeight()
        {
            if (_npVizPlacement == 0)
            {
                // Full-width bar
                if (_npVisualizerEnabled)
                    NpVizBar.Height = _npVizBarHeight;
            }
            else
            {
                // Under-cover
                if (_npVisualizerEnabled)
                    NpUnderCoverVizRow.Height = new GridLength(_npVizBarHeight);
            }
        }

        // ─── NP Lyrics-Off (Pure Viz + Art Mode) ───

        private void NpLyricsOff_Click(object sender, RoutedEventArgs e)
        {
            _npLyricsHidden = !_npLyricsHidden;
            NpApplyLyricsOffMode();
            NpUpdateLyricsOffIcon();

            // Auto-switch visualizer placement to match context
            if (_npVisualizerEnabled)
            {
                int newPlacement = _npLyricsHidden ? 0 : 1; // art-only → full-width, lyrics → under-cover
                if (_npVizPlacement != newPlacement)
                {
                    _npVizPlacement = newPlacement;
                    NpApplyVizPlacement();

                    // Restart visualizer to re-route canvas target
                    if (_player != null && _player.IsPlaying)
                    {
                        StopVisualizer();
                        StartVisualizer();
                    }
                }
            }

            NpSavePreferences();
        }

        private void NpApplyLyricsOffMode()
        {
            if (_npLyricsHidden)
            {
                // Collapse lyrics column, expand art column
                NpContentArea.ColumnDefinitions[0].Width = new GridLength(1, GridUnitType.Star);
                NpContentArea.ColumnDefinitions[1].Width = new GridLength(0);
                NpLyricsDropTarget.Visibility = Visibility.Collapsed;
            }
            else
            {
                // Restore two-column layout
                NpContentArea.ColumnDefinitions[0].Width = new GridLength(1, GridUnitType.Star);
                NpContentArea.ColumnDefinitions[1].Width = new GridLength(1, GridUnitType.Star);
                NpLyricsDropTarget.Visibility = Visibility.Visible;
            }
        }

        private void NpUpdateLyricsOffIcon()
        {
            NpLyricsOffIcon.Stroke = NpGetIconBrush(_npLyricsHidden);
            NpLyricsOffBtn.ToolTip = _npLyricsHidden ? "Show lyrics" : "Hide lyrics (art mode)";
            NpSetToggleBg(NpLyricsOffBtn, _npLyricsHidden);
        }

        // ─── NP Translation ───

        private void NpTranslate_Click(object sender, RoutedEventArgs e)
        {
            _npTranslateEnabled = !_npTranslateEnabled;
            NpUpdateTranslateIcon();

            if (_npTranslateEnabled)
            {
                if (ThemeManager.OfflineModeEnabled)
                {
                    ShowOfflineNotice("Lyric translation");
                    _npTranslateEnabled = false;
                    NpUpdateTranslateIcon();
                    return;
                }
                NpTranslateSettingsBtn.Visibility = Visibility.Visible;
                _ = NpTranslateLyricsAsync();
            }
            else
            {
                NpTranslateSettingsBtn.Visibility = Visibility.Collapsed;
                _npTranslatedLines = null;
                // Rebuild lines without translations
                NpBuildLyricLines();
            }
            NpSavePreferences();
        }

        private void NpUpdateTranslateIcon()
        {
            NpTranslateIcon.Stroke = NpGetIconBrush(_npTranslateEnabled);
            NpTranslateBtn.ToolTip = _npTranslateEnabled ? "Translation: ON" : "Translate lyrics";
            NpSetToggleBg(NpTranslateBtn, _npTranslateEnabled);
        }

        private void NpTranslateSettings_Click(object sender, RoutedEventArgs e)
        {
            // Toggle popup
            if (NpTranslatePopup.IsOpen)
            {
                NpTranslatePopup.IsOpen = false;
                return;
            }

            // Populate combos if empty
            if (NpTranslateFromCombo.Items.Count == 0)
            {
                NpTranslateFromCombo.Items.Add(new ComboBoxItem { Content = "Auto-detect", Tag = "auto" });
                foreach (var kv in TranslateService.LanguageNames)
                    NpTranslateFromCombo.Items.Add(new ComboBoxItem { Content = kv.Value, Tag = kv.Key });
                NpTranslateFromCombo.SelectedIndex = 0;
            }
            if (NpTranslateToCombo.Items.Count == 0)
            {
                foreach (var kv in TranslateService.LanguageNames)
                    NpTranslateToCombo.Items.Add(new ComboBoxItem { Content = kv.Value, Tag = kv.Key });
                // Default: English
                for (int i = 0; i < NpTranslateToCombo.Items.Count; i++)
                {
                    if (NpTranslateToCombo.Items[i] is ComboBoxItem ci && ci.Tag is string t && t == "en")
                    { NpTranslateToCombo.SelectedIndex = i; break; }
                }
                if (NpTranslateToCombo.SelectedIndex < 0)
                    NpTranslateToCombo.SelectedIndex = 0;
            }
            NpTranslatePopup.IsOpen = true;
        }

        private void NpTranslateFrom_Changed(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (NpTranslateFromCombo.SelectedItem is ComboBoxItem item && item.Tag is string lang)
            {
                _npTranslateFrom = lang;
                if (_npTranslateEnabled)
                {
                    if (ThemeManager.OfflineModeEnabled)
                    {
                        ShowOfflineNotice("Lyric translation");
                        return;
                    }
                    _npTranslatedLines = null;
                    _ = NpTranslateLyricsAsync();
                }
            }
        }

        private void NpTranslateTo_Changed(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (NpTranslateToCombo.SelectedItem is ComboBoxItem item && item.Tag is string lang)
            {
                _npTranslateTo = lang;
                if (_npTranslateEnabled)
                {
                    if (ThemeManager.OfflineModeEnabled)
                    {
                        ShowOfflineNotice("Lyric translation");
                        return;
                    }
                    _npTranslatedLines = null;
                    _ = NpTranslateLyricsAsync();
                }
            }
        }

        private async Task NpTranslateLyricsAsync()
        {
            var lyrics = _npCurrentLyrics; // capture reference before async work
            if (!lyrics.HasLyrics) return;
            int version = _npLyricsVersion; // snapshot to detect track change

            var lines = lyrics.Lines.Select(l => l.Text).ToList();
            if (lines.Count == 0) return;

            // Detect source language if set to auto
            string fromLang = _npTranslateFrom;
            if (fromLang == "auto")
            {
                // Use first non-empty, non-instrumental line to detect
                var sample = lines.FirstOrDefault(l =>
                    !string.IsNullOrWhiteSpace(l) && l.Length > 3
                    && !l.StartsWith('[') && !l.StartsWith('(')) ?? "";
                if (!string.IsNullOrWhiteSpace(sample))
                    fromLang = await TranslateService.DetectLanguageAsync(sample);
                else
                    fromLang = "en"; // fallback
            }

            if (version != _npLyricsVersion) return; // track changed during await

            // Don't translate if source == target
            if (string.Equals(fromLang, _npTranslateTo, StringComparison.OrdinalIgnoreCase))
            {
                _npTranslatedLines = null;
                NpBuildLyricLines();
                return;
            }

            try
            {
                _npTranslatedLines = await TranslateService.TranslateLinesAsync(lines, fromLang, _npTranslateTo);
            }
            catch
            {
                _npTranslatedLines = null;
            }

            if (version != _npLyricsVersion) return; // track changed during await
            NpBuildLyricLines();
        }

        // ─── NP Karaoke Mode ───

        private void NpKaraoke_Click(object sender, RoutedEventArgs e)
        {
            _npKaraokeEnabled = !_npKaraokeEnabled;
            NpUpdateKaraokeIcon();
            _npCurrentLyricIndex = -1;
            NpBuildLyricLines();
            NpSavePreferences();
        }

        private void NpUpdateKaraokeIcon()
        {
            NpKaraokeIcon.Stroke = NpGetIconBrush(_npKaraokeEnabled);
            NpKaraokeBtn.ToolTip = _npKaraokeEnabled ? "Karaoke: ON" : "Karaoke word-by-word";
            NpSetToggleBg(NpKaraokeBtn, _npKaraokeEnabled);
        }

        // ─── NP Double-click Album Art → Tag Editor ───

        private DateTime _npLastCoverClick = DateTime.MinValue;

        private void NpCoverBorder_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            // Detect double-click manually (MouseDoubleClick not available on Border)
            var now = DateTime.UtcNow;
            if ((now - _npLastCoverClick).TotalMilliseconds < 400)
            {
                // Double-click detected — open tag editor
                if (_player.CurrentFile != null)
                {
                    var file = _files.FirstOrDefault(f =>
                        string.Equals(f.FilePath, _player.CurrentFile, StringComparison.OrdinalIgnoreCase));
                    if (file != null)
                    {
                        var editor = new MetadataEditorWindow(file, this);
                        editor.ShowDialog();
                        if (editor.MetadataChanged)
                        {
                            _filteredView?.Refresh();
                            // Reload cover in case it changed
                            NpSetTrack(file);
                        }
                    }
                }
                _npLastCoverClick = DateTime.MinValue;
            }
            else
            {
                _npLastCoverClick = now;
            }
        }

        // ─── NP Next Track Preview ───

        private void NpNextTrackBorder_Click(object sender, MouseButtonEventArgs e)
        {
            _npSubCoverShowArtist = !_npSubCoverShowArtist;
            NpUpdateNextTrackPreview();
            NpSavePreferences();
        }

        private void NpUpdateNextTrackPreview()
        {
            if (_npSubCoverShowArtist)
            {
                // Show current artist
                string artist = NpSongArtist.Text;
                if (!string.IsNullOrWhiteSpace(artist))
                {
                    NpNextTrackLabel.Text = "Artist:  ";
                    NpNextTrackText.Text = artist;
                    NpNextTrackBorder.Visibility = Visibility.Visible;
                    NpNextTrackBorder.ToolTip = "Click to show Queue";
                }
                else
                {
                    NpNextTrackBorder.Visibility = Visibility.Collapsed;
                }
                return;
            }

            // Queue preview mode
            AudioFileInfo? nextTrack = null;

            try
            {
                if (_queue.Count > 0)
                {
                    nextTrack = _queue[0];
                }
                else
                {
                    var items = _filteredView?.Cast<AudioFileInfo>().ToList();
                    if (items != null && items.Count > 0 && _player.CurrentFile != null)
                    {
                        if (_shuffleMode && _shuffleDeck.Count > 0)
                        {
                            // Use the actual deck index to accurately show the next shuffled track
                            if (_shuffleDeckIndex < _shuffleDeck.Count)
                                nextTrack = _shuffleDeck[_shuffleDeckIndex];
                        }
                        else
                        {
                            int idx = items.FindIndex(f =>
                                string.Equals(f.FilePath, _player.CurrentFile, StringComparison.OrdinalIgnoreCase));
                            if (idx >= 0 && idx + 1 < items.Count)
                                nextTrack = items[idx + 1];
                        }
                    }
                }
            }
            catch { /* ignore */ }

            if (nextTrack != null && ThemeManager.AutoPlayNext)
            {
                string name = nextTrack.Title ?? nextTrack.FileName ?? "Unknown";
                if (!string.IsNullOrWhiteSpace(nextTrack.Artist))
                    name = $"{nextTrack.Artist} — {name}";
                NpNextTrackLabel.Text = "Up next:  ";
                NpNextTrackText.Text = name;
                NpNextTrackBorder.Visibility = Visibility.Visible;
                NpNextTrackBorder.ToolTip = "Click to show Artist";
            }
            else
            {
                NpNextTrackBorder.Visibility = Visibility.Collapsed;
            }
        }

        // ─── NP Animated Background ───

        private double _npBgAngle;

        private void NpStartBgAnimation()
        {
            if (_npBgAnimTimer != null) return;
            _npBgAngle = 45;
            _npBgAnimTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
            _npBgAnimTimer.Tick += NpBgAnim_Tick;
            _npBgAnimTimer.Start();
        }

        private void NpStopBgAnimation()
        {
            _npBgAnimTimer?.Stop();
            _npBgAnimTimer = null;
        }

        private void NpBgAnim_Tick(object? sender, EventArgs e)
        {
            if (NpBgGradient.Background is LinearGradientBrush lgb && !lgb.IsFrozen)
            {
                // Slow rotation: ~0.15 degrees per tick (50ms) ≈ 3 degrees/sec → full 360 in 2 minutes
                _npBgAngle = (_npBgAngle + 0.15) % 360;
                double rad = _npBgAngle * Math.PI / 180.0;
                double cos = Math.Cos(rad), sin = Math.Sin(rad);
                lgb.StartPoint = new System.Windows.Point(0.5 - cos * 0.5, 0.5 - sin * 0.5);
                lgb.EndPoint = new System.Windows.Point(0.5 + cos * 0.5, 0.5 + sin * 0.5);
            }
        }

        // ─── NP Glow Pulse Animation ───

        private DispatcherTimer? _npGlowPulseTimer;
        private double _npGlowPhase;

        private void NpStartGlowPulse()
        {
            if (_npGlowPulseTimer != null) return;
            _npGlowPhase = 0;
            _npGlowPulseTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
            _npGlowPulseTimer.Tick += NpGlowPulse_Tick;
            _npGlowPulseTimer.Start();
        }

        private void NpStopGlowPulse()
        {
            _npGlowPulseTimer?.Stop();
            _npGlowPulseTimer = null;
        }

        private void NpGlowPulse_Tick(object? sender, EventArgs e)
        {
            _npGlowPhase += 0.03; // Slow breathing
            double pulse = 0.5 + 0.12 * Math.Sin(_npGlowPhase); // oscillates 0.38 – 0.62
            double pulse2 = 0.35 + 0.08 * Math.Sin(_npGlowPhase + 1.0); // offset phase
            NpCoverGlow1.Opacity = pulse;
            NpCoverGlow2.Opacity = pulse2;
        }

        // ─── NP Drag Leave (hide overlay) ───

        private void NpLyrics_DragLeave(object sender, System.Windows.DragEventArgs e)
        {
            NpDropOverlay.Visibility = Visibility.Collapsed;
        }

        // ─── NP Drag and Drop LRC ───

        private void NpLyrics_DragOver(object sender, System.Windows.DragEventArgs e)
        {
            e.Effects = System.Windows.DragDropEffects.None;
            if (e.Data.GetDataPresent(System.Windows.DataFormats.FileDrop))
            {
                var files = e.Data.GetData(System.Windows.DataFormats.FileDrop) as string[];
                if (files != null && files.Any(f =>
                    f.EndsWith(".lrc", StringComparison.OrdinalIgnoreCase)))
                {
                    e.Effects = System.Windows.DragDropEffects.Copy;
                    NpDropOverlay.Visibility = Visibility.Visible;
                }
            }
            e.Handled = true;
        }

        private void NpLyrics_Drop(object sender, System.Windows.DragEventArgs e)
        {
            NpDropOverlay.Visibility = Visibility.Collapsed;

            if (!e.Data.GetDataPresent(System.Windows.DataFormats.FileDrop)) return;
            var files = e.Data.GetData(System.Windows.DataFormats.FileDrop) as string[];
            if (files == null) return;

            var lrcFile = files.FirstOrDefault(f =>
                f.EndsWith(".lrc", StringComparison.OrdinalIgnoreCase));
            if (lrcFile == null) return;

            var result = LyricService.LoadFromLrcFile(lrcFile);
            if (result.HasLyrics)
            {
                _npCurrentLyrics = result;
                _npCurrentLyricIndex = -1;
                NpBuildLyricLines();
            }
        }

        // ─── NP Load LRC File (file picker) ───

        private void NpLoadLrcFile_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "LRC files (*.lrc)|*.lrc|All files (*.*)|*.*",
                Title = "Select LRC lyrics file"
            };
            if (dlg.ShowDialog() == true)
            {
                var result = LyricService.LoadFromLrcFile(dlg.FileName);
                if (result.HasLyrics)
                {
                    _npCurrentLyrics = result;
                    _npCurrentLyricIndex = -1;
                    NpBuildLyricLines();
                }
            }
        }

        // ─── NP Search LRCLIB ───

        private async void NpSearchLyrics_Click(object sender, RoutedEventArgs e)
        {
            if (ThemeManager.OfflineModeEnabled)
            {
                ShowOfflineNotice("Online lyrics search");
                return;
            }
            if (_player.CurrentFile == null) return;

            var currentFile = _files.FirstOrDefault(f =>
                string.Equals(f.FilePath, _player.CurrentFile, StringComparison.OrdinalIgnoreCase));
            if (currentFile == null) return;

            var artist = currentFile.Artist ?? "";
            var title = currentFile.Title ?? currentFile.FileName ?? "";

            if (string.IsNullOrWhiteSpace(artist) && string.IsNullOrWhiteSpace(title)) return;

            NpSongSpecs.Text = "Searching lyrics...";

            try
            {
                var results = await LyricService.SearchLrcLibAsync(artist, title);
                if (results.Count > 0)
                {
                    var best = results.FirstOrDefault(r => r.HasSyncedLyrics)
                               ?? results.FirstOrDefault(r => r.HasPlainLyrics);
                    if (best != null)
                    {
                        _npCurrentLyrics = LyricService.ApplySearchResult(best);
                        _npCurrentLyricIndex = -1;
                        NpBuildLyricLines();
                        // Restore specs text
                        NpSongSpecs.Text = "";
                        if (_player.CurrentFile != null)
                        {
                            var cf = _files.FirstOrDefault(f =>
                                string.Equals(f.FilePath, _player.CurrentFile, StringComparison.OrdinalIgnoreCase));
                            if (cf != null) NpSetTrack(cf);
                        }
                        return;
                    }
                }
                NpSongSpecs.Text = "No lyrics found";
            }
            catch
            {
                NpSongSpecs.Text = "Search failed";
            }
        }

        /// <summary>Update the Now Playing panel when a new track starts.</summary>
        private void UpdateNowPlayingView()
        {
            if (_npVisible && _player.CurrentFile != null)
            {
                var currentFile = _files.FirstOrDefault(f =>
                    string.Equals(f.FilePath, _player.CurrentFile, StringComparison.OrdinalIgnoreCase));
                if (currentFile != null)
                    NpSetTrack(currentFile);
            }
        }

        private void NpLoadPreferences()
        {
            if (_npPrefsLoaded) return;
            _npPrefsLoaded = true;
            _npVisualizerEnabled = ThemeManager.NpVisualizerEnabled;
            _npColorMatchEnabled = ThemeManager.NpColorMatchEnabled;
            _npLyricsHidden = ThemeManager.NpLyricsHidden;
            _npTranslateEnabled = ThemeManager.NpTranslateEnabled;
            _npKaraokeEnabled = ThemeManager.NpKaraokeEnabled;
            _npVisualizerStyle = ThemeManager.NpVisualizerStyle;
            _npVizPlacement = ThemeManager.NpVizPlacement;
            _npSubCoverShowArtist = ThemeManager.NpSubCoverShowArtist;
            _npCoverSize = ThemeManager.NpCoverSize;
            _npTitleSize = ThemeManager.NpTitleSize;
            _npSubTextSize = ThemeManager.NpSubTextSize;
            _npLyricsSize = ThemeManager.NpLyricsSize;
            _npVizSize = ThemeManager.NpVizSize;
            _npLyricsOffsetX = ThemeManager.NpLyricsOffsetX;
            _npCoverOffsetX = ThemeManager.NpCoverOffsetX;
            _npCoverOffsetY = ThemeManager.NpCoverOffsetY;
            _npTitleOffsetX = ThemeManager.NpTitleOffsetX;
            _npTitleOffsetY = ThemeManager.NpTitleOffsetY;
            _npArtistOffsetX = ThemeManager.NpArtistOffsetX;
            _npArtistOffsetY = ThemeManager.NpArtistOffsetY;
            _npVizOffsetY = ThemeManager.NpVizOffsetY;
        }

        private void NpSavePreferences()
        {
            ThemeManager.NpVisualizerEnabled = _npVisualizerEnabled;
            ThemeManager.NpColorMatchEnabled = _npColorMatchEnabled;
            ThemeManager.NpLyricsHidden = _npLyricsHidden;
            ThemeManager.NpTranslateEnabled = _npTranslateEnabled;
            ThemeManager.NpKaraokeEnabled = _npKaraokeEnabled;
            ThemeManager.NpVisualizerStyle = _npVisualizerStyle;
            ThemeManager.NpVizPlacement = _npVizPlacement;
            ThemeManager.NpSubCoverShowArtist = _npSubCoverShowArtist;
            ThemeManager.NpCoverSize = _npCoverSize;
            ThemeManager.NpTitleSize = _npTitleSize;
            ThemeManager.NpSubTextSize = _npSubTextSize;
            ThemeManager.NpLyricsSize = _npLyricsSize;
            ThemeManager.NpVizSize = _npVizSize;
            ThemeManager.NpLyricsOffsetX = _npLyricsOffsetX;
            ThemeManager.NpCoverOffsetX = _npCoverOffsetX;
            ThemeManager.NpCoverOffsetY = _npCoverOffsetY;
            ThemeManager.NpTitleOffsetX = _npTitleOffsetX;
            ThemeManager.NpTitleOffsetY = _npTitleOffsetY;
            ThemeManager.NpArtistOffsetX = _npArtistOffsetX;
            ThemeManager.NpArtistOffsetY = _npArtistOffsetY;
            ThemeManager.NpVizOffsetY = _npVizOffsetY;
            ThemeManager.SavePlayOptions();
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
                PlayFile(playFile, isManualSkip: true);
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
            // Media playback controls
            else if (e.Key == Key.MediaPlayPause)
            {
                PlayPause_Click(this, new RoutedEventArgs());
                e.Handled = true;
            }
            else if (e.Key == Key.MediaNextTrack)
            {
                NextTrack_Click(this, new RoutedEventArgs());
                e.Handled = true;
            }
            else if (e.Key == Key.MediaPreviousTrack)
            {
                PrevTrack_Click(this, new RoutedEventArgs());
                e.Handled = true;
            }
            else if (e.Key == Key.MediaStop)
            {
                if (_player.IsPlaying)
                    PlayPause_Click(this, new RoutedEventArgs());
                e.Handled = true;
            }
            // Arrow key controls: Left/Right = seek, Up/Down = volume
            else if (e.Key == Key.Left)
            {
                _player.SeekRelative(-5);
                e.Handled = true;
            }
            else if (e.Key == Key.Right)
            {
                _player.SeekRelative(5);
                e.Handled = true;
            }
            else if (e.Key == Key.Up)
            {
                double newVol = Math.Min(100, VolumeSlider.Value + 5);
                VolumeSlider.Value = newVol;
                if (_npVisible) NpVolumeSlider.Value = newVol;
                e.Handled = true;
            }
            else if (e.Key == Key.Down)
            {
                double newVol = Math.Max(0, VolumeSlider.Value - 5);
                VolumeSlider.Value = newVol;
                if (_npVisible) NpVolumeSlider.Value = newVol;
                e.Handled = true;
            }
            else if (e.Key == Key.M)
            {
                // Toggle mute
                if (_npVisible)
                    NpVolumeIcon_Click(this, new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left) { RoutedEvent = MouseLeftButtonUpEvent });
                else
                    VolumeIcon_Click(this, new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left) { RoutedEvent = MouseLeftButtonUpEvent });
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
