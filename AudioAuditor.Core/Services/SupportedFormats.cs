using System;
using System.Collections.Generic;

namespace AudioQualityChecker.Services
{
    /// <summary>
    /// Canonical file-extension sets the app recognizes. Single source of truth shared by the
    /// WPF GUI and the CLI so the two can never silently drift apart again. All lookups are
    /// case-insensitive and expect a leading dot (e.g. ".flac").
    /// </summary>
    public static class SupportedFormats
    {
        /// <summary>
        /// Audio file extensions the app accepts. Membership here means the file can be loaded,
        /// have its tags read and edited, and be renamed — it does NOT promise the audio can be
        /// decoded. See <see cref="AnalysisUnsupportedExtensions"/> for the ones that cannot.
        /// </summary>
        public static readonly IReadOnlySet<string> AudioExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            // Common formats
            ".mp3", ".flac", ".wav", ".ogg", ".aac", ".m4a", ".wma",
            ".aiff", ".aif", ".ape", ".wv", ".opus", ".alac", ".dsf", ".dff",
            // Rare formats
            ".tta", ".mpc", ".spx", ".mp+",
            ".mp2",   // MPEG Layer II
            ".m4b",   // M4A audiobook container
            ".m4r",   // iPhone ringtone (M4A)
            ".mp4",   // MPEG-4 audio container
            ".3gp", ".3g2",   // 3GPP/3GPP2 mobile audio
            ".amr",   // Adaptive Multi-Rate (voice/mobile)
            ".ac3",   // Dolby AC-3 / Dolby Digital
            ".mka",   // Matroska audio container
            ".webm",  // WebM audio (Opus/Vorbis)
            ".tak",   // Tom's lossless Audio Kompressor
            ".au", ".snd",   // Sun/NeXT audio (legacy Unix)
            ".bwf", ".rf64", // Broadcast WAV / RF64 — WaveFileReader handles both
            ".fla",   // early FLAC extension — the managed FLAC decoder reads it
        };

        /// <summary>
        /// Extensions with no working audio decoder on this machine: tags, duration, and the rest of
        /// the metadata tooling work, but there is no waveform to analyze, so quality scanning
        /// reports metadata only rather than a bogus verdict.
        ///
        /// This depends on the machine, not just the build. <c>OpenAudioFile</c> ends its chain with
        /// an ffmpeg fallback that decodes every one of these, so when ffmpeg is installed the set is
        /// empty and a failure here means a genuinely broken file rather than a missing decoder.
        /// </summary>
        public static IReadOnlySet<string> AnalysisUnsupportedExtensions =>
            FfmpegDecoder.IsAvailable ? EmptyExtensions : NoDecoderWithoutFfmpeg;

        private static readonly IReadOnlySet<string> EmptyExtensions =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// The baseline when no ffmpeg is present. Determined by measurement, not assumption — one
        /// generated sample per extension was run through the scanner and these are the ones that
        /// produced no spectrum. APE, TAK and Musepack are included on code inspection instead: no
        /// encoder exists to generate a test sample, and no in-process decoder handles them.
        /// </summary>
        private static readonly IReadOnlySet<string> NoDecoderWithoutFfmpeg = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".wv",            // WavPack — no decoder
            ".tta",           // True Audio — the MediaFoundation codec is not present
            ".ape",           // Monkey's Audio — no decoder
            ".tak",           // Tom's lossless Audio Kompressor — no decoder
            ".mpc", ".mp+",   // Musepack — codec not installed
            ".spx",           // Speex — no decoder
            ".ac3",           // raw AC-3 elementary stream
            ".3gp", ".3g2",   // 3GPP containers (the AMR inside them is not decoded)
            ".au", ".snd",    // Sun/NeXT audio
#if CROSS_PLATFORM
            // Media Foundation decodes these on Windows but does not exist on Linux/macOS, so
            // without ffmpeg they have no decoder either. Listing them here is what turns a
            // misleading "cannot decode audio data" into the accurate "install ffmpeg".
            ".aac", ".m4a", ".m4b", ".m4r", ".mp4", ".alac", ".wma", ".amr", ".mka", ".webm",
#endif
        };

        /// <summary>Archive extensions whose audio contents are extracted and analyzed.</summary>
        public static readonly IReadOnlySet<string> ArchiveExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".zip", ".rar", ".7z", ".tar", ".gz", ".bz2", ".xz", ".tgz"
        };

        /// <summary>Playlist extensions that are expanded into the tracks they reference.</summary>
        public static readonly IReadOnlySet<string> PlaylistExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".m3u", ".m3u8", ".pls"
        };
    }
}
