using System.Collections.Generic;

namespace AudioQualityChecker.Abstractions;

public enum PlaybackCrossfadeCurve
{
    Linear,
    EqualPower,
    Natural,
    Sequential,
    SmoothStep,
    FastFade,
    SlowBlend
}

public enum PlaybackLoopMode
{
    Off,
    All,
    One
}

public readonly record struct AppColor(byte A, byte R, byte G, byte B);

public sealed record PlaybarPalette(
    AppColor BackgroundColor,
    IReadOnlyList<AppColor> ProgressGradient,
    double AnimationSpeed);

public sealed record SilenceSettings(
    bool MinGapEnabled,
    double MinGapSeconds,
    bool SkipEdgesEnabled,
    double SkipEdgeSeconds);

public interface IAppearanceSettings
{
    string CurrentTheme { get; }
    string CurrentPlaybarTheme { get; }
    PlaybarPalette GetPlaybarPalette();
}

public interface IPlaybackSettings
{
    bool CrossfadeEnabled { get; }
    int CrossfadeDurationSeconds { get; }
    PlaybackCrossfadeCurve CrossfadeCurve { get; }
    bool CrossfadeOnManualSkip { get; }
    bool GaplessEnabled { get; }
    bool AudioNormalization { get; }
    bool EqualizerEnabled { get; }
    IReadOnlyList<float> EqualizerGains { get; }
    bool SpatialAudioEnabled { get; }
    PlaybackLoopMode LoopMode { get; }
}

public interface IAnalysisSettings
{
    bool EnableBpmDetection { get; }
    bool EnableExperimentalAi { get; }
    bool EnableRipQuality { get; }
    bool EnableSilenceDetection { get; }
    bool EnableFakeStereoDetection { get; }
    bool EnableDynamicRange { get; }
    bool EnableTruePeak { get; }
    bool EnableLufs { get; }
    bool EnableClippingDetection { get; }
    bool EnableMqaDetection { get; }
    bool EnableDefaultAiDetection { get; }
    bool AlwaysFullAnalysis { get; }
    bool FrequencyCutoffAllowEnabled { get; }
    int FrequencyCutoffAllowHz { get; }
    SilenceSettings Silence { get; }
}

public interface ICredentialStore
{
    string? GetApiKey(string service);
    void SetApiKey(string service, string? key);
}

public interface IAppDataStore
{
    bool Exists(string relativePath);
    string? ReadText(string relativePath);
    void WriteText(string relativePath, string contents);
}
