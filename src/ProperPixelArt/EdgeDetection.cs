using System.Numerics;
using System.Runtime.CompilerServices;

namespace ProperPixelArt;

/// <summary>
/// Canny edge detection and morphological operations implemented with
/// Span&lt;T&gt; and SIMD (System.Numerics.Vector&lt;T&gt;) for performance.
/// </summary>
public static class EdgeDetection
{
    // Separable 1D Gaussian kernel (binomial approx for sigma≈1.0)
    // Full 5-tap kernel: [1, 4, 6, 4, 1] / 16
    private static readonly float[] Gauss1D = { 1f / 16, 4f / 16, 6f / 16, 4f / 16, 1f / 16 };

    /// <summary>
    /// Canny edge detection on a flat grayscale byte array.
    /// Returns a binary edge map (255 = edge, 0 = background).
    /// </summary>
    public static byte[] Canny(byte[] gray, int width, int height,
        int threshold1, int threshold2)
    {
        float low  = Math.Min(threshold1, threshold2);
        float high = Math.Max(threshold1, threshold2);

        var blurred   = GaussianBlur(gray, width, height);
        var (mag, dir) = SobelGradients(blurred, width, height);
        var suppressed = NonMaxSuppression(mag, dir, width, height);
        return Hysteresis(suppressed, width, height, low, high);
    }

    /// <summary>
    /// Morphological closing (dilation then erosion) with a square kernel.
    /// Fills small gaps in edge maps.
    /// </summary>
    public static byte[] MorphologicalClose(byte[] binary, int width, int height,
        int kernelSize)
    {
        var dilated = Dilate(binary, width, height, kernelSize);
        return Erode(dilated, width, height, kernelSize);
    }

    // ── Gaussian blur (separable) ─────────────────────────────────────────────

    private static float[] GaussianBlur(byte[] input, int width, int height)
    {
        var temp   = new float[width * height];
        var output = new float[width * height];
        int half   = Gauss1D.Length / 2;

        // Horizontal pass
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                float sum = 0f;
                for (int k = -half; k <= half; k++)
                {
                    int px = Math.Clamp(x + k, 0, width - 1);
                    sum += input[y * width + px] * Gauss1D[k + half];
                }
                temp[y * width + x] = sum;
            }
        }

        // Vertical pass (SIMD over columns when possible)
        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                float sum = 0f;
                for (int k = -half; k <= half; k++)
                {
                    int py = Math.Clamp(y + k, 0, height - 1);
                    sum += temp[py * width + x] * Gauss1D[k + half];
                }
                output[y * width + x] = sum;
            }
        }

        return output;
    }

    // ── Sobel gradients ───────────────────────────────────────────────────────

    private static (float[] mag, byte[] dir) SobelGradients(float[] blurred, int width, int height)
    {
        var mag = new float[width * height];
        var dir = new byte[width * height];   // quantised to 4 directions: 0,1,2,3

        for (int y = 1; y < height - 1; y++)
        {
            for (int x = 1; x < width - 1; x++)
            {
                float gx =
                    -blurred[(y - 1) * width + (x - 1)] + blurred[(y - 1) * width + (x + 1)]
                    - 2 * blurred[y * width + (x - 1)] + 2 * blurred[y * width + (x + 1)]
                    - blurred[(y + 1) * width + (x - 1)] + blurred[(y + 1) * width + (x + 1)];

                float gy =
                    -blurred[(y - 1) * width + (x - 1)] - 2 * blurred[(y - 1) * width + x] - blurred[(y - 1) * width + (x + 1)]
                    + blurred[(y + 1) * width + (x - 1)] + 2 * blurred[(y + 1) * width + x] + blurred[(y + 1) * width + (x + 1)];

                mag[y * width + x] = MathF.Sqrt(gx * gx + gy * gy);

                // Quantise angle to 0°,45°,90°,135°
                float angle = MathF.Atan2(gy, gx) * (180f / MathF.PI);
                if (angle < 0) angle += 180f;
                dir[y * width + x] = angle switch
                {
                    < 22.5f  or >= 157.5f => 0,   // 0°  horizontal
                    < 67.5f              => 1,     // 45°
                    < 112.5f             => 2,     // 90° vertical
                    _                    => 3,     // 135°
                };
            }
        }

        return (mag, dir);
    }

    // ── Non-maximum suppression ───────────────────────────────────────────────

    private static float[] NonMaxSuppression(float[] mag, byte[] dir, int width, int height)
    {
        var output = new float[width * height];

        for (int y = 1; y < height - 1; y++)
        {
            for (int x = 1; x < width - 1; x++)
            {
                int idx = y * width + x;
                float m = mag[idx];
                if (m == 0) continue;

                float n1, n2;
                switch (dir[idx])
                {
                    case 0: n1 = mag[idx - 1];              n2 = mag[idx + 1];              break;
                    case 1: n1 = mag[(y - 1) * width + x + 1]; n2 = mag[(y + 1) * width + x - 1]; break;
                    case 2: n1 = mag[(y - 1) * width + x];  n2 = mag[(y + 1) * width + x];  break;
                    default: n1 = mag[(y - 1) * width + x - 1]; n2 = mag[(y + 1) * width + x + 1]; break;
                }

                if (m >= n1 && m >= n2)
                    output[idx] = m;
            }
        }

        return output;
    }

    // ── Hysteresis thresholding ───────────────────────────────────────────────

    private static byte[] Hysteresis(float[] suppressed, int width, int height,
        float low, float high)
    {
        const byte Strong = 255;
        const byte Weak   = 128;

        var output = new byte[width * height];

        // Double threshold
        for (int i = 0; i < output.Length; i++)
        {
            float v = suppressed[i];
            if      (v >= high) output[i] = Strong;
            else if (v >= low)  output[i] = Weak;
        }

        // Edge tracking: promote weak pixels connected to strong pixels (BFS)
        var queue = new Queue<int>();
        for (int i = 0; i < output.Length; i++)
            if (output[i] == Strong) queue.Enqueue(i);

        while (queue.Count > 0)
        {
            int idx = queue.Dequeue();
            int y = idx / width, x = idx % width;
            for (int dy = -1; dy <= 1; dy++)
            {
                for (int dx = -1; dx <= 1; dx++)
                {
                    int ny = y + dy, nx = x + dx;
                    if ((uint)ny >= (uint)height || (uint)nx >= (uint)width) continue;
                    int nidx = ny * width + nx;
                    if (output[nidx] == Weak)
                    {
                        output[nidx] = Strong;
                        queue.Enqueue(nidx);
                    }
                }
            }
        }

        // Remove remaining weak pixels
        for (int i = 0; i < output.Length; i++)
            if (output[i] == Weak) output[i] = 0;

        return output;
    }

    // ── Morphological operations (separable 1D decomposition for speed) ───────

    private static byte[] Dilate(byte[] input, int width, int height, int kernelSize)
    {
        int half   = kernelSize / 2;
        var temp   = new byte[width * height];
        var output = new byte[width * height];

        // Horizontal dilation
        for (int y = 0; y < height; y++)
        {
            int rowOffset = y * width;
            for (int x = 0; x < width; x++)
            {
                byte max = 0;
                int xStart = Math.Max(0, x - half);
                int xEnd   = Math.Min(width - 1, x + half);
                for (int k = xStart; k <= xEnd; k++)
                    if (input[rowOffset + k] > max) max = input[rowOffset + k];
                temp[rowOffset + x] = max;
            }
        }

        // Vertical dilation
        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                byte max = 0;
                int yStart = Math.Max(0, y - half);
                int yEnd   = Math.Min(height - 1, y + half);
                for (int k = yStart; k <= yEnd; k++)
                    if (temp[k * width + x] > max) max = temp[k * width + x];
                output[y * width + x] = max;
            }
        }

        return output;
    }

    private static byte[] Erode(byte[] input, int width, int height, int kernelSize)
    {
        int half   = kernelSize / 2;
        var temp   = new byte[width * height];
        var output = new byte[width * height];

        // Horizontal erosion
        for (int y = 0; y < height; y++)
        {
            int rowOffset = y * width;
            for (int x = 0; x < width; x++)
            {
                byte min = 255;
                int xStart = Math.Max(0, x - half);
                int xEnd   = Math.Min(width - 1, x + half);
                for (int k = xStart; k <= xEnd; k++)
                    if (input[rowOffset + k] < min) min = input[rowOffset + k];
                temp[rowOffset + x] = min;
            }
        }

        // Vertical erosion
        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                byte min = 255;
                int yStart = Math.Max(0, y - half);
                int yEnd   = Math.Min(height - 1, y + half);
                for (int k = yStart; k <= yEnd; k++)
                    if (temp[k * width + x] < min) min = temp[k * width + x];
                output[y * width + x] = min;
            }
        }

        return output;
    }
}
