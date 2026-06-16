using System;
using System.Collections.Generic;
using System.Linq;
using AudioQualityChecker.Abstractions;

namespace AudioQualityChecker.Services
{
    public sealed class ThemeManagerSettings :
        IAppearanceSettings,
        IPlaybackSettings,
        IAnalysisSettings,
        ICredentialStore
    {
        public string CurrentTheme => ThemeManager.CurrentTheme;
        public string CurrentPlaybarTheme => ThemeManager.CurrentPlaybarTheme;

        public bool CrossfadeEnabled => ThemeManager.Crossfade;
        public int CrossfadeDurationSeconds => ThemeManager.CrossfadeDuration;
        public PlaybackCrossfadeCurve CrossfadeCurve => ToPlaybackCurve(ThemeManager.CrossfadeCurve);
        public bool CrossfadeOnManualSkip => ThemeManager.CrossfadeOnManualSkip;
        public bool GaplessEnabled => ThemeManager.GaplessEnabled;
        public bool AudioNormalization => ThemeManager.AudioNormalization;
        public bool EqualizerEnabled => ThemeManager.EqualizerEnabled;
        public IReadOnlyList<float> EqualizerGains => ThemeManager.EqualizerGains;
        public bool SpatialAudioEnabled => ThemeManager.SpatialAudioEnabled;
        public PlaybackLoopMode LoopMode => ThemeManager.LoopMode switch
        {
            AudioQualityChecker.Services.LoopMode.All => PlaybackLoopMode.All,
            AudioQualityChecker.Services.LoopMode.One => PlaybackLoopMode.One,
            _ => PlaybackLoopMode.Off
        };

        public bool EnableBpmDetection => ThemeManager.BpmDetectionEnabled;
        public bool EnableExperimentalAi => ThemeManager.ExperimentalAiDetection;
        public bool EnableRipQuality => ThemeManager.RipQualityEnabled;
        public bool EnableSilenceDetection => ThemeManager.SilenceDetectionEnabled;
        public bool EnableFakeStereoDetection => ThemeManager.FakeStereoDetectionEnabled;
        public bool EnableDynamicRange => ThemeManager.DynamicRangeEnabled;
        public bool EnableTruePeak => ThemeManager.TruePeakEnabled;
        public bool EnableLufs => ThemeManager.LufsEnabled;
        public bool EnableClippingDetection => ThemeManager.ClippingDetectionEnabled;
        public bool EnableMqaDetection => ThemeManager.MqaDetectionEnabled;
        public bool EnableDefaultAiDetection => ThemeManager.DefaultAiDetectionEnabled;
        public bool AlwaysFullAnalysis => ThemeManager.AlwaysFullAnalysis;
        public bool FrequencyCutoffAllowEnabled => ThemeManager.FrequencyCutoffAllowEnabled;
        public int FrequencyCutoffAllowHz => ThemeManager.FrequencyCutoffAllowHz;

        public SilenceSettings Silence => new(
            ThemeManager.SilenceMinGapEnabled,
            ThemeManager.SilenceMinGapSeconds,
            ThemeManager.SilenceSkipEdgesEnabled,
            ThemeManager.SilenceSkipEdgeSeconds);

        public PlaybarPalette GetPlaybarPalette()
        {
            var colors = ThemeManager.GetPlaybarColors();
            return new PlaybarPalette(
                ToAppColor(colors.BackgroundColor),
                colors.ProgressGradient.Select(ToAppColor).ToArray(),
                colors.AnimationSpeed);
        }

        public string? GetApiKey(string service)
        {
            return NormalizeServiceName(service) switch
            {
                "lastfm" or "lastfmapikey" => EmptyToNull(ThemeManager.LastFmApiKey),
                "lastfmapisecret" => EmptyToNull(ThemeManager.LastFmApiSecret),
                "lastfmsessionkey" => EmptyToNull(ThemeManager.LastFmSessionKey),
                "librefm" or "librefmapikey" => EmptyToNull(ThemeManager.LibreFmApiKey),
                "librefmapisecret" => EmptyToNull(ThemeManager.LibreFmApiSecret),
                "librefmsessionkey" => EmptyToNull(ThemeManager.LibreFmSessionKey),
                "listenbrainz" or "listenbrainztoken" => EmptyToNull(ThemeManager.ListenBrainzUserToken),
                "discord" or "discordrpc" or "discordrpcclientid" => EmptyToNull(ThemeManager.DiscordRpcClientId),
                "acoustid" or "acoustidapikey" => EmptyToNull(ThemeManager.AcoustIdApiKey),
                "shlabs" or "shlabsapikey" => EmptyToNull(ThemeManager.SHLabsCustomApiKey),
                _ => null
            };
        }

        public void SetApiKey(string service, string? key)
        {
            var value = key ?? string.Empty;
            switch (NormalizeServiceName(service))
            {
                case "lastfm":
                case "lastfmapikey":
                    ThemeManager.LastFmApiKey = value;
                    break;
                case "lastfmapisecret":
                    ThemeManager.LastFmApiSecret = value;
                    break;
                case "lastfmsessionkey":
                    ThemeManager.LastFmSessionKey = value;
                    break;
                case "librefm":
                case "librefmapikey":
                    ThemeManager.LibreFmApiKey = value;
                    break;
                case "librefmapisecret":
                    ThemeManager.LibreFmApiSecret = value;
                    break;
                case "librefmsessionkey":
                    ThemeManager.LibreFmSessionKey = value;
                    break;
                case "listenbrainz":
                case "listenbrainztoken":
                    ThemeManager.ListenBrainzUserToken = value;
                    break;
                case "discord":
                case "discordrpc":
                case "discordrpcclientid":
                    ThemeManager.DiscordRpcClientId = value;
                    break;
                case "acoustid":
                case "acoustidapikey":
                    ThemeManager.AcoustIdApiKey = value;
                    break;
                case "shlabs":
                case "shlabsapikey":
                    ThemeManager.SHLabsCustomApiKey = value;
                    break;
                default:
                    throw new ArgumentException($"Unsupported credential service '{service}'.", nameof(service));
            }

            ThemeManager.SavePlayOptions();
        }

        private static PlaybackCrossfadeCurve ToPlaybackCurve(CrossfadeType curve) => curve switch
        {
            CrossfadeType.Linear => PlaybackCrossfadeCurve.Linear,
            CrossfadeType.Natural => PlaybackCrossfadeCurve.Natural,
            CrossfadeType.Sequential => PlaybackCrossfadeCurve.Sequential,
            CrossfadeType.SmoothStep => PlaybackCrossfadeCurve.SmoothStep,
            CrossfadeType.FastFade => PlaybackCrossfadeCurve.FastFade,
            CrossfadeType.SlowBlend => PlaybackCrossfadeCurve.SlowBlend,
            _ => PlaybackCrossfadeCurve.EqualPower
        };

        private static AppColor ToAppColor(Color color) => new(color.A, color.R, color.G, color.B);

        private static string NormalizeServiceName(string service)
        {
            return new string((service ?? string.Empty)
                .Where(char.IsLetterOrDigit)
                .Select(char.ToLowerInvariant)
                .ToArray());
        }

        private static string? EmptyToNull(string value) => string.IsNullOrWhiteSpace(value) ? null : value;
    }
}
