using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace ProperPixelArt;

/// <summary>
/// Main pixel-art reconstruction pipeline.
/// </summary>
public static class Pixelator
{
    // ── Public API ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Convert a noisy, high-resolution pixel-art image to true-resolution pixel art.
    /// </summary>
    public static Image<Rgba32> Pixelate(Image<Rgba32> image, PixelateOptions? options = null)
    {
        options ??= new PixelateOptions();

        using var imageRgba = image.Clone();

        // 1. Detect pixel grid
        var (meshLines, upscaleFactor) = MeshDetector.ComputeMeshWithScaling(
            imageRgba,
            options.InitialUpscaleFactor,
            outputDir:   options.IntermediateDir,
            pixelWidth:  options.PixelWidth);

        bool skipQuantization = options.NumColors is null;

        // 2. Prepare image for colour processing
        Image<Rgba32> processed;
        if (skipQuantization)
        {
            processed = imageRgba.Clone();
        }
        else
        {
            processed = ColorProcessor.PaletteImage(
                imageRgba,
                numColors: options.NumColors!.Value,
                outputDir: options.IntermediateDir);
        }

        // 3. Scale to match mesh dimensions
        using var scaledImg = ImageUtils.ScaleImage(processed, upscaleFactor);
        processed.Dispose();

        // 4. Extract alpha channel (quantised path only)
        byte[]? scaledAlpha = skipQuantization
            ? null
            : ColorProcessor.ExtractAndScaleAlpha(imageRgba, upscaleFactor);

        // 5. Downsample: one representative colour per mesh cell
        using var result = Downsample(scaledImg, meshLines,
            skipQuantization: skipQuantization,
            originalAlpha:    scaledAlpha);

        // 6. Optional: make background transparent
        Image<Rgba32> final;
        if (options.TransparentBackground)
        {
            final = ColorProcessor.MakeBackgroundTransparent(result);
        }
        else
        {
            final = result.Clone();
        }

        // 7. Optional: up-scale result for display
        if (options.ScaleResult is int scale)
        {
            using var scaled = ImageUtils.ScaleImage(final, scale);
            final.Dispose();
            return scaled.Clone();
        }

        return final;
    }

    // ── Downsampling ──────────────────────────────────────────────────────────

    /// <summary>
    /// Reduce the image to one representative pixel per mesh cell.
    /// </summary>
    public static Image<Rgba32> Downsample(
        Image<Rgba32> image, Mesh meshLines,
        bool skipQuantization = false,
        byte[]? originalAlpha = null)
    {
        var linesX = meshLines.LinesX;
        var linesY = meshLines.LinesY;
        int outW   = linesX.Count - 1;
        int outH   = linesY.Count - 1;
        if (outW <= 0 || outH <= 0)
            return new Image<Rgba32>(1, 1);

        // Extract full pixel data for efficient cell access
        byte[] imgData = skipQuantization
            ? ImageUtils.ToRgbaBytes(image)
            : ImageUtils.ToRgbBytes(image);

        int imgW = image.Width;
        int channels = skipQuantization ? 4 : 3;

        var outPixels = new Rgba32[outH * outW];

        for (int j = 0; j < outH; j++)
        {
            int y0 = linesY[j], y1 = linesY[j + 1];
            for (int i = 0; i < outW; i++)
            {
                int x0 = linesX[i], x1 = linesX[i + 1];
                outPixels[j * outW + i] = GetCellColor(
                    imgData, imgW, x0, y0, x1, y1, channels,
                    skipQuantization, originalAlpha);
            }
        }

        var result = new Image<Rgba32>(outW, outH);
        result.ProcessPixelRows(acc =>
        {
            for (int y = 0; y < outH; y++)
            {
                var row = acc.GetRowSpan(y);
                for (int x = 0; x < outW; x++)
                    row[x] = outPixels[y * outW + x];
            }
        });
        return result;
    }

    // ── Cell colour extraction ─────────────────────────────────────────────────

    private static Rgba32 GetCellColor(
        byte[] imgData, int imgW,
        int x0, int y0, int x1, int y1,
        int channels,
        bool skipQuantization,
        byte[]? alpha)
    {
        int cellW  = x1 - x0;
        int cellH  = y1 - y0;
        int pixels = cellW * cellH;
        if (pixels <= 0) return new Rgba32(0, 0, 0, 0);

        // Copy cell into a compact flat buffer
        var cell = new byte[pixels * channels];
        int destOff = 0;
        for (int y = y0; y < y1; y++)
        {
            int srcOff = (y * imgW + x0) * channels;
            imgData.AsSpan(srcOff, cellW * channels).CopyTo(cell.AsSpan(destOff));
            destOff += cellW * channels;
        }

        if (skipQuantization)
            return ColorProcessor.GetCellColorSkipQuantization(cell, pixels);

        if (alpha is not null)
        {
            // Extract alpha sub-array for this cell
            var cellAlpha = new byte[pixels];
            int alphaOff = 0;
            for (int y = y0; y < y1; y++)
            {
                int srcOff = y * imgW + x0;
                alpha.AsSpan(srcOff, cellW).CopyTo(cellAlpha.AsSpan(alphaOff));
                alphaOff += cellW;
            }
            return ColorProcessor.GetCellColorWithAlpha(cell, cellAlpha, pixels);
        }

        return ColorProcessor.GetOpaqueCellColor(cell, pixels);
    }
}
