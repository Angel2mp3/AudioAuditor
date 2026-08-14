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

            UpdateBatterySaverOverrideEnabled();
        }

        // The visualizer override only means anything while Battery Saver is on.
        private void UpdateBatterySaverOverrideEnabled()
            => ChkBatteryKeepVisualizer.IsEnabled = ChkBatterySaver.IsChecked == true;

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
            UpdateBatterySaverOverrideEnabled();
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
