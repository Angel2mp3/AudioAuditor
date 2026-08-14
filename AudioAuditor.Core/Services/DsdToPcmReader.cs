using System;
using System.IO;
using System.Numerics;
using NAudio.Wave;

namespace AudioQualityChecker.Services
{
    /// <summary>
    /// Streams a DSD file (.dsf / .dff) as 16-bit PCM.
    ///
    /// DSD is a 1-bit stream running at 2.8 MHz or higher, so getting PCM out of it means
    /// de-interleaving the channels, lowpass filtering, and decimating. The previous version of
    /// this reader got all three wrong:
    ///
    ///   * It read the DSF <c>fmt</c> chunk at the wrong offsets — <c>samplingFrequency</c> lives
    ///     12 bytes into the chunk payload, but was read from where <c>formatID</c> sits, which is
    ///     always 0 for DSD. The sample rate therefore came out as 0.
    ///   * It assumed both containers store one channel's data contiguously after the other's.
    ///     Neither does: DSF interleaves fixed-size blocks per channel, DFF interleaves single
    ///     bytes. Stereo decoded to noise.
    ///   * It decimated with a bare 16-sample boxcar and no lowpass. DSD's noise shaping parks
    ///     enormous quantization noise above the audio band, and a boxcar's sidelobes fold it
    ///     straight back down, so every measurement taken afterwards was reading that noise.
    ///
    /// It also decoded the whole file into memory three times over (raw bytes, a float array, then
    /// a PCM byte array), which on a DSD128 album track meant gigabytes. This version decodes one
    /// block at a time.
    ///
    /// The conversion is two-stage. Stage one sums each byte's 8 bits with a popcount, which is
    /// both a cheap 8:1 decimation and — because a sum does not care what order its terms are in —
    /// the reason bit order (LSB-first in DSF, MSB-first in DFF) can be ignored entirely. Stage two
    /// runs a windowed-sinc lowpass and decimates the rest of the way.
    /// </summary>
    public class DsdToPcmReader : WaveStream
    {
        // 1-bit samples folded into one stage-one value. Fixed at 8 so a byte maps to exactly one
        // value and the popcount trick applies.
        private const int Stage1Decimation = 8;

        // DSD carries shaped noise far above the audio band. Landing near 88.2/96 kHz keeps every
        // musical frequency (nothing above ~40 kHz is music) while the anti-alias filter rejects
        // the bulk of that noise, instead of decimating to 176.4 kHz and admitting all of it.
        private const int TargetPcmRateCeiling = 96000;

        private readonly FileStream _file;
        private readonly WaveFormat _waveFormat;
        private readonly object _lock = new();

        private readonly int _channels;
        private readonly bool _isDsf;
        private readonly long _dataStart;
        private readonly long _dataBytes;      // DSD payload across all channels
        private readonly int _blockBytes;      // per channel, per interleave group
        private readonly int _stage2Decimation;
        private readonly double[] _fir;
        private readonly long _totalFrames;

        // Per-channel stage-two filter history, carried across blocks so filtering is continuous.
        private readonly double[][] _history;
        private int _historyPos;

        private readonly byte[] _blockBuf;     // one interleave group: _blockBytes * _channels
        private readonly byte[] _pcmBuf;       // that group decoded to PCM bytes
        private int _pcmValid;
        private int _pcmOffset;

        private long _position;                // in PCM bytes
        private long _dataPos;                 // read cursor within the DSD payload

        public DsdToPcmReader(string filePath)
        {
            _file = File.OpenRead(filePath);
            try
            {
                string ext = Path.GetExtension(filePath).ToLowerInvariant();
                _isDsf = ext == ".dsf";

                int dsdRate;
                if (_isDsf) ParseDsf(out dsdRate, out _channels, out _dataStart, out _dataBytes, out _blockBytes);
                else        ParseDff(out dsdRate, out _channels, out _dataStart, out _dataBytes, out _blockBytes);

                if (dsdRate <= 0 || _channels <= 0 || _dataBytes <= 0)
                    throw new InvalidDataException("Unreadable DSD header");

                // Total decimation is chosen from the file's actual rate rather than fixed at 16,
                // so DSD128/256/512 land on a sane PCM rate instead of being labelled 176400 while
                // actually carrying twice that — which made every frequency reading wrong by 2x.
                int decimation = Stage1Decimation;
                while (dsdRate / decimation > TargetPcmRateCeiling) decimation *= 2;
                _stage2Decimation = decimation / Stage1Decimation;

                int pcmRate = dsdRate / decimation;
                _fir = BuildLowpass(_stage2Decimation);

                _history = new double[_channels][];
                for (int ch = 0; ch < _channels; ch++) _history[ch] = new double[_fir.Length];

                // Keep the interleave group a whole number of output frames.
                if (_blockBytes % _stage2Decimation != 0)
                    _blockBytes = Math.Max(_stage2Decimation, _blockBytes / _stage2Decimation * _stage2Decimation);

                _waveFormat = new WaveFormat(pcmRate, 16, _channels);

                long bytesPerChannel = _dataBytes / _channels;
                _totalFrames = bytesPerChannel / _stage2Decimation;

                _blockBuf = new byte[_blockBytes * _channels];
                _pcmBuf = new byte[_blockBytes / _stage2Decimation * _channels * 2];

                _dataPos = 0;
                _file.Position = _dataStart;
            }
            catch
            {
                _file.Dispose();
                throw;
            }
        }

        // ── Header parsing ──────────────────────────────────────────────────────────────

        /// <summary>
        /// DSF (Sony). After the 28-byte "DSD " chunk comes "fmt ": 4-byte id, 8-byte chunk size,
        /// then the payload — formatVersion, formatID, channelType, channelNum, samplingFrequency,
        /// bitsPerSample, sampleCount (8), blockSizePerChannel. Offsets below are from the start of
        /// the fmt chunk, which is why each is 12 higher than the raw payload index.
        /// </summary>
        private void ParseDsf(out int rate, out int channels, out long dataStart, out long dataBytes, out int blockBytes)
        {
            byte[] head = new byte[80];
            _file.Position = 0;
            if (_file.Read(head, 0, head.Length) < head.Length)
                throw new InvalidDataException("DSF file too short");

            if (head[0] != 'D' || head[1] != 'S' || head[2] != 'D' || head[3] != ' ')
                throw new InvalidDataException("Not a DSF file");

            const int fmt = 28;
            if (head[fmt] != 'f' || head[fmt + 1] != 'm' || head[fmt + 2] != 't' || head[fmt + 3] != ' ')
                throw new InvalidDataException("DSF fmt chunk missing");

            long fmtSize = BitConverter.ToInt64(head, fmt + 4);
            channels = BitConverter.ToInt32(head, fmt + 24);      // channelNum
            rate = BitConverter.ToInt32(head, fmt + 28);          // samplingFrequency
            blockBytes = BitConverter.ToInt32(head, fmt + 44);    // blockSizePerChannel

            if (blockBytes <= 0 || blockBytes > 1 << 20) blockBytes = 4096;

            // Same clamp ParseDff applies. Without it a header-declared channel count feeds
            // "blockBytes * channels" below, which is an unchecked int multiply — a large value
            // wraps to a small positive one and yields a buffer smaller than the read loop expects.
            if (channels <= 0 || channels > 8) channels = 2;

            // The "data" chunk follows fmt: 4-byte id + 8-byte size, then the samples.
            long dataChunk = fmt + (fmtSize > 0 ? fmtSize : 52);
            byte[] dh = new byte[12];
            _file.Position = dataChunk;
            if (_file.Read(dh, 0, 12) < 12)
                throw new InvalidDataException("DSF data chunk missing");

            long dataSize = BitConverter.ToInt64(dh, 4);
            dataStart = dataChunk + 12;
            dataBytes = dataSize > 12 ? dataSize - 12 : _file.Length - dataStart;
            if (dataBytes > _file.Length - dataStart) dataBytes = _file.Length - dataStart;
        }

        /// <summary>
        /// DSDIFF (Philips). An IFF tree: "FRM8", 8-byte big-endian size, form type "DSD ", then
        /// chunks. Sample rate is PROP/SND /FS  , channel count is PROP/SND /CHNL, samples are in
        /// the "DSD " chunk, interleaved one byte per channel at a time.
        /// </summary>
        private void ParseDff(out int rate, out int channels, out long dataStart, out long dataBytes, out int blockBytes)
        {
            rate = 2822400;
            channels = 2;
            dataStart = 0;
            dataBytes = 0;
            blockBytes = 4096;

            byte[] hdr = new byte[12];
            _file.Position = 0;
            if (_file.Read(hdr, 0, 12) < 12) throw new InvalidDataException("DFF file too short");
            if (hdr[0] != 'F' || hdr[1] != 'R' || hdr[2] != 'M' || hdr[3] != '8')
                throw new InvalidDataException("Not a DSDIFF file");

            long pos = 16; // FRM8 id(4) + size(8) + form type(4)
            long end = _file.Length;

            while (pos + 12 <= end)
            {
                _file.Position = pos;
                byte[] ch = new byte[12];
                if (_file.Read(ch, 0, 12) < 12) break;

                string id = System.Text.Encoding.ASCII.GetString(ch, 0, 4);
                long size = ReadInt64Be(ch, 4);
                if (size < 0) break;
                long payload = pos + 12;

                if (id == "PROP")
                {
                    // Walk the sub-chunks after the 4-byte property type.
                    long sub = payload + 4, subEnd = payload + size;
                    while (sub + 12 <= subEnd && sub + 12 <= end)
                    {
                        _file.Position = sub;
                        byte[] sc = new byte[12];
                        if (_file.Read(sc, 0, 12) < 12) break;
                        string sid = System.Text.Encoding.ASCII.GetString(sc, 0, 4);
                        long ssize = ReadInt64Be(sc, 4);
                        if (ssize < 0) break;

                        if (sid == "FS  ")
                        {
                            byte[] v = new byte[4];
                            if (_file.Read(v, 0, 4) == 4)
                                rate = (v[0] << 24) | (v[1] << 16) | (v[2] << 8) | v[3];
                        }
                        else if (sid == "CHNL")
                        {
                            byte[] v = new byte[2];
                            if (_file.Read(v, 0, 2) == 2)
                                channels = (v[0] << 8) | v[1];
                        }

                        sub += 12 + ssize + (ssize & 1); // IFF chunks pad to even length
                    }
                }
                else if (id == "DSD ")
                {
                    dataStart = payload;
                    dataBytes = Math.Min(size, end - payload);
                    break;
                }

                pos += 12 + size + (size & 1);
            }

            if (channels <= 0 || channels > 8) channels = 2;
            if (rate <= 0) rate = 2822400;
            if (dataBytes <= 0) throw new InvalidDataException("DFF sound data chunk missing");

            // DFF is byte-interleaved, so a "block" is a read convenience rather than a format
            // structure. Keep it a multiple of the channel count.
            blockBytes = 4096;
        }

        private static long ReadInt64Be(byte[] b, int off)
        {
            long v = 0;
            for (int i = 0; i < 8; i++) v = (v << 8) | b[off + i];
            return v;
        }

        // ── Filter ──────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Windowed-sinc lowpass for the stage-two decimator, cut at the output Nyquist so the
        /// ultrasonic noise DSD is full of cannot alias down into the audio band.
        /// </summary>
        private static double[] BuildLowpass(int decimation)
        {
            if (decimation <= 1) return new double[] { 1.0 };

            int taps = Math.Min(257, 16 * decimation + 1);
            if ((taps & 1) == 0) taps++;

            double fc = 0.5 / decimation;   // cycles/sample at the stage-one rate
            var h = new double[taps];
            int mid = taps / 2;
            double sum = 0;

            for (int i = 0; i < taps; i++)
            {
                int n = i - mid;
                double sinc = n == 0 ? 2.0 * fc : Math.Sin(2.0 * Math.PI * fc * n) / (Math.PI * n);
                // Blackman window — cheap and its stopband is deep enough for this job.
                double w = 0.42 - 0.5 * Math.Cos(2.0 * Math.PI * i / (taps - 1))
                                + 0.08 * Math.Cos(4.0 * Math.PI * i / (taps - 1));
                h[i] = sinc * w;
                sum += h[i];
            }

            if (Math.Abs(sum) > 1e-12)
                for (int i = 0; i < taps; i++) h[i] /= sum;

            return h;
        }

        // ── Decoding ────────────────────────────────────────────────────────────────────

        /// <summary>Decodes the next interleave group into <see cref="_pcmBuf"/>. Returns false at EOF.</summary>
        private bool FillNextBlock()
        {
            if (_dataPos >= _dataBytes) return false;

            int want = (int)Math.Min(_blockBuf.Length, _dataBytes - _dataPos);
            want -= want % _channels;
            if (want <= 0) return false;

            _file.Position = _dataStart + _dataPos;
            int got = 0;
            while (got < want)
            {
                int n = _file.Read(_blockBuf, got, want - got);
                if (n <= 0) break;
                got += n;
            }
            got -= got % _channels;
            if (got <= 0) return false;
            _dataPos += got;

            int bytesPerChannel = got / _channels;
            int frames = bytesPerChannel / _stage2Decimation;
            if (frames <= 0) return false;

            int taps = _fir.Length;
            _pcmValid = 0;

            for (int ch = 0; ch < _channels; ch++)
            {
                var hist = _history[ch];
                int hp = _historyPos;

                for (int f = 0; f < frames; f++)
                {
                    double acc = 0;
                    for (int s = 0; s < _stage2Decimation; s++)
                    {
                        int idx = f * _stage2Decimation + s;

                        // DSF interleaves whole blocks per channel; DFF interleaves single bytes.
                        int byteIdx = _isDsf
                            ? ch * bytesPerChannel + idx
                            : idx * _channels + ch;

                        // Stage 1: eight 1-bit samples in one byte. A set bit is +1, a clear bit
                        // -1, so the sum is popcount*2 - 8. Order-independent, which is why the
                        // container's bit order never has to be consulted.
                        double s1 = (BitOperations.PopCount(_blockBuf[byteIdx]) * 2 - 8) / 8.0;

                        hist[hp] = s1;
                        hp = (hp + 1) % taps;

                        // Stage 2: convolve on the sample that completes each output frame.
                        if (s == _stage2Decimation - 1)
                        {
                            for (int k = 0; k < taps; k++)
                            {
                                int hi = (hp - 1 - k + taps * 2) % taps;
                                acc += hist[hi] * _fir[k];
                            }
                        }
                    }

                    short sample = (short)Math.Clamp(acc * 32767.0, short.MinValue, short.MaxValue);
                    int outIdx = (f * _channels + ch) * 2;
                    _pcmBuf[outIdx] = (byte)(sample & 0xFF);
                    _pcmBuf[outIdx + 1] = (byte)((sample >> 8) & 0xFF);
                }

                // Every channel advances its history identically; commit once.
                if (ch == _channels - 1) _historyPos = hp;
            }

            _pcmValid = frames * _channels * 2;
            _pcmOffset = 0;
            return true;
        }

        // ── WaveStream ──────────────────────────────────────────────────────────────────

        public override WaveFormat WaveFormat => _waveFormat;

        public override long Length => _totalFrames * _waveFormat.BlockAlign;

        public override long Position
        {
            get { lock (_lock) return _position; }
            set
            {
                lock (_lock)
                {
                    long clamped = Math.Clamp(value, 0, Length);
                    int blockAlign = _waveFormat.BlockAlign;
                    clamped = clamped / blockAlign * blockAlign;

                    _position = clamped;

                    long frame = clamped / blockAlign;
                    long bytesPerChannel = frame * _stage2Decimation;

                    // Round back to an interleave-group boundary so channel de-interleaving stays
                    // aligned, then drop the difference on the next read.
                    long group = bytesPerChannel / _blockBytes;
                    _dataPos = group * _blockBytes * _channels;

                    _pcmValid = 0;
                    _pcmOffset = 0;
                    _historyPos = 0;
                    foreach (var h in _history) Array.Clear(h, 0, h.Length);

                    // Skip forward within the group to the exact frame.
                    long skipFrames = frame - group * (_blockBytes / _stage2Decimation);
                    _pendingSkipBytes = Math.Max(0, skipFrames) * blockAlign;
                }
            }
        }

        private long _pendingSkipBytes;

        public override int Read(byte[] buffer, int offset, int count)
        {
            lock (_lock)
            {
                int written = 0;
                while (written < count)
                {
                    if (_pcmOffset >= _pcmValid)
                    {
                        if (!FillNextBlock()) break;

                        if (_pendingSkipBytes > 0)
                        {
                            int skip = (int)Math.Min(_pendingSkipBytes, _pcmValid);
                            _pcmOffset += skip;
                            _pendingSkipBytes -= skip;
                            if (_pcmOffset >= _pcmValid) continue;
                        }
                    }

                    int available = _pcmValid - _pcmOffset;
                    int toCopy = Math.Min(count - written, available);
                    Buffer.BlockCopy(_pcmBuf, _pcmOffset, buffer, offset + written, toCopy);
                    _pcmOffset += toCopy;
                    written += toCopy;
                }

                _position += written;
                return written;
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) _file.Dispose();
            base.Dispose(disposing);
        }
    }
}
