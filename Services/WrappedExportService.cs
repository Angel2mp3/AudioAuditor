using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace AudioQualityChecker.Services
{
    /// <summary>
    /// Composites live WPF elements (the Wrapped dashboard's header, columns and footer) into PNG/JPEG
    /// bytes, and into a one-page PDF via the dependency-free <see cref="MinimalPdfWriter"/>. A solid
    /// background is painted first so exported images aren't transparent/black.
    ///
    /// The caller supplies each piece already positioned, because the dashboard's columns live in a
    /// ScrollViewer: on screen they're clipped to the viewport, but the ScrollViewer measures its child
    /// with infinite height, so the columns grid is *already* laid out at its full content height. Drawing
    /// the pieces at explicit rects captures everything below the fold without touching layout — measuring
    /// or arranging a live element here doesn't stick, because UpdateLayout runs a global pass that puts
    /// the element straight back to its parent-supplied size before the render happens.
    /// </summary>
    public static class WrappedExportService
    {
        /// <summary>A stack of live elements to draw into one bitmap, each at an absolute rect.</summary>
        public sealed class Composition
        {
            public Size Canvas;
            public Brush Background = Brushes.Black;
            public List<(FrameworkElement Element, Rect Rect)> Pieces = new();
        }

        public static byte[] RenderPng(Composition composition, double scale = 2.0)
        {
            var (bmp, _, _) = Render(composition, scale);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bmp));
            return Encode(encoder);
        }

        public static byte[] RenderJpeg(Composition composition, double scale = 2.0, int quality = 92)
        {
            var (bmp, _, _) = Render(composition, scale);
            var encoder = new JpegBitmapEncoder { QualityLevel = quality };
            encoder.Frames.Add(BitmapFrame.Create(bmp));
            return Encode(encoder);
        }

        public static byte[] RenderPdf(Composition composition, double scale = 2.0)
        {
            var (bmp, pxW, pxH) = Render(composition, scale);
            var encoder = new JpegBitmapEncoder { QualityLevel = 92 };
            encoder.Frames.Add(BitmapFrame.Create(bmp));

            // 1px = 1pt would make a 3000x4000px export a 41in x 55in page. Fit the page to A4 width
            // instead and keep the aspect ratio; a tall dashboard still gives one tall page.
            const double A4WidthPt = 595.276;
            double pageH = A4WidthPt * pxH / pxW;
            return MinimalPdfWriter.SingleImagePdf(Encode(encoder), pxW, pxH, A4WidthPt, pageH);
        }

        /// <summary>
        /// Height the export canvas needs, and how far the footer shifts down, when the scrolled
        /// content is drawn un-clipped at its full height.
        /// </summary>
        public static (double canvasHeight, double footerShift) ExpandForFullContent(
            double hostHeight, double viewportHeight, double contentHeight)
        {
            double extra = Math.Max(0, contentHeight - viewportHeight);
            return (hostHeight + extra, extra);
        }

        private static (RenderTargetBitmap bmp, int pxWidth, int pxHeight) Render(
            Composition composition, double scale)
        {
            double w = composition.Canvas.Width, h = composition.Canvas.Height;
            if (w < 1 || h < 1) throw new InvalidOperationException("The dashboard isn't laid out yet.");

            int pxW = Math.Max(1, (int)Math.Round(w * scale));
            int pxH = Math.Max(1, (int)Math.Round(h * scale));
            var rtb = new RenderTargetBitmap(pxW, pxH, 96 * scale, 96 * scale, PixelFormats.Pbgra32);

            var dv = new DrawingVisual();
            using (var ctx = dv.RenderOpen())
            {
                ctx.DrawRectangle(composition.Background, null, new Rect(0, 0, w, h));
                foreach (var (element, rect) in composition.Pieces)
                {
                    if (rect.Width < 1 || rect.Height < 1) continue;

                    // Absolute viewbox/viewport maps the element's layout rect 1:1 onto the destination.
                    // The default relative viewbox would stretch the element's *descendant* bounds to fill
                    // the rect, which distorts the aspect ratio whenever those bounds differ from the
                    // layout rect (negative margins, drop shadows, render transforms).
                    var brush = new VisualBrush(element)
                    {
                        ViewboxUnits = BrushMappingMode.Absolute,
                        Viewbox = new Rect(0, 0, rect.Width, rect.Height),
                        ViewportUnits = BrushMappingMode.Absolute,
                        Viewport = rect,
                        Stretch = Stretch.Fill,
                        TileMode = TileMode.None
                    };
                    ctx.DrawRectangle(brush, null, rect);
                }
            }
            rtb.Render(dv);
            return (rtb, pxW, pxH);
        }

        private static byte[] Encode(BitmapEncoder encoder)
        {
            using var ms = new MemoryStream();
            encoder.Save(ms);
            return ms.ToArray();
        }
    }
}
