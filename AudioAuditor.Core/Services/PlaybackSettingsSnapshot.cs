using AudioQualityChecker.Abstractions;

namespace AudioQualityChecker.Services
{
    public sealed record PlaybackSettingsSnapshot : IPlaybackSettings
    {
        public bool CrossfadeEnabled { get; init; }
        public int CrossfadeDurationSeconds { get; init; }
        public PlaybackCrossfadeCurve CrossfadeCurve { get; init; }
        public bool CrossfadeOnManualSkip { get; init; }
        public bool GaplessEnabled { get; init; }
        public bool AudioNormalization { get; init; }
        public bool EqualizerEnabled { get; init; }
        public IReadOnlyList<float> EqualizerGains { get; init; } = Array.Empty<float>();
        public bool SpatialAudioEnabled { get; init; }
        public PlaybackLoopMode LoopMode { get; init; }

        public static PlaybackSettingsSnapshot From(IPlaybackSettings settings)
        {
            ArgumentNullException.ThrowIfNull(settings);
            return new PlaybackSettingsSnapshot
            {
                CrossfadeEnabled = settings.CrossfadeEnabled,
                CrossfadeDurationSeconds = settings.CrossfadeDurationSeconds,
                CrossfadeCurve = settings.CrossfadeCurve,
                CrossfadeOnManualSkip = settings.CrossfadeOnManualSkip,
                GaplessEnabled = settings.GaplessEnabled,
                AudioNormalization = settings.AudioNormalization,
                EqualizerEnabled = settings.EqualizerEnabled,
                EqualizerGains = settings.EqualizerGains.ToArray(),
                SpatialAudioEnabled = settings.SpatialAudioEnabled,
                LoopMode = settings.LoopMode
            };
        }
    }
}
