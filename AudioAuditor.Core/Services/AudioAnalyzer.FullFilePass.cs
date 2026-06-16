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
            CancellationToken ct)
        {
            var (disposable, samples, format) = OpenAudioFile(filePath);
            if (disposable == null || samples == null || format == null) return;

            using (disposable)
            {
                int sampleRate = format.SampleRate;
                int channels = format.Channels;
                int blockSize = 4096;
                float[] buffer = new float[blockSize * channels];
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
                        float maxChannelAbs = 0;
                        for (int ch = 0; ch < channels; ch++)
                        {
                            float abs = Math.Abs(buffer[offset + ch]);
                            if (abs > maxChannelAbs) maxChannelAbs = abs;
                        }

                        var frame = new FullFileFrame(buffer, offset, maxChannelAbs);
                        for (int contributorIndex = 0; contributorIndex < contributors.Count; contributorIndex++)
                            contributors[contributorIndex].ProcessFrame(frame);
                    }
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
            if (settings.EnableRipQuality)
                contributors.Add(new RipQualityContributor(info, sampleRate, channels));
            return contributors;
        }

        private interface IFullFileAnalysisContributor
        {
            void ProcessFrame(FullFileFrame frame);
            void Complete(AudioFileInfo info);
        }

        private readonly struct FullFileFrame
        {
            private readonly float[] _buffer;
            private readonly int _offset;

            public FullFileFrame(float[] buffer, int offset, float maxChannelAbs)
            {
                _buffer = buffer;
                _offset = offset;
                MaxChannelAbs = maxChannelAbs;
            }

            public float MaxChannelAbs { get; }

            public float Sample(int channel) => _buffer[_offset + channel];
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

            public void ProcessFrame(FullFileFrame frame)
            {
                if (!_foundAudio)
                {
                    if (frame.MaxChannelAbs > SilenceThresholdLinear)
                        _foundAudio = true;
                    else
                        _leadingSamples++;
                }

                if (!_foundAudio)
                    return;

                if (frame.MaxChannelAbs <= SilenceThresholdLinear)
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

        private sealed class DynamicRangeContributor : IFullFileAnalysisContributor
        {
            private readonly int _blockFrames;
            private readonly List<double> _blockDrs = new();
            private double _sumSq;
            private double _peak;
            private int _frameCount;

            public DynamicRangeContributor(int sampleRate)
            {
                _blockFrames = sampleRate * 3;
            }

            public void ProcessFrame(FullFileFrame frame)
            {
                double max = frame.MaxChannelAbs;
                _sumSq += max * max;
                if (max > _peak) _peak = max;
                _frameCount++;

                if (_frameCount >= _blockFrames)
                {
                    if (_peak >= 1e-10)
                    {
                        double rms = Math.Sqrt(_sumSq / _frameCount);
                        if (rms >= 1e-10)
                            _blockDrs.Add(20.0 * Math.Log10(_peak / rms));
                    }
                    _sumSq = 0;
                    _peak = 0;
                    _frameCount = 0;
                }
            }

            public void Complete(AudioFileInfo info)
            {
                if (_blockDrs.Count < 2)
                    return;

                _blockDrs.Sort();
                int topCount = Math.Max(2, _blockDrs.Count / 5);
                double avgDr = 0;
                for (int idx = _blockDrs.Count - topCount; idx < _blockDrs.Count; idx++)
                    avgDr += _blockDrs[idx];
                avgDr /= topCount;
                info.DynamicRange = Math.Round(avgDr, 1);
                info.HasDynamicRange = true;
            }
        }

        private sealed class TruePeakContributor : IFullFileAnalysisContributor
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
                    _history[ch] = new double[_filterLength];
            }

            public void ProcessFrame(FullFileFrame frame)
            {
                for (int ch = 0; ch < _channels; ch++)
                {
                    double sample = frame.Sample(ch);
                    _history[ch][_historyPosition] = sample;
                    double abs = Math.Abs(sample);
                    if (abs > _maxTruePeak) _maxTruePeak = abs;

                    for (int p = 1; p < 4; p++)
                    {
                        double interp = 0;
                        for (int k = 0; k < _filterLength; k++)
                        {
                            int idx = (_historyPosition - k + _filterLength * 2) % _filterLength;
                            interp += _history[ch][idx] * _phases[p][k];
                        }
                        abs = Math.Abs(interp);
                        if (abs > _maxTruePeak) _maxTruePeak = abs;
                    }
                }
                _historyPosition = (_historyPosition + 1) % _filterLength;
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
                    _channelWeight[ch] = (channels > 2 && (ch == 3 || ch == 4)) ? 1.41 : 1.0;
            }

            public void ProcessFrame(FullFileFrame frame)
            {
                double weightedSum = 0;
                for (int ch = 0; ch < _channels; ch++)
                {
                    double sample = frame.Sample(ch);
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

        private sealed class RipQualityContributor : IFullFileAnalysisContributor
        {
            private const float NoiseThreshold = 0.01f;
            private const float ClickThreshold = 0.90f;

            private readonly int _sampleRate;
            private readonly int _channels;
            private readonly int _zeroGapFrames;
            private readonly bool _checkBitTruncation;
            private readonly float[] _lastSample;
            private readonly int[] _consecutiveIdentical;
            private readonly float[] _previousSample;
            private long _totalFrames;
            private long _zeroRuns;
            private int _currentZeroRun;
            private long _truncatedSamples;
            private long _stickyRuns;
            private long _popClicks;
            private bool _first = true;
            private double _dcSum;
            private long _dcCount;
            private double _noiseSumSq;
            private long _noiseCount;

            public RipQualityContributor(AudioFileInfo info, int sampleRate, int channels)
            {
                _sampleRate = sampleRate;
                _channels = channels;
                _zeroGapFrames = sampleRate;
                _checkBitTruncation = info.BitsPerSample == 16 && IsLosslessFile(info);
                _lastSample = new float[channels];
                _consecutiveIdentical = new int[channels];
                _previousSample = new float[channels];
            }

            public void ProcessFrame(FullFileFrame frame)
            {
                _totalFrames++;
                bool allZero = true;
                for (int ch = 0; ch < _channels; ch++)
                {
                    float sample = frame.Sample(ch);
                    if (Math.Abs(sample) >= 1e-7f) allZero = false;

                    _dcSum += sample;
                    _dcCount++;

                    float abs = Math.Abs(sample);
                    if (abs < NoiseThreshold)
                    {
                        _noiseSumSq += sample * sample;
                        _noiseCount++;
                    }

                    if (sample == _lastSample[ch] && abs > 0.05f)
                    {
                        _consecutiveIdentical[ch]++;
                        if (_consecutiveIdentical[ch] == 250) _stickyRuns++;
                    }
                    else
                    {
                        _consecutiveIdentical[ch] = 0;
                    }
                    _lastSample[ch] = sample;

                    if (!_first && abs > 0.02f)
                    {
                        float diff = Math.Abs(sample - _previousSample[ch]);
                        if (diff > ClickThreshold && diff > abs * 2.0f)
                            _popClicks++;
                    }
                    _previousSample[ch] = sample;
                }
                _first = false;

                if (allZero)
                {
                    _currentZeroRun++;
                }
                else
                {
                    if (_currentZeroRun >= _zeroGapFrames) _zeroRuns++;
                    _currentZeroRun = 0;
                }

                if (_checkBitTruncation)
                {
                    float firstSample = frame.Sample(0);
                    int intVal = (int)(firstSample * 32768f);
                    if ((intVal & 0xFF) == 0 && Math.Abs(intVal) > 256)
                        _truncatedSamples++;
                }
            }

            public void Complete(AudioFileInfo info)
            {
                if (_currentZeroRun >= _zeroGapFrames) _zeroRuns++;
                float dcOffset = _dcCount > 0 ? (float)(_dcSum / _dcCount) : 0;
                float noiseRms = _noiseCount > 0 ? (float)Math.Sqrt(_noiseSumSq / _noiseCount) : 0;
                FinalizeRipQuality(info, _sampleRate, _channels, _totalFrames, _zeroRuns, _stickyRuns,
                    _popClicks, _truncatedSamples, dcOffset, noiseRms);
            }
        }
    }
}
