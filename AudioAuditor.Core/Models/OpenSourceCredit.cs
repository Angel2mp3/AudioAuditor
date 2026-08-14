namespace AudioQualityChecker.Models;

/// <summary>
/// One open-source library/runtime credited in the GUI's Credits window and the CLI's
/// `credits` command. Keep this list in sync with the "Technology" table in README.md.
/// </summary>
public readonly record struct OpenSourceCredit(string Name, string By, string License, string Usage, string Url, string NoticeFile)
{
    // Shipped runtime libraries and bundled assets only — mirrors README.md. (Build/test tooling
    // and the web build are intentionally excluded.)
    // NoticeFile = the bundled license file name embedded under Third.Party.Notices.
    // One list covers both frontends, so it spans everything the project ships: a few entries
    // are GUI-only (WPF, Discord RPC, DPAPI), SkiaSharp is CLI-only, and the two fonts ship only
    // in the cross-platform build. Callers that render it say so rather than filtering, since
    // every entry is a real dependency of the project.
    //
    // The fonts are here because that build redistributes their files; OFL 1.1 requires the
    // copyright notice and license travel with them. The Windows font picker needs no entry —
    // it only names families already installed on the user's machine and ships no font data.
    public static readonly OpenSourceCredit[] All =
    {
        new("NAudio", "Mark Heath", "MIT",
            "Audio playback, waveform reading, the sample-provider pipeline, FFT analysis, crossfade mixing, and all audio I/O.",
            "https://github.com/naudio/NAudio", "NAudio-LICENSE.txt"),
        new("NAudio.Vorbis", "Andrew Ward", "MIT",
            "OGG Vorbis audio file decoding and playback support.",
            "https://github.com/naudio/Vorbis", "NAudio.Vorbis-LICENSE.txt"),
        new("NLayer", "Mark Heath & Andrew Ward", "MIT",
            "MPEG/MP3 decoder — enables MP3 analysis on Linux/macOS (and as a Windows fallback) where Media Foundation isn't available.",
            "https://github.com/naudio/NLayer", "NLayer-LICENSE.txt"),
        new("Concentus & Concentus.OggFile", "Logan Stromberg", "MIT / BSD",
            "Decodes .opus files.",
            "https://github.com/lostromb/concentus", "Concentus-LICENSE.txt"),
        new("TagLib#", "Mono Project", "LGPL-2.1",
            "Reading and writing audio metadata tags across all supported formats (ID3v2, Xiph Comment, APEv2, M4A atoms).",
            "https://github.com/mono/taglib-sharp", "TagLibSharp-LICENSE.txt"),
        new("ClosedXML", "ClosedXML Contributors", "MIT",
            "Excel workbook generation with styled cells, headers, and auto-fit columns.",
            "https://github.com/ClosedXML/ClosedXML", "ClosedXML-LICENSE.txt"),
        new("discord-rpc-csharp", "Lachee", "MIT",
            "Discord Rich Presence client for showing playback status.",
            "https://github.com/Lachee/discord-rpc-csharp", "discord-rpc-csharp-LICENSE.txt"),
        new("SharpCompress", "Adam Hathcock", "MIT",
            "Archive extraction support (ZIP, RAR, 7Z, TAR).",
            "https://github.com/adamhathcock/sharpcompress", "SharpCompress-LICENSE.txt"),
        new("SkiaSharp", "Mono Project / Microsoft", "MIT",
            "Cross-platform 2D rendering — draws the CLI's spectrogram PNGs on Windows, Linux, and macOS.",
            "https://github.com/mono/SkiaSharp", "SkiaSharp-LICENSE.txt"),
        new("FFmpeg", "FFmpeg team", "LGPL-2.1",
            "Audio format conversion in the Batch Editor's Convert tab, and the fallback decoder for analysis — it is what makes AAC/M4A, ALAC, WMA, APE, WavPack, TAK, Musepack and Speex readable, including on Linux and macOS where Media Foundation isn't available.",
            "https://ffmpeg.org", "ffmpeg-LICENSE.txt"),
        new("cambia", "arg274", "MIT",
            "Powers the CD Rip Checker — parses EAC, XLD, and whipper rip logs and scores them with the OPS deduction model.",
            "https://github.com/arg274/cambia", "cambia-LICENSE.txt"),
        new("Selawik", "Microsoft", "SIL OFL 1.1",
            "Metric-compatible Segoe UI substitute — the bundled UI font on Linux and macOS builds, where Segoe UI cannot be redistributed and so is absent.",
            "https://github.com/microsoft/Selawik", "Selawik-LICENSE.txt"),
        new("Inter", "The Inter Project Authors", "SIL OFL 1.1",
            "Bundled UI typeface for the cross-platform build.",
            "https://github.com/rsms/inter", "Inter-LICENSE.txt"),
        new("System.Security.Cryptography.ProtectedData", "Microsoft", "MIT",
            "Encrypts your saved scrobbler credentials using Windows DPAPI.",
            "https://www.nuget.org/packages/System.Security.Cryptography.ProtectedData", "dotnet-runtime-LICENSE.txt"),
        new(".NET 8", "Microsoft", "MIT",
            "Application runtime.",
            "https://github.com/dotnet/runtime", "dotnet-runtime-LICENSE.txt"),
        new("WPF", "Microsoft", "MIT",
            "UI framework — all windows, controls, data binding, styling, and rendering.",
            "https://github.com/dotnet/wpf", "dotnet-wpf-LICENSE.txt"),
    };
}
