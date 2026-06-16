using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using IOPath = System.IO.Path;
using AudioQualityChecker.Models;
using AudioQualityChecker.Services;
using AudioQualityChecker.Services.Scrobbling;

namespace AudioQualityChecker
{
    public partial class MainWindow
    {
        private SolidColorBrush? _npColorMatchOptionActiveBrush;
        private SolidColorBrush? _npColorMatchOptionInactiveBrush;
        private Color _npColorMatchOptionAccent;

        // Fixed album-derived base fill for NowPlayingPanel while ColorMatch is ON. Pinning this
        // (instead of leaving the XAML's {DynamicResource WindowBg}) stops a theme switch from
        // bleeding through the semi-transparent gradient/backdrop. Null when ColorMatch is OFF.
        private SolidColorBrush? NpPanelBaseBrush;

        private void NpResetColorMatchCaches()
        {
            _npColorMatchOptionActiveBrush = null;
            _npColorMatchOptionInactiveBrush = null;
            _npColorMatchOptionAccent = default;
            _eqSliderTemplateCache = null;
            ClearVisualizerCaches();
            NpInvalidatePlaybarVisuals();
        }

        /// <summary>
        /// Dark-overlay alpha for the ColorMatch (and Color Drift) background, derived from the
        /// user's background-brightness setting (0.35..1.6, default 1.0). Default ≈ the previous
        /// fixed "minimal overlay" look; lower brightness darkens, higher lightens. Same value is
        /// used for static colormatch and drift so drift is never darker than static.
        /// </summary>
        private static byte NpColorMatchOverlayAlpha()
        {
            double b = Math.Clamp(ThemeManager.NpBackgroundBrightness, 0.35, 1.6);
            double alpha = b <= 1.0
                ? 20 + (1.0 - b) * (130.0 / 0.65)   // darker: up to ~150 at 0.35
                : 20 * (1.6 - b) / 0.6;             // lighter: down to 0 at 1.6
            return (byte)Math.Clamp(alpha, 0, 160);
        }

        private static (System.Windows.Media.Color primary, System.Windows.Media.Color secondary, System.Windows.Media.Color tertiary, System.Windows.Media.Color background)
            NpSanitizePalette(
                System.Windows.Media.Color primary,
                System.Windows.Media.Color secondary,
                System.Windows.Media.Color tertiary)
        {
            if (primary == default)
                return (default, default, default, default);

            if (secondary == default) secondary = primary;
            if (tertiary == default) tertiary = secondary;

            var background = System.Windows.Media.Color.FromRgb(
                (byte)((primary.R + secondary.R + tertiary.R) / 3),
                (byte)((primary.G + secondary.G + tertiary.G) / 3),
                (byte)((primary.B + secondary.B + tertiary.B) / 3));

            var sanitized = AlbumColorExtractor.SanitizeDominantColors(
                new AlbumColorExtractor.DominantColors(
                    new AlbumColorExtractor.Color(primary.R, primary.G, primary.B),
                    new AlbumColorExtractor.Color(secondary.R, secondary.G, secondary.B),
                    new AlbumColorExtractor.Color(tertiary.R, tertiary.G, tertiary.B),
                    new AlbumColorExtractor.Color(background.R, background.G, background.B),
                    new AlbumColorExtractor.Color(240, 240, 240)));

            return (
                System.Windows.Media.Color.FromRgb(sanitized.Primary.R, sanitized.Primary.G, sanitized.Primary.B),
                System.Windows.Media.Color.FromRgb(sanitized.Secondary.R, sanitized.Secondary.G, sanitized.Secondary.B),
                System.Windows.Media.Color.FromRgb(sanitized.Tertiary.R, sanitized.Tertiary.G, sanitized.Tertiary.B),
                System.Windows.Media.Color.FromRgb(sanitized.Background.R, sanitized.Background.G, sanitized.Background.B));
        }

        /// <summary>
        /// Palette for MANUAL eyedropper picks. Unlike <see cref="NpSanitizePalette"/>, this honors
        /// the exact colors the user clicked and does NOT run them through the auto-extraction
        /// sanitizer. That sanitizer's IsUsableAccent check rejects very bright or low-saturation
        /// colors (luminance &gt; 226) and falls back to a near-white monochrome palette — which is
        /// why picking a bright yellow used to turn the theme white. A manual pick is intentional,
        /// so we keep it verbatim and only derive a neutral background tone from the picks.
        /// </summary>
        private static (System.Windows.Media.Color primary, System.Windows.Media.Color secondary, System.Windows.Media.Color tertiary, System.Windows.Media.Color background)
            NpBuildManualPickPalette(
                System.Windows.Media.Color primary,
                System.Windows.Media.Color secondary,
                System.Windows.Media.Color tertiary)
        {
            if (primary == default)
                return (default, default, default, default);
            if (secondary == default) secondary = primary;
            if (tertiary == default) tertiary = secondary;

            var background = System.Windows.Media.Color.FromRgb(
                (byte)((primary.R + secondary.R + tertiary.R) / 3),
                (byte)((primary.G + secondary.G + tertiary.G) / 3),
                (byte)((primary.B + secondary.B + tertiary.B) / 3));

            return (primary, secondary, tertiary, background);
        }

        /// <summary>
        /// Builds a multi-stop background gradient from the user's eyedropper picks for the current
        /// track. Only the first three picks drive the primary/secondary/tertiary palette (icons,
        /// glow, visualizer); when the picker is configured for more, the extra picks would otherwise
        /// be invisible. This routes ALL picks into the NP background gradient so they still show.
        /// Each pick is darkened on a /3→/5 ramp (alpha 225) so it reads as a deep album-tinted
        /// backdrop, not full-saturation bands. Returns null with fewer than two picks so callers
        /// keep their existing two-stop fallback.
        /// </summary>
        private GradientStopCollection? NpBuildPickGradientStops()
        {
            var picks = _npColorPickerOverrides;
            if (picks.Count < 2) return null;

            var stops = new GradientStopCollection();
            int n = picks.Count;
            for (int i = 0; i < n; i++)
            {
                double t = (double)i / (n - 1);
                double divisor = 3.0 + 2.0 * t;
                var c = picks[i];
                var stopColor = System.Windows.Media.Color.FromArgb(225,
                    (byte)Math.Max(8, c.R / divisor),
                    (byte)Math.Max(8, c.G / divisor),
                    (byte)Math.Max(8, c.B / divisor));
                stops.Add(new GradientStop(stopColor, t));
            }
            return stops;
        }

        private bool NpTryResolveActiveColorMatchPalette(
            out System.Windows.Media.Color primary,
            out System.Windows.Media.Color secondary,
            out System.Windows.Media.Color tertiary,
            out System.Windows.Media.Color background)
        {
            primary = default;
            secondary = default;
            tertiary = default;
            background = default;

            if (!_npColorMatchEnabled)
                return false;

            if (NpHasColorPickerOverridesForCurrentTrack())
            {
                var (manualPrimary, manualSecondary, manualTertiary) = NpResolveColorOverrides();
                if (manualPrimary == default)
                    return false;

                (primary, secondary, tertiary, background) = NpSanitizePalette(manualPrimary, manualSecondary, manualTertiary);
                return true;
            }

            if (_npAlbumPrimary == default)
                return false;

            primary = _npAlbumPrimary;
            secondary = _npAlbumSecondary != default ? _npAlbumSecondary : _npAlbumPrimary;
            tertiary = _npAlbumTertiary != default ? _npAlbumTertiary : secondary;
            background = _npAlbumBackground != default ? _npAlbumBackground : _npAlbumPrimary;
            return true;
        }

        private bool NpTryResolveActivePlaybarPalette(
            out System.Windows.Media.Color background,
            out System.Windows.Media.Color[] gradientColors,
            out double animationSpeed)
        {
            // Animation speed always follows the playbar theme — it's motion, not color,
            // so it isn't part of the "no theme influence" rule.
            var playbarColors = ThemeManager.GetPlaybarColors();
            background = playbarColors.BackgroundColor;
            gradientColors = playbarColors.ProgressGradient;
            animationSpeed = playbarColors.AnimationSpeed;

            // When ColorMatch is ON, use album-or-neutral colors — never the theme. When OFF,
            // return false so the playbar keeps using the theme colors seeded above.
            if (!NpGetEffectiveColorMatchPalette(out var primary, out var secondary, out _, out var paletteBackground, out _))
                return false;

            var accent = EnsureMinLuminance(primary, 155);
            var secondaryAccent = EnsureMinLuminance(secondary, 135);
            var high = System.Windows.Media.Color.FromRgb(
                (byte)Math.Min(255, (accent.R + secondaryAccent.R) / 2 + 34),
                (byte)Math.Min(255, (accent.G + secondaryAccent.G) / 2 + 34),
                (byte)Math.Min(255, (accent.B + secondaryAccent.B) / 2 + 34));

            background = System.Windows.Media.Color.FromArgb(92,
                (byte)Math.Max(10, paletteBackground.R / 3),
                (byte)Math.Max(10, paletteBackground.G / 3),
                (byte)Math.Max(12, paletteBackground.B / 3));
            gradientColors =
            [
                System.Windows.Media.Color.FromArgb(190, accent.R, accent.G, accent.B),
                System.Windows.Media.Color.FromArgb(230, secondaryAccent.R, secondaryAccent.G, secondaryAccent.B),
                System.Windows.Media.Color.FromArgb(255, high.R, high.G, high.B)
            ];
            return true;
        }

        private void NpInvalidatePlaybarVisuals()
        {
            _npLastPlaybarRenderPosition = TimeSpan.MinValue;
            _npLastPlaybarRenderUtc = DateTime.MinValue;
            _lastWaveformRenderTime = DateTime.MinValue;
            if (ThemeManager.NpPlaybarAnimationStyle == PlaybarAnimationStyle.Wave)
                NpWaveformCanvas?.Children.Clear();
        }

        // ─── NP Color Match Menu ───

        // Opens the consolidated Color Match flyout above the toolbar button. The flyout hosts
        // the color-match toggle, the eyedropper picker, and a "3D" entry into the glow-options
        // sub-popup. State (toggle on/off, picker active state) is mirrored when the popup opens.
        private void NpColorMatchMenu_Click(object sender, RoutedEventArgs e)
        {
            if (NpColorMatchPopup == null) return;
            if (!NpColorMatchPopup.IsOpen)
                NpUpdateColorCountLabel();
            NpColorMatchPopup.IsOpen = !NpColorMatchPopup.IsOpen;
        }

        // ─── Color-picker count stepper (3–6) ───

        private void NpColorCountMinus_Click(object sender, RoutedEventArgs e) => NpAdjustColorPickerCount(-1);

        private void NpColorCountPlus_Click(object sender, RoutedEventArgs e) => NpAdjustColorPickerCount(+1);

        private void NpAdjustColorPickerCount(int delta)
        {
            int before = ThemeManager.NpColorPickerMaxColors;
            ThemeManager.NpColorPickerMaxColors = before + delta; // property clamps to 3–6
            if (ThemeManager.NpColorPickerMaxColors == before) return;
            NpUpdateColorCountLabel();
            NpSavePreferences();
        }

        private void NpUpdateColorCountLabel()
        {
            if (NpColorCountLabel != null)
                NpColorCountLabel.Text = ThemeManager.NpColorPickerMaxColors.ToString();
        }

        private void NpGlowMotionModeCombo_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (NpGlowMotionModeCombo == null) return;
            int idx = NpGlowMotionModeCombo.SelectedIndex;
            if (idx < 0 || idx > 6) return;
            _npGlowMotionMode = (GlowMotionMode)idx;
            // Reset Random-mode state so the next tick swaps in fresh targets
            _npGlowRandomLastSwapMs = 0;
            NpSavePreferences();
        }

        // ─── NP Color Match Toggle ───

        private void NpColorMatch_Click(object sender, RoutedEventArgs e)
        {
            _npColorMatchEnabled = !_npColorMatchEnabled;
            ThemeManager.NpColorMatchEnabled = _npColorMatchEnabled;
            NpResetColorMatchCaches();
            NpUpdateColorMatchIcon();
            NpApplyColorMatchMode();
            NpSavePreferences();
            // Close the consolidated flyout after toggling so the user sees the result
            if (NpColorMatchPopup != null) NpColorMatchPopup.IsOpen = false;
        }

        // ─── ColorMatch eyedropper picker ───

        private void NpColorPicker_Click(object sender, RoutedEventArgs e)
        {
            // Ensure ColorMatch is on so picked colors actually visibly take effect
            if (!_npColorMatchEnabled)
            {
                _npColorMatchEnabled = true;
                ThemeManager.NpColorMatchEnabled = true;
                NpUpdateColorMatchIcon();
            }

            // Clear override slots when starting a new picking session for a new track
            string? currentPath = _player?.CurrentFile;
            if (!string.Equals(_npColorPickerOwnerPath, currentPath, StringComparison.OrdinalIgnoreCase))
            {
                _npColorPickerOverrides.Clear();
                _npColorPickerNextSlot = 0;
                _npColorPickerOwnerPath = currentPath;
            }

            _npColorPickerActive = !_npColorPickerActive;
            if (_npColorPickerActive)
                _npColorPickerSessionPicks = 0;
            NpCoverImage.Cursor = _npColorPickerActive
                ? System.Windows.Input.Cursors.Cross
                : System.Windows.Input.Cursors.Arrow;
            UpdateColorPickerIconBrush();
            StatusText.Text = _npColorPickerActive
                ? $"Picker active — click the album cover to pick up to {ThemeManager.NpColorPickerMaxColors} colors."
                : "";
            // Close the flyout so the cover is visible for picking
            if (NpColorMatchPopup != null) NpColorMatchPopup.IsOpen = false;
        }

        private void NpColorPicker_RightClick(object sender, MouseButtonEventArgs e)
        {
            // Reset picked colors and revert to auto-extracted palette
            string? resetPath = _npColorPickerOwnerPath ?? _player?.CurrentFile;
            _npColorPickerOverrides.Clear();
            _npColorPickerNextSlot = 0;
            _npColorPickerSessionPicks = 0;
            _npColorPickerActive = false;
            _npColorPickerOwnerPath = resetPath;
            NpPersistColorPickerOverrides(resetPath);
            NpCoverImage.Cursor = System.Windows.Input.Cursors.Arrow;
            NpResetColorMatchCaches();
            UpdateColorPickerIconBrush();
            NpApplyColorMatchMode();
            StatusText.Text = "Picker reset — using auto-extracted album colors.";
            e.Handled = true;
        }

        private void UpdateColorPickerIconBrush()
        {
            if (NpColorPickerIcon == null) return;
            // Highlight the icon when active OR when overrides exist
            bool emphasised = _npColorPickerActive || _npColorPickerOverrides.Count > 0;
            NpColorPickerIcon.Stroke = emphasised ? NpGetIconBrush(true) : NpGetIconBrush(false);
            NpSetToggleBg(NpColorPickerBtn, _npColorPickerActive);
        }

        private void NpCoverImage_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (!_npColorPickerActive) return;
            if (NpCoverImage.Source is not BitmapSource src) return;

            var clickPoint = e.GetPosition(NpCoverImage);
            if (!NpTryMapCoverClickToPixel(src, clickPoint, out int px, out int py))
            {
                StatusText.Text = "Click inside the visible album art to pick a color.";
                return;
            }

            try
            {
                var converted = new FormatConvertedBitmap(src, PixelFormats.Bgra32, null, 0);
                var picked = NpSampleCoverColor(converted, px, py);

                int maxColors = ThemeManager.NpColorPickerMaxColors;

                if (_npColorPickerSessionPicks == 0)
                {
                    _npColorPickerOverrides.Clear();
                    _npColorPickerNextSlot = 0;
                }

                if (_npColorPickerOverrides.Count < maxColors)
                {
                    _npColorPickerOverrides.Add(picked);
                }
                else
                {
                    _npColorPickerNextSlot %= maxColors;
                    _npColorPickerOverrides[_npColorPickerNextSlot] = picked;
                    _npColorPickerNextSlot = (_npColorPickerNextSlot + 1) % maxColors;
                }

                _npColorPickerOwnerPath = _player?.CurrentFile;
                _npColorPickerSessionPicks++;
                NpPersistColorPickerOverrides(_npColorPickerOwnerPath);
                NpResetColorMatchCaches();
                NpApplyColorMatchMode();

                // Picked colors must also drive the toolbar/title-bar tint, otherwise the
                // top of the window stays at the previous album's color.
                if (_npColorPickerOverrides.Count > 0)
                {
                    var manualPrimary = _npColorPickerOverrides[0];
                    var manualSecondary = _npColorPickerOverrides.Count > 1
                        ? _npColorPickerOverrides[1]
                        : manualPrimary;
                    var manualTertiary = _npColorPickerOverrides.Count > 2
                        ? _npColorPickerOverrides[2]
                        : manualSecondary;
                    var sanitized = NpBuildManualPickPalette(manualPrimary, manualSecondary, manualTertiary);
                    _mainAlbumPrimary = sanitized.primary;
                    _mainAlbumSecondary = sanitized.secondary;
                    _mainAlbumTertiary = sanitized.tertiary;
                    if (ThemeManager.MainColorMatchEnabled)
                        ApplyMainColorMatch();
                }

                if (_npColorPickerSessionPicks >= maxColors)
                {
                    _npColorPickerActive = false;
                    _npColorPickerNextSlot = 0;
                    _npColorPickerSessionPicks = 0;
                    NpCoverImage.Cursor = System.Windows.Input.Cursors.Arrow;
                    System.Windows.Input.Mouse.Capture(null);
                    StatusText.Text = $"Picked {maxColors} colors — picker closed. Click the eyedropper to pick again or right-click to reset.";
                }
                else
                {
                    StatusText.Text = $"Picked color #{_npColorPickerSessionPicks} — pick {maxColors - _npColorPickerSessionPicks} more or click the eyedropper to exit.";
                }

                UpdateColorPickerIconBrush();
                e.Handled = true;
            }
            catch (Exception ex)
            {
                StatusText.Text = $"Color pick failed: {ex.Message}";
            }
        }

        private bool NpTryMapCoverClickToPixel(BitmapSource src, System.Windows.Point clickPoint, out int px, out int py)
        {
            px = py = 0;
            if (NpCoverImage.ActualWidth <= 0 || NpCoverImage.ActualHeight <= 0 || src.PixelWidth <= 0 || src.PixelHeight <= 0)
                return false;

            double controlWidth = NpCoverImage.ActualWidth;
            double controlHeight = NpCoverImage.ActualHeight;
            double scale = Math.Min(controlWidth / src.PixelWidth, controlHeight / src.PixelHeight);
            double renderedWidth = src.PixelWidth * scale;
            double renderedHeight = src.PixelHeight * scale;
            double left = (controlWidth - renderedWidth) / 2.0;
            double top = (controlHeight - renderedHeight) / 2.0;

            if (clickPoint.X < left || clickPoint.X > left + renderedWidth ||
                clickPoint.Y < top || clickPoint.Y > top + renderedHeight)
                return false;

            px = Math.Clamp((int)((clickPoint.X - left) / scale), 0, src.PixelWidth - 1);
            py = Math.Clamp((int)((clickPoint.Y - top) / scale), 0, src.PixelHeight - 1);
            return true;
        }

        private static System.Windows.Media.Color NpSampleCoverColor(BitmapSource converted, int px, int py)
        {
            int width = converted.PixelWidth;
            int height = converted.PixelHeight;
            int radius = 2;
            int x0 = Math.Max(0, px - radius);
            int y0 = Math.Max(0, py - radius);
            int x1 = Math.Min(width - 1, px + radius);
            int y1 = Math.Min(height - 1, py + radius);
            int sampleWidth = x1 - x0 + 1;
            int sampleHeight = y1 - y0 + 1;
            int stride = sampleWidth * 4;
            var pixels = new byte[stride * sampleHeight];
            converted.CopyPixels(new Int32Rect(x0, y0, sampleWidth, sampleHeight), pixels, stride, 0);

            long r = 0, g = 0, b = 0, count = 0;
            for (int i = 0; i < pixels.Length; i += 4)
            {
                byte alpha = pixels[i + 3];
                if (alpha < 16) continue;
                b += pixels[i];
                g += pixels[i + 1];
                r += pixels[i + 2];
                count++;
            }

            if (count == 0)
                return Colors.Transparent;

            return System.Windows.Media.Color.FromRgb(
                (byte)(r / count),
                (byte)(g / count),
                (byte)(b / count));
        }

        /// <summary>
        /// Returns the user's picked override colors when present, otherwise (default, default, default).
        /// Called from <see cref="NpApplyColorMatchMode"/> to optionally swap auto-extracted colors
        /// for manually picked ones.
        /// </summary>
        private (System.Windows.Media.Color primary, System.Windows.Media.Color secondary, System.Windows.Media.Color tertiary)
            NpResolveColorOverrides()
        {
            // Picks reset when the current track changes
            if (!string.Equals(_npColorPickerOwnerPath, _player?.CurrentFile, StringComparison.OrdinalIgnoreCase))
                return (default, default, default);

            var p = _npColorPickerOverrides.Count > 0 ? _npColorPickerOverrides[0] : default;
            var s = _npColorPickerOverrides.Count > 1 ? _npColorPickerOverrides[1] : p;
            var t = _npColorPickerOverrides.Count > 2 ? _npColorPickerOverrides[2] : s;
            return (p, s, t);
        }

        private bool NpHasColorPickerOverridesForCurrentTrack() =>
            string.Equals(_npColorPickerOwnerPath, _player?.CurrentFile, StringComparison.OrdinalIgnoreCase)
            && _npColorPickerOverrides.Count > 0;

        private void NpLoadColorPickerOverridesForTrack(string? filePath)
        {
            _npColorPickerActive = false;
            _npColorPickerOverrides.Clear();
            _npColorPickerNextSlot = 0;
            _npColorPickerSessionPicks = 0;
            _npColorPickerOwnerPath = filePath;
            if (NpCoverImage != null)
                NpCoverImage.Cursor = System.Windows.Input.Cursors.Arrow;

            List<System.Windows.Media.Color>? persisted = null;
            if (ThemeManager.NpRememberManualColorPicks && !string.IsNullOrWhiteSpace(filePath))
            {
                persisted = _npColorThemeService.GetManualPicksForFilePath(filePath);
            }

            if (persisted != null)
            {
                int maxColors = ThemeManager.NpColorPickerMaxColors;
                _npColorPickerOverrides.AddRange(persisted.Take(maxColors));
                _npColorPickerNextSlot = _npColorPickerOverrides.Count >= maxColors ? 0 : _npColorPickerOverrides.Count;
            }

            UpdateColorPickerIconBrush();
        }

        private void NpPersistColorPickerOverrides(string? filePath)
        {
            if (!ThemeManager.NpRememberManualColorPicks || string.IsNullOrWhiteSpace(filePath))
                return;

            _npColorThemeService.SetManualPicksForFilePath(filePath, _npColorPickerOverrides);
            SaveNpColorCacheToDisk();
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
            if (NpGetEffectiveColorMatchPalette(out var primary, out _, out _, out _, out _))
            {
                var accent = primary;
                if (_npColorMatchOptionActiveBrush == null || _npColorMatchOptionAccent != accent)
                {
                    _npColorMatchOptionAccent = accent;
                    var activeAccent = EnsureMinLuminance(accent, 165);
                    var inactiveAccent = EnsureMinLuminance(accent, 125);
                    _npColorMatchOptionActiveBrush = new SolidColorBrush(System.Windows.Media.Color.FromArgb(
                        255,
                        activeAccent.R,
                        activeAccent.G,
                        activeAccent.B));
                    _npColorMatchOptionInactiveBrush = new SolidColorBrush(System.Windows.Media.Color.FromArgb(
                        180,
                        inactiveAccent.R,
                        inactiveAccent.G,
                        inactiveAccent.B));
                    _npColorMatchOptionActiveBrush.Freeze();
                    _npColorMatchOptionInactiveBrush.Freeze();
                }

                return active ? _npColorMatchOptionActiveBrush : _npColorMatchOptionInactiveBrush!;
            }
            return (Brush)FindResource(active ? "TextPrimary" : "TextMuted");
        }

        private void NpSetToggleBg(System.Windows.Controls.Button btn, bool active)
        {
            if (NpGetEffectiveColorMatchPalette(out var primary, out _, out _, out _, out _))
            {
                // ColorMatch ON: album-or-neutral tint, never theme/hard-coded white.
                btn.Background = active
                    ? new SolidColorBrush(System.Windows.Media.Color.FromArgb(72, primary.R, primary.G, primary.B))
                    : System.Windows.Media.Brushes.Transparent;
                return;
            }

            // ColorMatch OFF: keep the existing subtle white highlight that pairs with themes.
            btn.Background = active
                ? new SolidColorBrush(System.Windows.Media.Color.FromArgb(0x28, 255, 255, 255))
                : System.Windows.Media.Brushes.Transparent;
        }

        private void NpSetScopedBrush(string key, System.Windows.Media.Color color)
        {
            var brush = new SolidColorBrush(color);
            foreach (var scope in NpGetThemedPopupScopes())
                scope.Resources[key] = brush.CloneCurrentValue();
        }

        private IEnumerable<FrameworkElement> NpGetThemedPopupScopes()
        {
            if (NpBottomBar != null) yield return NpBottomBar;
            if (NpCrossfadePopup != null) yield return NpCrossfadePopup;
            if (NpVizStylePopup != null) yield return NpVizStylePopup;
            if (NpVizStyleMenu != null) yield return NpVizStyleMenu;
            if (NpEqPopup != null) yield return NpEqPopup;
            if (NpBgFxPopup != null) yield return NpBgFxPopup;
            if (NpColorMatchPopup != null) yield return NpColorMatchPopup;
            if (NpTranslatePopup != null) yield return NpTranslatePopup;
            if (NpSaveLyricsMenu != null) yield return NpSaveLyricsMenu;
            if (NpQueuePopup != null) yield return NpQueuePopup;
            if (NpLayoutPopup != null) yield return NpLayoutPopup;
            if (NpSearchPopup != null) yield return NpSearchPopup;
        }

        // The set of theme resource keys ColorMatch overrides. Shared by the NP popups and the
        // Settings window so both tint identically. Keep in sync with NpClearScopedColorResources.
        private static readonly string[] NpScopedColorKeys =
        {
            "PlaybarAccentColor", "PlaybarSecondaryColor", "PlaybarTertiaryColor",
            "ButtonBg", "ButtonHover", "ButtonPressed", "ButtonBorder",
            "GlassPanelBg", "GlassToolbarBg", "GlassFloatingBg", "GlassBorderBrush",
            "ScrollBg", "TextPrimary", "TextSecondary", "TextMuted"
        };

        /// <summary>
        /// Builds the album-derived resource-key → color map used by ColorMatch. Single source
        /// of truth so the NP screen and the Settings window tint identically. Pure (no UI side
        /// effects) so it can be reused anywhere a scope needs the album palette applied.
        /// </summary>
        private Dictionary<string, System.Windows.Media.Color> NpBuildScopedColorMap(
            System.Windows.Media.Color primary,
            System.Windows.Media.Color secondary,
            System.Windows.Media.Color tertiary,
            System.Windows.Media.Color background)
        {
            var accent = EnsureMinLuminance(primary, 150);
            var secondaryAccent = EnsureMinLuminance(secondary, 115);
            var tertiaryAccent = EnsureMinLuminance(tertiary == default ? secondary : tertiary, 100);

            return new Dictionary<string, System.Windows.Media.Color>
            {
                ["PlaybarAccentColor"] = accent,
                ["PlaybarSecondaryColor"] = secondaryAccent,
                ["PlaybarTertiaryColor"] = tertiaryAccent,
                ["ButtonBg"] = System.Windows.Media.Color.FromArgb(44, primary.R, primary.G, primary.B),
                ["ButtonHover"] = System.Windows.Media.Color.FromArgb(76, primary.R, primary.G, primary.B),
                ["ButtonPressed"] = System.Windows.Media.Color.FromArgb(110, primary.R, primary.G, primary.B),
                ["ButtonBorder"] = System.Windows.Media.Color.FromArgb(110, secondaryAccent.R, secondaryAccent.G, secondaryAccent.B),
                ["GlassPanelBg"] = System.Windows.Media.Color.FromArgb(235,
                    (byte)Math.Max(12, background.R / 3), (byte)Math.Max(12, background.G / 3), (byte)Math.Max(16, background.B / 3)),
                ["GlassToolbarBg"] = System.Windows.Media.Color.FromArgb(226,
                    (byte)Math.Max(16, primary.R / 4), (byte)Math.Max(16, primary.G / 4), (byte)Math.Max(20, primary.B / 4)),
                ["GlassFloatingBg"] = System.Windows.Media.Color.FromArgb(244,
                    (byte)Math.Max(18, background.R / 3), (byte)Math.Max(18, background.G / 3), (byte)Math.Max(22, background.B / 3)),
                ["GlassBorderBrush"] = System.Windows.Media.Color.FromArgb(150, secondaryAccent.R, secondaryAccent.G, secondaryAccent.B),
                ["ScrollBg"] = System.Windows.Media.Color.FromArgb(72, background.R, background.G, background.B),
                ["TextPrimary"] = EnsureMinLuminance(primary, 170),
                ["TextSecondary"] = secondaryAccent,
                ["TextMuted"] = EnsureMinLuminance(secondary, 100),
            };
        }

        private void NpApplyScopedColorResources(System.Windows.Media.Color primary, System.Windows.Media.Color secondary, System.Windows.Media.Color tertiary, System.Windows.Media.Color background)
        {
            foreach (var kvp in NpBuildScopedColorMap(primary, secondary, tertiary, background))
                NpSetScopedBrush(kvp.Key, kvp.Value);
        }

        /// <summary>
        /// True when the Settings window should be tinted with album colors instead of the theme:
        /// the NP screen is showing and ColorMatch is active. Outputs the palette to apply.
        /// </summary>
        internal bool NpTryGetSettingsColorMatchBrushes(out Dictionary<string, Brush> brushes)
        {
            brushes = new Dictionary<string, Brush>();
            // Intentionally never tint the Settings window with ColorMatch — Settings always follows
            // the base/standard theme so its controls stay legible and consistent. ColorMatch still
            // applies everywhere else (NP screen, popups, main window). (User request.)
            return false;
        }

        private void NpClearScopedColorResources()
        {
            foreach (var key in NpScopedColorKeys)
            {
                foreach (var scope in NpGetThemedPopupScopes())
                    scope.Resources.Remove(key);
            }
        }

        private System.Windows.Media.Color EnsureMinLuminance(System.Windows.Media.Color c, byte minLum)
        {
            double lum = 0.299 * c.R + 0.587 * c.G + 0.114 * c.B;
            if (lum >= minLum) return c;
            if (lum < 1)
                return System.Windows.Media.Color.FromRgb(minLum, minLum, minLum);
            double factor = minLum / Math.Max(lum, 1);
            return System.Windows.Media.Color.FromRgb(
                (byte)Math.Min(255, (int)(c.R * factor)),
                (byte)Math.Min(255, (int)(c.G * factor)),
                (byte)Math.Min(255, (int)(c.B * factor)));
        }

        /// <summary>
        /// ThemeManager.ThemeChanged handler. A theme switch overwrites the global resource
        /// dictionary, wiping the album-derived scoped brushes on the NP popups/bottom bar.
        /// When ColorMatch is active and NP is showing, re-apply them so the screen stays
        /// album-colored (theme-independent) instead of flashing to the new theme.
        /// </summary>
        private void OnThemeChangedReapplyColorMatch()
        {
            if (!_npVisible || !_npColorMatchEnabled) return;
            Dispatcher.InvokeAsync(() =>
            {
                NpResetColorMatchCaches();
                NpApplyColorMatchMode();
                NpRenderPlaybarStyle();
            }, DispatcherPriority.Render);
        }

        private void NpApplyColorMatchMode()
        {
            // User-picked color overrides (eyedropper) replace the auto-extracted album palette
            // when set for the currently playing track. Overrides clear naturally on track change.
            var (ovPrimary, ovSecondary, ovTertiary) = NpResolveColorOverrides();
            if (NpHasColorPickerOverridesForCurrentTrack())
            {
                var sanitized = NpBuildManualPickPalette(ovPrimary, ovSecondary, ovTertiary);
                var overrideBackground = sanitized.background;
                if (_npAlbumPrimary != sanitized.primary
                    || _npAlbumSecondary != sanitized.secondary
                    || _npAlbumTertiary != sanitized.tertiary
                    || _npAlbumBackground != overrideBackground)
                {
                    _npAlbumPrimary = sanitized.primary;
                    _npAlbumSecondary = sanitized.secondary;
                    _npAlbumTertiary = sanitized.tertiary;
                    _npAlbumBackground = overrideBackground;
                    NpResetColorMatchCaches();
                }
                NpApplyCoverGlowBrushes(_npAlbumPrimary, _npAlbumSecondary);
                NpCoverShadow.Color = EnsureMinLuminance(_npAlbumPrimary, 80);
                NpCoverShadow.Opacity = 0.6;
                var manualBg1 = System.Windows.Media.Color.FromArgb(225,
                    (byte)Math.Max(10, _npAlbumBackground.R / 3),
                    (byte)Math.Max(10, _npAlbumBackground.G / 3),
                    (byte)Math.Max(10, _npAlbumBackground.B / 3));
                var manualBg2 = System.Windows.Media.Color.FromArgb(225,
                    (byte)Math.Max(6, sanitized.tertiary.R / 5),
                    (byte)Math.Max(6, sanitized.tertiary.G / 5),
                    (byte)Math.Max(6, sanitized.tertiary.B / 5));
                // With more than the three palette picks, render every pick as a gradient stop so
                // the extras are visible; otherwise keep the simple two-color background.
                var pickStops = NpBuildPickGradientStops();
                NpBgGradient.Background = pickStops != null
                    ? new LinearGradientBrush(pickStops, 45)
                    : new LinearGradientBrush(manualBg1, manualBg2, 45);
                // Also bias visualizer colors so they match the user's picks
                _npVizColorPrimary = EnsureMinLuminance(sanitized.primary, 120);
                _npVizColorSecondary = EnsureMinLuminance(sanitized.secondary, 120);
            }

            if (NpGetEffectiveColorMatchPalette(out var cmPrimary, out var cmSecondary, out var cmTertiary, out var cmBackground, out _))
            {
                // ColorMatch ON. Paint from the effective palette (real album colors when
                // available, otherwise a theme-independent neutral palette). No theme tokens.
                NpApplyScopedColorResources(cmPrimary, cmSecondary, cmTertiary, cmBackground);

                // Base panel fill. NowPlayingPanel.Background is `{DynamicResource WindowBg}` in
                // XAML, which auto-updates when the theme dictionary changes — and because the
                // gradient/backdrop above it aren't fully opaque, that theme color bleeds through
                // on a theme switch. Pin it to a fixed dark album-derived color so ColorMatch is
                // truly theme-independent. Reset to the theme token in the OFF branch below.
                NpPanelBaseBrush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(
                    (byte)Math.Max(6, cmBackground.R / 4),
                    (byte)Math.Max(6, cmBackground.G / 4),
                    (byte)Math.Max(8, cmBackground.B / 4)));
                NpPanelBaseBrush.Freeze();
                NowPlayingPanel.Background = NpPanelBaseBrush;

                // Background: stronger gradient; the dark overlay's strength is driven by the
                // user's background-brightness setting (lighter ↔ darker), used for BOTH the static
                // colormatch background and Color Drift so drift never looks darker than static.
                NpBgGradient.Opacity = 1.0;
                NpDarkOverlay.Background = new SolidColorBrush(
                    System.Windows.Media.Color.FromArgb(NpColorMatchOverlayAlpha(), 0, 0, 0));

                // Bottom bar: deeply tinted
                NpBottomBar.Background = NpBottomBar.Resources["GlassToolbarBg"] as Brush
                    ?? new SolidColorBrush(System.Windows.Media.Color.FromArgb(226,
                        (byte)Math.Max(16, cmPrimary.R / 4),
                        (byte)Math.Max(16, cmPrimary.G / 4),
                        (byte)Math.Max(20, cmPrimary.B / 4)));
                NpBottomBar.BorderBrush = NpBottomBar.Resources["GlassBorderBrush"] as Brush
                    ?? new SolidColorBrush(System.Windows.Media.Color.FromArgb(120,
                        cmPrimary.R, cmPrimary.G, cmPrimary.B));

                // Title highlight with brightened primary
                var bright = System.Windows.Media.Color.FromRgb(
                    (byte)Math.Min(255, cmPrimary.R + 100),
                    (byte)Math.Min(255, cmPrimary.G + 100),
                    (byte)Math.Min(255, cmPrimary.B + 100));
                NpSongTitle.Foreground = new SolidColorBrush(bright);
                NpBigTitle.Foreground = new SolidColorBrush(bright);

                // Artist text with lighter secondary
                NpSongArtist.Foreground = new SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(
                        (byte)Math.Min(255, cmSecondary.R + 80),
                        (byte)Math.Min(255, cmSecondary.G + 80),
                        (byte)Math.Min(255, cmSecondary.B + 80)));

                // Specs text brighter
                NpSongSpecs.Foreground = new SolidColorBrush(
                    System.Windows.Media.Color.FromArgb(200,
                        (byte)Math.Min(255, cmPrimary.R + 60),
                        (byte)Math.Min(255, cmPrimary.G + 60),
                        (byte)Math.Min(255, cmPrimary.B + 60)));

                // Tint Queue badge to match album colors
                NpNextTrackBorder.Background = new SolidColorBrush(
                    System.Windows.Media.Color.FromArgb(50,
                        cmPrimary.R, cmPrimary.G, cmPrimary.B));
                NpNextTrackLabel.Foreground = new SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(
                        (byte)Math.Min(255, cmSecondary.R + 80),
                        (byte)Math.Min(255, cmSecondary.G + 80),
                        (byte)Math.Min(255, cmSecondary.B + 80)));
                NpNextTrackText.Foreground = new SolidColorBrush(bright);

                // Tint all icon paths in the bottom bar with album colors
                var iconTint = new SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(
                        (byte)Math.Min(255, cmPrimary.R + 80),
                        (byte)Math.Min(255, cmPrimary.G + 80),
                        (byte)Math.Min(255, cmPrimary.B + 80)));
                var controlTint = new SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(
                        (byte)Math.Min(255, cmSecondary.R + 100),
                        (byte)Math.Min(255, cmSecondary.G + 100),
                        (byte)Math.Min(255, cmSecondary.B + 100)));

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
                NpUpdateFocusedLyricsIcon();
                NpUpdateKaraokeIcon();
                NpUpdateColorMatchIcon();
                NpUpdateCrossfadeIcon();

                // Tint remaining static icons that don't have toggle states
                NpVizStyleIcon.Stroke = iconTint;
                NpProviderCircle.Stroke = iconTint;
                NpProviderArc.Stroke = new SolidColorBrush(bright);
                NpSearchIcon.Foreground = iconTint;
                NpSaveLyricsIcon.Stroke = iconTint;
                NpSettingsGear.Stroke = iconTint;
                NpSettingsCenter.Stroke = iconTint;
                NpTranslateSettingsIcon.Stroke = iconTint;
                NpLayoutIcon.Stroke = iconTint;
                NpQueueIcon.Foreground = iconTint;
                NpEqIcon.Stroke = iconTint;
                NpEqKnob1.Fill = iconTint;
                NpEqKnob2.Fill = iconTint;
                NpEqKnob3.Fill = iconTint;

                // Store colors for visualizer tinting (used by render methods)
                // Boost to minimum luminance so bars are clearly visible
                _npVizColorPrimary = EnsureMinLuminance(cmPrimary, 120);
                _npVizColorSecondary = EnsureMinLuminance(cmSecondary, 120);
                _eqSliderTemplateCache = null;
                if (EqPanel.Visibility == Visibility.Visible)
                {
                    ApplyEqualizerPanelTheme();
                    InitializeEqualizerSliders();
                }
                ApplyThemeTitleBar();
            }
            else
            {
                NpClearScopedColorResources();

                // Restore defaults
                NpPanelBaseBrush = null;
                NowPlayingPanel.SetResourceReference(System.Windows.Controls.Panel.BackgroundProperty, "WindowBg");
                NpBgGradient.Opacity = 0.6;
                NpDarkOverlay.Background = new SolidColorBrush(
                    System.Windows.Media.Color.FromArgb(102, 0, 0, 0));
                NpBottomBar.Background = (Brush)FindResource("GlassToolbarBg");
                NpBottomBar.BorderBrush = (Brush)FindResource("GlassBorderBrush");
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
                NpUpdateFocusedLyricsIcon();
                NpUpdateKaraokeIcon();
                NpUpdateColorMatchIcon();
                NpUpdateCrossfadeIcon();

                // Restore static icon colors to theme defaults
                var muted = (Brush)FindResource("TextMuted");
                NpVizStyleIcon.Stroke = muted;
                NpProviderCircle.Stroke = muted;
                NpProviderArc.Stroke = (Brush)FindResource("AccentColor");
                NpSearchIcon.Foreground = muted;
                NpSaveLyricsIcon.Stroke = muted;
                NpSettingsGear.Stroke = muted;
                NpSettingsCenter.Stroke = muted;
                NpTranslateSettingsIcon.Stroke = muted;
                NpLayoutIcon.Stroke = muted;
                NpQueueIcon.Foreground = muted;
                NpEqIcon.Stroke = muted;
                NpEqKnob1.Fill = muted;
                NpEqKnob2.Fill = muted;
                NpEqKnob3.Fill = muted;

                _npVizColorPrimary = default;
                _npVizColorSecondary = default;
                _eqSliderTemplateCache = null;
                if (EqPanel.Visibility == Visibility.Visible)
                {
                    ApplyEqualizerPanelTheme();
                    InitializeEqualizerSliders();
                }
                ApplyThemeTitleBar();
            }
        }

        // ═══════════════════════════════════════════
        //  Main Window Color Match
        // ═══════════════════════════════════════════

        private async Task LoadMainCoverColors(string filePath)
        {
            int gen = _npColorGeneration;

            // Fast-path: if colors are already cached, apply instantly so the main
            // window doesn't flash the default theme while TagLib + image decode runs.
            string cacheKey = HashPath(filePath);
            if (TryGetNpColorFromCache(cacheKey, out var cachedColors))
            {
                Dispatcher.Invoke(() =>
                {
                    if (gen != _npColorGeneration) return;
                    _mainAlbumPrimary = System.Windows.Media.Color.FromRgb(cachedColors.Primary.R, cachedColors.Primary.G, cachedColors.Primary.B);
                    _mainAlbumSecondary = System.Windows.Media.Color.FromRgb(cachedColors.Secondary.R, cachedColors.Secondary.G, cachedColors.Secondary.B);
                    _mainAlbumTertiary = System.Windows.Media.Color.FromRgb(cachedColors.Tertiary.R, cachedColors.Tertiary.G, cachedColors.Tertiary.B);
                    ApplyMainColorMatch();
                });
                // Fall through to re-extract and update cache in the background.
            }

            try
            {
                var imageData = await Task.Run(() => NpReadCoverImageData(filePath));
                if (imageData == null)
                {
                    Dispatcher.Invoke(() =>
                    {
                        if (gen != _npColorGeneration) return;
                        if (!ThemeManager.MainColorMatchEnabled)
                        {
                            _mainAlbumPrimary = default;
                            _mainAlbumSecondary = default;
                            _mainAlbumTertiary = default;
                            RestoreMainColorMatch();
                        }
                        else if (_mainAlbumPrimary != default)
                        {
                            // Color match is on but the new track has no art; re-apply the
                            // previous track's colors so they survive any intervening reset.
                            ApplyMainColorMatch();
                        }
                    });
                    return;
                }

                var colors = await Task.Run(() =>
                {
                    var bmp = NpDecodeCoverBitmap(imageData);
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
                    _mainAlbumTertiary = System.Windows.Media.Color.FromRgb(colors.Tertiary.R, colors.Tertiary.G, colors.Tertiary.B);
                    ApplyMainColorMatch();

                    // Re-apply at ContextIdle so album colors win over resource writes from
                    // the same dispatch frame, such as NP cache-hit theme refreshes.
                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        if (gen != _npColorGeneration) return;
                        if (ThemeManager.MainColorMatchEnabled && _mainAlbumPrimary != default)
                            ApplyMainColorMatch();
                        if (_npColorMatchEnabled && (_npAlbumPrimary != default || NpHasColorPickerOverridesForCurrentTrack()))
                            NpApplyColorMatchMode();
                    }), System.Windows.Threading.DispatcherPriority.ContextIdle);
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[LoadMainCoverColors] {ex}");
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
                        ApplyMainColorMatch();
                    }
                });
            }
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
            var brightTertiary = EnsureAccentReadable(_mainAlbumTertiary == default ? _mainAlbumSecondary : _mainAlbumTertiary, 100);

            // Derive darker tints for theme resources
            var accentBrush = new SolidColorBrush(brightPrimary);
            var secondaryBrush = new SolidColorBrush(brightSecondary);
            var tertiaryBrush = new SolidColorBrush(brightTertiary);
            var borderBrush = new SolidColorBrush(Darken(brightPrimary, 0.55));
            var hoverBrush = new SolidColorBrush(Darken(brightPrimary, 0.40));
            var selectionBrush = new SolidColorBrush(Darken(brightPrimary, 0.30));
            var toolbarBrush = new SolidColorBrush(System.Windows.Media.Color.FromArgb(
                245,
                (byte)Math.Max(18, _mainAlbumPrimary.R / 4),
                (byte)Math.Max(18, _mainAlbumPrimary.G / 4),
                (byte)Math.Max(18, _mainAlbumPrimary.B / 4)));

            Application.Current.Resources["AccentColor"] = accentBrush;
            Application.Current.Resources["BorderColor"] = borderBrush;
            Application.Current.Resources["ButtonHover"] = hoverBrush;
            Application.Current.Resources["SelectionBg"] = selectionBrush;
            Application.Current.Resources["PlaybarAccentColor"] = accentBrush;
            Application.Current.Resources["PlaybarSecondaryColor"] = secondaryBrush;
            Application.Current.Resources["PlaybarTertiaryColor"] = tertiaryBrush;
            Application.Current.Resources["ToolbarBg"] = toolbarBrush;
            ThemeManager.ApplyColorMatchSurfaceResources(brightPrimary, brightSecondary, _mainAlbumPrimary);
            ApplyThemeTitleBar();

            // Sync mini player only if it wants ColorMatch — it owns its own ColorMatch choice,
            // so the main window must not force it on (or off) on every track change.
            if (ThemeManager.MiniColorMatchEnabled)
            {
                _miniPlayerWindow?.ApplyColorMatch(_mainAlbumPrimary, _mainAlbumSecondary);
                _miniPlayerWindow?.RefreshThemeResources();
            }

            // Tint main playback icons
            PlayIcon.Fill = accentBrush;
            PauseIcon.Fill = accentBrush;

            // Tint shuffle / loop / EQ / volume icons
            ShuffleIcon.Stroke = secondaryBrush;
            LoopIcon.Stroke = secondaryBrush;
            if (EqIcon != null) EqIcon.Stroke = secondaryBrush;
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
            ApplyThemeTitleBar();
            // Don't wipe the mini player's ColorMatch when it owns it — the main window being off
            // must not force the mini off on every track change. The mini self-updates its colors.
            if (!ThemeManager.MiniColorMatchEnabled)
            {
                _miniPlayerWindow?.ClearColorMatch();
                _miniPlayerWindow?.RefreshThemeResources();
            }

            PlayIcon.Fill = (Brush)FindResource("TextPrimary");
            PauseIcon.Fill = (Brush)FindResource("TextPrimary");
            ShuffleIcon.Stroke = (Brush)FindResource("TextMuted");
            LoopIcon.Stroke = (Brush)FindResource("TextMuted");
            if (EqIcon != null) EqIcon.Stroke = (Brush)FindResource("TextMuted");
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


    }
}
