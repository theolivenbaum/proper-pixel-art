using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace ProperPixelArt;

/// <summary>
/// Median-cut colour quantiser: reduces an image to at most
/// <c>numColors</c> distinct palette entries, then remaps every pixel
/// to its nearest palette colour.  Equivalent to PIL's MAXCOVERAGE mode.
/// </summary>
public static class ColorQuantizer
{
    /// <summary>
    /// Quantise the RGB channels of <paramref name="source"/> to at most
    /// <paramref name="numColors"/> colours, preserving the original alpha channel.
    /// </summary>
    public static Image<Rgba32> Quantize(Image<Rgba32> source, int numColors)
    {
        int w = source.Width, h = source.Height;

        // ── Collect opaque pixel colours for palette building ─────────────────
        var samples = new List<(byte r, byte g, byte b)>(w * h);
        source.ProcessPixelRows(acc =>
        {
            for (int y = 0; y < h; y++)
            {
                var row = acc.GetRowSpan(y);
                for (int x = 0; x < w; x++)
                    if (row[x].A >= ColorProcessor.AlphaThreshold)
                        samples.Add((row[x].R, row[x].G, row[x].B));
            }
        });

        if (samples.Count == 0)
            return source.Clone();

        // Sub-sample for speed while preserving colour diversity
        const int MaxSamples = 100_000;
        if (samples.Count > MaxSamples)
        {
            int step = samples.Count / MaxSamples;
            samples = [.. Enumerable.Range(0, MaxSamples).Select(i => samples[i * step])];
        }

        var palette = MedianCut(samples, numColors);

        // ── Remap every pixel to nearest palette entry ────────────────────────
        var result = new Image<Rgba32>(w, h);
        source.ProcessPixelRows(result, (src, dst) =>
        {
            for (int y = 0; y < src.Height; y++)
            {
                var srcRow = src.GetRowSpan(y);
                var dstRow = dst.GetRowSpan(y);
                for (int x = 0; x < src.Width; x++)
                {
                    var p = srcRow[x];
                    var c = NearestColor(palette, p.R, p.G, p.B);
                    dstRow[x] = new Rgba32(c.r, c.g, c.b, p.A);
                }
            }
        });

        return result;
    }

    // ── Median-cut algorithm ──────────────────────────────────────────────────

    private static (byte r, byte g, byte b)[] MedianCut(
        List<(byte r, byte g, byte b)> pixels, int numColors)
    {
        var buckets = new List<List<(byte r, byte g, byte b)>> { pixels };

        while (buckets.Count < numColors)
        {
            // Find the bucket with the largest colour range
            int maxIdx = 0, maxRange = -1;
            for (int i = 0; i < buckets.Count; i++)
            {
                int range = ColorRange(buckets[i]);
                if (range > maxRange) { maxRange = range; maxIdx = i; }
            }
            if (maxRange == 0) break;

            var bucket = buckets[maxIdx];
            buckets.RemoveAt(maxIdx);

            // Sort on the widest channel and split at median
            int rRange = Channel(bucket, 0);
            int gRange = Channel(bucket, 1);
            int bRange = Channel(bucket, 2);

            List<(byte r, byte g, byte b)> sorted;
            if (rRange >= gRange && rRange >= bRange)
                sorted = [.. bucket.OrderBy(p => p.r)];
            else if (gRange >= bRange)
                sorted = [.. bucket.OrderBy(p => p.g)];
            else
                sorted = [.. bucket.OrderBy(p => p.b)];

            int mid = sorted.Count / 2;
            buckets.Add(sorted[..mid]);
            buckets.Add(sorted[mid..]);
        }

        return [.. buckets.Where(b => b.Count > 0).Select(AverageColor)];
    }

    private static int ColorRange(List<(byte r, byte g, byte b)> bucket)
    {
        if (bucket.Count == 0) return 0;
        return Math.Max(Channel(bucket, 0), Math.Max(Channel(bucket, 1), Channel(bucket, 2)));
    }

    private static int Channel(List<(byte r, byte g, byte b)> bucket, int ch)
    {
        byte min = 255, max = 0;
        foreach (var p in bucket)
        {
            byte v = ch switch { 0 => p.r, 1 => p.g, _ => p.b };
            if (v < min) min = v;
            if (v > max) max = v;
        }
        return max - min;
    }

    private static (byte r, byte g, byte b) AverageColor(List<(byte r, byte g, byte b)> bucket)
    {
        long rs = 0, gs = 0, bs = 0;
        foreach (var (r, g, b) in bucket) { rs += r; gs += g; bs += b; }
        int n = bucket.Count;
        return ((byte)(rs / n), (byte)(gs / n), (byte)(bs / n));
    }

    // ── Nearest-colour search (linear scan; palette is small) ─────────────────

    private static (byte r, byte g, byte b) NearestColor(
        (byte r, byte g, byte b)[] palette, byte r, byte g, byte b)
    {
        int bestDist = int.MaxValue;
        var best = palette[0];
        foreach (var c in palette)
        {
            int dr = r - c.r, dg = g - c.g, db = b - c.b;
            int dist = dr * dr + dg * dg + db * db;
            if (dist < bestDist) { bestDist = dist; best = c; }
        }
        return best;
    }
}
