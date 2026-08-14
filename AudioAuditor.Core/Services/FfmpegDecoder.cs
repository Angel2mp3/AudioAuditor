using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace AudioQualityChecker.Services
{
    /// <summary>
    /// Last-resort audio decoder that shells out to <c>ffmpeg</c>, decoding the file to a temporary
    /// 32-bit float WAV and handing back a seekable <see cref="WaveStream"/> over it.
    ///
    /// This is what makes AAC/M4A, ALAC, WMA, APE, WavPack, TAK, Musepack, TTA, Speex and AC-3
    /// analyzable on Linux and macOS, where Media Foundation does not exist — and on Windows for the
    /// formats Media Foundation has no codec for. It sits at the very end of
    /// <see cref="AudioAnalyzer.OpenAudioFile"/>'s chain, so every format with a managed decoder is
    /// still handled without spawning a process.
    ///
    /// ffmpeg runs as a separate program and is never linked, which keeps this project's Apache-2.0
    /// license intact alongside an LGPL ffmpeg build — same arrangement as
    /// <see cref="AudioConversionService"/>, whose locator this reuses.
    /// </summary>
    public static class FfmpegDecoder
    {
        /// <summary>
        /// Decoding runs far faster than real time (a 3-hour file lands in a couple of minutes), so
        /// this only exists to stop a wedged process from hanging a scan forever.
        /// </summary>
        private const int DecodeTimeoutMs = 10 * 60 * 1000;

        private const string TempPrefix = "audioauditor-dec-";

        [ThreadStatic] private static string? _lastError;

        /// <summary>The last ffmpeg failure seen on this thread, for diagnostics. Null if none.</summary>
        public static string? LastError => _lastError;

        /// <summary>True when an ffmpeg binary is bundled or on PATH.</summary>
        public static bool IsAvailable => AudioConversionService.FindFfmpeg() != null;

        /// <summary>The resolved ffmpeg path, or null when none was found.</summary>
        public static string? FfmpegPath => AudioConversionService.FindFfmpeg();

        /// <summary>
        /// Decodes <paramref name="filePath"/> to a temp WAV via ffmpeg. Returns false when ffmpeg is
        /// unavailable or cannot decode the file; the temp file is removed on every failure path and
        /// when the returned stream is disposed.
        /// </summary>
        public static bool TryOpen(string filePath, out FfmpegWaveStream? stream, CancellationToken ct = default)
        {
            stream = null;
            _lastError = null;

            string? ffmpeg = AudioConversionService.FindFfmpeg();
            if (ffmpeg == null)
            {
                _lastError = "ffmpeg not found";
                return false;
            }

            // Unique per call: analysis decodes several files concurrently.
            string temp = Path.Combine(Path.GetTempPath(), TempPrefix + Guid.NewGuid().ToString("N") + ".wav");

            try
            {
                if (!Run(ffmpeg, filePath, temp, ct) || !File.Exists(temp))
                {
                    TryDelete(temp);
                    return false;
                }

                WaveFileReader? reader = null;
                try
                {
                    reader = new WaveFileReader(temp);
                    stream = new FfmpegWaveStream(reader, temp);
                    return true;
                }
                catch (Exception ex)
                {
                    _lastError = ex.Message;
                    reader?.Dispose();
                    TryDelete(temp);
                    return false;
                }
            }
            catch (OperationCanceledException)
            {
                // The temp WAV is half-written and useless; drop it before unwinding.
                TryDelete(temp);
                throw;
            }
            catch (Exception ex)
            {
                _lastError = ex.Message;
                TryDelete(temp);
                return false;
            }
        }

        private static bool Run(string ffmpeg, string source, string target, CancellationToken ct = default)
        {
            return RunFfmpeg(ffmpeg, new[]
            {
                "-i", source,
                // Embedded cover art shows up as a video stream that the wav muxer refuses to write.
                "-map", "a:0",
                "-vn",
                // Float keeps full precision for hi-res sources — true peak, LUFS and the spectral
                // floor all read from these samples, so a 16-bit intermediate would bias the verdict.
                "-c:a", "pcm_f32le",
                // RIFF tops out at 4 GB (~3.4 h at 44.1 kHz stereo float); RF64 takes over past that.
                "-rf64", "auto",
                "-f", "wav",
                target
            }, ct);
        }

        /// <summary>
        /// Runs ffmpeg to completion with the shared boilerplate flags. Returns false and sets
        /// <see cref="LastError"/> on a non-zero exit or a timeout.
        /// </summary>
        private static bool RunFfmpeg(string ffmpeg, string[] args, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();

            var psi = new ProcessStartInfo
            {
                FileName = ffmpeg,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            psi.ArgumentList.Add("-hide_banner");
            psi.ArgumentList.Add("-loglevel");
            psi.ArgumentList.Add("error");
            // Without -nostdin ffmpeg reads the console's stdin and fights the interactive CLI for it.
            psi.ArgumentList.Add("-nostdin");
            psi.ArgumentList.Add("-y");
            foreach (var arg in args) psi.ArgumentList.Add(arg);

            using var process = new Process { StartInfo = psi };

            // Event-based draining rather than ReadToEnd: a full pipe buffer would block ffmpeg
            // forever, and this path has to stay synchronous for OpenAudioFile.
            var stderr = new StringBuilder();
            process.ErrorDataReceived += (_, e) =>
            {
                if (e.Data == null) return;
                lock (stderr) { if (stderr.Length < 4096) stderr.AppendLine(e.Data); }
            };
            process.OutputDataReceived += (_, __) => { };

            process.Start();
            process.BeginErrorReadLine();
            process.BeginOutputReadLine();

            // Killed from the cancellation callback rather than by polling: a full-file decode is
            // the only thing standing between a grid selection and its spectrogram, and WaitForExit
            // returns the moment the process dies, so the wait unblocks immediately. Without this
            // the caller is pinned here for up to DecodeTimeoutMs no matter how fast it gave up.
            using (ct.Register(() => { try { process.Kill(entireProcessTree: true); } catch { } }))
            {
                if (!process.WaitForExit(DecodeTimeoutMs))
                {
                    try { process.Kill(entireProcessTree: true); } catch { }
                    _lastError = "ffmpeg timed out";
                    return false;
                }
            }

            // After the wait, so a cancelled decode reports as cancelled rather than as the
            // non-zero exit code the kill just produced.
            ct.ThrowIfCancellationRequested();

            if (process.ExitCode != 0)
            {
                string text;
                lock (stderr) text = stderr.ToString().Trim();
                _lastError = text.Length > 0 ? LastLine(text) : $"ffmpeg exited with code {process.ExitCode}";
                return false;
            }

            return true;
        }

        private static string LastLine(string text)
        {
            var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            return lines.Length == 0 ? text : lines[^1].Trim();
        }

        private static void TryDelete(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); } catch { }
        }

        /// <summary>
        /// Round-trips a synthesized AAC file through <see cref="TryOpen"/>: encode a known 1 kHz
        /// stereo tone, decode it back, and confirm the format survived and the samples are audible.
        /// This is the check that fails if the ffmpeg arguments, the WAV wrapper or the temp-file
        /// lifetime ever break. No-ops when ffmpeg is not installed — that is a missing prerequisite,
        /// not a defect.
        /// </summary>
        public static void SelfCheck()
        {
            string? ffmpeg = AudioConversionService.FindFfmpeg();
            if (ffmpeg == null) return;

            string source = Path.Combine(Path.GetTempPath(), TempPrefix + "selfcheck-" + Guid.NewGuid().ToString("N") + ".m4a");
            try
            {
                // lavfi and the native AAC encoder ship in every ffmpeg build, so this needs no assets.
                // aevalsrc rather than the sine source: it states the amplitude outright, so the peak
                // assert below tests the decoder instead of whatever level sine happens to default to.
                bool encoded = RunFfmpeg(ffmpeg, new[]
                {
                    "-f", "lavfi",
                    "-i", "aevalsrc=0.8*sin(2*PI*1000*t):d=2:s=44100:c=stereo",
                    "-c:a", "aac",
                    source
                });
                Assert(encoded && File.Exists(source), $"could not synthesize the AAC test file ({LastError})");

                Assert(TryOpen(source, out var stream) && stream != null,
                    $"ffmpeg failed to decode a plain AAC file ({LastError})");

                string tempPath;
                using (var decoded = stream!)
                {
                    Assert(decoded.WaveFormat.SampleRate == 44100,
                        $"sample rate should survive the round-trip, got {decoded.WaveFormat.SampleRate}");
                    Assert(decoded.WaveFormat.Channels == 2,
                        $"channel count should survive the round-trip, got {decoded.WaveFormat.Channels}");
                    Assert(decoded.WaveFormat.Encoding == WaveFormatEncoding.IeeeFloat,
                        $"decode should yield IEEE float, got {decoded.WaveFormat.Encoding}");
                    Assert(decoded.Length > 0, "decoded stream reports no length — segment selection depends on it");

                    var samples = new WaveToSampleProvider(decoded);
                    var buffer = new float[8192];
                    float peak = 0;
                    int read;
                    while ((read = samples.Read(buffer, 0, buffer.Length)) > 0)
                        for (int i = 0; i < read; i++)
                            peak = Math.Max(peak, Math.Abs(buffer[i]));

                    // Encoded at 0.8; AAC wobbles it slightly, so anything near that level is a pass
                    // and a silent or badly-scaled decode is not.
                    Assert(peak > 0.5f, $"a 1 kHz tone at 0.8 decoded at peak {peak:F4}");

                    tempPath = decoded.TempPath;
                }

                Assert(!File.Exists(tempPath), "the decode temp file should be deleted on dispose");
            }
            finally
            {
                TryDelete(source);
            }
        }

        private static void Assert(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }

        /// <summary>
        /// Removes decode temp files left behind by a process that was killed mid-scan. Cheap enough
        /// to call at startup; never throws.
        /// </summary>
        public static void CleanStaleTempFiles()
        {
            try
            {
                var cutoff = DateTime.UtcNow.AddHours(-6);
                foreach (var file in Directory.EnumerateFiles(Path.GetTempPath(), TempPrefix + "*.wav"))
                {
                    try
                    {
                        if (File.GetLastWriteTimeUtc(file) < cutoff) File.Delete(file);
                    }
                    catch { /* in use by a concurrent run, or not ours to delete */ }
                }
            }
            catch { }
        }
    }

    /// <summary>
    /// A <see cref="WaveStream"/> over an ffmpeg-produced temp WAV that deletes the file when
    /// disposed. It is a real WaveStream rather than a bare ISampleProvider so callers can read an
    /// exact <see cref="Length"/> — <c>AnalyzeSpectralContent</c> distributes its FFT segments across
    /// the file using that frame count and would otherwise fall back to estimating it from metadata.
    /// </summary>
    public sealed class FfmpegWaveStream : WaveStream
    {
        private readonly WaveFileReader _inner;
        private readonly string _tempPath;
        private int _disposed;

        internal FfmpegWaveStream(WaveFileReader inner, string tempPath)
        {
            _inner = inner;
            _tempPath = tempPath;
        }

        public override WaveFormat WaveFormat => _inner.WaveFormat;
        public override long Length => _inner.Length;

        /// <summary>The decoded WAV backing this stream. Exposed for the self-check's cleanup assert.</summary>
        public string TempPath => _tempPath;

        public override long Position
        {
            get => _inner.Position;
            set => _inner.Position = value;
        }

        public override int Read(byte[] buffer, int offset, int count) => _inner.Read(buffer, offset, count);

        protected override void Dispose(bool disposing)
        {
            if (disposing && Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                try { _inner.Dispose(); } catch { }
                try { if (File.Exists(_tempPath)) File.Delete(_tempPath); } catch { }
            }
            base.Dispose(disposing);
        }
    }
}
