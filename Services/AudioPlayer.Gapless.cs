using System;
using System.IO;
using AudioQualityChecker.Abstractions;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using NAudio.Vorbis;

namespace AudioQualityChecker.Services
{
    public partial class AudioPlayer
    {        /// <summary>
        /// ISampleProvider that concatenates the current source with a pre-loaded next source
        /// for gapless playback. When the current source is exhausted, seamlessly switches to
        /// the next source without dropping any samples.
        /// </summary>
        private class GaplessSampleProvider : ISampleProvider
        {
            private ISampleProvider _current;
            private ISampleProvider? _next;
            private readonly object _lock = new();
            private bool _ended;

            public event Action? TrackSwitched;

            public GaplessSampleProvider(ISampleProvider initial)
            {
                _current = initial;
            }

            public WaveFormat WaveFormat => _current.WaveFormat;

            public void SetNext(ISampleProvider? next)
            {
                lock (_lock) _next = next;
            }

            /// <summary>
            /// Replace the current source entirely (used when the UI seeks or the track
            /// is changed manually while gapless is active).
            /// </summary>
            public void ReplaceCurrent(ISampleProvider newSource)
            {
                lock (_lock)
                {
                    _current = newSource;
                    _ended = false;
                }
            }

            public int Read(float[] buffer, int offset, int count)
            {
                if (_ended) return 0;

                int read = _current.Read(buffer, offset, count);

                if (read < count)
                {
                    // Current source exhausted — try switching to next
                    ISampleProvider? next;
                    lock (_lock)
                    {
                        next = _next;
                        _next = null;
                    }

                    if (next != null)
                    {
                        _current = next;
                        int remaining = count - read;
                        read += _current.Read(buffer, offset + read, remaining);
                        GaplessTrace.Log("Read: current exhausted, switched to next; firing TrackSwitched");
                        TrackSwitched?.Invoke();
                    }
                    else if (read == 0)
                    {
                        GaplessTrace.Log("Read: current exhausted, NO next prepared -> _ended (will fall back to TrackFinished)");
                        _ended = true;
                    }
                }

                return read;
            }
        }

        /// <summary>
        /// Holds pre-loaded resources for the next gapless track.
        /// </summary>
        private class GaplessNextTrack : IDisposable
        {
            public string FilePath = "";
            public AudioFileReader? Reader;
            public MediaFoundationReader? MfReader;
            public WaveStream? WaveStreamReader;
            public SampleChannel? SampleChannel;
            public IDisposable? ExtraDisposable;
            public ISampleProvider? Source;
            public float NormalizationGain = 1f;

            public void Dispose()
            {
                Reader?.Dispose();
                MfReader?.Dispose();
                if (WaveStreamReader != null && WaveStreamReader != Reader)
                    (WaveStreamReader as IDisposable)?.Dispose();
                ExtraDisposable?.Dispose();
            }
        }

        // ─── Gapless Playback ───

        /// <summary>
        /// Pre-loads the next track for seamless gapless transition.
        /// Call when the current track has a few seconds remaining.
        /// </summary>
        public void PrepareGapless(string filePath, bool normalize = false, IPlaybackSettings? playbackSettings = null)
        {
            if (playbackSettings != null)
                RefreshPlaybackSettings(playbackSettings);

            GaplessTrace.Log($"PrepareGapless: start file={GaplessTrace.Name(filePath)} providerActive={_gaplessProvider != null}");
            if (_gaplessProvider == null) return;

            // Clean up any previous preparation
            _gaplessNext?.Dispose();
            _gaplessNext = null;

            try
            {
                var next = new GaplessNextTrack { FilePath = filePath };

                if (!AudioDecoderFactory.TryOpen(filePath, out var decoded))
                {
                    GaplessTrace.Log($"PrepareGapless: TryOpen FAILED file={GaplessTrace.Name(filePath)} -> fallback");
                    next.Dispose();
                    return; // will fall back to normal Play() when track ends
                }

                next.Reader = decoded.Reader;
                next.MfReader = decoded.MfReader;
                next.WaveStreamReader = decoded.WaveStreamReader;
                next.SampleChannel = decoded.SampleChannel;
                next.ExtraDisposable = decoded.ExtraDisposable;

                ISampleProvider? source = decoded.Source;

                // Apply normalization if requested
                if (normalize && next.Reader != null)
                {
                    try
                    {
                        float peak = 0f;
                        float[] normBuf = new float[4096];
                        next.Reader.Position = 0;
                        int read2;
                        while ((read2 = next.Reader.Read(normBuf, 0, normBuf.Length)) > 0)
                        {
                            for (int j = 0; j < read2; j++)
                            {
                                float abs = Math.Abs(normBuf[j]);
                                if (abs > peak) peak = abs;
                            }
                        }
                        next.Reader.Position = 0;
                        if (peak > 0.001f)
                        {
                            float targetDb = -1f;
                            float targetLinear = (float)Math.Pow(10, targetDb / 20);
                            next.NormalizationGain = targetLinear / peak;
                        }
                    }
                    catch { next.Reader.Position = 0; }
                }

                // Resample / remix if format doesn't match current output
                var currentFormat = _gaplessProvider.WaveFormat;
                if (source == null)
                {
                    next.Dispose();
                    return;
                }
                if (source.WaveFormat.Channels != currentFormat.Channels)
                {
                    if (source.WaveFormat.Channels == 1 && currentFormat.Channels == 2)
                        source = new MonoToStereoSampleProvider(source);
                    else if (source.WaveFormat.Channels == 2 && currentFormat.Channels == 1)
                        source = new StereoToMonoSampleProvider(source);
                }
                if (source.WaveFormat.SampleRate != currentFormat.SampleRate)
                {
                    source = new WdlResamplingSampleProvider(source, currentFormat.SampleRate);
                }

                next.Source = source;
                _gaplessNext = next;
                _gaplessProvider.SetNext(source);
                GaplessTrace.Log($"PrepareGapless: SetNext DONE file={GaplessTrace.Name(filePath)}");
            }
            catch (Exception ex)
            {
                GaplessTrace.Log($"PrepareGapless: EXCEPTION {ex.GetType().Name}: {ex.Message}");
                _gaplessNext?.Dispose();
                _gaplessNext = null;
                // Gapless prep failed — normal TrackFinished will handle transition
            }
        }

        /// <summary>
        /// Whether a gapless next track has been prepared.
        /// </summary>
        public bool IsGaplessPrepared => _gaplessNext != null;

        /// <summary>
        /// Whether gapless playback is currently active (provider is in the pipeline).
        /// </summary>
        public bool IsGaplessActive => _gaplessProvider != null;

        private void OnGaplessTrackSwitched()
        {
            // This is called from the audio callback thread, not the UI thread.
            // Marshal to the UI dispatcher so handlers don't get cross-thread exceptions.
            var dispatcher = System.Windows.Application.Current?.Dispatcher;
            GaplessTrace.Log($"OnGaplessTrackSwitched: invoked; dispatcher={(dispatcher != null ? "present" : "NULL")}");
            if (dispatcher != null)
            {
                dispatcher.BeginInvoke(() => HandleGaplessTrackSwitched());
                return;
            }
            HandleGaplessTrackSwitched();
        }

        private void HandleGaplessTrackSwitched()
        {
            GaplessTrace.Log($"HandleGaplessTrackSwitched: entry; gaplessNext={(_gaplessNext != null ? "present" : "NULL")}");
            if (_gaplessNext == null)
            {
                GaplessTrace.Log("HandleGaplessTrackSwitched: EARLY RETURN (gaplessNext null) -> GaplessTrackChanged NOT fired");
                return;
            }

            var next = _gaplessNext;
            _gaplessNext = null;

            // Dispose old track resources
            _reader?.Dispose();
            _mfReader?.Dispose();
            _extraDisposable?.Dispose();
            _extraDisposable2?.Dispose();
            if (_waveStreamReader != null && _waveStreamReader != _reader)
                (_waveStreamReader as IDisposable)?.Dispose();

            // Adopt new track resources
            _currentFile = next.FilePath;
            _reader = next.Reader;
            _mfReader = next.MfReader;
            _waveStreamReader = next.WaveStreamReader;
            _sampleChannel = next.SampleChannel;
            _extraDisposable = next.ExtraDisposable;
            _extraDisposable2 = null;
            _normalizationGain = next.NormalizationGain;
            StartPlaybackFadeIn(150);

            // Fire event on UI thread
            GaplessTrace.Log($"HandleGaplessTrackSwitched: adopted file={GaplessTrace.Name(_currentFile)}; firing GaplessTrackChanged (subscribed={GaplessTrackChanged != null})");
            GaplessTrackChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}
