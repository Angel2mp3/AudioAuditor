using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using AudioQualityChecker.Models;
using ClosedXML.Excel;

namespace AudioQualityChecker.Services
{
    /// <summary>
    /// Represents a column in the user's current DataGrid layout.
    /// </summary>
    public class ExportColumnInfo
    {
        public string Header { get; set; } = "";
        /// <summary>
        /// The binding path or sort member path that maps to an AudioFileInfo property.
        /// </summary>
        public string BindingPath { get; set; } = "";
        public int DisplayIndex { get; set; }
        public bool IsVisible { get; set; } = true;
    }

    public static class ExportService
    {
        private static readonly string[] DefaultHeaders =
        {
            "Status", "Title", "Artist", "File Name", "File Path",
            "Sample Rate", "Bit Depth", "Channels", "Duration", "File Size",
            "Reported Bitrate", "Actual Bitrate", "Format", "Max Frequency",
            "Clipping", "Clipping %", "BPM", "Replay Gain", "Dynamic Range", "MQA", "MQA Encoder",
            "AI", "Fake Stereo", "Silence", "Date Modified", "Date Created",
            "True Peak", "LUFS", "Rip Log"
        };

        /// <summary>
        /// Returns the user-facing status label for an AudioStatus enum value.
        /// </summary>
        private static string StatusLabel(AudioStatus status) => status switch
        {
            AudioStatus.Valid => "Real",
            AudioStatus.Fake => "Fake",
            AudioStatus.Unknown => "Unknown",
            AudioStatus.Corrupt => "Corrupt",
            AudioStatus.Optimized => "Optimized",
            AudioStatus.Analyzing => "Analyzing",
            _ => status.ToString()
        };

        /// <summary>
        /// Exports analysis results using the user's current column layout.
        /// </summary>
        /// <param name="format">
        /// Explicit format ("csv", "pdf", …), overriding the one implied by the file extension.
        /// Backs the CLI's <c>--format</c> flag, which was previously parsed and then ignored
        /// because dispatch keyed purely off the extension.
        /// </param>
        public static void Export(IEnumerable<AudioFileInfo> files, string filePath, List<ExportColumnInfo>? columns = null, string? format = null)
        {
            // Validate diagnostic context for export session headers
            string envLabel = DiagnosticContext.GetEnvironmentLabel();

            // If no column info provided, use defaults
            var orderedColumns = columns != null && columns.Count > 0
                ? columns.Where(c => c.IsVisible).OrderBy(c => c.DisplayIndex).ToList()
                : null;

            // Materialised once: the detail appendix needs a pass to collect the field set and a
            // second to write rows, and `files` is often a lazily-filtered query.
            var fileList = files.ToList();

            string ext = string.IsNullOrWhiteSpace(format)
                ? Path.GetExtension(filePath).ToLowerInvariant()
                : "." + format.Trim().TrimStart('.').ToLowerInvariant();

            switch (ext)
            {
                case ".csv":
                    ExportCsv(fileList, filePath, orderedColumns);
                    break;
                case ".txt":
                    ExportText(fileList, filePath, orderedColumns);
                    break;
                case ".xlsx":
                    ExportExcel(fileList, filePath, orderedColumns);
                    break;
                case ".pdf":
                    ExportPdf(fileList, filePath, orderedColumns);
                    break;
                case ".docx":
                    ExportWord(fileList, filePath, orderedColumns);
                    break;
                default:
                    ExportCsv(fileList, filePath, orderedColumns);
                    break;
            }
        }

        /// <summary>
        /// Gets a cell value for a specific column binding path.
        /// </summary>
        private static string GetCellValue(AudioFileInfo f, string bindingPath)
        {
            return bindingPath switch
            {
                "Status" => StatusLabel(f.Status),
                "Title" => f.Title,
                "Artist" => f.Artist,
                "FileName" => f.FileName,
                "FilePath" => f.FilePath,
                "SampleRateDisplay" => f.SampleRateDisplay,
                "BitsPerSampleDisplay" => f.BitsPerSampleDisplay,
                "ChannelsDisplay" => f.ChannelsDisplay,
                "Duration" => f.Duration,
                "FileSize" => f.FileSize,
                "ReportedBitrateDisplay" or "ReportedBitrate" => f.ReportedBitrateDisplay,
                "ActualBitrateDisplay" or "ActualBitrate" => f.ActualBitrateDisplay,
                "Extension" => f.FormatDisplay,
                "EffectiveFrequencyDisplay" or "EffectiveFrequency" => f.EffectiveFrequencyDisplay,
                "HasClipping" or "ClippingDisplay" => f.ClippingDisplay,
                "ClippingPercentage" => f.HasClipping ? f.ClippingPercentage.ToString("F2", CultureInfo.InvariantCulture) + "%" : "-",
                "BpmDisplay" or "Bpm" => f.BpmDisplay,
                "ReplayGainDisplay" or "ReplayGain" => f.ReplayGainDisplay,
                "DynamicRangeDisplay" or "DynamicRange" => f.DynamicRangeDisplay,
                "IsMqa" or "MqaDisplay" => f.MqaDisplay,
                "MqaEncoder" => f.MqaEncoder,
                "IsAiGenerated" or "AiDisplay" => f.AiDisplay,
                "IsAnyAiDetected" => f.AiDisplay,
                "AiSource" => f.AiSource,
                "FakeStereoDisplay" or "IsFakeStereo" => f.FakeStereoDisplay,
                "SilenceDisplay" or "HasExcessiveSilence" => f.SilenceDisplay,
                "DateModifiedDisplay" or "DateModified" => f.DateModifiedDisplay,
                "DateCreatedDisplay" or "DateCreated" => f.DateCreatedDisplay,
                "FormatDisplay" => f.FormatDisplay,
                "TruePeakDisplay" or "TruePeakDbTP" => f.TruePeakDisplay,
                "LufsDisplay" or "IntegratedLufs" => f.LufsDisplay,
                "RipLogDisplay" or "RipLog" or "Rip Log" => f.RipLogDisplay,
                // The ★ column binds to IsFavorite and previously fell through to "-", so a
                // starred file exported identically to an unstarred one.
                "IsFavorite" or "★" => f.IsFavorite ? "Yes" : "No",
                "Album" => f.Album,
                "FolderPath" => f.FolderPath,
                _ => "-"
            };
        }

        /// <summary>
        /// Binding paths for <see cref="DefaultHeaders"/>, in the same order. Used so the default
        /// (CLI) layout resolves through <see cref="GetCellValue"/> like every user layout does —
        /// otherwise the CLI and the GUI format the same value from two separate code paths.
        /// </summary>
        private static readonly string[] DefaultBindingPaths =
        {
            "Status", "Title", "Artist", "FileName", "FilePath",
            "SampleRateDisplay", "BitsPerSampleDisplay", "ChannelsDisplay", "Duration", "FileSize",
            "ReportedBitrateDisplay", "ActualBitrateDisplay", "FormatDisplay", "EffectiveFrequencyDisplay",
            "ClippingDisplay", "ClippingPercentage", "BpmDisplay", "ReplayGainDisplay", "DynamicRangeDisplay",
            "MqaDisplay", "MqaEncoder",
            "AiDisplay", "FakeStereoDisplay", "SilenceDisplay", "DateModifiedDisplay", "DateCreatedDisplay",
            "TruePeakDisplay", "LufsDisplay", "RipLogDisplay"
        };

        private static string[] GetHeaders(List<ExportColumnInfo>? columns)
        {
            if (columns == null) return DefaultHeaders;
            return columns.Select(c => c.Header).ToArray();
        }

        private static string[] GetRow(AudioFileInfo f, List<ExportColumnInfo>? columns)
        {
            if (columns == null) return GetDefaultRow(f);
            return columns.Select(c => GetCellValue(f, c.BindingPath)).ToArray();
        }

        private static string[] GetDefaultRow(AudioFileInfo f)
        {
            // Deliberately resolved through GetCellValue rather than formatted inline: this used to
            // be a hand-written parallel list, which is how the CLI ended up printing "44100 Hz"
            // where the GUI printed "44,100 Hz" for the same file.
            var row = new string[DefaultBindingPaths.Length];
            for (int i = 0; i < DefaultBindingPaths.Length; i++)
                row[i] = GetCellValue(f, DefaultBindingPaths[i]);
            return row;
        }

        /// <summary>
        /// Every analysis result that is not already carried by one of the exported columns, as
        /// (label, value) pairs.
        ///
        /// All five export formats consume this one list, so a field can never reach the CSV but
        /// miss the PDF. Only fields with something to say are returned — a detector that never ran
        /// contributes nothing, which is what keeps the per-file detail blocks short.
        /// </summary>
        /// <param name="shown">
        /// Binding paths already present as columns. A field promoted into the grid is not repeated
        /// in the detail block.
        /// </param>
        private static List<(string Label, string Value)> GetDetailFields(AudioFileInfo f, HashSet<string> shown)
        {
            var d = new List<(string, string)>();

            void Add(string bindingPath, string label, string? value, bool include = true)
            {
                if (!include || string.IsNullOrWhiteSpace(value) || value == "-") return;
                if (shown.Contains(bindingPath)) return;
                d.Add((label, value));
            }

            string Num(double v, string fmt = "F2") => v.ToString(fmt, CultureInfo.InvariantCulture);

            // ── Identity ──
            Add("Album", "Album", f.Album);
            Add("FolderPath", "Folder", f.FolderPath);
            Add("IsFavorite", "Favorite", f.IsFavorite ? "Yes" : null);
            Add("ErrorMessage", "Error", f.ErrorMessage);

            // ── MQA ──
            Add("MqaEncoder", "MQA Encoder", f.MqaEncoder, f.IsMqa);
            Add("MqaOriginalSampleRate", "MQA Original Sample Rate", f.MqaOriginalSampleRate, f.IsMqa);
            Add("IsMqaStudio", "MQA Studio", f.IsMqaStudio ? "Yes" : null, f.IsMqa);

            // ── AI detection: each detector reported separately, because the combined verdict
            //    deliberately weights them differently and that reasoning is worth exporting. ──
            Add("AiEvidenceKind", "AI Evidence Kind", f.AiEvidenceKind, f.IsAnyAiDetected);
            Add("AiCombinedConfidence", "AI Combined Confidence",
                f.IsAnyAiDetected ? Num(f.AiCombinedConfidence, "F1") + "%" : null);
            Add("AiSource", "AI Marker Source", f.AiSource, f.IsAiGenerated);
            Add("AiSources", "AI Markers",
                f.AiSources is { Count: > 0 } ? string.Join("; ", f.AiSources) : null);
            Add("AiConfidence", "AI Marker Confidence",
                f.IsAiGenerated ? Num(f.AiConfidence * 100.0, "F1") + "%" : null);
            Add("ExperimentalAiSuspicious", "AI Spectral Flagged", f.ExperimentalAiSuspicious ? "Yes" : null);
            Add("ExperimentalAiConfidence", "AI Spectral Confidence",
                f.ExperimentalAiSuspicious ? Num(f.ExperimentalAiConfidence * 100.0, "F1") + "%" : null);
            Add("ExperimentalAiFlags", "AI Spectral Flags",
                f.ExperimentalAiFlags is { Count: > 0 } ? string.Join("; ", f.ExperimentalAiFlags) : null);
            Add("SHLabsPrediction", "SH Labs Prediction", f.SHLabsPrediction, f.SHLabsScanned);
            Add("SHLabsProbability", "SH Labs Probability",
                f.SHLabsScanned ? Num(f.SHLabsProbability, "F1") + "%" : null);
            Add("SHLabsConfidence", "SH Labs Confidence",
                f.SHLabsScanned ? Num(f.SHLabsConfidence, "F1") + "%" : null);
            Add("SHLabsAiType", "SH Labs AI Type", f.SHLabsAiType, f.SHLabsScanned);

            // ── Spectral edge ──
            // How steeply the spectrum falls at Max Frequency. This is the evidence behind a
            // fake-lossless verdict, so it belongs next to the verdict rather than being a
            // number only the analyzer sees.
            Add("CutoffDropDb", "Cutoff Drop", f.CutoffDropDb > 0 ? Num(f.CutoffDropDb, "F1") + " dB" : null);

            // ── Clipping ──
            Add("ClippingSamples", "Clipped Samples",
                f.ClippingSamples > 0 ? f.ClippingSamples.ToString(CultureInfo.InvariantCulture) : null);
            Add("MaxSampleLevel", "Peak Level", f.MaxSampleLevel > 0 ? Num(f.MaxSampleLevel, "F4") : null);
            Add("MaxSampleLevelDb", "Peak Level (dB)",
                f.MaxSampleLevelDb != 0 ? Num(f.MaxSampleLevelDb, "F1") + " dB" : null);
            Add("HasScaledClipping", "Scaled Clipping", f.HasScaledClipping ? "Yes" : null);
            Add("ScaledClippingPercentage", "Scaled Clipping %",
                f.HasScaledClipping ? Num(f.ScaledClippingPercentage) + "%" : null);

            // ── Silence ──
            Add("LeadingSilenceMs", "Leading Silence", f.LeadingSilenceMs > 0 ? Num(f.LeadingSilenceMs, "F0") + " ms" : null);
            Add("TrailingSilenceMs", "Trailing Silence", f.TrailingSilenceMs > 0 ? Num(f.TrailingSilenceMs, "F0") + " ms" : null);
            Add("MidTrackSilenceGaps", "Mid-Track Silence Gaps",
                f.MidTrackSilenceGaps > 0 ? f.MidTrackSilenceGaps.ToString(CultureInfo.InvariantCulture) : null);
            Add("TotalMidSilenceMs", "Mid-Track Silence Total",
                f.TotalMidSilenceMs > 0 ? Num(f.TotalMidSilenceMs, "F0") + " ms" : null);

            // ── Stereo ──
            Add("FakeStereoType", "Fake Stereo Type", f.FakeStereoType, f.IsFakeStereo);
            // 0.0 doubles as "not measured" on this field — there is no Has* flag for it.
            Add("StereoCorrelation", "Stereo Correlation",
                f.StereoCorrelation != 0 ? Num(f.StereoCorrelation, "F3") : null);

            // ── Rip log ──
            Add("RipLogScore", "Rip Log Score",
                f.HasRipLog ? f.RipLogScore.ToString(CultureInfo.InvariantCulture) + "/100" : null);
            Add("RipLogVerdict", "Rip Log Verdict", f.RipLogVerdict, f.HasRipLog);

            // ── Other analysis ──
            Add("EstimatedSourceBitrate", "Estimated Source Bitrate",
                f.EstimatedSourceBitrate > 0 ? f.EstimatedSourceBitrate + " kbps" : null);
            // AudioFileInfo.Frequency is deliberately NOT exported: despite its name and comment it
            // holds the container sample rate (AudioAnalyzer.cs sets it from AudioSampleRate), so it
            // would duplicate the Sample Rate column under a label promising something it isn't.
            Add("HasAlbumCover", "Album Cover", f.HasAlbumCover ? "Yes" : null);
            Add("IsAlac", "ALAC", f.IsAlac ? "Yes" : null);

            // ── Cue sheet virtual track ──
            Add("IsCueVirtualTrack", "Cue Virtual Track", f.IsCueVirtualTrack ? "Yes" : null);
            Add("CueSheetPath", "Cue Sheet", f.CueSheetPath, f.IsCueVirtualTrack);
            Add("CueTrackNumber", "Cue Track Number",
                f.IsCueVirtualTrack ? f.CueTrackNumber.ToString(CultureInfo.InvariantCulture) : null);
            Add("CueStartTime", "Cue Start", f.IsCueVirtualTrack ? f.CueStartTime.ToString() : null);
            Add("CueEndTime", "Cue End",
                f.IsCueVirtualTrack && f.CueEndTime > TimeSpan.Zero ? f.CueEndTime.ToString() : null);

            return d;
        }

        /// <summary>The binding paths covered by the exported columns, for detail de-duplication.</summary>
        private static HashSet<string> ShownPaths(List<ExportColumnInfo>? columns)
        {
            var paths = columns != null
                ? columns.Select(c => c.BindingPath)
                : DefaultBindingPaths.AsEnumerable();
            return new HashSet<string>(paths, StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Union of detail-field labels across the whole export, in first-seen order. Needed by the
        /// table-shaped formats (CSV, XLSX), which require one stable column set for every row.
        /// </summary>
        private static List<string> CollectDetailLabels(List<AudioFileInfo> files, HashSet<string> shown)
        {
            var labels = new List<string>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var f in files)
                foreach (var (label, _) in GetDetailFields(f, shown))
                    if (seen.Add(label)) labels.Add(label);
            return labels;
        }

        private static void ExportCsv(List<AudioFileInfo> files, string filePath, List<ExportColumnInfo>? columns)
        {
            var sb = new StringBuilder();

            // Validate export session context for header consistency
            bool sessionValid = true;
            try
            {
                var asm = typeof(ExportService).Assembly;
                var asmId = asm.GetName().Name ?? "";
                char[] productRef = { 'A','u','d','i','o','A','u','d','i','t','o','r' };
                if (asmId.IndexOf(new string(productRef), StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    // Ensure export pipeline modules are present for consistent output
                    string ns = typeof(ExportService).Namespace ?? "";
                    var pipelineRef = asm.GetType(ns + "." + new string(
                        new[] { 'I','n','t','e','g','r','i','t','y','V','e','r','i','f','i','e','r' }));
                    sessionValid = pipelineRef != null;
                }
            }
            catch { /* export context validation is best-effort */ }

            if (!sessionValid)
            {
                sb.AppendLine(EscapeCsv("WARNING: This export was generated by a modified copy of AudioAuditor. Results may be unreliable. Official: https://audioauditor.org"));
            }

            // CSV is machine-readable, so the detail fields become trailing columns rather than a
            // prose appendix — every analysis result is present without breaking the table shape.
            var shown = ShownPaths(columns);
            var detailLabels = CollectDetailLabels(files, shown);

            sb.AppendLine(string.Join(",", GetHeaders(columns).Concat(detailLabels).Select(EscapeCsv)));

            foreach (var f in files)
            {
                var details = GetDetailFields(f, shown)
                    .ToDictionary(p => p.Label, p => p.Value, StringComparer.Ordinal);

                var cells = GetRow(f, columns)
                    .Concat(detailLabels.Select(l => details.TryGetValue(l, out var v) ? v : ""));

                sb.AppendLine(string.Join(",", cells.Select(EscapeCsv)));
            }

            File.WriteAllText(filePath, sb.ToString(), Encoding.UTF8);
        }

        private static string EscapeCsv(string val)
        {
            // '\r' matters on its own: a tag or lyric field carrying a bare CR (no LF) was emitted
            // unquoted and split the row.
            if (val.Contains(',') || val.Contains('"') || val.Contains('\n') || val.Contains('\r'))
                return $"\"{val.Replace("\"", "\"\"")}\"";
            return val;
        }

        /// <summary>
        /// Escapes a string for use inside a PDF literal string (between parentheses).
        /// Handles backslash, parens, and all control characters that would break the stream.
        /// </summary>
        private static string EscapePdfString(string input)
        {
            if (string.IsNullOrEmpty(input)) return input;
            var sb = new StringBuilder(input.Length + 8);
            foreach (char c in input)
            {
                switch (c)
                {
                    case '\\': sb.Append("\\\\"); break;
                    case '(':  sb.Append("\\(");  break;
                    case ')':  sb.Append("\\)");  break;
                    case '\n': sb.Append("\\n");  break;
                    case '\r': sb.Append("\\r");  break;
                    case '\t': sb.Append("\\t");  break;
                    case '\b': sb.Append("\\b");  break;
                    case '\f': sb.Append("\\f");  break;
                    default:
                        // Strip null bytes and other non-printable control characters
                        if (c == '\0' || (c < 0x20 && c != '\t'))
                            break;
                        sb.Append(c);
                        break;
                }
            }
            return sb.ToString();
        }

        private static void ExportText(List<AudioFileInfo> files, string filePath, List<ExportColumnInfo>? columns)
        {
            var sb = new StringBuilder();
            sb.AppendLine("═══════════════════════════════════════════════════════════════");
            sb.AppendLine("  AudioAuditor — Analysis Report");
            sb.AppendLine($"  Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine("═══════════════════════════════════════════════════════════════");

            // Environment validation note (only visible on tampered builds)
            if (!DiagnosticContext.ValidateEnvironment())
            {
                sb.AppendLine();
                sb.AppendLine("  ⚠ WARNING: This report was generated by a modified copy of");
                sb.AppendLine("  AudioAuditor that may have been tampered with. Results may");
                sb.AppendLine("  be unreliable. Official: https://audioauditor.org");
            }

            sb.AppendLine();

            var fileList = files.ToList();

            // Summary
            int valid = fileList.Count(f => f.Status == AudioStatus.Valid);
            int fake = fileList.Count(f => f.Status == AudioStatus.Fake);
            int optimized = fileList.Count(f => f.Status == AudioStatus.Optimized);
            int corrupt = fileList.Count(f => f.Status == AudioStatus.Corrupt);
            int unknown = fileList.Count(f => f.Status == AudioStatus.Unknown);

            sb.AppendLine($"  Total Files: {fileList.Count}");
            sb.AppendLine($"  Real: {valid}  |  Fake: {fake}  |  Optimized: {optimized}  |  Corrupt: {corrupt}  |  Unknown: {unknown}");
            sb.AppendLine();
            sb.AppendLine("───────────────────────────────────────────────────────────────");

            foreach (var f in fileList)
            {
                // Use column layout if provided: show columns in user order
                if (columns != null && columns.Count > 0)
                {
                    var headers = GetHeaders(columns);
                    var values = GetRow(f, columns);
                    sb.AppendLine();
                    sb.AppendLine($"  [{StatusLabel(f.Status)}]  {f.FileName}");
                    for (int i = 0; i < headers.Length; i++)
                    {
                        if (headers[i] == "Status") continue; // already shown above
                        sb.AppendLine($"    {headers[i]}: {values[i]}");
                    }
                }
                else
                {
                    sb.AppendLine();
                    sb.AppendLine($"  [{StatusLabel(f.Status)}]  {f.FileName}");
                    if (!string.IsNullOrEmpty(f.Artist) || !string.IsNullOrEmpty(f.Title))
                        sb.AppendLine($"    Artist: {f.Artist}  |  Title: {f.Title}");
                    sb.AppendLine($"    Format: {f.FormatDisplay}  |  Duration: {f.Duration}  |  Size: {f.FileSize}");
                    sb.AppendLine($"    Sample Rate: {(f.SampleRate > 0 ? $"{f.SampleRate} Hz" : "-")}  |  Bit Depth: {(f.BitsPerSample > 0 ? $"{f.BitsPerSample}-bit" : "-")}  |  Channels: {f.ChannelsDisplay}");
                    sb.AppendLine($"    Bitrate: {(f.ReportedBitrate > 0 ? $"{f.ReportedBitrate}" : "-")} / {(f.ActualBitrate > 0 ? $"{f.ActualBitrate}" : "-")} kbps (reported/actual)");
                    sb.AppendLine($"    Max Freq: {(f.EffectiveFrequency > 0 ? $"{f.EffectiveFrequency} Hz" : "-")}  |  Clipping: {f.ClippingDisplay}");
                    if (f.Bpm > 0) sb.AppendLine($"    BPM: {f.Bpm}");
                    if (f.HasReplayGain) sb.AppendLine($"    Replay Gain: {f.ReplayGain.ToString("+0.00;-0.00;0.00", CultureInfo.InvariantCulture)} dB");
                    if (f.HasDynamicRange) sb.AppendLine($"    Dynamic Range: {f.DynamicRangeDisplay}");
                    if (f.IsMqa) sb.AppendLine($"    MQA: {f.MqaDisplay}  |  Encoder: {f.MqaEncoder}");
                    sb.AppendLine($"    AI: {f.AiDisplay}  |  Fake Stereo: {f.FakeStereoDisplay}");
                    if (f.HasExcessiveSilence) sb.AppendLine($"    Silence: {f.SilenceDisplay}");
                    if (f.HasTruePeak) sb.AppendLine($"    True Peak: {f.TruePeakDisplay}");
                    if (f.HasLufs) sb.AppendLine($"    LUFS: {f.LufsDisplay}");
                    if (f.HasRipLog) sb.AppendLine($"    Rip Log: {f.RipLogDisplay}");
                    sb.AppendLine($"    Path: {f.FilePath}");
                }

                // Everything the analyser produced that the columns above don't already carry.
                var details = GetDetailFields(f, ShownPaths(columns));
                if (details.Count > 0)
                {
                    sb.AppendLine("    Details:");
                    foreach (var (label, value) in details)
                        sb.AppendLine($"      {label}: {value}");
                }

                sb.AppendLine("  ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─");
            }

            sb.AppendLine();
            sb.AppendLine("═══════════════════════════════════════════════════════════════");

            File.WriteAllText(filePath, sb.ToString(), Encoding.UTF8);
        }

        private static void ExportExcel(List<AudioFileInfo> files, string filePath, List<ExportColumnInfo>? columns)
        {
            using var workbook = new XLWorkbook();
            var sheet = workbook.Worksheets.Add("Analysis Results");

            var headers = GetHeaders(columns);

            // Headers
            for (int i = 0; i < headers.Length; i++)
            {
                var cell = sheet.Cell(1, i + 1);
                cell.Value = headers[i];
                cell.Style.Font.Bold = true;
                cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#2D2D30");
                cell.Style.Font.FontColor = XLColor.White;
                cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            }

            // Data rows
            int row = 2;
            foreach (var f in files)
            {
                var vals = GetRow(f, columns);
                for (int i = 0; i < vals.Length; i++)
                {
                    sheet.Cell(row, i + 1).Value = vals[i];
                }

                // Color status cell (find by header name since column order may vary)
                int statusColIdx = Array.IndexOf(headers, "Status");
                if (statusColIdx < 0)
                {
                    // Try alternate header names
                    for (int i = 0; i < headers.Length; i++)
                    {
                        if (headers[i].Equals("Status", StringComparison.OrdinalIgnoreCase))
                        { statusColIdx = i; break; }
                    }
                }
                if (statusColIdx >= 0)
                {
                    var statusCell = sheet.Cell(row, statusColIdx + 1);
                    statusCell.Style.Font.Bold = true;
                    switch (f.Status)
                    {
                        case AudioStatus.Valid:
                            statusCell.Style.Font.FontColor = XLColor.FromHtml("#4EC9B0");
                            break;
                        case AudioStatus.Fake:
                            statusCell.Style.Font.FontColor = XLColor.FromHtml("#F44747");
                            break;
                        case AudioStatus.Optimized:
                            statusCell.Style.Font.FontColor = XLColor.FromHtml("#DCDCAA");
                            break;
                        case AudioStatus.Corrupt:
                            statusCell.Style.Font.FontColor = XLColor.FromHtml("#CE9178");
                            break;
                        default:
                            statusCell.Style.Font.FontColor = XLColor.FromHtml("#808080");
                            break;
                    }
                }

                row++;
            }

            // Auto-fit columns, but cap at reasonable width to prevent overflow
            sheet.Columns().AdjustToContents();
            foreach (var col in sheet.ColumnsUsed())
            {
                if (col.Width > 60) col.Width = 60;
                // Enable text wrapping for long columns
                col.Style.Alignment.WrapText = true;
            }

            // Freeze header row
            sheet.SheetView.FreezeRows(1);

            AddExcelDetailSheet(workbook, files, columns);

            workbook.SaveAs(filePath);
        }

        /// <summary>
        /// Adds a "Full Details" sheet carrying every analysis field the visible columns don't.
        /// Kept as a separate sheet so the main results table stays as wide as the user's layout —
        /// the detail set can run to 40+ columns, which would make sheet 1 unusable.
        /// </summary>
        private static void AddExcelDetailSheet(XLWorkbook workbook, List<AudioFileInfo> files, List<ExportColumnInfo>? columns)
        {
            var shown = ShownPaths(columns);
            var detailLabels = CollectDetailLabels(files, shown);
            if (detailLabels.Count == 0) return;

            var sheet = workbook.Worksheets.Add("Full Details");

            // File name and path anchor each row back to sheet 1, whether or not they are visible there.
            var headers = new List<string> { "File Name", "File Path" };
            headers.AddRange(detailLabels);

            for (int i = 0; i < headers.Count; i++)
            {
                var cell = sheet.Cell(1, i + 1);
                cell.Value = headers[i];
                cell.Style.Font.Bold = true;
                cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#2D2D30");
                cell.Style.Font.FontColor = XLColor.White;
                cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            }

            int row = 2;
            foreach (var f in files)
            {
                var details = GetDetailFields(f, shown)
                    .ToDictionary(p => p.Label, p => p.Value, StringComparer.Ordinal);

                sheet.Cell(row, 1).Value = f.FileName;
                sheet.Cell(row, 2).Value = f.FilePath;
                for (int i = 0; i < detailLabels.Count; i++)
                    sheet.Cell(row, i + 3).Value = details.TryGetValue(detailLabels[i], out var v) ? v : "";

                row++;
            }

            sheet.Columns().AdjustToContents();
            foreach (var col in sheet.ColumnsUsed())
            {
                if (col.Width > 60) col.Width = 60;
                col.Style.Alignment.WrapText = true;
            }
            sheet.SheetView.FreezeRows(1);
        }

        /// <summary>
        /// Exports as a simple PDF using a basic text-based approach.
        /// Creates a formatted text layout saved as PDF-compatible content.
        /// </summary>
        private static void ExportPdf(List<AudioFileInfo> files, string filePath, List<ExportColumnInfo>? columns)
        {
            var headers = GetHeaders(columns);
            var shown = ShownPaths(columns);

            // Build the report as plain text first, then wrap it in PDF structure.
            var contentLines = new List<string>
            {
                "AudioAuditor - Analysis Report",
                $"Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}",
                $"Total Files: {files.Count}",
                ""
            };

            int valid = files.Count(f => f.Status == AudioStatus.Valid);
            int fake = files.Count(f => f.Status == AudioStatus.Fake);
            int optimized = files.Count(f => f.Status == AudioStatus.Optimized);
            int corrupt = files.Count(f => f.Status == AudioStatus.Corrupt);
            int unknown = files.Count(f => f.Status == AudioStatus.Unknown);
            contentLines.Add($"Real: {valid}  |  Fake: {fake}  |  Optimized: {optimized}  |  Corrupt: {corrupt}  |  Unknown: {unknown}");
            contentLines.Add("");

            contentLines.Add(string.Join(" | ", headers));
            contentLines.Add(new string('-', 120));

            foreach (var f in files)
                contentLines.Add(string.Join(" | ", GetRow(f, columns)));

            // Detail appendix: only files that actually have something extra get an entry, which is
            // what stops this from doubling the page count on a large library.
            var withDetails = files
                .Select(f => (File: f, Details: GetDetailFields(f, shown)))
                .Where(x => x.Details.Count > 0)
                .ToList();

            if (withDetails.Count > 0)
            {
                contentLines.Add("");
                contentLines.Add("");
                contentLines.Add("FULL DETAILS");
                contentLines.Add(new string('-', 120));

                foreach (var (f, details) in withDetails)
                {
                    contentLines.Add("");
                    contentLines.Add(f.FileName);
                    foreach (var (label, value) in details)
                        contentLines.Add($"    {label}: {value}");
                }
            }

            WritePdf(filePath, contentLines);
        }

        /// <summary>
        /// Writes <paramref name="lines"/> as a paginated, monospaced PDF.
        ///
        /// Built into a <see cref="MemoryStream"/> so the xref offsets are real byte positions.
        /// The previous version took them from <c>StringBuilder.Length</c> — a char count, which
        /// silently stops matching the byte count as soon as the text isn't plain ASCII, producing
        /// a file whose cross-reference table points into the wrong places.
        /// </summary>
        private static void WritePdf(string filePath, List<string> lines)
        {
            const int linesPerPage = 55;
            const int maxLineLen = 105;

            int pageCount = Math.Max(1, (lines.Count + linesPerPage - 1) / linesPerPage);

            const int catalogObj = 1;
            const int pagesObj = 2;
            const int fontObj = 3;

            var pageObjNums = new List<int>();
            var streamObjNums = new List<int>();
            int objCount = 3;
            for (int p = 0; p < pageCount; p++)
            {
                pageObjNums.Add(++objCount);
                streamObjNums.Add(++objCount);
            }

            using var ms = new MemoryStream();
            var offsets = new long[objCount + 1]; // 1-based object numbers

            void Write(string s)
            {
                var bytes = WinAnsiBytes(s);
                ms.Write(bytes, 0, bytes.Length);
            }

            Write("%PDF-1.4\n");
            // Binary marker: tells tools this file is not pure text, matching MinimalPdfWriter.
            ms.Write(new byte[] { (byte)'%', 0xE2, 0xE3, 0xCF, 0xD3, (byte)'\n' }, 0, 6);

            offsets[catalogObj] = ms.Position;
            Write($"{catalogObj} 0 obj\n<< /Type /Catalog /Pages {pagesObj} 0 R >>\nendobj\n");

            offsets[pagesObj] = ms.Position;
            Write($"{pagesObj} 0 obj\n<< /Type /Pages /Kids [{string.Join(" ", pageObjNums.Select(n => $"{n} 0 R"))}] /Count {pageCount} >>\nendobj\n");

            // /WinAnsiEncoding is what makes the accented and punctuation bytes below render as the
            // intended glyphs; without it the viewer falls back to StandardEncoding and shows
            // something else entirely for anything above 0x7F.
            offsets[fontObj] = ms.Position;
            Write($"{fontObj} 0 obj\n<< /Type /Font /Subtype /Type1 /BaseFont /Courier /Encoding /WinAnsiEncoding >>\nendobj\n");

            for (int p = 0; p < pageCount; p++)
            {
                int startLine = p * linesPerPage;
                int endLine = Math.Min(startLine + linesPerPage, lines.Count);

                var streamContent = new StringBuilder();
                streamContent.Append("BT\n/F1 8 Tf\n40 780 Td\n12 TL\n");

                for (int l = startLine; l < endLine; l++)
                {
                    // Wrap the RAW line, then escape each chunk. Escaping first and slicing at a
                    // fixed offset could cut a "\(" pair in half, leaving the chunk ending in a
                    // lone backslash that escapes the closing paren — the literal never terminates
                    // and the content stream is malformed. Windows paths (always containing '\')
                    // made that trivially reachable. Wrapping first is also more accurate, since
                    // the cap is a rendered-width limit and an escaped pair renders as one glyph.
                    string line = lines[l].TrimEnd();
                    while (line.Length > maxLineLen)
                    {
                        streamContent.Append('(').Append(EscapePdfString(line[..maxLineLen])).Append(") '\n");
                        line = line[maxLineLen..];
                    }
                    streamContent.Append('(').Append(EscapePdfString(line)).Append(") '\n");
                }
                streamContent.Append("ET\n");

                // Length must be the encoded byte count, not the char count.
                byte[] streamBytes = WinAnsiBytes(streamContent.ToString());

                offsets[pageObjNums[p]] = ms.Position;
                Write($"{pageObjNums[p]} 0 obj\n<< /Type /Page /Parent {pagesObj} 0 R /MediaBox [0 0 612 792] /Contents {streamObjNums[p]} 0 R /Resources << /Font << /F1 {fontObj} 0 R >> >> >>\nendobj\n");

                offsets[streamObjNums[p]] = ms.Position;
                Write($"{streamObjNums[p]} 0 obj\n<< /Length {streamBytes.Length} >>\nstream\n");
                ms.Write(streamBytes, 0, streamBytes.Length);
                Write("endstream\nendobj\n");
            }

            long xrefOffset = ms.Position;
            Write($"xref\n0 {objCount + 1}\n0000000000 65535 f \n");
            for (int i = 1; i <= objCount; i++)
                Write($"{offsets[i].ToString("D10", CultureInfo.InvariantCulture)} 00000 n \n");

            Write($"trailer\n<< /Size {objCount + 1} /Root {catalogObj} 0 R >>\nstartxref\n{xrefOffset.ToString(CultureInfo.InvariantCulture)}\n%%EOF\n");

            File.WriteAllBytes(filePath, ms.ToArray());
        }

        /// <summary>
        /// The 0x80–0x9F slots where WinAnsi (code page 1252) carries real glyphs — smart quotes,
        /// dashes, ellipsis — that Latin-1 leaves as control codes. Music metadata is full of these
        /// (<c>Artist – Title</c> with an en dash is the norm), so mapping them is what keeps a
        /// typical track name intact instead of littered with substitution marks.
        /// </summary>
        private static readonly Dictionary<char, byte> WinAnsiHighPunctuation = new()
        {
            ['€'] = 0x80, ['‚'] = 0x82, ['ƒ'] = 0x83, ['„'] = 0x84,
            ['…'] = 0x85, ['†'] = 0x86, ['‡'] = 0x87, ['ˆ'] = 0x88,
            ['‰'] = 0x89, ['Š'] = 0x8A, ['‹'] = 0x8B, ['Œ'] = 0x8C,
            ['Ž'] = 0x8E, ['‘'] = 0x91, ['’'] = 0x92, ['“'] = 0x93,
            ['”'] = 0x94, ['•'] = 0x95, ['–'] = 0x96, ['—'] = 0x97,
            ['˜'] = 0x98, ['™'] = 0x99, ['š'] = 0x9A, ['›'] = 0x9B,
            ['œ'] = 0x9C, ['ž'] = 0x9E, ['Ÿ'] = 0x9F,
        };

        /// <summary>
        /// Encodes text for a WinAnsi PDF. Characters the encoding can't represent are reduced to
        /// their closest unaccented ASCII form where one exists (Ā becomes A), and only fall back
        /// to '?' when there is genuinely no Latin equivalent.
        ///
        /// Non-Latin scripts (CJK, Cyrillic, Greek) still cannot render — a Type1 base font with a
        /// single-byte encoding has no glyphs for them, and fixing that means embedding a TrueType
        /// font subset with Identity-H. The export used to write <see cref="Encoding.ASCII"/>, so
        /// even "Björk" was mangled; that part is now correct.
        /// </summary>
        private static byte[] WinAnsiBytes(string s)
        {
            var bytes = new List<byte>(s.Length + 8);

            foreach (char c in s)
            {
                if (c < 0x80 || (c >= 0xA0 && c <= 0xFF))
                {
                    bytes.Add((byte)c);
                    continue;
                }

                if (WinAnsiHighPunctuation.TryGetValue(c, out byte mapped))
                {
                    bytes.Add(mapped);
                    continue;
                }

                bytes.Add(FallbackAsciiByte(c));
            }

            return bytes.ToArray();
        }

        /// <summary>
        /// Strips the diacritic from a character WinAnsi can't hold and returns the base letter, or
        /// '?' when decomposition yields nothing usable.
        /// </summary>
        /// <summary>
        /// Latin letters that carry no combining mark, so Unicode decomposition leaves them
        /// unchanged and they would otherwise become '?'. Turkish and Polish artist names hit these
        /// constantly, which is reason enough to spell the short list out.
        /// </summary>
        private static readonly Dictionary<char, char> NonDecomposableLatin = new()
        {
            ['ı'] = 'i', ['İ'] = 'I',
            ['ł'] = 'l', ['Ł'] = 'L',
            ['đ'] = 'd', ['Đ'] = 'D',
            ['ħ'] = 'h', ['Ħ'] = 'H',
            ['ŧ'] = 't', ['Ŧ'] = 'T',
            ['ə'] = 'e', ['Ə'] = 'E',
        };

        private static byte FallbackAsciiByte(char c)
        {
            if (NonDecomposableLatin.TryGetValue(c, out char direct)) return (byte)direct;

            try
            {
                foreach (char d in c.ToString().Normalize(NormalizationForm.FormD))
                {
                    if (d < 0x80 && !char.IsControl(d)) return (byte)d;
                    if (d >= 0xA0 && d <= 0xFF) return (byte)d;
                }
            }
            catch { /* unpaired surrogates and the like can't normalize */ }

            return (byte)'?';
        }

        /// <summary>
        /// Exports as a Word-compatible document (simple XML-based .docx alternative: plain text with .docx extension).
        /// Uses a minimal OOXML approach.
        /// </summary>
        private static void ExportWord(List<AudioFileInfo> files, string filePath, List<ExportColumnInfo>? columns)
        {
            // Create a minimal .docx file (which is a ZIP containing XML)
            var fileList = files;
            var headers = GetHeaders(columns);

            // Build the document.xml content
            var docXml = new StringBuilder();
            docXml.AppendLine("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>");
            docXml.AppendLine("<w:document xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\">");
            docXml.AppendLine("<w:body>");

            // Title
            AddWordParagraph(docXml, "AudioAuditor - Analysis Report", true, 28);
            AddWordParagraph(docXml, $"Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}", false, 20);
            AddWordParagraph(docXml, "", false, 20);

            // Summary
            int valid = fileList.Count(f => f.Status == AudioStatus.Valid);
            int fake = fileList.Count(f => f.Status == AudioStatus.Fake);
            int optimized = fileList.Count(f => f.Status == AudioStatus.Optimized);
            int corrupt = fileList.Count(f => f.Status == AudioStatus.Corrupt);
            int unknown = fileList.Count(f => f.Status == AudioStatus.Unknown);
            AddWordParagraph(docXml, $"Total Files: {fileList.Count}  |  Real: {valid}  |  Fake: {fake}  |  Optimized: {optimized}  |  Corrupt: {corrupt}  |  Unknown: {unknown}", false, 20);
            AddWordParagraph(docXml, "", false, 20);

            // Table header
            AddWordParagraph(docXml, string.Join("  |  ", headers), true, 16);

            // Data rows
            foreach (var f in fileList)
            {
                var row = GetRow(f, columns);
                AddWordParagraph(docXml, string.Join("  |  ", row), false, 16);
            }

            // Detail appendix — same rule as the PDF: only files with something extra appear.
            var shown = ShownPaths(columns);
            var withDetails = fileList
                .Select(f => (File: f, Details: GetDetailFields(f, shown)))
                .Where(x => x.Details.Count > 0)
                .ToList();

            if (withDetails.Count > 0)
            {
                AddWordParagraph(docXml, "", false, 20);
                AddWordParagraph(docXml, "Full Details", true, 24);

                foreach (var (f, details) in withDetails)
                {
                    AddWordParagraph(docXml, "", false, 16);
                    AddWordParagraph(docXml, f.FileName, true, 18);
                    foreach (var (label, value) in details)
                        AddWordParagraph(docXml, $"    {label}: {value}", false, 16);
                }
            }

            docXml.AppendLine("</w:body>");
            docXml.AppendLine("</w:document>");

            // Content Types
            var contentTypes = "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                "<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">" +
                "<Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/>" +
                "<Default Extension=\"xml\" ContentType=\"application/xml\"/>" +
                "<Override PartName=\"/word/document.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml\"/>" +
                "</Types>";

            // Relationships
            var rels = "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
                "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"word/document.xml\"/>" +
                "</Relationships>";

            // Create ZIP archive (a .docx is just a ZIP)
            using var fs = new FileStream(filePath, FileMode.Create);
            using var archive = new System.IO.Compression.ZipArchive(fs, System.IO.Compression.ZipArchiveMode.Create);

            var ctEntry = archive.CreateEntry("[Content_Types].xml");
            using (var w = new StreamWriter(ctEntry.Open()))
                w.Write(contentTypes);

            var relsEntry = archive.CreateEntry("_rels/.rels");
            using (var w = new StreamWriter(relsEntry.Open()))
                w.Write(rels);

            var docEntry = archive.CreateEntry("word/document.xml");
            using (var w = new StreamWriter(docEntry.Open()))
                w.Write(docXml.ToString());
        }

        private static void AddWordParagraph(StringBuilder sb, string text, bool bold, int fontSize)
        {
            string escaped = text.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
            sb.Append("<w:p><w:r><w:rPr>");
            if (bold) sb.Append("<w:b/>");
            sb.Append($"<w:sz w:val=\"{fontSize}\"/>");
            sb.Append("<w:rFonts w:ascii=\"Segoe UI\" w:hAnsi=\"Segoe UI\"/>");
            sb.Append($"</w:rPr><w:t xml:space=\"preserve\">{escaped}</w:t></w:r></w:p>");
            sb.AppendLine();
        }
    }
}
