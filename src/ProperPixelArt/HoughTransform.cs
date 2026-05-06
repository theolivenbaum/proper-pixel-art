namespace ProperPixelArt;

/// <summary>
/// Standard accumulator-based Hough transform for line-segment detection,
/// matching the behaviour of OpenCV HoughLinesP.
/// </summary>
public static class HoughTransform
{
    private const int NumAngles = 180;   // 0° … 179° in 1° steps

    // Precomputed cos/sin tables (initialised once at class load)
    private static readonly double[] CosTable = new double[NumAngles];
    private static readonly double[] SinTable = new double[NumAngles];

    static HoughTransform()
    {
        for (int a = 0; a < NumAngles; a++)
        {
            double theta = a * Math.PI / NumAngles;
            CosTable[a] = Math.Cos(theta);
            SinTable[a] = Math.Sin(theta);
        }
    }

    /// <summary>
    /// Detect line segments in a binary edge map.
    /// </summary>
    /// <param name="edges">Flat edge-pixel array (non-zero = edge), row-major.</param>
    /// <param name="width">Image width in pixels.</param>
    /// <param name="height">Image height in pixels.</param>
    /// <param name="threshold">Minimum accumulator votes for a valid line.</param>
    /// <param name="minLineLength">Minimum segment length in pixels.</param>
    /// <param name="maxLineGap">Maximum gap inside a segment in pixels.</param>
    public static List<LineSegment> HoughLinesP(
        byte[] edges, int width, int height,
        int threshold, int minLineLength, int maxLineGap)
    {
        int maxRho    = (int)Math.Ceiling(Math.Sqrt((double)width * width + (double)height * height));
        int rhoRange  = 2 * maxRho + 1;

        // ── Vote ─────────────────────────────────────────────────────────────
        var accum = new int[rhoRange * NumAngles];

        for (int y = 0; y < height; y++)
        {
            int rowOff = y * width;
            for (int x = 0; x < width; x++)
            {
                if (edges[rowOff + x] == 0) continue;
                for (int a = 0; a < NumAngles; a++)
                {
                    int rho = (int)Math.Round(x * CosTable[a] + y * SinTable[a]) + maxRho;
                    if ((uint)rho < (uint)rhoRange)
                        accum[rho * NumAngles + a]++;
                }
            }
        }

        // ── Find peaks (local maxima ≥ threshold) ─────────────────────────────
        var peaks = new List<(int rhoIdx, int angle, int votes)>();
        for (int r = 1; r < rhoRange - 1; r++)
        {
            for (int a = 0; a < NumAngles; a++)
            {
                int val = accum[r * NumAngles + a];
                if (val < threshold) continue;

                // 3×3 local-maximum check
                bool isMax = true;
                for (int dr = -1; dr <= 1 && isMax; dr++)
                    for (int da = -1; da <= 1 && isMax; da++)
                    {
                        if (dr == 0 && da == 0) continue;
                        int na = (a + da + NumAngles) % NumAngles;
                        if (accum[(r + dr) * NumAngles + na] > val) isMax = false;
                    }

                if (isMax) peaks.Add((r, a, val));
            }
        }

        // Sort by votes (descending) so stronger lines are reported first
        peaks.Sort((a, b) => b.votes.CompareTo(a.votes));

        // ── Trace line segments for each peak ─────────────────────────────────
        var segments = new List<LineSegment>();
        foreach (var (rhoIdx, angle, _) in peaks)
        {
            int rhoVal = rhoIdx - maxRho;
            TraceSegments(edges, width, height,
                rhoVal, angle,
                CosTable[angle], SinTable[angle],
                minLineLength, maxLineGap,
                segments);
        }

        return segments;
    }

    private static void TraceSegments(
        byte[] edges, int width, int height,
        int rho, int angleDeg,
        double cosA, double sinA,
        int minLineLength, int maxLineGap,
        List<LineSegment> output)
    {
        // Build the sequence of image pixels that lie on this line.
        // Iterate over the longer axis so adjacent points are ~1 px apart.
        bool iterateX = Math.Abs(sinA) > Math.Abs(cosA); // more horizontal line
        int count = iterateX ? width : height;

        int segStartIdx = -1;
        int lastEdgeIdx = -1;
        int lastEdgeX = 0, lastEdgeY = 0;
        int segStartX = 0, segStartY = 0;

        for (int i = 0; i < count; i++)
        {
            int px, py;
            if (iterateX)
            {
                px = i;
                py = Math.Abs(cosA) < 1e-9
                    ? rho
                    : (int)Math.Round((rho - i * cosA) / sinA);
            }
            else
            {
                py = i;
                px = Math.Abs(sinA) < 1e-9
                    ? rho
                    : (int)Math.Round((rho - i * sinA) / cosA);
            }

            if ((uint)px >= (uint)width || (uint)py >= (uint)height)
            {
                if (segStartIdx >= 0)
                    FlushSegment(segStartX, segStartY, lastEdgeX, lastEdgeY,
                        minLineLength, output);
                segStartIdx = lastEdgeIdx = -1;
                continue;
            }

            bool isEdge = edges[py * width + px] > 0;

            if (isEdge)
            {
                if (segStartIdx < 0)
                {
                    segStartIdx = i;
                    segStartX = px; segStartY = py;
                }
                lastEdgeIdx = i;
                lastEdgeX = px; lastEdgeY = py;
            }
            else if (segStartIdx >= 0)
            {
                // Measure pixel gap from last edge point
                int gap = i - lastEdgeIdx;
                if (gap > maxLineGap)
                {
                    FlushSegment(segStartX, segStartY, lastEdgeX, lastEdgeY,
                        minLineLength, output);
                    segStartIdx = lastEdgeIdx = -1;
                }
            }
        }

        if (segStartIdx >= 0)
            FlushSegment(segStartX, segStartY, lastEdgeX, lastEdgeY,
                minLineLength, output);
    }

    private static void FlushSegment(
        int x1, int y1, int x2, int y2,
        int minLineLength, List<LineSegment> output)
    {
        int dx = x2 - x1, dy = y2 - y1;
        int len = (int)Math.Round(Math.Sqrt((double)dx * dx + (double)dy * dy));
        if (len >= minLineLength)
            output.Add(new LineSegment(x1, y1, x2, y2));
    }
}
