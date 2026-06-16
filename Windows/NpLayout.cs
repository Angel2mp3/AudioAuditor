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
    {        // ─── NP Layout Customization ───

        private bool _npLayoutPopupInit;

        private void NpLoadActiveLayoutProfile(bool fullscreen, bool visualizerEnabled)
        {
            if (fullscreen && visualizerEnabled)
            {
                _npCoverSize = ThemeManager.NpFullscreenVizOnCoverSize;
                _npTitleSize = ThemeManager.NpFullscreenVizOnTitleSize;
                _npSubTextSize = ThemeManager.NpFullscreenVizOnSubTextSize;
                _npLyricsSize = ThemeManager.NpFullscreenVizOnLyricsSize;
                _npVizSize = ThemeManager.NpFullscreenVizOnVizSize;
                _npLyricsOffsetX = ThemeManager.NpFullscreenVizOnLyricsOffsetX;
                _npCoverOffsetX = ThemeManager.NpFullscreenVizOnCoverOffsetX;
                _npCoverOffsetY = ThemeManager.NpFullscreenVizOnCoverOffsetY;
                _npTitleOffsetX = ThemeManager.NpFullscreenVizOnTitleOffsetX;
                _npTitleOffsetY = ThemeManager.NpFullscreenVizOnTitleOffsetY;
                _npArtistOffsetX = ThemeManager.NpFullscreenVizOnArtistOffsetX;
                _npArtistOffsetY = ThemeManager.NpFullscreenVizOnArtistOffsetY;
                _npVizOffsetY = ThemeManager.NpFullscreenVizOnVizOffsetY;
                _npVizPlacement = ThemeManager.NpFullscreenVizOnPlacement;
                return;
            }

            if (fullscreen)
            {
                _npCoverSize = ThemeManager.NpFullscreenCoverSize;
                _npTitleSize = ThemeManager.NpFullscreenTitleSize;
                _npSubTextSize = ThemeManager.NpFullscreenSubTextSize;
                _npLyricsSize = ThemeManager.NpFullscreenLyricsSize;
                _npVizSize = ThemeManager.NpFullscreenVizSize;
                _npLyricsOffsetX = ThemeManager.NpFullscreenLyricsOffsetX;
                _npCoverOffsetX = ThemeManager.NpFullscreenCoverOffsetX;
                _npCoverOffsetY = ThemeManager.NpFullscreenCoverOffsetY;
                _npTitleOffsetX = ThemeManager.NpFullscreenTitleOffsetX;
                _npTitleOffsetY = ThemeManager.NpFullscreenTitleOffsetY;
                _npArtistOffsetX = ThemeManager.NpFullscreenArtistOffsetX;
                _npArtistOffsetY = ThemeManager.NpFullscreenArtistOffsetY;
                _npVizOffsetY = ThemeManager.NpFullscreenVizOffsetY;
                _npVizPlacement = ThemeManager.NpFullscreenVizPlacement;
                return;
            }

            if (visualizerEnabled)
            {
                _npCoverSize = ThemeManager.NpVizOnCoverSize;
                _npTitleSize = ThemeManager.NpVizOnTitleSize;
                _npSubTextSize = ThemeManager.NpVizOnSubTextSize;
                _npLyricsSize = ThemeManager.NpVizOnLyricsSize;
                _npVizSize = ThemeManager.NpVizOnVizSize;
                _npLyricsOffsetX = ThemeManager.NpVizOnLyricsOffsetX;
                _npCoverOffsetX = ThemeManager.NpVizOnCoverOffsetX;
                _npCoverOffsetY = ThemeManager.NpVizOnCoverOffsetY;
                _npTitleOffsetX = ThemeManager.NpVizOnTitleOffsetX;
                _npTitleOffsetY = ThemeManager.NpVizOnTitleOffsetY;
                _npArtistOffsetX = ThemeManager.NpVizOnArtistOffsetX;
                _npArtistOffsetY = ThemeManager.NpVizOnArtistOffsetY;
                _npVizOffsetY = ThemeManager.NpVizOnVizOffsetY;
                _npVizPlacement = ThemeManager.NpVizOnPlacement;
                return;
            }

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
            _npVizPlacement = ThemeManager.NpVizPlacement;
        }

        private void NpSaveActiveLayoutProfile(bool fullscreen, bool visualizerEnabled)
        {
            if (fullscreen && visualizerEnabled)
            {
                ThemeManager.NpFullscreenVizOnCoverSize = _npCoverSize;
                ThemeManager.NpFullscreenVizOnTitleSize = _npTitleSize;
                ThemeManager.NpFullscreenVizOnSubTextSize = _npSubTextSize;
                ThemeManager.NpFullscreenVizOnLyricsSize = _npLyricsSize;
                ThemeManager.NpFullscreenVizOnVizSize = _npVizSize;
                ThemeManager.NpFullscreenVizOnLyricsOffsetX = _npLyricsOffsetX;
                ThemeManager.NpFullscreenVizOnCoverOffsetX = _npCoverOffsetX;
                ThemeManager.NpFullscreenVizOnCoverOffsetY = _npCoverOffsetY;
                ThemeManager.NpFullscreenVizOnTitleOffsetX = _npTitleOffsetX;
                ThemeManager.NpFullscreenVizOnTitleOffsetY = _npTitleOffsetY;
                ThemeManager.NpFullscreenVizOnArtistOffsetX = _npArtistOffsetX;
                ThemeManager.NpFullscreenVizOnArtistOffsetY = _npArtistOffsetY;
                ThemeManager.NpFullscreenVizOnVizOffsetY = _npVizOffsetY;
                ThemeManager.NpFullscreenVizOnPlacement = _npVizPlacement;
                return;
            }

            if (fullscreen)
            {
                ThemeManager.NpFullscreenCoverSize = _npCoverSize;
                ThemeManager.NpFullscreenTitleSize = _npTitleSize;
                ThemeManager.NpFullscreenSubTextSize = _npSubTextSize;
                ThemeManager.NpFullscreenLyricsSize = _npLyricsSize;
                ThemeManager.NpFullscreenVizSize = _npVizSize;
                ThemeManager.NpFullscreenLyricsOffsetX = _npLyricsOffsetX;
                ThemeManager.NpFullscreenCoverOffsetX = _npCoverOffsetX;
                ThemeManager.NpFullscreenCoverOffsetY = _npCoverOffsetY;
                ThemeManager.NpFullscreenTitleOffsetX = _npTitleOffsetX;
                ThemeManager.NpFullscreenTitleOffsetY = _npTitleOffsetY;
                ThemeManager.NpFullscreenArtistOffsetX = _npArtistOffsetX;
                ThemeManager.NpFullscreenArtistOffsetY = _npArtistOffsetY;
                ThemeManager.NpFullscreenVizOffsetY = _npVizOffsetY;
                ThemeManager.NpFullscreenVizPlacement = _npVizPlacement;
                return;
            }

            if (visualizerEnabled)
            {
                ThemeManager.NpVizOnCoverSize = _npCoverSize;
                ThemeManager.NpVizOnTitleSize = _npTitleSize;
                ThemeManager.NpVizOnSubTextSize = _npSubTextSize;
                ThemeManager.NpVizOnLyricsSize = _npLyricsSize;
                ThemeManager.NpVizOnVizSize = _npVizSize;
                ThemeManager.NpVizOnLyricsOffsetX = _npLyricsOffsetX;
                ThemeManager.NpVizOnCoverOffsetX = _npCoverOffsetX;
                ThemeManager.NpVizOnCoverOffsetY = _npCoverOffsetY;
                ThemeManager.NpVizOnTitleOffsetX = _npTitleOffsetX;
                ThemeManager.NpVizOnTitleOffsetY = _npTitleOffsetY;
                ThemeManager.NpVizOnArtistOffsetX = _npArtistOffsetX;
                ThemeManager.NpVizOnArtistOffsetY = _npArtistOffsetY;
                ThemeManager.NpVizOnVizOffsetY = _npVizOffsetY;
                ThemeManager.NpVizOnPlacement = _npVizPlacement;
                return;
            }

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
            ThemeManager.NpVizPlacement = _npVizPlacement;
        }

        private void NpSeedLayoutSliders(bool fullscreen)
        {
            _npLayoutPopupInit = true;
            NpLayoutCoverSlider.Value = _npCoverSize > 0 ? _npCoverSize : (fullscreen ? 420 : 300);
            NpLayoutTitleSlider.Value = _npTitleSize > 0 ? _npTitleSize : (fullscreen ? 32 : 24);
            NpLayoutSubSlider.Value = _npSubTextSize > 0 ? _npSubTextSize : (fullscreen ? 12 : 10);
            NpLayoutLyricsSlider.Value = _npLyricsSize > 0 ? _npLyricsSize : (fullscreen ? 22 : 18);
            NpLayoutLyricsPosSlider.Value = _npLyricsOffsetX;
            NpLayoutVizSlider.Value = _npVizSize > 0 ? _npVizSize : (int)_npVizBarHeight;
            NpLayoutCoverGlowSlider.Value = Math.Clamp(_npCoverGlowSize, 0, 2.0);
            if (NpFocusedLyricsBlurSlider != null)
                NpFocusedLyricsBlurSlider.Value = Math.Clamp(_npFocusedLyricsBlurRadius, 0, 16.0);
            if (NpGlowMotionCheck != null)
                NpGlowMotionCheck.IsChecked = _npCoverGlowMotionEnabled;
            if (NpGlowMotionModeCombo != null)
                NpGlowMotionModeCombo.SelectedIndex = (int)_npGlowMotionMode;
            if (NpBackdropFocusXSlider != null)
                NpBackdropFocusXSlider.Value = Math.Clamp(ThemeManager.NpBackgroundHorizontalPosition, 0, 1);
            if (NpBackdropFocusYSlider != null)
                NpBackdropFocusYSlider.Value = Math.Clamp(ThemeManager.NpBackgroundVerticalPosition, 0, 1);
            NpSyncBackdropVerticalPreset();
            if (NpBackdropZoomSlider != null)
                NpBackdropZoomSlider.Value = Math.Clamp(ThemeManager.NpBackgroundZoom, 1, 2.5);
            if (NpBackdropBlurSlider != null)
                NpBackdropBlurSlider.Value = Math.Clamp(ThemeManager.NpBackgroundBlur, 0, 48);
            if (NpBackdropBrightnessSlider != null)
                NpBackdropBrightnessSlider.Value = Math.Clamp(ThemeManager.NpBackgroundBrightness, 0.35, 1.6);
            if (NpCoverShapeCombo != null)
                NpCoverShapeCombo.SelectedIndex = ThemeManager.NpCoverShapeMode switch
                {
                    "Rounded" => 1,
                    "Circle" => 2,
                    _ => 0
                };
            NpLayoutCoverXSlider.Value = _npCoverOffsetX;
            NpLayoutCoverYSlider.Value = _npCoverOffsetY;
            NpLayoutTitleXSlider.Value = _npTitleOffsetX;
            NpLayoutTitleYSlider.Value = _npTitleOffsetY;
            NpLayoutArtistXSlider.Value = _npArtistOffsetX;
            NpLayoutArtistYSlider.Value = _npArtistOffsetY;
            NpLayoutVizYSlider.Value = _npVizOffsetY;
            _npLayoutPopupInit = false;
        }

        private bool NpEnsureLayoutProfileForCurrentWindowState()
        {
            if (WindowState == WindowState.Minimized)
                return false;

            bool fullscreen = WindowState == WindowState.Maximized;
            bool visualizerEnabled = _npVisualizerEnabled;
            if (!_npPrefsLoaded)
            {
                _npLayoutProfileIsFullscreen = fullscreen;
                _npLayoutProfileVisualizerEnabled = visualizerEnabled;
                return false;
            }

            if (_npLayoutProfileIsFullscreen == fullscreen &&
                _npLayoutProfileVisualizerEnabled == visualizerEnabled)
                return false;

            NpSaveActiveLayoutProfile(_npLayoutProfileIsFullscreen, _npLayoutProfileVisualizerEnabled);
            _npLayoutProfileIsFullscreen = fullscreen;
            _npLayoutProfileVisualizerEnabled = visualizerEnabled;
            NpLoadActiveLayoutProfile(fullscreen, visualizerEnabled);
            NpApplyVizPlacement();
            if (NpLayoutPopup?.IsOpen == true)
                NpSeedLayoutSliders(fullscreen);
            ThemeManager.SavePlayOptions();
            return true;
        }

        private void NpLayoutCustomize_Click(object sender, RoutedEventArgs e)
        {
            NpEnsureLayoutProfileForCurrentWindowState();
            bool fs = WindowState == WindowState.Maximized;
            NpSeedLayoutSliders(fs);
            NpRefreshLayoutProfilesList();
            NpRefreshButtonCustomizeList();
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
            NpEnsureLayoutProfileForCurrentWindowState();

            _npCoverSize = (int)NpLayoutCoverSlider.Value;
            _npTitleSize = (int)NpLayoutTitleSlider.Value;
            _npSubTextSize = (int)NpLayoutSubSlider.Value;
            _npLyricsSize = (int)NpLayoutLyricsSlider.Value;
            _npLyricsOffsetX = (int)NpLayoutLyricsPosSlider.Value;
            _npVizSize = (int)NpLayoutVizSlider.Value;
            _npCoverGlowSize = NpLayoutCoverGlowSlider.Value;
            double blurRadius = Math.Clamp(NpFocusedLyricsBlurSlider.Value, 0, 16.0);
            if (Math.Abs(_npFocusedLyricsBlurRadius - blurRadius) > 0.001)
            {
                _npFocusedLyricsBlurRadius = blurRadius;
                _npFocusedLyricsInactiveBlur = null;
                NpApplyFocusedLyricsEffects();
            }
            NpApplyCoverGlowScale();
            _npCoverOffsetX = (int)NpLayoutCoverXSlider.Value;
            _npCoverOffsetY = (int)NpLayoutCoverYSlider.Value;
            _npTitleOffsetX = (int)NpLayoutTitleXSlider.Value;
            _npTitleOffsetY = (int)NpLayoutTitleYSlider.Value;
            _npArtistOffsetX = (int)NpLayoutArtistXSlider.Value;
            _npArtistOffsetY = (int)NpLayoutArtistYSlider.Value;
            _npVizOffsetY = (int)NpLayoutVizYSlider.Value;
            NpApplyBackdropSettingsFromLayout();

            // Live preview
            NpApplyFullscreenScaling(WindowState == WindowState.Maximized);
        }

        private void NpApplyBackdropSettingsFromLayout()
        {
            if (NpBackdropFocusXSlider == null || NpBackdropFocusYSlider == null ||
                NpBackdropZoomSlider == null || NpBackdropBlurSlider == null ||
                NpBackdropBrightnessSlider == null)
            {
                return;
            }

            ThemeManager.NpBackgroundHorizontalPosition = Math.Clamp(NpBackdropFocusXSlider.Value, 0, 1);
            ThemeManager.NpBackgroundVerticalPosition = Math.Clamp(NpBackdropFocusYSlider.Value, 0, 1);
            ThemeManager.NpBackgroundZoom = Math.Clamp(NpBackdropZoomSlider.Value, 1, 2.5);
            ThemeManager.NpBackgroundBlur = Math.Clamp(NpBackdropBlurSlider.Value, 0, 48);
            ThemeManager.NpBackgroundBrightness = Math.Clamp(NpBackdropBrightnessSlider.Value, 0.35, 1.6);
            NpSyncBackdropVerticalPreset();
            NpRefreshBackdropFromSettings();
            // Brightness also drives the colormatch/drift background overlay — refresh it live.
            if (_npColorMatchEnabled)
                NpApplyColorMatchMode();
        }

        private void NpSyncBackdropVerticalPreset()
        {
            if (NpBackdropVerticalPresetCombo == null || NpBackdropFocusYSlider == null)
                return;

            double value = Math.Clamp(NpBackdropFocusYSlider.Value, 0, 1);
            int index =
                Math.Abs(value - 0.0) < 0.001 ? 0 :
                Math.Abs(value - 0.5) < 0.001 ? 1 :
                Math.Abs(value - 1.0) < 0.001 ? 2 :
                -1;

            if (NpBackdropVerticalPresetCombo.SelectedIndex != index)
                NpBackdropVerticalPresetCombo.SelectedIndex = index;
        }

        private void NpBackdropVerticalPresetCombo_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (_npLayoutPopupInit || !IsLoaded || NpBackdropFocusYSlider == null)
                return;
            if (NpBackdropVerticalPresetCombo.SelectedIndex < 0)
                return;

            double value = NpBackdropVerticalPresetCombo.SelectedIndex switch
            {
                0 => 0.0,
                2 => 1.0,
                _ => 0.5
            };

            NpBackdropFocusYSlider.Value = value;
            NpApplyBackdropSettingsFromLayout();
        }

        private void NpGlowMotion_Changed(object sender, RoutedEventArgs e)
        {
            if (_npLayoutPopupInit || !IsLoaded) return;
            _npCoverGlowMotionEnabled = NpGlowMotionCheck.IsChecked == true;
            if (!_npCoverGlowMotionEnabled)
            {
                NpStopGlowPulse();
            }
            if (!_npCoverGlowMotionEnabled && _npAlbumPrimary != default)
                NpApplyCoverGlowBrushes(_npAlbumPrimary, _npAlbumSecondary);
            if (_npCoverGlowMotionEnabled && IsNowPlayingUiActive())
                NpStartGlowPulse();
            NpSavePreferences();
        }

        private void NpLayoutControlReset_MouseRightButtonUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (sender is not FrameworkElement target || target.Tag is not string key)
                return;

            var menu = new ContextMenu { PlacementTarget = target };
            var item = new MenuItem { Header = "Reset to Default", Tag = key };
            item.Click += (_, _) => NpResetLayoutControl(key);
            menu.Items.Add(item);
            menu.IsOpen = true;
            e.Handled = true;
        }

        private void NpResetLayoutControl(string key)
        {
            NpEnsureLayoutProfileForCurrentWindowState();
            bool fs = WindowState == WindowState.Maximized;

            _npLayoutPopupInit = true;
            switch (key)
            {
                case "GlowMotion":
                    _npCoverGlowMotionEnabled = true;
                    NpGlowMotionCheck.IsChecked = true;
                    if (IsNowPlayingUiActive())
                        NpStartGlowPulse();
                    break;
                case "MotionMode":
                    _npGlowMotionMode = GlowMotionMode.Swirl;
                    _npGlowRandomLastSwapMs = 0;
                    NpGlowMotionModeCombo.SelectedIndex = (int)_npGlowMotionMode;
                    break;
                case "CoverGlow":
                    _npCoverGlowSize = 1.0;
                    NpLayoutCoverGlowSlider.Value = 1.0;
                    break;
                case "FocusedBlur":
                    _npFocusedLyricsBlurRadius = 6.5;
                    _npFocusedLyricsInactiveBlur = null;
                    NpFocusedLyricsBlurSlider.Value = 6.5;
                    break;
                case "BackdropHorizontal":
                case "BackdropFocusX":
                    ThemeManager.NpBackgroundHorizontalPosition = 0.5;
                    NpBackdropFocusXSlider.Value = 0.5;
                    break;
                case "BackdropVertical":
                case "BackdropFocusY":
                    ThemeManager.NpBackgroundVerticalPosition = 0.5;
                    NpBackdropFocusYSlider.Value = 0.5;
                    NpSyncBackdropVerticalPreset();
                    break;
                case "BackdropVerticalPreset":
                    ThemeManager.NpBackgroundVerticalPosition = 0.5;
                    NpBackdropFocusYSlider.Value = 0.5;
                    NpBackdropVerticalPresetCombo.SelectedIndex = 1;
                    break;
                case "BackdropZoom":
                    ThemeManager.NpBackgroundZoom = 1.0;
                    NpBackdropZoomSlider.Value = 1.0;
                    break;
                case "BackdropBlur":
                    ThemeManager.NpBackgroundBlur = 24.0;
                    NpBackdropBlurSlider.Value = 24.0;
                    break;
                case "BackdropBrightness":
                    ThemeManager.NpBackgroundBrightness = 1.0;
                    NpBackdropBrightnessSlider.Value = 1.0;
                    break;
                case "CoverShape":
                    ThemeManager.NpCoverShapeMode = "Default";
                    NpCoverShapeCombo.SelectedIndex = 0;
                    NpApplyCoverShape();
                    break;
                case "CoverSize":
                    _npCoverSize = 0;
                    NpLayoutCoverSlider.Value = fs ? 420 : 300;
                    break;
                case "TitleSize":
                    _npTitleSize = 0;
                    NpLayoutTitleSlider.Value = fs ? 32 : 24;
                    break;
                case "SubTextSize":
                    _npSubTextSize = 0;
                    NpLayoutSubSlider.Value = fs ? 12 : 10;
                    break;
                case "LyricsSize":
                    _npLyricsSize = 0;
                    NpLayoutLyricsSlider.Value = fs ? 22 : 18;
                    break;
                case "LyricsOffsetX":
                    _npLyricsOffsetX = 0;
                    NpLayoutLyricsPosSlider.Value = 0;
                    break;
                case "VizSize":
                    _npVizSize = 0;
                    NpLayoutVizSlider.Value = (int)_npVizBarHeight;
                    break;
                case "CoverOffsetX":
                    _npCoverOffsetX = 0;
                    NpLayoutCoverXSlider.Value = 0;
                    break;
                case "CoverOffsetY":
                    _npCoverOffsetY = 0;
                    NpLayoutCoverYSlider.Value = 0;
                    break;
                case "TitleOffsetX":
                    _npTitleOffsetX = 0;
                    NpLayoutTitleXSlider.Value = 0;
                    break;
                case "TitleOffsetY":
                    _npTitleOffsetY = 0;
                    NpLayoutTitleYSlider.Value = 0;
                    break;
                case "ArtistOffsetX":
                    _npArtistOffsetX = 0;
                    NpLayoutArtistXSlider.Value = 0;
                    break;
                case "ArtistOffsetY":
                    _npArtistOffsetY = 0;
                    NpLayoutArtistYSlider.Value = 0;
                    break;
                case "VizOffsetY":
                    _npVizOffsetY = 0;
                    NpLayoutVizYSlider.Value = 0;
                    break;
            }
            _npLayoutPopupInit = false;

            NpApplyCoverGlowScale();
            NpApplyFullscreenScaling(fs);
            NpApplyFocusedLyricsEffects();
            NpRefreshBackdropFromSettings();
            NpSavePreferences();
        }

        private void NpLayoutReset_Click(object sender, RoutedEventArgs e)
        {
            NpEnsureLayoutProfileForCurrentWindowState();
            _npCoverSize = 0;
            _npTitleSize = 0;
            _npSubTextSize = 0;
            _npLyricsSize = 0;
            _npLyricsOffsetX = 0;
            _npVizSize = 0;
            _npCoverGlowSize = 1.0;
            _npFocusedLyricsBlurRadius = 6.5;
            _npFocusedLyricsInactiveBlur = null;
            _npCoverGlowMotionEnabled = true;
            _npGlowMotionMode = GlowMotionMode.Swirl;
            _npGlowRandomLastSwapMs = 0;
            ThemeManager.NpBackgroundHorizontalPosition = 0.5;
            ThemeManager.NpBackgroundVerticalPosition = 0.5;
            ThemeManager.NpBackgroundZoom = 1.0;
            ThemeManager.NpBackgroundBlur = 24.0;
            ThemeManager.NpBackgroundBrightness = 1.0;
            ThemeManager.NpCoverShapeMode = "Default";
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
            NpLayoutCoverGlowSlider.Value = 1.0;
            if (NpFocusedLyricsBlurSlider != null)
                NpFocusedLyricsBlurSlider.Value = 6.5;
            if (NpGlowMotionCheck != null)
                NpGlowMotionCheck.IsChecked = true;
            if (NpGlowMotionModeCombo != null)
                NpGlowMotionModeCombo.SelectedIndex = (int)_npGlowMotionMode;
            if (NpBackdropFocusXSlider != null)
                NpBackdropFocusXSlider.Value = 0.5;
            if (NpBackdropFocusYSlider != null)
                NpBackdropFocusYSlider.Value = 0.5;
            if (NpBackdropVerticalPresetCombo != null)
                NpBackdropVerticalPresetCombo.SelectedIndex = 1;
            if (NpBackdropZoomSlider != null)
                NpBackdropZoomSlider.Value = 1.0;
            if (NpBackdropBlurSlider != null)
                NpBackdropBlurSlider.Value = 24.0;
            if (NpBackdropBrightnessSlider != null)
                NpBackdropBrightnessSlider.Value = 1.0;
            if (NpCoverShapeCombo != null)
                NpCoverShapeCombo.SelectedIndex = 0;
            NpLayoutCoverXSlider.Value = 0;
            NpLayoutCoverYSlider.Value = 0;
            NpLayoutTitleXSlider.Value = 0;
            NpLayoutTitleYSlider.Value = 0;
            NpLayoutArtistXSlider.Value = 0;
            NpLayoutArtistYSlider.Value = 0;
            NpLayoutVizYSlider.Value = 0;
            _npLayoutPopupInit = false;

            NpApplyFullscreenScaling(fs);
            NpApplyFocusedLyricsEffects();
            NpApplyCoverShape();
            NpRefreshBackdropFromSettings();
            if (_npCoverGlowMotionEnabled && IsNowPlayingUiActive())
                NpStartGlowPulse();
            NpSavePreferences();
        }

        private void NpCoverShapeCombo_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (_npLayoutPopupInit || !IsLoaded || NpCoverShapeCombo == null) return;

            ThemeManager.NpCoverShapeMode = NpCoverShapeCombo.SelectedIndex switch
            {
                1 => "Rounded",
                2 => "Circle",
                _ => "Default"
            };
            NpApplyCoverShape();
            ThemeManager.SavePlayOptions();
        }

        private void NpApplyCoverShape()
        {
            if (NpCoverBorder == null || NpCoverClip == null) return;

            double radius = ThemeManager.NpCoverShapeMode switch
            {
                "Rounded" => 18,
                "Circle" => Math.Max(8, Math.Min(NpCoverBorder.ActualWidth, NpCoverBorder.ActualHeight) / 2.0),
                _ => 8
            };
            NpCoverBorder.CornerRadius = new CornerRadius(radius);
            NpCoverClip.RadiusX = radius;
            NpCoverClip.RadiusY = radius;
        }

        // ─── NP Settings Button ───

        private void NpSettings_Click(object sender, RoutedEventArgs e)
        {
            var settingsWindow = new SettingsWindow { Owner = this };
            // Opened from the NP screen — if ColorMatch is on, tint Settings to match it.
            if (NpTryGetSettingsColorMatchBrushes(out var cmBrushes))
                settingsWindow.ApplyColorMatchTint(cmBrushes);
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
                ApplyScrobbleSettings();
                UpdateScrobbleWidgetVisual();
                ApplyColumnVisibility();

                // NP-specific refreshes
                NpUpdateAutoPlayIcon();
                NpResetColorMatchCaches();
                if (_npColorMatchEnabled)
                    NpApplyColorMatchMode();
                else
                {
                    NpBottomBar.Background = (System.Windows.Media.Brush)FindResource("GlassToolbarBg");
                    NpBottomBar.BorderBrush = (System.Windows.Media.Brush)FindResource("GlassBorderBrush");
                    ApplyThemeTitleBar();
                }
                NpRenderPlaybarStyle();
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
            if (_npLyricsHidden)
            {
                NpCancelLyricsWork(invalidateVersion: true);
            }
            else if (_player?.CurrentFile != null)
            {
                var currentFile = _files.FirstOrDefault(f =>
                    string.Equals(f.FilePath, _player.CurrentFile, StringComparison.OrdinalIgnoreCase));
                if (currentFile != null)
                    ObserveUiTask(NpLoadLyricsAsync(currentFile.FilePath, currentFile.Artist, currentFile.Title, currentFile.Album, currentFile.DurationSeconds), nameof(NpLoadLyricsAsync));
            }
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
                        NpStopVisualizer();
                        NpStartVisualizer();
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

    }
}
