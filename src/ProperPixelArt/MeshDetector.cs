using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace ProperPixelArt;

/// <summary>
/// Detects the pixel-grid mesh from a noisy, high-resolution pixel-art image.
/// Pipeline: clamp alpha → Canny edges → morphological closing →
/// Hough lines → cluster → auto-detect pixel width → homogenise.
/// </summary>
public static class MeshDetector
{
    // ── Public API ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Compute the pixel grid mesh, optionally trying an upscaled image first
    /// and falling back to the original if only border lines are detected.
    /// Returns the mesh and the scale factor actually used.
    /// </summary>
    public static (Mesh mesh, int scaleUsed) ComputeMeshWithScaling(
        Image<Rgba32> img,
        int upscaleFactor,
        string? outputDir = null,
        int? pixelWidth = null)
    {
        using var upscaled = ImageUtils.ScaleImage(img, upscaleFactor);
        var meshLines = ComputeMesh(upscaled, outputDir: outputDir, pixelWidth: pixelWidth);
        if (!IsTrivialMesh(meshLines))
            return (meshLines, upscaleFactor);

        var fallback = ComputeMesh(img, outputDir: outputDir, pixelWidth: pixelWidth);
        return (fallback, 1);
    }

    /// <summary>Full mesh-detection pipeline on a single image.</summary>
    public static Mesh ComputeMesh(
        Image<Rgba32> img,
        (int low, int high) cannyThresholds = default,
        int closureKernelSize = 8,
        string? outputDir = null,
        int? pixelWidth = null)
    {
        if (cannyThresholds == default) cannyThresholds = (50, 200);

        using var cropped  = ImageUtils.CropBorder(img, numPixels: 2);
        using var clamped  = ColorProcessor.ClampAlpha(cropped);
        byte[] grayBytes   = ImageUtils.ToGrayscaleBytes(clamped);
        int w = clamped.Width, h = clamped.Height;

        byte[] edges       = EdgeDetection.Canny(grayBytes, w, h, cannyThresholds.low, cannyThresholds.high);
        byte[] closedEdges = EdgeDetection.MorphologicalClose(edges, w, h, closureKernelSize);
        var initialMesh    = DetectGridLines(closedEdges, w, h);
        int pw             = pixelWidth ?? GetPixelWidth(initialMesh);

        var meshX  = HomogenizeLines(initialMesh.LinesX, pw);
        var meshY  = HomogenizeLines(initialMesh.LinesY, pw);
        var final  = new Mesh(meshX, meshY);

        if (outputDir is not null)
            SaveDebugImages(img, edges, closedEdges, initialMesh, final, w, h, outputDir);

        return final;
    }

    // ── Grid line detection ───────────────────────────────────────────────────

    /// <summary>
    /// Run Hough transform, filter to near-vertical/horizontal segments, cluster.
    /// </summary>
    public static Mesh DetectGridLines(
        byte[] edges, int width, int height,
        int angleThresholdDeg = 15)
    {
        var segments = HoughTransform.HoughLinesP(
            edges, width, height,
            threshold: 100, minLineLength: 50, maxLineGap: 10);

        var linesX = new List<int> { 0, width  - 1 };
        var linesY = new List<int> { 0, height - 1 };

        double vertMin  = (90 - angleThresholdDeg) * Math.PI / 180.0;
        double horizMax = angleThresholdDeg          * Math.PI / 180.0;

        foreach (var (x1, y1, x2, y2) in segments)
        {
            double angle = Math.Abs(Math.Atan2(y2 - y1, x2 - x1));
            if      (angle > vertMin)  linesX.Add((x1 + x2) / 2);
            else if (angle < horizMax) linesY.Add((y1 + y2) / 2);
        }

        return new Mesh(ClusterLines(linesX), ClusterLines(linesY));
    }

    /// <summary>
    /// Remove near-duplicate lines by clustering values within
    /// <paramref name="threshold"/> pixels and keeping the median.
    /// </summary>
    public static Lines ClusterLines(IEnumerable<int> lines, int threshold = 4)
    {
        var sorted = lines.Order().ToList();
        if (sorted.Count == 0) return [];

        var clusters = new List<List<int>> { new() { sorted[0] } };
        foreach (int p in sorted.Skip(1))
        {
            var last = clusters[^1];
            if (Math.Abs(p - last[^1]) <= threshold)
                last.Add(p);
            else
                clusters.Add([p]);
        }

        return new Lines(clusters.Select(c => Median(c)));
    }

    // ── Pixel width estimation ────────────────────────────────────────────────

    /// <summary>
    /// Estimate the true pixel width by collecting all inter-line spacings,
    /// trimming outliers, and returning the median.
    /// </summary>
    public static int GetPixelWidth(Mesh mesh, double trimOutlierFraction = 0.2)
    {
        var allGaps = new List<int>();
        foreach (var axis in new[] { mesh.LinesX, mesh.LinesY })
        {
            for (int i = 1; i < axis.Count; i++)
                allGaps.Add(axis[i] - axis[i - 1]);
        }

        if (allGaps.Count == 0) return 1;

        allGaps.Sort();
        int lo  = (int)(allGaps.Count * trimOutlierFraction);
        int hi  = (int)(allGaps.Count * (1 - trimOutlierFraction));
        var mid = allGaps.Skip(lo).Take(Math.Max(hi - lo, 1)).ToList();
        return Median(mid.Count > 0 ? mid : allGaps);
    }

    // ── Line homogenisation ───────────────────────────────────────────────────

    /// <summary>
    /// Fill gaps between detected lines by subdividing each section into an
    /// integer number of equally-spaced pixels of width
    /// <paramref name="pixelWidth"/>.
    /// </summary>
    public static Lines HomogenizeLines(IReadOnlyList<int> lines, int pixelWidth)
    {
        if (lines.Count < 2) return new Lines(lines);

        var complete = new List<int>();
        for (int idx = 0; idx < lines.Count - 1; idx++)
        {
            int sectionWidth = lines[idx + 1] - lines[idx];
            int numPixels    = pixelWidth > 0
                ? (int)Math.Round((double)sectionWidth / pixelWidth)
                : 0;
            if (numPixels == 0)
            {
                complete.Add(lines[idx]);
                continue;
            }
            double sectionPixelWidth = (double)sectionWidth / numPixels;
            for (int n = 0; n < numPixels; n++)
                complete.Add(lines[idx] + (int)(n * sectionPixelWidth));
        }
        complete.Add(lines[^1]);
        return new Lines(complete);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static bool IsTrivialMesh(Mesh mesh) =>
        mesh.LinesX.Count is 2 or 3 && mesh.LinesY.Count is 2 or 3;

    private static int Median(List<int> sorted)
    {
        if (sorted.Count == 0) return 0;
        int mid = sorted.Count / 2;
        return sorted.Count % 2 == 1
            ? sorted[mid]
            : (sorted[mid - 1] + sorted[mid]) / 2;
    }

    private static void SaveDebugImages(
        Image<Rgba32> original,
        byte[] edges, byte[] closedEdges,
        Mesh initialMesh, Mesh finalMesh,
        int w, int h, string dir)
    {
        Directory.CreateDirectory(dir);

        SaveGray(edges,       w, h, Path.Combine(dir, "edges.png"));
        SaveGray(closedEdges, w, h, Path.Combine(dir, "closed_edges.png"));

        using var withLines = ImageUtils.OverlayGridLines(original, initialMesh);
        withLines.SaveAsPng(Path.Combine(dir, "lines.png"));

        using var withMesh = ImageUtils.OverlayGridLines(original, finalMesh);
        withMesh.SaveAsPng(Path.Combine(dir, "mesh.png"));
    }

    private static void SaveGray(byte[] data, int w, int h, string path)
    {
        using var img = new SixLabors.ImageSharp.Image<SixLabors.ImageSharp.PixelFormats.L8>(w, h);
        img.ProcessPixelRows(acc =>
        {
            for (int y = 0; y < h; y++)
            {
                var row = acc.GetRowSpan(y);
                for (int x = 0; x < w; x++)
                    row[x] = new SixLabors.ImageSharp.PixelFormats.L8(data[y * w + x]);
            }
        });
        img.SaveAsPng(path);
    }
}
