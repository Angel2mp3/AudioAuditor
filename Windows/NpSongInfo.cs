using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using AudioQualityChecker.Models;
using AudioQualityChecker.Services;

namespace AudioQualityChecker
{
    /// <summary>
    /// Lets the user choose which audio-file facts appear in the Now Playing bottom-bar info row,
    /// and in what order. The row mixes text specs (format, sample rate, bitrate, …) with colored
    /// tag pills (MQA / ALAC / AI / Fake Stereo); all of them participate in one reorderable,
    /// show/hide list — the same scheme as the bottom-bar buttons (see <see cref="NpButtonBar"/>-style
    /// helpers). Order + hidden-set persist as ThemeManager.NpSongInfoOrder / NpSongInfoHidden.
    ///
    /// Items only render when their data/feature gate is met (e.g. BPM needs detection enabled and a
    /// value); "shown" in the list just means "render when available". A pill breaks the text run so
    /// the " • " bullet separators only sit between adjacent text items.
    /// </summary>
    public partial class MainWindow
    {
        private sealed record NpInfoItemDef(string Id, string DisplayName);

        // Stable IDs (persisted) → friendly name. Order here is the DEFAULT order, matching the
        // legacy inline build (text specs first, then the tag pills).
        private static readonly NpInfoItemDef[] NpSongInfoDefs =
        {
            new("format",      "Format"),
            new("samplerate",  "Sample rate"),
            new("bitdepth",    "Bit depth"),
            new("channels",    "Channels"),
            new("bitrate",     "Bitrate"),
            new("dr",          "Dynamic range"),
            new("bpm",         "BPM"),
            new("riplog",      "Rip log"),
            new("replaygain",  "Replay Gain"),
            new("truepeak",    "True Peak"),
            new("lufs",        "LUFS"),
            new("mqa",         "MQA tag"),
            new("alac",        "ALAC tag"),
            new("ai",          "AI tag"),
            new("fakestereo",  "Fake Stereo tag"),
        };

        // ─── Order / hidden resolution ───

        private List<string> NpResolveSongInfoOrder()
        {
            var known = NpSongInfoDefs.Select(d => d.Id).ToList();
            var saved = (ThemeManager.NpSongInfoOrder ?? "")
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(known.Contains)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            // Append any known items not in the saved order (e.g. newly added ones) at the end.
            foreach (var id in known)
                if (!saved.Contains(id, StringComparer.OrdinalIgnoreCase))
                    saved.Add(id);
            return saved;
        }

        private HashSet<string> NpResolveHiddenSongInfo() =>
            new((ThemeManager.NpSongInfoHidden ?? "")
                    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
                StringComparer.OrdinalIgnoreCase);

        // ─── Render ───

        /// <summary>
        /// Rebuilds <c>NpSongInfoPanel</c> for the given track in the user's chosen order, skipping
        /// hidden items and items whose data/feature gate isn't met.
        /// </summary>
        private void NpBuildSongInfoPanel(AudioFileInfo file)
        {
            if (NpSongInfoPanel == null) return;

            // Leaving any transient status (e.g. "Searching lyrics…") back to the real info row.
            if (NpSongInfoStatus != null) NpSongInfoStatus.Visibility = Visibility.Collapsed;
            NpSongInfoPanel.Visibility = Visibility.Visible;
            NpSongInfoPanel.Children.Clear();

            var defaultBrush = (Brush)FindResource("TextSecondary");
            var hidden = NpResolveHiddenSongInfo();
            bool prevWasText = false;

            void AddText(string text, Brush? brush = null, bool semibold = false)
            {
                if (prevWasText)
                    NpSongInfoPanel.Children.Add(new TextBlock
                    {
                        Text = "  •  ",
                        FontSize = 10,
                        Foreground = defaultBrush,
                        FontFamily = new FontFamily("Segoe UI"),
                        VerticalAlignment = VerticalAlignment.Center,
                    });
                NpSongInfoPanel.Children.Add(new TextBlock
                {
                    Text = text,
                    FontSize = 10,
                    Foreground = brush ?? defaultBrush,
                    FontWeight = semibold ? FontWeights.SemiBold : FontWeights.Normal,
                    FontFamily = new FontFamily("Segoe UI"),
                    VerticalAlignment = VerticalAlignment.Center,
                });
                prevWasText = true;
            }

            void AddPill(Border pill)
            {
                NpSongInfoPanel.Children.Add(pill);
                prevWasText = false; // a pill breaks the text run — next text item gets no leading bullet
            }

            int displayBitrate = file.ActualBitrate > 0 ? file.ActualBitrate : file.ReportedBitrate;

            foreach (var id in NpResolveSongInfoOrder())
            {
                if (hidden.Contains(id)) continue;
                switch (id)
                {
                    case "format":
                        if (!string.IsNullOrEmpty(file.FormatDisplay)) AddText(file.FormatDisplay);
                        break;
                    case "samplerate":
                        if (file.SampleRate > 0) AddText($"{file.SampleRate / 1000.0:0.#} kHz");
                        break;
                    case "bitdepth":
                        if (file.BitsPerSample > 0) AddText($"{file.BitsPerSample}-bit");
                        break;
                    case "channels":
                        if (file.Channels > 0)
                            AddText(file.Channels == 1 ? "Mono" : file.Channels == 2 ? "Stereo" : $"{file.Channels}ch");
                        break;
                    case "bitrate":
                        if (displayBitrate > 0)
                        {
                            var statusColor = file.Status switch
                            {
                                AudioStatus.Valid => Color.FromRgb(0x4C, 0xC9, 0x4C),
                                AudioStatus.Fake => Color.FromRgb(0xFF, 0x5C, 0x5C),
                                AudioStatus.Corrupt => Color.FromRgb(0xFF, 0x5C, 0x5C),
                                _ => Color.FromRgb(0xFF, 0xA5, 0x00),
                            };
                            AddText($"{displayBitrate} kbps", new SolidColorBrush(statusColor), semibold: true);
                        }
                        break;
                    case "dr":
                        if (ThemeManager.DynamicRangeEnabled && file.HasDynamicRange && file.DynamicRange > 0)
                            AddText($"DR-{file.DynamicRange:0}");
                        break;
                    case "bpm":
                        if (ThemeManager.BpmDetectionEnabled && file.Bpm > 0)
                            AddText($"{file.Bpm} BPM");
                        break;
                    case "riplog":
                        if (ThemeManager.RipLogCheckEnabled && file.HasRipLog)
                        {
                            var ripColor = file.RipLogVerdict switch
                            {
                                "Perfect" => Color.FromRgb(0x4C, 0xC9, 0x4C),
                                "Good" => Color.FromRgb(0x4C, 0xC9, 0x4C),
                                "Suspect" => Color.FromRgb(0xFF, 0xA5, 0x00),
                                "Bad" => Color.FromRgb(0xFF, 0x5C, 0x5C),
                                _ => Color.FromRgb(0xFF, 0xA5, 0x00),
                            };
                            AddText(file.RipLogDisplay, new SolidColorBrush(ripColor), semibold: true);
                        }
                        break;
                    case "replaygain":
                        if (file.HasReplayGain) AddText(file.ReplayGainDisplay);
                        break;
                    case "truepeak":
                        if (file.HasTruePeak) AddText(file.TruePeakDisplay);
                        break;
                    case "lufs":
                        if (file.HasLufs) AddText(file.LufsDisplay);
                        break;
                    case "mqa":
                        if (file.IsMqa) AddPill(NpCreateTag(file.IsMqaStudio ? "MQA Studio" : "MQA", "#00C2FF"));
                        break;
                    case "alac":
                        if (file.IsAlac) AddPill(NpCreateTag("ALAC", "#7ACC52"));
                        break;
                    case "ai":
                        if (file.AiVerdict == "Yes") AddPill(NpCreateTag("AI", "#FF6B6B"));
                        else if (file.AiVerdict == "Possible") AddPill(NpCreateTag("AI?", "#FFC107"));
                        break;
                    case "fakestereo":
                        if (file.IsFakeStereo) AddPill(NpCreateTag("Fake Stereo", "#FFA500"));
                        break;
                }
            }
        }

        /// <summary>Shows a transient status string (e.g. lyric-search progress) in place of the info row.</summary>
        private void NpShowSongInfoStatus(string text)
        {
            if (NpSongInfoStatus == null) return;
            NpSongInfoStatus.Text = text;
            NpSongInfoStatus.Visibility = Visibility.Visible;
            if (NpSongInfoPanel != null) NpSongInfoPanel.Visibility = Visibility.Collapsed;
        }

        /// <summary>Rebuilds the info row for the current track (used after a customization change).</summary>
        private void NpApplySongInfo()
        {
            if (_player?.CurrentFile == null) return;
            var file = _files.FirstOrDefault(f =>
                string.Equals(f.FilePath, _player.CurrentFile, StringComparison.OrdinalIgnoreCase));
            if (file != null) NpBuildSongInfoPanel(file);
        }

        // ─── Mutations ───

        private void NpSetSongInfoHidden(string id, bool hide)
        {
            var hidden = NpResolveHiddenSongInfo();
            if (hide) hidden.Add(id); else hidden.Remove(id);
            ThemeManager.NpSongInfoHidden = string.Join(",", hidden);
            PersistNpSongInfo();
        }

        private void NpMoveSongInfo(string id, int direction)
        {
            var order = NpResolveSongInfoOrder();
            int idx = order.FindIndex(x => x.Equals(id, StringComparison.OrdinalIgnoreCase));
            if (idx < 0) return;
            int target = idx + Math.Sign(direction);
            if (target < 0 || target >= order.Count) return;
            (order[idx], order[target]) = (order[target], order[idx]);
            ThemeManager.NpSongInfoOrder = string.Join(",", order);
            PersistNpSongInfo();
        }

        private void NpResetSongInfo()
        {
            ThemeManager.NpSongInfoOrder = "";
            ThemeManager.NpSongInfoHidden = "";
            PersistNpSongInfo();
        }

        private void PersistNpSongInfo()
        {
            ThemeManager.SavePlayOptions();
            NpApplySongInfo();
            NpRefreshSongInfoCustomizeList();
        }

        // ─── Customize-list UI (rows in the layout popup) ───

        /// <summary>Rebuilds the song-info customize list in saved order. Reuses <see cref="NpButtonRow"/>.</summary>
        private void NpRefreshSongInfoCustomizeList()
        {
            if (NpSongInfoCustomizeList == null) return;
            var hidden = NpResolveHiddenSongInfo();
            var nameById = NpSongInfoDefs.ToDictionary(d => d.Id, d => d.DisplayName, StringComparer.OrdinalIgnoreCase);

            var rows = NpResolveSongInfoOrder()
                .Where(nameById.ContainsKey)
                .Select(id => new NpButtonRow
                {
                    Id = id,
                    DisplayName = nameById[id],
                    Visible = !hidden.Contains(id),
                    CanRemove = true,
                })
                .ToList();

            NpSongInfoCustomizeList.ItemsSource = null;
            NpSongInfoCustomizeList.ItemsSource = rows;
        }

        private void NpSongInfoVisible_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not CheckBox cb || cb.Tag is not string id) return;
            // cb.IsChecked reflects the new state after the click.
            NpSetSongInfoHidden(id, hide: cb.IsChecked != true);
        }

        private void NpSongInfoMoveLeft_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.Tag is string id) NpMoveSongInfo(id, -1);
        }

        private void NpSongInfoMoveRight_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.Tag is string id) NpMoveSongInfo(id, +1);
        }

        private void NpSongInfoReset_Click(object sender, RoutedEventArgs e)
        {
            ShowThemedConfirm(
                "Reset song info?",
                "Restore the Now Playing song-info row to its default items and order, and show them all again?",
                confirmLabel: "Reset",
                onConfirm: NpResetSongInfo);
        }
    }
}
