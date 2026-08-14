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
    public class OpusFileReader : WaveStream
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
            try
            {
                // Channel count comes from the file's own OpusHead, not a guess. This was hardcoded
                // to 2, so every mono Opus file decoded through a stereo-configured decoder, was
                // reported as Stereo, and — both channels then being identical — was flagged
                // "Mono Duplicate" by the fake-stereo detector.
                int channels = ReadOpusChannelCount(_stream);

                // Opus always decodes at 48 kHz regardless of what was fed to the encoder; the
                // OpusHead's "input sample rate" field is informational only. 48000 is correct here.
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
            catch
            {
                _stream.Dispose();
                throw;
            }
        }

        /// <summary>
        /// Reads the channel count out of the Opus identification header (RFC 7845 §5.1): the
        /// magic "OpusHead", then version, then channel count at byte 9. It lives in the first Ogg
        /// page, so a short scan from the start finds it. Falls back to stereo when the header is
        /// missing or malformed, which is the old behaviour and the safer guess.
        /// </summary>
        private static int ReadOpusChannelCount(Stream stream)
        {
            try
            {
                long saved = stream.Position;
                stream.Position = 0;

                byte[] buf = new byte[8192];
                int read = stream.Read(buf, 0, buf.Length);
                stream.Position = saved;

                ReadOnlySpan<byte> magic = "OpusHead"u8;
                for (int i = 0; i + magic.Length + 2 <= read; i++)
                {
                    if (!buf.AsSpan(i, magic.Length).SequenceEqual(magic)) continue;

                    int channels = buf[i + 9];
                    // Concentus' OpusDecoder handles mono and stereo. Surround Opus (channel
                    // mapping family 1) needs a projection decoder it does not provide, so those
                    // keep the stereo path rather than failing outright.
                    return channels is 1 or 2 ? channels : 2;
                }
            }
            catch { /* fall through to the stereo default */ }

            return 2;
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

}
