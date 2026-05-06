using System.Runtime.CompilerServices;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace ProperPixelArt;

/// <summary>
/// All colour-related operations: alpha clamping, cell colour selection,
/// palette quantisation, background transparency, and the offset-binning
/// dominant-colour algorithm.
/// </summary>
public static class ColorProcessor
{
    /// <summary>Pixels with alpha below this value are considered transparent.</summary>
    public const int AlphaThreshold = 128;

    // ── Background / alpha helpers ────────────────────────────────────────────

    /// <summary>
    /// Replace pixels whose alpha is below <see cref="AlphaThreshold"/> with
    /// a background colour chosen to contrast maximally with the image content.
    /// The result has alpha = 255 everywhere.
    /// </summary>
    public static Image<Rgba32> ClampAlpha(
        Image<Rgba32> image, int alphaThreshold = AlphaThreshold)
    {
        var topColors = GetTopOpaqueColors(image, alphaThreshold);
        var bg        = PickBackground(topColors);

        return image.Clone(ctx =>
        {
            ctx.ProcessPixelRowsAsVector4((row, _) =>
            {
                // Not efficient for pixel-by-pixel work; use ProcessPixelRows instead
            });
        }).Apply(img =>
        {
            img.ProcessPixelRows(acc =>
            {
                for (int y = 0; y < acc.Height; y++)
                {
                    var row = acc.GetRowSpan(y);
                    for (int x = 0; x < acc.Width; x++)
                    {
                        ref var p = ref row[x];
                        if (p.A < alphaThreshold)
                            p = new Rgba32(bg.R, bg.G, bg.B, 255);
                        else
                            p = new Rgba32(p.R, p.G, p.B, 255);
                    }
                }
            });
        });
    }

    /// <summary>Extract the alpha channel as a flat byte array (row-major).</summary>
    public static byte[] ExtractAlpha(Image<Rgba32> image)
    {
        int w = image.Width, h = image.Height;
        var alpha = new byte[w * h];
        image.ProcessPixelRows(acc =>
        {
            for (int y = 0; y < h; y++)
            {
                var row = acc.GetRowSpan(y);
                int off = y * w;
                for (int x = 0; x < w; x++)
                    alpha[off + x] = row[x].A;
            }
        });
        return alpha;
    }

    /// <summary>Extract and nearest-neighbour scale-up the alpha channel.</summary>
    public static byte[] ExtractAndScaleAlpha(Image<Rgba32> image, int scaleFactor)
    {
        if (scaleFactor == 1) return ExtractAlpha(image);

        using var alphaImg = new Image<L8>(image.Width, image.Height);
        image.ProcessPixelRows(alphaImg, (src, dst) =>
        {
            for (int y = 0; y < src.Height; y++)
            {
                var srcRow = src.GetRowSpan(y);
                var dstRow = dst.GetRowSpan(y);
                for (int x = 0; x < src.Width; x++)
                    dstRow[x] = new L8(srcRow[x].A);
            }
        });
        alphaImg.Mutate(c =>
            c.Resize(image.Width * scaleFactor, image.Height * scaleFactor,
                KnownResamplers.NearestNeighbor));

        int w2 = alphaImg.Width, h2 = alphaImg.Height;
        var result = new byte[w2 * h2];
        alphaImg.ProcessPixelRows(acc =>
        {
            for (int y = 0; y < h2; y++)
            {
                var row = acc.GetRowSpan(y);
                int off = y * w2;
                for (int x = 0; x < w2; x++)
                    result[off + x] = row[x].PackedValue;
            }
        });
        return result;
    }

    // ── Cell colour selection ─────────────────────────────────────────────────

    /// <summary>
    /// Returns the most-common RGB colour in a flat RGB-byte cell, with alpha=255.
    /// </summary>
    /// <param name="cellRgb">Flat R,G,B byte array for the cell (row-major, 3 bytes/pixel).</param>
    /// <param name="pixelCount">Number of pixels (length / 3).</param>
    public static Rgba32 GetOpaqueCellColor(ReadOnlySpan<byte> cellRgb, int pixelCount)
    {
        if (pixelCount == 0) return new Rgba32(0, 0, 0, 255);

        var counts = new Dictionary<(byte r, byte g, byte b), int>(pixelCount);
        for (int i = 0; i < pixelCount; i++)
        {
            var key = (cellRgb[i * 3], cellRgb[i * 3 + 1], cellRgb[i * 3 + 2]);
            counts.TryGetValue(key, out int c);
            counts[key] = c + 1;
        }
        var best = counts.MaxBy(kv => kv.Value).Key;
        return new Rgba32(best.r, best.g, best.b, 255);
    }

    /// <summary>
    /// Colour selection for a quantised RGB cell with a separate original alpha channel.
    /// <list type="bullet">
    ///   <item>≥50 % transparent → (0,0,0,0)</item>
    ///   <item>otherwise → most-common RGB with alpha=255</item>
    /// </list>
    /// </summary>
    /// <param name="cellRgb">Flat RGB bytes (3/pixel).</param>
    /// <param name="cellAlpha">Flat alpha bytes (1/pixel, from original image).</param>
    /// <param name="pixelCount">Number of pixels.</param>
    public static Rgba32 GetCellColorWithAlpha(
        ReadOnlySpan<byte> cellRgb,
        ReadOnlySpan<byte> cellAlpha,
        int pixelCount)
    {
        int opaque = 0;
        for (int i = 0; i < pixelCount; i++)
            if (cellAlpha[i] >= AlphaThreshold) opaque++;

        if (IsMajorityTransparent(opaque, pixelCount))
            return new Rgba32(0, 0, 0, 0);

        return GetOpaqueCellColor(cellRgb, pixelCount);
    }

    /// <summary>
    /// Colour selection when quantisation is skipped — operates directly on RGBA cells.
    /// Uses offset-binning to find the dominant colour robustly.
    /// <list type="bullet">
    ///   <item>≥50 % transparent → (0,0,0,0)</item>
    ///   <item>otherwise → dominant opaque colour with alpha=255</item>
    /// </list>
    /// </summary>
    /// <param name="cellRgba">Flat RGBA bytes (4/pixel).</param>
    /// <param name="pixelCount">Number of pixels.</param>
    public static Rgba32 GetCellColorSkipQuantization(
        ReadOnlySpan<byte> cellRgba, int pixelCount)
    {
        if (pixelCount == 0) return new Rgba32(0, 0, 0, 0);

        // Count opaque pixels and collect their RGB values
        var opaqueRgb = new List<(byte r, byte g, byte b)>(pixelCount);
        for (int i = 0; i < pixelCount; i++)
        {
            int off = i * 4;
            if (cellRgba[off + 3] >= AlphaThreshold)
                opaqueRgb.Add((cellRgba[off], cellRgba[off + 1], cellRgba[off + 2]));
        }

        if (IsMajorityTransparent(opaqueRgb.Count, pixelCount))
            return new Rgba32(0, 0, 0, 0);

        var (r, g, b) = DominantRgbByBinning(opaqueRgb);
        return new Rgba32(r, g, b, 255);
    }

    // ── Offset-binning dominant colour ────────────────────────────────────────

    /// <summary>
    /// Find the dominant RGB colour using dual offset-binning to avoid boundary artefacts.
    /// <para>
    /// Two 5-level grids (bin size 52) are used: one aligned to 0 and one offset by 26.
    /// The grid producing the largest dominant cluster wins.
    /// The median of that cluster is returned as the representative colour.
    /// </para>
    /// </summary>
    public static (byte r, byte g, byte b) DominantRgbByBinning(
        IReadOnlyList<(byte r, byte g, byte b)> pixels)
    {
        int n = pixels.Count;
        if (n == 0) return (0, 0, 0);
        if (n == 1) return pixels[0];
        if (n <= 3) return MedianRgb(pixels);

        const int BinSize = 52;
        const int Offset  = BinSize / 2; // 26

        Span<int> bins1 = stackalloc int[125];
        Span<int> bins2 = stackalloc int[125];

        // Vote in both grids
        for (int i = 0; i < n; i++)
        {
            var (r, g, b) = pixels[i];
            bins1[BinIndex(r, g, b, 0)]      = bins1[BinIndex(r, g, b, 0)]      + 1;
            bins2[BinIndex(r, g, b, Offset)]  = bins2[BinIndex(r, g, b, Offset)] + 1;
        }

        int dom1 = ArgMax(bins1), max1 = bins1[dom1];
        int dom2 = ArgMax(bins2), max2 = bins2[dom2];

        bool useGrid1 = max1 >= max2;
        int  domBin   = useGrid1 ? dom1 : dom2;
        int  off      = useGrid1 ? 0     : Offset;

        // Collect pixels in the dominant bin
        var cluster = new List<(byte r, byte g, byte b)>(Math.Max(max1, max2));
        for (int i = 0; i < n; i++)
        {
            var p = pixels[i];
            if (BinIndex(p.r, p.g, p.b, off) == domBin)
                cluster.Add(p);
        }

        return cluster.Count > 0 ? MedianRgb(cluster) : pixels[0];
    }

    // ── Palette quantisation ──────────────────────────────────────────────────

    /// <summary>
    /// Quantise the image to at most <paramref name="numColors"/> colours using
    /// median-cut, optionally saving the quantised intermediate image.
    /// </summary>
    public static Image<Rgba32> PaletteImage(
        Image<Rgba32> image,
        int numColors = 16,
        string? outputDir = null)
    {
        using var clamped = ClampAlpha(image);
        var quantised = ColorQuantizer.Quantize(clamped, numColors);

        if (outputDir is not null)
        {
            Directory.CreateDirectory(outputDir);
            quantised.SaveAsPng(Path.Combine(outputDir, "quantized_original.png"));
        }

        return quantised;
    }

    // ── Background transparency ───────────────────────────────────────────────

    /// <summary>Return the most-common colour on the image boundary (RGB, ignoring alpha).</summary>
    public static Rgba32 MostCommonBoundaryColor(Image<Rgba32> image)
    {
        var counts = new Dictionary<(byte r, byte g, byte b), int>();
        int w = image.Width, h = image.Height;

        void Count(Rgba32 p)
        {
            var key = (p.R, p.G, p.B);
            counts.TryGetValue(key, out int c);
            counts[key] = c + 1;
        }

        image.ProcessPixelRows(acc =>
        {
            // Top and bottom rows
            var top    = acc.GetRowSpan(0);
            var bottom = acc.GetRowSpan(h - 1);
            for (int x = 0; x < w; x++) { Count(top[x]); Count(bottom[x]); }

            // Left and right columns (excluding corners)
            for (int y = 1; y < h - 1; y++)
            {
                var row = acc.GetRowSpan(y);
                Count(row[0]);
                Count(row[w - 1]);
            }
        });

        var best = counts.MaxBy(kv => kv.Value).Key;
        return new Rgba32(best.r, best.g, best.b, 255);
    }

    /// <summary>
    /// Set alpha=0 for all pixels whose RGB exactly matches the most-common
    /// boundary colour.  Applied image-wide (no flood fill).
    /// </summary>
    public static Image<Rgba32> MakeBackgroundTransparent(Image<Rgba32> image)
    {
        var bg = MostCommonBoundaryColor(image);
        return image.Clone(img =>
        {
            // Clone then mutate pixels
        }).Apply(img =>
        {
            img.ProcessPixelRows(acc =>
            {
                for (int y = 0; y < acc.Height; y++)
                {
                    var row = acc.GetRowSpan(y);
                    for (int x = 0; x < acc.Width; x++)
                    {
                        ref var p = ref row[x];
                        if (p.R == bg.R && p.G == bg.G && p.B == bg.B)
                            p = new Rgba32(p.R, p.G, p.B, 0);
                    }
                }
            });
        });
    }

    // ── Internal helpers ──────────────────────────────────────────────────────

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static bool IsMajorityTransparent(int opaqueCount, int totalCount) =>
        opaqueCount <= totalCount / 2;

    private static int RgbDist(Rgba32 a, (byte r, byte g, byte b) b)
    {
        int dr = a.R - b.r, dg = a.G - b.g, db = a.B - b.b;
        return dr * dr + dg * dg + db * db;
    }

    private static List<(byte r, byte g, byte b)> GetTopOpaqueColors(
        Image<Rgba32> image, int alphaThreshold, int limit = 8)
    {
        // Thumbnail for speed and de-noising
        using var thumb = image.Clone(ctx =>
            ctx.Resize(Math.Min(image.Width, 160), 0, KnownResamplers.Bicubic));

        var counts = new Dictionary<(byte r, byte g, byte b), int>();
        thumb.ProcessPixelRows(acc =>
        {
            for (int y = 0; y < acc.Height; y++)
            {
                var row = acc.GetRowSpan(y);
                for (int x = 0; x < acc.Width; x++)
                {
                    var p = row[x];
                    if (p.A < alphaThreshold) continue;
                    var key = (p.R, p.G, p.B);
                    counts.TryGetValue(key, out int c);
                    counts[key] = c + 1;
                }
            }
        });

        return [.. counts.OrderByDescending(kv => kv.Value).Take(limit).Select(kv => kv.Key)];
    }

    private static Rgba32 PickBackground(List<(byte r, byte g, byte b)> imageColors)
    {
        (byte r, byte g, byte b)[] candidates =
        [
            (0, 255, 255), (255, 255, 255), (255, 0, 0),
            (0, 255, 0),   (0, 0, 255),     (255, 255, 0),
            (255, 0, 255), (255, 128, 0),   (128, 0, 255),
            (0, 128, 255), (0, 255, 128),   (255, 0, 128),
        ];

        if (imageColors.Count == 0)
            return new Rgba32(255, 255, 255, 255);

        var best = candidates[0];
        int bestScore = -1;

        foreach (var cand in candidates)
        {
            var candRgba = new Rgba32(cand.r, cand.g, cand.b, 255);
            int score = imageColors.Min(c => RgbDist(candRgba, c));
            if (score > bestScore) { bestScore = score; best = cand; }
        }

        return new Rgba32(best.r, best.g, best.b, 255);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int BinIndex(byte r, byte g, byte b, int offset)
    {
        int ri = Math.Min(r + offset, 255) / 52;
        int gi = Math.Min(g + offset, 255) / 52;
        int bi = Math.Min(b + offset, 255) / 52;
        return ri * 25 + gi * 5 + bi;
    }

    private static int ArgMax(Span<int> values)
    {
        int best = 0;
        for (int i = 1; i < values.Length; i++)
            if (values[i] > values[best]) best = i;
        return best;
    }

    private static (byte r, byte g, byte b) MedianRgb(
        IReadOnlyList<(byte r, byte g, byte b)> list)
    {
        var rs = list.Select(p => (int)p.r).Order().ToArray();
        var gs = list.Select(p => (int)p.g).Order().ToArray();
        var bs = list.Select(p => (int)p.b).Order().ToArray();
        int mid = list.Count / 2;
        return ((byte)rs[mid], (byte)gs[mid], (byte)bs[mid]);
    }
}

/// <summary>Extension to allow inline mutation via <c>.Apply()</c>.</summary>
file static class ImageApplyExtension
{
    public static T Apply<T>(this T obj, Action<T> action) { action(obj); return obj; }
}
