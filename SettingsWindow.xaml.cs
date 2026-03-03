using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using AudioQualityChecker.Services;
using Microsoft.Win32;

namespace AudioQualityChecker
{
    public partial class SettingsWindow : Window
    {
        private bool _initializing = true;
        private string? _lastFmToken; // stored between auth steps
        private bool _apiKeysVisible = false; // hidden by default
        private string _realApiKey = "";
        private string _realApiSecret = "";

        public SettingsWindow()
        {
            InitializeComponent();

            // Populate theme combo
            foreach (var theme in ThemeManager.AvailableThemes)
                ThemeCombo.Items.Add(theme);
            ThemeCombo.SelectedItem = ThemeManager.CurrentTheme;

            // Populate playbar theme combo
            foreach (var pt in ThemeManager.AvailablePlaybarThemes)
                PlaybarCombo.Items.Add(pt);
            PlaybarCombo.SelectedItem = ThemeManager.CurrentPlaybarTheme;

            // Set play option checkboxes from saved state
            ChkAutoPlay.IsChecked = ThemeManager.AutoPlayNext;
            ChkNormalization.IsChecked = ThemeManager.AudioNormalization;
            ChkCrossfade.IsChecked = ThemeManager.Crossfade;
            CrossfadeSlider.Value = ThemeManager.CrossfadeDuration;
            CrossfadeDurationLabel.Text = $"{ThemeManager.CrossfadeDuration}s";
            ChkSpatialAudio.IsChecked = ThemeManager.SpatialAudioEnabled;
            ChkRainbowVisualizer.IsChecked = ThemeManager.RainbowVisualizerEnabled;

            // Populate all 6 music service combos
            var serviceCombos = new[] { ServiceCombo0, ServiceCombo1, ServiceCombo2, ServiceCombo3, ServiceCombo4, ServiceCombo5 };
            for (int i = 0; i < 6; i++)
            {
                foreach (var svc in ThemeManager.AvailableMusicServices)
                    serviceCombos[i].Items.Add(svc);
                serviceCombos[i].SelectedItem = ThemeManager.MusicServiceSlots[i];
            }

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

            // Discord RPC
            ChkDiscordRpc.IsChecked = ThemeManager.DiscordRpcEnabled;

            // Last.fm
            _realApiKey = ThemeManager.LastFmApiKey;
            _realApiSecret = ThemeManager.LastFmApiSecret;
            LastFmApiKeyBox.Text = ThemeManager.LastFmApiKey;
            LastFmApiSecretBox.Text = ThemeManager.LastFmApiSecret;
            UpdateLastFmStatus();

            // Hide API keys by default (use dot masking)
            ApplyApiKeyVisibility();

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
            int selectedConcurrencyIdx = 0;
            for (int ci = 0; ci < ThemeManager.ConcurrencyPresets.Length; ci++)
            {
                var (label, value) = ThemeManager.ConcurrencyPresets[ci];
                string display = value == 0 ? $"{label} — {ThemeManager.DefaultConcurrency} threads" : label;
                ConcurrencyCombo.Items.Add(display);
                if (value == ThemeManager.MaxConcurrency ||
                    (value == 0 && ThemeManager.MaxConcurrency == ThemeManager.DefaultConcurrency))
                    selectedConcurrencyIdx = ci;
            }
            ConcurrencyCombo.SelectedIndex = selectedConcurrencyIdx;
            ConcurrencyInfoText.Text = $"Your system has {Environment.ProcessorCount} logical processors. " +
                $"Lower values reduce CPU spikes when analyzing large folders or exporting spectrograms.";

            // Memory limit
            int selectedMemoryIdx = 0;
            for (int mi = 0; mi < ThemeManager.MemoryPresets.Length; mi++)
            {
                var (label, valueMB) = ThemeManager.MemoryPresets[mi];
                string display = valueMB == 0 ? $"{label} — {ThemeManager.DefaultMemoryMB} MB" : label;
                MemoryLimitCombo.Items.Add(display);
                if (valueMB == ThemeManager.MaxMemoryMB ||
                    (valueMB == 0 && ThemeManager.MaxMemoryMB == ThemeManager.DefaultMemoryMB))
                    selectedMemoryIdx = mi;
            }
            MemoryLimitCombo.SelectedIndex = selectedMemoryIdx;
            MemoryInfoText.Text = $"Your system has {ThemeManager.TotalSystemMemoryMB:N0} MB total RAM. " +
                $"Limits how much memory AudioAuditor uses during analysis and spectrogram export.";

            _initializing = false;
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

        private void Header_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
                DragMove();
        }

        private void ThemeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_initializing) return;
            if (ThemeCombo.SelectedItem is string theme)
            {
                ThemeManager.ApplyTheme(theme);
            }
        }

        private void PlaybarCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_initializing) return;
            if (PlaybarCombo.SelectedItem is string playbarTheme)
            {
                ThemeManager.SetPlaybarTheme(playbarTheme);
            }
        }

        private void PlayOption_Changed(object sender, RoutedEventArgs e)
        {
            if (_initializing) return;

            ThemeManager.AutoPlayNext = ChkAutoPlay.IsChecked == true;
            ThemeManager.AudioNormalization = ChkNormalization.IsChecked == true;
            ThemeManager.Crossfade = ChkCrossfade.IsChecked == true;
            ThemeManager.SpatialAudioEnabled = ChkSpatialAudio.IsChecked == true;
            ThemeManager.RainbowVisualizerEnabled = ChkRainbowVisualizer.IsChecked == true;
            ThemeManager.SavePlayOptions();
        }

        private void CrossfadeSlider_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_initializing) return;
            int val = (int)CrossfadeSlider.Value;
            CrossfadeDurationLabel.Text = $"{val}s";
            ThemeManager.CrossfadeDuration = val;
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

        private void CustomUrl_Changed(object sender, TextChangedEventArgs e)
        {
            if (_initializing) return;

            if (sender is TextBox tb && tb.Tag is string tagStr && int.TryParse(tagStr, out int idx) && idx >= 0 && idx < 6)
            {
                ThemeManager.CustomServiceUrls[idx] = tb.Text;
                ThemeManager.SavePlayOptions();
            }
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

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            // Auto-hide API keys when closing
            _apiKeysVisible = false;
            ApplyApiKeyVisibility();
            Close();
        }

        // ═══════════════════════════════════════════
        //  Discord Rich Presence
        // ═══════════════════════════════════════════

        private void DiscordRpc_Changed(object sender, RoutedEventArgs e)
        {
            if (_initializing) return;
            ThemeManager.DiscordRpcEnabled = ChkDiscordRpc.IsChecked == true;
            ThemeManager.SavePlayOptions();
        }

        // ═══════════════════════════════════════════
        //  Last.fm
        // ═══════════════════════════════════════════

        private void LastFmKey_Changed(object sender, TextChangedEventArgs e)
        {
            if (_initializing) return;
            // Only save if keys are visible (not showing dots)
            if (_apiKeysVisible)
            {
                _realApiKey = LastFmApiKeyBox.Text.Trim();
                _realApiSecret = LastFmApiSecretBox.Text.Trim();
                ThemeManager.LastFmApiKey = _realApiKey;
                ThemeManager.LastFmApiSecret = _realApiSecret;
                ThemeManager.SavePlayOptions();
            }
        }

        private void LastFmCreateApiKey_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Process.Start(new ProcessStartInfo("https://www.last.fm/api/account/create") { UseShellExecute = true });
            }
            catch { }
        }

        private async void LastFmAuth_Click(object sender, RoutedEventArgs e)
        {
            string apiKey = _realApiKey.Trim();
            string apiSecret = _realApiSecret.Trim();

            if (string.IsNullOrEmpty(apiKey) || string.IsNullOrEmpty(apiSecret))
            {
                LastFmStatusText.Text = "Enter API Key and API Secret first.";
                return;
            }

            LastFmStatusText.Text = "Getting auth token...";

            var svc = new LastFmService();
            svc.Configure(apiKey, apiSecret, "");

            var result = await svc.GetAuthTokenAsync();
            svc.Dispose();

            if (result == null)
            {
                LastFmStatusText.Text = "Failed to get auth token.";
                return;
            }

            _lastFmToken = result.Value.token;

            try
            {
                Process.Start(new ProcessStartInfo(result.Value.authUrl) { UseShellExecute = true });
            }
            catch
            {
                LastFmStatusText.Text = "Could not open browser.";
                return;
            }

            LastFmStatusText.Text = "Authorize in browser, then click Confirm Auth.";
            BtnLastFmConfirm.Visibility = Visibility.Visible;
        }

        private async void LastFmConfirm_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_lastFmToken))
            {
                LastFmStatusText.Text = "Click Authenticate first.";
                return;
            }

            LastFmStatusText.Text = "Confirming...";

            var svc = new LastFmService();
            svc.Configure(_realApiKey.Trim(), _realApiSecret.Trim(), "");

            string? sessionKey = await svc.GetSessionKeyAsync(_lastFmToken);
            svc.Dispose();

            if (string.IsNullOrEmpty(sessionKey))
            {
                LastFmStatusText.Text = "Auth failed. Did you authorize in the browser?";
                return;
            }

            ThemeManager.LastFmSessionKey = sessionKey;
            ThemeManager.LastFmUsername = svc.Username;
            ThemeManager.LastFmEnabled = true;
            ThemeManager.SavePlayOptions();

            BtnLastFmConfirm.Visibility = Visibility.Collapsed;
            _lastFmToken = null;
            UpdateLastFmStatus();
        }

        private void UpdateLastFmStatus()
        {
            if (!string.IsNullOrEmpty(ThemeManager.LastFmSessionKey))
                LastFmStatusText.Text = "✓ Authenticated";
            else
                LastFmStatusText.Text = "";
        }

        // ═══════════════════════════════════════════
        //  API Key Visibility Toggle
        // ═══════════════════════════════════════════

        private void ToggleApiVisibility_Click(object sender, RoutedEventArgs e)
        {
            // If currently visible, save the real values before hiding
            if (_apiKeysVisible)
            {
                _realApiKey = LastFmApiKeyBox.Text.Trim();
                _realApiSecret = LastFmApiSecretBox.Text.Trim();
                ThemeManager.LastFmApiKey = _realApiKey;
                ThemeManager.LastFmApiSecret = _realApiSecret;
                ThemeManager.SavePlayOptions();
            }
            _apiKeysVisible = !_apiKeysVisible;
            ApplyApiKeyVisibility();
        }

        private void ApplyApiKeyVisibility()
        {
            if (_apiKeysVisible)
            {
                // Show keys: restore real text
                _initializing = true; // prevent saving dots as keys
                LastFmApiKeyBox.FontFamily = new System.Windows.Media.FontFamily("Segoe UI");
                LastFmApiSecretBox.FontFamily = new System.Windows.Media.FontFamily("Segoe UI");
                LastFmApiKeyBox.Text = _realApiKey;
                LastFmApiSecretBox.Text = _realApiSecret;
                LastFmApiKeyBox.IsReadOnly = false;
                LastFmApiSecretBox.IsReadOnly = false;
                EyeSlash.Visibility = Visibility.Collapsed;
                _initializing = false;
            }
            else
            {
                // Hide keys: replace text with dots
                _initializing = true;
                // Store current real values first
                if (LastFmApiKeyBox.FontFamily.Source != "Segoe UI" || string.IsNullOrEmpty(_realApiKey))
                {
                    // Already masked or empty, use stored values
                }
                else
                {
                    _realApiKey = LastFmApiKeyBox.Text;
                    _realApiSecret = LastFmApiSecretBox.Text;
                }
                LastFmApiKeyBox.FontFamily = new System.Windows.Media.FontFamily("Segoe UI");
                LastFmApiSecretBox.FontFamily = new System.Windows.Media.FontFamily("Segoe UI");
                // Fill with bullet dots matching full width
                string keyDots = _realApiKey.Length > 0 ? new string('●', Math.Max(_realApiKey.Length, 40)) : "";
                string secretDots = _realApiSecret.Length > 0 ? new string('●', Math.Max(_realApiSecret.Length, 40)) : "";
                LastFmApiKeyBox.Text = keyDots;
                LastFmApiSecretBox.Text = secretDots;
                LastFmApiKeyBox.IsReadOnly = true;
                LastFmApiSecretBox.IsReadOnly = true;
                EyeSlash.Visibility = Visibility.Visible;
                _initializing = false;
            }
        }

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
                ThemeManager.MaxConcurrency = ThemeManager.ConcurrencyPresets[idx].Value;
                ThemeManager.SavePlayOptions();
            }
        }

        private void MemoryLimitCombo_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (_initializing) return;
            int idx = MemoryLimitCombo.SelectedIndex;
            if (idx >= 0 && idx < ThemeManager.MemoryPresets.Length)
            {
                ThemeManager.MaxMemoryMB = ThemeManager.MemoryPresets[idx].ValueMB;
                ThemeManager.SavePlayOptions();
            }
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
    }
}
