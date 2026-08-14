using System;
using System.Collections.Generic;

namespace AudioQualityChecker.Services;

/// <summary>
/// Runs the assert-based self-checks that individual services expose. These guard the logic that is
/// easy to break silently and expensive to notice — tag-comment merging, junk detection, case-only
/// file renames, and the ffmpeg decode round-trip — without pulling a test framework into the
/// project. Invoked by the CLI's <c>selfcheck</c> command; not run at app startup (the rename and
/// ffmpeg checks touch the filesystem).
/// </summary>
public static class SelfChecks
{
    /// <summary>Runs every check. Returns one message per failure; empty means everything passed.</summary>
    public static IReadOnlyList<string> RunAll()
    {
        var failures = new List<string>();
        Run("AnalysisTagWriteService", AnalysisTagWriteService.SelfCheck, failures);
        Run("SourceJunkCleaner", SourceJunkCleaner.SelfCheck, failures);
        Run("FileRenamer", FileRenamer.SelfCheck, failures);
        Run("AiScoring", AiScoringSelfCheck.Run, failures);
        Run("MetadataEnrichmentService", MetadataEnrichmentService.SelfCheck, failures);
        Run("BatchFieldEditService", BatchFieldEditService.SelfCheck, failures);
        Run("AudioConversionService", AudioConversionService.SelfCheck, failures);
        Run("FfmpegDecoder", FfmpegDecoder.SelfCheck, failures);
        return failures;
    }

    private static void Run(string name, Action check, List<string> failures)
    {
        try { check(); }
        catch (Exception ex) { failures.Add($"{name}: {ex.Message}"); }
    }
}
