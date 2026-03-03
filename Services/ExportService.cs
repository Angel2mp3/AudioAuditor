using System;
using System.Collections.Generic;
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
            "Reported Bitrate", "Actual Bitrate", "Extension", "Max Frequency",
            "Clipping", "Clipping %", "BPM", "Replay Gain", "MQA", "MQA Encoder"
        };

        /// <summary>
        /// Exports analysis results using the user's current column layout.
        /// </summary>
        public static void Export(IEnumerable<AudioFileInfo> files, string filePath, List<ExportColumnInfo>? columns = null)
        {
            // If no column info provided, use defaults
            var orderedColumns = columns != null && columns.Count > 0
                ? columns.Where(c => c.IsVisible).OrderBy(c => c.DisplayIndex).ToList()
                : null;

            string ext = Path.GetExtension(filePath).ToLowerInvariant();
            switch (ext)
            {
                case ".csv":
                    ExportCsv(files, filePath, orderedColumns);
                    break;
                case ".txt":
                    ExportText(files, filePath, orderedColumns);
                    break;
                case ".xlsx":
                    ExportExcel(files, filePath, orderedColumns);
                    break;
                case ".pdf":
                    ExportPdf(files, filePath, orderedColumns);
                    break;
                case ".docx":
                    ExportWord(files, filePath, orderedColumns);
                    break;
                default:
                    ExportCsv(files, filePath, orderedColumns);
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
                "Status" => f.Status.ToString(),
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
                "Extension" => f.Extension,
                "EffectiveFrequencyDisplay" or "EffectiveFrequency" => f.EffectiveFrequencyDisplay,
                "HasClipping" or "ClippingDisplay" => f.ClippingDisplay,
                "ClippingPercentage" => f.HasClipping ? $"{f.ClippingPercentage:F2}%" : "-",
                "BpmDisplay" or "Bpm" => f.BpmDisplay,
                "ReplayGainDisplay" or "ReplayGain" => f.ReplayGainDisplay,
                "IsMqa" or "MqaDisplay" => f.MqaDisplay,
                "MqaEncoder" => f.MqaEncoder,
                "IsAiGenerated" or "AiDisplay" => f.AiDisplay,
                "AiSource" => f.AiSource,
                _ => "-"
            };
        }

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
            return new[]
            {
                f.Status.ToString(),
                f.Title,
                f.Artist,
                f.FileName,
                f.FilePath,
                f.SampleRate > 0 ? $"{f.SampleRate} Hz" : "-",
                f.BitsPerSample > 0 ? $"{f.BitsPerSample}-bit" : "-",
                f.Channels > 0 ? (f.Channels == 1 ? "Mono" : f.Channels == 2 ? "Stereo" : $"{f.Channels}ch") : "-",
                f.Duration,
                f.FileSize,
                f.ReportedBitrate > 0 ? $"{f.ReportedBitrate} kbps" : "-",
                f.ActualBitrate > 0 ? $"{f.ActualBitrate} kbps" : "-",
                f.Extension,
                f.EffectiveFrequency > 0 ? $"{f.EffectiveFrequency} Hz" : "-",
                f.HasClipping ? "YES" : "No",
                f.HasClipping ? $"{f.ClippingPercentage:F2}%" : "-",
                f.Bpm > 0 ? $"{f.Bpm}" : "-",
                f.HasReplayGain ? $"{f.ReplayGain:+0.00;-0.00;0.00} dB" : "-",
                f.MqaDisplay,
                f.MqaEncoder
            };
        }

        private static void ExportCsv(IEnumerable<AudioFileInfo> files, string filePath, List<ExportColumnInfo>? columns)
        {
            var sb = new StringBuilder();
            sb.AppendLine(string.Join(",", GetHeaders(columns).Select(EscapeCsv)));

            foreach (var f in files)
            {
                sb.AppendLine(string.Join(",", GetRow(f, columns).Select(EscapeCsv)));
            }

            File.WriteAllText(filePath, sb.ToString(), Encoding.UTF8);
        }

        private static string EscapeCsv(string val)
        {
            if (val.Contains(',') || val.Contains('"') || val.Contains('\n'))
                return $"\"{val.Replace("\"", "\"\"")}\"";
            return val;
        }

        private static void ExportText(IEnumerable<AudioFileInfo> files, string filePath, List<ExportColumnInfo>? columns)
        {
            var sb = new StringBuilder();
            sb.AppendLine("═══════════════════════════════════════════════════════════════");
            sb.AppendLine("  AudioAuditor — Analysis Report");
            sb.AppendLine($"  Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine("═══════════════════════════════════════════════════════════════");
            sb.AppendLine();

            var fileList = files.ToList();

            // Summary
            int valid = fileList.Count(f => f.Status == AudioStatus.Valid);
            int fake = fileList.Count(f => f.Status == AudioStatus.Fake);
            int optimized = fileList.Count(f => f.Status == AudioStatus.Optimized);
            int corrupt = fileList.Count(f => f.Status == AudioStatus.Corrupt);
            int unknown = fileList.Count(f => f.Status == AudioStatus.Unknown);

            sb.AppendLine($"  Total Files: {fileList.Count}");
            sb.AppendLine($"  Valid: {valid}  |  Fake: {fake}  |  Optimized: {optimized}  |  Corrupt: {corrupt}  |  Unknown: {unknown}");
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
                    sb.AppendLine($"  [{f.Status}]  {f.FileName}");
                    for (int i = 0; i < headers.Length; i++)
                    {
                        if (headers[i] == "Status") continue; // already shown above
                        sb.AppendLine($"    {headers[i]}: {values[i]}");
                    }
                }
                else
                {
                    sb.AppendLine();
                    sb.AppendLine($"  [{f.Status}]  {f.FileName}");
                    if (!string.IsNullOrEmpty(f.Artist) || !string.IsNullOrEmpty(f.Title))
                        sb.AppendLine($"    Artist: {f.Artist}  |  Title: {f.Title}");
                    sb.AppendLine($"    Format: {f.Extension}  |  Duration: {f.Duration}  |  Size: {f.FileSize}");
                    sb.AppendLine($"    Sample Rate: {(f.SampleRate > 0 ? $"{f.SampleRate} Hz" : "-")}  |  Bit Depth: {(f.BitsPerSample > 0 ? $"{f.BitsPerSample}-bit" : "-")}  |  Channels: {f.ChannelsDisplay}");
                    sb.AppendLine($"    Bitrate: {(f.ReportedBitrate > 0 ? $"{f.ReportedBitrate}" : "-")} / {(f.ActualBitrate > 0 ? $"{f.ActualBitrate}" : "-")} kbps (reported/actual)");
                    sb.AppendLine($"    Max Freq: {(f.EffectiveFrequency > 0 ? $"{f.EffectiveFrequency} Hz" : "-")}  |  Clipping: {f.ClippingDisplay}");
                    if (f.Bpm > 0) sb.AppendLine($"    BPM: {f.Bpm}");
                    if (f.HasReplayGain) sb.AppendLine($"    Replay Gain: {f.ReplayGain:+0.00;-0.00;0.00} dB");
                    if (f.IsMqa) sb.AppendLine($"    MQA: {f.MqaDisplay}  |  Encoder: {f.MqaEncoder}");
                    sb.AppendLine($"    Path: {f.FilePath}");
                }
                sb.AppendLine("  ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─");
            }

            sb.AppendLine();
            sb.AppendLine("═══════════════════════════════════════════════════════════════");

            File.WriteAllText(filePath, sb.ToString(), Encoding.UTF8);
        }

        private static void ExportExcel(IEnumerable<AudioFileInfo> files, string filePath, List<ExportColumnInfo>? columns)
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

            workbook.SaveAs(filePath);
        }

        /// <summary>
        /// Exports as a simple PDF using a basic text-based approach.
        /// Creates a formatted text layout saved as PDF-compatible content.
        /// </summary>
        private static void ExportPdf(IEnumerable<AudioFileInfo> files, string filePath, List<ExportColumnInfo>? columns)
        {
            // Use a simple text-based PDF generation (no external library needed)
            var fileList = files.ToList();
            var sb = new StringBuilder();
            var headers = GetHeaders(columns);

            // PDF header
            sb.AppendLine("%PDF-1.4");
            var objects = new List<(int objNum, long offset)>();
            int objCount = 0;

            // We'll build the content as plain text first, then wrap in PDF structure
            var contentLines = new StringBuilder();
            contentLines.AppendLine("AudioAuditor - Analysis Report");
            contentLines.AppendLine($"Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            contentLines.AppendLine($"Total Files: {fileList.Count}");
            contentLines.AppendLine("");

            int valid = fileList.Count(f => f.Status == AudioStatus.Valid);
            int fake = fileList.Count(f => f.Status == AudioStatus.Fake);
            int optimized = fileList.Count(f => f.Status == AudioStatus.Optimized);
            int corrupt = fileList.Count(f => f.Status == AudioStatus.Corrupt);
            int unknown = fileList.Count(f => f.Status == AudioStatus.Unknown);
            contentLines.AppendLine($"Valid: {valid}  |  Fake: {fake}  |  Optimized: {optimized}  |  Corrupt: {corrupt}  |  Unknown: {unknown}");
            contentLines.AppendLine("");

            // CSV-style header
            contentLines.AppendLine(string.Join(" | ", headers));
            contentLines.AppendLine(new string('-', 120));

            foreach (var f in fileList)
            {
                var row = GetRow(f, columns);
                contentLines.AppendLine(string.Join(" | ", row));
            }

            // Build minimal PDF
            var pdfContent = new StringBuilder();
            string textContent = contentLines.ToString().Replace("\\", "\\\\").Replace("(", "\\(").Replace(")", "\\)");

            // Split into pages (roughly 60 lines per page)
            var allLines = contentLines.ToString().Split('\n');
            int linesPerPage = 55;
            int pageCount = (allLines.Length + linesPerPage - 1) / linesPerPage;

            var pageObjNums = new List<int>();
            var streamObjNums = new List<int>();

            // Object 1: Catalog
            objCount++;
            int catalogObj = objCount;

            // Object 2: Pages
            objCount++;
            int pagesObj = objCount;

            // Object 3: Font
            objCount++;
            int fontObj = objCount;

            // Create page and stream objects
            for (int p = 0; p < pageCount; p++)
            {
                objCount++;
                pageObjNums.Add(objCount);
                objCount++;
                streamObjNums.Add(objCount);
            }

            // Now write the PDF
            var pdf = new StringBuilder();
            pdf.AppendLine("%PDF-1.4");
            var offsets = new List<long>();

            // Catalog
            offsets.Add(pdf.Length);
            pdf.AppendLine($"{catalogObj} 0 obj");
            pdf.AppendLine($"<< /Type /Catalog /Pages {pagesObj} 0 R >>");
            pdf.AppendLine("endobj");

            // Pages
            offsets.Add(pdf.Length);
            pdf.Append($"{pagesObj} 0 obj\n<< /Type /Pages /Kids [");
            foreach (var pn in pageObjNums)
                pdf.Append($"{pn} 0 R ");
            pdf.AppendLine($"] /Count {pageCount} >>");
            pdf.AppendLine("endobj");

            // Font
            offsets.Add(pdf.Length);
            pdf.AppendLine($"{fontObj} 0 obj");
            pdf.AppendLine("<< /Type /Font /Subtype /Type1 /BaseFont /Courier >>");
            pdf.AppendLine("endobj");

            // Pages and streams
            for (int p = 0; p < pageCount; p++)
            {
                int startLine = p * linesPerPage;
                int endLine = Math.Min(startLine + linesPerPage, allLines.Length);

                var streamContent = new StringBuilder();
                streamContent.AppendLine("BT");
                streamContent.AppendLine($"/F1 8 Tf");
                streamContent.AppendLine("40 780 Td");
                streamContent.AppendLine("12 TL");

                for (int l = startLine; l < endLine; l++)
                {
                    string line = allLines[l].TrimEnd().Replace("\\", "\\\\").Replace("(", "\\(").Replace(")", "\\)");
                    // Wrap long lines instead of truncating
                    const int maxLineLen = 105;
                    while (line.Length > maxLineLen)
                    {
                        streamContent.AppendLine($"({line[..maxLineLen]}) '");
                        line = line[maxLineLen..];
                    }
                    streamContent.AppendLine($"({line}) '");
                }
                streamContent.AppendLine("ET");

                string streamStr = streamContent.ToString();
                int streamLen = Encoding.ASCII.GetByteCount(streamStr);

                // Page object
                offsets.Add(pdf.Length);
                pdf.AppendLine($"{pageObjNums[p]} 0 obj");
                pdf.AppendLine($"<< /Type /Page /Parent {pagesObj} 0 R /MediaBox [0 0 612 792] /Contents {streamObjNums[p]} 0 R /Resources << /Font << /F1 {fontObj} 0 R >> >> >>");
                pdf.AppendLine("endobj");

                // Stream object
                offsets.Add(pdf.Length);
                pdf.AppendLine($"{streamObjNums[p]} 0 obj");
                pdf.AppendLine($"<< /Length {streamLen} >>");
                pdf.AppendLine("stream");
                pdf.Append(streamStr);
                pdf.AppendLine("endstream");
                pdf.AppendLine("endobj");
            }

            // Cross-reference table
            long xrefOffset = pdf.Length;
            pdf.AppendLine("xref");
            pdf.AppendLine($"0 {objCount + 1}");
            pdf.AppendLine("0000000000 65535 f ");
            foreach (var offset in offsets)
                pdf.AppendLine($"{offset:D10} 00000 n ");

            // Trailer
            pdf.AppendLine("trailer");
            pdf.AppendLine($"<< /Size {objCount + 1} /Root {catalogObj} 0 R >>");
            pdf.AppendLine("startxref");
            pdf.AppendLine(xrefOffset.ToString());
            pdf.AppendLine("%%EOF");

            File.WriteAllText(filePath, pdf.ToString(), Encoding.ASCII);
        }

        /// <summary>
        /// Exports as a Word-compatible document (simple XML-based .docx alternative: plain text with .docx extension).
        /// Uses a minimal OOXML approach.
        /// </summary>
        private static void ExportWord(IEnumerable<AudioFileInfo> files, string filePath, List<ExportColumnInfo>? columns)
        {
            // Create a minimal .docx file (which is a ZIP containing XML)
            var fileList = files.ToList();
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
            AddWordParagraph(docXml, $"Total Files: {fileList.Count}  |  Valid: {valid}  |  Fake: {fake}  |  Optimized: {optimized}  |  Corrupt: {corrupt}  |  Unknown: {unknown}", false, 20);
            AddWordParagraph(docXml, "", false, 20);

            // Table header
            AddWordParagraph(docXml, string.Join("  |  ", headers), true, 16);

            // Data rows
            foreach (var f in fileList)
            {
                var row = GetRow(f, columns);
                AddWordParagraph(docXml, string.Join("  |  ", row), false, 16);
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
