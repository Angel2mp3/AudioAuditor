using System;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using AudioQualityChecker.Models;
using AudioQualityChecker.Services;

namespace AudioQualityChecker
{
    public partial class MiniPlayerWindow : Window
    {
        private readonly AudioPlayer _player;
        private readonly Action _onPrev;
        private readonly Action _onNext;
        private readonly Action _onRestore;
        private readonly Func<AudioFileInfo?> _getCurrentTrack;
        private readonly Action _onToggleVisualizer;
        private readonly Action _onToggleShuffle;

        private DispatcherTimer? _updateTimer;
        private DispatcherTimer? _vizTimer;
        private MiniVisualizerRenderer? _vizRenderer;
        private bool _isSeeking;
        private bool _isDragging;
        private Point _dragStartPoint;
        private bool _isMuted;
        private double _preMuteVolume = 80;
        private AudioFileInfo? _lastTrack;
        private DateTime _lastMiniSeekTime = DateTime.MinValue;
        private bool _externalVisualizerSuspended;
        private System.Windows.Interop.HwndSource? _hwndSource;

        // Window-height management: _baseHeight is the height of the no-visualizer layout; when the
        // visualizer is on we add VizExtra. The user's manual resize updates _baseHeight so it sticks.
        private const double VizExtra = 40;   // viz canvas (36) + top margin (4)
        private double _baseHeight = 112;     // sensible default with no dead space below the controls
        private bool _vizSpaceOn;
        private bool _suppressSizeSync;
        private bool _heightInitialized;      // true once the natural base height has been measured
        private double _restoredBaseHeight = double.NaN; // persisted base height to apply on first layout
        private bool _positionRestored;       // true once a saved on-screen position was applied

        // ColorMatch state
        private bool _colorMatchEnabled;
        private Color _albumPrimary;
        private Color _albumSecondary;

        public bool ColorMatchEnabled => _colorMatchEnabled;
        public bool IsMiniPlayerUiActive => IsVisible && WindowState != WindowState.Minimized;
        public bool IsExternalVisualizerSuspended => _externalVisualizerSuspended;
        public bool IsMiniVisualizerActive => _vizRenderer?.IsActive == true && _vizRenderer.Style != 3;

        public MiniPlayerWindow(
            AudioPlayer player,
            Action onPrev,
            Action onNext,
            Action onRestore,
            Func<AudioFileInfo?> getCurrentTrack,
            Action onToggleVisualizer,
            Action onToggleColorMatch,
            Action onToggleShuffle)
        {
            InitializeComponent();
            Topmost = ThemeManager.MiniPlayerAlwaysOnTop;
            _player = player;
            _onPrev = onPrev;
            _onNext = onNext;
            _onRestore = onRestore;
            _getCurrentTrack = getCurrentTrack;
            _onToggleVisualizer = onToggleVisualizer;
            // onToggleColorMatch is intentionally not stored: the miniplayer owns its ColorMatch
            // state and must not re-sync from the main window's flag (which would clear it). The
            // parameter is kept so the main↔mini sync paths (NpColors.cs) can still drive us directly.
            _ = onToggleColorMatch;
            _onToggleShuffle = onToggleShuffle;

            // Copy theme resources from application
            CopyThemeResources();

            // Setup update timer
            _updateTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
            _updateTimer.Tick += UpdateTimer_Tick;
            _updateTimer.Start();

            // Setup visualizer timer
            _vizTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
            _vizTimer.Tick += VizTimer_Tick;
            _vizRenderer = new MiniVisualizerRenderer(MiniVizCanvas, _player);
            ApplyVisualizerSettings();
            ApplyCoverShape();
            ApplyDefaultCoverGlow();

            // Restore the mini player's remembered size/position (positioning is finalized in Loaded).
            RestoreWindowBounds();

            // Restore the mini player's own ColorMatch choice across sessions. (If the main window
            // also has ColorMatch on, MainWindow re-applies its album colors right after construction;
            // this covers the case where only the mini player had ColorMatch enabled.)
            if (ThemeManager.MiniColorMatchEnabled)
            {
                var cmTrack = _getCurrentTrack();
                if (cmTrack != null)
                    ExtractAndApplyAlbumColors(cmTrack.FilePath);
            }

            // Window drag
            MouseLeftButtonDown += Window_MouseLeftButtonDown;
            MouseMove += Window_MouseMove;
            MouseLeftButtonUp += Window_MouseLeftButtonUp;
            IsVisibleChanged += MiniPlayerWindow_IsVisibleChanged;
            StateChanged += MiniPlayerWindow_StateChanged;
            CoverBorder.SizeChanged += CoverBorder_SizeChanged;
            SizeChanged += MiniPlayerWindow_SizeChanged;
            SourceInitialized += MiniPlayerWindow_SourceInitialized;
            Loaded += MiniPlayerWindow_Loaded;

            Closed += (_, _) =>
            {
                SaveWindowBounds();
                _hwndSource?.RemoveHook(WndProc);
                _hwndSource = null;
                MiniPlaybarAnimCanvas?.Children.Clear();
                _updateTimer?.Stop();
                if (_updateTimer != null)
                    _updateTimer.Tick -= UpdateTimer_Tick;
                _updateTimer = null;
                _vizTimer?.Stop();
                if (_vizTimer != null)
                    _vizTimer.Tick -= VizTimer_Tick;
                _vizTimer = null;
                MouseLeftButtonDown -= Window_MouseLeftButtonDown;
                MouseMove -= Window_MouseMove;
                MouseLeftButtonUp -= Window_MouseLeftButtonUp;
                IsVisibleChanged -= MiniPlayerWindow_IsVisibleChanged;
                StateChanged -= MiniPlayerWindow_StateChanged;
                CoverBorder.SizeChanged -= CoverBorder_SizeChanged;
                SizeChanged -= MiniPlayerWindow_SizeChanged;
            };

            // Sync the volume slider to the player's actual volume (was hardcoded to 80).
            VolumeSlider.Value = Math.Clamp(_player.Volume * 100.0, 0, 100);
            _preMuteVolume = VolumeSlider.Value > 0 ? VolumeSlider.Value : 80;

            UpdateTrackInfo();
            UpdatePlayState();
            RefreshTimerActivity();
        }

        // ─── Theme Resources ───

        private void CopyThemeResources()
        {
            var keys = new[]
            {
                "WindowBg", "TextPrimary", "TextSecondary", "TextMuted",
                "AccentColor", "PanelBg", "BorderColor",
                "ButtonBg", "ButtonHover", "ButtonPressed",
                "PlaybarAccentColor", "PlaybarSecondaryColor",
                // Glass tokens the XAML binds to (root border, cover bg, slider track) — without
                // these the miniplayer rendered with broken/transparent backgrounds.
                "GlassFloatingBg", "GlassBorderBrush", "GlassPanelBg", "ScrollBg"
            };
            foreach (var key in keys)
            {
                if (Application.Current.Resources.Contains(key))
                    this.Resources[key] = Application.Current.Resources[key];
            }
        }

        public void RefreshThemeResources()
        {
            CopyThemeResources();
            if (_colorMatchEnabled)
                ApplyColorMatch(_albumPrimary, _albumSecondary);
        }

        // ─── ColorMatch ───

        public void ApplyColorMatch(Color primary, Color secondary)
        {
            _albumPrimary = primary;
            _albumSecondary = secondary;
            _colorMatchEnabled = true;

            var accent = EnsureMinLuminance(primary, 150);
            var sec = secondary != default ? EnsureMinLuminance(secondary, 115) : accent;

            this.Resources["PlaybarAccentColor"] = new SolidColorBrush(accent);
            this.Resources["PlaybarSecondaryColor"] = new SolidColorBrush(sec);
            this.Resources["AccentColor"] = new SolidColorBrush(accent);

            // Tint background subtly
            var bg = Color.FromArgb(255,
                (byte)Math.Max(10, accent.R / 6),
                (byte)Math.Max(10, accent.G / 6),
                (byte)Math.Max(10, accent.B / 6));
            this.Resources["WindowBg"] = new SolidColorBrush(bg);

            // PanelBg slightly lighter
            var panel = Color.FromArgb(255,
                (byte)Math.Max(14, accent.R / 5),
                (byte)Math.Max(14, accent.G / 5),
                (byte)Math.Max(14, accent.B / 5));
            this.Resources["PanelBg"] = new SolidColorBrush(panel);

            // TextPrimary brightened accent
            var textPri = EnsureMinLuminance(accent, 200);
            this.Resources["TextPrimary"] = new SolidColorBrush(textPri);

            // TextSecondary = secondary
            this.Resources["TextSecondary"] = new SolidColorBrush(EnsureMinLuminance(sec, 140));

            // Button hover tinted
            var btnHover = Color.FromArgb(255,
                (byte)Math.Min(255, accent.R / 3 + 30),
                (byte)Math.Min(255, accent.G / 3 + 30),
                (byte)Math.Min(255, accent.B / 3 + 30));
            this.Resources["ButtonHover"] = new SolidColorBrush(btnHover);

            // ButtonBg dark tinted
            var btnBg = Color.FromArgb(255,
                (byte)Math.Max(16, accent.R / 5),
                (byte)Math.Max(16, accent.G / 5),
                (byte)Math.Max(16, accent.B / 5));
            this.Resources["ButtonBg"] = new SolidColorBrush(btnBg);

            // Tint the root border subtly
            RootBorder.BorderBrush = new SolidColorBrush(Color.FromArgb(80, accent.R, accent.G, accent.B));
            RootBorder.Background = (Brush)this.Resources["WindowBg"];
            _vizRenderer?.SetPalette(accent, sec);

            ApplyCoverGlow(accent, sec);
        }

        /// <summary>Drives the two cover glow layers from the album colours (mirrors the NowPlaying glow).</summary>
        private void ApplyCoverGlow(Color primary, Color secondary)
        {
            if (CoverGlow1 == null || CoverGlow2 == null) return;
            CoverGlow1.Background = CreateGlowBrush(primary, secondary, reversed: false);
            CoverGlow2.Background = CreateGlowBrush(secondary, primary, reversed: true);
        }

        private static LinearGradientBrush CreateGlowBrush(Color first, Color second, bool reversed)
        {
            var mid = Color.FromRgb(
                (byte)((first.R + second.R) / 2),
                (byte)((first.G + second.G) / 2),
                (byte)((first.B + second.B) / 2));
            return new LinearGradientBrush(
                new GradientStopCollection
                {
                    new(first, 0.0),
                    new(mid, 0.5),
                    new(second, 1.0)
                },
                reversed ? new Point(1, 0.35) : new Point(0, 0.35),
                reversed ? new Point(0, 0.65) : new Point(1, 0.65));
        }

        /// <summary>A subtle default glow (used when ColorMatch is off) based on the theme accent.</summary>
        private void ApplyDefaultCoverGlow()
        {
            if (CoverGlow1 == null || CoverGlow2 == null) return;
            if (FindResource("PlaybarAccentColor") is SolidColorBrush accent)
            {
                var c = accent.Color;
                CoverGlow1.Background = new SolidColorBrush(Color.FromArgb(120, c.R, c.G, c.B));
                CoverGlow2.Background = new SolidColorBrush(Color.FromArgb(90, c.R, c.G, c.B));
            }
            else
            {
                CoverGlow1.Background = Brushes.Transparent;
                CoverGlow2.Background = Brushes.Transparent;
            }
        }

        public void ClearColorMatch()
        {
            _colorMatchEnabled = false;
            _albumPrimary = default;
            _albumSecondary = default;
            CopyThemeResources();
            RootBorder.BorderBrush = (Brush)FindResource("BorderColor");
            RootBorder.Background = (Brush)FindResource("WindowBg");
            _vizRenderer?.ClearPalette();
            ApplyDefaultCoverGlow();
        }

        private static Color EnsureMinLuminance(Color c, byte minLum)
        {
            byte lum = (byte)((0.299 * c.R + 0.587 * c.G + 0.114 * c.B));
            if (lum >= minLum) return c;
            double scale = minLum / Math.Max(1.0, lum);
            return Color.FromRgb(
                (byte)Math.Min(255, c.R * scale),
                (byte)Math.Min(255, c.G * scale),
                (byte)Math.Min(255, c.B * scale));
        }

        // ─── Visualizer ───

        private void ApplyVisualizerSettings()
        {
            int miniStyle = ThemeManager.MiniVisualizerStyle;
            if (miniStyle < 0)
            {
                // First run / never chosen: seed the mini style from the main visualizer setting
                // (preserves prior behavior on upgrade), then it persists independently.
                int main = ThemeManager.VisualizerMode ? ThemeManager.VisualizerStyle : 3; // 3 = Off
                miniStyle = main switch
                {
                    0 => 0, // Bars
                    1 => 1, // Mirror
                    4 => 2, // Scope (main index 4)
                    3 => 3, // Off
                    _ => 0  // anything else → Bars
                };
                ThemeManager.MiniVisualizerStyle = miniStyle;
                ThemeManager.SavePlayOptions();
            }
            if (miniStyle is < 0 or > 4) miniStyle = 0;

            _vizRenderer!.Style = miniStyle;
            _vizRenderer.IsActive = miniStyle != 3;
            SetVisualizerSpace(miniStyle != 3);

            RefreshTimerActivity();
        }

        /// <summary>
        /// Shows/hides the visualizer row and grows/shrinks the window height to match, so there is
        /// never dead vertical space when the visualizer is off. The user's manual height (without the
        /// visualizer) is remembered as the base and restored when the visualizer toggles.
        /// </summary>
        private void SetVisualizerSpace(bool on)
        {
            VisualizerRow.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
            _vizSpaceOn = on;

            // Before the window has been measured we don't know the true base height yet; let the
            // first layout pass settle (SizeToContent in the constructor), then we manage height here.
            if (!_heightInitialized) return;

            _suppressSizeSync = true;
            double target = _baseHeight + (on ? VizExtra : 0);
            target = Math.Clamp(target, MinHeight, MaxHeight);
            Height = target;
            _suppressSizeSync = false;
        }

        private void VizTimer_Tick(object? sender, EventArgs e)
        {
            _vizRenderer?.Render();
        }

        public void SetExternalVisualizerSuspended(bool suspended)
        {
            _externalVisualizerSuspended = suspended;
            RefreshTimerActivity();
        }

        /// <summary>Re-evaluate the mini visualizer loop after a Reduce Motion / Battery Saver change.</summary>
        public void ApplyVisualizerPerformancePolicy() => RefreshTimerActivity();

        private void RefreshTimerActivity()
        {
            bool visible = IsMiniPlayerUiActive;

            if (visible)
            {
                if (_updateTimer?.IsEnabled == false)
                    _updateTimer.Start();
            }
            else
            {
                _updateTimer?.Stop();
                MiniPlaybarAnimCanvas?.Children.Clear();
            }

            bool runViz = visible &&
                          !_externalVisualizerSuspended &&
                          _vizRenderer?.IsActive == true &&
                          _vizRenderer.Style != 3 &&
                          AnimationPolicy.IsMotionAllowed(AnimationArea.Visualizer);
            if (runViz)
            {
                if (_vizTimer?.IsEnabled == false)
                    _vizTimer.Start();
            }
            else
            {
                _vizTimer?.Stop();
                if (_vizRenderer != null)
                {
                    bool wasActive = _vizRenderer.IsActive;
                    _vizRenderer.IsActive = false;
                    _vizRenderer.Render();
                    _vizRenderer.IsActive = wasActive;
                }
            }
        }

        private void MiniPlayerWindow_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            RefreshTimerActivity();
        }

        private void MiniPlayerWindow_StateChanged(object? sender, EventArgs e)
        {
            RefreshTimerActivity();
        }

        /// <summary>
        /// Once the first layout pass has settled, measure the natural content height as the base and
        /// switch into managed-height mode (so toggling the visualizer adds/removes exactly its space).
        /// </summary>
        private void MiniPlayerWindow_Loaded(object sender, RoutedEventArgs e)
        {
            if (_heightInitialized) return;
            // Prefer the persisted base height; otherwise measure the natural content-sized height
            // (ActualHeight now reflects the real laid-out window, incl. the viz row if on).
            _baseHeight = !double.IsNaN(_restoredBaseHeight) && _restoredBaseHeight > 0
                ? _restoredBaseHeight
                : ActualHeight - (_vizSpaceOn ? VizExtra : 0);
            _heightInitialized = true;
            // Switch off content-sizing so we (and edge-resize) control the height explicitly.
            SizeToContent = SizeToContent.Manual;
            SetVisualizerSpace(_vizSpaceOn);

            // No remembered (valid) position: center on the work area now that the size is known.
            if (!_positionRestored)
            {
                var wa = SystemParameters.WorkArea;
                Left = wa.Left + Math.Max(0, (wa.Width - ActualWidth) / 2);
                Top = wa.Top + Math.Max(0, (wa.Height - ActualHeight) / 2);
            }
        }

        /// <summary>Restores the remembered width/base-height and (if still on-screen) position.</summary>
        private void RestoreWindowBounds()
        {
            double w = ThemeManager.MiniPlayerWidth;
            if (w >= MinWidth && w <= MaxWidth)
                Width = w;

            double bh = ThemeManager.MiniPlayerBaseHeight;
            if (bh > 0)
                _restoredBaseHeight = bh;

            double left = ThemeManager.MiniPlayerLeft;
            double top = ThemeManager.MiniPlayerTop;
            if (!double.IsNaN(left) && !double.IsNaN(top) && IsPositionOnScreen(left, top))
            {
                Left = left;
                Top = top;
                _positionRestored = true;
            }
        }

        /// <summary>True if the given top-left keeps a usable portion of the window on the virtual desktop.</summary>
        private static bool IsPositionOnScreen(double left, double top)
        {
            double vL = SystemParameters.VirtualScreenLeft;
            double vT = SystemParameters.VirtualScreenTop;
            double vR = vL + SystemParameters.VirtualScreenWidth;
            double vB = vT + SystemParameters.VirtualScreenHeight;
            // Require the title-drag area (top-left ~120x40) to stay within the desktop bounds.
            return left >= vL - 8 && top >= vT - 8 && left <= vR - 120 && top <= vB - 40;
        }

        /// <summary>Persists the mini player's current geometry (only when in the normal window state).</summary>
        private void SaveWindowBounds()
        {
            if (WindowState != WindowState.Normal) return;
            if (ActualWidth <= 0 || double.IsNaN(Left) || double.IsNaN(Top)) return;
            ThemeManager.MiniPlayerLeft = Left;
            ThemeManager.MiniPlayerTop = Top;
            ThemeManager.MiniPlayerWidth = ActualWidth;
            if (_baseHeight > 0)
                ThemeManager.MiniPlayerBaseHeight = _baseHeight;
            ThemeManager.SavePlayOptions();
        }

        /// <summary>Remember the user's manual height as the base (minus the visualizer space, if shown).</summary>
        private void MiniPlayerWindow_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (_suppressSizeSync || !_heightInitialized || !e.HeightChanged) return;
            // The reported height includes the viz row when it's on; strip it to get the base.
            _baseHeight = e.NewSize.Height - (_vizSpaceOn ? VizExtra : 0);
        }

        // ─── Edge resizing for the borderless window ───
        // WindowStyle="None" + AllowsTransparency disables the native resize border, so we answer
        // WM_NCHITTEST ourselves to let the user drag the edges/corners to resize.

        private const int WM_NCHITTEST = 0x0084;
        private const int HTLEFT = 10, HTRIGHT = 11, HTTOP = 12, HTTOPLEFT = 13,
                          HTTOPRIGHT = 14, HTBOTTOM = 15, HTBOTTOMLEFT = 16, HTBOTTOMRIGHT = 17;
        private const double ResizeBorder = 6.0;

        private void MiniPlayerWindow_SourceInitialized(object? sender, EventArgs e)
        {
            _hwndSource = (System.Windows.Interop.HwndSource?)PresentationSource.FromVisual(this);
            _hwndSource?.AddHook(WndProc);
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg != WM_NCHITTEST) return IntPtr.Zero;

            // Mouse position in screen coords (LOWORD/HIWORD of lParam, may be negative).
            int lp = lParam.ToInt32();
            double sx = (short)(lp & 0xFFFF);
            double sy = (short)((lp >> 16) & 0xFFFF);

            var topLeft = PointToScreen(new Point(0, 0));
            double dpiW = ActualWidth <= 0 ? Width : ActualWidth;
            double dpiH = ActualHeight <= 0 ? Height : ActualHeight;
            // Convert screen px to DIPs using the window's current transform.
            var src = (System.Windows.Interop.HwndSource?)PresentationSource.FromVisual(this);
            double scaleX = src?.CompositionTarget?.TransformToDevice.M11 ?? 1.0;
            double scaleY = src?.CompositionTarget?.TransformToDevice.M22 ?? 1.0;

            double x = (sx - topLeft.X) / scaleX;
            double y = (sy - topLeft.Y) / scaleY;
            double w = dpiW, h = dpiH;

            bool left = x <= ResizeBorder;
            bool right = x >= w - ResizeBorder;
            bool top = y <= ResizeBorder;
            bool bottom = y >= h - ResizeBorder;

            int hit = 0;
            if (top && left) hit = HTTOPLEFT;
            else if (top && right) hit = HTTOPRIGHT;
            else if (bottom && left) hit = HTBOTTOMLEFT;
            else if (bottom && right) hit = HTBOTTOMRIGHT;
            else if (left) hit = HTLEFT;
            else if (right) hit = HTRIGHT;
            else if (top) hit = HTTOP;
            else if (bottom) hit = HTBOTTOM;

            if (hit != 0)
            {
                handled = true;
                return new IntPtr(hit);
            }
            return IntPtr.Zero;
        }

        private void CoverBorder_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            ApplyCoverShape();
        }

        // ─── Settings Flyout ───

        private void SettingsBtn_Click(object sender, RoutedEventArgs e)
        {
            var menuItemStyle = (Style)FindResource("MiniMenuItem");

            // Local helper so every item picks up the themed style consistently.
            MenuItem MakeItem(string header, bool checkable = false, bool isChecked = false, bool enabled = true)
                => new()
                {
                    Header = header,
                    Style = menuItemStyle,
                    IsCheckable = checkable,
                    IsChecked = isChecked,
                    IsEnabled = enabled
                };

            Separator MakeSeparator() => new()
            {
                Background = (Brush)FindResource("BorderColor"),
                Opacity = 0.4,
                Margin = new Thickness(6, 3, 6, 3)
            };

            var menu = new ContextMenu { Style = (Style)FindResource("MiniContextMenu") };

            // Visualizer submenu
            menu.Items.Add(MakeItem("Visualizer", enabled: false));

            (string Name, int Style)[] vizOptions =
            {
                ("Bars", 0),
                ("Mirror", 1),
                ("Scope", 2),
                ("Circles", 4),
                ("Off", 3)
            };
            foreach (var option in vizOptions)
            {
                int idx = option.Style;
                var item = MakeItem(option.Name, checkable: true, isChecked: _vizRenderer!.Style == idx);
                item.Click += (_, _) =>
                {
                    _vizRenderer.Style = idx;
                    _vizRenderer.IsActive = idx != 3;
                    SetVisualizerSpace(idx != 3);
                    RefreshTimerActivity();
                    // Persist the mini player's own visualizer choice so it survives close/reopen.
                    ThemeManager.MiniVisualizerStyle = idx;
                    ThemeManager.SavePlayOptions();
                    _onToggleVisualizer();
                };
                menu.Items.Add(item);
            }

            menu.Items.Add(MakeSeparator());

            // ColorMatch toggle
            var cmItem = MakeItem("Color Match", checkable: true, isChecked: _colorMatchEnabled);
            cmItem.Click += (_, _) =>
            {
                if (cmItem.IsChecked == true)
                {
                    // Try to extract from current track
                    var track = _getCurrentTrack();
                    if (track != null)
                        ExtractAndApplyAlbumColors(track.FilePath);
                    else
                        cmItem.IsChecked = false;
                }
                else
                {
                    ClearColorMatch();
                }
                // Persist the mini player's own ColorMatch choice so it survives close/reopen.
                ThemeManager.MiniColorMatchEnabled = _colorMatchEnabled;
                ThemeManager.SavePlayOptions();
                // NOTE: deliberately do NOT call _onToggleColorMatch() here — that callback re-syncs
                // the miniplayer from the MAIN window's MainColorMatchEnabled flag, which would
                // immediately clear a mini-initiated toggle. The miniplayer owns its own state.
            };
            menu.Items.Add(cmItem);

            menu.Items.Add(MakeSeparator());

            var alwaysOnTopItem = MakeItem("Always on top", checkable: true, isChecked: ThemeManager.MiniPlayerAlwaysOnTop);
            alwaysOnTopItem.Click += (_, _) =>
            {
                ThemeManager.MiniPlayerAlwaysOnTop = !ThemeManager.MiniPlayerAlwaysOnTop;
                ThemeManager.SavePlayOptions();
                Topmost = ThemeManager.MiniPlayerAlwaysOnTop;
            };
            menu.Items.Add(alwaysOnTopItem);

            menu.Items.Add(MakeSeparator());

            menu.Items.Add(MakeItem("Cover Shape", enabled: false));

            foreach (var shape in new[] { "Rounded", "Circle" })
            {
                string shapeName = shape;
                var item = MakeItem(shapeName, checkable: true,
                    isChecked: string.Equals(ThemeManager.MiniCoverShapeMode, shapeName, StringComparison.OrdinalIgnoreCase));
                item.Click += (_, _) =>
                {
                    ThemeManager.MiniCoverShapeMode = shapeName;
                    ThemeManager.SavePlayOptions();
                    ApplyCoverShape();
                };
                menu.Items.Add(item);
            }

            menu.Items.Add(MakeSeparator());

            // Restore main window
            var restoreItem = MakeItem("Restore Main Window");
            restoreItem.Click += (_, _) => _onRestore();
            menu.Items.Add(restoreItem);

            menu.PlacementTarget = MiniSettingsBtn;
            menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
            menu.IsOpen = true;
        }

        private void ExtractAndApplyAlbumColors(string? filePath)
        {
            if (string.IsNullOrEmpty(filePath)) return;
            try
            {
                using var tagFile = TagLib.File.Create(filePath);
                if (tagFile.Tag.Pictures.Length > 0)
                {
                    var pic = tagFile.Tag.Pictures[0];
                    using var ms = new MemoryStream(pic.Data.Data);
                    var bmp = new BitmapImage();
                    bmp.BeginInit();
                    bmp.CacheOption = BitmapCacheOption.OnLoad;
                    bmp.StreamSource = ms;
                    bmp.EndInit();
                    bmp.Freeze();

                    var (primary, secondary, background) = ExtractColors(bmp);
                    ApplyColorMatch(primary, secondary);
                }
            }
            catch { }
        }

        private static (Color Primary, Color Secondary, Color Background) ExtractColors(BitmapSource bmp)
        {
            var converted = new FormatConvertedBitmap(bmp, PixelFormats.Bgra32, null, 0);
            int stride = converted.PixelWidth * 4;
            var pixels = new byte[stride * converted.PixelHeight];
            converted.CopyPixels(pixels, stride, 0);
            var colors = AlbumColorExtractor.Extract(pixels, converted.PixelWidth, converted.PixelHeight, stride);

            Color primary = Color.FromRgb(colors.Primary.R, colors.Primary.G, colors.Primary.B);
            Color secondary = Color.FromRgb(colors.Secondary.R, colors.Secondary.G, colors.Secondary.B);
            Color background = Color.FromRgb(colors.Background.R, colors.Background.G, colors.Background.B);

            return (primary, secondary, background);
        }

        private void ApplyCoverShape()
        {
            double w = CoverBorder.ActualWidth > 0 ? CoverBorder.ActualWidth : CoverBorder.Width;
            double h = CoverBorder.ActualHeight > 0 ? CoverBorder.ActualHeight : CoverBorder.Height;
            if (double.IsNaN(w) || w <= 0) w = 52;
            if (double.IsNaN(h) || h <= 0) h = 52;

            bool circle = string.Equals(ThemeManager.MiniCoverShapeMode, "Circle", StringComparison.OrdinalIgnoreCase);
            double radius = circle ? Math.Min(w, h) / 2.0 : 8;

            CoverBorder.CornerRadius = new CornerRadius(radius);
            // The Border's CornerRadius alone does NOT clip the child Image — drive the explicit
            // clip geometry so circle/rounded actually takes effect on the artwork.
            if (CoverClip != null)
            {
                CoverClip.Rect = new Rect(0, 0, w, h);
                CoverClip.RadiusX = radius;
                CoverClip.RadiusY = radius;
            }
            // Match the glow layers to the shape so the halo follows the cover.
            if (CoverGlow1 != null) CoverGlow1.CornerRadius = new CornerRadius(radius + 6);
            if (CoverGlow2 != null) CoverGlow2.CornerRadius = new CornerRadius(radius + 2);
        }

        // ─── Window Dragging (borderless) ───

        private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            // Walk up the visual tree to see if we clicked an interactive control
            var source = e.OriginalSource as DependencyObject;
            while (source != null)
            {
                if (source is Slider or Button or Image) return;
                if (source is Border b && b.Name == "CoverBorder") return;
                source = VisualTreeHelper.GetParent(source);
            }
            _isDragging = true;
            _dragStartPoint = e.GetPosition(this);
            CaptureMouse();
        }

        private void Window_MouseMove(object sender, MouseEventArgs e)
        {
            if (!_isDragging) return;
            var pos = e.GetPosition(this);
            Left += pos.X - _dragStartPoint.X;
            Top += pos.Y - _dragStartPoint.Y;
        }

        private void Window_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            _isDragging = false;
            ReleaseMouseCapture();
        }

        private void RenderMiniPlaybarStyle()
        {
            // The mini player never uses playbar animation styles — they caused issues and aren't
            // selectable here. The MiniSeekSlider draws its own plain accent-filled track, so the
            // overlay canvas stays empty (no Wave overlay regardless of NpPlaybarAnimationStyle).
            MiniPlaybarAnimCanvas?.Children.Clear();
        }

        // ─── Timer Update ───

        private void UpdateTimer_Tick(object? sender, EventArgs e)
        {
            if (_player == null) return;
            if (!IsVisible || WindowState == WindowState.Minimized) return;

            var pos = _player.CurrentPosition;
            var total = _player.TotalDuration;

            // Respect 500ms cooldown after seeks to prevent snap-back
            bool seekCooldown = (DateTime.UtcNow - _lastMiniSeekTime).TotalMilliseconds < 500;
            if (!_isSeeking && !seekCooldown && total.TotalSeconds > 0)
            {
                SeekSlider.Maximum = total.TotalSeconds;
                SeekSlider.Value = pos.TotalSeconds;
            }

            TimeText.Text = $"{FormatTime(pos)} / {FormatTime(total)}";
            UpdatePlayState();
            UpdateTrackInfo();
            RenderMiniPlaybarStyle();
        }

        private void UpdatePlayState()
        {
            bool isPlaying = _player.IsPlaying;
            PlayIcon.Visibility = isPlaying ? Visibility.Collapsed : Visibility.Visible;
            PauseIcon.Visibility = isPlaying ? Visibility.Visible : Visibility.Collapsed;
        }

        public void UpdateShuffleState(bool shuffleEnabled)
        {
            if (ShuffleIconPath != null)
            {
                var accent = (Brush)FindResource("PlaybarAccentColor");
                var muted = (Brush)FindResource("TextMuted");
                ShuffleIconPath.Stroke = shuffleEnabled ? accent : muted;
                ShuffleIconPath.StrokeThickness = shuffleEnabled ? 2.6 : 2.2;

                if (ShuffleBtn != null)
                {
                    if (shuffleEnabled && accent is SolidColorBrush scb)
                    {
                        var glowColor = scb.Color;
                        glowColor.A = 40;
                        ShuffleBtn.Background = new SolidColorBrush(glowColor);
                    }
                    else
                    {
                        ShuffleBtn.Background = Brushes.Transparent;
                    }
                }
            }
        }

        private static string FormatTime(TimeSpan ts)
        {
            if (ts.TotalHours >= 1)
                return $"{(int)ts.TotalHours}:{ts.Minutes:D2}:{ts.Seconds:D2}";
            return $"{ts.Minutes}:{ts.Seconds:D2}";
        }

        private void UpdateTrackInfo()
        {
            var track = _getCurrentTrack();
            if (track == _lastTrack) return;
            _lastTrack = track;

            if (track == null)
            {
                TitleText.Text = "No track";
                ArtistText.Text = "";
                CoverImage.Source = null;
                ClearColorMatch();
                return;
            }

            TitleText.Text = track.Title ?? track.FileName ?? "Unknown";
            ArtistText.Text = track.Artist ?? "";
            LoadCover(track.FilePath);

            // Auto-apply ColorMatch on the new track if the mini player's own ColorMatch preference
            // is on. Checking the persisted flag (not just the runtime field) re-arms it after a
            // transient "no track" gap cleared the runtime state, so it survives skips.
            if (_colorMatchEnabled || ThemeManager.MiniColorMatchEnabled)
                ExtractAndApplyAlbumColors(track.FilePath);
        }

        private void LoadCover(string? filePath)
        {
            if (string.IsNullOrEmpty(filePath))
            {
                SetCover(null);
                return;
            }

            try
            {
                using var tagFile = TagLib.File.Create(filePath);
                if (tagFile.Tag.Pictures.Length > 0)
                {
                    var pic = tagFile.Tag.Pictures[0];
                    var bmp = new BitmapImage();
                    bmp.BeginInit();
                    bmp.CacheOption = BitmapCacheOption.OnLoad;
                    bmp.StreamSource = new MemoryStream(pic.Data.Data);
                    bmp.EndInit();
                    bmp.Freeze();
                    SetCover(bmp);
                    return;
                }
            }
            catch { }
            SetCover(null);
        }

        /// <summary>Sets the cover image and shows the music-note placeholder when there's none.</summary>
        private void SetCover(BitmapImage? bmp)
        {
            CoverImage.Source = bmp;
            if (CoverPlaceholder != null)
                CoverPlaceholder.Visibility = bmp == null ? Visibility.Visible : Visibility.Collapsed;
        }

        // ─── Controls ───

        private void PlayPause_Click(object sender, RoutedEventArgs e)
        {
            if (_player.IsPlaying) _player.Pause();
            else _player.Resume();
            UpdatePlayState();
        }

        private void Prev_Click(object sender, RoutedEventArgs e) => _onPrev();
        private void Next_Click(object sender, RoutedEventArgs e) => _onNext();
        private void Shuffle_Click(object sender, RoutedEventArgs e) => _onToggleShuffle();
        private void Close_Click(object sender, RoutedEventArgs e) => Close();

        private void Cover_Click(object sender, MouseButtonEventArgs e)
        {
            e.Handled = true;
            _onRestore();
        }

        // ─── Seek ───

        private void SeekSlider_MouseDown(object sender, MouseButtonEventArgs e)
        {
            // If the click landed on the thumb, let the Slider drive the drag (DragStarted/Completed
            // handle it). Only treat it as a track click otherwise.
            var thumb = FindVisualChild<System.Windows.Controls.Primitives.Thumb>(SeekSlider);
            if (thumb != null)
            {
                var tp = e.GetPosition(thumb);
                if (tp.X >= -4 && tp.X <= thumb.ActualWidth + 4 &&
                    tp.Y >= -4 && tp.Y <= thumb.ActualHeight + 4)
                {
                    _isSeeking = true;
                    return;
                }
            }

            // Track click — seek immediately to the clicked position.
            if (SeekSlider.ActualWidth > 0 && _player.TotalDuration.TotalSeconds > 0)
            {
                double ratio = Math.Clamp(e.GetPosition(SeekSlider).X / SeekSlider.ActualWidth, 0, 1);
                double posSec = ratio * _player.TotalDuration.TotalSeconds;
                SeekSlider.Value = posSec;
                _player.Seek(posSec);
                _lastMiniSeekTime = DateTime.UtcNow;
                _isSeeking = true;
            }
            e.Handled = true;
        }

        private void SeekSlider_MouseUp(object sender, MouseButtonEventArgs e)
        {
            // Track clicks already seeked on mouse-down; thumb drags finish in DragCompleted.
            var thumb = FindVisualChild<System.Windows.Controls.Primitives.Thumb>(SeekSlider);
            if (thumb == null || !thumb.IsDragging)
            {
                _lastMiniSeekTime = DateTime.UtcNow;
                _isSeeking = false;
            }
        }

        private void SeekSlider_DragStarted(object sender, System.Windows.Controls.Primitives.DragStartedEventArgs e)
        {
            _isSeeking = true;
        }

        private void SeekSlider_DragCompleted(object sender, System.Windows.Controls.Primitives.DragCompletedEventArgs e)
        {
            if (_player.TotalDuration.TotalSeconds > 0)
            {
                _player.Seek(SeekSlider.Value);
                _lastMiniSeekTime = DateTime.UtcNow;
            }
            _isSeeking = false;
        }

        /// <summary>Depth-first search for the first visual child of the given type.</summary>
        private static T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
        {
            int count = VisualTreeHelper.GetChildrenCount(parent);
            for (int i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is T typed) return typed;
                var nested = FindVisualChild<T>(child);
                if (nested != null) return nested;
            }
            return null;
        }

        // ─── Volume & Mute ───

        private void VolumeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_player != null)
                _player.Volume = (float)(VolumeSlider.Value / 100.0);
            UpdateVolumeIcon();
        }

        private void MuteBtn_Click(object sender, RoutedEventArgs e)
        {
            if (!_isMuted)
            {
                _preMuteVolume = VolumeSlider.Value;
                VolumeSlider.Value = 0;
                _isMuted = true;
            }
            else
            {
                VolumeSlider.Value = _preMuteVolume;
                _isMuted = false;
            }
            UpdateVolumeIcon();
        }

        private void UpdateVolumeIcon()
        {
            double vol = VolumeSlider.Value;
            if (vol == 0)
            {
                VolumeIconPath.Data = Geometry.Parse("M 2,5 L 5,5 L 9,2 L 9,14 L 5,11 L 2,11 Z M 11,5 L 16,10 M 16,5 L 11,10");
            }
            else if (vol < 50)
            {
                VolumeIconPath.Data = Geometry.Parse("M 2,5 L 5,5 L 9,2 L 9,14 L 5,11 L 2,11 Z M 12,6 Q 14,8 12,10");
            }
            else
            {
                VolumeIconPath.Data = Geometry.Parse("M 2,5 L 5,5 L 9,2 L 9,14 L 5,11 L 2,11 Z M 12,4 Q 16,8 12,12 M 15,2 Q 20,8 15,14");
            }
        }
    }
}
