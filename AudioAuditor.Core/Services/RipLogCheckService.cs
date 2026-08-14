using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using AudioQualityChecker.Models;

namespace AudioQualityChecker.Services;

/// <summary>
/// Checks CD ripping logs (EAC / XLD / whipper) by invoking the bundled <c>cambia</c> binary as a
/// separate process. cambia (MIT, github.com/arg274/cambia) parses the log and scores it with the
/// OPS deduction model; we run it with <c>-p &lt;log&gt;</c>, read the JSON it prints to stdout, and
/// project it into a <see cref="RipLogResult"/>. Running it as an external program (never linking it)
/// keeps this project's license clean — same pattern as <see cref="AudioConversionService"/>.
/// </summary>
public sealed class RipLogCheckService
{
    private static string? _cachedCambiaPath;
    private static bool _searched;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true
    };

    /// <summary>Locates cambia: bundled beside the app first, then on PATH. Null if unavailable.</summary>
    public static string? FindCambia()
    {
        if (_searched) return _cachedCambiaPath;
        _searched = true;

        string exe = OperatingSystem.IsWindows() ? "cambia.exe" : "cambia";
        var candidates = new List<string>();
        foreach (var baseDir in AppPaths.SidecarSearchDirectories)
        {
            candidates.Add(Path.Combine(baseDir, exe));
            candidates.Add(Path.Combine(baseDir, "cambia", exe));
            candidates.Add(Path.Combine(baseDir, "third-party", "cambia", exe));
        }

        foreach (var candidate in candidates)
        {
            if (File.Exists(candidate))
            {
                _cachedCambiaPath = candidate;
                return _cachedCambiaPath;
            }
        }

        if (CanRun(exe)) _cachedCambiaPath = exe;
        return _cachedCambiaPath;
    }

    public static bool IsAvailable => FindCambia() != null;

    private static bool CanRun(string fileName)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = "--version",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            });
            if (process == null) return false;
            process.WaitForExit(4000);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Rip-log file extensions cambia can recognise. EAC/whipper write <c>.log</c>; some setups
    /// keep the EAC log as <c>.txt</c>.
    /// </summary>
    private static readonly string[] LogExtensions = { ".log", ".txt" };

    /// <summary>Finds candidate rip logs sitting in <paramref name="dir"/> (non-recursive).</summary>
    public static IEnumerable<string> FindLogsInFolder(string dir)
    {
        if (string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir)) yield break;
        IEnumerable<string> files;
        try { files = Directory.EnumerateFiles(dir); }
        catch { yield break; }
        foreach (var f in files)
            if (LogExtensions.Contains(Path.GetExtension(f).ToLowerInvariant()))
                yield return f;
    }

    /// <summary>Runs cambia against a single log file and projects the result.</summary>
    public static async Task<RipLogResult> CheckLogAsync(string logPath, CancellationToken ct = default)
    {
        string? cambia = FindCambia();
        if (cambia == null) return RipLogResult.MissingBinary(logPath);

        if (string.IsNullOrWhiteSpace(logPath) || !File.Exists(logPath))
            return RipLogResult.Unsupported(logPath, "Log file not found.");

        string stdout;
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = cambia,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            psi.ArgumentList.Add("-p");
            psi.ArgumentList.Add(logPath);

            using var process = new Process { StartInfo = psi };
            process.Start();
            // Drain both pipes concurrently — leaving stderr unread risks its buffer filling up and
            // blocking cambia forever on a log that produces a lot of diagnostics.
            var stdoutTask = process.StandardOutput.ReadToEndAsync(ct);
            var stderrTask = process.StandardError.ReadToEndAsync(ct);
            await Task.WhenAll(stdoutTask, stderrTask);
            stdout = await stdoutTask;
            await process.WaitForExitAsync(ct);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return RipLogResult.Unsupported(logPath, $"cambia failed to run: {ex.Message}");
        }

        // cambia prints nothing for logs it can't recognise/parse.
        if (string.IsNullOrWhiteSpace(stdout))
            return RipLogResult.Unsupported(logPath, "Unrecognised or unsupported log format.");

        return Parse(stdout, logPath);
    }

    /// <summary>Finds the first parseable log in a folder and checks it. Null result if none.</summary>
    public static async Task<RipLogResult?> CheckFolderAsync(string dir, CancellationToken ct = default)
    {
        foreach (var log in FindLogsInFolder(dir))
        {
            var result = await CheckLogAsync(log, ct);
            if (result.IsParsed || !result.BinaryAvailable) return result;
        }
        return null;
    }

    /// <summary>
    /// Checks each distinct folder once and returns a folder → result map (parsed results only).
    /// Used by the scan auto-detect to stamp every file in a folder from a single cambia run.
    /// Returns an empty map when cambia is unavailable.
    /// </summary>
    public static async Task<Dictionary<string, RipLogResult>> CheckFoldersAsync(
        IEnumerable<string> folders, CancellationToken ct = default)
    {
        var map = new Dictionary<string, RipLogResult>(StringComparer.OrdinalIgnoreCase);
        if (FindCambia() == null) return map;

        foreach (var folder in folders.Where(f => !string.IsNullOrWhiteSpace(f))
                                       .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            ct.ThrowIfCancellationRequested();
            var result = await CheckFolderAsync(folder, ct);
            if (result is { IsParsed: true }) map[folder] = result;
        }
        return map;
    }

    private static RipLogResult Parse(string json, string logPath)
    {
        try
        {
            var dto = JsonSerializer.Deserialize<CambiaResponse>(json, JsonOpts);
            var eval = dto?.EvaluationCombined?.FirstOrDefault();
            if (eval == null)
                return RipLogResult.Unsupported(logPath, "Log parsed but no evaluation was produced.");

            int score = int.TryParse(eval.CombinedScore, out var s) ? s : -1;
            var log0 = dto?.Parsed?.ParsedLogs?.FirstOrDefault();

            var deductions = (eval.Evaluations ?? new())
                .SelectMany(e => e.EvaluationUnits ?? new())
                .Where(u => u.Data != null)
                .Select(u => new RipLogDeduction
                {
                    Message = u.Data!.Message ?? "",
                    Class = u.Data.Class ?? "Neutral",
                    Score = u.UnitScore ?? ""
                })
                .ToList();

            return new RipLogResult
            {
                IsParsed = true,
                Score = score,
                Ripper = log0?.Ripper ?? "",
                RipperVersion = log0?.RipperVersion ?? "",
                Drive = log0?.Drive ?? "",
                SourceFile = logPath,
                Deductions = deductions
            };
        }
        catch (Exception ex)
        {
            return RipLogResult.Unsupported(logPath, $"Could not read cambia output: {ex.Message}");
        }
    }

    // ---- JSON DTOs mirroring cambia's CambiaResponse (only the fields we use) ----

    private sealed class CambiaResponse
    {
        public ParsedCombined? Parsed { get; set; }
        public List<EvaluationCombinedDto>? EvaluationCombined { get; set; }
    }

    private sealed class ParsedCombined
    {
        public List<ParsedLogDto>? ParsedLogs { get; set; }
    }

    private sealed class ParsedLogDto
    {
        public string? Ripper { get; set; }
        public string? RipperVersion { get; set; }
        public string? Drive { get; set; }
    }

    private sealed class EvaluationCombinedDto
    {
        public string? CombinedScore { get; set; }
        public List<EvaluationDto>? Evaluations { get; set; }
    }

    private sealed class EvaluationDto
    {
        public List<EvaluationUnitDto>? EvaluationUnits { get; set; }
    }

    private sealed class EvaluationUnitDto
    {
        public string? UnitScore { get; set; }
        public EvaluationUnitData? Data { get; set; }
    }

    private sealed class EvaluationUnitData
    {
        public string? Message { get; set; }
        public string? Class { get; set; }
    }
}
