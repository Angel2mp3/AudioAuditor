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
        /// Start playing with crossfade from the current track.
        /// </summary>
        public void PlayWithCrossfade(string filePath, bool normalize = false, IPlaybackSettings? playbackSettings = null)
        {
            RefreshPlaybackSettings(playbackSettings);

            if (_waveOut == null || _waveOut.PlaybackState != PlaybackState.Playing)
            {
                // Nothing playing, just play normally
                Play(filePath, normalize, playbackSettings);
                return;
            }

            // Clean up any gapless preparation before crossfade takes over
            _gaplessNext?.Dispose();
            _gaplessNext = null;
            if (_gaplessProvider != null)
            {
                _gaplessProvider.TrackSwitched -= OnGaplessTrackSwitched;
                _gaplessProvider = null;
            }

            // Move current playback to fade-out — unhook events FIRST
            CleanupFadeOut();
            _waveOut.PlaybackStopped -= OnPlaybackStopped;  // detach before moving
            _fadeOutDevice = _waveOut;
            _fadeOutReader = _reader;
            _fadeOutMfReader = _mfReader;
            _fadeOutWaveStreamReader = _waveStreamReader;
            _fadeOutSampleChannel = _sampleChannel;
            _fadeOutExtraDisposable = _extraDisposable;
            _fadeOutExtraDisposable2 = _extraDisposable2;

            _waveOut = null;
            _reader = null;
            _mfReader = null;
            _sampleChannel = null;
            _waveStreamReader = null;
            _extraDisposable = null;
            _extraDisposable2 = null;
            _currentFile = null;

            // Start the new track
            try
            {
                _currentFile = filePath;
                _normalizationGain = 1f;

                if (!AudioDecoderFactory.TryOpen(filePath, out var decoded))
                    return;

                _reader = decoded.Reader;
                _mfReader = decoded.MfReader;
                _waveStreamReader = decoded.WaveStreamReader;
                _sampleChannel = decoded.SampleChannel;
                _extraDisposable = decoded.ExtraDisposable;
                _extraDisposable2 = decoded.ExtraDisposable2;

                // Mute for crossfade fade-in
                if (_sampleChannel != null)
                    _sampleChannel.Volume = 0f;
                if (_reader != null)
                    _reader.Volume = 0f;

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
            var curve = CrossfadeCurve;

            _fadeTimer = new System.Threading.Timer(_ =>
            {
                // If the fade-out device hit EOF early, clean up immediately to avoid hanging
                if (_fadeOutDevice != null && _fadeOutDevice.PlaybackState == PlaybackState.Stopped)
                {
                    _fadeTimer?.Dispose();
                    _fadeTimer = null;
                    CleanupFadeOut();
                    ApplyVolume();
                    return;
                }

                currentStep++;
                float t = Math.Min(1f, (float)currentStep / steps);

                float outMult = CrossfadeCurveFadeOut(t, curve);
                float inMult  = CrossfadeCurveFadeIn(t, curve);

                SetFadeOutVolume(fadeOutStartVol * outMult);

                float targetVol = _userVolume * _normalizationGain;
                float fadeInVol = Math.Clamp(targetVol * inMult, 0f, 1f);
                if (_reader != null) _reader.Volume = fadeInVol;
                if (_sampleChannel != null) _sampleChannel.Volume = fadeInVol;

                if (currentStep >= steps)
                {
                    _fadeTimer?.Dispose();
                    _fadeTimer = null;
                    CleanupFadeOut();
                    ApplyVolume();
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
            var fadeOutExtra = _fadeOutExtraDisposable;
            var fadeOutExtra2 = _fadeOutExtraDisposable2;

            if (fadeOutExtra2 != null)
            {
                try { fadeOutExtra2.Dispose(); } catch { }
                _fadeOutExtraDisposable2 = null;
            }

            if (fadeOutExtra != null)
            {
                try { fadeOutExtra.Dispose(); } catch { }
                _fadeOutExtraDisposable = null;
            }

            if (_fadeOutWaveStreamReader != null
                && !ReferenceEquals(_fadeOutWaveStreamReader, _fadeOutReader)
                && !ReferenceEquals(_fadeOutWaveStreamReader, _fadeOutMfReader)
                && !ReferenceEquals(_fadeOutWaveStreamReader, fadeOutExtra)
                && !ReferenceEquals(_fadeOutWaveStreamReader, fadeOutExtra2))
            {
                try { _fadeOutWaveStreamReader.Dispose(); } catch { }
            }
            _fadeOutWaveStreamReader = null;

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

        // ── Crossfade curve helpers ──────────────────────────────────────────────

        internal static float CrossfadeCurveFadeOut(float t, CrossfadeType curve) => curve switch
        {
            CrossfadeType.EqualPower => MathF.Cos(t * MathF.PI / 2f),
            CrossfadeType.Natural    => (1f - t) * (1f - t),
            CrossfadeType.Sequential => t < 0.5f ? Math.Clamp(1f - t * 2f, 0f, 1f) : 0f,
            CrossfadeType.SmoothStep => 1f - SmoothStep01(t),
            CrossfadeType.FastFade   => (1f - t) * (1f - t) * (1f - t),
            CrossfadeType.SlowBlend  => 1f - (t * t * (2f - t)),
            _                        => 1f - t,
        };

        internal static float CrossfadeCurveFadeIn(float t, CrossfadeType curve) => curve switch
        {
            CrossfadeType.EqualPower => MathF.Sin(t * MathF.PI / 2f),
            CrossfadeType.Natural    => t * (2f - t),
            CrossfadeType.Sequential => t > 0.5f ? Math.Clamp((t - 0.5f) * 2f, 0f, 1f) : 0f,
            CrossfadeType.SmoothStep => SmoothStep01(t),
            CrossfadeType.FastFade   => 1f - ((1f - t) * (1f - t) * (1f - t)),
            CrossfadeType.SlowBlend  => t * t * (2f - t),
            _                        => t,
        };

        private static float SmoothStep01(float t)
        {
            t = Math.Clamp(t, 0f, 1f);
            return t * t * (3f - 2f * t);
        }

    }
}
