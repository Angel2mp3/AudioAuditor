using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace AudioQualityChecker.Services;

/// <summary>
/// Writes a one-page PDF that embeds a single JPEG image (via the PDF-native DCTDecode filter, so the
/// JPEG bytes are stored verbatim). Deliberately dependency-free — used to export the Wrapped dashboard
/// to PDF without pulling in a PDF library. Assumes an RGB (DeviceRGB) baseline JPEG.
/// </summary>
public static class MinimalPdfWriter
{
    /// <param name="pageWidthPt">Page width in points; 0 falls back to 1px = 1pt.</param>
    /// <param name="pageHeightPt">Page height in points; 0 falls back to 1px = 1pt.</param>
    public static byte[] SingleImagePdf(byte[] jpegBytes, int pxWidth, int pxHeight,
                                        double pageWidthPt = 0, double pageHeightPt = 0)
    {
        if (jpegBytes == null || jpegBytes.Length == 0) throw new ArgumentException("Empty image.", nameof(jpegBytes));
        if (pxWidth <= 0 || pxHeight <= 0) throw new ArgumentException("Bad image size.");

        // The page defaults to the image's pixel size at 1px = 1pt; the content matrix scales the unit
        // image to fill it. Callers pass explicit point dimensions to get a sane physical page size.
        double pw = pageWidthPt > 0 ? pageWidthPt : pxWidth;
        double ph = pageHeightPt > 0 ? pageHeightPt : pxHeight;

        string content = $"q {Num(pw)} 0 0 {Num(ph)} 0 0 cm /Im0 Do Q\n";
        byte[] contentBytes = Latin1(content);

        var objects = new List<byte[]>
        {
            Latin1("<< /Type /Catalog /Pages 2 0 R >>"),
            Latin1("<< /Type /Pages /Kids [3 0 R] /Count 1 >>"),
            Latin1($"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {Num(pw)} {Num(ph)}] "
                   + "/Resources << /XObject << /Im0 5 0 R >> >> /Contents 4 0 R >>"),
            Concat(Latin1($"<< /Length {contentBytes.Length} >>\nstream\n"), contentBytes, Latin1("\nendstream")),
            Concat(
                Latin1($"<< /Type /XObject /Subtype /Image /Width {pxWidth} /Height {pxHeight} "
                       + $"/ColorSpace /DeviceRGB /BitsPerComponent 8 /Filter /DCTDecode /Length {jpegBytes.Length} >>\nstream\n"),
                jpegBytes,
                Latin1("\nendstream")),
        };

        using var ms = new MemoryStream();
        void Write(byte[] b) => ms.Write(b, 0, b.Length);

        Write(Latin1("%PDF-1.4\n"));
        Write(new byte[] { (byte)'%', 0xE2, 0xE3, 0xCF, 0xD3, (byte)'\n' }); // binary marker

        var offsets = new long[objects.Count + 1]; // 1-based object numbers
        for (int i = 0; i < objects.Count; i++)
        {
            offsets[i + 1] = ms.Position;
            Write(Latin1($"{i + 1} 0 obj\n"));
            Write(objects[i]);
            Write(Latin1("\nendobj\n"));
        }

        long xrefStart = ms.Position;
        int count = objects.Count + 1;
        Write(Latin1($"xref\n0 {count}\n"));
        Write(Latin1("0000000000 65535 f \n"));
        for (int i = 1; i < count; i++)
            Write(Latin1($"{offsets[i].ToString("D10", CultureInfo.InvariantCulture)} 00000 n \n"));

        Write(Latin1($"trailer\n<< /Size {count} /Root 1 0 R >>\nstartxref\n{xrefStart}\n%%EOF"));
        return ms.ToArray();
    }

    private static string Num(double v) => v.ToString("0.####", CultureInfo.InvariantCulture);
    private static byte[] Latin1(string s) => Encoding.Latin1.GetBytes(s);

    private static byte[] Concat(params byte[][] parts)
    {
        int len = 0;
        foreach (var p in parts) len += p.Length;
        var result = new byte[len];
        int pos = 0;
        foreach (var p in parts) { Buffer.BlockCopy(p, 0, result, pos, p.Length); pos += p.Length; }
        return result;
    }
}
