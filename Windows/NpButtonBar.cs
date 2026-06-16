using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using AudioQualityChecker.Services;

namespace AudioQualityChecker
{
    /// <summary>
    /// Lets the user reorder and hide buttons in the Now Playing bottom bar.
    ///
    /// Two groups participate:
    ///  • OPTIONAL buttons (the right-hand <see cref="NpOptionButtonDefs"/>) — reorderable AND
    ///    removable. They live in the NpOptionsBar StackPanel, interleaved with their popups and
    ///    separator borders. Order + hidden-set persist as ThemeManager.NpButtonOrder / NpButtonHidden.
    ///  • TRANSPORT buttons (shuffle/loop/prev/play/next, <see cref="NpTransportButtonDefs"/>) — in the
    ///    center NpTransportBar; reorderable WITHIN that group but never removable. Order persists as
    ///    ThemeManager.NpTransportOrder.
    ///
    /// Popups are Placement-positioned (no layout footprint), so reordering only the registered Button
    /// instances is safe; we reinsert them as a contiguous block at the index of the first group button,
    /// leaving separators/volume/back where they are.
    /// </summary>
    public partial class MainWindow
    {
        private sealed record NpOptButton(string Id, string DisplayName, Func<MainWindow, Button?> Get, bool CanRemove = true);

        // Stable IDs (persisted) → friendly name + accessor. Order here is the DEFAULT order.
        private static readonly NpOptButton[] NpOptionButtonDefs =
        {
            new("autoplay",   "Auto-play",       w => w.NpAutoPlayBtn),
            new("crossfade",  "Crossfade",       w => w.NpCrossfadeBtn),
            new("visualizer", "Visualizer",      w => w.NpVisualizerBtn),
            new("vizstyle",   "Visualizer style",w => w.NpVizStyleBtn),
            new("vizplace",   "Visualizer place",w => w.NpVizPlacementBtn),
            new("bgfx",       "Background FX",   w => w.NpBgFxBtn),
            new("eq",         "Equalizer",       w => w.NpEqBtn),
            new("colormatch", "Color match",     w => w.NpColorMatchBtn),
            new("lyricsoff",  "Hide lyrics",     w => w.NpLyricsOffBtn),
            new("translate",  "Translate",       w => w.NpTranslateBtn),
            new("translateset","Translate settings", w => w.NpTranslateSettingsBtn),
            new("focusedlyrics","Lyrics mode",   w => w.NpFocusedLyricsBtn),
            new("karaoke",    "Karaoke",         w => w.NpKaraokeBtn),
            new("provider",   "Lyrics source",   w => w.NpProviderBtn),
            new("savelyrics", "Save lyrics",     w => w.NpSaveLyricsBtn),
            new("search",     "Look up song",    w => w.NpSearchBtn),
            new("queue",      "Queue",           w => w.NpQueueBtn),
            new("settings",   "Settings",        w => w.NpSettingsBtn),
            new("layout",     "Customize layout",w => w.NpLayoutBtn),
        };

        // Transport buttons — reorderable but NOT removable (CanRemove: false). Default order matches
        // the XAML layout of NpTransportBar.
        private static readonly NpOptButton[] NpTransportButtonDefs =
        {
            new("t_shuffle", "Shuffle",      w => w.NpShuffleBtn,    CanRemove: false),
            new("t_loop",    "Loop",         w => w.NpLoopBtn,       CanRemove: false),
            new("t_prev",    "Previous",     w => w.NpPrevBtn,       CanRemove: false),
            new("t_play",    "Play / Pause", w => w.NpPlayPauseBtn,  CanRemove: false),
            new("t_next",    "Next",         w => w.NpNextBtn,       CanRemove: false),
        };

        // ─── Order/hidden resolution ───

        /// <summary>Resolves the saved (or default) display order for a button group.</summary>
        private static List<string> NpResolveOrder(NpOptButton[] defs, string? savedCsv)
        {
            var known = defs.Select(d => d.Id).ToList();
            var saved = (savedCsv ?? "")
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(known.Contains)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            // Append any known buttons not in the saved order (e.g. newly added ones) at the end.
            foreach (var id in known)
                if (!saved.Contains(id, StringComparer.OrdinalIgnoreCase))
                    saved.Add(id);
            return saved;
        }

        private List<string> NpResolveButtonOrder() => NpResolveOrder(NpOptionButtonDefs, ThemeManager.NpButtonOrder);
        private List<string> NpResolveTransportOrder() => NpResolveOrder(NpTransportButtonDefs, ThemeManager.NpTransportOrder);

        private HashSet<string> NpResolveHiddenButtons() =>
            new((ThemeManager.NpButtonHidden ?? "")
                    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
                StringComparer.OrdinalIgnoreCase);

        // ─── Apply order to the live panels ───

        /// <summary>
        /// Reorders the optional buttons inside NpOptionsBar to the saved order and applies hidden
        /// visibility. Re-AND with feature-driven visibility (e.g. viz placement) afterward.
        /// </summary>
        private void NpApplyButtonBar()
        {
            if (NpOptionsBar != null)
            {
                var hidden = NpResolveHiddenButtons();
                NpReorderPanel(NpOptionsBar, NpOptionButtonDefs, NpResolveButtonOrder(),
                    id => hidden.Contains(id));
                // Re-apply feature-driven visibility that must override "shown" (e.g. viz placement
                // button only makes sense when the visualizer is on).
                NpApplyVizPlacement();
                NpCleanupSeparators(NpOptionsBar);
            }

            if (NpTransportBar != null)
            {
                // Transport is never hidden.
                NpReorderPanel(NpTransportBar, NpTransportButtonDefs, NpResolveTransportOrder(),
                    _ => false);
            }
        }

        /// <summary>
        /// Reinserts the group's buttons into <paramref name="panel"/> in <paramref name="order"/>,
        /// leaving any non-group children (separators/popups/volume) in place, and applies hidden.
        /// </summary>
        private void NpReorderPanel(Panel panel, NpOptButton[] defs, List<string> order, Func<string, bool> isHidden)
        {
            var byId = new Dictionary<string, Button>(StringComparer.OrdinalIgnoreCase);
            foreach (var def in defs)
            {
                var btn = def.Get(this);
                if (btn != null) byId[def.Id] = btn;
            }
            if (byId.Count == 0) return;

            // Find the lowest current child index among the group buttons — we reinsert the
            // reordered block starting there, leaving everything else in place.
            int insertAt = int.MaxValue;
            foreach (var btn in byId.Values)
            {
                int idx = panel.Children.IndexOf(btn);
                if (idx >= 0 && idx < insertAt) insertAt = idx;
            }
            if (insertAt == int.MaxValue) return;

            foreach (var btn in byId.Values)
            {
                int idx = panel.Children.IndexOf(btn);
                if (idx >= 0)
                {
                    panel.Children.RemoveAt(idx);
                    if (idx < insertAt) insertAt--; // shift anchor left as earlier items are removed
                }
            }
            int cursor = insertAt;
            foreach (var id in order)
            {
                if (!byId.TryGetValue(id, out var btn)) continue;
                panel.Children.Insert(cursor++, btn);
                btn.Visibility = isHidden(id) ? Visibility.Collapsed : Visibility.Visible;
            }
        }

        /// <summary>
        /// Collapses divider separators (Tag="sep") that are orphaned — i.e. have no visible button
        /// before them, none after, or sit directly next to another visible separator. Without this,
        /// hiding/reordering buttons leaves a run of dividers showing as "| | | |".
        /// </summary>
        private static void NpCleanupSeparators(Panel panel)
        {
            // First pass: reset all separators to visible so we re-evaluate from a clean slate.
            foreach (var child in panel.Children)
                if (child is FrameworkElement fe && (fe.Tag as string) == "sep")
                    fe.Visibility = Visibility.Visible;

            bool IsSep(UIElement el) => el is FrameworkElement fe && (fe.Tag as string) == "sep";
            bool IsVisibleButton(UIElement el) => el is Button b && b.Visibility == Visibility.Visible;

            for (int i = 0; i < panel.Children.Count; i++)
            {
                if (panel.Children[i] is not FrameworkElement sep || !IsSep(sep)) continue;

                // Is there a visible button to the left (before another separator)?
                bool buttonLeft = false;
                for (int j = i - 1; j >= 0; j--)
                {
                    if (IsSep(panel.Children[j])) break;
                    if (IsVisibleButton(panel.Children[j])) { buttonLeft = true; break; }
                }

                // Is there a visible button to the right (before another separator)?
                bool buttonRight = false;
                for (int j = i + 1; j < panel.Children.Count; j++)
                {
                    if (IsSep(panel.Children[j])) break;
                    if (IsVisibleButton(panel.Children[j])) { buttonRight = true; break; }
                }

                if (!buttonLeft || !buttonRight)
                    sep.Visibility = Visibility.Collapsed;
            }
        }

        // ─── Mutations ───

        private void NpSetButtonHidden(string id, bool hide)
        {
            // Only optional buttons can be hidden; transport entries are protected.
            if (NpTransportButtonDefs.Any(d => d.Id.Equals(id, StringComparison.OrdinalIgnoreCase))) return;
            var hidden = NpResolveHiddenButtons();
            if (hide) hidden.Add(id); else hidden.Remove(id);
            ThemeManager.NpButtonHidden = string.Join(",", hidden);
            PersistNpButtonBar();
        }

        /// <summary>Moves a button left (-1) or right (+1) within whichever group it belongs to.</summary>
        private void NpMoveButton(string id, int direction)
        {
            bool isTransport = NpTransportButtonDefs.Any(d => d.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
            var order = isTransport ? NpResolveTransportOrder() : NpResolveButtonOrder();

            int idx = order.FindIndex(x => x.Equals(id, StringComparison.OrdinalIgnoreCase));
            if (idx < 0) return;
            int target = idx + Math.Sign(direction);
            if (target < 0 || target >= order.Count) return;
            (order[idx], order[target]) = (order[target], order[idx]);

            if (isTransport)
                ThemeManager.NpTransportOrder = string.Join(",", order);
            else
                ThemeManager.NpButtonOrder = string.Join(",", order);
            PersistNpButtonBar();
        }

        private void NpResetButtonBar()
        {
            ThemeManager.NpButtonOrder = "";
            ThemeManager.NpButtonHidden = "";
            ThemeManager.NpTransportOrder = "";
            PersistNpButtonBar();
        }

        private void PersistNpButtonBar()
        {
            ThemeManager.SavePlayOptions();
            NpApplyButtonBar();
            NpRefreshButtonCustomizeList();
        }

        // ─── Customize-list UI (rows in the layout popup) ───

        /// <summary>Row view-model for the button-customize list.</summary>
        private sealed class NpButtonRow
        {
            public string Id { get; init; } = "";
            public string DisplayName { get; init; } = "";
            public bool Visible { get; init; }
            public bool CanRemove { get; init; } = true;
            /// <summary>Controls the show/hide checkbox visibility in the row template.</summary>
            public Visibility CheckBoxVisibility => CanRemove ? Visibility.Visible : Visibility.Collapsed;
        }

        /// <summary>Rebuilds the customize list (transport first, then optional) in saved order.</summary>
        private void NpRefreshButtonCustomizeList()
        {
            if (NpButtonCustomizeList == null) return;
            var hidden = NpResolveHiddenButtons();

            var rows = new List<NpButtonRow>();

            // Transport group first — reorderable, not removable.
            var transportNameById = NpTransportButtonDefs.ToDictionary(d => d.Id, d => d.DisplayName, StringComparer.OrdinalIgnoreCase);
            rows.AddRange(NpResolveTransportOrder()
                .Where(transportNameById.ContainsKey)
                .Select(id => new NpButtonRow
                {
                    Id = id,
                    DisplayName = transportNameById[id],
                    Visible = true,
                    CanRemove = false,
                }));

            // Optional group — reorderable and removable.
            var optNameById = NpOptionButtonDefs.ToDictionary(d => d.Id, d => d.DisplayName, StringComparer.OrdinalIgnoreCase);
            rows.AddRange(NpResolveButtonOrder()
                .Where(optNameById.ContainsKey)
                .Select(id => new NpButtonRow
                {
                    Id = id,
                    DisplayName = optNameById[id],
                    Visible = !hidden.Contains(id),
                    CanRemove = true,
                }));

            NpButtonCustomizeList.ItemsSource = null;
            NpButtonCustomizeList.ItemsSource = rows;
        }

        private void NpButtonVisible_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not CheckBox cb || cb.Tag is not string id) return;
            // cb.IsChecked reflects the new state after the click.
            NpSetButtonHidden(id, hide: cb.IsChecked != true);
        }

        private void NpButtonMoveLeft_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.Tag is string id) NpMoveButton(id, -1);
        }

        private void NpButtonMoveRight_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.Tag is string id) NpMoveButton(id, +1);
        }

        private void NpButtonReset_Click(object sender, RoutedEventArgs e)
        {
            ShowThemedConfirm(
                "Reset buttons?",
                "Restore the bottom-bar and transport buttons to their default order and show them all again?",
                confirmLabel: "Reset",
                onConfirm: NpResetButtonBar);
        }
    }
}
