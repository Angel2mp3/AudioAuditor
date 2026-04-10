using System;
using System.Collections.Generic;
using System.Linq;

namespace AudioQualityChecker.Services;

/// <summary>
/// Extracts dominant colors from album cover art using k-means clustering.
/// Platform-independent: takes raw BGRA32 pixel data.
/// </summary>
public static class AlbumColorExtractor
{
    public record DominantColors(
        Color Primary,
        Color Secondary,
        Color Tertiary,
        Color Background,
        Color TextOnBackground);

    public readonly struct Color
    {
        public byte R { get; init; }
        public byte G { get; init; }
        public byte B { get; init; }
        public byte A { get; init; }

        public Color(byte r, byte g, byte b, byte a = 255) { R = r; G = g; B = b; A = a; }

        public double Luminance => 0.299 * R + 0.587 * G + 0.114 * B;

        public Color WithAlpha(byte a) => new(R, G, B, a);

        public override string ToString() => $"#{A:X2}{R:X2}{G:X2}{B:X2}";
    }

    /// <summary>
    /// Extract dominant colors from raw BGRA32 pixel data.
    /// </summary>
    public static DominantColors Extract(byte[] pixelData, int width, int height, int stride = 0)
    {
        if (stride == 0) stride = width * 4;

        // Sample pixels (downsample to ~4000 for better coverage)
        var samples = SamplePixels(pixelData, width, height, stride, maxSamples: 4000);

        if (samples.Count < 3)
        {
            var fallback = new Color(80, 80, 80);
            return new DominantColors(fallback, fallback, fallback, new Color(20, 20, 20), new Color(240, 240, 240));
        }

        // K-means clustering with k=8 for finer color separation
        var clusters = KMeans(samples, 8, maxIterations: 30);

        // Snap each cluster center to the nearest actual pixel color
        // so extracted colors are truly present in the image
        foreach (var cluster in clusters)
        {
            double bestDist = double.MaxValue;
            (byte R, byte G, byte B) bestPixel = (cluster.Center.R, cluster.Center.G, cluster.Center.B);
            foreach (var (r, g, b) in samples)
            {
                double d = ColorDistSq(r, g, b, cluster.Center.R, cluster.Center.G, cluster.Center.B);
                if (d < bestDist)
                {
                    bestDist = d;
                    bestPixel = (r, g, b);
                }
            }
            cluster.Center = new Color(bestPixel.R, bestPixel.G, bestPixel.B);
        }

        // Sort by cluster size (most frequent first)
        clusters.Sort((a, b) => b.Count.CompareTo(a.Count));

        // Score each cluster: prefer vibrant (saturated) colors over grey/muddy ones
        // while still respecting frequency. This ensures the UI looks colorful.
        var scored = clusters
            .Select(c => new { Cluster = c, Score = ClusterScore(c) })
            .OrderByDescending(x => x.Score)
            .ToList();

        var primary = scored[0].Cluster.Center;
        var secondary = PickDistinct(scored.Select(s => s.Cluster).ToList(), primary, minDistance: 60)
            ?? scored[Math.Min(1, scored.Count - 1)].Cluster.Center;
        var tertiary = PickDistinct(scored.Select(s => s.Cluster).ToList(), primary, minDistance: 40, exclude: secondary)
            ?? scored[Math.Min(2, scored.Count - 1)].Cluster.Center;

        // If primary is too grey (low saturation), try to swap with a more vibrant option
        if (GetSaturation(primary) < 0.20 && scored.Count > 1 && GetSaturation(scored[1].Cluster.Center) > 0.20)
        {
            var oldPrimary = primary;
            primary = scored[1].Cluster.Center;
            secondary = PickDistinct(scored.Select(s => s.Cluster).ToList(), primary, minDistance: 60)
                ?? oldPrimary;
        }

        // Ensure secondary has decent contrast vs primary
        if (scored.Count > 2)
        {
            double dist = Math.Sqrt(ColorDistSq(primary.R, primary.G, primary.B,
                secondary.R, secondary.G, secondary.B));
            if (dist < 40)
            {
                // Secondary too similar — pick the most different available cluster
                var alt = scored.Select(s => s.Cluster.Center)
                    .OrderByDescending(c => Math.Sqrt(ColorDistSq(c.R, c.G, c.B, primary.R, primary.G, primary.B)))
                    .FirstOrDefault();
                if (alt.A != 0) secondary = alt;
            }
        }

        // Background: darkened version of primary
        var bg = Darken(primary, 0.25);

        // Text: white or near-white if bg is dark, dark if bg is light
        var text = bg.Luminance < 128
            ? new Color(240, 240, 240)
            : new Color(30, 30, 30);

        return new DominantColors(primary, secondary, tertiary, bg, text);
    }

    /// <summary>
    /// Extract from TagLib picture data (convenience overload).
    /// Decodes a simple BMP/raw or falls back to center-sampling.
    /// For full image decode, use the Avalonia/platform-specific overload instead.
    /// </summary>
    public static DominantColors ExtractFromImageBytes(byte[] imageBytes)
    {
        // This is a fallback. Each UI platform should decode to BGRA and call Extract().
        // Return default appealing colors for now.  
        // Platform-specific code will call the main Extract() method.
        var fallback = new Color(90, 90, 140);
        return new DominantColors(fallback, new Color(140, 100, 90), new Color(60, 120, 100),
            new Color(20, 20, 30), new Color(240, 240, 240));
    }

    private static List<(byte R, byte G, byte B)> SamplePixels(byte[] data, int w, int h, int stride, int maxSamples)
    {
        var result = new List<(byte, byte, byte)>(maxSamples);
        int step = Math.Max(1, (int)Math.Sqrt((double)w * h / maxSamples));
        var rng = new Random(42); // deterministic

        for (int y = 0; y < h; y += step)
        {
            for (int x = 0; x < w; x += step)
            {
                int idx = y * stride + x * 4;
                if (idx + 3 >= data.Length) continue;

                byte b = data[idx];
                byte g = data[idx + 1];
                byte r = data[idx + 2];
                byte a = data[idx + 3];

                // Skip fully transparent or near-black/near-white (uninteresting)
                if (a < 128) continue;
                int brightness = (r + g + b) / 3;
                if (brightness < 15 || brightness > 240) continue;

                result.Add((r, g, b));
            }
        }

        // If we filtered too aggressively, sample again without brightness filter
        if (result.Count < 50)
        {
            result.Clear();
            for (int y = 0; y < h; y += step)
            {
                for (int x = 0; x < w; x += step)
                {
                    int idx = y * stride + x * 4;
                    if (idx + 3 >= data.Length) continue;
                    result.Add((data[idx + 2], data[idx + 1], data[idx]));
                }
            }
        }

        return result;
    }

    private class Cluster
    {
        public Color Center;
        public int Count;
        public long SumR, SumG, SumB;

        public void Reset() { SumR = SumG = SumB = 0; Count = 0; }

        public void Add(byte r, byte g, byte b) { SumR += r; SumG += g; SumB += b; Count++; }

        public bool UpdateCenter()
        {
            if (Count == 0) return false;
            var newR = (byte)(SumR / Count);
            var newG = (byte)(SumG / Count);
            var newB = (byte)(SumB / Count);
            bool changed = Math.Abs(newR - Center.R) > 2 || Math.Abs(newG - Center.G) > 2 || Math.Abs(newB - Center.B) > 2;
            Center = new Color(newR, newG, newB);
            return changed;
        }
    }

    private static List<Cluster> KMeans(List<(byte R, byte G, byte B)> points, int k, int maxIterations)
    {
        var rng = new Random(42);
        var clusters = new List<Cluster>(k);

        // Initialize centers using k-means++ style
        var firstIdx = rng.Next(points.Count);
        clusters.Add(new Cluster { Center = new Color(points[firstIdx].R, points[firstIdx].G, points[firstIdx].B) });

        for (int i = 1; i < k && i < points.Count; i++)
        {
            double totalDist = 0;
            var distances = new double[points.Count];
            for (int j = 0; j < points.Count; j++)
            {
                double minDist = double.MaxValue;
                foreach (var c in clusters)
                {
                    double d = ColorDistSq(points[j].R, points[j].G, points[j].B, c.Center.R, c.Center.G, c.Center.B);
                    if (d < minDist) minDist = d;
                }
                distances[j] = minDist;
                totalDist += minDist;
            }

            if (totalDist < 1) { clusters.Add(new Cluster { Center = clusters[0].Center }); continue; }

            double target = rng.NextDouble() * totalDist;
            double cumulative = 0;
            for (int j = 0; j < points.Count; j++)
            {
                cumulative += distances[j];
                if (cumulative >= target)
                {
                    clusters.Add(new Cluster { Center = new Color(points[j].R, points[j].G, points[j].B) });
                    break;
                }
            }
            if (clusters.Count <= i) // safety
                clusters.Add(new Cluster { Center = new Color(points[rng.Next(points.Count)].R, points[rng.Next(points.Count)].G, points[rng.Next(points.Count)].B) });
        }

        // Iterate
        for (int iter = 0; iter < maxIterations; iter++)
        {
            foreach (var c in clusters) c.Reset();

            foreach (var (r, g, b) in points)
            {
                int bestIdx = 0;
                double bestDist = double.MaxValue;
                for (int ci = 0; ci < clusters.Count; ci++)
                {
                    double d = ColorDistSq(r, g, b, clusters[ci].Center.R, clusters[ci].Center.G, clusters[ci].Center.B);
                    if (d < bestDist) { bestDist = d; bestIdx = ci; }
                }
                clusters[bestIdx].Add(r, g, b);
            }

            bool anyChanged = false;
            foreach (var c in clusters)
                anyChanged |= c.UpdateCenter();

            if (!anyChanged) break;
        }

        return clusters.Where(c => c.Count > 0).ToList();
    }

    private static double ColorDistSq(byte r1, byte g1, byte b1, byte r2, byte g2, byte b2)
    {
        // Weighted Euclidean (human perception)
        int dr = r1 - r2, dg = g1 - g2, db = b1 - b2;
        return 2.0 * dr * dr + 4.0 * dg * dg + 3.0 * db * db;
    }

    private static Color? PickDistinct(List<Cluster> clusters, Color from, double minDistance, Color? exclude = null)
    {
        foreach (var c in clusters)
        {
            double d = Math.Sqrt(ColorDistSq(c.Center.R, c.Center.G, c.Center.B, from.R, from.G, from.B));
            if (d < minDistance) continue;
            if (exclude.HasValue)
            {
                double d2 = Math.Sqrt(ColorDistSq(c.Center.R, c.Center.G, c.Center.B, exclude.Value.R, exclude.Value.G, exclude.Value.B));
                if (d2 < minDistance * 0.5) continue;
            }
            return c.Center;
        }
        return null;
    }

    private static Color Darken(Color c, double factor)
    {
        return new Color(
            (byte)(c.R * factor),
            (byte)(c.G * factor),
            (byte)(c.B * factor));
    }

    /// <summary>
    /// Scores a cluster for primary-color selection.
    /// Balances frequency (cluster size) with color vibrancy (saturation)
    /// so the UI avoids grey/muddy tints on most album covers.
    /// </summary>
    private static double ClusterScore(Cluster c)
    {
        double saturation = GetSaturation(c.Center);
        double brightness = c.Center.Luminance / 255.0;
        // Penalize dark or washed-out colors more aggressively
        double brightPenalty = 1.0;
        if (brightness < 0.15 || brightness > 0.85) brightPenalty = 0.2;
        else if (brightness < 0.25 || brightness > 0.75) brightPenalty = 0.6;
        // Penalize very desaturated colors
        double satPenalty = saturation < 0.1 ? 0.3 : (saturation < 0.2 ? 0.6 : 1.0);
        // Blend frequency with vibrancy: 30% frequency, 70% saturation
        return (c.Count * 0.3) + (saturation * c.Count * 0.7) * brightPenalty * satPenalty;
    }

    /// <summary>
    /// Returns HSV saturation (0-1) for a color.
    /// </summary>
    private static double GetSaturation(Color c)
    {
        double r = c.R / 255.0, g = c.G / 255.0, b = c.B / 255.0;
        double max = Math.Max(r, Math.Max(g, b));
        double min = Math.Min(r, Math.Min(g, b));
        return max == 0 ? 0 : (max - min) / max;
    }
}
