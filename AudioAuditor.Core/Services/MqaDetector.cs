using System;
using System.IO;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace AudioQualityChecker.Services
{
    /// <summary>
    /// Detects MQA (Master Quality Authenticated) encoding in audio files.
    /// Ported from our own MQA-Toolkit Python implementation
    /// (https://github.com/Angel2mp3/MQA-Toolkit). The underlying MQA codec
    /// reverse-engineering — the 36-bit sync word and sample-rate decoding — is
    /// the work of Stavros Avramidis (purpl3F0x/MQA_identifier, Apache-2.0) and
    /// Dniel97/MQA-identifier-python. MQA embeds a 36-bit sync word in the XOR
    /// of the stereo channels.
    /// </summary>
    public static class MqaDetector
    {
        // MQA 36-bit sync word (magic number)
        private const long MAGIC_WORD = 0xBE0498C88L;

        /// <summary>
        /// Result of MQA detection.
        /// </summary>
        public class MqaResult
        {
            public bool IsMqa { get; set; }
            public bool IsStudio { get; set; }
            public string OriginalSampleRate { get; set; } = "";
            public string Encoder { get; set; } = "";
        }

        /// <summary>
        /// Decode 4-bit sample rate code to actual sample rate in Hz.
        /// </summary>
        private static int DecodeSampleRate(int code)
        {
            // Bit 0 (LSB): base frequency (0=44.1kHz, 1=48kHz)
            int baseRate = (code & 0x1) != 0 ? 48000 : 44100;

            // Bits 1-3: multiplier as power of 2
            int multiplierBits = (code >> 1) & 0x7;
            int multiplier = 1 << multiplierBits; // 2^n where n = 0-7

            // For DSD rates (multiplier > 16), double it
            if (multiplier > 16)
                multiplier *= 2;

            return baseRate * multiplier;
        }

        /// <summary>
        /// Extract bits from XOR'd stereo channels.
        /// </summary>
        private static int ExtractBits(int[] leftChannel, int[] rightChannel,
            int startPos, int numBits, int bitPosition)
        {
            int result = 0;
            for (int i = 0; i < numBits; i++)
            {
                if (startPos + i < leftChannel.Length)
                {
                    int left = leftChannel[startPos + i];
                    int right = rightChannel[startPos + i];
                    int bit = ((left ^ right) >> bitPosition) & 1;
                    result = (result << 1) | bit;
                }
            }
            return result;
        }

        /// <summary>
        /// Detect MQA encoding by analyzing raw PCM audio samples.
        /// Looks for the 36-bit MQA sync word embedded in the XOR of stereo channels.
        /// </summary>
        public static MqaResult? Detect(string filePath, TagLib.File? sharedTagFile = null, bool runAudioSampleAnalysis = true)
        {
            // Fast-skip: callers pass runAudioSampleAnalysis=false for files that can't possibly
            // be MQA (non-stereo, non-44.1k/48k, lossy formats). Saves a decoder open + bit scan.
            if (runAudioSampleAnalysis)
            {
                var result = DetectFromAudioSamples(filePath, sharedTagFile);
                if (result != null) return result;
            }

            // Fallback: check metadata tags (for files with proper MQA tagging)
            return DetectFromMetadata(filePath, sharedTagFile);
        }

        private static MqaResult? DetectFromAudioSamples(string filePath, TagLib.File? sharedTagFile = null)
        {
            try
            {
                IDisposable? reader = null;
                WaveFormat waveFormat;
                ISampleProvider sampleProvider;

                try
                {
                    var afr = new AudioFileReader(filePath);
                    reader = afr;
                    sampleProvider = afr;
                    waveFormat = afr.WaveFormat;
                }
                catch
                {
#if !CROSS_PLATFORM
                    try
                    {
                        var mfr = new MediaFoundationReader(filePath);
                        reader = mfr;
                        waveFormat = mfr.WaveFormat;
                        sampleProvider = mfr.WaveFormat.Encoding == WaveFormatEncoding.IeeeFloat
                            ? new WaveToSampleProvider(mfr)
                            : new Pcm16BitToSampleProvider(new WaveFormatConversionStream(
                                new WaveFormat(mfr.WaveFormat.SampleRate, 16, mfr.WaveFormat.Channels), mfr));
                    }
                    catch
                    {
                        return null;
                    }
#else
                    try
                    {
                        var (d, sp, fmt) = AudioAnalyzer.OpenAudioFile(filePath);
                        reader = d;
                        sampleProvider = sp;
                        waveFormat = fmt;
                    }
                    catch
                    {
                        return null;
                    }
#endif
                }

                using (reader)
                {
                    // MQA requires stereo (2 channels)
                    if (waveFormat.Channels != 2)
                        return null;

                    int sampleRate = waveFormat.SampleRate;
                    // Only scan first 3 seconds for performance
                    int maxSamples = Math.Min(sampleRate * 3, sampleRate * 10);

                    // Read interleaved float samples (L, R, L, R, ...)
                    float[] buffer = new float[maxSamples * 2];
                    int samplesRead = sampleProvider.Read(buffer, 0, buffer.Length);
                    int frameCount = samplesRead / 2;

                    if (frameCount < 100) return null;

                    // Convert to int32 arrays (separate channels)
                    // Scale float [-1,1] to int32 range
                    int[] left = new int[frameCount];
                    int[] right = new int[frameCount];

                    for (int i = 0; i < frameCount; i++)
                    {
                        left[i] = (int)(buffer[i * 2] * 2147483647.0f);
                        right[i] = (int)(buffer[i * 2 + 1] * 2147483647.0f);
                    }

                    var result = ScanSamplesForSync(left, right, frameCount);
                    if (result == null) return null;

                    // Embedded MQA found: enrich with the encoder name from tags if present.
                    result.Encoder = ReadEmbeddedEncoder(filePath, sharedTagFile);
                    return result;
                }
            }
            catch
            {
                return null;
            }
        }

        // Genuine MQA embeds the sync word once per frame, so it recurs many times across
        // the scan window. Requiring several hits at the SAME bit position is what separates
        // real MQA from a chance 36-bit collision in random PCM: a lone match (~1.5e-5 odds
        // per file over 8 positions x ~132k samples) no longer counts. Even the largest real
        // audio frame sizes still yield far more than this many syncs in a 3-second window.
        private const int MIN_SYNC_HITS = 3;

        /// <summary>
        /// Scan XOR'd stereo samples for the MQA sync word across candidate bit positions.
        /// A file is reported as MQA only when the sync word recurs at least
        /// <see cref="MIN_SYNC_HITS"/> times at the same bit position. Pure (no I/O) so it
        /// can be unit-tested with synthetic signals.
        /// </summary>
        public static MqaResult? ScanSamplesForSync(int[] left, int[] right, int frameCount)
        {
            // Check multiple bit positions (16-23)
            for (int bitPos = 16; bitPos < 24; bitPos++)
            {
                long rollingBuffer = 0;
                int hitCount = 0;
                int firstSyncPos = -1;

                for (int i = 0; i < frameCount; i++)
                {
                    // XOR samples and extract bit at current position
                    int bit = (int)(((uint)(left[i] ^ right[i]) >> bitPos) & 1);

                    // Build 36-bit rolling buffer
                    rollingBuffer = ((rollingBuffer << 1) | (long)(uint)bit) & 0xFFFFFFFFFL;

                    // Check for MQA magic word
                    if (rollingBuffer == MAGIC_WORD)
                    {
                        if (firstSyncPos < 0) firstSyncPos = i;
                        hitCount++;

                        if (hitCount >= MIN_SYNC_HITS)
                        {
                            // Decode from the first sync occurrence (every frame carries the
                            // same fields, so the first one is representative).
                            // Extract original sample rate (4 bits at sync+3)
                            int sampleRateCode = ExtractBits(left, right, firstSyncPos + 3, 4, bitPos);
                            int originalSr = DecodeSampleRate(sampleRateCode);

                            // Extract provenance (5 bits at sync+29) for MQA Studio detection
                            int provenance = ExtractBits(left, right, firstSyncPos + 29, 5, bitPos);
                            bool isStudio = provenance > 8;

                            return new MqaResult
                            {
                                IsMqa = true,
                                IsStudio = isStudio,
                                OriginalSampleRate = $"{originalSr / 1000.0:F1} kHz",
                                Encoder = "Embedded MQA"
                            };
                        }

                        // Require a fresh, non-overlapping 36-bit window before the next hit
                        // counts, so one sync can't be tallied twice.
                        rollingBuffer = 0;
                    }
                }
            }

            return null; // No MQA detected
        }

        /// <summary>
        /// Read the ENCODER / MQAENCODER Xiph tag for an embedded-MQA file.
        /// Returns "Embedded MQA" when no encoder tag is present. Reuses the caller's
        /// TagLib file handle when provided.
        /// </summary>
        private static string ReadEmbeddedEncoder(string filePath, TagLib.File? sharedTagFile)
        {
            try
            {
                TagLib.File? ownedTagFile = null;
                var tagFile = sharedTagFile;
                if (tagFile == null)
                {
                    ownedTagFile = TagLib.File.Create(filePath);
                    tagFile = ownedTagFile;
                }
                try
                {
                    if (tagFile.Tag is TagLib.Ogg.XiphComment xiph)
                    {
                        var enc = xiph.GetField("ENCODER");
                        var mqaEnc = xiph.GetField("MQAENCODER");
                        if (enc != null && enc.Length > 0 && !string.IsNullOrEmpty(enc[0]))
                            return enc[0];
                        if (mqaEnc != null && mqaEnc.Length > 0 && !string.IsNullOrEmpty(mqaEnc[0]))
                            return mqaEnc[0];
                    }
                }
                finally { ownedTagFile?.Dispose(); }
            }
            catch { }

            return "Embedded MQA";
        }

        private static MqaResult? DetectFromMetadata(string filePath, TagLib.File? sharedTagFile = null)
        {
            TagLib.File? ownedTagFile = null;
            try
            {
                var tagFile = sharedTagFile;
                if (tagFile == null)
                {
                    ownedTagFile = TagLib.File.Create(filePath);
                    tagFile = ownedTagFile;
                }

                string encoder = "";
                string mqaEncoder = "";
                string originalSr = "";

                if (tagFile.Tag is TagLib.Ogg.XiphComment xiph)
                {
                    var enc = xiph.GetField("ENCODER");
                    var mqaEnc = xiph.GetField("MQAENCODER");
                    var origSr = xiph.GetField("ORIGINALSAMPLERATE");

                    if (enc != null && enc.Length > 0) encoder = enc[0] ?? "";
                    if (mqaEnc != null && mqaEnc.Length > 0) mqaEncoder = mqaEnc[0] ?? "";
                    if (origSr != null && origSr.Length > 0) originalSr = origSr[0] ?? "";
                }

                bool isMqa = false;
                bool isStudio = false;

                if (encoder.Contains("MQA", StringComparison.OrdinalIgnoreCase) ||
                    mqaEncoder.Contains("MQA", StringComparison.OrdinalIgnoreCase))
                {
                    isMqa = true;
                    if (encoder.Contains("STUDIO", StringComparison.OrdinalIgnoreCase) ||
                        mqaEncoder.Contains("STUDIO", StringComparison.OrdinalIgnoreCase))
                        isStudio = true;
                }

                if (!string.IsNullOrWhiteSpace(originalSr))
                    isMqa = true;

                if (isMqa)
                {
                    string sampleRateDisplay;
                    if (!string.IsNullOrEmpty(originalSr) && int.TryParse(originalSr, out int origSrValue))
                        sampleRateDisplay = $"{origSrValue / 1000.0:F1} kHz";
                    else
                        sampleRateDisplay = $"{tagFile.Properties.AudioSampleRate / 1000.0:F1} kHz";

                    return new MqaResult
                    {
                        IsMqa = true,
                        IsStudio = isStudio,
                        OriginalSampleRate = sampleRateDisplay,
                        Encoder = encoder.Length > 0 ? encoder : (mqaEncoder.Length > 0 ? mqaEncoder : "Unknown")
                    };
                }

                return null;
            }
            catch
            {
                return null;
            }
            finally
            {
                ownedTagFile?.Dispose();
            }
        }
    }
}
