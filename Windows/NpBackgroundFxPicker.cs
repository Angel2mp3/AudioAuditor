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
    // NP background-animation picker popup handlers (mode combo, album-colors,
    // cycle, per-mode density/lightning/snowflake controls). Extracted verbatim
    // from NpCore.cs (2026-06-05 large-file split).
    public partial class MainWindow
    {
        // ─── NP Background Animation Picker ───

        private void NpBgFx_Click(object sender, RoutedEventArgs e)
        {
            if (NpBgFxPopup.IsOpen)
            {
                NpBgFxPopup.IsOpen = false;
                return;
            }

            NpSyncBgFxPopup();
            NpBgFxPopup.IsOpen = true;
        }

        private void NpSyncBgFxPopup()
        {
            _npBgFxPopupSyncing = true;
            try
            {
                if (NpBgFxModeCombo.Items.Count == 0)
                {
                    // "Color Drift" is intentionally NOT a mode here — it's controlled solely by the
                    // NpBgFxColorDriftCheck toggle below, which can layer the drift glow under any effect.
                    foreach (var bgMode in new[] { "Off", "Stars", "Rain", "Snow", "Leaves", "Underwater" })
                        NpBgFxModeCombo.Items.Add(bgMode);
                }
                NpBgFxModeCombo.SelectedItem = ThemeManager.NormalizeNpBackgroundAnimationMode(ThemeManager.NpBackgroundAnimationMode);
                if (NpBgFxColorDriftCheck != null)
                {
                    NpBgFxColorDriftCheck.IsChecked = ThemeManager.NpColorDriftBackgroundEnabled;
                    NpBgFxColorDriftCheck.IsEnabled = true;
                }
                NpBgFxAlbumColorsCheck.IsChecked = ThemeManager.NpBackgroundUseAlbumColors;
                NpBgFxCycleCheck.IsChecked = ThemeManager.NpBackgroundCycleEnabled;
                NpBgFxCycleSpeedSlider.Value = Math.Clamp(ThemeManager.NpBackgroundCycleSpeed, 0.25, 3.0);
                NpBgFxCycleSongCheck.IsChecked = ThemeManager.NpBackgroundCycleOnSongChange;
                string mode = ThemeManager.NormalizeNpBackgroundAnimationMode(ThemeManager.NpBackgroundAnimationMode);
                NpBgFxDensitySlider.Value = mode switch
                {
                    "Rain" => ThemeManager.ClampNpRainIntensity(ThemeManager.NpRainIntensity),
                    "Snow" or "Leaves" => ThemeManager.ClampNpSnowDensity(ThemeManager.NpSnowDensity),
                    _ => ThemeManager.ClampNpStarDensity(ThemeManager.NpStarDensity)
                };
                NpBgFxLightningCheck.IsChecked = ThemeManager.NpRainLightningEnabled;
                if (NpBgFxLightningPrompt != null && mode != "Rain")
                    NpBgFxLightningPrompt.Visibility = Visibility.Collapsed;
                NpBgFxSnowflakeSlider.Value = ThemeManager.ClampNpSnowflakeAmount(ThemeManager.NpSnowflakeAmount);
            }
            finally
            {
                _npBgFxPopupSyncing = false;
            }
        }

        private void NpBgFxModeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_npBgFxPopupSyncing) return;
            ThemeManager.NpBackgroundAnimationMode = ThemeManager.NormalizeNpBackgroundAnimationMode(NpBgFxModeCombo.SelectedItem?.ToString());
            if (ThemeManager.NpBackgroundAnimationMode == "Rain" && !ThemeManager.NpRainLightningPromptShown)
            {
                ThemeManager.NpRainLightningEnabled = false;
                NpBgFxLightningPrompt.Visibility = Visibility.Visible;
            }
            else
            {
                NpBgFxLightningPrompt.Visibility = Visibility.Collapsed;
            }
            ThemeManager.SavePlayOptions();
            NpSyncBgFxPopup();
            ApplyAnimationsEnabledState();
        }

        private void NpBgFxColorDrift_Changed(object sender, RoutedEventArgs e)
        {
            if (_npBgFxPopupSyncing) return;
            ThemeManager.NpColorDriftBackgroundEnabled = NpBgFxColorDriftCheck.IsChecked == true;
            ThemeManager.SavePlayOptions();
            ApplyAnimationsEnabledState();
        }

        private void NpBgFxAlbumColors_Changed(object sender, RoutedEventArgs e)
        {
            if (_npBgFxPopupSyncing) return;
            ThemeManager.NpBackgroundUseAlbumColors = NpBgFxAlbumColorsCheck.IsChecked == true;
            ThemeManager.SavePlayOptions();
            ApplyAnimationsEnabledState();
        }

        private void NpBgFxCycle_Changed(object sender, RoutedEventArgs e)
        {
            if (_npBgFxPopupSyncing) return;
            ThemeManager.NpBackgroundCycleEnabled = NpBgFxCycleCheck.IsChecked == true;
            ThemeManager.SavePlayOptions();
            ApplyAnimationsEnabledState();
        }

        private void NpBgFxCycleSpeed_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_npBgFxPopupSyncing) return;
            ThemeManager.NpBackgroundCycleSpeed = Math.Clamp(NpBgFxCycleSpeedSlider.Value, 0.25, 3.0);
            ThemeManager.SavePlayOptions();
            ApplyAnimationsEnabledState();
        }

        private void NpBgFxCycleSong_Changed(object sender, RoutedEventArgs e)
        {
            if (_npBgFxPopupSyncing) return;
            ThemeManager.NpBackgroundCycleOnSongChange = NpBgFxCycleSongCheck.IsChecked == true;
            ThemeManager.SavePlayOptions();
        }

        private void NpBgFxDensity_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_npBgFxPopupSyncing) return;
            string mode = ThemeManager.NormalizeNpBackgroundAnimationMode(ThemeManager.NpBackgroundAnimationMode);
            if (mode == "Rain") ThemeManager.NpRainIntensity = ThemeManager.ClampNpRainIntensity(NpBgFxDensitySlider.Value);
            else if (mode is "Snow" or "Leaves") ThemeManager.NpSnowDensity = ThemeManager.ClampNpSnowDensity(NpBgFxDensitySlider.Value);
            else ThemeManager.NpStarDensity = ThemeManager.ClampNpStarDensity(NpBgFxDensitySlider.Value);
            ThemeManager.SavePlayOptions();
            ApplyAnimationsEnabledState();
        }

        private void NpBgFxLightning_Changed(object sender, RoutedEventArgs e)
        {
            if (_npBgFxPopupSyncing) return;
            ThemeManager.NpRainLightningEnabled = NpBgFxLightningCheck.IsChecked == true;
            if (NpBgFxLightningPrompt != null)
                NpBgFxLightningPrompt.Visibility = Visibility.Collapsed;
            ThemeManager.SavePlayOptions();
            ApplyAnimationsEnabledState();
        }

        private void NpBgFxLightningPromptNo_Click(object sender, RoutedEventArgs e) =>
            NpResolveBgFxLightningPrompt(false);

        private void NpBgFxLightningPromptYes_Click(object sender, RoutedEventArgs e) =>
            NpResolveBgFxLightningPrompt(true);

        private void NpResolveBgFxLightningPrompt(bool enableLightning)
        {
            ThemeManager.NpRainLightningPromptShown = true;
            ThemeManager.NpRainLightningEnabled = enableLightning;
            _npBgFxPopupSyncing = true;
            try
            {
                NpBgFxLightningCheck.IsChecked = enableLightning;
            }
            finally
            {
                _npBgFxPopupSyncing = false;
            }
            if (NpBgFxLightningPrompt != null)
                NpBgFxLightningPrompt.Visibility = Visibility.Collapsed;
            ThemeManager.SavePlayOptions();
            ApplyAnimationsEnabledState();
        }

        private void NpBgFxSnowflake_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_npBgFxPopupSyncing) return;
            ThemeManager.NpSnowflakeAmount = ThemeManager.ClampNpSnowflakeAmount(NpBgFxSnowflakeSlider.Value);
            ThemeManager.SavePlayOptions();
            ApplyAnimationsEnabledState();
        }
    }
}
