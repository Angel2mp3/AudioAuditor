using System;
using System.IO;
using Concentus.Structs;
using Concentus.Oggfile;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using NAudio.Vorbis;

namespace AudioQualityChecker.Services
{
    /// <summary>
    /// Reads an Opus file from an Ogg container using Concentus, producing IEEE float PCM.
    /// </summary>
    internal class OpusFileReader : WaveStream
    {
        private readonly Stream _stream;
        private readonly WaveFormat _waveFormat;
        private byte[] _pcmData = Array.Empty<byte>();
        private int _readOffset;
        private long _position;
        private readonly long _totalBytes;

        public OpusFileReader(string filePath)
        {
            _stream = File.OpenRead(filePath);
            int channels = 2;
            int sampleRate = 48000;

            _stream.Position = 0;
#pragma warning disable CS0618 // OpusDecoder constructor is obsolete but works fine
            var decoder = new OpusDecoder(sampleRate, channels);
#pragma warning restore CS0618
            _waveFormat = WaveFormat.CreateIeeeFloatWaveFormat(sampleRate, channels);

            // Read all Opus packets and decode to short[], then convert to float
            var oggReader = new OpusOggReadStream(decoder, _stream);
            var allSamples = new List<float>();
            while (oggReader.HasNextPacket)
            {
                short[]? pcm = oggReader.DecodeNextPacket();
                if (pcm != null && pcm.Length > 0)
                {
                    // Convert short samples to float (-1..1)
                    for (int i = 0; i < pcm.Length; i++)
                        allSamples.Add(pcm[i] / 32768f);
                }
            }

            // Convert float list to byte array (IEEE float format)
            _pcmData = new byte[allSamples.Count * 4];
            for (int i = 0; i < allSamples.Count; i++)
            {
                byte[] bytes = BitConverter.GetBytes(allSamples[i]);
                Buffer.BlockCopy(bytes, 0, _pcmData, i * 4, 4);
            }
            _totalBytes = _pcmData.Length;
            _readOffset = 0;
            _position = 0;
        }

        public override WaveFormat WaveFormat => _waveFormat;
        public override long Length => _totalBytes;

        public override long Position
        {
            get => _position;
            set
            {
                _position = Math.Clamp(value, 0, _totalBytes);
                _readOffset = (int)_position;
            }
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            int available = _pcmData.Length - _readOffset;
            int toCopy = Math.Min(count, available);
            if (toCopy <= 0) return 0;

            Buffer.BlockCopy(_pcmData, _readOffset, buffer, offset, toCopy);
            _readOffset += toCopy;
            _position += toCopy;
            return toCopy;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                _stream.Dispose();
            base.Dispose(disposing);
        }
    }

    /// <summary>
    /// Reads DSD (.dsf/.dff) files by converting DSD bitstream to PCM at 176400 Hz.
    /// </summary>
    internal class DsdToPcmReader : WaveStream
    {
        private readonly WaveFormat _waveFormat;
        private readonly byte[] _pcmData;
        private long _position;

        public DsdToPcmReader(string filePath)
        {
            string ext = Path.GetExtension(filePath).ToLowerInvariant();
            byte[] raw = File.ReadAllBytes(filePath);

            int channels = 2;
            int dsdSampleRate = 2822400;
            byte[] dsdData;

            if (ext == ".dsf")
            {
                // DSF format parsing
                if (raw.Length < 92) throw new InvalidDataException("Invalid DSF file");
                // DSD chunk: "DSD " at offset 0
                // fmt chunk starts at offset 28 typically
                int fmtOffset = 28;
                if (raw.Length > fmtOffset + 52)
                {
                    int formatVersion = BitConverter.ToInt32(raw, fmtOffset + 8);
                    channels = BitConverter.ToInt32(raw, fmtOffset + 20);
                    dsdSampleRate = BitConverter.ToInt32(raw, fmtOffset + 16);
                    int bitsPerSample = BitConverter.ToInt32(raw, fmtOffset + 24);
                    long sampleCount = BitConverter.ToInt64(raw, fmtOffset + 28);
                    int blockSize = BitConverter.ToInt32(raw, fmtOffset + 36);

                    // Data chunk follows fmt
                    long dataOffset = BitConverter.ToInt64(raw, 20); // Pointer to metadata, but data is after fmt
                    // Actually in DSF, offset to data chunk is in DSD header
                    // The data chunk offset: read fmt chunk size + 28 (DSD chunk)
                    long fmtSize = BitConverter.ToInt64(raw, fmtOffset + 4);
                    long dataChunkOffset = 28 + fmtSize;
                    if (dataChunkOffset + 12 < raw.Length)
                    {
                        long dataSize = BitConverter.ToInt64(raw, (int)dataChunkOffset + 4);
                        int dataStart = (int)dataChunkOffset + 12;
                        int dataLen = (int)Math.Min(dataSize - 12, raw.Length - dataStart);
                        dsdData = new byte[dataLen];
                        Array.Copy(raw, dataStart, dsdData, 0, dataLen);
                    }
                    else
                    {
                        dsdData = Array.Empty<byte>();
                    }
                }
                else
                {
                    dsdData = Array.Empty<byte>();
                }
            }
            else // .dff (DSDIFF)
            {
                // Simplified DFF parsing - just find DSD data after headers
                // DFF files start with "FRM8" then "DSD "
                int dataStart = 0;
                for (int i = 0; i < Math.Min(raw.Length - 4, 8192); i++)
                {
                    if (raw[i] == 'D' && raw[i+1] == 'S' && raw[i+2] == 'D' && raw[i+3] == ' '
                        && i > 4) // Skip the initial DSD marker
                    {
                        dataStart = i + 12; // Skip chunk header
                        break;
                    }
                }
                if (dataStart == 0) dataStart = 512; // fallback
                dsdData = new byte[raw.Length - dataStart];
                Array.Copy(raw, dataStart, dsdData, 0, dsdData.Length);
            }

            // Convert DSD to PCM: each DSD byte = 8 1-bit samples
            // Decimate DSD to PCM at 1/16 rate using simple FIR averaging
            int decimationFactor = 16;
            int pcmSampleRate = dsdSampleRate / decimationFactor; // typically 176400
            if (pcmSampleRate > 192000) pcmSampleRate = 176400;

            int dsdBytesPerChannel = dsdData.Length / channels;
            int pcmSamplesPerChannel = (dsdBytesPerChannel * 8) / decimationFactor;

            var pcmSamples = new float[pcmSamplesPerChannel * channels];

            for (int ch = 0; ch < channels; ch++)
            {
                for (int i = 0; i < pcmSamplesPerChannel; i++)
                {
                    // Average decimationFactor DSD bits
                    int dsdBitStart = i * decimationFactor;
                    float sum = 0;
                    for (int b = 0; b < decimationFactor; b++)
                    {
                        int bitIdx = dsdBitStart + b;
                        int byteIdx = ch * dsdBytesPerChannel + bitIdx / 8;
                        int bitPos = 7 - (bitIdx % 8);
                        if (byteIdx < dsdData.Length)
                        {
                            int bit = (dsdData[byteIdx] >> bitPos) & 1;
                            sum += bit == 1 ? 1f : -1f;
                        }
                    }
                    pcmSamples[i * channels + ch] = sum / decimationFactor;
                }
            }

            // Convert float PCM to 16-bit PCM bytes
            _waveFormat = new WaveFormat(pcmSampleRate, 16, channels);
            _pcmData = new byte[pcmSamples.Length * 2];
            for (int i = 0; i < pcmSamples.Length; i++)
            {
                short sample = (short)(Math.Clamp(pcmSamples[i], -1f, 1f) * 32767);
                _pcmData[i * 2] = (byte)(sample & 0xFF);
                _pcmData[i * 2 + 1] = (byte)((sample >> 8) & 0xFF);
            }
            _position = 0;
        }

        public override WaveFormat WaveFormat => _waveFormat;
        public override long Length => _pcmData.Length;

        public override long Position
        {
            get => _position;
            set => _position = Math.Clamp(value, 0, _pcmData.Length);
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            int available = _pcmData.Length - (int)_position;
            int toCopy = Math.Min(count, available);
            if (toCopy <= 0) return 0;

            Buffer.BlockCopy(_pcmData, (int)_position, buffer, offset, toCopy);
            _position += toCopy;
            return toCopy;
        }
    }

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
                int read = _source.Read(buffer, offset, count);
                if (read > 0)
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
                    try
                    {
                        var opusReader = new OpusFileReader(filePath);
                        _sampleChannel = new SampleChannel(opusReader, true);
                        playbackSource = new SampleToWaveProvider(_sampleChannel);
                        _extraDisposable = opusReader;
                        _waveStreamReader = opusReader;
                        opened = true;
                    }
                    catch
                    {
                        _sampleChannel = null;
                        _extraDisposable = null;
                        _waveStreamReader = null;
                    }
                }

                // For Ogg Vorbis files, use VorbisWaveReader
                if (!opened && (ext == ".ogg"))
                {
                    try
                    {
                        var vorbisReader = new VorbisWaveReader(filePath);
                        _sampleChannel = new SampleChannel(vorbisReader, true);
                        playbackSource = new SampleToWaveProvider(_sampleChannel);
                        _extraDisposable = vorbisReader;
                        _waveStreamReader = vorbisReader;
                        opened = true;
                    }
                    catch
                    {
                        _sampleChannel = null;
                        _extraDisposable = null;
                        _waveStreamReader = null;
                    }
                }

                // For DSD files (.dsf, .dff), use DSD-to-PCM converter
                if (!opened && (ext == ".dsf" || ext == ".dff"))
                {
                    try
                    {
                        var dsdReader = new DsdToPcmReader(filePath);
                        _sampleChannel = new SampleChannel(dsdReader, true);
                        playbackSource = new SampleToWaveProvider(_sampleChannel);
                        _extraDisposable = dsdReader;
                        _waveStreamReader = dsdReader;
                        opened = true;
                    }
                    catch
                    {
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

                // Try VorbisWaveReader as fallback for any Ogg-based format
                if (!opened)
                {
                    try
                    {
                        var vorbisReader = new VorbisWaveReader(filePath);
                        _sampleChannel = new SampleChannel(vorbisReader, true);
                        playbackSource = new SampleToWaveProvider(_sampleChannel);
                        _extraDisposable = vorbisReader;
                        _waveStreamReader = vorbisReader;
                        opened = true;
                    }
                    catch
                    {
                        _sampleChannel = null;
                        _extraDisposable = null;
                        _waveStreamReader = null;
                    }
                }

                // Try WaveFileReader for raw WAV/RF64/BWF (including 24-bit)
                if (!opened)
                {
                    try
                    {
                        var rawReader = new WaveFileReader(filePath);
                        // For 24-bit or other non-standard WAV, convert to PCM then resample
                        if (rawReader.WaveFormat.BitsPerSample == 24 || rawReader.WaveFormat.BitsPerSample == 32)
                        {
                            // Convert to IEEE float for 24/32-bit WAV
                            var floatProvider = new Wave32To16Stream(rawReader);
                            _sampleChannel = new SampleChannel(floatProvider, true);
                            _extraDisposable2 = floatProvider;
                        }
                        else
                        {
                            var pcmStream = WaveFormatConversionStream.CreatePcmStream(rawReader);
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
                        _sampleChannel = null;
                        _waveStreamReader = null;
                    }
                }

                // Try Opus decoder as last resort for any unrecognized file
                if (!opened)
                {
                    try
                    {
                        var opusReader = new OpusFileReader(filePath);
                        _sampleChannel = new SampleChannel(opusReader, true);
                        playbackSource = new SampleToWaveProvider(_sampleChannel);
                        _extraDisposable = opusReader;
                        _waveStreamReader = opusReader;
                        opened = true;
                    }
                    catch
                    {
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
                // Try native rate first, fall back to resample on failure.
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
                Stop();
                throw;
            }
        }

        /// <summary>
        /// Builds the EQ → Spatial → WaveOut pipeline and attempts to init + play.
        /// Returns true on success. If resample is requested, inserts a resampler before the EQ.
        /// On failure, disposes the WaveOutEvent so the caller can retry.
        /// </summary>
        private bool TryInitPlaybackPipeline(ISampleProvider source, bool resample, int targetRate = 48000)
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
                _waveOut = new WaveOutEvent
                {
                    DesiredLatency = sampleRate > 48000 ? 400 : 250,
                    NumberOfBuffers = sampleRate > 48000 ? 5 : 3
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
                    try
                    {
                        var opusReader = new OpusFileReader(filePath);
                        _sampleChannel = new SampleChannel(opusReader, true);
                        _sampleChannel.Volume = 0f;
                        playbackSource = new SampleToWaveProvider(_sampleChannel);
                        _extraDisposable = opusReader;
                        _waveStreamReader = opusReader;
                        opened = true;
                    }
                    catch { _sampleChannel = null; _extraDisposable = null; _waveStreamReader = null; }
                }

                // Ogg Vorbis
                if (!opened && ext == ".ogg")
                {
                    try
                    {
                        var vorbisReader = new VorbisWaveReader(filePath);
                        _sampleChannel = new SampleChannel(vorbisReader, true);
                        _sampleChannel.Volume = 0f;
                        playbackSource = new SampleToWaveProvider(_sampleChannel);
                        _extraDisposable = vorbisReader;
                        _waveStreamReader = vorbisReader;
                        opened = true;
                    }
                    catch { _sampleChannel = null; _extraDisposable = null; _waveStreamReader = null; }
                }

                // DSD
                if (!opened && (ext == ".dsf" || ext == ".dff"))
                {
                    try
                    {
                        var dsdReader = new DsdToPcmReader(filePath);
                        _sampleChannel = new SampleChannel(dsdReader, true);
                        _sampleChannel.Volume = 0f;
                        playbackSource = new SampleToWaveProvider(_sampleChannel);
                        _extraDisposable = dsdReader;
                        _waveStreamReader = dsdReader;
                        opened = true;
                    }
                    catch { _sampleChannel = null; _extraDisposable = null; _waveStreamReader = null; }
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
