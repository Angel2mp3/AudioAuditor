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
        /// <summary>Audio file extensions accepted for analysis.</summary>
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
        };

        /// <summary>Archive extensions whose audio contents are extracted and analyzed.</summary>
        public static readonly IReadOnlySet<string> ArchiveExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".zip", ".rar", ".7z", ".tar", ".gz", ".bz2", ".xz", ".tgz"
        };
    }
}
