using System;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using AudioQualityChecker.Services;
using AudioQualityChecker.Services.Scrobbling;

namespace AudioQualityChecker
{
    public partial class SettingsWindow
    {        // ═══════════════════════════════════════════
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

        private void AcoustIdKey_Changed(object sender, TextChangedEventArgs e)
        {
            if (_initializing) return;
            if (_acoustIdKeyVisible)
            {
                _realAcoustIdKey = AcoustIdKeyBox.Text.Trim();
                ThemeManager.AcoustIdApiKey = _realAcoustIdKey;
            }
            else
            {
                // Don't save dots
                return;
            }
            ThemeManager.SavePlayOptions();
        }

        private void LastFmCreateApiKey_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Process.Start(new ProcessStartInfo("https://www.last.fm/api/account/create") { UseShellExecute = true });
            }
            catch { }
        }

        private void AcoustIdCreateApiKey_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Process.Start(new ProcessStartInfo("https://acoustid.org/new-application") { UseShellExecute = true });
            }
            catch { }
        }

        private async void LastFmAuth_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string apiKey = _realApiKey.Trim();
                string apiSecret = _realApiSecret.Trim();

                if (string.IsNullOrEmpty(apiKey) || string.IsNullOrEmpty(apiSecret))
                {
                    LastFmStatusText.Text = "Enter API Key and API Secret first.";
                    return;
                }

                LastFmStatusText.Text = "Getting auth token...";

                var svc = new LastFmScrobbler();
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
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[LastFmAuth_Click] {ex}");
                LastFmStatusText.Text = "Auth request failed.";
            }
        }

        private async void LastFmConfirm_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (string.IsNullOrEmpty(_lastFmToken))
                {
                    LastFmStatusText.Text = "Click Authenticate first.";
                    return;
                }

                LastFmStatusText.Text = "Confirming...";

                var svc = new LastFmScrobbler();
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
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[LastFmConfirm_Click] {ex}");
                LastFmStatusText.Text = "Confirmation failed.";
            }
        }

        private void UpdateLastFmStatus()
        {
            if (!string.IsNullOrEmpty(ThemeManager.LastFmSessionKey))
                LastFmStatusText.Text = "✓ Authenticated";
            else
                LastFmStatusText.Text = "";
        }

        // ═══════════════════════════════════════════
        //  Libre.fm
        // ═══════════════════════════════════════════

        private void LibreFmKey_Changed(object sender, TextChangedEventArgs e)
        {
            if (_initializing || !_libreFmKeysVisible)
            {
                return;
            }
            _realLibreApiKey = LibreFmApiKeyBox.Text.Trim();
            _realLibreApiSecret = LibreFmApiSecretBox.Text.Trim();
            ThemeManager.LibreFmApiKey = _realLibreApiKey;
            ThemeManager.LibreFmApiSecret = _realLibreApiSecret;
            ThemeManager.SavePlayOptions();
        }

        private async void LibreFmAuth_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string apiKey = _realLibreApiKey.Trim();
                string apiSecret = _realLibreApiSecret.Trim();

                if (string.IsNullOrEmpty(apiKey) || string.IsNullOrEmpty(apiSecret))
                {
                    LibreFmStatusText.Text = "Enter API Key and API Secret first.";
                    return;
                }

                LibreFmStatusText.Text = "Getting auth token...";

                var svc = new LibreFmScrobbler();
                svc.Configure(apiKey, apiSecret, "");

                var result = await svc.GetAuthTokenAsync();
                svc.Dispose();

                if (result == null)
                {
                    LibreFmStatusText.Text = "Failed to get auth token.";
                    return;
                }

                _libreFmToken = result.Value.token;

                try
                {
                    Process.Start(new ProcessStartInfo(result.Value.authUrl) { UseShellExecute = true });
                }
                catch
                {
                    LibreFmStatusText.Text = "Could not open browser.";
                    return;
                }

                LibreFmStatusText.Text = "Authorize in browser, then click Confirm Auth.";
                BtnLibreFmConfirm.Visibility = Visibility.Visible;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[LibreFmAuth_Click] {ex}");
                LibreFmStatusText.Text = "Auth request failed.";
            }
        }

        private async void LibreFmConfirm_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (string.IsNullOrEmpty(_libreFmToken))
                {
                    LibreFmStatusText.Text = "Click Authenticate first.";
                    return;
                }

                LibreFmStatusText.Text = "Confirming...";

                var svc = new LibreFmScrobbler();
                svc.Configure(_realLibreApiKey.Trim(), _realLibreApiSecret.Trim(), "");

                string? sessionKey = await svc.GetSessionKeyAsync(_libreFmToken);
                svc.Dispose();

                if (string.IsNullOrEmpty(sessionKey))
                {
                    LibreFmStatusText.Text = "Auth failed. Did you authorize in the browser?";
                    return;
                }

                ThemeManager.LibreFmSessionKey = sessionKey;
                ThemeManager.LibreFmUsername = svc.Username;
                ThemeManager.LibreFmEnabled = true;
                ThemeManager.SavePlayOptions();

                BtnLibreFmConfirm.Visibility = Visibility.Collapsed;
                ChkLibreFmEnabled.IsChecked = true;
                _libreFmToken = null;
                UpdateLibreFmStatus();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[LibreFmConfirm_Click] {ex}");
                LibreFmStatusText.Text = "Confirmation failed.";
            }
        }

        private void LibreFmEnabled_Changed(object sender, RoutedEventArgs e)
        {
            ThemeManager.LibreFmEnabled = ChkLibreFmEnabled.IsChecked == true;
            ThemeManager.SavePlayOptions();
        }

        private void UpdateLibreFmStatus()
        {
            if (!string.IsNullOrEmpty(ThemeManager.LibreFmSessionKey))
                LibreFmStatusText.Text = string.IsNullOrEmpty(ThemeManager.LibreFmUsername)
                    ? "✓ Authenticated"
                    : $"✓ Authenticated as {ThemeManager.LibreFmUsername}";
            else
                LibreFmStatusText.Text = "";
        }

        // ═══════════════════════════════════════════
        //  ListenBrainz
        // ═══════════════════════════════════════════

        private void ListenBrainzField_Changed(object sender, TextChangedEventArgs e)
        {
            if (_initializing)
                return;

            _realListenBrainzUsername = ListenBrainzUsernameBox.Text.Trim();
            if (_listenBrainzTokenVisible)
                _realListenBrainzToken = ListenBrainzTokenBox.Text.Trim();
            ThemeManager.ListenBrainzUsername = _realListenBrainzUsername;
            ThemeManager.ListenBrainzUserToken = _realListenBrainzToken;
            ThemeManager.SavePlayOptions();
            UpdateListenBrainzStatus();
        }

        private void ListenBrainzEnabled_Changed(object sender, RoutedEventArgs e)
        {
            ThemeManager.ListenBrainzEnabled = ChkListenBrainzEnabled.IsChecked == true;
            ThemeManager.SavePlayOptions();
        }

        private void UpdateListenBrainzStatus()
        {
            if (!string.IsNullOrEmpty(ThemeManager.ListenBrainzUserToken))
                ListenBrainzStatusText.Text = string.IsNullOrEmpty(ThemeManager.ListenBrainzUsername)
                    ? "✓ Token saved"
                    : $"✓ Token saved for {ThemeManager.ListenBrainzUsername}";
            else
                ListenBrainzStatusText.Text = "";
        }

        // ═══════════════════════════════════════════
        //  Scrobble Thresholds
        // ═══════════════════════════════════════════

        private void ScrobbleThreshold_Changed(object sender, TextChangedEventArgs e)
        {
            if (_suppressScrobbleTextEvents) return;

            if (int.TryParse(ScrobbleAtPercentBox.Text, out var pct) && pct >= 0 && pct <= 100)
                ThemeManager.ScrobbleAtPercent = pct;
            if (int.TryParse(ScrobbleAtSecondsBox.Text, out var secs) && secs >= 0 && secs <= 7200)
                ThemeManager.ScrobbleAtSeconds = secs;
            if (int.TryParse(MinScrobbleTrackSecondsBox.Text, out var min) && min >= 0 && min <= 3600)
                ThemeManager.MinScrobbleTrackSeconds = min;
            ThemeManager.SavePlayOptions();
        }

        private void PauseScrobbling_Changed(object sender, RoutedEventArgs e)
        {
            ThemeManager.PauseScrobbling = ChkPauseScrobbling.IsChecked == true;
            ThemeManager.SavePlayOptions();
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

        private void ToggleLibreFmVisibility_Click(object sender, RoutedEventArgs e)
        {
            if (_libreFmKeysVisible)
            {
                _realLibreApiKey = LibreFmApiKeyBox.Text.Trim();
                _realLibreApiSecret = LibreFmApiSecretBox.Text.Trim();
                ThemeManager.LibreFmApiKey = _realLibreApiKey;
                ThemeManager.LibreFmApiSecret = _realLibreApiSecret;
                ThemeManager.SavePlayOptions();
            }

            _libreFmKeysVisible = !_libreFmKeysVisible;
            ApplyLibreFmVisibility();
        }

        private void ApplyLibreFmVisibility()
        {
            _initializing = true;
            if (_libreFmKeysVisible)
            {
                LibreFmApiKeyBox.Text = _realLibreApiKey;
                LibreFmApiSecretBox.Text = _realLibreApiSecret;
                LibreFmApiKeyBox.IsReadOnly = false;
                LibreFmApiSecretBox.IsReadOnly = false;
                LibreFmEyeSlash.Visibility = Visibility.Collapsed;
            }
            else
            {
                LibreFmApiKeyBox.Text = _realLibreApiKey.Length > 0 ? new string('●', Math.Max(_realLibreApiKey.Length, 32)) : "";
                LibreFmApiSecretBox.Text = _realLibreApiSecret.Length > 0 ? new string('●', Math.Max(_realLibreApiSecret.Length, 32)) : "";
                LibreFmApiKeyBox.IsReadOnly = true;
                LibreFmApiSecretBox.IsReadOnly = true;
                LibreFmEyeSlash.Visibility = Visibility.Visible;
            }
            _initializing = false;
        }

        private void ToggleListenBrainzVisibility_Click(object sender, RoutedEventArgs e)
        {
            if (_listenBrainzTokenVisible)
            {
                _realListenBrainzUsername = ListenBrainzUsernameBox.Text.Trim();
                _realListenBrainzToken = ListenBrainzTokenBox.Text.Trim();
                ThemeManager.ListenBrainzUsername = _realListenBrainzUsername;
                ThemeManager.ListenBrainzUserToken = _realListenBrainzToken;
                ThemeManager.SavePlayOptions();
                UpdateListenBrainzStatus();
            }

            _listenBrainzTokenVisible = !_listenBrainzTokenVisible;
            ApplyListenBrainzVisibility();
        }

        private void ApplyListenBrainzVisibility()
        {
            _initializing = true;
            if (_listenBrainzTokenVisible)
            {
                ListenBrainzUsernameBox.Text = _realListenBrainzUsername;
                ListenBrainzTokenBox.Text = _realListenBrainzToken;
                ListenBrainzUsernameBox.IsReadOnly = false;
                ListenBrainzTokenBox.IsReadOnly = false;
                ListenBrainzEyeSlash.Visibility = Visibility.Collapsed;
            }
            else
            {
                ListenBrainzUsernameBox.Text = _realListenBrainzUsername;
                ListenBrainzTokenBox.Text = _realListenBrainzToken.Length > 0 ? new string('●', Math.Max(_realListenBrainzToken.Length, 32)) : "";
                ListenBrainzUsernameBox.IsReadOnly = false;
                ListenBrainzTokenBox.IsReadOnly = true;
                ListenBrainzEyeSlash.Visibility = Visibility.Visible;
            }
            _initializing = false;
        }

        // ═══════════════════════════════════════════
        //  Maloja
        // ═══════════════════════════════════════════

        private void MalojaField_Changed(object sender, TextChangedEventArgs e)
        {
            if (_initializing)
                return;

            ThemeManager.MalojaServerUrl = MalojaServerBox.Text.Trim();
            ThemeManager.MalojaUsername = MalojaUsernameBox.Text.Trim();
            if (_malojaKeyVisible)
                _realMalojaKey = MalojaKeyBox.Text.Trim();
            ThemeManager.MalojaApiKey = _realMalojaKey;
            ThemeManager.SavePlayOptions();
            UpdateMalojaStatus();
        }

        private void MalojaEnabled_Changed(object sender, RoutedEventArgs e)
        {
            ThemeManager.MalojaEnabled = ChkMalojaEnabled.IsChecked == true;
            ThemeManager.SavePlayOptions();
        }

        private void UpdateMalojaStatus()
        {
            bool hasServer = !string.IsNullOrEmpty(ThemeManager.MalojaServerUrl);
            bool hasKey = !string.IsNullOrEmpty(ThemeManager.MalojaApiKey);
            if (hasServer && hasKey)
                MalojaStatusText.Text = string.IsNullOrEmpty(ThemeManager.MalojaUsername)
                    ? "✓ Connected"
                    : $"✓ Connected as {ThemeManager.MalojaUsername}";
            else if (hasServer || hasKey)
                MalojaStatusText.Text = "Enter both server URL and API key";
            else
                MalojaStatusText.Text = "";
        }

        private void ToggleMalojaVisibility_Click(object sender, RoutedEventArgs e)
        {
            if (_malojaKeyVisible)
            {
                _realMalojaKey = MalojaKeyBox.Text.Trim();
                ThemeManager.MalojaApiKey = _realMalojaKey;
                ThemeManager.SavePlayOptions();
                UpdateMalojaStatus();
            }
            _malojaKeyVisible = !_malojaKeyVisible;
            ApplyMalojaVisibility();
        }

        private void ApplyMalojaVisibility()
        {
            _initializing = true;
            if (_malojaKeyVisible)
            {
                MalojaKeyBox.Text = _realMalojaKey;
                MalojaKeyBox.IsReadOnly = false;
                MalojaEyeSlash.Visibility = Visibility.Collapsed;
            }
            else
            {
                MalojaKeyBox.Text = _realMalojaKey.Length > 0 ? new string('●', Math.Max(_realMalojaKey.Length, 32)) : "";
                MalojaKeyBox.IsReadOnly = true;
                MalojaEyeSlash.Visibility = Visibility.Visible;
            }
            _initializing = false;
        }

        // ═══════════════════════════════════════════
        //  AcoustID API Key Visibility Toggle
        // ═══════════════════════════════════════════

        private void ToggleAcoustIdVisibility_Click(object sender, RoutedEventArgs e)
        {
            if (_acoustIdKeyVisible)
            {
                _realAcoustIdKey = AcoustIdKeyBox.Text.Trim();
                ThemeManager.AcoustIdApiKey = _realAcoustIdKey;
                ThemeManager.SavePlayOptions();
            }
            _acoustIdKeyVisible = !_acoustIdKeyVisible;
            ApplyAcoustIdKeyVisibility();
        }

        private void ApplyAcoustIdKeyVisibility()
        {
            if (_acoustIdKeyVisible)
            {
                _initializing = true;
                AcoustIdKeyBox.Text = _realAcoustIdKey;
                AcoustIdKeyBox.IsReadOnly = false;
                AcoustIdEyeSlash.Visibility = Visibility.Collapsed;
                _initializing = false;
            }
            else
            {
                _initializing = true;
                string dots = _realAcoustIdKey.Length > 0 ? new string('●', Math.Max(_realAcoustIdKey.Length, 32)) : "";
                AcoustIdKeyBox.Text = dots;
                AcoustIdKeyBox.IsReadOnly = true;
                AcoustIdEyeSlash.Visibility = Visibility.Visible;
                _initializing = false;
            }
        }

    }
}