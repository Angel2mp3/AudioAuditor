using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using AudioQualityChecker.Services;

namespace AudioQualityChecker
{
    /// <summary>
    /// Modal/overlay UI for the main window: donation prompts (timed + 30-day), the one-hour feedback
    /// nudge, the reusable themed-notice toast, crash-recovery notice, restore-last-session prompt,
    /// the feature-configuration overlay, the SH Labs privacy + scan-limit overlays, and the footer
    /// support link. Pure UI/flow code split out of MainWindow.xaml.cs. The Update-available overlay
    /// intentionally stays in MainWindow.xaml.cs.
    /// </summary>
    public partial class MainWindow
    {
        // Overlay-owned state (used only by the methods in this partial).
        private DispatcherTimer? _feedbackUsageTimer;
        private DispatcherTimer? _donationTimer;
        private bool _donationScheduled;
        private DispatcherTimer? _themedNoticeTimer;
        private List<string>? _pendingRestoreRoots;
        private List<string>? _pendingRestoreFiles;
        private bool _shLabsPrivacyFromFeatureConfig;
        private TaskCompletionSource<bool>? _shLabsLimitTcs;

        // ═══════════════════════════════════════════
        //  Crash-recovery notice (startup)
        // ═══════════════════════════════════════════

        private void MaybeShowCrashRecoveryNotice()
        {
            try
            {
                if (SessionRestoreService.ConsumeRecoveryMarker(out _, out _))
                {
                    ShowThemedNotice(
                        "Recovered from a problem",
                        "AudioAuditor closed unexpectedly last time and has restarted. Your previous files can be restored if session restore is enabled.");
                }
            }
            catch { /* never block startup */ }
        }

        // ═══════════════════════════════════════════
        //  Donation Overlay
        // ═══════════════════════════════════════════

        private void ScheduleDonationPopup()
        {
            if (ThemeManager.DonationDismissed || _donationScheduled) return;
            _donationScheduled = true;

            _donationTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(120) };
            _donationTimer.Tick += (s, e) =>
            {
                _donationTimer!.Stop();
                _donationTimer = null;
                // Don't interrupt an in-progress analysis
                if (_isAnalyzing)
                {
                    _donationScheduled = false; // allow re-schedule after next analysis
                    return;
                }
                if (!ThemeManager.DonationDismissed)
                    ShowDonationOverlay();
            };
            _donationTimer.Start();
        }

        // ─── 30-Day Usage Donation Popup ───

        private static readonly string UsageDaysFile = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "AudioAuditor", "usage-days.txt");

        private void Check30DayDonationPopup()
        {
            if (ThemeManager.Donation30DayShown) return;
            if (ThemeManager.FirstScanDate == default) return;

            int usageDays = CountUsageDays();
            if (usageDays < 30) return;
            if (ThemeManager.TotalFilesScannedLifetime <= 0) return;

            ShowDonation30DayOverlay();
        }

        private static int CountUsageDays()
        {
            try
            {
                if (!File.Exists(UsageDaysFile)) return 0;
                var lines = File.ReadAllLines(UsageDaysFile);
                var dates = new HashSet<string>();
                foreach (var line in lines)
                {
                    var trimmed = line.Trim();
                    if (!string.IsNullOrEmpty(trimmed))
                        dates.Add(trimmed);
                }
                return dates.Count;
            }
            catch { return 0; }
        }

        private static void RecordUsageDay()
        {
            try
            {
                string today = DateTime.Now.ToString("yyyy-MM-dd");
                string dir = Path.GetDirectoryName(UsageDaysFile)!;
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

                var dates = new HashSet<string>();
                if (File.Exists(UsageDaysFile))
                {
                    foreach (var line in File.ReadAllLines(UsageDaysFile))
                    {
                        var trimmed = line.Trim();
                        if (!string.IsNullOrEmpty(trimmed)) dates.Add(trimmed);
                    }
                }
                dates.Add(today);
                File.WriteAllLines(UsageDaysFile, dates.OrderBy(d => d));
            }
            catch { }
        }

        private void ShowDonationOverlay()
        {
            DonationOverlay.Visibility = Visibility.Visible;
            MainContent.Effect = new System.Windows.Media.Effects.BlurEffect { Radius = 6 };
        }

        private void HideDonationOverlay()
        {
            DonationOverlay.Visibility = Visibility.Collapsed;
            MainContent.Effect = null;
            ThemeManager.DonationDismissed = true;
            ThemeManager.SetRegistryFlag("DonationDismissed", true);
            ThemeManager.SavePlayOptions();
        }

        private void ShowDonation30DayOverlay()
        {
            double hours = ThemeManager.TotalListeningSecondsLifetime / 3600.0;
            string stats = $"Scanned {ThemeManager.TotalFilesScannedLifetime:N0} files · {hours:F1} hours of listening";
            Donation30DayStatsText.Text = stats;
            Donation30DayOverlay.Visibility = Visibility.Visible;
        }

        private void HideDonation30DayOverlay()
        {
            Donation30DayOverlay.Visibility = Visibility.Collapsed;
            ThemeManager.Donation30DayShown = true;
            ThemeManager.SavePlayOptions();
        }

        private void StartFeedbackUsageTimer()
        {
            if (ThemeManager.FeedbackOneHourShown) return;
            _feedbackUsageTimer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(1) };
            _feedbackUsageTimer.Tick += (_, _) =>
            {
                if (ThemeManager.FeedbackOneHourShown)
                {
                    _feedbackUsageTimer?.Stop();
                    return;
                }

                if (!IsVisible || WindowState == WindowState.Minimized)
                    return;

                ThemeManager.FeedbackActiveUsageSeconds = Math.Min(3600, ThemeManager.FeedbackActiveUsageSeconds + 60);
                ThemeManager.SavePlayOptions();
                if (ThemeManager.FeedbackActiveUsageSeconds >= 3600)
                    ShowFeedbackOneHourOverlay();
            };
            _feedbackUsageTimer.Start();
        }

        private void ShowFeedbackOneHourOverlay()
        {
            if (ThemeManager.FeedbackOneHourShown) return;
            if (!IsVisible || WindowState == WindowState.Minimized) return;
            if (DonationOverlay.Visibility == Visibility.Visible || Donation30DayOverlay.Visibility == Visibility.Visible)
                return;

            FeedbackOneHourOverlay.Visibility = Visibility.Visible;
            _feedbackUsageTimer?.Stop();
        }

        private void HideFeedbackOneHourOverlay()
        {
            FeedbackOneHourOverlay.Visibility = Visibility.Collapsed;
            ThemeManager.FeedbackOneHourShown = true;
            ThemeManager.FeedbackActiveUsageSeconds = 3600;
            ThemeManager.SavePlayOptions();
            _feedbackUsageTimer?.Stop();
        }

        private static void OpenFeedbackLink(string url)
        {
            try
            {
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            }
            catch { }
        }

        private void FeedbackStar_Click(object sender, RoutedEventArgs e)
        {
            OpenFeedbackLink("https://github.com/Angel2mp3/AudioAuditor");
            HideFeedbackOneHourOverlay();
        }

        private void FeedbackDonate_Click(object sender, RoutedEventArgs e)
        {
            OpenFeedbackLink("https://ko-fi.com/angelsoftware");
            HideFeedbackOneHourOverlay();
        }

        private void FeedbackIssue_Click(object sender, RoutedEventArgs e)
        {
            OpenFeedbackLink("https://github.com/Angel2mp3/AudioAuditor/issues/new");
            HideFeedbackOneHourOverlay();
        }

        private void FeedbackEmail_Click(object sender, RoutedEventArgs e)
        {
            OpenFeedbackLink("https://audioauditor.org/#contact");
            HideFeedbackOneHourOverlay();
        }

        private void FeedbackClose_Click(object sender, RoutedEventArgs e) => HideFeedbackOneHourOverlay();

        private void FeedbackOneHourBackdrop_Click(object sender, System.Windows.Input.MouseButtonEventArgs e) =>
            HideFeedbackOneHourOverlay();

        private void Donation30DayDonate_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Process.Start(new ProcessStartInfo("https://ko-fi.com/angelsoftware") { UseShellExecute = true });
            }
            catch { }
            HideDonation30DayOverlay();
        }

        private void Donation30DayClose_Click(object sender, RoutedEventArgs e)
        {
            HideDonation30DayOverlay();
        }

        private void Donation30DayBackdrop_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            HideDonation30DayOverlay();
        }

        private void DonationDonate_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Process.Start(new ProcessStartInfo("https://ko-fi.com/angelsoftware") { UseShellExecute = true });
            }
            catch { }
            HideDonationOverlay();
        }

        private void DonationClose_Click(object sender, RoutedEventArgs e)
        {
            HideDonationOverlay();
        }

        private void DonationBackdrop_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            // Clicking outside the popup also dismisses it
            HideDonationOverlay();
        }

        // ═══════════════════════════════════════════
        //  Themed notice toast (reusable, non-modal)
        //  Used by Settings ("scan cache turned on too") and crash recovery
        //  ("AudioAuditor recovered from a problem"). Auto-dismisses; click to close.
        // ═══════════════════════════════════════════

        /// <summary>
        /// Shows a small, self-dismissing toast in the bottom-right corner. Safe to call
        /// from any thread and before the window is fully shown. Reuses the glass theme.
        /// </summary>
        public void ShowThemedNotice(string title, string body, int autoDismissSeconds = 8)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.InvokeAsync(() => ShowThemedNotice(title, body, autoDismissSeconds));
                return;
            }

            try
            {
                ThemedNoticeTitle.Text = title ?? "";
                ThemedNoticeBody.Text = body ?? "";
                ThemedNoticeBody.Visibility = string.IsNullOrEmpty(body)
                    ? Visibility.Collapsed : Visibility.Visible;
                ThemedNoticeToast.Visibility = Visibility.Visible;

                _themedNoticeTimer?.Stop();
                if (autoDismissSeconds > 0)
                {
                    _themedNoticeTimer ??= new DispatcherTimer();
                    _themedNoticeTimer.Interval = TimeSpan.FromSeconds(autoDismissSeconds);
                    _themedNoticeTimer.Tick -= ThemedNoticeTimer_Tick;
                    _themedNoticeTimer.Tick += ThemedNoticeTimer_Tick;
                    _themedNoticeTimer.Start();
                }
            }
            catch { /* a notice must never break the caller */ }
        }

        private void ThemedNoticeTimer_Tick(object? sender, EventArgs e) => HideThemedNotice();

        private void HideThemedNotice()
        {
            _themedNoticeTimer?.Stop();
            ThemedNoticeToast.Visibility = Visibility.Collapsed;
        }

        private void ThemedNoticeClose_Click(object sender, RoutedEventArgs e) => HideThemedNotice();

        // ═══════════════════════════════════════════
        //  Restore Last Session prompt (startup)
        // ═══════════════════════════════════════════

        /// <summary>
        /// If the user enabled "restore last session" and a saved session exists, shows a
        /// themed prompt offering to reload it. Called once at startup after the window is up.
        /// </summary>
        private void MaybeOfferSessionRestore()
        {
            try
            {
                if (!ThemeManager.RestoreLastSessionEnabled) return;
                // Don't fight the startup file-open path: if the user launched us with
                // explicit paths (Open With / drag-onto-exe), skip the restore prompt.
                if (App.PendingStartupPaths is { Count: > 0 }) return;
                if (!SessionRestoreService.HasSavedSession()) return;

                var state = SessionRestoreService.Load();
                if (state == null) return;

                _pendingRestoreRoots = state.Roots ?? new List<string>();
                _pendingRestoreFiles = state.Files ?? new List<string>();
                if (_pendingRestoreRoots.Count == 0 && _pendingRestoreFiles.Count == 0) return;

                // Describe by file count when we have it (most meaningful to the user),
                // otherwise by the number of added items (folders/files).
                int fileCount = _pendingRestoreFiles.Count;
                int itemCount = _pendingRestoreRoots.Count;
                string what = fileCount > 0
                    ? $"{fileCount} file{(fileCount == 1 ? "" : "s")}"
                    : $"{itemCount} item{(itemCount == 1 ? "" : "s")}";
                RestoreSessionDetail.Text =
                    $"You had {what} loaded last time. Reopen them now?";

                RestoreSessionOverlay.Visibility = Visibility.Visible;
            }
            catch { /* never let restore offer break startup */ }
        }

        private void HideRestoreSessionOverlay()
        {
            RestoreSessionOverlay.Visibility = Visibility.Collapsed;
        }

        private void RestoreSessionRestore_Click(object sender, RoutedEventArgs e)
        {
            HideRestoreSessionOverlay();
            // Prefer the user-added roots (picks up new files since last time); fall back
            // to the resolved file list when no roots were recorded.
            var toLoad = (_pendingRestoreRoots is { Count: > 0 })
                ? _pendingRestoreRoots
                : (_pendingRestoreFiles ?? new List<string>());
            _pendingRestoreRoots = null;
            _pendingRestoreFiles = null;
            if (toLoad.Count > 0)
                LoadPathsFromExternal(toLoad);
        }

        private void RestoreSessionDismiss_Click(object sender, RoutedEventArgs e)
        {
            _pendingRestoreRoots = null;
            _pendingRestoreFiles = null;
            HideRestoreSessionOverlay();
        }

        private void RestoreSessionBackdrop_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            _pendingRestoreRoots = null;
            _pendingRestoreFiles = null;
            HideRestoreSessionOverlay();
        }

        // ═══════════════════════════════════════════
        //  Feature Configuration Overlay
        // ═══════════════════════════════════════════

        private void ShowFeatureConfigOverlay()
        {
            // Load saved toggle states into checkboxes
            FcChkSilence.IsChecked = ThemeManager.SilenceDetectionEnabled;
            FcChkFakeStereo.IsChecked = ThemeManager.FakeStereoDetectionEnabled;
            FcChkDR.IsChecked = ThemeManager.DynamicRangeEnabled;
            FcChkTruePeak.IsChecked = ThemeManager.TruePeakEnabled;
            FcChkLufs.IsChecked = ThemeManager.LufsEnabled;
            FcChkClipping.IsChecked = ThemeManager.ClippingDetectionEnabled;
            FcChkMqa.IsChecked = ThemeManager.MqaDetectionEnabled;
            FcChkBpm.IsChecked = ThemeManager.BpmDetectionEnabled;
            FcChkRipQuality.IsChecked = ThemeManager.RipQualityEnabled;
            FcChkDefaultAi.IsChecked = ThemeManager.DefaultAiDetectionEnabled;
            FcChkExperimentalAi.IsChecked = ThemeManager.ExperimentalAiDetection;
            FcChkSHLabs.IsChecked = ThemeManager.SHLabsAiDetection;

            FeatureConfigOverlay.Visibility = Visibility.Visible;
            MainContent.Effect = new System.Windows.Media.Effects.BlurEffect { Radius = 6 };
        }

        private void HideFeatureConfigOverlay()
        {
            FeatureConfigOverlay.Visibility = Visibility.Collapsed;
            MainContent.Effect = null;
        }

        private void FeatureConfigSave_Click(object sender, RoutedEventArgs e)
        {
            // Persist core feature toggles
            ThemeManager.SilenceDetectionEnabled = FcChkSilence.IsChecked == true;
            AudioAnalyzer.EnableSilenceDetection = ThemeManager.SilenceDetectionEnabled;

            ThemeManager.FakeStereoDetectionEnabled = FcChkFakeStereo.IsChecked == true;
            AudioAnalyzer.EnableFakeStereoDetection = ThemeManager.FakeStereoDetectionEnabled;

            ThemeManager.DynamicRangeEnabled = FcChkDR.IsChecked == true;
            AudioAnalyzer.EnableDynamicRange = ThemeManager.DynamicRangeEnabled;

            ThemeManager.TruePeakEnabled = FcChkTruePeak.IsChecked == true;
            AudioAnalyzer.EnableTruePeak = ThemeManager.TruePeakEnabled;

            ThemeManager.LufsEnabled = FcChkLufs.IsChecked == true;
            AudioAnalyzer.EnableLufs = ThemeManager.LufsEnabled;

            ThemeManager.ClippingDetectionEnabled = FcChkClipping.IsChecked == true;
            AudioAnalyzer.EnableClippingDetection = ThemeManager.ClippingDetectionEnabled;

            ThemeManager.MqaDetectionEnabled = FcChkMqa.IsChecked == true;
            AudioAnalyzer.EnableMqaDetection = ThemeManager.MqaDetectionEnabled;

            ThemeManager.BpmDetectionEnabled = FcChkBpm.IsChecked == true;
            AudioAnalyzer.EnableBpmDetection = ThemeManager.BpmDetectionEnabled;

            // AI detection choices
            ThemeManager.DefaultAiDetectionEnabled = FcChkDefaultAi.IsChecked == true;
            AudioAnalyzer.EnableDefaultAiDetection = ThemeManager.DefaultAiDetectionEnabled;

            ThemeManager.ExperimentalAiDetection = FcChkExperimentalAi.IsChecked == true;
            AudioAnalyzer.EnableExperimentalAi = ThemeManager.ExperimentalAiDetection;

            // Persist Rip Quality opt-in
            ThemeManager.RipQualityEnabled = FcChkRipQuality.IsChecked == true;
            AudioAnalyzer.EnableRipQuality = ThemeManager.RipQualityEnabled;

            bool wantsSHLabs = FcChkSHLabs.IsChecked == true;

            // Mark this version as configured
            string currentVersion = System.Reflection.Assembly.GetExecutingAssembly()
                .GetName().Version is { } v ? $"{v.Major}.{v.Minor}.{v.Build}" : "0.0.0";
            ThemeManager.FeatureConfigVersion = currentVersion;
            ThemeManager.SyncHiddenColumnsWithAnalysisOptions();
            ThemeManager.SavePlayOptions();

            // Show/hide columns based on user choices
            ApplyColumnVisibility();

            HideFeatureConfigOverlay();

            // If SH Labs was just enabled and privacy not yet accepted, show privacy notice
            if (wantsSHLabs && !ThemeManager.SHLabsPrivacyAccepted)
            {
                ShowSHLabsPrivacyOverlay();
                return;
            }

            ThemeManager.SHLabsAiDetection = wantsSHLabs;
            ThemeManager.SavePlayOptions();
        }

        private void FeatureConfigBackdrop_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            // Clicking outside does nothing — user must press Save
        }

        private void FcChkSHLabs_Checked(object sender, RoutedEventArgs e)
        {
            // When user checks SH Labs on the feature config screen, show privacy notice
            // if they haven't already accepted it. If they decline, uncheck the box.
            if (!ThemeManager.SHLabsPrivacyAccepted)
            {
                _shLabsPrivacyFromFeatureConfig = true;
                ShowSHLabsPrivacyOverlay();
            }
        }

        // ═══════════════════════════════════════════
        //  SH Labs Privacy Notice Overlay
        // ═══════════════════════════════════════════

        private void ShowSHLabsPrivacyOverlay()
        {
            SHLabsPrivacyOverlay.Visibility = Visibility.Visible;
            MainContent.Effect = new System.Windows.Media.Effects.BlurEffect { Radius = 6 };
        }

        private void HideSHLabsPrivacyOverlay()
        {
            SHLabsPrivacyOverlay.Visibility = Visibility.Collapsed;
            // Only clear blur if feature config overlay is NOT still showing
            if (FeatureConfigOverlay.Visibility != Visibility.Visible)
                MainContent.Effect = null;
        }

        private void SHLabsPrivacyAccept_Click(object sender, RoutedEventArgs e)
        {
            ThemeManager.SHLabsPrivacyAccepted = true;
            ThemeManager.SHLabsAiDetection = true;
            ThemeManager.SavePlayOptions();
            HideSHLabsPrivacyOverlay();
            _shLabsPrivacyFromFeatureConfig = false;
        }

        private void SHLabsPrivacyDecline_Click(object sender, RoutedEventArgs e)
        {
            // User declined — SH Labs stays off
            ThemeManager.SHLabsAiDetection = false;
            ThemeManager.SavePlayOptions();
            HideSHLabsPrivacyOverlay();
            // If triggered from the feature config checkbox, uncheck it
            if (_shLabsPrivacyFromFeatureConfig)
            {
                FcChkSHLabs.IsChecked = false;
                _shLabsPrivacyFromFeatureConfig = false;
            }
        }

        private void SHLabsPrivacyBackdrop_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            // Clicking outside = decline
            SHLabsPrivacyDecline_Click(sender, e);
        }

        /// <summary>
        /// Called from SettingsWindow when user enables SH Labs and needs privacy confirmation.
        /// </summary>
        public void RequestSHLabsPrivacyFromSettings()
        {
            ShowSHLabsPrivacyOverlay();
        }

        // ═══════════════════════════════════════════
        //  SH Labs Scan Limit Overlay
        // ═══════════════════════════════════════════

        private Task<bool> ShowSHLabsLimitOverlayAsync(string message, bool showCancel)
        {
            _shLabsLimitTcs = new TaskCompletionSource<bool>();
            SHLabsLimitMessage.Text = message;
            SHLabsLimitCancelBtn.Visibility = showCancel ? Visibility.Visible : Visibility.Collapsed;
            SHLabsLimitOverlay.Visibility = Visibility.Visible;
            return _shLabsLimitTcs.Task;
        }

        private void HideSHLabsLimitOverlay(bool result)
        {
            SHLabsLimitOverlay.Visibility = Visibility.Collapsed;
            _shLabsLimitTcs?.TrySetResult(result);
        }

        private void SHLabsLimitOk_Click(object sender, RoutedEventArgs e)
            => HideSHLabsLimitOverlay(true);

        private void SHLabsLimitCancel_Click(object sender, RoutedEventArgs e)
            => HideSHLabsLimitOverlay(false);

        private void SHLabsLimitBackdrop_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
            => HideSHLabsLimitOverlay(false);

        // ═══════════════════════════════════════════
        //  Footer Support Link
        // ═══════════════════════════════════════════

        private void InitializeFooterSupport()
        {
            if (ThemeManager.FooterSupportDismissed)
                FooterSupportText.Visibility = Visibility.Collapsed;
        }

        private void FooterSupport_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            try
            {
                Process.Start(new ProcessStartInfo("https://ko-fi.com/angelsoftware") { UseShellExecute = true });
            }
            catch { }
        }

        private void FooterSupport_Dismiss(object sender, RoutedEventArgs e)
        {
            FooterSupportText.Visibility = Visibility.Collapsed;
            ThemeManager.FooterSupportDismissed = true;
            ThemeManager.SetRegistryFlag("FooterSupportDismissed", true);
            ThemeManager.SavePlayOptions();
        }
    }
}
