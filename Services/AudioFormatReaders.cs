using System;
using System.Collections.Generic;
using System.IO;
using Concentus.Structs;
using Concentus.Oggfile;
using NAudio.Wave;

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
        private readonly object _lock = new();

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
            get { lock (_lock) return _position; }
            set
            {
                lock (_lock)
                {
                    _position = Math.Clamp(value, 0, _totalBytes);
                    // Snap to block alignment (8 bytes for stereo float32)
                    // to prevent misaligned reads producing garbage floats
                    int blockAlign = WaveFormat.BlockAlign;
                    _position = (_position / blockAlign) * blockAlign;
                    _readOffset = (int)_position;
                }
            }
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            lock (_lock)
            {
                int available = _pcmData.Length - _readOffset;
                int toCopy = Math.Min(count, available);
                if (toCopy <= 0) return 0;

                Buffer.BlockCopy(_pcmData, _readOffset, buffer, offset, toCopy);
                _readOffset += toCopy;
                _position += toCopy;
                return toCopy;
            }
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
        private readonly object _lock = new();

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
                int fmtOffset = 28;
                if (raw.Length > fmtOffset + 52)
                {
                    int formatVersion = BitConverter.ToInt32(raw, fmtOffset + 8);
                    channels = BitConverter.ToInt32(raw, fmtOffset + 20);
                    dsdSampleRate = BitConverter.ToInt32(raw, fmtOffset + 16);
                    int bitsPerSample = BitConverter.ToInt32(raw, fmtOffset + 24);
                    long sampleCount = BitConverter.ToInt64(raw, fmtOffset + 28);
                    int blockSize = BitConverter.ToInt32(raw, fmtOffset + 36);

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
                int dataStart = 0;
                for (int i = 0; i < Math.Min(raw.Length - 4, 8192); i++)
                {
                    if (raw[i] == 'D' && raw[i+1] == 'S' && raw[i+2] == 'D' && raw[i+3] == ' '
                        && i > 4)
                    {
                        dataStart = i + 12;
                        break;
                    }
                }
                if (dataStart == 0) dataStart = 512;
                dsdData = new byte[raw.Length - dataStart];
                Array.Copy(raw, dataStart, dsdData, 0, dsdData.Length);
            }

            int decimationFactor = 16;
            int pcmSampleRate = dsdSampleRate / decimationFactor;
            if (pcmSampleRate > 192000) pcmSampleRate = 176400;

            int dsdBytesPerChannel = dsdData.Length / channels;
            int pcmSamplesPerChannel = (dsdBytesPerChannel * 8) / decimationFactor;

            var pcmSamples = new float[pcmSamplesPerChannel * channels];

            for (int ch = 0; ch < channels; ch++)
            {
                for (int i = 0; i < pcmSamplesPerChannel; i++)
                {
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
            get { lock (_lock) return _position; }
            set
            {
                lock (_lock)
                {
                    _position = Math.Clamp(value, 0, _pcmData.Length);
                    int blockAlign = WaveFormat.BlockAlign;
                    _position = (_position / blockAlign) * blockAlign;
                }
            }
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            lock (_lock)
            {
                int available = _pcmData.Length - (int)_position;
                int toCopy = Math.Min(count, available);
                if (toCopy <= 0) return 0;

                Buffer.BlockCopy(_pcmData, (int)_position, buffer, offset, toCopy);
                _position += toCopy;
                return toCopy;
            }
        }
    }
}
