using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using AudioQualityChecker.Models;
using AudioQualityChecker.Services;

namespace AudioQualityChecker
{
    /// <summary>
    /// Saved Now Playing layout profiles UI (the list + Save/apply/delete/reorder in the Customize
    /// Layout popup), plus the themed reset-to-default confirmation. Persistence lives in
    /// <see cref="NpLayoutProfileStore"/>; capture/apply of the actual values lives in ThemeManager.
    /// </summary>
    public partial class MainWindow
    {
        /// <summary>Rebinds the profiles list in the layout popup from disk.</summary>
        private void NpRefreshLayoutProfilesList()
        {
            if (NpLayoutProfilesList == null) return;
            var profiles = NpLayoutProfileStore.LoadProfiles();
            NpLayoutProfilesList.ItemsSource = null;
            NpLayoutProfilesList.ItemsSource = profiles;
            if (NpLayoutProfilesEmpty != null)
                NpLayoutProfilesEmpty.Visibility = profiles.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        private void NpLayoutProfileSave_Click(object sender, RoutedEventArgs e)
        {
            // Make sure the current on-screen layout is flushed into ThemeManager before capturing.
            NpSavePreferences();

            ShowThemedInput(
                "Save layout profile",
                "Name this layout. It saves your complete Now Playing arrangement and can be re-applied any time.",
                initial: "My layout",
                confirmLabel: "Save",
                onConfirm: name =>
                {
                    if (NpLayoutProfileStore.Exists(name))
                    {
                        ShowThemedConfirm(
                            "Overwrite profile?",
                            $"A layout named “{name}” already exists. Overwrite it with the current layout?",
                            confirmLabel: "Overwrite",
                            onConfirm: () => NpCommitLayoutProfile(name));
                    }
                    else
                    {
                        NpCommitLayoutProfile(name);
                    }
                });
        }

        private void NpCommitLayoutProfile(string name)
        {
            try
            {
                var profile = ThemeManager.CaptureNpLayout(name);
                NpLayoutProfileStore.SaveProfile(profile);
                NpRefreshLayoutProfilesList();
                ShowThemedNotice("Layout saved", $"“{name}” was saved. Pick it from the list any time.");
            }
            catch (Exception ex)
            {
                ShowThemedNotice("Couldn't save layout", ex.Message);
            }
        }

        private void NpLayoutProfileApply_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn || btn.Tag is not string name) return;
            var profile = NpLayoutProfileStore.FindProfile(name);
            if (profile == null) return;

            // Apply values into ThemeManager, then refresh the in-memory NP fields for the current
            // window/visualizer context and re-render the live layout + popup sliders.
            ThemeManager.ApplyNpLayout(profile);
            NpReloadLayoutAfterProfileChange();
            ShowThemedNotice("Layout applied", $"Switched to “{name}”.");
        }

        /// <summary>
        /// After ThemeManager layout values change (profile applied or reset), pull them back into
        /// the live NP fields and re-render everything that depends on them.
        /// </summary>
        private void NpReloadLayoutAfterProfileChange()
        {
            bool fs = WindowState == WindowState.Maximized;

            // Glow + backdrop locals
            _npCoverGlowSize = ThemeManager.NpCoverGlowSize;
            _npFocusedLyricsBlurRadius = ThemeManager.NpFocusedLyricsBlurRadius;
            _npFocusedLyricsInactiveBlur = null;
            _npCoverGlowMotionEnabled = ThemeManager.NpCoverGlowMotionEnabled;
            _npGlowMotionMode = ThemeManager.NpGlowMotionMode;

            // Per-context size/offset bundle for the current state
            NpLoadActiveLayoutProfile(fs, _npVisualizerEnabled);

            // Reflect into the popup sliders if it's open, then apply visuals
            if (NpLayoutPopup?.IsOpen == true)
                NpSeedLayoutSliders(fs);
            NpApplyFullscreenScaling(fs);
            NpApplyCoverGlowScale();
            NpApplyCoverShape();
            NpApplyFocusedLyricsEffects();
            NpRefreshBackdropFromSettings();
            NpApplyVizPlacement();
            NpApplyButtonBar();
            if (_npCoverGlowMotionEnabled && IsNowPlayingUiActive())
                NpStartGlowPulse();
            else
                NpStopGlowPulse();
        }

        private void NpLayoutProfileDelete_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn || btn.Tag is not string name) return;
            ShowThemedConfirm(
                "Delete profile?",
                $"Delete the layout profile “{name}”? This can't be undone.",
                confirmLabel: "Delete",
                onConfirm: () =>
                {
                    NpLayoutProfileStore.DeleteProfile(name);
                    NpRefreshLayoutProfilesList();
                    ShowThemedNotice("Profile deleted", $"“{name}” was removed.");
                });
        }

        private void NpLayoutProfileMoveUp_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn || btn.Tag is not string name) return;
            NpLayoutProfileStore.MoveProfile(name, -1);
            NpRefreshLayoutProfilesList();
        }

        private void NpLayoutProfileMoveDown_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn || btn.Tag is not string name) return;
            NpLayoutProfileStore.MoveProfile(name, +1);
            NpRefreshLayoutProfilesList();
        }

        /// <summary>Reset button now double-checks with a themed confirm before wiping the layout.</summary>
        private void NpLayoutResetConfirm_Click(object sender, RoutedEventArgs e)
        {
            ShowThemedConfirm(
                "Reset layout to default?",
                "This clears your current Now Playing layout customizations (sizes, positions, glow, and backdrop) and restores the defaults. Saved profiles are not affected.",
                confirmLabel: "Reset",
                onConfirm: () => NpLayoutReset_Click(sender, e));
        }
    }
}
