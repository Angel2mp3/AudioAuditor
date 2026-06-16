using System;
using System.IO;
using AudioQualityChecker.Abstractions;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using NAudio.Vorbis;

namespace AudioQualityChecker.Services
{
    public enum CrossfadeType
    {
        Linear,      // straight amplitude lines
        EqualPower,  // sin/cos curves for steady perceived loudness
        Natural,     // complementary quadratic
        Sequential,  // fade-out completes before fade-in begins
        SmoothStep,
        FastFade,
        SlowBlend,
    }

    public partial class AudioPlayer : IDisposable
    {
        private readonly IPlaybackSettings _playbackSettings;
        private PlaybackSettingsSnapshot _currentPlaybackSettings = null!;
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
        private WaveStream? _fadeOutWaveStreamReader;
        private SampleChannel? _fadeOutSampleChannel;
        private IDisposable? _fadeOutExtraDisposable;
        private IDisposable? _fadeOutExtraDisposable2;
        private System.Threading.Timer? _fadeTimer;
        private System.Threading.Timer? _startFadeTimer; // ramps volume at playback start to avoid clicks
        private int _crossfadeDurationMs = 5000;
        private const int FadeStepMs = 40;
        public CrossfadeType CrossfadeCurve { get; set; } = CrossfadeType.EqualPower;

        // Visualizer sample capture
        private readonly float[] _vizBuffer = new float[8192];
        private int _vizWritePos;
        private float _vizCapturedUserVolume = 1f;
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

        // Serializes reader state mutations (Seek) against the audio thread's Read so
        // that NAudio's internal state can't be corrupted by a concurrent CurrentTime=
        // write. Without this, a lyric click while scanning (heavy ThreadPool load)
        // can crash the process inside the MP3/MediaFoundation decoder.
        internal readonly object _readerLock = new();

        // Equalizer
        private Equalizer? _equalizer;
        public Equalizer? CurrentEqualizer => _equalizer;

        // Spatial Audio
        private SpatialAudioProcessor? _spatialAudio;
        public SpatialAudioProcessor? CurrentSpatialAudio => _spatialAudio;

        // Gapless playback
        private GaplessSampleProvider? _gaplessProvider;
        private GaplessNextTrack? _gaplessNext; // pre-loaded next track
        public event EventHandler? GaplessTrackChanged; // fired on seamless track switch

        public AudioPlayer()
            : this(new ThemeManagerSettings())
        {
        }

        public AudioPlayer(IPlaybackSettings playbackSettings)
        {
            _playbackSettings = playbackSettings ?? throw new ArgumentNullException(nameof(playbackSettings));
            RefreshPlaybackSettings();
        }

        private void RefreshPlaybackSettings(IPlaybackSettings? playbackSettings = null)
        {
            _currentPlaybackSettings = PlaybackSettingsSnapshot.From(playbackSettings ?? _playbackSettings);
            CrossfadeDurationSeconds = _currentPlaybackSettings.CrossfadeDurationSeconds;
            CrossfadeCurve = ToCrossfadeType(_currentPlaybackSettings.CrossfadeCurve);
        }

        private static CrossfadeType ToCrossfadeType(PlaybackCrossfadeCurve curve) => curve switch
        {
            PlaybackCrossfadeCurve.Linear => CrossfadeType.Linear,
            PlaybackCrossfadeCurve.Natural => CrossfadeType.Natural,
            PlaybackCrossfadeCurve.Sequential => CrossfadeType.Sequential,
            PlaybackCrossfadeCurve.SmoothStep => CrossfadeType.SmoothStep,
            PlaybackCrossfadeCurve.FastFade => CrossfadeType.FastFade,
            PlaybackCrossfadeCurve.SlowBlend => CrossfadeType.SlowBlend,
            _ => CrossfadeType.EqualPower
        };

        private float GetEqualizerGain(int band)
        {
            var gains = _currentPlaybackSettings.EqualizerGains;
            return band >= 0 && band < gains.Count ? gains[band] : 0f;
        }

        public float[] GetVisualizerSamples(int count)
        {
            return GetVisualizerSnapshot(count).Samples;
        }

        public (float[] Samples, float UserVolume) GetVisualizerSnapshot(int count)
        {
            lock (_vizLock)
            {
                int actual = Math.Min(count, _vizBuffer.Length);
                float[] result = new float[actual];
                int start = (_vizWritePos - actual + _vizBuffer.Length) % _vizBuffer.Length;
                for (int i = 0; i < actual; i++)
                    result[i] = _vizBuffer[(start + i) % _vizBuffer.Length];
                return (result, _vizCapturedUserVolume);
            }
        }

        /// <summary>
        /// Clears the visualizer capture ring buffer. Call on track change so the visualizer starts
        /// the new song from a clean baseline instead of bleeding the previous track's trailing
        /// samples through for the first second or two (which reads as a laggy/garbled transition).
        /// </summary>
        public void ResetVisualizerCapture()
        {
            lock (_vizLock)
            {
                Array.Clear(_vizBuffer, 0, _vizBuffer.Length);
                _vizWritePos = 0;
            }
        }

        private void CaptureVisualizerSamples(byte[] buffer, int offset, int count, WaveFormat format)
        {
            lock (_vizLock)
            {
                _vizCapturedUserVolume = _userVolume;
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

                // Serialize against Seek() so the underlying reader's state can't be
                // mutated mid-decode. Prevents process crashes when seeking under load.
                int read;
                lock (_player._readerLock)
                {
                    read = _source.Read(buffer, offset, count);
                }
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

        // Gapless inner classes + methods - see AudioPlayer.Gapless.cs

        /// <summary>
        /// Crossfade duration in seconds (1-30). Default is 5.
        /// </summary>
        public int CrossfadeDurationSeconds
        {
            get => _crossfadeDurationMs / 1000;
            set => _crossfadeDurationMs = Math.Clamp(value, 1, 30) * 1000;
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
                lock (_readerLock)
                {
                    try
                    {
                        if (_reader != null) return _reader.CurrentTime;
                        if (_mfReader != null) return _mfReader.CurrentTime;
                        if (_waveStreamReader != null) return _waveStreamReader.CurrentTime;
                    }
                    catch (Exception ex) when (ex is ObjectDisposedException or InvalidOperationException)
                    {
                    }
                }
                return TimeSpan.Zero;
            }
        }

        public TimeSpan TotalDuration
        {
            get
            {
                lock (_readerLock)
                {
                    try
                    {
                        if (_reader != null) return _reader.TotalTime;
                        if (_mfReader != null) return _mfReader.TotalTime;
                        if (_waveStreamReader != null) return _waveStreamReader.TotalTime;
                    }
                    catch (Exception ex) when (ex is ObjectDisposedException or InvalidOperationException)
                    {
                    }
                }
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

        /// <summary>
        /// Sets the target user volume without immediately applying it to the audio stream.
        /// Used during crossfade so the fade timer controls the actual volume ramp.
        /// </summary>
        public void SetUserVolume(float volume)
        {
            _userVolume = Math.Clamp(volume, 0f, 1f);
        }

        private void ApplyVolume()
        {
            _startFadeTimer?.Dispose();
            _startFadeTimer = null;
            float effective = _userVolume * _normalizationGain;
            effective = Math.Clamp(effective, 0f, 1f);
            if (_reader != null) _reader.Volume = effective;
            if (_sampleChannel != null) _sampleChannel.Volume = effective;
        }

        /// <summary>
        /// Ramps volume from 0 to the target effective volume over a short period.
        /// Prevents clicks / spikes at the very start of playback or gapless switches.
        /// </summary>
        private void StartPlaybackFadeIn(int durationMs = 150)
        {
            float targetVol = _userVolume * _normalizationGain;
            targetVol = Math.Clamp(targetVol, 0f, 1f);

            // If a fade is already running, let it continue unless we're forcing a restart
            if (_startFadeTimer != null)
            {
                _startFadeTimer?.Dispose();
                _startFadeTimer = null;
            }

            // Start from silence
            if (_reader != null) _reader.Volume = 0f;
            if (_sampleChannel != null) _sampleChannel.Volume = 0f;

            int steps = Math.Max(1, durationMs / FadeStepMs);
            int currentStep = 0;
            _startFadeTimer = new System.Threading.Timer(_ =>
            {
                currentStep++;
                float progress = Math.Min(1f, (float)currentStep / steps);
                float vol = targetVol * progress;
                if (_reader != null) _reader.Volume = vol;
                if (_sampleChannel != null) _sampleChannel.Volume = vol;
                if (currentStep >= steps)
                {
                    _startFadeTimer?.Dispose();
                    _startFadeTimer = null;
                    ApplyVolume(); // ensure exact final volume
                }
            }, null, FadeStepMs, FadeStepMs);
        }

        /// <summary>
        /// Enables or disables normalization on the currently playing track without restarting playback.
        /// </summary>
        public void SetNormalization(bool enabled)
        {
            if (enabled && _currentFile != null)
            {
                string targetFile = _currentFile;
                _ = System.Threading.Tasks.Task.Run(() =>
                {
                    float gain = CalculateNormalizationGainForFile(targetFile);
                    if (_currentFile == targetFile)
                    {
                        _normalizationGain = gain;
                        ApplyVolume();
                    }
                });
                return;
            }
            _normalizationGain = 1f;
            ApplyVolume();
        }

        public void Play(string filePath, bool normalize = false, IPlaybackSettings? playbackSettings = null)
        {
            RefreshPlaybackSettings(playbackSettings);

            if (_currentFile == filePath && _waveOut?.PlaybackState == PlaybackState.Paused)
            {
                _waveOut.Play();
                float currentEffective = _userVolume * _normalizationGain;
                currentEffective = Math.Clamp(currentEffective, 0f, 1f);
                float actualVol = _reader?.Volume ?? (_sampleChannel?.Volume ?? currentEffective);
                if (Math.Abs(actualVol - currentEffective) > 0.05f)
                    StartPlaybackFadeIn(80);
                PlaybackStarted?.Invoke(this, EventArgs.Empty);
                return;
            }

            // Adopt the pre-opened (warm) decoder if it's for this track; otherwise drop the stale
            // prediction so its file handle isn't leaked. Stop() (below) is deliberately left as the
            // immediate teardown and does NOT touch the pre-open.
            bool adoptedWarmDecoder = TryTakePreopenedDecoder(filePath, out var decoded);
            if (!adoptedWarmDecoder)
                DisposePreparedDecoder();

            Stop();

            try
            {
                _currentFile = filePath;
                _normalizationGain = 1f;

                // Unified decoder fallback chain (cold open only when no warm decoder was ready).
                if (!adoptedWarmDecoder && !AudioDecoderFactory.TryOpen(filePath, out decoded))
                {
                    string fileExt = System.IO.Path.GetExtension(filePath);
                    throw new InvalidOperationException(
                        $"This audio format ({fileExt}) is not supported for playback. " +
                        "The file may use an unsupported codec or proprietary encoding.");
                }

                _reader = decoded.Reader;
                _mfReader = decoded.MfReader;
                _waveStreamReader = decoded.WaveStreamReader;
                _sampleChannel = decoded.SampleChannel;
                _extraDisposable = decoded.ExtraDisposable;
                _extraDisposable2 = decoded.ExtraDisposable2;

                // Apply normalization if requested — run in background so playback starts immediately
                if (normalize)
                {
                    string targetFile = filePath;
                    _ = System.Threading.Tasks.Task.Run(() =>
                    {
                        float gain = CalculateNormalizationGainForFile(targetFile);
                        if (_currentFile == targetFile)
                        {
                            _normalizationGain = gain;
                            ApplyVolume();
                        }
                    });
                }

                ApplyVolume();
                StartPlaybackFadeIn();

                // Insert equalizer into pipeline
                ISampleProvider sampleSource;
                if (_reader != null)
                    sampleSource = _reader;
                else
                    sampleSource = _sampleChannel!;

                // Wrap in gapless provider when gapless playback is enabled
                if (_currentPlaybackSettings.GaplessEnabled && !_currentPlaybackSettings.CrossfadeEnabled)
                {
                    _gaplessProvider = new GaplessSampleProvider(sampleSource);
                    _gaplessProvider.TrackSwitched += OnGaplessTrackSwitched;
                    sampleSource = _gaplessProvider;
                }
                else
                {
                    _gaplessProvider = null;
                }

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
                _equalizer.Enabled = _currentPlaybackSettings.EqualizerEnabled;
                for (int i = 0; i < 10; i++)
                    _equalizer.UpdateBand(i, GetEqualizerGain(i));

                _spatialAudio = new SpatialAudioProcessor(_equalizer);
                _spatialAudio.Enabled = _currentPlaybackSettings.SpatialAudioEnabled;

                IWaveProvider finalSource = new SampleToWaveProvider(_spatialAudio);

                int sampleRate = _spatialAudio.WaveFormat.SampleRate;
                int baseLatency = sampleRate > 48000 ? 300 : 200;
                int baseBuffers = sampleRate > 48000 ? 4 : 3;
                _waveOut = new WaveOutEvent
                {
                    DesiredLatency = useLargeBuffers ? Math.Max(baseLatency, 300) : baseLatency,
                    NumberOfBuffers = useLargeBuffers ? Math.Max(baseBuffers, 4) : baseBuffers
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

        // Crossfade - see AudioPlayer.Crossfade.cs
        /// <summary>
        /// Calculates normalization gain for a specific file path.
        /// Returns the gain value without modifying instance state.
        /// </summary>
        private float CalculateNormalizationGainForFile(string filePath)
        {
            const float targetPeak = 0.891f;
            float maxSample = 0f;

            try
            {
                if (!AudioDecoderFactory.TryOpen(filePath, out var decoded))
                    return 1f;

                ISampleProvider scanner = decoded.Source!;
                IDisposable scanDisposable = decoded.Reader
                    ?? (IDisposable?)decoded.MfReader
                    ?? decoded.WaveStreamReader
                    ?? decoded.ExtraDisposable
                    ?? decoded.ExtraDisposable2
                    ?? throw new InvalidOperationException("No disposable scanner available");

                float[] buf = new float[8192];
                int read;
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

                scanDisposable.Dispose();

                if (maxSample > 0.001f)
                    return Math.Min(targetPeak / maxSample, 3f);
            }
            catch { }
            return 1f;
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

            // Clean up gapless resources
            _gaplessNext?.Dispose();
            _gaplessNext = null;
            if (_gaplessProvider != null)
            {
                _gaplessProvider.TrackSwitched -= OnGaplessTrackSwitched;
                _gaplessProvider = null;
            }

            _startFadeTimer?.Dispose();
            _startFadeTimer = null;

            if (_waveOut != null)
            {
                _waveOut.PlaybackStopped -= OnPlaybackStopped;
                try { _waveOut.Stop(); } catch { }
                _waveOut.Dispose();
                _waveOut = null;
            }

            _sampleChannel = null;

            // Serialize reader disposal against Seek() to prevent AV if Seek()
            // is currently reading CurrentTime on another thread.
            lock (_readerLock)
            {
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
            }
            // Drop pipeline references so a subsequent failed Play() can't observe a half-built chain
            _equalizer = null;
            _spatialAudio = null;
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
            var waveOut = _waveOut;
            float restoreDeviceVolume = 1f;
            bool shouldRestoreDeviceVolume = false;

            try
            {
                if (waveOut != null)
                {
                    restoreDeviceVolume = waveOut.Volume;
                    waveOut.Volume = 0f;
                    shouldRestoreDeviceVolume = true;
                }

                // Increment generation counter so the audio thread knows the current
                // buffer (if one is being filled right now) is tainted
                System.Threading.Interlocked.Increment(ref _seekGeneration);

                // Hard-mute for 3 buffer fills while decoder stabilizes
                System.Threading.Interlocked.Exchange(ref _seekMuteBuffers, 3);

                // Reset DSP processor state
                _equalizer?.ResetFilterState();
                _spatialAudio?.ResetBuffers();

                // Arm post-mute fade-in
                _seekFadeSamplesRemaining = _seekFadeTotalSamples;

                // Serialize the actual reader-position write against the audio thread's
                // Read() so we don't crash inside NAudio when seeking under heavy load.
                lock (_readerLock)
                {
                    if (_reader != null)
                    {
                        var target = TimeSpan.FromSeconds(Math.Clamp(positionSeconds, 0, Math.Max(0, _reader.TotalTime.TotalSeconds)));
                        _reader.CurrentTime = target;
                    }
                    else if (_mfReader != null)
                    {
                        var target = TimeSpan.FromSeconds(Math.Clamp(positionSeconds, 0, Math.Max(0, _mfReader.TotalTime.TotalSeconds)));
                        _mfReader.CurrentTime = target;
                    }
                    else if (_waveStreamReader != null)
                    {
                        var target = TimeSpan.FromSeconds(Math.Clamp(positionSeconds, 0, Math.Max(0, _waveStreamReader.TotalTime.TotalSeconds)));
                        _waveStreamReader.CurrentTime = target;
                    }
                }
            }
            catch (Exception ex) when (ex is ObjectDisposedException or InvalidOperationException or IOException)
            {
                System.Diagnostics.Debug.WriteLine($"AudioPlayer.Seek failed: {ex.GetType().Name}: {ex.Message}");
            }
            finally
            {
                // Restore WaveOut device volume even when reader seeking throws.
                // The mute buffers + fade-in in Read() still keep audio silent until
                // it is safe, but the output device cannot remain stuck muted.
                if (shouldRestoreDeviceVolume)
                {
                    try
                    {
                        if (waveOut != null)
                            waveOut.Volume = restoreDeviceVolume <= 0f ? 1f : restoreDeviceVolume;
                    }
                    catch
                    {
                    }
                }
            }
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
            DisposePreparedDecoder();
        }
    }
}
