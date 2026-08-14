using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AudioQualityChecker.Models;

namespace AudioQualityChecker.Services;

public enum AudioConversionFormat
{
    Mp3,
    Flac,
    Wav,
    Aac,
    Ogg,
    Opus,
    Wma,
    Aiff
}

public sealed class AudioConversionOptions
{
    public AudioConversionFormat TargetFormat { get; set; } = AudioConversionFormat.Mp3;

    /// <summary>MP3 VBR quality for libmp3lame: 0 (best) … 9 (smallest). 2 ≈ ~190 kbps.</summary>
    public int Mp3Quality { get; set; } = 2;

    /// <summary>Vorbis quality for libvorbis: 0 … 10.</summary>
    public int OggQuality { get; set; } = 6;

    /// <summary>Target bitrate (kbps) for the lossy CBR-ish codecs (AAC, Opus, WMA).</summary>
    public int BitrateKbps { get; set; } = 256;

    /// <summary>Output folder; empty means "same folder as each source file".</summary>
    public string OutputFolder { get; set; } = "";

    public bool KeepMetadata { get; set; } = true;
    public bool Overwrite { get; set; }
    public bool DeleteOriginal { get; set; }

    public int MaxConcurrency { get; set; } = 2;

    public string Extension => TargetFormat switch
    {
        AudioConversionFormat.Mp3 => "mp3",
        AudioConversionFormat.Flac => "flac",
        AudioConversionFormat.Wav => "wav",
        AudioConversionFormat.Aac => "m4a",
        AudioConversionFormat.Ogg => "ogg",
        AudioConversionFormat.Opus => "opus",
        AudioConversionFormat.Wma => "wma",
        AudioConversionFormat.Aiff => "aiff",
        _ => "mp3"
    };
}

public sealed class AudioConversionResult
{
    public int Converted { get; set; }
    public int Skipped { get; set; }
    public int Failed { get; set; }
    public List<string> Errors { get; } = new();
    public List<string> OutputPaths { get; } = new();
}

/// <summary>
/// Converts audio files between formats by invoking a bundled (or system) <c>ffmpeg</c> as a
/// separate process. Running ffmpeg as an external program (never linking it) keeps this project's
/// Apache-2.0 license intact while using the LGPL ffmpeg build. Metadata and embedded cover art are
/// carried over with <c>-map_metadata 0</c> and stream copying where the target container supports it.
/// </summary>
public sealed class AudioConversionService
{
    private static string? _cachedFfmpegPath;
    private static volatile bool _searched;
    private static readonly object _searchLock = new();

    /// <summary>
    /// Locates ffmpeg: bundled beside the app first, then on PATH. Null if unavailable.
    ///
    /// The result is published only once the search has finished. Setting the "searched" flag up
    /// front instead let a concurrent caller read back a null path while the first thread was still
    /// probing — invisible while only the Convert button called this, but analysis now asks on every
    /// worker thread, and a lost race there silently downgrades a file to "no decoder".
    /// </summary>
    public static string? FindFfmpeg()
    {
        if (_searched) return _cachedFfmpegPath;

        lock (_searchLock)
        {
            if (_searched) return _cachedFfmpegPath;
            _cachedFfmpegPath = Locate();
            _searched = true;
            return _cachedFfmpegPath;
        }
    }

    private static string? Locate()
    {
        string exe = OperatingSystem.IsWindows() ? "ffmpeg.exe" : "ffmpeg";
        var candidates = new List<string>();
        foreach (var baseDir in AppPaths.SidecarSearchDirectories)
        {
            candidates.Add(Path.Combine(baseDir, exe));
            candidates.Add(Path.Combine(baseDir, "ffmpeg", exe));
            candidates.Add(Path.Combine(baseDir, "third-party", "ffmpeg", exe));
        }

        foreach (var candidate in candidates)
        {
            if (File.Exists(candidate)) return candidate;
        }

        // Fall back to PATH — verify it actually runs.
        return CanRun(exe) ? exe : null;
    }

    public static bool IsAvailable => FindFfmpeg() != null;

    /// <summary>
    /// The folder a user should drop ffmpeg.exe into. Anchored to the executable rather than to
    /// <see cref="AppContext.BaseDirectory"/>: in the single-file build the latter is a temp
    /// extraction folder that is wiped, so a binary placed there would vanish.
    /// </summary>
    public static string BundledFfmpegFolder =>
        Path.Combine(AppPaths.ExecutableDirectory, "third-party", "ffmpeg");

    /// <summary>Where to get a build (pick an LGPL one — see third-party/ffmpeg/README.txt).</summary>
    public const string DownloadPageUrl = "https://ffmpeg.org/download.html";

    /// <summary>
    /// Clears the cached lookup so a user who just installed ffmpeg (or dropped the binary into
    /// <see cref="BundledFfmpegFolder"/>) is picked up without restarting the app.
    /// </summary>
    public static void ResetCache()
    {
        lock (_searchLock)
        {
            _searched = false;
            _cachedFfmpegPath = null;
        }
    }

    private static bool CanRun(string fileName)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = "-version",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            });
            if (process == null) return false;
            process.WaitForExit(4000);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public async Task<AudioConversionResult> ConvertAsync(
        IReadOnlyList<AudioFileInfo> files,
        AudioConversionOptions options,
        IProgress<(int done, int total, string fileName)>? progress = null,
        CancellationToken ct = default)
    {
        var result = new AudioConversionResult();
        string? ffmpeg = FindFfmpeg();
        if (ffmpeg == null)
        {
            result.Errors.Add("ffmpeg was not found. Bundle ffmpeg.exe with the app or install it on your PATH.");
            return result;
        }

        int done = 0;
        using var gate = new SemaphoreSlim(Math.Max(1, options.MaxConcurrency));
        var sync = new object();
        // Targets already spoken for by this batch. Same idea as SmartRenameService's usedTargets.
        var claimedTargets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        async Task ProcessAsync(AudioFileInfo file)
        {
            await gate.WaitAsync(ct);
            try
            {
                ct.ThrowIfCancellationRequested();
                await ConvertOneAsync(ffmpeg, file, options, result, sync, claimedTargets, ct);
            }
            finally
            {
                int n = Interlocked.Increment(ref done);
                progress?.Report((n, files.Count, file.FileName));
                gate.Release();
            }
        }

        await Task.WhenAll(files.Select(ProcessAsync));
        return result;
    }

    private static async Task ConvertOneAsync(
        string ffmpeg,
        AudioFileInfo file,
        AudioConversionOptions options,
        AudioConversionResult result,
        object sync,
        HashSet<string> claimedTargets,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(file.FilePath) || !File.Exists(file.FilePath))
        {
            lock (sync) { result.Failed++; result.Errors.Add($"{file.FileName}: source not found"); }
            return;
        }

        string sourceDir = Path.GetDirectoryName(file.FilePath) ?? "";
        string outDir = string.IsNullOrWhiteSpace(options.OutputFolder) ? sourceDir : options.OutputFolder;
        string target = ResolveTargetPath(file, options);

        // Don't let the target collide with the source (e.g. converting mp3 → mp3 in place).
        if (string.Equals(target, file.FilePath, StringComparison.OrdinalIgnoreCase))
        {
            lock (sync) { result.Skipped++; }
            return;
        }

        if (File.Exists(target) && !options.Overwrite)
        {
            lock (sync) { result.Skipped++; }
            return;
        }

        // Claim the target before doing any work. With an output folder set, two sources with the
        // same base name from different directories resolve to one target — and with Overwrite on,
        // the second conversion clobbered the first's output and DeleteOriginal then removed both
        // originals, losing a file outright. Two workers could also race the same path.
        lock (sync)
        {
            if (!claimedTargets.Add(target))
            {
                result.Skipped++;
                result.Errors.Add(
                    $"{file.FileName}: another file in this batch already converts to \"{Path.GetFileName(target)}\" — " +
                    "convert into separate folders, or rename one of them first");
                return;
            }
        }

        try
        {
            if (!string.IsNullOrWhiteSpace(outDir)) Directory.CreateDirectory(outDir);

            var args = BuildArgs(file.FilePath, target, options, file.BitsPerSample);
            var psi = new ProcessStartInfo
            {
                FileName = ffmpeg,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            foreach (var arg in args) psi.ArgumentList.Add(arg);

            using var process = new Process { StartInfo = psi };
            process.Start();
            // Drain both pipes concurrently. Reading only one risks the other's buffer filling up
            // and blocking ffmpeg forever, which would hang the whole conversion.
            var stderrTask = process.StandardError.ReadToEndAsync(ct);
            var stdoutTask = process.StandardOutput.ReadToEndAsync(ct);
            await Task.WhenAll(stderrTask, stdoutTask);
            string stderr = await stderrTask;
            await process.WaitForExitAsync(ct);

            if (process.ExitCode == 0 && File.Exists(target))
            {
                lock (sync) { result.Converted++; result.OutputPaths.Add(target); }
                if (options.DeleteOriginal)
                {
                    try { File.Delete(file.FilePath); } catch { /* leave the original if it can't be removed */ }
                }
            }
            else
            {
                if (File.Exists(target)) { try { File.Delete(target); } catch { } }
                lock (sync) { result.Failed++; result.Errors.Add($"{file.FileName}: {LastLine(stderr)}"); }
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            lock (sync) { result.Failed++; result.Errors.Add($"{file.FileName}: {ex.Message}"); }
        }
    }

    /// <summary>
    /// Where a file converts to. An empty <see cref="AudioConversionOptions.OutputFolder"/> means
    /// "beside the source" — otherwise every source lands in one folder, which is where same-named
    /// files from different directories collide.
    /// </summary>
    internal static string ResolveTargetPath(AudioFileInfo file, AudioConversionOptions options)
    {
        string sourceDir = Path.GetDirectoryName(file.FilePath) ?? "";
        string outDir = string.IsNullOrWhiteSpace(options.OutputFolder) ? sourceDir : options.OutputFolder;
        return Path.Combine(outDir, Path.GetFileNameWithoutExtension(file.FilePath) + "." + options.Extension);
    }

    /// <summary>Assert-based checks for the pure conversion helpers (no test framework in this repo).</summary>
    public static void SelfCheck()
    {
        // "Lossless" targets must carry the source's bit depth, not a hardcoded 16.
        Assert(PcmCodec(24, bigEndian: false) == "pcm_s24le", "a 24-bit source must convert to 24-bit WAV");
        Assert(PcmCodec(16, bigEndian: false) == "pcm_s16le", "a 16-bit source stays 16-bit");
        Assert(PcmCodec(32, bigEndian: false) == "pcm_s32le", "a 32-bit source stays 32-bit");
        Assert(PcmCodec(24, bigEndian: true) == "pcm_s24be", "AIFF is big-endian");
        Assert(PcmCodec(0, bigEndian: false) == "pcm_s24le", "an unprobed source must not be truncated to 16-bit");

        // Two same-named sources with a shared output folder resolve to one target — the collision
        // the batch's claimed-target set exists to catch before DeleteOriginal removes both originals.
        var options = new AudioConversionOptions { TargetFormat = AudioConversionFormat.Mp3, OutputFolder = @"C:\out" };
        var a = new AudioFileInfo { FilePath = @"C:\a\song.flac" };
        var b = new AudioFileInfo { FilePath = @"C:\b\song.flac" };
        Assert(ResolveTargetPath(a, options) == ResolveTargetPath(b, options),
            "same-named sources in one output folder must be recognised as colliding");

        var claimed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        Assert(claimed.Add(ResolveTargetPath(a, options)), "the first file claims the target");
        Assert(!claimed.Add(ResolveTargetPath(b, options)), "the second file must fail to claim it");

        // Beside-the-source conversion keeps them apart.
        var inPlace = new AudioConversionOptions { TargetFormat = AudioConversionFormat.Mp3 };
        Assert(ResolveTargetPath(a, inPlace) != ResolveTargetPath(b, inPlace),
            "without an output folder the two targets stay in their own directories");

        static void Assert(bool ok, string what)
        {
            if (!ok) throw new Exception("AudioConversionService.SelfCheck failed: " + what);
        }
    }

    /// <summary>
    /// PCM codec matching the source's bit depth. WAV and AIFF are offered as the "Lossless" targets,
    /// so hardcoding 16-bit silently threw away 8 bits of every 24-bit master. An unknown depth
    /// (0, i.e. the source was never probed) takes 24-bit: padding a 16-bit source costs disk space,
    /// truncating a 24-bit one costs the thing this app exists to measure.
    /// </summary>
    internal static string PcmCodec(int sourceBitsPerSample, bool bigEndian)
    {
        string width = sourceBitsPerSample switch
        {
            <= 0 => "s24",
            <= 16 => "s16",
            <= 24 => "s24",
            _ => "s32"
        };
        return "pcm_" + width + (bigEndian ? "be" : "le");
    }

    private static List<string> BuildArgs(
        string source, string target, AudioConversionOptions options, int sourceBitsPerSample)
    {
        // -nostdin: ffmpeg inherits this process's stdin otherwise, and in the CLI that means it can
        // swallow keystrokes meant for the console prompt.
        var args = new List<string> { "-nostdin", options.Overwrite ? "-y" : "-n", "-i", source };

        // Carry tags. Map the audio stream, plus an optional attached cover where the target supports it.
        if (options.KeepMetadata)
            args.AddRange(new[] { "-map_metadata", "0" });

        // Ogg is deliberately absent: Vorbis carries artwork as a base64 METADATA_BLOCK_PICTURE
        // comment, not as a mapped video stream, and "-c:v copy" into an Ogg container fails the
        // whole conversion for any source that has embedded art.
        bool coverCapable = options.TargetFormat is AudioConversionFormat.Mp3
            or AudioConversionFormat.Flac or AudioConversionFormat.Aac;

        args.AddRange(new[] { "-map", "0:a:0" });
        if (coverCapable)
            args.AddRange(new[] { "-map", "0:v:0?", "-c:v", "copy" });

        switch (options.TargetFormat)
        {
            case AudioConversionFormat.Mp3:
                args.AddRange(new[] { "-c:a", "libmp3lame", "-q:a", Clamp(options.Mp3Quality, 0, 9).ToString() });
                if (options.KeepMetadata) args.AddRange(new[] { "-id3v2_version", "3" });
                break;
            case AudioConversionFormat.Flac:
                args.AddRange(new[] { "-c:a", "flac" });
                break;
            case AudioConversionFormat.Wav:
                args.AddRange(new[] { "-c:a", PcmCodec(sourceBitsPerSample, bigEndian: false) });
                break;
            case AudioConversionFormat.Aac:
                args.AddRange(new[] { "-c:a", "aac", "-b:a", $"{Clamp(options.BitrateKbps, 32, 512)}k" });
                break;
            case AudioConversionFormat.Ogg:
                args.AddRange(new[] { "-c:a", "libvorbis", "-q:a", Clamp(options.OggQuality, 0, 10).ToString() });
                break;
            case AudioConversionFormat.Opus:
                args.AddRange(new[] { "-c:a", "libopus", "-b:a", $"{Clamp(options.BitrateKbps, 32, 512)}k" });
                break;
            case AudioConversionFormat.Wma:
                args.AddRange(new[] { "-c:a", "wmav2", "-b:a", $"{Clamp(options.BitrateKbps, 32, 512)}k" });
                break;
            case AudioConversionFormat.Aiff:
                args.AddRange(new[] { "-c:a", PcmCodec(sourceBitsPerSample, bigEndian: true) });
                break;
        }

        args.Add(target);
        return args;
    }

    private static int Clamp(int value, int min, int max) => Math.Max(min, Math.Min(max, value));

    private static string LastLine(string stderr)
    {
        if (string.IsNullOrWhiteSpace(stderr)) return "conversion failed";
        var line = stderr.Replace("\r", "")
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .LastOrDefault();
        return string.IsNullOrWhiteSpace(line) ? "conversion failed" : line;
    }
}
