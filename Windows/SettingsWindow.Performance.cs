using System.Windows;
using AudioQualityChecker.Services;

namespace AudioQualityChecker
{
    // Battery Saver + GPU acceleration settings (Performance section, Cache & Files tab).
    // Reduce Motion lives in the Appearance tab and is handled in SettingsWindow.xaml.cs.
    public partial class SettingsWindow
    {
        // Acceleration combo item order — must match the GpuRenderMode enum mapping below.
        // Only two honest options: WPF can force software rendering off the GPU, but has no
        // API to *force* the GPU on, so a "force hardware" choice would be a no-op (== Auto).
        private static readonly (string Label, GpuRenderMode Mode)[] GpuRenderModeItems =
        {
            ("Auto (recommended)", GpuRenderMode.Auto),
            ("Force software (CPU only)", GpuRenderMode.ForceSoftware),
        };

        /// <summary>Populate Battery Saver + GPU controls from ThemeManager. Runs while _initializing is true.</summary>
        private void InitPerformanceControls()
        {
            ChkBatterySaver.IsChecked = ThemeManager.BatterySaverEnabled;
            ChkBatteryEntireProgram.IsChecked = ThemeManager.BatterySaverEntireProgram;
            ChkBatteryNpBackground.IsChecked = ThemeManager.BatterySaverNpBackground;
            ChkBatteryVisualizer.IsChecked = ThemeManager.BatterySaverVisualizer;
            ChkBatteryCoverGlow.IsChecked = ThemeManager.BatterySaverCoverGlow;
            ChkBatteryLyrics.IsChecked = ThemeManager.BatterySaverLyrics;
            ChkBatteryPlaybar.IsChecked = ThemeManager.BatterySaverPlaybar;
            ChkBatteryKeepVisualizer.IsChecked = ThemeManager.BatterySaverKeepVisualizer;

            GpuRenderModeCombo.Items.Clear();
            int selectedGpuIdx = 0;
            for (int i = 0; i < GpuRenderModeItems.Length; i++)
            {
                GpuRenderModeCombo.Items.Add(GpuRenderModeItems[i].Label);
                if (GpuRenderModeItems[i].Mode == ThemeManager.GpuRenderMode)
                    selectedGpuIdx = i;
            }
            GpuRenderModeCombo.SelectedIndex = selectedGpuIdx;

            int tier = ThemeManager.GetRenderTier();
            string tierDesc = tier switch
            {
                >= 2 => "full GPU acceleration",
                1 => "partial GPU acceleration",
                _ => "software rendering (no GPU)"
            };
            GpuRenderTierText.Text = $"Detected render tier: {tier} — {tierDesc}.";

            UpdateBatterySaverAreasEnabled();
        }

        // Per-area checkboxes only matter when the master is on and not targeting the
        // whole program; grey them out otherwise so the UI reflects what's active.
        private void UpdateBatterySaverAreasEnabled()
        {
            bool master = ChkBatterySaver.IsChecked == true;
            ChkBatteryEntireProgram.IsEnabled = master;

            bool perArea = master && ChkBatteryEntireProgram.IsChecked != true;
            ChkBatteryNpBackground.IsEnabled = perArea;
            ChkBatteryVisualizer.IsEnabled = perArea;
            ChkBatteryCoverGlow.IsEnabled = perArea;
            ChkBatteryLyrics.IsEnabled = perArea;
            ChkBatteryPlaybar.IsEnabled = perArea;

            // The visualizer override stays available whenever Battery Saver is on — it exists
            // specifically to win over "Entire program" mode.
            ChkBatteryKeepVisualizer.IsEnabled = master;
        }

        private void BatterySaverKeepVisualizer_Changed(object sender, RoutedEventArgs e)
        {
            if (_initializing) return;
            ThemeManager.BatterySaverKeepVisualizer = ChkBatteryKeepVisualizer.IsChecked == true;
            ThemeManager.SavePlayOptions();
            ApplyPerformancePolicyToMain();
        }

        private void BatterySaver_Changed(object sender, RoutedEventArgs e)
        {
            if (_initializing) return;
            ThemeManager.BatterySaverEnabled = ChkBatterySaver.IsChecked == true;
            ThemeManager.SavePlayOptions();
            UpdateBatterySaverAreasEnabled();
            ApplyPerformancePolicyToMain();
        }

        private void BatterySaverArea_Changed(object sender, RoutedEventArgs e)
        {
            if (_initializing) return;
            ThemeManager.BatterySaverEntireProgram = ChkBatteryEntireProgram.IsChecked == true;
            ThemeManager.BatterySaverNpBackground = ChkBatteryNpBackground.IsChecked == true;
            ThemeManager.BatterySaverVisualizer = ChkBatteryVisualizer.IsChecked == true;
            ThemeManager.BatterySaverCoverGlow = ChkBatteryCoverGlow.IsChecked == true;
            ThemeManager.BatterySaverLyrics = ChkBatteryLyrics.IsChecked == true;
            ThemeManager.BatterySaverPlaybar = ChkBatteryPlaybar.IsChecked == true;
            ThemeManager.SavePlayOptions();
            UpdateBatterySaverAreasEnabled();
            ApplyPerformancePolicyToMain();
        }

        private void GpuRenderMode_Changed(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (_initializing) return;
            int idx = GpuRenderModeCombo.SelectedIndex;
            if (idx < 0 || idx >= GpuRenderModeItems.Length) return;
            ThemeManager.GpuRenderMode = GpuRenderModeItems[idx].Mode;
            ThemeManager.SavePlayOptions();
            // ProcessRenderMode can only be set once at startup, so this takes effect on restart.
        }

        private void ApplyPerformancePolicyToMain()
        {
            if (Owner is MainWindow mw)
                mw.ApplyPerformancePolicy();
        }
    }
}
