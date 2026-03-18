using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using TagLib;

namespace AudioQualityChecker.Services
{
    /// <summary>
    /// Detects verifiable AI watermarks and metadata signatures embedded by
    /// AI music generation services (Suno, Udio, AIVA, Boomy, etc.).
    ///
    /// Detection methods (verifiable evidence ONLY):
    ///   1. Metadata tag scanning — known AI service markers in ID3v2, Vorbis, APE, MP4 tags
    ///   2. Raw byte pattern scanning — AI service identifiers embedded in file headers/trailers
    ///   3. C2PA / Content Credentials — provenance manifest markers
    ///   4. Known custom chunks/atoms — service-specific embedded data
    ///
    /// This detector does NOT use heuristic or statistical methods (spectral analysis,
    /// fingerprinting, etc.). It only flags files with concrete, verifiable evidence.
    /// </summary>
    public static class AiWatermarkDetector
    {
        // ══════════════════════════════════════════════════════════════
        //  Known AI service metadata markers
        // ══════════════════════════════════════════════════════════════

        private static readonly string[] AiMetaMarkers =
        {
            // AI music generation services — domain-level identifiers only
            "suno.ai", "suno.com",
            "chirp-v", // Suno's model identifier (chirp-v2, chirp-v3, etc.)
            "udio.com", "udio.ai",
            "soundraw.io",
            "aiva.ai",
            "boomy.com",
            "amper music", "shutterstock ai",
            "loudly.com",
            "beatoven.ai",
            "mubert.com",
            "stable audio", "stability.ai",
            "openai jukebox",
            "topmediai",
            "sunoai",
            "vocals.ai",

            // AI watermarking systems
            "audioseal", "audio_seal", "audio seal",
            "synthid", "synth_id", "synth id",
            "wavmark", "wav_mark",

            // Content provenance / credentials
            "content credentials", "contentcredentials",
            "contentauthenticity", "content authenticity",
            "cai manifest",

            // Generic AI markers (only matched in metadata, not user-facing text)
            "ai-generated", "ai_generated",
        };

        // Specific metadata field names that AI services use
        private static readonly string[] AiTagFieldNames =
        {
            "ai_generated", "ai-generated", "is_ai",
            "generation_model", "generation-model",
            "ai_model", "ai-model",
            "ai_source", "ai-source", "ai_service",
            "generated_by", "generated-by",
            "ai_watermark", "watermark_type",
            "c2pa.actions", "c2pa.claim",
            "content_credentials",
            "synthesis_tool", "synthesis-tool",
            "suno_id", "suno-id", "suno_prompt",
            "udio_id", "udio-id", "udio_track",
            "ai_platform", "ai-platform",
        };

        // Known non-AI software/encoders to exclude (prevents false positives)
        private static readonly string[] KnownNonAiSoftware =
        {
            "audacity", "adobe audition", "fl studio", "ableton",
            "pro tools", "logic pro", "cubase", "reaper", "garageband",
            "studio one", "bitwig", "lmms", "ardour", "cakewalk",
            "bandlab", "soundforge", "wavelab", "izotope", "ozone",
            "ffmpeg", "lame", "libvorbis", "libopus", "libflac",
            "mediamonkey", "foobar2000", "musicbee", "winamp",
            "itunes", "xld", "dbpoweramp", "exact audio copy",
            "freac", "sox", "goldwave", "ocenaudio", "wavepad",
            "nero", "cdex", "audiograbber",
            "streamrip", "deemix", "spotify", "tidal",
        };

        // Raw byte patterns to search for in file data (>= 8 bytes to avoid false matches).
        private static readonly byte[][] RawBytePatterns =
        {
            Encoding.ASCII.GetBytes("contentcredentials"),
            Encoding.ASCII.GetBytes("audioseal"),
        };

        // Text patterns to search for in raw file header/trailer bytes (>= 7 chars).
        private static readonly string[] RawTextPatterns =
        {
            "suno.ai", "suno.com",
            "udio.com", "soundraw.io",
            "aiva.ai", "boomy.com", "mubert.com",
            "beatoven.ai", "stability.ai", "loudly.com",
            "audioseal", "synthid",
            "contentcredentials",
            "chirp-v2", "chirp-v3", "chirp-v4",
        };

        // ══════════════════════════════════════════════════════════════
        //  Public API
        // ══════════════════════════════════════════════════════════════

        public class AiDetectionResult
        {
            public bool IsAiDetected { get; set; }
            public List<string> Sources { get; set; } = new();
            public string Summary { get; set; } = "";
            public double Confidence { get; set; }
        }

        /// <summary>
        /// Scans an audio file for verifiable AI watermarks and metadata signatures.
        /// Returns detection result with found evidence.
        /// </summary>
        public static AiDetectionResult Detect(string filePath)
        {
            var result = new AiDetectionResult();
            var foundMarkers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var encoderInfo = new List<string>();

            try { ScanMetadataTags(filePath, foundMarkers, encoderInfo); }
            catch { }

            try { ScanRawBytes(filePath, foundMarkers); }
            catch { }

            // False-positive filtering: if known DAW/encoder, keep only named-service markers
            bool hasKnownEncoder = encoderInfo.Any(enc =>
                KnownNonAiSoftware.Any(sw => enc.Contains(sw, StringComparison.OrdinalIgnoreCase)));

            if (hasKnownEncoder)
            {
                var strongMarkers = foundMarkers.Where(m => IsStrongAiMarker(m))
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);
                foundMarkers = strongMarkers;
            }

            double confidence = CalculateConfidence(foundMarkers);

            if (foundMarkers.Count > 0 && confidence >= 0.5)
            {
                result.IsAiDetected = true;
                result.Sources = foundMarkers.ToList();
                result.Confidence = confidence;
                result.Summary = string.Join(", ", foundMarkers.Take(3));
                if (foundMarkers.Count > 3)
                    result.Summary += $" (+{foundMarkers.Count - 3} more)";
            }

            return result;
        }

        private static bool IsStrongAiMarker(string marker)
        {
            var strong = new[] {
                "Suno", "Udio", "Soundraw", "AIVA", "Boomy", "Amper", "Loudly",
                "Beatoven", "Mubert", "Stable Audio", "Jukebox", "TopMediai", "Vocals.ai",
                "AudioSeal", "SynthID", "WavMark", "Content Credentials",
                "Content Authenticity", "Chirp"
            };
            return strong.Any(s => marker.Contains(s, StringComparison.OrdinalIgnoreCase));
        }

        private static double CalculateConfidence(HashSet<string> markers)
        {
            if (markers.Count == 0) return 0;
            double score = 0;
            foreach (var m in markers)
                score += IsStrongAiMarker(m) ? 0.5 : 0.2;
            return Math.Min(score, 1.0);
        }

        // ══════════════════════════════════════════════════════════════
        //  Metadata Tag Scanning
        // ══════════════════════════════════════════════════════════════

        private static void ScanMetadataTags(string filePath, HashSet<string> found, List<string>? encoderInfo = null)
        {
            using var tagFile = TagLib.File.Create(filePath);
            if (tagFile?.Tag == null) return;

            var allTagText = new StringBuilder(2048);

            encoderInfo ??= new List<string>();
            try
            {
                foreach (var codec in tagFile.Properties.Codecs)
                    if (codec != null) encoderInfo.Add(codec.Description ?? "");
            }
            catch { }

            // ── General tag fields ──
            AppendIfNotEmpty(allTagText, "comment", tagFile.Tag.Comment);
            AppendIfNotEmpty(allTagText, "copyright", tagFile.Tag.Copyright);
            AppendIfNotEmpty(allTagText, "description", tagFile.Tag.Description);
            AppendIfNotEmpty(allTagText, "conductor", tagFile.Tag.Conductor);
            AppendIfNotEmpty(allTagText, "grouping", tagFile.Tag.Grouping);

            if (tagFile.Tag.Composers != null)
                foreach (var c in tagFile.Tag.Composers)
                    AppendIfNotEmpty(allTagText, "composer", c);

            // ── ID3v2 specific frames ──
            if (tagFile.GetTag(TagTypes.Id3v2) is TagLib.Id3v2.Tag id3)
            {
                foreach (var frame in id3.GetFrames<TagLib.Id3v2.UserTextInformationFrame>())
                {
                    string desc = frame.Description ?? "";
                    string val = frame.Text?.Length > 0 ? string.Join(" ", frame.Text) : "";

                    foreach (var fieldName in AiTagFieldNames)
                    {
                        if (desc.Contains(fieldName, StringComparison.OrdinalIgnoreCase))
                            found.Add($"Tag:{desc}={val}");
                    }

                    AppendIfNotEmpty(allTagText, desc, val);
                }

                foreach (var frame in id3.GetFrames<TagLib.Id3v2.UserUrlLinkFrame>())
                {
                    var urlText = frame.Text != null ? string.Join(" ", frame.Text) : "";
                    AppendIfNotEmpty(allTagText, frame.Description ?? "url", urlText);
                }

                foreach (var frame in id3.GetFrames<TagLib.Id3v2.CommentsFrame>())
                {
                    AppendIfNotEmpty(allTagText, "comment", frame.Text ?? "");
                    AppendIfNotEmpty(allTagText, "comment_desc", frame.Description ?? "");
                }

                foreach (var frame in id3.GetFrames<TagLib.Id3v2.TextInformationFrame>())
                {
                    string val = frame.Text?.Length > 0 ? string.Join(" ", frame.Text) : "";
                    AppendIfNotEmpty(allTagText, frame.FrameId.ToString(), val);
                }
            }

            // ── Xiph/Vorbis comments (FLAC, OGG, OPUS) ──
            if (tagFile.GetTag(TagTypes.Xiph) is TagLib.Ogg.XiphComment xiph)
            {
                foreach (var fieldName in AiTagFieldNames)
                {
                    var values = xiph.GetField(fieldName);
                    if (values != null && values.Length > 0)
                    {
                        foreach (var val in values)
                        {
                            found.Add($"Tag:{fieldName}={val}");
                            AppendIfNotEmpty(allTagText, fieldName, val);
                        }
                    }
                }

                string[] commonFields = { "ENCODER", "COMMENT", "DESCRIPTION", "SOURCE",
                    "TOOL", "SOFTWARE", "GENERATOR", "CREATED_BY" };
                foreach (var fieldName in commonFields)
                {
                    var values = xiph.GetField(fieldName);
                    if (values != null && values.Length > 0)
                        foreach (var val in values)
                            AppendIfNotEmpty(allTagText, fieldName, val);
                }
            }

            // ── APE tags ──
            if (tagFile.GetTag(TagTypes.Ape) is TagLib.Ape.Tag ape)
            {
                foreach (var fieldName in AiTagFieldNames)
                {
                    var item = ape.GetItem(fieldName);
                    if (item != null)
                        found.Add($"Tag:{fieldName}={item}");
                }
            }

            // ── Apple/MP4 tags ──
            if (tagFile.GetTag(TagTypes.Apple) is TagLib.Mpeg4.AppleTag apple)
            {
                var toolAtom = apple.DataBoxes("©too");
                foreach (var box in toolAtom)
                    AppendIfNotEmpty(allTagText, "encoding_tool", box.Text ?? "");

                var cmtAtom = apple.DataBoxes("©cmt");
                foreach (var box in cmtAtom)
                    AppendIfNotEmpty(allTagText, "comment", box.Text ?? "");

                string[] freeFormKeys = {
                    "AI_GENERATED", "AI_SOURCE", "GENERATOR", "CREATED_BY",
                    "SUNO_ID", "UDIO_ID", "AI_PLATFORM", "AI_SERVICE"
                };
                foreach (var key in freeFormKeys)
                {
                    try
                    {
                        var boxes = apple.DataBoxes("----:com.apple.iTunes:" + key);
                        foreach (var box in boxes)
                        {
                            if (!string.IsNullOrEmpty(box.Text))
                            {
                                found.Add($"Atom:{key}={box.Text}");
                                AppendIfNotEmpty(allTagText, key, box.Text);
                            }
                        }
                    }
                    catch { }
                }
            }

            // ── Collect encoder info ──
            string[] encoderFields = { "ENCODER", "TOOL", "SOFTWARE", "ENCODED_BY", "ENCODING", "TSSE" };
            if (tagFile.GetTag(TagTypes.Xiph) is TagLib.Ogg.XiphComment xiphEnc)
            {
                foreach (var ef in encoderFields)
                {
                    var vals = xiphEnc.GetField(ef);
                    if (vals != null)
                        foreach (var v in vals)
                            if (!string.IsNullOrWhiteSpace(v)) encoderInfo.Add(v);
                }
            }
            if (tagFile.GetTag(TagTypes.Id3v2) is TagLib.Id3v2.Tag id3Enc)
            {
                foreach (var frame in id3Enc.GetFrames<TagLib.Id3v2.TextInformationFrame>())
                {
                    string fid = frame.FrameId.ToString();
                    if (fid == "TSSE" || fid == "TENC")
                    {
                        string val = frame.Text?.Length > 0 ? string.Join(" ", frame.Text) : "";
                        if (!string.IsNullOrWhiteSpace(val)) encoderInfo.Add(val);
                    }
                }
            }

            // ── Search collected text for AI markers ──
            string allText = allTagText.ToString();

            foreach (var marker in AiMetaMarkers)
            {
                if (allText.Contains(marker, StringComparison.OrdinalIgnoreCase))
                {
                    string source = CategorizeMarker(marker);
                    if (!string.IsNullOrEmpty(source))
                        found.Add(source);
                }
            }
        }

        // ══════════════════════════════════════════════════════════════
        //  Raw Byte Pattern Scanning
        // ══════════════════════════════════════════════════════════════

        private static void ScanRawBytes(string filePath, HashSet<string> found)
        {
            var fi = new FileInfo(filePath);
            if (!fi.Exists || fi.Length == 0) return;

            // Read first 64KB and last 64KB for embedded markers
            int headTailSize = (int)Math.Min(65536, fi.Length);

            byte[] headBytes;
            byte[] tailBytes;

            using (var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                headBytes = new byte[headTailSize];
                int headRead = fs.Read(headBytes, 0, headTailSize);
                if (headRead < headTailSize)
                    Array.Resize(ref headBytes, headRead);

                if (fi.Length > headTailSize * 2)
                {
                    fs.Seek(-headTailSize, SeekOrigin.End);
                    tailBytes = new byte[headTailSize];
                    int tailRead = fs.Read(tailBytes, 0, headTailSize);
                    if (tailRead < headTailSize)
                        Array.Resize(ref tailBytes, tailRead);
                }
                else
                {
                    tailBytes = Array.Empty<byte>();
                }
            }

            foreach (var pattern in RawBytePatterns)
            {
                if (ContainsPattern(headBytes, pattern) || ContainsPattern(tailBytes, pattern))
                {
                    string patternStr = Encoding.ASCII.GetString(pattern);
                    string source = CategorizeMarker(patternStr);
                    if (!string.IsNullOrEmpty(source))
                        found.Add(source);
                }
            }

            string headText = Encoding.ASCII.GetString(headBytes).ToLowerInvariant();
            string tailText = tailBytes.Length > 0
                ? Encoding.ASCII.GetString(tailBytes).ToLowerInvariant()
                : "";

            foreach (var pattern in RawTextPatterns)
            {
                if (headText.Contains(pattern) || tailText.Contains(pattern))
                {
                    string source = CategorizeMarker(pattern);
                    if (!string.IsNullOrEmpty(source))
                        found.Add(source);
                }
            }
        }

        // ══════════════════════════════════════════════════════════════
        //  Helpers
        // ══════════════════════════════════════════════════════════════

        private static void AppendIfNotEmpty(StringBuilder sb, string key, string? value)
        {
            if (!string.IsNullOrWhiteSpace(value))
                sb.Append(' ').Append(key).Append(':').Append(value);
        }

        private static string CategorizeMarker(string marker)
        {
            var lower = marker.ToLowerInvariant();

            if (lower.Contains("suno.ai") || lower.Contains("suno.com") || lower.Contains("sunoai")) return "Suno AI";
            if (lower.Contains("chirp-v")) return "Suno AI";
            if (lower.Contains("udio.com") || lower.Contains("udio.ai")) return "Udio";
            if (lower.Contains("soundraw")) return "Soundraw";
            if (lower.Contains("aiva.ai")) return "AIVA";
            if (lower.Contains("boomy.com")) return "Boomy";
            if (lower.Contains("amper music") || lower.Contains("shutterstock ai")) return "Amper/Shutterstock";
            if (lower.Contains("loudly.com")) return "Loudly";
            if (lower.Contains("beatoven")) return "Beatoven.ai";
            if (lower.Contains("mubert")) return "Mubert";
            if (lower.Contains("stable audio") || lower.Contains("stability.ai")) return "Stable Audio";
            if (lower.Contains("openai jukebox")) return "OpenAI Jukebox";
            if (lower.Contains("topmediai")) return "TopMediai";
            if (lower.Contains("vocals.ai")) return "Vocals.ai";
            if (lower.Contains("audioseal") || lower.Contains("audio_seal")) return "AudioSeal Watermark";
            if (lower.Contains("synthid") || lower.Contains("synth_id")) return "SynthID Watermark";
            if (lower.Contains("wavmark") || lower.Contains("wav_mark")) return "WavMark Watermark";
            if (lower.Contains("contentcredential") || lower.Contains("content credential")) return "Content Credentials";
            if (lower.Contains("contentauthenticity") || lower.Contains("content authenticity")) return "Content Authenticity";
            if (lower.Contains("ai-generated") || lower.Contains("ai_generated")) return "AI Generated";

            return "AI Marker Found";
        }

        private static bool ContainsPattern(byte[] buffer, byte[] pattern)
        {
            if (pattern.Length == 0 || buffer.Length < pattern.Length) return false;

            for (int i = 0; i <= buffer.Length - pattern.Length; i++)
            {
                bool match = true;
                for (int j = 0; j < pattern.Length; j++)
                {
                    if (buffer[i + j] != pattern[j])
                    {
                        match = false;
                        break;
                    }
                }
                if (match) return true;
            }
            return false;
        }
    }
}
