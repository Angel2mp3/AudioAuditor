using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using AudioQualityChecker.Abstractions;
using AudioQualityChecker.Models;

namespace AudioQualityChecker.Services
{
    public static partial class AudioAnalyzer
    {
        private const int FullFilePassMaxSeconds = 180;

        private static void RunFullFilePass(
            string filePath,
            AudioFileInfo info,
            IAnalysisSettings settings,
            CancellationToken ct,
            SharedAudioSource? shared = null)
        {
            var lease = AudioLease.Open(filePath, shared);
            var samples = lease.Samples;
            var format = lease.Format;
            if (samples == null || format == null) { lease.Dispose(); return; }

            using (lease)
            {
                int sampleRate = format.SampleRate;
                int channels = format.Channels;
                int blockSize = 4096;
                float[] buffer = new float[blockSize * channels];
                // Per-frame channel maximum, computed once per block and handed to every
                // contributor. Silence and DR both need it; recomputing it inside each of them
                // would walk the block twice more.
                float[] maxAbs = new float[blockSize];
                var contributors = CreateFullFileContributors(info, settings, sampleRate, channels);

                int read;
                int frameCounter = 0;
                long totalFramesRead = 0;
                long maxFramesToRead = (long)sampleRate * FullFilePassMaxSeconds;
                while ((read = samples.Read(buffer, 0, buffer.Length)) > 0)
                {
                    frameCounter += read / channels;
                    totalFramesRead += read / channels;
                    if (frameCounter >= sampleRate)
                    {
                        frameCounter = 0;
                        WaitIfPaused(ct);
                    }
                    if (totalFramesRead >= maxFramesToRead)
                        break;

                    int frames = read / channels;
                    for (int i = 0; i < frames; i++)
                    {
                        int offset = i * channels;
                        float frameMax = 0;
                        for (int ch = 0; ch < channels; ch++)
                        {
                            float abs = Math.Abs(buffer[offset + ch]);
                            if (abs > frameMax) frameMax = abs;
                        }
                        maxAbs[i] = frameMax;
                    }

                    // One interface dispatch per block rather than one per frame per contributor —
                    // at 4096 frames a block that is four orders of magnitude fewer virtual calls
                    // over a full-length track. Each contributor walks the block in frame order, so
                    // the arithmetic and its ordering are unchanged.
                    for (int contributorIndex = 0; contributorIndex < contributors.Count; contributorIndex++)
                        contributors[contributorIndex].ProcessBlock(buffer, maxAbs, frames, channels);
                }

                for (int i = 0; i < contributors.Count; i++)
                    contributors[i].Complete(info);

                Thread.Yield();
            }
        }

        private static List<IFullFileAnalysisContributor> CreateFullFileContributors(
            AudioFileInfo info,
            IAnalysisSettings settings,
            int sampleRate,
            int channels)
        {
            var contributors = new List<IFullFileAnalysisContributor>(5);
            if (settings.EnableSilenceDetection)
                contributors.Add(new SilenceContributor(settings, sampleRate));
            if (settings.EnableDynamicRange)
                contributors.Add(new DynamicRangeContributor(sampleRate));
            if (settings.EnableTruePeak)
                contributors.Add(new TruePeakContributor(channels));
            if (settings.EnableLufs)
                contributors.Add(new LufsContributor(sampleRate, channels));
            return contributors;
        }

        internal interface IFullFileAnalysisContributor
        {
            /// <param name="buffer">Interleaved samples for the block.</param>
            /// <param name="maxAbs">Per-frame maximum absolute sample across channels.</param>
            /// <param name="frames">Number of valid frames in the block.</param>
            /// <param name="channels">Channel count (the interleave stride).</param>
            void ProcessBlock(float[] buffer, float[] maxAbs, int frames, int channels);
            void Complete(AudioFileInfo info);
        }

        private sealed class SilenceContributor : IFullFileAnalysisContributor
        {
            private readonly IAnalysisSettings _settings;
            private readonly int _sampleRate;
            private readonly double _minMidGapMs;
            private readonly long _edgeFrames;
            private long _leadingSamples;
            private bool _foundAudio;
            private long _currentPosition;
            private long _runStart = -1;
            private int _midGaps;
            private double _totalMidSilenceMs;
            private long _lastSilenceRunLength;

            public SilenceContributor(IAnalysisSettings settings, int sampleRate)
            {
                _settings = settings;
                _sampleRate = sampleRate;
                _minMidGapMs = settings.Silence.MinGapEnabled
                    ? settings.Silence.MinGapSeconds * 1000.0
                    : 500.0;
                _edgeFrames = settings.Silence.SkipEdgesEnabled
                    ? (long)(settings.Silence.SkipEdgeSeconds * sampleRate)
                    : 0;
            }

            public void ProcessBlock(float[] buffer, float[] maxAbs, int frames, int channels)
            {
                for (int i = 0; i < frames; i++)
                {
                    float frameMax = maxAbs[i];

                    if (!_foundAudio)
                    {
                        if (frameMax > SilenceThresholdLinear)
                            _foundAudio = true;
                        else
                            _leadingSamples++;
                    }

                    if (!_foundAudio)
                        continue;

                    if (frameMax <= SilenceThresholdLinear)
                    {
                        if (_runStart < 0) _runStart = _currentPosition;
                    }
                    else
                    {
                        if (_runStart >= 0)
                        {
                            long runFrames = _currentPosition - _runStart;
                            double runMs = (double)runFrames / _sampleRate * 1000.0;
                            if (runMs >= _minMidGapMs)
                            {
                                bool inEdge = _edgeFrames > 0 && (_leadingSamples + _runStart) < _edgeFrames;
                                if (!inEdge)
                                {
                                    _midGaps++;
                                    _totalMidSilenceMs += runMs;
                                }
                            }
                            _runStart = -1;
                        }
                    }

                    _currentPosition++;
                }
            }

            public void Complete(AudioFileInfo info)
            {
                info.LeadingSilenceMs = Math.Round((double)_leadingSamples / _sampleRate * 1000.0, 0);
                if (_runStart >= 0)
                    _lastSilenceRunLength = _currentPosition - _runStart;
                info.TrailingSilenceMs = Math.Round((double)_lastSilenceRunLength / _sampleRate * 1000.0, 0);
                info.MidTrackSilenceGaps = _midGaps;
                info.TotalMidSilenceMs = Math.Round(_totalMidSilenceMs, 0);
                bool leadingExcessive = !_settings.Silence.SkipEdgesEnabled && info.LeadingSilenceMs > 5000;
                bool trailingExcessive = !_settings.Silence.SkipEdgesEnabled && info.TrailingSilenceMs > 10000;
                info.HasExcessiveSilence = leadingExcessive || trailingExcessive || _midGaps > 0;
            }
        }

        /// <summary>
        /// Dynamic range, following the TT DR / foobar "DR meter" method closely enough to be
        /// comparable with it.
        ///
        /// The block selection is the part that matters and the part this used to get wrong: it
        /// sorted the per-block DR *values* and averaged the highest 20%. That picks the quietest,
        /// most dynamic passages of a track — an intro or a fade has a huge peak-to-RMS ratio — and
        /// reported DR several points above what every other meter said. The reference algorithm
        /// selects the loudest 20% of blocks, by RMS, and computes DR from those.
        /// </summary>
        private sealed class DynamicRangeContributor : IFullFileAnalysisContributor
        {
            private readonly int _blockFrames;
            private readonly List<(double Rms, double Peak)> _blocks = new();
            private double _sumSq;
            private double _peak;
            private int _frameCount;

            public DynamicRangeContributor(int sampleRate)
            {
                _blockFrames = sampleRate * 3;
            }

            public void ProcessBlock(float[] buffer, float[] maxAbs, int frames, int channels)
            {
                for (int i = 0; i < frames; i++)
                {
                    double max = maxAbs[i];
                    _sumSq += max * max;
                    if (max > _peak) _peak = max;
                    _frameCount++;

                    if (_frameCount >= _blockFrames)
                    {
                        if (_peak >= 1e-10)
                        {
                            double rms = Math.Sqrt(_sumSq / _frameCount);
                            if (rms >= 1e-10)
                                _blocks.Add((rms, _peak));
                        }
                        _sumSq = 0;
                        _peak = 0;
                        _frameCount = 0;
                    }
                }
            }

            public void Complete(AudioFileInfo info)
            {
                if (_blocks.Count < 2)
                    return;

                // Loudest 20% of blocks by RMS — not the 20% with the widest peak-to-RMS spread.
                _blocks.Sort(static (a, b) => a.Rms.CompareTo(b.Rms));
                int topCount = Math.Max(2, _blocks.Count / 5);
                int firstIdx = _blocks.Count - topCount;

                // RMS of those blocks' RMS values (power average, not an average of dB), and the
                // second-highest peak among them — the reference uses the 2nd peak so one stray
                // sample cannot set the whole figure.
                double sumSq = 0;
                double highestPeak = 0, secondPeak = 0;
                for (int idx = firstIdx; idx < _blocks.Count; idx++)
                {
                    var (rms, peak) = _blocks[idx];
                    sumSq += rms * rms;
                    if (peak > highestPeak) { secondPeak = highestPeak; highestPeak = peak; }
                    else if (peak > secondPeak) { secondPeak = peak; }
                }

                double loudRms = Math.Sqrt(sumSq / topCount);
                double refPeak = secondPeak > 1e-10 ? secondPeak : highestPeak;
                if (loudRms < 1e-10 || refPeak < 1e-10)
                    return;

                info.DynamicRange = Math.Round(20.0 * Math.Log10(refPeak / loudRms), 1);
                info.HasDynamicRange = true;
            }
        }

        internal sealed class TruePeakContributor : IFullFileAnalysisContributor
        {
            private readonly int _channels;
            private readonly double[][] _phases;
            private readonly int _filterLength;
            private readonly double[][] _history;
            private int _historyPosition;
            private double _maxTruePeak;

            public TruePeakContributor(int channels)
            {
                _channels = channels;
                _phases = GetOversamplingPhases();
                _filterLength = _phases[0].Length;
                _history = new double[channels][];
                for (int ch = 0; ch < channels; ch++)
                    // Twice the filter length: each sample is written to slot `pos` and to
                    // `pos + filterLength`, so buffer[m] always holds ring slot m % filterLength.
                    // That lets the tap loop read a contiguous descending window without the
                    // per-tap integer modulo the ring index used to need — one integer division
                    // per tap, per phase, per channel, per frame was the single hottest
                    // instruction in the whole full-file pass.
                    _history[ch] = new double[_filterLength * 2];
            }

            public void ProcessBlock(float[] buffer, float[] maxAbs, int frames, int channels)
            {
                int len = _filterLength;
                int pos = _historyPosition;

                for (int i = 0; i < frames; i++)
                {
                    int offset = i * channels;
                    for (int ch = 0; ch < _channels; ch++)
                    {
                        double[] h = _history[ch];
                        double sample = buffer[offset + ch];
                        h[pos] = sample;
                        h[pos + len] = sample;
                        double abs = Math.Abs(sample);
                        if (abs > _maxTruePeak) _maxTruePeak = abs;

                        for (int p = 1; p < 4; p++)
                        {
                            double[] ph = _phases[p];
                            double interp = 0;
                            // h[pos + len - k] is ring slot (pos - k) mod len — the same element,
                            // visited in the same ascending-k order, so the accumulation is
                            // bit-for-bit what the modulo version produced.
                            for (int k = 0; k < len; k++)
                                interp += h[pos + len - k] * ph[k];
                            abs = Math.Abs(interp);
                            if (abs > _maxTruePeak) _maxTruePeak = abs;
                        }
                    }

                    if (++pos == len) pos = 0;
                }

                _historyPosition = pos;
            }

            public void Complete(AudioFileInfo info)
            {
                if (_maxTruePeak <= 1e-10)
                    return;

                info.TruePeakDbTP = 20.0 * Math.Log10(_maxTruePeak);
                info.HasTruePeak = true;
            }
        }

        private sealed class LufsContributor : IFullFileAnalysisContributor
        {
            private readonly int _channels;
            private readonly BiquadState[] _preFilters;
            private readonly BiquadState[] _rlbFilters;
            private readonly BiquadCoefficients _preCoefficients;
            private readonly BiquadCoefficients _rlbCoefficients;
            private readonly int _blockSamples;
            private readonly int _stepSamples;
            private readonly double[] _gateBuffer;
            private readonly List<double> _blockLoudness = new();
            private readonly double[] _channelWeight;
            private int _gatePosition;
            private int _gateCount;
            private int _stepCounter;

            public LufsContributor(int sampleRate, int channels)
            {
                _channels = channels;
                _preFilters = new BiquadState[channels];
                _rlbFilters = new BiquadState[channels];
                for (int ch = 0; ch < channels; ch++)
                {
                    _preFilters[ch] = new BiquadState();
                    _rlbFilters[ch] = new BiquadState();
                }

                GetKWeightingCoefficients(sampleRate, out _preCoefficients, out _rlbCoefficients);
                _blockSamples = (int)(sampleRate * 0.4);
                _stepSamples = (int)(sampleRate * 0.1);
                _gateBuffer = new double[_blockSamples];
                _channelWeight = new double[channels];
                for (int ch = 0; ch < channels; ch++)
                    _channelWeight[ch] = ChannelWeight(ch, channels);
            }

            /// <summary>
            /// ITU-R BS.1770-4 channel weight for an interleaved multichannel stream in WAVE order
            /// (FL, FR, FC, LFE, BL, BR): 1.0 for left/right/centre, 1.41 for the two surrounds,
            /// and LFE excluded outright.
            ///
            /// The previous rule was `ch == 3 || ch == 4 ? 1.41 : 1.0`, which weighted LFE (ch 3) at
            /// 1.41 and gave the right surround (ch 5) only 1.0 — so every 5.1 file's Integrated
            /// LUFS was inflated by its LFE content and under-weighted on the right surround.
            /// </summary>
            private static double ChannelWeight(int ch, int channels)
            {
                if (channels <= 2) return 1.0;       // mono / stereo: unweighted
                if (ch == 3) return 0.0;             // LFE is excluded from the loudness sum
                if (ch == 4 || ch == 5) return 1.41; // BL / BR surrounds
                return 1.0;                          // FL, FR, FC (and anything beyond 5.1)
            }

            public void ProcessBlock(float[] buffer, float[] maxAbs, int frames, int channels)
            {
                for (int i = 0; i < frames; i++)
                {
                    int offset = i * channels;
                    double weightedSum = 0;
                    for (int ch = 0; ch < _channels; ch++)
                    {
                        double sample = buffer[offset + ch];
                        sample = ApplyBiquad(ref _preFilters[ch], _preCoefficients, sample);
                        sample = ApplyBiquad(ref _rlbFilters[ch], _rlbCoefficients, sample);
                        weightedSum += _channelWeight[ch] * sample * sample;
                    }

                    _gateBuffer[_gatePosition] = weightedSum;
                    _gatePosition = (_gatePosition + 1) % _blockSamples;
                    _gateCount = Math.Min(_gateCount + 1, _blockSamples);
                    _stepCounter++;
                    if (_stepCounter >= _stepSamples && _gateCount >= _blockSamples)
                    {
                        _stepCounter = 0;
                        double sum = 0;
                        for (int k = 0; k < _blockSamples; k++)
                            sum += _gateBuffer[k];
                        double meanPower = sum / _blockSamples;
                        if (meanPower > 1e-20)
                            _blockLoudness.Add(-0.691 + 10.0 * Math.Log10(meanPower));
                    }
                }
            }

            public void Complete(AudioFileInfo info)
            {
                if (_blockLoudness.Count == 0)
                    return;

                var aboveAbsolute = _blockLoudness.Where(l => l > -70).ToList();
                if (aboveAbsolute.Count == 0)
                    return;

                double absLoudness = -0.691 + 10.0 * Math.Log10(
                    aboveAbsolute.Average(l => Math.Pow(10, (l + 0.691) / 10.0)));
                double relThreshold = absLoudness - 10.0;
                var aboveRelative = aboveAbsolute.Where(l => l > relThreshold).ToList();
                if (aboveRelative.Count == 0)
                    return;

                double integratedLoudness = -0.691 + 10.0 * Math.Log10(
                    aboveRelative.Average(l => Math.Pow(10, (l + 0.691) / 10.0)));
                info.IntegratedLufs = Math.Round(integratedLoudness, 1);
                info.HasLufs = true;
            }
        }

    }
}
