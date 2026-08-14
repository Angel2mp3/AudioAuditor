using System;
using System.IO;
using System.Text;
using AudioQualityChecker.Models;

namespace AudioQualityChecker.Services;

/// <summary>
/// Assert-based checks for the AI verdict combination in <see cref="AudioFileInfo"/>. This logic
/// decides whether the app accuses a file of being AI generated, so the properties that matter are
/// pinned here rather than left to be re-derived by whoever next touches the weights:
///   - evidence ordering (a verifiable watermark outranks vague markers),
///   - non-dilution (adding weak evidence can never lower a strong score),
///   - the heuristic ceiling (spectral checks alone can never reach "Yes"),
///   - exoneration (a confident "human" model result silences heuristics but not hard evidence).
/// </summary>
public static class AiScoringSelfCheck
{
    public static void Run()
    {
        // ── A single strong marker is verifiable evidence and must read "Yes" ──
        // This is the regression case: scoring used to be re-derived from the *count* of markers,
        // so one AudioSeal/SynthID/Suno hit scored 65% and displayed as merely "Possible".
        var watermark = new AudioFileInfo { IsAiGenerated = true, AiConfidence = 0.75 };
        Assert(watermark.AiVerdict == "Yes",
            $"one strong marker should be Yes, got {watermark.AiVerdict} ({watermark.AiCombinedConfidence:F0}%)");
        Assert(watermark.HasVerifiableAiEvidence, "a watermark hit is verifiable evidence");
        Assert(watermark.AiEvidenceKind == "watermark", "evidence kind should be 'watermark'");

        // ── Weak markers must rank BELOW a strong one (the ordering that was inverted) ──
        var weakMarkers = new AudioFileInfo { IsAiGenerated = true, AiConfidence = 0.4 };
        Assert(weakMarkers.AiCombinedConfidence < watermark.AiCombinedConfidence,
            "two weak markers must score below one strong marker");

        // ── Adding weak evidence must never LOWER a strong score (noisy-OR, not averaging) ──
        var both = new AudioFileInfo
        {
            IsAiGenerated = true,
            AiConfidence = 0.75,
            ExperimentalAiSuspicious = true,
            ExperimentalAiConfidence = 0.30
        };
        Assert(both.AiCombinedConfidence > watermark.AiCombinedConfidence,
            "a corroborating spectral flag must raise the score, never dilute it");
        Assert(both.AiVerdict == "Yes", "watermark + spectral must stay Yes");

        // ── Heuristics alone can reach "Possible" but never a confident "Yes" ──
        var maxSpectral = new AudioFileInfo { ExperimentalAiSuspicious = true, ExperimentalAiConfidence = 1.0 };
        Assert(maxSpectral.AiVerdict != "Yes",
            $"spectral heuristics alone must never reach Yes, got {maxSpectral.AiCombinedConfidence:F0}%");
        Assert(maxSpectral.AiEvidenceKind == "heuristic", "evidence kind should be 'heuristic'");
        Assert(!maxSpectral.HasVerifiableAiEvidence, "a spectral flag is not verifiable evidence");

        var weakSpectral = new AudioFileInfo { ExperimentalAiSuspicious = true, ExperimentalAiConfidence = 0.25 };
        Assert(weakSpectral.AiVerdict == "No", "a lone weak heuristic must not accuse a file");

        // ── A confident "human" model result silences heuristics ──
        var exonerated = new AudioFileInfo
        {
            ExperimentalAiSuspicious = true,
            ExperimentalAiConfidence = 0.8,
            SHLabsScanned = true,
            SHLabsPrediction = "Human Made",
            SHLabsProbability = 5.0,
            SHLabsConfidence = 90.0
        };
        Assert(exonerated.AiVerdict == "No",
            $"a confident Human Made result must override a spectral flag, got {exonerated.AiVerdict}");

        // ── …but it must NOT erase a real watermark ──
        var exoneratedButMarked = new AudioFileInfo
        {
            IsAiGenerated = true,
            AiConfidence = 0.75,
            SHLabsScanned = true,
            SHLabsPrediction = "Human Made",
            SHLabsProbability = 5.0,
            SHLabsConfidence = 90.0
        };
        Assert(exoneratedButMarked.AiVerdict == "Yes",
            "a model opinion must not override an embedded watermark");

        // ── A high SH Labs probability counts even if the label is unexpected ──
        var modelOnly = new AudioFileInfo
        {
            SHLabsScanned = true,
            SHLabsPrediction = "Something Unexpected",
            SHLabsProbability = 95.0,
            SHLabsConfidence = 90.0
        };
        Assert(modelOnly.IsAnyAiDetected, "a 95% AI probability must register regardless of the label");
        Assert(modelOnly.AiVerdict == "Yes", $"95% model probability should be Yes, got {modelOnly.AiVerdict}");
        Assert(modelOnly.AiEvidenceKind == "model", "evidence kind should be 'model'");

        // ── A clean file stays clean ──
        var clean = new AudioFileInfo();
        Assert(!clean.IsAnyAiDetected, "an unscanned file must not be flagged");
        Assert(clean.AiVerdict == "No", "an unscanned file must read No");
        Assert(clean.AiDisplay == "No", "a clean file's display is plain 'No'");

        // ── Confidence stays inside 0-100 ──
        var everything = new AudioFileInfo
        {
            IsAiGenerated = true,
            AiConfidence = 1.0,
            ExperimentalAiSuspicious = true,
            ExperimentalAiConfidence = 1.0,
            SHLabsScanned = true,
            SHLabsProbability = 100.0,
            SHLabsConfidence = 100.0
        };
        Assert(everything.AiCombinedConfidence <= 100.0 && everything.AiCombinedConfidence >= 0.0,
            $"confidence must stay in 0-100, got {everything.AiCombinedConfidence}");

        CheckByteScanWindows();

        static void Assert(bool ok, string what)
        {
            if (!ok) throw new Exception("AiScoringSelfCheck failed: " + what);
        }
    }

    /// <summary>
    /// Verifies the raw-byte scanner covers the whole file, not just its ends. A marker sitting in
    /// the body used to be invisible: the scanner read the first and last 64KB only, and a file
    /// between one and two window-widths had its tail skipped entirely.
    /// </summary>
    private static void CheckByteScanWindows()
    {
        string dir = Path.Combine(Path.GetTempPath(), "aa-aiscan-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);
        try
        {
            // A marker exactly halfway into a 1MB file — outside both the head and tail windows.
            Assert(DetectsMarkerAt(dir, "middle.bin", totalBytes: 1024 * 1024, markerOffset: 512 * 1024),
                "a marker in the middle of the file must be found");

            // Inside the widened 128KB head but outside the old 64KB one.
            Assert(DetectsMarkerAt(dir, "deephead.bin", totalBytes: 1024 * 1024, markerOffset: 100 * 1024),
                "a marker 100KB in must be found");

            // A file larger than the head window but smaller than two of them — the region past
            // the head must still be covered rather than being skipped as it once was.
            Assert(DetectsMarkerAt(dir, "gap.bin", totalBytes: 200 * 1024, markerOffset: 150 * 1024),
                "a marker past the head window in a 200KB file must be found");

            // The ends still work.
            Assert(DetectsMarkerAt(dir, "head.bin", totalBytes: 512 * 1024, markerOffset: 1024),
                "a marker near the start must be found");
            Assert(DetectsMarkerAt(dir, "tail.bin", totalBytes: 512 * 1024, markerOffset: 500 * 1024),
                "a marker near the end must be found");

            // A clean file must stay clean — the scan must not invent markers.
            string clean = Path.Combine(dir, "clean.bin");
            File.WriteAllBytes(clean, new byte[256 * 1024]);
            Assert(!AiWatermarkDetector.Detect(clean).IsAiDetected, "a file with no markers must not be flagged");
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { }
        }

        static bool DetectsMarkerAt(string dir, string name, int totalBytes, int markerOffset)
        {
            var marker = Encoding.ASCII.GetBytes("audioseal");
            var buffer = new byte[totalBytes];
            Array.Copy(marker, 0, buffer, markerOffset, marker.Length);
            string path = Path.Combine(dir, name);
            File.WriteAllBytes(path, buffer);
            return AiWatermarkDetector.Detect(path).IsAiDetected;
        }

        static void Assert(bool ok, string what)
        {
            if (!ok) throw new Exception("AiScoringSelfCheck failed: " + what);
        }
    }
}
