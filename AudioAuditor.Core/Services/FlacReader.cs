using System;
using System.Collections.Generic;
using System.IO;
using NAudio.Wave;

namespace AudioQualityChecker.Services
{
    /// <summary>
    /// Pure managed FLAC decoder. Reads a FLAC file and provides decoded PCM via WaveStream.
    /// Used as fallback when MediaFoundation FLAC codec is unavailable or fails.
    /// Supports 8/16/24/32-bit, all standard sample rates, 1-8 channels.
    /// Outputs IEEE float PCM to preserve full dynamic range for analysis.
    /// </summary>
    public sealed class FlacFileReader : WaveStream
    {
        private readonly WaveFormat _waveFormat;
        private readonly byte[] _pcmData;
        private long _position;
        private readonly object _seekLock = new();

        /// <summary>Original FLAC bits per sample (8/16/24/32) for analysis accuracy.</summary>
        public int NativeBitsPerSample { get; private set; }

        // STREAMINFO fields
        private int _minBlockSize, _maxBlockSize;
        private int _streamSampleRate, _streamChannels, _streamBitsPerSample;
        private long _totalSamples;

        /// <summary>
        /// Creates a FlacFileReader synchronously from pre-read file data.
        /// Use <see cref="CreateAsync"/> to avoid blocking the UI thread.
        /// </summary>
        public FlacFileReader(string filePath) : this(File.ReadAllBytes(filePath)) { }

        private FlacFileReader(byte[] fileData)
        {
            int offset = 0;

            // ── Verify magic number "fLaC" ──
            if (fileData.Length < 42 ||
                fileData[0] != 0x66 || fileData[1] != 0x4C ||
                fileData[2] != 0x61 || fileData[3] != 0x43)
                throw new InvalidDataException("Not a valid FLAC file");
            offset = 4;

            // ── Read metadata blocks ──
            bool lastBlock = false;
            while (!lastBlock && offset + 4 <= fileData.Length)
            {
                byte blockHeader = fileData[offset++];
                lastBlock = (blockHeader & 0x80) != 0;
                int blockType = blockHeader & 0x7F;
                int blockLen = (fileData[offset] << 16) | (fileData[offset + 1] << 8) | fileData[offset + 2];
                offset += 3;

                if (blockType == 0 && blockLen >= 34) // STREAMINFO
                {
                    _minBlockSize = (fileData[offset] << 8) | fileData[offset + 1];
                    _maxBlockSize = (fileData[offset + 2] << 8) | fileData[offset + 3];
                    _streamSampleRate = (fileData[offset + 10] << 12) |
                                        (fileData[offset + 11] << 4) |
                                        (fileData[offset + 12] >> 4);
                    _streamChannels = ((fileData[offset + 12] >> 1) & 0x07) + 1;
                    _streamBitsPerSample = ((fileData[offset + 12] & 0x01) << 4) |
                                           (fileData[offset + 13] >> 4);
                    _streamBitsPerSample += 1;
                    _totalSamples = ((long)(fileData[offset + 13] & 0x0F) << 32) |
                                    ((long)fileData[offset + 14] << 24) |
                                    ((long)fileData[offset + 15] << 16) |
                                    ((long)fileData[offset + 16] << 8) |
                                    fileData[offset + 17];
                }
                offset += blockLen;
            }

            if (_streamSampleRate == 0 || _streamChannels == 0 || _streamBitsPerSample == 0)
                throw new InvalidDataException("FLAC STREAMINFO is missing or invalid");

            NativeBitsPerSample = _streamBitsPerSample;

            // ── Decode all frames to IEEE float PCM ──
            // IEEE float preserves full dynamic range regardless of source bit depth
            _waveFormat = WaveFormat.CreateIeeeFloatWaveFormat(_streamSampleRate, _streamChannels);

            var allSamples = new List<float>();
            if (_totalSamples > 0)
                allSamples.Capacity = (int)Math.Min(_totalSamples * _streamChannels, int.MaxValue / 4);

            DecodeFrames(fileData, offset, allSamples);

            // Convert float list to byte array (4 bytes per sample)
            _pcmData = new byte[allSamples.Count * 4];
            for (int i = 0; i < allSamples.Count; i++)
            {
                byte[] bytes = BitConverter.GetBytes(allSamples[i]);
                Buffer.BlockCopy(bytes, 0, _pcmData, i * 4, 4);
            }
            _position = 0;
        }

        /// <summary>
        /// Creates a FlacFileReader on a background thread to avoid UI lag.
        /// </summary>
        public static async System.Threading.Tasks.Task<FlacFileReader> CreateAsync(string filePath)
        {
            byte[] data = await System.Threading.Tasks.Task.Run(() => File.ReadAllBytes(filePath));
            return await System.Threading.Tasks.Task.Run(() => new FlacFileReader(data));
        }

        public override WaveFormat WaveFormat => _waveFormat;
        public override long Length => _pcmData.Length;

        public override long Position
        {
            get { lock (_seekLock) return _position; }
            set
            {
                lock (_seekLock)
                {
                    _position = Math.Clamp(value, 0, _pcmData.Length);
                    int blockAlign = WaveFormat.BlockAlign;
                    _position = (_position / blockAlign) * blockAlign;
                }
            }
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            lock (_seekLock)
            {
                int available = _pcmData.Length - (int)_position;
                int toCopy = Math.Min(count, available);
                if (toCopy <= 0) return 0;
                Buffer.BlockCopy(_pcmData, (int)_position, buffer, offset, toCopy);
                _position += toCopy;
                return toCopy;
            }
        }

        // ═══════════════════════════════════════════════════════
        //  Frame Decoding
        // ═══════════════════════════════════════════════════════

        private void DecodeFrames(byte[] data, int startOffset, List<float> output)
        {
            // Pre-compute scaling factor: maps signed int at native bps to -1.0..1.0 float
            float scale = 1.0f / (1 << (_streamBitsPerSample - 1));

            int pos = startOffset;
            while (pos + 4 < data.Length)
            {
                // Scan for frame sync: 0xFFF8 or 0xFFF9
                if (data[pos] != 0xFF || (data[pos + 1] & 0xFC) != 0xF8)
                {
                    pos++;
                    continue;
                }

                try
                {
                    int frameStart = pos;
                    var br = new FlacBitReader(data, pos);

                    // Read frame header
                    int syncCode = br.ReadBits(14); // should be 0x3FFE
                    if (syncCode != 0x3FFE) { pos++; continue; }

                    int reserved1 = br.ReadBits(1);
                    if (reserved1 != 0) { pos++; continue; }

                    int blockingStrategy = br.ReadBits(1);
                    int blockSizeCode = br.ReadBits(4);
                    int sampleRateCode = br.ReadBits(4);
                    int channelAssignment = br.ReadBits(4);
                    int sampleSizeCode = br.ReadBits(3);
                    int reserved2 = br.ReadBits(1);
                    if (reserved2 != 0) { pos++; continue; }

                    // Read UTF-8 coded frame/sample number (skip it, we don't need it)
                    br.ReadUtf8Long();

                    // Resolve block size
                    int blockSize = ResolveBlockSize(blockSizeCode, br);
                    if (blockSize <= 0 || blockSize > 65536) { pos++; continue; }

                    // Resolve sample rate
                    int sampleRate = ResolveSampleRate(sampleRateCode, br);
                    if (sampleRate <= 0) sampleRate = _streamSampleRate;

                    // Resolve bits per sample
                    int bps = ResolveBitsPerSample(sampleSizeCode);
                    if (bps <= 0) bps = _streamBitsPerSample;

                    // Determine number of channels from channel assignment
                    int numChannels;
                    if (channelAssignment <= 7)
                        numChannels = channelAssignment + 1;
                    else if (channelAssignment <= 10)
                        numChannels = 2; // stereo decorrelation modes
                    else
                    {
                        pos++;
                        continue; // reserved
                    }

                    // Skip CRC-8 (1 byte after aligning to byte boundary)
                    br.AlignToByte();
                    br.ReadBits(8); // CRC-8

                    // Decode subframes
                    int[][] subframeSamples = new int[numChannels][];
                    for (int ch = 0; ch < numChannels; ch++)
                    {
                        // Left-side: side channel has bps+1
                        // Right-side: side channel (ch0) has bps+1
                        // Mid-side: side channel (ch1) has bps+1
                        int channelBps = bps;
                        if (channelAssignment == 8 && ch == 1) channelBps++;   // left-side
                        else if (channelAssignment == 9 && ch == 0) channelBps++; // right-side (side is ch0)
                        else if (channelAssignment == 10 && ch == 1) channelBps++; // mid-side

                        subframeSamples[ch] = DecodeSubframe(br, blockSize, channelBps);
                    }

                    // Channel decorrelation
                    ApplyChannelDecorrelation(channelAssignment, subframeSamples, blockSize);

                    // Interleave and scale samples to IEEE float output (-1.0..1.0)
                    float bpsScale = 1.0f / (1 << (bps - 1));
                    for (int i = 0; i < blockSize; i++)
                    {
                        for (int ch = 0; ch < numChannels; ch++)
                        {
                            output.Add(subframeSamples[ch][i] * bpsScale);
                        }
                    }

                    // Skip to end of frame (align + CRC-16)
                    br.AlignToByte();
                    // CRC-16 is 2 bytes at the end, but we've already consumed the frame data
                    // Just skip the CRC
                    if (br.BytePosition + 2 <= data.Length)
                        br.ReadBits(16);

                    pos = br.BytePosition;
                }
                catch
                {
                    // Frame decode failed — skip this sync and try the next one
                    pos++;
                }
            }
        }

        private static int ResolveBlockSize(int code, FlacBitReader br)
        {
            return code switch
            {
                0 => 0, // reserved
                1 => 192,
                2 => 576,
                3 => 1152,
                4 => 2304,
                5 => 4608,
                6 => br.PeekBlockSize8() + 1,   // read later after header
                7 => br.PeekBlockSize16() + 1,   // read later after header
                >= 8 and <= 15 => 256 << (code - 8),
                _ => 0
            };
        }

        private static int ResolveSampleRate(int code, FlacBitReader br)
        {
            return code switch
            {
                0 => 0,     // get from STREAMINFO
                1 => 88200,
                2 => 176400,
                3 => 192000,
                4 => 8000,
                5 => 16000,
                6 => 22050,
                7 => 24000,
                8 => 32000,
                9 => 44100,
                10 => 48000,
                11 => 96000,
                12 => br.PeekSampleRate8kHz() * 1000,
                13 => br.PeekSampleRateHz16(),
                14 => br.PeekSampleRateTensHz16() * 10,
                _ => 0
            };
        }

        private static int ResolveBitsPerSample(int code)
        {
            return code switch
            {
                0 => 0, // get from STREAMINFO
                1 => 8,
                2 => 12,
                4 => 16,
                5 => 20,
                6 => 24,
                7 => 32,
                _ => 0
            };
        }

        private static void ApplyChannelDecorrelation(int assignment, int[][] samples, int blockSize)
        {
            if (assignment == 8) // left-side → ch0=left, ch1=side; right = left - side
            {
                for (int i = 0; i < blockSize; i++)
                    samples[1][i] = samples[0][i] - samples[1][i];
            }
            else if (assignment == 9) // side-right → ch0=side, ch1=right; left = side + right
            {
                for (int i = 0; i < blockSize; i++)
                    samples[0][i] = samples[0][i] + samples[1][i];
            }
            else if (assignment == 10) // mid-side → ch0=mid, ch1=side; left = (mid + side), right = (mid - side)
            {
                for (int i = 0; i < blockSize; i++)
                {
                    int mid = samples[0][i];
                    int side = samples[1][i];
                    // mid is actually (left+right) and side is (left-right)
                    // but FLAC shifts mid: mid = (left+right) with side's LSB added
                    mid = (mid << 1) | (side & 1);
                    samples[0][i] = (mid + side) >> 1;  // left
                    samples[1][i] = (mid - side) >> 1;  // right
                }
            }
            // assignment 0-7: independent channels, no decorrelation needed
        }

        // ═══════════════════════════════════════════════════════
        //  Subframe Decoding
        // ═══════════════════════════════════════════════════════

        private static int[] DecodeSubframe(FlacBitReader br, int blockSize, int bps)
        {
            // Subframe header
            int zeroPad = br.ReadBits(1);
            if (zeroPad != 0) throw new InvalidDataException("Subframe padding bit is not zero");

            int subframeType = br.ReadBits(6);

            // Wasted bits-per-sample flag
            int wastedBits = 0;
            int hasWasted = br.ReadBits(1);
            if (hasWasted == 1)
            {
                wastedBits = 1;
                while (br.ReadBits(1) == 0)
                    wastedBits++;
                bps -= wastedBits;
            }

            int[] samples;

            if (subframeType == 0)
            {
                // CONSTANT
                samples = DecodeConstant(br, blockSize, bps);
            }
            else if (subframeType == 1)
            {
                // VERBATIM
                samples = DecodeVerbatim(br, blockSize, bps);
            }
            else if (subframeType >= 8 && subframeType <= 12)
            {
                // FIXED (order 0-4)
                int order = subframeType - 8;
                samples = DecodeFixed(br, blockSize, bps, order);
            }
            else if (subframeType >= 32 && subframeType <= 63)
            {
                // LPC (order 1-32)
                int order = subframeType - 31;
                samples = DecodeLpc(br, blockSize, bps, order);
            }
            else
            {
                throw new InvalidDataException($"Reserved subframe type: {subframeType}");
            }

            // Restore wasted bits
            if (wastedBits > 0)
            {
                for (int i = 0; i < blockSize; i++)
                    samples[i] <<= wastedBits;
            }

            return samples;
        }

        private static int[] DecodeConstant(FlacBitReader br, int blockSize, int bps)
        {
            int value = br.ReadSignedBits(bps);
            int[] samples = new int[blockSize];
            Array.Fill(samples, value);
            return samples;
        }

        private static int[] DecodeVerbatim(FlacBitReader br, int blockSize, int bps)
        {
            int[] samples = new int[blockSize];
            for (int i = 0; i < blockSize; i++)
                samples[i] = br.ReadSignedBits(bps);
            return samples;
        }

        private static int[] DecodeFixed(FlacBitReader br, int blockSize, int bps, int order)
        {
            int[] samples = new int[blockSize];

            // Warm-up samples
            for (int i = 0; i < order; i++)
                samples[i] = br.ReadSignedBits(bps);

            // Residual
            int[] residual = DecodeResidual(br, blockSize, order);

            // Apply fixed predictor
            for (int i = order; i < blockSize; i++)
            {
                int prediction = order switch
                {
                    0 => 0,
                    1 => samples[i - 1],
                    2 => 2 * samples[i - 1] - samples[i - 2],
                    3 => 3 * samples[i - 1] - 3 * samples[i - 2] + samples[i - 3],
                    4 => 4 * samples[i - 1] - 6 * samples[i - 2] + 4 * samples[i - 3] - samples[i - 4],
                    _ => 0
                };
                samples[i] = prediction + residual[i - order];
            }

            return samples;
        }

        private static int[] DecodeLpc(FlacBitReader br, int blockSize, int bps, int order)
        {
            int[] samples = new int[blockSize];

            // Warm-up samples
            for (int i = 0; i < order; i++)
                samples[i] = br.ReadSignedBits(bps);

            // LPC precision (4 bits + 1)
            int qlpPrecision = br.ReadBits(4) + 1;
            if (qlpPrecision == 16) // 1111 binary + 1 = 16 means "invalid"
                throw new InvalidDataException("Invalid QLP precision");

            // QLP shift (5 bits, signed)
            int qlpShift = br.ReadSignedBits(5);

            // QLP coefficients
            int[] qlpCoeffs = new int[order];
            for (int i = 0; i < order; i++)
                qlpCoeffs[i] = br.ReadSignedBits(qlpPrecision);

            // Residual
            int[] residual = DecodeResidual(br, blockSize, order);

            // Apply LPC predictor
            for (int i = order; i < blockSize; i++)
            {
                long prediction = 0;
                for (int j = 0; j < order; j++)
                    prediction += (long)qlpCoeffs[j] * samples[i - 1 - j];

                if (qlpShift >= 0)
                    prediction >>= qlpShift;
                else
                    prediction <<= -qlpShift;

                samples[i] = (int)prediction + residual[i - order];
            }

            return samples;
        }

        // ═══════════════════════════════════════════════════════
        //  Rice Residual Decoding
        // ═══════════════════════════════════════════════════════

        private static int[] DecodeResidual(FlacBitReader br, int blockSize, int predictorOrder)
        {
            int codingMethod = br.ReadBits(2);
            // 0 = RICE (4-bit param), 1 = RICE2 (5-bit param)
            if (codingMethod > 1)
                throw new InvalidDataException($"Unsupported residual coding method: {codingMethod}");

            int paramBits = codingMethod == 0 ? 4 : 5;
            int escapeCode = codingMethod == 0 ? 0xF : 0x1F;

            int partitionOrder = br.ReadBits(4);
            int numPartitions = 1 << partitionOrder;

            int residualCount = blockSize - predictorOrder;
            int[] residual = new int[residualCount];
            int sampleIdx = 0;

            for (int partition = 0; partition < numPartitions; partition++)
            {
                int riceParam = br.ReadBits(paramBits);
                int partitionSamples;

                if (partitionOrder == 0)
                    partitionSamples = blockSize - predictorOrder;
                else if (partition == 0)
                    partitionSamples = (blockSize >> partitionOrder) - predictorOrder;
                else
                    partitionSamples = blockSize >> partitionOrder;

                if (riceParam == escapeCode)
                {
                    // Escape: samples are stored as signed n-bit values
                    int escapeBps = br.ReadBits(5);
                    for (int i = 0; i < partitionSamples && sampleIdx < residualCount; i++)
                    {
                        residual[sampleIdx++] = escapeBps > 0 ? br.ReadSignedBits(escapeBps) : 0;
                    }
                }
                else
                {
                    // Rice-coded samples
                    for (int i = 0; i < partitionSamples && sampleIdx < residualCount; i++)
                    {
                        residual[sampleIdx++] = br.ReadRice(riceParam);
                    }
                }
            }

            return residual;
        }
    }

    // ═══════════════════════════════════════════════════════
    //  Bit Reader for FLAC stream data
    // ═══════════════════════════════════════════════════════

    internal sealed class FlacBitReader
    {
        private readonly byte[] _data;
        private int _bytePos;
        private int _bitPos; // bits consumed in current byte (0-7), 0 = none consumed

        public FlacBitReader(byte[] data, int offset)
        {
            _data = data;
            _bytePos = offset;
            _bitPos = 0;
        }

        public int BytePosition => _bitPos == 0 ? _bytePos : _bytePos + 1;

        public void AlignToByte()
        {
            if (_bitPos != 0)
            {
                _bytePos++;
                _bitPos = 0;
            }
        }

        public int ReadBits(int count)
        {
            if (count == 0) return 0;
            int result = 0;
            int remaining = count;

            while (remaining > 0)
            {
                if (_bytePos >= _data.Length)
                    throw new EndOfStreamException("FLAC: unexpected end of data");

                int bitsAvailable = 8 - _bitPos;
                int bitsToRead = Math.Min(remaining, bitsAvailable);

                // Extract bits from current byte, MSB first
                int shift = bitsAvailable - bitsToRead;
                int mask = ((1 << bitsToRead) - 1) << shift;
                int bits = (_data[_bytePos] & mask) >> shift;

                result = (result << bitsToRead) | bits;
                remaining -= bitsToRead;

                _bitPos += bitsToRead;
                if (_bitPos >= 8)
                {
                    _bytePos++;
                    _bitPos = 0;
                }
            }

            return result;
        }

        public int ReadSignedBits(int count)
        {
            int val = ReadBits(count);
            // Sign-extend
            if (count > 0 && (val & (1 << (count - 1))) != 0)
                val |= ~0 << count;
            return val;
        }

        /// <summary>
        /// Reads a unary-coded value: count of 1-bits before the terminating 0-bit.
        /// </summary>
        public int ReadUnary()
        {
            int count = 0;
            while (ReadBits(1) == 1)
                count++;
            return count;
        }

        /// <summary>
        /// Reads a Rice-coded signed integer with the given parameter.
        /// </summary>
        public int ReadRice(int parameter)
        {
            // Quotient: unary coded (count of 0s before 1)
            int quotient = 0;
            while (ReadBits(1) == 0)
                quotient++;

            // Remainder: parameter bits
            int remainder = parameter > 0 ? ReadBits(parameter) : 0;

            // Reconstruct unsigned value
            uint uval = (uint)(quotient << parameter) | (uint)remainder;

            // Fold to signed: if LSB is 0 → val/2; if LSB is 1 → -(val/2 + 1)
            return (uval & 1) == 0 ? (int)(uval >> 1) : -(int)(uval >> 1) - 1;
        }

        /// <summary>
        /// Reads a UTF-8 coded long value (for frame/sample numbers).
        /// </summary>
        public long ReadUtf8Long()
        {
            int firstByte = ReadBits(8);
            int extraBytes;
            long value;

            if ((firstByte & 0x80) == 0)
            {
                return firstByte;
            }
            else if ((firstByte & 0xE0) == 0xC0)
            {
                extraBytes = 1;
                value = firstByte & 0x1F;
            }
            else if ((firstByte & 0xF0) == 0xE0)
            {
                extraBytes = 2;
                value = firstByte & 0x0F;
            }
            else if ((firstByte & 0xF8) == 0xF0)
            {
                extraBytes = 3;
                value = firstByte & 0x07;
            }
            else if ((firstByte & 0xFC) == 0xF8)
            {
                extraBytes = 4;
                value = firstByte & 0x03;
            }
            else if ((firstByte & 0xFE) == 0xFC)
            {
                extraBytes = 5;
                value = firstByte & 0x01;
            }
            else if (firstByte == 0xFE)
            {
                extraBytes = 6;
                value = 0;
            }
            else
            {
                throw new InvalidDataException("Invalid UTF-8 start byte in FLAC frame header");
            }

            for (int i = 0; i < extraBytes; i++)
            {
                int b = ReadBits(8);
                if ((b & 0xC0) != 0x80)
                    throw new InvalidDataException("Invalid UTF-8 continuation byte in FLAC frame header");
                value = (value << 6) | (long)(b & 0x3F);
            }

            return value;
        }

        // ── Deferred header fields (block size / sample rate read after UTF-8 number) ──
        // These are called from ResolveBlockSize/ResolveSampleRate when the code
        // indicates "read 8/16 bits from end of header". By the time these are called,
        // the bit reader is positioned right after the UTF-8 coded number.

        // For block size code 6: 8-bit value
        public int PeekBlockSize8() => ReadBits(8);

        // For block size code 7: 16-bit value
        public int PeekBlockSize16() => ReadBits(16);

        // For sample rate code 12: 8-bit value in kHz
        public int PeekSampleRate8kHz() => ReadBits(8);

        // For sample rate code 13: 16-bit value in Hz
        public int PeekSampleRateHz16() => ReadBits(16);

        // For sample rate code 14: 16-bit value in tens of Hz
        public int PeekSampleRateTensHz16() => ReadBits(16);
    }
}
