using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using AudioQualityChecker.Models;
using AudioQualityChecker.Services;
using IOPath = System.IO.Path;

namespace AudioQualityChecker
{
    /// <summary>
    /// "Look up this song" button on the Now Playing bottom bar. Mirrors the main window's
    /// music-service search buttons (<c>ServiceSearch_Click</c> / UpdateServiceButtonLabels)
    /// but as a single magnifier button that opens a popup of the user's configured services,
    /// so the NP screen can search the currently playing track on Spotify, YouTube Music, etc.
    ///
    /// Uses the NP-specific service config (<see cref="ThemeManager.NpSearchServiceSlots"/>,
    /// NpSearchServiceSlotVisible, NpSearchCustomServiceUrls/Icons) so the Now Playing lookup
    /// can differ from the main window. Shares the URL builder
    /// (<see cref="ThemeManager.GetMusicServiceUrl"/>).
    /// </summary>
    public partial class MainWindow
    {
        // Frozen logos are immutable; cache by "service|index" so reopening the popup
        // doesn't rebuild identical DrawingImages/BitmapImages each time. Zero behavior
        // change — purely avoids repeat icon construction.
        private readonly Dictionary<string, ImageSource> _npSearchLogoCache = new();

        private ImageSource NpGetSearchLogo(string service, int slotIndex)
        {
            // Custom slots vary by per-slot icon path, so they're keyed by index too.
            string customIcon = ThemeManager.NpSearchCustomServiceIcons[slotIndex];
            string key = $"{service}|{slotIndex}|{customIcon}";
            if (_npSearchLogoCache.TryGetValue(key, out var cached))
                return cached;
            var logo = CreateServiceLogo(service, slotIndex, customIconPathOverride: customIcon);
            _npSearchLogoCache[key] = logo;
            return logo;
        }

        /// <summary>Drops cached NP search logos so edited custom icons/services re-render.</summary>
        public void InvalidateNpSearchLogoCache() => _npSearchLogoCache.Clear();
        private void NpSearch_Click(object sender, RoutedEventArgs e)
        {
            if (NpGetCurrentTrackForSearch() is null)
            {
                ErrorDialog.Show("Nothing playing", "Start playing a song to look it up.", this);
                return;
            }

            NpBuildSearchServicesList();
            NpSearchPopup.IsOpen = true;
        }

        /// <summary>The track to search: the one that's playing (NP always reflects playback).</summary>
        private AudioFileInfo? NpGetCurrentTrackForSearch()
        {
            if (_player.CurrentFile == null) return null;
            return _files.FirstOrDefault(f =>
                string.Equals(f.FilePath, _player.CurrentFile, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Rebuilds the popup's service rows from the user's visible service slots. Each row is a
        /// PlayerButton (so it picks up the same ColorMatch/theme tinting as the rest of the NP bar)
        /// showing the service's icon + name.
        /// </summary>
        private void NpBuildSearchServicesList()
        {
            NpSearchServicesList.Children.Clear();

            for (int i = 0; i < ThemeManager.NpSearchServiceSlots.Length; i++)
            {
                if (!ThemeManager.NpSearchServiceSlotVisible[i]) continue;
                string svc = ThemeManager.NpSearchServiceSlots[i];
                if (string.IsNullOrWhiteSpace(svc)) continue;

                string label = svc == "Custom..." ? "Custom search" : svc;

                var row = new StackPanel { Orientation = Orientation.Horizontal };
                row.Children.Add(new Image
                {
                    Width = 16,
                    Height = 16,
                    Margin = new Thickness(0, 0, 8, 0),
                    Source = NpGetSearchLogo(svc, i),
                    Stretch = Stretch.Uniform,
                    VerticalAlignment = VerticalAlignment.Center
                });
                row.Children.Add(new TextBlock
                {
                    Text = label,
                    FontFamily = new FontFamily("Segoe UI"),
                    FontSize = 12,
                    VerticalAlignment = VerticalAlignment.Center,
                    Foreground = (Brush)FindResource("TextPrimary")
                });

                var btn = new Button
                {
                    Content = row,
                    Tag = i,
                    Style = (Style)FindResource("PlayerButton"),
                    Padding = new Thickness(8, 6, 8, 6),
                    HorizontalContentAlignment = HorizontalAlignment.Left,
                    Margin = new Thickness(0, 0, 0, 2),
                    ToolTip = svc == "Custom..." ? "Search on custom service" : $"Search on {svc}"
                };
                btn.Click += NpSearchService_Click;
                NpSearchServicesList.Children.Add(btn);
            }

            // No visible services configured — tell the user where to set them up.
            if (NpSearchServicesList.Children.Count == 0)
            {
                NpSearchServicesList.Children.Add(new TextBlock
                {
                    Text = "No search services enabled.\nAdd them in Settings → Now Playing.",
                    FontFamily = new FontFamily("Segoe UI"),
                    FontSize = 11,
                    Margin = new Thickness(6, 2, 6, 4),
                    Foreground = (Brush)FindResource("TextMuted"),
                    TextWrapping = TextWrapping.Wrap
                });
            }
        }

        private void NpSearchService_Click(object sender, RoutedEventArgs e)
        {
            NpSearchPopup.IsOpen = false;
            if (sender is not Button btn || btn.Tag is not int idx) return;
            if (idx < 0 || idx >= ThemeManager.NpSearchServiceSlots.Length) return;

            var file = NpGetCurrentTrackForSearch();
            if (file is null)
            {
                ErrorDialog.Show("Nothing playing", "Start playing a song to look it up.", this);
                return;
            }

            // Same query/URL/launch logic as the main window's ServiceSearch_Click.
            string serviceName = ThemeManager.NpSearchServiceSlots[idx];
            string query = !string.IsNullOrEmpty(file.Artist) && !string.IsNullOrEmpty(file.Title)
                ? $"{file.Artist} {file.Title}"
                : IOPath.GetFileNameWithoutExtension(file.FileName);

            string url;
            if (serviceName == "Custom...")
            {
                string customUrl = ThemeManager.NpSearchCustomServiceUrls[idx];
                if (string.IsNullOrWhiteSpace(customUrl))
                {
                    ErrorDialog.Show("No Custom URL", "Configure a custom search URL in Settings first.\nPaste the search URL and the song name will be appended automatically.", this);
                    return;
                }
                string encoded = Uri.EscapeDataString(query);
                url = customUrl.Contains("{query}")
                    ? customUrl.Replace("{query}", encoded)
                    : customUrl.TrimEnd('/') + "/" + encoded;
            }
            else
            {
                url = ThemeManager.GetMusicServiceUrl(serviceName, query);
            }

            try
            {
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                ErrorDialog.Show("Browser Error", $"Could not open browser:\n{ex.Message}", this);
            }
        }
    }
}
