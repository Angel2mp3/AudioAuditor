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

        public static void SavePlayOptions()
        {
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
                    $"EqualizerGains={string.Join(";", EqualizerGains.Select(g => g.ToString("F1")))}",
                    $"DiscordRpc={DiscordRpcEnabled}",
                    $"DiscordRpcDisplayMode={DiscordRpcDisplayMode}",
                    $"DiscordRpcShowElapsed={DiscordRpcShowElapsed}",
                    $"LastFmEnabled={LastFmEnabled}",
                    $"ExportFormat={ExportFormat}",
                    $"SpatialAudio={SpatialAudioEnabled}",
                    $"ExperimentalAiDetection={ExperimentalAiDetection}",
                    $"RipQualityEnabled={RipQualityEnabled}",
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
                    $"SHLabsCustomApiKey={SHLabsCustomApiKey}",
                    $"AiConfigDismissed={AiConfigDismissed}",
                    $"FeatureConfigVersion={FeatureConfigVersion}",
                    $"VisualizerFullVolume={VisualizerFullVolume}",
                    $"Volume={Volume:0.##}",
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
                    $"TotalListeningSecondsLifetime={TotalListeningSecondsLifetime}",
                    $"FooterSupportDismissed={FooterSupportDismissed}",
                    $"CloseToTray={CloseToTray}",
                    $"PreloadNextTrackEnabled={PreloadNextTrackEnabled}",
                    $"CheckForUpdates={CheckForUpdates}",
                    $"AnimationsEnabled={AnimationsEnabled}",
                    $"BatterySaverEnabled={BatterySaverEnabled}",
                    $"BatterySaverEntireProgram={BatterySaverEntireProgram}",
                    $"BatterySaverNpBackground={BatterySaverNpBackground}",
                    $"BatterySaverVisualizer={BatterySaverVisualizer}",
                    $"BatterySaverCoverGlow={BatterySaverCoverGlow}",
                    $"BatterySaverLyrics={BatterySaverLyrics}",
                    $"BatterySaverPlaybar={BatterySaverPlaybar}",
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
                    $"NpVisualizerEnabled={NpVisualizerEnabled}",
                    $"NpColorMatchEnabled={NpColorMatchEnabled}",
                    $"NpColorCacheEnabled={NpColorCacheEnabled}",
                    $"NpColorCachePersist={NpColorCachePersist}",
                    $"NpRememberManualColorPicks={NpRememberManualColorPicks}",
                    $"NpColorPickerMaxColors={NpColorPickerMaxColors}",
                    $"NpAlbumBackdropEnabled={NpAlbumBackdropEnabled}",
                    $"NpBackgroundMode={NpBackgroundMode}",
                    $"NpCustomBackgroundImagePath={NpCustomBackgroundImagePath}",
                    $"NpCustomBackgroundColors={NpCustomBackgroundColors}",
                    $"NpBackgroundBlur={NpBackgroundBlur.ToString(System.Globalization.CultureInfo.InvariantCulture)}",
                    $"NpBackgroundOpacity={NpBackgroundOpacity.ToString(System.Globalization.CultureInfo.InvariantCulture)}",
                    $"NpBackgroundHorizontalPosition={NpBackgroundHorizontalPosition.ToString(System.Globalization.CultureInfo.InvariantCulture)}",
                    $"NpBackgroundVerticalPosition={NpBackgroundVerticalPosition.ToString(System.Globalization.CultureInfo.InvariantCulture)}",
                    $"NpBackgroundZoom={NpBackgroundZoom.ToString(System.Globalization.CultureInfo.InvariantCulture)}",
                    $"NpBackgroundBrightness={NpBackgroundBrightness.ToString(System.Globalization.CultureInfo.InvariantCulture)}",
                    $"NpBackgroundAnimationMode={NpBackgroundAnimationMode}",
                    $"NpColorDriftBackgroundEnabled={NpColorDriftBackgroundEnabled}",
                    $"NpBackgroundUseAlbumColors={NpBackgroundUseAlbumColors}",
                    $"NpBackgroundCycleEnabled={NpBackgroundCycleEnabled}",
                    $"NpBackgroundCycleSpeed={NpBackgroundCycleSpeed.ToString(System.Globalization.CultureInfo.InvariantCulture)}",
                    $"NpBackgroundCycleOnSongChange={NpBackgroundCycleOnSongChange}",
                    $"NpStarDensity={NpStarDensity.ToString(System.Globalization.CultureInfo.InvariantCulture)}",
                    $"NpShootingStarDensity={NpShootingStarDensity.ToString(System.Globalization.CultureInfo.InvariantCulture)}",
                    $"NpShootingStarsEnabled={NpShootingStarsEnabled}",
                    $"NpRainIntensity={NpRainIntensity.ToString(System.Globalization.CultureInfo.InvariantCulture)}",
                    $"NpRainLightningEnabled={NpRainLightningEnabled}",
                    $"NpRainLightningPromptShown={NpRainLightningPromptShown}",
                    $"NpRainLightningAmount={NpRainLightningAmount.ToString(System.Globalization.CultureInfo.InvariantCulture)}",
                    $"NpSnowDensity={NpSnowDensity.ToString(System.Globalization.CultureInfo.InvariantCulture)}",
                    $"NpSnowflakeAmount={NpSnowflakeAmount.ToString(System.Globalization.CultureInfo.InvariantCulture)}",
                    $"NpUnderwaterBubbleDensity={NpUnderwaterBubbleDensity.ToString(System.Globalization.CultureInfo.InvariantCulture)}",
                    $"NpUnderwaterCausticIntensity={NpUnderwaterCausticIntensity.ToString(System.Globalization.CultureInfo.InvariantCulture)}",
                    $"NpUnderwaterFishEnabled={NpUnderwaterFishEnabled}",
                    $"NpUnderwaterSeaweedEnabled={NpUnderwaterSeaweedEnabled}",
                    $"NpBackgroundAnimationSpeed={NpBackgroundAnimationSpeed.ToString(System.Globalization.CultureInfo.InvariantCulture)}",
                    $"MainBackgroundImagePath={MainBackgroundImagePath}",
                    $"MainBackgroundOpacity={MainBackgroundOpacity.ToString(System.Globalization.CultureInfo.InvariantCulture)}",
                    $"MainBackgroundBlur={MainBackgroundBlur.ToString(System.Globalization.CultureInfo.InvariantCulture)}",
                    $"NpCoverShapeMode={NpCoverShapeMode}",
                    $"MiniCoverShapeMode={MiniCoverShapeMode}",
                    $"MiniPlayerAlwaysOnTop={MiniPlayerAlwaysOnTop}",
                    $"MiniVisualizerStyle={MiniVisualizerStyle}",
                    $"MiniColorMatchEnabled={MiniColorMatchEnabled}",
                    $"MiniPlayerLeft={MiniPlayerLeft.ToString(System.Globalization.CultureInfo.InvariantCulture)}",
                    $"MiniPlayerTop={MiniPlayerTop.ToString(System.Globalization.CultureInfo.InvariantCulture)}",
                    $"MiniPlayerWidth={MiniPlayerWidth.ToString(System.Globalization.CultureInfo.InvariantCulture)}",
                    $"MiniPlayerBaseHeight={MiniPlayerBaseHeight.ToString(System.Globalization.CultureInfo.InvariantCulture)}",
                    $"ShowWrappedButton={ShowWrappedButton}",
                    $"ShowMiniPlayerButton={ShowMiniPlayerButton}",
                    $"ShowMusicServiceButtons={ShowMusicServiceButtons}",
                    $"NpLyricsHidden={NpLyricsHidden}",
                    $"NpTranslateEnabled={NpTranslateEnabled}",
                    $"NpAutoSaveLyricsEnabled={NpAutoSaveLyricsEnabled}",
                    $"NpKaraokeEnabled={NpKaraokeEnabled}",
                    $"NpLyricMode={NpLyricMode}",
                    $"NpFocusedLyricsBlurRadius={NpFocusedLyricsBlurRadius.ToString(System.Globalization.CultureInfo.InvariantCulture)}",
                    $"NpCoverGlowMotionEnabled={NpCoverGlowMotionEnabled}",
                    $"NpGlowMotionMode={NpGlowMotionMode}",
                    $"NpVisualizerStyle={NpVisualizerStyle}",
                    $"NpVizPlacement={NpVizPlacement}",
                    $"RegionAwareSearchEnabled={RegionAwareSearchEnabled}",
                    $"StreamingRegion={StreamingRegion}",
                    $"NpSubCoverShowArtist={NpSubCoverShowArtist}",
                    $"NpButtonOrder={NpButtonOrder}",
                    $"NpButtonHidden={NpButtonHidden}",
                    $"NpTransportOrder={NpTransportOrder}",
                    $"NpCoverSize={NpCoverSize}",
                    $"NpTitleSize={NpTitleSize}",
                    $"NpSubTextSize={NpSubTextSize}",
                    $"NpLyricsSize={NpLyricsSize}",
                    $"NpVizSize={NpVizSize}",
                    $"NpCoverGlowSize={NpCoverGlowSize.ToString(System.Globalization.CultureInfo.InvariantCulture)}",
                    $"NpLyricsOffsetX={NpLyricsOffsetX}",
                    $"NpCoverOffsetX={NpCoverOffsetX}",
                    $"NpCoverOffsetY={NpCoverOffsetY}",
                    $"NpTitleOffsetX={NpTitleOffsetX}",
                    $"NpTitleOffsetY={NpTitleOffsetY}",
                    $"NpArtistOffsetX={NpArtistOffsetX}",
                    $"NpArtistOffsetY={NpArtistOffsetY}",
                    $"NpVizOffsetY={NpVizOffsetY}",
                    $"NpFullscreenCoverSize={NpFullscreenCoverSize}",
                    $"NpFullscreenTitleSize={NpFullscreenTitleSize}",
                    $"NpFullscreenSubTextSize={NpFullscreenSubTextSize}",
                    $"NpFullscreenLyricsSize={NpFullscreenLyricsSize}",
                    $"NpFullscreenVizSize={NpFullscreenVizSize}",
                    $"NpFullscreenLyricsOffsetX={NpFullscreenLyricsOffsetX}",
                    $"NpFullscreenCoverOffsetX={NpFullscreenCoverOffsetX}",
                    $"NpFullscreenCoverOffsetY={NpFullscreenCoverOffsetY}",
                    $"NpFullscreenTitleOffsetX={NpFullscreenTitleOffsetX}",
                    $"NpFullscreenTitleOffsetY={NpFullscreenTitleOffsetY}",
                    $"NpFullscreenArtistOffsetX={NpFullscreenArtistOffsetX}",
                    $"NpFullscreenArtistOffsetY={NpFullscreenArtistOffsetY}",
                    $"NpFullscreenVizOffsetY={NpFullscreenVizOffsetY}",
                    $"NpFullscreenVizPlacement={NpFullscreenVizPlacement}",
                    $"NpVizOnCoverSize={NpVizOnCoverSize}",
                    $"NpVizOnTitleSize={NpVizOnTitleSize}",
                    $"NpVizOnSubTextSize={NpVizOnSubTextSize}",
                    $"NpVizOnLyricsSize={NpVizOnLyricsSize}",
                    $"NpVizOnVizSize={NpVizOnVizSize}",
                    $"NpVizOnLyricsOffsetX={NpVizOnLyricsOffsetX}",
                    $"NpVizOnCoverOffsetX={NpVizOnCoverOffsetX}",
                    $"NpVizOnCoverOffsetY={NpVizOnCoverOffsetY}",
                    $"NpVizOnTitleOffsetX={NpVizOnTitleOffsetX}",
                    $"NpVizOnTitleOffsetY={NpVizOnTitleOffsetY}",
                    $"NpVizOnArtistOffsetX={NpVizOnArtistOffsetX}",
                    $"NpVizOnArtistOffsetY={NpVizOnArtistOffsetY}",
                    $"NpVizOnVizOffsetY={NpVizOnVizOffsetY}",
                    $"NpVizOnPlacement={NpVizOnPlacement}",
                    $"NpFullscreenVizOnCoverSize={NpFullscreenVizOnCoverSize}",
                    $"NpFullscreenVizOnTitleSize={NpFullscreenVizOnTitleSize}",
                    $"NpFullscreenVizOnSubTextSize={NpFullscreenVizOnSubTextSize}",
                    $"NpFullscreenVizOnLyricsSize={NpFullscreenVizOnLyricsSize}",
                    $"NpFullscreenVizOnVizSize={NpFullscreenVizOnVizSize}",
                    $"NpFullscreenVizOnLyricsOffsetX={NpFullscreenVizOnLyricsOffsetX}",
                    $"NpFullscreenVizOnCoverOffsetX={NpFullscreenVizOnCoverOffsetX}",
                    $"NpFullscreenVizOnCoverOffsetY={NpFullscreenVizOnCoverOffsetY}",
                    $"NpFullscreenVizOnTitleOffsetX={NpFullscreenVizOnTitleOffsetX}",
                    $"NpFullscreenVizOnTitleOffsetY={NpFullscreenVizOnTitleOffsetY}",
                    $"NpFullscreenVizOnArtistOffsetX={NpFullscreenVizOnArtistOffsetX}",
                    $"NpFullscreenVizOnArtistOffsetY={NpFullscreenVizOnArtistOffsetY}",
                    $"NpFullscreenVizOnVizOffsetY={NpFullscreenVizOnVizOffsetY}",
                    $"NpFullscreenVizOnPlacement={NpFullscreenVizOnPlacement}",
                    $"LoopMode={LoopMode}",
                    $"RenamePatternIndex={RenamePatternIndex}",
                    $"SmartRenameStyleIndex={SmartRenameStyleIndex}",
                    $"SmartRenameFolderIndex={SmartRenameFolderIndex}",
                    $"SmartRenameIncludeTrackNumbers={SmartRenameIncludeTrackNumbers}",
                    $"SmartRenameAppendDuplicateNumbers={SmartRenameAppendDuplicateNumbers}",
                    $"SmartRenameRenameCleanFiles={SmartRenameRenameCleanFiles}",
                    $"DefaultCopyFolder={DefaultCopyFolder}",
                    $"DefaultMoveFolder={DefaultMoveFolder}",
                    $"DefaultPlaylistFolder={DefaultPlaylistFolder}",
                    $"MainColorMatchEnabled={MainColorMatchEnabled}",
                    $"WelcomeVersionSeen={WelcomeVersionSeen}",
                    $"OfflineModeEnabled={OfflineModeEnabled}",
                    $"LyricsAvoidCensored={LyricsAvoidCensored}",
                    $"LibreFmEnabled={LibreFmEnabled}",
                    $"ListenBrainzEnabled={ListenBrainzEnabled}",
                    $"MalojaEnabled={MalojaEnabled}",
                    $"PauseScrobbling={PauseScrobbling}",
                    $"ScrobbleAtPercent={ScrobbleAtPercent}",
                    $"ScrobbleAtSeconds={ScrobbleAtSeconds}",
                    $"MinScrobbleTrackSeconds={MinScrobbleTrackSeconds}",
                    $"ScrobbleBlacklist={ScrobbleBlacklist}",
                    $"LastSettingsTab={LastSettingsTab}"
                };
                lock (_savePlayOptionsLock)
                    File.WriteAllLines(OptionsFile, lines);
            }
            catch (Exception ex)
            {
                // A silent failure here is exactly the "doesn't save settings" report.
                if (CrashLoggingEnabled) LocalCrashLogger.Write(ex);
            }

            // Save sensitive Last.fm data to Documents (DPAPI-encrypted)
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
                    $"AcoustIdApiKey={AcoustIdApiKey}"
                };
                var plaintext = System.Text.Encoding.UTF8.GetBytes(string.Join("\n", sensitiveLines));
                var encrypted = System.Security.Cryptography.ProtectedData.Protect(
                    plaintext, null, System.Security.Cryptography.DataProtectionScope.CurrentUser);
                File.WriteAllBytes(SensitiveFile, encrypted);
            }
            catch { }
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

            bool hasNpFullscreenLayout = false;
            bool hasNpVizOnLayout = false;
            bool hasNpFullscreenVizOnLayout = false;
            bool hasNpFullscreenVizPlacement = false;

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
                            if (int.TryParse(val, out var dur) && dur >= 1 && dur <= 30)
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
                            if (int.TryParse(val, out var vs) && vs >= 0 && vs <= 5)
                            {
                                // Migrate old Abstract style (index 5 was removed; 5 is now VU Meter)
                                // Old index 5 (Abstract) → 0 (Bars), old 6 (VU) → 5 (VU)
                                VisualizerStyle = vs == 5 ? 0 : vs;
                            }
                            break;
                        case "VisualizerCycleSpeed":
                            if (int.TryParse(val, out var vcs) && vcs >= 5 && vcs <= 60) VisualizerCycleSpeed = vcs;
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
                                if (float.TryParse(parts2[i], out var g)) EqualizerGains[i] = g;
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
                        case "PauseScrobbling": PauseScrobbling = bool.TryParse(val, out var bps) && bps; break;
                        case "ScrobbleAtPercent": if (int.TryParse(val, out var sap) && sap >= 0 && sap <= 100) ScrobbleAtPercent = sap; break;
                        case "ScrobbleAtSeconds": if (int.TryParse(val, out var sas) && sas >= 0 && sas <= 7200) ScrobbleAtSeconds = sas; break;
                        case "MinScrobbleTrackSeconds": if (int.TryParse(val, out var msts) && msts >= 0 && msts <= 3600) MinScrobbleTrackSeconds = msts; break;
                        case "ScrobbleBlacklist": ScrobbleBlacklist = val; break;
                        case "ExportFormat":
                            if (new[] { "csv", "txt", "pdf", "xlsx", "docx" }.Contains(val))
                                ExportFormat = val;
                            break;
                        case "SpatialAudio": SpatialAudioEnabled = bool.TryParse(val, out var bsa) && bsa; break;
                        case "ExperimentalAiDetection": ExperimentalAiDetection = bool.TryParse(val, out var bea) && bea; AudioAnalyzer.EnableExperimentalAi = ExperimentalAiDetection; break;
                        case "RipQualityEnabled": RipQualityEnabled = bool.TryParse(val, out var brq) && brq; AudioAnalyzer.EnableRipQuality = RipQualityEnabled; break;
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
                            if (int.TryParse(val, out var mc) && mc >= 0 && mc <= Environment.ProcessorCount)
                                _maxConcurrency = mc;
                            break;
                        case "MaxMemoryMB":
                            if (int.TryParse(val, out var mm) && mm >= 0 && mm <= (int)Math.Min(TotalSystemMemoryMB, 65536))
                                _maxMemoryMB = mm;
                            break;
                        case "DonationDismissed": DonationDismissed = bool.TryParse(val, out var bdd) && bdd; break;
                        case "Donation30DayShown": Donation30DayShown = bool.TryParse(val, out var d30) && d30; break;
                        case "FeedbackOneHourShown": FeedbackOneHourShown = bool.TryParse(val, out var f1h) && f1h; break;
                        case "FeedbackActiveUsageSeconds": if (double.TryParse(val, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var faus)) FeedbackActiveUsageSeconds = Math.Clamp(faus, 0, 3600); break;
                        case "FirstScanDate": if (DateTime.TryParse(val, out var fsd)) FirstScanDate = fsd; break;
                        case "TotalFilesScannedLifetime": if (int.TryParse(val, out var tfsl)) TotalFilesScannedLifetime = tfsl; break;
                        case "TotalListeningSecondsLifetime": if (double.TryParse(val, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var tlsl)) TotalListeningSecondsLifetime = tlsl; break;
                        case "FooterSupportDismissed": FooterSupportDismissed = bool.TryParse(val, out var bfs) && bfs; break;
                        case "CloseToTray": CloseToTray = bool.TryParse(val, out var bct) && bct; break;
                        case "PreloadNextTrackEnabled": PreloadNextTrackEnabled = !bool.TryParse(val, out var bpte) || bpte; break; // default true
                        case "CheckForUpdates": CheckForUpdates = !bool.TryParse(val, out var bcu) || bcu; break; // default true
                        case "AnimationsEnabled": AnimationsEnabled = !bool.TryParse(val, out var bae) || bae; break; // default true
                        case "BatterySaverEnabled": BatterySaverEnabled = bool.TryParse(val, out var bbse) && bbse; break; // default false
                        case "BatterySaverEntireProgram": BatterySaverEntireProgram = !bool.TryParse(val, out var bbsep) || bbsep; break; // default true
                        case "BatterySaverNpBackground": BatterySaverNpBackground = !bool.TryParse(val, out var bbsnb) || bbsnb; break; // default true
                        case "BatterySaverVisualizer": BatterySaverVisualizer = !bool.TryParse(val, out var bbsv) || bbsv; break; // default true
                        case "BatterySaverCoverGlow": BatterySaverCoverGlow = !bool.TryParse(val, out var bbscg) || bbscg; break; // default true
                        case "BatterySaverLyrics": BatterySaverLyrics = !bool.TryParse(val, out var bbsl) || bbsl; break; // default true
                        case "BatterySaverPlaybar": BatterySaverPlaybar = !bool.TryParse(val, out var bbsp) || bbsp; break; // default true
                        case "BatterySaverKeepVisualizer": BatterySaverKeepVisualizer = bool.TryParse(val, out var bbskv) && bbskv; break; // default false
                        case "GpuRenderMode": GpuRenderMode = ParseGpuRenderMode(val); break; // default Auto
                        case "ScanCacheEnabled": ScanCacheEnabled = bool.TryParse(val, out var bsce) && bsce; break;
                        case "RestoreLastSessionEnabled": RestoreLastSessionEnabled = bool.TryParse(val, out var brls) && brls; break;
                        case "RestoreSessionCacheNoticeShown": RestoreSessionCacheNoticeShown = bool.TryParse(val, out var brsn) && brsn; break;
                        case "FocusNewlyAddedFilesEnabled": FocusNewlyAddedFilesEnabled = !bool.TryParse(val, out var bfnaf) || bfnaf; break;
                        case "SilenceMinGapEnabled": SilenceMinGapEnabled = bool.TryParse(val, out var bsmg) && bsmg; AudioAnalyzer.SilenceMinGapEnabled = SilenceMinGapEnabled; break;
                        case "SilenceMinGapSeconds": if (double.TryParse(val, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var smgs) && smgs > 0) { SilenceMinGapSeconds = smgs; AudioAnalyzer.SilenceMinGapSeconds = smgs; } break;
                        case "SilenceSkipEdgesEnabled": SilenceSkipEdgesEnabled = bool.TryParse(val, out var bsse) && bsse; AudioAnalyzer.SilenceSkipEdgesEnabled = SilenceSkipEdgesEnabled; break;
                        case "SilenceSkipEdgeSeconds": if (double.TryParse(val, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var sses) && sses > 0) { SilenceSkipEdgeSeconds = sses; AudioAnalyzer.SilenceSkipEdgeSeconds = sses; } break;
                        case "CrashLoggingEnabled": CrashLoggingEnabled = !bool.TryParse(val, out var bcl) || bcl; break;
                        case "StatsCollectionEnabled": StatsCollectionEnabled = bool.TryParse(val, out var bsc) && bsc; break;
                        case "AlwaysFullAnalysis": AlwaysFullAnalysis = bool.TryParse(val, out var bafa) && bafa; AudioAnalyzer.AlwaysFullAnalysis = AlwaysFullAnalysis; break;
                        case "SpectrogramHiFiMode": SpectrogramHiFiMode = bool.TryParse(val, out var bshf) && bshf; break;
                        case "SpectrogramMagmaColormap": SpectrogramMagmaColormap = bool.TryParse(val, out var bsmc) && bsmc; break;
                        case "FrequencyCutoffAllowEnabled": FrequencyCutoffAllowEnabled = bool.TryParse(val, out var bfca) && bfca; AudioAnalyzer.FrequencyCutoffAllowEnabled = FrequencyCutoffAllowEnabled; break;
                        case "FrequencyCutoffAllowHz": if (int.TryParse(val, out var fcah) && fcah > 0) { FrequencyCutoffAllowHz = fcah; AudioAnalyzer.FrequencyCutoffAllowHz = fcah; } break;
                        case "NpVisualizerEnabled": NpVisualizerEnabled = bool.TryParse(val, out var bNpViz) && bNpViz; break;
                        case "NpColorMatchEnabled": NpColorMatchEnabled = bool.TryParse(val, out var bNpCm) && bNpCm; break;
                        case "NpColorCacheEnabled": NpColorCacheEnabled = bool.TryParse(val, out var bNpCc) && bNpCc; break;
                        case "NpColorCachePersist": NpColorCachePersist = bool.TryParse(val, out var bNpCp) && bNpCp; break;
                        case "NpRememberManualColorPicks": NpRememberManualColorPicks = !bool.TryParse(val, out var bNpRmcp) || bNpRmcp; break;
                        case "NpColorPickerMaxColors": if (int.TryParse(val, out var iNpCpmc)) NpColorPickerMaxColors = iNpCpmc; break;
                        case "NpAlbumBackdropEnabled": NpAlbumBackdropEnabled = bool.TryParse(val, out var bNpAbe) && bNpAbe; break;
                        case "NpBackgroundMode": NpBackgroundMode = string.IsNullOrWhiteSpace(val) ? "AlbumArt" : val; break;
                        case "NpCustomBackgroundImagePath": NpCustomBackgroundImagePath = val; break;
                        case "NpCustomBackgroundColors": NpCustomBackgroundColors = val; break;
                        case "NpBackgroundBlur":
                            if (double.TryParse(val, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var npbb))
                                NpBackgroundBlur = Math.Clamp(npbb, 0, 48);
                            break;
                        case "NpBackgroundOpacity":
                            if (double.TryParse(val, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var npbo))
                                NpBackgroundOpacity = Math.Clamp(npbo, 0, 0.8);
                            break;
                        case "NpBackgroundHorizontalPosition":
                        case "NpBackgroundFocusX":
                            if (double.TryParse(val, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var npbfx))
                                NpBackgroundHorizontalPosition = Math.Clamp(npbfx, 0, 1);
                            break;
                        case "NpBackgroundVerticalPosition":
                        case "NpBackgroundFocusY":
                            if (double.TryParse(val, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var npbfy))
                                NpBackgroundVerticalPosition = Math.Clamp(npbfy, 0, 1);
                            break;
                        case "NpBackgroundZoom":
                            if (double.TryParse(val, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var npbz))
                                NpBackgroundZoom = Math.Clamp(npbz, 1, 2.5);
                            break;
                        case "NpBackgroundBrightness":
                            if (double.TryParse(val, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var npbr))
                                NpBackgroundBrightness = Math.Clamp(npbr, 0.35, 1.6);
                            break;
                        case "NpBackgroundAnimationMode": NpBackgroundAnimationMode = NormalizeNpBackgroundAnimationMode(val); break;
                        case "NpColorDriftBackgroundEnabled": NpColorDriftBackgroundEnabled = bool.TryParse(val, out var ncdbe) && ncdbe; break;
                        case "NpBackgroundUseAlbumColors": NpBackgroundUseAlbumColors = bool.TryParse(val, out var nbbac) && nbbac; break;
                        case "NpBackgroundCycleEnabled": NpBackgroundCycleEnabled = bool.TryParse(val, out var nbce) && nbce; break;
                        case "NpBackgroundCycleSpeed":
                            if (double.TryParse(val, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var nbcs))
                                NpBackgroundCycleSpeed = Math.Clamp(nbcs, 0.25, 3.0);
                            break;
                        case "NpBackgroundCycleOnSongChange": NpBackgroundCycleOnSongChange = bool.TryParse(val, out var nbcosc) && nbcosc; break;
                        case "NpStarDensity":
                            if (double.TryParse(val, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var nsd))
                                NpStarDensity = ClampNpStarDensity(nsd);
                            break;
                        case "NpShootingStarDensity":
                            if (double.TryParse(val, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var nssd))
                                NpShootingStarDensity = ClampNpShootingStarDensity(nssd);
                            break;
                        case "NpShootingStarsEnabled": NpShootingStarsEnabled = !bool.TryParse(val, out var nsse) || nsse; break;
                        case "NpRainIntensity":
                            if (double.TryParse(val, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var nri))
                                NpRainIntensity = ClampNpRainIntensity(nri);
                            break;
                        case "NpRainLightningEnabled": NpRainLightningEnabled = bool.TryParse(val, out var nrle) && nrle; break;
                        case "NpRainLightningPromptShown": NpRainLightningPromptShown = bool.TryParse(val, out var nrlps) && nrlps; break;
                        case "NpRainLightningAmount":
                            if (double.TryParse(val, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var nrla))
                                NpRainLightningAmount = ClampNpRainLightningAmount(nrla);
                            break;
                        case "NpSnowDensity":
                            if (double.TryParse(val, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var nsdn))
                                NpSnowDensity = ClampNpSnowDensity(nsdn);
                            break;
                        case "NpSnowflakeAmount":
                            if (double.TryParse(val, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var nsfa))
                                NpSnowflakeAmount = ClampNpSnowflakeAmount(nsfa);
                            break;
                        case "NpUnderwaterBubbleDensity":
                            if (double.TryParse(val, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var nubd))
                                NpUnderwaterBubbleDensity = ClampNpUnderwaterBubbleDensity(nubd);
                            break;
                        case "NpUnderwaterCausticIntensity":
                            if (double.TryParse(val, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var nuci))
                                NpUnderwaterCausticIntensity = ClampNpUnderwaterCausticIntensity(nuci);
                            break;
                        case "NpUnderwaterFishEnabled": NpUnderwaterFishEnabled = !bool.TryParse(val, out var nufe) || nufe; break;
                        case "NpUnderwaterSeaweedEnabled": NpUnderwaterSeaweedEnabled = !bool.TryParse(val, out var nuse) || nuse; break;
                        case "NpBackgroundAnimationSpeed":
                            if (double.TryParse(val, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var nbas))
                                NpBackgroundAnimationSpeed = ClampNpBackgroundAnimationSpeed(nbas);
                            break;
                        case "MainBackgroundImagePath": MainBackgroundImagePath = val; break;
                        case "MainBackgroundOpacity":
                            if (double.TryParse(val, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var mbo))
                                MainBackgroundOpacity = Math.Clamp(mbo, 0, 0.8);
                            break;
                        case "MainBackgroundBlur":
                            if (double.TryParse(val, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var mbb))
                                MainBackgroundBlur = Math.Clamp(mbb, 0, 48);
                            break;
                        case "NpCoverShapeMode": NpCoverShapeMode = NormalizeCoverShapeMode(val); break;
                        case "MiniCoverShapeMode": MiniCoverShapeMode = NormalizeCoverShapeMode(val) == "Default" ? "Rounded" : NormalizeCoverShapeMode(val); break;
                        case "MiniPlayerAlwaysOnTop": MiniPlayerAlwaysOnTop = !bool.TryParse(val, out var miniAlwaysOnTop) || miniAlwaysOnTop; break;
                        case "MiniVisualizerStyle": if (int.TryParse(val, out var mvs) && mvs is >= -1 and <= 4) MiniVisualizerStyle = mvs; break;
                        case "MiniColorMatchEnabled": MiniColorMatchEnabled = bool.TryParse(val, out var mcme) && mcme; break;
                        case "MiniPlayerLeft": if (double.TryParse(val, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var mpl)) MiniPlayerLeft = mpl; break;
                        case "MiniPlayerTop": if (double.TryParse(val, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var mpt)) MiniPlayerTop = mpt; break;
                        case "MiniPlayerWidth": if (double.TryParse(val, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var mpw)) MiniPlayerWidth = mpw; break;
                        case "MiniPlayerBaseHeight": if (double.TryParse(val, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var mpbh)) MiniPlayerBaseHeight = mpbh; break;
                        case "ShowWrappedButton": ShowWrappedButton = !bool.TryParse(val, out var bSwb) || bSwb; break; // default true
                        case "ShowMiniPlayerButton": ShowMiniPlayerButton = !bool.TryParse(val, out var bSmpb) || bSmpb; break; // default true
                        case "ShowMusicServiceButtons": ShowMusicServiceButtons = !bool.TryParse(val, out var bSmsb) || bSmsb; break; // default true
                        case "NpLyricsHidden": NpLyricsHidden = bool.TryParse(val, out var bNpLh) && bNpLh; break;
                        case "NpTranslateEnabled": NpTranslateEnabled = bool.TryParse(val, out var bNpTr) && bNpTr; break;
                        case "NpAutoSaveLyricsEnabled": NpAutoSaveLyricsEnabled = bool.TryParse(val, out var bNpAs) && bNpAs; break;
                        case "NpKaraokeEnabled": NpKaraokeEnabled = bool.TryParse(val, out var bNpKa) && bNpKa; break;
                        // "Uniform" was removed as an option — migrate any saved Uniform to Standard.
                        case "NpLyricMode": if (System.Enum.TryParse<NpLyricDisplayMode>(val, out var nlm)) NpLyricMode = nlm == NpLyricDisplayMode.Uniform ? NpLyricDisplayMode.Standard : nlm; break;
                        // Legacy (pre-3-mode) key: a saved "on" focused-lyrics flag maps to Blur mode.
                        case "NpFocusedLyricsEnabled": if (bool.TryParse(val, out var bNpFl) && bNpFl) NpLyricMode = NpLyricDisplayMode.Blur; break;
                        case "NpFocusedLyricsBlurRadius":
                            if (double.TryParse(val, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var nflb)
                                && nflb >= 0 && nflb <= 16.0)
                                NpFocusedLyricsBlurRadius = nflb;
                            break;
                        case "NpCoverGlowMotionEnabled": NpCoverGlowMotionEnabled = !bool.TryParse(val, out var bNpGm) || bNpGm; break;
                        case "NpGlowMotionMode": if (Enum.TryParse<GlowMotionMode>(val, true, out var bNpGmm)) NpGlowMotionMode = bNpGmm; break;
                        case "NpVisualizerStyle":
                            if (int.TryParse(val, out var nvs) && nvs >= 0 && nvs <= 5)
                            {
                                // Migrate old Abstract style (index 5 was removed; 5 is now VU Meter)
                                NpVisualizerStyle = nvs == 5 ? 0 : nvs;
                            }
                            break;
                        case "NpVizPlacement":
                            if (int.TryParse(val, out var nvp) && nvp >= 0 && nvp <= 1) NpVizPlacement = nvp;
                            break;
                        case "RegionAwareSearchEnabled": RegionAwareSearchEnabled = !(bool.TryParse(val, out var bra) && !bra); break; // default true
                        case "StreamingRegion": StreamingRegion = string.IsNullOrWhiteSpace(val) ? "us" : val; break;
                        case "NpSubCoverShowArtist": NpSubCoverShowArtist = !bool.TryParse(val, out var bNpSca) || bNpSca; break; // default true
                        case "NpButtonOrder": NpButtonOrder = val ?? ""; break;
                        case "NpButtonHidden": NpButtonHidden = val ?? ""; break;
                        case "NpTransportOrder": NpTransportOrder = val ?? ""; break;
                        case "NpCoverSize": if (int.TryParse(val, out var ncs) && ncs >= 0 && ncs <= 900) NpCoverSize = ncs; break;
                        case "NpTitleSize": if (int.TryParse(val, out var nts) && nts >= 0 && nts <= 72) NpTitleSize = nts; break;
                        case "NpSubTextSize": if (int.TryParse(val, out var nss) && nss >= 0 && nss <= 36) NpSubTextSize = nss; break;
                        case "NpLyricsSize": if (int.TryParse(val, out var nls) && nls >= 0 && nls <= 72) NpLyricsSize = nls; break;
                        case "NpVizSize": if (int.TryParse(val, out var nvz) && nvz >= 0 && nvz <= 400) NpVizSize = nvz; break;
                        case "NpCoverGlowSize":
                            if (double.TryParse(val, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var ncgs)
                                && ncgs >= 0 && ncgs <= 2.0)
                                NpCoverGlowSize = ncgs;
                            break;
                        case "NpLyricsOffsetX": if (int.TryParse(val, out var nlx) && nlx >= 0 && nlx <= 500) NpLyricsOffsetX = nlx; break;
                        case "NpCoverOffsetX": if (int.TryParse(val, out var ncox) && ncox >= -200 && ncox <= 200) NpCoverOffsetX = ncox; break;
                        case "NpCoverOffsetY": if (int.TryParse(val, out var ncoy) && ncoy >= -200 && ncoy <= 200) NpCoverOffsetY = ncoy; break;
                        case "NpTitleOffsetX": if (int.TryParse(val, out var ntox) && ntox >= -200 && ntox <= 200) NpTitleOffsetX = ntox; break;
                        case "NpTitleOffsetY": if (int.TryParse(val, out var ntoy) && ntoy >= -200 && ntoy <= 200) NpTitleOffsetY = ntoy; break;
                        case "NpArtistOffsetX": if (int.TryParse(val, out var naox) && naox >= -200 && naox <= 200) NpArtistOffsetX = naox; break;
                        case "NpArtistOffsetY": if (int.TryParse(val, out var naoy) && naoy >= -200 && naoy <= 200) NpArtistOffsetY = naoy; break;
                        case "NpVizOffsetY": if (int.TryParse(val, out var nvoy) && nvoy >= -200 && nvoy <= 200) NpVizOffsetY = nvoy; break;
                        case "NpFullscreenCoverSize": hasNpFullscreenLayout = true; if (int.TryParse(val, out var nfcs) && nfcs >= 0 && nfcs <= 900) NpFullscreenCoverSize = nfcs; break;
                        case "NpFullscreenTitleSize": hasNpFullscreenLayout = true; if (int.TryParse(val, out var nfts) && nfts >= 0 && nfts <= 72) NpFullscreenTitleSize = nfts; break;
                        case "NpFullscreenSubTextSize": hasNpFullscreenLayout = true; if (int.TryParse(val, out var nfss) && nfss >= 0 && nfss <= 36) NpFullscreenSubTextSize = nfss; break;
                        case "NpFullscreenLyricsSize": hasNpFullscreenLayout = true; if (int.TryParse(val, out var nfls) && nfls >= 0 && nfls <= 72) NpFullscreenLyricsSize = nfls; break;
                        case "NpFullscreenVizSize": hasNpFullscreenLayout = true; if (int.TryParse(val, out var nfvz) && nfvz >= 0 && nfvz <= 400) NpFullscreenVizSize = nfvz; break;
                        case "NpFullscreenLyricsOffsetX": hasNpFullscreenLayout = true; if (int.TryParse(val, out var nflx) && nflx >= 0 && nflx <= 500) NpFullscreenLyricsOffsetX = nflx; break;
                        case "NpFullscreenCoverOffsetX": hasNpFullscreenLayout = true; if (int.TryParse(val, out var nfcox) && nfcox >= -200 && nfcox <= 200) NpFullscreenCoverOffsetX = nfcox; break;
                        case "NpFullscreenCoverOffsetY": hasNpFullscreenLayout = true; if (int.TryParse(val, out var nfcoy) && nfcoy >= -200 && nfcoy <= 200) NpFullscreenCoverOffsetY = nfcoy; break;
                        case "NpFullscreenTitleOffsetX": hasNpFullscreenLayout = true; if (int.TryParse(val, out var nftox) && nftox >= -200 && nftox <= 200) NpFullscreenTitleOffsetX = nftox; break;
                        case "NpFullscreenTitleOffsetY": hasNpFullscreenLayout = true; if (int.TryParse(val, out var nftoy) && nftoy >= -200 && nftoy <= 200) NpFullscreenTitleOffsetY = nftoy; break;
                        case "NpFullscreenArtistOffsetX": hasNpFullscreenLayout = true; if (int.TryParse(val, out var nfaox) && nfaox >= -200 && nfaox <= 200) NpFullscreenArtistOffsetX = nfaox; break;
                        case "NpFullscreenArtistOffsetY": hasNpFullscreenLayout = true; if (int.TryParse(val, out var nfaoy) && nfaoy >= -200 && nfaoy <= 200) NpFullscreenArtistOffsetY = nfaoy; break;
                        case "NpFullscreenVizOffsetY": hasNpFullscreenLayout = true; if (int.TryParse(val, out var nfvoy) && nfvoy >= -200 && nfvoy <= 200) NpFullscreenVizOffsetY = nfvoy; break;
                        case "NpFullscreenVizPlacement": hasNpFullscreenLayout = true; hasNpFullscreenVizPlacement = true; if (int.TryParse(val, out var nfvp) && nfvp >= 0 && nfvp <= 1) NpFullscreenVizPlacement = nfvp; break;
                        case "NpVizOnCoverSize": hasNpVizOnLayout = true; if (int.TryParse(val, out var nvocs) && nvocs >= 0 && nvocs <= 900) NpVizOnCoverSize = nvocs; break;
                        case "NpVizOnTitleSize": hasNpVizOnLayout = true; if (int.TryParse(val, out var nvots) && nvots >= 0 && nvots <= 72) NpVizOnTitleSize = nvots; break;
                        case "NpVizOnSubTextSize": hasNpVizOnLayout = true; if (int.TryParse(val, out var nvoss) && nvoss >= 0 && nvoss <= 36) NpVizOnSubTextSize = nvoss; break;
                        case "NpVizOnLyricsSize": hasNpVizOnLayout = true; if (int.TryParse(val, out var nvols) && nvols >= 0 && nvols <= 72) NpVizOnLyricsSize = nvols; break;
                        case "NpVizOnVizSize": hasNpVizOnLayout = true; if (int.TryParse(val, out var nvovz) && nvovz >= 0 && nvovz <= 400) NpVizOnVizSize = nvovz; break;
                        case "NpVizOnLyricsOffsetX": hasNpVizOnLayout = true; if (int.TryParse(val, out var nvolx) && nvolx >= 0 && nvolx <= 500) NpVizOnLyricsOffsetX = nvolx; break;
                        case "NpVizOnCoverOffsetX": hasNpVizOnLayout = true; if (int.TryParse(val, out var nvocox) && nvocox >= -200 && nvocox <= 200) NpVizOnCoverOffsetX = nvocox; break;
                        case "NpVizOnCoverOffsetY": hasNpVizOnLayout = true; if (int.TryParse(val, out var nvocoy) && nvocoy >= -200 && nvocoy <= 200) NpVizOnCoverOffsetY = nvocoy; break;
                        case "NpVizOnTitleOffsetX": hasNpVizOnLayout = true; if (int.TryParse(val, out var nvotox) && nvotox >= -200 && nvotox <= 200) NpVizOnTitleOffsetX = nvotox; break;
                        case "NpVizOnTitleOffsetY": hasNpVizOnLayout = true; if (int.TryParse(val, out var nvotoy) && nvotoy >= -200 && nvotoy <= 200) NpVizOnTitleOffsetY = nvotoy; break;
                        case "NpVizOnArtistOffsetX": hasNpVizOnLayout = true; if (int.TryParse(val, out var nvoaox) && nvoaox >= -200 && nvoaox <= 200) NpVizOnArtistOffsetX = nvoaox; break;
                        case "NpVizOnArtistOffsetY": hasNpVizOnLayout = true; if (int.TryParse(val, out var nvoaoy) && nvoaoy >= -200 && nvoaoy <= 200) NpVizOnArtistOffsetY = nvoaoy; break;
                        case "NpVizOnVizOffsetY": hasNpVizOnLayout = true; if (int.TryParse(val, out var nvovoy) && nvovoy >= -200 && nvovoy <= 200) NpVizOnVizOffsetY = nvovoy; break;
                        case "NpVizOnPlacement": hasNpVizOnLayout = true; if (int.TryParse(val, out var nvop) && nvop >= 0 && nvop <= 1) NpVizOnPlacement = nvop; break;
                        case "NpFullscreenVizOnCoverSize": hasNpFullscreenVizOnLayout = true; if (int.TryParse(val, out var nfvocs) && nfvocs >= 0 && nfvocs <= 900) NpFullscreenVizOnCoverSize = nfvocs; break;
                        case "NpFullscreenVizOnTitleSize": hasNpFullscreenVizOnLayout = true; if (int.TryParse(val, out var nfvots) && nfvots >= 0 && nfvots <= 72) NpFullscreenVizOnTitleSize = nfvots; break;
                        case "NpFullscreenVizOnSubTextSize": hasNpFullscreenVizOnLayout = true; if (int.TryParse(val, out var nfvoss) && nfvoss >= 0 && nfvoss <= 36) NpFullscreenVizOnSubTextSize = nfvoss; break;
                        case "NpFullscreenVizOnLyricsSize": hasNpFullscreenVizOnLayout = true; if (int.TryParse(val, out var nfvols) && nfvols >= 0 && nfvols <= 72) NpFullscreenVizOnLyricsSize = nfvols; break;
                        case "NpFullscreenVizOnVizSize": hasNpFullscreenVizOnLayout = true; if (int.TryParse(val, out var nfvovz) && nfvovz >= 0 && nfvovz <= 400) NpFullscreenVizOnVizSize = nfvovz; break;
                        case "NpFullscreenVizOnLyricsOffsetX": hasNpFullscreenVizOnLayout = true; if (int.TryParse(val, out var nfvolx) && nfvolx >= 0 && nfvolx <= 500) NpFullscreenVizOnLyricsOffsetX = nfvolx; break;
                        case "NpFullscreenVizOnCoverOffsetX": hasNpFullscreenVizOnLayout = true; if (int.TryParse(val, out var nfvocox) && nfvocox >= -200 && nfvocox <= 200) NpFullscreenVizOnCoverOffsetX = nfvocox; break;
                        case "NpFullscreenVizOnCoverOffsetY": hasNpFullscreenVizOnLayout = true; if (int.TryParse(val, out var nfvocoy) && nfvocoy >= -200 && nfvocoy <= 200) NpFullscreenVizOnCoverOffsetY = nfvocoy; break;
                        case "NpFullscreenVizOnTitleOffsetX": hasNpFullscreenVizOnLayout = true; if (int.TryParse(val, out var nfvotox) && nfvotox >= -200 && nfvotox <= 200) NpFullscreenVizOnTitleOffsetX = nfvotox; break;
                        case "NpFullscreenVizOnTitleOffsetY": hasNpFullscreenVizOnLayout = true; if (int.TryParse(val, out var nfvotoy) && nfvotoy >= -200 && nfvotoy <= 200) NpFullscreenVizOnTitleOffsetY = nfvotoy; break;
                        case "NpFullscreenVizOnArtistOffsetX": hasNpFullscreenVizOnLayout = true; if (int.TryParse(val, out var nfvoaox) && nfvoaox >= -200 && nfvoaox <= 200) NpFullscreenVizOnArtistOffsetX = nfvoaox; break;
                        case "NpFullscreenVizOnArtistOffsetY": hasNpFullscreenVizOnLayout = true; if (int.TryParse(val, out var nfvoaoy) && nfvoaoy >= -200 && nfvoaoy <= 200) NpFullscreenVizOnArtistOffsetY = nfvoaoy; break;
                        case "NpFullscreenVizOnVizOffsetY": hasNpFullscreenVizOnLayout = true; if (int.TryParse(val, out var nfvovoy) && nfvovoy >= -200 && nfvovoy <= 200) NpFullscreenVizOnVizOffsetY = nfvovoy; break;
                        case "NpFullscreenVizOnPlacement": hasNpFullscreenVizOnLayout = true; if (int.TryParse(val, out var nfvop) && nfvop >= 0 && nfvop <= 1) NpFullscreenVizOnPlacement = nfvop; break;
                        case "LoopMode": if (Enum.TryParse<LoopMode>(val, out var lm)) LoopMode = lm; break;
                        case "RenamePatternIndex": if (int.TryParse(val, out var rpi) && rpi >= 0 && rpi <= 2) RenamePatternIndex = rpi; break;
                        case "SmartRenameStyleIndex": if (int.TryParse(val, out var srsi) && srsi >= 0 && srsi <= 4) SmartRenameStyleIndex = srsi; break;
                        case "SmartRenameFolderIndex": if (int.TryParse(val, out var srfi) && srfi >= 0 && srfi <= 2) SmartRenameFolderIndex = srfi; break;
                        case "SmartRenameIncludeTrackNumbers": SmartRenameIncludeTrackNumbers = !(bool.TryParse(val, out var sritn) && !sritn); break;
                        case "SmartRenameAppendDuplicateNumbers": SmartRenameAppendDuplicateNumbers = bool.TryParse(val, out var sradn) && sradn; break;
                        case "SmartRenameRenameCleanFiles": SmartRenameRenameCleanFiles = bool.TryParse(val, out var srrcf) && srrcf; break;
                        case "DefaultCopyFolder": DefaultCopyFolder = val; break;
                        case "DefaultMoveFolder": DefaultMoveFolder = val; break;
                        case "DefaultPlaylistFolder": DefaultPlaylistFolder = val; break;
                        case "MainColorMatchEnabled": MainColorMatchEnabled = bool.TryParse(val, out var bcm) && bcm; break;
                        case "OfflineModeEnabled": OfflineModeEnabled = bool.TryParse(val, out var bom) && bom; break;
                        case "LyricsAvoidCensored": LyricsAvoidCensored = bool.TryParse(val, out var blac) && blac; break;
                        case "LastSettingsTab": if (int.TryParse(val, out var lst) && lst >= 0 && lst <= 7) LastSettingsTab = lst; break;
                        case "CrossfadeOnManualSkip": CrossfadeOnManualSkip = !(bool.TryParse(val, out var bcoms) && !bcoms); break; // default true

                    }
                }
            }
            catch (Exception ex)
            {
                if (CrashLoggingEnabled) LocalCrashLogger.Write(ex);
            }

            // Migration: "Color Drift" is no longer a mutually-exclusive background mode — it's now
            // controlled solely by the NpColorDriftBackgroundEnabled toggle (which can run under any
            // effect). Convert a legacy saved mode of "Color Drift" to Off + drift glow enabled.
            if (NormalizeNpBackgroundAnimationMode(NpBackgroundAnimationMode) == "Color Drift")
            {
                NpBackgroundAnimationMode = "Off";
                NpColorDriftBackgroundEnabled = true;
            }

            if (!hasNpFullscreenLayout)
            {
                NpFullscreenCoverSize = NpCoverSize;
                NpFullscreenTitleSize = NpTitleSize;
                NpFullscreenSubTextSize = NpSubTextSize;
                NpFullscreenLyricsSize = NpLyricsSize;
                NpFullscreenVizSize = NpVizSize;
                NpFullscreenLyricsOffsetX = NpLyricsOffsetX;
                NpFullscreenCoverOffsetX = NpCoverOffsetX;
                NpFullscreenCoverOffsetY = NpCoverOffsetY;
                NpFullscreenTitleOffsetX = NpTitleOffsetX;
                NpFullscreenTitleOffsetY = NpTitleOffsetY;
                NpFullscreenArtistOffsetX = NpArtistOffsetX;
                NpFullscreenArtistOffsetY = NpArtistOffsetY;
                NpFullscreenVizOffsetY = NpVizOffsetY;
                NpFullscreenVizPlacement = NpVizPlacement;
            }
            else if (!hasNpFullscreenVizPlacement)
            {
                NpFullscreenVizPlacement = NpVizPlacement;
            }

            if (!hasNpVizOnLayout)
            {
                NpVizOnCoverSize = NpCoverSize;
                NpVizOnTitleSize = NpTitleSize;
                NpVizOnSubTextSize = NpSubTextSize;
                NpVizOnLyricsSize = NpLyricsSize;
                NpVizOnVizSize = NpVizSize;
                NpVizOnLyricsOffsetX = NpLyricsOffsetX;
                NpVizOnCoverOffsetX = NpCoverOffsetX;
                NpVizOnCoverOffsetY = NpCoverOffsetY;
                NpVizOnTitleOffsetX = NpTitleOffsetX;
                NpVizOnTitleOffsetY = NpTitleOffsetY;
                NpVizOnArtistOffsetX = NpArtistOffsetX;
                NpVizOnArtistOffsetY = NpArtistOffsetY;
                NpVizOnVizOffsetY = NpVizOffsetY;
                NpVizOnPlacement = NpVizPlacement;
            }

            if (!hasNpFullscreenVizOnLayout)
            {
                NpFullscreenVizOnCoverSize = NpFullscreenCoverSize;
                NpFullscreenVizOnTitleSize = NpFullscreenTitleSize;
                NpFullscreenVizOnSubTextSize = NpFullscreenSubTextSize;
                NpFullscreenVizOnLyricsSize = NpFullscreenLyricsSize;
                NpFullscreenVizOnVizSize = NpFullscreenVizSize;
                NpFullscreenVizOnLyricsOffsetX = NpFullscreenLyricsOffsetX;
                NpFullscreenVizOnCoverOffsetX = NpFullscreenCoverOffsetX;
                NpFullscreenVizOnCoverOffsetY = NpFullscreenCoverOffsetY;
                NpFullscreenVizOnTitleOffsetX = NpFullscreenTitleOffsetX;
                NpFullscreenVizOnTitleOffsetY = NpFullscreenTitleOffsetY;
                NpFullscreenVizOnArtistOffsetX = NpFullscreenArtistOffsetX;
                NpFullscreenVizOnArtistOffsetY = NpFullscreenArtistOffsetY;
                NpFullscreenVizOnVizOffsetY = NpFullscreenVizOffsetY;
                NpFullscreenVizOnPlacement = NpFullscreenVizPlacement;
            }

            // Existing config from before NP search had its own slots: copy the
            // user's main-window services across once so NP isn't blank.
            SeedNpSearchServicesFromMain();

            // Load sensitive Last.fm data from Documents
            LoadSensitiveData();
            ApplyScanPerformanceDefaultsMigration();
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
                RipQualityEnabled = false;

                AudioAnalyzer.EnableSilenceDetection = false;
                AudioAnalyzer.EnableDynamicRange = false;
                AudioAnalyzer.EnableTruePeak = false;
                AudioAnalyzer.EnableLufs = false;
                AudioAnalyzer.EnableBpmDetection = false;
                AudioAnalyzer.EnableRipQuality = false;
            }

            SyncHiddenColumnsWithAnalysisOptions(applyDefaultHiddenColumns: string.IsNullOrWhiteSpace(HiddenColumns));
            ScanPerformanceDefaultsVersion = CurrentScanPerformanceDefaultsVersion;
            SavePlayOptions();
        }
    }
}
