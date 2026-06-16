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
    // Now Playing settings handlers: color cache, album backdrop, and the
    // background-animation controls (mode combo, per-mode sliders/toggles incl.
    // Underwater). Extracted verbatim from SettingsWindow.xaml.cs (2026-06-05 split).
    public partial class SettingsWindow
    {
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

        private void NpRememberManualColors_Changed(object sender, RoutedEventArgs e)
        {
            if (_initializing) return;
            ThemeManager.NpRememberManualColorPicks = ChkNpRememberManualColors.IsChecked == true;
            ThemeManager.SavePlayOptions();
        }

        private void NpAlbumBackdrop_Changed(object sender, RoutedEventArgs e)
        {
            if (_initializing) return;
            ThemeManager.NpAlbumBackdropEnabled = ChkNpAlbumBackdrop.IsChecked == true;
            ThemeManager.SavePlayOptions();
            if (Owner is MainWindow mw)
                mw.NpRefreshBackdropFromSettings();
        }

        private void NpBackgroundAnimationCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_initializing) return;
            ThemeManager.NpBackgroundAnimationMode =
                ThemeManager.NormalizeNpBackgroundAnimationMode(NpBackgroundAnimationCombo.SelectedItem?.ToString());
            if (ThemeManager.NpBackgroundAnimationMode == "Rain" && !ThemeManager.NpRainLightningPromptShown)
                NpShowRainLightningPrompt();
            ThemeManager.SavePlayOptions();
            NpUpdateBgEffectRowsVisibility();
            if (Owner is MainWindow mw)
                mw.ApplyAnimationsEnabledState();
        }

        private void NpColorDriftGlow_Changed(object sender, RoutedEventArgs e)
        {
            if (_initializing) return;
            ThemeManager.NpColorDriftBackgroundEnabled = ChkNpColorDriftGlow.IsChecked == true;
            ThemeManager.SavePlayOptions();
            if (Owner is MainWindow mw)
                mw.ApplyAnimationsEnabledState();
        }

        private void NpUpdateBgEffectRowsVisibility()
        {
            if (NpStarsControls == null || NpRainControls == null || NpSnowControls == null
                || NpUnderwaterControls == null)
                return;
            string mode = ThemeManager.NormalizeNpBackgroundAnimationMode(
                NpBackgroundAnimationCombo?.SelectedItem?.ToString());
            NpStarsControls.Visibility = mode == "Stars" ? Visibility.Visible : Visibility.Collapsed;
            NpRainControls.Visibility = mode == "Rain" ? Visibility.Visible : Visibility.Collapsed;
            NpSnowControls.Visibility = mode == "Snow" ? Visibility.Visible : Visibility.Collapsed;
            NpUnderwaterControls.Visibility = mode == "Underwater" ? Visibility.Visible : Visibility.Collapsed;
            if (NpRainLightningPrompt != null && mode != "Rain")
                NpRainLightningPrompt.Visibility = Visibility.Collapsed;
        }

        private void NpShowRainLightningPrompt()
        {
            if (NpRainLightningPrompt != null)
                NpRainLightningPrompt.Visibility = Visibility.Visible;
        }

        private void NpResolveRainLightningPrompt(bool enableLightning)
        {
            ThemeManager.NpRainLightningPromptShown = true;
            ThemeManager.NpRainLightningEnabled = enableLightning;
            ChkNpRainLightning.IsChecked = enableLightning;
            if (NpRainLightningPrompt != null)
                NpRainLightningPrompt.Visibility = Visibility.Collapsed;
            ThemeManager.SavePlayOptions();
            if (Owner is MainWindow mw)
                mw.ApplyAnimationsEnabledState();
        }

        private void NpRainLightningPromptNo_Click(object sender, RoutedEventArgs e) =>
            NpResolveRainLightningPrompt(false);

        private void NpRainLightningPromptYes_Click(object sender, RoutedEventArgs e) =>
            NpResolveRainLightningPrompt(true);


        private void NpBackgroundUseAlbumColors_Changed(object sender, RoutedEventArgs e)
        {
            if (_initializing) return;
            ThemeManager.NpBackgroundUseAlbumColors = ChkNpBackgroundUseAlbumColors.IsChecked == true;
            ThemeManager.SavePlayOptions();
            if (Owner is MainWindow mw)
                mw.ApplyAnimationsEnabledState();
        }

        private void NpBackgroundCycle_Changed(object sender, RoutedEventArgs e)
        {
            if (_initializing) return;
            ThemeManager.NpBackgroundCycleEnabled = ChkNpBackgroundCycle.IsChecked == true;
            ThemeManager.SavePlayOptions();
            if (Owner is MainWindow mw)
                mw.ApplyAnimationsEnabledState();
        }

        private void NpBackgroundCycleSpeedSlider_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_initializing) return;
            ThemeManager.NpBackgroundCycleSpeed = Math.Clamp(NpBackgroundCycleSpeedSlider.Value, 0.25, 3.0);
            NpBackgroundCycleSpeedLabel.Text = $"{ThemeManager.NpBackgroundCycleSpeed:0.0}x";
            ThemeManager.SavePlayOptions();
            if (Owner is MainWindow mw)
                mw.ApplyAnimationsEnabledState();
        }

        private void NpBackgroundCycleOnSongChange_Changed(object sender, RoutedEventArgs e)
        {
            if (_initializing) return;
            ThemeManager.NpBackgroundCycleOnSongChange = ChkNpBackgroundCycleOnSongChange.IsChecked == true;
            ThemeManager.SavePlayOptions();
        }

        private void NpStarDensitySlider_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_initializing) return;
            ThemeManager.NpStarDensity = ThemeManager.ClampNpStarDensity(NpStarDensitySlider.Value);
            NpStarDensityLabel.Text = $"{ThemeManager.NpStarDensity:0.0}x";
            ThemeManager.SavePlayOptions();
            if (Owner is MainWindow mw)
                mw.ApplyAnimationsEnabledState();
        }

        private void NpShootingStars_Changed(object sender, RoutedEventArgs e)
        {
            if (_initializing) return;
            ThemeManager.NpShootingStarsEnabled = ChkNpShootingStars.IsChecked == true;
            ThemeManager.SavePlayOptions();
            if (Owner is MainWindow mw)
                mw.ApplyAnimationsEnabledState();
        }

        private void NpShootingStarDensitySlider_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_initializing) return;
            ThemeManager.NpShootingStarDensity = ThemeManager.ClampNpShootingStarDensity(NpShootingStarDensitySlider.Value);
            NpShootingStarDensityLabel.Text = $"{ThemeManager.NpShootingStarDensity:0.0}x";
            ThemeManager.SavePlayOptions();
            if (Owner is MainWindow mw)
                mw.ApplyAnimationsEnabledState();
        }

        private void NpAnimationSpeedSlider_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_initializing) return;
            ThemeManager.NpBackgroundAnimationSpeed =
                ThemeManager.ClampNpBackgroundAnimationSpeed(NpAnimationSpeedSlider.Value);
            NpAnimationSpeedLabel.Text = $"{ThemeManager.NpBackgroundAnimationSpeed:0.0}x";
            ThemeManager.SavePlayOptions();
            if (Owner is MainWindow mw)
                mw.ApplyAnimationsEnabledState();
        }

        private void NpRainIntensitySlider_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_initializing) return;
            ThemeManager.NpRainIntensity = ThemeManager.ClampNpRainIntensity(NpRainIntensitySlider.Value);
            NpRainIntensityLabel.Text = $"{ThemeManager.NpRainIntensity:0.0}x";
            ThemeManager.SavePlayOptions();
            if (Owner is MainWindow mw)
                mw.ApplyAnimationsEnabledState();
        }

        private void NpRainLightning_Changed(object sender, RoutedEventArgs e)
        {
            if (_initializing) return;
            ThemeManager.NpRainLightningEnabled = ChkNpRainLightning.IsChecked == true;
            if (NpRainLightningPrompt != null)
                NpRainLightningPrompt.Visibility = Visibility.Collapsed;
            ThemeManager.SavePlayOptions();
            if (Owner is MainWindow mw)
                mw.ApplyAnimationsEnabledState();
        }

        private void NpRainLightningSlider_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_initializing) return;
            ThemeManager.NpRainLightningAmount = ThemeManager.ClampNpRainLightningAmount(NpRainLightningSlider.Value);
            NpRainLightningLabel.Text = $"{ThemeManager.NpRainLightningAmount:0.0}x";
            ThemeManager.SavePlayOptions();
            if (Owner is MainWindow mw)
                mw.ApplyAnimationsEnabledState();
        }

        private void NpSnowDensitySlider_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_initializing) return;
            ThemeManager.NpSnowDensity = ThemeManager.ClampNpSnowDensity(NpSnowDensitySlider.Value);
            NpSnowDensityLabel.Text = $"{ThemeManager.NpSnowDensity:0.0}x";
            ThemeManager.SavePlayOptions();
            if (Owner is MainWindow mw)
                mw.ApplyAnimationsEnabledState();
        }

        private void NpSnowflakeSlider_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_initializing) return;
            ThemeManager.NpSnowflakeAmount = ThemeManager.ClampNpSnowflakeAmount(NpSnowflakeSlider.Value);
            NpSnowflakeLabel.Text = $"{ThemeManager.NpSnowflakeAmount:0.0}x";
            ThemeManager.SavePlayOptions();
            if (Owner is MainWindow mw)
                mw.ApplyAnimationsEnabledState();
        }

        private void NpUnderwaterBubbleSlider_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_initializing) return;
            ThemeManager.NpUnderwaterBubbleDensity = ThemeManager.ClampNpUnderwaterBubbleDensity(NpUnderwaterBubbleSlider.Value);
            NpUnderwaterBubbleLabel.Text = $"{ThemeManager.NpUnderwaterBubbleDensity:0.0}x";
            ThemeManager.SavePlayOptions();
            if (Owner is MainWindow mw)
                mw.ApplyAnimationsEnabledState();
        }

        private void NpUnderwaterCausticSlider_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_initializing) return;
            ThemeManager.NpUnderwaterCausticIntensity = ThemeManager.ClampNpUnderwaterCausticIntensity(NpUnderwaterCausticSlider.Value);
            NpUnderwaterCausticLabel.Text = $"{ThemeManager.NpUnderwaterCausticIntensity:0.0}x";
            ThemeManager.SavePlayOptions();
            if (Owner is MainWindow mw)
                mw.ApplyAnimationsEnabledState();
        }

        private void NpUnderwaterFish_Changed(object sender, RoutedEventArgs e)
        {
            if (_initializing) return;
            ThemeManager.NpUnderwaterFishEnabled = ChkNpUnderwaterFish.IsChecked == true;
            ThemeManager.SavePlayOptions();
            if (Owner is MainWindow mw)
                mw.ApplyAnimationsEnabledState();
        }

        private void NpUnderwaterSeaweed_Changed(object sender, RoutedEventArgs e)
        {
            if (_initializing) return;
            ThemeManager.NpUnderwaterSeaweedEnabled = ChkNpUnderwaterSeaweed.IsChecked == true;
            ThemeManager.SavePlayOptions();
            if (Owner is MainWindow mw)
                mw.ApplyAnimationsEnabledState();
        }
    }
}
