using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using AudioQualityChecker.Models;

namespace AudioQualityChecker.Services
{
    // Save/load of user-visible play options (options.txt). Extracted verbatim
    // from ThemeManager.cs as part of the 2026-06-05 large-file split.
    // NOTE: LoadPlayOptions is invoked by reflection in ThemeManagerPersistenceTests
    // (BindingFlags.NonPublic | Static) — do not rename or change its signature.
    public static partial class ThemeManager
    {
        // Serializes the options-file write so it can be called from a background thread (e.g. the
        // off-hot-path stats save on track transitions) without two writers tearing the file.
        private static readonly object _savePlayOptionsLock = new();

        /// <summary>
        /// Splits one built "Key=Value" line for <see cref="OptionsFileStore.Merge"/>. Splits on the
        /// FIRST '=' only — values legitimately contain more of them (service URLs, ColumnLayout).
        /// </summary>
        private static KeyValuePair<string, string?> SplitOptionLine(string line)
        {
            int eq = line.IndexOf('=');
            return eq <= 0
                ? new KeyValuePair<string, string?>(line, "")
                : new KeyValuePair<string, string?>(line[..eq], line[(eq + 1)..]);
        }

        // ─── Debounced save ───
        //
        // SavePlayOptions is expensive: it builds ~300 interpolated strings, then Merge does a full
        // read-modify-write of options.txt via a temp file + File.Move, then the DPAPI block
        // encrypts and writes a second file. Continuous controls (volume, EQ, crossfade and the Now
        // Playing sliders) used to call it on EVERY ValueChanged, i.e. once per pixel of drag, all
        // synchronously on the UI thread. Those callers now go through SavePlayOptionsDebounced and
        // the real write happens once the user stops moving.
        //
        // FlushPendingPlayOptions() runs on app exit / main-window close so a drag followed by an
        // immediate quit still persists. One-shot controls (checkboxes, pickers) keep calling
        // SavePlayOptions directly — there is nothing to coalesce there.
        private const int SaveDebounceMs = 500;
        private static System.Threading.Timer? _saveDebounceTimer;
        private static readonly object _saveDebounceLock = new();
        private static bool _savePending;

        /// <summary>
        /// Bumped every time a settings write is requested. MainWindow pre-builds a SettingsWindow
        /// ahead of the user clicking Settings, and that window loads its entire control state in
        /// its constructor — so a pre-built instance goes stale the moment a setting changes from
        /// anywhere else (Now Playing toggles, the feature-config overlay, column hide/show).
        /// Comparing this counter tells the caller whether its pre-built window is still good.
        /// Anything that persists a setting goes through here, so it stays correct by default.
        /// </summary>
        public static int SettingsRevision => System.Threading.Volatile.Read(ref _settingsRevision);
        private static int _settingsRevision;

        private static void BumpSettingsRevision() =>
            System.Threading.Interlocked.Increment(ref _settingsRevision);

        /// <summary>
        /// Requests a settings save once the caller stops changing values. Safe to call at slider
        /// tick rate. See <see cref="FlushPendingPlayOptions"/> for the shutdown flush.
        /// </summary>
        public static void SavePlayOptionsDebounced()
        {
            // Bump on the *request*, not on the eventual write — the value has already changed in
            // memory, so anything caching a view of the settings is stale from this moment.
            BumpSettingsRevision();
            lock (_saveDebounceLock)
            {
                _savePending = true;
                if (_saveDebounceTimer == null)
                {
                    _saveDebounceTimer = new System.Threading.Timer(_ => FlushPendingPlayOptions(),
                        null, SaveDebounceMs, System.Threading.Timeout.Infinite);
                }
                else
                {
                    _saveDebounceTimer.Change(SaveDebounceMs, System.Threading.Timeout.Infinite);
                }
            }
        }

        /// <summary>
        /// Writes immediately if a debounced save is still pending; otherwise a no-op. Call on app
        /// exit and main-window close so nothing is lost between the last tick and shutdown.
        /// </summary>
        public static void FlushPendingPlayOptions()
        {
            lock (_saveDebounceLock)
            {
                if (!_savePending) return;
                _savePending = false;
                _saveDebounceTimer?.Change(System.Threading.Timeout.Infinite, System.Threading.Timeout.Infinite);
            }
            SavePlayOptions();
        }

        public static void SavePlayOptions()
        {
            BumpSettingsRevision();
            bool credentialsSaved = SaveSensitiveData();
            try
            {
                EnsureDir();
                var lines = new List<string>
                {
                    $"AutoPlayNext={AutoPlayNext}",
                    $"AudioNormalization={AudioNormalization}",
                    $"Crossfade={Crossfade}",
                    $"CrossfadeDuration={CrossfadeDuration}",
                    $"CrossfadeCurve={CrossfadeCurve}",
                    $"CrossfadeOnManualSkip={CrossfadeOnManualSkip}",
                    $"GaplessEnabled={GaplessEnabled}",
                    $"PlaybarTheme={(IsPlaybarFollowingTheme ? "" : _currentPlaybarTheme)}",
                    $"MainPlaybarAnimationStyle={MainPlaybarAnimationStyle}",
                    $"NpPlaybarAnimationStyle={NpPlaybarAnimationStyle}",
                    $"Service1={MusicServiceSlots[0]}",
                    $"Service2={MusicServiceSlots[1]}",
                    $"Service3={MusicServiceSlots[2]}",
                    $"Service4={MusicServiceSlots[3]}",
                    $"Service5={MusicServiceSlots[4]}",
                    $"Service6={MusicServiceSlots[5]}",
                    $"ServiceVisible1={MusicServiceSlotVisible[0]}",
                    $"ServiceVisible2={MusicServiceSlotVisible[1]}",
                    $"ServiceVisible3={MusicServiceSlotVisible[2]}",
                    $"ServiceVisible4={MusicServiceSlotVisible[3]}",
                    $"ServiceVisible5={MusicServiceSlotVisible[4]}",
                    $"ServiceVisible6={MusicServiceSlotVisible[5]}",
                    $"VisualizerMode={VisualizerMode}",
                    $"SpectrogramLinearScale={SpectrogramLinearScale}",
                    $"SpectrogramDifferenceChannel={SpectrogramDifferenceChannel}",
                    $"RainbowVisualizer={RainbowVisualizerEnabled}",
                    $"VisualizerStyle={VisualizerStyle}",
                    $"VisualizerCycleSpeed={VisualizerCycleSpeed}",
                    $"VisualizerCycleList={VisualizerCycleList}",
                    $"VisualizerTheme={_currentVisualizerTheme}",
                    $"CustomUrl1={CustomServiceUrls[0]}",
                    $"CustomIcon1={CustomServiceIcons[0]}",
                    $"CustomUrl2={CustomServiceUrls[1]}",
                    $"CustomIcon2={CustomServiceIcons[1]}",
                    $"CustomUrl3={CustomServiceUrls[2]}",
                    $"CustomIcon3={CustomServiceIcons[2]}",
                    $"CustomUrl4={CustomServiceUrls[3]}",
                    $"CustomIcon4={CustomServiceIcons[3]}",
                    $"CustomUrl5={CustomServiceUrls[4]}",
                    $"CustomIcon5={CustomServiceIcons[4]}",
                    $"CustomUrl6={CustomServiceUrls[5]}",
                    $"CustomIcon6={CustomServiceIcons[5]}",
                    $"NpSearchServicesConfigured={NpSearchServicesConfigured}",
                    $"NpSearchService1={NpSearchServiceSlots[0]}",
                    $"NpSearchService2={NpSearchServiceSlots[1]}",
                    $"NpSearchService3={NpSearchServiceSlots[2]}",
                    $"NpSearchService4={NpSearchServiceSlots[3]}",
                    $"NpSearchService5={NpSearchServiceSlots[4]}",
                    $"NpSearchService6={NpSearchServiceSlots[5]}",
                    $"NpSearchServiceVisible1={NpSearchServiceSlotVisible[0]}",
                    $"NpSearchServiceVisible2={NpSearchServiceSlotVisible[1]}",
                    $"NpSearchServiceVisible3={NpSearchServiceSlotVisible[2]}",
                    $"NpSearchServiceVisible4={NpSearchServiceSlotVisible[3]}",
                    $"NpSearchServiceVisible5={NpSearchServiceSlotVisible[4]}",
                    $"NpSearchServiceVisible6={NpSearchServiceSlotVisible[5]}",
                    $"NpSearchCustomUrl1={NpSearchCustomServiceUrls[0]}",
                    $"NpSearchCustomIcon1={NpSearchCustomServiceIcons[0]}",
                    $"NpSearchCustomUrl2={NpSearchCustomServiceUrls[1]}",
                    $"NpSearchCustomIcon2={NpSearchCustomServiceIcons[1]}",
                    $"NpSearchCustomUrl3={NpSearchCustomServiceUrls[2]}",
                    $"NpSearchCustomIcon3={NpSearchCustomServiceIcons[2]}",
                    $"NpSearchCustomUrl4={NpSearchCustomServiceUrls[3]}",
                    $"NpSearchCustomIcon4={NpSearchCustomServiceIcons[3]}",
                    $"NpSearchCustomUrl5={NpSearchCustomServiceUrls[4]}",
                    $"NpSearchCustomIcon5={NpSearchCustomServiceIcons[4]}",
                    $"NpSearchCustomUrl6={NpSearchCustomServiceUrls[5]}",
                    $"NpSearchCustomIcon6={NpSearchCustomServiceIcons[5]}",
                    $"EqualizerEnabled={EqualizerEnabled}",
                    $"EqualizerGains={string.Join(";", EqualizerGains.Select(g => g.ToString("F1", System.Globalization.CultureInfo.InvariantCulture)))}",
                    $"DiscordRpc={DiscordRpcEnabled}",
                    $"DiscordRpcDisplayMode={DiscordRpcDisplayMode}",
                    $"DiscordRpcShowElapsed={DiscordRpcShowElapsed}",
                    $"LastFmEnabled={LastFmEnabled}",
                    $"ExportFormat={ExportFormat}",
                    $"SpatialAudio={SpatialAudioEnabled}",
                    $"ExperimentalAiDetection={ExperimentalAiDetection}",
                    $"RipLogCheckEnabled={RipLogCheckEnabled}",
                    $"SilenceDetectionEnabled={SilenceDetectionEnabled}",
                    $"FakeStereoDetectionEnabled={FakeStereoDetectionEnabled}",
                    $"DynamicRangeEnabled={DynamicRangeEnabled}",
                    $"TruePeakEnabled={TruePeakEnabled}",
                    $"LufsEnabled={LufsEnabled}",
                    $"ClippingDetectionEnabled={ClippingDetectionEnabled}",
                    $"MqaDetectionEnabled={MqaDetectionEnabled}",
                    $"DefaultAiDetectionEnabled={DefaultAiDetectionEnabled}",
                    $"BpmDetectionEnabled={BpmDetectionEnabled}",
                    $"ScanPerformanceDefaultsVersion={ScanPerformanceDefaultsVersion}",
                    $"SHLabsAiDetection={SHLabsAiDetection}",
                    $"SHLabsPrivacyAccepted={SHLabsPrivacyAccepted}",
                    $"AiConfigDismissed={AiConfigDismissed}",
                    $"FeatureConfigVersion={FeatureConfigVersion}",
                    $"VisualizerFullVolume={VisualizerFullVolume}",
                    $"Volume={Volume.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)}",
                    $"ColumnLayout={ColumnLayout}",
                    $"HiddenColumns={HiddenColumns}",
                    $"ShowFavoritesColumn={ShowFavoritesColumn}",
                    $"UserShownColumns={FormatUserShownColumns()}",
                    $"MaxConcurrency={_maxConcurrency}",
                    $"MaxMemoryMB={_maxMemoryMB}",
                    $"DonationDismissed={DonationDismissed}",
                    $"Donation30DayShown={Donation30DayShown}",
                    $"FeedbackOneHourShown={FeedbackOneHourShown}",
                    $"FeedbackActiveUsageSeconds={FeedbackActiveUsageSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture)}",
                    $"FirstScanDate={FirstScanDate:O}",
                    $"TotalFilesScannedLifetime={TotalFilesScannedLifetime}",
                    $"TotalListeningSecondsLifetime={TotalListeningSecondsLifetime.ToString(System.Globalization.CultureInfo.InvariantCulture)}",
                    $"FooterSupportDismissed={FooterSupportDismissed}",
                    $"CloseToTray={CloseToTray}",
                    $"PreloadNextTrackEnabled={PreloadNextTrackEnabled}",
                    $"CheckForUpdates={CheckForUpdates}",
                    $"AnimationsEnabled={AnimationsEnabled}",
                    $"BatterySaverEnabled={BatterySaverEnabled}",
                    $"BatterySaverKeepVisualizer={BatterySaverKeepVisualizer}",
                    $"GpuRenderMode={GpuRenderMode}",
                    $"ScanCacheEnabled={ScanCacheEnabled}",
                    $"RestoreLastSessionEnabled={RestoreLastSessionEnabled}",
                    $"RestoreSessionCacheNoticeShown={RestoreSessionCacheNoticeShown}",
                    $"FocusNewlyAddedFilesEnabled={FocusNewlyAddedFilesEnabled}",
                    $"SilenceMinGapEnabled={SilenceMinGapEnabled}",
                    $"SilenceMinGapSeconds={SilenceMinGapSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture)}",
                    $"SilenceSkipEdgesEnabled={SilenceSkipEdgesEnabled}",
                    $"SilenceSkipEdgeSeconds={SilenceSkipEdgeSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture)}",
                    $"CrashLoggingEnabled={CrashLoggingEnabled}",
                    $"StatsCollectionEnabled={StatsCollectionEnabled}",
                    $"AlwaysFullAnalysis={AlwaysFullAnalysis}",
                    $"SpectrogramHiFiMode={SpectrogramHiFiMode}",
                    $"SpectrogramMagmaColormap={SpectrogramMagmaColormap}",
                    $"FrequencyCutoffAllowEnabled={FrequencyCutoffAllowEnabled}",
                    $"FrequencyCutoffAllowHz={FrequencyCutoffAllowHz}",
                    $"StreamingRegion={StreamingRegion}",
                    $"LoopMode={LoopMode}",
                    $"RenamePatternIndex={RenamePatternIndex}",
                    $"SmartRenameStyleIndex={SmartRenameStyleIndex}",
                    $"SmartRenameFolderIndex={SmartRenameFolderIndex}",
                    $"SmartRenameIncludeTrackNumbers={SmartRenameIncludeTrackNumbers}",
                    $"SmartRenameAppendDuplicateNumbers={SmartRenameAppendDuplicateNumbers}",
                    $"SmartRenameRenameCleanFiles={SmartRenameRenameCleanFiles}",
                    $"SmartRenameNameCaseIndex={SmartRenameNameCaseIndex}",
                    $"SmartRenameSpaceModeIndex={SmartRenameSpaceModeIndex}",
                    $"SmartRenameStripFeaturing={SmartRenameStripFeaturing}",
                    $"StreamingLinkPlatformIndex={StreamingLinkPlatformIndex}",
                    $"DefaultCopyFolder={DefaultCopyFolder}",
                    $"DefaultMoveFolder={DefaultMoveFolder}",
                    $"DefaultPlaylistFolder={DefaultPlaylistFolder}",
                    $"MainColorMatchEnabled={MainColorMatchEnabled}",
                    $"MainColorMatchTargets={MainColorMatchTargets}",
                    $"AppFontFamily={AppFontFamily}",
                    $"WelcomeVersionSeen={WelcomeVersionSeen}",
                    $"OfflineModeEnabled={OfflineModeEnabled}",
                    $"LyricsAvoidCensored={LyricsAvoidCensored}",
                    $"LibreFmEnabled={LibreFmEnabled}",
                    $"ListenBrainzEnabled={ListenBrainzEnabled}",
                    $"MalojaEnabled={MalojaEnabled}",
                    $"SystemMediaControlsEnabled={SystemMediaControlsEnabled}",
                    $"PauseScrobbling={PauseScrobbling}",
                    $"ScrobbleAtPercent={ScrobbleAtPercent}",
                    $"ScrobbleAtSeconds={ScrobbleAtSeconds}",
                    $"MinScrobbleTrackSeconds={MinScrobbleTrackSeconds}",
                    $"ScrobbleBlacklist={ScrobbleBlacklist}",
                    $"LastSettingsTab={LastSettingsTab}"
                };
                // Merge rather than rewrite. A whole-file write deletes every key this build does
                // not know about, so an older release — or a future one, after a rollback — silently
                // drops the other's settings. Merge also writes atomically via a temp file, so an
                // interrupted save cannot truncate options.txt.
                lines.AddRange(NowPlayingSettings.SaveLines());
                IEnumerable<KeyValuePair<string, string?>> optionUpdates = lines.Select(SplitOptionLine);
                if (credentialsSaved)
                    optionUpdates = optionUpdates.Concat(CredentialStore.Keys.Select(key =>
                        new KeyValuePair<string, string?>(key, null)));
                lock (_savePlayOptionsLock)
                    OptionsFileStore.Merge(OptionsFile, optionUpdates);
            }
            catch (Exception ex)
            {
                // A silent failure here is exactly the "doesn't save settings" report.
                if (CrashLoggingEnabled) LocalCrashLogger.Write(ex);
            }

        }

        private static bool SaveSensitiveData()
        {
            try
            {
                var sensitiveDir = Path.GetDirectoryName(SensitiveFile)!;
                if (!Directory.Exists(sensitiveDir))
                    Directory.CreateDirectory(sensitiveDir);

                var sensitiveLines = new List<string>
                {
                    $"LastFmApiKey={LastFmApiKey}",
                    $"LastFmApiSecret={LastFmApiSecret}",
                    $"LastFmSessionKey={LastFmSessionKey}",
                    $"LastFmUsername={LastFmUsername}",
                    $"LibreFmApiKey={LibreFmApiKey}",
                    $"LibreFmApiSecret={LibreFmApiSecret}",
                    $"LibreFmSessionKey={LibreFmSessionKey}",
                    $"LibreFmUsername={LibreFmUsername}",
                    $"ListenBrainzUserToken={ListenBrainzUserToken}",
                    $"ListenBrainzUsername={ListenBrainzUsername}",
                    $"MalojaServerUrl={MalojaServerUrl}",
                    $"MalojaApiKey={MalojaApiKey}",
                    $"MalojaUsername={MalojaUsername}",
                    $"DiscordRpcClientId={DiscordRpcClientId}",
                    $"AcoustIdApiKey={AcoustIdApiKey}",
                    $"DiscogsToken={DiscogsToken}",
                    $"FanartTvApiKey={FanartTvApiKey}",
                    $"SpotifyClientId={SpotifyClientId}",
                    $"SpotifyClientSecret={SpotifyClientSecret}",
                    $"YouTubeApiKey={YouTubeApiKey}",
                    $"SHLabsCustomApiKey={SHLabsCustomApiKey}"
                };
                var plaintext = System.Text.Encoding.UTF8.GetBytes(string.Join("\n", sensitiveLines));
                var encrypted = System.Security.Cryptography.ProtectedData.Protect(
                    plaintext, null, System.Security.Cryptography.DataProtectionScope.CurrentUser);
                string temp = SensitiveFile + "." + Environment.ProcessId + "."
                            + Guid.NewGuid().ToString("N")[..8] + ".tmp";
                try
                {
                    File.WriteAllBytes(temp, encrypted);
                    File.Move(temp, SensitiveFile, overwrite: true);
                }
                catch
                {
                    try { File.Delete(temp); } catch { }
                    throw;
                }
                return true;
            }
            catch (Exception ex)
            {
                // Same reasoning as the options write above: this block holds every scrobbler
                // session key and API token, so swallowing a DPAPI or disk failure silently drops
                // all of them and the user just sees "it forgot my Last.fm login" with no trace.
                if (CrashLoggingEnabled) LocalCrashLogger.Write(ex);
                return false;
            }
        }

        private static void LoadPlayOptions()
        {
            // Set fixed defaults
            MusicServiceSlots[0] = "Spotify";
            MusicServiceSlots[1] = "YouTube Music";
            MusicServiceSlots[2] = "Tidal";
            MusicServiceSlots[3] = "Qobuz";
            MusicServiceSlots[4] = "Amazon Music";
            MusicServiceSlots[5] = "Apple Music";

            // Legacy Battery Saver per-area mode, read only so the one surviving choice can be
            // migrated after the loop. Both default to the old property defaults.
            bool legacyBatteryEntireProgram = true;
            bool legacyBatteryVisualizer = true;

            var npSeen = new NowPlayingSettings.LayoutSeen();

            try
            {
                if (!File.Exists(OptionsFile))
                {
                    SeedNpSearchServicesFromMain(); // first run: NP search mirrors the default main slots
                    LoadSensitiveData();
                    SyncHiddenColumnsWithAnalysisOptions(applyDefaultHiddenColumns: true);
                    ApplyScanPerformanceDefaultsMigration();
                    return;
                }
                foreach (var line in File.ReadAllLines(OptionsFile))
                {
                    var parts = line.Split('=', 2);
                    if (parts.Length != 2) continue;
                    string key = parts[0], val = parts[1];

                    switch (key)
                    {
                        case "AutoPlayNext": AutoPlayNext = !bool.TryParse(val, out var b1) || b1; break; // default true
                        case "AudioNormalization": AudioNormalization = bool.TryParse(val, out var b2) && b2; break;
                        case "Crossfade": Crossfade = bool.TryParse(val, out var b3) && b3; break;
                        case "GaplessEnabled": GaplessEnabled = bool.TryParse(val, out var bGap) && bGap; break;
                        case "CrossfadeDuration":
                            if (int.TryParse(val, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var dur) && dur >= 1 && dur <= 30)
                                CrossfadeDuration = dur;
                            break;
                        case "CrossfadeCurve":
                            if (Enum.TryParse<CrossfadeType>(val, out var curveType))
                                CrossfadeCurve = curveType;
                            break;
                        case "PlaybarTheme":
                            if (val == "" || AvailablePlaybarThemes.Contains(val))
                            {
                                _currentPlaybarTheme = val == "Follow Theme" ? "" : val;
                            }
                            break;
                        case "MainPlaybarAnimationStyle":
                            if (Enum.TryParse<PlaybarAnimationStyle>(val, true, out var mainPlaybarStyle))
                                MainPlaybarAnimationStyle = mainPlaybarStyle;
                            // "Wavey" was removed in v1.8.0; fall back to Regular if stored value
                            // no longer parses (TryParse returns false for unknown names).
                            break;
                        case "NpPlaybarAnimationStyle":
                            if (Enum.TryParse<PlaybarAnimationStyle>(val, true, out var npPlaybarStyle))
                                NpPlaybarAnimationStyle = npPlaybarStyle;
                            // "Wavey" was removed in v1.8.0; fall back to Regular if stored value
                            // no longer parses (TryParse returns false for unknown names).
                            break;

                        case "Service1": if (AvailableMusicServices.Contains(val)) MusicServiceSlots[0] = val; break;
                        case "Service2": if (AvailableMusicServices.Contains(val)) MusicServiceSlots[1] = val; break;
                        case "Service3": if (AvailableMusicServices.Contains(val)) MusicServiceSlots[2] = val; break;
                        case "Service4": if (AvailableMusicServices.Contains(val)) MusicServiceSlots[3] = val; break;
                        case "Service5": if (AvailableMusicServices.Contains(val)) MusicServiceSlots[4] = val; break;
                        case "Service6": if (AvailableMusicServices.Contains(val)) MusicServiceSlots[5] = val; break;
                        case "ServiceVisible1": MusicServiceSlotVisible[0] = bool.TryParse(val, out var sv1) && sv1; break;
                        case "ServiceVisible2": MusicServiceSlotVisible[1] = bool.TryParse(val, out var sv2) && sv2; break;
                        case "ServiceVisible3": MusicServiceSlotVisible[2] = bool.TryParse(val, out var sv3) && sv3; break;
                        case "ServiceVisible4": MusicServiceSlotVisible[3] = bool.TryParse(val, out var sv4) && sv4; break;
                        case "ServiceVisible5": MusicServiceSlotVisible[4] = bool.TryParse(val, out var sv5) && sv5; break;
                        case "ServiceVisible6": MusicServiceSlotVisible[5] = bool.TryParse(val, out var sv6) && sv6; break;
                        case "VisualizerMode": VisualizerMode = bool.TryParse(val, out var bv) && bv; break;
                        case "SpectrogramLinearScale": SpectrogramLinearScale = bool.TryParse(val, out var bsl) && bsl; break;
                        case "SpectrogramDifferenceChannel": SpectrogramDifferenceChannel = bool.TryParse(val, out var bsd) && bsd; break;
                        case "RainbowVisualizer": RainbowVisualizerEnabled = bool.TryParse(val, out var brv) && brv; break;
                        case "VisualizerStyle":
                            if (int.TryParse(val, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var vs) && vs >= 0 && vs <= 5)
                            {
                                // Migrate old Abstract style (index 5 was removed; 5 is now VU Meter)
                                // Old index 5 (Abstract) → 0 (Bars), old 6 (VU) → 5 (VU)
                                VisualizerStyle = vs == 5 ? 0 : vs;
                            }
                            break;
                        case "VisualizerCycleSpeed":
                            if (int.TryParse(val, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var vcs) && vcs >= 5 && vcs <= 60) VisualizerCycleSpeed = vcs;
                            break;
                        case "VisualizerCycleList":
                            VisualizerCycleList = val;
                            break;
                        case "VisualizerTheme":
                            if (AvailableVisualizerThemes.Contains(val) || GetThemeDefinition(val) != null)
                                _currentVisualizerTheme = val;
                            else
                                _currentVisualizerTheme = ""; // follow playbar
                            break;
                        case "CustomUrl1": CustomServiceUrls[0] = val; break;
                        case "CustomIcon1": CustomServiceIcons[0] = val; break;
                        case "CustomUrl2": CustomServiceUrls[1] = val; break;
                        case "CustomIcon2": CustomServiceIcons[1] = val; break;
                        case "CustomUrl3": CustomServiceUrls[2] = val; break;
                        case "CustomIcon3": CustomServiceIcons[2] = val; break;
                        case "CustomUrl4": CustomServiceUrls[3] = val; break;
                        case "CustomIcon4": CustomServiceIcons[3] = val; break;
                        case "CustomUrl5": CustomServiceUrls[4] = val; break;
                        case "CustomIcon5": CustomServiceIcons[4] = val; break;
                        case "CustomUrl6": CustomServiceUrls[5] = val; break;
                        case "CustomIcon6": CustomServiceIcons[5] = val; break;
                        // NP "look up this song" services (independent of the main slots above)
                        case "NpSearchServicesConfigured": NpSearchServicesConfigured = bool.TryParse(val, out var nssc) && nssc; break;
                        case "NpSearchService1": if (AvailableMusicServices.Contains(val)) NpSearchServiceSlots[0] = val; break;
                        case "NpSearchService2": if (AvailableMusicServices.Contains(val)) NpSearchServiceSlots[1] = val; break;
                        case "NpSearchService3": if (AvailableMusicServices.Contains(val)) NpSearchServiceSlots[2] = val; break;
                        case "NpSearchService4": if (AvailableMusicServices.Contains(val)) NpSearchServiceSlots[3] = val; break;
                        case "NpSearchService5": if (AvailableMusicServices.Contains(val)) NpSearchServiceSlots[4] = val; break;
                        case "NpSearchService6": if (AvailableMusicServices.Contains(val)) NpSearchServiceSlots[5] = val; break;
                        case "NpSearchServiceVisible1": NpSearchServiceSlotVisible[0] = bool.TryParse(val, out var nsv1) && nsv1; break;
                        case "NpSearchServiceVisible2": NpSearchServiceSlotVisible[1] = bool.TryParse(val, out var nsv2) && nsv2; break;
                        case "NpSearchServiceVisible3": NpSearchServiceSlotVisible[2] = bool.TryParse(val, out var nsv3) && nsv3; break;
                        case "NpSearchServiceVisible4": NpSearchServiceSlotVisible[3] = bool.TryParse(val, out var nsv4) && nsv4; break;
                        case "NpSearchServiceVisible5": NpSearchServiceSlotVisible[4] = bool.TryParse(val, out var nsv5) && nsv5; break;
                        case "NpSearchServiceVisible6": NpSearchServiceSlotVisible[5] = bool.TryParse(val, out var nsv6) && nsv6; break;
                        case "NpSearchCustomUrl1": NpSearchCustomServiceUrls[0] = val; break;
                        case "NpSearchCustomIcon1": NpSearchCustomServiceIcons[0] = val; break;
                        case "NpSearchCustomUrl2": NpSearchCustomServiceUrls[1] = val; break;
                        case "NpSearchCustomIcon2": NpSearchCustomServiceIcons[1] = val; break;
                        case "NpSearchCustomUrl3": NpSearchCustomServiceUrls[2] = val; break;
                        case "NpSearchCustomIcon3": NpSearchCustomServiceIcons[2] = val; break;
                        case "NpSearchCustomUrl4": NpSearchCustomServiceUrls[3] = val; break;
                        case "NpSearchCustomIcon4": NpSearchCustomServiceIcons[3] = val; break;
                        case "NpSearchCustomUrl5": NpSearchCustomServiceUrls[4] = val; break;
                        case "NpSearchCustomIcon5": NpSearchCustomServiceIcons[4] = val; break;
                        case "NpSearchCustomUrl6": NpSearchCustomServiceUrls[5] = val; break;
                        case "NpSearchCustomIcon6": NpSearchCustomServiceIcons[5] = val; break;
                        // Legacy keys (migrate old Custom1/Custom2 to slot 4/5)
                        case "Custom1Url": if (string.IsNullOrEmpty(CustomServiceUrls[4])) CustomServiceUrls[4] = val; break;
                        case "Custom1Icon": if (string.IsNullOrEmpty(CustomServiceIcons[4])) CustomServiceIcons[4] = val; break;
                        case "Custom2Url": if (string.IsNullOrEmpty(CustomServiceUrls[5])) CustomServiceUrls[5] = val; break;
                        case "Custom2Icon": if (string.IsNullOrEmpty(CustomServiceIcons[5])) CustomServiceIcons[5] = val; break;
                        case "EqualizerEnabled": EqualizerEnabled = bool.TryParse(val, out var beq) && beq; break;
                        case "EqualizerGains":
                            var parts2 = val.Split(';');
                            for (int i = 0; i < Math.Min(parts2.Length, 10); i++)
                                if (float.TryParse(parts2[i], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var g)) EqualizerGains[i] = g;
                            break;
                        case "DiscordRpc": DiscordRpcEnabled = bool.TryParse(val, out var bdr) && bdr; break;
                        case "DiscordRpcDisplayMode":
                            if (new[] { "TrackDetails", "FileName" }.Contains(val))
                                DiscordRpcDisplayMode = val;
                            break;
                        case "DiscordRpcShowElapsed": DiscordRpcShowElapsed = !(bool.TryParse(val, out var bde) && !bde); break;
                        case "LastFmEnabled": LastFmEnabled = bool.TryParse(val, out var blf) && blf; break;
                        case "LibreFmEnabled": LibreFmEnabled = bool.TryParse(val, out var blibre) && blibre; break;
                        case "ListenBrainzEnabled": ListenBrainzEnabled = bool.TryParse(val, out var blbz) && blbz; break;
                        case "MalojaEnabled": MalojaEnabled = bool.TryParse(val, out var bmlj) && bmlj; break;
                        case "SystemMediaControlsEnabled": SystemMediaControlsEnabled = !(bool.TryParse(val, out var bsmtc) && !bsmtc); break;
                        case "PauseScrobbling": PauseScrobbling = bool.TryParse(val, out var bps) && bps; break;
                        case "ScrobbleAtPercent": if (int.TryParse(val, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var sap) && sap >= 0 && sap <= 100) ScrobbleAtPercent = sap; break;
                        case "ScrobbleAtSeconds": if (int.TryParse(val, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var sas) && sas >= 0 && sas <= 7200) ScrobbleAtSeconds = sas; break;
                        case "MinScrobbleTrackSeconds": if (int.TryParse(val, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var msts) && msts >= 0 && msts <= 3600) MinScrobbleTrackSeconds = msts; break;
                        case "ScrobbleBlacklist": ScrobbleBlacklist = val; break;
                        case "ExportFormat":
                            if (new[] { "csv", "txt", "pdf", "xlsx", "docx" }.Contains(val))
                                ExportFormat = val;
                            break;
                        case "SpatialAudio": SpatialAudioEnabled = bool.TryParse(val, out var bsa) && bsa; break;
                        case "ExperimentalAiDetection": ExperimentalAiDetection = bool.TryParse(val, out var bea) && bea; AudioAnalyzer.EnableExperimentalAi = ExperimentalAiDetection; break;
                        case "RipLogCheckEnabled": RipLogCheckEnabled = bool.TryParse(val, out var brq) && brq; break;
                        case "SilenceDetectionEnabled": SilenceDetectionEnabled = bool.TryParse(val, out var bSilDet) && bSilDet; AudioAnalyzer.EnableSilenceDetection = SilenceDetectionEnabled; break;
                        case "FakeStereoDetectionEnabled": FakeStereoDetectionEnabled = !(bool.TryParse(val, out var bFsDet) && !bFsDet); AudioAnalyzer.EnableFakeStereoDetection = FakeStereoDetectionEnabled; break;
                        case "DynamicRangeEnabled": DynamicRangeEnabled = bool.TryParse(val, out var bDrEn) && bDrEn; AudioAnalyzer.EnableDynamicRange = DynamicRangeEnabled; break;
                        case "TruePeakEnabled": TruePeakEnabled = bool.TryParse(val, out var bTpEn) && bTpEn; AudioAnalyzer.EnableTruePeak = TruePeakEnabled; break;
                        case "LufsEnabled": LufsEnabled = bool.TryParse(val, out var bLuEn) && bLuEn; AudioAnalyzer.EnableLufs = LufsEnabled; break;
                        case "ClippingDetectionEnabled": ClippingDetectionEnabled = !(bool.TryParse(val, out var bClEn) && !bClEn); AudioAnalyzer.EnableClippingDetection = ClippingDetectionEnabled; break;
                        case "MqaDetectionEnabled": MqaDetectionEnabled = !(bool.TryParse(val, out var bMqEn) && !bMqEn); AudioAnalyzer.EnableMqaDetection = MqaDetectionEnabled; break;
                        case "DefaultAiDetectionEnabled": DefaultAiDetectionEnabled = !(bool.TryParse(val, out var bDaEn) && !bDaEn); AudioAnalyzer.EnableDefaultAiDetection = DefaultAiDetectionEnabled; break;
                        case "BpmDetectionEnabled": BpmDetectionEnabled = bool.TryParse(val, out var bBpmEn) && bBpmEn; AudioAnalyzer.EnableBpmDetection = BpmDetectionEnabled; break;
                        case "ScanPerformanceDefaultsVersion": ScanPerformanceDefaultsVersion = val; break;
                        case "SHLabsAiDetection": SHLabsAiDetection = bool.TryParse(val, out var bsh) && bsh; break;
                        case "SHLabsPrivacyAccepted": SHLabsPrivacyAccepted = bool.TryParse(val, out var bsp) && bsp; break;
                        case "SHLabsCustomApiKey": SHLabsCustomApiKey = val; SHLabsDetectionService.CustomApiKey = val; break;
                        case "AiConfigDismissed": AiConfigDismissed = bool.TryParse(val, out var bac) && bac; break;
                        case "FeatureConfigVersion": FeatureConfigVersion = val; break;
                        case "WelcomeVersionSeen": WelcomeVersionSeen = val; break;
                        case "VisualizerFullVolume": VisualizerFullVolume = !bool.TryParse(val, out var bvfv) || bvfv; break; // default true
                        case "Volume": if (double.TryParse(val, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var bvol)) Volume = Math.Clamp(bvol, 0, 100); break;
                        case "ColumnLayout": ColumnLayout = val; break;
                        // Derive each flagless default-hidden column's shown/hidden preference
                        // from legacy files (no explicit key) by whether it was hidden; the
                        // explicit ShowFavoritesColumn / UserShownColumns lines written after
                        // this one override it.
                        case "HiddenColumns": HiddenColumns = val; DeriveUserShownColumnsFromHidden(val); break;
                        case "ShowFavoritesColumn": ShowFavoritesColumn = bool.TryParse(val, out var bsfc) && bsfc; break;
                        case "UserShownColumns": SetUserShownColumns(val); break;
                        case "MaxConcurrency":
                            if (int.TryParse(val, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var mc) && mc >= 0 && mc <= Environment.ProcessorCount)
                                _maxConcurrency = mc;
                            break;
                        case "MaxMemoryMB":
                            if (int.TryParse(val, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var mm) && mm >= 0 && mm <= (int)Math.Min(TotalSystemMemoryMB, 65536))
                                _maxMemoryMB = mm;
                            break;
                        case "DonationDismissed": DonationDismissed = bool.TryParse(val, out var bdd) && bdd; break;
                        case "Donation30DayShown": Donation30DayShown = bool.TryParse(val, out var d30) && d30; break;
                        case "FeedbackOneHourShown": FeedbackOneHourShown = bool.TryParse(val, out var f1h) && f1h; break;
                        case "FeedbackActiveUsageSeconds": if (double.TryParse(val, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var faus)) FeedbackActiveUsageSeconds = Math.Clamp(faus, 0, 3600); break;
                        case "FirstScanDate": if (DateTime.TryParse(val, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.RoundtripKind, out var fsd)) FirstScanDate = fsd; break;
                        case "TotalFilesScannedLifetime": if (int.TryParse(val, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var tfsl)) TotalFilesScannedLifetime = tfsl; break;
                        case "TotalListeningSecondsLifetime": if (double.TryParse(val, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var tlsl)) TotalListeningSecondsLifetime = tlsl; break;
                        case "FooterSupportDismissed": FooterSupportDismissed = bool.TryParse(val, out var bfs) && bfs; break;
                        case "CloseToTray": CloseToTray = bool.TryParse(val, out var bct) && bct; break;
                        case "PreloadNextTrackEnabled": PreloadNextTrackEnabled = !bool.TryParse(val, out var bpte) || bpte; break; // default true
                        case "CheckForUpdates": CheckForUpdates = !bool.TryParse(val, out var bcu) || bcu; break; // default true
                        case "AnimationsEnabled": AnimationsEnabled = !bool.TryParse(val, out var bae) || bae; break; // default true
                        case "BatterySaverEnabled": BatterySaverEnabled = bool.TryParse(val, out var bbse) && bbse; break; // default false
                        // Retired per-area Battery Saver keys. Only these two feed the migration
                        // below; NpBackground/CoverGlow/Lyrics/Playbar have no equivalent now and
                        // fall through as unknown keys.
                        case "BatterySaverEntireProgram": legacyBatteryEntireProgram = !bool.TryParse(val, out var bbsep) || bbsep; break;
                        case "BatterySaverVisualizer": legacyBatteryVisualizer = !bool.TryParse(val, out var bbsv) || bbsv; break;
                        case "BatterySaverKeepVisualizer": BatterySaverKeepVisualizer = bool.TryParse(val, out var bbskv) && bbskv; break; // default false
                        case "GpuRenderMode": GpuRenderMode = ParseGpuRenderMode(val); break; // default Auto
                        case "ScanCacheEnabled": ScanCacheEnabled = bool.TryParse(val, out var bsce) && bsce; break;
                        case "RestoreLastSessionEnabled": RestoreLastSessionEnabled = bool.TryParse(val, out var brls) && brls; break;
                        case "RestoreSessionCacheNoticeShown": RestoreSessionCacheNoticeShown = bool.TryParse(val, out var brsn) && brsn; break;
                        case "FocusNewlyAddedFilesEnabled": FocusNewlyAddedFilesEnabled = !bool.TryParse(val, out var bfnaf) || bfnaf; break;
                        case "SilenceMinGapEnabled": SilenceMinGapEnabled = bool.TryParse(val, out var bsmg) && bsmg; AudioAnalyzer.SilenceMinGapEnabled = SilenceMinGapEnabled; break;
                        case "SilenceMinGapSeconds": if (double.TryParse(val, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var smgs) && smgs > 0) { SilenceMinGapSeconds = smgs; AudioAnalyzer.SilenceMinGapSeconds = smgs; } break;
                        case "SilenceSkipEdgesEnabled": SilenceSkipEdgesEnabled = bool.TryParse(val, out var bsse) && bsse; AudioAnalyzer.SilenceSkipEdgesEnabled = SilenceSkipEdgesEnabled; break;
                        case "SilenceSkipEdgeSeconds": if (double.TryParse(val, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var sses) && sses > 0) { SilenceSkipEdgeSeconds = sses; AudioAnalyzer.SilenceSkipEdgeSeconds = sses; } break;
                        case "CrashLoggingEnabled": CrashLoggingEnabled = !bool.TryParse(val, out var bcl) || bcl; break;
                        case "StatsCollectionEnabled": StatsCollectionEnabled = bool.TryParse(val, out var bsc) && bsc; break;
                        case "AlwaysFullAnalysis": AlwaysFullAnalysis = bool.TryParse(val, out var bafa) && bafa; AudioAnalyzer.AlwaysFullAnalysis = AlwaysFullAnalysis; break;
                        case "SpectrogramHiFiMode": SpectrogramHiFiMode = bool.TryParse(val, out var bshf) && bshf; break;
                        case "SpectrogramMagmaColormap": SpectrogramMagmaColormap = bool.TryParse(val, out var bsmc) && bsmc; break;
                        case "FrequencyCutoffAllowEnabled": FrequencyCutoffAllowEnabled = bool.TryParse(val, out var bfca) && bfca; AudioAnalyzer.FrequencyCutoffAllowEnabled = FrequencyCutoffAllowEnabled; break;
                        case "FrequencyCutoffAllowHz": if (int.TryParse(val, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var fcah) && fcah > 0) { FrequencyCutoffAllowHz = fcah; AudioAnalyzer.FrequencyCutoffAllowHz = fcah; } break;
                        case "StreamingRegion": StreamingRegion = string.IsNullOrWhiteSpace(val) ? "us" : val; break;
                        case "LoopMode": if (Enum.TryParse<LoopMode>(val, out var lm)) LoopMode = lm; break;
                        case "RenamePatternIndex": if (int.TryParse(val, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var rpi) && rpi >= 0 && rpi <= 2) RenamePatternIndex = rpi; break;
                        // 0-5: the Batch Editor's style combo ends at 5 ("Custom"), which SaveSmartSettings
                        // writes. A <= 4 bound here silently dropped Custom back to the default on restart.
                        case "SmartRenameStyleIndex": if (int.TryParse(val, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var srsi) && srsi >= 0 && srsi <= 5) SmartRenameStyleIndex = srsi; break;
                        case "SmartRenameFolderIndex": if (int.TryParse(val, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var srfi) && srfi >= 0 && srfi <= 2) SmartRenameFolderIndex = srfi; break;
                        case "SmartRenameIncludeTrackNumbers": SmartRenameIncludeTrackNumbers = !(bool.TryParse(val, out var sritn) && !sritn); break;
                        case "SmartRenameAppendDuplicateNumbers": SmartRenameAppendDuplicateNumbers = bool.TryParse(val, out var sradn) && sradn; break;
                        case "SmartRenameRenameCleanFiles": SmartRenameRenameCleanFiles = bool.TryParse(val, out var srrcf) && srrcf; break;
                        case "SmartRenameNameCaseIndex": if (int.TryParse(val, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var srnci) && srnci >= 0 && srnci <= 3) SmartRenameNameCaseIndex = srnci; break;
                        case "SmartRenameSpaceModeIndex": if (int.TryParse(val, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var srsmi) && srsmi >= 0 && srsmi <= 2) SmartRenameSpaceModeIndex = srsmi; break;
                        case "SmartRenameStripFeaturing": SmartRenameStripFeaturing = bool.TryParse(val, out var srsf) && srsf; break;
                        case "StreamingLinkPlatformIndex": if (int.TryParse(val, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var slpi) && slpi >= 0 && slpi <= 3) StreamingLinkPlatformIndex = slpi; break;
                        case "DefaultCopyFolder": DefaultCopyFolder = val; break;
                        case "DefaultMoveFolder": DefaultMoveFolder = val; break;
                        case "DefaultPlaylistFolder": DefaultPlaylistFolder = val; break;
                        case "MainColorMatchEnabled": MainColorMatchEnabled = bool.TryParse(val, out var bcm) && bcm; break;
                        case "MainColorMatchTargets": MainColorMatchTargets = Enum.TryParse<ColorMatchTarget>(val, out var mainCmt) ? mainCmt : ColorMatchTarget.All; break;
                        case "AppFontFamily": AppFontFamily = string.IsNullOrWhiteSpace(val) ? "Segoe UI" : val; break;
                        case "OfflineModeEnabled": OfflineModeEnabled = bool.TryParse(val, out var bom) && bom; break;
                        case "LyricsAvoidCensored": LyricsAvoidCensored = bool.TryParse(val, out var blac) && blac; break;
                        case "LastSettingsTab": if (int.TryParse(val, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var lst) && lst >= 0 && lst <= 7) LastSettingsTab = lst; break;
                        case "CrossfadeOnManualSkip": CrossfadeOnManualSkip = !(bool.TryParse(val, out var bcoms) && !bcoms); break; // default true

                        // Every Now Playing / background / mini-player key is owned by Core so both
                        // builds parse it identically. Unrecognised keys still fall through untouched.
                        default: NowPlayingSettings.TryLoad(key, val, ref npSeen); break;
                    }
                }
            }
            catch (Exception ex)
            {
                if (CrashLoggingEnabled) LocalCrashLogger.Write(ex);
            }

            // Legacy "Color Drift" background mode and the per-context layout bundles an older
            // options.txt did not carry are both reconciled in Core, shared with the Avalonia build.
            NowPlayingSettings.ApplyPostLoadMigrations(in npSeen);

            // Battery Saver collapsed from master + "entire program" + 5 per-area flags down to
            // master + a visualizer override. A user who ran per-area mode with the visualizer
            // deliberately left animating keeps that; the other four areas have no equivalent
            // now and lose their exemption.
            if (BatterySaverEnabled && !legacyBatteryEntireProgram && !legacyBatteryVisualizer)
                BatterySaverKeepVisualizer = true;

            // Existing config from before NP search had its own slots: copy the
            // user's main-window services across once so NP isn't blank.
            SeedNpSearchServicesFromMain();

            // Load sensitive Last.fm data from Documents
            LoadSensitiveData();
            RepairInflatedListeningTotal();
            ApplyScanPerformanceDefaultsMigration();
        }

        /// <summary>
        /// Repairs lifetime listening totals corrupted by the pre-2.0 culture mismatch: the value was
        /// written in the current culture but read back as invariant, so on a comma-decimal locale
        /// (pt-BR, de-DE, fr-FR…) "12345,67" was parsed as the thousands-grouped 1234567 and the stat
        /// inflated roughly 100x on every launch. Both sides are invariant now, but an options file
        /// written by an older build can still hold the bad number.
        ///
        /// The bound is a fact rather than a guess: you cannot have listened for more seconds than
        /// have elapsed since you first ran the app, so anything above that is corruption. Legitimate
        /// totals are always below the ceiling and pass through untouched.
        /// </summary>
        private static void RepairInflatedListeningTotal()
        {
            if (TotalListeningSecondsLifetime <= 0) return;
            if (FirstScanDate == default) return; // no anchor to measure against — leave it alone

            double elapsedSeconds = (DateTime.Now - FirstScanDate).TotalSeconds;
            if (elapsedSeconds <= 0) return;

            if (TotalListeningSecondsLifetime > elapsedSeconds)
                TotalListeningSecondsLifetime = elapsedSeconds;
        }

        private static void ApplyScanPerformanceDefaultsMigration()
        {
            if (ScanPerformanceDefaultsVersion == CurrentScanPerformanceDefaultsVersion)
            {
                if (SyncHiddenColumnsWithAnalysisOptions())
                    SavePlayOptions();
                return;
            }

            // Migrate the old inherited "everything on" profile back to fast scan defaults.
            if (SilenceDetectionEnabled && DynamicRangeEnabled && TruePeakEnabled && LufsEnabled && BpmDetectionEnabled && !AlwaysFullAnalysis)
            {
                SilenceDetectionEnabled = false;
                DynamicRangeEnabled = false;
                TruePeakEnabled = false;
                LufsEnabled = false;
                BpmDetectionEnabled = false;
                RipLogCheckEnabled = false;

                AudioAnalyzer.EnableSilenceDetection = false;
                AudioAnalyzer.EnableDynamicRange = false;
                AudioAnalyzer.EnableTruePeak = false;
                AudioAnalyzer.EnableLufs = false;
                AudioAnalyzer.EnableBpmDetection = false;
            }

            SyncHiddenColumnsWithAnalysisOptions(applyDefaultHiddenColumns: string.IsNullOrWhiteSpace(HiddenColumns));
            ScanPerformanceDefaultsVersion = CurrentScanPerformanceDefaultsVersion;
            SavePlayOptions();
        }
    }
}
