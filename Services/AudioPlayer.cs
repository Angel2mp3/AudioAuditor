using System;
using System.IO;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using NAudio.Vorbis;

namespace AudioQualityChecker.Services
{

    public class AudioPlayer : IDisposable
    {
        private WaveOutEvent? _waveOut;
        private AudioFileReader? _reader;           // Primary reader (MP3, WAV, AIFF, WMA)
        private MediaFoundationReader? _mfReader;    // Fallback for other formats
        private SampleChannel? _sampleChannel;       // Volume wrapper for MF fallback
        private WaveStream? _waveStreamReader;       // Tracks WaveStream for Opus/Vorbis/DSD/WAV readers
        private IDisposable? _extraDisposable;       // Third-fallback reader cleanup
        private IDisposable? _extraDisposable2;      // Third-fallback conversion stream cleanup
        private string? _currentFile;
        private bool _disposed;
        private float _userVolume = 1f;
        private float _normalizationGain = 1f;

        // Crossfade support
        private WaveOutEvent? _fadeOutDevice;
        private AudioFileReader? _fadeOutReader;
        private MediaFoundationReader? _fadeOutMfReader;
        private SampleChannel? _fadeOutSampleChannel;
        private System.Threading.Timer? _fadeTimer;
        private int _crossfadeDurationMs = 3000;
        private const int FadeStepMs = 40;

        // Visualizer sample capture
        private readonly float[] _vizBuffer = new float[8192];
        private int _vizWritePos;
        private readonly object _vizLock = new();
        public int VisualizerSampleRate { get; private set; } = 44100;
        public int VisualizerChannels { get; private set; } = 2;

        // Post-seek safety: generation counter detects seeks that happen while
        // _source.Read() is in progress (corrupting the decode buffer), then
        // hard-mute + fade-in provides clean silence while the decoder stabilizes.
        private int _seekGeneration;                       // incremented atomically by Seek()
        private int _seekMuteBuffers;                      // read() calls to hard-mute after seek
        private int _seekFadeSamplesRemaining;
        private readonly int _seekFadeTotalSamples = 1764; // ~40ms at 44.1kHz

        // Equalizer
        private Equalizer? _equalizer;
        public Equalizer? CurrentEqualizer => _equalizer;

        // Spatial Audio
        private SpatialAudioProcessor? _spatialAudio;
        public SpatialAudioProcessor? CurrentSpatialAudio => _spatialAudio;

        public float[] GetVisualizerSamples(int count)
        {
            lock (_vizLock)
            {
                int actual = Math.Min(count, _vizBuffer.Length);
                float[] result = new float[actual];
                int start = (_vizWritePos - actual + _vizBuffer.Length) % _vizBuffer.Length;
                for (int i = 0; i < actual; i++)
                    result[i] = _vizBuffer[(start + i) % _vizBuffer.Length];
                return result;
            }
        }

        private void CaptureVisualizerSamples(byte[] buffer, int offset, int count, WaveFormat format)
        {
            lock (_vizLock)
            {
                int bytesPerSample = format.BitsPerSample / 8;
                if (format.Encoding == WaveFormatEncoding.IeeeFloat && bytesPerSample == 4)
                {
                    int sampleCount = count / 4;
                    for (int i = 0; i < sampleCount; i++)
                    {
                        _vizBuffer[_vizWritePos] = BitConverter.ToSingle(buffer, offset + i * 4);
                        _vizWritePos = (_vizWritePos + 1) % _vizBuffer.Length;
                    }
                }
                else if (format.Encoding == WaveFormatEncoding.Pcm && bytesPerSample == 2)
                {
                    int sampleCount = count / 2;
                    for (int i = 0; i < sampleCount; i++)
                    {
                        _vizBuffer[_vizWritePos] = BitConverter.ToInt16(buffer, offset + i * 2) / 32768f;
                        _vizWritePos = (_vizWritePos + 1) % _vizBuffer.Length;
                    }
                }
            }
        }

        private class CaptureWaveProvider : IWaveProvider
        {
            private readonly IWaveProvider _source;
            private readonly AudioPlayer _player;

            public CaptureWaveProvider(IWaveProvider source, AudioPlayer player)
            {
                _source = source;
                _player = player;
                player.VisualizerSampleRate = source.WaveFormat.SampleRate;
                player.VisualizerChannels = source.WaveFormat.Channels;
            }

            public WaveFormat WaveFormat => _source.WaveFormat;

            public int Read(byte[] buffer, int offset, int count)
            {
                // ── AUDIO SAFETY SYSTEM ──
                // Prevents ear-damaging blasts when seeking by detecting corrupted
                // decode buffers (seek changed reader position mid-Read) and forcing
                // silence until the decoder stabilizes.
                //
                // The bug: Seek() on UI thread changes the reader position while the
                // audio thread is inside _source.Read(). The decoder returns garbage —
                // half old position, half new, producing valid [-1,1] floats that
                // sound like deafening white static. Clamps can't catch this because
                // the samples are individually valid; only the PATTERN is noise.
                //
                // Fix: generation counter detects ANY seek during Read(). If it fires,
                // the entire buffer is zeroed. Mute period keeps silence while the
                // decoder stabilizes post-seek. Fade-in smooths re-entry.

                // Snapshot generation BEFORE reading from the pipeline
                int genBefore = System.Threading.Volatile.Read(ref _player._seekGeneration);

                int read = _source.Read(buffer, offset, count);
                if (read <= 0) return read;

                // CHECK 1: Did a seek happen DURING _source.Read()?
                // If so, the buffer is corrupted — zero it entirely.
                int genAfter = System.Threading.Volatile.Read(ref _player._seekGeneration);
                if (genAfter != genBefore)
                {
                    Array.Clear(buffer, offset, read);
                    _player.CaptureVisualizerSamples(buffer, offset, read, _source.WaveFormat);
                    return read;
                }

                // CHECK 2: Post-seek mute period — decoder needs several buffer
                // fills to produce clean output after a position change.
                int muteLeft = System.Threading.Volatile.Read(ref _player._seekMuteBuffers);
                if (muteLeft > 0)
                {
                    Array.Clear(buffer, offset, read);
                    System.Threading.Interlocked.Decrement(ref _player._seekMuteBuffers);
                    _player.CaptureVisualizerSamples(buffer, offset, read, _source.WaveFormat);
                    return read;
                }

                // CHECK 3: Per-sample hard limiter + post-mute fade-in (always active)
                if (_source.WaveFormat.Encoding == WaveFormatEncoding.IeeeFloat
                    && _source.WaveFormat.BitsPerSample == 32)
                {
                    int sampleCount = read / 4;
                    int fadeRemaining = _player._seekFadeSamplesRemaining;
                    int fadeTotal = _player._seekFadeTotalSamples;

                    for (int i = 0; i < sampleCount; i++)
                    {
                        int pos = offset + i * 4;
                        float sample = BitConverter.ToSingle(buffer, pos);

                        // Kill NaN / Infinity
                        if (float.IsNaN(sample) || float.IsInfinity(sample))
                            sample = 0f;

                        // Hard clamp to [-1, 1]
                        if (sample > 1f) sample = 1f;
                        else if (sample < -1f) sample = -1f;

                        // Post-mute fade-in ramp
                        if (fadeRemaining > 0)
                        {
                            float gain = 1f - (float)fadeRemaining / fadeTotal;
                            sample *= gain * gain; // quadratic curve
                            fadeRemaining--;
                        }

                        BitConverter.TryWriteBytes(buffer.AsSpan(pos, 4), sample);
                    }

                    _player._seekFadeSamplesRemaining = fadeRemaining;

                    // FINAL CHECK: if a seek happened during the limiter loop, nuke it
                    if (System.Threading.Volatile.Read(ref _player._seekGeneration) != genAfter)
                    {
                        Array.Clear(buffer, offset, read);
                    }
                }

                _player.CaptureVisualizerSamples(buffer, offset, read, _source.WaveFormat);
                return read;
            }
        }

        /// <summary>
        /// Crossfade duration in seconds (1-10). Default is 3.
        /// </summary>
        public int CrossfadeDurationSeconds
        {
            get => _crossfadeDurationMs / 1000;
            set => _crossfadeDurationMs = Math.Clamp(value, 1, 10) * 1000;
        }

        /// <summary>
        /// Raised when playback finishes naturally (reached end of track).
        /// Not raised on manual Stop().
        /// </summary>
        public event EventHandler? TrackFinished;
        public event EventHandler? PlaybackStarted;
        public event EventHandler? PlaybackStopped;

        public bool IsPlaying => _waveOut?.PlaybackState == PlaybackState.Playing;
        public bool IsPaused => _waveOut?.PlaybackState == PlaybackState.Paused;
        public bool IsStopped => _waveOut == null || _waveOut.PlaybackState == PlaybackState.Stopped;

        public TimeSpan CurrentPosition
        {
            get
            {
                if (_reader != null) return _reader.CurrentTime;
                if (_mfReader != null) return _mfReader.CurrentTime;
                if (_waveStreamReader != null) return _waveStreamReader.CurrentTime;
                return TimeSpan.Zero;
            }
        }

        public TimeSpan TotalDuration
        {
            get
            {
                if (_reader != null) return _reader.TotalTime;
                if (_mfReader != null) return _mfReader.TotalTime;
                if (_waveStreamReader != null) return _waveStreamReader.TotalTime;
                return TimeSpan.Zero;
            }
        }

        public string? CurrentFile => _currentFile;

        /// <summary>
        /// Volume from 0.0 to 1.0
        /// </summary>
        public float Volume
        {
            get => _userVolume;
            set
            {
                _userVolume = Math.Clamp(value, 0f, 1f);
                ApplyVolume();
            }
        }

        private void ApplyVolume()
        {
            float effective = _userVolume * _normalizationGain;
            effective = Math.Clamp(effective, 0f, 1f);
            if (_reader != null) _reader.Volume = effective;
            if (_sampleChannel != null) _sampleChannel.Volume = effective;
        }

        /// <summary>
        /// Enables or disables normalization on the currently playing track without restarting playback.
        /// </summary>
        public void SetNormalization(bool enabled)
        {
            if (enabled && _currentFile != null)
                CalculateNormalizationGain();
            else
                _normalizationGain = 1f;
            ApplyVolume();
        }

        public void Play(string filePath, bool normalize = false)
        {
            if (_currentFile == filePath && _waveOut?.PlaybackState == PlaybackState.Paused)
            {
                _waveOut.Play();
                PlaybackStarted?.Invoke(this, EventArgs.Empty);
                return;
            }

            Stop();

            try
            {
                _currentFile = filePath;
                _normalizationGain = 1f;

                // Try AudioFileReader first (best support for MP3, WAV, AIFF, WMA, FLAC)
                IWaveProvider playbackSource;
                bool opened = false;
                string ext = System.IO.Path.GetExtension(filePath).ToLowerInvariant();

                // For Opus files, use dedicated Concentus decoder first
                if (ext == ".opus")
                {
                    OpusFileReader? opusReader = null;
                    try
                    {
                        opusReader = new OpusFileReader(filePath);
                        _sampleChannel = new SampleChannel(opusReader, true);
                        playbackSource = new SampleToWaveProvider(_sampleChannel);
                        _extraDisposable = opusReader;
                        _waveStreamReader = opusReader;
                        opened = true;
                    }
                    catch
                    {
                        opusReader?.Dispose();
                        _sampleChannel = null;
                        _extraDisposable = null;
                        _waveStreamReader = null;
                    }
                }

                // For Ogg Vorbis files, use VorbisWaveReader
                if (!opened && (ext == ".ogg"))
                {
                    VorbisWaveReader? vorbisReader = null;
                    try
                    {
                        vorbisReader = new VorbisWaveReader(filePath);
                        _sampleChannel = new SampleChannel(vorbisReader, true);
                        playbackSource = new SampleToWaveProvider(_sampleChannel);
                        _extraDisposable = vorbisReader;
                        _waveStreamReader = vorbisReader;
                        opened = true;
                    }
                    catch
                    {
                        vorbisReader?.Dispose();
                        _sampleChannel = null;
                        _extraDisposable = null;
                        _waveStreamReader = null;
                    }
                }

                // For DSD files (.dsf, .dff), use DSD-to-PCM converter
                if (!opened && (ext == ".dsf" || ext == ".dff"))
                {
                    DsdToPcmReader? dsdReader = null;
                    try
                    {
                        dsdReader = new DsdToPcmReader(filePath);
                        _sampleChannel = new SampleChannel(dsdReader, true);
                        playbackSource = new SampleToWaveProvider(_sampleChannel);
                        _extraDisposable = dsdReader;
                        _waveStreamReader = dsdReader;
                        opened = true;
                    }
                    catch
                    {
                        dsdReader?.Dispose();
                        _sampleChannel = null;
                        _extraDisposable = null;
                        _waveStreamReader = null;
                    }
                }

                // Try AudioFileReader (MP3, WAV, AIFF, WMA, FLAC, etc.)
                if (!opened)
                {
                    try
                    {
                        _reader = new AudioFileReader(filePath);
                        playbackSource = _reader;
                        opened = true;
                    }
                    catch
                    {
                        _reader = null;
                    }
                }

                // Try MediaFoundationReader with forced PCM output for problematic formats
                if (!opened)
                {
                    try
                    {
                        var settings = new MediaFoundationReader.MediaFoundationReaderSettings
                        {
                            RequestFloatOutput = false  // request PCM, more compatible
                        };
                        _mfReader = new MediaFoundationReader(filePath, settings);
                        _sampleChannel = new SampleChannel(_mfReader, true);
                        playbackSource = new SampleToWaveProvider(_sampleChannel);
                        opened = true;
                    }
                    catch
                    {
                        _mfReader?.Dispose();
                        _mfReader = null;
                        _sampleChannel = null;
                    }
                }

                // Try standard MediaFoundationReader (float output)
                if (!opened)
                {
                    try
                    {
                        _mfReader = new MediaFoundationReader(filePath);
                        _sampleChannel = new SampleChannel(_mfReader, true);
                        playbackSource = new SampleToWaveProvider(_sampleChannel);
                        opened = true;
                    }
                    catch
                    {
                        _mfReader?.Dispose();
                        _mfReader = null;
                        _sampleChannel = null;
                    }
                }

                // Try MediaFoundationReader with explicit 16-bit PCM conversion
                // (handles FLAC/formats where SampleChannel fails on 24-bit MF output)
                if (!opened)
                {
                    try
                    {
                        _mfReader = new MediaFoundationReader(filePath);
                        var pcmStream = new WaveFormatConversionStream(
                            new WaveFormat(_mfReader.WaveFormat.SampleRate, 16, _mfReader.WaveFormat.Channels),
                            _mfReader);
                        _sampleChannel = new SampleChannel(pcmStream, true);
                        playbackSource = new SampleToWaveProvider(_sampleChannel);
                        _extraDisposable2 = pcmStream;
                        opened = true;
                    }
                    catch
                    {
                        _mfReader?.Dispose();
                        _mfReader = null;
                        _sampleChannel = null;
                        _extraDisposable2 = null;
                    }
                }

                // Try managed FLAC decoder (handles hi-res and files MediaFoundation can't decode)
                if (!opened && (ext == ".flac" || ext == ".fla"))
                {
                    FlacFileReader? flacReader = null;
                    try
                    {
                        // Decode on thread pool to avoid UI freeze on large hi-res files
                        flacReader = System.Threading.Tasks.Task.Run(() => new FlacFileReader(filePath)).Result;
                        _sampleChannel = new SampleChannel(flacReader, true);
                        playbackSource = new SampleToWaveProvider(_sampleChannel);
                        _extraDisposable = flacReader;
                        _waveStreamReader = flacReader;
                        opened = true;
                    }
                    catch
                    {
                        flacReader?.Dispose();
                        _sampleChannel = null;
                        _extraDisposable = null;
                        _waveStreamReader = null;
                    }
                }

                // Try VorbisWaveReader as fallback for any Ogg-based format
                if (!opened)
                {
                    VorbisWaveReader? vorbisReader2 = null;
                    try
                    {
                        vorbisReader2 = new VorbisWaveReader(filePath);
                        _sampleChannel = new SampleChannel(vorbisReader2, true);
                        playbackSource = new SampleToWaveProvider(_sampleChannel);
                        _extraDisposable = vorbisReader2;
                        _waveStreamReader = vorbisReader2;
                        opened = true;
                    }
                    catch
                    {
                        vorbisReader2?.Dispose();
                        _sampleChannel = null;
                        _extraDisposable = null;
                        _waveStreamReader = null;
                    }
                }

                // Try WaveFileReader for raw WAV/RF64/BWF (including 24-bit)
                if (!opened)
                {
                    WaveFileReader? rawReader = null;
                    IDisposable? converter = null;
                    try
                    {
                        rawReader = new WaveFileReader(filePath);
                        // For 24-bit or other non-standard WAV, convert to PCM then resample
                        if (rawReader.WaveFormat.BitsPerSample == 24 || rawReader.WaveFormat.BitsPerSample == 32)
                        {
                            var floatProvider = new Wave32To16Stream(rawReader);
                            converter = floatProvider;
                            _sampleChannel = new SampleChannel(floatProvider, true);
                            _extraDisposable2 = floatProvider;
                        }
                        else
                        {
                            var pcmStream = WaveFormatConversionStream.CreatePcmStream(rawReader);
                            converter = pcmStream;
                            _sampleChannel = new SampleChannel(pcmStream, true);
                            _extraDisposable2 = pcmStream;
                        }
                        playbackSource = new SampleToWaveProvider(_sampleChannel);
                        _extraDisposable = rawReader;
                        _waveStreamReader = rawReader;
                        opened = true;
                    }
                    catch
                    {
                        converter?.Dispose();
                        rawReader?.Dispose();
                        _sampleChannel = null;
                        _waveStreamReader = null;
                        _extraDisposable2 = null;
                    }
                }

                // Try Opus decoder as last resort for any unrecognized file
                if (!opened)
                {
                    OpusFileReader? opusReader2 = null;
                    try
                    {
                        opusReader2 = new OpusFileReader(filePath);
                        _sampleChannel = new SampleChannel(opusReader2, true);
                        playbackSource = new SampleToWaveProvider(_sampleChannel);
                        _extraDisposable = opusReader2;
                        _waveStreamReader = opusReader2;
                        opened = true;
                    }
                    catch
                    {
                        opusReader2?.Dispose();
                        _sampleChannel = null;
                        _extraDisposable = null;
                        _waveStreamReader = null;
                    }
                }

                // Try managed FLAC decoder as absolute last resort (may be FLAC with wrong extension)
                if (!opened)
                {
                    FlacFileReader? flacReader2 = null;
                    try
                    {
                        flacReader2 = System.Threading.Tasks.Task.Run(() => new FlacFileReader(filePath)).Result;
                        _sampleChannel = new SampleChannel(flacReader2, true);
                        playbackSource = new SampleToWaveProvider(_sampleChannel);
                        _extraDisposable = flacReader2;
                        _waveStreamReader = flacReader2;
                        opened = true;
                    }
                    catch
                    {
                        flacReader2?.Dispose();
                        _sampleChannel = null;
                        _extraDisposable = null;
                        _waveStreamReader = null;
                    }
                }

                if (!opened)
                {
                    throw new InvalidOperationException(
                        "This audio format is not supported for playback. " +
                        "The file may use an unsupported codec or proprietary encoding.");
                }

                // Apply normalization if requested
                if (normalize)
                    CalculateNormalizationGain();

                ApplyVolume();

                // Insert equalizer into pipeline
                ISampleProvider sampleSource;
                if (_reader != null)
                    sampleSource = _reader;
                else
                    sampleSource = _sampleChannel!;

                // Build processing chain and try to init WaveOut.
                // Use larger buffers for fallback readers (MediaFoundation, etc.)
                // to prevent stuttering from slower decode paths.
                bool useLargeBuffers = _mfReader != null || _waveStreamReader != null;
                // Try native rate first, fall back to resample on failure.
                if (!TryInitPlaybackPipeline(sampleSource, false, 48000, useLargeBuffers))
                {
                    if (!TryInitPlaybackPipeline(sampleSource, true, 48000, useLargeBuffers))
                    {
                        if (!TryInitPlaybackPipeline(sampleSource, true, 44100, useLargeBuffers))
                        {
                            throw new InvalidOperationException(
                                "Unable to play this file. Your audio device may not support the required format.");
                        }
                    }
                }

                PlaybackStarted?.Invoke(this, EventArgs.Empty);
            }
            catch
            {
                Stop();
                throw;
            }
        }

        /// <summary>
        /// Builds the EQ → Spatial → WaveOut pipeline and attempts to init + play.
        /// Returns true on success. If resample is requested, inserts a resampler before the EQ.
        /// On failure, disposes the WaveOutEvent so the caller can retry.
        /// </summary>
        private bool TryInitPlaybackPipeline(ISampleProvider source, bool resample, int targetRate = 48000, bool useLargeBuffers = false)
        {
            try
            {
                ISampleProvider sampleSource = source;

                if (resample && sampleSource.WaveFormat.SampleRate != targetRate)
                {
                    sampleSource = new WdlResamplingSampleProvider(sampleSource, targetRate);
                }

                _equalizer = new Equalizer(sampleSource);
                _equalizer.Enabled = ThemeManager.EqualizerEnabled;
                for (int i = 0; i < 10; i++)
                    _equalizer.UpdateBand(i, ThemeManager.EqualizerGains[i]);

                _spatialAudio = new SpatialAudioProcessor(_equalizer);
                _spatialAudio.Enabled = ThemeManager.SpatialAudioEnabled;

                IWaveProvider finalSource = new SampleToWaveProvider(_spatialAudio);

                int sampleRate = _spatialAudio.WaveFormat.SampleRate;
                int baseLatency = sampleRate > 48000 ? 400 : 250;
                int baseBuffers = sampleRate > 48000 ? 5 : 3;
                _waveOut = new WaveOutEvent
                {
                    DesiredLatency = useLargeBuffers ? Math.Max(baseLatency, 400) : baseLatency,
                    NumberOfBuffers = useLargeBuffers ? Math.Max(baseBuffers, 5) : baseBuffers
                };
                _waveOut.PlaybackStopped += OnPlaybackStopped;
                _waveOut.Init(new CaptureWaveProvider(finalSource, this));
                _waveOut.Play();
                return true;
            }
            catch
            {
                if (_waveOut != null)
                {
                    _waveOut.PlaybackStopped -= OnPlaybackStopped;
                    try { _waveOut.Stop(); } catch { }
                    _waveOut.Dispose();
                    _waveOut = null;
                }
                return false;
            }
        }

        /// <summary>
        /// Start playing with crossfade from the current track.
        /// </summary>
        public void PlayWithCrossfade(string filePath, bool normalize = false)
        {
            if (_waveOut == null || _waveOut.PlaybackState != PlaybackState.Playing)
            {
                // Nothing playing, just play normally
                Play(filePath, normalize);
                return;
            }

            // Move current playback to fade-out — unhook events FIRST
            CleanupFadeOut();
            _waveOut.PlaybackStopped -= OnPlaybackStopped;  // detach before moving
            _fadeOutDevice = _waveOut;
            _fadeOutReader = _reader;
            _fadeOutMfReader = _mfReader;
            _fadeOutSampleChannel = _sampleChannel;

            _waveOut = null;
            _reader = null;
            _mfReader = null;
            _sampleChannel = null;
            _waveStreamReader = null;
            _currentFile = null;

            // Start the new track
            try
            {
                _currentFile = filePath;
                _normalizationGain = 1f;

                IWaveProvider playbackSource;
                bool opened = false;
                string ext = System.IO.Path.GetExtension(filePath).ToLowerInvariant();

                // Opus
                if (ext == ".opus")
                {
                    OpusFileReader? opusReader = null;
                    try
                    {
                        opusReader = new OpusFileReader(filePath);
                        _sampleChannel = new SampleChannel(opusReader, true);
                        _sampleChannel.Volume = 0f;
                        playbackSource = new SampleToWaveProvider(_sampleChannel);
                        _extraDisposable = opusReader;
                        _waveStreamReader = opusReader;
                        opened = true;
                    }
                    catch { opusReader?.Dispose(); _sampleChannel = null; _extraDisposable = null; _waveStreamReader = null; }
                }

                // Ogg Vorbis
                if (!opened && ext == ".ogg")
                {
                    VorbisWaveReader? vorbisReader = null;
                    try
                    {
                        vorbisReader = new VorbisWaveReader(filePath);
                        _sampleChannel = new SampleChannel(vorbisReader, true);
                        _sampleChannel.Volume = 0f;
                        playbackSource = new SampleToWaveProvider(_sampleChannel);
                        _extraDisposable = vorbisReader;
                        _waveStreamReader = vorbisReader;
                        opened = true;
                    }
                    catch { vorbisReader?.Dispose(); _sampleChannel = null; _extraDisposable = null; _waveStreamReader = null; }
                }

                // DSD
                if (!opened && (ext == ".dsf" || ext == ".dff"))
                {
                    DsdToPcmReader? dsdReader = null;
                    try
                    {
                        dsdReader = new DsdToPcmReader(filePath);
                        _sampleChannel = new SampleChannel(dsdReader, true);
                        _sampleChannel.Volume = 0f;
                        playbackSource = new SampleToWaveProvider(_sampleChannel);
                        _extraDisposable = dsdReader;
                        _waveStreamReader = dsdReader;
                        opened = true;
                    }
                    catch { dsdReader?.Dispose(); _sampleChannel = null; _extraDisposable = null; _waveStreamReader = null; }
                }

                // AudioFileReader
                if (!opened)
                {
                    try
                    {
                        _reader = new AudioFileReader(filePath);
                        _reader.Volume = 0f; // start at 0, fade in
                        playbackSource = _reader;
                        opened = true;
                    }
                    catch { _reader = null; }
                }

                // MediaFoundationReader with PCM output
                if (!opened)
                {
                    try
                    {
                        var settings = new MediaFoundationReader.MediaFoundationReaderSettings
                        {
                            RequestFloatOutput = false
                        };
                        _mfReader = new MediaFoundationReader(filePath, settings);
                        _sampleChannel = new SampleChannel(_mfReader, true);
                        _sampleChannel.Volume = 0f;
                        playbackSource = new SampleToWaveProvider(_sampleChannel);
                        opened = true;
                    }
                    catch
                    {
                        _mfReader?.Dispose();
                        _mfReader = null;
                        _sampleChannel = null;
                    }
                }

                // MediaFoundationReader standard
                if (!opened)
                {
                    try
                    {
                        _mfReader = new MediaFoundationReader(filePath);
                        _sampleChannel = new SampleChannel(_mfReader, true);
                        _sampleChannel.Volume = 0f;
                        playbackSource = new SampleToWaveProvider(_sampleChannel);
                        opened = true;
                    }
                    catch
                    {
                        _mfReader?.Dispose();
                        _mfReader = null;
                        _sampleChannel = null;
                    }
                }

                // MediaFoundationReader with explicit 16-bit PCM conversion
                if (!opened)
                {
                    try
                    {
                        _mfReader = new MediaFoundationReader(filePath);
                        var pcmStream = new WaveFormatConversionStream(
                            new WaveFormat(_mfReader.WaveFormat.SampleRate, 16, _mfReader.WaveFormat.Channels),
                            _mfReader);
                        _sampleChannel = new SampleChannel(pcmStream, true);
                        _sampleChannel.Volume = 0f;
                        playbackSource = new SampleToWaveProvider(_sampleChannel);
                        _extraDisposable2 = pcmStream;
                        opened = true;
                    }
                    catch
                    {
                        _mfReader?.Dispose();
                        _mfReader = null;
                        _sampleChannel = null;
                        _extraDisposable2 = null;
                    }
                }

                if (!opened)
                {
                    throw new InvalidOperationException(
                        "This audio format is not supported for playback.");
                }

                if (normalize)
                    CalculateNormalizationGain();

                // Insert equalizer into crossfade pipeline
                ISampleProvider sampleSource;
                if (_reader != null)
                    sampleSource = _reader;
                else
                    sampleSource = _sampleChannel!;

                // Try native rate first, fall back to resample on failure
                if (!TryInitPlaybackPipeline(sampleSource, false))
                {
                    if (!TryInitPlaybackPipeline(sampleSource, true, 48000))
                    {
                        if (!TryInitPlaybackPipeline(sampleSource, true, 44100))
                        {
                            throw new InvalidOperationException(
                                "Unable to play this file. Your audio device may not support the required format.");
                        }
                    }
                }

                PlaybackStarted?.Invoke(this, EventArgs.Empty);
            }
            catch
            {
                // If new track fails, stop everything
                Stop();
                CleanupFadeOut();
                throw;
            }

            // Start crossfade timer
            int steps = _crossfadeDurationMs / FadeStepMs;
            int currentStep = 0;
            float fadeOutStartVol = GetFadeOutVolume();

            _fadeTimer = new System.Threading.Timer(_ =>
            {
                currentStep++;
                float progress = Math.Min(1f, (float)currentStep / steps);

                // Fade out old track
                float fadeOutVol = fadeOutStartVol * (1f - progress);
                SetFadeOutVolume(fadeOutVol);

                // Fade in new track
                float targetVol = _userVolume * _normalizationGain;
                float fadeInVol = targetVol * progress;
                if (_reader != null) _reader.Volume = Math.Clamp(fadeInVol, 0f, 1f);
                if (_sampleChannel != null) _sampleChannel.Volume = Math.Clamp(fadeInVol, 0f, 1f);

                if (currentStep >= steps)
                {
                    _fadeTimer?.Dispose();
                    _fadeTimer = null;
                    CleanupFadeOut();
                    ApplyVolume(); // ensure final volume is correct
                }
            }, null, FadeStepMs, FadeStepMs);
        }

        private float GetFadeOutVolume()
        {
            if (_fadeOutReader != null) return _fadeOutReader.Volume;
            if (_fadeOutSampleChannel != null) return _fadeOutSampleChannel.Volume;
            return 0f;
        }

        private void SetFadeOutVolume(float vol)
        {
            vol = Math.Clamp(vol, 0f, 1f);
            if (_fadeOutReader != null) _fadeOutReader.Volume = vol;
            if (_fadeOutSampleChannel != null) _fadeOutSampleChannel.Volume = vol;
        }

        private void CleanupFadeOut()
        {
            _fadeTimer?.Dispose();
            _fadeTimer = null;

            if (_fadeOutDevice != null)
            {
                _fadeOutDevice.PlaybackStopped -= OnPlaybackStopped;
                try { _fadeOutDevice.Stop(); } catch { }
                _fadeOutDevice.Dispose();
                _fadeOutDevice = null;
            }

            _fadeOutSampleChannel = null;

            if (_fadeOutReader != null)
            {
                try { _fadeOutReader.Dispose(); } catch { }
                _fadeOutReader = null;
            }

            if (_fadeOutMfReader != null)
            {
                try { _fadeOutMfReader.Dispose(); } catch { }
                _fadeOutMfReader = null;
            }
        }

        private void CalculateNormalizationGain()
        {
            // Scan peak level of the track to normalize volume
            // Target: -1dB (0.891)
            const float targetPeak = 0.891f;
            float maxSample = 0f;

            try
            {
                ISampleProvider? scanner = null;
                IDisposable? scanDisposable = null;

                try
                {
                    var scanReader = new AudioFileReader(_currentFile!);
                    scanner = scanReader;
                    scanDisposable = scanReader;
                }
                catch
                {
                    var scanMf = new MediaFoundationReader(_currentFile!);
                    var sc = new SampleChannel(scanMf, true);
                    scanner = sc;
                    scanDisposable = scanMf;
                }

                float[] buf = new float[8192];
                int read;
                // Read up to 30 seconds for performance
                long maxSamples = (long)(scanner.WaveFormat.SampleRate * scanner.WaveFormat.Channels * 30);
                long totalRead = 0;

                while ((read = scanner.Read(buf, 0, buf.Length)) > 0 && totalRead < maxSamples)
                {
                    for (int i = 0; i < read; i++)
                    {
                        float abs = Math.Abs(buf[i]);
                        if (abs > maxSample) maxSample = abs;
                    }
                    totalRead += read;
                }

                scanDisposable?.Dispose();

                if (maxSample > 0.001f)
                {
                    _normalizationGain = Math.Min(targetPeak / maxSample, 3f); // cap at +9.5dB
                }
            }
            catch
            {
                _normalizationGain = 1f;
            }
        }

        public void Pause()
        {
            if (_waveOut?.PlaybackState == PlaybackState.Playing)
            {
                _waveOut.Pause();
            }
        }

        public void Resume()
        {
            if (_waveOut?.PlaybackState == PlaybackState.Paused)
            {
                _waveOut.Play();
                PlaybackStarted?.Invoke(this, EventArgs.Empty);
            }
        }

        public void Stop()
        {
            CleanupFadeOut();

            if (_waveOut != null)
            {
                _waveOut.PlaybackStopped -= OnPlaybackStopped;
                try { _waveOut.Stop(); } catch { }
                _waveOut.Dispose();
                _waveOut = null;
            }

            _sampleChannel = null;

            if (_reader != null)
            {
                _reader.Dispose();
                _reader = null;
            }

            if (_mfReader != null)
            {
                _mfReader.Dispose();
                _mfReader = null;
            }

            if (_extraDisposable2 != null)
            {
                try { _extraDisposable2.Dispose(); } catch { }
                _extraDisposable2 = null;
            }

            if (_extraDisposable != null)
            {
                try { _extraDisposable.Dispose(); } catch { }
                _extraDisposable = null;
            }

            _waveStreamReader = null;
            _currentFile = null;
            _normalizationGain = 1f;
            PlaybackStopped?.Invoke(this, EventArgs.Empty);
        }

        public void Seek(double positionSeconds)
        {
            // ── NUCLEAR SAFETY: mute the WaveOut device at the Windows audio level ──
            // This is the absolute last line of defense. Even if every other safety
            // mechanism fails, no sound can physically reach the speakers while the
            // device volume is 0.
            if (_waveOut != null) _waveOut.Volume = 0f;

            // Increment generation counter so the audio thread knows the current
            // buffer (if one is being filled right now) is tainted
            System.Threading.Interlocked.Increment(ref _seekGeneration);

            // Hard-mute for 6 buffer fills while decoder stabilizes
            System.Threading.Interlocked.Exchange(ref _seekMuteBuffers, 6);

            // Reset DSP processor state
            _equalizer?.ResetFilterState();
            _spatialAudio?.ResetBuffers();

            // Arm post-mute fade-in
            _seekFadeSamplesRemaining = _seekFadeTotalSamples;

            if (_reader != null)
            {
                var target = TimeSpan.FromSeconds(Math.Clamp(positionSeconds, 0, _reader.TotalTime.TotalSeconds));
                _reader.CurrentTime = target;
            }
            else if (_mfReader != null)
            {
                var target = TimeSpan.FromSeconds(Math.Clamp(positionSeconds, 0, _mfReader.TotalTime.TotalSeconds));
                _mfReader.CurrentTime = target;
            }
            else if (_waveStreamReader != null)
            {
                var target = TimeSpan.FromSeconds(Math.Clamp(positionSeconds, 0, _waveStreamReader.TotalTime.TotalSeconds));
                _waveStreamReader.CurrentTime = target;
            }

            // Restore WaveOut device volume — the mute buffers + fade-in in Read()
            // will keep actual audio silent until it's safe, but the device is now
            // allowed to produce sound again for when the fade-in starts.
            if (_waveOut != null) _waveOut.Volume = 1f;
        }

        public void SeekRelative(double offsetSeconds)
        {
            double curPos = CurrentPosition.TotalSeconds;
            Seek(curPos + offsetSeconds);
        }

        private void OnPlaybackStopped(object? sender, StoppedEventArgs e)
        {
            // Determine if playback reached the end naturally
            bool reachedEnd = false;
            if (_waveOut != null && sender == _waveOut)
            {
                var pos = CurrentPosition;
                var dur = TotalDuration;
                if (dur.TotalSeconds > 0)
                    reachedEnd = pos >= dur - TimeSpan.FromMilliseconds(200);
            }

            PlaybackStopped?.Invoke(this, EventArgs.Empty);

            if (reachedEnd)
                TrackFinished?.Invoke(this, EventArgs.Empty);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Stop();
        }
    }
}
