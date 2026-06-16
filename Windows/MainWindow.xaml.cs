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
    public partial class MainWindow : Window
    {
        private readonly ObservableCollection<AudioFileInfo> _files = new();
        // Top-level paths the user actually added this session (folders or files).
        // Persisted by SessionRestoreService so "restore last session" can reload them.
        private readonly List<string> _sessionRootPaths = new();
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
        private AnalysisSettingsSnapshot? _analysisSettingsSnapshot;
        private ManualResetEventSlim _analysisPauseEvent = new(true); // signaled = not paused

        // Audio player
        private readonly AudioPlayer _player = new();
        private readonly DispatcherTimer _playerTimer;
        private bool _isSeeking;
        private bool _npIsSeeking;  // drag guard for NP seek slider

        // SMTC (media session for FluentFlyout/Windows media overlay)
        private SmtcService? _smtc;

        // System tray icon fields (_trayIcon, _forceClose) live in MainWindow.Tray.cs.

        // Search
        private string _searchText = "";
        private AudioStatus? _statusFilter = null;
        private bool _mismatchedBitrateFilter;

        // Drag-from-grid: track whether we initiated an outbound drag
        private bool _isOutboundDrag;

        // Seek cooldown to prevent snap-back
        private DateTime _lastSeekTime = DateTime.MinValue;
        private TimeSpan _lastSeekTargetPosition = TimeSpan.Zero;

        // Track the currently displayed spectrogram file
        private AudioFileInfo? _currentSpectrogramFile;

        // In-memory spectrogram cache keyed by "filePath|linearScale|channel|magma|hifi"
        // Cleared when spectrogram settings change or manually via settings
        private readonly Dictionary<string, System.Windows.Media.Imaging.BitmapSource> _spectrogramCache =
            new(StringComparer.OrdinalIgnoreCase);
        private readonly LinkedList<string> _spectrogramCacheLru = new();
        private const int SpectrogramCacheMaxEntries = 30;

        private readonly ColorThemeService _npColorThemeService = new();

        // Background color preload for the next track
        private CancellationTokenSource? _npPreloadCts;

        // Cover image bytes cache for instant track switching (path-hash → image bytes)
        private readonly Dictionary<string, byte[]> _coverBytesCache = new(StringComparer.OrdinalIgnoreCase);
        private readonly object _coverBytesCacheLock = new();
        private const int CoverBytesCacheMaxEntries = 20;
        private string? _npBackdropCacheKey;
        private BitmapSource? _npBackdropCachedSource;
        private ImageSource? _npBackdropCustomSource;
        private string? _npBackdropCustomPath;

        // Generation counter to prevent stale async color tasks from overwriting current track
        private int _npColorGeneration;

        // Queue system
        private readonly ObservableCollection<AudioFileInfo> _queue = new();

        // Mini player window
        private MiniPlayerWindow? _miniPlayerWindow;

        // Shuffle mode — uses a pre-shuffled deck that is rebuilt when exhausted
        private bool _shuffleMode;
        private readonly ShuffleEngine _shuffleEngine = new();

        // Now Playing panel state
        private DispatcherTimer? _npUpdateTimer;
        private DispatcherTimer? _npLyricsScrollTimer;
        private DispatcherTimer? _npLyricStatusRestoreTimer;
        private CancellationTokenSource? _npLyricsCts;
        private TimeSpan _npLastPlaybarRenderPosition = TimeSpan.MinValue;
        private DateTime _npLastPlaybarRenderUtc = DateTime.MinValue;
        private LyricsResult _npCurrentLyrics = LyricsResult.Empty;
        private int _npCurrentLyricIndex = -1;
        private readonly List<TextBlock> _npLyricTextBlocks = new();
        private bool _npLyricsAutoScrolling;
        private DateTime _npLyricsManualScrollPauseUntilUtc = DateTime.MinValue;
        private LyricService.LyricProvider _npLyricProvider = LyricService.LyricProvider.Auto;
        private bool _npVisible;
        private enum NpLifecycleState
        {
            Hidden,
            Suspended,
            Resuming,
            Active
        }
        private NpLifecycleState _npLifecycleState = NpLifecycleState.Hidden;
        private bool _npResumeVisibleWorkRunning;
        private DateTime _npLastResumeVisibleWorkUtc = DateTime.MinValue;
        private bool _npVisualizerEnabled;
        private bool _npColorMatchEnabled;
        private DateTime _lastTrackFinishedTime = DateTime.MinValue;
        private volatile bool _crossfadeEarlyTriggered;
        // Single-flight latch for gapless pre-buffering: the 50ms player timer would otherwise
        // spawn a fresh PrepareGapless task on every tick (~20 concurrent during the <5s window),
        // each nulling _gaplessNext mid-scan and racing the track switch into a frozen UI.
        private volatile bool _gaplessPrepInFlight;
        private DateTime _lastOfflineNoticeTime = DateTime.MinValue;
        private string? _npLastTrackPath; // track which song is loaded in NP to detect changes
        private int _npVisualizerStyle; // NP has its own style selection
        private bool _mainVizWasActive; // remember main visualizer state when NP takes over
        private bool _npPlaybarCycleRendering;
        private bool _npLyricRendering; // frame-synced lyric-highlight loop (tighter than the 50ms timer)

        // Album colors for color-match theming (set in NpApplyGlow)
        private System.Windows.Media.Color _npAlbumPrimary;
        private System.Windows.Media.Color _npAlbumSecondary;
        private System.Windows.Media.Color _npAlbumTertiary;
        private System.Windows.Media.Color _npAlbumBackground;

        // Album colors for main-window color-match theming
        private System.Windows.Media.Color _mainAlbumPrimary;
        private System.Windows.Media.Color _mainAlbumSecondary;
        private System.Windows.Media.Color _mainAlbumTertiary;
        private System.Windows.Media.Color _npVizColorPrimary;   // for visualizer tinting
        private System.Windows.Media.Color _npVizColorSecondary; // for visualizer tinting

        // ColorMatch eyedropper: up to 3 user-picked colors that override the auto-extracted
        // album palette for the current track.
        private bool _npColorPickerActive;
        private readonly List<System.Windows.Media.Color> _npColorPickerOverrides = new(6);
        private int _npColorPickerNextSlot;
        private int _npColorPickerSessionPicks;
        private string? _npColorPickerOwnerPath;
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
        private bool _npTranslationInProgress;
        private bool _npKaraokeEnabled;           // word-by-word karaoke mode
        private NpLyricDisplayMode _npLyricMode;   // Standard / Blur / Uniform lyric display
        private bool _npPendingVisibleRefresh;    // defer NP lyric/UI loads while hidden or minimized
        private int _npLyricsVersion;              // incremented each track change to discard stale lyrics
        private bool _npLyricsNeedCatchUp;         // force one immediate highlight update on next tick
        private bool _npSubCoverShowArtist = true; // true = Artist (default), false = Queue preview
        private bool _npPrefsLoaded;              // one-time load from ThemeManager
        private int _npCoverSize;                 // custom album cover size (0 = default)
        private int _npTitleSize;                 // custom title font size (0 = default)
        private int _npSubTextSize;               // custom artist/up-next font size (0 = default)
        private int _npLyricsSize;                // custom lyrics font size (0 = default)
        private int _npVizSize;                   // custom visualizer height (0 = default)
        private double _npCoverGlowSize = 1.0;    // album cover glow scale (0 = off, 1 = default, >1 larger)
        private double _npFocusedLyricsBlurRadius = 6.5;
        private bool _npCoverGlowMotionEnabled = true;   // animate album glow gradient while NP is visible
        private GlowMotionMode _npGlowMotionMode = GlowMotionMode.Swirl;
        // Random-mode state — palette swap between "current" and a tween target every ~2s
        private Color _npGlowRandomCurrentA, _npGlowRandomCurrentB;
        private Color _npGlowRandomTargetA,  _npGlowRandomTargetB;
        private double _npGlowRandomTweenT = 1.0;
        private long _npGlowRandomLastSwapMs;
        private const int NpGlowRandomSwapMs = 2000;
        private const int NpGlowRandomTweenDurationMs = 600;
        private static readonly Random _npGlowRandomRng = new();
        private int _npLyricsOffsetX;             // horizontal lyrics offset in px (0 = default ~24px margin)
        private int _npCoverOffsetX;              // cover horizontal position offset
        private int _npCoverOffsetY;              // cover vertical position offset
        private int _npTitleOffsetX;              // title horizontal position offset
        private int _npTitleOffsetY;              // title vertical position offset
        private int _npArtistOffsetX;             // artist horizontal position offset
        private int _npArtistOffsetY;             // artist vertical position offset
        private int _npVizOffsetY;                 // visualizer vertical position offset
        private bool _npLayoutProfileIsFullscreen; // active persisted layout profile
        private bool _npLayoutProfileVisualizerEnabled;
        private DispatcherTimer? _npBgAnimTimer;  // animated background timer
        private DispatcherTimer? _npBackgroundCycleTimer;
        private bool _npBgFxPopupSyncing;
        private readonly List<UIElement> _npStarShapes = new();
        private double _npStarfieldWidth;
        private double _npStarfieldHeight;
        private double _npStarfieldDensity = 1.0;
        private double _npStarfieldShootingStarDensity = 1.0;
        private bool _npStarfieldShootingStars = true;
        private string _npBgFxPaletteKey = "fixed";
        private string _npBgFxMode = "Off";       // active particle-bg mode (Stars/Rain/Snow)
        private DispatcherTimer? _npShootingStarTimer; // sporadic shooting-star spawner
        private DispatcherTimer? _npLightningTimer;    // sporadic rain lightning spawner
        private DispatcherTimer? _npFishTimer;         // sporadic underwater fish spawner
        private readonly List<Canvas> _npShootingStarPool = new();
        private System.Windows.Shapes.Rectangle? _npLightningOverlay;
        // _feedbackUsageTimer and other overlay state live in MainWindow.Overlays.cs.

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
        private System.Windows.Media.Effects.BlurEffect? _npFocusedLyricsInactiveBlur;
        private Panel? _eqPanelHome;
        private int _eqPanelHomeIndex = -1;

        private System.Windows.Media.Effects.BlurEffect NpGetFocusedLyricsInactiveBlur()
        {
            double radius = Math.Clamp(_npFocusedLyricsBlurRadius, 0, 16.0);
            if (_npFocusedLyricsInactiveBlur != null
                && Math.Abs(_npFocusedLyricsInactiveBlur.Radius - radius) < 0.001)
            {
                return _npFocusedLyricsInactiveBlur;
            }

            var blur = new System.Windows.Media.Effects.BlurEffect
            {
                Radius = radius,
                KernelType = System.Windows.Media.Effects.KernelType.Gaussian,
                RenderingBias = System.Windows.Media.Effects.RenderingBias.Performance
            };
            blur.Freeze();
            _npFocusedLyricsInactiveBlur = blur;
            return blur;
        }

        private static void ObserveUiTask(Task task, string operation)
        {
            if (task.IsCompletedSuccessfully)
                return;

            _ = ObserveUiTaskAsync(task, operation);
        }

        private static async Task ObserveUiTaskAsync(Task task, string operation)
        {
            try
            {
                await task.ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"{operation} failed: {ex}");
            }
        }

        // Playback history for back-button navigation
        private readonly List<AudioFileInfo> _playHistory = new();
        private int _playHistoryIndex = -1;
        private bool _navigatingHistory; // true when playing from history (prevents re-pushing)

        // Animated waveform state (fields) lives in MainWindow.Waveform.cs.

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
        private bool _isResumingAnimations; // re-entrancy guard
        private DispatcherTimer? _occlusionCheckTimer;

        // Spectrogram options
        private bool _spectrogramLinearScale;
        private SpectrogramChannel _spectrogramChannel = SpectrogramChannel.Mono;
        private bool _spectrogramEndZoom;
        private double _spectrogramZoomLevel = 1.0;

        // Integrations
        private readonly DiscordRichPresenceService _discord = new();
        private readonly ScrobbleManager _scrobbler = new();
        // Most recent scrobble submission failure for the current track; surfaced in the widget
        // and cleared when a new track starts. Null when the last submission succeeded.
        private string? _lastScrobbleError;
        private LastFmScrobbler? _lastFm;
        private LibreFmScrobbler? _libreFm;
        private ListenBrainzScrobbler? _listenBrainz;
        private MalojaScrobbler? _maloja;

        // EQ sliders
        private Slider[] _eqSliders = Array.Empty<Slider>();
        private TextBlock[] _eqValueLabels = Array.Empty<TextBlock>();

        // Mute state
        private bool _isMuted;
        private double _preMuteVolume = 100;

        // Previous track: restart vs go-back
        private DateTime _lastPrevClickTime = DateTime.MinValue;

        // Canonical sets live in AudioAuditor.Core (SupportedFormats) so the GUI and CLI share one list.
        private static readonly IReadOnlySet<string> SupportedExtensions = Services.SupportedFormats.AudioExtensions;
        private static readonly IReadOnlySet<string> ArchiveExtensions = Services.SupportedFormats.ArchiveExtensions;

        private static readonly HashSet<string> PlaylistExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".m3u", ".m3u8", ".pls"
        };

        public MainWindow()
        {
            InitializeComponent();
            WaveformCanvas.SizeChanged += WaveformCanvas_SizeChanged;
            NpWaveformCanvas.SizeChanged += WaveformCanvas_SizeChanged;

            // Feed live, non-private app state into crash logs (no paths/metadata).
            LocalCrashLogger.AppStateProvider = () =>
                $"Analyzing={_isAnalyzing}; ActiveBatches={_activeBatches}; Files={_files.Count}";

            // A theme change writes global resources that clobber the NP ColorMatch overrides.
            // Re-apply ColorMatch when NP is visible so album colors survive a theme switch.
            ThemeManager.ThemeChanged += OnThemeChangedReapplyColorMatch;

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

                            // Apply privacy / opt-in choices from welcome dialog
                            ThemeManager.StatsCollectionEnabled = dlg.EnableStatsCollection;
                            ThemeManager.CrashLoggingEnabled = dlg.EnableCrashLogging;
                            ThemeManager.SavePlayOptions();

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

                            ThemeManager.SyncHiddenColumnsWithAnalysisOptions();
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

            // Record usage day and check for 30-day donation popup
            RecordUsageDay();
            Dispatcher.InvokeAsync(() =>
            {
                try { Check30DayDonationPopup(); }
                catch { /* never block startup */ }
            }, DispatcherPriority.Background);
            StartFeedbackUsageTimer();

            // Load scan cache if enabled
            if (ThemeManager.ScanCacheEnabled)
                ScanCacheService.EnsureLoaded();

            // Restore saved column layout (order + widths)
            RestoreColumnLayout();
            ApplyColumnVisibility();
            ApplyMainCustomBackground();

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

            // Persist column layout when the user reorders or resizes columns, not only
            // on close — a crash skips OnClosed and would otherwise lose the layout.
            HookColumnLayoutPersistence();

            // Set up filtered view
            _filteredView = CollectionViewSource.GetDefaultView(_files);
            _filteredView.Filter = SearchFilter;
            _filteredView.GroupDescriptions.Add(new PropertyGroupDescription("FolderPath"));
            FileGrid.ItemsSource = _filteredView;
            FileGrid.AddHandler(ScrollViewer.ScrollChangedEvent, new ScrollChangedEventHandler(FileGrid_ScrollChanged));

            _player.PlaybackStopped += Player_PlaybackStopped;
            _player.TrackFinished += Player_TrackFinished;
            _player.GaplessTrackChanged += Player_GaplessTrackChanged;

            _playerTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
            _playerTimer.Tick += PlayerTimer_Tick;

            // Initialize music service button labels
            UpdateServiceButtonLabels();
            ApplyToolbarButtonVisibility();

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

            // Initialize scrobblers
            InitializeScrobblers();
            UpdateScrobbleWidgetVisual();
            _scrobbler.StateChanged += (_, __) => Dispatcher.Invoke(UpdateScrobbleWidgetVisual);
            _scrobbler.SubmissionFailed += (_, msg) => Dispatcher.Invoke(() =>
            {
                _lastScrobbleError = msg;
                if (ThemeManager.CrashLoggingEnabled)
                    try { LocalCrashLogger.Write(new Exception(msg)); } catch { }
                UpdateScrobbleWidgetVisual();
            });

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
                ObserveUiTask(Task.Run(async () =>
                {
                    try
                    {
                        string currentVersion = System.Reflection.Assembly.GetExecutingAssembly()
                            .GetName().Version is { } v ? $"{v.Major}.{v.Minor}.{v.Build}" : "0.0.0";
                        bool hasUpdate = await UpdateChecker.CheckForUpdateAsync(currentVersion);
                        if (hasUpdate && UpdateChecker.LatestVersion != null)
                        {
                            await Dispatcher.InvokeAsync(() =>
                            {
                                UpdateLatestText.Text = $"AudioAuditor v{UpdateChecker.LatestVersion} is available!";
                                UpdateCurrentText.Text = $"You're currently on v{currentVersion}";
                                UpdateOverlay.Visibility = Visibility.Visible;
                            });
                        }
                    }
                    catch { /* silently ignore update check failures */ }
                }), "StartupUpdateCheck");
            }
        }

        // System tray setup (InitializeTrayIcon, RestoreFromTray, DarkColorTable) lives in MainWindow.Tray.cs.

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
            if (_smtc != null) return;
            _smtc = new SmtcService();
            _smtc.Initialize(hwnd);
            _smtc.PlayRequested += (_, _) => Dispatcher.InvokeAsync(() => { if (_player.IsPaused) _player.Resume(); });
            _smtc.PauseRequested += (_, _) => Dispatcher.InvokeAsync(() => { if (_player.IsPlaying) _player.Pause(); });
            _smtc.NextRequested += (_, _) => Dispatcher.InvokeAsync(() => NextTrack_Click(this, new RoutedEventArgs()));
            _smtc.PreviousRequested += (_, _) => Dispatcher.InvokeAsync(() => PrevTrack_Click(this, new RoutedEventArgs()));

            // If the previous run ended in a crash (and relaunched us), consume the
            // breadcrumb and show a subtle "recovered from a problem" notice.
            Dispatcher.InvokeAsync(MaybeShowCrashRecoveryNotice,
                System.Windows.Threading.DispatcherPriority.Background);

            // Consume any file/folder paths passed via "Open With" / drag-onto-exe at startup
            if (App.PendingStartupPaths is { Count: > 0 } pending)
            {
                Dispatcher.InvokeAsync(() => LoadPathsFromExternal(pending),
                    System.Windows.Threading.DispatcherPriority.Background);
            }
            else
            {
                // No startup paths — offer to restore the last session (if enabled and one exists).
                // Deferred to Background priority so the window paints first.
                Dispatcher.InvokeAsync(MaybeOfferSessionRestore,
                    System.Windows.Threading.DispatcherPriority.Background);
            }
        }

        // MaybeShowCrashRecoveryNotice lives in MainWindow.Overlays.cs.

        private const int WM_MOUSEHWHEEL = 0x020E;
        private const int WM_COPYDATA = 0x004A;
        // Magic ID identifying WM_COPYDATA messages from another instance carrying paths.
        // Pinned: changing this number breaks cross-instance "Open With" forwarding.
        public const int OpenPathsCopyDataId = 0x415541;

        [StructLayout(LayoutKind.Sequential)]
        private struct CopyDataStruct
        {
            public IntPtr dwData;
            public int cbData;
            public IntPtr lpData;
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr hParam, ref bool handled)
        {
            if (msg == WM_MOUSEHWHEEL)
            {
                // wParam high word is the delta (positive = scroll right, negative = scroll left)
                int delta = (short)(wParam.ToInt64() >> 16);
                var scrollViewer = FindVisualChild<ScrollViewer>(FileGrid);
                if (scrollViewer != null)
                {
                    ScrollFileGridHorizontally(scrollViewer, delta);
                    handled = true;
                }
            }
            else if (msg == WM_COPYDATA)
            {
                try
                {
                    var cds = Marshal.PtrToStructure<CopyDataStruct>(hParam);
                    if (cds.dwData.ToInt32() == OpenPathsCopyDataId && cds.cbData > 0 && cds.lpData != IntPtr.Zero)
                    {
                        string payload = Marshal.PtrToStringUni(cds.lpData, cds.cbData / 2) ?? "";
                        var paths = payload.Split('\n', StringSplitOptions.RemoveEmptyEntries);
                        Dispatcher.InvokeAsync(() =>
                        {
                            if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
                            Activate();
                            LoadPathsFromExternal(paths);
                        });
                        handled = true;
                    }
                }
                catch { }
            }
            return IntPtr.Zero;
        }

        protected override void OnClosed(EventArgs e)
        {
            SaveColumnLayout();
            // Final session snapshot for restore-on-next-launch (no-op unless enabled).
            SaveSessionState(cleanExit: true);
            StopWaveformAnimation();
            StopVisualizer();
            _playerTimer.Stop();
            _donationTimer?.Stop();
            _feedbackUsageTimer?.Stop();
            _occlusionCheckTimer?.Stop();

            this.Activated -= OnWindowActivated;
            this.Deactivated -= OnWindowDeactivated;
            this.StateChanged -= OnWindowStateChanged;
            CompositionTarget.Rendering -= WaveformAnimation_Tick;
            CompositionTarget.Rendering -= Visualizer_Tick;
            EnsurePlaybarAnimRendering(false);
            if (_occlusionCheckTimer != null) _occlusionCheckTimer.Tick -= OcclusionCheckTimer_Tick;
            if (_vizCycleTimer != null) _vizCycleTimer.Tick -= VizCycleTimer_Tick;
            if (_npUpdateTimer != null) _npUpdateTimer.Tick -= NpUpdateTimer_Tick;
            _npLyricsScrollTimer?.Stop();
            _npLyricsScrollTimer = null;
            NpCancelLyricsWork(invalidateVersion: true);
            if (_npBgAnimTimer != null) _npBgAnimTimer.Tick -= NpBgAnim_Tick;
            if (_npGlowPulseTimer != null) _npGlowPulseTimer.Tick -= NpGlowPulse_Tick;
            NpLayoutPopup.Closed -= NpLayoutPopup_Closed;

            _miniPlayerWindow?.Close();
            _analysisSemaphore?.Dispose();
            _shLabsSemaphore?.Dispose();
            _analysisPauseEvent?.Dispose();
            _player.Dispose();
            _discord.Dispose();
            _scrobbler.Dispose();
            _smtc?.Dispose();
            SaveNpColorCacheToDisk();
            base.OnClosed(e);
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
            _refreshCts?.Cancel(); // also stop an in-flight Refresh
            _isAnalyzing = false;
            _activeBatches = 0;
            _analysisTotal = 0;
            _analysisCompleted = 0;
            _analysisSettingsSnapshot = null;
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

        // File Analysis (multi-threaded) - see Analysis.cs


        // Donation, 30-day donation, feedback-one-hour, themed notice, and restore-session
        // overlays live in MainWindow.Overlays.cs.

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
            try
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
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[UpdateDownloadInstall_Click] {ex}");
                UpdateProgressText.Text = "Update failed.";
                BtnUpdateDownloadInstall.IsEnabled = true;
                BtnUpdateOpenBrowser.IsEnabled = true;
            }
        }

        // Feature-config, SH Labs privacy + scan-limit, and footer-support overlays live in
        // MainWindow.Overlays.cs.

        private void Wrapped_Click(object sender, RoutedEventArgs e)
        {
            // In-app overlay (not a separate window) so it fits the current window size.
            ShowWrappedOverlay();
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

        // Audio Player - see Playback.cs
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
            LoadPathsFromExternal(droppedPaths);
        }

        /// <summary>
        /// Loads files and folders given by absolute path. Called from the in-window drag/drop
        /// handler, from command-line args at startup, and from WM_COPYDATA messages forwarded
        /// by a second instance launched via Open With or "drag onto the exe".
        /// </summary>
        public void LoadPathsFromExternal(IEnumerable<string> paths)
        {
            if (paths == null) return;
            var audioFiles = new List<string>();
            foreach (var path in paths)
            {
                if (string.IsNullOrWhiteSpace(path)) continue;
                // Remember the user-supplied root (folder or file) for session restore.
                if ((Directory.Exists(path) || File.Exists(path)) &&
                    !_sessionRootPaths.Contains(path, StringComparer.OrdinalIgnoreCase))
                    _sessionRootPaths.Add(path);
                if (Directory.Exists(path))
                {
                    try
                    {
                        audioFiles.AddRange(
                            Directory.EnumerateFiles(path, "*.*", SearchOption.AllDirectories)
                                     .Where(f => SupportedExtensions.Contains(IOPath.GetExtension(f))
                                              || ArchiveExtensions.Contains(IOPath.GetExtension(f))
                                              || IOPath.GetExtension(f).Equals(".cue", StringComparison.OrdinalIgnoreCase)));
                    }
                    catch { }
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

        /// <summary>
        /// Persists the current working set (added roots + loaded file paths) so the app
        /// can offer to restore it next launch. No-op unless the user enabled restore.
        /// <paramref name="cleanExit"/>/<paramref name="crashSnapshot"/> tag why we saved.
        /// </summary>
        public void SaveSessionState(bool cleanExit = false, bool crashSnapshot = false)
        {
            try
            {
                if (!ThemeManager.RestoreLastSessionEnabled) return;
                // Real on-disk file paths only — skip cue virtual tracks (path#CUEnn).
                var files = _files
                    .Where(f => !f.IsCueVirtualTrack && !string.IsNullOrEmpty(f.FilePath))
                    .Select(f => f.FilePath);
                SessionRestoreService.Save(_sessionRootPaths, files, cleanExit, crashSnapshot);
            }
            catch { /* never let session saving break the app */ }
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

        private void BatchMetadata_Click(object sender, RoutedEventArgs e)
        {
            var selected = FileGrid.SelectedItems.Cast<AudioFileInfo>().ToList();
            if (selected.Count == 0 && FileGrid.SelectedItem is AudioFileInfo current)
                selected.Add(current);
            if (selected.Count == 0) return;

            var batch = new BatchMetadataWindow(selected, this);
            batch.ShowDialog();

            if (batch.MetadataChanged)
                _filteredView?.Refresh();
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
            try
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
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[WriteReplayGain_Click] {ex}");
                MessageBox.Show($"Replay Gain failed: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
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
                ObserveUiTask(UpdateAlbumCoverAsync(), nameof(UpdateAlbumCoverAsync));
            }
            else
            {
                AlbumCoverColumn.Width = new GridLength(0);
                AlbumCoverPanel.Visibility = Visibility.Collapsed;
                AlbumCoverImage.Source = null;
            }
        }

        private async Task UpdateAlbumCoverAsync(string? expectedPath = null, int generation = -1)
        {
            try
            {
                if (!_showAlbumCover) return;

                AudioFileInfo? file = null;
                if (!string.IsNullOrEmpty(expectedPath))
                    file = _files.FirstOrDefault(f => string.Equals(f.FilePath, expectedPath, StringComparison.OrdinalIgnoreCase));
                if (file == null && _player.CurrentFile != null)
                    file = _files.FirstOrDefault(f => string.Equals(f.FilePath, _player.CurrentFile, StringComparison.OrdinalIgnoreCase));
                if (file == null)
                    file = FileGrid.SelectedItem as AudioFileInfo;

                if (file == null || string.IsNullOrEmpty(file.FilePath))
                {
                    AlbumCoverImage.Source = null;
                    return;
                }

                string filePath = file.FilePath;
                int coverGeneration = generation >= 0 ? generation : _npColorGeneration;

                try
                {
                    var cover = await Task.Run(() => ExtractAlbumCover(filePath));
                    if (coverGeneration != _npColorGeneration) return;
                    if (!string.IsNullOrEmpty(_player.CurrentFile) &&
                        !string.Equals(_player.CurrentFile, filePath, StringComparison.OrdinalIgnoreCase))
                        return;
                    AlbumCoverImage.Source = cover;
                }
                catch
                {
                    if (coverGeneration == _npColorGeneration)
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
            // If the NP screen is showing with ColorMatch on, tint Settings to match it.
            if (NpTryGetSettingsColorMatchBrushes(out var cmBrushes))
                settingsWindow.ApplyColorMatchTint(cmBrushes);
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

                // Sync scrobbler state from settings
                ApplyScrobbleSettings();
                UpdateScrobbleWidgetVisual();
                ApplyColumnVisibility();
                NpUpdateCrossfadeIcon();
                NpUpdateAutoPlayIcon();

                // Settings can change theme / playbar theme / ColorMatch toggle. Re-apply NP
                // ColorMatch so the Now Playing screen reflects the change (and, when ColorMatch
                // is on, stays album-colored rather than picking up the new theme).
                NpResetColorMatchCaches();
                NpApplyColorMatchMode();
                NpRenderPlaybarStyle();
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

        // Mini Player (MiniPlayer_Click, RefreshVisualizerOwnershipAfterMiniChange) lives in
        // MainWindow.WindowState.cs.

        // ═══════════════════════════════════════════
        //  Queue
        // ═══════════════════════════════════════════

        private void Queue_Click(object sender, RoutedEventArgs e)
        {
            var queueWindow = new QueueWindow(_queue) { Owner = this, UpNext = GetUpNextTracks() };
            if (ThemeManager.MainColorMatchEnabled && _mainAlbumPrimary != default)
            {
                var tertiary = _mainAlbumTertiary == default ? _mainAlbumSecondary : _mainAlbumTertiary;
                var background = Color.FromRgb(18, 22, 30);
                queueWindow.ApplyColorMatch(_mainAlbumPrimary, _mainAlbumSecondary, tertiary, background);
            }
            queueWindow.ShowDialog();
            if (_npVisible) NpUpdateQueuePopup();
        }

        private List<AudioFileInfo> GetUpNextTracks()
        {
            return GetUpcomingTracks(3);
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

        private void AutoRenameArtistTitle_Click(object sender, RoutedEventArgs e)
            => RunAutoRename(artistFirst: true);

        private void AutoRenameTitleArtist_Click(object sender, RoutedEventArgs e)
            => RunAutoRename(artistFirst: false);

        /// <summary>
        /// Auto-renames selected files to a canonical "Artist - Title.ext" or "Title - Artist.ext"
        /// using tag metadata as the source of truth (NOT the existing filename). This is robust to
        /// already-correct filenames (skipped untouched), filenames with leading track numbers
        /// (preserved), and filenames containing extra dashes (rebuilt cleanly from tags).
        /// Files without artist or title tags are skipped — never guessed.
        /// </summary>
        private void RunAutoRename(bool artistFirst)
        {
            var selected = FileGrid.SelectedItems.OfType<AudioFileInfo>().ToList();
            int renamed = 0, skipped = 0, failed = 0;
            foreach (var f in selected)
            {
                if (f.IsCueVirtualTrack) { skipped++; continue; }
                string artist = (f.Artist ?? "").Trim();
                string title = (f.Title ?? "").Trim();
                if (string.IsNullOrEmpty(artist) || string.IsNullOrEmpty(title))
                {
                    skipped++; continue;
                }

                string dir = IOPath.GetDirectoryName(f.FilePath) ?? "";
                string ext = IOPath.GetExtension(f.FilePath);
                string nameNoExt = IOPath.GetFileNameWithoutExtension(f.FilePath);

                // Preserve leading track-number prefix like "01 - ", "01. ", "1) ".
                string trackPrefix = "";
                var prefixMatch = System.Text.RegularExpressions.Regex.Match(
                    nameNoExt, @"^\s*\d{1,3}\s*[-.\)]\s+");
                if (prefixMatch.Success)
                    trackPrefix = prefixMatch.Value;

                string body = SanitizeForFilename(artistFirst
                    ? $"{artist} - {title}"
                    : $"{title} - {artist}");
                string desiredFull = $"{trackPrefix}{body}{ext}";
                string currentFile = IOPath.GetFileName(f.FilePath);

                if (string.Equals(currentFile, desiredFull, StringComparison.OrdinalIgnoreCase))
                {
                    // Already in target form — leave it alone.
                    skipped++; continue;
                }

                string newPath = IOPath.Combine(dir, desiredFull);
                if (File.Exists(newPath) &&
                    !string.Equals(newPath, f.FilePath, StringComparison.OrdinalIgnoreCase))
                {
                    // A different file already owns that name — don't overwrite.
                    skipped++; continue;
                }

                try
                {
                    File.Move(f.FilePath, newPath);
                    f.FilePath = newPath;
                    f.FileName = desiredFull;
                    renamed++;
                }
                catch { failed++; }
            }

            string note = artistFirst ? "Artist - Title" : "Title - Artist";
            StatusText.Text = failed > 0
                ? $"Auto rename ({note}): {renamed} renamed, {skipped} skipped, {failed} failed."
                : $"Auto rename ({note}): {renamed} renamed, {skipped} skipped.";
        }

        private static string SanitizeForFilename(string s)
        {
            foreach (char c in IOPath.GetInvalidFileNameChars())
                s = s.Replace(c, '_');
            // Collapse repeated spaces that may result from sanitization
            s = System.Text.RegularExpressions.Regex.Replace(s, @"\s+", " ").Trim();
            return s;
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

        // Animated waveform visualization (DrawWaveformBackground, Start/StopWaveformAnimation,
        // WaveformCanvas_SizeChanged, the render loop, and playbar-anim rendering) lives in
        // MainWindow.Waveform.cs.

        // ═══════════════════════════════════════════
        //  Animation Occlusion Pause
        // ═══════════════════════════════════════════
        // Window activate/deactivate/state-change handlers, the occlusion-check timer, the
        // other-app-fullscreen P/Invoke + structs, and IsAnotherAppFullscreen live in
        // MainWindow.WindowState.cs. PauseAnimations/ResumeAnimations stay here because they
        // bridge window-state, waveform, visualizer, and NP work.

        private void PauseAnimations(bool pauseNowPlayingWork = true)
        {
            if (pauseNowPlayingWork || !IsNowPlayingUiActive())
                StopVisualizer();
            StopWaveformAnimation();

            if (pauseNowPlayingWork && _npVisible)
            {
                NpSuspendVisibleWork(markPendingRefresh: true);
            }
        }

        private void ResumeAnimations(bool resumeNowPlayingWork = true)
        {
            if (_isResumingAnimations) return;
            _isResumingAnimations = true;
            try
            {
                if (resumeNowPlayingWork && IsNowPlayingUiActive())
                {
                    // Idempotent: if NP is already Active with its update timer running, there's
                    // nothing frozen to recover — skip the (expensive) lyric resync/rebuild.
                    bool alreadyRunning = _npLifecycleState == NpLifecycleState.Active
                        && _npUpdateTimer is { IsEnabled: true }
                        && !_npPendingVisibleRefresh;
                    if (!alreadyRunning)
                        NpResumeVisibleWork(forceReloadLyrics: _npPendingVisibleRefresh, forceLyricResync: true);
                }
                else
                {
                    if (!_npVisible && _visualizerMode && _player.IsPlaying)
                        StartVisualizer();
                    if (!_npVisible && _waveformData.Length > 0)
                        StartWaveformAnimation();
                }
            }
            finally
            {
                _isResumingAnimations = false;
            }
        }

        // IsAnotherAppFullscreen lives in MainWindow.WindowState.cs.
        // The waveform render loop (WaveformAnimation_Tick, RenderWaveformCanvas, GenerateWaveformData,
        // GetCurrentBassEnergy, the playbar-anim rendering, and waveform helpers) lives in
        // MainWindow.Waveform.cs.

        // Audio Visualizer + Spectrogram Scale - see Spectrogram.cs
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

    }
}
