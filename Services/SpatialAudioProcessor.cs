using System;
using NAudio.Wave;

namespace AudioQualityChecker.Services
{
    /// <summary>
    /// Spatial audio processor that creates a wider, more immersive headphone soundstage.
    /// Uses gentle crossfeed, subtle stereo widening, and early reflections for a speaker-like experience.
    /// </summary>
    public class SpatialAudioProcessor : ISampleProvider
    {
        private readonly ISampleProvider _source;
        private volatile bool _enabled;

        // Crossfeed parameters — kept subtle to avoid distortion
        private const float CrossfeedAmount = 0.18f;  // Gentle bleed to opposite channel
        private const float WideningAmount = 0.20f;    // Subtle stereo widening
        private const float OutputGain = 0.85f;        // Compensate for summed signals

        // Simple delay line for HRTF-like interaural time difference (ITD)
        private readonly float[] _delayBufferL;
        private readonly float[] _delayBufferR;
        private int _delayWritePos;
        private readonly int _delaySamples; // ~0.3ms delay for crossfeed

        // Low-pass state for crossfeed (head shadow simulation)
        private float _lpStateL;
        private float _lpStateR;
        private readonly float _lpCoeff; // Low-pass coefficient

        // Early reflection simulation (simple delay tap, no feedback)
        private readonly float[] _reflectionBufferL;
        private readonly float[] _reflectionBufferR;
        private int _reflectionWritePos;
        private readonly int _reflectionDelaySamples; // ~15ms delay
        private const float ReflectionGain = 0.06f;   // Very subtle room cue

        public SpatialAudioProcessor(ISampleProvider source)
        {
            _source = source;

            int sampleRate = source.WaveFormat.SampleRate;

            // ITD delay: ~0.3ms (interaural time difference)
            _delaySamples = Math.Max(1, (int)(sampleRate * 0.0003));
            _delayBufferL = new float[_delaySamples + 1];
            _delayBufferR = new float[_delaySamples + 1];

            // Low-pass for crossfeed: approximate head shadow ~3.5kHz
            double cutoffHz = 3500.0;
            double rc = 1.0 / (2.0 * Math.PI * cutoffHz);
            double dt = 1.0 / sampleRate;
            _lpCoeff = (float)(dt / (rc + dt));

            // Early reflection delay: ~15ms
            _reflectionDelaySamples = Math.Max(1, (int)(sampleRate * 0.015));
            _reflectionBufferL = new float[_reflectionDelaySamples + 1];
            _reflectionBufferR = new float[_reflectionDelaySamples + 1];
        }

        public WaveFormat WaveFormat => _source.WaveFormat;

        public bool Enabled
        {
            get => _enabled;
            set => _enabled = value;
        }

        public int Read(float[] buffer, int offset, int count)
        {
            int read = _source.Read(buffer, offset, count);

            if (!_enabled || _source.WaveFormat.Channels != 2)
                return read;

            // Process stereo pairs
            for (int i = offset; i < offset + read - 1; i += 2)
            {
                float left = buffer[i];
                float right = buffer[i + 1];

                // 1. Stereo widening: gently enhance side signal (L-R) relative to mid (L+R)
                float mid = (left + right) * 0.5f;
                float side = (left - right) * 0.5f;
                side *= (1f + WideningAmount);
                float widenedL = mid + side;
                float widenedR = mid - side;

                // 2. Crossfeed with delay and low-pass (head shadow simulation)
                int delayReadPos = (_delayWritePos - _delaySamples + _delayBufferL.Length) % _delayBufferL.Length;
                float delayedL = _delayBufferL[delayReadPos];
                float delayedR = _delayBufferR[delayReadPos];

                // Write current samples to delay buffers
                _delayBufferL[_delayWritePos] = widenedL;
                _delayBufferR[_delayWritePos] = widenedR;
                _delayWritePos = (_delayWritePos + 1) % _delayBufferL.Length;

                // Low-pass the crossfeed (simulates head shadow — high frequencies are more attenuated)
                _lpStateL += _lpCoeff * (delayedR - _lpStateL);
                _lpStateR += _lpCoeff * (delayedL - _lpStateR);

                float crossfedL = widenedL + _lpStateL * CrossfeedAmount;
                float crossfedR = widenedR + _lpStateR * CrossfeedAmount;

                // 3. Early reflection (subtle room cue — opposite channel for width)
                int refReadPos = (_reflectionWritePos - _reflectionDelaySamples + _reflectionBufferL.Length) % _reflectionBufferL.Length;
                float refL = _reflectionBufferL[refReadPos];
                float refR = _reflectionBufferR[refReadPos];

                _reflectionBufferL[_reflectionWritePos] = crossfedL;
                _reflectionBufferR[_reflectionWritePos] = crossfedR;
                _reflectionWritePos = (_reflectionWritePos + 1) % _reflectionBufferL.Length;

                float finalL = (crossfedL + refR * ReflectionGain) * OutputGain;
                float finalR = (crossfedR + refL * ReflectionGain) * OutputGain;

                // Smooth soft-clip to prevent distortion
                buffer[i] = SoftClip(finalL);
                buffer[i + 1] = SoftClip(finalR);
            }

            return read;
        }

        /// <summary>
        /// Hyperbolic tangent soft-clipper — smooth, no discontinuities.
        /// </summary>
        private static float SoftClip(float sample)
        {
            // Fast tanh approximation — smooth and continuous at all levels
            if (sample > 3f) return 1f;
            if (sample < -3f) return -1f;
            float s2 = sample * sample;
            return sample * (27f + s2) / (27f + 9f * s2);
        }
    }
}
