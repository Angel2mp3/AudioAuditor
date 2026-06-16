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
    // Scrobble widget (Last.fm + Libre.fm + ListenBrainz): init, settings apply,
    // blacklist parse/save, widget visual, and context menu handlers. Extracted
    // verbatim from NpCore.cs (2026-06-05 large-file split).
    public partial class MainWindow
    {
        // ═══════════════════════════════════════════
        //  Scrobble Widget (Last.fm + Libre.fm + ListenBrainz)
        // ═══════════════════════════════════════════

        private void InitializeScrobblers()
        {
            _lastFm = new LastFmScrobbler();
            _libreFm = new LibreFmScrobbler();
            _listenBrainz = new ListenBrainzScrobbler();
            _maloja = new MalojaScrobbler();
            _scrobbler.Register(_lastFm);
            _scrobbler.Register(_libreFm);
            _scrobbler.Register(_listenBrainz);
            _scrobbler.Register(_maloja);
            ApplyScrobbleSettings();
        }

        private void ApplyScrobbleSettings()
        {
            if (_lastFm == null || _libreFm == null || _listenBrainz == null || _maloja == null) return;

            _lastFm.Configure(
                ThemeManager.LastFmApiKey,
                ThemeManager.LastFmApiSecret,
                ThemeManager.LastFmSessionKey,
                ThemeManager.LastFmUsername,
                ThemeManager.LastFmEnabled);

            _libreFm.Configure(
                ThemeManager.LibreFmApiKey,
                ThemeManager.LibreFmApiSecret,
                ThemeManager.LibreFmSessionKey,
                ThemeManager.LibreFmUsername,
                ThemeManager.LibreFmEnabled);

            _listenBrainz.Configure(
                ThemeManager.ListenBrainzUserToken,
                ThemeManager.ListenBrainzUsername,
                ThemeManager.ListenBrainzEnabled);

            _maloja.Configure(
                ThemeManager.MalojaServerUrl,
                ThemeManager.MalojaApiKey,
                ThemeManager.MalojaUsername,
                ThemeManager.MalojaEnabled);

            _scrobbler.PauseScrobbling = ThemeManager.PauseScrobbling;
            _scrobbler.ScrobbleAtPercent = ThemeManager.ScrobbleAtPercent;
            _scrobbler.ScrobbleAtSeconds = ThemeManager.ScrobbleAtSeconds;
            _scrobbler.MinScrobbleTrackSeconds = ThemeManager.MinScrobbleTrackSeconds;
            _scrobbler.LoadBlacklist(ParseBlacklist(ThemeManager.ScrobbleBlacklist));
            _scrobbler.RaiseStateChanged();
        }

        private static IEnumerable<string> ParseBlacklist(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) yield break;
            foreach (var entry in raw.Split(";;", StringSplitOptions.RemoveEmptyEntries))
            {
                var trimmed = entry.Trim();
                if (!string.IsNullOrEmpty(trimmed)) yield return trimmed;
            }
        }

        private void SaveBlacklist()
        {
            ThemeManager.ScrobbleBlacklist = string.Join(";;", _scrobbler.Blacklist);
            ThemeManager.SavePlayOptions();
        }

        private void UpdateScrobbleWidgetVisual()
        {
            if (ScrobbleWidgetButton == null) return;

            bool offline = ThemeManager.OfflineModeEnabled;
            bool paused = ThemeManager.PauseScrobbling;
            bool anyAuth = _scrobbler.AnyAuthenticated;
            bool anyEnabled = _scrobbler.AnyEnabled;
            bool hasTrack = _scrobbler.HasCurrentTrack;
            bool suppressed = _scrobbler.CurrentTrackSuppressed || _scrobbler.IsCurrentBlacklisted();

            string tooltip;
            string statusLabel;
            Brush fg;
            double opacity;

            if (offline)
            {
                tooltip = "Scrobbling — Offline mode active";
                statusLabel = "Offline";
                fg = (Brush)FindResource("TextMuted");
                opacity = 0.4;
            }
            else if (paused)
            {
                tooltip = "Scrobbling — Paused";
                statusLabel = "Paused";
                fg = (Brush)FindResource("TextMuted");
                opacity = 0.5;
            }
            else if (!anyAuth)
            {
                tooltip = "Scrobbling — Not connected";
                statusLabel = "Not connected";
                fg = (Brush)FindResource("TextMuted");
                opacity = 0.48;
            }
            else if (!anyEnabled)
            {
                tooltip = "Scrobbling — No enabled services";
                statusLabel = "Ready";
                fg = (Brush)FindResource("TextMuted");
                opacity = 0.58;
            }
            else if (!hasTrack)
            {
                tooltip = "Scrobbling — Ready";
                statusLabel = "Ready";
                fg = (Brush)FindResource("TextMuted");
                opacity = 0.62;
            }
            else if (suppressed)
            {
                tooltip = "Scrobbling — Current song skipped";
                statusLabel = "Skipped";
                fg = (Brush)FindResource("TextMuted");
                opacity = 0.58;
            }
            else if (_lastScrobbleError != null)
            {
                tooltip = "Scrobbling error — " + _lastScrobbleError;
                statusLabel = "Error";
                fg = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0xE0, 0x6C, 0x75));
                opacity = 0.95;
            }
            else if (_scrobbler.CurrentTrackAlreadyScrobbled)
            {
                tooltip = "Scrobbling — Current song submitted";
                statusLabel = "Scrobbled";
                fg = (Brush)FindResource("AccentColor");
                opacity = 0.78;
            }
            else
            {
                var names = _scrobbler.Scrobblers
                    .Where(s => s.IsEnabled && s.IsAuthenticated)
                    .Select(s => s.ServiceName)
                    .ToArray();
                tooltip = names.Length > 0
                    ? "Scrobbling: " + string.Join(", ", names)
                    : "Scrobbling — Click to manage";
                statusLabel = "Scrobbling";
                fg = (Brush)FindResource("AccentColor");
                opacity = 0.86;
            }

            ScrobbleWidgetButton.ToolTip = tooltip;
            ScrobbleWidgetButton.Foreground = fg;
            ScrobbleWidgetButton.Opacity = opacity;

            if (ScrobbleStatusText != null)
            {
                ScrobbleStatusText.Text = statusLabel;
                // Match the icon color so the whole widget reads as one themed unit
                ScrobbleStatusText.Foreground = fg;
                ScrobbleStatusText.Opacity = opacity;
            }
        }

        private void ScrobbleWidgetButton_Click(object sender, RoutedEventArgs e)
        {
            if (ScrobbleWidgetButton.ContextMenu == null) return;
            BuildScrobbleMenu();
            ScrobbleWidgetButton.ContextMenu.PlacementTarget = ScrobbleWidgetButton;
            ScrobbleWidgetButton.ContextMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.Top;
            ScrobbleWidgetButton.ContextMenu.IsOpen = true;
        }

        private void BuildScrobbleMenu()
        {
            // Profiles submenu
            MnuScrobbleProfiles.Items.Clear();
            var authed = _scrobbler.Scrobblers.Where(s => s.IsAuthenticated).ToArray();
            if (authed.Length == 0)
            {
                var empty = new MenuItem { Header = "No accounts connected", IsEnabled = false };
                MnuScrobbleProfiles.Items.Add(empty);
            }
            else
            {
                foreach (var s in authed)
                {
                    var item = new MenuItem
                    {
                        Header = $"Open {s.ServiceName} profile" + (string.IsNullOrEmpty(s.Username) ? "" : $" ({s.Username})")
                    };
                    string url = s.ProfileUrl;
                    item.Click += (_, __) =>
                    {
                        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
                        catch { }
                    };
                    MnuScrobbleProfiles.Items.Add(item);
                }
            }

            bool hasTrack = _scrobbler.CurrentTrack != null;
            MnuScrobbleNow.IsEnabled = hasTrack && _scrobbler.AnyAuthenticated && !ThemeManager.PauseScrobbling;
            MnuDontScrobble.IsEnabled = hasTrack;
            bool blacklisted = _scrobbler.IsCurrentBlacklisted();
            MnuBlacklist.Visibility = (hasTrack && !blacklisted) ? Visibility.Visible : Visibility.Collapsed;
            MnuUnblacklist.Visibility = (hasTrack && blacklisted) ? Visibility.Visible : Visibility.Collapsed;
            MnuPauseAll.IsChecked = ThemeManager.PauseScrobbling;
        }

        private void MnuScrobbleNow_Click(object sender, RoutedEventArgs e)
        {
            _scrobbler.ScrobbleNow();
            UpdateScrobbleWidgetVisual();
        }

        private void MnuDontScrobble_Click(object sender, RoutedEventArgs e)
        {
            _scrobbler.DontScrobbleCurrent();
            UpdateScrobbleWidgetVisual();
        }

        private void MnuBlacklist_Click(object sender, RoutedEventArgs e)
        {
            _scrobbler.BlacklistCurrent();
            SaveBlacklist();
            UpdateScrobbleWidgetVisual();
        }

        private void MnuUnblacklist_Click(object sender, RoutedEventArgs e)
        {
            _scrobbler.UnblacklistCurrent();
            SaveBlacklist();
            UpdateScrobbleWidgetVisual();
        }

        private void MnuPauseAll_Click(object sender, RoutedEventArgs e)
        {
            ThemeManager.PauseScrobbling = !ThemeManager.PauseScrobbling;
            _scrobbler.PauseScrobbling = ThemeManager.PauseScrobbling;
            ThemeManager.SavePlayOptions();
            UpdateScrobbleWidgetVisual();
        }
    }
}
