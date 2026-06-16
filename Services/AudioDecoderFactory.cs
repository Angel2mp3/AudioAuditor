using System;
using System.IO;
using System.Threading.Tasks;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using NAudio.Vorbis;

namespace AudioQualityChecker.Services
{
    /// <summary>
    /// Unified audio decoder factory. Eliminates copy-pasted fallback chains
    /// across Play(), Crossfade, and Gapless paths.
    /// </summary>
    public readonly struct DecoderResult
    {
        public AudioFileReader? Reader { get; init; }
        public MediaFoundationReader? MfReader { get; init; }
        public WaveStream? WaveStreamReader { get; init; }
        public SampleChannel? SampleChannel { get; init; }
        public IDisposable? ExtraDisposable { get; init; }
        public IDisposable? ExtraDisposable2 { get; init; }

        /// <summary>
        /// The sample provider to feed into the audio pipeline.
        /// </summary>
        public ISampleProvider? Source => (ISampleProvider?)Reader ?? SampleChannel;
    }

    public static class AudioDecoderFactory
    {
        public static bool TryOpen(string filePath, out DecoderResult result)
        {
            result = default;
            string ext = Path.GetExtension(filePath).ToLowerInvariant();

            // 1. Opus files — dedicated Concentus decoder
            if (ext == ".opus")
            {
                OpusFileReader? opus = null;
                try
                {
                    opus = new OpusFileReader(filePath);
                    result = new DecoderResult
                    {
                        SampleChannel = new SampleChannel(opus, true),
                        ExtraDisposable = opus,
                        WaveStreamReader = opus
                    };
                    return true;
                }
                catch
                {
                    opus?.Dispose();
                }
            }

            // 2. Ogg Vorbis
            if (ext == ".ogg")
            {
                VorbisWaveReader? vorbis = null;
                try
                {
                    vorbis = new VorbisWaveReader(filePath);
                    result = new DecoderResult
                    {
                        SampleChannel = new SampleChannel(vorbis, true),
                        ExtraDisposable = vorbis,
                        WaveStreamReader = vorbis
                    };
                    return true;
                }
                catch
                {
                    vorbis?.Dispose();
                }
            }

            // 3. DSD (.dsf, .dff)
            if (ext is ".dsf" or ".dff")
            {
                DsdToPcmReader? dsd = null;
                try
                {
                    dsd = new DsdToPcmReader(filePath);
                    result = new DecoderResult
                    {
                        SampleChannel = new SampleChannel(dsd, true),
                        ExtraDisposable = dsd,
                        WaveStreamReader = dsd
                    };
                    return true;
                }
                catch
                {
                    dsd?.Dispose();
                }
            }

            // 3b. FLAC — use the managed decoder FIRST. AudioFileReader (step 4) decodes FLAC via
            //     MediaFoundation, which over-runs the real end (encoder padding / length mismatch):
            //     playback keeps going past the true end and the track only advances late. The
            //     managed FlacFileReader fully decodes the file, so its length is exact and Read()
            //     returns 0 at the true end → clean stop and immediate next-track advance.
            if (ext is ".flac" or ".fla")
            {
                FlacFileReader? flac = null;
                try
                {
                    flac = Task.Run(() => new FlacFileReader(filePath)).GetAwaiter().GetResult();
                    result = new DecoderResult
                    {
                        SampleChannel = new SampleChannel(flac, true),
                        ExtraDisposable = flac,
                        WaveStreamReader = flac
                    };
                    return true;
                }
                catch
                {
                    flac?.Dispose(); // fall through to AudioFileReader / MediaFoundation
                }
            }

            // 4. AudioFileReader (MP3, WAV, AIFF, WMA, FLAC, etc.)
            try
            {
                var reader = new AudioFileReader(filePath);
                result = new DecoderResult { Reader = reader };
                return true;
            }
            catch { }

            // 5. MediaFoundationReader with forced PCM output
            try
            {
                var settings = new MediaFoundationReader.MediaFoundationReaderSettings
                {
                    RequestFloatOutput = false
                };
                var mf = new MediaFoundationReader(filePath, settings);
                result = new DecoderResult
                {
                    MfReader = mf,
                    SampleChannel = new SampleChannel(mf, true)
                };
                return true;
            }
            catch
            {
                result.MfReader?.Dispose();
            }

            // 6. Standard MediaFoundationReader (float output)
            try
            {
                var mf = new MediaFoundationReader(filePath);
                result = new DecoderResult
                {
                    MfReader = mf,
                    SampleChannel = new SampleChannel(mf, true)
                };
                return true;
            }
            catch
            {
                result.MfReader?.Dispose();
            }

            // 7. MediaFoundationReader with explicit 16-bit PCM conversion
            try
            {
                var mf = new MediaFoundationReader(filePath);
                var pcm = new WaveFormatConversionStream(
                    new WaveFormat(mf.WaveFormat.SampleRate, 16, mf.WaveFormat.Channels),
                    mf);
                result = new DecoderResult
                {
                    MfReader = mf,
                    SampleChannel = new SampleChannel(pcm, true),
                    ExtraDisposable2 = pcm
                };
                return true;
            }
            catch
            {
                result.MfReader?.Dispose();
            }

            // 8. Managed FLAC decoder (hi-res / files MF can't decode)
            if (ext is ".flac" or ".fla")
            {
                FlacFileReader? flac = null;
                try
                {
                    flac = Task.Run(() => new FlacFileReader(filePath)).GetAwaiter().GetResult();
                    result = new DecoderResult
                    {
                        SampleChannel = new SampleChannel(flac, true),
                        ExtraDisposable = flac,
                        WaveStreamReader = flac
                    };
                    return true;
                }
                catch
                {
                    flac?.Dispose();
                }
            }

            // 9. VorbisWaveReader fallback for any Ogg-based format
            {
                VorbisWaveReader? vorbis = null;
                try
                {
                    vorbis = new VorbisWaveReader(filePath);
                    result = new DecoderResult
                    {
                        SampleChannel = new SampleChannel(vorbis, true),
                        ExtraDisposable = vorbis,
                        WaveStreamReader = vorbis
                    };
                    return true;
                }
                catch
                {
                    vorbis?.Dispose();
                }
            }

            // 10. WaveFileReader for raw WAV/RF64/BWF (including 24-bit)
            {
                WaveFileReader? wav = null;
                IDisposable? converter = null;
                try
                {
                    wav = new WaveFileReader(filePath);
                    if (wav.WaveFormat.BitsPerSample == 24 || wav.WaveFormat.BitsPerSample == 32)
                    {
                        var floatProvider = new Wave32To16Stream(wav);
                        converter = floatProvider;
                        result = new DecoderResult
                        {
                            SampleChannel = new SampleChannel(floatProvider, true),
                            ExtraDisposable = wav,
                            WaveStreamReader = wav,
                            ExtraDisposable2 = floatProvider
                        };
                    }
                    else
                    {
                        var pcm = WaveFormatConversionStream.CreatePcmStream(wav);
                        converter = pcm;
                        result = new DecoderResult
                        {
                            SampleChannel = new SampleChannel(pcm, true),
                            ExtraDisposable = wav,
                            WaveStreamReader = wav,
                            ExtraDisposable2 = pcm
                        };
                    }
                    return true;
                }
                catch
                {
                    converter?.Dispose();
                    wav?.Dispose();
                }
            }

            // 11. Opus decoder as last resort for any unrecognized file
            {
                OpusFileReader? opus = null;
                try
                {
                    opus = new OpusFileReader(filePath);
                    result = new DecoderResult
                    {
                        SampleChannel = new SampleChannel(opus, true),
                        ExtraDisposable = opus,
                        WaveStreamReader = opus
                    };
                    return true;
                }
                catch
                {
                    opus?.Dispose();
                }
            }

            // 12. Managed FLAC as absolute last resort (may be FLAC with wrong extension)
            {
                FlacFileReader? flac = null;
                try
                {
                    flac = Task.Run(() => new FlacFileReader(filePath)).GetAwaiter().GetResult();
                    result = new DecoderResult
                    {
                        SampleChannel = new SampleChannel(flac, true),
                        ExtraDisposable = flac,
                        WaveStreamReader = flac
                    };
                    return true;
                }
                catch
                {
                    flac?.Dispose();
                }
            }

            result = default;
            return false;
        }
    }
}
