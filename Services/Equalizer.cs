using System;
using NAudio.Dsp;
using NAudio.Wave;

namespace AudioQualityChecker.Services
{
    /// <summary>
    /// 10-band equalizer using low-shelf, peaking, and high-shelf BiQuad filters.
    /// Bands: 32, 64, 125, 250, 500, 1K, 2K, 4K, 8K, 16K Hz
    /// Uses shelf filters at extremes and wide-Q peaking filters in the middle
    /// for a smooth, musical response that avoids resonance artifacts.
    /// </summary>
    public class Equalizer : ISampleProvider
    {
        private readonly ISampleProvider _source;
        private readonly EqualizerBand[] _bands;
        private BiQuadFilter[,] _filters; // [channel, band]
        private readonly int _channels;
        private readonly int _sampleRate;
        private bool _enabled;

        public static readonly float[] BandFrequencies =
            { 32, 64, 125, 250, 500, 1000, 2000, 4000, 8000, 16000 };

        public static readonly string[] BandLabels =
            { "32", "64", "125", "250", "500", "1K", "2K", "4K", "8K", "16K" };

        // Wider Q for smoother overlap — roughly 1.5 octave per band
        // Lower Q = wider bandwidth = smoother, more musical response
        private static readonly float[] BandQ =
            { 0.6f, 0.7f, 0.8f, 0.9f, 1.0f, 1.0f, 0.9f, 0.8f, 0.7f, 0.6f };

        public WaveFormat WaveFormat => _source.WaveFormat;
        public bool Enabled
        {
            get => _enabled;
            set => _enabled = value;
        }

        public EqualizerBand[] Bands => _bands;

        public Equalizer(ISampleProvider source)
        {
            _source = source;
            _channels = source.WaveFormat.Channels;
            _sampleRate = source.WaveFormat.SampleRate;

            _bands = new EqualizerBand[BandFrequencies.Length];
            for (int i = 0; i < BandFrequencies.Length; i++)
            {
                _bands[i] = new EqualizerBand
                {
                    Frequency = BandFrequencies[i],
                    Gain = 0f,
                    Q = BandQ[i]
                };
            }

            _filters = new BiQuadFilter[_channels, _bands.Length];
            CreateFilters();
        }

        private void CreateFilters()
        {
            for (int ch = 0; ch < _channels; ch++)
            {
                for (int b = 0; b < _bands.Length; b++)
                {
                    _filters[ch, b] = CreateFilter(b, _bands[b].Gain);
                }
            }
        }

        private BiQuadFilter CreateFilter(int bandIndex, float gain)
        {
            float freq = _bands[bandIndex].Frequency;
            float q = _bands[bandIndex].Q;

            // Use shelving filters at the extremes for natural roll-off
            if (bandIndex == 0)
                return BiQuadFilter.LowShelf(_sampleRate, freq, 0.7f, gain);
            if (bandIndex == _bands.Length - 1)
                return BiQuadFilter.HighShelf(_sampleRate, freq, 0.7f, gain);

            return BiQuadFilter.PeakingEQ(_sampleRate, freq, q, gain);
        }

        public void UpdateBand(int bandIndex, float gainDb)
        {
            if (bandIndex < 0 || bandIndex >= _bands.Length) return;
            _bands[bandIndex].Gain = Math.Clamp(gainDb, -12f, 12f);

            for (int ch = 0; ch < _channels; ch++)
            {
                _filters[ch, bandIndex] = CreateFilter(bandIndex, _bands[bandIndex].Gain);
            }
        }

        public void Reset()
        {
            for (int i = 0; i < _bands.Length; i++)
            {
                _bands[i].Gain = 0f;
                for (int ch = 0; ch < _channels; ch++)
                    _filters[ch, i] = CreateFilter(i, 0f);
            }
        }

        public int Read(float[] buffer, int offset, int count)
        {
            int read = _source.Read(buffer, offset, count);

            if (!_enabled || read <= 0) return read;

            int samples = read;
            for (int n = 0; n < samples; n++)
            {
                int ch = n % _channels;
                float sample = buffer[offset + n];

                for (int b = 0; b < _bands.Length; b++)
                {
                    // Skip bands with zero gain to save CPU
                    if (_bands[b].Gain == 0f) continue;
                    sample = _filters[ch, b].Transform(sample);
                }

                // Soft clip using tanh for smooth, musical limiting
                buffer[offset + n] = MathF.Tanh(sample);
            }

            return read;
        }
    }

    public class EqualizerBand
    {
        public float Frequency { get; set; }
        public float Gain { get; set; }
        public float Q { get; set; }
    }
}
